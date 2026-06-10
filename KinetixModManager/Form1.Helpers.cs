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

/// <summary>Shortcut resolution, status/speech output, list events, discovery, and loading helpers for Form1.</summary>
public partial class Form1
{
	/// <summary>Returns a human-readable key label for <paramref name="action"/> (e.g. "Ctrl+R").</summary>
	private string GetShortcutString(string action)
	{
		if (_settings.Shortcuts.TryGetValue(action, out var value))
		{
			if (value == Keys.None)
			{
				return "Unmapped";
			}
			StringBuilder stringBuilder = new StringBuilder();
			if ((value & Keys.Control) == Keys.Control)
			{
				stringBuilder.Append("Ctrl+");
			}
			if ((value & Keys.Shift) == Keys.Shift)
			{
				stringBuilder.Append("Shift+");
			}
			if ((value & Keys.Alt) == Keys.Alt)
			{
				stringBuilder.Append("Alt+");
			}
			stringBuilder.Append(value & Keys.KeyCode);
			return stringBuilder.ToString();
		}
		return "Unmapped";
	}

	/// <summary>Returns <c>true</c> if <paramref name="e"/> matches the configured shortcut for <paramref name="action"/>.</summary>
	private bool IsShortcut(KeyEventArgs e, string action)
	{
		if (!_settings.Shortcuts.TryGetValue(action, out var value))
		{
			return false;
		}
		if (value == Keys.None)
		{
			return false;
		}
		return e.KeyData == value;
	}

	/// <summary>
	/// Applies the current search query and category filter to the installed mods list,
	/// then rebuilds the list box via <see cref="RebuildInstalledListBox"/>.
	/// </summary>
	private void FilterInstalledMods()
	{
		string query = txtSearchInstalled.Text.Trim().ToLower();
		string category = cmbCategoryFilter.SelectedItem?.ToString() ?? "All Categories";
		listInstalled.BeginUpdate();
		listInstalled.Items.Clear();
		List<StardewMod> list = _allInstalledMods.Where((StardewMod m) => (m.Name.ToLower().Contains(query) || m.Author.ToLower().Contains(query)) && (category == "All Categories" || m.Category == category)).ToList();
		foreach (StardewMod item in list)
		{
			listInstalled.Items.Add(item);
		}
		listInstalled.EndUpdate();
		if (!string.IsNullOrEmpty(query) || category != "All Categories")
		{
			Speak($"{list.Count} mods found.");
		}
	}

	private async void List_Enter(object? sender, EventArgs e)
	{
		if (sender is not ListBox listBox) return;

		if (listBox.Items.Count == 0)
		{
			if (!_isLoading) Speak("List is empty.");
			return;
		}
		if (listBox.SelectedIndex == -1)
		{
			// Selecting an item raises SelectedIndexChanged, which announces the position itself.
			listBox.SelectedIndex = 0;
			return;
		}
		// An item is already selected, so focusing did not raise SelectedIndexChanged. Announce the
		// position here, after a short delay so the screen reader speaks the list name and selected
		// item first — putting "X of Y" at the end, matching the arrow-key path (List_SelectedIndexChanged).
		await Task.Delay(100);
		if (!listBox.Focused) return;
		Speak($"{listBox.SelectedIndex + 1} of {listBox.Items.Count}");
	}

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern IntPtr GetFocus();

	/// <summary>
	/// Re-announces the focused list's position by invoking <see cref="List_Enter"/> on it. Used
	/// when focus is restored by a path that does not raise GotFocus — notably exiting the
	/// MenuStrip's Alt menu mode, which keeps the underlying control's focus and so never re-fires
	/// the event. Generic: it acts on whichever ListBox currently holds focus, or does nothing.
	/// </summary>
	private void AnnounceFocusedList()
	{
		if (Control.FromHandle(GetFocus()) is ListBox list)
		{
			List_Enter(list, EventArgs.Empty);
		}
	}

	/// <summary>Updates the bottom status-bar label with <paramref name="text"/>.</summary>
	private void SetStatus(string text, bool speak = true)
	{
		if (base.InvokeRequired)
		{
			Invoke(delegate
			{
				SetStatus(text, speak);
			});
		}
		else
		{
			string gameName = _settings.ActiveGame switch
			{
				"SkyrimSE" => "Skyrim Special Edition",
				"Fallout4" => "Fallout 4",
				_ => "Stardew Valley"
			};
			string text2 = $"{gameName} Kinetix Mod Manager - Status: {text}";
			Text = text2;
			if (speak)
			{
				Speak(text);
			}
		}
	}

	private async void List_SelectedIndexChanged(object? sender, EventArgs e)
	{
		if (!(sender is ListBox { SelectedItem: not null } list) || _isLoading || !list.Focused)
		{
			return;
		}
		await Task.Delay(100);
		if (!list.Focused) return;
		string text = $"{list.SelectedIndex + 1} of {list.Items.Count}";
		if (list.Name == "listLog")
		{
			string lineText = list.SelectedItem.ToString() ?? "";
			string suggestedFix = LogAnalyzer.GetSuggestedFix(lineText);
			if (!string.IsNullOrEmpty(suggestedFix))
			{
				text = text + ". Suggested Fix: " + suggestedFix;
			}
			// Let a screen-reader user know this line is actionable (e.g. a SMAPI update notice).
			if (!string.IsNullOrEmpty(LogAnalyzer.ExtractUrl(lineText)))
			{
				text = text + ". Press Enter to open mod page.";
			}
		}
		Speak(text);
	}

	/// <summary>
	/// Speaks a short description of the currently active tab's purpose and available keyboard shortcuts.
	/// </summary>
	private void ShowContextHelp()
	{
		string text = "";
		switch (mainTabs.SelectedIndex)
		{
		case (int)AppTab.Installed:
			text = $"Installed Mods: Space to Toggle. Delete to remove mod. {GetShortcutString("Search")} to Search. {GetShortcutString("ChangeCategory")} to change Category. {GetShortcutString("BatchCategory")} to batch toggle Category. {GetShortcutString("OpenModPage")} for Nexus. {GetShortcutString("ShowDependencies")} for dependencies. {GetShortcutString("QuickFix")} to Quick-Fix. {GetShortcutString("ManualID")} for Nexus ID. {GetShortcutString("InstallZip")} to install zip. {GetShortcutString("SaveProfile")} to save as profile. {GetShortcutString("ReadDescription")} to read description. {GetShortcutString("OpenConfig")} to edit the mod's config file. {GetShortcutString("OpenManifest")} to edit the mod's manifest file. {GetShortcutString("LaunchGame")} to launch game.";
			break;
		case (int)AppTab.Updates:
			text = $"Updates: Enter for Nexus page. Delete to Ignore this update. {GetShortcutString("UpdateAll")} to Update All (Premium). {GetShortcutString("ReadDescription")} to read description. {GetShortcutString("LaunchGame")} to launch game.";
			break;
		case (int)AppTab.Backups:
			text = $"Backups: Enter to restore. Delete to remove zip. {GetShortcutString("PruneBackups")} to prune old backups. {GetShortcutString("OpenBackups")} to open backups folder.";
			break;
		case (int)AppTab.Discovery:
			text = "Discovery: Enter for Nexus page. " + GetShortcutString("ReadDescription") + " to read summary. Tab to search.";
			break;
		case (int)AppTab.Wiki:
			text = "Stardew Wiki: Type in Search box and press Enter. Or select a Category. In Results: Enter to open page or sub-category. Backspace to go back up. Tab into the web view to read content with screen reader commands (H for headings, T for tables).";
			break;
		case (int)AppTab.Walkthroughs:
			string activeGameWalkthroughTitle = _settings.ActiveGame switch
			{
				"SkyrimSE" => "Skyrim",
				"Fallout4" => "Fallout 4",
				_ => "Stardew Valley"
			};
			text = $"{activeGameWalkthroughTitle} Walkthroughs: Select a guide from the list using Up/Down arrow keys. Press F6 or Tab to move to the Web View to read the guide with screen reader commands (H for headings, T for tables).";
			break;
		case (int)AppTab.Profiles:
			text = "Profiles: Enter to apply profile. Delete to remove profile.";
			break;
		case (int)AppTab.SmapiLog:
			text = "SMAPI Log: Filter dropdown includes Errors Only, Errors and Warnings, Full Log, and Links Only. Search box available. Enter on a line with a link opens the mod page; Enter on a search result jumps to it in full view. " + GetShortcutString("QuickFix") + " to diagnose and fix the selected line. Control C to copy selected lines to the clipboard. " + GetShortcutString("Login") + " to upload to SMAPI.io. " + GetShortcutString("OpenLogFile") + " to open raw file.";
			break;
		}
		if (!string.IsNullOrEmpty(text))
		{
			Speak(text);
		}
	}

	/// <summary>
	/// Queries the Nexus Mods GraphQL API for the current search text and populates
	/// <c>listDiscovery</c>. Pass <paramref name="loadMore"/> as <c>true</c> to append the
	/// next page of results instead of starting fresh.
	/// </summary>
	private async Task RunDiscovery(bool loadMore = false)
	{
		if (string.IsNullOrEmpty(_settings.ApiKey))
		{
			Speak("Please login first.");
			return;
		}
		if (!loadMore) { _currentDiscoveryPage = 1; listDiscovery.Items.Clear(); }
		else _currentDiscoveryPage++;

		string searchType = cmbDiscoveryType.SelectedItem?.ToString() ?? "Search";
		string searchTerm = txtSearch.Text.Trim();
		Speak((loadMore ? "Loading more " : "Starting mod ") + searchType + "...");
		SetStatus((loadMore ? "Loading more " : "Running ") + searchType + "...");
		try
		{
			const int pageSize = 20;
			var (results, total) = await _nexusService.SearchModsAsync(searchType, searchTerm, _currentDiscoveryPage, pageSize);
			int offset = (_currentDiscoveryPage - 1) * pageSize;
			foreach (var mod in results) listDiscovery.Items.Add(mod);
			btnLoadMoreDiscovery.Visible = (offset + results.Count) < total;
			if (results.Count > 0)
			{
				Speak((loadMore ? "Added " : "Found ") + results.Count + " mods.");
				if (!loadMore) listDiscovery.SelectedIndex = 0;
			}
			else
			{
				Speak(loadMore ? "No more mods found." : "No mods found.");
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("Discovery Error: " + ex.Message);
		}
	}

	/// <summary>Maps a Nexus Mods numeric category ID to a human-readable category name.</summary>
	private string MapNexusCategory(int id)
	{
		return id switch
		{
			1 => "Expansion", 
			2 => "NPC", 
			3 => "Portrait", 
			4 => "Map", 
			5 => "Crafting", 
			6 => "Gameplay", 
			7 => "Visual", 
			8 => "Audio", 
			_ => "General", 
		};
	}

	/// <summary>
	/// Sends <paramref name="text"/> to the active screen reader via Tolk.
	/// Auto-reloads Tolk if it has been externally unloaded since the last call.
	/// </summary>
	private void Speak(string text)
	{
		// If the screen reader was unloaded externally (e.g., NVDA restarted),
		// attempt a silent reload before speaking so users don't lose announcements.
		if (!Tolk.IsLoaded())
		{
			try { Tolk.Load(); Tolk.TrySAPI(trySAPI: true); } catch { }
		}
		if (Tolk.IsLoaded())
		{
			Tolk.Output(text);
		}
	}

	/// <summary>
	/// Plays a periodic loading sound while update checks are in flight (<c>_isLoading</c> is true).
	/// Exits and plays the completion sound once all pending checks finish.
	/// </summary>
	private async Task RunLoadingLoop()
	{
		while (_isLoading)
		{
			_soundEngine.Play("loading_indicator");
			await Task.Delay(1500);
		}
	}
}
