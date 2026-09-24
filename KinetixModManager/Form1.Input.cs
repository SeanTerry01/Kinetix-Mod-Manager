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
		// Recorded before anything else can return, so the held key is known even for presses this handler
		// otherwise ignores — an overlay opening mid-press would otherwise leave a stale key held here.
		bool isAutoRepeat = !_keyRepeat.IsFirstPress((int)e.KeyData);

		// While a prompt or an in-window view is covering the window, the window's own shortcuts must stay out
		// of the way — F5 must not launch the game from behind a confirmation, and Escape belongs to whatever
		// is on top, which handles it itself. The overlay disables the controls beneath it, but key preview
		// reaches the form whatever has focus, so it has to be turned away here.
		if (OverlayIsOpen) return;

		// The user is driving. Startup finishes by putting focus on the first tab and saying so, which is right
		// when they have been waiting for it and wrong the moment they have started moving around themselves —
		// being pulled back to Installed Mods mid-keystroke is worse than never being told where you started.
		// Set here rather than on any key at all, so typing into a first-run wizard does not count as wandering
		// the main window.
		_userMovedSinceLoadBegan = true;

		// Every command below is a one-shot, and none of them wants to run again because a key was held a
		// moment too long: holding Refresh Everything started a refresh per repeat, and holding F1 would have
		// opened the manual as many times. Returning without marking the key handled leaves it to whatever has
		// focus, so a held Backspace in a text box still repeats the way typing is supposed to.
		if (isAutoRepeat) return;

		if (_settings.ActiveGame == "None")
		{
			// On the game-selection screen most shortcuts act on a mod list that isn't loaded yet, but the
			// app-wide ones still make sense and are needed here — Settings in particular, since that is where
			// a game folder is set when auto-detection missed the install. Focus goes back to the game list
			// afterwards so the user lands where they left off.
			if (e.KeyCode == Keys.Escape)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				Application.Exit();
				return;
			}
			if (IsShortcut(e, "Manual") || IsShortcut(e, "ChangeLog") || IsShortcut(e, "Settings"))
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				if (IsShortcut(e, "Manual")) ShowManual();
				else if (IsShortcut(e, "ChangeLog")) ShowChangeLog();
				else ShowSettings();
				_lstGames?.Focus();
			}
			return;
		}
		if (IsShortcut(e, "Manual"))
		{
			e.SuppressKeyPress = true;
			ShowManual();
		}
		if (IsShortcut(e, "ChangeLog"))
		{
			e.SuppressKeyPress = true;
			ShowChangeLog();
		}
		if (IsShortcut(e, "ModDocs"))
		{
			e.SuppressKeyPress = true;
			ShowModDocs();
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
		if (IsShortcut(e, "SearchHistory"))
		{
			e.SuppressKeyPress = true;
			ShowSearchHistoryDialog();
		}
		if (IsShortcut(e, "LaunchGame"))
		{
			e.SuppressKeyPress = true;
			LaunchGame();
		}
		if (IsShortcut(e, "OpenLogFile"))
		{
			e.SuppressKeyPress = true;
			OpenGameLog();
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
				Fire(LinkModUpdateSource(), "LinkModUpdateSource");
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
		if (IsShortcut(e, "OpenConfigFile"))
		{
			e.SuppressKeyPress = true;
			OpenSelectedModConfigFile();
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
			Fire(LinkModUpdateSource(), "LinkModUpdateSource");
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
		if (IsShortcut(e, "MarkVersionInstalled"))
		{
			e.SuppressKeyPress = true;
			MarkSelectedUpdateAsInstalled();
		}
		if (IsShortcut(e, "QuickFix"))
		{
			e.SuppressKeyPress = true;
			QuickFixDependencies();
		}
		if (IsShortcut(e, "Endorse"))
		{
			e.SuppressKeyPress = true;
			EndorseSelectedMod();
		}
		if (IsShortcut(e, "ViewChangelog"))
		{
			e.SuppressKeyPress = true;
			ViewModChangelog();
		}
		if (IsShortcut(e, "ViewDescription"))
		{
			e.SuppressKeyPress = true;
			ViewModDescription();
		}
		if (IsShortcut(e, "CheckBrokenMods"))
		{
			e.SuppressKeyPress = true;
			Fire(ShowBrokenModsReport(), "ShowBrokenModsReport");
		}
		if (IsShortcut(e, "HealthCheck"))
		{
			e.SuppressKeyPress = true;
			Fire(RunHealthCheck(), "RunHealthCheck");
		}
		if (IsShortcut(e, "PluginSlots"))
		{
			e.SuppressKeyPress = true;
			AnnouncePluginSlotUsage();
		}
		if (IsShortcut(e, "SaveManager"))
		{
			e.SuppressKeyPress = true;
			Fire(ShowSaveManager(), "ShowSaveManager");
		}
		if (IsShortcut(e, "DownloadsHistory"))
		{
			e.SuppressKeyPress = true;
			ShowDownloadsHistory();
		}
		if (IsShortcut(e, "InstallPendingDownload"))
		{
			e.SuppressKeyPress = true;
			ShowPendingDownloads();
		}
		if (IsShortcut(e, "TrackedMods"))
		{
			e.SuppressKeyPress = true;
			Fire(ShowTrackedMods(), "ShowTrackedMods");
		}
		if (IsShortcut(e, "EditNote"))
		{
			e.SuppressKeyPress = true;
			SetModNote();
		}
		if (IsShortcut(e, "ExportCollection"))
		{
			e.SuppressKeyPress = true;
			Fire(ExportCollection(), "ExportCollection");
		}
		if (IsShortcut(e, "InstallCollection"))
		{
			e.SuppressKeyPress = true;
			Fire(InstallCollectionAsync(), "InstallCollectionAsync");
		}
		if (IsShortcut(e, "ApiCredits"))
		{
			e.SuppressKeyPress = true;
			ShowApiCredits();
		}
		if (IsShortcut(e, "DiagnoseAi"))
		{
			e.SuppressKeyPress = true;
			DiagnoseWithAi();
		}
		if (IsShortcut(e, "FileConflicts"))
		{
			e.SuppressKeyPress = true;
			ShowFileConflictsReport();
		}
		if (IsShortcut(e, "CheckRequirements"))
		{
			e.SuppressKeyPress = true;
			Fire(ShowRequirementsReport(), "ShowRequirementsReport");
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
			Fire(UpdateAllMods(), "UpdateAllMods");
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
		if (IsShortcut(e, "DeleteOldBackups"))
		{
			e.SuppressKeyPress = true;
			PruneAllBackups();
		}
		if (IsShortcut(e, "RefreshAll"))
		{
			e.SuppressKeyPress = true;
			RequestManualRefresh(everything: true);
		}
		if (IsShortcut(e, "RefreshInstalled"))
		{
			e.SuppressKeyPress = true;
			RequestManualRefresh(everything: false);
		}
		if (IsShortcut(e, "CycleFocus"))
		{
			e.SuppressKeyPress = true;
			HandleCycleFocus();
		}
		if (IsShortcut(e, "AutoSort"))
		{
			e.SuppressKeyPress = true;
			Fire(AutoSortPluginsAsync(), "AutoSortPluginsAsync");
		}
		if (IsShortcut(e, "OpenErrorLog"))
		{
			e.SuppressKeyPress = true;
			OpenErrorLog();
		}

		// Reading the suggested list is not curating, so it is never gated. Unmapped by default; this is here so
		// a key assigned in the shortcut manager works. See Form1.SuggestedMods.
		if (IsShortcut(e, "SuggestedMods"))
		{
			e.Handled = true;
			e.SuppressKeyPress = true;
			ShowSuggestedMods();
		}
		// The way in, and the only one of the three that works while curation is off — turning it on is precisely
		// what it is for. See Form1.Curator.
		if (IsShortcut(e, "CurationMode"))
		{
			e.Handled = true;
			e.SuppressKeyPress = true;
			ToggleCurationMode();
		}
		// The other two do nothing until curation is on. Checked here rather than inside each command so a key
		// that is switched off stays unhandled, and whatever else wants it can have it.
		if (_settings.CuratorMode && IsShortcut(e, "MarkSuggestion"))
		{
			e.Handled = true;
			e.SuppressKeyPress = true;
			ToggleModSuggestion();
		}
		if (_settings.CuratorMode && IsShortcut(e, "SuggestedList"))
		{
			e.Handled = true;
			e.SuppressKeyPress = true;
			ShowSuggestedModsReview();
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
				case AppTab.Creations:    listCreations.Focus();   break;
				case AppTab.GameLog:      listGameLog.Focus();     break;
				case AppTab.MinecraftPacks: listMinecraftPacks.Focus(); break;
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
				// Chunked so a long description is read in full — a single long spoken string can be clipped.
				SpeakLong(stardewMod.Description);
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
				// The screen reader re-reads the group line on selection (it states "Expanded"/"Collapsed") and
				// List_SelectedIndexChanged adds the position, which is all that should be heard here.
				if (flag)
				{
					if (!_expandedGroups.Contains(groupName))
					{
						_expandedGroups.Add(groupName);
						RebuildInstalledListBox();
					}
				}
				else if (flag2 && _expandedGroups.Contains(groupName))
				{
					_expandedGroups.Remove(groupName);
					RebuildInstalledListBox();
				}
			}
			else if (stardewMod3.IsSubMod && flag2)
			{
				// The same key the list grouped by, worked out the same way — a sub-mod has to name the group it
				// is under, and a disabled BepInEx mod does not live under the mods folder to be measured from.
				string text3 = ModEnableState.InstalledGroupFolder(_settings.CurrentModsPath, stardewMod3.FolderPath);
				if (_expandedGroups.Contains(text3))
				{
					_expandedGroups.Remove(text3);
					// Select the parent group directly in the rebuild. Passing its id avoids the previous
					// null-then-reselect, which briefly selected index 0 and made the screen reader announce
					// the first mod before landing on the collapsed group. The screen reader reads the group
					// line (stating "Collapsed") on selection, so suppress the rebuild's own speech.
					RebuildInstalledListBox("GROUP:" + text3);
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
				SpeakBox(Loc.T("modlist.detailsBox", stardewMod3.Name, stardewMod3.Author, stardewMod3.Description), Loc.T("modlist.detailsTitle"));
				e.Handled = true;
			}
			if (e.KeyCode == Keys.L && e.Control)
			{
				Fire(LinkModUpdateSource(), "LinkModUpdateSource");
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
		}
		if (list.Name == "listUpdates" && list.SelectedItem is StardewMod stardewMod4 && e.KeyCode == Keys.Delete)
		{
			if (SpeakBox(Loc.T("modlist.ignoreConfirm", stardewMod4.LatestVersion ?? "", stardewMod4.Name), Loc.T("modlist.ignoreTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
			{
				_settings.IgnoredVersions[stardewMod4.UniqueId] = stardewMod4.LatestVersion ?? "";
				_settings.Save();
				if (RemoveSettledUpdateRow(stardewMod4) is { } landed)
					Speak(Loc.T("modlist.updateIgnoredPos", landed.Row, landed.Index, landed.Count));
				else
					Speak(Loc.T("modlist.updateIgnoredEmpty"));
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
				if (SpeakBox(Loc.T("modlist.restoreConfirm", backupItem.Name), Loc.T("modlist.restoreTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					Fire(InstallFromZip(backupItem.FullPath), "InstallFromZip");
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
				if (SpeakBox(Loc.T("modlist.deleteProfileConfirm", modProfile.Name), Loc.T("common.confirmDelete"), MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					string path = ProfileStore.PathFor(profilesPath, modProfile.Name);
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
			SpeakBox(text5, Loc.T("modlist.logDetailTitle"));
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
		// Alt+O: run the same search again, this time asking the catalogues the preferred-source mode leaves
		// out. Only offered when there is somewhere else to ask that would actually answer — see
		// AnnounceOtherSources, which is what told the user the key was there.
		if (list.Name == "listDiscovery" && e.Alt && e.KeyCode == Keys.O)
		{
			Fire(RunDiscovery(alsoTheOthers: true), "RunDiscovery");
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
		if (e.KeyCode == Keys.Return && (list.Name == "listUpdates" || list.Name == "listDiscovery"))
		{
			// On the Discovery list's inline "Load more" row, Enter loads the next page of results;
			// on any real result it opens the Nexus page as usual.
			if (list.Name == "listDiscovery" && list.SelectedItem is DiscoveryLoadMoreRow)
			{
				Fire(RunDiscovery(loadMore: true), "RunDiscovery");
			}
			// A result the manager can fetch itself gets an offer to do so, rather than handing the user to a
			// web page whose download button they then have to find. Everything else opens its page, because
			// for those that IS the only way in — and that is the answer for every site added later too:
			// worst case it degrades to the page, which every site has.
			else if (list.Name == "listDiscovery" && list.SelectedItem is StardewMod result && CanFetchDirectly(result))
			{
				Fire(OfferMinecraftSearchResultAsync(result), "OfferMinecraftSearchResultAsync");
			}
			// The Fabric loader and the Minecraft version are not mods and have no page to offer, so Enter does the
			// one thing they are for. See CheckMinecraftPlatformUpdatesAsync.
			else if (list.Name == "listUpdates" && list.SelectedItem is StardewMod platform &&
					 (platform.UniqueId == FabricLoaderRowId || platform.UniqueId == MinecraftVersionRowId ||
					  platform.UniqueId == ModpackRowId))
			{
				Fire(DownloadAndInstallUpdate(platform), "DownloadAndInstallUpdate");
			}
			// An update the manager can fetch itself asks which the user wants, rather than assuming they pressed
			// Enter to go and read about a mod they already have.
			else if (list.Name == "listUpdates" && list.SelectedItem is StardewMod pending && CanUpdateWithoutBrowser(pending))
			{
				Fire(OfferUpdateChoiceAsync(pending), "OfferUpdateChoiceAsync");
			}
			else
			{
				OpenModPage();
			}
			e.Handled = true;
		}
	}
}
