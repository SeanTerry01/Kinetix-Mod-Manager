using System.Diagnostics;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>Mod and application update checking and installation for Form1.</summary>
public partial class Form1
{
	/// <summary>
	/// Records the newest version any source has reported for <paramref name="mod"/>, keeping whichever is
	/// higher. The Nexus and smapi.io checks run at the same time and don't always agree: a Nexus page's own
	/// version field is set by hand and often lags the files on it (Machine Control Panel's page said 2.2.0
	/// while 2.3.0 was the current release). Plain assignment let whichever check finished last win, so the
	/// Nexus answer could overwrite a newer one after smapi.io had already listed the mod as updatable —
	/// leaving a row reading "Current: 2.2.0. Latest: 2.2.0". Taking the maximum makes the result independent
	/// of which check finishes first.
	/// </summary>
	private void RecordLatestVersion(GameMod mod, string? candidate) =>
		mod.LatestVersion = ModVersions.Best(mod.LatestVersion, candidate) ?? mod.LatestVersion;

	/// <summary>The key identifying the one download a mod comes from — its Nexus page or GitHub repo.</summary>
	private static string DownloadKey(GameMod mod) => ModVersions.DownloadKey(mod);

	/// <summary>The release of <paramref name="key"/>'s download the manager knows is installed, or null.</summary>
	private string? InstalledDownloadVersion(string key) =>
		_settings.InstalledDownloadVersions.TryGetValue(_settings.ActiveGame, out var map) &&
		map.TryGetValue(key, out string? version) && !string.IsNullOrWhiteSpace(version)
			? version : null;

	/// <summary>
	/// Forgets which release of a download is installed, after an older copy has been put back. Left in place, the
	/// record would say the newer release is still here, and the update would never be offered again.
	/// </summary>
	private void ForgetInstalledDownloadVersion(string key)
	{
		if (_settings.InstalledDownloadVersions.TryGetValue(_settings.ActiveGame, out var map) && map.Remove(key))
			_settings.Save();
	}

	/// <summary>
	/// Records which release of a download is installed, so later checks compare like with like. Called when the
	/// manager installs a download and therefore knows exactly what went on disk.
	/// </summary>
	private void RecordInstalledDownloadVersion(string key, string? version)
	{
		if (string.IsNullOrWhiteSpace(version) || string.IsNullOrEmpty(key) || key == "GitHub:") return;
		if (!_settings.InstalledDownloadVersions.TryGetValue(_settings.ActiveGame, out var map))
			_settings.InstalledDownloadVersions[_settings.ActiveGame] = map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (map.TryGetValue(key, out string? existing) && existing == version) return;
		map[key] = version!;
		_settings.Save();
	}

	/// <summary>
	/// Whether <paramref name="installed"/> still has an update pending at <paramref name="latestVersion"/>.
	/// Uses the recorded release of its download when there is one, so a mod whose manifest version the author
	/// never bumped isn't offered the same update forever. The single place this question is answered.
	/// </summary>
	private bool HasPendingUpdate(GameMod installed, string? latestVersion) =>
		ModVersions.HasPendingUpdate(installed, latestVersion, InstalledDownloadVersion(DownloadKey(installed)));

	/// <summary>
	/// Takes a row the user has settled off the Updates list and reports where the cursor landed: the row now
	/// under it and its position, or <c>null</c> when that was the last one. Shared by "ignore this version" and
	/// "mark this version as installed" — a list that quietly loses the row you were on leaves you somewhere you
	/// were never told about.
	///
	/// The caller does the speaking so each phrase stays a literal <c>Loc.T</c> key in the source, where
	/// <c>SpokenStringGuardTests</c> can see it. Passing the key in as a variable would put the announcement
	/// beyond the reach of the test that stops a typo being read aloud as itself.
	/// </summary>
	private (string Row, int Index, int Count)? RemoveSettledUpdateRow(GameMod mod)
	{
		int oldIndex = listUpdates.SelectedIndex;
		listUpdates.Items.Remove(mod);
		if (listUpdates.Items.Count == 0) return null;

		listUpdates.SelectedIndex = Math.Min(oldIndex, listUpdates.Items.Count - 1);
		return (listUpdates.SelectedItem?.ToString() ?? "", listUpdates.SelectedIndex + 1, listUpdates.Items.Count);
	}

	/// <summary>
	/// Records the version the Updates tab is offering as the one the user already has, for the selected row.
	///
	/// This is the answer to a mod whose manifest version can never match its Nexus page. SMAPI requires a
	/// semantic version — two or three numbers — while a Nexus version field is free text, so an author who
	/// publishes "2.0.3.5" has a manifest that must still say "2.0.3". The check falls back to the manifest
	/// version when nothing better is recorded, so such a mod is offered the same update forever. Editing the
	/// manifest to match is not a fix: SMAPI refuses to parse it and skips the mod entirely.
	///
	/// The manager already records the real release whenever it installs a download itself. This is the way to
	/// say so for a mod that arrived some other way — installed by hand, or before that recording existed —
	/// without reinstalling it. Unlike ignoring a version, this is not a mute: a genuinely newer release is
	/// still reported, because what gets stored is a version to compare against, not a version to skip.
	/// </summary>
	private void MarkSelectedUpdateAsInstalled()
	{
		// The Updates tab has to be the one in front. This is a global shortcut, and listUpdates keeps whatever
		// was selected the last time the user was there — so without this, pressing it from the Installed tab
		// would silently settle a row they are not looking at and cannot hear.
		if (mainTabs.SelectedTab != tabUpdates || listUpdates.SelectedItem is not GameMod mod || mod.IsGroup)
		{
			Speak(Loc.T("updates.markSelectMod"));
			return;
		}
		if (string.IsNullOrWhiteSpace(mod.LatestVersion))
		{
			Speak(Loc.T("updates.markNoVersion", mod.Name));
			return;
		}
		// Without a Nexus page or GitHub repo there is no download to key the record to, and recording it
		// against nothing would look like it had worked while changing nothing at all.
		if (!UpdateCoverage.HasUpdateLink(mod))
		{
			Speak(ShortcutIsSet("ManualID") ? Loc.T("updates.markNoLink", mod.Name, GetShortcutString("ManualID")) : Loc.T("updates.markNoLinkNoKey", mod.Name));
			return;
		}

		// The result is said before the question closes, and the row goes once it has been heard. See ActBeforeReturning.
		SpeakBox(Loc.T("updates.markConfirm", mod.LatestVersion, mod.Name), Loc.T("updates.markTitle"),
			MessageBoxButtons.YesNo, answer =>
			{
				if (answer != DialogResult.Yes) return new ResultBeforeReturning(Loc.T("updates.markCancelled"));

				string version = mod.LatestVersion!;
				RecordInstalledDownloadVersion(DownloadKey(mod), version);
				// The mod's own Version is left alone on purpose: it is what the manifest says, and on exactly the mods
				// this command exists for the manifest legitimately says something else. Overwriting it here would put a
				// number in the installed list that no file on disk agrees with, until the next rescan quietly undid it.

				bool last = listUpdates.Items.Count <= 1;
				return new ResultBeforeReturning(
					last ? Loc.T("updates.markedEmpty", mod.Name, version) : Loc.T("updates.marked", mod.Name, version),
					() => RemoveSettledUpdateRow(mod));
			});
	}

	/// <summary>
	/// The newest version among the files a mod page offers as its main download, or <c>null</c> when there are
	/// none to compare. Only ever used as a second opinion when the page's own version said the mod was current.
	/// </summary>
	private string? NewestMainFileVersion(List<string>? versions)
	{
		if (versions == null || versions.Count == 0) return null;

		// Highest rather than most recently uploaded. A page whose mod ships in parts has a MAIN file per part
		// with a version of its own — Engine Fixes' preloader is version "7" beside a plugin at "7.0.21" — and
		// the one that answers "is there anything newer than what I have" is the highest of them.
		return versions.Aggregate((best, v) => IsNewerVersion(best, v) ? v : best);
	}

	/// <summary>Nexus mods this update check answered from the record, and those it asked about. Logged at the end.</summary>
	private int _nexusRemembered, _nexusAsked;

	private async Task CheckForUpdates(List<GameMod> group, Task<HashSet<int>?>? updatedThisMonth, Task<Dictionary<string, string>?>? modrinthAnswers)
	{
		// Read now, while this is the game the check is for. A queued request reading it later asked the other game's
		// page about this mod's id, and a row for an unrelated mod landed on the new game's list.
		string game = _settings.ActiveGame;
		string domain = _nexusService.CurrentGameDomain;

		try
		{
			string? latestVersion = null;
			bool remembered = false;
			if (GameProfiles.Find(_settings.ActiveGame)?.IsMinecraft == true)
			{
				// Modrinth first, by the hash of the jar, which is exact. Then GitHub for a mod Modrinth does
				// not host — United Minecraft is published on GitHub releases only, and it names its own
				// repository in its manifest, so there is no reason for it to be the one mod nothing can check.
				string? remote = await LatestModrinthVersionAsync(group[0], modrinthAnswers)
					?? (!string.IsNullOrEmpty(group[0].GitHubRepo)
						? await LatestGitHubBuildForMinecraftAsync(group[0].GitHubRepo!)
						: null);

				// ⚠️ Settled here rather than left to the general comparison, which splits on dots and so reads
				// "1.1.0+mc26.2" as having a fourth segment that "1.1.0" lacks — reporting the mod out of date
				// for ever, and "updating" it by downloading the same release again.
				latestVersion = MinecraftLayout.SameRelease(group[0].Version, remote) ? null : remote;
			}
			else if (!string.IsNullOrEmpty(group[0].NexusID))
			{
				latestVersion = await RememberedNexusVersion(game, group[0].NexusID!, updatedThisMonth);
				remembered = latestVersion != null;
				Interlocked.Increment(ref remembered ? ref _nexusRemembered : ref _nexusAsked);
				latestVersion ??= await _nexusService.GetLatestVersionAsync(group[0].NexusID ?? "", domain);
				if (latestVersion == null) Interlocked.Increment(ref _updateChecksUnanswered);
			}
			else if (!string.IsNullOrEmpty(group[0].GitHubRepo))
			{
				latestVersion = await GetGitHubLatestReleaseVersionAsync(group[0].GitHubRepo ?? "");
				if (latestVersion == null) Interlocked.Increment(ref _updateChecksUnanswered);
			}

			if (latestVersion == null || _settings.ActiveGame != game) return;

			string key = DownloadKey(group[0]);
			// What's installed from this download: what we recorded when we installed it, or — for a mod that
			// arrived by hand — the newest version any of its mods claims, since the main mod usually carries
			// the release's number while the extras bundled with it keep their own.
			string installedVersion = InstalledDownloadVersion(key)
				?? group.Select(m => m.Version).Aggregate((best, v) => IsNewerVersion(best, v) ? v : best);

			// A mod page's version number is maintained by hand, separately from uploading the file, so a page
			// can sit on an old number while its own Files tab already offers a newer release. Believing the page
			// alone is how SSE Engine Fixes 7.0.21 went unreported for a week — the release that Skyrim 1.7.104
			// will not start without. So when the page claims there is nothing new, the files are asked as well.
			if (!remembered && !string.IsNullOrEmpty(group[0].NexusID))
			{
				// A page whose files were not asked about, for want of quota, has not been fully checked, so it isn't remembered.
				bool thorough = true;
				if (!IsNewerVersion(installedVersion, latestVersion))
				{
					List<string>? mainFiles = await _nexusService.GetMainFileVersionsAsync(group[0].NexusID!);
					thorough = mainFiles != null;
					string? fromFiles = NewestMainFileVersion(mainFiles);
					if (IsNewerVersion(installedVersion, fromFiles)) latestVersion = fromFiles!;
					if (_settings.ActiveGame != game) return;
				}
				if (thorough) RememberNexusCheck(game, group[0].NexusID!, latestVersion);
			}

			// Any of them, because the row stands for the download through whichever mod represents it, and Delete records the
			// ignore against that one. Asking only the first in the group never found it, so the row came back on the next check.
			if (group.Any(m => _settings.IgnoredVersions.TryGetValue(m.UniqueId, out string? ignored) && ignored == latestVersion))
				return;

			foreach (GameMod mod in group) RecordLatestVersion(mod, latestVersion);

			if (!IsNewerVersion(installedVersion, latestVersion)) return;

			// One row per download, named after the mod that best stands for it — the main mod rather than a
			// content pack bundled with it — so the row is recognisable (see UpdateCoverage.PickRepresentative).
			GameMod representative = UpdateCoverage.PickRepresentative(group, _settings.CurrentModsPath);

			Invoke(delegate
			{
				listUpdates.BeginUpdate();
				if (!listUpdates.Items.OfType<GameMod>().Any(representative.IsSameModAs))
					listUpdates.Items.Add(representative.AsUpdateRow());
				listUpdates.EndUpdate();
			});
		}
		catch (Exception ex)
		{
			Interlocked.Increment(ref _updateChecksUnanswered);
			DiagnosticLog.WriteException("Updates", $"checking {group[0].Name} for updates", ex);
		}
		finally
		{
			CompleteUpdateCheckUnit();
		}
	}

	/// <summary>
	/// Decrements the count of in-flight update-check units (Nexus/GitHub groups plus the optional
	/// smapi.io batch) and, once the last one finishes, ends the loading state, releases the
	/// single-batch guard, plays the completion cue, and announces the final result. Shared by every
	/// check unit so the cue and announcement fire exactly once regardless of how many sources ran.
	/// </summary>
	// Lookups in this check that got no usable answer, so the result can say so rather than call everything current.
	private int _updateChecksUnanswered;

	// The game the running check is for, and whether another check was refused while it ran on after a game switch.
	private string? _updateCheckGame;
	private bool _checkAgainWhenFree;

	// A SMAPI program update found during the smapi.io check, surfaced only after the whole update
	// run finishes so its prompt doesn't interrupt the mod-update announcement. Null when none.
	private (string Current, string Latest)? _pendingSmapiUpdate;

	// The installed mods the last update check had no Nexus/GitHub link for, so the manifest-based Nexus check
	// couldn't cover them. Surfaced in the completion announcement so "all up to date" isn't misleading — a
	// manually-placed mod with a null NexusID would otherwise be silently ignored. See RefreshModList.
	private List<GameMod> _updateUnlinkedMods = new();

	// UniqueIDs the SMAPI web API recognised during this check. Those mods were version-checked against SMAPI's
	// mod database whether or not their manifest carries an update key, so they are not "unchecked" — Stardew
	// users were previously told dozens of perfectly ordinary mods "have no Nexus or GitHub link".
	private HashSet<string> _smapiCheckedIds = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// How many mods really went unchecked. A mod counts only when nothing can tell the manager where it came
	/// from: no update link of its own, unknown to the SMAPI mod database, and not bundled inside another,
	/// linked mod's download (see <see cref="ClassifyUpdateCoverage"/>). Bundled mods — the several mods one
	/// Nexus archive unpacks into, where only one carries the update key — are covered by that download, and
	/// counting them made an ordinary Stardew setup sound like dozens of mods were being ignored.
	/// </summary>
	private int UncheckedModCount() =>
		ClassifyUpdateCoverage().Count(c => c.Kind == UpdateCoverageKind.Unchecked);

	/// <summary>
	/// Final consistency pass over the Updates list: drops any row whose latest version is no longer newer than
	/// what is installed. Rows are added by two checks running at once, each of which may learn a different
	/// "latest" from its own source, so this is the one place that guarantees what the user is shown is
	/// genuinely an update — never a row reading "Current: 2.2.0. Latest: 2.2.0". Runs on the UI thread once
	/// every check in the batch has finished.
	/// </summary>
	private void DropUpToDateUpdateRows()
	{
		listUpdates.BeginUpdate();
		for (int i = listUpdates.Items.Count - 1; i >= 0; i--)
		{
			if (listUpdates.Items[i] is GameMod m && !HasPendingUpdate(m, m.LatestVersion))
				listUpdates.Items.RemoveAt(i);
		}
		listUpdates.EndUpdate();
	}

	private void CompleteUpdateCheckUnit()
	{
		if (Interlocked.Decrement(ref _activeChecks) <= 0)
		{
			_updateCheckPulse?.End();
			// This batch is done; release the guard so the next update check can start.
			Interlocked.Exchange(ref _updateCheckRunning, 0);

			// The check was for a game that is no longer open, so its result means nothing here. Its rows go, and the
			// open game gets the check it was refused while this one was winding down.
			if (_updateCheckGame != _settings.ActiveGame)
			{
				_pendingSmapiUpdate = null;
				_pendingMinecraftUpdate = null;
				bool again = _checkAgainWhenFree;
				_checkAgainWhenFree = false;
				Invoke(delegate
				{
					listUpdates.Items.Clear();
					if (again) Fire(RefreshModList(checkUpdates: true), "RefreshModList");
				});
				return;
			}
			// Set only for a check refused while an abandoned one wound down, so it must not carry over into a later refresh.
			_checkAgainWhenFree = false;

			_soundEngine.Play("load_complete");
			var pendingSmapi = _pendingSmapiUpdate;
			_pendingSmapiUpdate = null;
			var pendingMinecraft = _pendingMinecraftUpdate;
			_pendingMinecraftUpdate = null;
			int unchecked_ = UncheckedModCount();
			// How much the record saved, for the log: the only way to tell a working record from one that is never used.
			DiagnosticLog.Write("Nexus", $"update check: {Interlocked.Exchange(ref _nexusRemembered, 0)} mods known from the record, {Interlocked.Exchange(ref _nexusAsked, 0)} asked");
			// Written once at the end, so a check of 1,000 mods doesn't rewrite the settings 1,000 times.
			_settings.Save();
			bool toldToSlowDown = _nexusService.Pause.IsActive(DateTime.UtcNow) || _gitHubPause.IsActive(DateTime.UtcNow);
			Invoke(delegate
			{
				DropUpToDateUpdateRows();
				// "All mods are up-to-date" is a claim about every mod. With Nexus down, or the day's requests used up, every
				// lookup failed and it was said anyway, so someone was told all was well by a check that learned nothing.
				bool someUnanswered = Volatile.Read(ref _updateChecksUnanswered) > 0;
				string message = listUpdates.Items.Count > 0
					? Loc.T(listUpdates.Items.Count == 1 ? "updates.checkCompleteOne" : "updates.checkComplete", listUpdates.Items.Count)
					: Loc.T(someUnanswered ? "updates.checkFoundNoneSoFar" : "updates.checkCompleteNone");
				if (someUnanswered) message += " " + Loc.T(toldToSlowDown ? "updates.toldToSlowDown" : "updates.someUnanswered");
				// Tell the user when some mods couldn't be checked at all (no Nexus/GitHub link), so an
				// "all up to date" result isn't taken to cover a manually-added mod that was really just skipped.
				if (unchecked_ > 0)
					message += " " + Loc.T(unchecked_ == 1 ? "updates.unlinkedNoteOne" : "updates.unlinkedNote", unchecked_);
				Speak(message);
				// A prompt straight after this interrupts it, and the count and the mods that could not be checked were lost.
				if (pendingSmapi != null || pendingMinecraft != null) LetSpeechFinish();
				if (pendingSmapi is { } s)
					NotifySmapiUpdateAvailable(s.Current, s.Latest);
				// The same courtesy for the one update that is not a mod at all. See
				// NotifyMinecraftUpdateAvailable: a new Minecraft version decides whether every mod still loads.
				if (pendingMinecraft is { } mc)
					NotifyMinecraftUpdateAvailable(mc.Current, mc.Latest);
			});
		}
	}

	/// <summary>
	/// Update-check unit that queries the SMAPI web API (smapi.io) for the supplied Stardew Valley mods and
	/// adds any with a newer suggested version to <c>listUpdates</c>. This is the primary Stardew check: it
	/// catches mods the manifest-only Nexus check misses because their update key is missing or broken. When
	/// the API resolves a mod to a Nexus page, the mod's <see cref="GameMod.NexusID"/> is back-filled so the
	/// existing download/open-page actions work on it. Also surfaces a SMAPI program update if one is offered.
	/// Runs alongside the Nexus group checks and de-dupes against them by reusing the same mod instances.
	/// </summary>
	private async Task CheckUpdatesViaSmapiApi(List<GameMod> installed, string smapiVersion, string gameVersion)
	{
		string game = _settings.ActiveGame;
		try
		{
			var entries = new List<(string Id, string Version, IEnumerable<string> UpdateKeys)>();
			foreach (GameMod mod in installed)
			{
				if (mod.IsGroup || string.IsNullOrEmpty(mod.UniqueId)) continue;
				var keys = new List<string>();
				if (!string.IsNullOrEmpty(mod.NexusID)) keys.Add("Nexus:" + mod.NexusID);
				if (!string.IsNullOrEmpty(mod.GitHubRepo)) keys.Add("GitHub:" + mod.GitHubRepo);
				entries.Add((mod.UniqueId, mod.Version, keys));
			}
			// Include SMAPI itself so the same call reports a SMAPI program update.
			entries.Add(("SMAPI", smapiVersion, new[] { "GitHub:Pathoschild/SMAPI", "Nexus:2400" }));

			var updates = await _nexusService.GetSmapiUpdatesAsync(entries, smapiVersion, gameVersion);
			if (updates == null) return; // Service unreachable; the Nexus fallback check still runs.

			if (updates.TryGetValue("SMAPI", out var smapiUpd) && !string.IsNullOrEmpty(smapiUpd.Version)
				&& IsNewerVersion(smapiVersion, smapiUpd.Version))
			{
				// Defer the prompt until the whole check completes (see CompleteUpdateCheckUnit).
				_pendingSmapiUpdate = (smapiVersion, smapiUpd.Version!);
			}

			var linked = new Dictionary<string, string>();
			var pages = new Dictionary<string, string>();
			foreach (GameMod mod in installed)
			{
				if (mod.IsGroup || string.IsNullOrEmpty(mod.UniqueId)) continue;
				if (!updates.TryGetValue(mod.UniqueId, out var upd)) continue;

				// Remember that this mod really was version-checked, so it isn't counted as "not checked". A mod
				// whose manifest version can't be expressed as a semantic version is sent without one: the
				// service can identify it but not compare it, so that doesn't count as checked.
				string sentVersion = NexusService.SanitizeModVersion(mod.Version);
				if (upd.Known && sentVersion.Length > 0) _smapiCheckedIds.Add(mod.UniqueId);

				// Back-fill the mod's update source from SMAPI's mod database, which maps UniqueID to mod page
				// directly — an authoritative link, unlike guessing from the mod's name. This makes previously
				// "unlinked" mods actionable (download the update, open the page) and is remembered on disk.
				if (string.IsNullOrEmpty(mod.NexusID) && string.IsNullOrEmpty(mod.GitHubRepo))
				{
					if (!string.IsNullOrEmpty(upd.NexusId))
					{
						mod.NexusID = upd.NexusId;
						linked[mod.UniqueId] = upd.NexusId!;
					}
					else if (!string.IsNullOrEmpty(upd.GitHubRepo))
					{
						mod.GitHubRepo = upd.GitHubRepo;
					}
					else if (!string.IsNullOrEmpty(upd.Url))
					{
						// Whichever site smapi.io names. It understands ModDrop, CurseForge and Chucklefish
						// update keys as well as Nexus and GitHub, and this used to recognise two of them and
						// drop the rest — so a mod hosted anywhere else was reported to the user as one the
						// manager could not track, moments after being handed its exact page.
						ModPageLink? link = ModSources.ParsePage(upd.Url);
						switch (link?.SourceId)
						{
							case ModSources.Nexus:
								mod.NexusID = link!.Id;
								linked[mod.UniqueId] = mod.NexusID;
								break;

							case ModSources.GitHub:
								mod.GitHubRepo = link!.Id;
								break;
						}

						// Kept whatever the site, and kept even when one of the two above matched: opening the
						// page is the one thing that works for every site there will ever be.
						mod.PageUrl = upd.Url;
						mod.SourceId = link?.SourceId ?? "";
						pages[mod.UniqueId] = upd.Url!;
					}
				}

				if (string.IsNullOrEmpty(upd.Version)) continue;

				// smapi.io only suggests an update when it is newer than the version we sent, so its verdict is
				// authoritative whenever we sent the mod's exact version. Fall back to the local comparison only
				// when the version had to be coerced to satisfy the API (see NexusService.SanitizeModVersion).
				bool sentExactVersion = sentVersion == (mod.Version ?? "").Trim();
				bool isUpdate = sentExactVersion
					? !string.Equals(upd.Version, (mod.Version ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
					: IsNewerVersion(mod.Version, upd.Version);
				if (!isUpdate) continue;
				// If we know which release of this mod's download is installed, that beats the manifest version:
				// the manifest may simply never have been bumped by its author (see HasPendingUpdate).
				if (UpdateCoverage.HasUpdateLink(mod) &&
					InstalledDownloadVersion(DownloadKey(mod)) is string recorded &&
					!IsNewerVersion(recorded, upd.Version)) continue;

				if (_settings.IgnoredVersions.TryGetValue(mod.UniqueId, out string? ignored) && ignored == upd.Version)
					continue;

				RecordLatestVersion(mod, upd.Version);

				Invoke(delegate
				{
					listUpdates.BeginUpdate();
					if (_settings.ActiveGame == game && !listUpdates.Items.OfType<GameMod>().Any(mod.IsSameModAs))
						listUpdates.Items.Add(mod.AsUpdateRow());
					listUpdates.EndUpdate();
				});
			}

			PersistNexusIdLinks(linked);
			ModPageLinks.Save(AppSettings.AppDataFolder, pages);
		}
		catch (Exception ex)
		{
			LogFailure("SmapiUpdate", "SMAPI update check failed", ex);
			Interlocked.Increment(ref _updateChecksUnanswered);
		}
		finally
		{
			CompleteUpdateCheckUnit();
		}
	}

	/// <summary>
	/// Announces an available SMAPI program update and offers to install it in place automatically. SMAPI is
	/// updated by re-running its installer over the existing install, which is exactly what <see
	/// cref="InstallSmapiAsync"/> does, so "Yes" downloads the latest installer and runs it unattended. SMAPI
	/// is intentionally not added to the updatable mod list because it is the loader, not a mod. Declining
	/// leaves the install untouched; any failure inside the installer falls back to opening smapi.io.
	/// </summary>
	private async void NotifySmapiUpdateAvailable(string current, string latest)
	{
		// A check that found something, not a connection.
		_soundEngine.Play("load_complete");
		if (SpeakBox(
				Loc.T("updates.smapiBox", current, latest),
				Loc.T("updates.smapiTitle"), MessageBoxButtons.YesNo) != DialogResult.Yes)
			return;

		// SMAPI's installer updates an existing install in place, so the same routine used for a fresh
		// install performs the update; it self-reports success/failure by voice and handles its own fallback.
		// The open copy, not the first one detection finds: with two copies of the game, this updated the other one.
		await InstallSmapiAsync(_settings.CurrentGamePath is { Length: > 0 } open ? open : DetectGameFolder("StardewValley"));
	}

	/// <summary>
	/// Best-effort detection of the installed SMAPI and Stardew Valley versions for the smapi.io request.
	/// Reads them from the SMAPI log header when present (it records "SMAPI x.y.z with Stardew Valley a.b.c"),
	/// otherwise falls back to the StardewModdingAPI.dll file version and a current game-version default.
	/// </summary>
	private (string Smapi, string Game) DetectStardewVersions()
	{
		string smapi = "";
		string game = "";
		try
		{
			string logPath = Path.Combine(UserFolders.ApplicationData,
				"StardewValley", "ErrorLogs", "SMAPI-latest.txt");
			if (File.Exists(logPath))
			{
				foreach (string line in File.ReadLines(logPath))
				{
					var m = Regex.Match(line, @"SMAPI\s+(\d+\.\d+\.\d+)\s+with\s+Stardew Valley\s+(\d+\.\d+(?:\.\d+)?)",
						RegexOptions.IgnoreCase);
					if (m.Success) { smapi = m.Groups[1].Value; game = m.Groups[2].Value; break; }
				}
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Updates", "reading the record of installed download versions", ex); }

		if (string.IsNullOrEmpty(smapi))
		{
			try
			{
				string gameFolder = Path.GetDirectoryName(_settings.CurrentModsPath) ?? "";
				string dll = Path.Combine(gameFolder, "StardewModdingAPI.dll");
				if (File.Exists(dll))
				{
					var fv = System.Diagnostics.FileVersionInfo.GetVersionInfo(dll);
					if (!string.IsNullOrEmpty(fv.FileVersion))
						smapi = fv.FileVersion!.Split('+', ' ')[0];
				}
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Updates", "saving the record of installed download versions", ex); }
		}

		if (string.IsNullOrEmpty(smapi)) smapi = "4.0.0";
		if (string.IsNullOrEmpty(game)) game = "1.6.15";
		return (smapi, game);
	}

	/// <summary>
	/// Downloads the latest file for <paramref name="mod"/> from Nexus Mods or GitHub and installs it
	/// via <see cref="InstallFromZip"/>. Pass <paramref name="silent"/> as <c>true</c> to suppress
	/// per-mod spoken feedback during a batch update.
	/// </summary>
    private async Task DownloadAndInstallUpdate(GameMod mod, bool silent = false)
    {
        // Enter on a row during Update All downloaded the same file twice at once, and one of them failed.
        if (!silent && ModOperationRunning)
        {
            Speak(Loc.T("session.busy"));
            return;
        }
        using IDisposable busy = BeginModOperation();

        void NoteFailure() { if (silent) _updatesThatFailed.Add(mod.Name); }

        // The row is a copy taken when the check ran (see GameMod.AsUpdateRow), and the mod may have moved since: switching
        // a Minecraft mod off renames its jar, so the update looked for a file that had gone and said no build existed for
        // the Minecraft version in use. Where it is now and whether it is on come from the installed list. Measured 2026-11-10.
        if (!mod.IsGroup && _allInstalledMods.FirstOrDefault(m => !m.IsGroup && m.UniqueId == mod.UniqueId) is { } installed)
        {
            mod.FolderPath = installed.FolderPath;
            mod.IsEnabled = installed.IsEnabled;
        }

        // Updates install the mod enabled; remember if it was disabled so we can turn it back off afterward.
        bool wasDisabled = !mod.IsEnabled;

        // Minecraft's mods come from Modrinth and from their authors' own GitHub releases, and every one of them is
        // a single jar. Neither the Nexus branch below nor the GitHub one can install that: the first wants a
        // Premium account for a site that hosts none of these mods, and the second unpacks the jar. See
        // UpdateMinecraftModAsync.
        if (GameProfiles.Find(_settings.ActiveGame)?.IsMinecraft == true)
        {
            // The two rows that are not mods: the loader that runs them, and the Minecraft version they are built
            // for. See CheckMinecraftPlatformUpdatesAsync.
            if (mod.UniqueId == FabricLoaderRowId)
            {
                if (await UpdateFabricLoaderAsync()) { RemoveUpdateRowsFor(mod); if (!silent) AnnounceUpdatesListEmptyIfFocused(); }
                else NoteFailure();
                return;
            }
            // The loaded modpack's own newer version. See CheckModpackUpdateAsync.
            if (mod.UniqueId == ModpackRowId)
            {
                if (ActiveMinecraftPack() is { } pack && _packUpdates.TryGetValue(pack.Folder, out ModpackVersion? packUpdate))
                    await UpdateModpackAsync(pack, packUpdate);
                return;
            }
            if (mod.UniqueId == MinecraftVersionRowId)
            {
                if (await MoveToMinecraftVersionAsync(mod.LatestVersion ?? "", silent))
                {
                    // Every mod's row is stale now: they were checked against the version that has just been left.
                    Invoke(delegate { listUpdates.Items.Clear(); });
                    if (!silent) AnnounceUpdatesListEmptyIfFocused();
                }
                return;
            }

            try
            {
                // One of a modpack's own mods: said, and asked, before anything changes. Update All asks its own
                // question about the whole set, so a silent run is not interrupted here. See CameWithPack.
                if (!silent && CameWithPack(mod) && ActiveMinecraftPack() is { } pack &&
                    SpeakBox(Loc.T("mc.pack.modUpdate.confirm", mod.Name, pack.Name), Loc.T("mc.pack.modUpdate.title"),
                        MessageBoxButtons.YesNo) != DialogResult.Yes)
                {
                    Speak(Loc.T("common.changesCancelled"));
                    return;
                }

                if (!await UpdateMinecraftModAsync(mod, silent))
                {
                    _soundEngine.Play("error");
                    NoteFailure();
                    if (!silent) SpeakBox(Loc.T("updates.failBox", mod.Name, Loc.T("updates.noMinecraftBuild", mod.Name)));
                    ResetStatus();
                    return;
                }

                RemoveUpdateRowsFor(mod);
                await RefreshModList(checkUpdates: false);
                bool saidMinecraft = await ReapplyDisabledIfNeeded(mod, wasDisabled, silent);

                if (!silent)
                {
                    _soundEngine.Play("load_complete");
                    if (!saidMinecraft) Speak(Loc.T("updates.success", mod.Name));
                    ResetStatus();
                    AnnounceUpdatesListEmptyIfFocused();
                }
                else ResetStatus();
            }
            catch (DownloadCancelledException)
            {
                // Asked for, and already said. The row stays, to be tried again.
                ResetStatus();
            }
            catch (Exception ex)
            {
                _soundEngine.Play("error");
                LogFailure(mod.Name, "Download/Install Failure", ex);
                NoteFailure();
                ResetStatus();
                if (!silent) Invoke(delegate { SpeakBox(Loc.T("updates.failBox", mod.Name, FriendlyError(ex))); });
            }
            return;
        }

        if (!string.IsNullOrEmpty(mod.GitHubRepo))
        {
            try
            {
                SetStatus(Loc.T("updates.updatingGitHub", mod.Name), speak: false);
                if (!silent) Speak(Loc.T("updates.downloading", mod.Name));

                string? downloadUrl = await GetGitHubLatestReleaseZipUrl(mod.GitHubRepo);
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    throw new Exception("Could not retrieve latest release ZIP from GitHub.");
                }

                string destinationPath = Path.Combine(downloadsPath, $"{mod.UniqueId}_github_latest.zip");

                // Progress runs during a batch (Update All) too — it follows the user's tones/speech/both/off
                // setting through ProgressAnnouncer, so "off" stays silent. Only the per-mod spoken chatter and
                // success message box are suppressed by the silent flag; the progress feedback is not.
                ProgressAnnouncer? dlProgress = NewProgress(mod.Name, installing: false);
                await _nexusService.DownloadFileWithProgressAsync(downloadUrl, destinationPath, dlProgress);
                dlProgress?.Complete();

                ProgressAnnouncer? instProgress = NewProgress(mod.Name, installing: true);
                string name = await ExtractModOffTheWindowAsync(destinationPath, mod.NexusID, mod.GitHubRepo,
                    fomodWizard: false, instProgress, updating: mod);
                instProgress?.Complete();
                RecordDownloadInstalled(destinationPath);

                Invoke(delegate
                {
                    listUpdates.BeginUpdate();
                    for (int i = listUpdates.Items.Count - 1; i >= 0; i--)
                    {
                        if (listUpdates.Items[i] is GameMod m &&
                            !string.IsNullOrEmpty(m.GitHubRepo) &&
                            m.GitHubRepo.Equals(mod.GitHubRepo, StringComparison.OrdinalIgnoreCase))
                            listUpdates.Items.RemoveAt(i);
                    }
                    listUpdates.EndUpdate();
                });

                await RefreshModList(checkUpdates: false);

                // Reconcile assets after the rescan; forceRelink replaces the old version's hard links,
                // which would otherwise still point at the now-deleted previous file data.
                if (IsBethesdaGame)
                {
                    SyncBethesdaDeployment(new HashSet<string>(new[] { name }, StringComparer.OrdinalIgnoreCase));
                    Invoke(delegate { RefreshModPriorityList(); });
                }

                bool saidGitHub = await ReapplyDisabledIfNeeded(mod, wasDisabled, silent);
                // Remember which release is now on disk, so the next check compares against what was installed

                // rather than whatever version numbers the mods inside the download happen to declare.

                RecordInstalledDownloadVersion(DownloadKey(mod), mod.LatestVersion);

                await SyncManifestVersionAfterUpdate(mod, mod.LatestVersion);

                if (!silent)
                {
                    _soundEngine.Play("load_complete");
                    if (!saidGitHub) Speak(Loc.T("updates.githubSuccess", mod.Name));
                    // ResetStatus, not SetStatus: the title only needs to stop showing "Installing...", and SetStatus speaks by
                    // default, so this said "Connected as ..." straight after the update was announced. ResetStatus also asks
                    // RestingStatus what the title should be, which answers "Ready" when there is no known user -- the hardcoded
                    // line could put "Connected as Unknown User" up instead.
                    ResetStatus();
                    AnnounceUpdatesListEmptyIfFocused();
                }
                return;
            }
            catch (DownloadCancelledException)
            {
                // Asked for, and already said. The row stays, to be tried again.
                ResetStatus();
                return;
            }
            catch (Exception ex)
            {
                _soundEngine.Play("error");
                LogFailure(mod.Name, "GitHub Download/Install Failure", ex);
                NoteFailure();
                ResetStatus();
                if (!silent) SpeakBox(Loc.T("updates.githubFailBox", mod.Name, FriendlyError(ex)));
                return;
            }
        }

        if (!_nexusService.IsPremium)
        {
            if (!silent) OpenModPage();
            return;
        }
        try
        {
            SetStatus(Loc.T("updates.updating", mod.Name), speak: false);
            if (!silent) Speak(Loc.T("updates.downloading", mod.Name));

            // Download progress runs in a batch too (honouring the tones/speech/both/off setting); the silent
            // flag only mutes the per-mod chatter and success box, not the progress feedback itself.
            ProgressAnnouncer? progress = NewProgress(mod.Name, installing: false);
            string tempPath = await _nexusService.DownloadModUpdateAsync(mod, downloadsPath, progress);
            progress?.Complete();
            // Nothing below may run unless it really installed: it takes the row away, records the new version as the
            // one on disk and says "updated". After a failure that left the old version in place, believed to be new,
            // and the update was never offered again.
            if (!await InstallFromZip(tempPath, mod.NexusID, silent: silent, updating: mod))
            {
                NoteFailure();
                return;
            }
            Invoke(delegate
            {
                listUpdates.BeginUpdate();
                for (int i = listUpdates.Items.Count - 1; i >= 0; i--)
                {
                    if (listUpdates.Items[i] is GameMod m &&
                        !string.IsNullOrEmpty(m.NexusID) &&
                        m.NexusID.Equals(mod.NexusID, StringComparison.OrdinalIgnoreCase))
                        listUpdates.Items.RemoveAt(i);
                }
                listUpdates.EndUpdate();
            });
            // No rescan here: InstallFromZip has just done one, and each costs seconds on a large setup.
            bool saidNexus = await ReapplyDisabledIfNeeded(mod, wasDisabled, silent);
            // Remember which release is now on disk, so the next check compares against what was installed

            // rather than whatever version numbers the mods inside the download happen to declare.

            RecordInstalledDownloadVersion(DownloadKey(mod), mod.LatestVersion);

            await SyncManifestVersionAfterUpdate(mod, mod.LatestVersion);
            if (!silent)
            {
                _soundEngine.Play("load_complete");
                if (!saidNexus) Speak(Loc.T("updates.success", mod.Name));
                // ResetStatus, not SetStatus: the title only needs to stop showing "Installing...", and SetStatus speaks by
                // default, so this said "Connected as ..." straight after the update was announced. ResetStatus also asks
                // RestingStatus what the title should be, which answers "Ready" when there is no known user -- the hardcoded
                // line could put "Connected as Unknown User" up instead.
                ResetStatus();
                AnnounceUpdatesListEmptyIfFocused();
            }
        }
        catch (DownloadCancelledException)
        {
            // Asked for, and already said, so it is neither a failure nor worth a box. The row stays, to be tried again.
            ResetStatus();
        }
        catch (Exception ex)
        {
            _soundEngine.Play("error");
            LogFailure(mod.Name, "Download/Install Failure", ex);
            NoteFailure();
            if (!silent) Invoke(delegate { SpeakBox(Loc.T("updates.failBox", mod.Name, FriendlyError(ex))); });
        }
    }

	/// <summary>
	/// Enter on an update the manager can fetch itself: asks whether to install it or to open its page, the same
	/// two-way choice a search result offers.
	///
	/// <para>
	/// Enter used to open the page for every row, which was the right answer while every update came from Nexus and
	/// needed a browser session to fetch. It is the wrong one for a mod from Modrinth or a GitHub release, where the
	/// manager can simply do it — and reading about a mod you already have is the rarer of the two wants, not the
	/// only one.
	/// </para>
	/// </summary>
	private async Task OfferUpdateChoiceAsync(GameMod mod)
	{
		string[] actions = { Loc.T("updates.choiceUpdate"), Loc.T("updates.choicePage") };

		string? chosen = ShowChoiceList(
			Loc.T("updates.choiceTitle", mod.Name),
			Loc.T("updates.choiceListName"),
			actions,
			actions[0],
			Loc.T("updates.choiceHint", mod.Name, mod.Version, mod.LatestVersion ?? ""));

		if (chosen is null) { Speak(Loc.T("common.changesCancelled")); return; }

		if (chosen == actions[1]) { OpenModPage(); return; }

		await DownloadAndInstallUpdate(mod);
	}

	/// <summary>
	/// Takes a mod's row out of the Updates list once its update has gone in. Matched on the mod's own id, which is
	/// what identifies a Minecraft mod — it has neither a Nexus id nor, usually, a GitHub repository.
	/// </summary>
	private void RemoveUpdateRowsFor(GameMod mod)
	{
		Invoke(delegate
		{
			listUpdates.BeginUpdate();
			for (int i = listUpdates.Items.Count - 1; i >= 0; i--)
				if (listUpdates.Items[i] is GameMod m && !string.IsNullOrEmpty(m.UniqueId) &&
					m.UniqueId.Equals(mod.UniqueId, StringComparison.OrdinalIgnoreCase))
					listUpdates.Items.RemoveAt(i);
			listUpdates.EndUpdate();
		});
	}

	/// <summary>
	/// Announces "List is empty" after the last available update has been installed and removed in place. A screen
	/// reader only re-reads a list's state on a focus change, so emptying it programmatically would otherwise stay
	/// silent. We put focus on the now-empty Updates list (so the reader speaks its name) and then add the empty
	/// status, but only while the Updates tab is showing — we never yank focus if the user has moved on.
	/// </summary>
	private void AnnounceUpdatesListEmptyIfFocused()
	{
		if (IsLoading || listUpdates.Items.Count != 0 || CurrentTab() != AppTab.Updates)
			return;
		// Focusing fires GotFocus -> List_Enter -> AnnounceListEmpty; if it is already focused that path does not
		// fire, so call the announcer directly too. AnnounceListEmpty de-dupes, so this never doubles up.
		if (!listUpdates.Focused)
			listUpdates.Focus();
		AnnounceListEmpty(listUpdates);
	}

	/// <summary>
	/// Brings a freshly updated mod's manifest version in line with the release that was installed, for the
	/// simple case: one mod, one download. Some authors ship an update without bumping <c>Version</c> in the
	/// manifest, so the mod keeps reporting the old number; stamping the release's version into it keeps what
	/// the mod reports (in SMAPI's log, say) honest.
	///
	/// Deliberately skipped when several installed mods share the download. Their versions are the authors'
	/// own numbers for each mod, not the release's, and installing the download rewrites every one of those
	/// folders — so stamping one would be undone the next time any of them updated, while the sibling it just
	/// overwrote started asking to be updated in its place. That ping-pong is why the release's version is
	/// recorded separately (see <see cref="RecordInstalledDownloadVersion"/>), which settles the comparison
	/// without editing anyone's manifest.
	/// </summary>
	private async Task SyncManifestVersionAfterUpdate(GameMod original, string? installedVersion)
	{
		if (string.IsNullOrEmpty(installedVersion)) return;
		// ⚠️ Never on Stardew. There the file is the author's manifest.json, and a Nexus page's version is free text: "2.0.3.5"
		// or "1.4 beta" written into it makes SMAPI refuse the manifest and skip the mod — for an accessibility mod, a silent
		// game. See MarkSelectedUpdateAsInstalled. RecordInstalledDownloadVersion, which runs before this, already settles the
		// comparison, so the Updates tab still lets go of the row.
		if (GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley)) return;
		if (UpdateCoverage.HasUpdateLink(original))
		{
			string key = DownloadKey(original);
			int sharing = _allInstalledMods.Count(m => !m.IsGroup && UpdateCoverage.HasUpdateLink(m) && DownloadKey(m) == key);
			if (sharing > 1) return;
		}
		try
		{
			GameMod? updated = _allInstalledMods.FirstOrDefault(m => !m.IsGroup
					&& !string.IsNullOrEmpty(original.UniqueId) && m.UniqueId == original.UniqueId)
				?? _allInstalledMods.FirstOrDefault(m => !m.IsGroup &&
					((!string.IsNullOrEmpty(original.NexusID) && original.NexusID.Equals(m.NexusID, StringComparison.OrdinalIgnoreCase)) ||
					 (!string.IsNullOrEmpty(original.GitHubRepo) && original.GitHubRepo.Equals(m.GitHubRepo, StringComparison.OrdinalIgnoreCase))));

			if (updated == null || !Directory.Exists(updated.FolderPath)) return;
			if (!IsNewerVersion(updated.Version, installedVersion)) return;   // already at or beyond the new version

			string manifestPath = Path.Combine(updated.FolderPath, ".manager_manifest.json");
			if (!File.Exists(manifestPath)) return;

			JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
			manifest["Version"] = installedVersion;
			AtomicFile.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
			updated.Version = installedVersion;
			LogError(updated.Name, $"Manifest version corrected to the installed version {installedVersion}.");

			// Re-scan so the installed list reads the new version and the Updates tab drops the stale row.
			await RefreshModList(checkUpdates: false);
		}
		catch (Exception ex) { LogFailure(original.Name, "Could not update the manifest version", ex); }
	}

	/// <summary>
	/// Mods left switched off by an Update All, so the batch can say so once at the end rather than asking about
	/// each one in the middle of a run the user started precisely to avoid answering things.
	/// </summary>
	private readonly List<string> _updatesLeftDisabled = new();

	/// <summary>
	/// Puts a mod back the way the user had it after an update, and — outside a batch — asks whether they would
	/// rather have it on now.
	///
	/// <para>
	/// An update installs the mod switched on, whatever it was before, so without this a mod deliberately parked
	/// comes back to life behind the user's back. Off stays the answer unless they say otherwise, because that is
	/// the state they chose; but a mod is often parked <em>because</em> it was broken, and the update may be
	/// exactly what fixes it, so the question is worth asking once.
	/// </para>
	///
	/// <para>
	/// ⚠️ Matched on the Nexus id, the GitHub repository AND the mod's own id. The first two are all it used to
	/// look at, so for Minecraft — where a mod has neither — nothing matched and every disabled mod silently came
	/// back switched on.
	/// </para>
	/// </summary>
	/// <returns>
	/// True when it has said the result itself, which names the update: the caller then says nothing more. "Disrobe Sounds
	/// updated, and left switched off as it was. Disrobe Sounds updated successfully." was heard. Measured 2026-11-10.
	/// </returns>
	private async Task<bool> ReapplyDisabledIfNeeded(GameMod original, bool wasDisabled, bool silent = false)
	{
		if (!wasDisabled) return false;

		GameMod? updated = _allInstalledMods.FirstOrDefault(m => !m.IsGroup && m.IsEnabled && ModHealth.IsSameMod(original, m));
		if (updated == null || !updated.IsEnabled) return false;

		// In a batch nothing is asked: the run is unattended by design, and the state the user chose is kept. The
		// end of the run says which mods that happened to.
		if (!silent && SpeakBox(Loc.T("updates.enableAfterUpdate", updated.Name, updated.Version),
				Loc.T("updates.enableAfterUpdateTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
		{
			_soundEngine.Play("enable");
			Speak(Loc.T("updates.leftEnabled", updated.Name));
			return true;
		}

		// Its folder is the mods folder, so switching it off would take every mod with it. See RefusedAsTheWholeModsFolder.
		if (IsTheWholeModsFolder(updated)) return false;

		try
		{
			updated.FolderPath = ModFileSystem.SetModEnabled(updated.FolderPath, false, _settings.ActiveGame,
				locatedGameFolder: _settings.CurrentGamePath);
			updated.IsEnabled = false;
			_soundEngine.Play("disable");
			if (silent)
			{
				_updatesLeftDisabled.Add(updated.Name);
				await RefreshModList(checkUpdates: false);
			}
			else
			{
				await SayAndRefreshModList(Loc.T("updates.leftDisabled", updated.Name), updated);
				return true;
			}
		}
		catch (Exception ex)
		{
			LogFailure(original.Name, "Could not re-disable after update", ex);
		}
		return false;
	}

	private async Task<string?> GetGitHubLatestReleaseVersionAsync(string repo)
	{
		await _gitHubLimit.WaitAsync();
		try
		{
			if (_gitHubPause.IsActive(DateTime.UtcNow)) return null;

			using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
			req.Headers.UserAgent.ParseAdd($"KinetixModManager/{NexusService.AppVersion}");
			using var resp = await NexusService.HttpClient.SendAsync(req);

			NoteGitHubLimit(resp);
			if (!resp.IsSuccessStatusCode) return null;

			JObject json = JObject.Parse(await resp.Content.ReadAsStringAsync());
			string? tag = json["tag_name"]?.ToString();
			if (tag == null) return null;
			return tag.StartsWith("v") ? tag.Substring(1) : tag;
		}
		catch { return null; }
		finally { _gitHubLimit.Release(); }
	}

	/// <summary>The newest build for the Minecraft version in use, not simply the newest. Null when there is none or GitHub didn't answer, which is counted.</summary>
	private async Task<string?> LatestGitHubBuildForMinecraftAsync(string repo)
	{
		await _gitHubLimit.WaitAsync();
		try
		{
			if (_gitHubPause.IsActive(DateTime.UtcNow))
			{
				Interlocked.Increment(ref _updateChecksUnanswered);
				return null;
			}

			GitHubRelease? release = await GitHubReleaseForAsync(repo, MinecraftGameVersionInUse(MinecraftRootFolder()), mustNameIt: false);
			if (release == null) return null;
			return release.TagName.StartsWith('v') ? release.TagName[1..] : release.TagName;
		}
		catch (Exception ex)
		{
			Interlocked.Increment(ref _updateChecksUnanswered);
			DiagnosticLog.WriteException("GitHub", $"checking {repo} for updates", ex);
			return null;
		}
		finally { _gitHubLimit.Release(); }
	}

	/// <summary>
	/// GitHub says it is over the limit with a 403 as well as a 429, and its remaining-count header tells them apart from a
	/// repository that refuses access.
	/// </summary>
	private void NoteGitHubLimit(HttpResponseMessage resp)
	{
		bool outOfRequests = RequestPause.IsTooMany(resp)
			|| (resp.StatusCode == System.Net.HttpStatusCode.Forbidden
				&& resp.Headers.TryGetValues("x-ratelimit-remaining", out var left) && left.FirstOrDefault() == "0");
		if (!outOfRequests) return;

		DateTime now = DateTime.UtcNow;
		_gitHubPause.For(RequestPause.LengthFrom(resp, "x-ratelimit-reset", now), now);
	}

	/// <summary>GitHub allows 60 requests an hour without an account, so its checks go a few at a time and stop when told.</summary>
	private readonly SemaphoreSlim _gitHubLimit = new(3);
	private readonly RequestPause _gitHubPause = new();

	/// <summary>
	/// The version a Nexus page is known to be at without asking, when its list of this month's changes doesn't name the mod.
	/// </summary>
	private async Task<string?> RememberedNexusVersion(string game, string nexusId, Task<HashSet<int>?>? updatedThisMonth)
	{
		if (updatedThisMonth == null) return null;
		HashSet<int>? updated = await updatedThisMonth;
		NexusCheckRecord? record = _settings.NexusChecks.TryGetValue(game, out var forGame)
			&& forGame.TryGetValue(nexusId, out NexusCheckRecord? found) ? found : null;
		return NexusCheckMemory.RememberedLatest(record, nexusId, updated, DateTime.UtcNow);
	}

	private void RememberNexusCheck(string game, string nexusId, string latest)
	{
		if (!_settings.NexusChecks.TryGetValue(game, out var forGame))
			_settings.NexusChecks[game] = forGame = new Dictionary<string, NexusCheckRecord>();
		forGame[nexusId] = new NexusCheckRecord { Latest = latest, CheckedUtc = DateTime.UtcNow };
	}

	/// <summary>
	/// Records mod UniqueID to Nexus ID links in the manager's own <c>mod_id_map.json</c>, which every later
	/// scan consults (see <see cref="RefreshModList"/>). The map is used rather than the mod's manifest.json so
	/// a link discovered by the manager never rewrites a file the mod author ships.
	/// </summary>
	private void PersistNexusIdLinks(IReadOnlyDictionary<string, string> links)
	{
		if (links.Count == 0) return;
		try
		{
			string mapPath = Path.Combine(AppSettings.AppDataFolder, "mod_id_map.json");
			JObject map = AtomicFile.ReadJsonObjectOrEmpty(mapPath);
			foreach (var kv in links) map[kv.Key] = kv.Value;
			AtomicFile.WriteAllText(mapPath, map.ToString(Formatting.Indented));
		}
		catch (Exception ex) { LogFailure("ModIdMap", "Failed to persist Nexus ID mappings", ex); }
	}

	/// <summary>
	/// Links unmatched Stardew Valley mods to their Nexus page (or GitHub repo) using SMAPI's mod database,
	/// which resolves a mod by its UniqueID. Links found this way are exact, so they replace the name-search
	/// guesswork for every mod the database knows. Returns how many mods were linked; a no-op for other games.
	/// </summary>
	private async Task<int> MatchStardewIdsViaSmapiAsync(List<GameMod> targetMods)
	{
		if (!GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley) || targetMods.Count == 0) return 0;

		var (smapiVer, gameVer) = DetectStardewVersions();
		var entries = targetMods
			.Where(m => !string.IsNullOrEmpty(m.UniqueId))
			.Select(m => (m.UniqueId, m.Version, (IEnumerable<string>)Array.Empty<string>()));

		var info = await _nexusService.GetSmapiUpdatesAsync(entries, smapiVer, gameVer);
		if (info == null) return 0;

		int matched = 0;
		var links = new Dictionary<string, string>();
		foreach (GameMod mod in targetMods)
		{
			if (!info.TryGetValue(mod.UniqueId, out var entry)) continue;
			if (!string.IsNullOrEmpty(entry.NexusId))
			{
				mod.NexusID = entry.NexusId;
				links[mod.UniqueId] = entry.NexusId!;
			}
			else if (!string.IsNullOrEmpty(entry.GitHubRepo))
			{
				mod.GitHubRepo = entry.GitHubRepo;
			}
			else continue;

			matched++;
		}
		PersistNexusIdLinks(links);
		// One summary rather than a line per mod: this phase links dozens of mods in a couple of seconds, so
		// per-mod speech would be a wall of chatter before the slower name search even starts.
		if (matched > 0) Speak(Loc.T(matched == 1 ? "updates.smapiLinkedOne" : "updates.smapiLinked", matched));
		return matched;
	}

	/// <summary>
	/// True when a mod is linked to a Nexus page but the manager still has no real author or description for it
	/// — the state every mod is in that the manager did not install itself, because nothing on disk records
	/// either. The placeholder texts here are the ones the scanners write when they have nothing better.
	/// </summary>
	private static bool NeedsNexusDetails(GameMod mod) => ModVersions.NeedsDetails(mod);

	/// <summary>
	/// Fetches the author and summary for every linked mod that is still missing them, and returns how many were
	/// filled in. The installed version is never touched — see <see cref="EnrichLinkedModFromNexusAsync"/>.
	/// </summary>
	private async Task<int> FillInMissingNexusDetailsAsync()
	{
		List<GameMod> needing = _allInstalledMods
			.Where(m => !m.IsGroup && !string.IsNullOrEmpty(m.NexusID) && NeedsNexusDetails(m))
			.ToList();
		if (needing.Count == 0) return 0;

		int filled = 0;
		for (int i = 0; i < needing.Count; i++)
		{
			GameMod mod = needing[i];
			SetStatus(Loc.T("updates.fillingDetails", i + 1, needing.Count, mod.Name), speak: false);

			string before = mod.Author + "" + mod.Description;
			await EnrichLinkedModFromNexusAsync(mod, mod.NexusID!);
			if (before != mod.Author + "" + mod.Description) filled++;

			await Task.Delay(250);
		}
		return filled;
	}

	private async Task AutoMatchNexusIDs()
	{
		// Twice from the menu was two runs talking over each other, each spending the same requests.
		if (Interlocked.Exchange(ref _autoMatchRunning, 1) == 1)
		{
			Speak(Loc.T("updates.autoMatchAlready"));
			return;
		}

		int matchCount = 0;
		int totalMods = 0;
		LoadingPulse pulse = StartLoadingPulse();
		Speak(Loc.T("updates.autoMatchStart"));

		try
		{
			var targetMods = _allInstalledMods.Where(m => !m.IsGroup && string.IsNullOrEmpty(m.NexusID) && string.IsNullOrEmpty(m.GitHubRepo)).ToList();
			totalMods = targetMods.Count;

			if (totalMods == 0)
			{
				Speak(Loc.T("updates.noUnmatched"));
				pulse.End();
				_soundEngine.Play("load_complete");
				return;
			}

			// Exact sources first, guesswork last. The archives in the downloads folder record which Nexus page
			// each mod actually came from, and SMAPI's mod database maps a mod's UniqueID straight to its page;
			// only what neither knows falls through to the name search below.
			matchCount += RecoverNexusIdsFromDownloads();
			targetMods = targetMods.Where(m => !UpdateCoverage.HasUpdateLink(m)).ToList();
			matchCount += await MatchStardewIdsViaSmapiAsync(targetMods);
			targetMods = targetMods.Where(m => !UpdateCoverage.HasUpdateLink(m)).ToList();

			int current = 0;
			foreach (var mod in targetMods)
			{
				current++;
				// Three sentences a mod, queued, put this minutes behind what it was really doing on a large list. The title keeps
				// count, and what it found is said only when the reader has finished with the last one.
				SetStatus(Loc.T("updates.matchingStatus", current, targetMods.Count, mod.Name), speak: false);

				// A mod goes by more than one name: what the mod itself declares, the folder it was installed
				// into, and (for a BepInEx plugin) the parts of its GUID. Any of them can be the one its Nexus
				// page is titled with, so each is searched in turn until one produces a confident match. The
				// language filter is deliberately not applied — most authors leave that field blank, and
				// filtering here would hide the very page we are trying to find.
				GameMod? bestMatch = null;
				bool anyResults = false;
				foreach (string alias in ModNameMatch.SearchAliases(mod))
				{
					var (results, _) = await SearchActiveGameCatalogueAsync("Search", alias, 1, 10);
					if (results.Count == 0) continue;
					anyResults = true;

					bestMatch = results.FirstOrDefault(r => ModNameMatch.IsConfident(mod, r));
					if (bestMatch != null) break;

					await Task.Delay(250);
				}

				if (anyResults)
				{
					if (bestMatch != null)
					{
						mod.NexusID = bestMatch.NexusID;

						string manifestPath = ManifestPathFor(mod);

						if (File.Exists(manifestPath))
						{
							JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
							if (GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley))
							{
								manifest["UpdateKeys"] = new JArray($"Nexus:{bestMatch.NexusID}");
							}
							else
							{
								manifest["NexusID"] = bestMatch.NexusID;
							}
							AtomicFile.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
						}

						// A mod the manager had to identify by name usually has no author and no real summary
						// recorded — nothing local ever knew them. Now that its page is known, fetch them. The
						// installed version is deliberately left alone; see EnrichLinkedModFromNexusAsync.
						await EnrichLinkedModFromNexusAsync(mod, bestMatch.NexusID!);

						matchCount++;
						SpeakIfReaderIsFree(Loc.T("updates.matchedWith", bestMatch.Name));
					}
					else
					{
						SpeakIfReaderIsFree(Loc.T("updates.noConfidentMatch"));
					}
				}
				else
				{
					SpeakIfReaderIsFree(Loc.T("updates.noResults"));
				}

				await Task.Delay(500);
			}

			// Mods linked on an earlier run, or by an install, can still be missing the things only the mod page
			// knows — who wrote it, what it actually does. Fill those in as well, so auto-match leaves every
			// linked mod complete rather than only the ones it matched just now. Only mods that are actually
			// missing something are fetched, so running it again costs nothing.
			int detailed = await FillInMissingNexusDetailsAsync();

			pulse.End();
			// The rescan first, so the list reloading cannot cut off the one sentence that matters.
			await RefreshModList(checkUpdates: false);
			_soundEngine.Play("load_complete");
			Speak(Loc.T("updates.autoMatchComplete", matchCount, totalMods));
			if (detailed > 0)
				Speak(Loc.T(detailed == 1 ? "updates.detailsFilledOne" : "updates.detailsFilled", detailed));
		}
		catch (Exception ex)
		{
			pulse.End();
			_soundEngine.Play("error");
			LogFailure("AutoMatch", "Auto-match failed", ex);
			SpeakBox(Loc.T("updates.autoMatchFailBox", FriendlyError(ex)), Loc.T("common.error"));
		}
		finally
		{
			Volatile.Write(ref _autoMatchRunning, 0);
		}
	}

	private int _autoMatchRunning;

    private async Task CheckForAppUpdates(bool manual)
	{
		// Its installer would go over the real copy.
		if (TestMode.IsOn)
		{
			if (manual) Speak(Loc.T("test.noSelfUpdate"));
			return;
		}

		if (manual) Speak(Loc.T("updates.checkingManager"));
		try
		{
			NexusService.AppReleaseInfo? release = await _nexusService.GetLatestAppReleaseAsync();
			if (release == null) { if (manual) { Speak(Loc.T("updates.checkFailed")); } return; }
			string tag = release.TagName;
			string target = tag.StartsWith("v") ? tag.Substring(1) : tag;
			if (IsNewerVersion(NexusService.AppVersion, target))
			{
				// A check that found something, not a connection.
				_soundEngine.Play("load_complete");
				if (!string.IsNullOrEmpty(release.DownloadUrl))
				{
					if (SpeakBox(Loc.T("updates.versionAvailDownload", tag),
							Loc.T("updates.updateAvailTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
					{
						await DownloadAndInstallAppUpdateAsync(release);
					}
				}
				else
				{
					if (SpeakBox(Loc.T("updates.versionAvailNoInstaller", tag),
							Loc.T("updates.updateAvailTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
					{
						Process.Start(new ProcessStartInfo(
							"https://github.com/SeanTerry01/Kinetix-Mod-Manager/releases/latest")
						{ UseShellExecute = true });
					}
				}
			}
			else if (manual)
			{
				SpeakBox(Loc.T("updates.upToDateBox"), Loc.T("updates.upToDateTitle"));
			}
		}
		catch (Exception ex)
		{
			if (manual) SpeakBox(Loc.T("updates.checkFailBox", FriendlyError(ex)));
		}
	}

	private async Task DownloadAndInstallAppUpdateAsync(NexusService.AppReleaseInfo release)
	{
		string originalTitle = Text;
		LoadingPulse pulse = StartLoadingPulse();
		try
		{
			Speak(Loc.T("updates.downloadingUpdate"));

			if (!Directory.Exists(downloadsPath))
			{
				Directory.CreateDirectory(downloadsPath);
			}

			string destinationPath = Path.Combine(downloadsPath, release.FileName);

			ProgressAnnouncer progress = NewProgress(Loc.T("updates.appUpdateName"), installing: false);
			await _nexusService.DownloadFileWithProgressAsync(release.DownloadUrl, destinationPath, progress, release.Digest);
			progress.Complete();

			pulse.End();
			Speak(Loc.T("updates.downloadComplete"));

			string installerPath = destinationPath;

			// If it's a zip file, extract it
			if (release.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
			{
				string tempExtractDir = Path.Combine(downloadsPath, "KMM_Update_Extracted");
				if (Directory.Exists(tempExtractDir))
				{
					Directory.Delete(tempExtractDir, true);
				}
				Directory.CreateDirectory(tempExtractDir);

				System.IO.Compression.ZipFile.ExtractToDirectory(destinationPath, tempExtractDir);

				string[] exeFiles = Directory.GetFiles(tempExtractDir, "*.exe", SearchOption.AllDirectories);
				if (exeFiles.Length == 0)
				{
					throw new FileNotFoundException("Could not find any executable installer inside the downloaded zip archive.");
				}
				installerPath = exeFiles[0];
			}

			if (!File.Exists(installerPath))
			{
				throw new FileNotFoundException("Installer executable not found.");
			}

			SpeakBox(Loc.T("updates.installerBox"), Loc.T("updates.installingTitle"));

			Process.Start(new ProcessStartInfo(installerPath)
			{
				UseShellExecute = true
			});

			Application.Exit();
		}
		catch (Exception ex)
		{
			pulse.End();
			Text = originalTitle;
			LogFailure("AppUpdate", "Self-update failed", ex);
			SpeakBox(Loc.T("updates.selfUpdateFailBox", FriendlyError(ex)), Loc.T("updates.updateErrorTitle"));
		}
	}
}
