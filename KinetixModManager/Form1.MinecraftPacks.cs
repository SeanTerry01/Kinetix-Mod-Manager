using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// Minecraft modpacks: installing one from a <c>.mrpack</c> file and starting it.
///
/// <para>
/// A pack is a whole modded setup — its own mods, its own settings, its own worlds — pinned to one Minecraft
/// version and one Fabric build. So it gets a folder of its own, and the player's own Minecraft is not touched:
/// their mods, their worlds and their Minecraft version stay exactly as they were. What the two share is only
/// what is identical anyway — the game's files, its libraries, its sounds and Java — which live in
/// <c>.minecraft</c> once rather than once per pack.
/// </para>
///
/// <para>
/// Deciding what a pack contains and whether it is safe is <see cref="MinecraftModpacks"/>, in the core, and
/// tested there. What is here is the asking, the fetching and the saying.
/// </para>
/// </summary>
public partial class Form1
{
	/// <summary>
	/// Installs a modpack from a downloaded <c>.mrpack</c> file, then offers to start it.
	///
	/// <para>
	/// The pack is built in a temporary folder and moved into place only once every file has arrived and checked
	/// out. A download that fails halfway, or a pack refused partway through, leaves nothing behind — no
	/// half-pack sitting in the list looking installed and starting without half its mods.
	/// </para>
	/// </summary>
	private async Task InstallModpackFromFileAsync(string mrpackPath)
	{
		string fileName = Path.GetFileName(mrpackPath);

		MrpackIndex index;
		OverridePlan overrides;
		try
		{
			index = await Task.Run(() => MinecraftModpacks.ReadIndex(mrpackPath));
			overrides = index.Problem == ModpackProblem.None
				? await Task.Run(() => PlanOverridesOf(mrpackPath))
				: new OverridePlan();
		}
		catch (Exception ex)
		{
			LogFailure("Minecraft", $"Could not read the modpack {fileName}", ex);
			SpeakBox(Loc.T("mc.pack.unreadable", fileName, FriendlyError(ex)), Loc.T("mc.pack.notInstalledTitle"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}

		// A bundled file with an unsafe path refuses the pack exactly as a downloaded one does.
		if (index.Problem == ModpackProblem.None && overrides.Unsafe.Count > 0)
		{
			index = new MrpackIndex
			{
				Name = index.Name, VersionId = index.VersionId,
				Problem = ModpackProblem.UnsafePath, ProblemDetail = overrides.Unsafe[0]
			};
		}

		if (index.Problem != ModpackProblem.None)
		{
			ReportModpackProblem(index, fileName);
			return;
		}

		string root = MinecraftRootFolder();
		string name = index.Name.Length > 0 ? index.Name : Path.GetFileNameWithoutExtension(mrpackPath);

		// Which Modrinth pack this file is, when it is one — quietly, and not a reason to stop when it is not.
		// See ModrinthService.IdentifyFileAsync for why a pack from a file is worth linking at all.
		(string ProjectId, string VersionId)? identified = null;
		try
		{
			string sha1 = await Task.Run(() => ModrinthService.Sha1Of(mrpackPath));
			identified = await ModrinthService.IdentifyFileAsync(sha1);
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Minecraft", "identifying the modpack on Modrinth", ex); }

		string packsFolder = MinecraftModpacks.PacksFolder;
		MinecraftPack? same = MinecraftModpacks.SamePack(
			MinecraftModpacks.FindInstalled(packsFolder), name, identified?.ProjectId ?? "");
		if (same != null &&
			SpeakBox(Loc.T("mc.pack.alreadyInstalled", same.Name, same.PackVersion), Loc.T("mc.pack.confirmTitle"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			Speak(Loc.T("common.changesCancelled"));
			return;
		}

		// One question, with everything in it that decides the answer. Whether Minecraft itself will also have to
		// be downloaded is part of that — though its size is not known until Mojang has been asked, which is why
		// that download asks again, with a number, when it is big.
		bool gameOnDisk = MinecraftLauncher.ReadVersionJson(root, index.MinecraftVersion) != null;
		string question = Loc.T(gameOnDisk ? "mc.pack.confirm" : "mc.pack.confirmWithGame",
			name, index.VersionId, index.MinecraftVersion, index.LoaderVersion,
			index.Files.Count + overrides.Files.Count, FormatBytes(index.DownloadBytes));

		if (SpeakBox(question, Loc.T("mc.pack.confirmTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question)
			!= DialogResult.Yes)
		{
			Speak(Loc.T("common.changesCancelled"));
			return;
		}

		// Minecraft itself, and the exact Fabric build the pack was made on — both shared, both in .minecraft.
		// Fabric is written WITHOUT a launcher installation: see FabricInstaller.WriteVersionJsonAsync.
		if (!await EnsureMinecraftVersionAsync(root, index.MinecraftVersion)) return;

		try
		{
			await FabricInstaller.WriteVersionJsonAsync(root, index.MinecraftVersion, index.LoaderVersion);
		}
		catch (Exception ex)
		{
			LogFailure("Minecraft", $"Could not install Fabric {index.LoaderVersion} for {index.MinecraftVersion}", ex);
			SpeakBox(Loc.T("mc.pack.fabricFailed", name, index.LoaderVersion, FriendlyError(ex)),
				Loc.T("mc.pack.notInstalledTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}

		MinecraftPack? pack = await BuildPackAsync(mrpackPath, index, overrides, name, identified, packsFolder);
		if (pack is null) return;

		await OfferToPlayNewPackAsync(pack);
	}

	/// <summary>
	/// Makes sure a pack can be started: its folder is still there, and its Fabric build is installed. Returns
	/// false, having said why, when it cannot.
	///
	/// The Fabric build is put back rather than complained about when it is missing. It lives in the shared
	/// <c>.minecraft</c>, where the official launcher or a tidy-up can remove it, and it is a few kilobytes
	/// fetched from Fabric's own site — not something worth making the player do by hand.
	/// </summary>
	private async Task<bool> PrepareModpackForLaunchAsync(string root, MinecraftPack pack)
	{
		if (!Directory.Exists(pack.Folder))
		{
			SpeakBox(Loc.T("mc.pack.folderMissing", pack.Name, pack.Folder), Loc.T("mc.launch.failedTitle"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return false;
		}

		if (MinecraftLauncher.ReadVersionJson(root, pack.FabricVersionId) != null) return true;

		try
		{
			await FabricInstaller.WriteVersionJsonAsync(root, pack.MinecraftVersion, pack.LoaderVersion);
			return true;
		}
		catch (Exception ex)
		{
			LogFailure("Minecraft", $"Could not reinstall Fabric {pack.LoaderVersion} for the pack {pack.Name}", ex);
			SpeakBox(Loc.T("mc.pack.fabricFailed", pack.Name, pack.LoaderVersion, FriendlyError(ex)),
				Loc.T("mc.launch.failedTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return false;
		}
	}

	/// <summary>Reads the zip's entry names and plans where its bundled files go.</summary>
	private static OverridePlan PlanOverridesOf(string mrpackPath)
	{
		using var zip = System.IO.Compression.ZipFile.OpenRead(mrpackPath);
		return MinecraftModpacks.PlanOverrides(zip.Entries.Select(e => e.FullName).ToList());
	}

	/// <summary>
	/// Downloads a pack's files and lays down its bundled ones in a temporary folder, writes its record, and moves
	/// it into place. Returns the installed pack, or <c>null</c> when it did not install — having said why.
	/// </summary>
	private async Task<MinecraftPack?> BuildPackAsync(string mrpackPath, MrpackIndex index, OverridePlan overrides,
		string name, (string ProjectId, string VersionId)? identified, string packsFolder)
	{
		string folderName = MinecraftModpacks.UniqueFolderName(name, n =>
			Directory.Exists(Path.Combine(packsFolder, n)) ||
			Directory.Exists(MinecraftModpacks.StagingFolderFor(packsFolder, n)));
		string staging = MinecraftModpacks.StagingFolderFor(packsFolder, folderName);
		string final = Path.Combine(packsFolder, folderName);

		try
		{
			Directory.CreateDirectory(staging);

			string opening = Loc.T("mc.pack.downloading", name, index.Files.Count, FormatBytes(index.DownloadBytes));
			Speak(opening);
			SetStatus(opening, speak: false);

			List<MinecraftFetch> wanted = index.Files
				.Select(f => new MinecraftFetch(f.Url, Path.Combine(staging, f.RelativePath), f.Sha1, f.Size))
				.ToList();

			List<string> failed = await FetchAllAsync(wanted, name);
			if (failed.Count > 0)
			{
				DiscardStaging(staging);
				SpeakBox(Loc.T("mc.pack.downloadFailed", name, failed.Count, string.Join(", ", failed.Take(5))),
					Loc.T("mc.pack.notInstalledTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return null;
			}

			IReadOnlyList<string> bundled =
				await Task.Run(() => MinecraftModpacks.ExtractOverrides(mrpackPath, staging, overrides));

			var pack = new MinecraftPack
			{
				Folder = staging,
				Name = name,
				Summary = index.Summary,
				PackVersion = index.VersionId,
				MinecraftVersion = index.MinecraftVersion,
				LoaderVersion = index.LoaderVersion,
				ModrinthProjectId = identified?.ProjectId ?? "",
				ModrinthVersionId = identified?.VersionId ?? "",
				SourceFileName = Path.GetFileName(mrpackPath),
				InstalledUtc = DateTime.UtcNow,
				ProvidedFiles = index.Files.Select(f => f.RelativePath)
					.Concat(bundled)
					.Select(p => p.Replace(Path.DirectorySeparatorChar, '/'))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToList(),
				FileProjects = index.Files
					.Select(f => (Path: MinecraftModpacks.RecordPath(f.RelativePath), Project: MinecraftModpacks.ProjectFromUrl(f.Url)))
					.Where(f => f.Project.Length > 0)
					.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
					.ToDictionary(g => g.Key, g => g.First().Project, StringComparer.OrdinalIgnoreCase)
			};
			MinecraftModpacks.Save(pack);

			Directory.Move(staging, final);
			pack.Folder = final;
			return pack;
		}
		catch (Exception ex)
		{
			LogFailure("Minecraft", $"Could not install the modpack {name}", ex);
			DiscardStaging(staging);
			SpeakBox(Loc.T("mc.pack.installFailed", name, FriendlyError(ex)), Loc.T("mc.pack.notInstalledTitle"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return null;
		}
		finally
		{
			ResetStatus();
		}
	}

	/// <summary>Removes a pack that did not finish installing. Never throws: a leftover is logged, not fatal.</summary>
	private static void DiscardStaging(string staging)
	{
		try
		{
			if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Minecraft", $"removing the unfinished pack {staging}", ex); }
	}

	/// <summary>
	/// Says the pack is in, says whether it will speak, and offers to start it — as one question, because a
	/// message box silences whatever was said just before it.
	///
	/// Whether it will speak is the first thing a blind player needs to know about a pack, and the one thing the
	/// pack's own page may not make clear: a pack with no accessibility mod installs, starts and plays perfectly,
	/// and says nothing at all.
	/// </summary>
	/// <param name="offerWorldCopy">
	/// Whether to offer one of the player's worlds. Not for a pack imported from the Modrinth App, which brings its
	/// own worlds with it.
	/// </param>
	private async Task OfferToPlayNewPackAsync(MinecraftPack pack, bool offerWorldCopy = true)
	{
		IReadOnlyList<MinecraftSuiteMod> accessMods = await Task.Run(() => AccessModsInPack(pack));
		await RefreshMinecraftPacksListAsync();

		string installed = accessMods.Count == 0
			? Loc.T("mc.pack.installedSilent", pack.Name, pack.MinecraftVersion)
			: Loc.T("mc.pack.installed", pack.Name, pack.MinecraftVersion,
				accessMods.Count == 1
					? accessMods[0].DisplayName
					: Loc.T("common.listAnd", accessMods[0].DisplayName, accessMods[1].DisplayName));

		// The worlds question rides on the "installed" message when there are worlds to offer, so the player hears
		// what matters most — whether the pack will speak — in the same breath, and is asked two things, not three.
		bool offeredWorlds = offerWorldCopy && await OfferToCopyWorldAsync(pack, askFirst: true, preamble: installed);
		string playQuestion = offeredWorlds
			? Loc.T("mc.pack.playNow", pack.Name)
			: installed + "\n\n" + Loc.T("mc.pack.playNow", pack.Name);

		if (SpeakBox(playQuestion, Loc.T("mc.pack.installedTitle"), MessageBoxButtons.YesNo,
				accessMods.Count == 0 && !offeredWorlds ? MessageBoxIcon.Warning : MessageBoxIcon.Question) == DialogResult.Yes)
			await LaunchMinecraftAsync(pack);
	}

	/// <summary>The accessibility mods switched on in a pack, read from the jars themselves.</summary>
	private static IReadOnlyList<MinecraftSuiteMod> AccessModsInPack(MinecraftPack pack)
	{
		if (!Directory.Exists(pack.ModsFolder)) return Array.Empty<MinecraftSuiteMod>();

		IEnumerable<string> ids = Directory.EnumerateFiles(pack.ModsFolder, "*" + MinecraftLayout.ModExtension)
			.Select(jar =>
			{
				try { return MinecraftLayout.ReadModInfo(jar).Id; }
				catch (Exception ex)
				{
					DiagnosticLog.WriteException("Minecraft", $"reading {Path.GetFileName(jar)}", ex);
					return "";
				}
			});

		return MinecraftModpacks.AccessModsAmong(ids);
	}

	/// <summary>Says why a pack cannot be installed, in the words that fit the reason.</summary>
	private void ReportModpackProblem(MrpackIndex index, string fileName)
	{
		string name = index.Name.Length > 0 ? index.Name : fileName;

		string message = index.Problem switch
		{
			ModpackProblem.NotAPack => Loc.T("mc.pack.problem.notAPack", fileName),
			ModpackProblem.UnsupportedFormat => Loc.T("mc.pack.problem.format", name),
			ModpackProblem.NotMinecraft => Loc.T("mc.pack.problem.notMinecraft", name),
			ModpackProblem.NoMinecraftVersion => Loc.T("mc.pack.problem.noVersion", name),
			ModpackProblem.UnsupportedLoader => Loc.T("mc.pack.problem.loader", name, index.ProblemDetail),
			ModpackProblem.NoLoader => Loc.T("mc.pack.problem.noLoader", name),
			ModpackProblem.UnsafePath => Loc.T("mc.pack.problem.unsafe", name, index.ProblemDetail),
			ModpackProblem.UntrustedDownload => Loc.T("mc.pack.problem.untrusted", name, index.ProblemDetail),
			ModpackProblem.MissingChecksum => Loc.T("mc.pack.problem.checksum", name, index.ProblemDetail),
			_ => Loc.T("mc.pack.problem.notAPack", fileName)
		};

		SpeakBox(message, Loc.T("mc.pack.notInstalledTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
	}
}
