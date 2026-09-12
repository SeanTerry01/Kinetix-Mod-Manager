using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// Installs the Fabric mod loader, which Minecraft needs before it will load a single mod.
///
/// <para>
/// Fabric is normally installed by downloading and running <c>fabric-installer-x.y.z.exe</c>, a small graphical
/// window. The manager does not need it. Fabric publishes a metadata API, and
/// <c>/v2/versions/loader/{game}/{loader}/profile/json</c> returns byte for byte what that installer writes —
/// verified by fetching it and diffing against the file the real installer had just produced: same id, same
/// inheritsFrom, same mainClass, same library list with the same hashes.
/// </para>
///
/// <para>
/// So installing Fabric is three plain file operations — fetch the profile JSON, write it under
/// <c>versions\</c>, and add an entry to the launcher's own installation list. No exe, no Java, no
/// administrator prompt, and no graphical installer for a screen reader to fight. The launcher downloads the
/// loader libraries itself the first time it starts the profile.
/// </para>
/// </summary>
public static class FabricInstaller
{
	private const string MetaBaseUrl = "https://meta.fabricmc.net/v2/versions";

	/// <summary>The launcher's own icon for a Fabric installation, so the entry looks like the installer's.</summary>
	private const string ProfileIcon = "TNT";

	// -------------------------------------------------------------------------
	// Naming - pure, so the rest can be reasoned about without a network
	// -------------------------------------------------------------------------

	/// <summary>
	/// The version id Fabric uses, e.g. <c>fabric-loader-0.19.5-26.2</c>. This is both the folder name under
	/// <c>versions\</c> and the <c>lastVersionId</c> the launcher profile points at, so it has to match what
	/// the meta API puts in the profile JSON's own <c>id</c> field exactly.
	/// </summary>
	public static string VersionIdFor(string loaderVersion, string gameVersion) =>
		$"fabric-loader-{loaderVersion}-{gameVersion}";

	/// <summary>
	/// The key and name the installer gives its entry in <c>launcher_profiles.json</c>, e.g.
	/// <c>fabric-loader-26.2</c> — note it carries the GAME version but not the loader version, so installing a
	/// newer loader for the same Minecraft version updates the existing entry instead of piling up a second one.
	/// </summary>
	public static string ProfileKeyFor(string gameVersion) => $"fabric-loader-{gameVersion}";

	/// <summary>Where the version JSON has to be written for the launcher to find it.</summary>
	public static string VersionJsonPathFor(string root, string versionId) =>
		Path.Combine(root, "versions", versionId, versionId + ".json");

	/// <summary>
	/// True when a Fabric version for <paramref name="gameVersion"/> is already installed under
	/// <paramref name="root"/>, whichever loader build it is.
	/// </summary>
	public static bool IsInstalledFor(string root, string gameVersion)
	{
		string versions = Path.Combine(root, "versions");
		if (!Directory.Exists(versions)) return false;

		string suffix = "-" + gameVersion;
		return Directory.EnumerateDirectories(versions)
			.Select(Path.GetFileName)
			.Any(name => name is not null &&
						 name.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase) &&
						 name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// The Minecraft version a Fabric version id is for — <c>fabric-loader-0.19.5-26.2</c> gives <c>26.2</c> —
	/// or <c>""</c> when the id is not one of Fabric's.
	///
	/// The game version is everything after the loader version, not simply the last segment: a snapshot or
	/// release candidate carries hyphens of its own, so <c>fabric-loader-0.19.5-26.3-rc-2</c> is Minecraft
	/// <c>26.3-rc-2</c> and taking the final part would answer <c>2</c>.
	/// </summary>
	public static string GameVersionOf(string versionId)
	{
		if (string.IsNullOrEmpty(versionId)) return "";

		const string prefix = "fabric-loader-";
		if (!versionId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";

		string rest = versionId.Substring(prefix.Length);   // "<loader>-<game>"
		int split = rest.IndexOf('-');
		return split < 0 ? "" : rest.Substring(split + 1);
	}

	/// <summary>The loader build a Fabric version id names, or <c>""</c> when the id is not one of Fabric's.</summary>
	public static string LoaderVersionOf(string versionId)
	{
		if (string.IsNullOrEmpty(versionId)) return "";

		const string prefix = "fabric-loader-";
		if (!versionId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";

		string rest = versionId.Substring(prefix.Length);
		int split = rest.IndexOf('-');
		return split < 0 ? "" : rest.Substring(0, split);
	}

	/// <summary>
	/// The Minecraft version an existing Fabric install under <paramref name="root"/> is for, or <c>""</c> when
	/// there is none.
	///
	/// This is what lets the manager adopt a Fabric that somebody installed by hand rather than ignoring it.
	/// Without it, a perfectly good install is reported as missing purely because the manager was not the one
	/// that put it there — which is a poor answer, and one that invites the user to install it twice.
	/// </summary>
	public static string DetectInstalledGameVersion(string root)
	{
		foreach (string id in InstalledVersionIds(root))
		{
			string game = GameVersionOf(id);
			if (game.Length > 0) return game;
		}

		return "";
	}

	/// <summary>Any installed Fabric version id under <paramref name="root"/>, newest-looking first.</summary>
	public static IReadOnlyList<string> InstalledVersionIds(string root)
	{
		string versions = Path.Combine(root, "versions");
		if (!Directory.Exists(versions)) return Array.Empty<string>();

		return Directory.EnumerateDirectories(versions)
			.Select(Path.GetFileName)
			.Where(n => n is not null && n.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase))
			.Select(n => n!)
			.OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	// -------------------------------------------------------------------------
	// Reading the meta API
	// -------------------------------------------------------------------------

	/// <summary>
	/// The newest Minecraft version Fabric supports and marks stable.
	///
	/// Stability is checked rather than taking the first entry, and that is not pedantry: the list is ordered
	/// newest first and the newest entries are routinely snapshots and release candidates. Installing Fabric for
	/// a release candidate would produce a profile that works and a mods folder where nothing loads, because no
	/// mod is built for it yet — the exact silent failure this whole game is prone to.
	/// </summary>
	public static async Task<string> GetLatestStableGameVersionAsync()
	{
		JArray games = JArray.Parse(await GetStringAsync(MetaBaseUrl + "/game"));
		return FirstStableVersion(games);
	}

	/// <summary>The newest stable loader build for <paramref name="gameVersion"/>.</summary>
	public static async Task<string> GetLatestStableLoaderVersionAsync(string gameVersion)
	{
		JArray loaders = JArray.Parse(await GetStringAsync($"{MetaBaseUrl}/loader/{Uri.EscapeDataString(gameVersion)}"));

		foreach (JToken entry in loaders)
		{
			JToken? loader = entry["loader"];
			if (loader is null) continue;
			if ((bool?)loader["stable"] != true) continue;

			string? version = (string?)loader["version"];
			if (!string.IsNullOrEmpty(version)) return version!;
		}

		throw new InvalidOperationException($"Fabric has no stable loader for Minecraft {gameVersion}.");
	}

	/// <summary>
	/// The exact file the Fabric installer would write for this pairing. Returned as text rather than parsed,
	/// because it is written straight to disk and re-serialising it would change nothing but could only
	/// introduce differences.
	/// </summary>
	public static async Task<string> GetProfileJsonAsync(string gameVersion, string loaderVersion) =>
		await GetStringAsync(
			$"{MetaBaseUrl}/loader/{Uri.EscapeDataString(gameVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json");

	/// <summary>The first entry in a meta list marked <c>"stable": true</c>.</summary>
	public static string FirstStableVersion(JArray entries)
	{
		foreach (JToken entry in entries)
		{
			if ((bool?)entry["stable"] != true) continue;

			string? version = (string?)entry["version"];
			if (!string.IsNullOrEmpty(version)) return version!;
		}

		throw new InvalidOperationException("Fabric's version list contained nothing marked stable.");
	}

	/// <summary>
	/// The User-Agent every request here carries. Computed locally rather than taken from
	/// <c>NexusService.AppVersion</c>, which is the same one-line assembly lookup: this file is compiled into
	/// the test project, and borrowing the constant would drag the whole Nexus service in behind it.
	/// </summary>
	private static readonly string UserAgent =
		"KinetixModManager/" +
		(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.1");

	private static async Task<string> GetStringAsync(string url)
	{
		using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
		client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
		return await client.GetStringAsync(url);
	}

	// -------------------------------------------------------------------------
	// Installing
	// -------------------------------------------------------------------------

	/// <summary>
	/// Installs Fabric for <paramref name="gameVersion"/> into <paramref name="root"/> and points the launcher
	/// at it. Returns the version id that was installed.
	///
	/// <paramref name="loaderVersion"/> may be <c>null</c> to take the newest stable build.
	/// </summary>
	public static async Task<string> InstallAsync(string root, string gameVersion, string? loaderVersion = null)
	{
		if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("No .minecraft folder.", nameof(root));

		loaderVersion ??= await GetLatestStableLoaderVersionAsync(gameVersion);
		string versionId = VersionIdFor(loaderVersion, gameVersion);
		string profileJson = await GetProfileJsonAsync(gameVersion, loaderVersion);

		string path = VersionJsonPathFor(root, versionId);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, profileJson);

		UpsertLauncherProfile(MinecraftLayout.LauncherProfilesPathFor(root), gameVersion, versionId);

		return versionId;
	}

	/// <summary>
	/// Adds or updates this game version's entry in <c>launcher_profiles.json</c>, and marks it as the most
	/// recently used one.
	///
	/// <para>
	/// ⚠️ The <c>lastUsed</c> bump is the point, not a detail. Modern <c>launcher_profiles.json</c> (version 6)
	/// has no <c>selectedProfile</c> key — the launcher's Play button follows whichever installation was used
	/// most recently, per its own <c>profileSorting</c> setting. Writing the profile without claiming that spot
	/// leaves the user one dropdown away from launching vanilla, which starts, plays normally, and says nothing.
	/// </para>
	///
	/// <para>
	/// ⚠️ Do not call this while the launcher is running: it rewrites this file on exit and will discard the
	/// change. See <see cref="IsLauncherRunning"/>.
	/// </para>
	/// </summary>
	/// <summary>
	/// Parses JSON without letting Json.NET turn date-shaped strings into <see cref="DateTime"/> values.
	///
	/// ⚠️ Not a nicety. <c>launcher_profiles.json</c> is full of ISO-8601 timestamps, and Json.NET recognises
	/// them by default, converts them to <c>DateTime</c>, and then re-serialises them in its own format — so
	/// reading the launcher's file and writing it back turns <c>2026-09-11T20:54:24.830Z</c> into
	/// <c>09/11/2026 20:54:24</c>, for every installation in the file, not just the one being changed. A test
	/// caught this; the launcher would have been handed timestamps in a shape it never writes.
	/// </summary>
	public static JObject ParsePreservingDates(string json)
	{
		using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(json))
		{
			DateParseHandling = Newtonsoft.Json.DateParseHandling.None
		};
		return JObject.Load(reader);
	}

	public static void UpsertLauncherProfile(string launcherProfilesPath, string gameVersion, string versionId)
	{
		JObject root = File.Exists(launcherProfilesPath)
			? ParsePreservingDates(File.ReadAllText(launcherProfilesPath))
			: new JObject();

		string json = UpsertLauncherProfileJson(root, gameVersion, versionId, DateTime.UtcNow);

		Directory.CreateDirectory(Path.GetDirectoryName(launcherProfilesPath)!);
		File.WriteAllText(launcherProfilesPath, json);
	}

	/// <summary>
	/// <see cref="UpsertLauncherProfile"/>'s pure half — takes the parsed file and the moment to stamp, and
	/// returns the text to write. Split out so the rules can be tested without a launcher on disk.
	///
	/// Everything already in the file is preserved: the user's other installations, their settings, and any key
	/// a future launcher version adds that this code has never heard of.
	/// </summary>
	public static string UpsertLauncherProfileJson(JObject root, string gameVersion, string versionId, DateTime utcNow)
	{
		var profiles = root["profiles"] as JObject;
		if (profiles is null)
		{
			profiles = new JObject();
			root["profiles"] = profiles;
		}

		string key = ProfileKeyFor(gameVersion);
		string stamp = FormatLauncherTime(utcNow);

		if (profiles[key] is JObject existing)
		{
			existing["lastVersionId"] = versionId;
			existing["lastUsed"] = stamp;
			// Name and icon are left alone if the user renamed the installation - it is theirs.
			if (string.IsNullOrEmpty((string?)existing["name"])) existing["name"] = key;
		}
		else
		{
			profiles[key] = new JObject
			{
				["created"]       = stamp,
				["lastUsed"]      = stamp,
				["lastVersionId"] = versionId,
				["name"]          = key,
				["icon"]          = ProfileIcon,
				["type"]          = "custom"
			};
		}

		if (root["version"] is null) root["version"] = 6;

		return root.ToString(Newtonsoft.Json.Formatting.Indented);
	}

	/// <summary>The timestamp shape the launcher writes, e.g. <c>2026-09-12T00:11:43.867Z</c>.</summary>
	public static string FormatLauncherTime(DateTime utc) =>
		utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

	// -------------------------------------------------------------------------
	// Quick Play - the trap that cost an evening
	// -------------------------------------------------------------------------

	/// <summary>
	/// Repoints the launcher's Quick Play tiles at <paramref name="profileKey"/>. Returns how many were changed.
	///
	/// <para>
	/// ⚠️ This exists because selecting the right installation is NOT enough. The launcher's Quick Play tiles —
	/// the "jump back into this world" entries on its home screen — each store their own <c>configId</c>, baked
	/// in when the world was first played. Launching from a tile ignores the selected installation completely.
	/// </para>
	///
	/// <para>
	/// Observed directly: a user selected the Fabric installation correctly, and eleven seconds later a Quick
	/// Play tile launched vanilla anyway, because the tile still named the vanilla profile. The game started,
	/// played normally, and never spoke. Nothing anywhere said why.
	/// </para>
	///
	/// <para>
	/// The launcher rewrites this file on exit, so treat the change as a nudge that may not stick rather than a
	/// guarantee — which is the better argument for launching the game ourselves and not depending on any of it.
	/// </para>
	/// </summary>
	public static int RepointQuickPlay(string root, string profileKey)
	{
		string path = Path.Combine(root, "launcher_quick_play.json");
		if (!File.Exists(path)) return 0;

		JObject doc;
		try { doc = ParsePreservingDates(File.ReadAllText(path)); }
		catch (Exception) { return 0; }

		int changed = 0;
		if (doc["quickPlayData"] is JObject data)
		{
			foreach (JProperty account in data.Properties())
			{
				if (account.Value is not JArray entries) continue;

				foreach (JToken entry in entries)
				{
					if (entry["javaInstance"] is not JObject instance) continue;
					if ((string?)instance["configId"] == profileKey) continue;

					instance["configId"] = profileKey;
					changed++;
				}
			}
		}

		if (changed > 0) File.WriteAllText(path, doc.ToString(Newtonsoft.Json.Formatting.Indented));
		return changed;
	}

	/// <summary>
	/// True when the Minecraft launcher or the game is running, in which case the profile files must be left
	/// alone — the launcher rewrites them on exit and would discard anything written underneath it.
	/// </summary>
	public static bool IsLauncherRunning()
	{
		foreach (string name in new[] { "Minecraft", "MinecraftLauncher", "GameLaunchHelper", "javaw" })
		{
			try
			{
				if (Process.GetProcessesByName(name).Length > 0) return true;
			}
			catch (Exception ex)
			{
				// Enumerating processes can fail on a locked-down machine; treat that as "not running" rather
				// than refusing to install. Recorded, because if this is failing then the "don't write while
				// the launcher is running" guard is not actually guarding anything.
				DiagnosticLog.WriteException("Minecraft", $"checking whether the process '{name}' is running", ex);
			}
		}

		return false;
	}
}
