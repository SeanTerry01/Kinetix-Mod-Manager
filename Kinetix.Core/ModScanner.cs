using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// Reads what is installed. Every supported game's mod folder, turned into a list of <see cref="GameMod"/>.
///
/// <para>
/// This is the half of the old ModFileSystem that only ever looked. The other half — enabling, deploying,
/// backing up, extracting, writing plugins.txt — still lives in the app, and the split is along exactly that
/// line: reading what is there against changing it. A 3,825-line class doing both was the second-largest
/// thing in the review after Form1 itself, and scanning is the piece a second front end needs first, since
/// a mod manager that cannot list your mods has nothing to show you.
/// </para>
///
/// <para>
/// Four layouts, and they genuinely differ rather than being variations on a theme: Stardew's folders with a
/// manifest.json, the Bethesda games' staged folders deployed elsewhere, BepInEx's DLLs under plugins, and
/// Minecraft's jars — the one layout where a mod is a file rather than a folder. See <see cref="ModLayout"/>.
/// </para>
/// </summary>
public static class ModScanner
{
	// Manifest reading is case-insensitive and tolerant of the ways hand-written manifests differ; the rules
	// (and why they matter) live in ModManifest, which is unit tested.
	private static JToken? ManifestField(JObject manifest, string name) => ModManifest.Field(manifest, name);

	public static string? ManifestString(JObject manifest, string name) => ModManifest.String(manifest, name);

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
		IModScanContext settings,
		string activeGame,
		Action<string, string> logError,
		Action<GameMod>? removeSuperseded = null)
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
		else if (GameProfiles.Find(activeGame)?.IsMinecraft == true)
		{
			mods.AddRange(ScanFabricMods(modsPath, settings, activeGame, logError));
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
								// Handing the superseded copy to the caller rather than deleting it here. A scan
								// that removes folders is not a scan, and this one did: refreshing the list could
								// undeploy, back up and delete a mod, with nothing in the name to suggest it.
								// Leaving the behaviour exactly where it was, but in the caller's hands and
								// visible in this method's signature — and a caller that passes nothing (the GTK
								// head, for now) gets a scan that only ever reads.
								removeSuperseded?.Invoke(m);
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
	/// <summary>
	/// Every Fabric mod in <paramref name="modsPath"/> — one per <c>.jar</c> file, enabled or not.
	///
	/// The scan is flat by nature: Fabric loads the mods folder itself and does not recurse, so a jar in a
	/// subfolder is not a mod that is installed somewhere odd, it is a mod that is not installed. Listing it
	/// would say otherwise.
	/// </summary>
	private static List<GameMod> ScanFabricMods(
		string modsPath,
		IModScanContext settings,
		string activeGame,
		Action<string, string> logError)
	{
		var mods = new List<GameMod>();
		string? disabledSuffix = GameProfiles.Find(activeGame)?.DisabledModSuffix;

		foreach (string file in Directory.GetFiles(modsPath, "*", SearchOption.TopDirectoryOnly))
		{
			if (!MinecraftLayout.IsModFile(file, disabledSuffix)) continue;

			try
			{
				FabricModInfo info = MinecraftLayout.ReadModInfo(file);
				string fileName = Path.GetFileName(file);

				// A jar with no readable fabric.mod.json still gets a row. It is sitting in the mods folder, so
				// the user put it there and expects to see it — and showing it is the only way they can find out
				// it is broken and delete it. Keyed by file name, since it gave no id to key on.
				string uid = info.Id.Length > 0 ? info.Id : fileName;

				var mod = new GameMod
				{
					Name        = info.Name.Length > 0 ? info.Name : fileName,
					Version     = info.Version,
					Author      = info.Authors.Count > 0 ? string.Join(", ", info.Authors) : "User",
					UniqueId    = uid,
					Description = info.Description,
					FolderPath  = file,
					IsEnabled   = MinecraftLayout.IsEnabledModFile(file),
					// Kept from the mod's own manifest so a mod that is NOT on Modrinth can still be checked
					// for updates. United Minecraft is published on GitHub releases only and names its
					// repository right here — without this it would be the one mod in the folder nothing
					// could ever tell the player was out of date.
					GitHubRepo  = MinecraftLayout.GitHubRepoFromUrl(info.HomepageUrl) is { Length: > 0 } repo
						? repo
						: null
				};

				if (info.IsUnreadable)
					logError(file, "Not a readable Fabric mod: no " + MinecraftLayout.ManifestEntryName + " inside.");

				mod.Category = settings.ModCategories.TryGetValue(uid, out string? cat) ? cat
					: DetectCategory(mod.Name, mod.Description);
				mod.Note = settings.ModNotes.TryGetValue(uid, out string? note) ? note : "";

				// Fabric's depends map mixes real mods in with three pseudo-ids the loader satisfies itself —
				// fabricloader, minecraft and java. Listing those as dependencies would have the requirements
				// check hunting the mods folder for a mod called "java" and reporting it missing forever.
				// Anything the mod carries nested inside its own jar is likewise already satisfied, which is the
				// whole reason Minecraft Access needs nothing installed alongside it.
				foreach (KeyValuePair<string, string> dep in info.Depends)
				{
					if (IsFabricBuiltInDependency(dep.Key)) continue;
					if (info.NestedJars.Contains(dep.Key, StringComparer.OrdinalIgnoreCase)) continue;

					mod.Dependencies.Add(new ModDependency
					{
						UniqueId       = dep.Key,
						MinimumVersion = dep.Value,
						IsRequired     = true
					});
				}

				mods.Add(mod);
			}
			catch (Exception ex)
			{
				logError(file, "Parse Error: " + ex.Message);
			}
		}

		return mods;
	}

	/// <summary>
	/// True for the pseudo-dependencies Fabric resolves itself rather than from the mods folder: the loader, the
	/// game, and the Java runtime. A mod declaring these is stating a minimum version, not naming another mod.
	/// </summary>
	private static bool IsFabricBuiltInDependency(string id) =>
		id.Equals("fabricloader", StringComparison.OrdinalIgnoreCase) ||
		id.Equals("minecraft", StringComparison.OrdinalIgnoreCase) ||
		id.Equals("java", StringComparison.OrdinalIgnoreCase);

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
		string pluginsPath, JObject nexusIdMap, IModScanContext settings, Action<string, string> logError)
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

	/// <summary>
	/// Strips the characters Windows forbids in a folder name, so a plugin name can become a folder.
	/// The set is <see cref="WindowsFileName"/>'s rather than the host's, for the reason given there:
	/// the name has to satisfy the game, which is a Windows game wherever this manager happens to run.
	/// </summary>
	public static string SanitiseFolderName(string name)
	{
		string cleaned = WindowsFileName.StripInvalid(name).Trim();
		return cleaned.TrimEnd('.');
	}

	// ---------------------------------------------------------------------------------------------
	// Reading what a mod says about itself: its update keys, its version, what sort of mod it is.
	// These came across with the scan because they are only ever used while scanning, and they are
	// pure - given the same manifest they give the same answer, on any machine.
	// ---------------------------------------------------------------------------------------------

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
}
