using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// Who the game is told it is. Identity is <em>not</em> cosmetic: Minecraft keys a character to its uuid, and
/// a world stores that player's inventory, position, advancements and statistics under it.
/// </summary>
public sealed class MinecraftIdentity
{
	public required string Username { get; init; }

	/// <summary>Dashed uuid, e.g. <c>7b05bd63-2994-4bcb-97b1-79772ffcc404</c>.</summary>
	public required string Uuid { get; init; }

	/// <summary>
	/// The session token, or <c>"0"</c> for an offline launch.
	///
	/// Singleplayer does not check it. Multiplayer, Realms and the skin service do, which is the whole of the
	/// difference between the two modes.
	/// </summary>
	public required string AccessToken { get; init; }

	/// <summary><c>msa</c> for a real signed-in session, <c>legacy</c> offline.</summary>
	public required string UserType { get; init; }

	/// <summary>True when this identity carries no real session and so cannot reach servers or Realms.</summary>
	public bool IsOffline => AccessToken is "0" or "";

	/// <summary>
	/// An offline identity carrying the player's REAL uuid and name, read from the launcher's accounts file.
	///
	/// ⚠️ The real uuid is the point. The usual offline trick is to hash <c>OfflinePlayer:&lt;name&gt;</c> into a
	/// version 3 uuid, which produces a DIFFERENT player — so loading an existing world would start a brand new
	/// character in it: empty inventory, back at spawn, no advancements, and the old character orphaned on disk
	/// rather than deleted. That looks exactly like the manager ate the save. Passing the real uuid makes an
	/// offline session byte-for-byte the same player as an online one.
	///
	/// The accounts file holds no credential — <c>accessToken</c>, <c>azureToken</c> and
	/// <c>mojangClientToken</c> are all empty strings there, the real one being encrypted elsewhere — so this
	/// reads public profile data only. Returns <c>null</c> when there is no account to read.
	/// </summary>
	public static MinecraftIdentity? OfflineFromLauncher(string root)
	{
		foreach (string fileName in new[]
				 { "launcher_accounts_microsoft_store.json", "launcher_accounts.json" })
		{
			string path = Path.Combine(root, fileName);
			if (!File.Exists(path)) continue;

			try
			{
				JObject doc = JObject.Parse(File.ReadAllText(path));
				string? activeId = (string?)doc["activeAccountLocalId"];
				if (doc["accounts"] is not JObject accounts) continue;

				JToken? account = activeId is not null ? accounts[activeId] : accounts.Properties().FirstOrDefault()?.Value;
				JToken? profile = account?["minecraftProfile"];

				string? rawUuid = (string?)profile?["id"];
				string? name = (string?)profile?["name"];
				if (string.IsNullOrEmpty(rawUuid) || string.IsNullOrEmpty(name)) continue;

				return new MinecraftIdentity
				{
					Username    = name!,
					Uuid        = FormatUuid(rawUuid!),
					AccessToken = "0",
					UserType    = "legacy"
				};
			}
			catch (Exception ex)
			{
				DiagnosticLog.WriteException("Minecraft", $"reading the player's profile from {fileName}", ex);
			}
		}

		return null;
	}

	/// <summary>Inserts the dashes Minecraft expects into a bare 32-character uuid.</summary>
	public static string FormatUuid(string raw)
	{
		string bare = raw.Replace("-", "");
		if (bare.Length != 32) return raw;

		return string.Join("-", bare.Substring(0, 8), bare.Substring(8, 4), bare.Substring(12, 4),
			bare.Substring(16, 4), bare.Substring(20, 12));
	}
}

/// <summary>Everything needed to start the game, worked out but not yet run.</summary>
public sealed class MinecraftLaunchPlan
{
	public required string JavaPath { get; init; }
	public required IReadOnlyList<string> Arguments { get; init; }
	public required string WorkingDirectory { get; init; }
	public required string VersionId { get; init; }
}

/// <summary>
/// Starts Minecraft directly, without the launcher.
///
/// <para>
/// This exists because the launcher is where Minecraft's accessibility problem actually lives. Selecting a mod
/// loader installation and launching a world are two separate pieces of launcher state, the second silently
/// overrides the first, and nothing reports it when it goes wrong — the game starts, plays normally, and never
/// speaks. Building the java command ourselves removes the whole class of failure.
/// </para>
///
/// <para>
/// The command is reconstructed from the version JSONs the same way the launcher builds it, and was verified
/// end to end: Fabric loaded, the accessibility mod loaded, its speech backend picked up NVDA, a world loaded
/// and saved, and the player's own character came through untouched.
/// </para>
/// </summary>
public static class MinecraftLauncher
{
	/// <summary>What the game is told started it. Mirrors the launcher's own two properties.</summary>
	private const string LauncherBrand = "kinetix-mod-manager";

	/// <summary>
	/// The natives folder the manager owns.
	///
	/// ⚠️ Deliberately not the launcher's. It extracts natives into <c>bin\&lt;sha1&gt;\</c> and DELETES that
	/// folder when the game exits, so anything reusing it works on a machine where the launcher happened to
	/// leave one behind and fails mysteriously everywhere else.
	/// </summary>
	public static string NativesDirectoryFor(string root) => Path.Combine(root, "bin", "kinetix");

	// -------------------------------------------------------------------------
	// Version JSONs
	// -------------------------------------------------------------------------

	/// <summary>Reads one version's JSON, or <c>null</c> when it isn't installed.</summary>
	public static JObject? ReadVersionJson(string root, string versionId)
	{
		string path = Path.Combine(root, "versions", versionId, versionId + ".json");
		if (!File.Exists(path)) return null;

		try { return JObject.Parse(File.ReadAllText(path)); }
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", $"reading the version file for {versionId}", ex);
			return null;
		}
	}

	/// <summary>
	/// Merges a version onto the one it inherits from — Fabric's profile onto vanilla's.
	///
	/// The child's libraries come FIRST, which is not arbitrary: Fabric's loader and mixin have to precede the
	/// game on the classpath or the game class loads before anything can transform it. The launcher's own
	/// command line puts them first too.
	/// </summary>
	public static JObject Merge(JObject child, JObject parent)
	{
		var merged = (JObject)parent.DeepClone();

		foreach (JProperty p in child.Properties())
		{
			if (p.Name is "libraries" or "arguments" or "inheritsFrom") continue;
			merged[p.Name] = p.Value.DeepClone();
		}

		var libraries = new JArray();
		foreach (JToken l in child["libraries"] as JArray ?? new JArray()) libraries.Add(l.DeepClone());
		foreach (JToken l in parent["libraries"] as JArray ?? new JArray()) libraries.Add(l.DeepClone());
		merged["libraries"] = libraries;

		// Argument lists append rather than replace: Fabric adds -DFabricMcEmu and expects the game's own
		// arguments to still be there.
		var arguments = new JObject();
		foreach (string section in new[] { "jvm", "game" })
		{
			var combined = new JArray();
			foreach (JToken a in parent["arguments"]?[section] as JArray ?? new JArray()) combined.Add(a.DeepClone());
			foreach (JToken a in child["arguments"]?[section] as JArray ?? new JArray()) combined.Add(a.DeepClone());
			arguments[section] = combined;
		}
		merged["arguments"] = arguments;

		return merged;
	}

	/// <summary>Resolves a version and everything it inherits from into one document.</summary>
	public static JObject? ResolveVersion(string root, string versionId, int depth = 0)
	{
		if (depth > 8) return null;   // a cycle in inheritsFrom, which would otherwise hang

		JObject? version = ReadVersionJson(root, versionId);
		if (version is null) return null;

		string? parentId = (string?)version["inheritsFrom"];
		if (string.IsNullOrEmpty(parentId)) return version;

		JObject? parent = ResolveVersion(root, parentId!, depth + 1);
		return parent is null ? version : Merge(version, parent);
	}

	// -------------------------------------------------------------------------
	// Rules
	// -------------------------------------------------------------------------

	/// <summary>
	/// Whether a rules array allows this entry on the running machine.
	///
	/// No rules means allowed. Otherwise the last matching rule wins, which is how Mojang's launcher reads
	/// them. Feature rules (<c>is_demo_user</c>, <c>has_custom_resolution</c>, quick play) are treated as not
	/// present, because the manager sets none of those features.
	/// </summary>
	public static bool RulesAllow(JToken? rules, string osName, string osArch)
	{
		if (rules is not JArray array || array.Count == 0) return true;

		bool allowed = false;
		foreach (JToken rule in array)
		{
			// A rule gated on a feature the manager never turns on can only ever have denied things.
			if (rule["features"] is JObject) continue;

			bool matches = true;
			if (rule["os"] is JObject os)
			{
				string? wantName = (string?)os["name"];
				string? wantArch = (string?)os["arch"];

				if (!string.IsNullOrEmpty(wantName) &&
					!string.Equals(wantName, osName, StringComparison.OrdinalIgnoreCase))
					matches = false;

				if (!string.IsNullOrEmpty(wantArch) && !ArchMatches(wantArch!, osArch))
					matches = false;
			}

			if (matches) allowed = (string?)rule["action"] == "allow";
		}

		return allowed;
	}

	/// <summary>
	/// Whether an architecture rule applies here.
	///
	/// ⚠️ <c>"arch": "x86"</c> means the x86 FAMILY, not 32-bit only. Minecraft's own version file gates
	/// <c>-Xss1M</c> on it, and the real launcher passes that flag on a 64-bit machine — confirmed by reading
	/// the command line it built. Treating x86 as 32-bit-only would silently drop a flag the game expects.
	/// </summary>
	public static bool ArchMatches(string wanted, string actual) =>
		string.Equals(wanted, actual, StringComparison.OrdinalIgnoreCase) ||
		(wanted.Equals("x86", StringComparison.OrdinalIgnoreCase) &&
		 actual.Equals("x86_64", StringComparison.OrdinalIgnoreCase));

	/// <summary>The OS name Mojang's version files use for the running machine.</summary>
	public static string CurrentOsName =>
		RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
		: RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
		: "linux";

	/// <summary>The architecture name Mojang's version files use for the running machine.</summary>
	public static string CurrentOsArch =>
		RuntimeInformation.OSArchitecture switch
		{
			Architecture.X86 => "x86",
			Architecture.X64 => "x86_64",
			Architecture.Arm64 => "arm64",
			_ => "x86_64"
		};

	// -------------------------------------------------------------------------
	// Classpath
	// -------------------------------------------------------------------------

	/// <summary>
	/// The path a maven coordinate resolves to under <c>libraries\</c> —
	/// <c>net.fabricmc:fabric-loader:0.19.5</c> gives
	/// <c>net\fabricmc\fabric-loader\0.19.5\fabric-loader-0.19.5.jar</c>.
	///
	/// Needed because Fabric's libraries name themselves this way and give no download path, while the game's
	/// own libraries carry an explicit one.
	/// </summary>
	public static string MavenToRelativePath(string coordinate)
	{
		string[] parts = coordinate.Split(':');
		if (parts.Length < 3) return "";

		string group = parts[0].Replace('.', Path.DirectorySeparatorChar);
		string artifact = parts[1];
		string version = parts[2];
		string classifier = parts.Length > 3 ? "-" + parts[3] : "";

		return Path.Combine(group, artifact, version, $"{artifact}-{version}{classifier}.jar");
	}

	/// <summary>
	/// Which library an entry is, whatever its version: <c>group:artifact</c>, plus the classifier when it has
	/// one — so <c>org.ow2.asm:asm:9.6</c> and <c>org.ow2.asm:asm:9.9</c> are the same library, while
	/// <c>org.lwjgl:lwjgl:3.3.3</c> and its <c>natives-windows</c> jar are two. Falls back to the path for an
	/// entry with no usable name.
	/// </summary>
	public static string LibraryIdentity(string? coordinate, string relativePath)
	{
		string[] parts = (coordinate ?? "").Split(':');
		if (parts.Length < 3) return relativePath;

		return parts.Length > 3 ? $"{parts[0]}:{parts[1]}:{parts[3]}" : $"{parts[0]}:{parts[1]}";
	}

	/// <summary>
	/// Every classpath entry for a resolved version, in order, as paths relative to <c>libraries\</c> — plus
	/// the game jar last, which the caller supplies since it lives under <c>versions\</c>.
	///
	/// <para>
	/// ⚠️ A library named twice is taken ONCE, the first time — and since <see cref="Merge"/> puts Fabric's
	/// libraries first, that is Fabric's copy. The official launcher does the same. Taking both was invisible on
	/// Minecraft 26.x, which ships no ASM of its own, and fatal on 1.21.x, which ships ASM 9.6 while Fabric
	/// brings 9.9: Fabric checks for exactly this and refuses to start with "duplicate ASM classes found on
	/// classpath". The player hears the game start and, three seconds later, close.
	/// </para>
	/// </summary>
	public static IReadOnlyList<string> LibraryPaths(JObject resolved, string osName, string osArch)
	{
		var paths = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (JToken library in resolved["libraries"] as JArray ?? new JArray())
		{
			if (!RulesAllow(library["rules"], osName, osArch)) continue;

			string relative = (string?)library["downloads"]?["artifact"]?["path"] is { Length: > 0 } explicitPath
				? explicitPath.Replace('/', Path.DirectorySeparatorChar)
				: MavenToRelativePath((string?)library["name"] ?? "");

			if (relative.Length > 0 && seen.Add(LibraryIdentity((string?)library["name"], relative))) paths.Add(relative);
		}

		return paths;
	}

	/// <summary>One file the game needs before it can start, and where to get it.</summary>
	/// <param name="RelativePath">Where it belongs under <c>libraries\</c>.</param>
	/// <param name="Url">Where to fetch it.</param>
	/// <param name="Sha1">Its checksum, where the version file gave one.</param>
	/// <param name="Bytes">Its size, for the progress announcement; 0 when unknown.</param>
	public readonly record struct MissingLibrary(string RelativePath, string Url, string? Sha1, long Bytes);

	/// <summary>
	/// The libraries this version needs that are not on disk.
	///
	/// <para>
	/// The manager starts Minecraft itself rather than through the official launcher, and until now it assumed
	/// every file the game needs was already there — which is true only because the official launcher had fetched
	/// them at some point. On a Minecraft version it had never started, it was not true: Sean's move to 26.3 left
	/// exactly one library missing, <c>jtracy</c>, and the game died on a missing class with no mention of a file.
	/// The official launcher repairs this silently every time it plays; a launcher that does not is a launcher that
	/// works until the day the game updates.
	/// </para>
	///
	/// <para>
	/// Two spellings have to be understood. The game's own libraries carry a <c>downloads.artifact</c> with an
	/// explicit path, url and checksum. Fabric's carry a maven coordinate and the repository to take it from, and
	/// the path is worked out from the coordinate — which is why <see cref="MavenToRelativePath"/> exists.
	/// </para>
	/// </summary>
	public static IReadOnlyList<MissingLibrary> MissingLibraries(
		JObject resolved, string librariesRoot, string osName, string osArch, Func<string, bool> exists)
	{
		var missing = new List<MissingLibrary>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (JToken library in resolved["libraries"] as JArray ?? new JArray())
		{
			if (!RulesAllow(library["rules"], osName, osArch)) continue;

			JToken? artifact = library["downloads"]?["artifact"];
			string relative = (string?)artifact?["path"] is { Length: > 0 } explicitPath
				? explicitPath.Replace('/', Path.DirectorySeparatorChar)
				: MavenToRelativePath((string?)library["name"] ?? "");
			// The same rule as the classpath: a library the classpath will not use is not worth fetching.
			if (relative.Length == 0 || !seen.Add(LibraryIdentity((string?)library["name"], relative))) continue;

			if (exists(Path.Combine(librariesRoot, relative))) continue;

			string url = (string?)artifact?["url"] ?? MavenUrl((string?)library["url"], (string?)library["name"] ?? "");
			if (url.Length == 0) continue;   // nowhere to get it from; the caller reports the gap rather than guessing

			missing.Add(new MissingLibrary(relative, url,
				(string?)artifact?["sha1"] ?? (string?)library["sha1"],
				(long?)artifact?["size"] ?? (long?)library["size"] ?? 0));
		}

		return missing;
	}

	/// <summary>The URL a maven coordinate resolves to under a repository, or "" without one.</summary>
	public static string MavenUrl(string? repository, string coordinate)
	{
		if (string.IsNullOrWhiteSpace(repository) || coordinate.Length == 0) return "";

		string relative = MavenToRelativePath(coordinate);
		if (relative.Length == 0) return "";

		return repository.TrimEnd('/') + "/" + relative.Replace(Path.DirectorySeparatorChar, '/');
	}

	// -------------------------------------------------------------------------
	// Arguments
	// -------------------------------------------------------------------------

	/// <summary>Expands <c>${name}</c> placeholders, leaving unknown ones alone rather than blanking them.</summary>
	public static string Substitute(string template, IReadOnlyDictionary<string, string> values)
	{
		foreach (KeyValuePair<string, string> pair in values)
			template = template.Replace("${" + pair.Key + "}", pair.Value);

		return template;
	}

	/// <summary>
	/// Flattens one of the version file's argument lists into plain strings, dropping anything this machine's
	/// rules disallow and expanding the placeholders.
	/// </summary>
	public static List<string> BuildArguments(
		JToken? section, IReadOnlyDictionary<string, string> values, string osName, string osArch)
	{
		var result = new List<string>();

		foreach (JToken entry in section as JArray ?? new JArray())
		{
			if (entry.Type == JTokenType.String)
			{
				result.Add(Substitute((string?)entry ?? "", values));
				continue;
			}

			if (!RulesAllow(entry["rules"], osName, osArch)) continue;

			JToken? value = entry["value"];
			if (value is JArray many)
				foreach (JToken v in many) result.Add(Substitute((string?)v ?? "", values));
			else if (value is not null)
				result.Add(Substitute((string?)value ?? "", values));
		}

		return result;
	}

	// -------------------------------------------------------------------------
	// Java
	// -------------------------------------------------------------------------

	/// <summary>
	/// The Java the launcher bundles for a given runtime component, or <c>""</c> when it isn't there.
	///
	/// Looked up rather than hard-coded: the version file names the component it needs
	/// (<c>javaVersion.component</c>), and the Microsoft Store and standalone launchers keep their runtimes in
	/// different places. Falling back to a system Java would be wrong more often than right — Minecraft 26.2
	/// needs Java 25, which almost nobody has installed separately.
	///
	/// <para>
	/// With a <paramref name="root"/> it also looks under <c>&lt;root&gt;\runtime</c>, where the manager puts a
	/// runtime it fetched itself. Last, so that a runtime the launcher installed and keeps up to date is
	/// preferred to a copy of ours — and so that an empty answer still means what it has always meant: nobody
	/// on this machine has this component. See <see cref="MinecraftGameFiles.RuntimeRootFor"/>.
	/// </para>
	/// </summary>
	public static string FindBundledJava(string component, string root = "")
	{
		string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

		var roots = new List<string>
		{
			// Microsoft Store launcher.
			Path.Combine(local, "Packages", MinecraftLayout.StorePackageFamilyName,
				"LocalCache", "Local", "runtime"),
			// Standalone launcher.
			Path.Combine(local, "Packages", "Microsoft.4297127D64EC6_8wekyb3d8bbwe",
				"LocalCache", "Local", "runtime"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
				"Minecraft Launcher", "runtime")
		};

		if (root.Length > 0) roots.Add(Path.Combine(root, "runtime"));

		foreach (string runtimeRoot in roots)
		{
			if (!Directory.Exists(runtimeRoot)) continue;

			string componentRoot = Path.Combine(runtimeRoot, component);
			if (!Directory.Exists(componentRoot)) continue;

			// runtime\<component>\<platform>\<component>\bin\javaw.exe
			foreach (string candidate in Directory.EnumerateFiles(componentRoot, "javaw.exe", SearchOption.AllDirectories))
				return candidate;
		}

		return "";
	}

	// -------------------------------------------------------------------------
	// The plan
	// -------------------------------------------------------------------------

	/// <summary>
	/// Works out how to start <paramref name="versionId"/>, or throws with a sentence explaining what is
	/// missing. Nothing is launched here.
	/// </summary>
	/// <param name="maxMemoryMb">The most memory Java may take, in megabytes, as <c>-Xmx</c>; 0 leaves it to Java. See AppSettings.MinecraftMaxMemoryMb.</param>
	/// <param name="gameDirectory">
	/// Where the game keeps its mods, config, saves and logs, when that is not <paramref name="root"/> — a
	/// modpack's own folder. Everything shared between setups (versions, libraries, assets, Java, natives) still
	/// comes from <paramref name="root"/>, so a pack costs its own mods and nothing more.
	///
	/// ⚠️ It is the process's working directory as well as <c>--gameDir</c>, and that is not tidiness. Packs
	/// built for blind players put <c>Tolk.dll</c> and <c>nvdaControllerClient64.dll</c> at the top of their
	/// folder, and the accessibility mod loads them from the working directory — started anywhere else, the game
	/// runs perfectly and never speaks.
	/// </param>
	public static MinecraftLaunchPlan BuildPlan(
		string root, string versionId, MinecraftIdentity identity, string? quickPlaySingleplayerWorld = null,
		string? gameDirectory = null, int maxMemoryMb = 0)
	{
		string gameDir = string.IsNullOrEmpty(gameDirectory) ? root : gameDirectory!;

		JObject resolved = ResolveVersion(root, versionId)
			?? throw new InvalidOperationException(
				$"Minecraft version '{versionId}' is not installed under {root}.");

		string? inheritsFrom = (string?)ReadVersionJson(root, versionId)?["inheritsFrom"];

		string osName = CurrentOsName;
		string osArch = CurrentOsArch;

		string natives = NativesDirectoryFor(root);
		Directory.CreateDirectory(natives);
		foreach (string sub in new[] { "java", "jna", "lwjgl", "netty" })
			Directory.CreateDirectory(Path.Combine(natives, sub));

		string librariesRoot = Path.Combine(root, "libraries");
		var classpath = LibraryPaths(resolved, osName, osArch)
			.Select(p => Path.Combine(librariesRoot, p))
			.ToList();
		classpath.Add(GameJarPath(root, versionId, inheritsFrom));

		string component = (string?)resolved["javaVersion"]?["component"] ?? "java-runtime-delta";
		string java = FindBundledJava(component, root);
		if (java.Length == 0)
			throw new InvalidOperationException(
				$"Could not find the Java runtime '{component}' that Minecraft {versionId} needs. " +
				"Starting the game once through the Minecraft launcher will download it.");

		var values = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["auth_player_name"]  = identity.Username,
			["auth_uuid"]         = identity.Uuid,
			["auth_access_token"] = identity.AccessToken,
			["user_type"]         = identity.UserType,
			["version_name"]      = versionId,
			["game_directory"]    = gameDir,
			["assets_root"]       = Path.Combine(root, "assets"),
			["assets_index_name"] = (string?)resolved["assetIndex"]?["id"] ?? (string?)resolved["assets"] ?? "",
			["version_type"]      = (string?)resolved["type"] ?? "release",
			["natives_directory"] = natives,
			["launcher_name"]     = LauncherBrand,
			["launcher_version"]  = System.Reflection.Assembly.GetExecutingAssembly()
										.GetName().Version?.ToString(3) ?? "1.0.1",
			["classpath"]         = string.Join(Path.PathSeparator.ToString(), classpath),
			// Not carried by an offline session. Left empty rather than absent so the placeholder does not
			// survive into the command line as literal "${clientid}".
			["clientid"]          = "",
			["auth_xuid"]         = ""
		};

		var args = new List<string>();
		// The memory ceiling comes first, and only when the version file did not set one itself. See
		// AppSettings.MinecraftMaxMemoryMb for why there has to be one.
		List<string> jvm = BuildArguments(resolved["arguments"]?["jvm"], values, osName, osArch);
		if (maxMemoryMb > 0 && !jvm.Any(a => a.StartsWith("-Xmx", StringComparison.Ordinal)))
			args.Add($"-Xmx{maxMemoryMb}M");
		args.AddRange(jvm);
		args.Add((string?)resolved["mainClass"] ?? "net.minecraft.client.main.Main");
		args.AddRange(BuildArguments(resolved["arguments"]?["game"], values, osName, osArch));

		if (!string.IsNullOrEmpty(quickPlaySingleplayerWorld))
		{
			args.Add("--quickPlaySingleplayer");
			args.Add(quickPlaySingleplayerWorld!);
		}

		// ⚠️ An empty argument cannot be passed reliably through a Windows process launcher, and these two are
		// optional. Dropping the flag is correct; passing it with nothing after it is not.
		args = DropEmptyValuedFlags(args, "--clientId", "--xuid");

		return new MinecraftLaunchPlan
		{
			JavaPath         = java,
			Arguments        = args,
			WorkingDirectory = gameDir,
			VersionId        = versionId
		};
	}

	/// <summary>Removes the named flags when the value that follows them is empty.</summary>
	public static List<string> DropEmptyValuedFlags(IReadOnlyList<string> args, params string[] flags)
	{
		var result = new List<string>();

		for (int i = 0; i < args.Count; i++)
		{
			bool isEmptyFlag = flags.Contains(args[i], StringComparer.Ordinal) &&
							   i + 1 < args.Count && args[i + 1].Length == 0;

			if (isEmptyFlag) { i++; continue; }
			result.Add(args[i]);
		}

		return result;
	}

	/// <summary>
	/// The game jar for a version: its own if it has one, else the jar of the version it inherits from.
	///
	/// A Fabric version has no jar of its own until something puts one there — the launcher copies the parent's
	/// across on first run, which is why the two are byte-identical on a machine that has launched it.
	/// </summary>
	public static string GameJarPath(string root, string versionId, string? inheritsFrom)
	{
		string own = Path.Combine(root, "versions", versionId, versionId + ".jar");
		if (File.Exists(own)) return own;

		// Read from the version's OWN file rather than the merged document: merging drops inheritsFrom, so by
		// then there is nothing left to say which version this one is built on.
		if (!string.IsNullOrEmpty(inheritsFrom) && inheritsFrom != versionId)
		{
			string inherited = Path.Combine(root, "versions", inheritsFrom!, inheritsFrom + ".jar");
			if (File.Exists(inherited)) return inherited;
		}

		return own;
	}
}
