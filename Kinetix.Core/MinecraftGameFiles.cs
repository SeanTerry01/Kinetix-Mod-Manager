using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>Which part of an installation a file belongs to, so the spoken progress can name it.</summary>
public enum MinecraftFileKind
{
	/// <summary>The game jar itself, around 41 MB.</summary>
	ClientJar,

	/// <summary>The index that lists every asset a version uses.</summary>
	AssetIndex,

	/// <summary>One asset object — a sound, a language file, a texture.</summary>
	Asset
}

/// <summary>One file to fetch, where it belongs, and what it should hash to.</summary>
/// <param name="RelativePath">Where it goes, relative to the <c>.minecraft</c> root.</param>
/// <param name="Url">Where to fetch it.</param>
/// <param name="Sha1">Its published checksum, or <c>null</c> when the source gave none.</param>
/// <param name="Bytes">Its size, for the progress announcement; 0 when unknown.</param>
/// <param name="Kind">Which part of the installation it belongs to.</param>
public readonly record struct MinecraftDownload(
	string RelativePath, string Url, string? Sha1, long Bytes, MinecraftFileKind Kind);

/// <summary>Where a version's own JSON comes from, as the manifest lists it.</summary>
/// <param name="Id">The version id, e.g. <c>26.3</c>.</param>
/// <param name="Url">The URL of that version's JSON.</param>
/// <param name="Sha1">Its checksum.</param>
/// <param name="Type">"release", "snapshot", "old_beta" or "old_alpha".</param>
public readonly record struct MinecraftVersionSource(string Id, string Url, string? Sha1, string Type);

/// <summary>One file of a Java runtime, and whether it has to be marked executable after it lands.</summary>
public readonly record struct MinecraftRuntimeFile(
	string RelativePath, string Url, string? Sha1, long Bytes, bool Executable);

/// <summary>A symbolic link a Java runtime expects; absent from the Windows runtimes, present on the others.</summary>
public readonly record struct MinecraftRuntimeLink(string RelativePath, string Target);

/// <summary>Everything a Java runtime needs laid down, with the files narrowed to the ones not already there.</summary>
public sealed class MinecraftRuntimePlan
{
	/// <summary>Folders to create first, so a file never lands before the folder holding it exists.</summary>
	public required IReadOnlyList<string> Directories { get; init; }

	/// <summary>The files that are missing. Ones already on disk are not listed.</summary>
	public required IReadOnlyList<MinecraftRuntimeFile> Files { get; init; }

	/// <summary>The links to make. Empty on Windows.</summary>
	public required IReadOnlyList<MinecraftRuntimeLink> Links { get; init; }

	/// <summary>How much there is to download, for the warning given before any of it starts.</summary>
	public long TotalBytes => Files.Sum(f => f.Bytes);
}

/// <summary>
/// Installs Minecraft itself — the version JSON, the game jar, the assets and the Java runtime — so that moving
/// to a new version does not mean going back to the official launcher.
///
/// <para>
/// Until now the manager could start a Minecraft version and install Fabric and mods for it, but it could not
/// put the version there: it assumed the official launcher had already fetched the game, which is true of every
/// version the player has played and of no version they have not. So "move to Minecraft 26.3" produced a setup
/// pinned to a version that did not exist on disk, and the failure surfaced later as a Java stack trace. For a
/// player who uses the manager precisely because the launcher is the inaccessible part, "now open the launcher"
/// is the one answer that is no answer at all.
/// </para>
///
/// <para>
/// Every file comes from Mojang's own public endpoints with a published sha1, which is how third-party
/// launchers have always done this. No account and no credential is involved: signing in stays the launcher's
/// job, and <see cref="MinecraftIdentity"/> reads the result of it. There are four endpoints —
/// <see cref="VersionManifestUrl"/> for what versions exist, each version's own JSON for its jar and libraries,
/// <see cref="AssetsBaseUrl"/> for the thousands of small asset objects, and
/// <see cref="JavaRuntimeManifestUrl"/> for the Java a version wants.
/// </para>
///
/// <para>
/// Everything here is a pure function over JSON that has already been fetched, with disk touched only through a
/// caller-supplied <c>exists</c> predicate. That is what makes the parts worth getting right — the asset path
/// split, the dedup, the platform key — testable without a network or a 480 MB download.
/// </para>
/// </summary>
public static class MinecraftGameFiles
{
	/// <summary>Every Minecraft version there has ever been, with the URL of each one's JSON.</summary>
	public const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

	/// <summary>Where asset objects come from. They are addressed by hash, not by name.</summary>
	public const string AssetsBaseUrl = "https://resources.download.minecraft.net";

	/// <summary>The Java runtimes Mojang ships, per platform and per component.</summary>
	public const string JavaRuntimeManifestUrl =
		"https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

	/// <summary>The component a version JSON asks for when it names none. What the launcher falls back to too.</summary>
	public const string DefaultJavaComponent = "java-runtime-delta";

	// -------------------------------------------------------------------------
	// The version manifest
	// -------------------------------------------------------------------------

	/// <summary>The newest full release, which is the only version the manager ever offers to move to.</summary>
	public static string LatestRelease(JObject manifest) => (string?)manifest["latest"]?["release"] ?? "";

	/// <summary>
	/// Where one version's JSON lives, or <c>null</c> when the manifest has never heard of it.
	///
	/// A version id that is not in the manifest is not an error worth a stack trace: Fabric profiles, snapshots
	/// the player installed by hand and anything they made themselves all live in the same folder, and none of
	/// them can be fetched from Mojang. The caller leaves those alone.
	/// </summary>
	public static MinecraftVersionSource? VersionSource(JObject manifest, string versionId)
	{
		foreach (JToken entry in manifest["versions"] as JArray ?? new JArray())
		{
			if (!string.Equals((string?)entry["id"], versionId, StringComparison.OrdinalIgnoreCase)) continue;

			string url = (string?)entry["url"] ?? "";
			if (url.Length == 0) return null;

			return new MinecraftVersionSource(
				(string?)entry["id"] ?? versionId, url, (string?)entry["sha1"],
				(string?)entry["type"] ?? "release");
		}

		return null;
	}

	// -------------------------------------------------------------------------
	// The version's own files
	// -------------------------------------------------------------------------

	/// <summary>Where a version's JSON belongs, relative to the root.</summary>
	public static string VersionJsonRelativePath(string versionId) =>
		Path.Combine("versions", versionId, versionId + ".json");

	/// <summary>Where a version's game jar belongs, relative to the root.</summary>
	public static string ClientJarRelativePath(string versionId) =>
		Path.Combine("versions", versionId, versionId + ".jar");

	/// <summary>
	/// The game jar, when it is not already on disk.
	///
	/// <para>
	/// Not checked against its checksum here, only for presence, which is the same rule
	/// <see cref="MinecraftLauncher.MissingLibraries"/> follows: hashing a 41 MB jar on every single launch to
	/// re-prove something that was verified when it arrived would cost more than it saves. Corruption is caught
	/// on the way in, where it can still be re-fetched.
	/// </para>
	///
	/// <para>
	/// ⚠️ <paramref name="alsoInstalledAs"/> is not a nicety either. A version's jar does not always live under
	/// that version's name: the official launcher copies it into the Fabric profile's folder on first run, so a
	/// machine that has played modded 26.3 has the jar at <c>versions\fabric-loader-…-26.3\</c> and nothing at
	/// all under <c>versions\26.3\</c>. <see cref="MinecraftLauncher.GameJarPath"/> already knows this and
	/// launches happily from either. Checking only the plain name would declare a 41 MB file missing on a
	/// machine that is running it, and fetch it again — which is exactly what the first version of this did on
	/// the development machine.
	/// </para>
	/// </summary>
	/// <param name="alsoInstalledAs">
	/// Another version id whose folder the jar may be sitting in — the Fabric profile that inherits from this
	/// version, in practice.
	/// </param>
	public static MinecraftDownload? MissingClientJar(
		JObject versionJson, string versionId, string root, Func<string, bool> exists,
		string? alsoInstalledAs = null)
	{
		JToken? client = versionJson["downloads"]?["client"];
		string url = (string?)client?["url"] ?? "";
		if (url.Length == 0) return null;

		string relative = ClientJarRelativePath(versionId);
		if (exists(Path.Combine(root, relative))) return null;

		if (!string.IsNullOrEmpty(alsoInstalledAs) &&
			!string.Equals(alsoInstalledAs, versionId, StringComparison.OrdinalIgnoreCase) &&
			exists(Path.Combine(root, ClientJarRelativePath(alsoInstalledAs!))))
			return null;

		return new MinecraftDownload(relative, url, (string?)client?["sha1"],
			(long?)client?["size"] ?? 0, MinecraftFileKind.ClientJar);
	}

	// -------------------------------------------------------------------------
	// Assets
	// -------------------------------------------------------------------------

	/// <summary>Which asset index a version uses. Several versions usually share one.</summary>
	public static string AssetIndexId(JObject versionJson) =>
		(string?)versionJson["assetIndex"]?["id"] ?? (string?)versionJson["assets"] ?? "";

	/// <summary>Where an asset index belongs, relative to the root.</summary>
	public static string AssetIndexRelativePath(string indexId) =>
		Path.Combine("assets", "indexes", indexId + ".json");

	/// <summary>The asset index, when it is not already on disk.</summary>
	public static MinecraftDownload? MissingAssetIndex(JObject versionJson, string root, Func<string, bool> exists)
	{
		JToken? index = versionJson["assetIndex"];
		string id = (string?)index?["id"] ?? "";
		string url = (string?)index?["url"] ?? "";
		if (id.Length == 0 || url.Length == 0) return null;

		string relative = AssetIndexRelativePath(id);
		if (exists(Path.Combine(root, relative))) return null;

		return new MinecraftDownload(relative, url, (string?)index?["sha1"],
			(long?)index?["size"] ?? 0, MinecraftFileKind.AssetIndex);
	}

	/// <summary>
	/// Where one asset object belongs: <c>assets\objects\ab\abcdef…</c>, bucketed by the first two characters
	/// of its hash. The hash IS the file's sha1, so an object never needs a separate checksum.
	/// </summary>
	public static string AssetObjectRelativePath(string hash) =>
		hash.Length < 2 ? "" : Path.Combine("assets", "objects", hash[..2], hash);

	/// <summary>Where to fetch one asset object from.</summary>
	public static string AssetObjectUrl(string hash) =>
		hash.Length < 2 ? "" : $"{AssetsBaseUrl}/{hash[..2]}/{hash}";

	/// <summary>
	/// Every asset object this index names that is not on disk.
	///
	/// <para>
	/// This is the big one and the one that matters most here. A full index is around 5,000 objects and 480 MB,
	/// but objects are addressed by content and shared between versions, so a machine that has played a recent
	/// version usually needs a small fraction of them.
	/// </para>
	///
	/// <para>
	/// ⚠️ And a missing asset is not cosmetic for the player this manager is built for. Sean's 26.3 was missing
	/// four objects and all four were <c>.ogg</c> sounds: a game that starts, plays, looks right to anyone
	/// watching, and has lost part of how he perceives it. That is why the repair pass covers assets and not
	/// just the files that stop the game starting.
	/// </para>
	///
	/// <para>
	/// The same hash appears under several names — one sound serving several events — so objects are deduped by
	/// hash, not by name. Legacy versions (before 1.7) also want the objects copied out into
	/// <c>resources\</c> by name; that is left alone, since the manager only ever offers to move to a current
	/// release, and an old version the player installed themselves is already theirs.
	/// </para>
	/// </summary>
	public static IReadOnlyList<MinecraftDownload> MissingAssets(
		JObject assetIndex, string root, Func<string, bool> exists)
	{
		var missing = new List<MinecraftDownload>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (assetIndex["objects"] is not JObject objects) return missing;

		foreach (JProperty entry in objects.Properties())
		{
			string hash = (string?)entry.Value["hash"] ?? "";
			if (hash.Length < 2 || !seen.Add(hash)) continue;

			string relative = AssetObjectRelativePath(hash);
			if (relative.Length == 0 || exists(Path.Combine(root, relative))) continue;

			missing.Add(new MinecraftDownload(relative, AssetObjectUrl(hash), hash,
				(long?)entry.Value["size"] ?? 0, MinecraftFileKind.Asset));
		}

		return missing;
	}

	// -------------------------------------------------------------------------
	// The Java runtime
	// -------------------------------------------------------------------------

	/// <summary>Which Java runtime a version wants. Versions have moved through delta, epsilon and beyond.</summary>
	public static string JavaComponent(JObject versionJson) =>
		(string?)versionJson["javaVersion"]?["component"] ?? DefaultJavaComponent;

	/// <summary>
	/// The key the runtime manifest files this machine's runtimes under.
	///
	/// Mojang's spelling, which is not the one the version JSONs use: <c>windows-x64</c> rather than
	/// <c>windows</c> plus <c>x86_64</c>, and <c>mac-os</c> with a hyphen.
	/// </summary>
	public static string RuntimePlatform(string osName, string osArch) => (osName, osArch) switch
	{
		("windows", "x86")     => "windows-x86",
		("windows", "arm64")   => "windows-arm64",
		("windows", _)         => "windows-x64",
		("linux", "x86")       => "linux-i386",
		("linux", _)           => "linux",
		("osx", "arm64")       => "mac-os-arm64",
		("osx", _)             => "mac-os",
		_                      => "windows-x64"
	};

	/// <summary>
	/// The manifest URL for one runtime component on one platform, or "" when Mojang publishes none.
	///
	/// An empty answer is a real one and has to be said plainly rather than retried: it means this component
	/// does not exist for this machine — an arm64 Windows with no arm64 build, most likely — and no amount of
	/// downloading will change that.
	/// </summary>
	public static string RuntimeManifestUrl(JObject allRuntimes, string platform, string component) =>
		(string?)(allRuntimes[platform]?[component] as JArray)?.FirstOrDefault()?["manifest"]?["url"] ?? "";

	/// <summary>The version name Mojang gives the runtime it would install, e.g. "21.0.3". "" when unknown.</summary>
	public static string RuntimeVersionName(JObject allRuntimes, string platform, string component) =>
		(string?)(allRuntimes[platform]?[component] as JArray)?.FirstOrDefault()?["version"]?["name"] ?? "";

	/// <summary>
	/// What a Java runtime needs laid down under <paramref name="componentRoot"/>, narrowed to what is missing.
	///
	/// <para>
	/// The manifest is a flat map of every path in the runtime to one of three things: a directory, a file with
	/// two downloads, or a symbolic link. The <c>raw</c> download is taken rather than the <c>lzma</c> one —
	/// they are the same bytes, and the published sha1 is the raw file's, so taking the compressed one would
	/// mean carrying an LZMA decoder to arrive at a file that then has to be verified against the same hash
	/// anyway. It costs about a third more transfer, once, for a whole dependency not taken.
	/// </para>
	///
	/// <para>
	/// The executable flag matters on Linux and macOS and is meaningless on Windows; it is reported either way
	/// so the caller decides, which is the rule the rest of the core follows now that there is a GTK head.
	/// </para>
	/// </summary>
	public static MinecraftRuntimePlan MissingRuntimeFiles(
		JObject runtimeManifest, string componentRoot, Func<string, bool> exists)
	{
		var directories = new List<string>();
		var files = new List<MinecraftRuntimeFile>();
		var links = new List<MinecraftRuntimeLink>();

		if (runtimeManifest["files"] is not JObject entries)
			return new MinecraftRuntimePlan { Directories = directories, Files = files, Links = links };

		foreach (JProperty entry in entries.Properties())
		{
			string relative = entry.Name.Replace('/', Path.DirectorySeparatorChar);
			if (relative.Length == 0) continue;

			switch ((string?)entry.Value["type"])
			{
				case "directory":
					directories.Add(relative);
					break;

				case "link":
					string target = (string?)entry.Value["target"] ?? "";
					if (target.Length > 0) links.Add(new MinecraftRuntimeLink(relative, target));
					break;

				case "file":
					JToken? raw = entry.Value["downloads"]?["raw"];
					string url = (string?)raw?["url"] ?? "";
					if (url.Length == 0) break;
					if (exists(Path.Combine(componentRoot, relative))) break;

					files.Add(new MinecraftRuntimeFile(relative, url, (string?)raw?["sha1"],
						(long?)raw?["size"] ?? 0, (bool?)entry.Value["executable"] ?? false));
					break;
			}
		}

		return new MinecraftRuntimePlan { Directories = directories, Files = files, Links = links };
	}

	/// <summary>
	/// Where the manager puts a Java runtime it fetched itself: <c>&lt;root&gt;\runtime\&lt;component&gt;\…</c>,
	/// laid out the way the launcher lays its own out so <see cref="MinecraftLauncher.FindBundledJava"/> finds
	/// it by the same walk.
	///
	/// ⚠️ Deliberately under the <c>.minecraft</c> root and not in the launcher's folder. That one lives inside
	/// a Microsoft Store package's local cache, and writing another application's package data to save a
	/// download is a trade worth refusing.
	/// </summary>
	public static string RuntimeRootFor(string root, string component, string platform) =>
		Path.Combine(root, "runtime", component, platform, component);
}
