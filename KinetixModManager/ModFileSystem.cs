using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Common;

namespace KinetixModManager;

/// <summary>
/// Static helpers for all mod-related file I/O: scanning mods, parsing manifests,
/// creating and pruning backups, extracting archives, and deploying/syncing files.
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

	// Manifest reading is case-insensitive and tolerant of the ways hand-written manifests differ; the rules
	// (and why they matter) live in ModManifest, which is unit tested.
	private static JToken? ManifestField(JObject manifest, string name) => ModManifest.Field(manifest, name);

	private static string? ManifestString(JObject manifest, string name) => ModManifest.String(manifest, name);

	/// <summary>The description the manager writes for a mod it found on disk with no metadata of its own.</summary>
	private const string LocalModDescription = "Installed local mod.";

	/// <summary>
	/// True for a manifest the manager wrote itself for a mod that came with no metadata — as opposed to one
	/// carrying an author's real details.
	///
	/// All three fields together, because any one alone is ordinary: plenty of real mods are at 1.0.0, and a
	/// real mod can have "Unknown" typed in its author field. Only the exact trio the old auto-generator wrote
	/// identifies its own output.
	/// </summary>
	private static bool IsPlaceholderManifest(JObject manifest) =>
		ManifestString(manifest, "Description") == LocalModDescription &&
		ManifestString(manifest, "Author")      == "Unknown" &&
		ManifestString(manifest, "Version")     == "1.0.0";

	/// <summary>
	/// How a mod found on disk with no metadata is named in the list. For The Witcher 3 that is the folder name
	/// made readable, since the folder name is genuinely all there is; every other game gets the mod id, version and
	/// upload timestamp that Nexus appends to a download taken off the end, because a mod installed before those
	/// were stripped still sits in a folder called "Skyrim Access-181131-1-2-3-1723456789" and should not be read
	/// out that way. Only what is shown changes — the folder keeps its name, and the mod keeps its identity.
	/// </summary>
	private static string LocalModDisplayName(string folderName, string activeGame)
	{
		if (GameProfiles.Find(activeGame)?.IsWitcher3 == true)
			return Witcher3Layout.DisplayNameFromFolder(folderName);

		string cleaned = ModDisplayName.Clean(folderName);
		return cleaned.Length > 0 ? cleaned : folderName;
	}

	/// <summary>
	/// Scans the mods directory for installed mods depending on the active game.
	/// </summary>
	public static List<GameMod> ScanMods(
		string modsPath,
		JObject nexusIdMap,
		AppSettings settings,
		string activeGame,
		Action<string, string> logError)
	{
		var mods = new List<GameMod>();
		if (!Directory.Exists(modsPath)) return mods;

		if (GameProfiles.IsGame(activeGame, GameProfiles.StardewValley))
		{
			foreach (string manifestPath in Directory.GetFiles(modsPath, "manifest.json", SearchOption.AllDirectories))
			{
				try
				{
					JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
					string uid = ManifestString(manifest, "UniqueID") ?? Guid.NewGuid().ToString();
					JToken? updateKeys = ManifestField(manifest, "UpdateKeys");

					var mod = new GameMod
					{
						Name        = ManifestString(manifest, "Name")        ?? "Unknown",
						Version     = ManifestString(manifest, "Version")     ?? "0",
						Author      = ManifestString(manifest, "Author")      ?? "User",
						UniqueId    = uid,
						Description = ManifestString(manifest, "Description") ?? "",
						NexusID     = ParseNexusId(updateKeys),
						GitHubRepo  = ParseGitHubRepo(updateKeys),
						FolderPath  = Path.GetDirectoryName(manifestPath) ?? "",
						IsEnabled   = !Path.GetFileName(Path.GetDirectoryName(manifestPath) ?? "").StartsWith(".")
					};
					// An UpdateKeys entry that is present but unusable — a blank string, or "Nexus: " with no
					// number after it — is an author's typo, not an absent key. Remember the difference so the
					// coverage report can say which it is instead of lumping both under "no link".
					mod.HasBlankUpdateKey = ModManifest.HasUnusableUpdateKey(updateKeys) &&
											string.IsNullOrEmpty(mod.NexusID) && string.IsNullOrEmpty(mod.GitHubRepo);

					if (nexusIdMap.TryGetValue(uid, out JToken? mappedId))
						mod.NexusID = mappedId?.ToString();

					mod.Category = settings.ModCategories.TryGetValue(uid, out string? cat) ? cat
						: DetectCategory(mod.Name, mod.Description);
					mod.Note = settings.ModNotes.TryGetValue(uid, out string? note) ? note : "";

					if (ManifestField(manifest, "Dependencies") is JArray deps)
					{
						foreach (JToken dep in deps)
						{
							if (dep is not JObject depObj) continue;
							mod.Dependencies.Add(new ModDependency
							{
								UniqueId       = ManifestString(depObj, "UniqueID")       ?? "Unknown",
								MinimumVersion = ManifestString(depObj, "MinimumVersion"),
								IsRequired     = ((bool?)ManifestField(depObj, "IsRequired")) ?? true
							});
						}
					}

					// A content pack (e.g. a Content Patcher pack) declares its host mod in ContentPackFor and
					// cannot load without it, so treat that host as a required dependency for the requirements check.
					if (ManifestField(manifest, "ContentPackFor") is JObject cpFor)
					{
						string? hostId = ManifestString(cpFor, "UniqueID");
						if (!string.IsNullOrEmpty(hostId) &&
							!mod.Dependencies.Exists(d => d.UniqueId.Equals(hostId, StringComparison.OrdinalIgnoreCase)))
						{
							mod.Dependencies.Add(new ModDependency
							{
								UniqueId       = hostId,
								MinimumVersion = ManifestString(cpFor, "MinimumVersion"),
								IsRequired     = true
							});
						}
					}

					mods.Add(mod);
				}
				catch (Exception ex)
				{
					logError(manifestPath, "Parse Error: " + ex.Message);
				}
			}
		}
		else if (GameProfiles.Find(activeGame)?.IsBepInEx == true)
		{
			mods.AddRange(ScanBepInExMods(modsPath, nexusIdMap, settings, logError));
		}
		else
		{
			// Skyrim / Fallout 4 (staged) and The Witcher 3 (in the game's own mods folder): a mod is a direct
			// subdirectory, switched off by a prefix on its name — a dot for the former, a tilde for the latter.
			GameProfile? scanProfile = GameProfiles.Find(activeGame);
			string disabledPrefix = scanProfile?.DisabledModPrefix ?? ".";

			// The Witcher 3 also keeps its own record of which mods are on, and that record is what its in-game
			// mod menu shows. A mod switched off there is off, however its folder is named.
			string witcherModsSettings = scanProfile?.IsWitcher3 == true
				? Witcher3ModSettings.PathFor(scanProfile.UserDataDirectoryFor(
					Path.GetDirectoryName(modsPath.TrimEnd(Path.DirectorySeparatorChar)) ?? ""))
				: "";
			var witcherDisabled = witcherModsSettings.Length > 0
				? Witcher3ModSettings.Read(witcherModsSettings)
					.Where(e => !e.Enabled)
					.Select(e => e.Name)
					.ToHashSet(StringComparer.OrdinalIgnoreCase)
				: new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (string dir in Directory.GetDirectories(modsPath))
			{
				string folderName = Path.GetFileName(dir);
				if (folderName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
					folderName.Equals("obj", StringComparison.OrdinalIgnoreCase))
					continue;

				bool folderEnabled = !folderName.StartsWith(disabledPrefix, StringComparison.Ordinal) &&
					!witcherDisabled.Contains(Witcher3ModSettings.BareName(folderName));

				string manifestPath = Path.Combine(dir, ".manager_manifest.json");
				GameMod mod;

				try
				{
					if (File.Exists(manifestPath))
					{
						JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
						string uid = ManifestString(manifest, "UniqueID") ?? folderName;
						string? nexusId = ManifestString(manifest, "NexusID");

						// Auto-extract NexusID from folderName if not present. The folder is usually named after the
						// download it came out of, so it still carries the mod id — and finding that is the same
						// job as reading one out of an archive name, so it goes through the same rules. The
						// pattern that used to live here required three digits and a hyphen on each side, which
						// missed a young game's mods entirely (Moonlight Peaks' are numbered 7, 11, 33) and every
						// older download that carries no upload timestamp.
						if (string.IsNullOrEmpty(nexusId) && !GameProfiles.IsGame(activeGame, GameProfiles.StardewValley))
						{
							nexusId = ModDisplayName.ModIdFromArchiveName(folderName);
							if (!string.IsNullOrEmpty(nexusId))
							{
								try
								{
									manifest["NexusID"] = nexusId;
									File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
								}
								catch (Exception ex) { DiagnosticLog.WriteException("ScanMods", $"recording the Nexus id in {manifestPath}", ex); }
							}
						}

						// An auto-generated manifest for a mod that came with no metadata used to record a made-up
						// "1.0.0" and an author of "Unknown". Those were written to disk, so they outlive the
						// code that invented them — recognise them and go back to not knowing, which is both
						// true and what stops a mod page at 1.0 looking older than what is installed.
						bool placeholder = IsPlaceholderManifest(manifest);

						mod = new GameMod
						{
							Name        = placeholder
								? LocalModDisplayName(folderName, activeGame)
								: ManifestString(manifest, "Name") ?? LocalModDisplayName(folderName, activeGame),
							Version     = placeholder ? "" : ManifestString(manifest, "Version") ?? "",
							Author      = placeholder ? "" : ManifestString(manifest, "Author")  ?? "",
							UniqueId    = uid,
							Description = ManifestString(manifest, "Description") ?? "",
							NexusID     = nexusId,
							GitHubRepo  = ManifestString(manifest, "GitHubRepo"),
							FolderPath  = dir,
							IsEnabled   = folderEnabled
						};
					}
					else
					{
						// Create automatic manifest
						string cleanName = folderName.StartsWith(disabledPrefix, StringComparison.Ordinal)
							? folderName.Substring(disabledPrefix.Length)
							: folderName;
						// Same rules again, and deliberately the same ones: a mod with no metadata of its own is
						// exactly the mod whose folder name is all there is to go on, so this is where reading it
						// correctly matters most. Getting the id here is what lets the mod be update-checked at
						// all.
						string? extractedNexusId = GameProfiles.IsGame(activeGame, GameProfiles.StardewValley)
							? null
							: ModDisplayName.ModIdFromArchiveName(folderName);

						// No version in the folder name means there is no version to state. Leaving it empty is
						// the honest answer and reads as "version unknown"; inventing 1.0.0 made every such mod
						// look older than any real release, with nothing afterwards to tell the two apart.
						string guessedVersion = ExtractVersionFromFileName(folderName, extractedNexusId) ?? "";

						mod = new GameMod
						{
							Name        = LocalModDisplayName(cleanName, activeGame),
							Version     = guessedVersion,
							Author      = "",
							UniqueId    = cleanName,
							Description = "Installed local mod.",
							FolderPath  = dir,
							IsEnabled   = folderEnabled,
							NexusID     = extractedNexusId
						};
						
						var newManifest = new JObject
						{
							["Name"] = mod.Name,
							["Version"] = mod.Version,
							["Author"] = mod.Author,
							["UniqueID"] = mod.UniqueId,
							["Description"] = mod.Description,
							["NexusID"] = mod.NexusID,
							["GitHubRepo"] = mod.GitHubRepo
						};
						File.WriteAllText(manifestPath, newManifest.ToString(Formatting.Indented));
					}

					if (nexusIdMap.TryGetValue(mod.UniqueId, out JToken? mappedId))
						mod.NexusID = mappedId?.ToString();

					mod.Category = settings.ModCategories.TryGetValue(mod.UniqueId, out string? cat) ? cat
						: DetectCategory(mod.Name, mod.Description);
					mod.Note = settings.ModNotes.TryGetValue(mod.UniqueId, out string? note) ? note : "";

					mods.Add(mod);
				}
				catch (Exception ex)
				{
					logError(manifestPath, "Parse Error: " + ex.Message);
				}
			}
		}

		// Duplicate collapsing exists for the Bethesda games, where the same mod is routinely re-downloaded into a
		// second "-1234-" folder and both copies then deploy their files. It deletes a mod folder, so it stays
		// strictly limited to those games: BepInEx mods install in place and are never duplicated this way.
		if (GameProfiles.Find(activeGame)?.IsBethesda == true && mods.Count > 1)
		{
			var duplicateGroups = mods
				.GroupBy(m => !string.IsNullOrEmpty(m.NexusID) ? ("id_" + m.NexusID) : ("name_" + m.Name.ToLowerInvariant()))
				.Where(g => g.Count() > 1)
				.ToList();

			foreach (var group in duplicateGroups)
			{
				GameMod bestMod = group.First();
				foreach (var m in group.Skip(1))
				{
					if (CompareVersionsNewer(bestMod.Version, m.Version))
					{
						bestMod = m;
					}
				}

				foreach (var m in group)
				{
					if (m != bestMod)
					{
						mods.Remove(m);
						if (Directory.Exists(m.FolderPath))
						{
							try
							{
								string gameData = Path.Combine(settings.CurrentGamePath, "Data");
								DeployModFiles(m.FolderPath, gameData, false, logError);
								SyncPluginsFile(m.FolderPath, activeGame, settings.CurrentGamePath, false, logError);

								string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
								string backupsPath = Path.Combine(appData, "AudiVentureGames", "KinetixModManager", "backups", activeGame);
								if (!Directory.Exists(backupsPath))
								{
									Directory.CreateDirectory(backupsPath);
								}
								CreateBackup(m.FolderPath, Path.GetFileName(m.FolderPath), backupsPath);
								Directory.Delete(m.FolderPath, true);
							}
							catch (Exception ex)
							{
								logError(m.Name, "Failed to remove duplicate: " + ex.Message);
							}
						}
					}
				}
			}
		}

		return mods;
	}

	// -------------------------------------------------------------------------
	// BepInEx games (Moonlight Peaks)
	// -------------------------------------------------------------------------

	/// <summary>
	/// The folder disabled BepInEx mods are parked in, beside <c>plugins</c>. BepInEx has no notion of a disabled
	/// plugin: its chainloader scans <c>plugins</c> recursively for DLLs and ignores folder names entirely, so the
	/// leading-dot convention that disables a Stardew or Skyrim mod would leave a BepInEx mod running. Moving the
	/// mod out of the scanned folder is what actually turns it off, and it keeps the mod whole so enabling is just
	/// the move back.
	/// </summary>
	public const string BepInExDisabledFolderName = ModEnableState.BepInExDisabledFolderName;

	/// <summary>The disabled-mods folder that sits beside the given <c>BepInEx\plugins</c> folder.</summary>
	public static string BepInExDisabledFolder(string pluginsPath)
	{
		string parent = Path.GetDirectoryName(pluginsPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "";
		return parent.Length == 0 ? "" : Path.Combine(parent, BepInExDisabledFolderName);
	}

	/// <summary>BepInEx's own log, which sits beside the plugins folder and records every plugin it loaded.</summary>
	public static string BepInExLogPath(string pluginsPath)
	{
		string parent = Path.GetDirectoryName(pluginsPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "";
		return parent.Length == 0 ? "" : Path.Combine(parent, "LogOutput.log");
	}

	/// <summary>
	/// Scans a BepInEx game's mods: every folder under <c>BepInEx\plugins</c> (enabled) and under
	/// <c>BepInEx\plugins-disabled</c> (disabled). Each folder's real name, version and GUID come from the
	/// <c>[BepInPlugin]</c> attribute in its DLLs, with BepInEx's log as a second source — see
	/// <see cref="BepInExPlugin"/> for why neither the file version nor the folder name can be trusted for this.
	/// </summary>
	private static List<GameMod> ScanBepInExMods(
		string pluginsPath, JObject nexusIdMap, AppSettings settings, Action<string, string> logError)
	{
		var mods = new List<GameMod>();

		// A mod shipped as a bare DLL gets a folder of its own first, so everything below is folder-based.
		AdoptLooseBepInExPlugins(pluginsPath, logError);

		string logPath = BepInExLogPath(pluginsPath);
		Dictionary<string, string> logged = BepInExPlugin.ParseLoadedPluginsFromLog(logPath);
		// When the log was written, so a log predating a mod's files is not believed about them. The log only
		// changes when the game runs, and mods are usually updated between sessions rather than during one.
		DateTime? logWrittenUtc = null;
		try { if (File.Exists(logPath)) logWrittenUtc = File.GetLastWriteTimeUtc(logPath); }
		catch (Exception ex) { DiagnosticLog.WriteException("Mods", $"reading the age of {logPath}", ex); }

		string disabledPath = BepInExDisabledFolder(pluginsPath);

		foreach ((string root, bool enabled) in new[] { (pluginsPath, true), (disabledPath, false) })
		{
			if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

			foreach (string dir in Directory.GetDirectories(root))
			{
				string folderName = Path.GetFileName(dir);
				string manifestPath = Path.Combine(dir, ".manager_manifest.json");

				try
				{
					BepInExPluginInfo info = BepInExPlugin.Identify(dir, logged, logWrittenUtc);

					JObject manifest = File.Exists(manifestPath)
						? JObject.Parse(File.ReadAllText(manifestPath))
						: new JObject();

					// A Nexus download unpacks as "Mod Name-1234-1-0-0.zip", so the mod id is recoverable from the
					// folder name when the manifest doesn't already carry it.
					string? nexusId = ManifestString(manifest, "NexusID");
					if (string.IsNullOrEmpty(nexusId))
						nexusId = ModDisplayName.ModIdFromArchiveName(folderName);

					// The plugin's own declaration wins over anything the manager guessed earlier and wrote to the
					// manifest, because it is the author's answer rather than an inference from a file name.
					// A folder named from a download still carries Nexus's mod id, version and timestamp; the plugin's
					// own name is preferred anyway, so this only matters for a plugin that declares none.
					string cleanedFolderName = ModDisplayName.Clean(folderName);
					if (cleanedFolderName.Length == 0) cleanedFolderName = folderName;
					string name = info.Name.Length > 0
						? info.Name
						: (ManifestString(manifest, "Name") ?? cleanedFolderName);
					string version = info.Version.Length > 0
						? info.Version
						: (ManifestString(manifest, "Version") ?? ExtractVersionFromFileName(folderName, nexusId) ?? "1.0.0");
					string uniqueId = info.Guid.Length > 0 ? info.Guid : (ManifestString(manifest, "UniqueID") ?? folderName);

					var mod = new GameMod
					{
						Name        = name,
						Version     = version,
						Author      = ManifestString(manifest, "Author") ?? "Unknown",
						UniqueId    = uniqueId,
						Description = ManifestString(manifest, "Description") ?? "Installed BepInEx plugin.",
						NexusID     = nexusId,
						GitHubRepo  = ManifestString(manifest, "GitHubRepo"),
						FolderPath  = dir,
						IsEnabled   = enabled
					};

					if (nexusIdMap.TryGetValue(mod.UniqueId, out JToken? mappedId))
						mod.NexusID = mappedId?.ToString();

					mod.Category = settings.ModCategories.TryGetValue(mod.UniqueId, out string? cat) ? cat
						: DetectCategory(mod.Name, mod.Description);
					mod.Note = settings.ModNotes.TryGetValue(mod.UniqueId, out string? note) ? note : "";

					WriteBepInExManifest(manifestPath, mod);

					mods.Add(mod);
				}
				catch (Exception ex)
				{
					logError(dir, "Scan Error: " + ex.Message);
				}
			}
		}

		return mods;
	}

	/// <summary>
	/// Records what the manager knows about a BepInEx mod so its Nexus link survives the next scan. Written only
	/// when something actually changed: the mod list is rebuilt often, and rewriting an identical file into every
	/// mod folder each time would churn the game folder and reset every mod's modification date for nothing.
	/// </summary>
	private static void WriteBepInExManifest(string manifestPath, GameMod mod)
	{
		try
		{
			var manifest = new JObject
			{
				["Name"]        = mod.Name,
				["Version"]     = mod.Version,
				["Author"]      = mod.Author,
				["UniqueID"]    = mod.UniqueId,
				["Description"] = mod.Description,
				["NexusID"]     = mod.NexusID,
				["GitHubRepo"]  = mod.GitHubRepo
			};
			string updated = manifest.ToString(Formatting.Indented);

			if (File.Exists(manifestPath) && File.ReadAllText(manifestPath) == updated) return;

			File.WriteAllText(manifestPath, updated);
		}
		catch (Exception ex)
		{
			// A read-only or locked mod folder is not a reason to fail the scan; the mod still lists correctly,
			// it just re-derives its metadata next time.
			DiagnosticLog.WriteException("Mods", $"saving the mod details to {manifestPath}", ex);
		}
	}

	/// <summary>
	/// Gives a mod shipped as a bare DLL — dropped straight into <c>plugins</c> rather than into a folder — a
	/// folder of its own named after the plugin. BepInEx loads it identically either way, but a mod with a folder
	/// can be listed, enabled, disabled, backed up and removed as one thing.
	///
	/// Only files that actually declare a <c>[BepInPlugin]</c> are moved. A loose DLL without one is a shared
	/// library that some other plugin loads from this folder, and moving it would break that plugin.
	/// </summary>
	private static void AdoptLooseBepInExPlugins(string pluginsPath, Action<string, string> logError)
	{
		try
		{
			if (!Directory.Exists(pluginsPath)) return;

			foreach (string dll in Directory.GetFiles(pluginsPath, "*.dll", SearchOption.TopDirectoryOnly))
			{
				BepInExPluginInfo? info = BepInExPlugin.ReadFromAssembly(dll);
				if (info == null) continue;

				string folderName = SanitiseFolderName(
					info.Name.Length > 0 ? info.Name : Path.GetFileNameWithoutExtension(dll));
				if (folderName.Length == 0) continue;

				string target = Path.Combine(pluginsPath, folderName);
				if (Directory.Exists(target)) continue; // a folder of that name already owns this mod

				Directory.CreateDirectory(target);
				File.Move(dll, Path.Combine(target, Path.GetFileName(dll)));
			}
		}
		catch (Exception ex)
		{
			logError(pluginsPath, "Could not tidy loose plugin DLLs: " + ex.Message);
		}
	}

	/// <summary>Strips the characters Windows forbids in a folder name, so a plugin name can become a folder.</summary>
	private static string SanitiseFolderName(string name)
	{
		var invalid = Path.GetInvalidFileNameChars();
		string cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
		return cleaned.TrimEnd('.');
	}

	/// <summary>
	/// Enables or disables an installed mod and returns its new folder path.
	///
	/// How a mod is switched off depends on the game. Stardew Valley and the Bethesda games use the long-standing
	/// leading-dot convention (SMAPI skips dot-prefixed folders, and the manager's deployment skips them too).
	/// The Witcher 3 loads only folders named <c>mod*</c>, so a leading tilde does the same job there. BepInEx
	/// ignores folder names entirely, so a Moonlight Peaks mod is moved between <c>BepInEx\plugins</c> and
	/// <c>BepInEx\plugins-disabled</c> instead — see <see cref="BepInExDisabledFolderName"/>.
	/// </summary>
	public static string SetModEnabled(string modFolderPath, bool enable, string activeGame,
		Action<string, string>? logError = null)
	{
		string target = ModEnableState.TargetPath(modFolderPath, enable, activeGame);

		// Even when the folder needs no renaming, The Witcher 3's own record of the mod may still disagree with
		// what the user just asked for, so that is brought into line either way.
		SyncWitcherModSettings(modFolderPath, enable, activeGame);

		// A Witcher 3 mod's working parts may live outside its folder — the accessibility mod's .asi sits beside
		// the game exe and is loaded by the game itself, so renaming the folder alone would leave it running.
		// Done before the rename, while the record of those files is still at this path.
		if (GameProfiles.Find(activeGame)?.IsWitcher3 == true)
		{
			string witcherModsFolder = Path.GetDirectoryName(modFolderPath.TrimEnd(Path.DirectorySeparatorChar)) ?? "";
			string witcherGameFolder = Path.GetDirectoryName(witcherModsFolder) ?? "";

			SetWitcher3ExtrasEnabled(
				modFolderPath,
				Path.GetFileName(modFolderPath.TrimEnd(Path.DirectorySeparatorChar)),
				enable,
				activeGame,
				witcherGameFolder,
				logError ?? ((_, _) => { }));
		}

		if (string.Equals(target, modFolderPath, StringComparison.OrdinalIgnoreCase)) return modFolderPath;

		// The disabled folder doesn't exist until the first mod is switched off.
		string? targetParent = Path.GetDirectoryName(target);
		if (!string.IsNullOrEmpty(targetParent)) Directory.CreateDirectory(targetParent);

		Directory.Move(modFolderPath, target);
		return target;
	}

	/// <summary>
	/// Mirrors a Witcher 3 mod's on/off state into the game's own <c>mods.settings</c>, so the in-game mod menu
	/// says the same thing the manager does. A no-op for every other game.
	/// </summary>
	private static void SyncWitcherModSettings(string modFolderPath, bool enable, string activeGame)
	{
		GameProfile? profile = GameProfiles.Find(activeGame);
		if (profile?.IsWitcher3 != true || string.IsNullOrEmpty(modFolderPath)) return;

		// <game>\mods\modFoo — so the game folder is two levels up.
		string modsFolder = Path.GetDirectoryName(modFolderPath.TrimEnd(Path.DirectorySeparatorChar)) ?? "";
		string gameFolder = Path.GetDirectoryName(modsFolder) ?? "";
		if (gameFolder.Length == 0) return;

		string settingsPath = Witcher3ModSettings.PathFor(profile.UserDataDirectoryFor(gameFolder));
		Witcher3ModSettings.SetEnabled(settingsPath, Path.GetFileName(modFolderPath.TrimEnd(Path.DirectorySeparatorChar)), enable);
	}

	/// <summary>
	/// Forgets a Witcher 3 mod in the game's own <c>mods.settings</c> once its folder has gone, so an uninstalled
	/// mod doesn't linger in the in-game menu. A no-op for every other game.
	/// </summary>
	public static void ForgetWitcherMod(string modFolderPath, string activeGame)
	{
		GameProfile? profile = GameProfiles.Find(activeGame);
		if (profile?.IsWitcher3 != true || string.IsNullOrEmpty(modFolderPath)) return;

		string modsFolder = Path.GetDirectoryName(modFolderPath.TrimEnd(Path.DirectorySeparatorChar)) ?? "";
		string gameFolder = Path.GetDirectoryName(modsFolder) ?? "";
		if (gameFolder.Length == 0) return;

		string settingsPath = Witcher3ModSettings.PathFor(profile.UserDataDirectoryFor(gameFolder));
		Witcher3ModSettings.Remove(settingsPath, Path.GetFileName(modFolderPath.TrimEnd(Path.DirectorySeparatorChar)));
	}

	/// <summary>
	/// Finds Skyrim/Fallout 4 plugins whose declared master files are not available among the supplied mods
	/// (or the base game/DLC masters) — the classic "missing master" that stops a plugin loading. Each result
	/// is the plugin, the mod that ships it, and the master that can't be found. Reads masters from the TES4
	/// header via <see cref="ReadPluginMasters"/>; only considers the given (typically enabled) mods.
	/// </summary>
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
				if (!IsPluginFile(leaf) || !activePlugins.Contains(leaf)) continue; // only active plugins load

				foreach (string master in ReadPluginMasters(file))
				{
					if (IsBaseMaster(activeGame, master) || activePlugins.Contains(master)) continue;
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

	/// <summary>
	/// Creates a timestamped <c>.zip</c> backup of a mod folder.
	/// </summary>
	/// <summary>
	/// Zips a mod folder into the backups folder. Supply <paramref name="progress"/> (0–100) to be told how far
	/// along it is — a large mod can take long enough that silence looks like the manager has hung.
	/// </summary>
	public static void CreateBackup(string folderPath, string modName, string backupsPath,
		IProgress<double>? progress = null)
	{
		if (!Directory.Exists(folderPath)) return;
		Directory.CreateDirectory(backupsPath);
		string dest = Path.Combine(backupsPath, $"{modName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

		if (progress == null)
		{
			ZipFile.CreateFromDirectory(folderPath, dest);
			return;
		}

		// Written entry by entry so progress can be reported, and measured in BYTES rather than files: mods are
		// routinely one large archive beside a handful of small files, and counting files would race to 90% and
		// then sit there for the entire wait — the opposite of reassuring.
		string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
		long total = 0;
		foreach (string file in files)
		{
			try { total += new FileInfo(file).Length; }
			catch (Exception ex) { DiagnosticLog.WriteException("Backup", $"measuring {file}", ex); }
		}
		if (total <= 0) total = 1;

		string root = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		long done = 0;

		using (var zip = ZipFile.Open(dest, ZipArchiveMode.Create))
		{
			foreach (string file in files)
			{
				string relative = Path.GetFullPath(file).Substring(root.Length);
				zip.CreateEntryFromFile(file, relative);
				try { done += new FileInfo(file).Length; }
				catch (Exception ex) { DiagnosticLog.WriteException("Backup", $"measuring {file}", ex); }
				progress.Report(Math.Min(100.0, done * 100.0 / total));
			}

			// ZipFile.CreateFromDirectory records empty folders; keep doing so, or restoring a backup would
			// quietly drop a folder a mod expects to exist.
			foreach (string dir in Directory.GetDirectories(folderPath, "*", SearchOption.AllDirectories))
			{
				if (Directory.EnumerateFileSystemEntries(dir).Any()) continue;
				string relative = Path.GetFullPath(dir).Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
				zip.CreateEntry(relative + "/");
			}
		}

		progress.Report(100.0);
	}

	/// <summary>
	/// Deletes oldest backups.
	/// </summary>
	public static void PruneBackups(string modName, string backupsPath, int maxCount)
	{
		if (!Directory.Exists(backupsPath)) return;
		var files = Directory.GetFiles(backupsPath, modName + "_*.zip")
			.Select(p => new FileInfo(p))
			.OrderByDescending(f => f.CreationTime)
			.ToList();

		for (int i = maxCount; i < files.Count; i++)
		{
			try { files[i].Delete(); }
			catch (Exception ex) { DiagnosticLog.WriteException("Backup", $"deleting the old backup {files[i].FullName}", ex); }
		}
	}

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
				File.WriteAllLines(pluginsFilePath, lines);
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
	/// Full path to the custom INI that holds the archive-invalidation [Archive] block, or <c>null</c> for any game
	/// where the toggle doesn't apply (everything except Fallout 4). Uses Fallout4Custom.ini — the file the engine
	/// merges over its generated Fallout4.ini — so the toggle is non-destructive and never edits a game-owned INI.
	/// </summary>
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
		string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
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
			File.WriteAllLines(path, lines);
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

		// 1. Remove files we previously deployed that are no longer wanted by any enabled mod.
		foreach (var kv in manifest.Deployed)
		{
			if (desiredSource.ContainsKey(kv.Key)) continue;
			string destAbs = Path.Combine(gameRootPath, kv.Key);
			try
			{
				RobustDeleteFile(destAbs);
				CleanEmptyParents(Path.GetDirectoryName(destAbs), gameRootPath);
			}
			catch (Exception ex) { logError(destAbs, $"Undeploy failed: {ex.Message}"); }
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
				RobustDeleteFile(destAbs);
				if (!TryCreateHardLink(destAbs, sourceFile)) RobustCopy(sourceFile, destAbs);
			}
			catch (Exception ex) { logError(sourceFile, $"Deploy failed: {ex.Message}"); }
		}

		// 3. Record the new state and surface conflicts.
		manifest.Deployed = desiredOwner;
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
	/// never removed. Empty directories left behind are tidied up. Returns the number of files actually removed.
	/// </summary>
	public static int PurgeDeployment(string gameRootPath, DeploymentManifest manifest, Action<string, string> logError)
	{
		if (string.IsNullOrEmpty(gameRootPath) || manifest.Deployed.Count == 0) return 0;
		gameRootPath = Path.GetFullPath(gameRootPath).TrimEnd(Path.DirectorySeparatorChar);

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
				CleanEmptyParents(Path.GetDirectoryName(destAbs), gameRootPath);
			}
			catch (Exception ex) { logError(destAbs, $"Purge failed: {ex.Message}"); }
		}
		// The game folder now holds none of our deployed files, so the manifest must be emptied — otherwise the
		// next sync would think these paths are still deployed and skip re-linking them.
		manifest.Deployed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

	/// <summary>File extensions of Bethesda plugins that participate in load order.</summary>
	public static bool IsPluginFile(string fileName)
	{
		string ext = Path.GetExtension(fileName).ToLowerInvariant();
		return ext == ".esp" || ext == ".esm" || ext == ".esl";
	}

	/// <summary>
	/// Base-game and official DLC master files that the engine always loads first on its own. They are
	/// kept implicit: never shown in the Plugin Order list and never written to plugins.txt.
	/// </summary>
	private static readonly HashSet<string> SkyrimBaseMasters = new(StringComparer.OrdinalIgnoreCase)
	{
		"Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm"
	};

	private static readonly HashSet<string> Fallout4BaseMasters = new(StringComparer.OrdinalIgnoreCase)
	{
		"Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm", "DLCCoast.esm",
		"DLCworkshop02.esm", "DLCworkshop03.esm", "DLCNukaWorld.esm", "DLCUltraHighResolution.esm"
	};

	/// <summary>True when <paramref name="fileName"/> is an implicit base-game/DLC master for the game.</summary>
	public static bool IsBaseMaster(string activeGame, string fileName) => GameProfiles.BaseId(activeGame) switch
	{
		"SkyrimSE" => SkyrimBaseMasters.Contains(fileName),
		"Fallout4" => Fallout4BaseMasters.Contains(fileName),
		_ => false
	};

	/// <summary>
	/// The implicit base-game/DLC master file names for the game. These never appear in the Plugin Order list but
	/// still occupy regular plugin slots, so the plugin-limit check counts the ones actually present on disk.
	/// </summary>
	public static IReadOnlyCollection<string> BaseMasters(string activeGame) => GameProfiles.BaseId(activeGame) switch
	{
		"SkyrimSE" => SkyrimBaseMasters,
		"Fallout4" => Fallout4BaseMasters,
		_ => Array.Empty<string>()
	};

	/// <summary>
	/// Reads a plugin's master/light status from its TES4 record header flags, falling back to the file
	/// extension when the header cannot be read. The engine loads master-flagged and light (ESL) plugins
	/// before regular plugins, so this — not the extension alone — decides the masters-first grouping
	/// (an ESL-flagged <c>.esp</c> loads with the masters even though its extension says otherwise).
	/// </summary>
	public static (bool IsMaster, bool IsLight) ReadPluginFlags(string filePath)
	{
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		bool extMaster = ext == ".esm" || ext == ".esl";
		bool extLight = ext == ".esl";
		try
		{
			using FileStream fs = File.OpenRead(filePath);
			byte[] buf = new byte[12];
			if (fs.Read(buf, 0, 12) == 12 && buf[0] == (byte)'T' && buf[1] == (byte)'E' && buf[2] == (byte)'S' && buf[3] == (byte)'4')
			{
				uint flags = BitConverter.ToUInt32(buf, 8);
				bool master = (flags & 0x1u) != 0 || extMaster;     // 0x1 = ESM (master)
				bool light  = (flags & 0x200u) != 0 || extLight;    // 0x200 = light (ESL / ESPFE)
				return (master, light);
			}
		}
		// An unreadable header falls back to the extension classification below, but a plugin whose header
		// cannot be read is worth knowing about: it is how a corrupt download presents.
		catch (Exception ex) { DiagnosticLog.WriteException("Plugins", $"reading the header of {filePath}", ex); }
		return (extMaster, extLight);
	}

	/// <summary>
	/// Reads the master files a plugin depends on, from the MAST subrecords in its TES4 header. These are
	/// the plugins that must load before this one, and drive the dependency-aware auto-sort. Returns an
	/// empty list if the header cannot be parsed.
	/// </summary>
	public static List<string> ReadPluginMasters(string filePath)
	{
		var masters = new List<string>();
		try
		{
			using FileStream fs = File.OpenRead(filePath);
			using var br = new System.IO.BinaryReader(fs);

			byte[] sig = br.ReadBytes(4);
			if (sig.Length < 4 || sig[0] != (byte)'T' || sig[1] != (byte)'E' || sig[2] != (byte)'S' || sig[3] != (byte)'4')
				return masters;

			uint dataSize = br.ReadUInt32();
			br.ReadUInt32(); // flags
			br.ReadUInt32(); // form id
			br.ReadUInt32(); // version control info
			br.ReadUInt16(); // internal version
			br.ReadUInt16(); // unknown
			// The remaining record data is a series of fields: type[4] + size[2] + data[size].
			byte[] data = br.ReadBytes((int)Math.Min(dataSize, (uint)int.MaxValue));

			int pos = 0;
			while (pos + 6 <= data.Length)
			{
				string type = System.Text.Encoding.ASCII.GetString(data, pos, 4);
				ushort size = BitConverter.ToUInt16(data, pos + 4);
				pos += 6;
				if (pos + size > data.Length) break;
				if (type == "MAST")
				{
					int strLen = size;
					while (strLen > 0 && data[pos + strLen - 1] == 0) strLen--; // trim trailing null(s)
					if (strLen > 0)
					{
						string name = System.Text.Encoding.Latin1.GetString(data, pos, strLen);
						if (!string.IsNullOrWhiteSpace(name)) masters.Add(name);
					}
				}
				pos += size;
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Plugins", $"reading the masters of {filePath}", ex); }
		return masters;
	}

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
			File.WriteAllLines(path, orderedActivePlugins.Select(p => "*" + p));
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
		string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
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
	/// Returns a writable temp-extraction base directory on the same volume as <paramref name="modsPath"/>,
	/// so extraction/staging don't consume space on the system drive. Falls back to the system temp folder
	/// when the mods drive can't be resolved or isn't writable.
	/// </summary>
	private static string GetExtractionTempRoot(string modsPath)
	{
		try
		{
			if (!string.IsNullOrEmpty(modsPath))
			{
				string? root = Path.GetPathRoot(Path.GetFullPath(modsPath));
				if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
				{
					string candidate = Path.Combine(root, "KinetixModManager.tmp");
					Directory.CreateDirectory(candidate);
					return candidate;
				}
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Install", "choosing a temporary folder on the mods drive", ex); }
		return Path.GetTempPath();
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
		bool matchExistingByNexusId = true)
	{
		// Two names, deliberately. The archive's own name is the one that identifies the download — the mod id and
		// version inside it are what match a reinstall to what is already there — while the folder the mod ends up
		// in is named after the mod, without the id, version and upload timestamp Nexus appends to every file.
		// Those used to be the same string, which is why installed mods sat in folders called
		// "Skyrim Access-181131-1-2-3-1723456789" and were announced by that name too.
		string archiveName = Path.GetFileNameWithoutExtension(zipPath);
		string installFolderName = SanitiseFolderName(ModDisplayName.Clean(archiveName, nexusId));
		if (installFolderName.Length == 0) installFolderName = SanitiseFolderName(archiveName);

		// The id Nexus buries in a file name is the only identity a hand-picked archive has, so it is read out of
		// the raw name before that name is cleaned away — otherwise a manual re-install of a mod already installed
		// would no longer recognise it and would leave two copies deploying the same files.
		if (string.IsNullOrEmpty(nexusId))
			nexusId = ModDisplayName.ModIdFromArchiveName(archiveName);

		// For Skyrim/Fallout 4 the mod's identity is known up front (its Nexus id or folder name), so a
		// reinstall can be confirmed before we even extract. Stardew's identity lives in manifest.json inside
		// the archive, so that prompt happens after extraction (below). A declined prompt cancels the install.
		if (confirmOverwrite != null && !GameProfiles.IsGame(activeGame, GameProfiles.StardewValley))
		{
			GameMod? existing = FindExistingInstall(installedMods, nexusId, archiveName, matchExistingByNexusId);
			if (existing != null && Directory.Exists(existing.FolderPath) && !confirmOverwrite(existing.Name, existing.Version))
				throw new OperationCanceledException("User declined to overwrite an existing mod.");
		}

		// Extract on the same volume as the destination mods folder so a full system drive (C:) never
		// blocks an install whose mods live elsewhere (e.g. D:). The staging copy and the final move stay
		// on one drive too, which keeps the move cheap. Falls back to the system temp folder if the mods
		// drive can't be resolved or written to.
		string tempDir = Path.Combine(GetExtractionTempRoot(modsPath), "Extract_" + Path.GetRandomFileName());
		try
		{
			await Task.Run(async () =>
			{
				Directory.CreateDirectory(tempDir);
				// Route by the archive's real signature, not its file name: a 7z/rar mod can arrive named ".zip"
				// (see DetectArchiveFormat), which would otherwise crash the zip extractor. Only when the bytes are
				// unrecognised do we trust the extension.
				ArchiveFormat fmt = DetectArchiveFormat(zipPath);
				if (fmt == ArchiveFormat.Unknown)
				{
					string ext = Path.GetExtension(zipPath).ToLower();
					fmt = ext == ".7z" ? ArchiveFormat.SevenZip : ext == ".rar" ? ArchiveFormat.Rar : ArchiveFormat.Zip;
				}
				if (fmt == ArchiveFormat.SevenZip)
				{
					string dataBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudiVentureGames", "KinetixModManager");
					string exePath = await Ensure7ZipCommandLineTool(dataBasePath, nexusService);
					Run7ZipExtract(exePath, zipPath, tempDir, installProgress);
				}
				else if (fmt == ArchiveFormat.Rar)
				{
					// The bundled 7za and .NET's ZipFile cannot read RAR, so use SharpCompress (managed).
					ExtractWithSharpCompress(zipPath, tempDir, installProgress);
				}
				else
				{
					ExtractZipWithProgress(zipPath, tempDir, installProgress);
				}
			});

			string canonicalTemp = Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			foreach (string entry in Directory.GetFileSystemEntries(tempDir, "*", SearchOption.AllDirectories))
			{
				if (!Path.GetFullPath(entry).StartsWith(canonicalTemp, StringComparison.OrdinalIgnoreCase))
					throw new InvalidOperationException($"Unsafe archive: entry escapes the extraction directory ({entry}).");
			}

			if (GameProfiles.Find(activeGame)?.IsBepInEx == true)
			{
				return await FinalizeBepInExModAsync(
					tempDir, installFolderName, zipPath, modsPath, installedMods,
					backupsPath, maxBackups, logError, nexusId, nexusService, gitHubRepo);
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
					backupsPath, maxBackups, activeGame, logError, nexusId, nexusService, gitHubRepo, currentGamePath);
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
						docsSourceRoot: tempDir, matchExistingByNexusId: matchExistingByNexusId);
				}

				return await FinalizeBethesdaModAsync(
					ResolveBethesdaModSource(tempDir), installFolderName, zipPath, modsPath, installedMods,
					backupsPath, maxBackups, activeGame, logError, nexusId, nexusService, gitHubRepo, currentGamePath, null,
					docsSourceRoot: tempDir, matchExistingByNexusId: matchExistingByNexusId);
			}

			// Stardew Valley Manifest logic
			string[] manifests = Directory.GetFiles(tempDir, "manifest.json", SearchOption.AllDirectories);
			if (manifests.Length == 0)
			{
				// Some downloads wrap the mod itself in a second archive — a "pick the variant you want" pack, or
				// a zip that simply contains the real zip. Unpack one level of nested archives and look again
				// before declaring the download unusable.
				await ExtractNestedArchivesAsync(tempDir, nexusService);
				manifests = Directory.GetFiles(tempDir, "manifest.json", SearchOption.AllDirectories);
			}
			if (manifests.Length == 0)
				throw new ModArchiveContentException(DescribeMissingManifest(zipPath, tempDir));

			bool overwriteConfirmed = confirmOverwrite == null; // no callback => proceed without prompting
			foreach (string mPath in manifests)
			{
				try
				{
					JObject manifest = JObject.Parse(File.ReadAllText(mPath));
					string uid   = ((string?)manifest["UniqueID"]) ?? "";
					string mName = ((string?)manifest["Name"])     ?? "Unknown";
					var existing = installedMods.FirstOrDefault(m => m.UniqueId == uid);
					if (existing != null && Directory.Exists(existing.FolderPath))
					{
						// Confirm once before touching anything the user already has on disk.
						if (!overwriteConfirmed)
						{
							if (!confirmOverwrite!(existing.Name, existing.Version))
								throw new OperationCanceledException("User declined to overwrite an existing mod.");
							overwriteConfirmed = true;
						}
						CreateBackup(existing.FolderPath, mName, backupsPath);
						PruneBackups(mName, backupsPath, maxBackups);
						ForceDeleteDirectory(existing.FolderPath);
					}
				}
				catch (OperationCanceledException) { throw; } // a declined overwrite must cancel, not be swallowed
				catch (Exception ex) { logError(mPath, "Pre-install backup error: " + ex.Message); }
			}

			bool isGroup = manifests.Length > 1;
			string sourceFolderStardew, targetFolderNameStardew;

			if (isGroup)
			{
				string commonPath = Path.GetDirectoryName(manifests[0]) ?? tempDir;
				foreach (string m in manifests)
				{
					string dir = Path.GetDirectoryName(m) ?? tempDir;
					while (!dir.StartsWith(commonPath))
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
					targetFolderNameStardew = mName.Replace(" ", "");
			}

			string destModFolderStardew = Path.Combine(modsPath, targetFolderNameStardew);
			if (Directory.Exists(destModFolderStardew))
				Directory.Delete(destModFolderStardew, recursive: true);

			Directory.CreateDirectory(destModFolderStardew);
			foreach (string dir in Directory.GetDirectories(sourceFolderStardew, "*", SearchOption.AllDirectories))
				Directory.CreateDirectory(dir.Replace(sourceFolderStardew, destModFolderStardew));
			foreach (string file in Directory.GetFiles(sourceFolderStardew, "*.*", SearchOption.AllDirectories))
				File.Copy(file, file.Replace(sourceFolderStardew, destModFolderStardew), overwrite: true);

			return (isGroup ? "Mod Group " : "") + targetFolderNameStardew;
		}
		finally
		{
			try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
			catch (Exception ex) { DiagnosticLog.WriteException("Install", $"clearing the temporary folder {tempDir}", ex); }
		}
	}

	/// <summary>
	/// Finalises a prepared Bethesda mod folder: backs up and removes any prior version, copies the
	/// prepared <paramref name="sourceFolder"/> into the mods directory, and writes the manager manifest.
	/// Shared by the normal flat-copy install and the FOMOD pipeline. When <paramref name="fomodInfo"/>
	/// is supplied its Name/Author/Version seed the manifest (a Nexus lookup still overrides when available).
	/// </summary>
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

	/// <summary>
	/// Finds the already-installed mod that a new archive would replace, matched first by Nexus id (extracted
	/// from the file name when not supplied, e.g. "SkyUI-12604-5-2"), then by folder/UniqueID name. Used both to
	/// prompt before overwriting and to back up/remove the old copy. Returns null when nothing matches.
	/// </summary>
	/// <param name="matchByNexusId">
	/// Whether an installed mod carrying the same Nexus id counts as the one being replaced. True for an ordinary
	/// mod, where the id is its identity and the folder name changes with every version.
	///
	/// It must be FALSE for a mod that ships as several separate downloads from one page, because then the id
	/// identifies the page rather than the install. SSE Engine Fixes is the case that proved it: its plugin and
	/// its preloader are both mod 17230, so installing the plugin found the preloader as "the existing copy",
	/// backed it up and deleted it — leaving a game that refuses to start, since the plugin it had just
	/// installed cannot load without the preloader it had just removed.
	/// </param>
	private static GameMod? FindExistingInstall(
		List<GameMod> installedMods, string? nexusId, string targetFolderName, bool matchByNexusId = true)
	{
		if (string.IsNullOrEmpty(nexusId))
			nexusId = ModDisplayName.ModIdFromArchiveName(targetFolderName);

		GameMod? existing = null;
		if (matchByNexusId && !string.IsNullOrEmpty(nexusId))
			existing = installedMods.FirstOrDefault(x => x.NexusID == nexusId);
		existing ??= installedMods.FirstOrDefault(x =>
			x.Name.Equals(targetFolderName, StringComparison.OrdinalIgnoreCase) ||
			x.UniqueId.Equals(targetFolderName, StringComparison.OrdinalIgnoreCase));
		return existing;
	}

	private static async Task<string> FinalizeBethesdaModAsync(
		string sourceFolder, string targetFolderName, string zipPath, string modsPath, List<GameMod> installedMods,
		string backupsPath, int maxBackups, string activeGame, Action<string, string> logError,
		string? nexusId, NexusService? nexusService, string? gitHubRepo, string? currentGamePath, FomodInfo? fomodInfo,
		string? docsSourceRoot = null, bool matchExistingByNexusId = true)
	{
		string destModFolder = Path.Combine(modsPath, targetFolderName);

		// Backup and remove old version (the reinstall prompt, if any, already happened in ExtractModAsync).
		GameMod? existing = FindExistingInstall(installedMods, nexusId, targetFolderName, matchExistingByNexusId);

		if (existing != null && Directory.Exists(existing.FolderPath))
		{
			// Remove the old version's plugin entries; its deployed asset files are reconciled by the
			// caller's SyncDeployment pass after extraction, so no per-file asset undeploy is needed here.
			if (!string.IsNullOrEmpty(currentGamePath))
				SyncPluginsFile(existing.FolderPath, activeGame, currentGamePath, false, logError);

			CreateBackup(existing.FolderPath, targetFolderName, backupsPath);
			PruneBackups(targetFolderName, backupsPath, maxBackups);
			ForceDeleteDirectory(existing.FolderPath);
		}

		if (Directory.Exists(destModFolder))
			ForceDeleteDirectory(destModFolder);

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
				Directory.CreateDirectory(dir.Replace(sourceFolder, rootDest));
			foreach (string file in Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories))
				RobustCopy(file, file.Replace(sourceFolder, rootDest));
		}
		else
		{
			foreach (string dir in Directory.GetDirectories(sourceFolder, "*", SearchOption.AllDirectories))
				Directory.CreateDirectory(dir.Replace(sourceFolder, destModFolder));
			foreach (string file in Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories))
				RobustCopy(file, file.Replace(sourceFolder, destModFolder));
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
		string? versionFromArchive = ExtractVersionFromFileName(zipPath, nexusId);
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
		File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));

		return targetFolderName;
	}

	/// <summary>The name the manifest records a Witcher 3 mod's out-of-folder files under.</summary>
	private const string WitcherExtraPathsKey = "ExtraPaths";

	/// <summary>Where a disabled Witcher 3 mod's out-of-folder files are parked, inside the game folder.</summary>
	public const string WitcherDisabledExtrasFolderName = "_KinetixDisabledMods";

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
		string? nexusId, NexusService? nexusService, string? gitHubRepo, string? currentGamePath)
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

		foreach (string source in modFolders)
		{
			string folderName = source == wrapped
				? WitcherModFolderName(archiveName)
				: Path.GetFileName(source);

			string dest = Path.Combine(modsPath, folderName);

			// Replacing an existing copy: keep a backup first, exactly as the other games' installs do.
			GameMod? existing = FindExistingInstall(installedMods, nexusId, folderName);
			if (existing != null && Directory.Exists(existing.FolderPath))
			{
				CreateBackup(existing.FolderPath, folderName, backupsPath);
				PruneBackups(folderName, backupsPath, maxBackups);
				ForceDeleteDirectory(existing.FolderPath);
			}

			// A disabled copy sits under the tilde name and would otherwise survive the reinstall, leaving the
			// mod installed twice under two names.
			string disabledTwin = Path.Combine(modsPath, "~" + folderName);
			if (Directory.Exists(disabledTwin)) ForceDeleteDirectory(disabledTwin);
			if (Directory.Exists(dest)) ForceDeleteDirectory(dest);

			CopyDirectoryRecursively(source, dest);

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
						RobustCopy(file, targetFile);
						extraPaths.Add(Path.Combine(extraName, relative));
					}
				}
			}
		}

		string destModFolder = Path.Combine(modsPath, primaryFolderName);

		// The mod's own documentation, so the F3 viewer and the controls list have something to read.
		CaptureModDocs(tempDir, destModFolder);

		string mName = primaryFolderName;
		string mAuthor = "Unknown";
		string mDesc = "Installed local mod.";

		string? versionFromArchive = ExtractVersionFromFileName(zipPath, nexusId);
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
		File.WriteAllText(Path.Combine(destModFolder, ".manager_manifest.json"), manifest.ToString(Formatting.Indented));

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

			var footprint = added.Where(rel => !IsOwn(rel)).ToList();
			var edited = changed.Where(rel => !IsOwn(rel)).ToList();

			var manifest = new JObject
			{
				["Name"] = modFolderName,
				["Version"] = ExtractVersionFromFileName(zipPath, nexusId) ?? "1.0.0",
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

			File.WriteAllText(
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
		string cleaned = SanitiseFolderName(ModDisplayName.Clean(archiveName));

		if (cleaned.Length == 0) cleaned = "Mod";
		cleaned = cleaned.Replace(" ", "");

		return cleaned.StartsWith("mod", StringComparison.OrdinalIgnoreCase) ? cleaned : "mod" + cleaned;
	}

	/// <summary>
	/// Deletes the files a Witcher 3 mod put outside its own folder — the <c>dlc</c> and <c>bin</c> parts recorded
	/// when it was installed. Called before the mod folder itself goes, since that is where the record lives.
	/// </summary>
	public static void RemoveWitcher3Extras(string modFolderPath, string activeGame, string gameFolder, Action<string, string> logError)
	{
		if (GameProfiles.Find(activeGame)?.IsWitcher3 != true) return;
		if (string.IsNullOrEmpty(gameFolder) || string.IsNullOrEmpty(modFolderPath)) return;

		try
		{
			// A mod that is currently disabled has these files parked outside the game; delete that copy too, or
			// uninstalling would leave them behind where the user can't see them.
			string parked = Path.Combine(
				gameFolder, WitcherDisabledExtrasFolderName,
				Witcher3ModSettings.BareName(Path.GetFileName(modFolderPath.TrimEnd(Path.DirectorySeparatorChar))));
			if (Directory.Exists(parked)) ForceDeleteDirectory(parked);
		}
		catch (Exception ex)
		{
			logError(modFolderPath, "Could not remove the mod's parked files: " + ex.Message);
		}

		string manifestPath = Path.Combine(modFolderPath, ".manager_manifest.json");
		if (!File.Exists(manifestPath)) return;

		try
		{
			JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
			if (manifest[WitcherExtraPathsKey] is not JArray extras) return;

			foreach (JToken entry in extras)
			{
				string? relative = entry?.ToString();
				if (string.IsNullOrEmpty(relative)) continue;

				string full = Path.GetFullPath(Path.Combine(gameFolder, relative));

				// Never step outside the game folder, whatever the manifest says.
				if (!full.StartsWith(Path.GetFullPath(gameFolder), StringComparison.OrdinalIgnoreCase)) continue;
				if (!File.Exists(full)) continue;

				File.SetAttributes(full, FileAttributes.Normal);
				File.Delete(full);

				// Take the folder too once the mod's last file has left it, but never a folder of the game's own.
				string? dir = Path.GetDirectoryName(full);
				if (dir != null && Directory.Exists(dir) &&
					!Directory.EnumerateFileSystemEntries(dir).Any() &&
					!IsWitcherGameOwnedFolder(dir, gameFolder))
					Directory.Delete(dir);
			}
		}
		catch (Exception ex)
		{
			logError(modFolderPath, "Could not remove the mod's files outside its folder: " + ex.Message);
		}
	}

	/// <summary>
	/// Moves a Witcher 3 mod's out-of-folder files out of the game (when disabling) or back into it (when
	/// enabling), and reports how many moved.
	///
	/// Renaming the mod folder is enough for a mod that is only a mod folder. It is not enough for one whose
	/// working parts live elsewhere: the accessibility mod's <c>.asi</c> sits beside the game's executable and is
	/// loaded by the game itself, so a "disabled" mod whose <c>.asi</c> is still there goes on running — which is
	/// the same trap BepInEx mods posed, where a renamed folder kept loading. Those files are parked in a folder
	/// inside the game install and put back, byte for byte, on enabling.
	/// </summary>
	public static int SetWitcher3ExtrasEnabled(
		string modFolderPath, string modFolderName, bool enable, string activeGame, string gameFolder,
		Action<string, string> logError)
	{
		if (GameProfiles.Find(activeGame)?.IsWitcher3 != true) return 0;
		if (string.IsNullOrEmpty(gameFolder) || string.IsNullOrEmpty(modFolderPath)) return 0;

		List<string> extras = ReadWitcherExtraPaths(modFolderPath);
		if (extras.Count == 0) return 0;

		string parked = Path.Combine(gameFolder, WitcherDisabledExtrasFolderName, Witcher3ModSettings.BareName(modFolderName));
		int moved = 0;

		foreach (string relative in extras)
		{
			try
			{
				string inGame = Path.GetFullPath(Path.Combine(gameFolder, relative));
				string outOfGame = Path.GetFullPath(Path.Combine(parked, relative));

				// Never step outside the game folder, whatever the manifest says.
				if (!inGame.StartsWith(Path.GetFullPath(gameFolder), StringComparison.OrdinalIgnoreCase)) continue;

				string from = enable ? outOfGame : inGame;
				string to = enable ? inGame : outOfGame;

				if (!File.Exists(from)) continue;
				if (File.Exists(to)) File.Delete(to);

				Directory.CreateDirectory(Path.GetDirectoryName(to)!);
				File.Move(from, to);
				moved++;
			}
			catch (Exception ex)
			{
				logError(relative, $"Could not {(enable ? "restore" : "park")} a file belonging to the mod: " + ex.Message);
			}
		}

		// Leave nothing behind once the last file has gone back.
		if (enable)
		{
			try
			{
				if (Directory.Exists(parked) && !Directory.EnumerateFiles(parked, "*", SearchOption.AllDirectories).Any())
					Directory.Delete(parked, recursive: true);
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Witcher3", $"removing the emptied folder {parked}", ex); }
		}

		return moved;
	}

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
		if (string.IsNullOrEmpty(gameFolder)) return null;

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
	/// True for the game's own folders, which must survive a mod's removal however empty they look. The two
	/// executable folders are named because they are where an ASI-style mod puts most of its files, and an
	/// over-eager tidy-up there would take the game's executable folder with it.
	/// </summary>
	private static bool IsWitcherGameOwnedFolder(string folder, string gameFolder)
	{
		string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
		foreach (string own in new[] { "dlc", "bin", "mods", @"bin\x64", @"bin\x64_dx12", @"bin\config" })
			if (full.Equals(Path.GetFullPath(Path.Combine(gameFolder, own)).TrimEnd(Path.DirectorySeparatorChar),
					StringComparison.OrdinalIgnoreCase))
				return true;
		return false;
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
		string? nexusId, NexusService? nexusService, string? gitHubRepo)
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
		string targetFolderName = SanitiseFolderName(identity.Name.Length > 0 ? identity.Name : archiveName);
		if (targetFolderName.Length == 0) targetFolderName = SanitiseFolderName(archiveName);

		string destModFolder = Path.Combine(modsPath, targetFolderName);

		// Back up and clear whatever is already installed, whether the manager knows it as this mod (matched by
		// Nexus id) or simply as a folder of the same name. A disabled copy counts too — it is the same mod.
		GameMod? existing = FindExistingInstall(installedMods, nexusId, targetFolderName);
		if (existing != null && Directory.Exists(existing.FolderPath))
		{
			CreateBackup(existing.FolderPath, targetFolderName, backupsPath);
			PruneBackups(targetFolderName, backupsPath, maxBackups);
			ForceDeleteDirectory(existing.FolderPath);
		}
		if (Directory.Exists(destModFolder))
			ForceDeleteDirectory(destModFolder);

		Directory.CreateDirectory(destModFolder);
		foreach (string dir in Directory.GetDirectories(modSource, "*", SearchOption.AllDirectories))
			Directory.CreateDirectory(dir.Replace(modSource, destModFolder));
		foreach (string file in Directory.GetFiles(modSource, "*.*", SearchOption.AllDirectories))
			RobustCopy(file, file.Replace(modSource, destModFolder));

		// Keep whatever documentation the author shipped, so the controls viewer can read the mod's keybindings.
		CaptureModDocs(tempDir, destModFolder);

		string mName = identity.Name.Length > 0 ? identity.Name : targetFolderName;
		string mVersion = identity.Version.Length > 0
			? identity.Version
			: (ExtractVersionFromFileName(zipPath, nexusId) ?? "1.0.0");
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
					if (identity.Name.Length == 0) mName = details["name"]?.ToString() ?? mName;
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
		File.WriteAllText(Path.Combine(destModFolder, ".manager_manifest.json"), manifest.ToString(Formatting.Indented));

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

	public static string? ParseNexusId(JToken? keys)
	{
		if (keys == null) return null;

		IEnumerable<JToken> tokens = keys.Type == JTokenType.Array
			? keys.Children()
			: keys.Type == JTokenType.String ? new List<JToken> { keys } : Enumerable.Empty<JToken>();

		foreach (JToken token in tokens)
		{
			string text = token.ToString();
			if (!text.Contains("Nexus:", StringComparison.OrdinalIgnoreCase)) continue;

			string[] parts = text.Split(':');
			if (parts.Length < 2) continue;

			string id = parts[1].Trim();
			if (id.Contains('@')) id = id.Split('@')[0].Trim();
			if (long.TryParse(id, out _)) return id;
		}
		return null;
	}

	/// <summary>
	/// Reads the mod UniqueIDs declared inside a downloaded .zip, by parsing every manifest.json in it without
	/// extracting anything. Pairing these with <see cref="ModManifest.ParseNexusIdFromFileName"/> recovers exactly which
	/// installed mods came from which Nexus page — including every mod of a multi-mod download, where usually
	/// only one (or none) carries an update key. Returns an empty list for anything unreadable or not a zip.
	/// </summary>
	public static List<string> ReadModIdsInArchive(string archivePath)
	{
		var ids = new List<string>();
		try
		{
			if (DetectArchiveFormat(archivePath) != ArchiveFormat.Zip) return ids;
			using ZipArchive archive = ZipFile.OpenRead(archivePath);
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				if (!entry.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
				if (entry.Length > 512 * 1024) continue;   // a "manifest" that large isn't one
				try
				{
					using var reader = new StreamReader(entry.Open());
					JObject manifest = JObject.Parse(reader.ReadToEnd());
					string? id = ManifestString(manifest, "UniqueID");
					if (!string.IsNullOrWhiteSpace(id)) ids.Add(id!.Trim());
				}
				catch (Exception ex) { DiagnosticLog.WriteException("Install", $"reading a manifest inside {archivePath}", ex); }
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Install", $"reading {archivePath} as a zip", ex); }
		return ids;
	}

	public static string? ParseGitHubRepo(JToken? keys)
	{
		if (keys == null) return null;

		IEnumerable<JToken> tokens = keys.Type == JTokenType.Array
			? keys.Children()
			: keys.Type == JTokenType.String ? new List<JToken> { keys } : Enumerable.Empty<JToken>();

		foreach (JToken token in tokens)
		{
			string text = token.ToString();
			if (!text.Contains("GitHub:", StringComparison.OrdinalIgnoreCase)) continue;

			string[] parts = text.Split(':');
			if (parts.Length < 2) continue;

			string repo = parts[1].Trim();
			if (repo.Contains('@')) repo = repo.Split('@')[0].Trim();
			return repo;
		}
		return null;
	}

	public static string DetectCategory(string name, string desc)
	{
		string combined = (name + " " + desc).ToLower();
		if (combined.Contains("expansion") || combined.Contains("content pack"))                    return "Expansion";
		if (combined.Contains("npc") || combined.Contains("character"))                             return "NPC";
		if (combined.Contains("portrait") || combined.Contains("sprite"))                           return "Portrait";
		if (combined.Contains("farm") || combined.Contains("map") || combined.Contains("location")) return "Map";
		if (combined.Contains("craft") || combined.Contains("machine") || combined.Contains("item")) return "Crafting";
		if (combined.Contains("audio") || combined.Contains("music") || combined.Contains("sound")) return "Audio";
		if (combined.Contains("visual") || combined.Contains("recolor") || combined.Contains("texture")) return "Visual";
		return "General";
	}

	private static async Task<string> Ensure7ZipCommandLineTool(string dataBasePath, NexusService? nexusService)
	{
		string toolDir = Path.Combine(dataBasePath, "tools");
		if (!Directory.Exists(toolDir))
		{
			Directory.CreateDirectory(toolDir);
		}
		string exePath = Path.Combine(toolDir, "7za.exe");
		if (File.Exists(exePath))
		{
			return exePath;
		}

		string zipPath = Path.Combine(toolDir, "7za920.zip");
		string url = "https://www.7-zip.org/a/7za920.zip";
		
		byte[] zipBytes;
		if (nexusService != null)
		{
			zipBytes = await nexusService.DownloadBytesAsync(url);
		}
		else
		{
			using var client = new System.Net.Http.HttpClient();
			zipBytes = await client.GetByteArrayAsync(url);
		}

		File.WriteAllBytes(zipPath, zipBytes);
		
		using (ZipArchive archive = ZipFile.OpenRead(zipPath))
		{
			ZipArchiveEntry? entry = archive.GetEntry("7za.exe");
			if (entry != null)
			{
				entry.ExtractToFile(exePath, overwrite: true);
			}
		}

		try { File.Delete(zipPath); }
		catch (Exception ex) { DiagnosticLog.WriteException("Install", $"deleting the downloaded {zipPath}", ex); }

		return exePath;
	}

	/// <summary>
	/// Extracts a RAR (or other SharpCompress-supported) archive to <paramref name="outputDir"/>, preserving
	/// folder structure. Used for <c>.rar</c> mods, which neither .NET's ZipFile nor the bundled 7za can read.
	/// The caller's post-extraction path-escape check (in <see cref="ExtractModAsync"/>) still guards against
	/// malicious entries.
	/// </summary>
	/// <summary>
	/// Extracts a .zip entry-by-entry so install progress can be reported as a true percentage of bytes written.
	/// Mirrors <see cref="ZipFile.ExtractToDirectory(string,string)"/> including its path-traversal guard (an entry
	/// whose resolved path escapes <paramref name="outputDir"/> is rejected before any bytes are written).
	/// </summary>
	/// <summary>The archive container of a downloaded mod, identified by its file signature.</summary>
	/// <summary>
	/// Thrown when an archive extracted fine but doesn't hold a mod for the active game — most often a Stardew
	/// download with no <c>manifest.json</c> anywhere inside. Distinct from an I/O or extraction failure because
	/// the fix is different: the file is the wrong download, usually because the mod is linked to the wrong Nexus
	/// page. The message carries what the archive actually contained, for the error log.
	/// </summary>
	public sealed class ModArchiveContentException : Exception
	{
		public ModArchiveContentException(string message) : base(message) { }
	}

	/// <summary>
	/// Builds the diagnostic message for a Stardew download with no manifest.json: names the archive and lists
	/// what was actually extracted, so the error log shows whether the file was empty, held only documentation,
	/// or is simply a different mod than expected.
	/// </summary>
	private static string DescribeMissingManifest(string archivePath, string extractedRoot)
	{
		string contents;
		try
		{
			var names = Directory.EnumerateFileSystemEntries(extractedRoot, "*", SearchOption.TopDirectoryOnly)
				.Select(Path.GetFileName).Take(8).ToList();
			contents = names.Count == 0 ? "the archive extracted to nothing" : "it contains: " + string.Join(", ", names);
		}
		catch { contents = "its contents could not be listed"; }

		return $"No manifest.json found in {Path.GetFileName(archivePath)} — {contents}. " +
			   "This download is not a SMAPI mod, so it is probably the wrong file for this mod.";
	}

	/// <summary>
	/// Unpacks any archives found inside an already-extracted download, one level deep, into a sibling folder
	/// each. Used only as a fallback when the expected mod files weren't found at the top level. Best-effort:
	/// an archive that can't be read is skipped rather than failing the install.
	/// </summary>
	private static async Task ExtractNestedArchivesAsync(string tempDir, NexusService? nexusService)
	{
		string[] inner;
		try
		{
			inner = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories)
				.Where(f =>
				{
					string ext = Path.GetExtension(f).ToLowerInvariant();
					return ext == ".zip" || ext == ".7z" || ext == ".rar";
				})
				.Take(12)   // a sane bound: a mod pack with more variants than this isn't auto-installable anyway
				.ToArray();
		}
		catch { return; }

		foreach (string archive in inner)
		{
			try
			{
				string outDir = archive + "__unpacked";
				Directory.CreateDirectory(outDir);
				ArchiveFormat fmt = DetectArchiveFormat(archive);
				if (fmt == ArchiveFormat.Unknown)
				{
					string ext = Path.GetExtension(archive).ToLowerInvariant();
					fmt = ext == ".7z" ? ArchiveFormat.SevenZip : ext == ".rar" ? ArchiveFormat.Rar : ArchiveFormat.Zip;
				}
				if (fmt == ArchiveFormat.SevenZip)
				{
					string dataBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudiVentureGames", "KinetixModManager");
					string exePath = await Ensure7ZipCommandLineTool(dataBasePath, nexusService);
					Run7ZipExtract(exePath, archive, outDir);
				}
				else if (fmt == ArchiveFormat.Rar)
				{
					ExtractWithSharpCompress(archive, outDir);
				}
				else
				{
					ExtractZipWithProgress(archive, outDir, null);
				}
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Install", $"unpacking the nested archive {archive}", ex); }
		}
	}

	private enum ArchiveFormat { Zip, SevenZip, Rar, Unknown }

	/// <summary>
	/// Detects a mod archive's real format from its leading bytes (magic number) rather than its file extension.
	/// Nexus mods are commonly .7z or .rar, but the downloaded file can end up named ".zip" — the CDN filename
	/// isn't always present, in which case <see cref="ResolveNxmUrlAsync"/> falls back to a ".zip" name. Feeding a
	/// 7z/rar to .NET's ZipFile then throws "End of Central Directory record could not be found". Sniffing the
	/// signature routes each archive to the right extractor regardless of how it was named.
	/// </summary>
	private static ArchiveFormat DetectArchiveFormat(string path)
	{
		try
		{
			using FileStream fs = File.OpenRead(path);
			byte[] head = new byte[8];
			int n = fs.Read(head, 0, head.Length);
			// 7z: 37 7A BC AF 27 1C
			if (n >= 6 && head[0] == 0x37 && head[1] == 0x7A && head[2] == 0xBC && head[3] == 0xAF && head[4] == 0x27 && head[5] == 0x1C)
				return ArchiveFormat.SevenZip;
			// RAR (v1.5–4.x and v5.0 both start): 52 61 72 21 1A 07
			if (n >= 6 && head[0] == 0x52 && head[1] == 0x61 && head[2] == 0x72 && head[3] == 0x21 && head[4] == 0x1A && head[5] == 0x07)
				return ArchiveFormat.Rar;
			// ZIP (incl. empty/spanned variants): 50 4B {03 04 | 05 06 | 07 08}
			if (n >= 4 && head[0] == 0x50 && head[1] == 0x4B &&
				((head[2] == 0x03 && head[3] == 0x04) || (head[2] == 0x05 && head[3] == 0x06) || (head[2] == 0x07 && head[3] == 0x08)))
				return ArchiveFormat.Zip;
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Install", $"reading the first bytes of {path} to identify it", ex); }
		return ArchiveFormat.Unknown;
	}

	private static void ExtractZipWithProgress(string archivePath, string outputDir, IProgress<double>? progress)
	{
		using ZipArchive archive = ZipFile.OpenRead(archivePath);
		string canonicalRoot = Path.GetFullPath(outputDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		long total = 0;
		foreach (ZipArchiveEntry e in archive.Entries) total += e.Length;
		if (total <= 0) total = 1;

		long done = 0;
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			string destPath = Path.GetFullPath(Path.Combine(outputDir, entry.FullName));
			if (!destPath.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException($"Unsafe archive: entry escapes the extraction directory ({entry.FullName}).");

			// Directory entries have an empty Name; create the folder and move on.
			bool isDir = entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\") || string.IsNullOrEmpty(entry.Name);
			if (isDir)
			{
				Directory.CreateDirectory(destPath);
				continue;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
			entry.ExtractToFile(destPath, overwrite: true);
			done += entry.Length;
			progress?.Report((double)done / total * 100.0);
		}
		progress?.Report(100.0);
	}

	private static void ExtractWithSharpCompress(string archivePath, string outputDir, IProgress<double>? progress = null)
	{
		if (progress == null)
		{
			// ArchiveFactory auto-detects the format (RAR4/RAR5) and extracts every entry, preserving paths.
			ArchiveFactory.WriteToDirectory(archivePath, outputDir,
				new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
			return;
		}

		// Progress variant: extract entry-by-entry, reporting bytes written as a percentage of the total.
		using IArchive archive = RarArchive.OpenArchive(archivePath, null);
		var options = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
		long total = 0;
		foreach (IArchiveEntry e in archive.Entries)
			if (!e.IsDirectory && e.Size > 0) total += e.Size;
		if (total <= 0) total = 1;

		long done = 0;
		foreach (IArchiveEntry entry in archive.Entries)
		{
			if (entry.IsDirectory) continue;
			entry.WriteToDirectory(outputDir, options);
			if (entry.Size > 0) done += entry.Size;
			progress.Report((double)done / total * 100.0);
		}
		progress.Report(100.0);
	}

	private static void Run7ZipExtract(string exePath, string archivePath, string outputDir, IProgress<double>? progress = null)
	{
		// Parsing 7za's in-place progress output is brittle across versions, so for install % we instead poll the
		// bytes written to the output folder against the archive's known uncompressed size. The extraction command
		// itself is left exactly as before, so progress can never break an install — it's purely observational.
		long totalUncompressed = progress != null ? Try7ZipUncompressedSize(exePath, archivePath) : 0;

		using var process = new System.Diagnostics.Process();
		process.StartInfo.FileName = exePath;
		process.StartInfo.Arguments = $"x \"{archivePath}\" -o\"{outputDir}\" -y";
		process.StartInfo.CreateNoWindow = true;
		process.StartInfo.UseShellExecute = false;
		process.StartInfo.RedirectStandardOutput = false;
		process.StartInfo.RedirectStandardError = false;

		process.Start();

		if (progress != null && totalUncompressed > 0)
		{
			while (!process.WaitForExit(250))
			{
				long written = DirectorySize(outputDir);
				progress.Report(Math.Clamp((double)written / totalUncompressed * 100.0, 0, 99));
			}
		}
		process.WaitForExit();

		if (process.ExitCode != 0)
		{
			throw new Exception($"7-Zip extraction failed with exit code {process.ExitCode}.");
		}
		progress?.Report(100.0);
	}

	/// <summary>Sums the uncompressed size of every file in a 7z archive via <c>7za l -slt</c>; 0 if it can't be read.</summary>
	private static long Try7ZipUncompressedSize(string exePath, string archivePath)
	{
		try
		{
			using var p = new System.Diagnostics.Process();
			p.StartInfo.FileName = exePath;
			p.StartInfo.Arguments = $"l -slt \"{archivePath}\"";
			p.StartInfo.CreateNoWindow = true;
			p.StartInfo.UseShellExecute = false;
			p.StartInfo.RedirectStandardOutput = true;
			p.Start();
			string output = p.StandardOutput.ReadToEnd();
			p.WaitForExit();

			long sum = 0;
			foreach (System.Text.RegularExpressions.Match m in
				System.Text.RegularExpressions.Regex.Matches(output, @"(?m)^Size = (\d+)\s*$"))
			{
				if (long.TryParse(m.Groups[1].Value, out long size)) sum += size;
			}
			return sum;
		}
		catch { return 0; }
	}

	/// <summary>Total size in bytes of all files currently under <paramref name="dir"/> (best-effort; 0 on error).</summary>
	private static long DirectorySize(string dir)
	{
		try
		{
			long sum = 0;
			foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
			{
				try { sum += new FileInfo(f).Length; }
				catch (Exception ex) { DiagnosticLog.WriteException("Disk", $"measuring {f}", ex); }
			}
			return sum;
		}
		catch { return 0; }
	}

	public static async Task InstallScriptExtenderAsync(string archivePath, string gamePath, string activeGame, Action<string, string> logError, NexusService? nexusService = null, IProgress<double>? installProgress = null)
	{
		string tempDir = Path.Combine(Path.GetTempPath(), "Extender_" + Path.GetRandomFileName());
		try
		{
			Directory.CreateDirectory(tempDir);
			string ext = Path.GetExtension(archivePath).ToLower();
			if (ext == ".7z")
			{
				string dataBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudiVentureGames", "KinetixModManager");
				string exePath = await Ensure7ZipCommandLineTool(dataBasePath, nexusService);
				await Task.Run(() => Run7ZipExtract(exePath, archivePath, tempDir, installProgress));
			}
			else
			{
				await Task.Run(() => ExtractZipWithProgress(archivePath, tempDir, installProgress));
			}

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
			File.WriteAllText(path, JsonConvert.SerializeObject(manifest, Formatting.Indented));
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
	public static async Task InstallEnginePreloaderAsync(string archivePath, string gamePath, Action<string, string> logError, NexusService? nexusService = null)
	{
		string tempDir = Path.Combine(Path.GetTempPath(), "Preloader_" + Path.GetRandomFileName());
		try
		{
			Directory.CreateDirectory(tempDir);
			string ext = Path.GetExtension(archivePath).ToLower();
			if (ext == ".7z")
			{
				string dataBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudiVentureGames", "KinetixModManager");
				string exePath = await Ensure7ZipCommandLineTool(dataBasePath, nexusService);
				await Task.Run(() => Run7ZipExtract(exePath, archivePath, tempDir));
			}
			else
			{
				await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, tempDir));
			}

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

	public static string? ExtractVersionFromFileName(string fileName, string? modId)
	{
		if (string.IsNullOrEmpty(modId)) return null;

		string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
		string target = "-" + modId + "-";
		int index = nameWithoutExt.IndexOf(target, StringComparison.OrdinalIgnoreCase);
		if (index == -1) return null;

		string suffix = nameWithoutExt.Substring(index + target.Length);
		int lastDash = suffix.LastIndexOf('-');
		string versionPart = lastDash == -1 ? suffix : suffix.Substring(0, lastDash);

		return versionPart.Replace('-', '.');
	}

	public static bool CompareVersionsNewer(string? current, string? target)
	{
		if (string.IsNullOrEmpty(target)) return false;
		if (string.IsNullOrEmpty(current)) return true;

		string[] parts1 = current.Split('.');
		string[] parts2 = target.Split('.');
		for (int i = 0; i < Math.Max(parts1.Length, parts2.Length); i++)
		{
			int v1 = (i < parts1.Length && int.TryParse(parts1[i], out int r1)) ? r1 : 0;
			int v2 = (i < parts2.Length && int.TryParse(parts2[i], out int r2)) ? r2 : 0;
			if (v2 > v1) return true;
			if (v1 > v2) return false;
		}
		return false;
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

	/// <summary>Deletes a directory tree robustly: clears read-only attributes first (the common cause of an
	/// "access is denied" on delete) and retries a few times for a transient lock (e.g. an antivirus scanning a
	/// freshly written file).</summary>
	private static void ForceDeleteDirectory(string path)
	{
		if (!Directory.Exists(path)) return;
		ClearReadOnlyRecursive(path);
		for (int attempt = 0; ; attempt++)
		{
			try { Directory.Delete(path, recursive: true); return; }
			catch (Exception) when (attempt < 3) { Thread.Sleep(200); }
		}
	}

	/// <summary>Public entry point for deleting a mod's folder from the managed mods directory. Uses the same
	/// read-only-clearing, retrying delete as the installer, so a mod that ships a read-only file (e.g. SkyPatcher's
	/// DLL) no longer fails the user's Delete action with "Access to the path '…' is denied".</summary>
	public static void DeleteModFolder(string path) => ForceDeleteDirectory(path);

	/// <summary>Deletes a single file robustly: clears a read-only attribute first (otherwise the delete throws
	/// "access is denied" on mods that mark files read-only) and retries a few times for a transient lock.</summary>
	private static void RobustDeleteFile(string path)
	{
		if (!File.Exists(path)) return;
		for (int attempt = 0; ; attempt++)
		{
			try
			{
				FileAttributes attrs = File.GetAttributes(path);
				if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
				File.Delete(path);
				return;
			}
			catch (Exception) when (attempt < 3) { Thread.Sleep(200); }
		}
	}

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
