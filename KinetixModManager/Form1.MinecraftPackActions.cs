using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// What can be done to a Minecraft modpack once there is one: finding more on Modrinth, keeping one up to date,
/// bringing one across from the Modrinth App, and copying a world into one.
/// </summary>
public partial class Form1
{
	/// <summary>The Updates-tab row for the loaded pack's own newer version. Not a mod, so it has a reserved id.</summary>
	internal const string ModpackRowId = "kinetix:modpack";

	// -------------------------------------------------------------------------
	// Finding packs
	// -------------------------------------------------------------------------

	/// <summary>Takes the player to the search tab, set to modpacks, with the search box ready.</summary>
	private void SearchForModpacks()
	{
		if (cmbDiscoveryContent != null) cmbDiscoveryContent.SelectedIndex = 1;
		SelectTab(AppTab.Discovery);
		txtSearch.Focus();
		Speak(Loc.T("mc.pack.searchReady"));
	}

	/// <summary>True when the search tab is set to modpacks, in a Minecraft session.</summary>
	private bool SearchingForModpacks =>
		GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.Minecraft) && cmbDiscoveryContent?.SelectedIndex == 1;

	/// <summary>
	/// What Enter on a modpack search result offers: install it, hear its description, or open its page. The same
	/// shape as a mod result, but installing makes a whole setup of its own rather than a jar in the mods folder.
	/// </summary>
	private async Task OfferModpackSearchResultAsync(GameMod result)
	{
		var actions = new List<string>
		{
			Loc.T("mc.pack.result.install"),
			Loc.T("mc.result.describe"),
			Loc.T("mc.result.page"),
		};
		(string Label, Func<Task> Run)? follow = await FollowActionForAsync(result);
		if (follow is { } f) actions.Add(f.Label);

		string? chosen = ShowChoiceList(Loc.T("mc.result.title", result.Name), Loc.T("mc.result.listName"),
			actions, actions[0], Loc.T("mc.pack.result.hint", result.Name));

		if (chosen is null) { Speak(Loc.T("common.changesCancelled")); return; }
		if (chosen == actions[1]) { SpeakLong(result.Description); return; }
		if (chosen == actions[2]) { OpenModPage(); return; }
		if (follow is { } toggle && chosen == toggle.Label) { await toggle.Run(); return; }

		try
		{
			Speak(Loc.T("mc.pack.result.finding", result.Name));
			IReadOnlyList<ModpackVersion> versions = await ModrinthService.GetModpackVersionsAsync(result.ModrinthId!);
			ModpackVersion? version = ModrinthService.ChooseVersionToInstall(versions);
			if (version is null)
			{
				SpeakBox(Loc.T("mc.pack.result.noFabric", result.Name), Loc.T("mc.pack.notInstalledTitle"),
					MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			// Said, because it is a choice made for the player: when a pack has only test builds, they should know
			// that what they are installing is one.
			if (!version.IsRelease) Speak(Loc.T("mc.pack.result.testBuild", result.Name, version.VersionNumber, version.VersionType));

			string file = await DownloadModpackFileAsync(version, downloadsPath, result.Name);
			await InstallModpackFromFileAsync(file);
		}
		catch (Exception ex)
		{
			LogFailure("Minecraft", $"Could not fetch the modpack {result.Name}", ex);
			SpeakBox(Loc.T("mc.pack.result.fetchFailed", result.Name, FriendlyError(ex)), Loc.T("mc.pack.notInstalledTitle"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}

	/// <summary>
	/// Downloads one version's <c>.mrpack</c> into <paramref name="folder"/> and checks it against its published
	/// checksum. Throws when it cannot be had or does not match.
	/// </summary>
	private async Task<string> DownloadModpackFileAsync(ModpackVersion version, string folder, string name)
	{
		if (!MinecraftModpacks.IsTrustedDownload(version.Url))
			throw new InvalidDataException($"The pack file is offered from {version.Url}, which is not Modrinth's.");

		Directory.CreateDirectory(folder);
		string path = Path.Combine(folder, WindowsFileName.StripInvalid(version.FileName));

		SetStatus(Loc.T("suite.downloading", name), speak: false);
		try
		{
			ProgressAnnouncer progress = NewProgress(name, installing: false);
			await _nexusService.DownloadFileWithProgressAsync(version.Url, path, progress);
			progress.Complete();
		}
		finally { ResetStatus(); }

		if (version.Sha1.Length > 0 && !FileHasSha1(path, version.Sha1))
		{
			File.Delete(path);
			throw new InvalidDataException($"{version.FileName} did not match its published checksum.");
		}

		return path;
	}

	// -------------------------------------------------------------------------
	// Keeping packs up to date
	// -------------------------------------------------------------------------

	/// <summary>
	/// Asks Modrinth whether a pack has a newer version, remembers the answer for the Minecraft Packs tab, and — in
	/// the pack's own session — adds it to the Updates tab. With <paramref name="announce"/>, says what it found.
	/// </summary>
	private async Task CheckModpackUpdateAsync(MinecraftPack pack, bool announce = false)
	{
		if (pack.ModrinthProjectId.Length == 0 || pack.ModrinthVersionId.Length == 0)
		{
			if (announce) Speak(Loc.T("mc.pack.update.cannotCheck", pack.Name));
			return;
		}

		try
		{
			IReadOnlyList<ModpackVersion> versions = await ModrinthService.GetModpackVersionsAsync(pack.ModrinthProjectId);
			ModpackVersion? update = ModrinthService.ChooseUpdate(versions, pack.ModrinthVersionId);

			if (update is null)
			{
				_packUpdates.Remove(pack.Folder);
				if (announce) Speak(Loc.T("mc.pack.update.upToDate", pack.Name, pack.PackVersion));
				return;
			}

			_packUpdates[pack.Folder] = update;
			if (announce) Speak(Loc.T("mc.pack.update.available", pack.Name, update.VersionNumber, pack.PackVersion));

			if (MinecraftModpacks.InstallKeyFor(pack) == _settings.ActiveGame)
				AddPlatformUpdateRow(ModpackRowId, pack.Name, Loc.T("mc.pack.update.rowAuthor"), pack.PackVersion, update.VersionNumber);
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", $"checking {pack.Name} for a newer version", ex);
			if (announce) Speak(Loc.T("mc.pack.update.checkFailed", pack.Name));
		}
	}

	/// <summary>Checks every installed pack, then refreshes the tab. With <paramref name="announce"/>, sums up.</summary>
	private async Task CheckAllModpackUpdatesAsync(bool announce)
	{
		IReadOnlyList<MinecraftPack> packs = MinecraftModpacks.FindInstalled(MinecraftModpacks.PacksFolder);
		if (packs.Count == 0)
		{
			if (announce) Speak(Loc.T("mc.pack.none"));
			return;
		}

		if (announce) Speak(Loc.T("mc.pack.update.checking", packs.Count));
		foreach (MinecraftPack pack in packs) await CheckModpackUpdateAsync(pack);

		await RefreshMinecraftPacksListAsync();

		if (!announce) return;
		List<string> waiting = packs.Where(p => _packUpdates.ContainsKey(p.Folder)).Select(p => p.Name).ToList();
		int unknown = packs.Count(p => p.ModrinthVersionId.Length == 0);
		Speak(waiting.Count == 0
			? Loc.T("mc.pack.update.noneWaiting", packs.Count - unknown)
			: Loc.T("mc.pack.update.someWaiting", waiting.Count, string.Join(", ", waiting)));
		if (unknown > 0) Speak(Loc.T("mc.pack.update.someUnknown", unknown));
	}

	/// <summary>
	/// Updates a pack to a newer version.
	///
	/// <para>
	/// Everything is worked out before anything changes (see <see cref="MinecraftModpacks.PlanUpdate"/>), the new
	/// files are downloaded into a temporary folder, and only once all of them have arrived is the pack touched —
	/// after a backup of every file the update will remove or replace. What the player added and what they changed
	/// in <c>options.txt</c> is not the pack's, and stays.
	/// </para>
	/// </summary>
	private async Task UpdateModpackAsync(MinecraftPack pack, ModpackVersion update)
	{
		string root = MinecraftRootFolder();
		string key = MinecraftModpacks.InstallKeyFor(pack);

		string mrpack;
		try
		{
			Speak(Loc.T("mc.pack.update.fetching", pack.Name, update.VersionNumber));
			mrpack = await DownloadModpackFileAsync(update, DownloadsPathFor(key), pack.Name);
		}
		catch (Exception ex)
		{
			LogFailure("Minecraft", $"Could not fetch {pack.Name} {update.VersionNumber}", ex);
			SpeakBox(Loc.T("mc.pack.result.fetchFailed", pack.Name, FriendlyError(ex)), Loc.T("mc.pack.update.title"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}

		MrpackIndex index = await Task.Run(() => MinecraftModpacks.ReadIndex(mrpack));
		OverridePlan overrides = index.Problem == ModpackProblem.None ? await Task.Run(() => PlanOverridesOf(mrpack)) : new OverridePlan();
		if (index.Problem == ModpackProblem.None && overrides.Unsafe.Count > 0)
			index = new MrpackIndex { Name = index.Name, Problem = ModpackProblem.UnsafePath, ProblemDetail = overrides.Unsafe[0] };
		if (index.Problem != ModpackProblem.None)
		{
			ReportModpackProblem(index, Path.GetFileName(mrpack));
			return;
		}

		string PathOf(string recordPath) => Path.Combine(pack.Folder, recordPath.Replace('/', Path.DirectorySeparatorChar));
		string? Sha1Of(string recordPath)
		{
			string enabled = PathOf(recordPath), disabled = enabled + MinecraftDisabledSuffix;
			string? existing = File.Exists(enabled) ? enabled : File.Exists(disabled) ? disabled : null;
			return existing is null ? null : ModrinthService.Sha1Of(existing);
		}

		ModpackUpdatePlan plan = await Task.Run(() => MinecraftModpacks.PlanUpdate(pack, index, overrides,
			Sha1Of, p => File.Exists(PathOf(p) + MinecraftDisabledSuffix)));

		// One question, holding everything that decides it — including a move to another Minecraft version, which
		// is the one part of an update that cannot be undone for a world opened afterwards.
		string question = Loc.T("mc.pack.update.confirm", pack.Name, pack.PackVersion, update.VersionNumber,
			plan.Download.Count, FormatBytes(plan.Download.Sum(f => f.Size)), plan.Remove.Count);
		if (index.MinecraftVersion != pack.MinecraftVersion)
			question += "\n\n" + Loc.T("mc.pack.update.movesVersion", pack.MinecraftVersion, index.MinecraftVersion);

		if (SpeakBox(question, Loc.T("mc.pack.update.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			Speak(Loc.T("common.changesCancelled"));
			return;
		}

		if (!await EnsureMinecraftVersionAsync(root, index.MinecraftVersion)) return;
		try { await FabricInstaller.WriteVersionJsonAsync(root, index.MinecraftVersion, index.LoaderVersion); }
		catch (Exception ex)
		{
			LogFailure("Minecraft", $"Could not install Fabric {index.LoaderVersion}", ex);
			SpeakBox(Loc.T("mc.pack.fabricFailed", pack.Name, index.LoaderVersion, FriendlyError(ex)),
				Loc.T("mc.pack.update.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}

		string staging = pack.Folder.TrimEnd('\\', '/') + ".updating";
		try
		{
			DiscardStaging(staging);
			Directory.CreateDirectory(staging);

			if (plan.Download.Count > 0)
			{
				string opening = Loc.T("mc.pack.downloading", pack.Name, plan.Download.Count, FormatBytes(plan.Download.Sum(f => f.Size)));
				Speak(opening);
				SetStatus(opening, speak: false);

				List<string> failed = await FetchAllAsync(plan.Download
					.Select(f => new MinecraftFetch(f.Url, Path.Combine(staging, f.RelativePath), f.Sha1, f.Size)).ToList(), pack.Name);
				if (failed.Count > 0)
				{
					DiscardStaging(staging);
					SpeakBox(Loc.T("mc.pack.update.downloadFailed", pack.Name, failed.Count, string.Join(", ", failed.Take(5))),
						Loc.T("mc.pack.update.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}
			}

			// Only now, with every new file in hand, can the pack's own mod ids be read — and with them, any other copy
			// of one of its mods that would stop the game starting. See MinecraftModpacks.FindSuperseded.
			(IReadOnlyList<(string Path, bool WasDisabled)> superseded, IReadOnlyList<string> keepOffToo) =
				await Task.Run(() => FindModsSupersededByPack(pack, plan, staging));
			var keepDisabled = new HashSet<string>(plan.KeepDisabled, StringComparer.OrdinalIgnoreCase);
			keepDisabled.UnionWith(keepOffToo);

			string backup = await Task.Run(() => BackUpBeforePackUpdate(pack, key, plan, superseded.Select(s => s.Path)));

			await Task.Run(() => ApplyPackUpdate(pack, plan, staging, mrpack, keepDisabled, superseded.Select(s => s.Path)));

			pack.PackVersion = index.VersionId.Length > 0 ? index.VersionId : update.VersionNumber;
			pack.MinecraftVersion = index.MinecraftVersion;
			pack.LoaderVersion = index.LoaderVersion;
			pack.ModrinthVersionId = update.Id;
			pack.SourceFileName = Path.GetFileName(mrpack);
			pack.ProvidedFiles = plan.ProvidedFiles;
			pack.FileProjects = plan.FileProjects;
			MinecraftModpacks.Save(pack);

			_packUpdates.Remove(pack.Folder);
			_activePackCache = default;
			InvalidateGameDisplayName();

			string done = Loc.T("mc.pack.update.done", pack.Name, pack.PackVersion, keepDisabled.Count, backup);
			if (superseded.Count > 0)
				done += "\n\n" + Loc.T("mc.pack.update.replacedYours", superseded.Count,
					string.Join(", ", superseded.Select(s => Path.GetFileName(s.Path))));
			SpeakBox(done, Loc.T("mc.pack.update.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
		catch (Exception ex)
		{
			LogFailure("Minecraft", $"Could not update the modpack {pack.Name}", ex);
			SpeakBox(Loc.T("mc.pack.update.failed", pack.Name, FriendlyError(ex)), Loc.T("mc.pack.update.title"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
		finally
		{
			DiscardStaging(staging);
			ResetStatus();
		}

		RemoveUpdateRowsFor(new GameMod { UniqueId = ModpackRowId });
		await RefreshMinecraftPacksListAsync();
		if (MinecraftModpacks.InstallKeyFor(pack) == _settings.ActiveGame) RefreshAllData(checkUpdates: false);
	}

	/// <summary>The suffix a switched-off Minecraft mod carries — <c>.disabled</c>.</summary>
	private static string MinecraftDisabledSuffix =>
		GameProfiles.Find(GameProfiles.Minecraft)?.DisabledModSuffix ?? ".disabled";

	/// <summary>
	/// Zips every file the update will remove or replace, into the pack's own backups. Returns the zip's path.
	/// Worlds and anything the player added are not touched by an update, so they are not in it.
	/// </summary>
	private string BackUpBeforePackUpdate(MinecraftPack pack, string key, ModpackUpdatePlan plan, IEnumerable<string> superseded)
	{
		string folder = Path.Combine(dataBasePath, "backups", key);
		Directory.CreateDirectory(folder);
		string zipPath = Path.Combine(folder,
			$"{WindowsFileName.ToFolderName(pack.Name)}_{WindowsFileName.StripInvalid(pack.PackVersion)}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

		IEnumerable<string> touched = plan.Remove
			.Concat(plan.Download.Select(f => MinecraftModpacks.RecordPath(f.RelativePath)))
			.Concat(plan.Bundled.Files.Select(o => MinecraftModpacks.RecordPath(o.RelativePath)))
			.Concat(superseded);

		using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
		foreach (string recordPath in touched.Distinct(StringComparer.OrdinalIgnoreCase))
		{
			foreach (string candidate in new[] { recordPath, recordPath + MinecraftDisabledSuffix })
			{
				string full = Path.Combine(pack.Folder, candidate.Replace('/', Path.DirectorySeparatorChar));
				if (File.Exists(full)) zip.CreateEntryFromFile(full, candidate);
			}
		}

		return zipPath;
	}

	/// <summary>
	/// The other copies of the pack's own mods (see <see cref="MinecraftModpacks.FindSuperseded"/>), and the pack
	/// files to leave switched off because the player's copy of that mod was switched off. Reads the mod id inside
	/// every jar involved, so it runs only once the new files are all in the staging folder.
	/// </summary>
	private static (IReadOnlyList<(string Path, bool WasDisabled)> Superseded, IReadOnlyList<string> KeepOff)
		FindModsSupersededByPack(MinecraftPack pack, ModpackUpdatePlan plan, string staging)
	{
		string suffix = MinecraftDisabledSuffix;
		static string IdOf(string file)
		{
			try { return MinecraftLayout.ReadModInfo(file).Id; }
			catch (Exception ex)
			{
				DiagnosticLog.WriteException("Minecraft", $"reading {Path.GetFileName(file)}", ex);
				return "";
			}
		}

		var downloaded = new HashSet<string>(plan.Download.Select(f => MinecraftModpacks.RecordPath(f.RelativePath)),
			StringComparer.OrdinalIgnoreCase);
		var packJars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string recordPath in plan.ProvidedFiles.Where(p =>
					 p.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) &&
					 p.EndsWith(MinecraftLayout.ModExtension, StringComparison.OrdinalIgnoreCase)))
		{
			string local = recordPath.Replace('/', Path.DirectorySeparatorChar);
			string file = downloaded.Contains(recordPath) ? Path.Combine(staging, local) : Path.Combine(pack.Folder, local);
			if (!File.Exists(file)) file += suffix;
			if (File.Exists(file)) packJars[recordPath] = IdOf(file);
		}

		var provided = new HashSet<string>(pack.ProvidedFiles.Concat(plan.ProvidedFiles), StringComparer.OrdinalIgnoreCase);
		var others = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string mods = Path.Combine(pack.Folder, "mods");
		if (Directory.Exists(mods))
		{
			foreach (string file in Directory.EnumerateFiles(mods).Where(f => MinecraftLayout.IsModFile(f, suffix)))
			{
				string recordPath = "mods/" + Path.GetFileName(file);
				string canonical = recordPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
					? recordPath.Substring(0, recordPath.Length - suffix.Length)
					: recordPath;
				if (!provided.Contains(canonical)) others[recordPath] = IdOf(file);
			}
		}

		IReadOnlyList<(string Path, bool WasDisabled)> superseded = MinecraftModpacks.FindSuperseded(packJars, others, suffix);

		// A mod the player had switched off stays off when the pack's copy of it takes over.
		var offIds = new HashSet<string>(superseded.Where(s => s.WasDisabled).Select(s => others[s.Path]),
			StringComparer.OrdinalIgnoreCase);
		List<string> keepOff = packJars.Where(j => offIds.Contains(j.Value)).Select(j => j.Key).ToList();

		return (superseded, keepOff);
	}

	/// <summary>Puts an update's files in place. Runs only after every new file has arrived and the backup is made.</summary>
	private static void ApplyPackUpdate(MinecraftPack pack, ModpackUpdatePlan plan, string staging, string mrpack,
		IReadOnlySet<string> keepDisabled, IEnumerable<string> superseded)
	{
		string Full(string recordPath) => Path.Combine(pack.Folder, recordPath.Replace('/', Path.DirectorySeparatorChar));

		// Other copies of the pack's own mods — already in the backup. Two copies of one mod stop Fabric starting.
		foreach (string recordPath in superseded)
			if (File.Exists(Full(recordPath))) File.Delete(Full(recordPath));

		// What the old version provided and the new one does not — in either state.
		foreach (string recordPath in plan.Remove)
			foreach (string candidate in new[] { Full(recordPath), Full(recordPath) + MinecraftDisabledSuffix })
				if (File.Exists(candidate)) File.Delete(candidate);

		// The new downloads, each landing switched on or off as the player had it.
		foreach (MrpackFile file in plan.Download)
		{
			string recordPath = MinecraftModpacks.RecordPath(file.RelativePath);
			string target = Full(recordPath);
			foreach (string old in new[] { target, target + MinecraftDisabledSuffix })
				if (File.Exists(old)) File.Delete(old);

			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Move(Path.Combine(staging, file.RelativePath),
				keepDisabled.Contains(recordPath) ? target + MinecraftDisabledSuffix : target);
		}

		// A file that did not change but whose mod the player had switched off under its old name is switched off
		// under this one too.
		foreach (string recordPath in keepDisabled)
		{
			string enabled = Full(recordPath);
			if (File.Exists(enabled) && !File.Exists(enabled + MinecraftDisabledSuffix))
				File.Move(enabled, enabled + MinecraftDisabledSuffix);
		}

		MinecraftModpacks.ExtractOverrides(mrpack, pack.Folder, plan.Bundled);
	}

	// -------------------------------------------------------------------------
	// Bringing packs across from the Modrinth App
	// -------------------------------------------------------------------------

	/// <summary>
	/// Copies one of the Modrinth App's modpacks into the manager, the way the Mod Organizer 2 import does: the
	/// app's copy is left exactly where it is, still working in the app.
	/// </summary>
	private async Task ImportFromModrinthAppAsync()
	{
		string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		IReadOnlyList<ModrinthAppInstance> found = await Task.Run(() =>
			ModrinthAppImport.FindAll(appData, Path.GetTempPath()));

		if (found.Count == 0)
		{
			SpeakBox(Loc.T("mc.import.none"), Loc.T("mc.import.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		// Packs already brought across say so — importing one twice is allowed, but should never be an accident.
		IReadOnlyList<MinecraftPack> managerPacks = MinecraftModpacks.FindInstalled(MinecraftModpacks.PacksFolder);
		var alreadyImported = new HashSet<string>(
			managerPacks
				.Where(p => p.ImportedFrom.Length > 0).Select(p => Path.GetFullPath(p.ImportedFrom).TrimEnd('\\', '/')),
			StringComparer.OrdinalIgnoreCase);

		var labels = new Dictionary<string, ModrinthAppInstance>(StringComparer.Ordinal);
		foreach (ModrinthAppInstance instance in found)
		{
			string label = !instance.IsFabric
				? Loc.T("mc.import.rowWrongLoader", instance.Name, instance.MinecraftVersion, LoaderDisplayName(instance.Loader))
				: !instance.HasVersions
					? Loc.T("mc.import.rowUnknown", instance.Name)
					: ModrinthAppImport.VersionFromFolderName(Path.GetFileName(instance.Folder), instance.Name) is { Length: > 0 } packVersion
						? Loc.T("mc.import.rowVersion", instance.Name, packVersion, instance.MinecraftVersion, instance.ModCount)
						: Loc.T("mc.import.row", instance.Name, instance.MinecraftVersion, instance.ModCount);
			if (instance.FromOlderApp) label = Loc.T("mc.import.rowOlder", label);
			if (alreadyImported.Contains(Path.GetFullPath(instance.Folder).TrimEnd('\\', '/')))
				label = Loc.T("mc.import.rowImported", label);
			// The same pack installed in the manager some other way — from a file, or from search — is a separate
			// copy, and without saying so this row reads as "you already have this": the player asked exactly that.
			else if (managerPacks.FirstOrDefault(p => p.ImportedFrom.Length == 0 &&
						 string.Equals(p.Name, instance.Name, StringComparison.CurrentCultureIgnoreCase)) is { } separate)
				label = Loc.T("mc.import.rowSeparateCopy", label, separate.PackVersion);
			while (labels.ContainsKey(label)) label += " ";
			labels[label] = instance;
		}

		string? picked = ShowChoiceList(Loc.T("mc.import.title"), Loc.T("mc.import.listName"), labels.Keys.ToList(),
			labels.Keys.First(), Loc.T("mc.import.hint"));
		if (picked is null || !labels.TryGetValue(picked, out ModrinthAppInstance? chosen))
		{
			Speak(Loc.T("common.changesCancelled"));
			return;
		}

		if (!chosen.IsFabric || !chosen.HasVersions)
		{
			SpeakBox(!chosen.IsFabric
					? Loc.T("mc.import.wrongLoader", chosen.Name, LoaderDisplayName(chosen.Loader))
					: Loc.T("mc.import.unknownVersions", chosen.Name),
				Loc.T("mc.import.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		string root = MinecraftRootFolder();
		if (SpeakBox(Loc.T("mc.import.confirm", chosen.Name, chosen.MinecraftVersion, chosen.LoaderVersion, chosen.ModCount),
				Loc.T("mc.import.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			Speak(Loc.T("common.changesCancelled"));
			return;
		}

		if (!await EnsureMinecraftVersionAsync(root, chosen.MinecraftVersion)) return;
		try { await FabricInstaller.WriteVersionJsonAsync(root, chosen.MinecraftVersion, chosen.LoaderVersion); }
		catch (Exception ex)
		{
			LogFailure("Minecraft", $"Could not install Fabric {chosen.LoaderVersion}", ex);
			SpeakBox(Loc.T("mc.pack.fabricFailed", chosen.Name, chosen.LoaderVersion, FriendlyError(ex)),
				Loc.T("mc.import.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}

		string packsFolder = MinecraftModpacks.PacksFolder;
		string folderName = MinecraftModpacks.UniqueFolderName(chosen.Name, n =>
			Directory.Exists(Path.Combine(packsFolder, n)) || Directory.Exists(MinecraftModpacks.StagingFolderFor(packsFolder, n)));
		string staging = MinecraftModpacks.StagingFolderFor(packsFolder, folderName);
		string final = Path.Combine(packsFolder, folderName);

		try
		{
			Speak(Loc.T("mc.import.copying", chosen.Name));
			SetStatus(Loc.T("mc.import.copying", chosen.Name), speak: false);
			DiscardStaging(staging);
			int files = await Task.Run(() => ModrinthAppImport.CopyInstance(chosen.Folder, staging));

			var pack = new MinecraftPack
			{
				Folder = staging,
				Name = chosen.Name,
				PackVersion = ModrinthAppImport.VersionFromFolderName(Path.GetFileName(chosen.Folder), chosen.Name),
				MinecraftVersion = chosen.MinecraftVersion,
				LoaderVersion = chosen.LoaderVersion,
				ModrinthProjectId = chosen.LinkedProjectId,
				ModrinthVersionId = chosen.LinkedVersionId,
				SourceFileName = Loc.T("mc.import.sourceName"),
				ImportedFrom = chosen.Folder,
				InstalledUtc = DateTime.UtcNow
			};

			// A pack the app still links knows exactly which version it is, so its own file list can be had — and
			// with it, what an update may replace. An unlinked one cannot be told apart from what the player
			// added, so nothing in it is treated as the pack's and it is not offered updates.
			if (pack.ModrinthVersionId.Length > 0) await RecordProvidedFilesAsync(pack);

			MinecraftModpacks.Save(pack);
			Directory.Move(staging, final);
			pack.Folder = final;

			ResetStatus();
			DiagnosticLog.Write("Minecraft", $"imported {chosen.Name} from {chosen.Folder}: {files} files");
			await RefreshMinecraftPacksListAsync();
			await OfferModrinthAppCleanupAfterImportAsync();
			await OfferToPlayNewPackAsync(pack, offerWorldCopy: false);
		}
		catch (Exception ex)
		{
			ResetStatus();
			DiscardStaging(staging);
			LogFailure("Minecraft", $"Could not import {chosen.Name}", ex);
			SpeakBox(Loc.T("mc.import.failed", chosen.Name, FriendlyError(ex)), Loc.T("mc.import.title"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}

	/// <summary>Fetches the linked version's file list, so an imported pack knows which of its files are the pack's.</summary>
	private async Task RecordProvidedFilesAsync(MinecraftPack pack)
	{
		try
		{
			IReadOnlyList<ModpackVersion> versions = await ModrinthService.GetModpackVersionsAsync(pack.ModrinthProjectId);
			if (versions.FirstOrDefault(v => v.Id == pack.ModrinthVersionId) is not { } version) return;

			string mrpack = await DownloadModpackFileAsync(version, DownloadsPathFor(MinecraftModpacks.InstallKeyPrefix + Path.GetFileName(pack.Folder)), pack.Name);
			MrpackIndex index = MinecraftModpacks.ReadIndex(mrpack);
			if (index.Problem != ModpackProblem.None) return;

			pack.PackVersion = version.VersionNumber;
			pack.ProvidedFiles = index.Files.Select(f => MinecraftModpacks.RecordPath(f.RelativePath))
				.Concat(PlanOverridesOf(mrpack).Files.Select(o => MinecraftModpacks.RecordPath(o.RelativePath)))
				.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			pack.FileProjects = index.Files
				.Select(f => (Path: MinecraftModpacks.RecordPath(f.RelativePath), Project: MinecraftModpacks.ProjectFromUrl(f.Url)))
				.Where(f => f.Project.Length > 0)
				.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Project, StringComparer.OrdinalIgnoreCase);
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", $"reading the file list of {pack.Name}", ex);
		}
	}

	/// <summary>A loader's name the way a player would say it.</summary>
	private static string LoaderDisplayName(string loader) => loader.ToLowerInvariant() switch
	{
		"fabric" => "Fabric",
		"forge" => "Forge",
		"neoforge" => "NeoForge",
		"quilt" => "Quilt",
		"vanilla" => Loc.T("mc.import.noLoader"),
		"" => Loc.T("mc.import.unknownLoader"),
		_ => loader
	};

	// -------------------------------------------------------------------------
	// Copying a world into a pack
	// -------------------------------------------------------------------------

	/// <summary>
	/// Offers to copy one of the player's worlds into a pack — from their own Minecraft or from another pack.
	/// With <paramref name="askFirst"/> (straight after an install), asks whether to at all before listing them,
	/// and says nothing when there are none. Returns whether the player was asked anything.
	/// </summary>
	/// <param name="preamble">Said before the question, in the same box — see OfferToPlayNewPackAsync.</param>
	private async Task<bool> OfferToCopyWorldAsync(MinecraftPack pack, bool askFirst, string? preamble = null)
	{
		var sources = new List<(string Label, MinecraftWorld World)>();

		foreach (MinecraftWorld world in MinecraftWorlds.FindIn(MinecraftRootFolder()))
			sources.Add((Loc.T("mc.world.rowOwn", world.Name, VersionOrUnknown(world)), world));

		foreach (MinecraftPack other in MinecraftModpacks.FindInstalled(MinecraftModpacks.PacksFolder))
		{
			if (string.Equals(other.Folder, pack.Folder, StringComparison.OrdinalIgnoreCase)) continue;
			foreach (MinecraftWorld world in MinecraftWorlds.FindIn(other.Folder))
				sources.Add((Loc.T("mc.world.rowPack", world.Name, other.Name, VersionOrUnknown(world)), world));
		}

		// The Modrinth App's worlds too, straight from its folders — so a world can come across on its own, without
		// importing a whole pack of mods just to reach it. Only ever copied from; the app's files are not changed.
		string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		IReadOnlyList<ModrinthAppInstance> appPacks = await Task.Run(() => ModrinthAppImport.FindAll(appData, Path.GetTempPath()));
		foreach (ModrinthAppInstance appPack in appPacks)
			foreach (MinecraftWorld world in MinecraftWorlds.FindIn(appPack.Folder))
				sources.Add((Loc.T("mc.world.rowModrinthApp", world.Name, appPack.Name, VersionOrUnknown(world)), world));

		if (sources.Count == 0)
		{
			if (!askFirst) Speak(Loc.T("mc.world.none"));
			return false;
		}

		string offer = Loc.T("mc.world.offer", pack.Name, sources.Count);
		if (askFirst &&
			SpeakBox(preamble is null ? offer : preamble + "\n\n" + offer, Loc.T("mc.world.title"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
			return true;

		var labels = new Dictionary<string, MinecraftWorld>(StringComparer.Ordinal);
		foreach ((string label, MinecraftWorld world) in sources)
		{
			string unique = label;
			while (labels.ContainsKey(unique)) unique += " ";
			labels[unique] = world;
		}

		string? picked = ShowChoiceList(Loc.T("mc.world.title"), Loc.T("mc.world.listName"), labels.Keys.ToList(),
			labels.Keys.First(), Loc.T("mc.world.hint", pack.Name));
		if (picked is null || !labels.TryGetValue(picked, out MinecraftWorld? chosen))
		{
			Speak(Loc.T("common.changesCancelled"));
			return true;
		}

		// A world from a newer Minecraft is the one copy that can go wrong. Asked, not refused: the copy is only a
		// copy, and a player may well want to try.
		if (IsNewerThanPack(chosen, pack) &&
			SpeakBox(Loc.T("mc.world.newer", chosen.Name, chosen.LastPlayedVersion, pack.Name, pack.MinecraftVersion),
				Loc.T("mc.world.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
		{
			Speak(Loc.T("common.changesCancelled"));
			return true;
		}

		try
		{
			Speak(Loc.T("mc.world.copying", chosen.Name));
			await Task.Run(() => MinecraftWorlds.CopyInto(chosen, pack.Folder));
			Speak(Loc.T("mc.world.copied", chosen.Name, pack.Name));
		}
		catch (Exception ex)
		{
			LogFailure("Minecraft", $"Could not copy the world {chosen.Name}", ex);
			SpeakBox(Loc.T("mc.world.failed", chosen.Name, FriendlyError(ex)), Loc.T("mc.world.title"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	
		return true;
	}

	private static string VersionOrUnknown(MinecraftWorld world) =>
		world.LastPlayedVersion.Length > 0 ? world.LastPlayedVersion : Loc.T("mc.world.versionUnknown");

	/// <summary>Whether a world was last played in a newer Minecraft than the pack runs. Unknown counts as no.</summary>
	private bool IsNewerThanPack(MinecraftWorld world, MinecraftPack pack)
	{
		try
		{
			string manifestPath = Path.Combine(MinecraftRootFolder(), "versions", "version_manifest_v2.json");
			if (!File.Exists(manifestPath)) return false;

			return MinecraftWorlds.IsNewer(world.LastPlayedVersion, pack.MinecraftVersion,
				JObject.Parse(File.ReadAllText(manifestPath))) == true;
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", "comparing a world's version with a pack's", ex);
			return false;
		}
	}
}
