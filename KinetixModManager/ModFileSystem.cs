using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// Putting an unpacked mod where each game wants it, and keeping it there.
///
/// <para>
/// What is left here after the extraction passes: deployment and hard links, per-game finalisation for
/// Bethesda, BepInEx and Witcher layouts, <c>plugins.txt</c>, INI editing, archive invalidation, script
/// extender installs, and the uninstaller registry lookup. What used to be here and is not any more:
/// scanning (<see cref="ModScanner"/>), backups (<see cref="BackupStore"/>) and getting a mod out of
/// whatever it arrived in (<see cref="ModArchive"/>) — all three now live in Kinetix.Core, where a second
/// front end can reach them.
/// </para>
///
/// <para>
/// Still a god object, and named as one in TODO.md. About five more classes' worth.
/// </para>
/// </summary>
public static class ModFileSystem
{
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

	public static bool TryCreateHardLink(string newFilePath, string existingFilePath)
	{
		try
		{
			return CreateHardLink(newFilePath, existingFilePath, IntPtr.Zero);
		}
		catch
		{
			return false;
		}
	}

	// -------------------------------------------------------------------------
	// Manifest scanning
	// -------------------------------------------------------------------------

	/// <summary>
	/// Enables or disables an installed mod and returns its new folder path.
	///
	/// How a mod is switched off depends on the game. Stardew Valley and the Bethesda games use the long-standing
	/// leading-dot convention (SMAPI skips dot-prefixed folders, and the manager's deployment skips them too).
	/// The Witcher 3 loads only folders named <c>mod*</c>, so a leading tilde does the same job there. BepInEx
	/// ignores folder names entirely, so a Moonlight Peaks mod is moved between <c>BepInEx\plugins</c> and
	/// <c>BepInEx\plugins-disabled</c> instead — see <see cref="BepInExDisabledFolderName"/>.
	/// </summary>
	/// <remarks>
	/// The work is <see cref="ModOnDisk.SetEnabled"/>, which the Linux head uses too. This held its own copy until November
	/// 2026, without the core's retry on a locked folder or its putting a Witcher 3 mod's outside files back when the
	/// folder would not move.
	/// </remarks>
	/// <param name="locatedGameFolder">
	/// Where the game was found, for The Witcher 3's files outside the mod. The game folder was taken to be two levels up
	/// from the mod, which is wrong for a mods folder the user typed somewhere else. See <see cref="Witcher3ModFiles.GameFolderFor"/>.
	/// </param>
	public static string SetModEnabled(string modFolderPath, bool enable, string activeGame,
		Action<string, string>? logError = null, string? locatedGameFolder = null) =>
		ModOnDisk.SetEnabled(modFolderPath, enable, activeGame, WitcherGameFolderOf(modFolderPath, activeGame, locatedGameFolder),
			logError ?? ((where, why) => DiagnosticLog.Write("ModFiles", $"{where}: {why}")));

	/// <summary>The Witcher 3 game folder a mod's outside files belong to, or empty for another game.</summary>
	private static string WitcherGameFolderOf(string modFolderPath, string activeGame, string? locatedGameFolder = null)
	{
		if (GameProfiles.Find(activeGame)?.IsWitcher3 != true || string.IsNullOrEmpty(modFolderPath)) return "";
		string modsFolder = Path.GetDirectoryName(modFolderPath.TrimEnd(Path.DirectorySeparatorChar)) ?? "";
		return Witcher3ModFiles.GameFolderFor(modsFolder, locatedGameFolder);
	}

	/// <summary>
	/// Mirrors a Witcher 3 mod's on/off state into the game's own <c>mods.settings</c>, so the in-game mod menu
	/// says the same thing the manager does. A no-op for every other game. See <see cref="Witcher3ModFiles.SyncSettings"/>.
	/// </summary>
	private static void SyncWitcherModSettings(string modFolderPath, bool enable, string activeGame) =>
		Witcher3ModFiles.SyncSettings(modFolderPath, enable, activeGame, WitcherGameFolderOf(modFolderPath, activeGame),
			(where, why) => DiagnosticLog.Write("Witcher3", $"{where}: {why}"));

	/// <summary>A declared master that won't satisfy a plugin: missing entirely, or present but not active.</summary>
	public sealed class MasterIssue
	{
		public string Plugin = "";
		public string OwnerMod = "";
		public string Master = "";
		/// <summary>True when the master file is in the Data folder but isn't active (so enabling it fixes it);
		/// false when the file isn't present at all.</summary>
		public bool PresentButInactive;
	}

	/// <summary>
	/// Finds active Skyrim/Fallout 4 plugins whose declared masters aren't satisfied. A master is satisfied when
	/// it's a base-game/DLC master or is itself active in the load order (<paramref name="activePlugins"/>, the
	/// plugins.txt set — which includes enabled mods' plugins AND active Creations in the Data folder). An
	/// unsatisfied master is reported as either "installed but not enabled" (the file exists in Data, e.g. a
	/// Creation that's toggled off) or "not installed" (no file at all). Only active plugins are checked, since
	/// an inactive plugin isn't loading anyway.
	/// </summary>
	public static List<MasterIssue> FindMissingMasters(
		string activeGame, IEnumerable<GameMod> mods, ISet<string> activePlugins, string? gameRoot)
	{
		var result = new List<MasterIssue>();
		if (!GameProfiles.IsAnyGame(activeGame, GameProfiles.SkyrimSE, GameProfiles.Fallout4)) return result;

		string dataDir = string.IsNullOrEmpty(gameRoot) ? "" : Path.Combine(gameRoot, "Data");

		foreach (GameMod mod in mods.Where(m => !m.IsGroup && !string.IsNullOrEmpty(m.FolderPath) && Directory.Exists(m.FolderPath)))
		{
			foreach (string file in EnumerateFilesSafe(mod.FolderPath))
			{
				string leaf = Path.GetFileName(file);
				if (!BethesdaPlugins.IsPluginFile(leaf) || !activePlugins.Contains(leaf)) continue; // only active plugins load

				foreach (string master in BethesdaPlugins.ReadPluginMasters(file))
				{
					if (BethesdaPlugins.IsBaseMaster(activeGame, master) || activePlugins.Contains(master)) continue;
					bool inData = !string.IsNullOrEmpty(dataDir) && File.Exists(Path.Combine(dataDir, master));
					result.Add(new MasterIssue { Plugin = leaf, OwnerMod = mod.Name, Master = master, PresentButInactive = inData });
				}
			}
		}
		return result;
	}

	/// <summary>Enumerates files under <paramref name="root"/> recursively, swallowing IO errors (returns what it can).</summary>
	private static IEnumerable<string> EnumerateFilesSafe(string root)
	{
		try { return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories); }
		catch { return Enumerable.Empty<string>(); }
	}

	// -------------------------------------------------------------------------
	// Backup management
	// -------------------------------------------------------------------------

	// -------------------------------------------------------------------------
	// File deployment (Skyrim / Fallout 4)
	// -------------------------------------------------------------------------

	/// <summary>
	/// Hardlinks or copies files from a mod storage folder to the game's Data folder.
	/// </summary>
	public static void DeployModFiles(string modFolderPath, string gameDataPath, bool isDeploy, Action<string, string> logError)
	{
		if (!Directory.Exists(modFolderPath)) return;
		if (!Directory.Exists(gameDataPath))
		{
			try { Directory.CreateDirectory(gameDataPath); }
			catch (Exception ex)
			{
				logError("Deploy", $"Failed to create Data folder: {ex.Message}");
				return;
			}
		}

		string canonicalModFolder = Path.GetFullPath(modFolderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string gameRoot = Path.GetDirectoryName(gameDataPath) ?? gameDataPath;

		foreach (string sourceFile in Directory.GetFiles(modFolderPath, "*.*", SearchOption.AllDirectories))
		{
			if (Path.GetFileName(sourceFile).Equals(".manager_manifest.json", StringComparison.OrdinalIgnoreCase))
				continue;

			string relativePath = Path.GetFullPath(sourceFile).Substring(canonicalModFolder.Length);
			bool isRootFile = relativePath.StartsWith("Root" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
			                  relativePath.StartsWith("Root" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

			string destFile;
			if (isRootFile)
			{
				string subPath = relativePath.Substring(5); // Remove "Root\" (5 chars)
				destFile = Path.Combine(gameRoot, subPath);
			}
			else
			{
				destFile = Path.Combine(gameDataPath, relativePath);
			}

			if (isDeploy)
			{
				try
				{
					string? destDir = Path.GetDirectoryName(destFile);
					if (destDir != null && !Directory.Exists(destDir))
					{
						Directory.CreateDirectory(destDir);
					}

					if (File.Exists(destFile))
					{
						File.Delete(destFile);
					}

					bool linkCreated = TryCreateHardLink(destFile, sourceFile);
					if (!linkCreated)
					{
						File.Copy(sourceFile, destFile, true);
					}
				}
				catch (Exception ex)
				{
					logError(sourceFile, $"Deployment failed: {ex.Message}");
				}
			}
			else
			{
				try
				{
					if (File.Exists(destFile))
					{
						File.Delete(destFile);
					}

					string? parentDir = Path.GetDirectoryName(destFile);
					string limitPath = isRootFile ? gameRoot : gameDataPath;
					while (parentDir != null && parentDir.Length > limitPath.Length)
					{
						if (Directory.Exists(parentDir) && !Directory.EnumerateFileSystemEntries(parentDir).Any())
						{
							Directory.Delete(parentDir);
							parentDir = Path.GetDirectoryName(parentDir);
						}
						else
						{
							break;
						}
					}
				}
				catch (Exception ex)
				{
					logError(destFile, $"Undeployment failed: {ex.Message}");
				}
			}
		}
	}

	/// <summary>
	/// Scans mod folder for plugins and adds/removes them in the game's plugins.txt.
	/// </summary>
	public static void SyncPluginsFile(string modFolderPath, string activeGame, string gameFolder, bool isDeploy, Action<string, string> logError)
	{
		string pluginsFilePath = PluginsTxtPath(activeGame, gameFolder);
		if (pluginsFilePath.Length == 0) return;

		var plugins = Directory.GetFiles(modFolderPath, "*.*", SearchOption.AllDirectories)
			.Select(p => Path.GetFileName(p))
			.Where(name => !string.IsNullOrEmpty(name))
			.Select(name => name!)
			.Where(name =>
			{
				string ext = Path.GetExtension(name).ToLower();
				return ext == ".esp" || ext == ".esm" || ext == ".esl";
			})
			.Distinct()
			.ToList();

		if (plugins.Count == 0) return;

		try
		{
			string? dir = Path.GetDirectoryName(pluginsFilePath);
			if (dir != null && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			List<string> lines = new List<string>();
			if (File.Exists(pluginsFilePath))
			{
				lines = File.ReadAllLines(pluginsFilePath).ToList();
			}

			bool changed = false;
			foreach (string plugin in plugins)
			{
				string entry = "*" + plugin;
				if (isDeploy)
				{
					if (!lines.Any(l => l.Trim().Equals(entry, StringComparison.OrdinalIgnoreCase) || 
										l.Trim().Equals(plugin, StringComparison.OrdinalIgnoreCase)))
					{
						lines.Add(entry);
						changed = true;
					}
				}
				else
				{
					int removed = lines.RemoveAll(l => l.Trim().Equals(entry, StringComparison.OrdinalIgnoreCase) || 
													   l.Trim().Equals(plugin, StringComparison.OrdinalIgnoreCase));
					if (removed > 0) changed = true;
				}
			}

			if (changed)
			{
				// plugins.txt may be marked read-only to stop the game rewriting it (see SetPluginsTxtProtection);
				// clear that for our own write and restore it afterwards.
				bool wasProtected = IsReadOnly(pluginsFilePath);
				SetReadOnly(pluginsFilePath, false);
				AtomicFile.WriteAllLines(pluginsFilePath, lines);
				if (wasProtected) SetReadOnly(pluginsFilePath, true);
			}
		}
		catch (Exception ex)
		{
			logError(pluginsFilePath, $"Failed to update plugins.txt: {ex.Message}");
		}
	}

	// -------------------------------------------------------------------------
	// Archive invalidation (loose-file loading)
	// -------------------------------------------------------------------------
	// Fallout 4 ignores mods' loose files (textures, meshes, scripts) unless "archive invalidation" is turned on
	// via a small [Archive] block in Fallout4Custom.ini. Skyrim SE deliberately isn't covered here: it loads loose
	// files by default, so the toggle would be a misleading no-op there.

	/// <summary>
	/// Every BepInEx plugin's configuration file for a Moonlight Peaks install, as (display label, full path)
	/// pairs sorted by label. BepInEx writes one <c>.cfg</c> per plugin that has settings, into
	/// <c>BepInEx\config</c>. They are INI files in all but name — sections, <c>key = value</c>, and <c>#</c>
	/// comment lines — so the manager's accessible INI editor reads and writes them unchanged.
	///
	/// Each file names its own plugin in its header, so the list shows "Moonlight Access" rather than
	/// "com.moonlightaccess.core.cfg", falling back to the file name when there is no header to read.
	/// </summary>
	public static List<(string Label, string Path)> BepInExConfigFiles(string gameRoot)
	{
		var files = new List<(string Label, string Path)>();
		try
		{
			if (string.IsNullOrEmpty(gameRoot)) return files;
			string configDir = Path.Combine(gameRoot, "BepInEx", "config");
			if (!Directory.Exists(configDir)) return files;

			foreach (string path in Directory.GetFiles(configDir, "*.cfg", SearchOption.TopDirectoryOnly))
			{
				BepInExPluginInfo? info = BepInExPlugin.ReadFromConfig(path);
				string label = info != null && info.Name.Length > 0
					? info.Name
					: Path.GetFileNameWithoutExtension(path);
				files.Add((label, path));
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("BepInEx", "listing the BepInEx config files", ex); }

		files.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.CurrentCultureIgnoreCase));
		return files;
	}

	public static string? ArchiveInvalidationIniPath(string activeGame, string gameFolder)
	{
		if (!GameProfiles.IsGame(activeGame, GameProfiles.Fallout4)) return null;
		string folder = UserDataFolderName(activeGame, gameFolder);
		if (folder.Length == 0) return null;
		string docs = UserFolders.Documents;
		return Path.Combine(docs, "My Games", folder, "Fallout4Custom.ini");
	}

	/// <summary>
	/// The folder the game at <paramref name="gameFolder"/> keeps its INIs, saves and load order in — under
	/// <c>Documents\My Games\</c> and <c>%LOCALAPPDATA%\</c>. Empty for a game that keeps none of that.
	///
	/// Depends on the install, not just the game: a GOG copy of a Bethesda game uses a different folder from the
	/// Steam copy of the same game, and reading or writing the wrong one means silently managing the other
	/// install — which on a machine with both is worse than doing nothing.
	/// </summary>
	public static string UserDataFolderName(string activeGame, string gameFolder) =>
		GameProfiles.Find(activeGame)?.UserDataFolderFor(gameFolder) ?? "";

	/// <summary>
	/// The standard configuration INI files for a Bethesda game, as (display label, full path) pairs in the order
	/// players usually reach for them: the main INI, the preferences INI, and the custom INI (the safe place for
	/// tweaks — the engine merges it over the generated ones). They live in <c>Documents\My Games\&lt;game&gt;\</c>.
	/// Any of them may not exist yet (the game generates the first two on first launch; the custom one is optional).
	/// Empty for non-Bethesda games.
	/// </summary>
	public static List<(string Label, string Path)> GameIniFiles(string activeGame, string gameFolder)
	{
		GameProfile? profile = GameProfiles.Find(activeGame);
		if (profile == null || profile.ConfigFileNames.Count == 0) return new List<(string, string)>();

		string dir = profile.UserDataDirectoryFor(gameFolder);
		if (dir.Length == 0) return new List<(string, string)>();

		return profile.ConfigFileNames
			.Select(name => (name, Path.Combine(dir, name)))
			.ToList();
	}

	/// <summary>
	/// The folder where the game keeps its save files (<c>Documents\My Games\&lt;game&gt;\Saves</c>), and the save
	/// file extension for the game (<c>.ess</c> for Skyrim, <c>.fos</c> for Fallout 4). Empty for non-Bethesda
	/// games. The folder may not exist yet if the player has never saved.
	/// </summary>
	public static (string Folder, string Extension) SavesLocation(string activeGame, string gameFolder)
	{
		GameProfile? profile = GameProfiles.Find(activeGame);
		if (profile == null ||
			string.IsNullOrEmpty(profile.SavesFolderName) ||
			string.IsNullOrEmpty(profile.SaveFileExtension)) return ("", "");

		string dir = profile.UserDataDirectoryFor(gameFolder);
		if (dir.Length == 0) return ("", "");

		return (Path.Combine(dir, profile.SavesFolderName), profile.SaveFileExtension);
	}

	/// <summary>The [Archive] keys that together tell the engine to load loose mod files ahead of the packed BA2 archives.</summary>
	private static readonly (string Key, string Value)[] ArchiveInvalidationKeys =
	{
		("bInvalidateOlderFiles", "1"),
		("sResourceDataDirsFinal", ""),
	};

	/// <summary>
	/// True when archive invalidation is currently enabled, judged by <c>bInvalidateOlderFiles=1</c> in the
	/// [Archive] section of the custom INI. Returns false where the toggle doesn't apply or the file/key is absent.
	/// </summary>
	public static bool IsArchiveInvalidationEnabled(string activeGame, string gameFolder)
	{
		string? path = ArchiveInvalidationIniPath(activeGame, gameFolder);
		if (path == null || !File.Exists(path)) return false;
		try
		{
			string? value = ReadIniValue(File.ReadAllLines(path), "Archive", "bInvalidateOlderFiles");
			return value != null && value.Trim() == "1";
		}
		catch { return false; }
	}

	/// <summary>
	/// Turns archive invalidation on or off by adding or removing the managed [Archive] keys in the custom INI,
	/// creating the file if needed. No-op where the toggle doesn't apply. Errors go to <paramref name="logError"/>.
	/// </summary>
	public static void SetArchiveInvalidation(string activeGame, string gameFolder, bool enable, Action<string, string> logError)
	{
		string? path = ArchiveInvalidationIniPath(activeGame, gameFolder);
		if (path == null) return;
		try
		{
			string? dir = Path.GetDirectoryName(path);
			if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

			List<string> lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
			foreach (var (key, value) in ArchiveInvalidationKeys)
				SetIniValue(lines, "Archive", key, enable ? value : null);
			AtomicFile.WriteAllLines(path, lines);
		}
		catch (Exception ex)
		{
			logError(path, $"Failed to update archive invalidation: {ex.Message}");
		}
	}

	/// <summary>Reads a key's raw value from a section of an INI's lines, or <c>null</c> if the section/key is absent.</summary>
	private static string? ReadIniValue(IReadOnlyList<string> lines, string section, string key)
	{
		bool inSection = false;
		foreach (string raw in lines)
		{
			string line = raw.Trim();
			if (line.StartsWith("[") && line.EndsWith("]"))
			{
				inSection = line.Substring(1, line.Length - 2).Trim().Equals(section, StringComparison.OrdinalIgnoreCase);
				continue;
			}
			if (!inSection) continue;
			int eq = line.IndexOf('=');
			if (eq <= 0) continue;
			if (line.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
				return line.Substring(eq + 1);
		}
		return null;
	}

	/// <summary>
	/// Sets (value non-null) or removes (value null) a key within a section of an INI held as a mutable line list,
	/// appending the section at the end of the file if it doesn't exist. All other lines and comments are preserved.
	/// Each call re-scans from scratch, so it's safe to call repeatedly on the same list.
	/// </summary>
	private static void SetIniValue(List<string> lines, string section, string key, string? value)
	{
		// Locate the section body: (sectionStart, sectionEnd) bracket the lines between this header and the next.
		int sectionStart = -1, sectionEnd = lines.Count;
		for (int i = 0; i < lines.Count; i++)
		{
			string line = lines[i].Trim();
			if (!(line.StartsWith("[") && line.EndsWith("]"))) continue;
			string name = line.Substring(1, line.Length - 2).Trim();
			if (sectionStart < 0 && name.Equals(section, StringComparison.OrdinalIgnoreCase))
				sectionStart = i;
			else if (sectionStart >= 0) { sectionEnd = i; break; }
		}

		int keyLine = -1;
		if (sectionStart >= 0)
		{
			for (int i = sectionStart + 1; i < sectionEnd; i++)
			{
				string line = lines[i].Trim();
				int eq = line.IndexOf('=');
				if (eq > 0 && line.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
				{
					keyLine = i;
					break;
				}
			}
		}

		if (value == null)
		{
			if (keyLine >= 0) lines.RemoveAt(keyLine);
			return;
		}

		string entry = key + "=" + value;
		if (keyLine >= 0) lines[keyLine] = entry;
		else if (sectionStart >= 0) lines.Insert(sectionStart + 1, entry);
		else
		{
			if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1])) lines.Add("");
			lines.Add("[" + section + "]");
			lines.Add(entry);
		}
	}

	// -------------------------------------------------------------------------
	// Mod installation
	// -------------------------------------------------------------------------

	/// <summary>
	/// Finds the deepest common directory containing game files or plugins.
	/// </summary>
	public static List<FileConflict> SyncDeployment(
		string gameRootPath,
		List<(string Name, string FolderPath)> enabledModsHighToLow,
		DeploymentManifest manifest,
		Action<string, string> logError,
		HashSet<string>? forceRelink = null,
		IReadOnlyDictionary<string, string>? winnerOverrides = null)
	{
		var conflicts = new List<FileConflict>();
		if (string.IsNullOrEmpty(gameRootPath)) return conflicts;
		gameRootPath = Path.GetFullPath(gameRootPath).TrimEnd(Path.DirectorySeparatorChar);

		// Destination path (relative to the game root) -> winning source file / owning mod. providers
		// tracks every mod that supplies a path so conflicts can be reported. providerSource keeps each
		// provider's own source file for a path, so a per-file override can force a specific mod to win.
		var desiredSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var desiredOwner  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var providers     = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		var providerSource = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

		// Walk lowest priority first so the highest-priority provider is written last and wins.
		for (int i = enabledModsHighToLow.Count - 1; i >= 0; i--)
		{
			var (modName, folderPath) = enabledModsHighToLow[i];
			if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) continue;
			string canonical = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

			string[] modFiles = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
			// Which of this mod's files must sit beside the game's .exe rather than under Data, whatever folder
			// the mod keeps them in. Asked here as well as at install time so a mod already installed in the
			// wrong shape is put right by its next deployment, with nothing to reinstall. One mod shipping two
			// builds of the same DLL resolves to one winner here, exactly as it would during an install.
			HashSet<string> besideTheExe = BethesdaLayout.ChooseFilesForGameRoot(
				modFiles.Select(f => Path.GetFullPath(f).Substring(canonical.Length)));

			foreach (string sourceFile in modFiles)
			{
				if (Path.GetFileName(sourceFile).Equals(".manager_manifest.json", StringComparison.OrdinalIgnoreCase))
					continue;

				string relativePath = Path.GetFullPath(sourceFile).Substring(canonical.Length);
				// The manager's own captured documentation lives in .kinetix_docs and must never deploy to the game.
				if (relativePath.StartsWith(DocsFolderName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
					relativePath.StartsWith(DocsFolderName + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
					continue;

				bool isRootFile = relativePath.StartsWith("Root" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
								  relativePath.StartsWith("Root" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
				// A screen-reader bridge DLL deploys beside the .exe under its bare name — the folders around it
				// in the mod are dropped, because Windows resolves the name the mod asks for against the .exe's
				// own folder and looks nowhere else.
				string destRel =
					besideTheExe.Contains(relativePath.Replace(Path.DirectorySeparatorChar, '/')) ? Path.GetFileName(relativePath)
					: isRootFile ? relativePath.Substring(5)
					: Path.Combine("Data", relativePath);

				desiredSource[destRel] = sourceFile;
				desiredOwner[destRel]  = modName;
				if (!providers.TryGetValue(destRel, out var list)) { list = new List<string>(); providers[destRel] = list; }
				list.Add(modName);
				// Only remember each provider's own source for paths that actually carry an override — otherwise this
				// would allocate a nested dictionary for every deployed file (heavy on a large load order).
				if (winnerOverrides != null && winnerOverrides.ContainsKey(destRel))
				{
					if (!providerSource.TryGetValue(destRel, out var bySrc))
					{
						bySrc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
						providerSource[destRel] = bySrc;
					}
					bySrc[modName] = sourceFile;
				}
			}
		}

		// Apply per-file winner overrides: force the chosen mod to win a path regardless of priority, but only when
		// it actually provides that path (a stale override for a since-removed/renamed mod is silently ignored, so
		// deployment falls back to the normal priority winner).
		if (winnerOverrides != null)
		{
			foreach (var (destRel, forcedOwner) in winnerOverrides)
			{
				if (providerSource.TryGetValue(destRel, out var bySrc) &&
					bySrc.TryGetValue(forcedOwner, out string? forcedSource))
				{
					desiredSource[destRel] = forcedSource;
					desiredOwner[destRel]  = forcedOwner;
				}
			}
		}

		// What the record will say afterwards. It starts as what was asked for and is corrected below wherever that
		// did not happen. Recording it regardless is what used to orphan files: a removal that failed dropped the file
		// from the record, so nothing ever tried again, and a "disabled" mod went on loading.
		var recorded = new Dictionary<string, string>(desiredOwner, StringComparer.OrdinalIgnoreCase);
		var originals = new OriginalFiles(manifest.OriginalsFolder);
		manifest.Failures = 0;

		// 1. Remove files we previously deployed that are no longer wanted by any enabled mod.
		foreach (var kv in manifest.Deployed)
		{
			if (desiredSource.ContainsKey(kv.Key)) continue;
			string destAbs = Path.Combine(gameRootPath, kv.Key);
			try
			{
				RobustDeleteFile(destAbs);
				originals.Restore(destAbs, kv.Key);
				CleanEmptyParents(Path.GetDirectoryName(destAbs), gameRootPath);
			}
			catch (Exception ex)
			{
				logError(destAbs, $"Undeploy failed: {ex.Message}");
				recorded[kv.Key] = kv.Value;
				manifest.Failures++;
			}
		}

		// 2. (Re)link desired files whose winning owner changed, that are missing, or that are forced.
		foreach (var kv in desiredSource)
		{
			string destRel = kv.Key;
			string sourceFile = kv.Value;
			string owner = desiredOwner[destRel];
			string destAbs = Path.Combine(gameRootPath, destRel);

			bool needsRelink = !manifest.Deployed.TryGetValue(destRel, out string? prevOwner)
							   || !string.Equals(prevOwner, owner, StringComparison.OrdinalIgnoreCase)
							   || !File.Exists(destAbs)
							   || (forceRelink != null && forceRelink.Contains(owner));
			if (!needsRelink) continue;

			try
			{
				string? destDir = Path.GetDirectoryName(destAbs);
				if (destDir != null && !Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
				// Not in the record means the manager did not put it there, so it is the game's own or the user's. A
				// failure to keep it stops here, rather than overwriting something that cannot be got back.
				if (prevOwner == null) originals.Keep(destAbs, destRel);
				RobustDeleteFile(destAbs);
				if (!TryCreateHardLink(destAbs, sourceFile)) RobustCopy(sourceFile, destAbs);
			}
			catch (Exception ex)
			{
				logError(sourceFile, $"Deploy failed: {ex.Message}");
				manifest.Failures++;
				// As it was, so the next pass sees the mismatch and tries again. Recording the new owner meant the old
				// file was never replaced at all.
				if (prevOwner != null) recorded[destRel] = prevOwner;
				else recorded.Remove(destRel);
			}
		}

		// 3. Record the new state and surface conflicts.
		manifest.Deployed = recorded;
		foreach (var kv in providers)
		{
			if (kv.Value.Count <= 1) continue;
			string winner = desiredOwner[kv.Key];
			var losers = kv.Value
				.Where(n => !string.Equals(n, winner, StringComparison.OrdinalIgnoreCase))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			if (losers.Count > 0)
				conflicts.Add(new FileConflict { RelativePath = kv.Key, Winner = winner, Losers = losers });
		}
		return conflicts;
	}

	/// <summary>
	/// Removes every file the manager has deployed into <paramref name="gameRootPath"/> (per the manifest) and
	/// empties the manifest, returning the game folder to its un-deployed state while leaving the installed mods
	/// untouched in the mod store. Only manifest-tracked paths are deleted — a file the manager never deployed is
	/// never removed, and a game file a mod replaced goes back. Empty directories left behind are tidied up. Returns
	/// the number of files actually removed.
	/// </summary>
	public static int PurgeDeployment(string gameRootPath, DeploymentManifest manifest, Action<string, string> logError)
	{
		if (string.IsNullOrEmpty(gameRootPath) || manifest.Deployed.Count == 0) return 0;
		gameRootPath = Path.GetFullPath(gameRootPath).TrimEnd(Path.DirectorySeparatorChar);

		var originals = new OriginalFiles(manifest.OriginalsFolder);
		var stillThere = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		manifest.Failures = 0;

		int removed = 0;
		foreach (var kv in manifest.Deployed)
		{
			string destAbs = Path.Combine(gameRootPath, kv.Key);
			try
			{
				if (File.Exists(destAbs))
				{
					RobustDeleteFile(destAbs);
					removed++;
				}
				originals.Restore(destAbs, kv.Key);
				CleanEmptyParents(Path.GetDirectoryName(destAbs), gameRootPath);
			}
			catch (Exception ex)
			{
				logError(destAbs, $"Purge failed: {ex.Message}");
				stillThere[kv.Key] = kv.Value;
				manifest.Failures++;
			}
		}
		// Only what really went comes out of the record. Emptying it regardless left any file that failed in the game
		// folder for good, with nothing remembering it was ours.
		manifest.Deployed = stillThere;
		return removed;
	}

	/// <summary>Deletes empty directories upward from <paramref name="dir"/>, stopping at <paramref name="limitRoot"/>.</summary>
	private static void CleanEmptyParents(string? dir, string limitRoot)
	{
		try
		{
			while (dir != null && dir.Length > limitRoot.Length && Directory.Exists(dir) &&
				   !Directory.EnumerateFileSystemEntries(dir).Any())
			{
				Directory.Delete(dir);
				dir = Path.GetDirectoryName(dir);
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Deploy", $"tidying up empty folders under {limitRoot}", ex); }
	}

	// -------------------------------------------------------------------------
	// Plugin load order (Skyrim / Fallout 4)
	// -------------------------------------------------------------------------

	/// <summary>Returns the active plugin names (asterisk-prefixed lines) from the game's plugins.txt.</summary>
	public static List<string> ReadActivePlugins(string activeGame, string gameFolder)
	{
		var result = new List<string>();
		string path = PluginsTxtPath(activeGame, gameFolder);
		if (string.IsNullOrEmpty(path) || !File.Exists(path)) return result;
		try
		{
			foreach (string raw in File.ReadAllLines(path))
			{
				string line = raw.Trim();
				if (line.StartsWith("*") && line.Length > 1)
					result.Add(line.Substring(1).Trim());
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Plugins", $"reading {path}", ex); }
		return result;
	}

	/// <summary>
	/// Writes plugins.txt as the authoritative active load order: one <c>*name</c> line per plugin in the
	/// given order, which the caller has already arranged masters-first. Replaces the previous per-mod
	/// add/remove approach so the order is deterministic.
	/// </summary>
	/// <param name="protect">
	/// When true the file is marked read-only after writing, which stops the game rewriting the active plugin
	/// list on its own (see <see cref="AppSettings.ProtectPluginOrder"/>). The read-only flag is always cleared
	/// first so the manager's own write succeeds either way.
	/// </param>
	public static void WritePluginsTxt(string activeGame, string gameFolder, IEnumerable<string> orderedActivePlugins, Action<string, string> logError, bool protect = false)
	{
		string path = PluginsTxtPath(activeGame, gameFolder);
		if (string.IsNullOrEmpty(path)) return;
		try
		{
			string? dir = Path.GetDirectoryName(path);
			if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
			SetReadOnly(path, false);
			AtomicFile.WriteAllLines(path, orderedActivePlugins.Select(p => "*" + p));
			if (protect) SetReadOnly(path, true);
		}
		catch (Exception ex) { logError(path, $"Failed to write plugins.txt: {ex.Message}"); }
	}

	/// <summary>
	/// Marks the game's plugins.txt read-only (or clears that), which is what actually stops Skyrim/Fallout 4
	/// deactivating Creations and reordering plugins when a new game is started — the game silently gives up on
	/// rewriting a read-only file and keeps loading the order it was given. Safe to call when the file does not
	/// exist yet. Returns true if the attribute was changed.
	/// </summary>
	public static bool SetPluginsTxtProtection(string activeGame, string gameFolder, bool protect, Action<string, string> logError)
	{
		string path = PluginsTxtPath(activeGame, gameFolder);
		if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
		try
		{
			if (IsReadOnly(path) == protect) return false;
			SetReadOnly(path, protect);
			return true;
		}
		catch (Exception ex)
		{
			logError(path, $"Failed to change the plugins.txt read-only flag: {ex.Message}");
			return false;
		}
	}

	private static bool IsReadOnly(string path) => (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;

	private static void SetReadOnly(string path, bool readOnly)
	{
		if (!File.Exists(path)) return;
		FileAttributes attrs = File.GetAttributes(path);
		File.SetAttributes(path, readOnly ? (attrs | FileAttributes.ReadOnly) : (attrs & ~FileAttributes.ReadOnly));
	}

	private static string PluginsTxtPath(string activeGame, string gameFolder)
	{
		string folderName = UserDataFolderName(activeGame, gameFolder);
		if (folderName.Length == 0) return "";
		string localAppData = UserFolders.LocalApplicationData;
		return Path.Combine(localAppData, folderName, "plugins.txt");
	}

	/// <summary>The absolute path of the game's active-plugins list (plugins.txt); empty for non-Bethesda games.</summary>
	public static string ActivePluginsTxtPath(string activeGame, string gameFolder) => PluginsTxtPath(activeGame, gameFolder);

	/// <summary>
	/// Finds the deepest common directory containing game files or plugins.
	/// </summary>
	public static string FindEffectiveModRoot(string tempDir)
	{
		var targets = Directory.GetFileSystemEntries(tempDir, "*", SearchOption.AllDirectories)
			.Where(p =>
			{
				string name = Path.GetFileName(p).ToLower();
				string ext = Path.GetExtension(p).ToLower();
				return ext == ".esp" || ext == ".esm" || ext == ".esl" ||
					   name == "interface" || name == "scripts" || name == "textures" ||
					   name == "meshes" || name == "music" || name == "sound" ||
					   name == "strings" || name == "skse" || name == "f4se";
			}).ToList();

		if (targets.Count == 0) return tempDir;

		string best = targets.OrderBy(p => p.Split(Path.DirectorySeparatorChar).Length).First();
		if (Directory.Exists(best))
		{
			return Path.GetDirectoryName(best) ?? tempDir;
		}
		else
		{
			return Path.GetDirectoryName(best) ?? tempDir;
		}
	}

	/// <summary>
	/// Resolves which extracted folder to store as the mod, adding "root-folder mod" support: files that belong in
	/// the game's root (ENB/ReShade injectors, an explicit <c>Root\</c> folder, tools/DLLs beside a <c>Data\</c>
	/// folder) are gathered under a reserved <c>Root\</c> subfolder, which the deployment engine maps back out to
	/// the game root. When the archive has no game-root content this falls back to <see cref="FindEffectiveModRoot"/>,
	/// so an ordinary Data-only mod is stored exactly as before (no behaviour change, no extra copying).
	/// </summary>
	private static string ResolveBethesdaModSource(string tempDir)
	{
		char sep = Path.DirectorySeparatorChar;
		string contentRoot = StripWrapperFolders(tempDir);
		string canonical = Path.GetFullPath(contentRoot).TrimEnd(sep) + sep;

		var files = Directory.GetFiles(contentRoot, "*.*", SearchOption.AllDirectories);
		var rels = files.Select(f => Path.GetFullPath(f).Substring(canonical.Length)).ToList();

		List<BethesdaLayout.Entry> plan = BethesdaLayout.Plan(rels);
		// No game-root content: keep the long-standing behaviour verbatim so normal installs can't regress.
		if (!BethesdaLayout.HasRootContent(plan)) return FindEffectiveModRoot(tempDir);

		// Realise the plan in a staging folder (Data content at the top level, game-root content under Root\).
		// Files are moved, not copied — staging lives on the same volume as the extraction, so this stays cheap.
		string stage = Path.Combine(tempDir, "__kmm_layout__");
		Directory.CreateDirectory(stage);
		foreach (BethesdaLayout.Entry e in plan)
		{
			string src = Path.Combine(contentRoot, e.Source.Replace('/', sep));
			string dst = Path.Combine(stage, e.Dest.Replace('/', sep));
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
				if (File.Exists(src)) File.Move(src, dst, overwrite: true);
			}
			catch
			{
				// Moving failed, so copy instead — across volumes, or with the source still open, a move cannot
				// work where a copy can. If the copy fails too the file simply is not deployed, and the mod is
				// quietly incomplete in the game folder, which is exactly the failure nobody can see.
				try { RobustCopy(src, dst); }
				catch (Exception ex) { DiagnosticLog.WriteException("Deploy", $"putting {src} into place at {dst}", ex); }
			}
		}
		return stage;
	}

	/// <summary>
	/// Descends through single-child wrapper folders (e.g. an archive that nests everything under "MyMod v1.2\")
	/// to reach the folder that actually holds the mod's content. Stops as soon as a child folder's name carries
	/// layout meaning (a Data/Root or known asset folder), so a real structure folder is never peeled away.
	/// </summary>
	private static string StripWrapperFolders(string dir)
	{
		while (true)
		{
			string[] entries = Directory.GetFileSystemEntries(dir);
			if (entries.Length != 1 || !Directory.Exists(entries[0])) break;
			if (BethesdaLayout.IsLayoutFolder(Path.GetFileName(entries[0]))) break;
			dir = entries[0];
		}
		return dir;
	}

	/// <summary>
	/// Whether <paramref name="path"/> sits inside <paramref name="folder"/>, by path segments rather than by
	/// characters — so a folder is not mistaken for a parent of a sibling whose name merely begins the same
	/// way. See PathRebase, which exists for the same class of mistake.
	/// </summary>
	private static bool IsUnder(string folder, string path)
	{
		if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(path)) return false;

		string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

		return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Extracts a .zip archive, backs up older version, and creates manifests for non-Stardew games.
	/// </summary>
	public static async Task<string> ExtractModAsync(
		string zipPath,
		string modsPath,
		List<GameMod> installedMods,
		string backupsPath,
		int maxBackups,
		string activeGame,
		Action<string, string> logError,
		string? nexusId = null,
		NexusService? nexusService = null,
		string? gitHubRepo = null,
		string? currentGamePath = null,
		Func<FomodConfig, Task<FomodSelection?>>? fomodSelector = null,
		IProgress<double>? installProgress = null,
		Func<string, string, bool>? confirmOverwrite = null,
		Func<string, Task<bool>>? runInstaller = null,
		bool matchExistingByNexusId = true,
		GameMod? updating = null,
		Func<string, string, SamePageChoice>? askSamePage = null)
	{
		// Two names, deliberately. The archive's own name is the one that identifies the download — the mod id and
		// version inside it are what match a reinstall to what is already there — while the folder the mod ends up
		// in is named after the mod, without the id, version and upload timestamp Nexus appends to every file.
		// Those used to be the same string, which is why installed mods sat in folders called
		// "Skyrim Access-181131-1-2-3-1723456789" and were announced by that name too.
		string archiveName = Path.GetFileNameWithoutExtension(zipPath);
		string installFolderName = ModScanner.SanitiseFolderName(ModDisplayName.Clean(archiveName, nexusId));
		if (installFolderName.Length == 0) installFolderName = ModScanner.SanitiseFolderName(archiveName);

		// The id Nexus buries in a file name is the only identity a hand-picked archive has, so it is read out of
		// the raw name before that name is cleaned away — otherwise a manual re-install of a mod already installed
		// would no longer recognise it and would leave two copies deploying the same files.
		if (string.IsNullOrEmpty(nexusId))
			nexusId = ModDisplayName.ModIdFromArchiveName(archiveName);

		// For Skyrim/Fallout 4 the mod's identity is known up front (its Nexus id or folder name), so a
		// reinstall can be confirmed before we even extract. Stardew's identity lives in manifest.json inside
		// the archive, so that prompt happens after extraction (below). A declined prompt cancels the install.
		if (updating == null && !GameProfiles.IsGame(activeGame, GameProfiles.StardewValley))
		{
			(GameMod? existing, InstallMatch match) = InstallTarget.Find(installedMods, nexusId, installFolderName, matchExistingByNexusId);
			if (existing != null && Directory.Exists(existing.FolderPath))
			{
				// Sharing a page does not make it the same mod: this may be a patch or an optional file. Only the player
				// knows, and saying no used to mean it could not be installed at all.
				if (match == InstallMatch.SamePage && askSamePage != null)
				{
					SamePageChoice choice = askSamePage(installFolderName, existing.Name);
					if (choice == SamePageChoice.Cancel)
						throw new OperationCanceledException("User chose not to install a second file from a mod page.");
					if (choice == SamePageChoice.InstallAlongside) matchExistingByNexusId = false;
				}
				else if (confirmOverwrite != null && !confirmOverwrite(existing.Name, existing.Version))
				{
					throw new OperationCanceledException("User declined to overwrite an existing mod.");
				}
			}
		}

		// Stage on the destination's own filesystem so a full system drive never blocks an install whose mods
		// live elsewhere, and so the final move stays a rename rather than a copy. ModArchive.TempRootFor
		// picks the spot and falls back to the system temp folder when it can't.
		string tempDir = Path.Combine(ModArchive.TempRootFor(modsPath), "Extract_" + Path.GetRandomFileName());
		try
		{
			await Task.Run(() => ModArchive.Extract(zipPath, tempDir, installProgress));
			ModArchive.GuardAgainstEscapedEntries(tempDir);

			if (GameProfiles.Find(activeGame)?.IsBepInEx == true)
			{
				return await FinalizeBepInExModAsync(
					tempDir, installFolderName, zipPath, modsPath, installedMods,
					backupsPath, maxBackups, logError, nexusId, nexusService, gitHubRepo, matchExistingByNexusId, updating);
			}

			if (GameProfiles.Find(activeGame)?.IsWitcher3 == true)
			{
				// A mod that ships as its author's installer is installed by running it — copying its files into
				// the mods folder would put most of them in the wrong place. The caller asks the user first and
				// waits for the installer to finish; only then is the mod treated as installed.
				string? installerExe = FindInstallerExecutable(tempDir);
				if (installerExe != null && runInstaller != null)
				{
					// Watch the game folder across the installer's run, because that is the only way to learn
					// what it put there — and without knowing, the mod could never be cleanly removed or
					// switched off again.
					var before = SnapshotWitcherGameFolder(currentGamePath ?? "");

					if (!await runInstaller(installerExe))
						throw new OperationCanceledException("The mod's installer did not complete.");

					return RecordInstallerFootprint(
						before, currentGamePath ?? "", modsPath, zipPath, nexusId, gitHubRepo, activeGame, logError);
				}

				return await FinalizeWitcher3ModAsync(
					tempDir, installFolderName, zipPath, modsPath, installedMods,
					backupsPath, maxBackups, activeGame, logError, nexusId, nexusService, gitHubRepo, currentGamePath, matchExistingByNexusId);
			}

			if (!GameProfiles.IsGame(activeGame, GameProfiles.StardewValley))
			{
				// Script extender (SKSE/F4SE)? Its loader exe and DLLs belong in the GAME ROOT, with its scripts
				// merging into Data. Detect it by the loader and install to the game folder directly — exactly like
				// the Accessibility Suite does. Without this, a normal mod install mis-identifies the mod root as the
				// Data sub-folder (it contains Scripts/SKSE) and drops the loader and DLLs entirely.
				string seLoaderName = ScriptExtenderLoaderName(activeGame);
				if (!string.IsNullOrEmpty(seLoaderName) && !string.IsNullOrEmpty(currentGamePath) && Directory.Exists(currentGamePath))
				{
					string[] seMatches = Directory.GetFiles(tempDir, seLoaderName, SearchOption.AllDirectories);
					if (seMatches.Length > 0)
					{
						string seRoot = Path.GetDirectoryName(seMatches[0]) ?? tempDir;
						CopyDirectoryRecursively(seRoot, currentGamePath);
						var relPaths = Directory.GetFiles(seRoot, "*.*", SearchOption.AllDirectories)
							.Select(f => f.Substring(seRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
							.ToList();
						SaveScriptExtenderManifest(activeGame, currentGamePath, relPaths);
						return GameProfiles.IsGame(activeGame, GameProfiles.SkyrimSE) ? "Skyrim Script Extender (SKSE64)" : "Fallout 4 Script Extender (F4SE)";
					}
				}

				// FOMOD scripted installers (e.g. Immersive Sounds Compendium) carry a fomod/ModuleConfig.xml
				// describing option groups and conditional file copies. When one is present, run it through the
				// FOMOD pipeline (the caller-supplied wizard, or auto-selected defaults) instead of the flat copy.
				if (FomodInstaller.TryFindFomod(tempDir, out string moduleConfigPath, out string? infoXmlPath, out string fomodRoot))
				{
					FomodConfig fomodConfig = FomodParser.ParseFile(moduleConfigPath);
					FomodInfo fomodInfo = infoXmlPath != null ? FomodParser.ParseInfo(infoXmlPath) : new FomodInfo();
					Func<string, FomodFileState> fileState = BuildFomodFileStateProvider(installedMods);

					FomodSelection? selection = fomodSelector != null
						? await fomodSelector(fomodConfig)
						: FomodInstaller.ComputeDefaultSelection(fomodConfig, fileState);
					if (selection == null)
						throw new OperationCanceledException("FOMOD installation was cancelled.");

					string stagingDir = Path.Combine(tempDir, "__fomod_stage__");
					FomodInstaller.BuildStaging(fomodConfig, selection, fomodRoot, stagingDir, fileState);
					// A FOMOD cannot place a file beside the game's .exe — its destinations are always relative
					// to Data — so a mod needing one ships it outside the scripted install. Without this the
					// screen-reader bridge DLL was simply dropped, and the mod installed mute.
					FomodInstaller.AddGameRootFiles(fomodRoot, stagingDir);

					return await FinalizeBethesdaModAsync(
						stagingDir, installFolderName, zipPath, modsPath, installedMods,
						backupsPath, maxBackups, activeGame, logError, nexusId, nexusService, gitHubRepo, currentGamePath, fomodInfo,
						docsSourceRoot: tempDir, matchExistingByNexusId: matchExistingByNexusId, updating: updating,
						buildFolder: ModFolderSwap.BuildFolderFor(tempDir));
				}

				return await FinalizeBethesdaModAsync(
					ResolveBethesdaModSource(tempDir), installFolderName, zipPath, modsPath, installedMods,
					backupsPath, maxBackups, activeGame, logError, nexusId, nexusService, gitHubRepo, currentGamePath, null,
					docsSourceRoot: tempDir, matchExistingByNexusId: matchExistingByNexusId, updating: updating,
					buildFolder: ModFolderSwap.BuildFolderFor(tempDir));
			}

			// Stardew Valley Manifest logic
			string[] manifests = Directory.GetFiles(tempDir, "manifest.json", SearchOption.AllDirectories);
			if (manifests.Length == 0)
			{
				// Some downloads wrap the mod itself in a second archive — a "pick the variant you want" pack, or
				// a zip that simply contains the real zip. Unpack one level of nested archives and look again
				// before declaring the download unusable.
				ModArchive.ExtractNested(tempDir);
				manifests = Directory.GetFiles(tempDir, "manifest.json", SearchOption.AllDirectories);
			}
			if (manifests.Length == 0)
				throw new ModArchiveContentException(ModArchive.DescribeMissingManifest(zipPath, tempDir));

			bool overwriteConfirmed = confirmOverwrite == null; // no callback => proceed without prompting
			var replacing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string mPath in manifests)
			{
				string uid, mName;
				try
				{
					JObject manifest = JObject.Parse(File.ReadAllText(mPath));
					uid = ((string?)manifest["UniqueID"]) ?? "";
					mName = ((string?)manifest["Name"]) ?? "Unknown";
				}
				catch (Exception ex)
				{
					// A manifest that cannot be read names no installed mod, so there is nothing of the user's at stake.
					logError(mPath, "Could not read a manifest in the download: " + ex.Message);
					continue;
				}

				var existing = installedMods.FirstOrDefault(m => m.UniqueId == uid);
				if (existing == null || !Directory.Exists(existing.FolderPath)) continue;

				// Confirm once before touching anything the user already has on disk.
				if (!overwriteConfirmed)
				{
					if (!confirmOverwrite!(existing.Name, existing.Version))
						throw new OperationCanceledException("User declined to overwrite an existing mod.");
					overwriteConfirmed = true;
				}

				// ⚠️ A backup that fails stops the install, with the old copy untouched. It used to be logged and
				// ignored, and the folder was then deleted with no copy anywhere.
				if (replacing.Add(existing.FolderPath))
				{
					BackupStore.CreateBackup(existing.FolderPath, mName, backupsPath);
					BackupStore.PruneBackups(mName, backupsPath, maxBackups);
				}

				// A Stardew mod keeps its settings and keybinds in here. Replacing the folder put them back to the
				// defaults on every update, with the only copy inside a backup nobody was told about.
				ModFolderSwap.KeepUserFile(existing.FolderPath, Path.GetDirectoryName(mPath)!, "config.json");
			}

			bool isGroup = manifests.Length > 1;
			string sourceFolderStardew, targetFolderNameStardew;

			if (isGroup)
			{
				// Climbs until every manifest is genuinely underneath, comparing whole path SEGMENTS rather
				// than characters. A plain StartsWith made "Mods/Auto" look like a parent of "Mods/AutoFish",
				// so a download holding both landed a level too high — every file in the wrong folder, and
				// nothing to say so.
				string commonPath = Path.GetDirectoryName(manifests[0]) ?? tempDir;
				foreach (string m in manifests)
				{
					string dir = Path.GetDirectoryName(m) ?? tempDir;
					while (!IsUnder(commonPath, dir))
						commonPath = Path.GetDirectoryName(commonPath) ?? tempDir;
				}
				sourceFolderStardew    = commonPath;
				targetFolderNameStardew = Path.GetFileName(sourceFolderStardew);
				if (sourceFolderStardew.TrimEnd('\\') == tempDir.TrimEnd('\\'))
					targetFolderNameStardew = installFolderName;
			}
			else
			{
				sourceFolderStardew = Path.GetDirectoryName(manifests[0]) ?? tempDir;
				JObject manifest = JObject.Parse(File.ReadAllText(manifests[0]));
				string mName     = ((string?)manifest["Name"]) ?? "Unknown";
				targetFolderNameStardew = Path.GetFileName(sourceFolderStardew);
				if (sourceFolderStardew.TrimEnd('\\') == tempDir.TrimEnd('\\'))
				{
					// Through the same cleaning every other name gets. Straight from the archive, a Name of "" or "." made the
					// destination the Mods folder itself, and the install replaced every mod with this one; ".." was the game.
					string fromName = ModScanner.SanitiseFolderName(mName.Replace(" ", ""));
					targetFolderNameStardew = fromName.Length > 0 ? fromName : installFolderName;
				}
			}

			string destModFolderStardew = Path.Combine(modsPath, targetFolderNameStardew);
			// Never anywhere but a folder directly inside Mods, whatever named it.
			if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(destModFolderStardew)),
					Path.GetFullPath(modsPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
				throw new ModArchiveContentException($"'{Path.GetFileName(zipPath)}' names a folder that isn't inside the Mods folder.");
			// Something else already in that folder is replaced as well, so it gets the same backup.
			if (Directory.Exists(destModFolderStardew) && replacing.Add(destModFolderStardew))
			{
				BackupStore.CreateBackup(destModFolderStardew, targetFolderNameStardew, backupsPath);
				BackupStore.PruneBackups(targetFolderNameStardew, backupsPath, maxBackups);
			}

			ModFolderSwap.ReplaceWithCopyOf(sourceFolderStardew, destModFolderStardew, replacing,
				ModArchive.TempRootFor(modsPath), logError);

			return (isGroup ? "Mod Group " : "") + targetFolderNameStardew;
		}
		finally
		{
			try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
			catch (Exception ex) { DiagnosticLog.WriteException("Install", $"clearing the temporary folder {tempDir}", ex); }

			string buildFolder = ModFolderSwap.BuildFolderFor(tempDir);
			try { if (Directory.Exists(buildFolder)) ModOnDisk.DeleteDirectory(buildFolder); }
			catch (Exception ex) { DiagnosticLog.WriteException("Install", $"clearing the temporary folder {buildFolder}", ex); }
		}
	}

	/// <summary>
	/// Reserved subfolder inside a mod where the manager keeps the mod's own documentation (README, guides,
	/// keybind lists). Deployment skips it, so it never reaches the game's Data folder.
	/// </summary>
	public const string DocsFolderName = ".kinetix_docs";

	/// <summary>
	/// Copies documentation files (markdown, readme/guide/keybind text, keybind HTML) found anywhere under
	/// <paramref name="archiveRoot"/> into <c>&lt;mod&gt;\.kinetix_docs\</c>, preserving relative paths.
	/// Bounded (skips large files, caps total) and best-effort: never throws into the install.
	/// </summary>
	private static void CaptureModDocs(string archiveRoot, string destModFolder)
	{
		try
		{
			if (!Directory.Exists(archiveRoot)) return;
			string docsDest = Path.Combine(destModFolder, DocsFolderName);
			string canonical = Path.GetFullPath(archiveRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

			int count = 0;
			long total = 0;
			foreach (string file in Directory.EnumerateFiles(archiveRoot, "*.*", SearchOption.AllDirectories))
			{
				string name = Path.GetFileName(file);
				string ext = Path.GetExtension(name).ToLowerInvariant();
				bool isDoc =
					ext == ".md" ||
					(ext == ".txt" && System.Text.RegularExpressions.Regex.IsMatch(name, "readme|guide|control|keybind|lisezmoi", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) ||
					((ext == ".html" || ext == ".htm") && System.Text.RegularExpressions.Regex.IsMatch(name, "keybind|control", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
				if (!isDoc) continue;

				var info = new FileInfo(file);
				if (info.Length > 2 * 1024 * 1024) continue;          // skip oversized "docs"
				if (count >= 50 || total > 10L * 1024 * 1024) break;   // overall safety cap

				string rel = Path.GetFullPath(file).Substring(canonical.Length);
				string dest = Path.Combine(docsDest, rel);
				Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
				File.Copy(file, dest, overwrite: true);
				count++;
				total += info.Length;
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("ModDocs", $"capturing the documentation shipped with {destModFolder}", ex); }
	}

	private static async Task<string> FinalizeBethesdaModAsync(
		string sourceFolder, string targetFolderName, string zipPath, string modsPath, List<GameMod> installedMods,
		string backupsPath, int maxBackups, string activeGame, Action<string, string> logError,
		string? nexusId, NexusService? nexusService, string? gitHubRepo, string? currentGamePath, FomodInfo? fomodInfo,
		string buildFolder, string? docsSourceRoot = null, bool matchExistingByNexusId = true, GameMod? updating = null)
	{
		// An update keeps the name of the copy it replaces. Taken from the download, a GitHub update's "X_github_latest.zip"
		// renamed the mod to that, and with it went its place in Mod Priority, its category, note and profile entries, all
		// kept against the old name; the next update made "X_github_latest_github_latest". The same for a Nexus download whose
		// name changed between versions ("A 2.0 SE"), and for a copy the user chose to replace: Mod Priority put it at the top.
		if ((InstallTarget.ForUpdate(updating, installedMods)
				?? InstallTarget.Find(installedMods, nexusId, targetFolderName, matchExistingByNexusId).Mod) is { FolderPath.Length: > 0 } updated)
		{
			string ownName = Path.GetFileName(ModEnableState.TargetPath(
				updated.FolderPath.TrimEnd(Path.DirectorySeparatorChar), enable: true, activeGame));
			if (ownName.Length > 0) targetFolderName = ownName;
		}

		// The new copy is built here and only moved into place once it is whole. See ModFolderSwap.
		string destModFolder = buildFolder;

		Directory.CreateDirectory(destModFolder);
		bool treatAsRoot = false;
		string[] rootDLLs = { "d3dx9_42.dll", "tbb.dll", "tbbmalloc.dll", "binkw64.dll" };
		foreach (string dll in rootDLLs)
		{
			if (File.Exists(Path.Combine(sourceFolder, dll)))
			{
				treatAsRoot = true;
				break;
			}
		}

		if (treatAsRoot && !Directory.Exists(Path.Combine(sourceFolder, "Root")))
		{
			string rootDest = Path.Combine(destModFolder, "Root");
			Directory.CreateDirectory(rootDest);
			foreach (string dir in Directory.GetDirectories(sourceFolder, "*", SearchOption.AllDirectories))
				Directory.CreateDirectory(PathRebase.To(sourceFolder, dir, rootDest));
			foreach (string file in Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories))
				RobustCopy(file, PathRebase.To(sourceFolder, file, rootDest));
		}
		else
		{
			foreach (string dir in Directory.GetDirectories(sourceFolder, "*", SearchOption.AllDirectories))
				Directory.CreateDirectory(PathRebase.To(sourceFolder, dir, destModFolder));
			foreach (string file in Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories))
				RobustCopy(file, PathRebase.To(sourceFolder, file, destModFolder));
		}

		// Preserve the mod's documentation (README/guide/keybind files) so the accessibility-controls
		// viewer can read its keybindings straight from what the author shipped. These often sit outside
		// the data files (e.g. a README.md at the archive root) and would otherwise be discarded.
		if (!string.IsNullOrEmpty(docsSourceRoot))
			CaptureModDocs(docsSourceRoot, destModFolder);

		// Generate manifest
		string manifestPath = Path.Combine(destModFolder, ".manager_manifest.json");
		string mName = targetFolderName;
		string mAuthor = "Unknown";
		string mDesc = "Installed local mod.";

		// Where each answer about the version comes from, kept apart until they can be weighed against each
		// other. Only the last of these describes the mod rather than the copy being installed — see
		// ModManifest.VersionOfTheInstalledCopy, which is where the difference is spelled out and why it matters.
		string? versionFromArchive = ModScanner.ExtractVersionFromFileName(zipPath, nexusId);
		string? versionFromFomod = null;
		string? versionFromModPage = null;

		// FOMOD info.xml seeds the metadata for locally-installed scripted mods that have no Nexus id.
		if (fomodInfo != null)
		{
			if (!string.IsNullOrWhiteSpace(fomodInfo.Name)) mName = fomodInfo.Name!;
			if (!string.IsNullOrWhiteSpace(fomodInfo.Version)) versionFromFomod = fomodInfo.Version;
			if (!string.IsNullOrWhiteSpace(fomodInfo.Author)) mAuthor = fomodInfo.Author!;
		}

		if (!string.IsNullOrEmpty(nexusId) && nexusService != null)
		{
			try
			{
				var details = await nexusService.GetModDetailsAsync(nexusId);
				if (details != null)
				{
					// The page is the authority on what the mod is called, who wrote it and what it does. It is
					// NOT the authority on which version is now sitting on this disk.
					mName = details["name"]?.ToString() ?? mName;
					mAuthor = details["author"]?.ToString() ?? mAuthor;
					mDesc = details["summary"]?.ToString() ?? mDesc;
					versionFromModPage = details["version"]?.ToString();
				}
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Nexus", $"looking up the details of mod {nexusId}", ex); }
		}

		// "1.0.0" as a last resort is a number nobody wrote down, and IsPlaceholderManifest knows to distrust it.
		string mVersion =
			ModManifest.VersionOfTheInstalledCopy(versionFromFomod, versionFromArchive, versionFromModPage) ?? "1.0.0";

		var manifest = new JObject
		{
			["Name"] = mName,
			["Version"] = mVersion,
			["Author"] = mAuthor,
			["UniqueID"] = targetFolderName,
			["Description"] = mDesc,
			["NexusID"] = nexusId,
			["GitHubRepo"] = gitHubRepo
		};
		AtomicFile.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));

		// Everything that could fail for the new copy has now had its chance, with the old one untouched.
		string finalFolder = Path.Combine(modsPath, targetFolderName);
		GameMod? existing = InstallTarget.ForUpdate(updating, installedMods) ?? InstallTarget.Find(installedMods, nexusId, targetFolderName, matchExistingByNexusId).Mod;
		var replacing = new List<string>();
		if (existing != null && Directory.Exists(existing.FolderPath)) replacing.Add(existing.FolderPath);
		if (Directory.Exists(finalFolder) && !replacing.Contains(finalFolder, StringComparer.OrdinalIgnoreCase)) replacing.Add(finalFolder);

		foreach (string old in replacing)
		{
			BackupStore.CreateBackup(old, targetFolderName, backupsPath);
			BackupStore.PruneBackups(targetFolderName, backupsPath, maxBackups);
		}

		// The old copy's plugin entries come out once it has gone. Its deployed files are reconciled by the caller's
		// SyncDeployment pass afterwards, so there is no per-file undeploy here.
		ModFolderSwap.Replace(buildFolder, finalFolder, replacing, ModArchive.TempRootFor(modsPath), logError,
			parked =>
			{
				if (!string.IsNullOrEmpty(currentGamePath))
					SyncPluginsFile(parked, activeGame, currentGamePath, false, logError);
			});

		return targetFolderName;
	}

	/// <summary>The name the manifest records a Witcher 3 mod's out-of-folder files under.</summary>
	private const string WitcherExtraPathsKey = "ExtraPaths";

	/// <summary>Where game files a Witcher 3 mod replaced are kept, inside the game folder. See <see cref="OriginalFiles"/>.</summary>
	public const string WitcherOriginalsFolderName = Witcher3ModFiles.OriginalsFolderName;

	/// <summary>
	/// The parts of a Witcher 3 install a mod can write into. Everything else — above all <c>content</c>, which
	/// is the game's own packed data and tens of gigabytes of it — is left out, so taking the snapshot below
	/// costs a moment rather than a disk crawl.
	/// </summary>
	private static readonly string[] WitcherModdableFolders = { "mods", "dlc", "bin", "plugins" };

	/// <summary>
	/// Every file in the places a Witcher 3 mod can install to, with the size and time it was last written.
	///
	/// This exists because of a kind of mod the manager cannot otherwise account for: one that installs itself by
	/// running its author's program. The manager never sees those files being copied, so it has no idea what the
	/// mod consists of — and a mod it cannot describe is one it cannot cleanly remove or switch off. Taking one
	/// of these before the installer runs and one after, and comparing them, answers the question the only way
	/// available: by watching what actually changed on disk.
	/// </summary>
	public static Dictionary<string, string> SnapshotWitcherGameFolder(string gameFolder)
	{
		var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrEmpty(gameFolder) || !Directory.Exists(gameFolder)) return snapshot;

		try
		{
			// Loose files at the game's root, where an ASI loader or a shim DLL usually lands.
			foreach (string file in Directory.GetFiles(gameFolder))
				Record(snapshot, gameFolder, file);

			foreach (string folderName in WitcherModdableFolders)
			{
				string folder = Path.Combine(gameFolder, folderName);
				if (!Directory.Exists(folder)) continue;

				foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
					Record(snapshot, gameFolder, file);
			}
		}
		catch (Exception ex)
		{
			// A partial snapshot is still worth having: it can only make the recorded footprint smaller, never
			// make it claim files that aren't the mod's.
			DiagnosticLog.WriteException("Witcher3", $"recording what {gameFolder} held before the install", ex);
		}

		return snapshot;

		static void Record(Dictionary<string, string> into, string root, string file)
		{
			try
			{
				var info = new FileInfo(file);
				into[file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)]
					= info.Length + "|" + info.LastWriteTimeUtc.Ticks;
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Witcher3", $"recording {file} in the game-folder snapshot", ex); }
		}
	}

	/// <summary>
	/// What an installer added or replaced, as paths relative to the game folder: everything in the folder now
	/// that wasn't there before, or that is no longer the same file.
	///
	/// Files the installer <em>changed</em> rather than created are deliberately included, because that is how
	/// the interesting ones arrive — an accessibility mod replaces the game's own config XML and rewrites the
	/// file list that indexes it. They are recorded separately from created ones by the caller so that removing
	/// the mod can delete what it brought and leave alone what it merely edited.
	/// </summary>
	public static (List<string> Added, List<string> Changed) DiffWitcherGameFolder(
		Dictionary<string, string> before, string gameFolder)
	{
		var added = new List<string>();
		var changed = new List<string>();

		foreach (var entry in SnapshotWitcherGameFolder(gameFolder))
		{
			if (!before.TryGetValue(entry.Key, out string? was)) added.Add(entry.Key);
			else if (!string.Equals(was, entry.Value, StringComparison.Ordinal)) changed.Add(entry.Key);
		}

		added.Sort(StringComparer.OrdinalIgnoreCase);
		changed.Sort(StringComparer.OrdinalIgnoreCase);
		return (added, changed);
	}

	/// <summary>
	/// The installer executable inside an extracted archive, or <c>null</c> when it holds none.
	///
	/// Some mods are not a folder to be copied but a program to be run — The Witcher 3's accessibility mod is
	/// one, because what it installs goes to four different places at once: a mod folder, an .asi and its
	/// screen-reader DLLs beside the game exe, an XML in the game's config matrix (plus a line in the file list
	/// that indexes it), and an entry in the player's own settings. No file-copying manager reproduces that
	/// correctly, so the right thing is to let the author's installer do its job.
	/// </summary>
	public static string? FindInstallerExecutable(string extractedRoot)
	{
		try
		{
			string[] exes = Directory.GetFiles(extractedRoot, "*.exe", SearchOption.AllDirectories);
			if (exes.Length == 0) return null;

			// Named like an installer wins outright; otherwise a single exe in the archive is taken to be one.
			string? named = exes.FirstOrDefault(e =>
				Path.GetFileName(e).Contains("install", StringComparison.OrdinalIgnoreCase) ||
				Path.GetFileName(e).Contains("setup", StringComparison.OrdinalIgnoreCase));

			return named ?? (exes.Length == 1 ? exes[0] : null);
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Installs an extracted Witcher 3 mod and returns the name it is listed under.
	///
	/// A Witcher 3 mod is a folder under the game's own <c>mods</c> folder whose name begins with <c>mod</c> —
	/// that prefix is not a convention but the rule the engine loads by, so a mod whose archive unpacks to some
	/// other name is renamed rather than left never to load. The larger mods also bring parts that belong
	/// elsewhere in the game: a <c>dlc</c> folder, and menu XMLs under <c>bin</c>. Those are copied where they
	/// belong and their paths recorded, so uninstalling the mod can take them with it instead of leaving them
	/// behind for the player to find later.
	/// </summary>
	private static async Task<string> FinalizeWitcher3ModAsync(
		string tempDir, string archiveName, string zipPath, string modsPath, List<GameMod> installedMods,
		string backupsPath, int maxBackups, string activeGame, Action<string, string> logError,
		string? nexusId, NexusService? nexusService, string? gitHubRepo, string? currentGamePath, bool matchExistingByNexusId = true)
	{
		List<string> modFolders = FindWitcher3ModFolders(tempDir);

		// Nothing named mod* anywhere: an archive that is the mod's insides (a bare content folder) rather than
		// the mod folder itself. Wrap it in a folder the engine will actually load.
		string wrapped = "";
		if (modFolders.Count == 0)
		{
			string source = ResolveBethesdaModSource(tempDir);
			if (!Directory.Exists(Path.Combine(source, "content")))
				throw new ModArchiveContentException(
					$"'{Path.GetFileName(zipPath)}' doesn't look like a Witcher 3 mod: it has no folder named mod… " +
					"and no content folder to make one from.");

			wrapped = source;
			modFolders.Add(source);
		}

		string primaryFolderName = "";
		var extraPaths = new List<string>();
		// What the copies about to be replaced put in the game folder, read while they are still here. Those files are
		// that mod's, so a reinstall overwriting them is not touching anything of the game's.
		var alreadyTheMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		// ⚠️ Not for a copy that is switched off. Its files were parked out of the game and the game's own put back, so what
		// sits at those paths now is the game's. Counted as the mod's, the reinstall wrote over the game's file and kept
		// nothing, and the parked copies outlived it: RemoveExtras later took them for the live ones and left the mod in
		// the game. So a parked path is kept like any game file, and the stale parked folder goes once the new copy is in.
		var staleParked = new List<string>();
		foreach (string source in modFolders)
		{
			string name = source == wrapped ? WitcherModFolderName(archiveName) : Path.GetFileName(source);
			GameMod? had = InstallTarget.Find(installedMods, nexusId, name, matchExistingByNexusId).Mod;
			if (had == null) continue;

			string parked = string.IsNullOrEmpty(currentGamePath) ? "" : Path.Combine(currentGamePath, Witcher3ModFiles.DisabledExtrasFolderName,
				Witcher3ModSettings.BareName(Path.GetFileName(had.FolderPath.TrimEnd(Path.DirectorySeparatorChar))));
			alreadyTheMods.UnionWith(ReadWitcherExtraPaths(had.FolderPath)
				.Where(rel => parked.Length == 0 || !File.Exists(Path.Combine(parked, rel))));
			if (parked.Length > 0 && Directory.Exists(parked)) staleParked.Add(parked);
		}
		var originals = new OriginalFiles(Path.Combine(currentGamePath ?? "", WitcherOriginalsFolderName));

		foreach (string source in modFolders)
		{
			string folderName = source == wrapped
				? WitcherModFolderName(archiveName)
				: Path.GetFileName(source);

			string dest = Path.Combine(modsPath, folderName);

			// A disabled copy sits under the tilde name and would otherwise survive the reinstall, leaving the
			// mod installed twice under two names.
			GameMod? existing = InstallTarget.Find(installedMods, nexusId, folderName, matchExistingByNexusId).Mod;
			var replacing = new List<string>();
			if (existing != null && Directory.Exists(existing.FolderPath)) replacing.Add(existing.FolderPath);
			foreach (string other in new[] { Path.Combine(modsPath, "~" + folderName), dest })
				if (Directory.Exists(other) && !replacing.Contains(other, StringComparer.OrdinalIgnoreCase)) replacing.Add(other);

			// Backed up first, as the other games' installs do, and a failure stops here with nothing changed.
			foreach (string old in replacing)
			{
				BackupStore.CreateBackup(old, folderName, backupsPath);
				BackupStore.PruneBackups(folderName, backupsPath, maxBackups);
			}

			ModFolderSwap.ReplaceWithCopyOf(source, dest, replacing, ModArchive.TempRootFor(modsPath), logError);

			if (primaryFolderName.Length == 0) primaryFolderName = folderName;
		}

		// The parts that live in the game folder rather than the mods folder.
		if (!string.IsNullOrEmpty(currentGamePath) && Directory.Exists(currentGamePath))
		{
			foreach (string extraName in new[] { "dlc", "bin" })
			{
				foreach (string source in FindTopLevelFolders(tempDir, extraName))
				{
					string dest = Path.Combine(currentGamePath, extraName);
					foreach (string file in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
					{
						string relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
						string targetFile = Path.Combine(dest, relative);
						Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
						string extraPath = Path.Combine(extraName, relative);
						// Something already there that this mod did not put there is kept, and comes back when the mod goes.
						if (!alreadyTheMods.Contains(extraPath)) originals.Keep(targetFile, extraPath);
						RobustCopy(file, targetFile);
						extraPaths.Add(extraPath);
					}
				}
			}
		}

		// The new copy goes in switched on, so what the old one had parked is no longer anything's. See alreadyTheMods.
		foreach (string parked in staleParked)
		{
			try { ModOnDisk.DeleteDirectory(parked); }
			catch (Exception ex) { logError(parked, "Could not clear the old copy's parked files: " + ex.Message); }
		}

		string destModFolder = Path.Combine(modsPath, primaryFolderName);

		// The mod's own documentation, so the F3 viewer and the controls list have something to read.
		CaptureModDocs(tempDir, destModFolder);

		string mName = primaryFolderName;
		string mAuthor = "Unknown";
		string mDesc = "Installed local mod.";

		string? versionFromArchive = ModScanner.ExtractVersionFromFileName(zipPath, nexusId);
		string? versionFromModPage = null;

		if (!string.IsNullOrEmpty(nexusId) && nexusService != null)
		{
			try
			{
				var details = await nexusService.GetModDetailsAsync(nexusId);
				if (details != null)
				{
					// As above: the page names the mod, the archive dates the copy. See
					// ModManifest.VersionOfTheInstalledCopy.
					mName = details["name"]?.ToString() ?? mName;
					mAuthor = details["author"]?.ToString() ?? mAuthor;
					mDesc = details["summary"]?.ToString() ?? mDesc;
					versionFromModPage = details["version"]?.ToString();
				}
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Nexus", $"looking up the details of mod {nexusId}", ex); }
		}

		string mVersion =
			ModManifest.VersionOfTheInstalledCopy(null, versionFromArchive, versionFromModPage) ?? "1.0.0";

		var manifest = new JObject
		{
			["Name"] = mName,
			["Version"] = mVersion,
			["Author"] = mAuthor,
			["UniqueID"] = primaryFolderName,
			["Description"] = mDesc,
			["NexusID"] = nexusId,
			["GitHubRepo"] = gitHubRepo,
			[WitcherExtraPathsKey] = new JArray(extraPaths.Distinct(StringComparer.OrdinalIgnoreCase))
		};
		AtomicFile.WriteAllText(Path.Combine(destModFolder, ".manager_manifest.json"), manifest.ToString(Formatting.Indented));

		// Tell the game's own mod list the new mod is on, so its in-game menu agrees with the manager's.
		SyncWitcherModSettings(destModFolder, true, activeGame);

		return primaryFolderName;
	}

	/// <summary>
	/// Works out what an installer just installed and writes it down, returning the mod's folder name.
	///
	/// The mod folder the installer created is the one the manager lists the mod under, so it is taken from what
	/// actually appeared rather than guessed from the archive's name. Everything else the installer left behind —
	/// the native plugin beside the game exe, its sounds, the config XML it replaced — is recorded as the mod's
	/// footprint, which is what makes uninstalling and disabling it possible later.
	/// </summary>
	private static string RecordInstallerFootprint(
		Dictionary<string, string> before, string gameFolder, string modsPath, string zipPath,
		string? nexusId, string? gitHubRepo, string activeGame, Action<string, string> logError)
	{
		// Named after the mod, not after the download — the same rule the other install paths follow.
		string rawArchiveName = Path.GetFileNameWithoutExtension(zipPath);
		string archiveName = ModDisplayName.Clean(rawArchiveName, nexusId);
		if (archiveName.Length == 0) archiveName = rawArchiveName;
		if (string.IsNullOrEmpty(gameFolder)) return WitcherModFolderName(archiveName);

		var (added, changed) = DiffWitcherGameFolder(before, gameFolder);

		// The mod folder is whichever mods\mod* folder the installer created files in.
		string modFolderName = added.Concat(changed)
			.Select(WitcherModFolderNameFromRelativePath)
			.FirstOrDefault(name => name.Length > 0)
			?? "";

		if (modFolderName.Length == 0) modFolderName = WitcherModFolderName(archiveName);

		string destModFolder = Path.Combine(modsPath, modFolderName);
		try
		{
			Directory.CreateDirectory(destModFolder);

			// The mod's own folder is not part of the footprint: deleting the mod deletes that folder anyway,
			// and listing its files here would only have them deleted twice.
			string ownPrefix = Path.Combine("mods", modFolderName) + Path.DirectorySeparatorChar;
			bool IsOwn(string rel) => rel.StartsWith(ownPrefix, StringComparison.OrdinalIgnoreCase);

			// On a reinstall the mod's own files are already there, so they show up as changed rather than added. Going by
			// that alone rewrote the record without them, and the mod's .asi then stayed in the game when it was switched
			// off: "disabled", and still running. What the last install recorded stays recorded, if it is still there.
			List<string> hadBefore = ReadWitcherExtraPaths(destModFolder)
				.Where(rel => File.Exists(Path.Combine(gameFolder, rel)))
				.ToList();
			var footprint = added.Where(rel => !IsOwn(rel)).Union(hadBefore, StringComparer.OrdinalIgnoreCase).ToList();
			var edited = changed.Where(rel => !IsOwn(rel)).Except(hadBefore, StringComparer.OrdinalIgnoreCase).ToList();

			var manifest = new JObject
			{
				["Name"] = modFolderName,
				["Version"] = ModScanner.ExtractVersionFromFileName(zipPath, nexusId) ?? "1.0.0",
				["Author"] = "Unknown",
				["UniqueID"] = modFolderName,
				["Description"] = "Installed by the mod's own installer.",
				["NexusID"] = nexusId,
				["GitHubRepo"] = gitHubRepo,
				["InstalledByInstaller"] = true,
				[WitcherExtraPathsKey] = new JArray(footprint),
				// Files the installer replaced rather than created — the game's own, edited in place. Removing
				// the mod must not delete these; they are recorded so the manager can say what was touched.
				["EditedPaths"] = new JArray(edited)
			};

			AtomicFile.WriteAllText(
				Path.Combine(destModFolder, ".manager_manifest.json"),
				manifest.ToString(Formatting.Indented));
		}
		catch (Exception ex)
		{
			logError(destModFolder, "Could not record what the installer installed: " + ex.Message);
		}

		SyncWitcherModSettings(destModFolder, true, activeGame);
		return modFolderName;
	}

	/// <summary>The mod folder name in a path like <c>mods\modFoo\content\…</c>, or <c>""</c> when it isn't one.</summary>
	private static string WitcherModFolderNameFromRelativePath(string relativePath)
	{
		string[] parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (parts.Length < 2) return "";
		if (!parts[0].Equals("mods", StringComparison.OrdinalIgnoreCase)) return "";
		return parts[1].StartsWith("mod", StringComparison.OrdinalIgnoreCase) ? parts[1] : "";
	}

	/// <summary>
	/// Every folder in the extracted archive that is a Witcher 3 mod folder — one named <c>mod*</c> that isn't
	/// itself inside another. An archive holding several is normal: mods routinely ship an optional patch or a
	/// compatibility variant as a second mod folder beside the first.
	/// </summary>
	private static List<string> FindWitcher3ModFolders(string root)
	{
		try
		{
			// The rules live in Witcher3Layout so they can be tested against real archive layouts — in
			// particular the "mods" wrapper, which begins with "mod" and so used to be taken for a mod itself.
			return Witcher3Layout.SelectModFolders(Directory.GetDirectories(root, "*", SearchOption.AllDirectories));
		}
		catch
		{
			return new List<string>();
		}
	}

	/// <summary>Folders called <paramref name="name"/> anywhere in the extracted archive that aren't nested inside
	/// a mod folder (a mod's own <c>bin</c> belongs to the mod, not to the game).</summary>
	private static List<string> FindTopLevelFolders(string root, string name)
	{
		var result = new List<string>();
		try
		{
			foreach (string dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
			{
				if (!Path.GetFileName(dir).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;

				string parent = Path.GetFileName(Path.GetDirectoryName(dir) ?? "");
				if (parent.StartsWith("mod", StringComparison.OrdinalIgnoreCase)) continue;

				result.Add(dir);
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Install", $"listing the folders inside {root}", ex); }
		return result;
	}

	/// <summary>
	/// A folder name The Witcher 3 will load, made from an archive's name: stripped of the download suffixes
	/// Nexus adds, and given the <c>mod</c> prefix the engine insists on if it hasn't got one.
	/// </summary>
	private static string WitcherModFolderName(string archiveName)
	{
		// Cleaned by the same rules every other name goes through, so a Witcher folder is not the one place that
		// keeps a tail the rest of the manager knows how to remove.
		string cleaned = ModScanner.SanitiseFolderName(ModDisplayName.Clean(archiveName));

		if (cleaned.Length == 0) cleaned = "Mod";
		cleaned = cleaned.Replace(" ", "");

		return cleaned.StartsWith("mod", StringComparison.OrdinalIgnoreCase) ? cleaned : "mod" + cleaned;
	}

	// Removing a Witcher 3 mod's outside files and switching them on or off is Witcher3ModFiles, in the core. This file held
	// its own copies of both until November 2026. See SetModEnabled and Form1.DeleteSelectedMod.

	/// <summary>An uninstaller Windows has registered for a mod that installed itself into the game folder.</summary>
	public sealed record ModUninstaller(string DisplayName, string Command, string RegistryKeyPath);

	/// <summary>
	/// The uninstaller registered by a mod that installed itself into <paramref name="gameFolder"/>, or
	/// <c>null</c> when there is none.
	///
	/// A mod that ships as a setup program usually leaves an uninstaller behind, and that uninstaller is a far
	/// better answer than anything the manager can work out for itself: it holds the installer's own record of
	/// every file it wrote, including the ones in places the manager would never think to look. The Witcher 3's
	/// accessibility mod is built with Inno Setup and does exactly this, leaving <c>unins000.exe</c> in the game
	/// folder.
	///
	/// Candidates are recognised by where they point — an uninstaller living inside the game folder was put there
	/// by something that installed into the game — and preferred by name when one matches the mod.
	/// </summary>
	public static ModUninstaller? FindUninstallerInsideGame(string gameFolder, string modFolderName)
	{
		// What Windows lists is for the real install, and running it from a test copy would uninstall the real mod.
		if (TestMode.IsOn || string.IsNullOrEmpty(gameFolder)) return null;

		var candidates = new List<ModUninstaller>();
		string root;
		try { root = Path.GetFullPath(gameFolder).TrimEnd(Path.DirectorySeparatorChar); }
		catch { return null; }

		(Microsoft.Win32.RegistryKey Hive, string Path)[] places =
		{
			(Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
			(Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
			(Microsoft.Win32.Registry.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
		};

		foreach (var place in places)
		{
			try
			{
				using var uninstallKey = place.Hive.OpenSubKey(place.Path);
				if (uninstallKey == null) continue;

				foreach (string subKeyName in uninstallKey.GetSubKeyNames())
				{
					try
					{
						using var entry = uninstallKey.OpenSubKey(subKeyName);
						string? command = entry?.GetValue("UninstallString")?.ToString();
						if (string.IsNullOrEmpty(command)) continue;

						// Only ever an uninstaller that lives inside this game's folder. Anything else belongs to
						// a program that has nothing to do with the mod being removed.
						if (command.IndexOf(root, StringComparison.OrdinalIgnoreCase) < 0) continue;

						candidates.Add(new ModUninstaller(
							entry?.GetValue("DisplayName")?.ToString() ?? subKeyName,
							command,
							place.Path + "\\" + subKeyName));
					}
					catch (Exception ex) { DiagnosticLog.WriteException("Uninstall", $"reading the uninstall entry {subKeyName}", ex); }
				}
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Uninstall", $"reading the uninstall entries under {place.Path}", ex); }
		}

		if (candidates.Count == 0) return null;

		// Only one whose name is this mod's — "modWitcherAccess" against "WitcherAccess v0.3". No match means no
		// uninstaller, never "the first one we found": see Witcher3Layout.UninstallerBelongsToMod for what that
		// fallback did.
		return candidates.FirstOrDefault(c => Witcher3Layout.UninstallerBelongsToMod(modFolderName, c.DisplayName));
	}

	/// <summary>
	/// Whether <paramref name="uninstaller"/>'s registry entry is still there.
	///
	/// Needed because of how a silent Inno Setup uninstall behaves: the executable copies itself to a temporary
	/// folder, starts that copy and exits immediately, so waiting on the process the manager started proves
	/// nothing. The entry disappearing is the real signal that the uninstall has finished.
	/// </summary>
	public static bool UninstallerStillRegistered(ModUninstaller uninstaller)
	{
		foreach (Microsoft.Win32.RegistryKey hive in
			new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
		{
			try
			{
				using var key = hive.OpenSubKey(uninstaller.RegistryKeyPath);
				if (key?.GetValue("UninstallString") != null) return true;
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Uninstall", $"checking whether {uninstaller.RegistryKeyPath} is still registered", ex); }
		}
		return false;
	}

	/// <summary>The out-of-folder files recorded for a mod, or an empty list when it has none.</summary>
	private static List<string> ReadWitcherExtraPaths(string modFolderPath)
	{
		var paths = new List<string>();
		try
		{
			string manifestPath = Path.Combine(modFolderPath, ".manager_manifest.json");
			if (!File.Exists(manifestPath)) return paths;

			if (JObject.Parse(File.ReadAllText(manifestPath))[WitcherExtraPathsKey] is not JArray extras) return paths;

			foreach (JToken entry in extras)
			{
				string? relative = entry?.ToString();
				if (!string.IsNullOrEmpty(relative)) paths.Add(relative);
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Witcher3", "reading the extra paths the mod recorded", ex); }
		return paths;
	}

	/// <summary>
	/// Installs an extracted BepInEx mod into <c>BepInEx\plugins</c> as a folder of its own, and returns the name
	/// that folder was given.
	///
	/// BepInEx mods arrive in two shapes. Most are "extract this over your game folder" archives that carry a
	/// <c>BepInEx\</c> tree inside, in which case the plugins, patchers and default configs within it each belong
	/// somewhere different. The rest are a bare DLL, or a folder holding one. Both end up the same way here: the
	/// plugin's own files in one folder under <c>plugins</c>, named after the plugin rather than after whatever
	/// the archive happened to be called, so the mod list shows the name the author gave it.
	/// </summary>
	private static async Task<string> FinalizeBepInExModAsync(
		string tempDir, string archiveName, string zipPath, string modsPath, List<GameMod> installedMods,
		string backupsPath, int maxBackups, Action<string, string> logError,
		string? nexusId, NexusService? nexusService, string? gitHubRepo, bool matchExistingByNexusId = true, GameMod? updating = null)
	{
		string bepInExRoot = Path.GetDirectoryName(modsPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "";

		// An archive built to be dropped on the game folder has a BepInEx\ directory somewhere inside it.
		string? packagedBepInEx = Directory.GetDirectories(tempDir, "BepInEx", SearchOption.AllDirectories).FirstOrDefault();

		string modSource;
		if (packagedBepInEx != null)
		{
			string packagedPlugins = Path.Combine(packagedBepInEx, "plugins");

			// Preloader patchers load before the game does and cannot live under plugins\, so they go to their own
			// folder. A default config is copied only when the user has none, so reinstalling never overwrites
			// settings the user has already changed.
			CopyBepInExSideFolder(Path.Combine(packagedBepInEx, "patchers"), Path.Combine(bepInExRoot, "patchers"), overwrite: true, logError);
			CopyBepInExSideFolder(Path.Combine(packagedBepInEx, "config"), Path.Combine(bepInExRoot, "config"), overwrite: false, logError);

			modSource = Directory.Exists(packagedPlugins) ? ResolveBepInExModRoot(packagedPlugins) : StripWrapperFolders(tempDir);
		}
		else
		{
			modSource = ResolveBepInExModRoot(tempDir);
		}

		// Name the folder after the plugin itself where we can read it, falling back to the archive name.
		BepInExPluginInfo identity = BepInExPlugin.Identify(modSource);
		// With no readable plugin name, Identify falls back on the folder the files are in. For a DLL at the top of an
		// archive that is the random temporary folder, and the mod was installed as "Extract_x1y2z3", matching nothing.
		string pluginName = identity.Name == Path.GetFileName(tempDir.TrimEnd(Path.DirectorySeparatorChar)) ? "" : identity.Name;
		string targetFolderName = ModScanner.SanitiseFolderName(pluginName.Length > 0 ? pluginName : archiveName);
		if (targetFolderName.Length == 0) targetFolderName = ModScanner.SanitiseFolderName(archiveName);

		// The new copy is built beside the extraction and only moved into place once it is whole. See ModFolderSwap.
		string finalFolder = Path.Combine(modsPath, targetFolderName);
		string destModFolder = ModFolderSwap.BuildFolderFor(tempDir);
		ModFolderSwap.CopyTree(modSource, destModFolder);

		// Keep whatever documentation the author shipped, so the controls viewer can read the mod's keybindings.
		CaptureModDocs(tempDir, destModFolder);

		string mName = pluginName.Length > 0 ? pluginName : targetFolderName;
		string mVersion = identity.Version.Length > 0
			? identity.Version
			: (ModScanner.ExtractVersionFromFileName(zipPath, nexusId) ?? "1.0.0");
		string mAuthor = "Unknown";
		string mDesc = "Installed BepInEx plugin.";

		if (!string.IsNullOrEmpty(nexusId) && nexusService != null)
		{
			try
			{
				var details = await nexusService.GetModDetailsAsync(nexusId);
				if (details != null)
				{
					// The plugin's own name and version stay authoritative — the Nexus page's version is the
					// version of the download, which routinely differs from what the plugin reports.
					mAuthor = details["author"]?.ToString() ?? mAuthor;
					mDesc = details["summary"]?.ToString() ?? mDesc;
					if (pluginName.Length == 0) mName = details["name"]?.ToString() ?? mName;
				}
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Nexus", $"looking up the details of mod {nexusId}", ex); }
		}

		var manifest = new JObject
		{
			["Name"]        = mName,
			["Version"]     = mVersion,
			["Author"]      = mAuthor,
			["UniqueID"]    = identity.Guid.Length > 0 ? identity.Guid : targetFolderName,
			["Description"] = mDesc,
			["NexusID"]     = nexusId,
			["GitHubRepo"]  = gitHubRepo
		};
		AtomicFile.WriteAllText(Path.Combine(destModFolder, ".manager_manifest.json"), manifest.ToString(Formatting.Indented));

		// Whatever is already installed is replaced, whether the manager knows it as this mod (matched by Nexus id)
		// or simply as a folder of the same name. A disabled copy counts too: it is the same mod.
		GameMod? existing = InstallTarget.ForUpdate(updating, installedMods) ?? InstallTarget.Find(installedMods, nexusId, targetFolderName, matchExistingByNexusId).Mod;
		var replacing = new List<string>();
		if (existing != null && Directory.Exists(existing.FolderPath)) replacing.Add(existing.FolderPath);
		if (Directory.Exists(finalFolder) && !replacing.Contains(finalFolder, StringComparer.OrdinalIgnoreCase)) replacing.Add(finalFolder);

		foreach (string old in replacing)
		{
			BackupStore.CreateBackup(old, targetFolderName, backupsPath);
			BackupStore.PruneBackups(targetFolderName, backupsPath, maxBackups);
		}

		ModFolderSwap.Replace(destModFolder, finalFolder, replacing, ModArchive.TempRootFor(modsPath), logError);

		return targetFolderName;
	}

	/// <summary>
	/// Finds the folder inside an extracted BepInEx archive that holds the mod itself, peeling off the "MyMod
	/// v1.2\" style wrapper folders archives are usually built with. A folder holding the plugin DLLs directly is
	/// the answer; a single subfolder containing them means the archive wrapped the mod one level deeper.
	/// </summary>
	private static string ResolveBepInExModRoot(string dir)
	{
		try
		{
			while (true)
			{
				// DLLs at this level mean this is the mod.
				if (Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly).Length > 0) return dir;

				string[] children = Directory.GetDirectories(dir);
				if (children.Length != 1) return dir;
				dir = children[0];
			}
		}
		catch
		{
			return dir;
		}
	}

	/// <summary>
	/// Copies one of the folders that sit beside <c>plugins</c> (<c>patchers</c>, <c>config</c>) out of an
	/// archive into the game's own BepInEx folder. With <paramref name="overwrite"/> false an existing file is
	/// left alone, which is what a shipped default config wants: the user's edited settings must survive a
	/// reinstall.
	/// </summary>
	private static void CopyBepInExSideFolder(string source, string target, bool overwrite, Action<string, string> logError)
	{
		try
		{
			if (!Directory.Exists(source)) return;
			Directory.CreateDirectory(target);

			foreach (string file in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
			{
				string relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				string destination = Path.Combine(target, relative);
				if (!overwrite && File.Exists(destination)) continue;
				Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
				RobustCopy(file, destination);
			}
		}
		catch (Exception ex)
		{
			logError(source, "Could not install BepInEx side folder: " + ex.Message);
		}
	}

	/// <summary>
	/// Builds a plugin-state lookup for FOMOD <c>fileDependency</c> checks: base-game masters count as
	/// Active, a plugin found in an installed mod reflects that mod's enabled state, and anything else is
	/// Missing. Good enough for default selection; richer load-order awareness can refine it later.
	/// </summary>
	internal static Func<string, FomodFileState> BuildFomodFileStateProvider(List<GameMod> installedMods)
	{
		var baseMasters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm",
			"Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm", "DLCCoast.esm", "DLCworkshop02.esm",
			"DLCworkshop03.esm", "DLCNukaWorld.esm"
		};
		return file =>
		{
			if (string.IsNullOrEmpty(file)) return FomodFileState.Missing;
			if (baseMasters.Contains(file)) return FomodFileState.Active;
			foreach (GameMod m in installedMods)
			{
				if (string.IsNullOrEmpty(m.FolderPath) || !Directory.Exists(m.FolderPath)) continue;
				try
				{
					if (Directory.EnumerateFiles(m.FolderPath, file, SearchOption.AllDirectories).Any())
						return m.IsEnabled ? FomodFileState.Active : FomodFileState.Inactive;
				}
				catch (Exception ex) { DiagnosticLog.WriteException("FOMOD", $"looking for {file} in {m.FolderPath}", ex); }
			}
			return FomodFileState.Missing;
		};
	}

	// -------------------------------------------------------------------------
	// Helpers
	// -------------------------------------------------------------------------

	public static async Task InstallScriptExtenderAsync(string archivePath, string gamePath, string activeGame, IProgress<double>? installProgress = null)
	{
		string tempDir = Path.Combine(Path.GetTempPath(), "Extender_" + Path.GetRandomFileName());
		try
		{
			Directory.CreateDirectory(tempDir);
			await Task.Run(() => ModArchive.Extract(archivePath, tempDir, installProgress));
			ModArchive.GuardAgainstEscapedEntries(tempDir);

			string loaderExePattern = GameProfiles.IsGame(activeGame, GameProfiles.SkyrimSE) ? "skse64_loader.exe" : "f4se_loader.exe";
			string[] matches = Directory.GetFiles(tempDir, loaderExePattern, SearchOption.AllDirectories);
			if (matches.Length == 0)
			{
				throw new Exception($"Could not find {loaderExePattern} inside the downloaded archive.");
			}

			string sourceDir = Path.GetDirectoryName(matches[0]) ?? tempDir;
			await Task.Run(() => CopyDirectoryRecursively(sourceDir, gamePath));

			// Record exactly which files we placed in the game folder (relative paths, matching
			// CopyDirectoryRecursively) so the script extender can later be cleanly uninstalled.
			var relPaths = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories)
				.Select(f => f.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
				.ToList();
			SaveScriptExtenderManifest(activeGame, gamePath, relPaths);
		}
		finally
		{
			try { Directory.Delete(tempDir, true); }
			catch (Exception ex) { DiagnosticLog.WriteException("Install", $"clearing the temporary folder {tempDir}", ex); }
		}
	}

	/// <summary>Per-game record of the files a script-extender install placed in the game folder, so they can
	/// be removed exactly on uninstall (kept in the manager's AppData, not in the game folder).</summary>
	private class ScriptExtenderManifest
	{
		public string GamePath { get; set; } = "";
		public List<string> Files { get; set; } = new();
	}

	private static string ScriptExtenderManifestPath(string activeGame) =>
		Path.Combine(AppSettings.AppDataFolder, "script_extender", activeGame + ".json");

	/// <summary>The script extender's loader exe name for a game, or "" for games without one (Stardew).</summary>
	private static string ScriptExtenderLoaderName(string activeGame) => ScriptExtenderInfo.LoaderName(activeGame);

	/// <summary>
	/// True if the script extender is installed in the game folder.
	///
	/// Judged by <see cref="ScriptExtenderInfo.Read"/>, which accepts either the loader exe or the versioned
	/// runtime DLLs. Looking for the loader alone — which this used to do — reported SKSE as missing for anyone
	/// who starts it through the SSE Engine Fixes preloader rather than its own exe, which is a normal setup and
	/// the usual one on GOG.
	/// </summary>
	public static bool IsScriptExtenderInstalled(string activeGame, string gamePath) =>
		ScriptExtenderInfo.Read(activeGame, gamePath) != null;

	/// <summary>
	/// What script extender is installed in the game folder and whether it will load — see
	/// <see cref="ScriptExtenderInfo.Read"/>, which does the reading. Kept here so the rest of the app has one
	/// name for every script-extender question it asks about a game folder.
	/// </summary>
	public static ScriptExtenderStatus? ReadScriptExtenderStatus(string activeGame, string gamePath) =>
		ScriptExtenderInfo.Read(activeGame, gamePath);

	private static void SaveScriptExtenderManifest(string activeGame, string gamePath, List<string> files)
	{
		try
		{
			string path = ScriptExtenderManifestPath(activeGame);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			var manifest = new ScriptExtenderManifest { GamePath = gamePath, Files = files };
			AtomicFile.WriteAllText(path, JsonConvert.SerializeObject(manifest, Formatting.Indented));
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Script Extender", "recording which files were installed, for a later uninstall", ex); }
	}

	/// <summary>
	/// Removes a previously installed script extender (SKSE/F4SE) from the game folder. When an install manifest
	/// is present, removes exactly the files we recorded (and prunes any directories that become empty) so files
	/// belonging to other mods are never touched. Without a manifest (e.g. an install from before this was
	/// tracked), falls back to removing only the unambiguous root loader and versioned DLLs. Returns how many
	/// files were removed and whether a manifest was used.
	/// </summary>
	public static (int Removed, bool UsedManifest) UninstallScriptExtender(string activeGame, string gamePath, Action<string, string> logError)
	{
		string loader = ScriptExtenderLoaderName(activeGame);
		if (string.IsNullOrEmpty(loader) || string.IsNullOrEmpty(gamePath)) return (0, false);

		string manifestPath = ScriptExtenderManifestPath(activeGame);
		int removed = 0;

		if (File.Exists(manifestPath))
		{
			try
			{
				var manifest = JsonConvert.DeserializeObject<ScriptExtenderManifest>(File.ReadAllText(manifestPath));
				var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (string rel in manifest?.Files ?? new List<string>())
				{
					string full = Path.Combine(gamePath, rel);
					try
					{
						if (File.Exists(full)) { File.Delete(full); removed++; }
						string? dir = Path.GetDirectoryName(full);
						if (!string.IsNullOrEmpty(dir)) dirs.Add(dir);
					}
					catch (Exception ex) { logError("Script Extender", $"Could not remove {rel}: {ex.Message}"); }
				}
				// Prune directories we emptied, deepest first, but never the game root itself.
				foreach (string dir in dirs.OrderByDescending(d => d.Length))
					PruneEmptyDir(dir, gamePath);
				try { File.Delete(manifestPath); }
				catch (Exception ex) { DiagnosticLog.WriteException("Script Extender", $"deleting the install record {manifestPath}", ex); }
				return (removed, true);
			}
			catch (Exception ex) { logError("Script Extender", $"Could not read uninstall manifest: {ex.Message}"); }
		}

		// Fallback: no manifest — remove only the unambiguous root files (loader + versioned DLLs).
		string prefix = GameProfiles.IsGame(activeGame, GameProfiles.SkyrimSE) ? "skse64_" : "f4se_";
		try
		{
			foreach (string file in Directory.EnumerateFiles(gamePath, prefix + "*.dll", SearchOption.TopDirectoryOnly))
			{
				try { File.Delete(file); removed++; } catch (Exception ex) { logError("Script Extender", $"Could not remove {Path.GetFileName(file)}: {ex.Message}"); }
			}
			string loaderPath = Path.Combine(gamePath, loader);
			if (File.Exists(loaderPath)) { try { File.Delete(loaderPath); removed++; } catch (Exception ex) { logError("Script Extender", $"Could not remove {loader}: {ex.Message}"); } }
		}
		catch (Exception ex) { logError("Script Extender", $"Uninstall fallback failed: {ex.Message}"); }
		return (removed, false);
	}

	/// <summary>Deletes <paramref name="dir"/> if it is empty and sits below <paramref name="root"/> (never the
	/// root itself), then walks up doing the same so a chain of emptied folders is cleaned.</summary>
	private static void PruneEmptyDir(string dir, string root)
	{
		try
		{
			string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
			string cur = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
			while (cur.Length > rootFull.Length && cur.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) && Directory.Exists(cur))
			{
				if (Directory.EnumerateFileSystemEntries(cur).Any()) break;
				Directory.Delete(cur);
				cur = Path.GetDirectoryName(cur)?.TrimEnd(Path.DirectorySeparatorChar) ?? "";
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Deploy", $"removing emptied folders under {root}", ex); }
	}

	/// <summary>
	/// Installs the SSE Engine Fixes "Part 2" SKSE64 preloader by extracting <c>d3dx9_42.dll</c>
	/// from <paramref name="archivePath"/> directly into the game's root folder (where the preloader
	/// must live), so the user does not have to perform the manual root-folder step themselves.
	/// </summary>
	public static async Task InstallEnginePreloaderAsync(string archivePath, string gamePath)
	{
		string tempDir = Path.Combine(Path.GetTempPath(), "Preloader_" + Path.GetRandomFileName());
		try
		{
			Directory.CreateDirectory(tempDir);
			await Task.Run(() => ModArchive.Extract(archivePath, tempDir));
			ModArchive.GuardAgainstEscapedEntries(tempDir);

			string[] matches = Directory.GetFiles(tempDir, "d3dx9_42.dll", SearchOption.AllDirectories);
			if (matches.Length == 0)
			{
				throw new Exception("Could not find d3dx9_42.dll inside the Engine Fixes preloader archive.");
			}

			Directory.CreateDirectory(gamePath);
			File.Copy(matches[0], Path.Combine(gamePath, "d3dx9_42.dll"), overwrite: true);
		}
		finally
		{
			try { Directory.Delete(tempDir, true); }
			catch (Exception ex) { DiagnosticLog.WriteException("Install", $"clearing the temporary folder {tempDir}", ex); }
		}
	}

	private static void CopyDirectoryRecursively(string sourceDir, string targetDir)
	{
		Directory.CreateDirectory(targetDir);
		foreach (string file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
		{
			string relativePath = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar);
			string destFile = Path.Combine(targetDir, relativePath);
			string? destDir = Path.GetDirectoryName(destFile);
			if (destDir != null)
			{
				Directory.CreateDirectory(destDir);
			}
			RobustCopy(file, destFile);
		}
	}

	/// <summary>Clears the read-only attribute on a file/directory tree. A single read-only file (some mods ship
	/// their config or ESL files that way) makes <see cref="Directory.Delete(string, bool)"/> and an overwriting
	/// <see cref="File.Copy(string, string, bool)"/> fail with "access to the path is denied", so we strip it first.</summary>
	private static void ClearReadOnlyRecursive(string path)
	{
		try
		{
			var di = new DirectoryInfo(path);
			if (!di.Exists) return;
			if ((di.Attributes & FileAttributes.ReadOnly) != 0) di.Attributes &= ~FileAttributes.ReadOnly;
			foreach (FileInfo file in di.GetFiles("*", SearchOption.AllDirectories))
				if ((file.Attributes & FileAttributes.ReadOnly) != 0) file.Attributes &= ~FileAttributes.ReadOnly;
			foreach (DirectoryInfo dir in di.GetDirectories("*", SearchOption.AllDirectories))
				if ((dir.Attributes & FileAttributes.ReadOnly) != 0) dir.Attributes &= ~FileAttributes.ReadOnly;
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Files", $"clearing read-only flags under {path}", ex); }
	}

	/// <summary>Public entry point for deleting a mod's folder from the managed mods directory. Uses the same
	/// read-only-clearing, retrying delete as the installer, so a mod that ships a read-only file (e.g. SkyPatcher's
	/// DLL) no longer fails the user's Delete action with "Access to the path '…' is denied".</summary>
	public static void DeleteModFolder(string path) => ModOnDisk.Delete(path);

	/// <summary>Deletes a single file robustly: clears a read-only attribute first (otherwise the delete throws
	/// "access is denied" on mods that mark files read-only) and retries a few times for a transient lock.</summary>
	private static void RobustDeleteFile(string path) => ModOnDisk.DeleteFile(path);

	/// <summary>An overwriting file copy that first clears a read-only destination (otherwise the copy fails with
	/// "access is denied") and retries a few times for a transient lock.</summary>
	private static void RobustCopy(string source, string dest)
	{
		for (int attempt = 0; ; attempt++)
		{
			try
			{
				if (File.Exists(dest))
				{
					FileAttributes attrs = File.GetAttributes(dest);
					if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(dest, attrs & ~FileAttributes.ReadOnly);
				}
				File.Copy(source, dest, overwrite: true);
				return;
			}
			catch (Exception) when (attempt < 3) { Thread.Sleep(200); }
		}
	}
}
