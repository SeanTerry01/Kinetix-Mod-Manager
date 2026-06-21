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
			Speak(Loc.T("discovery.modsFound", list.Count));
		}
	}

	private async void List_Enter(object? sender, EventArgs e)
	{
		if (sender is not ListBox listBox) return;

		if (listBox.Items.Count == 0)
		{
			AnnounceListEmpty(listBox);
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
		// The Discovery "Load more" row carries no position; its row text is read by the screen reader.
		if (listBox.SelectedItem is DiscoveryLoadMoreRow) return;
		int itemCount = listBox.Name == "listDiscovery" ? DiscoveryResultCount() : listBox.Items.Count;
		Speak(Loc.T("common.position", listBox.SelectedIndex + 1, itemCount));
	}

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern IntPtr GetFocus();

	private ListBox? _lastEmptyList;
	private long _lastEmptyTicks;

	/// <summary>
	/// Speaks "List is empty" for a focused, empty list. Two things make this reliable: it waits a moment first so
	/// the screen reader reads the list's <em>name</em> before we add the empty status (otherwise an immediate Tolk
	/// call wins the race and you hear "List is empty. Available updates list." in the wrong order); and it
	/// de-duplicates, because a focus change and a window activation can both target the same empty list at once.
	/// </summary>
	private async void AnnounceListEmpty(ListBox list)
	{
		if (_isLoading) return;
		await Task.Delay(120);
		if (list.IsDisposed || !list.Focused || list.Items.Count != 0 || _isLoading) return;

		long now = Environment.TickCount64;
		if (ReferenceEquals(_lastEmptyList, list) && now - _lastEmptyTicks < 1000) return;
		_lastEmptyList = list;
		_lastEmptyTicks = now;
		Speak(Loc.T("common.listEmpty"));
	}

	/// <summary>
	/// When the manager regains the foreground (e.g. Alt+Tab back in), the child control gets focus again without
	/// reliably re-firing GotFocus, so an empty list would never re-announce that it is empty. If focus has landed
	/// on an empty list, announce it here. Non-empty lists are left to the screen reader, which reads them itself.
	/// </summary>
	private async void Form1_Activated(object? sender, EventArgs e)
	{
		await Task.Delay(50); // let Windows restore focus to the child control first
		if (Control.FromHandle(GetFocus()) is ListBox { Items.Count: 0 } list && list.Focused)
			AnnounceListEmpty(list);
	}

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
	/// <summary>The status the title bar should rest at between operations: the Nexus connection when logged in,
	/// otherwise a neutral "Ready". Only this should linger in the title — transient operation statuses
	/// (installing, rebuilding, downloading, …) reset to it when they finish so the title never shows a stale
	/// message long after the work is done.</summary>
	private string RestingStatus() =>
		!string.IsNullOrEmpty(_nexusService.NexusUser) && _nexusService.NexusUser != "Unknown User"
			? Loc.T("status.connectedAs", _nexusService.NexusUser)
			: Loc.T("status.ready");

	/// <summary>Returns the title bar to the resting status after a transient operation, without speaking it.</summary>
	private void ResetStatus() => SetStatus(RestingStatus(), speak: false);

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
			string text2 = Loc.T("status.titleFormat", GameDisplayName(), text);
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
		// The Discovery list's inline "Load more" row is an action, not a numbered result: the screen
		// reader already reads its row text on focus, so add no position announcement.
		if (list.SelectedItem is DiscoveryLoadMoreRow) return;
		// Exclude that row from the Discovery count so positions read "20 of 20", not "20 of 21".
		int itemCount = list.Name == "listDiscovery" ? DiscoveryResultCount() : list.Items.Count;
		string text = Loc.T("common.position", list.SelectedIndex + 1, itemCount);
		if (list.Name == "listLog")
		{
			string lineText = list.SelectedItem.ToString() ?? "";
			string suggestedFix = LogAnalyzer.GetSuggestedFix(lineText);
			if (!string.IsNullOrEmpty(suggestedFix))
			{
				text = text + Loc.T("helpers.suggestedFixSuffix", suggestedFix);
			}
			// Let a screen-reader user know this line is actionable (e.g. a SMAPI update notice),
			// and whether Enter opens a page directly or offers a choice of several links.
			int linkCount = LogAnalyzer.ExtractUrls(lineText).Count;
			if (linkCount == 1)
			{
				text = text + Loc.T("helpers.pressEnterOpenPage");
			}
			else if (linkCount > 1)
			{
				text = text + Loc.T("helpers.pressEnterChoose", linkCount);
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
		switch (CurrentTab())
		{
		case AppTab.Installed:
			text = Loc.T("help.installed", GetShortcutString("Search"), GetShortcutString("ChangeCategory"), GetShortcutString("BatchCategory"), GetShortcutString("OpenModPage"), GetShortcutString("ShowDependencies"), GetShortcutString("QuickFix"), GetShortcutString("ManualID"), GetShortcutString("InstallZip"), GetShortcutString("SaveProfile"), GetShortcutString("ReadDescription"), GetShortcutString("OpenConfig"), GetShortcutString("OpenManifest"), GetShortcutString("LaunchGame"));
			break;
		case AppTab.Updates:
			text = Loc.T("help.updates", GetShortcutString("UpdateAll"), GetShortcutString("ReadDescription"), GetShortcutString("LaunchGame"));
			break;
		case AppTab.Backups:
			text = Loc.T("help.backups", GetShortcutString("PruneBackups"), GetShortcutString("OpenBackups"));
			break;
		case AppTab.Discovery:
			text = Loc.T("help.discovery", GetShortcutString("ReadDescription"));
			break;
		case AppTab.Wiki:
			text = Loc.T("help.wiki");
			break;
		case AppTab.Walkthroughs:
			string activeGameWalkthroughTitle = _settings.ActiveGame switch
			{
				"SkyrimSE" => "Skyrim",
				"Fallout4" => "Fallout 4",
				_ => "Stardew Valley"
			};
			text = Loc.T("help.walkthroughs", activeGameWalkthroughTitle);
			break;
		case AppTab.Profiles:
			text = Loc.T("help.profiles");
			break;
		case AppTab.ModPriority:
			text = Loc.T("help.modPriority");
			break;
		case AppTab.PluginOrder:
			text = Loc.T("help.pluginOrder", GetShortcutString("AutoSort"));
			break;
		case AppTab.Creations:
			text = Loc.T("help.creations");
			break;
		case AppTab.GameLog:
			text = Loc.T("help.gameLog", GetShortcutString("RefreshLog"), GetShortcutString("OpenLogFile"));
			break;
		case AppTab.SmapiLog:
			text = Loc.T("help.smapiLog", GetShortcutString("QuickFix"), GetShortcutString("RefreshLog"), GetShortcutString("Login"), GetShortcutString("OpenLogFile"));
			break;
		}
		if (!string.IsNullOrEmpty(text))
		{
			Speak(text);
		}
	}

	/// <summary>
	/// Sentinel placeholder for the inline "Load more" row pinned to the bottom of the Discovery
	/// results list. It is never a real search result: it is excluded from the spoken "X of Y"
	/// position count, and pressing Enter on it loads the next page rather than opening a mod page.
	/// Its <see cref="ToString"/> is what the screen reader reads when the row is focused.
	/// </summary>
	private sealed class DiscoveryLoadMoreRow
	{
		public override string ToString() => Loc.T("discovery.loadMoreRow");
	}

	/// <summary>True when the Discovery list currently ends with the inline "Load more" row.</summary>
	private bool DiscoveryHasLoadMoreRow() =>
		listDiscovery.Items.Count > 0 &&
		listDiscovery.Items[listDiscovery.Items.Count - 1] is DiscoveryLoadMoreRow;

	/// <summary>Removes the inline "Load more" row if present (it is always the last item).</summary>
	private void RemoveDiscoveryLoadMoreRow()
	{
		if (DiscoveryHasLoadMoreRow())
			listDiscovery.Items.RemoveAt(listDiscovery.Items.Count - 1);
	}

	/// <summary>
	/// The number of real search results in the Discovery list, i.e. the item count minus the inline
	/// "Load more" row if it is present. Used so the row is not counted in the spoken "X of Y".
	/// </summary>
	private int DiscoveryResultCount() =>
		listDiscovery.Items.Count - (DiscoveryHasLoadMoreRow() ? 1 : 0);

	/// <summary>
	/// Queries the Nexus Mods GraphQL API for the current search text and populates
	/// <c>listDiscovery</c>. Pass <paramref name="loadMore"/> as <c>true</c> to append the
	/// next page of results instead of starting fresh.
	/// </summary>
	private async Task RunDiscovery(bool loadMore = false)
	{
		if (string.IsNullOrEmpty(_settings.ApiKey))
		{
			Speak(Loc.T("discovery.loginFirst"));
			return;
		}
		if (!loadMore)
		{
			_currentDiscoveryPage = 1;
			listDiscovery.Items.Clear();
			// Lock the page size for this whole search series; honour the tab's session-only
			// selector, falling back to the persisted default.
			_currentDiscoveryPageSize = cmbDiscoveryPageSize.SelectedItem is int n ? n : _settings.DiscoverySearchPageSize;
		}
		else _currentDiscoveryPage++;

		string searchType = cmbDiscoveryType.SelectedItem?.ToString() ?? "Search";
		string searchTerm = txtSearch.Text.Trim();
		string language = (cmbDiscoveryLanguage?.SelectedItem as LanguageOption)?.Name ?? _settings.DiscoveryLanguage;

		// Record real text searches (not "load more" pages or the browse modes) to the active game's history.
		if (!loadMore && searchType == "Search" && searchTerm.Length > 0 && _settings.SaveSearchHistory)
			SearchHistoryStore.Add(_settings.ActiveGame, searchTerm);
		Speak(loadMore ? Loc.T("discovery.loadingMore", searchType) : Loc.T("discovery.startingSearch", searchType));
		SetStatus(loadMore ? Loc.T("discovery.loadingMore", searchType) : Loc.T("discovery.statusRunning", searchType));
		try
		{
			int pageSize = _currentDiscoveryPageSize;
			var (results, total) = await _nexusService.SearchModsAsync(searchType, searchTerm, _currentDiscoveryPage, pageSize, language);
			int offset = (_currentDiscoveryPage - 1) * pageSize;

			// Drop the old inline "Load more" row (always last) before appending; firstNewIndex is then
			// the slot of the first freshly loaded result, which we select so focus lands on it.
			RemoveDiscoveryLoadMoreRow();
			int firstNewIndex = listDiscovery.Items.Count;
			foreach (var mod in results) listDiscovery.Items.Add(mod);

			// Re-add the inline "Load more" row only while this page returned results AND more remain.
			// It is excluded from the spoken "X of Y" count and, on Enter, triggers RunDiscovery(loadMore: true).
			if (results.Count > 0 && (offset + results.Count) < total)
				listDiscovery.Items.Add(new DiscoveryLoadMoreRow());

			if (results.Count > 0)
			{
				Speak(loadMore ? Loc.T("discovery.added", results.Count) : Loc.T("discovery.found", results.Count));
				// Fresh search lands on the first result; Load more lands on the first new result.
				listDiscovery.SelectedIndex = loadMore ? firstNewIndex : 0;
			}
			else
			{
				Speak(loadMore ? Loc.T("discovery.noMore") : Loc.T("discovery.none"));
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(Loc.T("discovery.errorBox", ex.Message));
		}
		finally
		{
			// "Running search…" / "Loading more…" are transient; return the title to the resting status
			// (Nexus connection or "Ready") so it never sits on a stale "Loading more…" forever.
			ResetStatus();
		}
	}

	/// <summary>
	/// Fills the Discovery "Language" dropdown with the languages that actually have mods for the active game
	/// (most common first, with counts), and selects the user's saved language preference. Falls back to the
	/// starter list if the facet query returns nothing.
	/// </summary>
	private async Task PopulateDiscoveryLanguagesAsync()
	{
		if (cmbDiscoveryLanguage == null) return;
		var langs = await _nexusService.GetModLanguagesAsync();
		if (langs.Count == 0) return; // network failure etc. — keep whatever is already there

		string desired = _settings.DiscoveryLanguage; // "" = Any language
		_suppressDiscoveryLanguageEvent = true;
		cmbDiscoveryLanguage.Items.Clear();
		cmbDiscoveryLanguage.Items.Add(new LanguageOption { Name = "" }); // Any language
		int selectIndex = 0;
		foreach (var (name, count) in langs)
		{
			cmbDiscoveryLanguage.Items.Add(new LanguageOption { Name = name, Count = count });
			if (string.Equals(name, desired, StringComparison.OrdinalIgnoreCase))
				selectIndex = cmbDiscoveryLanguage.Items.Count - 1;
		}
		cmbDiscoveryLanguage.SelectedIndex = selectIndex;
		_suppressDiscoveryLanguageEvent = false;
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
	private void Speak(string text) => Speak(text, interrupt: false);

	/// <summary>
	/// Sends <paramref name="text"/> to the active screen reader via Tolk. When <paramref name="interrupt"/>
	/// is true, it cuts off any in-progress/queued speech first — used to take full control of the wording
	/// and ordering of an announcement (e.g. so a list's title is spoken before its first item).
	/// </summary>
	private void Speak(string text, bool interrupt)
	{
		// If the screen reader was unloaded externally (e.g., NVDA restarted),
		// attempt a silent reload before speaking so users don't lose announcements.
		if (!Tolk.IsLoaded())
		{
			try { Tolk.Load(); Tolk.TrySAPI(trySAPI: true); } catch { }
		}
		if (Tolk.IsLoaded())
		{
			Tolk.Output(text, interrupt);
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
