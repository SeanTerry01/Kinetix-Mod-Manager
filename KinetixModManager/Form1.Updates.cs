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
	private void RecordLatestVersion(StardewMod mod, string? candidate) =>
		mod.LatestVersion = ModVersions.Best(mod.LatestVersion, candidate) ?? mod.LatestVersion;

	/// <summary>The key identifying the one download a mod comes from — its Nexus page or GitHub repo.</summary>
	private static string DownloadKey(StardewMod mod) => ModVersions.DownloadKey(mod);

	/// <summary>The release of <paramref name="key"/>'s download the manager knows is installed, or null.</summary>
	private string? InstalledDownloadVersion(string key) =>
		_settings.InstalledDownloadVersions.TryGetValue(_settings.ActiveGame, out var map) &&
		map.TryGetValue(key, out string? version) && !string.IsNullOrWhiteSpace(version)
			? version : null;

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
	private bool HasPendingUpdate(StardewMod installed, string? latestVersion) =>
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
	private (string Row, int Index, int Count)? RemoveSettledUpdateRow(StardewMod mod)
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
		if (mainTabs.SelectedTab != tabUpdates || listUpdates.SelectedItem is not StardewMod mod || mod.IsGroup)
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
			Speak(Loc.T("updates.markNoLink", mod.Name));
			return;
		}

		if (SpeakBox(Loc.T("updates.markConfirm", mod.LatestVersion, mod.Name), Loc.T("updates.markTitle"),
				MessageBoxButtons.YesNo) != DialogResult.Yes)
		{
			SpeakAfterPrompt(Loc.T("updates.markCancelled"));
			return;
		}

		string version = mod.LatestVersion!;
		RecordInstalledDownloadVersion(DownloadKey(mod), version);
		// The mod's own Version is left alone on purpose: it is what the manifest says, and on exactly the mods
		// this command exists for the manifest legitimately says something else. Overwriting it here would put a
		// number in the installed list that no file on disk agrees with, until the next rescan quietly undid it.

		// SpeakAfterPrompt, not Speak: closing the confirmation hands focus back and the screen reader answers by
		// re-reading the window, which flushes anything said in that instant. This is the same treatment the
		// search-history and backup-trim confirmations get.
		if (RemoveSettledUpdateRow(mod) is { } landed)
			SpeakAfterPrompt(Loc.T("updates.markedPos", mod.Name, version, landed.Row, landed.Index, landed.Count));
		else
			SpeakAfterPrompt(Loc.T("updates.markedEmpty", mod.Name, version));
	}

	/// <summary>
	/// Queries the Nexus Mods REST API or GitHub Releases for the latest version of a group of mods that share
	/// one download, and lists that download once when it has a newer release. Rate-limited by
	/// <c>_apiSemaphore</c> for Nexus.
	///
	/// The comparison deliberately isn't "each mod's manifest version versus the mod page's version". Those are
	/// different things: a "1.0.2" release routinely contains manifests that still say "1.0.0", and one download
	/// often installs several mods that each carry a version of their own. Comparing them offered an update that
	/// installing could never satisfy — the files arrived, the manifests still said the old number, and the mod
	/// came back next check. With two mods in one download they came back alternately, because installing it
	/// rewrote both folders and undid whatever the previous round had settled. So we compare what we know is
	/// installed (recorded at install time), falling back to the newest manifest version in the group, and list
	/// one row for the download rather than one per mod inside it.
	/// </summary>
	/// <summary>
	/// The newest version among the files a mod page offers as its main download, or <c>null</c> when there are
	/// none to compare. Only ever used as a second opinion when the page's own version said the mod was current.
	/// </summary>
	private async Task<string?> NewestMainFileVersion(string nexusId)
	{
		List<string> versions = await _nexusService.GetMainFileVersionsAsync(nexusId);
		if (versions.Count == 0) return null;

		// Highest rather than most recently uploaded. A page whose mod ships in parts has a MAIN file per part
		// with a version of its own — Engine Fixes' preloader is version "7" beside a plugin at "7.0.21" — and
		// the one that answers "is there anything newer than what I have" is the highest of them.
		return versions.Aggregate((best, v) => IsNewerVersion(best, v) ? v : best);
	}

	private async Task CheckForUpdates(List<StardewMod> group)
	{
		try
		{
			string? latestVersion = null;
			if (GameProfiles.Find(_settings.ActiveGame)?.IsMinecraft == true)
			{
				// Modrinth first, by the hash of the jar, which is exact. Then GitHub for a mod Modrinth does
				// not host — United Minecraft is published on GitHub releases only, and it names its own
				// repository in its manifest, so there is no reason for it to be the one mod nothing can check.
				string? remote = await LatestModrinthVersionAsync(group[0])
					?? (!string.IsNullOrEmpty(group[0].GitHubRepo)
						? await GetGitHubLatestReleaseVersionAsync(group[0].GitHubRepo!)
						: null);

				// ⚠️ Settled here rather than left to the general comparison, which splits on dots and so reads
				// "1.1.0+mc26.2" as having a fourth segment that "1.1.0" lacks — reporting the mod out of date
				// for ever, and "updating" it by downloading the same release again.
				latestVersion = MinecraftLayout.SameRelease(group[0].Version, remote) ? null : remote;
			}
			else if (!string.IsNullOrEmpty(group[0].NexusID))
			{
				latestVersion = await _nexusService.GetLatestVersionAsync(group[0].NexusID ?? "");
			}
			else if (!string.IsNullOrEmpty(group[0].GitHubRepo))
			{
				latestVersion = await GetGitHubLatestReleaseVersionAsync(group[0].GitHubRepo ?? "");
			}

			if (latestVersion == null) return;

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
			if (!IsNewerVersion(installedVersion, latestVersion) && !string.IsNullOrEmpty(group[0].NexusID))
			{
				string? fromFiles = await NewestMainFileVersion(group[0].NexusID!);
				if (IsNewerVersion(installedVersion, fromFiles)) latestVersion = fromFiles!;
			}

			if (_settings.IgnoredVersions.TryGetValue(group[0].UniqueId, out string? ignored) && ignored == latestVersion)
				return;

			foreach (StardewMod mod in group) RecordLatestVersion(mod, latestVersion);

			if (!IsNewerVersion(installedVersion, latestVersion)) return;

			// One row per download, named after the mod that best stands for it — the main mod rather than a
			// content pack bundled with it — so the row is recognisable (see UpdateCoverage.PickRepresentative).
			StardewMod representative = UpdateCoverage.PickRepresentative(group, _settings.CurrentModsPath);

			Invoke(delegate
			{
				listUpdates.BeginUpdate();
				if (!listUpdates.Items.Contains(representative))
				{
					representative.IsUpdateResult = true;
					listUpdates.Items.Add(representative);
				}
				listUpdates.EndUpdate();
			});
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
	// A SMAPI program update found during the smapi.io check, surfaced only after the whole update
	// run finishes so its prompt doesn't interrupt the mod-update announcement. Null when none.
	private (string Current, string Latest, string Url)? _pendingSmapiUpdate;

	// The installed mods the last update check had no Nexus/GitHub link for, so the manifest-based Nexus check
	// couldn't cover them. Surfaced in the completion announcement so "all up to date" isn't misleading — a
	// manually-placed mod with a null NexusID would otherwise be silently ignored. See RefreshModList.
	private List<StardewMod> _updateUnlinkedMods = new();

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
			if (listUpdates.Items[i] is StardewMod m && !HasPendingUpdate(m, m.LatestVersion))
				listUpdates.Items.RemoveAt(i);
		}
		listUpdates.EndUpdate();
	}

	private void CompleteUpdateCheckUnit()
	{
		if (Interlocked.Decrement(ref _activeChecks) <= 0)
		{
			_isLoading = false;
			// This batch is done; release the guard so the next update check can start.
			Interlocked.Exchange(ref _updateCheckRunning, 0);
			_soundEngine.Play("load_complete");
			var pendingSmapi = _pendingSmapiUpdate;
			_pendingSmapiUpdate = null;
			int unchecked_ = UncheckedModCount();
			Invoke(delegate
			{
				DropUpToDateUpdateRows();
				string message = listUpdates.Items.Count > 0
					? Loc.T("updates.checkComplete", listUpdates.Items.Count)
					: Loc.T("updates.checkCompleteNone");
				// Tell the user when some mods couldn't be checked at all (no Nexus/GitHub link), so an
				// "all up to date" result isn't taken to cover a manually-added mod that was really just skipped.
				if (unchecked_ > 0)
					message += " " + Loc.T(unchecked_ == 1 ? "updates.unlinkedNoteOne" : "updates.unlinkedNote", unchecked_);
				Speak(message);
				if (pendingSmapi is { } s)
					NotifySmapiUpdateAvailable(s.Current, s.Latest, s.Url);
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
	private async Task CheckUpdatesViaSmapiApi(List<StardewMod> installed, string smapiVersion, string gameVersion)
	{
		try
		{
			var entries = new List<(string Id, string Version, IEnumerable<string> UpdateKeys)>();
			foreach (StardewMod mod in installed)
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
				_pendingSmapiUpdate = (smapiVersion, smapiUpd.Version!, smapiUpd.Url ?? "");
			}

			var linked = new Dictionary<string, string>();
			foreach (StardewMod mod in installed)
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
						var nexus = Regex.Match(upd.Url!, @"nexusmods\.com/stardewvalley/mods/(\d+)", RegexOptions.IgnoreCase);
						if (nexus.Success)
						{
							mod.NexusID = nexus.Groups[1].Value;
							linked[mod.UniqueId] = mod.NexusID;
						}
						else
						{
							var gh = Regex.Match(upd.Url!, @"github\.com/([^/]+/[^/]+?)(?:/|$)", RegexOptions.IgnoreCase);
							if (gh.Success) mod.GitHubRepo = gh.Groups[1].Value;
						}
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
					if (!listUpdates.Items.Contains(mod))
					{
						mod.IsUpdateResult = true;
						listUpdates.Items.Add(mod);
					}
					listUpdates.EndUpdate();
				});
			}

			PersistNexusIdLinks(linked);
		}
		catch (Exception ex)
		{
			LogFailure("SmapiUpdate", "SMAPI update check failed", ex);
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
	private async void NotifySmapiUpdateAvailable(string current, string latest, string url)
	{
		// A check that found something, not a connection.
		_soundEngine.Play("load_complete");
		Speak(Loc.T("updates.smapiAvailableSpeak", current, latest));
		if (SpeakBox(
				Loc.T("updates.smapiBox", current, latest),
				Loc.T("updates.smapiTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
			return;

		// SMAPI's installer updates an existing install in place, so the same routine used for a fresh
		// install performs the update; it self-reports success/failure by voice and handles its own fallback.
		await InstallSmapiAsync(DetectGameFolder("StardewValley"));
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
			string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
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
    private async Task DownloadAndInstallUpdate(StardewMod mod, bool silent = false)
    {
        // Updates install the mod enabled; remember if it was disabled so we can turn it back off afterward.
        bool wasDisabled = !mod.IsEnabled;
        if (!string.IsNullOrEmpty(mod.GitHubRepo))
        {
            try
            {
                SetStatus(Loc.T("updates.updatingGitHub", mod.Name));
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
                string name = await ModFileSystem.ExtractModAsync(
                    destinationPath, _settings.CurrentModsPath, _allInstalledMods,
                    backupsPath, _settings.MaxBackupsPerMod, _settings.ActiveGame, LogError,
                    mod.NexusID, _nexusService, mod.GitHubRepo, _settings.CurrentGamePath, null, instProgress);
                instProgress?.Complete();

                Invoke(delegate
                {
                    listUpdates.BeginUpdate();
                    for (int i = listUpdates.Items.Count - 1; i >= 0; i--)
                    {
                        if (listUpdates.Items[i] is StardewMod m &&
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

                await ReapplyDisabledIfNeeded(mod, wasDisabled);
                // Remember which release is now on disk, so the next check compares against what was installed

                // rather than whatever version numbers the mods inside the download happen to declare.

                RecordInstalledDownloadVersion(DownloadKey(mod), mod.LatestVersion);

                await SyncManifestVersionAfterUpdate(mod, mod.LatestVersion);

                if (!silent)
                {
                    _soundEngine.Play("load_complete");
                    Speak(Loc.T("updates.githubSuccess", mod.Name));
                    // ResetStatus, not SetStatus: the title only needs to stop showing "Installing...", and SetStatus speaks by
                    // default, so this said "Connected as ..." straight after the update was announced. ResetStatus also asks
                    // RestingStatus what the title should be, which answers "Ready" when there is no known user -- the hardcoded
                    // line could put "Connected as Unknown User" up instead.
                    ResetStatus();
                    AnnounceUpdatesListEmptyIfFocused();
                }
                return;
            }
            catch (Exception ex)
            {
                _soundEngine.Play("error");
                LogFailure(mod.Name, "GitHub Download/Install Failure", ex);
                SpeakBox(Loc.T("updates.githubFailBox", mod.Name, FriendlyError(ex)));
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
            SetStatus(Loc.T("updates.updating", mod.Name));
            if (!silent) Speak(Loc.T("updates.downloading", mod.Name));

            // Download progress runs in a batch too (honouring the tones/speech/both/off setting); the silent
            // flag only mutes the per-mod chatter and success box, not the progress feedback itself.
            ProgressAnnouncer? progress = NewProgress(mod.Name, installing: false);
            string tempPath = await _nexusService.DownloadModUpdateAsync(mod, downloadsPath, progress);
            progress?.Complete();
            await InstallFromZip(tempPath, mod.NexusID, silent: silent);
            Invoke(delegate
            {
                listUpdates.BeginUpdate();
                for (int i = listUpdates.Items.Count - 1; i >= 0; i--)
                {
                    if (listUpdates.Items[i] is StardewMod m &&
                        !string.IsNullOrEmpty(m.NexusID) &&
                        m.NexusID.Equals(mod.NexusID, StringComparison.OrdinalIgnoreCase))
                        listUpdates.Items.RemoveAt(i);
                }
                listUpdates.EndUpdate();
            });
            await RefreshModList(checkUpdates: false);
            await ReapplyDisabledIfNeeded(mod, wasDisabled);
            // Remember which release is now on disk, so the next check compares against what was installed

            // rather than whatever version numbers the mods inside the download happen to declare.

            RecordInstalledDownloadVersion(DownloadKey(mod), mod.LatestVersion);

            await SyncManifestVersionAfterUpdate(mod, mod.LatestVersion);
            if (!silent)
            {
                _soundEngine.Play("load_complete");
                Speak(Loc.T("updates.success", mod.Name));
                // ResetStatus, not SetStatus: the title only needs to stop showing "Installing...", and SetStatus speaks by
                // default, so this said "Connected as ..." straight after the update was announced. ResetStatus also asks
                // RestingStatus what the title should be, which answers "Ready" when there is no known user -- the hardcoded
                // line could put "Connected as Unknown User" up instead.
                ResetStatus();
                AnnounceUpdatesListEmptyIfFocused();
            }
        }
        catch (Exception ex)
        {
            _soundEngine.Play("error");
            LogFailure(mod.Name, "Download/Install Failure", ex);
            Invoke(delegate { SpeakBox(Loc.T("updates.failBox", mod.Name, FriendlyError(ex))); });
        }
    }

	/// <summary>
	/// Announces "List is empty" after the last available update has been installed and removed in place. A screen
	/// reader only re-reads a list's state on a focus change, so emptying it programmatically would otherwise stay
	/// silent. We put focus on the now-empty Updates list (so the reader speaks its name) and then add the empty
	/// status, but only while the Updates tab is showing — we never yank focus if the user has moved on.
	/// </summary>
	private void AnnounceUpdatesListEmptyIfFocused()
	{
		if (_isLoading || listUpdates.Items.Count != 0 || CurrentTab() != AppTab.Updates)
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
	private async Task SyncManifestVersionAfterUpdate(StardewMod original, string? installedVersion)
	{
		if (string.IsNullOrEmpty(installedVersion)) return;
		if (UpdateCoverage.HasUpdateLink(original))
		{
			string key = DownloadKey(original);
			int sharing = _allInstalledMods.Count(m => !m.IsGroup && UpdateCoverage.HasUpdateLink(m) && DownloadKey(m) == key);
			if (sharing > 1) return;
		}
		try
		{
			StardewMod? updated = _allInstalledMods.FirstOrDefault(m => !m.IsGroup
					&& !string.IsNullOrEmpty(original.UniqueId) && m.UniqueId == original.UniqueId)
				?? _allInstalledMods.FirstOrDefault(m => !m.IsGroup &&
					((!string.IsNullOrEmpty(original.NexusID) && original.NexusID.Equals(m.NexusID, StringComparison.OrdinalIgnoreCase)) ||
					 (!string.IsNullOrEmpty(original.GitHubRepo) && original.GitHubRepo.Equals(m.GitHubRepo, StringComparison.OrdinalIgnoreCase))));

			if (updated == null || !Directory.Exists(updated.FolderPath)) return;
			if (!IsNewerVersion(updated.Version, installedVersion)) return;   // already at or beyond the new version

			string manifestPath = Path.Combine(updated.FolderPath,
				GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley) ? "manifest.json" : ".manager_manifest.json");
			if (!File.Exists(manifestPath)) return;

			JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
			manifest["Version"] = installedVersion;
			File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
			updated.Version = installedVersion;
			LogError(updated.Name, $"Manifest version corrected to the installed version {installedVersion}.");

			// Re-scan so the installed list reads the new version and the Updates tab drops the stale row.
			await RefreshModList(checkUpdates: false);
		}
		catch (Exception ex) { LogFailure(original.Name, "Could not update the manifest version", ex); }
	}

	/// <summary>
	/// If <paramref name="original"/> was disabled before being updated, re-disables the freshly installed copy
	/// (updates always install a mod enabled), so updating never silently turns a mod back on. The new copy is
	/// matched by Nexus ID or GitHub repo; its folder is renamed to the disabled form (a leading dot) and the mod
	/// list re-reconciled so deployment and plugins.txt reflect the disabled state.
	/// </summary>
	private async Task ReapplyDisabledIfNeeded(StardewMod original, bool wasDisabled)
	{
		if (!wasDisabled) return;
		StardewMod? updated = _allInstalledMods.FirstOrDefault(m => !m.IsGroup && m.IsEnabled &&
			((!string.IsNullOrEmpty(original.NexusID) && original.NexusID.Equals(m.NexusID, StringComparison.OrdinalIgnoreCase)) ||
			 (!string.IsNullOrEmpty(original.GitHubRepo) && original.GitHubRepo.Equals(m.GitHubRepo, StringComparison.OrdinalIgnoreCase))));
		if (updated == null) return;
		try
		{
			if (!updated.IsEnabled) return;   // already disabled
			updated.FolderPath = ModFileSystem.SetModEnabled(updated.FolderPath, false, _settings.ActiveGame);
			updated.IsEnabled = false;
			_soundEngine.Play("disable");
			await RefreshModList(checkUpdates: false);
		}
		catch (Exception ex)
		{
			LogFailure(original.Name, "Could not re-disable after update", ex);
		}
	}

	private async Task<string?> GetGitHubLatestReleaseVersionAsync(string repo)
	{
		try
		{
			using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
			req.Headers.UserAgent.ParseAdd($"KinetixModManager/{NexusService.AppVersion}");
			using var resp = await NexusService.HttpClient.SendAsync(req);
			if (!resp.IsSuccessStatusCode) return null;

			JObject json = JObject.Parse(await resp.Content.ReadAsStringAsync());
			string? tag = json["tag_name"]?.ToString();
			if (tag == null) return null;
			return tag.StartsWith("v") ? tag.Substring(1) : tag;
		}
		catch { return null; }
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
			JObject map = (File.Exists(mapPath) ? JObject.Parse(File.ReadAllText(mapPath)) : new JObject()) ?? new JObject();
			foreach (var kv in links) map[kv.Key] = kv.Value;
			File.WriteAllText(mapPath, map.ToString(Formatting.Indented));
		}
		catch (Exception ex) { LogFailure("ModIdMap", "Failed to persist Nexus ID mappings", ex); }
	}

	/// <summary>
	/// Links unmatched Stardew Valley mods to their Nexus page (or GitHub repo) using SMAPI's mod database,
	/// which resolves a mod by its UniqueID. Links found this way are exact, so they replace the name-search
	/// guesswork for every mod the database knows. Returns how many mods were linked; a no-op for other games.
	/// </summary>
	private async Task<int> MatchStardewIdsViaSmapiAsync(List<StardewMod> targetMods)
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
		foreach (StardewMod mod in targetMods)
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
	private static bool NeedsNexusDetails(StardewMod mod) => ModVersions.NeedsDetails(mod);

	/// <summary>
	/// Fetches the author and summary for every linked mod that is still missing them, and returns how many were
	/// filled in. The installed version is never touched — see <see cref="EnrichLinkedModFromNexusAsync"/>.
	/// </summary>
	private async Task<int> FillInMissingNexusDetailsAsync()
	{
		List<StardewMod> needing = _allInstalledMods
			.Where(m => !m.IsGroup && !string.IsNullOrEmpty(m.NexusID) && NeedsNexusDetails(m))
			.ToList();
		if (needing.Count == 0) return 0;

		int filled = 0;
		for (int i = 0; i < needing.Count; i++)
		{
			StardewMod mod = needing[i];
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
		int matchCount = 0;
		int totalMods = 0;
		_isLoading = true;
		Fire(RunLoadingLoop(), "RunLoadingLoop");
		Speak(Loc.T("updates.autoMatchStart"));

		try
		{
			var targetMods = _allInstalledMods.Where(m => !m.IsGroup && string.IsNullOrEmpty(m.NexusID) && string.IsNullOrEmpty(m.GitHubRepo)).ToList();
			totalMods = targetMods.Count;

			if (totalMods == 0)
			{
				Speak(Loc.T("updates.noUnmatched"));
				_isLoading = false;
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
				SetStatus(Loc.T("updates.matchingStatus", current, targetMods.Count, mod.Name));
				Speak(Loc.T("updates.searchingFor", mod.Name));

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
							File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
						}

						// A mod the manager had to identify by name usually has no author and no real summary
						// recorded — nothing local ever knew them. Now that its page is known, fetch them. The
						// installed version is deliberately left alone; see EnrichLinkedModFromNexusAsync.
						await EnrichLinkedModFromNexusAsync(mod, bestMatch.NexusID!);

						matchCount++;
						Speak(Loc.T("updates.matchedWith", bestMatch.Name));
					}
					else
					{
						Speak(Loc.T("updates.noConfidentMatch"));
					}
				}
				else
				{
					Speak(Loc.T("updates.noResults"));
				}

				await Task.Delay(500);
			}

			// Mods linked on an earlier run, or by an install, can still be missing the things only the mod page
			// knows — who wrote it, what it actually does. Fill those in as well, so auto-match leaves every
			// linked mod complete rather than only the ones it matched just now. Only mods that are actually
			// missing something are fetched, so running it again costs nothing.
			int detailed = await FillInMissingNexusDetailsAsync();

			_isLoading = false;
			_soundEngine.Play("load_complete");
			Speak(Loc.T("updates.autoMatchComplete", matchCount, totalMods));
			if (detailed > 0)
				Speak(Loc.T(detailed == 1 ? "updates.detailsFilledOne" : "updates.detailsFilled", detailed));
			await RefreshModList(checkUpdates: false);
		}
		catch (Exception ex)
		{
			_isLoading = false;
			_soundEngine.Play("error");
			LogFailure("AutoMatch", "Auto-match failed", ex);
			SpeakBox(Loc.T("updates.autoMatchFailBox", FriendlyError(ex)), Loc.T("common.error"));
			Speak(Loc.T("updates.autoMatchFailed"));
		}
	}

    private async Task CheckForAppUpdates(bool manual)
	{
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
				Speak(Loc.T("updates.newVersion", tag));
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
				Speak(Loc.T("updates.upToDate"));
				SpeakBox(Loc.T("updates.upToDateBox"), Loc.T("updates.upToDateTitle"));
			}
		}
		catch (Exception ex)
		{
			if (manual) { Speak(Loc.T("updates.checkFailed")); SpeakBox(Loc.T("updates.checkFailBox", FriendlyError(ex))); }
		}
	}

	private async Task DownloadAndInstallAppUpdateAsync(NexusService.AppReleaseInfo release)
	{
		string originalTitle = Text;
		try
		{
			_isLoading = true;
			Fire(RunLoadingLoop(), "RunLoadingLoop");
			Speak(Loc.T("updates.downloadingUpdate"));

			if (!Directory.Exists(downloadsPath))
			{
				Directory.CreateDirectory(downloadsPath);
			}

			string destinationPath = Path.Combine(downloadsPath, release.FileName);

			ProgressAnnouncer progress = NewProgress(Loc.T("updates.appUpdateName"), installing: false);
			await _nexusService.DownloadFileWithProgressAsync(release.DownloadUrl, destinationPath, progress);
			progress.Complete();

			_isLoading = false;
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

			Speak(Loc.T("updates.startingInstaller"));
			SpeakBox(Loc.T("updates.installerBox"), Loc.T("updates.installingTitle"));

			Process.Start(new ProcessStartInfo(installerPath)
			{
				UseShellExecute = true
			});

			Application.Exit();
		}
		catch (Exception ex)
		{
			_isLoading = false;
			Text = originalTitle;
			LogFailure("AppUpdate", "Self-update failed", ex);
			SpeakBox(Loc.T("updates.selfUpdateFailBox", FriendlyError(ex)), Loc.T("updates.updateErrorTitle"));
			Speak(Loc.T("updates.failed"));
		}
	}
}
