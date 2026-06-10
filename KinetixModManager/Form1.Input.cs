using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using DavyKager;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewMod = KinetixModManager.GameMod;

namespace KinetixModManager;

/// <summary>Keyboard input, focus cycling, and list key handling for Form1.</summary>
public partial class Form1
{
	private void Form1_KeyDown(object? sender, KeyEventArgs e)
	{
		if (_settings.ActiveGame == "None")
		{
			if (e.KeyCode == Keys.Escape)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				Application.Exit();
			}
			return;
		}
		if (IsShortcut(e, "Manual"))
		{
			e.SuppressKeyPress = true;
			ShowManual();
		}
		if (IsShortcut(e, "ContextHelp"))
		{
			e.SuppressKeyPress = true;
			ShowContextHelp();
		}
		if (IsShortcut(e, "ControlsHelp"))
		{
			e.SuppressKeyPress = true;
			ShowAccessibilityControls();
		}
		if (IsShortcut(e, "LaunchGame"))
		{
			e.SuppressKeyPress = true;
			LaunchGame();
		}
		if (IsShortcut(e, "OpenLogFile"))
		{
			e.SuppressKeyPress = true;
			OpenRawSmapiLog();
		}
		if (IsShortcut(e, "Settings"))
		{
			e.SuppressKeyPress = true;
			ShowSettings();
		}
		if (IsShortcut(e, "Login"))
		{
			e.SuppressKeyPress = true;
			if (mainTabs.SelectedTab == tabInstalled && listInstalled.Focused && listInstalled.SelectedItem is StardewMod)
			{
				_ = LinkModUpdateSource();
			}
			else
			{
				PromptForApiKey();
			}
		}
		if (IsShortcut(e, "InstallZip"))
		{
			e.SuppressKeyPress = true;
			ManualInstall();
		}
		if (IsShortcut(e, "OpenModPage"))
		{
			e.SuppressKeyPress = true;
			OpenModPage();
		}
		if (IsShortcut(e, "OpenConfig"))
		{
			e.SuppressKeyPress = true;
			OpenSelectedModConfig();
		}
		if (IsShortcut(e, "OpenManifest"))
		{
			e.SuppressKeyPress = true;
			OpenSelectedModManifest();
		}
		if (IsShortcut(e, "OpenDownloads"))
		{
			e.SuppressKeyPress = true;
			Process.Start("explorer.exe", downloadsPath);
		}
		if (IsShortcut(e, "OpenBackups"))
		{
			e.SuppressKeyPress = true;
			Process.Start("explorer.exe", backupsPath);
		}
		if (IsShortcut(e, "ManualID"))
		{
			e.SuppressKeyPress = true;
			_ = LinkModUpdateSource();
		}
		if (IsShortcut(e, "ChangeCategory"))
		{
			e.SuppressKeyPress = true;
			if (e.Shift)
			{
				BatchManageCategory();
			}
			else
			{
				SetManualCategory();
			}
		}
		if (IsShortcut(e, "ShowDependencies"))
		{
			e.SuppressKeyPress = true;
			ShowDependencies();
		}
		if (IsShortcut(e, "QuickFix"))
		{
			e.SuppressKeyPress = true;
			QuickFixDependencies();
		}
		if (IsShortcut(e, "Search"))
		{
			e.SuppressKeyPress = true;
			if (mainTabs.SelectedIndex == (int)AppTab.Installed)
			{
				txtSearchInstalled.Focus();
			}
			else if (mainTabs.SelectedIndex == (int)AppTab.SmapiLog)
			{
				txtSearchLog.Focus();
			}
		}
		if (IsShortcut(e, "UpdateAll"))
		{
			e.SuppressKeyPress = true;
			_ = UpdateAllMods();
		}
		if (IsShortcut(e, "SaveProfile"))
		{
			e.SuppressKeyPress = true;
			CreateProfileFromCurrent();
		}
		if (IsShortcut(e, "ReadDescription"))
		{
			e.SuppressKeyPress = true;
			ReadSelectedDescription();
		}
		if (IsShortcut(e, "PruneBackups"))
		{
			e.SuppressKeyPress = true;
			PruneAllBackups();
		}
		if (IsShortcut(e, "RefreshAll"))
		{
			e.SuppressKeyPress = true;
			RefreshAllData(checkUpdates: true);
			Speak("Refreshing everything.");
		}
		if (IsShortcut(e, "RefreshInstalled"))
		{
			e.SuppressKeyPress = true;
			_ = RefreshModList(checkUpdates: false);
			Speak("Refreshed installed mods.");
		}
		if (IsShortcut(e, "CycleFocus"))
		{
			e.SuppressKeyPress = true;
			HandleCycleFocus();
		}
		if (IsShortcut(e, "OpenErrorLog") && File.Exists("mod_manager_log.txt"))
		{
			Process.Start(new ProcessStartInfo("notepad.exe", "mod_manager_log.txt")
			{
				UseShellExecute = true
			});
		}
	}

	/// <summary>
	/// Cycles keyboard focus between the main tab headers and the primary control of the active tab.
	/// In the Wiki tab: cycles list → WebView → tab headers. In other tabs: toggles tab headers ↔ list.
	/// </summary>
	private void HandleCycleFocus()
	{
		// Use Control.Focused on the actual candidate controls rather than Form.ActiveControl:
		// ActiveControl only reports the immediate active *container* (the TabControl / TabPage),
		// not the deeply nested ListBox or WebView that truly holds focus, so identity checks
		// against it never matched and the cycle skipped the WebView step.
		bool onHeaders = mainTabs.Focused;

		// If focus is somewhere inside the TabControl but not on the headers
		if (!onHeaders)
		{
			// If we are in the Wiki tab, cycle between Results -> Web View -> Tabs
			if (mainTabs.SelectedIndex == (int)AppTab.Wiki)
			{
				// If we're in the list, move to web view
				if (listWikiResults.Focused)
				{
					webViewWiki.Focus();
					Speak("Wiki Content");
				}
				else
				{
					// Otherwise (likely in WebView or search boxes), move to headers
					mainTabs.Focus();
					Speak((mainTabs.SelectedTab?.Text ?? "") + " Tab");
				}
			}
			else if (mainTabs.SelectedIndex == (int)AppTab.Walkthroughs)
			{
				// If we're in the list, move to web view
				if (listWalkthroughs.Focused)
				{
					webViewWalkthrough.Focus();
					Speak("Walkthrough Content");
				}
				else
				{
					mainTabs.Focus();
					Speak((mainTabs.SelectedTab?.Text ?? "") + " Tab");
				}
			}
			else
			{
				// In other tabs, just jump back to the Tab headers
				mainTabs.Focus();
				Speak((mainTabs.SelectedTab?.Text ?? "") + " Tab");
			}
		}
		else
		{
			// Focus is on the Tab headers, jump into the primary control of the current tab.
			// The list's Enter handler (List_Enter) announces the name/position, so focusing is
			// enough here — and it works the same whether the user arrived via F6, Tab, or click.
			switch (mainTabs.SelectedIndex)
			{
				case (int)AppTab.Installed:    listInstalled.Focus();   break;
				case (int)AppTab.Updates:      listUpdates.Focus();     break;
				case (int)AppTab.Backups:      listBackups.Focus();     break;
				case (int)AppTab.Discovery:    listDiscovery.Focus();   break;
				case (int)AppTab.Wiki:         listWikiResults.Focus(); break;
				case (int)AppTab.Walkthroughs: listWalkthroughs.Focus(); break;
				case (int)AppTab.Profiles:     listProfiles.Focus();    break;
				case (int)AppTab.SmapiLog:     listLog.Focus();         break;
			}
		}
	}

	/// <summary>Speaks the full <see cref="object.ToString"/> description of the currently focused list item.</summary>
	private void ReadSelectedDescription()
	{
		ListBox listBox;
		if (mainTabs.SelectedIndex == (int)AppTab.Installed)
		{
			listBox = listInstalled;
		}
		else if (mainTabs.SelectedIndex == (int)AppTab.Updates)
		{
			listBox = listUpdates;
		}
		else
		{
			if (mainTabs.SelectedIndex != (int)AppTab.Discovery)
			{
				return;
			}
			listBox = listDiscovery;
		}
		if (listBox.SelectedItem is StardewMod stardewMod)
		{
			if (!string.IsNullOrEmpty(stardewMod.Description))
			{
				Speak(stardewMod.Description);
			}
			else
			{
				Speak("No description available for this mod.");
			}
		}
	}

	private async void List_KeyDown(object? sender, KeyEventArgs e)
	{
		if (!(sender is ListBox list))
		{
			return;
		}
		if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z && !e.Control && !e.Alt)
		{
			char c = char.ToLower((char)e.KeyCode);
			_searchTimer.Stop();
			_searchBuffer += c;
			_searchTimer.Start();
			int num = list.SelectedIndex;
			if (_searchBuffer.Length == 1)
			{
				num++;
			}
			for (int i = 0; i < list.Items.Count; i++)
			{
				int num2 = (num + i) % list.Items.Count;
				if (list.Items[num2] is StardewMod stardewMod)
				{
					string text = (stardewMod.IsGroup ? stardewMod.GroupName : stardewMod.Name).ToLower();
					if (!string.IsNullOrEmpty(text) && text.StartsWith(_searchBuffer))
					{
						list.SelectedIndex = num2;
						e.Handled = true;
						e.SuppressKeyPress = true;
						return;
					}
				}
			}
			if (_searchBuffer.Length > 1)
			{
				string text2 = c.ToString();
				for (int j = 0; j < list.Items.Count; j++)
				{
					int num3 = (list.SelectedIndex + 1 + j) % list.Items.Count;
					if (list.Items[num3] is StardewMod stardewMod2 && (stardewMod2.IsGroup ? stardewMod2.GroupName : stardewMod2.Name).ToLower().StartsWith(text2))
					{
						list.SelectedIndex = num3;
						break;
					}
				}
				_searchBuffer = text2;
			}
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
		if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
		{
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
		if (list.Name == "listInstalled" && list.SelectedItem is StardewMod stardewMod3)
		{
			bool flag = e.KeyCode == Keys.Right || e.KeyValue == 187 || e.KeyCode == Keys.Add;
			bool flag2 = e.KeyCode == Keys.Left || e.KeyValue == 189 || e.KeyCode == Keys.Subtract;
			if (stardewMod3.IsGroup)
			{
				string groupName = stardewMod3.GroupName;
				if (flag)
				{
					if (!_expandedGroups.Contains(groupName))
					{
						_expandedGroups.Add(groupName);
						RebuildInstalledListBox();
						Speak("Expanded.");
					}
				}
				else if (flag2 && _expandedGroups.Contains(groupName))
				{
					_expandedGroups.Remove(groupName);
					RebuildInstalledListBox();
					Speak("Collapsed.");
				}
			}
			else if (stardewMod3.IsSubMod && flag2)
			{
				string relativePath = Path.GetRelativePath(_settings.CurrentModsPath, stardewMod3.FolderPath);
				int num4 = relativePath.IndexOf(Path.DirectorySeparatorChar);
				string text3 = ((num4 == -1) ? relativePath : relativePath.Substring(0, num4));
				if (_expandedGroups.Contains(text3))
				{
					_expandedGroups.Remove(text3);
					StardewMod tag = new StardewMod
					{
						UniqueId = "GROUP:" + text3
					};
					listInstalled.SelectedItem = null;
					listInstalled.Tag = tag;
					RebuildInstalledListBox();
					for (int k = 0; k < listInstalled.Items.Count; k++)
					{
						if ((listInstalled.Items[k] as StardewMod)?.UniqueId == "GROUP:" + text3)
						{
							listInstalled.SelectedIndex = k;
							break;
						}
					}
					Speak(text3 + " Collapsed.");
				}
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			if (e.KeyCode == Keys.Space)
			{
				ToggleModStatus();
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			if (e.KeyCode == Keys.Delete)
			{
				DeleteSelectedMod();
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			if (e.KeyCode == Keys.Apps)
			{
				MessageBox.Show($"Mod: {stardewMod3.Name}\nAuthor: {stardewMod3.Author}\nDescription: {stardewMod3.Description}", "Details");
				e.Handled = true;
			}
			if (e.KeyCode == Keys.L && e.Control)
			{
				_ = LinkModUpdateSource();
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
		}
		if (list.Name == "listUpdates" && list.SelectedItem is StardewMod stardewMod4 && e.KeyCode == Keys.Delete)
		{
			if (MessageBox.Show($"Ignore version {stardewMod4.LatestVersion} for {stardewMod4.Name}?", "Ignore Update", MessageBoxButtons.YesNo) == DialogResult.Yes)
			{
				_settings.IgnoredVersions[stardewMod4.UniqueId] = stardewMod4.LatestVersion ?? "";
				_settings.Save();
				int oldIndex = listUpdates.SelectedIndex;
				listUpdates.Items.Remove(stardewMod4);
				if (listUpdates.Items.Count > 0)
				{
					listUpdates.SelectedIndex = Math.Min(oldIndex, listUpdates.Items.Count - 1);
					if (listUpdates.SelectedItem != null)
					{
						string itemText = listUpdates.SelectedItem.ToString() ?? "";
						Speak($"Update ignored. {itemText}. {listUpdates.SelectedIndex + 1} of {listUpdates.Items.Count}");
					}
				}
				else
				{
					Speak("Update ignored. List is empty.");
				}
			}
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
		if (list.Name == "listBackups" && list.SelectedItem is BackupItem backupItem)
		{
			if (e.KeyCode == Keys.Delete)
			{
				DeleteSelectedBackup();
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			if (e.KeyCode == Keys.Return)
			{
				if (MessageBox.Show("Restore backup " + backupItem.Name + "?", "Confirm Restore", MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					_ = InstallFromZip(backupItem.FullPath);
				}
				e.Handled = true;
			}
		}
		if (list.Name == "listProfiles" && list.SelectedItem is ModProfile modProfile)
		{
			if (e.KeyCode == Keys.Return)
			{
				ApplyProfile(modProfile);
				e.Handled = true;
			}
			if (e.KeyCode == Keys.Delete)
			{
				if (MessageBox.Show("Delete profile '" + modProfile.Name + "'?", "Confirm Delete", MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					string path = Path.Combine(profilesPath, modProfile.Name + ".json");
					if (File.Exists(path))
					{
						File.Delete(path);
					}
					RefreshProfilesList();
					Speak("Profile deleted.");
				}
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
		}
		if (list.Name == "listLog" && e.KeyCode == Keys.Return && list.SelectedItem is LogEntry logEntry)
		{
			// A SMAPI "you can update N mods" line includes the mod's page URL. Pressing Enter on such
			// a line opens it (Nexus pages on the Files tab, just like the updates list does) so the
			// user can grab an update SMAPI's own checker missed. Lines without a link fall through to
			// the view-return / detail behavior below.
			string logUrl = LogAnalyzer.ExtractUrl(logEntry.Text);
			if (!string.IsNullOrEmpty(logUrl))
			{
				try
				{
					Process.Start(new ProcessStartInfo(logUrl) { UseShellExecute = true });
					Speak("Opening mod page in your browser.");
				}
				catch (Exception ex)
				{
					LogError("SmapiLog", "Could not open log link: " + ex.Message);
					Speak("Could not open the link.");
				}
				e.Handled = true;
				return;
			}

			if (list.Items.Count < _fullLogEntries.Count)
			{
				txtSearchLog.Text = "";
				list.BeginUpdate();
				list.Items.Clear();
				foreach (LogEntry fullLogEntry in _fullLogEntries)
				{
					list.Items.Add(fullLogEntry);
				}
				list.SelectedItem = logEntry;
				list.EndUpdate();
				Speak("Returned to filtered view. Scroll down to read more.");
				e.Handled = true;
				return;
			}
			string text4 = logEntry.Text;
			string suggestedFix = LogAnalyzer.GetSuggestedFix(text4);
			string text5 = text4;
			if (!string.IsNullOrEmpty(suggestedFix))
			{
				text5 = text5 + "\n\nSUGGESTED FIX:\n" + suggestedFix;
			}
			MessageBox.Show(text5, "Log Detail");
			e.Handled = true;
		}
		if (list.Name == "listLog" && IsShortcut(e, "Login"))
		{
			await UploadSmapiLog();
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
		// Ctrl+C copies the selected log line(s) to the clipboard so the user can paste them elsewhere
		// (a forum post, the mod's Discord) without opening and searching SMAPI-latest.txt by hand.
		if (list.Name == "listLog" && e.Control && e.KeyCode == Keys.C)
		{
			var selected = list.SelectedItems.Count > 0
				? list.SelectedItems.Cast<object>()
				: (list.SelectedItem != null ? new[] { list.SelectedItem } : System.Array.Empty<object>());
			string copied = string.Join("\r\n", selected.Select(o => o.ToString()));
			if (!string.IsNullOrEmpty(copied))
			{
				Clipboard.SetText(copied);
				int count = copied.Split('\n').Length;
				Speak(count == 1 ? "Copied line to clipboard." : $"Copied {count} lines to clipboard.");
			}
			else
			{
				Speak("No log lines selected to copy.");
			}
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
		if (e.KeyCode == Keys.Return && (list.Name == "listUpdates" || list.Name == "listDiscovery"))
		{
			OpenModPage();
			e.Handled = true;
		}
	}
}
