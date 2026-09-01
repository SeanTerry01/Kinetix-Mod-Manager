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

/// <summary>Installed-mod list, backups list, and Nexus connection/linking for Form1.</summary>
public partial class Form1
{
	/// <summary>
	/// Convenience wrapper that refreshes the mod list, backups list, profiles list,
	/// and SMAPI log in one call.
	/// </summary>
	private void RefreshAllData(bool checkUpdates)
	{
		if (_settings.ActiveGame == "None") return;
		// Catch a Steam/GOG game update before the user tries to launch: warns once if the exe version changed
		// since we last loaded this game. Harmless on Stardew and cheap enough to run on every refresh.
		CheckGameUpdateGuardian();
		Fire(RefreshModList(checkUpdates), "RefreshModList");
		RefreshBackupsList();
		RefreshProfilesList();
		RefreshSmapiLog();
		RefreshGameLog();
		// Startup comes through here rather than SwitchActiveGame, so without this the Discovery category list
		// would stay empty until the user switched games. Guarded to fetch once per game, so the repeat calls
		// from every other refresh cost nothing.
		Fire(PopulateDiscoveryCategoriesAsync(), "PopulateDiscoveryCategoriesAsync");
	}

	/// <summary>
	/// Scans the Mods folder, parses every manifest.json, resolves dependencies, applies the
	/// category map, and rebuilds <c>listInstalled</c>. Optionally fires update checks against
	/// the Nexus API when <paramref name="checkUpdates"/> is <c>true</c>.
	/// </summary>
	private async Task RefreshModList(bool checkUpdates)
	{
		if (_settings.ActiveGame == "None") return;
		if (string.IsNullOrEmpty(_settings.ApiKey))
		{
			if (!_isSettingsOpen)
			{
				SetStatus(Loc.T("status.authRequired"));
				_soundEngine.Play("disconnect");
				ShowSettings();
			}
			return;
		}
		// Acquire the single-batch update-check guard. See _updateCheckRunning: it is held across the async
		// checks and released either by their completion (CheckForUpdates) or by the finally below when none
		// get launched.
		//
		// Losing the race only cancels the UPDATE CHECK, never the rescan. That distinction matters more than
		// it looks: this used to `return` outright, so a game switch that happened while a check was running
		// silently kept the previous game's mods on screen — the title said Moonlight Peaks while the list held
		// 147 Stardew mods. Rescanning the folder has nothing to do with checking Nexus for versions, and the
		// caller asked for both.
		bool doUpdateChecks = checkUpdates;
		if (checkUpdates && Interlocked.CompareExchange(ref _updateCheckRunning, 1, 0) != 0)
		{
			Speak(Loc.T("modlist.updateInProgress"));
			doUpdateChecks = false;
		}

		// Claim this pass. Two refreshes can overlap — a slow one started before a game switch and the switch's
		// own — and the older one would otherwise finish last and overwrite the list with the previous game's
		// mods. Only the newest pass is allowed to publish its results.
		int generation = Interlocked.Increment(ref _refreshGeneration);
		string refreshingGame = _settings.ActiveGame;
		bool Superseded() => Volatile.Read(ref _refreshGeneration) != generation || _settings.ActiveGame != refreshingGame;

		bool launchedChecks = false;
		Interlocked.Increment(ref _refreshInFlight);
		try
		{
		// Nexus's API is stateless, so once the key is validated for this session there's nothing to reconnect —
		// re-checking on every refresh just cost a round-trip and a "connecting… connected" chime each time.
		// Validate only the first time (or after the key changes); afterwards reflect the status quietly.
		if (!_nexusService.IsValidated)
		{
			SetStatus(Loc.T("status.connecting"));
			if (!(await ValidateNexusConnection()))
			{
				SetStatus(Loc.T("status.authFailed"));
				_soundEngine.Play("error");
				return;
			}
			SetStatus(Loc.T("status.connectedAs", _nexusService.NexusUser));
			_soundEngine.Play("connect");
		}
		else
		{
			SetStatus(Loc.T("status.connectedAs", _nexusService.NexusUser), speak: false);
		}
		// Which mod the user was on, remembered before the list is emptied and handed back to the rebuild at the
		// end. RebuildInstalledListBox restores the selection from the list's own current item, and by the time it
		// runs there is no current item left to read — so without carrying it across here, every refresh dropped
		// the user on whatever ended up first. Editing a mod's config or manifest made that obvious, because the
		// save refreshes and the user is returned to a list they were in the middle of working through.
		string? selectedBefore = null;
		Invoke(delegate
		{
			selectedBefore = (listInstalled.SelectedItem as StardewMod)?.UniqueId;
			listInstalled.BeginUpdate();
			if (doUpdateChecks)
			{
				listUpdates.BeginUpdate();
			}
			listInstalled.Items.Clear();
			if (doUpdateChecks)
			{
				listUpdates.Items.Clear();
			}
		});
		_allInstalledMods.Clear();
		if (!Directory.Exists(_settings.CurrentModsPath))
		{
			Invoke(delegate
			{
				listInstalled.EndUpdate();
				if (doUpdateChecks)
				{
					listUpdates.EndUpdate();
				}
			});
			SpeakBox(Loc.T("modlist.modsPathInvalid"));
			return;
		}
		string idMapPath = Path.Combine(AppSettings.AppDataFolder, "mod_id_map.json");
		string legacyIdMapPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mod_id_map.json");
		if (File.Exists(legacyIdMapPath) && !File.Exists(idMapPath))
		{
			try
			{
				File.Copy(legacyIdMapPath, idMapPath, overwrite: true);
			}
			catch (Exception ex) { DiagnosticLog.WriteException("ModIdMap", "copying the old Nexus id map into place", ex); }
		}
		JObject nexusIdMap = (File.Exists(idMapPath) ? JObject.Parse(File.ReadAllText(idMapPath)) : new JObject()) ?? new JObject();
		List<StardewMod> scanned = ModFileSystem.ScanMods(_settings.CurrentModsPath, nexusIdMap, _settings, refreshingGame, LogError);

		// The scan reads the disk and can take a while on a large mod folder. If the user switched games while it
		// ran, these are the wrong game's mods and the newer pass is already on its way — publishing them would
		// put the game they just left back on screen. Balance the BeginUpdate above before standing down, or the
		// list stays suspended and the newer pass's items never paint.
		if (Superseded())
		{
			Invoke(delegate
			{
				listInstalled.EndUpdate();
				if (doUpdateChecks) listUpdates.EndUpdate();
			});
			return;
		}

		_allInstalledMods = scanned;
		ModHealth.ResolveDependencies(_allInstalledMods, IsNewerVersion);
		// Reconcile Skyrim/Fallout 4 asset deployment to the current enabled set and priority order, and
		// refresh the conflict scan. Cheap when nothing changed (only files whose winner changed relink),
		// so it is safe to run on every list rebuild and keeps the manifest and conflict counts current.
		if (IsBethesdaGame)
		{
			SyncBethesdaDeployment();
			SyncBethesdaPlugins();
		}
		HashSet<string> hashSet = new HashSet<string>(_allInstalledMods.Select(m => m.Category));
		Invoke(delegate
		{
			// Refilling this list changes its selection, which would otherwise rebuild the mod list and speak
			// the focused mod — once on the Clear and again on the restored selection — before the rebuild
			// below does it properly. The list is rebuilt once, immediately after this.
			_suppressInstalledFilterEvent = true;
			try
			{
				cmbCategoryFilter.BeginUpdate();
				string text2 = cmbCategoryFilter.SelectedItem?.ToString() ?? "All Categories";
				cmbCategoryFilter.Items.Clear();
				cmbCategoryFilter.Items.Add("All Categories");
				foreach (string item2 in hashSet.OrderBy((string c) => c))
				{
					cmbCategoryFilter.Items.Add(item2);
				}
				if (cmbCategoryFilter.Items.Contains(text2))
				{
					cmbCategoryFilter.SelectedItem = text2;
				}
				else
				{
					cmbCategoryFilter.SelectedIndex = 0;
				}
				cmbCategoryFilter.EndUpdate();
			}
			finally
			{
				_suppressInstalledFilterEvent = false;
			}
		});
		Invoke(delegate
		{
			// A mod that has since been deleted, or a game that has been switched, simply is not there any more —
			// RebuildInstalledListBox falls back to the first row, which is the right answer for both.
			RebuildInstalledListBox(selectedBefore, announceRestoredRow: true);
			RefreshModPriorityList();
			RefreshPluginOrderList();
			RefreshCreationsList();
			// The Discovery results were marked against the installed list as it was when they were fetched, so a
			// mod that has arrived since then is still sitting there inviting you to fetch it again. Re-marked
			// here, where the scan has just finished and _allInstalledMods is current.
			RefreshDiscoveryInstalledMarks();
			listInstalled.EndUpdate();

			// Say so when the sync above put Creations (or other Data-folder plugins) back after the game
			// deactivated them, so the repair isn't silent — that reset is exactly what users notice and
			// report, and hearing it confirmed is the difference between "fixed" and "did it happen again?".
			if (_restoredExternalPlugins > 0)
			{
				Speak(Loc.T(_restoredExternalPlugins == 1 ? "loadorder.restoredOne" : "loadorder.restoredMany", _restoredExternalPlugins));
				_restoredExternalPlugins = 0;
			}

			int oldSelectedIndex = listUpdates.SelectedIndex;
			object? oldSelectedItem = listUpdates.SelectedItem;

			listUpdates.BeginUpdate();
			for (int i = listUpdates.Items.Count - 1; i >= 0; i--)
			{
				if (listUpdates.Items[i] is StardewMod updateMod)
				{
					var installedMod = _allInstalledMods.FirstOrDefault(m =>
						(!string.IsNullOrEmpty(updateMod.UniqueId) && m.UniqueId == updateMod.UniqueId) ||
						(!string.IsNullOrEmpty(updateMod.Name) && m.Name.Equals(updateMod.Name, StringComparison.OrdinalIgnoreCase))
					);

					if (installedMod == null)
					{
						listUpdates.Items.RemoveAt(i);
					}
					else if (!HasPendingUpdate(installedMod, updateMod.LatestVersion))
					{
						listUpdates.Items.RemoveAt(i);
					}
				}
			}

			if (listUpdates.Items.Count > 0)
			{
				if (oldSelectedItem != null && listUpdates.Items.Contains(oldSelectedItem))
				{
					listUpdates.SelectedItem = oldSelectedItem;
				}
				else
				{
					listUpdates.SelectedIndex = Math.Min(Math.Max(0, oldSelectedIndex), listUpdates.Items.Count - 1);
				}

				// Position only — the screen reader reads the re-selected row itself. See RebuildInstalledListBox.
				if (listUpdates.Focused && listUpdates.SelectedItem != null)
				{
					Speak(Loc.T("common.position", listUpdates.SelectedIndex + 1, listUpdates.Items.Count));
				}
			}
			else if (listUpdates.Focused && oldSelectedIndex != -1)
			{
				Speak(Loc.T("common.listEmpty"));
			}
			listUpdates.EndUpdate();

			if (doUpdateChecks)
			{
				listUpdates.BeginUpdate();
			}

			// Startup only, and last: once the list is actually on screen, put the user on the first tab and say
			// which one it is. Before this, a session opened, announced it was connected, and then went quiet with
			// focus wherever Windows happened to leave it — and with the window's caption deliberately swallowed
			// there was nothing left to say where you had landed. Consumed here rather than in the Shown handler
			// because the refresh is asynchronous, and this is the first point at which "Connected as …" has
			// already been said.
			if (_landOnFirstTabWhenReady)
			{
				_landOnFirstTabWhenReady = false;
				LandOnFirstTab();
			}
		});
		if (!doUpdateChecks)
		{
			return;
		}
		List<IGrouping<string, StardewMod>> list = (from m in _allInstalledMods
			where !string.IsNullOrEmpty(m.NexusID) || !string.IsNullOrEmpty(m.GitHubRepo)
			group m by (!string.IsNullOrEmpty(m.NexusID) ? "Nexus:" + m.NexusID : "GitHub:" + m.GitHubRepo)).ToList();
		// Mods with neither a Nexus nor a GitHub link are excluded from the grouping above; remember them so the
		// completion announcement can say they were skipped (a manually-placed mod would otherwise look "up to
		// date" when it was never checked). For Stardew Valley most of these are still covered by the smapi.io
		// batch below, which resolves mods by UniqueID — only what it doesn't recognise is really unchecked, so
		// the final count is worked out at the end of the run (see UncheckedModCount).
		_updateUnlinkedMods = _allInstalledMods.Where(m => !m.IsGroup
			&& string.IsNullOrEmpty(m.NexusID) && string.IsNullOrEmpty(m.GitHubRepo)).ToList();
		_smapiCheckedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		// Stardew Valley additionally runs one smapi.io batch check (counted as a unit), which catches
		// mods whose manifest update key is missing or broken — the manifest-only Nexus grouping below
		// can't see those. Skyrim/Fallout 4 use only the Nexus group checks.
		bool runSmapi = GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley);
		int unitCount = list.Count + (runSmapi ? 1 : 0);
		if (unitCount == 0)
		{
			_isLoading = false;
			_soundEngine.Play("load_complete");
			// When there are unlinked mods present, point the user at Auto-match rather than a dead-end message.
			Speak(_updateUnlinkedMods.Count > 0
				? Loc.T("modlist.noUpdateSourcesUnlinked")
				: Loc.T("modlist.noUpdateSources"));
			return;
		}
		_isLoading = true;
		Interlocked.Exchange(ref _activeChecks, unitCount);
		Speak(Loc.T("modlist.checkingUpdates"));
		Fire(RunLoadingLoop(), "RunLoadingLoop");
		launchedChecks = true;
		if (runSmapi)
		{
			var (smapiVer, gameVer) = DetectStardewVersions();
			Fire(CheckUpdatesViaSmapiApi(_allInstalledMods.Where(m => !m.IsGroup).ToList(), smapiVer, gameVer), "CheckUpdatesViaSmapiApi");
		}
		foreach (IGrouping<string, StardewMod> item3 in list)
		{
			Fire(CheckForUpdates(item3.ToList()), "CheckForUpdates");
		}
		}
		finally
		{
			Interlocked.Decrement(ref _refreshInFlight);
			// Release the guard unless the async checks took ownership of it; once launched they
			// release it on completion (see CheckForUpdates). This also frees it on any early
			// return or exception above, so a failed scan can't block all future update checks.
			if (doUpdateChecks && !launchedChecks)
				Interlocked.Exchange(ref _updateCheckRunning, 0);
		}
	}

	/// <summary>
	/// Runs the user's Refresh Everything / Refresh Installed Mods command and says what is happening.
	///
	/// The busy check is the point, and it has to cover the whole operation. <c>_refreshInFlight</c> alone does
	/// not: it is released as soon as the update checks have been *launched*, because they are fire-and-forget
	/// and finish long afterwards. That left a wide window — the entire length of the check, which is the slow
	/// part — in which another Refresh Everything was waved through. It then announced "Refreshing everything",
	/// found the check already running, said so, and rescanned the folder anyway, so a second press produced a
	/// second full rescan and a contradictory pair of announcements.
	///
	/// So a running update check counts as busy too, for Refresh Everything — which is a refresh plus a check,
	/// and cannot honestly claim to have run either while the previous one is still going. Refresh Installed
	/// Mods is deliberately not blocked by it: rescanning the folder has nothing to do with asking Nexus about
	/// versions, and that is the whole difference between the two commands.
	/// </summary>
	private async void RequestManualRefresh(bool everything)
	{
		bool refreshing = Volatile.Read(ref _refreshInFlight) > 0;
		bool checking = everything && Volatile.Read(ref _updateCheckRunning) != 0;
		if (refreshing || checking)
		{
			Speak(Loc.T("modlist.refreshAlreadyRunning"));
			return;
		}

		if (everything)
		{
			// Announced up front: the update check that follows has plenty to say for itself when it finishes.
			Speak(Loc.T("modlist.refreshingAll"));
			RefreshAllData(checkUpdates: true);
			return;
		}

		// A rescan on its own has no completion announcement of its own, so it gets one here — and it is said
		// when the scan has actually finished rather than when it started, which is what "Refreshed" claims.
		Speak(Loc.T("modlist.refreshingInstalled"));
		await RefreshModList(checkUpdates: false);
		Speak(Loc.T("modlist.refreshedInstalled"));
	}

	/// <summary>
	/// What puts two installed mods in the same group in the list.
	///
	/// Normally the top folder they share, which is how one download that unpacks several mods stays one row.
	/// The Witcher 3 has no such structure — every mod is a sibling folder under <c>mods</c> — so a framework
	/// shipping as ten <c>mod_&lt;family&gt;_*</c> folders filled ten rows. Those group by their family instead,
	/// which is the author's own naming rather than a guess about what belongs with what.
	/// </summary>
	private string InstalledGroupKey(StardewMod mod)
	{
		string topFolder = ModEnableState.InstalledGroupFolder(_settings.CurrentModsPath, mod.FolderPath);

		if (GameProfiles.Find(_settings.ActiveGame)?.IsWitcher3 == true)
			return Witcher3Layout.FamilyKey(topFolder) ?? topFolder;

		return topFolder;
	}

	/// <summary>
	/// How many installed mods matched the search, category and status filters the last time the list was built —
	/// including any sitting inside a collapsed group, which is why it is recorded rather than counted from the
	/// rows. Spoken by <see cref="FilterInstalledMods"/> as "N mods found".
	/// </summary>
	private int _installedMatchCount;

	/// <summary>
	/// Re-renders <c>listInstalled</c> from <c>_allInstalledMods</c>, applying the current search
	/// query and category filter, and grouping mods by their top-level sub-folder.
	/// </summary>
	/// <param name="preferUniqueId">
	/// When set, the rebuilt list selects the item with this <c>UniqueId</c> instead of restoring the
	/// previously selected item. Callers that collapse a group from one of its sub-mods pass the parent
	/// group's id here so selection lands on the group directly — without transiently selecting index 0,
	/// which a screen reader would otherwise announce as the first mod.
	/// </param>
	/// <param name="announceRestoredRow">
	/// Whether the restored row should say what it is, rather than only where it sits. True for a rebuild the user
	/// did not ask for — a rescan finishing under them — where nothing else will name the row they land on. False
	/// when a keypress caused the rebuild, because the screen reader announces that row itself and a second
	/// announcement is heard as a stutter.
	/// </param>
	private void RebuildInstalledListBox(string? preferUniqueId = null, bool announceRestoredRow = false)
	{
		string query = txtSearchInstalled.Text.Trim().ToLower();
		string category = cmbCategoryFilter.SelectedItem?.ToString() ?? "All Categories";
		// Status filter by combo index (language-independent): 0 All, 1 Enabled, 2 Disabled, 3 Has Note,
		// 4 Single Mods Only, 5 Mod Groups Only. The last two are applied per folder further down, not here.
		int status = cmbStatusFilter?.SelectedIndex ?? 0;
		bool StatusMatch(StardewMod m) => status switch
		{
			1 => m.IsEnabled,
			2 => !m.IsEnabled,
			3 => !string.IsNullOrWhiteSpace(m.Note),
			_ => true
		};
		// How many mods matched, counted here because this is where the filter is actually applied. It cannot be
		// taken from the rows afterwards: a collapsed group keeps its matches off screen, so counting rows would
		// report only the ones that happen to be visible.
		_installedMatchCount = 0;
		listInstalled.BeginUpdate();
		StardewMod? stardewMod = listInstalled.SelectedItem as StardewMod;
		string? restoreId = preferUniqueId ?? stardewMod?.UniqueId;
		listInstalled.Items.Clear();
		foreach (IGrouping<string, StardewMod> item2 in from g in _allInstalledMods.Where((StardewMod m) => !m.IsGroup).GroupBy(InstalledGroupKey)
			orderby g.Key
			select g)
		{
			List<StardewMod> list = item2.ToList();
			// Status 4 and 5 filter by shape, not by state, so they are decided here: whether something is a mod
			// group is a property of the folder it lives in and not of any one mod, which is all StatusMatch can
			// see. "Single mods only" keeps folders holding exactly one mod; "Mod groups only" keeps the rest.
			if (status == 4 && list.Count > 1) continue;
			if (status == 5 && list.Count == 1) continue;

			List<StardewMod> list2 = list.Where((StardewMod m) => (string.IsNullOrEmpty(query) || m.Name.ToLower().Contains(query) || m.Author.ToLower().Contains(query) || m.Note.ToLower().Contains(query)) && (category == "All Categories" || m.Category == category) && StatusMatch(m)).ToList();
			_installedMatchCount += list2.Count;
			if (list2.Count == 0)
			{
				continue;
			}
			// Grouping survives a filter. What changes is which mods the group is made of: a folder still reads as
			// one group when more than one of its mods matched, and the header counts only those, so "Contains 3
			// mods" is never a claim about rows that are not there. A folder with a single match is not a group at
			// all, the same rule an unfiltered list of one follows.
			if (list2.Count == 1)
			{
				foreach (StardewMod item3 in list2)
				{
					item3.IsSubMod = false;
					item3.IsGroup = false;
					listInstalled.Items.Add(item3);
				}
				continue;
			}
			// Collapsed or expanded exactly as the user last left it, filter or no filter. A collapsed group under
			// a filter reads as "Contains 2 mods", meaning two that matched — the same bargain the unfiltered list
			// already makes, and the reason the count above is not taken from the visible rows.
			bool flag2 = _expandedGroups.Contains(item2.Key);
			StardewMod item = new StardewMod
			{
				IsGroup = true,
				GroupName = item2.Key,
				IsExpanded = flag2,
				UniqueId = "GROUP:" + item2.Key,
				SubMods = list2,
				// The active game's mods folder, not the legacy Stardew one this used — and for a group keyed by
				// a Witcher family there is no folder of that name at all, so the parent is the honest answer.
				FolderPath = _settings.CurrentModsPath
			};
			listInstalled.Items.Add(item);
			if (!flag2)
			{
				continue;
			}
			foreach (StardewMod item4 in list2)
			{
				item4.IsSubMod = true;
				item4.IsGroup = false;
				listInstalled.Items.Add(item4);
			}
		}
		if (restoreId != null)
		{
			for (int num = 0; num < listInstalled.Items.Count; num++)
			{
				if (listInstalled.Items[num] is StardewMod stardewMod2 && stardewMod2.UniqueId == restoreId)
				{
					// Putting the user back after a rescan is a move they did not make, so the row has to name
					// itself — otherwise a refresh finishing under them reads out a position and nothing else.
					// Not so when a keypress caused the rebuild (collapsing a group): the reader announces the row
					// the key landed on, and naming it here as well would say it twice.
					if (announceRestoredRow && num != listInstalled.SelectedIndex) _announceRowNameOnNextChange = true;
					listInstalled.SelectedIndex = num;
					// The list is usually not focused while a rescan runs, so the focus rectangle would otherwise
					// stay on whatever row it was left on and be read out ahead of this one.
					AlignListCaretToSelection(listInstalled);
					break;
				}
			}
		}
		if (listInstalled.SelectedIndex == -1 && listInstalled.Items.Count > 0)
		{
			listInstalled.SelectedIndex = 0;
		}
		// The position is NOT announced here.
		//
		// Re-selecting the row above raises SelectedIndexChanged, and List_SelectedIndexChanged announces the
		// position from there — after a short delay, so the screen reader gets to read the row first, and
		// through SpeakListPosition, which drops a repeat of the same row within a moment.
		//
		// Saying it here as well meant hearing "1 of 12. 1 of 12." and then the mod, because this one skipped
		// both: it spoke immediately, ahead of the reader, and it bypassed the de-duplication that would have
		// swallowed the second. Closing Settings showed it plainly, since that both restores focus to the list
		// and rebuilds it.
		listInstalled.EndUpdate();
	}

	/// <summary>
	/// Puts the selection back on the mod with <paramref name="uniqueId"/> after something moved it — a screen
	/// that rebuilt the list on its way out, most often.
	///
	/// Does nothing when that mod is already selected, so no announcement is provoked for a selection that never
	/// moved, and nothing when it is no longer in the list at all: a mod that has just been deleted, or one a
	/// filter now hides, has no row to go back to and the list's own choice stands.
	/// </summary>
	/// <param name="announce">
	/// Whether the move should name the mod it lands on. True for a move the user will hear nothing else about.
	/// False when the move is a correction being made <em>before</em> focus returns to the list — there the
	/// list's own focus announcement is about to describe where it landed, and saying it here as well would put
	/// two announcements in flight for one arrival, the second cutting off the first.
	/// </param>
	private void ReselectMod(string? uniqueId, bool announce = true)
	{
		if (string.IsNullOrEmpty(uniqueId)) return;
		if ((listInstalled.SelectedItem as StardewMod)?.UniqueId == uniqueId) return;

		for (int i = 0; i < listInstalled.Items.Count; i++)
		{
			if (listInstalled.Items[i] is StardewMod candidate && candidate.UniqueId == uniqueId)
			{
				// Say which mod, not just where it sits: nothing else will, because the user did not move here.
				_announceRowNameOnNextChange = announce;
				// Held across the assignment only, because that is where SelectedIndexChanged is raised from and
				// the handler reads it before its first await.
				_movingListSilently = !announce;
				try
				{
					listInstalled.SelectedIndex = i;
				}
				finally
				{
					_movingListSilently = false;
				}
				AlignListCaretToSelection(listInstalled);
				// SelectedIndexChanged consumes the flag whether or not it goes on to speak, so a silent move
				// cannot leave it set to surprise the user's next arrow press.
				_announceRowNameOnNextChange = false;
				return;
			}
		}
	}

	/// <summary>
	/// The mod the installed list should be sitting on by the time focus comes back to it, set for as long as a
	/// view that might move the selection is open.
	///
	/// <para>
	/// Editing a mod's settings or its manifest can rebuild the list underneath the view — saving rescans the
	/// mods folder — and the rebuild does not always land back on the mod being edited. The callers used to
	/// correct that after the view returned, but by then the overlay has already handed focus back to the list
	/// and everything that speaks on arrival has already spoken, about the wrong mod. Handing the id over here
	/// lets the correction happen inside the close, before focus moves, so there is one arrival and one thing
	/// said about it.
	/// </para>
	///
	/// <para>Consumed by <see cref="RunOverlay"/>'s focus restore; see <see cref="EditModKeepingPlace"/>.</para>
	/// </summary>
	private string? _restoreInstalledSelectionTo;

	/// <summary>
	/// Opens an editor for <paramref name="mod"/> and guarantees the installed list is back on that mod by the
	/// time the user is back in it, however the editor was left and whatever it did to the list on its way out.
	/// </summary>
	private void EditModKeepingPlace(StardewMod mod, Action openEditor)
	{
		string editingId = mod.UniqueId;
		_restoreInstalledSelectionTo = editingId;
		try
		{
			openEditor();
		}
		finally
		{
			// Cleared unconditionally: an editor that reported a problem and never opened a view at all would
			// otherwise leave this set, to be spent on some unrelated screen closing later.
			_restoreInstalledSelectionTo = null;
			// Normally already done inside the close, and then this finds the mod selected and does nothing. It
			// stands for the paths that never went through an overlay.
			ReselectMod(editingId);
		}
	}

	/// <summary>Scans the backups directory and repopulates <c>listBackups</c> with <see cref="BackupItem"/> entries.</summary>
	private void RefreshBackupsList()
	{
		if (_settings.ActiveGame == "None") return;
		if (listBackups == null)
		{
			return;
		}
		int oldIndex = listBackups.SelectedIndex;
		string? oldName = (listBackups.SelectedItem as BackupItem)?.Name;

		listBackups.BeginUpdate();
		listBackups.Items.Clear();
		if (Directory.Exists(backupsPath))
		{
			string[] files = Directory.GetFiles(backupsPath, "*.zip");
			foreach (string text in files)
			{
				string text2 = Path.GetFileNameWithoutExtension(text);
				if (text2.Length > 16 && text2[text2.Length - 16] == '_')
				{
					text2 = text2.Substring(0, text2.Length - 16);
				}
				listBackups.Items.Add(new BackupItem
				{
					Name = text2,
					FullPath = text
				});
			}
		}

		if (listBackups.Items.Count > 0)
		{
			int newIndex = 0;
			if (!string.IsNullOrEmpty(oldName))
			{
				for (int i = 0; i < listBackups.Items.Count; i++)
				{
					if ((listBackups.Items[i] as BackupItem)?.Name == oldName)
					{
						newIndex = i;
						break;
					}
				}
			}
			listBackups.SelectedIndex = Math.Min(Math.Max(newIndex, oldIndex), listBackups.Items.Count - 1);

			// Position only — the screen reader reads the re-selected row itself. See RebuildInstalledListBox.
			if (listBackups.Focused && listBackups.SelectedItem != null)
			{
				Speak(Loc.T("common.position", listBackups.SelectedIndex + 1, listBackups.Items.Count));
			}
		}
		else if (listBackups.Focused && oldIndex != -1)
		{
			Speak(Loc.T("common.listEmpty"));
		}
		listBackups.EndUpdate();
	}

	/// <summary>
	/// Calls the Nexus Mods /users/validate endpoint to confirm the stored API key is valid.
	/// Returns <c>true</c> on success; speaks an error and returns <c>false</c> otherwise.
	/// </summary>
	private async Task<bool> ValidateNexusConnection() =>
		await _nexusService.ValidateAsync();

	/// <summary>Prompts the user to enter or replace their Nexus Mods API key, then refreshes the mod list.</summary>
	private void PromptForApiKey()
	{
		string? text = ShowTextPrompt(Loc.T("login.title"), Loc.T("login.pasteApiKey"), _settings.ApiKey);
		if (text == null) { Speak(Loc.T("common.changesCancelled")); return; }

		text = text.Trim();
		if (text.Length == 0) { Speak(Loc.T("login.keyEmpty")); return; }

		_settings.ApiKey = text;
		_settings.Save();
		Fire(RefreshModList(checkUpdates: true), "RefreshModList");
	}

	/// <summary>
	/// Prompts the user to enter a Nexus Mods ID or GitHub repo for the selected mod
	/// and updates its manifest.
	/// </summary>
	/// <param name="target">
	/// The mod to link. Defaults to the Installed list's selection (the Ctrl+K case); the Update Coverage
	/// report passes the mod its row stands for, so a mod can be linked straight from where the problem
	/// was reported without hunting for it in the list first.
	/// </param>
	/// <summary>The file the manager records a mod's metadata in — the author's own manifest for Stardew Valley,
	/// the manager's sidecar for every other game.</summary>
	private string ManifestPathFor(StardewMod mod) => Path.Combine(mod.FolderPath,
		GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley) ? "manifest.json" : ".manager_manifest.json");

	/// <summary>
	/// Fills in what only Nexus knows about a mod that has just been linked to a mod page — its author, a real
	/// summary, and a proper name for a mod the manager could only name after its folder — and writes that into
	/// the mod's manifest so it survives the next rescan. Safe to call for any linked mod; does nothing when the
	/// page can't be read.
	///
	/// ⭐ It deliberately does NOT touch the mod's VERSION, and that is the whole point of doing this in one
	/// place. The version a mod reports is the version you have <em>installed</em>; the version on its Nexus page
	/// is the newest one <em>published</em>. Writing the page's version over the installed one tells the manager
	/// the mod is already up to date — so a mod linked by hand would never report an update again, which is
	/// exactly the opposite of why anyone links a mod in the first place.
	/// </summary>
	private async Task EnrichLinkedModFromNexusAsync(StardewMod mod, string nexusId)
	{
		if (string.IsNullOrEmpty(nexusId)) return;

		JObject? details = await _nexusService.GetModDetailsAsync(nexusId);
		if (details == null) return;

		string? author  = details["author"]?.ToString();
		string? summary = details["summary"]?.ToString();
		string? pageName = details["name"]?.ToString();

		if (!string.IsNullOrWhiteSpace(author)) mod.Author = author;
		if (!string.IsNullOrWhiteSpace(summary)) mod.Description = summary;

		// Only rename a mod the manager had to name after its folder — an archive unpacked as
		// "SkyUI-12604-5-2SE" is not what the mod is called. A mod that declared its own name (a Stardew
		// manifest, a BepInEx plugin) keeps it: that is the name it goes by in game.
		string folderName = Path.GetFileName(mod.FolderPath.TrimEnd(Path.DirectorySeparatorChar));
		if (folderName.StartsWith(".")) folderName = folderName.Substring(1);
		if (!string.IsNullOrWhiteSpace(pageName) &&
			string.Equals(mod.Name, folderName, StringComparison.OrdinalIgnoreCase))
		{
			mod.Name = pageName;
		}

		try
		{
			string manifestPath = ManifestPathFor(mod);
			JObject manifest = File.Exists(manifestPath)
				? JObject.Parse(File.ReadAllText(manifestPath))
				: new JObject();
			manifest["Name"] = mod.Name;
			manifest["Author"] = mod.Author;
			manifest["Description"] = mod.Description;
			File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
		}
		catch (Exception ex)
		{
			LogFailure(mod.Name, "Could not save mod details", ex);
		}
	}

	private async Task LinkModUpdateSource(StardewMod? target = null)
	{
		if ((target ?? listInstalled.SelectedItem as StardewMod) is StardewMod stardewMod3)
		{
			string currentId = stardewMod3.NexusID ?? stardewMod3.GitHubRepo ?? "";
			string? typed = ShowTextPrompt(
				Loc.T("link.title"),
				Loc.T("link.prompt", stardewMod3.Name),
				currentId);

			if (typed == null) { Speak(Loc.T("common.changesCancelled")); return; }

			string input = typed.Trim();
			// An emptied box leaves the link alone; "0" is what clears it, as the prompt says. Kept that way on
			// purpose — it is documented in the prompt and people have it in their fingers.
			if (input.Length == 0) { Speak(Loc.T("common.changesCancelled")); return; }

			{
				bool isGitHub = input.Contains("/");
				string? val = (input == "0") ? null : input;

				try
				{
					string manifestPath = Path.Combine(stardewMod3.FolderPath, ".manager_manifest.json");
					if (GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley))
					{
						manifestPath = Path.Combine(stardewMod3.FolderPath, "manifest.json");
					}

					if (!File.Exists(manifestPath))
					{
						var tempManifest = new JObject();
						File.WriteAllText(manifestPath, tempManifest.ToString());
					}

					JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));

					if (isGitHub)
					{
						stardewMod3.GitHubRepo = val;
						stardewMod3.NexusID = null;

						if (GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley))
						{
							manifest["UpdateKeys"] = new JArray($"GitHub:{val}");
						}
						else
						{
							manifest["GitHubRepo"] = val;
							manifest["NexusID"] = null;
						}
					}
					else
					{
						stardewMod3.NexusID = val;
						stardewMod3.GitHubRepo = null;

						if (GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley))
						{
							manifest["UpdateKeys"] = new JArray($"Nexus:{val}");
						}
						else
						{
							manifest["NexusID"] = val;
							manifest["GitHubRepo"] = null;
						}
					}

					File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));

					try
					{
						string mapPath = Path.Combine(AppSettings.AppDataFolder, "mod_id_map.json");
						JObject mapObj = (File.Exists(mapPath) ? JObject.Parse(File.ReadAllText(mapPath)) : new JObject()) ?? new JObject();
						mapObj[stardewMod3.UniqueId] = val;
						File.WriteAllText(mapPath, mapObj.ToString());
					}
					catch (Exception ex) { LogFailure("ModIdMap", "Failed to persist Nexus ID mapping", ex); }

					if (!string.IsNullOrEmpty(val))
					{
						if (isGitHub)
						{
							Speak(Loc.T("link.updatingGitHub"));
							using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{val}");
							req.Headers.UserAgent.ParseAdd($"KinetixModManager/{NexusService.AppVersion}");
							using var resp = await NexusService.HttpClient.SendAsync(req);
							if (resp.IsSuccessStatusCode)
							{
								var details = JObject.Parse(await resp.Content.ReadAsStringAsync());
								stardewMod3.Name = details["name"]?.ToString() ?? stardewMod3.Name;
								stardewMod3.Description = details["description"]?.ToString() ?? stardewMod3.Description;

								// The repository's latest release is NOT the version installed here — recording it
								// as such would mark the mod up to date and hide every future update. The version
								// stays whatever the installed copy reports.
								manifest = JObject.Parse(File.ReadAllText(manifestPath));
								manifest["Name"] = stardewMod3.Name;
								manifest["Description"] = stardewMod3.Description;
								File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
							}
						}
						else
						{
							Speak(Loc.T("link.updatingNexus"));
							await EnrichLinkedModFromNexusAsync(stardewMod3, val!);
						}
					}

					Speak(Loc.T("link.success"));
					Fire(RefreshModList(checkUpdates: false), "RefreshModList");
				}
				catch (Exception ex)
				{
					SpeakBox(Loc.T("link.saveFailedBox", FriendlyError(ex)), Loc.T("common.error"));
					Speak(Loc.T("link.failed"));
				}
			}
		}
	}
}
