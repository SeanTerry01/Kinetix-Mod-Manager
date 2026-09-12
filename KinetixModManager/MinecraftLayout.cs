using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// Which launcher a copy of Minecraft was installed by. This matters only for telling the user where their
/// copy came from and for finding the bundled Java runtime; the mods folder is in the same place either way.
/// </summary>
public enum MinecraftLauncherKind
{
	/// <summary>No launcher was found, though a <c>.minecraft</c> folder may still exist.</summary>
	Unknown,

	/// <summary>The Microsoft Store package <c>Microsoft.MinecraftJavaEdition_8wekyb3d8bbwe</c>.</summary>
	MicrosoftStore,

	/// <summary>The standalone <c>MinecraftLauncher.exe</c> installer.</summary>
	Standalone
}

/// <summary>One mod's metadata, read out of the <c>fabric.mod.json</c> inside its jar.</summary>
public sealed class FabricModInfo
{
	/// <summary>The mod's stable id, e.g. <c>united_minecraft</c>. Empty when the jar had no usable metadata.</summary>
	public string Id { get; init; } = "";

	/// <summary>The mod's display name, falling back to <see cref="Id"/> and then to the file name.</summary>
	public string Name { get; init; } = "";

	public string Version { get; init; } = "";
	public string Description { get; init; } = "";

	/// <summary>Authors, flattened from either plain strings or objects carrying a <c>name</c>.</summary>
	public IReadOnlyList<string> Authors { get; init; } = Array.Empty<string>();

	/// <summary>The mod's homepage or sources URL, whichever it gave, or <c>""</c>.</summary>
	public string HomepageUrl { get; init; } = "";

	/// <summary>
	/// Hard dependencies as id → version range, straight out of <c>depends</c>. Includes the pseudo-ids Fabric
	/// itself uses — <c>fabricloader</c>, <c>minecraft</c> and <c>java</c> — because a mod that will not load
	/// for want of a newer loader is exactly as broken as one missing a real dependency, and the user deserves
	/// to be told which.
	/// </summary>
	public IReadOnlyDictionary<string, string> Depends { get; init; } = new Dictionary<string, string>();

	/// <summary>
	/// Ids of mods packaged INSIDE this jar (Fabric's "jar-in-jar"), read from the <c>jars</c> list.
	///
	/// This is what makes the two Minecraft accessibility mods behave so differently, and why the manager can
	/// hide the difference: Minecraft Access ships Fabric API, Cloth Config and Balm nested inside its own jar,
	/// so installing it is one file; United Minecraft declares Fabric API as an ordinary dependency, so it needs
	/// two. Anything listed here is already satisfied and must not be reported missing.
	/// </summary>
	public IReadOnlyList<string> NestedJars { get; init; } = Array.Empty<string>();

	/// <summary>True when the jar carried no <c>fabric.mod.json</c> — not a Fabric mod, or a corrupt download.</summary>
	public bool IsUnreadable { get; init; }
}

/// <summary>
/// Everything specific to how Minecraft (Java Edition) keeps itself on disk.
///
/// Minecraft is the first supported game that is not a store install: there is no Steam app id, no GOG product
/// and no install folder to detect. What there is instead is <c>%APPDATA%\.minecraft</c>, which holds the mods,
/// the config, the saves, the logs and the launcher's own profile list — so that folder plays the part the game
/// folder plays for every other game, and <c>GameProfile.ModsFolderRelativeToGame</c> resolves against it
/// exactly as it does elsewhere.
/// </summary>
public static class MinecraftLayout
{
	/// <summary>The Microsoft Store package family name for Java Edition's launcher.</summary>
	public const string StorePackageFamilyName = "Microsoft.MinecraftJavaEdition_8wekyb3d8bbwe";

	/// <summary>
	/// The application user model id used to start the Store launcher, for the days when we still hand the
	/// user back to it. Note there is no <c>minecraft://</c> protocol and the package's execution alias is a
	/// placeholder called <c>MCPlaceholderStub.exe</c>, so this is the only way in from outside.
	/// </summary>
	public const string StoreAppUserModelId = StorePackageFamilyName + "!Game";

	/// <summary>The file inside a mod jar that carries its metadata.</summary>
	public const string ManifestEntryName = "fabric.mod.json";

	/// <summary>The extension a loadable mod carries. Fabric accepts nothing else.</summary>
	public const string ModExtension = ".jar";

	/// <summary>Where Minecraft keeps everything, unless the user has moved it.</summary>
	public static string DefaultRootFolder =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

	/// <summary>The mods folder for a given <c>.minecraft</c> root.</summary>
	public static string ModsFolderFor(string root) => Path.Combine(root, "mods");

	/// <summary>The config folder, which only exists once at least one mod has actually loaded.</summary>
	public static string ConfigFolderFor(string root) => Path.Combine(root, "config");

	/// <summary>The launcher's installation list.</summary>
	public static string LauncherProfilesPathFor(string root) => Path.Combine(root, "launcher_profiles.json");

	/// <summary>The current session's log — the only reliable answer to "did the mods actually load?".</summary>
	public static string LatestLogPathFor(string root) => Path.Combine(root, "logs", "latest.log");

	// -------------------------------------------------------------------------
	// Finding the install
	// -------------------------------------------------------------------------

	/// <summary>
	/// The <c>.minecraft</c> folder, or <c>""</c> when there is not one.
	///
	/// Both the Microsoft Store launcher and the standalone one use the same folder, so this is one check
	/// rather than a per-launcher search. A folder counts as real only when it holds a
	/// <c>launcher_profiles.json</c> or a <c>versions</c> folder — an empty <c>.minecraft</c> left behind by an
	/// uninstall would otherwise be reported as a working copy.
	/// </summary>
	public static string FindRootFolder()
	{
		string def = DefaultRootFolder;
		return LooksLikeMinecraftRoot(def) ? def : "";
	}

	/// <summary>True when <paramref name="folder"/> holds something recognisably Minecraft.</summary>
	public static bool LooksLikeMinecraftRoot(string folder)
	{
		if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;
		return File.Exists(LauncherProfilesPathFor(folder)) ||
			   Directory.Exists(Path.Combine(folder, "versions"));
	}

	/// <summary>
	/// Which launcher installed this copy. Only used for what the manager tells the user and for locating the
	/// bundled Java runtime — never for finding the mods folder, which does not vary.
	/// </summary>
	public static MinecraftLauncherKind DetectLauncher()
	{
		string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (Directory.Exists(Path.Combine(local, "Packages", StorePackageFamilyName)))
			return MinecraftLauncherKind.MicrosoftStore;

		foreach (Environment.SpecialFolder pf in new[]
				 { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
		{
			string exe = Path.Combine(Environment.GetFolderPath(pf), "Minecraft Launcher", "MinecraftLauncher.exe");
			if (File.Exists(exe)) return MinecraftLauncherKind.Standalone;
		}

		return MinecraftLauncherKind.Unknown;
	}

	/// <summary>The standalone launcher's executable, or <c>""</c> when this is not a standalone install.</summary>
	public static string FindStandaloneLauncherExe()
	{
		foreach (Environment.SpecialFolder pf in new[]
				 { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
		{
			string exe = Path.Combine(Environment.GetFolderPath(pf), "Minecraft Launcher", "MinecraftLauncher.exe");
			if (File.Exists(exe)) return exe;
		}
		return "";
	}

	// -------------------------------------------------------------------------
	// Mod files
	// -------------------------------------------------------------------------

	/// <summary>
	/// True when this file is a mod the manager should list, enabled or not — i.e. it ends in <c>.jar</c>, or
	/// in <c>.jar</c> plus the disabling suffix.
	/// </summary>
	public static bool IsModFile(string path, string? disabledSuffix)
	{
		string name = Path.GetFileName(path);
		if (name.EndsWith(ModExtension, StringComparison.OrdinalIgnoreCase)) return true;

		return !string.IsNullOrEmpty(disabledSuffix) &&
			   name.EndsWith(ModExtension + disabledSuffix, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>True when this mod file is one Fabric will actually load.</summary>
	public static bool IsEnabledModFile(string path) =>
		Path.GetFileName(path).EndsWith(ModExtension, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// The path this mod file would have when enabled or disabled, leaving it alone when it is already that way.
	/// Renaming is the whole mechanism — nothing is moved and nothing is rewritten.
	/// </summary>
	public static string PathWithEnabled(string path, bool enable, string? disabledSuffix)
	{
		string suffix = disabledSuffix ?? "";
		if (suffix.Length == 0) return path;

		bool currentlyEnabled = IsEnabledModFile(path);
		if (currentlyEnabled == enable) return path;

		return enable
			? path.Substring(0, path.Length - suffix.Length)   // strip ".disabled"
			: path + suffix;
	}

	// -------------------------------------------------------------------------
	// fabric.mod.json
	// -------------------------------------------------------------------------

	/// <summary>
	/// The metadata inside <paramref name="jarPath"/>, or an entry marked
	/// <see cref="FabricModInfo.IsUnreadable"/> when the jar has no <c>fabric.mod.json</c> or cannot be opened.
	///
	/// A jar is a zip, so this is a zip read — no Java, no extraction, and the file is never modified. An
	/// unreadable jar is reported rather than thrown on: a half-finished download in the mods folder should
	/// show up in the list as a broken mod the user can delete, not take the whole scan down with it.
	/// </summary>
	public static FabricModInfo ReadModInfo(string jarPath)
	{
		string fallbackName = Path.GetFileName(jarPath);

		try
		{
			using ZipArchive zip = ZipFile.OpenRead(jarPath);
			ZipArchiveEntry? entry = zip.GetEntry(ManifestEntryName);
			if (entry is null)
				return new FabricModInfo { Name = fallbackName, IsUnreadable = true };

			using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
			return ParseModInfo(reader.ReadToEnd(), fallbackName);
		}
		catch (Exception)
		{
			// Corrupt zip, locked file, truncated download — all the same answer to the caller.
			return new FabricModInfo { Name = fallbackName, IsUnreadable = true };
		}
	}

	/// <summary>
	/// <see cref="ReadModInfo"/>'s parsing half, split out so it can be tested against manifest text without
	/// building a jar first.
	/// </summary>
	public static FabricModInfo ParseModInfo(string manifestJson, string fallbackName)
	{
		JObject o;
		try
		{
			o = JObject.Parse(manifestJson);
		}
		catch (Exception)
		{
			return new FabricModInfo { Name = fallbackName, IsUnreadable = true };
		}

		string id = (string?)o["id"] ?? "";

		// "authors" is either ["Some Name"] or [{"name": "Some Name", "contact": {...}}]. Both are legal and
		// both appear in the wild, so handle each rather than picking one and rendering the other as "{}".
		var authors = new List<string>();
		if (o["authors"] is JArray authorArray)
		{
			foreach (JToken a in authorArray)
			{
				string? name = a.Type == JTokenType.Object ? (string?)a["name"] : (string?)a;
				if (!string.IsNullOrWhiteSpace(name)) authors.Add(name!);
			}
		}

		string homepage = (string?)o["contact"]?["homepage"] ?? (string?)o["contact"]?["sources"] ?? "";

		var depends = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (o["depends"] is JObject dependsObj)
		{
			foreach (JProperty p in dependsObj.Properties())
			{
				// A range is normally a string ("&gt;=0.19.3") but may be an array of alternatives.
				string range = p.Value is JArray alternatives
					? string.Join(" or ", alternatives.Select(v => (string?)v).Where(v => !string.IsNullOrEmpty(v)))
					: (string?)p.Value ?? "*";
				depends[p.Name] = range;
			}
		}

		// Nested jars are listed by file path, e.g. "META-INF/jars/fabric-api-base-2.0.4+ece063239e.jar". The
		// mod id is not stated, so derive it from the file name by stripping the version, which by Fabric
		// convention follows the last hyphen that precedes a digit.
		var nested = new List<string>();
		if (o["jars"] is JArray jarsArray)
		{
			foreach (JToken j in jarsArray)
			{
				string file = (string?)j?["file"] ?? "";
				string nestedId = NestedJarIdFromFileName(file);
				if (nestedId.Length > 0) nested.Add(nestedId);
			}
		}

		return new FabricModInfo
		{
			Id          = id,
			Name        = (string?)o["name"] ?? (id.Length > 0 ? id : fallbackName),
			Version     = (string?)o["version"] ?? "",
			Description = (string?)o["description"] ?? "",
			Authors     = authors,
			HomepageUrl = homepage,
			Depends     = depends,
			NestedJars  = nested
		};
	}

	/// <summary>
	/// The mod id inside a nested jar's file name — <c>META-INF/jars/fabric-api-base-2.0.4+ece0.jar</c> gives
	/// <c>fabric-api-base</c>.
	///
	/// Fabric names these <c>&lt;id&gt;-&lt;version&gt;.jar</c>, and an id may itself contain hyphens, so the
	/// split is at the last hyphen whose next character is a digit. Approximate by nature — it is used to avoid
	/// reporting a bundled dependency as missing, so erring toward "recognised" is the safe direction.
	/// </summary>
	public static string NestedJarIdFromFileName(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath)) return "";

		string name = Path.GetFileNameWithoutExtension(filePath.Replace('/', Path.DirectorySeparatorChar));
		if (name.Length == 0) return "";

		for (int i = name.Length - 2; i > 0; i--)
		{
			if (name[i] == '-' && char.IsDigit(name[i + 1]))
				return name.Substring(0, i);
		}

		return name;
	}
}
