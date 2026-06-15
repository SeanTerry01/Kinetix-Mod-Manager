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
			if (CurrentTab() == AppTab.Installed)
			{
				txtSearchInstalled.Focus();
			}
			else if (CurrentTab() == AppTab.SmapiLog)
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
			Speak(Loc.T("modlist.refreshingAll"));
		}
		if (IsShortcut(e, "RefreshInstalled"))
		{
			e.SuppressKeyPress = true;
			_ = RefreshModList(checkUpdates: false);
			Speak(Loc.T("modlist.refreshedInstalled"));
		}
		if (IsShortcut(e, "CycleFocus"))
		{
			e.SuppressKeyPress = true;
			HandleCycleFocus();
		}
		if (IsShortcut(e, "AutoSort"))
		{
			e.SuppressKeyPress = true;
			_ = AutoSortPluginsAsync();
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
			if (CurrentTab() == AppTab.Wiki)
			{
				// If we're in the list, move to web view
				if (listWikiResults.Focused)
				{
					webViewWiki.Focus();
					Speak(Loc.T("modlist.wikiContent"));
				}
				else
				{
					// Otherwise (likely in WebView or search boxes), move to headers
					mainTabs.Focus();
					Speak(Loc.T("common.tabSuffix", mainTabs.SelectedTab?.Text ?? ""));
				}
			}
			else if (CurrentTab() == AppTab.Walkthroughs)
			{
				// If we're in the list, move to web view
				if (listWalkthroughs.Focused)
				{
					webViewWalkthrough.Focus();
					Speak(Loc.T("modlist.walkthroughContent"));
				}
				else
				{
					mainTabs.Focus();
					Speak(Loc.T("common.tabSuffix", mainTabs.SelectedTab?.Text ?? ""));
				}
			}
			else
			{
				// In other tabs, just jump back to the Tab headers
				mainTabs.Focus();
				Speak(Loc.T("common.tabSuffix", mainTabs.SelectedTab?.Text ?? ""));
			}
		}
		else
		{
			// Focus is on the Tab headers, jump into the primary control of the current tab.
			// The list's Enter handler (List_Enter) announces the name/position, so focusing is
			// enough here — and it works the same whether the user arrived via F6, Tab, or click.
			switch (CurrentTab())
			{
				case AppTab.Installed:    listInstalled.Focus();   break;
				case AppTab.Updates:      listUpdates.Focus();     break;
				case AppTab.Backups:      listBackups.Focus();     break;
				case AppTab.Discovery:    listDiscovery.Focus();   break;
				case AppTab.Wiki:         listWikiResults.Focus(); break;
				case AppTab.Walkthroughs: listWalkthroughs.Focus(); break;
				case AppTab.Profiles:     listProfiles.Focus();    break;
				case AppTab.ModPriority:  listModPriority.Focus(); break;
				case AppTab.PluginOrder:  listPluginOrder.Focus(); break;
				case AppTab.SmapiLog:     listLog.Focus();         break;
			}
		}
	}

	/// <summary>Speaks the full <see cref="object.ToString"/> description of the currently focused list item.</summary>
	private void ReadSelectedDescription()
	{
		ListBox listBox;
		if (CurrentTab() == AppTab.Installed)
		{
			listBox = listInstalled;
		}
		else if (CurrentTab() == AppTab.Updates)
		{
			listBox = listUpdates;
		}
		else
		{
			if (CurrentTab() != AppTab.Discovery)
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
				Speak(Loc.T("modlist.noDescription"));
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
				// The screen reader re-reads the group line on selection (it states "Expanded"/"Collapsed"),
				// and List_SelectedIndexChanged announces the position, so suppress the rebuild's own speech
				// to avoid repeating the whole group line. See _suppressRebuildSpeak.
				if (flag)
				{
					if (!_expandedGroups.Contains(groupName))
					{
						_expandedGroups.Add(groupName);
						_suppressRebuildSpeak = true;
						RebuildInstalledListBox();
						_suppressRebuildSpeak = false;
					}
				}
				else if (flag2 && _expandedGroups.Contains(groupName))
				{
					_expandedGroups.Remove(groupName);
					_suppressRebuildSpeak = true;
					RebuildInstalledListBox();
					_suppressRebuildSpeak = false;
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
					// Select the parent group directly in the rebuild. Passing its id avoids the previous
					// null-then-reselect, which briefly selected index 0 and made the screen reader announce
					// the first mod before landing on the collapsed group. The screen reader reads the group
					// line (stating "Collapsed") on selection, so suppress the rebuild's own speech.
					_suppressRebuildSpeak = true;
					RebuildInstalledListBox("GROUP:" + text3);
					_suppressRebuildSpeak = false;
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
				MessageBox.Show(Loc.T("modlist.detailsBox", stardewMod3.Name, stardewMod3.Author, stardewMod3.Description), Loc.T("modlist.detailsTitle"));
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
			if (MessageBox.Show(Loc.T("modlist.ignoreConfirm", stardewMod4.LatestVersion ?? "", stardewMod4.Name), Loc.T("modlist.ignoreTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
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
						Speak(Loc.T("modlist.updateIgnoredPos", itemText, listUpdates.SelectedIndex + 1, listUpdates.Items.Count));
					}
				}
				else
				{
					Speak(Loc.T("modlist.updateIgnoredEmpty"));
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
				if (MessageBox.Show(Loc.T("modlist.restoreConfirm", backupItem.Name), Loc.T("modlist.restoreTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
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
				if (MessageBox.Show(Loc.T("modlist.deleteProfileConfirm", modProfile.Name), Loc.T("common.confirmDelete"), MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					string path = Path.Combine(profilesPath, modProfile.Name + ".json");
					if (File.Exists(path))
					{
						File.Delete(path);
					}
					RefreshProfilesList();
					Speak(Loc.T("modlist.profileDeleted"));
				}
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
		}
		if (list.Name == "listLog" && e.KeyCode == Keys.Return && list.SelectedItem is LogEntry logEntry)
		{
			// A SMAPI line can include one or more links (e.g. a "no longer compatible" line lists the
			// Nexus, GitHub, and smapi.io pages). Enter on a single-link line opens it directly (Nexus
			// pages on the Files tab, like the updates list); a multi-link line shows a picker so the
			// user chooses which to open. Lines without a link fall through to the view/detail behavior.
			List<string> logUrls = LogAnalyzer.ExtractUrls(logEntry.Text);
			if (logUrls.Count == 1)
			{
				OpenLogLink(logUrls[0]);
				e.Handled = true;
				return;
			}
			if (logUrls.Count > 1)
			{
				ShowLogLinkPicker(logUrls);
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
				Speak(Loc.T("modlist.returnedFiltered"));
				e.Handled = true;
				return;
			}
			string text4 = logEntry.Text;
			string suggestedFix = LogAnalyzer.GetSuggestedFix(text4);
			string text5 = text4;
			if (!string.IsNullOrEmpty(suggestedFix))
			{
				text5 = text5 + Loc.T("modlist.suggestedFixSuffix", suggestedFix);
			}
			MessageBox.Show(text5, Loc.T("modlist.logDetailTitle"));
			e.Handled = true;
		}
		if (list.Name == "listLog" && IsShortcut(e, "Login"))
		{
			await UploadSmapiLog();
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
		// Re-read SMAPI-latest.txt on demand. SMAPI holds the log open for the whole game session, so this
		// pulls in lines written since the tab was last loaded (e.g. after triggering an error in-game)
		// without closing Stardew. The shared-read in RefreshSmapiLog means it succeeds mid-session.
		if (list.Name == "listLog" && IsShortcut(e, "RefreshLog"))
		{
			RefreshSmapiLog();
			Speak(listLog.Items.Count > 0
				? Loc.T("modlist.logRefreshed", listLog.Items.Count)
				: Loc.T("modlist.logRefreshedEmpty"));
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
				Speak(count == 1 ? Loc.T("modlist.copiedOne") : Loc.T("modlist.copiedMany", count));
			}
			else
			{
				Speak(Loc.T("modlist.noLinesSelected"));
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
