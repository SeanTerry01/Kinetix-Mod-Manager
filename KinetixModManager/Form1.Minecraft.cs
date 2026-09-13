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
	private MinecraftSuiteMod? EnsureAccessModChosen()
	{
		if (!string.IsNullOrEmpty(_settings.MinecraftAccessModId))
			return MinecraftSuite.AccessModFor(_settings.MinecraftAccessModId);

		string? picked = ShowChoiceList(
			Loc.T("mc.chooser.title"),
			Loc.T("mc.chooser.listName"),
			MinecraftSuite.AccessModNames,
			MinecraftSuite.Default.DisplayName,
			Loc.T("mc.chooser.hint"));

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

			SetStatus(Loc.T("mc.fabric.installing", gameVersion));
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
	private async Task<bool> InstallMinecraftModAsync(MinecraftSuiteMod mod, string root)
	{
		string modsFolder = MinecraftLayout.ModsFolderFor(root);
		Directory.CreateDirectory(modsFolder);

		try
		{
			SetStatus(Loc.T("suite.downloading", mod.DisplayName));
			Speak(Loc.T("suite.downloading", mod.DisplayName));

			string installed = mod.Origin == MinecraftModOrigin.Modrinth
				? await InstallModrinthJarAsync(mod, modsFolder)
				: await InstallGitHubJarAsync(mod, modsFolder);

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

	/// <summary>Fetches a Modrinth project's newest build for the pinned Minecraft version.</summary>
	private async Task<string> InstallModrinthJarAsync(MinecraftSuiteMod mod, string modsFolder)
	{
		ModrinthFile? file = await ModrinthService.GetLatestFileAsync(mod.Source, _settings.MinecraftGameVersion);

		if (file is null)
		{
			// Said out loud rather than swallowed: "there is no build of this for the version you are on" is
			// something the user can act on, and installing a build for another version would not work.
			Speak(Loc.T("mc.install.noBuildSpeak", mod.DisplayName, _settings.MinecraftGameVersion));
			SpeakBox(
				Loc.T("mc.install.noBuildBox", mod.DisplayName, _settings.MinecraftGameVersion),
				Loc.T("mc.install.noBuildTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
			return "";
		}

		return await ModrinthService.DownloadAsync(file, modsFolder);
	}

	/// <summary>
	/// Fetches the newest <c>.jar</c> asset from a GitHub repository's latest release.
	///
	/// Deliberately the .jar and not the source zip GitHub always attaches: a release carries both, and
	/// grabbing the wrong one puts a folder of source code in the mods folder.
	/// </summary>
	private async Task<string> InstallGitHubJarAsync(MinecraftSuiteMod mod, string modsFolder)
	{
		string json = await KinetixHttp.Api.GetStringAsync($"https://api.github.com/repos/{mod.Source}/releases/latest");
		Newtonsoft.Json.Linq.JObject release = Newtonsoft.Json.Linq.JObject.Parse(json);

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

			await Task.Run(() =>
			{
				if (existing != null && !string.Equals(existing, destination, StringComparison.OrdinalIgnoreCase))
					File.Delete(existing);

				File.Copy(jarPath, destination, overwrite: true);
			});

			Speak(Loc.T("mc.install.done", info.Name));
			await RefreshModList(checkUpdates: false);
		}
		catch (Exception ex)
		{
			LogFailure(Path.GetFileName(jarPath), "Failed to install the mod", ex);
		}
	}

	/// <summary>
	/// Searches whichever catalogue the loaded game's mods actually come from.
	///
	/// Every caller used to ask Nexus directly, which for Minecraft means asking about a game Nexus has no
	/// Fabric mods for, using a domain that is empty. This is the one place that decides, so the discovery
	/// list, the update checker and the coverage report cannot disagree about it.
	/// </summary>
	private async Task<(List<GameMod> Results, int Total)> SearchActiveGameCatalogueAsync(
		string searchType, string searchTerm, int page, int pageSize,
		string? language = null, string? category = null)
	{
		if (GameProfiles.Find(_settings.ActiveGame)?.ModSource != ModSource.Modrinth)
			return await _nexusService.SearchModsAsync(searchType, searchTerm, page, pageSize, language, category);

		// Modrinth needs to know which Minecraft version to filter to, and there is no sensible default: a
		// mod built for another version installs cleanly and then loads nothing.
		string gameVersion = MinecraftGameVersionInUse(MinecraftRootFolder());
		if (gameVersion.Length == 0)
		{
			Speak(Loc.T("mc.search.noVersionSpeak"));
			return (new List<GameMod>(), 0);
		}

		try
		{
			// Language and category are Nexus's filters and have no equivalent here; Modrinth's own categories
			// are a different vocabulary entirely, so offering them would promise filtering that is not
			// happening.
			return await ModrinthService.SearchAsync(searchTerm, gameVersion, (page - 1) * pageSize, pageSize);
		}
		catch (Exception ex)
		{
			LogFailure("Modrinth", "Search failed", ex);
			return (new List<GameMod>(), 0);
		}
	}

	/// <summary>
	/// Starts Minecraft with Fabric and the installed mods, without going near the launcher.
	///
	/// This is the feature the whole of Minecraft support exists for. The launcher is where the accessibility
	/// problem actually lives — selecting a mod loader installation and launching a world are separate pieces
	/// of state, the second silently overrides the first, and when it goes wrong the game starts, plays
	/// normally and never speaks.
	/// </summary>
	private void LaunchMinecraft()
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
			MinecraftLaunchPlan plan = MinecraftLauncher.BuildPlan(root, versionId, identity);

			SetStatus(Loc.T("mc.launch.starting"));
			Speak(Loc.T("mc.launch.startingSpeak", identity.Username));

			var start = new System.Diagnostics.ProcessStartInfo(plan.JavaPath)
			{
				WorkingDirectory = plan.WorkingDirectory,
				UseShellExecute = false
			};
			foreach (string argument in plan.Arguments) start.ArgumentList.Add(argument);

			System.Diagnostics.Process? started = System.Diagnostics.Process.Start(start);
			if (started != null) Fire(TrackMinecraftSessionAsync(started), "TrackMinecraftSessionAsync");
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
	private async Task TrackMinecraftSessionAsync(System.Diagnostics.Process game)
	{
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
