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
		using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
		client.DefaultRequestHeaders.UserAgent.ParseAdd($"KinetixModManager/{NexusService.AppVersion}");

		string json = await client.GetStringAsync($"https://api.github.com/repos/{mod.Source}/releases/latest");
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
		File.WriteAllBytes(path, await client.GetByteArrayAsync(url));
		return path;
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

			System.Diagnostics.Process.Start(start);
		}
		catch (Exception ex)
		{
			LogFailure("Minecraft", "Failed to start the game", ex);
			SpeakBox(Loc.T("mc.launch.failedBox", FriendlyError(ex)), Loc.T("mc.launch.failedTitle"),
				MessageBoxButtons.OK, MessageBoxIcon.Error);
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
