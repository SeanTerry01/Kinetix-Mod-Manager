using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpCompress.Archives;
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

		if (activeGame == "StardewValley")
		{
			foreach (string manifestPath in Directory.GetFiles(modsPath, "manifest.json", SearchOption.AllDirectories))
			{
				try
				{
					JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
					string uid = ((string?)manifest["UniqueID"]) ?? Guid.NewGuid().ToString();

					var mod = new GameMod
					{
						Name        = ((string?)manifest["Name"])        ?? "Unknown",
						Version     = ((string?)manifest["Version"])     ?? "0",
						Author      = ((string?)manifest["Author"])      ?? "User",
						UniqueId    = uid,
						Description = ((string?)manifest["Description"]) ?? "",
						NexusID     = ParseNexusId(manifest["UpdateKeys"]),
						GitHubRepo  = ParseGitHubRepo(manifest["UpdateKeys"]),
						FolderPath  = Path.GetDirectoryName(manifestPath) ?? "",
						IsEnabled   = !Path.GetFileName(Path.GetDirectoryName(manifestPath) ?? "").StartsWith(".")
					};

					if (nexusIdMap.TryGetValue(uid, out JToken? mappedId))
						mod.NexusID = mappedId?.ToString();

					mod.Category = settings.ModCategories.TryGetValue(uid, out string? cat) ? cat
						: DetectCategory(mod.Name, mod.Description);

					if (manifest["Dependencies"] is JArray deps)
					{
						foreach (JToken dep in deps)
						{
							if (dep == null) continue;
							mod.Dependencies.Add(new ModDependency
							{
								UniqueId       = ((string?)dep["UniqueID"])       ?? "Unknown",
								MinimumVersion = (string?)dep["MinimumVersion"],
								IsRequired     = ((bool?)dep["IsRequired"]) ?? true
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
		else
		{
			// Skyrim / Fallout 4: scan direct subdirectories of modsPath
			foreach (string dir in Directory.GetDirectories(modsPath))
			{
				string folderName = Path.GetFileName(dir);
				if (folderName.Equals("bin", StringComparison.OrdinalIgnoreCase) || 
					folderName.Equals("obj", StringComparison.OrdinalIgnoreCase))
					continue;

				string manifestPath = Path.Combine(dir, ".manager_manifest.json");
				GameMod mod;

				try
				{
					if (File.Exists(manifestPath))
					{
						JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
						string uid = ((string?)manifest["UniqueID"]) ?? folderName;
						string? nexusId = (string?)manifest["NexusID"];

						// Auto-extract NexusID from folderName if not present
						if (string.IsNullOrEmpty(nexusId) && activeGame != "StardewValley")
						{
							var match = System.Text.RegularExpressions.Regex.Match(folderName, @"-(\d{3,9})-");
							if (match.Success)
							{
								nexusId = match.Groups[1].Value;
								try
								{
									manifest["NexusID"] = nexusId;
									File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
								}
								catch {}
							}
						}

						mod = new GameMod
						{
							Name        = ((string?)manifest["Name"])        ?? folderName,
							Version     = ((string?)manifest["Version"])     ?? "1.0.0",
							Author      = ((string?)manifest["Author"])      ?? "Unknown",
							UniqueId    = uid,
							Description = ((string?)manifest["Description"]) ?? "",
							NexusID     = nexusId,
							GitHubRepo  = ((string?)manifest["GitHubRepo"]),
							FolderPath  = dir,
							IsEnabled   = !folderName.StartsWith(".")
						};
					}
					else
					{
						// Create automatic manifest
						string cleanName = folderName.StartsWith(".") ? folderName.Substring(1) : folderName;
						string? extractedNexusId = null;
						if (activeGame != "StardewValley")
						{
							var match = System.Text.RegularExpressions.Regex.Match(folderName, @"-(\d{3,9})-");
							if (match.Success)
							{
								extractedNexusId = match.Groups[1].Value;
							}
						}

						string guessedVersion = ExtractVersionFromFileName(folderName, extractedNexusId) ?? "1.0.0";

						mod = new GameMod
						{
							Name        = cleanName,
							Version     = guessedVersion,
							Author      = "Unknown",
							UniqueId    = cleanName,
							Description = "Installed local mod.",
							FolderPath  = dir,
							IsEnabled   = !folderName.StartsWith("."),
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

					mods.Add(mod);
				}
				catch (Exception ex)
				{
					logError(manifestPath, "Parse Error: " + ex.Message);
				}
			}
		}

		if (activeGame != "StardewValley" && mods.Count > 1)
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
								SyncPluginsFile(m.FolderPath, activeGame, false, logError);

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

	/// <summary>
	/// Resolves dependencies by cross-referencing with other mods.
	/// </summary>
	public static void ResolveDependencies(
		List<GameMod> mods,
		Func<string?, string?, bool> isNewerVersion)
	{
		foreach (var mod in mods)
		{
			foreach (var dep in mod.Dependencies)
			{
				var found = mods.FirstOrDefault(m => m.UniqueId == dep.UniqueId);
				if (found != null)
				{
					dep.IsPresent  = true;
					dep.IsEnabled  = found.IsEnabled;
					dep.IsNewEnough = isNewerVersion(dep.MinimumVersion, found.Version)
					               || dep.MinimumVersion == found.Version;
				}
			}
		}
	}

	// -------------------------------------------------------------------------
	// Backup management
	// -------------------------------------------------------------------------

	/// <summary>
	/// Creates a timestamped <c>.zip</c> backup of a mod folder.
	/// </summary>
	public static void CreateBackup(string folderPath, string modName, string backupsPath)
	{
		if (!Directory.Exists(folderPath)) return;
		string dest = Path.Combine(backupsPath, $"{modName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
		ZipFile.CreateFromDirectory(folderPath, dest);
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
			try { files[i].Delete(); } catch { }
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
	public static void SyncPluginsFile(string modFolderPath, string activeGame, bool isDeploy, Action<string, string> logError)
	{
		if (activeGame != "SkyrimSE" && activeGame != "Fallout4") return;

		string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string folderName = activeGame == "SkyrimSE" ? "Skyrim Special Edition" : "Fallout4";
		string pluginsFilePath = Path.Combine(localAppData, folderName, "plugins.txt");

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
				File.WriteAllLines(pluginsFilePath, lines);
			}
		}
		catch (Exception ex)
		{
			logError(pluginsFilePath, $"Failed to update plugins.txt: {ex.Message}");
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
		HashSet<string>? forceRelink = null)
	{
		var conflicts = new List<FileConflict>();
		if (string.IsNullOrEmpty(gameRootPath)) return conflicts;
		gameRootPath = Path.GetFullPath(gameRootPath).TrimEnd(Path.DirectorySeparatorChar);

		// Destination path (relative to the game root) -> winning source file / owning mod. providers
		// tracks every mod that supplies a path so conflicts can be reported.
		var desiredSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var desiredOwner  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var providers     = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

		// Walk lowest priority first so the highest-priority provider is written last and wins.
		for (int i = enabledModsHighToLow.Count - 1; i >= 0; i--)
		{
			var (modName, folderPath) = enabledModsHighToLow[i];
			if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) continue;
			string canonical = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

			foreach (string sourceFile in Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories))
			{
				if (Path.GetFileName(sourceFile).Equals(".manager_manifest.json", StringComparison.OrdinalIgnoreCase))
					continue;

				string relativePath = Path.GetFullPath(sourceFile).Substring(canonical.Length);
				bool isRootFile = relativePath.StartsWith("Root" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
								  relativePath.StartsWith("Root" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
				string destRel = isRootFile ? relativePath.Substring(5) : Path.Combine("Data", relativePath);

				desiredSource[destRel] = sourceFile;
				desiredOwner[destRel]  = modName;
				if (!providers.TryGetValue(destRel, out var list)) { list = new List<string>(); providers[destRel] = list; }
				list.Add(modName);
			}
		}

		// 1. Remove files we previously deployed that are no longer wanted by any enabled mod.
		foreach (var kv in manifest.Deployed)
		{
			if (desiredSource.ContainsKey(kv.Key)) continue;
			string destAbs = Path.Combine(gameRootPath, kv.Key);
			try
			{
				if (File.Exists(destAbs)) File.Delete(destAbs);
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
				if (File.Exists(destAbs)) File.Delete(destAbs);
				if (!TryCreateHardLink(destAbs, sourceFile)) File.Copy(sourceFile, destAbs, true);
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
		catch { /* best-effort tidy-up; leftover empty dirs are harmless */ }
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
	public static bool IsBaseMaster(string activeGame, string fileName) => activeGame switch
	{
		"SkyrimSE" => SkyrimBaseMasters.Contains(fileName),
		"Fallout4" => Fallout4BaseMasters.Contains(fileName),
		_ => false
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
		catch { /* unreadable header: fall back to the extension classification below */ }
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
		catch { /* unreadable/odd header: treat as no declared masters */ }
		return masters;
	}

	/// <summary>Returns the active plugin names (asterisk-prefixed lines) from the game's plugins.txt.</summary>
	public static List<string> ReadActivePlugins(string activeGame)
	{
		var result = new List<string>();
		string path = PluginsTxtPath(activeGame);
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
		catch { /* unreadable plugins.txt: treat as no external entries */ }
		return result;
	}

	/// <summary>
	/// Writes plugins.txt as the authoritative active load order: one <c>*name</c> line per plugin in the
	/// given order, which the caller has already arranged masters-first. Replaces the previous per-mod
	/// add/remove approach so the order is deterministic.
	/// </summary>
	public static void WritePluginsTxt(string activeGame, IEnumerable<string> orderedActivePlugins, Action<string, string> logError)
	{
		string path = PluginsTxtPath(activeGame);
		if (string.IsNullOrEmpty(path)) return;
		try
		{
			string? dir = Path.GetDirectoryName(path);
			if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
			File.WriteAllLines(path, orderedActivePlugins.Select(p => "*" + p));
		}
		catch (Exception ex) { logError(path, $"Failed to write plugins.txt: {ex.Message}"); }
	}

	private static string PluginsTxtPath(string activeGame)
	{
		if (activeGame != "SkyrimSE" && activeGame != "Fallout4") return "";
		string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		string folderName = activeGame == "SkyrimSE" ? "Skyrim Special Edition" : "Fallout4";
		return Path.Combine(localAppData, folderName, "plugins.txt");
	}

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
		catch { /* unresolved/unwritable mods drive: fall back to system temp below */ }
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
		Func<FomodConfig, Task<FomodSelection?>>? fomodSelector = null)
	{
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
				string ext = Path.GetExtension(zipPath).ToLower();
				if (ext == ".7z")
				{
					string dataBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudiVentureGames", "KinetixModManager");
					string exePath = await Ensure7ZipCommandLineTool(dataBasePath, nexusService);
					Run7ZipExtract(exePath, zipPath, tempDir);
				}
				else if (ext == ".rar")
				{
					// The bundled 7za and .NET's ZipFile cannot read RAR, so use SharpCompress (managed).
					ExtractWithSharpCompress(zipPath, tempDir);
				}
				else
				{
					ZipFile.ExtractToDirectory(zipPath, tempDir);
				}
			});

			string canonicalTemp = Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			foreach (string entry in Directory.GetFileSystemEntries(tempDir, "*", SearchOption.AllDirectories))
			{
				if (!Path.GetFullPath(entry).StartsWith(canonicalTemp, StringComparison.OrdinalIgnoreCase))
					throw new InvalidOperationException($"Unsafe archive: entry escapes the extraction directory ({entry}).");
			}

			if (activeGame != "StardewValley")
			{
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

					return await FinalizeBethesdaModAsync(
						stagingDir, Path.GetFileNameWithoutExtension(zipPath), zipPath, modsPath, installedMods,
						backupsPath, maxBackups, activeGame, logError, nexusId, nexusService, gitHubRepo, currentGamePath, fomodInfo);
				}

				return await FinalizeBethesdaModAsync(
					FindEffectiveModRoot(tempDir), Path.GetFileNameWithoutExtension(zipPath), zipPath, modsPath, installedMods,
					backupsPath, maxBackups, activeGame, logError, nexusId, nexusService, gitHubRepo, currentGamePath, null);
			}

			// Stardew Valley Manifest logic
			string[] manifests = Directory.GetFiles(tempDir, "manifest.json", SearchOption.AllDirectories);
			if (manifests.Length == 0)
				throw new Exception("No manifest.json found.");

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
						CreateBackup(existing.FolderPath, mName, backupsPath);
						PruneBackups(mName, backupsPath, maxBackups);
						Directory.Delete(existing.FolderPath, recursive: true);
					}
				}
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
					targetFolderNameStardew = Path.GetFileNameWithoutExtension(zipPath);
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
			try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
		}
	}

	/// <summary>
	/// Finalises a prepared Bethesda mod folder: backs up and removes any prior version, copies the
	/// prepared <paramref name="sourceFolder"/> into the mods directory, and writes the manager manifest.
	/// Shared by the normal flat-copy install and the FOMOD pipeline. When <paramref name="fomodInfo"/>
	/// is supplied its Name/Author/Version seed the manifest (a Nexus lookup still overrides when available).
	/// </summary>
	private static async Task<string> FinalizeBethesdaModAsync(
		string sourceFolder, string targetFolderName, string zipPath, string modsPath, List<GameMod> installedMods,
		string backupsPath, int maxBackups, string activeGame, Action<string, string> logError,
		string? nexusId, NexusService? nexusService, string? gitHubRepo, string? currentGamePath, FomodInfo? fomodInfo)
	{
		string destModFolder = Path.Combine(modsPath, targetFolderName);

		// Backup and remove old version
		GameMod? existing = null;
		if (!string.IsNullOrEmpty(nexusId))
			existing = installedMods.FirstOrDefault(m => m.NexusID == nexusId);
		if (existing == null)
			existing = installedMods.FirstOrDefault(m => m.Name.Equals(targetFolderName, StringComparison.OrdinalIgnoreCase) ||
														 m.UniqueId.Equals(targetFolderName, StringComparison.OrdinalIgnoreCase));

		if (existing != null && Directory.Exists(existing.FolderPath))
		{
			// Remove the old version's plugin entries; its deployed asset files are reconciled by the
			// caller's SyncDeployment pass after extraction, so no per-file asset undeploy is needed here.
			if (!string.IsNullOrEmpty(currentGamePath))
				SyncPluginsFile(existing.FolderPath, activeGame, false, logError);

			CreateBackup(existing.FolderPath, targetFolderName, backupsPath);
			PruneBackups(targetFolderName, backupsPath, maxBackups);
			Directory.Delete(existing.FolderPath, recursive: true);
		}

		if (Directory.Exists(destModFolder))
			Directory.Delete(destModFolder, recursive: true);

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
				File.Copy(file, file.Replace(sourceFolder, rootDest), overwrite: true);
		}
		else
		{
			foreach (string dir in Directory.GetDirectories(sourceFolder, "*", SearchOption.AllDirectories))
				Directory.CreateDirectory(dir.Replace(sourceFolder, destModFolder));
			foreach (string file in Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories))
				File.Copy(file, file.Replace(sourceFolder, destModFolder), overwrite: true);
		}

		// Generate manifest
		string manifestPath = Path.Combine(destModFolder, ".manager_manifest.json");
		string mName = targetFolderName;
		string mVersion = ExtractVersionFromFileName(zipPath, nexusId) ?? "1.0.0";
		string mAuthor = "Unknown";
		string mDesc = "Installed local mod.";

		// FOMOD info.xml seeds the metadata for locally-installed scripted mods that have no Nexus id.
		if (fomodInfo != null)
		{
			if (!string.IsNullOrWhiteSpace(fomodInfo.Name)) mName = fomodInfo.Name!;
			if (!string.IsNullOrWhiteSpace(fomodInfo.Version)) mVersion = fomodInfo.Version!;
			if (!string.IsNullOrWhiteSpace(fomodInfo.Author)) mAuthor = fomodInfo.Author!;
		}

		if (!string.IsNullOrEmpty(nexusId) && nexusService != null)
		{
			try
			{
				var details = await nexusService.GetModDetailsAsync(nexusId);
				if (details != null)
				{
					mName = details["name"]?.ToString() ?? mName;
					mVersion = details["version"]?.ToString() ?? mVersion;
					mAuthor = details["author"]?.ToString() ?? mAuthor;
					mDesc = details["summary"]?.ToString() ?? mDesc;
				}
			}
			catch { }
		}

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
				catch { }
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

		try { File.Delete(zipPath); } catch {}

		return exePath;
	}

	/// <summary>
	/// Extracts a RAR (or other SharpCompress-supported) archive to <paramref name="outputDir"/>, preserving
	/// folder structure. Used for <c>.rar</c> mods, which neither .NET's ZipFile nor the bundled 7za can read.
	/// The caller's post-extraction path-escape check (in <see cref="ExtractModAsync"/>) still guards against
	/// malicious entries.
	/// </summary>
	private static void ExtractWithSharpCompress(string archivePath, string outputDir)
	{
		// ArchiveFactory auto-detects the format (RAR4/RAR5) and extracts every entry, preserving paths.
		ArchiveFactory.WriteToDirectory(archivePath, outputDir,
			new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
	}

	private static void Run7ZipExtract(string exePath, string archivePath, string outputDir)
	{
		using var process = new System.Diagnostics.Process();
		process.StartInfo.FileName = exePath;
		process.StartInfo.Arguments = $"x \"{archivePath}\" -o\"{outputDir}\" -y";
		process.StartInfo.CreateNoWindow = true;
		process.StartInfo.UseShellExecute = false;
		process.StartInfo.RedirectStandardOutput = false;
		process.StartInfo.RedirectStandardError = false;
		
		process.Start();
		process.WaitForExit();
		
		if (process.ExitCode != 0)
		{
			throw new Exception($"7-Zip extraction failed with exit code {process.ExitCode}.");
		}
	}

	public static async Task InstallScriptExtenderAsync(string archivePath, string gamePath, string activeGame, Action<string, string> logError, NexusService? nexusService = null)
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
				await Task.Run(() => Run7ZipExtract(exePath, archivePath, tempDir));
			}
			else
			{
				await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, tempDir));
			}

			string loaderExePattern = activeGame == "SkyrimSE" ? "skse64_loader.exe" : "f4se_loader.exe";
			string[] matches = Directory.GetFiles(tempDir, loaderExePattern, SearchOption.AllDirectories);
			if (matches.Length == 0)
			{
				throw new Exception($"Could not find {loaderExePattern} inside the downloaded archive.");
			}

			string sourceDir = Path.GetDirectoryName(matches[0]) ?? tempDir;
			await Task.Run(() => CopyDirectoryRecursively(sourceDir, gamePath));
		}
		finally
		{
			try { Directory.Delete(tempDir, true); } catch {}
		}
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
			try { Directory.Delete(tempDir, true); } catch {}
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
			File.Copy(file, destFile, true);
		}
	}
}
