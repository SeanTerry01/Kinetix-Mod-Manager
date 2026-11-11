using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
		if (_settings.ActiveGame == GameProfiles.NoGame) return;
		// Catch a Steam/GOG game update before the user tries to launch: warns once if the exe version changed
		// since we last loaded this game. Harmless on Stardew and cheap enough to run on every refresh.
		CheckGameUpdateGuardian();
		Fire(RefreshModList(checkUpdates), "RefreshModList");
		RefreshBackupsList();
		RefreshProfilesList();
		RefreshSmapiLog();
		RefreshGameLog();
		Fire(RefreshMinecraftPacksListAsync(), "RefreshMinecraftPacksListAsync");
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
		if (_settings.ActiveGame == GameProfiles.NoGame) return;

		// Only a game whose mods come from Nexus needs a Nexus key. Without this check a Minecraft player —
		// whose mods come from Modrinth, which has no accounts — opened the manager to the disconnect cue,
		// Settings forced open, and an empty mod list, with nothing on screen explaining what was wanted.
		// Nothing they could have done would have satisfied it, either: the key is not used for their game.
		//
		// ⚠️ Nor does anyone need a key to see what is on their own disk. This used to return here, so local mods could
		// not be listed, switched off or deleted until a key was given. The list comes first, then the question.
		bool needsNexusKey = _nexusService.UsesNexus && string.IsNullOrEmpty(_settings.ApiKey);
		// Once, because every refresh gets here, and being sent to Settings each time would make the list unusable.
		bool askForNexusKey = needsNexusKey && !_isSettingsOpen && !_askedForNexusKey;
		if (needsNexusKey) checkUpdates = false;
		if (askForNexusKey)
		{
			_askedForNexusKey = true;
			SetStatus(Loc.T("status.authRequired"));
			_soundEngine.Play("disconnect");
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
			// A check still running for the game just left is being abandoned, not something to wait on. This game's own
			// check runs once it has wound down. See CompleteUpdateCheckUnit.
			if (_updateCheckGame != _settings.ActiveGame) _checkAgainWhenFree = true;
			else Speak(Loc.T("modlist.updateInProgress"));
			doUpdateChecks = false;
		}
		else if (checkUpdates)
		{
			_updateCheckGame = _settings.ActiveGame;
		}

		// Claim this pass. Two refreshes can overlap — a slow one started before a game switch and the switch's
		// own — and the older one would otherwise finish last and overwrite the list with the previous game's
		// mods. Only the newest pass is allowed to publish its results.
		int generation = Interlocked.Increment(ref _refreshGeneration);
		string refreshingGame = _settings.ActiveGame;
		bool Superseded() => Volatile.Read(ref _refreshGeneration) != generation || _settings.ActiveGame != refreshingGame;

		bool launchedChecks = false;
		bool openedSettings = false;
		bool counted = true;
		void NoLongerRefreshing()
		{
			if (counted) Interlocked.Decrement(ref _refreshInFlight);
			counted = false;
		}
		Interlocked.Increment(ref _refreshInFlight);
		try
		{
		// Nexus's API is stateless, so once the key is validated for this session there's nothing to reconnect —
		// re-checking on every refresh just cost a round-trip and a "connecting… connected" chime each time.
		// Validate only the first time (or after the key changes); afterwards reflect the status quietly.
		// A game whose mods do not come from Nexus never contacts it. Two reasons, and the second is the
		// serious one: connecting costs a round trip and a "connecting… connected as" announcement that means
		// nothing in a Minecraft session — and a FAILED validation returns out of this method entirely, so a
		// player who has no Nexus key (having never needed one) would open the manager to an empty mod list
		// with the real explanation nowhere on screen.
		if (!_nexusService.UsesNexus)
		{
			// No Nexus connection to report. Minecraft in online play has a Microsoft one instead, checked once a
			// run, as Nexus is: once proved, its resting title says so quietly, and a failure is not retried on
			// every refresh — the launch tries again anyway. Unlike Nexus, a failure does not stop the refresh:
			// the mods are on disk either way.
			//
			// ⚠️ The quiet branch matters as much as the check: a game switch leaves the title with no status at all,
			// and a Nexus game gets its "Connected as" back from the else branch below. Minecraft had nothing, so
			// switching away and back left a bare "Minecraft Kinetix Mod Manager".
			if (GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.Minecraft))
			{
				if (_minecraftOnlineWorking is null && _settings.MinecraftPlayOnline && _settings.MinecraftAccount is not null)
					await ConnectMinecraftAccountAsync();
				else
					ResetStatus();
			}
		}
		else if (needsNexusKey)
		{
			// Said above. There is nothing to connect with, and the mods are on disk either way.
		}
		else if (!_nexusService.IsValidated && !checkUpdates && _nexusFailedForKey == _settings.ApiKey)
		{
			// Already tried and already said. Every Space and Delete rescans, and each of them used to wait up to ten
			// seconds and repeat the whole failure before the list changed. Asking for an update check tries again.
			SetStatus(Loc.T(_nexusFailedStatus), speak: false);
		}
		else if (!_nexusService.IsValidated)
		{
			SetStatus(Loc.T("status.connecting"));
			NexusConnection connection = await _nexusService.CheckConnectionAsync();

			// A switch during that wait makes this pass the wrong one, and the newer pass is on its way.
			if (Superseded()) return;

			if (connection == NexusConnection.Connected)
			{
				_nexusFailedForKey = null;
				SetStatus(Loc.T("status.connectedAs", _nexusService.NexusUser));
				_soundEngine.Play("connect");
			}
			else
			{
				// ⚠️ This used to return, leaving an empty list. Everything below reads the disk and needs no network, so
				// being offline cost someone every mod they had, and the message blamed their key.
				_nexusFailedForKey = _settings.ApiKey;
				_nexusFailedStatus = connection == NexusConnection.KeyRejected ? "status.authFailed" : "status.nexusUnreachable";
				SetStatus(Loc.T(_nexusFailedStatus));
				_soundEngine.Play("error");
				if (doUpdateChecks)
				{
					Interlocked.Exchange(ref _updateCheckRunning, 0);
					doUpdateChecks = false;
				}
			}
		}
		else
		{
			SetStatus(Loc.T("status.connectedAs", _nexusService.NexusUser), speak: false);
		}
		// ⚠️ Off the interface thread from here until the list is rebuilt. Measured on a generated Skyrim setup: 1,000 mods
		// of 20 files each is 2.2 seconds a refresh, and 200 files each is 23 seconds, nearly all of it the deployment check.
		// Every Space and Delete runs one, and on this thread the screen reader goes quiet and keys appear dead, which to
		// someone who cannot see the screen reads as a crash. The old list stays up until the new one is ready, which is
		// safe because a game switch has already taken the other game's off the screen. See ForgetPreviousGamesMods.
		//
		// Asked here too, for a slow Minecraft sign-in: this pass carrying on after a newer one finished made that one's
		// list look untrustworthy, and deployment stopped until the next refresh.
		if (Superseded()) return;
		string modsPath = _settings.CurrentModsPath;
		if (!Directory.Exists(modsPath))
		{
			_scannedGame = null;
			_allInstalledMods = new List<GameMod>();
			Invoke(delegate
			{
				listInstalled.Items.Clear();
				if (doUpdateChecks) listUpdates.Items.Clear();
			});
			SpeakBox(Loc.T("modlist.modsPathInvalid"));
			return;
		}

		List<GameMod> scanned;
		try
		{
			scanned = await Task.Run(() => ScanModsFolder(modsPath, refreshingGame));
		}
		catch (Exception ex)
		{
			// One unreadable folder used to end the refresh here with the lists empty and suspended, and the only
			// record of why in a log. To the user the manager opened with no mods and said nothing.
			LogFailure("Scan", "Could not read the mods folder", ex);
			if (Superseded()) return;
			_scannedGame = null;
			_allInstalledMods = new List<GameMod>();
			Invoke(delegate
			{
				listInstalled.Items.Clear();
				if (doUpdateChecks) listUpdates.Items.Clear();
			});
			_soundEngine.Play("error");
			SpeakBox(Loc.T("modlist.scanFailedBox", FriendlyError(ex)));
			return;
		}

		// If the user switched games while that ran, these are the wrong game's mods and the newer pass is already on its
		// way. Publishing them would put the game they just left back on screen.
		if (Superseded()) return;

		// A new list rather than clearing this one. An install hands the list to the installer and awaits the extraction,
		// and a refresh in that gap emptied it, so the installer found nothing to back up.
		_allInstalledMods = scanned;
		_scannedGame = refreshingGame;
		ModHealth.ResolveDependencies(_allInstalledMods, IsNewerVersion);

		// Reconcile Skyrim/Fallout 4 asset deployment to the current enabled set and priority order, and refresh the
		// conflict scan. Only files whose winner changed relink, but finding that out means looking at every one of them.
		if (IsBethesdaGame)
		{
			// Caught, and the list rebuilt anyway. A folder the scan accepts can still make the deployment throw (a subfolder
			// the user can't read), and escaping here left the list never rebuilt for the rest of the session and a Space that
			// had worked reported as failed. Nothing on disk was changed, so the list is still true and stays trusted.
			try
			{
				await Task.Run(() => SyncBethesdaDeployment());
			}
			catch (Exception ex) when (Volatile.Read(ref _refreshInFlight) <= 1)
			{
				LogFailure("Deploy", "Could not bring the game folder up to date", ex);
				Speak(Loc.T("loadorder.deploySyncFailed", FriendlyError(ex)));
			}
			catch (Exception ex)
			{
				// A newer pass is on its way: a folder renamed under this one is no news.
				DiagnosticLog.WriteException("Deploy", "bringing the game folder up to date, with a newer refresh coming", ex);
			}
			try
			{
				await Task.Run(SyncBethesdaPlugins);
			}
			catch (Exception ex)
			{
				LogFailure("Deploy", "Could not write the plugin list", ex);
			}
			if (Superseded()) return;
		}

		// Which mod the user was on, read now rather than before the wait, because they may have moved since.
		string? selectedBefore = null;
		Invoke(delegate
		{
			selectedBefore = (listInstalled.SelectedItem as GameMod)?.UniqueId;
			listInstalled.BeginUpdate();
			if (doUpdateChecks)
			{
				listUpdates.BeginUpdate();
				listUpdates.Items.Clear();
			}
		});
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
				string previousCategory = cmbCategoryFilter.SelectedItem?.ToString() ?? "All Categories";
				cmbCategoryFilter.Items.Clear();
				cmbCategoryFilter.Items.Add("All Categories");
				foreach (string categoryName in hashSet.OrderBy((string c) => c))
				{
					cmbCategoryFilter.Items.Add(categoryName);
				}
				if (cmbCategoryFilter.Items.Contains(previousCategory))
				{
					cmbCategoryFilter.SelectedItem = previousCategory;
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
				if (listUpdates.Items[i] is GameMod updateMod)
				{
					// The Fabric loader, the Minecraft version and a modpack are not mods, so there is no installed mod to
					// match them to, and every rescan dropped them: switching any mod off took the loader's update away.
					// Measured 2026-11-10. They stay until the next check says otherwise.
					if (updateMod.UniqueId is FabricLoaderRowId or MinecraftVersionRowId or ModpackRowId) continue;

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
			}
			else if (listUpdates.Focused && oldSelectedIndex != -1)
			{
				Speak(Loc.T("common.listEmpty"));
			}
			listUpdates.EndUpdate();
			// The one the rescan opened when it cleared the list. Left open, the list stayed suspended for the session.
			if (doUpdateChecks) listUpdates.EndUpdate();

			// Startup only, and last: once the list is actually on screen, put the user on the first tab and say
			// which one it is. Before this, a session opened, announced it was connected, and then went quiet with
			// focus wherever Windows happened to leave it — and with the window's caption deliberately swallowed
			// there was nothing left to say where you had landed. Consumed here rather than in the Shown handler
			// because the refresh is asynchronous, and this is the first point at which "Connected as …" has
			// already been said.
			// Profiles saved before they were kept per game are listed by the mods they name, which are only known now.
			if (!listProfiles.Focused) RefreshProfilesList();

			if (_landOnFirstTabWhenReady)
			{
				_landOnFirstTabWhenReady = false;
				LandOnFirstTab();
			}
		});

		// After the list, so the mods are there behind the question rather than waiting on the answer to it. No longer
		// counted as refreshing, because Settings stays open for as long as the player wants it. Not over another prompt.
		if (askForNexusKey && !OverlayIsOpen)
		{
			openedSettings = true;
			NoLongerRefreshing();
			ShowSettings();
		}

		if (!doUpdateChecks)
		{
			return;
		}
		// Minecraft mods carry no stored catalogue link and need none: Modrinth identifies a mod by the SHA-1
		// of its jar, so every installed mod is checkable and each is its own group. Requiring a link here
		// reported "none of your mods have a Nexus or GitHub link" for a folder in which every single mod
		// could in fact be checked.
		bool minecraft = GameProfiles.Find(_settings.ActiveGame)?.IsMinecraft == true;

		List<IGrouping<string, GameMod>> list = minecraft
			? _allInstalledMods
				.Where(m => !m.IsGroup && !string.IsNullOrEmpty(m.FolderPath))
				.GroupBy(m => "Minecraft:" + m.FolderPath).ToList()
			: (from m in _allInstalledMods
			   where !string.IsNullOrEmpty(m.NexusID) || !string.IsNullOrEmpty(m.GitHubRepo)
			   group m by (!string.IsNullOrEmpty(m.NexusID) ? "Nexus:" + m.NexusID : "GitHub:" + m.GitHubRepo)).ToList();
		// Mods with neither a Nexus nor a GitHub link are excluded from the grouping above; remember them so the
		// completion announcement can say they were skipped (a manually-placed mod would otherwise look "up to
		// date" when it was never checked). For Stardew Valley most of these are still covered by the smapi.io
		// batch below, which resolves mods by UniqueID — only what it doesn't recognise is really unchecked, so
		// the final count is worked out at the end of the run (see UncheckedModCount).
		// Nothing is "unlinked" for Minecraft — every mod can be asked about by hash, so none is skipped for
		// want of a link. A mod Modrinth turns out not to host simply gets no answer, which is reported when
		// the run finishes rather than guessed at before it starts.
		_updateUnlinkedMods = minecraft
			? new List<GameMod>()
			: _allInstalledMods.Where(m => !m.IsGroup
				&& string.IsNullOrEmpty(m.NexusID) && string.IsNullOrEmpty(m.GitHubRepo)).ToList();
		_smapiCheckedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		// Stardew Valley additionally runs one smapi.io batch check (counted as a unit), which catches
		// mods whose manifest update key is missing or broken — the manifest-only Nexus grouping below
		// can't see those. Skyrim/Fallout 4 use only the Nexus group checks.
		bool runSmapi = GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley);
		// Minecraft's loader and the Minecraft version itself are not mods and are checked as a unit of their own,
		// so the run's completion cue waits for them — see CheckMinecraftPlatformUpdatesAsync.
		int unitCount = list.Count + (runSmapi ? 1 : 0) + (minecraft ? 1 : 0);
		if (unitCount == 0)
		{
			_soundEngine.Play("load_complete");
			// When there are unlinked mods present, point the user at Auto-match rather than a dead-end message.
			Speak(_updateUnlinkedMods.Count > 0
				? Loc.T("modlist.noUpdateSourcesUnlinked")
				: Loc.T("modlist.noUpdateSources"));
			return;
		}
		_updateCheckPulse = StartLoadingPulse();
		Interlocked.Exchange(ref _updateChecksUnanswered, 0);
		Interlocked.Exchange(ref _activeChecks, unitCount);
		Speak(Loc.T("modlist.checkingUpdates"));
		launchedChecks = true;
		if (runSmapi)
		{
			var (smapiVer, gameVer) = DetectStardewVersions();
			Fire(CheckUpdatesViaSmapiApi(_allInstalledMods.Where(m => !m.IsGroup).ToList(), smapiVer, gameVer), "CheckUpdatesViaSmapiApi");
		}
		if (minecraft)
		{
			Fire(CheckMinecraftPlatformUpdatesAsync(), "CheckMinecraftPlatformUpdatesAsync");
		}
		// One request each that can answer for every mod, shared by the checks below. With only one page to ask, the
		// list of changes would cost as much as it saves.
		Task<HashSet<int>?>? updatedThisMonth = list.Count(g => g.Key.StartsWith("Nexus:", StringComparison.Ordinal)) > 1
			? _nexusService.GetRecentlyUpdatedModIdsAsync("1m", _nexusService.CurrentGameDomain)
			: null;
		Task<Dictionary<string, string>?>? modrinthAnswers = minecraft ? LatestModrinthVersionsAsync(list.Select(g => g.First()).ToList()) : null;
		foreach (IGrouping<string, GameMod> modsToCheck in list)
		{
			Fire(CheckForUpdates(modsToCheck.ToList(), updatedThisMonth, modrinthAnswers), "CheckForUpdates");
		}
		}
		finally
		{
			NoLongerRefreshing();
			// The question was never put, so a later pass may still put it.
			if (askForNexusKey && !openedSettings) _askedForNexusKey = false;
			// Release the guard unless the async checks took ownership of it; once launched they
			// release it on completion (see CheckForUpdates). This also frees it on any early
			// return or exception above, so a failed scan can't block all future update checks.
			if (doUpdateChecks && !launchedChecks)
			{
				Interlocked.Exchange(ref _updateCheckRunning, 0);

				// ⚠️ A check that was asked for must not vanish because a rescan overtook this pass. The scan takes seconds
				// now, so pressing Space during Refresh Everything used to lose the check with nothing said, and a game switch
				// during the startup scan left the new game's check refused and nothing to run it.
				bool overtaken = Volatile.Read(ref _refreshGeneration) != generation && _settings.ActiveGame == refreshingGame;
				bool anotherWasRefused = _checkAgainWhenFree;
				if ((overtaken || anotherWasRefused) && !_shuttingDown)
				{
					_checkAgainWhenFree = false;
					BeginInvoke(() => Fire(RefreshModList(checkUpdates: true), "RefreshModList"));
				}
			}
		}
	}

	/// <summary>The key Nexus last failed for this run, so that failure is said once rather than on every rescan.</summary>
	private string? _nexusFailedForKey;

	/// <summary>What that failure was, as a phrase key, for the window title.</summary>
	private string _nexusFailedStatus = "status.nexusUnreachable";

	/// <summary>Everything a refresh reads from the disk. Off the interface thread, so it touches no control.</summary>
	private List<GameMod> ScanModsFolder(string modsPath, string game)
	{
		string idMapPath = Path.Combine(AppSettings.AppDataFolder, "mod_id_map.json");
		string legacyIdMapPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mod_id_map.json");
		if (File.Exists(legacyIdMapPath) && !File.Exists(idMapPath))
		{
			try { File.Copy(legacyIdMapPath, idMapPath, overwrite: true); }
			catch (Exception ex) { DiagnosticLog.WriteException("ModIdMap", "copying the old Nexus id map into place", ex); }
		}

		List<GameMod> scanned = ModScanner.ScanMods(modsPath, AtomicFile.ReadJsonObjectOrEmpty(idMapPath), _settings, game, LogError);

		// Where each mod's page is, from whatever earlier check learned it. A convenience the scan itself does not need.
		ModPageLinks.Apply(scanned, ModPageLinks.Load(AppSettings.AppDataFolder));
		return scanned;
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
			// Announced up front: the update check that follows has plenty to say for itself when it finishes. With no key
			// there is no check to follow, and nothing else would say why.
			Speak(Loc.T(_nexusService.UsesNexus && string.IsNullOrEmpty(_settings.ApiKey) ? "modlist.refreshingNoKey" : "modlist.refreshingAll"));
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
	private string InstalledGroupKey(GameMod mod)
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
		bool StatusMatch(GameMod m) => status switch
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
		GameMod? selectedMod = listInstalled.SelectedItem as GameMod;
		string? restoreId = preferUniqueId ?? selectedMod?.UniqueId;
		// Read before the list is emptied. Compared afterwards, it was always -1, so every rescan counted as moving the user
		// even when they were put back on the same row, which started the swallow that destroys whatever was just said:
		// "Foo is now dis...", then the row again.
		int indexBefore = listInstalled.SelectedIndex;
		listInstalled.Items.Clear();
		foreach (IGrouping<string, GameMod> folderGroup in from g in _allInstalledMods.Where((GameMod m) => !m.IsGroup).GroupBy(InstalledGroupKey)
			orderby g.Key
			select g)
		{
			List<GameMod> folderMods = folderGroup.ToList();
			// Status 4 and 5 filter by shape, not by state, so they are decided here: whether something is a mod
			// group is a property of the folder it lives in and not of any one mod, which is all StatusMatch can
			// see. "Single mods only" keeps folders holding exactly one mod; "Mod groups only" keeps the rest.
			if (status == 4 && folderMods.Count > 1) continue;
			if (status == 5 && folderMods.Count == 1) continue;

			List<GameMod> matchingMods = folderMods.Where((GameMod m) => (string.IsNullOrEmpty(query) || m.Name.ToLower().Contains(query) || m.Author.ToLower().Contains(query) || m.Note.ToLower().Contains(query)) && (category == "All Categories" || m.Category == category) && StatusMatch(m)).ToList();
			_installedMatchCount += matchingMods.Count;
			if (matchingMods.Count == 0)
			{
				continue;
			}
			// Grouping survives a filter. What changes is which mods the group is made of: a folder still reads as
			// one group when more than one of its mods matched, and the header counts only those, so "Contains 3
			// mods" is never a claim about rows that are not there. A folder with a single match is not a group at
			// all, the same rule an unfiltered list of one follows.
			if (matchingMods.Count == 1)
			{
				foreach (GameMod onlyMatch in matchingMods)
				{
					onlyMatch.IsSubMod = false;
					onlyMatch.IsGroup = false;
					listInstalled.Items.Add(onlyMatch);
				}
				continue;
			}
			// Collapsed or expanded exactly as the user last left it, filter or no filter. A collapsed group under
			// a filter reads as "Contains 2 mods", meaning two that matched — the same bargain the unfiltered list
			// already makes, and the reason the count above is not taken from the visible rows.
			bool groupIsOpen = _expandedGroups.Contains(folderGroup.Key);
			GameMod groupRow = new GameMod
			{
				IsGroup = true,
				GroupName = folderGroup.Key,
				IsExpanded = groupIsOpen,
				UniqueId = "GROUP:" + folderGroup.Key,
				SubMods = matchingMods,
				// The active game's mods folder, not the legacy Stardew one this used — and for a group keyed by
				// a Witcher family there is no folder of that name at all, so the parent is the honest answer.
				FolderPath = _settings.CurrentModsPath
			};
			listInstalled.Items.Add(groupRow);
			if (!groupIsOpen)
			{
				continue;
			}
			foreach (GameMod subMod in matchingMods)
			{
				subMod.IsSubMod = true;
				subMod.IsGroup = false;
				listInstalled.Items.Add(subMod);
			}
		}
		if (restoreId != null)
		{
			for (int num = 0; num < listInstalled.Items.Count; num++)
			{
				if (listInstalled.Items[num] is GameMod rowMod && rowMod.UniqueId == restoreId)
				{
					// Putting the user back after a rescan is a move they did not make, so the row has to name
					// itself — otherwise a refresh finishing under them reads out a position and nothing else.
					// Not so when a keypress caused the rebuild (collapsing a group): the reader announces the row
					// the key landed on, and naming it here as well would say it twice.
					if (announceRestoredRow && num != indexBefore) _announceRowNameOnNextChange = true;
					listInstalled.SelectedIndex = num;
					// The list is usually not focused while a rescan runs, so the focus rectangle would otherwise
					// stay on whatever row it was left on and be read out ahead of this one.
					AlignListCaretToSelection(listInstalled);
					break;
				}
			}
		}
		// The row it was on has gone, deleted or filtered out. The one now in its place, as every other list does: going back
		// to the top left someone at mod 120 of 147 arrowing all the way back, and only told so by a position.
		if (listInstalled.SelectedIndex == -1 && listInstalled.Items.Count > 0)
		{
			if (announceRestoredRow && indexBefore >= 0) _announceRowNameOnNextChange = true;
			listInstalled.SelectedIndex = Math.Min(Math.Max(indexBefore, 0), listInstalled.Items.Count - 1);
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

	/// <summary>What <see cref="SayAndRefreshModList"/> has yet to say.</summary>
	private string? _resultForRestoredRow;
	/// <summary>The mod that result names, so the row it lands on is not named a second time.</summary>
	private string? _resultAboutModId;

	/// <summary>
	/// Says the result of something that changes the list, and rescans it, without the rescan cutting the result off.
	///
	/// When the rescan moves the row under the user, the reader's own reading of that move has to be swallowed (see
	/// <see cref="SpeakListPosition"/>), and the swallow can't tell the reader's voice from ours. The focus event behind
	/// the move throws speech away as well. So "X is now disabled" said up front lost out to "X. 12 of 149". Saying it
	/// after the move is the only order that can't lose: "X is now disabled. 12 of 149", in one sentence.
	///
	/// With the list not holding focus nothing gets swallowed, so it is simply said now. A slow rescan doesn't get to hold
	/// the result back for long, either: a large Bethesda setup can take many seconds.
	/// </summary>
	/// <param name="result">What happened.</param>
	/// <param name="about">The mod <paramref name="result"/> names, when it names the one the user is on.</param>
	private async Task SayAndRefreshModList(string result, GameMod? about = null)
	{
		SayWaitingResult();

		// Sean's "way 2" (2026-11-11), with a reader that can say when it has finished: the result is heard out first, and
		// only then does the rescan move the list, so nothing has to be swallowed. The reader reads the row if it moved and
		// the position follows. A key pressed meanwhile stops the reader, which ends the wait. See ActBeforeReturning.
		if (_announcer.IsSpeakingForCertain is not null)
		{
			Speak(result);
			await WaitForSpeechAsync();
			await RefreshModList(checkUpdates: false);
			return;
		}

		if (!listInstalled.Focused)
		{
			Speak(result);
			await RefreshModList(checkUpdates: false);
			return;
		}

		_resultForRestoredRow = result;
		_resultAboutModId = about?.UniqueId;
		try
		{
			Task refresh = RefreshModList(checkUpdates: false);
			if (await Task.WhenAny(refresh, Task.Delay(1000)) != refresh) SayWaitingResult();
			await refresh;
		}
		finally
		{
			// The row didn't move, or there is no row left, so nothing carried it.
			SayWaitingResult();
		}
	}

	private void SayWaitingResult()
	{
		if (_resultForRestoredRow is not { } waiting) return;
		_resultForRestoredRow = null;
		Speak(waiting);
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
		if ((listInstalled.SelectedItem as GameMod)?.UniqueId == uniqueId) return;

		for (int i = 0; i < listInstalled.Items.Count; i++)
		{
			if (listInstalled.Items[i] is GameMod candidate && candidate.UniqueId == uniqueId)
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
	private void EditModKeepingPlace(GameMod mod, Action openEditor)
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

	/// <summary>
	/// Scans the backups directory and fills the Backups tab's two lists: Deleted Mods, the backups taken as a mod was
	/// deleted, and Replaced Copies, everything else — taken before an update, a reinstall or a restore wrote over a mod.
	/// </summary>
	private void RefreshBackupsList()
	{
		if (_settings.ActiveGame == GameProfiles.NoGame) return;
		if (listBackups == null || listDeletedBackups == null)
		{
			return;
		}

		// Newest first, which is what the store returns: the backup a user wants is nearly always the one
		// taken moments ago, by the update they are now regretting.
		List<BackupItem> all = BackupStore.List(backupsPath).ToList();
		FillBackupList(listDeletedBackups, all.Where(IsDeletedModBackup));
		FillBackupList(listBackups, all.Where(b => !IsDeletedModBackup(b)));
	}

	/// <summary>Whether a backup was taken as its mod was deleted. Those carry <see cref="BackupRestore.DeleteSuffix"/> after the name.</summary>
	private static bool IsDeletedModBackup(BackupItem backup) =>
		BackupStore.ModNameFromFileName(backup.FullPath).EndsWith(BackupRestore.DeleteSuffix, StringComparison.Ordinal);

	/// <summary>One of the Backups tab's lists, put back on the row the user was on.</summary>
	private void FillBackupList(ListBox list, IEnumerable<BackupItem> items)
	{
		// Your place is kept only while you are in the list; otherwise the newest, the one a new backup would be.
		bool inList = list.Focused;
		int oldIndex = inList ? list.SelectedIndex : 0;
		// By file: two backups of one mod share a name.
		string? oldName = inList ? (list.SelectedItem as BackupItem)?.FullPath : null;

		list.BeginUpdate();
		list.Items.Clear();
		foreach (BackupItem item in items) list.Items.Add(item);

		if (list.Items.Count > 0)
		{
			int newIndex = -1;
			if (!string.IsNullOrEmpty(oldName))
			{
				for (int i = 0; i < list.Items.Count; i++)
				{
					if (string.Equals((list.Items[i] as BackupItem)?.FullPath, oldName, StringComparison.OrdinalIgnoreCase))
					{
						newIndex = i;
						break;
					}
				}
			}
			// The row found, wherever it is now. Taking the larger of the two landed on a different row whenever rows above had gone.
			list.SelectedIndex = newIndex >= 0 ? newIndex : Math.Min(Math.Max(oldIndex, 0), list.Items.Count - 1);
		}
		else if (list.Focused && oldIndex != -1)
		{
			Speak(Loc.T("common.listEmpty"));
		}
		list.EndUpdate();
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
		// Protected, like every key box: filled with the saved key and selected, a plain box read it out in full.
		string? text = ShowTextPrompt(Loc.T("login.title"), Loc.T("login.pasteApiKey"), _settings.ApiKey, secret: true, sayOnCancel: Loc.T("common.changesCancelled"));
		if (text == null) return;

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
	private string ManifestPathFor(GameMod mod) => Path.Combine(mod.FolderPath,
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
	private async Task EnrichLinkedModFromNexusAsync(GameMod mod, string nexusId)
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
			AtomicFile.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
		}
		catch (Exception ex)
		{
			LogFailure(mod.Name, "Could not save mod details", ex);
		}
	}

	private async Task LinkModUpdateSource(GameMod? target = null)
	{
		if ((target ?? ModOnThisTab()) is GameMod linkedMod)
		{
			if (linkedMod.IsGroup)
			{
				Speak(Loc.T("common.modGroupFirst"));
				return;
			}

			string currentId = linkedMod.NexusID ?? linkedMod.GitHubRepo ?? "";
			string? typed = ShowTextPrompt(
				Loc.T("link.title"),
				Loc.T("link.prompt", linkedMod.Name),
				currentId, sayOnCancel: Loc.T("common.changesCancelled"));

			if (typed == null) return;

			string input = typed.Trim();
			// An emptied box leaves the link alone; "0" is what clears it, as the prompt says. Kept that way on
			// purpose — it is documented in the prompt and people have it in their fingers.
			if (input.Length == 0) { Speak(Loc.T("common.changesCancelled")); return; }

			{
				bool isGitHub = input.Contains("/");
				string? val = (input == "0") ? null : input;

				try
				{
					string manifestPath = Path.Combine(linkedMod.FolderPath, ".manager_manifest.json");
					if (GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley))
					{
						manifestPath = Path.Combine(linkedMod.FolderPath, "manifest.json");
					}

					if (!File.Exists(manifestPath))
					{
						var tempManifest = new JObject();
						AtomicFile.WriteAllText(manifestPath, tempManifest.ToString());
					}

					JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));

					if (isGitHub)
					{
						linkedMod.GitHubRepo = val;
						linkedMod.NexusID = null;

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
						linkedMod.NexusID = val;
						linkedMod.GitHubRepo = null;

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

					AtomicFile.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));

					try
					{
						string mapPath = Path.Combine(AppSettings.AppDataFolder, "mod_id_map.json");
						JObject mapObj = AtomicFile.ReadJsonObjectOrEmpty(mapPath);
						mapObj[linkedMod.UniqueId] = val;
						AtomicFile.WriteAllText(mapPath, mapObj.ToString());
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
								linkedMod.Name = details["name"]?.ToString() ?? linkedMod.Name;
								linkedMod.Description = details["description"]?.ToString() ?? linkedMod.Description;

								// The repository's latest release is NOT the version installed here — recording it
								// as such would mark the mod up to date and hide every future update. The version
								// stays whatever the installed copy reports.
								manifest = JObject.Parse(File.ReadAllText(manifestPath));
								manifest["Name"] = linkedMod.Name;
								manifest["Description"] = linkedMod.Description;
								AtomicFile.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
							}
						}
						else
						{
							Speak(Loc.T("link.updatingNexus"));
							await EnrichLinkedModFromNexusAsync(linkedMod, val!);
						}
					}

					Fire(SayAndRefreshModList(Loc.T("link.success"), linkedMod), "RefreshModList");
				}
				catch (Exception ex)
				{
					SpeakBox(Loc.T("link.saveFailedBox", FriendlyError(ex)), Loc.T("common.error"));
				}
			}
		}
		else
		{
			Speak(Loc.T("common.noModSelected"));
		}
	}
}
