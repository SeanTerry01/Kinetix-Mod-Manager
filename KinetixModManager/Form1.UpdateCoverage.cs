using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using StardewMod = KinetixModManager.GameMod;

namespace KinetixModManager;

/// <summary>
/// The Update Coverage report: which installed mods the update check can cover, and — the part that matters —
/// which it can't, with enough detail to fix them. The rules behind it live in <see cref="UpdateCoverage"/>;
/// this file is the accessible list, the spoken summary, and the error-log dump.
/// </summary>
public partial class Form1
{
	/// <summary>
	/// Classifies every installed mod by how its updates are covered. <paramref name="smapiKnownIds"/> defaults
	/// to what the last update check learned from smapi.io; the report passes a freshly fetched set so it is
	/// accurate even if no check has run this session.
	/// </summary>
	private List<UpdateCoverageEntry> ClassifyUpdateCoverage(ISet<string>? smapiKnownIds = null) =>
		UpdateCoverage.Classify(_allInstalledMods, _settings.CurrentModsPath, smapiKnownIds ?? _smapiCheckedIds,
			BundledWithMap(),
			// Minecraft's catalogue recognises a mod by the SHA-1 of its jar, so no stored link is needed and
			// none of its mods is unchecked for want of one.
			checkedByFileHash: GameProfiles.Find(_settings.ActiveGame)?.IsMinecraft == true);

	/// <summary>The user's "this mod comes with that one" associations for the active game.</summary>
	private Dictionary<string, string> BundledWithMap() =>
		_settings.ModBundledWith.TryGetValue(_settings.ActiveGame, out var map) && map != null
			? map : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Records that <paramref name="child"/> arrives with <paramref name="parent"/>'s download, so it is treated
	/// as covered by it instead of being reported as impossible to check. For the case nothing on disk reveals:
	/// a mod from the same mod page as another — an optional file, typically — that unpacked into a folder of
	/// its own, with no update key and no shared folder to give the connection away.
	/// </summary>
	private void SetBundledWith(StardewMod child, StardewMod parent)
	{
		if (string.IsNullOrEmpty(child.UniqueId) || string.IsNullOrEmpty(parent.UniqueId)) return;
		if (!_settings.ModBundledWith.TryGetValue(_settings.ActiveGame, out var map) || map == null)
			_settings.ModBundledWith[_settings.ActiveGame] = map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		map[child.UniqueId] = parent.UniqueId;
		_settings.Save();
		_soundEngine.Play("enable");
		Speak(Loc.T("coverage.bundledSet", child.Name, parent.Name));
	}

	/// <summary>
	/// Lists the installed mods that can be checked for updates, so the user can say which one a stray mod
	/// arrives with. Only linked mods are offered: picking one that can't be checked itself would cover nothing.
	/// </summary>
	private void ShowBundleParentPicker(StardewMod child)
	{
		// One entry per download rather than per mod, named after the mod that best stands for it and described
		// by the folder it lives in — a download that installs five mods is recognised by "Cape Stardew 1.6",
		// not by whichever of its content packs happens to sort first.
		var downloads = _allInstalledMods
			.Where(m => !m.IsGroup && UpdateCoverage.HasUpdateLink(m) && m.UniqueId != child.UniqueId)
			.GroupBy(m => DownloadKey(m), StringComparer.OrdinalIgnoreCase)
			.Select(g => (Representative: UpdateCoverage.PickRepresentative(g, _settings.CurrentModsPath),
						  Count: g.Count()))
			.OrderBy(d => d.Representative.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		var rows = new List<ReportRow>();
		foreach (var (candidate, count) in downloads)
		{
			StardewMod chosen = candidate;
			string link = !string.IsNullOrEmpty(candidate.NexusID)
				? Loc.T("coverage.viaNexus", candidate.NexusID)
				: Loc.T("coverage.viaGitHub", candidate.GitHubRepo ?? "");
			string folder = UpdateCoverage.TopLevelFolder(candidate, _settings.CurrentModsPath);
			if (folder.Length == 0) folder = candidate.Name;
			rows.Add(new ReportRow
			{
				Text = count > 1
					? Loc.T("coverage.parentCandidateMany", candidate.Name, candidate.Author, folder, count, link)
					: Loc.T("coverage.parentCandidate", candidate.Name, candidate.Author, folder, link),
				OnEnter = () => { SetBundledWith(child, chosen); return Task.CompletedTask; }
			});
		}

		ShowReportDialog(Loc.T("coverage.parentTitle", child.Name), Loc.T("coverage.parentHeader", child.Name),
			Loc.T("coverage.parentNone"), rows, Loc.T("coverage.parentHint"), null, Loc.T("coverage.parentListName"));
	}

	/// <summary>
	/// Shows the Update Coverage report. For Stardew Valley it first asks SMAPI's mod database about every
	/// installed mod, so the report reflects the real position rather than whatever a previous check happened
	/// to learn — and links the ones it recognises along the way. Press Enter on an unchecked mod to give it a
	/// Nexus ID or GitHub repo. The full list is also written to the error log.
	/// </summary>
	private async Task ShowUpdateCoverageReport()
	{
		if (_settings.ActiveGame == "None") return;

		Speak(Loc.T("coverage.gathering"));
		// Recover what the manager can before reporting: links from the original downloads (exact), then
		// SMAPI's mod database. The report should show the position after those, not before — and both are
		// cheap enough to run every time.
		int recovered = RecoverNexusIdsFromDownloads();
		HashSet<string>? known = await FetchSmapiKnownIdsAsync();
		List<UpdateCoverageEntry> coverage = ClassifyUpdateCoverage(known);
		if (recovered > 0)
			Speak(Loc.T(recovered == 1 ? "coverage.recoveredOne" : "coverage.recovered", recovered));

		var notChecked = coverage.Where(c => c.Kind == UpdateCoverageKind.Unchecked).OrderBy(c => c.Mod.Name).ToList();
		var bundled = coverage.Where(c => c.Kind == UpdateCoverageKind.Bundled).OrderBy(c => c.Mod.Name).ToList();
		int covered = coverage.Count - notChecked.Count;

		var rows = new List<ReportRow>();
		foreach (UpdateCoverageEntry info in notChecked)
		{
			StardewMod m = info.Mod;
			// Say which of the two it is: a manifest with no update key at all, or one whose key is there but
			// blank ("UpdateKeys": [""] / ["Nexus: "]) — an author's typo. Both need the same fix from the
			// user, but knowing the mod isn't simply unknown to the world is the difference between an
			// actionable report and a mystery.
			// "Update key" is a Stardew Valley concept: SMAPI reads it from the mod's own manifest.json. The
			// other games have no such thing — a mod is linked by the Nexus mod ID the manager records for it —
			// so telling a Moonlight Peaks or Skyrim user their "manifest lists no update key" describes a file
			// and a field that don't exist, and gives them nothing to act on.
			string reason = info.Reason == UncheckedReason.BlankUpdateKey
				? Loc.T("coverage.reasonBlankKey")
				: GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley)
					? Loc.T("coverage.reasonNoKey")
					: Loc.T("coverage.reasonNoNexusId");
			rows.Add(new ReportRow
			{
				Text = Loc.T("coverage.rowUnchecked", m.Name, m.Version, reason, m.UniqueId, DisplayFolder(m)),
				OnEnter = () => ShowLinkCandidatesAsync(m)
			});
		}
		// Bundled mods are listed after the actionable ones, as reassurance rather than a to-do: they are
		// covered, and this report is the only place that says which download covers them.
		foreach (UpdateCoverageEntry info in bundled)
		{
			StardewMod m = info.Mod;
			StardewMod p = info.Parent!;
			string parentLink = !string.IsNullOrEmpty(p.NexusID)
				? Loc.T("coverage.viaNexus", p.NexusID)
				: Loc.T("coverage.viaGitHub", p.GitHubRepo ?? "");
			rows.Add(new ReportRow { Text = Loc.T("coverage.rowBundled", m.Name, m.Version, p.Name, parentLink) });
		}
		foreach (UpdateCoverageEntry info in coverage.Where(c => c.Kind == UpdateCoverageKind.PartOfSmapi)
					 .OrderBy(c => c.Mod.Name))
			rows.Add(new ReportRow { Text = Loc.T("coverage.rowSmapi", info.Mod.Name, info.Mod.Version) });

		WriteCoverageToLog(coverage);

		string header = Loc.T("coverage.header", covered, coverage.Count, notChecked.Count);
		string hint = notChecked.Count > 0 ? Loc.T("coverage.actionHint") : Loc.T("coverage.loggedHint");
		ShowReportDialog(Loc.T("coverage.title"), header, Loc.T("coverage.allCovered"), rows, hint,
			null, Loc.T("coverage.listName"));
	}

	/// <summary>
	/// Searches Nexus for the mod and offers the results as an accessible list to pick from, so a mod the
	/// manager couldn't link automatically can be fixed without knowing its mod ID or leaving the keyboard —
	/// "I don't know what to do with these" is otherwise where the coverage report leaves the user. Results the
	/// name and author agree with are marked as likely and listed first; the last row always falls back to
	/// typing an ID by hand.
	/// </summary>
	private async Task ShowLinkCandidatesAsync(StardewMod mod)
	{
		// Every name the mod goes by, best first, rather than only the one it calls itself.
		//
		// The single-name search was the same bug from the other end: a mod the manager could not identify is
		// very often one with no manifest to read, and its name is then whatever its FOLDER is called — which for
		// anything installed from Nexus is the download's name with the mod id and datestamp still attached.
		// Searching for that finds nothing, and the user is told "no results" for a mod that is plainly on the
		// site. The same aliases the automatic matcher already uses are tried here, in the same order.
		List<string> terms = ModNameMatch.SearchAliases(mod);
		if (terms.Count == 0) terms.Add(ModNameMatch.StripTypeTag(mod.Name));

		Speak(Loc.T("coverage.searching", terms[0]));

		var results = new List<GameMod>();
		foreach (string term in terms)
		{
			try
			{
				(List<GameMod> found, _) = await SearchActiveGameCatalogueAsync("Search", term, 1, 10);
				results = found;
			}
			catch (Exception ex)
			{
				LogFailure("LinkCandidates", $"searching Nexus for \"{term}\"", ex);
				results = new List<GameMod>();
			}

			// Stop at the first alias that turns anything up. A later alias is a broader, less certain guess —
			// the folder's parts, the GUID's segments — and running it over results we already have would bury
			// the good answer under worse ones.
			if (results.Any(r => !string.IsNullOrEmpty(r.NexusID))) break;
		}

		var rows = new List<ReportRow>();
		foreach (GameMod candidate in results
					 .Where(r => !string.IsNullOrEmpty(r.NexusID))
					 .OrderByDescending(r => ModNameMatch.IsConfident(mod, r)))
		{
			GameMod chosen = candidate;
			bool likely = ModNameMatch.IsConfident(mod, candidate);
			rows.Add(new ReportRow
			{
				Text = Loc.T(likely ? "coverage.candidateLikely" : "coverage.candidate",
					candidate.Name, candidate.Author, candidate.NexusID ?? ""),
				OnEnter = () => { ApplyNexusLink(mod, chosen.NexusID!, chosen.Name); return Task.CompletedTask; }
			});
		}
		rows.Add(new ReportRow { Text = Loc.T("coverage.enterIdManually"), OnEnter = () => LinkModUpdateSource(mod) });
		// The other way a stray mod gets settled: it has no page of its own because it arrived with another mod
		// — an optional file from the same page, unpacked into its own folder — which only the user can know.
		rows.Add(new ReportRow
		{
			Text = Loc.T("coverage.comesWithAnother"),
			OnEnter = () => { ShowBundleParentPicker(mod); return Task.CompletedTask; }
		});

		ShowReportDialog(Loc.T("coverage.linkTitle", mod.Name), Loc.T("coverage.linkHeader", mod.Name),
			Loc.T("coverage.linkNoResults"), rows, Loc.T("coverage.linkHint"), null, Loc.T("coverage.linkListName"));
	}

	/// <summary>
	/// Links a mod to a Nexus page the user picked, remembering it in the manager's own map rather than editing
	/// the author's manifest, then re-scans so the mod is immediately checkable.
	/// </summary>
	private async void ApplyNexusLink(StardewMod mod, string nexusId, string pageName)
	{
		mod.NexusID = nexusId;
		mod.GitHubRepo = null;
		PersistNexusIdLinks(new Dictionary<string, string> { [mod.UniqueId] = nexusId });
		_soundEngine.Play("enable");
		Speak(Loc.T("coverage.linked", mod.Name, pageName));

		// Pick up the author and summary now the page is known — the installed version is left as it is, so the
		// mod can actually report an update. See EnrichLinkedModFromNexusAsync.
		await EnrichLinkedModFromNexusAsync(mod, nexusId);

		await RefreshModList(checkUpdates: false);
	}

	/// <summary>
	/// Asks SMAPI's mod database which of the installed Stardew mods it recognises, linking any it can resolve
	/// to a Nexus page along the way. Returns null for other games (or when the service can't be reached), which
	/// leaves the classification relying on whatever the last update check learned.
	/// </summary>
	private async Task<HashSet<string>?> FetchSmapiKnownIdsAsync()
	{
		if (!GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley)) return null;

		var candidates = _allInstalledMods.Where(m => !m.IsGroup && !string.IsNullOrEmpty(m.UniqueId)).ToList();
		if (candidates.Count == 0) return null;

		var (smapiVer, gameVer) = DetectStardewVersions();
		var info = await _nexusService.GetSmapiUpdatesAsync(
			candidates.Select(m => (m.UniqueId, m.Version, (IEnumerable<string>)Array.Empty<string>())),
			smapiVer, gameVer);
		if (info == null) return null;

		var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var links = new Dictionary<string, string>();
		foreach (StardewMod mod in candidates)
		{
			if (!info.TryGetValue(mod.UniqueId, out var entry) || !entry.Known) continue;
			known.Add(mod.UniqueId);
			if (UpdateCoverage.HasUpdateLink(mod)) continue;
			if (!string.IsNullOrEmpty(entry.NexusId))
			{
				mod.NexusID = entry.NexusId;
				links[mod.UniqueId] = entry.NexusId!;
			}
			else if (!string.IsNullOrEmpty(entry.GitHubRepo))
			{
				mod.GitHubRepo = entry.GitHubRepo;
			}
		}
		PersistNexusIdLinks(links);
		// Remember what this lookup learned, so the next update check's completion count agrees with the report.
		foreach (string id in known) _smapiCheckedIds.Add(id);
		return known;
	}

	/// <summary>
	/// Recovers update links from the archives already sitting in the downloads folder. A Nexus download is named
	/// "Granny's Recipe Box-23737-1-0-2-1715181269.zip" — the number after the name is the mod id — and the
	/// archive lists the UniqueIDs of every mod it installs. Pairing the two links each installed mod to the exact
	/// page it came from, including the extra mods a single download unpacks, which commonly carry no update key
	/// of their own. Exact, unlike matching by name. Returns how many mods were newly linked.
	/// </summary>
	private int RecoverNexusIdsFromDownloads()
	{
		if (!Directory.Exists(downloadsPath)) return 0;

		var byUniqueId = _allInstalledMods
			.Where(m => !m.IsGroup && !string.IsNullOrEmpty(m.UniqueId))
			.GroupBy(m => m.UniqueId, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
		if (byUniqueId.Count == 0) return 0;

		var archives = Directory.EnumerateFiles(downloadsPath, "*.zip")
			.Select(path => (Path: path, NexusId: ModManifest.ParseNexusIdFromFileName(path)))
			.Where(a => !string.IsNullOrEmpty(a.NexusId))
			.ToList();
		// How many archives each Nexus id has. Exactly one means it is unambiguously the release that was
		// installed from that page; with several (the same mod downloaded repeatedly) guessing which one is on
		// disk could hide a real update, so those contribute links only.
		var archiveCount = archives.GroupBy(a => a.NexusId!, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

		int linkedCount = 0;
		var links = new Dictionary<string, string>();
		foreach (var (path, nexusId) in archives)
		{
			bool installedFromHere = false;
			foreach (string id in ModFileSystem.ReadModIdsInArchive(path))
			{
				if (!byUniqueId.TryGetValue(id, out var mods)) continue;
				installedFromHere = true;   // this download's mods are on disk
				foreach (StardewMod mod in mods)
				{
					if (UpdateCoverage.HasUpdateLink(mod)) continue;
					mod.NexusID = nexusId;
					links[mod.UniqueId] = nexusId!;
					linkedCount++;
				}
			}

			// Record which release is installed even when the mods were already linked: without it the check
			// falls back to comparing manifest versions, which for a download whose authors never bump them
			// reports an update that installing can never satisfy. An install or update always overwrites this
			// with what it actually put on disk, so a wrong guess here corrects itself.
			if (installedFromHere && archiveCount[nexusId!] == 1 &&
				InstalledDownloadVersion("Nexus:" + nexusId) == null &&
				ModFileSystem.ExtractVersionFromFileName(path, nexusId) is string version)
				RecordInstalledDownloadVersion("Nexus:" + nexusId, version);
		}
		PersistNexusIdLinks(links);
		return linkedCount;
	}

	/// <summary>
	/// Links every mod that a just-installed download placed on disk to the Nexus page it came from.
	/// <paramref name="installedName"/> is what the installer reported (the mods folder it created, prefixed
	/// "Mod Group " when the archive held several mods), so everything under that folder belongs to this
	/// download. Mods that declare their own update key keep it; only unlinked ones are filled in.
	/// </summary>
	private void LinkModsInstalledFrom(string installedName, string nexusId, string archiveName)
	{
		try
		{
			string folderName = installedName.StartsWith("Mod Group ", StringComparison.Ordinal)
				? installedName.Substring("Mod Group ".Length)
				: installedName;
			string root = Path.GetFullPath(Path.Combine(_settings.CurrentModsPath, folderName));
			if (!Directory.Exists(root)) return;
			string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

			var links = new Dictionary<string, string>();
			foreach (StardewMod mod in _allInstalledMods.Where(m => !m.IsGroup && !UpdateCoverage.HasUpdateLink(m)))
			{
				string path = Path.GetFullPath(mod.FolderPath);
				if (!path.Equals(root, StringComparison.OrdinalIgnoreCase) &&
					!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
				mod.NexusID = nexusId;
				links[mod.UniqueId] = nexusId;
			}
			PersistNexusIdLinks(links);
			// The release this download installed, so later checks compare against what is really on disk
			// rather than the version numbers the mods inside it happen to declare.
			if (ModFileSystem.ExtractVersionFromFileName(archiveName, nexusId) is string version)
				RecordInstalledDownloadVersion("Nexus:" + nexusId, version);
		}
		catch (Exception ex) { LogFailure("ModIdMap", "Could not record the download's Nexus ID", ex); }
	}

	/// <summary>The mod's folder relative to the Mods directory, falling back to the full path if it sits elsewhere.</summary>
	private string DisplayFolder(StardewMod mod)
	{
		string relative = UpdateCoverage.RelativeFolder(mod, _settings.CurrentModsPath);
		return relative.Length > 0 ? relative : mod.FolderPath;
	}

	/// <summary>
	/// Writes the whole classification to the error log (Ctrl+Shift+L), one line per mod, so the user has a
	/// copy they can read at leisure or paste into a bug report. Only the summary and the unchecked mods are
	/// spoken; the log holds everything, including each mod's unique ID, folder, and update link.
	/// </summary>
	private void WriteCoverageToLog(List<UpdateCoverageEntry> coverage)
	{
		var sb = new StringBuilder();
		sb.AppendLine($"--- Update coverage for {_settings.ActiveGame}: {coverage.Count} mods ---");
		foreach (UpdateCoverageEntry info in coverage.OrderBy(c => c.Kind).ThenBy(c => c.Mod.Name))
		{
			StardewMod m = info.Mod;
			string link = !string.IsNullOrEmpty(m.NexusID) ? "Nexus:" + m.NexusID
				: !string.IsNullOrEmpty(m.GitHubRepo) ? "GitHub:" + m.GitHubRepo
				: "no link";
			string detail = info.Kind switch
			{
				UpdateCoverageKind.Linked        => "checked directly",
				UpdateCoverageKind.SmapiDatabase => "checked via the SMAPI mod database",
				UpdateCoverageKind.Bundled       => $"bundled with \"{info.Parent!.Name}\" ({LinkOf(info.Parent!)})",
				UpdateCoverageKind.PartOfSmapi   => "ships with SMAPI; updates with SMAPI",
				UpdateCoverageKind.ByFileHash    => "checked by the mod file itself, so it needs no link",
				_ => "NOT CHECKED - " + (info.Reason == UncheckedReason.BlankUpdateKey
						? "manifest has a blank/malformed UpdateKeys entry"
						: "manifest declares no UpdateKeys")
			};
			sb.AppendLine($"  [{info.Kind}] {m.Name} {m.Version} | id={m.UniqueId} | {link} | folder={DisplayFolder(m)} | {detail}");
		}
		LogError("UpdateCoverage", Environment.NewLine + sb.ToString().TrimEnd());
	}

	private static string LinkOf(StardewMod mod) =>
		!string.IsNullOrEmpty(mod.NexusID) ? "Nexus:" + mod.NexusID : "GitHub:" + mod.GitHubRepo;
}
