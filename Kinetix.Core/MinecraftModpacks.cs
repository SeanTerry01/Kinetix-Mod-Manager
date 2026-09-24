using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>Why a modpack cannot be installed, when it cannot. Each is a different sentence to the player.</summary>
public enum ModpackProblem
{
	None,

	/// <summary>No <c>modrinth.index.json</c>, or one that is not JSON: not a Modrinth pack at all.</summary>
	NotAPack,

	/// <summary>A <c>formatVersion</c> newer than the one format there is.</summary>
	UnsupportedFormat,

	/// <summary>A pack for some game other than Minecraft. The format allows for it; nothing uses it yet.</summary>
	NotMinecraft,

	/// <summary>No Minecraft version among the dependencies, so there is nothing to start.</summary>
	NoMinecraftVersion,

	/// <summary>Forge, NeoForge or Quilt — loaders the manager cannot run. The detail names which.</summary>
	UnsupportedLoader,

	/// <summary>No mod loader at all.</summary>
	NoLoader,

	/// <summary>A file that would land outside the pack's folder, or on a name Windows cannot hold. The detail names it.</summary>
	UnsafePath,

	/// <summary>A file offered only from somewhere outside the format's four trusted hosts. The detail names it.</summary>
	UntrustedDownload,

	/// <summary>A file with no SHA-1, so a corrupted download could not be told from a good one. The detail names it.</summary>
	MissingChecksum
}

/// <summary>One file a pack downloads, already checked: a safe path, a trusted address and a checksum.</summary>
public sealed class MrpackFile
{
	/// <summary>Where it goes inside the pack's folder, with this machine's separators.</summary>
	public required string RelativePath { get; init; }

	public required string Url { get; init; }
	public required string Sha1 { get; init; }
	public long Size { get; init; }
}

/// <summary>What a <c>.mrpack</c> says about itself, and whether it can be installed here.</summary>
public sealed class MrpackIndex
{
	public string Name { get; init; } = "";
	public string Summary { get; init; } = "";

	/// <summary>The pack's own version — <c>2.4.2</c> — which is not the Minecraft version.</summary>
	public string VersionId { get; init; } = "";

	public string MinecraftVersion { get; init; } = "";

	/// <summary>The loader's dependency key as the format spells it — <c>fabric-loader</c>, <c>forge</c> — or <c>""</c>.</summary>
	public string Loader { get; init; } = "";

	public string LoaderVersion { get; init; } = "";

	/// <summary>The files to download. Empty when <see cref="Problem"/> is set.</summary>
	public IReadOnlyList<MrpackFile> Files { get; init; } = Array.Empty<MrpackFile>();

	/// <summary>Files the pack marks as server-only, which a player's copy leaves out.</summary>
	public int ServerOnlyFiles { get; init; }

	public ModpackProblem Problem { get; init; }

	/// <summary>What the problem is about — a loader's name or a file's path — or <c>""</c>.</summary>
	public string ProblemDetail { get; init; } = "";

	public long DownloadBytes => Files.Sum(f => f.Size);
}

/// <summary>Which files from a pack's bundled folders go where, and which were refused.</summary>
public sealed class OverridePlan
{
	/// <summary>Zip entry → where it goes inside the pack's folder. Later layers have already replaced earlier ones.</summary>
	public IReadOnlyList<(string Entry, string RelativePath)> Files { get; init; } = Array.Empty<(string, string)>();

	/// <summary>Entries whose path was unsafe. Any at all means the pack is refused.</summary>
	public IReadOnlyList<string> Unsafe { get; init; } = Array.Empty<string>();
}

/// <summary>What updating a pack will do — worked out in full before anything on disk changes.</summary>
public sealed class ModpackUpdatePlan
{
	/// <summary>Files of the new version that are not already on disk with the right checksum.</summary>
	public IReadOnlyList<MrpackFile> Download { get; init; } = Array.Empty<MrpackFile>();

	/// <summary>Files the old version provided that the new one does not, as record paths.</summary>
	public IReadOnlyList<string> Remove { get; init; } = Array.Empty<string>();

	/// <summary>New files to leave switched off, because the player had switched that mod off.</summary>
	public IReadOnlySet<string> KeepDisabled { get; init; } = new HashSet<string>();

	/// <summary>Files already on disk with the right checksum, which need nothing.</summary>
	public int Unchanged { get; init; }

	/// <summary>The new version's bundled files, less the player's <c>options.txt</c>.</summary>
	public OverridePlan Bundled { get; init; } = new();

	/// <summary>The record's new list of what the pack provides.</summary>
	public List<string> ProvidedFiles { get; init; } = new();

	/// <summary>The record's new file-to-project map.</summary>
	public Dictionary<string, string> FileProjects { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// An installed modpack: the record kept in its own folder, as <c>kinetix-pack.json</c>.
///
/// <para>
/// Kept in the pack rather than in the settings file on purpose. A pack's folder then describes itself —
/// copying it, backing it up or deleting it takes its record with it — and <c>settings.json</c>, which an
/// older installed copy of the manager also reads, gains nothing it could trip over.
/// </para>
/// </summary>
public sealed class MinecraftPack
{
	/// <summary>The pack's folder. Not stored: it is wherever the record was read from.</summary>
	[JsonIgnore]
	public string Folder { get; set; } = "";

	public string Name { get; set; } = "";
	public string Summary { get; set; } = "";

	/// <summary>The pack's own version, e.g. <c>2.4.2</c>.</summary>
	public string PackVersion { get; set; } = "";

	public string MinecraftVersion { get; set; } = "";

	/// <summary>Always <c>fabric</c> for now; recorded so that a later loader is not a guess.</summary>
	public string Loader { get; set; } = "fabric";

	public string LoaderVersion { get; set; } = "";

	/// <summary>The pack's Modrinth project, when the file could be identified as one of Modrinth's. Updates need it.</summary>
	public string ModrinthProjectId { get; set; } = "";

	/// <summary>The exact Modrinth version installed, when known.</summary>
	public string ModrinthVersionId { get; set; } = "";

	/// <summary>The <c>.mrpack</c> this was installed from, for saying where a pack came from.</summary>
	public string SourceFileName { get; set; } = "";

	/// <summary>
	/// The Modrinth App instance folder this pack was copied from, or <c>""</c>. What tells the Modrinth App's
	/// leftover files apart from packs the player has not brought across yet — see <see cref="ModrinthAppCleanup"/>.
	/// </summary>
	public string ImportedFrom { get; set; } = "";

	public DateTime InstalledUtc { get; set; }

	/// <summary>
	/// Every file the pack itself put here, as relative paths with forward slashes.
	///
	/// The line between the pack's content and the player's. An update replaces the first and must not touch the
	/// second — a mod the player added, their worlds, their settings — and after the fact nothing on disk can
	/// tell the two apart, so it is written down at the one moment it is certain.
	/// </summary>
	public List<string> ProvidedFiles { get; set; } = new();

	/// <summary>
	/// Which Modrinth project each downloaded file belongs to, by relative path — read from its download address.
	///
	/// What lets an update keep a switched-off mod switched off. A new version of a mod arrives under a new file
	/// name (<c>sodium-0.6.14.jar</c> replacing <c>sodium-0.6.13.jar</c>), so the path alone cannot say it is the
	/// same mod; the project can. A mod that came back on after an update was a bug the player hit once already.
	/// </summary>
	public Dictionary<string, string> FileProjects { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>The Fabric version id this pack starts: <c>fabric-loader-0.18.4-1.21.11</c>.</summary>
	[JsonIgnore]
	public string FabricVersionId => FabricInstaller.VersionIdFor(LoaderVersion, MinecraftVersion);

	[JsonIgnore]
	public string ModsFolder => Path.Combine(Folder, "mods");

	[JsonIgnore]
	public string LatestLogPath => MinecraftLayout.LatestLogPathFor(Folder);
}

/// <summary>
/// Modrinth modpacks: reading a <c>.mrpack</c>, deciding whether it is safe and runnable, and keeping the record
/// of one once it is installed.
///
/// <para>
/// A pack is a zip holding <c>modrinth.index.json</c> — the files to download, each with an address and checksums,
/// and the exact Minecraft and loader versions it was built on — plus <c>overrides/</c> and
/// <c>client-overrides/</c>, folders of files copied in as they are. The format is Modrinth's and published;
/// what this class adds is the refusing. A pack is a list of files someone else chose to write onto this
/// computer, so every path is checked before anything is written, and every download comes from one of the
/// four hosts the format permits.
/// </para>
///
/// <para>
/// ⚠️ A pack is not only its download list. The accessibility pack blind players are pointed to ships its
/// Minecraft Access build inside <c>overrides/mods/</c> — a snapshot that is on no download server — and an
/// older release of it put <c>Tolk.dll</c> and the NVDA client at the top of the folder. Treat the overrides as
/// content, not decoration.
/// </para>
/// </summary>
public static class MinecraftModpacks
{
	public const string IndexFileName = "modrinth.index.json";
	public const string ManifestFileName = "kinetix-pack.json";
	public const string FileExtension = ".mrpack";

	/// <summary>The loader key for Fabric, the one loader the manager runs.</summary>
	public const string FabricLoader = "fabric-loader";

	/// <summary>The bundled-file folders a player's copy takes, in the order they are laid down. Later wins.</summary>
	public static readonly IReadOnlyList<string> OverrideFolders = new[] { "overrides/", "client-overrides/" };

	/// <summary>
	/// The only hosts a pack may download from. This is the format's own list, not a choice made here: Modrinth
	/// will not accept a pack onto its site that points anywhere else, so a file that does is either a pack from
	/// somewhere else entirely or one that has been tampered with.
	/// </summary>
	public static readonly IReadOnlyList<string> TrustedHosts = new[]
	{
		"cdn.modrinth.com", "github.com", "raw.githubusercontent.com", "gitlab.com"
	};

	/// <summary>Loaders a pack might name that the manager cannot run, with the name to say.</summary>
	private static readonly IReadOnlyDictionary<string, string> OtherLoaders = new Dictionary<string, string>
	{
		["forge"] = "Forge",
		["neoforge"] = "NeoForge",
		["quilt-loader"] = "Quilt"
	};

	/// <summary>Names Windows reserves for devices. A file called <c>CON.jar</c> is not a file.</summary>
	private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"CON", "PRN", "AUX", "NUL",
		"COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
		"LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
	};

	/// <summary>Where installed packs live, one folder each.</summary>
	public static string PacksFolder => Path.Combine(AppSettings.AppDataFolder, "minecraft-packs");

	// -------------------------------------------------------------------------
	// A pack as a session
	// -------------------------------------------------------------------------

	/// <summary>
	/// What every pack's install key starts with: <c>Minecraft@Pack-</c>.
	///
	/// <para>
	/// A pack is loaded as a copy of Minecraft in its own right, the way a second, GOG copy of Skyrim is — so the
	/// mod list, backups, downloads, update history and search history are all filed under the pack's key and
	/// never mix with the player's own Minecraft. Restoring a backup from a pack can then only ever put it back
	/// into that pack. Unlike a store copy, a pack is never listed among the games: it is reached through the
	/// Minecraft session, so that a pack and a game are never confused.
	/// </para>
	///
	/// <para>
	/// The key doubles as a folder name under <c>%AppData%</c> — <c>downloads\Minecraft@Pack-…</c> — which is why
	/// it is built from the pack's folder name (already safe for Windows) and uses a hyphen, not a colon.
	/// </para>
	/// </summary>
	public static readonly string InstallKeyPrefix =
		GameProfiles.Minecraft + GameProfiles.InstallKeySeparator + "Pack-";

	/// <summary>The install key a pack is loaded under.</summary>
	public static string InstallKeyFor(MinecraftPack pack) =>
		InstallKeyPrefix + Path.GetFileName(pack.Folder.TrimEnd('\\', '/'));

	/// <summary>Whether an install key is a modpack's rather than a store copy's.</summary>
	public static bool IsPackKey(string? installKey) =>
		!string.IsNullOrEmpty(installKey) &&
		installKey!.StartsWith(InstallKeyPrefix, StringComparison.Ordinal) &&
		installKey.Length > InstallKeyPrefix.Length;

	/// <summary>The pack an install key names, read from its folder under <paramref name="packsFolder"/> — or <c>null</c>.</summary>
	public static MinecraftPack? ForKey(string? installKey, string packsFolder)
	{
		if (!IsPackKey(installKey)) return null;

		string folderName = installKey!.Substring(InstallKeyPrefix.Length);
		if (SafeRelativePath(folderName) is not { } safe || safe.Contains(Path.DirectorySeparatorChar)) return null;

		return Load(Path.Combine(packsFolder, folderName));
	}

	// -------------------------------------------------------------------------
	// Reading a pack
	// -------------------------------------------------------------------------

	/// <summary>Reads a <c>.mrpack</c> file's index. Never throws for a bad pack; it says so in the result.</summary>
	public static MrpackIndex ReadIndex(string mrpackPath)
	{
		try
		{
			using ZipArchive zip = ZipFile.OpenRead(mrpackPath);
			ZipArchiveEntry? entry = zip.GetEntry(IndexFileName);
			if (entry is null) return new MrpackIndex { Problem = ModpackProblem.NotAPack };

			using var reader = new StreamReader(entry.Open());
			return ParseIndex(reader.ReadToEnd());
		}
		catch (InvalidDataException)
		{
			return new MrpackIndex { Problem = ModpackProblem.NotAPack };
		}
	}

	/// <summary>
	/// Reads <c>modrinth.index.json</c> and decides whether the pack can be installed here.
	///
	/// <para>
	/// A single unsafe path, untrusted address or missing checksum refuses the WHOLE pack rather than skipping
	/// the one file. Skipping would leave a pack that installs, starts, and is quietly missing a mod — the exact
	/// failure the rest of the Minecraft support exists to prevent — and a pack with a file like that is broken
	/// or hostile, neither of which is worth half-installing.
	/// </para>
	/// </summary>
	public static MrpackIndex ParseIndex(string json)
	{
		JObject doc;
		try { doc = JObject.Parse(json); }
		catch (JsonException) { return new MrpackIndex { Problem = ModpackProblem.NotAPack }; }

		string name = ((string?)doc["name"] ?? "").Trim();
		string summary = ((string?)doc["summary"] ?? "").Trim();
		string versionId = ((string?)doc["versionId"] ?? "").Trim();

		MrpackIndex Refuse(ModpackProblem problem, string detail = "") => new()
		{
			Name = name, Summary = summary, VersionId = versionId, Problem = problem, ProblemDetail = detail
		};

		if ((int?)doc["formatVersion"] != 1) return Refuse(ModpackProblem.UnsupportedFormat);
		if (!string.Equals((string?)doc["game"], "minecraft", StringComparison.OrdinalIgnoreCase))
			return Refuse(ModpackProblem.NotMinecraft);

		var dependencies = doc["dependencies"] as JObject ?? new JObject();
		string minecraft = ((string?)dependencies["minecraft"] ?? "").Trim();
		if (minecraft.Length == 0) return Refuse(ModpackProblem.NoMinecraftVersion);

		string fabric = ((string?)dependencies[FabricLoader] ?? "").Trim();
		if (fabric.Length == 0)
		{
			foreach (KeyValuePair<string, string> other in OtherLoaders)
				if (dependencies[other.Key] is not null) return Refuse(ModpackProblem.UnsupportedLoader, other.Value);

			return Refuse(ModpackProblem.NoLoader);
		}

		var files = new List<MrpackFile>();
		int serverOnly = 0;

		foreach (JToken file in doc["files"] as JArray ?? new JArray())
		{
			string rawPath = (string?)file["path"] ?? "";

			// A file the pack marks as not for players — a server's own admin tool — is left out, the way every
			// launcher leaves it out. It is not a refusal: the pack is doing exactly what the format asks.
			if (string.Equals((string?)file["env"]?["client"], "unsupported", StringComparison.OrdinalIgnoreCase))
			{
				serverOnly++;
				continue;
			}

			string? relative = SafeRelativePath(rawPath);
			if (relative is null) return Refuse(ModpackProblem.UnsafePath, rawPath);

			string? url = (file["downloads"] as JArray ?? new JArray())
				.Select(d => (string?)d ?? "")
				.FirstOrDefault(IsTrustedDownload);
			if (url is null) return Refuse(ModpackProblem.UntrustedDownload, rawPath);

			string sha1 = ((string?)file["hashes"]?["sha1"] ?? "").Trim().ToLowerInvariant();
			if (sha1.Length != 40) return Refuse(ModpackProblem.MissingChecksum, rawPath);

			files.Add(new MrpackFile
			{
				RelativePath = relative,
				Url = url,
				Sha1 = sha1,
				Size = (long?)file["fileSize"] ?? 0
			});
		}

		return new MrpackIndex
		{
			Name = name,
			Summary = summary,
			VersionId = versionId,
			MinecraftVersion = minecraft,
			Loader = FabricLoader,
			LoaderVersion = fabric,
			Files = files,
			ServerOnlyFiles = serverOnly
		};
	}

	/// <summary>
	/// A pack's path turned into one that is safe to write under the pack's folder, or <c>null</c> when it is not.
	///
	/// <para>
	/// Refused: an absolute path, a drive letter, <c>..</c> anywhere, an empty or <c>.</c> segment, a character
	/// Windows forbids (the colon among them, which would otherwise open an alternate data stream), a segment
	/// ending in a dot or space (Windows silently trims those, so two different names land on one file), and a
	/// device name such as <c>CON</c>. Backslashes count as separators, so none of these can be smuggled past
	/// as part of a name.
	/// </para>
	/// </summary>
	public static string? SafeRelativePath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path)) return null;
		if (path[0] is '/' or '\\') return null;

		string[] segments = path.Split('/', '\\');
		foreach (string segment in segments)
		{
			// ".." is also caught by the trailing-dot rule below — two locks, deliberately. Removing either
			// alone changes nothing a test can see (a sabotage run confirmed it), so do not remove both.
			if (segment.Length == 0 || segment is "." or "..") return null;
			if (segment.Any(WindowsFileName.IsInvalid)) return null;
			if (segment.EndsWith('.') || segment.EndsWith(' ')) return null;

			string stem = segment.Split('.')[0];
			if (ReservedNames.Contains(stem)) return null;
		}

		return Path.Combine(segments);
	}

	/// <summary>Whether a download address is https on one of the format's trusted hosts.</summary>
	public static bool IsTrustedDownload(string? url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return false;
		if (uri.Scheme != Uri.UriSchemeHttps) return false;

		return TrustedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Works out where each bundled file goes. <c>overrides/</c> first, then <c>client-overrides/</c> over it,
	/// so a file in both takes the client copy — the format's own layering. Server-only overrides are ignored.
	/// </summary>
	public static OverridePlan PlanOverrides(IEnumerable<string> entryNames)
	{
		// Case-insensitive, because Windows is: two entries differing only in case are one file here, and the
		// later layer must be the one that wins, not whichever the zip happened to list last.
		var placed = new Dictionary<string, (string Entry, string RelativePath)>(StringComparer.OrdinalIgnoreCase);
		var order = new List<string>();
		var refused = new List<string>();
		List<string> names = entryNames.ToList();

		foreach (string folder in OverrideFolders)
		{
			foreach (string entry in names)
			{
				if (!entry.StartsWith(folder, StringComparison.Ordinal)) continue;

				string inside = entry.Substring(folder.Length);
				if (inside.Length == 0 || inside.EndsWith('/')) continue;   // a folder entry, not a file

				string? relative = SafeRelativePath(inside);
				if (relative is null)
				{
					refused.Add(entry);
					continue;
				}

				if (!placed.ContainsKey(relative)) order.Add(relative);
				placed[relative] = (entry, relative);
			}
		}

		return new OverridePlan
		{
			Files = order.Select(r => placed[r]).ToList(),
			Unsafe = refused
		};
	}

	/// <summary>
	/// Copies a pack's bundled files into <paramref name="destination"/>, overwriting what is there. Returns the
	/// relative paths written.
	///
	/// Each path is checked against the destination once more as it is written. <see cref="PlanOverrides"/> has
	/// already refused anything unsafe, and this is the second lock on the same door: if the two ever disagreed,
	/// nothing leaves the folder.
	/// </summary>
	public static IReadOnlyList<string> ExtractOverrides(string mrpackPath, string destination, OverridePlan plan)
	{
		string root = Path.GetFullPath(destination);
		string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
		var written = new List<string>();

		using ZipArchive zip = ZipFile.OpenRead(mrpackPath);
		foreach ((string entryName, string relative) in plan.Files)
		{
			ZipArchiveEntry? entry = zip.GetEntry(entryName);
			if (entry is null) continue;

			string target = Path.GetFullPath(Path.Combine(root, relative));
			if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException($"The pack tried to write outside its folder: {entryName}");

			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			entry.ExtractToFile(target, overwrite: true);
			written.Add(relative);
		}

		return written;
	}

	// -------------------------------------------------------------------------
	// Installed packs
	// -------------------------------------------------------------------------

	/// <summary>
	/// A folder name for a new pack, from its name: made safe for Windows and, when a folder of that name exists,
	/// numbered — <c>Pack (2)</c>. The pack's version is deliberately left out: the folder outlives the version,
	/// and an update should not have to rename the place a player's worlds are kept.
	/// </summary>
	public static string UniqueFolderName(string packName, Func<string, bool> exists)
	{
		string stem = WindowsFileName.ToFolderName(packName);
		if (stem.Length == 0 || stem.StartsWith('.')) stem = "Modpack";

		if (!exists(stem)) return stem;

		for (int n = 2; ; n++)
		{
			string numbered = $"{stem} ({n})";
			if (!exists(numbered)) return numbered;
		}
	}

	/// <summary>The temporary folder a pack is built in before it is moved into place.</summary>
	public static string StagingFolderFor(string packsFolder, string folderName) =>
		Path.Combine(packsFolder, ".installing-" + folderName);

	/// <summary>Reads a pack's record from its folder, or <c>null</c> when there is none or it is unreadable.</summary>
	public static MinecraftPack? Load(string folder)
	{
		string path = Path.Combine(folder, ManifestFileName);
		if (!File.Exists(path)) return null;

		try
		{
			MinecraftPack? pack = JsonConvert.DeserializeObject<MinecraftPack>(File.ReadAllText(path));
			if (pack is null || pack.MinecraftVersion.Length == 0 || pack.LoaderVersion.Length == 0) return null;

			pack.Folder = folder;
			return pack;
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", $"reading the modpack record in {folder}", ex);
			return null;
		}
	}

	/// <summary>Writes a pack's record into its folder.</summary>
	public static void Save(MinecraftPack pack) =>
		File.WriteAllText(Path.Combine(pack.Folder, ManifestFileName),
			JsonConvert.SerializeObject(pack, Formatting.Indented));

	/// <summary>
	/// Every installed pack, by name. A folder without a readable record is skipped, not guessed at, and so is
	/// a half-built one: those start with a dot.
	/// </summary>
	public static IReadOnlyList<MinecraftPack> FindInstalled(string packsFolder)
	{
		if (!Directory.Exists(packsFolder)) return Array.Empty<MinecraftPack>();

		return Directory.EnumerateDirectories(packsFolder)
			.Where(d => !Path.GetFileName(d).StartsWith('.'))
			.Select(Load)
			.OfType<MinecraftPack>()
			.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
	}

	/// <summary>
	/// The installed pack that is the same pack as this one — the same Modrinth project, or failing that the same
	/// name — or <c>null</c>. Installing a pack twice is allowed, but it should never happen by accident.
	/// </summary>
	public static MinecraftPack? SamePack(IEnumerable<MinecraftPack> installed, string name, string modrinthProjectId) =>
		installed.FirstOrDefault(p =>
			(modrinthProjectId.Length > 0 && p.ModrinthProjectId == modrinthProjectId) ||
			string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));

	/// <summary>
	/// The Modrinth project a download belongs to, from its address —
	/// <c>https://cdn.modrinth.com/data/P7dR8mSH/versions/…</c> gives <c>P7dR8mSH</c> — or <c>""</c> for an address
	/// that is not Modrinth's.
	/// </summary>
	public static string ProjectFromUrl(string? url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
			!string.Equals(uri.Host, "cdn.modrinth.com", StringComparison.OrdinalIgnoreCase))
			return "";

		string[] parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
		return parts.Length >= 3 && parts[0] == "data" && parts[2] == "versions" ? parts[1] : "";
	}

	/// <summary>A path in the record's form: forward slashes, whatever this machine uses.</summary>
	public static string RecordPath(string relativePath) => relativePath.Replace('\\', '/');

	/// <summary>
	/// Works out what updating a pack to a new version will do, before anything is touched.
	///
	/// <para>
	/// The rule is Modrinth's own: everything the OLD version put in is taken out, everything the NEW version lists
	/// is put in — except <c>options.txt</c>, which is the player's settings and keys once they have played, and is
	/// never removed or overwritten. Anything the pack did not provide — a mod the player added, their worlds, their
	/// screenshots — is not the pack's, so an update never touches it.
	/// </para>
	///
	/// <para>
	/// A file already on disk with the right checksum is not downloaded again; most of a pack does not change
	/// between versions. And a mod the player switched off stays off: matched by path, or — when the new version of
	/// it has a new file name — by its Modrinth project.
	/// </para>
	/// </summary>
	/// <param name="existingSha1">The SHA-1 of a file in the pack's folder, or <c>null</c> when there is none.</param>
	public static ModpackUpdatePlan PlanUpdate(MinecraftPack installed, MrpackIndex index, OverridePlan overrides,
		Func<string, string?> existingSha1, Func<string, bool> existsDisabled)
	{
		static bool IsOptions(string recordPath) => string.Equals(recordPath, "options.txt", StringComparison.OrdinalIgnoreCase);

		var newPaths = new HashSet<string>(
			index.Files.Select(f => RecordPath(f.RelativePath)).Concat(overrides.Files.Select(o => RecordPath(o.RelativePath))),
			StringComparer.OrdinalIgnoreCase);

		// Projects the player had switched off, so the new file of the same mod can be switched off too.
		var disabledProjects = new HashSet<string>(
			installed.ProvidedFiles
				.Where(p => existsDisabled(p))
				.Select(p => installed.FileProjects.GetValueOrDefault(p, ""))
				.Where(p => p.Length > 0),
			StringComparer.OrdinalIgnoreCase);

		var download = new List<MrpackFile>();
		var keepDisabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		int unchanged = 0;

		foreach (MrpackFile file in index.Files)
		{
			string path = RecordPath(file.RelativePath);
			bool wasOff = existsDisabled(path) || disabledProjects.Contains(ProjectFromUrl(file.Url));
			if (wasOff) keepDisabled.Add(path);

			if (string.Equals(existingSha1(path), file.Sha1, StringComparison.OrdinalIgnoreCase)) unchanged++;
			else download.Add(file);
		}

		List<string> remove = installed.ProvidedFiles
			.Where(p => !newPaths.Contains(p) && !IsOptions(p))
			.ToList();

		var bundled = new OverridePlan
		{
			Files = overrides.Files.Where(o => !IsOptions(RecordPath(o.RelativePath))).ToList(),
			Unsafe = overrides.Unsafe
		};

		return new ModpackUpdatePlan
		{
			Download = download,
			Remove = remove,
			KeepDisabled = keepDisabled,
			Unchanged = unchanged,
			Bundled = bundled,
			ProvidedFiles = newPaths.Where(p => !IsOptions(p) || installed.ProvidedFiles.Contains(p, StringComparer.OrdinalIgnoreCase))
				.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList(),
			FileProjects = index.Files
				.Select(f => (Path: RecordPath(f.RelativePath), Project: ProjectFromUrl(f.Url)))
				.Where(f => f.Project.Length > 0)
				.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Project, StringComparer.OrdinalIgnoreCase)
		};
	}

	/// <summary>
	/// Mods in a pack's folder that are NOT the pack's own files but ARE one of its mods — the same Fabric mod id —
	/// and so must go when the pack's copy goes in. Returned as their paths, with whether each was switched off.
	///
	/// <para>
	/// ⚠️ How one arises: the player updates a pack mod themselves. The update replaces the pack's
	/// <c>sodium-0.6.13.jar</c> with <c>sodium-0.6.14.jar</c>, a name the pack never listed — so to the pack it looks
	/// like a mod the player added, and "what the player added stays" would keep it. Then the pack's own update puts
	/// its Sodium back beside it, and <b>Fabric refuses to start with two copies of one mod</b>. The same happens
	/// when a new pack version starts including a mod the player had already added by hand. Judged by mod id, not
	/// file name, for exactly that reason.
	/// </para>
	/// </summary>
	/// <param name="packJars">The pack's own mod jars after the update, as path → Fabric mod id.</param>
	/// <param name="otherJars">Every other mod jar in the folder, as path → Fabric mod id (paths may end <c>.disabled</c>).</param>
	public static IReadOnlyList<(string Path, bool WasDisabled)> FindSuperseded(
		IReadOnlyDictionary<string, string> packJars, IReadOnlyDictionary<string, string> otherJars, string disabledSuffix)
	{
		var packIds = new HashSet<string>(packJars.Values.Where(id => id.Length > 0), StringComparer.OrdinalIgnoreCase);

		return otherJars
			.Where(j => j.Value.Length > 0 && packIds.Contains(j.Value))
			.Select(j => (j.Key, j.Key.EndsWith(disabledSuffix, StringComparison.OrdinalIgnoreCase)))
			.OrderBy(j => j.Key, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>
	/// The accessibility mods among a set of Fabric mod ids, in the suite's own order. What decides whether a pack
	/// will speak: one with none of them starts, plays, and says nothing.
	/// </summary>
	public static IReadOnlyList<MinecraftSuiteMod> AccessModsAmong(IEnumerable<string> fabricModIds)
	{
		var ids = new HashSet<string>(fabricModIds, StringComparer.OrdinalIgnoreCase);
		return MinecraftSuite.AccessMods.Where(m => ids.Contains(m.FabricModId)).ToList();
	}
}
