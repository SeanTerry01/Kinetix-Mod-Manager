using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// The Minecraft-specific half of the accessibility suite: finding the game, installing Fabric without its
/// exe, choosing between the two accessibility mods, and dropping jars into the mods folder.
///
/// Installing a Minecraft mod is not the usual "download a zip and unpack it" — a Fabric mod IS the file, and
/// unpacking it would destroy it. That is why these do not go through <c>InstallFromZip</c>.
/// </summary>
public partial class Form1
{
	/// <summary>
	/// The <c>.minecraft</c> folder this session is managing: whatever the user set, else wherever it is.
	///
	/// Minecraft has no install folder in the sense the other games do — no Steam library, no GOG entry, no
	/// executable to find. <c>.minecraft</c> holds the mods, the config, the saves and the logs, so it plays
	/// the part the game folder plays elsewhere and is stored in the same setting.
	/// </summary>
	private string MinecraftRootFolder()
	{
		if (!string.IsNullOrEmpty(_settings.CurrentGamePath) &&
			MinecraftLayout.LooksLikeMinecraftRoot(_settings.CurrentGamePath))
			return _settings.CurrentGamePath;

		string found = MinecraftLayout.FindRootFolder();
		return found.Length > 0 ? found : MinecraftLayout.DefaultRootFolder;
	}

	/// <summary>
	/// What the Fabric row says after "Installed": the loader build and the Minecraft version it is for.
	///
	/// Worth spelling out rather than a bare "Installed", because "Fabric is installed" and "Fabric is
	/// installed for the version you are playing" are different facts, and only the second one makes mods load.
	/// </summary>
	private string FabricStatusLine(string root)
	{
		string newest = FabricInstaller.InstalledVersionIds(root).FirstOrDefault() ?? "";
		if (newest.Length == 0) return "";

		string loader = FabricInstaller.LoaderVersionOf(newest);
		string gameVersion = FabricInstaller.GameVersionOf(newest);

		return loader.Length > 0 && gameVersion.Length > 0
			? Loc.T("mc.suite.fabricDetail", loader, gameVersion)
			: "";
	}

	/// <summary>
	/// The Minecraft version the manager is working with: the pinned one, or — when nothing has been pinned yet
	/// — whatever an existing Fabric install on disk says.
	///
	/// The fallback matters for anyone who installed Fabric before they installed this manager, which is most
	/// people who already play modded. Without it their working setup reads as "Fabric not installed", because
	/// the manager was checking against a version it had never been told.
	/// </summary>
	private string MinecraftGameVersionInUse(string root)
	{
		string pinned = _settings.MinecraftGameVersion;
		return pinned.Length > 0 ? pinned : FabricInstaller.DetectInstalledGameVersion(root);
	}

	/// <summary>
	/// Makes sure an accessibility mod has been chosen, asking the first time and remembering the answer.
	/// Returns <c>null</c> if the user backs out.
	///
	/// Asked once rather than every install, and asked as a plain "which of these two?" — the difference in
	/// what each one needs installed alongside it is the manager's problem, not the player's.
	/// </summary>
	/// <param name="alwaysAsk">
	/// Ask even when a mod has already been chosen, opening on that choice. What the suite installer does, so
	/// that the choice comes before the list built from it — and so there is a way to change it at all, which
	/// there was not: once set, this setting could only ever be read.
	/// </param>
	private MinecraftSuiteMod? EnsureAccessModChosen(bool alwaysAsk = false)
	{
		MinecraftSuiteMod? already = string.IsNullOrEmpty(_settings.MinecraftAccessModId)
			? null
			: MinecraftSuite.AccessModFor(_settings.MinecraftAccessModId);

		if (already != null && !alwaysAsk) return already;

		string? picked = ShowChoiceList(
			Loc.T("mc.chooser.title"),
			Loc.T("mc.chooser.listName"),
			MinecraftSuite.AccessModNames,
			(already ?? MinecraftSuite.Default).DisplayName,
			Loc.T("mc.chooser.hint"));

		// Escape. The existing choice is deliberately left alone rather than cleared: backing out of a question
		// is not the same as unpicking the answer given to it last time.
		MinecraftSuiteMod? chosen = MinecraftSuite.AccessModByDisplayName(picked);
		if (chosen is null) return null;

		_settings.MinecraftAccessModId = chosen.Id;
		_settings.Save();
		return chosen;
	}

	/// <summary>
	/// Installs Fabric for the pinned Minecraft version, pinning one first if there isn't one. Returns true
	/// when the loader is in place afterwards.
	/// </summary>
	private async Task<bool> InstallFabricAsync(string root, MinecraftSuiteMod accessMod)
	{
		// ⚠️ The launcher rewrites its own profile list when it closes, so anything written underneath it is
		// discarded. Better to say so than to appear to succeed and change nothing.
		if (FabricInstaller.IsLauncherRunning())
		{
			Speak(Loc.T("mc.fabric.closeLauncherSpeak"));
			SpeakBox(Loc.T("mc.fabric.closeLauncherBox"), Loc.T("mc.fabric.closeLauncherTitle"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return false;
		}

		try
		{
			string gameVersion = _settings.MinecraftGameVersion;
			if (gameVersion.Length == 0)
			{
				gameVersion = await ChooseMinecraftVersionAsync(accessMod);
				if (gameVersion.Length == 0) return false;

				_settings.MinecraftGameVersion = gameVersion;
				_settings.Save();
			}

			// Before Fabric, because Fabric is a profile that inherits from the game and a profile inheriting
			// from a version that is not there is exactly the broken setup this used to produce. See
			// EnsureMinecraftVersionAsync: on a version the player already has, this costs no network call.
			if (!await EnsureMinecraftVersionAsync(root, gameVersion)) return false;

			// speak: false, because Speak says the same sentence on the next line. SetStatus speaks by default,
			// so this pair announced "Installing Fabric for Minecraft 1.19.2..." twice, back to back.
			SetStatus(Loc.T("mc.fabric.installing", gameVersion), speak: false);
			Speak(Loc.T("mc.fabric.installing", gameVersion));

			await FabricInstaller.InstallAsync(root, gameVersion);

			// The Play button follows the most recently used installation, and the Quick Play tiles carry their
			// own profile reference that ignores it entirely. Both are claimed here — see FabricInstaller for
			// what happens when they are not.
			FabricInstaller.RepointQuickPlay(root, FabricInstaller.ProfileKeyFor(gameVersion));

			Speak(Loc.T("mc.fabric.installed", gameVersion));
			return true;
		}
		catch (Exception ex)
		{
			LogFailure("Fabric", "Failed to install the Fabric loader", ex);
			return false;
		}
	}

	/// <summary>
	/// The newest Minecraft version that BOTH Fabric and the chosen accessibility mod support.
	///
	/// Not simply Fabric's newest stable: Fabric is usually ready for a new Minecraft version well before the
	/// mods are, and installing for a version the access mod has no build for produces a setup that launches
	/// perfectly and never speaks. The accessibility mod is the binding constraint, so it decides.
	/// </summary>
	private async Task<string> ChooseMinecraftVersionAsync(MinecraftSuiteMod accessMod)
	{
		string fabricNewest = await FabricInstaller.GetLatestStableGameVersionAsync();

		// A GitHub-released mod does not publish a machine-readable version list, so Fabric's newest stable is
		// the best available answer for United Minecraft; its release tags carry the Minecraft version, which
		// the update check reads separately.
		if (accessMod.Origin != MinecraftModOrigin.Modrinth) return fabricNewest;

		IReadOnlyList<string> supported = await ModrinthService.GetSupportedGameVersionsAsync(accessMod.Source);
		if (supported.Contains(fabricNewest)) return fabricNewest;

		string best = supported.FirstOrDefault() ?? fabricNewest;
		if (best != fabricNewest)
			Speak(Loc.T("mc.fabric.pinnedOlder", accessMod.DisplayName, best));

		return best;
	}

	/// <summary>
	/// Puts one suite mod's jar into the mods folder. Returns true when the file is there afterwards.
	///
	/// ⚠️ The jar is copied, not extracted. A Fabric mod IS the .jar file — unpacking it, which is what every
	/// other game's install path does with a download, would leave a folder of loose classes that Fabric walks
	/// straight past.
	/// </summary>
	/// <param name="noBuildFor">
	/// Where to collect the names of mods with no build for this Minecraft version, instead of interrupting
	/// with a message box. The suite installer passes one; a caller installing a single mod passes nothing and
	/// gets told there and then, which is right when there is no sequence to interrupt.
	/// </param>
	private async Task<bool> InstallMinecraftModAsync(
		MinecraftSuiteMod mod, string root, List<string>? noBuildFor = null)
	{
		string modsFolder = MinecraftLayout.ModsFolderFor(root);
		Directory.CreateDirectory(modsFolder);

		try
		{
			// ⚠️ Nothing is announced here any more. Whether there IS a build is settled first, inside the two
			// methods below, and only then is a download announced — because "Downloading United Minecraft..."
			// followed a moment later by "United Minecraft has no build for this version" is a sentence the
			// screen reader never finishes. A message box takes focus, and the reader abandons whatever it was
			// halfway through to read the dialog, so the announcement and the step before it were both lost.
			string installed = mod.Origin == MinecraftModOrigin.Modrinth
				? await InstallModrinthJarAsync(mod, modsFolder, noBuildFor)
				: await InstallGitHubJarAsync(mod, modsFolder, noBuildFor);

			if (installed.Length == 0) return false;

			Speak(Loc.T("mc.install.done", mod.DisplayName));
			return true;
		}
		catch (Exception ex)
		{
			LogFailure(mod.DisplayName, "Failed to install the mod", ex);
			return false;
		}
	}

	/// <summary>Says a download is starting — only ever called once there is genuinely something to download.</summary>
	private void AnnounceSuiteDownload(MinecraftSuiteMod mod)
	{
		// speak: false — see InstallFabricAsync. Every suite mod was announced twice for the same reason.
		SetStatus(Loc.T("suite.downloading", mod.DisplayName), speak: false);
		Speak(Loc.T("suite.downloading", mod.DisplayName));
	}

	/// <summary>
	/// Records, or says, that a mod has no build for the Minecraft version in use.
	///
	/// Collected rather than announced when the caller is working through a list: one box at the end of the
	/// run says the same thing without cutting the reader off partway through the step before it.
	/// </summary>
	private void ReportNoBuild(MinecraftSuiteMod mod, List<string>? noBuildFor)
	{
		if (noBuildFor != null) { noBuildFor.Add(mod.DisplayName); return; }

		Speak(Loc.T("mc.install.noBuildSpeak", mod.DisplayName, _settings.MinecraftGameVersion));
		SpeakBox(
			Loc.T("mc.install.noBuildBox", mod.DisplayName, _settings.MinecraftGameVersion),
			Loc.T("mc.install.noBuildTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
	}

	/// <summary>Fetches a Modrinth project's newest build for the pinned Minecraft version.</summary>
	private async Task<string> InstallModrinthJarAsync(
		MinecraftSuiteMod mod, string modsFolder, List<string>? noBuildFor)
	{
		ModrinthFile? file = await ModrinthService.GetLatestFileAsync(mod.Source, _settings.MinecraftGameVersion);

		// Reported rather than swallowed: "there is no build of this for the version you are on" is something
		// the user can act on, and installing a build for another version would not work.
		if (file is null) { ReportNoBuild(mod, noBuildFor); return ""; }

		AnnounceSuiteDownload(mod);
		return await ModrinthService.DownloadAsync(file, modsFolder);
	}

	/// <summary>
	/// Fetches the newest <c>.jar</c> asset from a GitHub repository's latest release.
	///
	/// Deliberately the .jar and not the source zip GitHub always attaches: a release carries both, and
	/// grabbing the wrong one puts a folder of source code in the mods folder.
	/// </summary>
	private async Task<string> InstallGitHubJarAsync(
		MinecraftSuiteMod mod, string modsFolder, List<string>? noBuildFor)
	{
		string json = await KinetixHttp.Api.GetStringAsync($"https://api.github.com/repos/{mod.Source}/releases/latest");
		Newtonsoft.Json.Linq.JObject release = Newtonsoft.Json.Linq.JObject.Parse(json);

		// ⚠️ The version gate the Modrinth path has always had, and this one never did.
		//
		// Found by installing the suite into a bare 1.19.2 folder: Fabric API arrived as 0.77.0+1.19.2,
		// correctly, and United Minecraft arrived as 1.2.0+mc26.3 — the newest release, built for a Minecraft
		// seven versions later. A mod built for the wrong version does not degrade anything; Fabric refuses to
		// start the game and names it, which is the wall of Java text this whole game's support exists to
		// spare the player. MinecraftSuite.ReleaseIsForGameVersion already existed for the update check.
		if (!MinecraftSuite.ReleaseIsForGameVersion(GitHubReleases.Parse(json), _settings.MinecraftGameVersion))
		{
			ReportNoBuild(mod, noBuildFor);
			return "";
		}

		var jar = (release["assets"] as Newtonsoft.Json.Linq.JArray)?
			.FirstOrDefault(a => ((string?)a["name"] ?? "")
				.EndsWith(MinecraftLayout.ModExtension, StringComparison.OrdinalIgnoreCase));

		string? url = (string?)jar?["browser_download_url"];
		string? name = (string?)jar?["name"];

		if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(name))
		{
			Speak(Loc.T("mc.install.noJarSpeak", mod.DisplayName));
			return "";
		}

		AnnounceSuiteDownload(mod);

		string path = Path.Combine(modsFolder, name!);
		// The jar itself goes through the download client: a mod is large enough to deserve the long timeout.
		File.WriteAllBytes(path, await KinetixHttp.Downloads.GetByteArrayAsync(url));
		return path;
	}

	/// <summary>
	/// Where a Fabric mod keeps its settings, or <c>""</c> when it has none.
	///
	/// ⚠️ Outside the mod, like a BepInEx plugin and unlike everything else. A Fabric mod is a single jar with
	/// nowhere inside it to write to, so its settings go to <c>.minecraft\config\</c> — which means the usual
	/// "look for a config beside the mod" finds nothing and reports a mod as having no settings when it has
	/// two dozen.
	///
	/// Two shapes are used in the wild: one file named for the mod, and a folder of them. Both are looked for,
	/// the single file first because that is what a mod with a handful of options does.
	/// </summary>
	private string FindMinecraftConfigFor(GameMod mod)
	{
		string configFolder = MinecraftLayout.ConfigFolderFor(MinecraftRootFolder());
		if (!Directory.Exists(configFolder) || string.IsNullOrEmpty(mod.UniqueId)) return "";

		string single = Path.Combine(configFolder, mod.UniqueId + ".json");
		if (File.Exists(single)) return single;

		// A mod with more settings splits them into its own folder. Its main file is usually named for the mod
		// or simply "config.json"; failing both, the only .json in there is unambiguous enough to offer.
		string folder = Path.Combine(configFolder, mod.UniqueId);
		if (!Directory.Exists(folder)) return "";

		foreach (string candidate in new[] { mod.UniqueId + ".json", "config.json" })
		{
			string path = Path.Combine(folder, candidate);
			if (File.Exists(path)) return path;
		}

		string[] jsons = Directory.GetFiles(folder, "*.json");
		return jsons.Length == 1 ? jsons[0] : "";
	}

	/// <summary>
	/// The newest version of an installed Minecraft mod, or <c>null</c> when Modrinth has nothing to say
	/// about it.
	///
	/// <para>
	/// Identified by the SHA-1 of the jar itself, which is a better answer than any other update check in this
	/// manager gets. Everywhere else the manager needs a catalogue id, guesses one from a download's file name,
	/// or matches on the mod's name and hopes — and name matching is what once installed a six-week-old build
	/// over a working one. A hash is exact: this file IS that release of that project, or Modrinth has never
	/// seen it.
	/// </para>
	///
	/// <para>
	/// ⚠️ Null means "no answer", NOT "up to date". A mod Modrinth does not host — United Minecraft is on
	/// GitHub only — is absent from the reply, and reading that as "nothing newer" would quietly stop checking
	/// the one mod the player most needs kept current.
	/// </para>
	/// </summary>
	private async Task<string?> LatestModrinthVersionAsync(GameMod mod)
	{
		string jar = mod.FolderPath;
		if (string.IsNullOrEmpty(jar) || !File.Exists(jar)) return null;

		string gameVersion = MinecraftGameVersionInUse(MinecraftRootFolder());
		if (gameVersion.Length == 0) return null;

		try
		{
			string hash = ModrinthService.Sha1Of(jar);
			Dictionary<string, ModrinthFile> latest =
				await ModrinthService.GetLatestForHashesAsync(new[] { hash }, gameVersion);

			return latest.TryGetValue(hash, out ModrinthFile? file) ? file.VersionNumber : null;
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Modrinth", $"checking for updates to {mod.Name}", ex);
			return null;
		}
	}

	/// <summary>The Updates row that stands for the Fabric loader rather than for a mod.</summary>
	internal const string FabricLoaderRowId = "kinetix:fabric-loader";

	/// <summary>The Updates row that stands for a newer Minecraft version being available.</summary>
	internal const string MinecraftVersionRowId = "kinetix:minecraft-version";

	/// <summary>
	/// Checks the two things a Minecraft player depends on that are not mods: the Fabric loader, and the Minecraft
	/// version itself. Runs as a unit of the update check, so the run's completion cue waits for it like any other.
	///
	/// <para>
	/// Neither was ever checked. The loader is what makes the game load mods at all, and a new Minecraft version is
	/// the event that makes every one of them need a new build — so a player who heard "no updates" was being told
	/// about the mods and nothing about the ground they stand on.
	/// </para>
	///
	/// <para>
	/// The rows are ordinary update rows carrying a reserved id, so Delete ignores one and Update All takes them
	/// both, exactly as for a mod. <see cref="DownloadAndInstallUpdate"/> recognises the ids and does the right
	/// thing instead of trying to install a jar.
	/// </para>
	/// </summary>
	private async Task CheckMinecraftPlatformUpdatesAsync()
	{
		try
		{
			string root = MinecraftRootFolder();
			if (root.Length == 0) return;

			string inUse = MinecraftGameVersionInUse(root);
			if (inUse.Length == 0) return;

			try
			{
				string installed = InstalledLoaderVersionFor(root, inUse);
				string newest = await FabricInstaller.GetLatestStableLoaderVersionAsync(inUse);
				if (installed.Length > 0 && ModVersions.IsNewer(installed, newest))
					AddPlatformUpdateRow(FabricLoaderRowId, Loc.T("mc.update.loaderName"), Loc.T("mc.update.loaderAuthor"), installed, newest);
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Fabric", $"asking for the newest loader for Minecraft {inUse}", ex); }

			try
			{
				string newestGame = await FabricInstaller.GetLatestStableGameVersionAsync();
				// Offered only once the accessibility mod has a build for it. Fabric is ready for a new Minecraft
				// version well before the mods are, and moving early gives a game that launches and says nothing.
				if (ModVersions.IsNewer(inUse, newestGame) && await AccessModSupportsAsync(newestGame))
				{
					AddPlatformUpdateRow(MinecraftVersionRowId, Loc.T("mc.update.gameName"), Loc.T("mc.update.gameAuthor"), inUse, newestGame);

					// Held rather than spoken here, so it lands after the check's own "N updates" line instead
					// of interrupting a run that is still going. See CompleteUpdateCheckUnit, which does the
					// same for SMAPI.
					if (_minecraftUpdateAlreadyOffered != newestGame)
						_pendingMinecraftUpdate = (inUse, newestGame);
				}
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Fabric", "asking for the newest Minecraft version", ex); }
		}
		finally
		{
			CompleteUpdateCheckUnit();
		}
	}

	/// <summary>The loader version installed for <paramref name="gameVersion"/>, newest if there are several.</summary>
	private static string InstalledLoaderVersionFor(string root, string gameVersion)
	{
		string best = "";
		foreach (string id in FabricInstaller.InstalledVersionIds(root))
		{
			if (!string.Equals(FabricInstaller.GameVersionOf(id), gameVersion, StringComparison.OrdinalIgnoreCase)) continue;

			string loader = FabricInstaller.LoaderVersionOf(id);
			if (loader.Length > 0 && (best.Length == 0 || ModVersions.IsNewer(best, loader))) best = loader;
		}
		return best;
	}

	/// <summary>
	/// Fetches any of Minecraft's own libraries that are missing before the game is started, and says whether the
	/// launch can go ahead.
	///
	/// <para>
	/// The manager starts the game itself, and assumed every file it needs was already on disk — true only because
	/// the official launcher had fetched them at some point. After a move to a Minecraft version the official
	/// launcher had never played, one library was missing and the game died on <c>NoClassDefFoundError</c>: a Java
	/// stack trace naming a class, with nothing to connect it to a file, a version, or anything the player did.
	/// The official launcher repairs this on every single launch, quietly. So does this now.
	/// </para>
	///
	/// <para>
	/// Each file is checked against the checksum the version file gives before it is kept. A library that arrives
	/// corrupted would fail in exactly the same unreadable way as one that is missing.
	/// </para>
	/// </summary>
	private async Task<bool> FetchMissingLibrariesAsync(string root, string versionId)
	{
		IReadOnlyList<MinecraftLauncher.MissingLibrary> missing;
		try
		{
			Newtonsoft.Json.Linq.JObject? resolved = MinecraftLauncher.ResolveVersion(root, versionId);
			if (resolved == null) return true;   // BuildPlan reports this properly a moment later

			missing = MinecraftLauncher.MissingLibraries(resolved, Path.Combine(root, "libraries"),
				MinecraftLauncher.CurrentOsName, MinecraftLauncher.CurrentOsArch, File.Exists);
		}
		catch (Exception ex)
		{
			// Never a reason to refuse to start a game that may well be fine.
			DiagnosticLog.WriteException("Minecraft", "checking which of the game's libraries are present", ex);
			return true;
		}

		if (missing.Count == 0) return true;

		Speak(Loc.T("mc.launch.fetchingLibraries", missing.Count));
		SetStatus(Loc.T("mc.launch.fetchingLibraries", missing.Count), speak: false);

		var failed = new List<string>();
		foreach (MinecraftLauncher.MissingLibrary library in missing)
		{
			string destination = Path.Combine(root, "libraries", library.RelativePath);
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
				await _nexusService.DownloadFileWithProgressAsync(library.Url, destination, null);

				if (!string.IsNullOrEmpty(library.Sha1) && !FileHasSha1(destination, library.Sha1!))
				{
					File.Delete(destination);
					failed.Add(Path.GetFileName(library.RelativePath));
				}
			}
			catch (Exception ex)
			{
				LogFailure("Minecraft", $"Could not fetch {library.RelativePath}", ex);
				failed.Add(Path.GetFileName(library.RelativePath));
			}
		}

		ResetStatus();
		if (failed.Count == 0) return true;

		// Said plainly, and the launch is stopped: starting anyway produces a Java stack trace about a missing
		// class, which tells the player nothing about what is wrong or what to do.
		Speak(Loc.T("mc.launch.librariesFailedSpeak", failed.Count));
		SpeakBox(Loc.T("mc.launch.librariesFailedBox", string.Join(", ", failed)),
			Loc.T("mc.launch.librariesFailedTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
		return false;
	}

	/// <summary>Whether a file on disk matches the checksum the version file published for it.</summary>
	private static bool FileHasSha1(string path, string expected)
	{
		try
		{
			using FileStream stream = File.OpenRead(path);
			byte[] hash = System.Security.Cryptography.SHA1.HashData(stream);
			return Convert.ToHexString(hash).Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", $"checking {path}", ex);
			return false;
		}
	}

	/// <summary>
	/// The accessibility mod this setup actually runs: the one in the mods folder, whatever the settings say.
	///
	/// <para>
	/// The setting is only written when the manager installs an access mod through the suite installer, so anyone
	/// who put one there themselves has it empty — and the gate that decides whether a newer Minecraft version is
	/// safe was then asking about the DEFAULT mod rather than the one they depend on. That is the one question in
	/// this game where being wrong means a game that launches and never speaks.
	/// </para>
	/// </summary>
	private MinecraftSuiteMod ActiveAccessMod()
	{
		foreach (MinecraftSuiteMod candidate in MinecraftSuite.AccessMods)
			if (_allInstalledMods.Any(m => !m.IsGroup &&
					string.Equals(m.UniqueId, candidate.FabricModId, StringComparison.OrdinalIgnoreCase)))
				return candidate;

		return MinecraftSuite.AccessModFor(_settings.MinecraftAccessModId);
	}

	/// <summary>Whether the accessibility mod in use has a build for <paramref name="gameVersion"/>.</summary>
	private async Task<bool> AccessModSupportsAsync(string gameVersion)
	{
		MinecraftSuiteMod access = ActiveAccessMod();

		try
		{
			if (access.Origin == MinecraftModOrigin.Modrinth)
				return (await ModrinthService.GetSupportedGameVersionsAsync(access.Source))
					.Contains(gameVersion, StringComparer.OrdinalIgnoreCase);

			// A GitHub release publishes no version list, so its tag and file names are the only answer available.
			using var req = new HttpRequestMessage(HttpMethod.Get, GitHubReleases.LatestReleaseApiUrl(access.Source));
			req.Headers.UserAgent.ParseAdd($"KinetixModManager/{NexusService.AppVersion}");
			using HttpResponseMessage resp = await NexusService.HttpClient.SendAsync(req);
			if (!resp.IsSuccessStatusCode) return false;

			return MinecraftSuite.ReleaseIsForGameVersion(
				GitHubReleases.Parse(await resp.Content.ReadAsStringAsync()), gameVersion);
		}
		catch (Exception ex)
		{
			// Not knowing is not the same as knowing it works. An unanswered question leaves the move unoffered.
			DiagnosticLog.WriteException("Minecraft", $"asking whether {access.DisplayName} supports Minecraft {gameVersion}", ex);
			return false;
		}
	}

	/// <summary>
	/// A new Minecraft version found by the last check, waiting to be offered once the check has finished
	/// speaking. Mirrors the SMAPI one; see <c>CompleteUpdateCheckUnit</c>.
	/// </summary>
	private (string Current, string Latest)? _pendingMinecraftUpdate;

	/// <summary>
	/// The version already offered this session, so declining is not re-asked on every later check.
	///
	/// Deliberately not the persistent ignore list, which is what Delete on the row means and which suppresses
	/// the row itself. Saying "not now" to a prompt is a smaller thing than saying "never mention this
	/// version": the row stays in the Updates list, and Enter on it still moves.
	/// </summary>
	private string _minecraftUpdateAlreadyOffered = "";

	/// <summary>
	/// Offers a new Minecraft version after an update check, naming the version in use as well as the new one.
	///
	/// <para>
	/// The Updates list already carried a Minecraft row, and Sean asked for this because a row is something you
	/// have to go and look at. An update to the game is not like an update to a mod — it is the event that
	/// decides whether every mod he depends on still loads — so it is worth saying out loud, unprompted, and
	/// worth saying which version he is on rather than only which one is new.
	/// </para>
	///
	/// <para>
	/// Saying yes goes through the same move as the row does, which asks its own question afterwards: this one
	/// cannot name the mods that would be switched off, because working that out takes a Modrinth lookup per
	/// mod. So this asks whether to look at the move at all, and the move itself asks whether to go through
	/// with it once it can say exactly what would happen.
	/// </para>
	/// </summary>
	private async void NotifyMinecraftUpdateAvailable(string current, string latest)
	{
		_minecraftUpdateAlreadyOffered = latest;

		Speak(Loc.T("mc.update.availableSpeak", current, latest));
		if (SpeakBox(Loc.T("mc.update.availableBox", current, latest),
				Loc.T("mc.update.availableTitle", latest),
				MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
		{
			Speak(Loc.T("mc.update.availableLater", latest));
			return;
		}

		if (!await MoveToMinecraftVersionAsync(latest, silent: false)) return;

		// Every mod's row was checked against the version just left, so none of them mean anything now.
		Invoke(delegate { listUpdates.Items.Clear(); });
	}

	/// <summary>Puts one of the two platform rows into the Updates list, unless it is there or has been ignored.</summary>
	private void AddPlatformUpdateRow(string rowId, string name, string author, string installed, string latest)
	{
		if (_settings.IgnoredVersions.TryGetValue(rowId, out string? ignored) && ignored == latest) return;

		Invoke(delegate
		{
			foreach (object item in listUpdates.Items)
				if (item is GameMod existing && existing.UniqueId == rowId) return;

			listUpdates.Items.Add(new GameMod
			{
				UniqueId = rowId,
				Name = name,
				Author = author,
				Version = installed,
				LatestVersion = latest,
				IsUpdateResult = true
			});
		});
	}

	/// <summary>
	/// Installs the newest Fabric loader for the Minecraft version in use. The pinned version does not change, so
	/// every installed mod goes on working: a loader update is the safe half of keeping Fabric current.
	/// </summary>
	private async Task<bool> UpdateFabricLoaderAsync()
	{
		string root = MinecraftRootFolder();
		return root.Length > 0 && await InstallFabricAsync(root, ActiveAccessMod());
	}

	/// <summary>
	/// Moves the setup to a newer Minecraft version: Fabric is installed for it, and every mod that has a build for
	/// it is updated.
	///
	/// <para>
	/// The warning is the point. A mod with no build for the new version is not removed and does not error — Fabric
	/// simply does not load it, and for a mod the player's speech depends on that is a silent game. So the counts
	/// are given before anything is written, and the mods left behind are named afterwards.
	/// </para>
	/// </summary>
	private async Task<bool> MoveToMinecraftVersionAsync(string newVersion, bool silent)
	{
		string root = MinecraftRootFolder();
		if (root.Length == 0 || newVersion.Length == 0) return false;

		List<GameMod> mods = _allInstalledMods.Where(m => !m.IsGroup && !string.IsNullOrEmpty(m.FolderPath) && File.Exists(m.FolderPath)).ToList();
		Dictionary<string, ModrinthFile> available = await BuildsForVersionAsync(mods, newVersion);

		// Three outcomes, not two. A mod with a build for the new version is updated; a mod whose own declared
		// range already accepts it needs nothing; and the rest have to be switched OFF, because Fabric does not
		// skip a mod built for another version — it refuses to start the game and names every offender.
		Dictionary<string, ModrinthFile> installedBuilds = await InstalledBuildsAsync(mods);

		List<GameMod> ready = mods.Where(m => available.ContainsKey(ModrinthService.Sha1Of(m.FolderPath))).ToList();
		List<GameMod> fineAsTheyAre = mods.Except(ready)
			.Where(m => SurvivesTheMove(m, newVersion, installedBuilds))
			.ToList();
		List<GameMod> leftBehind = mods.Except(ready).Except(fineAsTheyAre).ToList();

		// Asked even in a batch: this is not an update, it is a change to what the game is.
		string question = leftBehind.Count == 0
			? Loc.T("mc.move.confirmAll", newVersion, ready.Count + fineAsTheyAre.Count)
			: Loc.T("mc.move.confirmSome", newVersion, ready.Count, leftBehind.Count,
				string.Join(", ", leftBehind.Select(m => m.Name)));
		if (SpeakBox(question, Loc.T("mc.move.title", newVersion), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
		{
			Speak(Loc.T("common.changesCancelled"));
			return false;
		}

		string pinnedBefore = _settings.MinecraftGameVersion;
		_settings.MinecraftGameVersion = newVersion;
		_settings.Save();

		if (!await InstallFabricAsync(root, ActiveAccessMod()))
		{
			// Put the pin back: a half-moved setup, pinned to a version whose loader was never installed, would
			// have the manager checking for builds the game cannot run.
			_settings.MinecraftGameVersion = pinnedBefore;
			_settings.Save();
			return false;
		}

		string modsFolder = MinecraftLayout.ModsFolderFor(root);
		int updated = 0;
		foreach (GameMod mod in ready)
		{
			try
			{
				if (!available.TryGetValue(ModrinthService.Sha1Of(mod.FolderPath), out ModrinthFile? file)) continue;

				SetStatus(Loc.T("updateAll.updatingStatus", updated + 1, ready.Count, mod.Name), speak: false);
				string downloaded = await ModrinthService.DownloadAsync(file, downloadsPath);
				if (File.Exists(mod.FolderPath)) BackupMod(mod.FolderPath, mod.Name);
				await Task.Run(() => ModInstaller.InstallFile(downloaded, modsFolder));
				RecordDownloadInstalled(downloaded);
				updated++;
			}
			catch (Exception ex) { LogFailure(mod.Name, $"Failed to update for Minecraft {newVersion}", ex); }
		}

		// Switched off rather than left to break the launch. A mod Fabric rejects takes the whole game down with
		// it, so leaving these enabled would hand back a setup that cannot start — which is precisely what
		// happened the first time this ran.
		var switchedOff = new List<string>();
		foreach (GameMod mod in leftBehind)
		{
			try
			{
				string off = MinecraftLayout.PathWithEnabled(mod.FolderPath, enable: false,
					GameProfiles.Require(GameProfiles.Minecraft).DisabledModSuffix);
				if (off != mod.FolderPath && File.Exists(mod.FolderPath)) File.Move(mod.FolderPath, off);
				switchedOff.Add(mod.Name);
			}
			catch (Exception ex) { LogFailure(mod.Name, $"Could not switch off a mod with no build for Minecraft {newVersion}", ex); }
		}

		ResetStatus();
		await RefreshModList(checkUpdates: false);

		string done = switchedOff.Count == 0
			? Loc.T("mc.move.doneAll", newVersion, updated + fineAsTheyAre.Count)
			: Loc.T("mc.move.doneSome", newVersion, updated, switchedOff.Count, string.Join(", ", switchedOff));
		Speak(done);
		if (!silent && switchedOff.Count > 0)
			SpeakBox(done, Loc.T("mc.move.title", newVersion), MessageBoxButtons.OK, MessageBoxIcon.Warning);

		return true;
	}

	/// <summary>
	/// Ctrl+Q for Minecraft: fetches the mods this one needs and has not got.
	///
	/// <para>
	/// A missing dependency is not a degraded setup here, it is a game that will not start — Fabric lists what is
	/// missing and stops. The manager had nothing to offer for it ("not supported for this game"), which left the
	/// user with an id like <c>cloth-config</c> and a web search. Modrinth is keyless and its project slugs are
	/// usually the Fabric mod id, so most of these can simply be fetched.
	/// </para>
	/// </summary>
	private async Task ResolveMinecraftDependenciesAsync(GameMod mod)
	{
		List<string> missing = mod.Dependencies
			.Where(d => d.IsRequired && !d.IsPresent)
			.Select(d => d.UniqueId)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (missing.Count == 0) { Speak(Loc.T("deps.resolveNoneMissing")); return; }

		string root = MinecraftRootFolder();
		string gameVersion = MinecraftGameVersionInUse(root);
		if (gameVersion.Length == 0) { Speak(Loc.T("mc.search.noVersionSpeak")); return; }

		if (SpeakBox(Loc.T("deps.mcConfirm", mod.Name, missing.Count, string.Join(", ", missing), gameVersion),
				Loc.T("deps.resolveTitle"), MessageBoxButtons.YesNo) != DialogResult.Yes)
		{
			Speak(Loc.T("deps.resolveCancelled"));
			return;
		}

		string modsFolder = MinecraftLayout.ModsFolderFor(root);
		var installed = new List<string>();
		var notFound = new List<string>();

		for (int i = 0; i < missing.Count; i++)
		{
			string id = missing[i];
			SetStatus(Loc.T("deps.resolveInstalling", i + 1, missing.Count, id), speak: false);
			try
			{
				ModrinthFile? file = await FindDependencyBuildAsync(id, gameVersion);
				if (file == null) { notFound.Add(id); continue; }

				string downloaded = await ModrinthService.DownloadAsync(file, downloadsPath);
				await Task.Run(() => ModInstaller.InstallFile(downloaded, modsFolder));
				RecordDownloadInstalled(downloaded);
				installed.Add(id);
			}
			catch (Exception ex)
			{
				LogFailure(id, $"Failed to install a dependency of {mod.Name}", ex);
				notFound.Add(id);
			}
		}

		ResetStatus();
		await RefreshModList(checkUpdates: false);

		// Named individually, both ways round: what arrived, and what has no build for this Minecraft version and
		// so still stands between the user and a game that starts.
		string said = notFound.Count == 0
			? Loc.T("deps.mcDone", installed.Count, string.Join(", ", installed))
			: Loc.T("deps.mcDoneSome", installed.Count, notFound.Count, string.Join(", ", notFound), gameVersion);
		Speak(said);
		SpeakBox(said, Loc.T("deps.resolveTitle"), MessageBoxButtons.OK,
			notFound.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
	}

	/// <summary>
	/// Finds a build of a dependency for this Minecraft version. A Fabric mod id and a Modrinth slug usually agree,
	/// but not always — <c>yet_another_config_lib_v3</c> is published as <c>yet-another-config-lib</c> — so the
	/// obvious spellings are tried before giving up.
	/// </summary>
	private static async Task<ModrinthFile?> FindDependencyBuildAsync(string modId, string gameVersion)
	{
		var tried = new List<string>();
		foreach (string candidate in new[] { modId, modId.Replace('_', '-'), TrimVersionSuffix(modId.Replace('_', '-')) })
		{
			if (candidate.Length == 0 || tried.Contains(candidate, StringComparer.OrdinalIgnoreCase)) continue;
			tried.Add(candidate);

			try
			{
				if (await ModrinthService.GetLatestFileAsync(candidate, gameVersion) is { } file) return file;
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Modrinth", $"looking for {candidate}", ex); }
		}

		return null;
	}

	/// <summary>Drops a trailing major-version marker from a mod id: "yet-another-config-lib-v3" to "…-lib".</summary>
	private static string TrimVersionSuffix(string id)
	{
		int cut = id.LastIndexOf("-v", StringComparison.OrdinalIgnoreCase);
		return cut > 0 && id[(cut + 2)..].All(char.IsDigit) ? id[..cut] : id;
	}

	/// <summary>
	/// Whether this mod can be left alone when the setup moves to <paramref name="gameVersion"/>.
	///
	/// <para>
	/// Two authorities, and the catalogue is the better one. A mod's own declared range is optional and plenty of
	/// mods leave it out — Toolbar Sounds declares nothing, so it loaded happily on a Minecraft version it was
	/// never built for, and then its data pack would not parse and the world refused to load. Its Modrinth entry
	/// knew it supported 26.2 and no further. So: what the catalogue says about the exact jar installed, and only
	/// where the catalogue has never heard of it, what the mod says about itself.
	/// </para>
	/// </summary>
	private static bool SurvivesTheMove(GameMod mod, string gameVersion, Dictionary<string, ModrinthFile> installedBuilds)
	{
		IReadOnlyList<string>? catalogue = null;
		try
		{
			if (!string.IsNullOrEmpty(mod.FolderPath) && File.Exists(mod.FolderPath) &&
				installedBuilds.TryGetValue(ModrinthService.Sha1Of(mod.FolderPath), out ModrinthFile? build))
				catalogue = build.GameVersions;
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Modrinth", $"reading what {mod.Name} supports", ex); }

		return FabricVersionRange.SurvivesMove(catalogue, DeclaredMinecraftRange(mod), gameVersion);
	}

	/// <summary>What the catalogue says about the exact jars installed, by hash. Empty when it cannot be asked.</summary>
	private static async Task<Dictionary<string, ModrinthFile>> InstalledBuildsAsync(List<GameMod> mods)
	{
		try
		{
			string[] hashes = mods
				.Where(m => !string.IsNullOrEmpty(m.FolderPath) && File.Exists(m.FolderPath))
				.Select(m => ModrinthService.Sha1Of(m.FolderPath))
				.Distinct()
				.ToArray();

			return await ModrinthService.GetInstalledVersionsForHashesAsync(hashes);
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Modrinth", "asking what the installed mods support", ex);
			return new Dictionary<string, ModrinthFile>();
		}
	}

	/// <summary>
	/// Whether this installed mod's own declared Minecraft range accepts <paramref name="gameVersion"/> — which is
	/// the difference between a mod that needs a new build and one that is already happy. See
	/// <see cref="FabricVersionRange"/> for what that cost when nothing asked.
	/// </summary>
	private static bool AcceptsGameVersion(GameMod mod, string gameVersion) =>
		FabricVersionRange.Accepts(DeclaredMinecraftRange(mod), gameVersion);

	/// <summary>The Minecraft range a mod declares in its own jar, or null when it declares none or cannot be read.</summary>
	private static string? DeclaredMinecraftRange(GameMod mod)
	{
		try
		{
			if (string.IsNullOrEmpty(mod.FolderPath) || !File.Exists(mod.FolderPath)) return null;

			FabricModInfo info = MinecraftLayout.ReadModInfo(mod.FolderPath);
			return info.IsUnreadable || !info.Depends.TryGetValue("minecraft", out string? range) ? null : range;
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", $"reading what {mod.Name} requires", ex);
			return null;
		}
	}

	/// <summary>Which of these installed mods Modrinth has a build of for <paramref name="gameVersion"/>, by jar hash.</summary>
	private async Task<Dictionary<string, ModrinthFile>> BuildsForVersionAsync(List<GameMod> mods, string gameVersion)
	{
		try
		{
			string[] hashes = mods.Select(m => ModrinthService.Sha1Of(m.FolderPath)).Distinct().ToArray();
			return hashes.Length == 0
				? new Dictionary<string, ModrinthFile>()
				: await ModrinthService.GetLatestForHashesAsync(hashes, gameVersion);
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Modrinth", $"asking which mods have builds for Minecraft {gameVersion}", ex);
			return new Dictionary<string, ModrinthFile>();
		}
	}

	/// <summary>
	/// Updates one installed Minecraft mod in place, and says whether it did.
	///
	/// <para>
	/// Minecraft's updates went through the Nexus path, which is wrong twice over. Nexus hosts none of these mods,
	/// so a free account was told to buy Premium to update a mod nobody was charging for; and a Fabric mod IS its
	/// jar, so the GitHub branch's "download it and extract it" left a folder of loose classes that the loader walks
	/// straight past, with the old jar still beside it.
	/// </para>
	///
	/// <para>
	/// Modrinth first, by the hash of the installed jar — the same question the update check asked — then the mod's
	/// own GitHub releases, which is where United Minecraft is published. <see cref="ModInstaller.InstallFile"/>
	/// removes the copy being replaced, including a disabled one: Fabric refuses to start when two jars declare the
	/// same mod id, and a <c>.jar.disabled</c> left behind is invisible to it but not to the manager.
	/// </para>
	/// </summary>
	private async Task<bool> UpdateMinecraftModAsync(GameMod mod, bool silent)
	{
		string root = MinecraftRootFolder();
		string modsFolder = MinecraftLayout.ModsFolderFor(root);
		if (modsFolder.Length == 0) return false;

		if (!silent) Speak(Loc.T("updates.downloading", mod.Name));
		SetStatus(Loc.T("updates.updating", mod.Name), speak: false);

		string? downloaded = await DownloadNewestMinecraftJarAsync(mod, root);
		if (downloaded == null) return false;

		if (!MinecraftLayout.IsModFile(downloaded, null))
		{
			// Whatever that release publishes, it is not a Fabric mod. Copying it into the mods folder would be a
			// file the loader ignores in place of the working mod that is there now.
			LogError(mod.Name, $"The newest release of {mod.Name} is {Path.GetFileName(downloaded)}, which is not a Fabric mod jar.");
			return false;
		}

		// The copy being replaced, kept before it goes. Every other game's update path backs the mod up first;
		// Minecraft's did not, so when a move to a new Minecraft version turned out badly the jars that had been
		// replaced were simply gone — and two of them had been installed straight into the mods folder, so they
		// were not in the downloads folder either.
		if (File.Exists(mod.FolderPath)) BackupMod(mod.FolderPath, mod.Name);

		ProgressAnnouncer? progress = NewProgress(mod.Name, installing: true);
		ModInstaller.InstallResult result = await Task.Run(() => ModInstaller.InstallFile(downloaded, modsFolder));
		progress?.Complete();

		RecordDownloadInstalled(downloaded);
		return true;
	}

	/// <summary>
	/// Downloads the newest jar for an installed Minecraft mod into the downloads folder, or null when neither
	/// catalogue offers one for the Minecraft version in use.
	/// </summary>
	private async Task<string?> DownloadNewestMinecraftJarAsync(GameMod mod, string root)
	{
		string gameVersion = MinecraftGameVersionInUse(root);

		try
		{
			if (gameVersion.Length > 0 && !string.IsNullOrEmpty(mod.FolderPath) && File.Exists(mod.FolderPath))
			{
				string hash = ModrinthService.Sha1Of(mod.FolderPath);
				Dictionary<string, ModrinthFile> latest =
					await ModrinthService.GetLatestForHashesAsync(new[] { hash }, gameVersion);
				if (latest.TryGetValue(hash, out ModrinthFile? file))
					return await ModrinthService.DownloadAsync(file, downloadsPath);
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Modrinth", $"fetching the newest build of {mod.Name}", ex); }

		if (string.IsNullOrEmpty(mod.GitHubRepo)) return null;

		try
		{
			// The asset's own name, not "<id>_github_latest.zip": it is a jar, the name carries the release, and the
			// downloads folder is a record somebody reads.
			string? url = await GetGitHubLatestReleaseZipUrl(mod.GitHubRepo!);
			if (string.IsNullOrEmpty(url)) return null;

			string fileName = Path.GetFileName(new Uri(url).LocalPath);
			if (fileName.Length == 0) fileName = mod.UniqueId + MinecraftLayout.ModExtension;

			Directory.CreateDirectory(downloadsPath);
			string destination = Path.Combine(downloadsPath, fileName);
			ProgressAnnouncer? progress = NewProgress(mod.Name, installing: false);
			await _nexusService.DownloadFileWithProgressAsync(url, destination, progress);
			progress?.Complete();
			return destination;
		}
		catch (Exception ex) { DiagnosticLog.WriteException("GitHub", $"fetching the newest release of {mod.Name}", ex); }

		return null;
	}

	/// <summary>
	/// Asks what to do with a mod found by searching Modrinth, and does it.
	///
	/// Enter on a result opens the mod's web page for every other catalogue, because for those that is the
	/// only way to get a file: Nexus wants a browser session or a premium account. Modrinth wants neither —
	/// the download is a plain URL — so sending the user to a web page to hunt for a download button, on a
	/// site whose button did not even respond to a screen reader, would be handing back a problem the manager
	/// is able to solve outright.
	/// </summary>
	private async Task OfferMinecraftSearchResultAsync(GameMod result)
	{
		string[] actions =
		{
			Loc.T("mc.result.install"),
			Loc.T("mc.result.describe"),
			Loc.T("mc.result.page"),
		};

		string? chosen = ShowChoiceList(
			Loc.T("mc.result.title", result.Name),
			Loc.T("mc.result.listName"),
			actions,
			actions[0],
			Loc.T("mc.result.hint", result.Name));

		if (chosen is null) { Speak(Loc.T("common.changesCancelled")); return; }

		if (chosen == actions[1]) { SpeakLong(result.Description); return; }
		if (chosen == actions[2]) { OpenModPage(); return; }

		await DownloadAndInstallModrinthModAsync(result);
	}

	/// <summary>
	/// Downloads a Modrinth mod's jar and offers to install it, in the same two beats every other game uses:
	/// it says the file arrived, then asks whether to install it.
	/// </summary>
	private async Task DownloadAndInstallModrinthModAsync(GameMod result)
	{
		string gameVersion = MinecraftGameVersionInUse(MinecraftRootFolder());
		if (gameVersion.Length == 0) { Speak(Loc.T("mc.search.noVersionSpeak")); return; }

		try
		{
			SetStatus(Loc.T("suite.downloading", result.Name));
			Speak(Loc.T("suite.downloading", result.Name));

			ModrinthFile? file = await ModrinthService.GetLatestFileAsync(result.ModrinthId!, gameVersion);
			if (file is null)
			{
				// The search filters on the game version, so this is rare — but a mod can lose support for a
				// version between the search and the download, and installing a build for another version
				// would load nothing and say nothing.
				Speak(Loc.T("mc.install.noBuildSpeak", result.Name, gameVersion));
				SpeakBox(Loc.T("mc.install.noBuildBox", result.Name, gameVersion),
					Loc.T("mc.install.noBuildTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			// Into the downloads folder first, exactly like a Nexus download, so the file is kept and the
			// downloads history has something to show.
			string downloaded = await ModrinthService.DownloadAsync(file, downloadsPath);

			ResetStatus();
			Speak(Loc.T("mc.download.done", result.Name, file.VersionNumber));

			if (SpeakBox(
					Loc.T("mc.download.installNowBox", result.Name, file.VersionNumber),
					Loc.T("mc.download.installNowTitle"),
					MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
			{
				Speak(Loc.T("mc.download.keptInDownloads"));
				return;
			}

			await InstallMinecraftJarAsync(downloaded);
		}
		catch (Exception ex)
		{
			LogFailure(result.Name, "Failed to download the mod", ex);
			ResetStatus();
		}
	}

	/// <summary>
	/// Installs a mod jar the user picked off disk — the other half of browsing Modrinth, since a mod page
	/// downloads to their Downloads folder and something has to bring it in from there.
	///
	/// ⚠️ The jar is COPIED, not extracted. Every other game's manual install unpacks the archive it is given,
	/// which for a Fabric mod would replace the mod with a folder of loose classes that the loader ignores.
	/// </summary>
	private async Task InstallMinecraftJarAsync(string jarPath)
	{
		string modsFolder = MinecraftLayout.ModsFolderFor(MinecraftRootFolder());

		try
		{
			// Read it before copying anything. A file that is not a Fabric mod — a resource pack, a shader
			// pack, a source jar off a GitHub release — sits in the mods folder doing nothing, and the loader
			// says not a word about it.
			FabricModInfo info = MinecraftLayout.ReadModInfo(jarPath);
			if (info.IsUnreadable)
			{
				Speak(Loc.T("mc.install.notAModSpeak", Path.GetFileName(jarPath)));
				SpeakBox(Loc.T("mc.install.notAModBox", Path.GetFileName(jarPath)),
					Loc.T("mc.install.notAModTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			Directory.CreateDirectory(modsFolder);
			string destination = Path.Combine(modsFolder, Path.GetFileName(jarPath));

			// An existing copy of the SAME mod may be under a different file name — the version is in the name,
			// so an update never matches by file. Matched by mod id instead, or the mods folder collects every
			// version the user has ever installed and Fabric refuses to start with duplicates.
			string? existing = Directory.Exists(modsFolder)
				? Directory.EnumerateFiles(modsFolder)
					.Where(f => MinecraftLayout.IsModFile(f, GameProfiles.Require(GameProfiles.Minecraft).DisabledModSuffix))
					.FirstOrDefault(f => string.Equals(MinecraftLayout.ReadModInfo(f).Id, info.Id,
						StringComparison.OrdinalIgnoreCase))
				: null;

			if (existing != null && !ConfirmOverwrite(info.Name, MinecraftLayout.ReadModInfo(existing).Version))
				return;

			// Backed up before it is replaced, like every other game's install path does.
			if (existing != null && File.Exists(existing))
				BackupMod(existing, MinecraftLayout.ReadModInfo(existing).Name is { Length: > 0 } had ? had : info.Name);

			await Task.Run(() =>
			{
				if (existing != null && !string.Equals(existing, destination, StringComparison.OrdinalIgnoreCase))
					File.Delete(existing);

				File.Copy(jarPath, destination, overwrite: true);
			});
			RecordDownloadInstalled(jarPath);

			Speak(Loc.T("mc.install.done", info.Name));
			await RefreshModList(checkUpdates: false);
		}
		catch (Exception ex)
		{
			LogFailure(Path.GetFileName(jarPath), "Failed to install the mod", ex);
		}
	}

	/// <summary>
	/// Searches wherever the user has said this game's mods should come from.
	///
	/// <para>
	/// One place decides, so the discovery list, the update checker and the coverage report cannot disagree
	/// about it. It used to decide by asking the game — Modrinth for Minecraft, Nexus for everything else —
	/// and now asks the user first, falling back to exactly that when they have said nothing.
	/// </para>
	///
	/// <para>
	/// <paramref name="alsoTheOthers"/> is the "search the rest as well" request, and only means anything in
	/// the preferred-source mode. Notes from catalogues that could not answer are spoken rather than dropped:
	/// a search that quietly left one out reads as "there is nothing there".
	/// </para>
	/// </summary>
	private async Task<(List<GameMod> Results, int Total)> SearchActiveGameCatalogueAsync(
		string searchType, string searchTerm, int page, int pageSize,
		string? language = null, string? category = null, bool alsoTheOthers = false)
	{
		string game = _settings.ActiveGame;

		IReadOnlyList<IModSource> asking = ModSearchPlan.Choose(
			_modSources, game, _settings.ModSearchMode, _settings.PreferredModSourceFor(game), alsoTheOthers);

		if (asking.Count == 0)
		{
			Speak(Loc.T("search.noSourceForGame", GameProfiles.DisplayNameFor(game)));
			return (new List<GameMod>(), 0);
		}

		var query = new ModSearchQuery(game, searchTerm, page, pageSize)
		{
			SearchType = searchType,
			Language = language,
			Category = category,
			// Only Modrinth reads this, and it cannot search without one: a Minecraft mod built for another
			// version installs perfectly and then loads nothing.
			GameVersion = GameProfiles.Find(game)?.ModSource == ModSource.Modrinth
				? MinecraftGameVersionInUse(MinecraftRootFolder())
				: null,
		};

		ModSearchResults found = await ModSearchPlan.SearchAsync(asking, query);

		foreach (string note in found.Notes) Speak(note);
		AnnounceOtherSources(asking, game, alsoTheOthers);

		return (found.Results.ToList(), found.Total);
	}

	/// <summary>
	/// Mentions the catalogues this search did not ask, once, and only when asking them would actually work.
	///
	/// A source the manager cannot use yet — CurseForge, until it has a key — is left out of the offer
	/// deliberately. Telling a user they can press a key to search somewhere, and answering "that is not
	/// available yet" when they do, is worse than not mentioning it.
	/// </summary>
	private void AnnounceOtherSources(IReadOnlyList<IModSource> asked, string game, bool alsoTheOthers)
	{
		if (alsoTheOthers || _settings.ModSearchMode != ModSearchMode.PreferredFirst) return;

		var ready = ModSearchPlan.Remaining(_modSources, game, asked)
			.Where(s => s.Status.Ready)
			.Select(s => s.Info.DisplayName)
			.ToList();
		if (ready.Count == 0) return;

		Speak(Loc.T("search.othersAvailable", string.Join(", ", ready)));
	}

	/// <summary>
	/// Starts Minecraft with Fabric and the installed mods, without going near the launcher.
	///
	/// This is the feature the whole of Minecraft support exists for. The launcher is where the accessibility
	/// problem actually lives — selecting a mod loader installation and launching a world are separate pieces
	/// of state, the second silently overrides the first, and when it goes wrong the game starts, plays
	/// normally and never speaks.
	/// </summary>
	private async Task LaunchMinecraftAsync()
	{
		string root = MinecraftRootFolder();

		string versionId = FabricInstaller.InstalledVersionIds(root).FirstOrDefault() ?? "";
		if (versionId.Length == 0)
		{
			Speak(Loc.T("mc.launch.noFabricSpeak"));
			SpeakBox(Loc.T("mc.launch.noFabricBox"), Loc.T("mc.launch.noFabricTitle"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}

		// Identity comes from the launcher's own accounts file — public profile data, no credential — and
		// carries the player's REAL uuid. See MinecraftIdentity: a derived offline uuid would walk into their
		// world as a different person, with the old character orphaned rather than deleted.
		MinecraftIdentity? identity = MinecraftIdentity.OfflineFromLauncher(root);
		if (identity is null)
		{
			Speak(Loc.T("mc.launch.noAccountSpeak"));
			SpeakBox(Loc.T("mc.launch.noAccountBox"), Loc.T("mc.launch.noAccountTitle"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}

		try
		{
			// Before the game is asked to start, not after it has failed to. See FetchMissingLibrariesAsync.
			//
			// The game's own files are checked first, and the version checked is the vanilla one the Fabric
			// profile inherits from — that is where the jar and the assets live. ⚠️ Assets are included
			// deliberately: Sean's 26.3 was missing four objects and all four were sounds, which is a loss no
			// error message anywhere would have mentioned. See EnsureMinecraftVersionAsync.
			string vanilla = (string?)MinecraftLauncher.ReadVersionJson(root, versionId)?["inheritsFrom"] ?? versionId;
			if (!await EnsureMinecraftVersionAsync(root, vanilla, versionId)) return;

			if (!await FetchMissingLibrariesAsync(root, versionId)) return;

			MinecraftLaunchPlan plan = MinecraftLauncher.BuildPlan(root, versionId, identity);

			SetStatus(Loc.T("mc.launch.starting"));
			Speak(Loc.T("mc.launch.startingSpeak", identity.Username));

			var start = new System.Diagnostics.ProcessStartInfo(plan.JavaPath)
			{
				WorkingDirectory = plan.WorkingDirectory,
				UseShellExecute = false
			};
			foreach (string argument in plan.Arguments) start.ArgumentList.Add(argument);

			// Built before the game starts, deliberately: it notes how long the previous run's log is so that
			// the run about to begin is read from its own beginning. See MinecraftLogTail.
			var log = new MinecraftLogTail(MinecraftLayout.LatestLogPathFor(root));

			System.Diagnostics.Process? started = System.Diagnostics.Process.Start(start);
			if (started != null) Fire(TrackMinecraftSessionAsync(started, log), "TrackMinecraftSessionAsync");
		}
		catch (Exception ex)
		{
			LogFailure("Minecraft", "Failed to start the game", ex);
			SpeakBox(Loc.T("mc.launch.failedBox", FriendlyError(ex)), Loc.T("mc.launch.failedTitle"),
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	/// <summary>
	/// Follows the running game until it exits, so the status bar says the same things it says for every other
	/// game: running while it runs, closed when it closes, and back to rest after that.
	///
	/// Much simpler than the general tracker, and deliberately separate from it. That one exists to cope with a
	/// launcher that hands off to another process and a Steam game that restarts itself, so it follows the game
	/// by executable NAME — which cannot work here, because Minecraft's process is <c>javaw.exe</c> and its
	/// profile has no executable name at all. What the manager starts here IS the game, from first frame to
	/// last, so waiting on the handle is both sufficient and exact.
	/// </summary>
	private async Task TrackMinecraftSessionAsync(System.Diagnostics.Process game, MinecraftLogTail log)
	{
		// The connect and disconnect cues, for the one game where they are not about Nexus at all. See
		// MinecraftServerLog: Minecraft's mods come from Modrinth, which needs no account, so the connection
		// worth hearing about is the player's to a server — and the client says so in its own log.
		var server = new MinecraftServerSession();
		using var stopFollowingLog = new CancellationTokenSource();
		Task following = log.RunAsync(line => PlayServerCue(server, line), stopFollowingLog.Token);

		try
		{
			// Long enough to be past the point where a bad command line would have thrown it straight out, so
			// "running" is not announced over a game that has already died.
			await Task.Delay(3000);

			bool alive = false;
			try { alive = !game.HasExited; }
			catch (Exception ex)
			{
				DiagnosticLog.WriteException("Minecraft", "checking whether the game we started is still running", ex);
			}

			if (alive) SetStatus(Loc.T("launch.gameRunning"));

			await game.WaitForExitAsync();
		}
		catch (Exception ex)
		{
			LogFailure("Minecraft", "Could not follow the game process", ex);
		}
		finally
		{
			stopFollowingLog.Cancel();
			try { await following; }
			catch (Exception ex) { DiagnosticLog.WriteException("Minecraft", "finishing with the game's log", ex); }

			// Quitting straight out of a server closes the process, and whether the client got a line out
			// first is not something to depend on. If the player was still connected, say so now.
			if (server.GameEnded() != null) _soundEngine.Play("disconnect");

			try { game.Dispose(); }
			catch (Exception ex)
			{
				DiagnosticLog.WriteException("Minecraft", "releasing the game process", ex);
			}
		}

		SetStatus(Loc.T("launch.gameClosed"));

		// Returned to rest silently: "game closed" has just been spoken, and speaking the resting title over
		// the top of it sounds like a second thing happened.
		await Task.Delay(5000);
		ResetStatus();
	}

	/// <summary>
	/// Sounds the connect or disconnect cue for one line of the game's log.
	///
	/// Sound and nothing else, on purpose. The game is in front of the player with its own accessibility mod
	/// talking, and the manager speaking over the top of that would be worse than saying nothing — which is
	/// exactly what a short cue is for: it carries the fact without taking the floor.
	///
	/// Runs on the log follower's thread. Nothing here touches a control, and the sound engine plays on a
	/// thread of its own, so there is no marshalling to do.
	/// </summary>
	private void PlayServerCue(MinecraftServerSession session, string line)
	{
		foreach (MinecraftServerChange change in session.Read(line))
		{
			if (change == MinecraftServerChange.Connected)
			{
				DiagnosticLog.Write("Minecraft", $"joined the server {session.Server}");
				_soundEngine.Play("connect");
			}
			else
			{
				DiagnosticLog.Write("Minecraft", "left the server");
				_soundEngine.Play("disconnect");
			}
		}
	}

	/// <summary>
	/// Warns when the accessibility mod the user did NOT choose is also installed.
	///
	/// Both mods hook narration on the same screens, so running the pair is expected to double-speak
	/// everything. Not removed automatically — a mod the user installed is theirs — but not left unmentioned
	/// either, because the symptom is confusing and the cause is not guessable.
	/// </summary>
	private void WarnAboutRivalAccessMod(MinecraftSuiteMod chosen)
	{
		foreach (MinecraftSuiteMod rival in MinecraftSuite.RivalsOf(chosen))
		{
			if (!HasModUniqueId(rival.FabricModId)) continue;

			Speak(Loc.T("mc.rival.speak", rival.DisplayName, chosen.DisplayName));
			SpeakBox(
				Loc.T("mc.rival.box", rival.DisplayName, chosen.DisplayName),
				Loc.T("mc.rival.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}
}
