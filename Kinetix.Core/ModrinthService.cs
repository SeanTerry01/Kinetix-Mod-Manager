using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>One downloadable file of a Modrinth project, already narrowed to a game version and loader.</summary>
public sealed class ModrinthFile
{
	public required string VersionNumber { get; init; }
	public required string FileName { get; init; }
	public required string Url { get; init; }
	public long Size { get; init; }

	/// <summary>The Minecraft versions this file is built for, straight from Modrinth.</summary>
	public IReadOnlyList<string> GameVersions { get; init; } = Array.Empty<string>();

	/// <summary>
	/// Project ids this file needs installed separately — the <c>required</c> dependencies only.
	///
	/// <c>embedded</c> ones are deliberately excluded: they are already inside the jar, and reporting them
	/// would have the manager installing Fabric API beside a mod that already contains it.
	/// </summary>
	public IReadOnlyList<string> RequiredDependencyProjectIds { get; init; } = Array.Empty<string>();
}

/// <summary>One version of a Modrinth modpack, with the <c>.mrpack</c> file that installs it.</summary>
public sealed class ModpackVersion
{
	public string Id { get; init; } = "";
	public string ProjectId { get; init; } = "";
	public string VersionNumber { get; init; } = "";

	/// <summary><c>release</c>, <c>beta</c> or <c>alpha</c>.</summary>
	public string VersionType { get; init; } = "release";

	public IReadOnlyList<string> GameVersions { get; init; } = Array.Empty<string>();
	public IReadOnlyList<string> Loaders { get; init; } = Array.Empty<string>();
	public DateTimeOffset Published { get; init; }
	public string FileName { get; init; } = "";
	public string Url { get; init; } = "";
	public string Sha1 { get; init; } = "";
	public long Size { get; init; }

	public bool IsRelease => string.Equals(VersionType, "release", StringComparison.OrdinalIgnoreCase);

	/// <summary>The newest Minecraft version it lists — the one a pack is actually built on.</summary>
	public string MinecraftVersion => GameVersions.Count > 0 ? GameVersions[GameVersions.Count - 1] : "";

	public bool IsFor(string loader) => Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Reads Modrinth, which is where Minecraft's Fabric mods actually live.
///
/// <para>
/// Nexus has a Minecraft section, but it is largely maps and legacy content — the mods this manager needs are
/// on Modrinth and CurseForge. Modrinth's API is also simply better for the job: no API key, no auth, a
/// documented 300 requests a minute, and — the part that matters most — each file states the game versions and
/// loaders it was built for. That is the fact the update checker has to infer from file names everywhere else,
/// and getting it wrong is what once installed a six-week-old build over a working one.
/// </para>
/// </summary>
public static class ModrinthService
{
	private const string ApiBase = "https://api.modrinth.com/v2";

	/// <summary>
	/// The newest file of <paramref name="projectIdOrSlug"/> built for <paramref name="gameVersion"/> on Fabric,
	/// or <c>null</c> when the project has no such file yet.
	///
	/// A null answer is a sentence to say to the user — "there is no build of this for Minecraft 26.3 yet" — and
	/// not a reason to fall back to a file for some other version. Installing a mod built for a different game
	/// version is the single most common way a Minecraft setup ends up silently loading nothing.
	/// </summary>
	public static async Task<ModrinthFile?> GetLatestFileAsync(
		string projectIdOrSlug, string gameVersion, string loader = "fabric")
	{
		string url = $"{ApiBase}/project/{Uri.EscapeDataString(projectIdOrSlug)}/version" +
					 $"?game_versions=%5B%22{Uri.EscapeDataString(gameVersion)}%22%5D" +
					 $"&loaders=%5B%22{Uri.EscapeDataString(loader)}%22%5D";

		JArray versions = JArray.Parse(await GetStringAsync(url));
		return ParseNewestFile(versions);
	}

	/// <summary>
	/// <see cref="GetLatestFileAsync"/>'s parsing half, split out so the picking rules can be tested against
	/// recorded API responses rather than the live service.
	///
	/// Modrinth returns versions newest first, so the first entry carrying a primary file wins. A version whose
	/// files are all non-primary is skipped rather than guessed at — that shape means a source jar or a
	/// supplementary download, not the mod.
	/// </summary>
	public static ModrinthFile? ParseNewestFile(JArray versions)
	{
		foreach (JToken version in versions)
		{
			if (version["files"] is not JArray files) continue;

			JToken? primary = files.FirstOrDefault(f => (bool?)f["primary"] == true);
			if (primary is null) continue;

			string? url = (string?)primary["url"];
			string? fileName = (string?)primary["filename"];
			if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(fileName)) continue;

			var required = new List<string>();
			if (version["dependencies"] is JArray deps)
			{
				foreach (JToken dep in deps)
				{
					if ((string?)dep["dependency_type"] != "required") continue;

					string? id = (string?)dep["project_id"];
					if (!string.IsNullOrEmpty(id)) required.Add(id!);
				}
			}

			return new ModrinthFile
			{
				VersionNumber = (string?)version["version_number"] ?? "",
				FileName      = fileName!,
				Url           = url!,
				Size          = (long?)primary["size"] ?? 0,
				GameVersions  = (version["game_versions"] as JArray)?
					.Select(v => (string?)v ?? "").Where(v => v.Length > 0).ToList()
					?? (IReadOnlyList<string>)Array.Empty<string>(),
				RequiredDependencyProjectIds = required
			};
		}

		return null;
	}

	/// <summary>Every Minecraft version this project has a Fabric build for, newest first as Modrinth orders them.</summary>
	public static async Task<IReadOnlyList<string>> GetSupportedGameVersionsAsync(string projectIdOrSlug)
	{
		JObject project = JObject.Parse(
			await GetStringAsync($"{ApiBase}/project/{Uri.EscapeDataString(projectIdOrSlug)}"));

		return (project["game_versions"] as JArray)?
			.Select(v => (string?)v ?? "")
			.Where(v => v.Length > 0)
			.Reverse()
			.ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
	}

	// -------------------------------------------------------------------------
	// Updates
	// -------------------------------------------------------------------------

	/// <summary>
	/// The newest build of each installed mod, keyed by the SHA-1 of the jar that is installed now.
	///
	/// <para>
	/// Modrinth identifies a mod by the hash of its file, which is a considerably better answer than every
	/// other update check in this manager gets. Elsewhere the manager has to know a mod's catalogue id, guess
	/// it from a download's file name, or match on the mod's name and hope — and matching by name is what once
	/// installed a six-week-old build over a working one. A hash is exact: this file IS that release of that
	/// project, or Modrinth has never seen it.
	/// </para>
	///
	/// <para>
	/// A hash Modrinth does not know is simply absent from the answer. That is the honest result for a mod it
	/// does not host — United Minecraft is on GitHub only — and it must not be read as "no update available".
	/// </para>
	/// </summary>
	public static async Task<Dictionary<string, ModrinthFile>> GetLatestForHashesAsync(
		IReadOnlyCollection<string> sha1Hashes, string gameVersion, string loader = "fabric")
	{
		var latest = new Dictionary<string, ModrinthFile>(StringComparer.OrdinalIgnoreCase);
		if (sha1Hashes.Count == 0) return latest;

		var body = new JObject
		{
			["hashes"]        = new JArray(sha1Hashes),
			["algorithm"]     = "sha1",
			["loaders"]       = new JArray(loader),
			["game_versions"] = new JArray(gameVersion)
		};

		using var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
		HttpResponseMessage response = await KinetixHttp.Api.PostAsync($"{ApiBase}/version_files/update", content);
		if (!response.IsSuccessStatusCode) return latest;

		return ParseHashUpdates(JObject.Parse(await response.Content.ReadAsStringAsync()));
	}

	/// <summary>
	/// What the installed jars themselves say they support, looked up by their hashes — not the newest build, the
	/// one on disk.
	///
	/// <para>
	/// The question "will this mod survive a move to a new Minecraft version" needs this. A mod's own
	/// <c>fabric.mod.json</c> is the first answer, but it is optional: Toolbar Sounds declares no Minecraft
	/// version at all, so nothing stopped it loading on 26.3 — where its data pack failed to parse and the world
	/// would not load, which a player sees as "errors in the currently selected data packs" and no way back in.
	/// Its catalogue entry knew perfectly well that it supports 26.2 and no further.
	/// </para>
	/// </summary>
	public static async Task<Dictionary<string, ModrinthFile>> GetInstalledVersionsForHashesAsync(
		IReadOnlyCollection<string> sha1Hashes)
	{
		var found = new Dictionary<string, ModrinthFile>(StringComparer.OrdinalIgnoreCase);
		if (sha1Hashes.Count == 0) return found;

		var body = new JObject
		{
			["hashes"]    = new JArray(sha1Hashes),
			["algorithm"] = "sha1"
		};

		using var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
		HttpResponseMessage response = await KinetixHttp.Api.PostAsync($"{ApiBase}/version_files", content);
		if (!response.IsSuccessStatusCode) return found;

		return ParseHashUpdates(JObject.Parse(await response.Content.ReadAsStringAsync()));
	}

	/// <summary>
	/// <see cref="GetLatestForHashesAsync"/>'s parsing half — the response is an object keyed by the hash that
	/// was asked about, each value a version in the same shape the version listing uses.
	/// </summary>
	public static Dictionary<string, ModrinthFile> ParseHashUpdates(JObject response)
	{
		var latest = new Dictionary<string, ModrinthFile>(StringComparer.OrdinalIgnoreCase);

		foreach (JProperty entry in response.Properties())
		{
			if (entry.Value is not JObject version) continue;

			ModrinthFile? file = ParseNewestFile(new JArray(version));
			if (file != null) latest[entry.Name] = file;
		}

		return latest;
	}

	/// <summary>
	/// Which Modrinth project and version a file is, from its SHA-1 — or <c>null</c> when it is not a file
	/// Modrinth published.
	///
	/// <para>
	/// What lets a modpack installed from a downloaded <c>.mrpack</c> still be offered updates. The Modrinth App
	/// treats a pack installed from a file as unlinked for good, which is why the accessibility pack on the
	/// development machine was never once told a newer version existed. The file's own checksum says exactly which
	/// release it is.
	/// </para>
	/// </summary>
	public static async Task<(string ProjectId, string VersionId)?> IdentifyFileAsync(string sha1)
	{
		try
		{
			HttpResponseMessage response = await KinetixHttp.Api.GetAsync(
				$"{ApiBase}/version_file/{Uri.EscapeDataString(sha1)}?algorithm=sha1");
			if (!response.IsSuccessStatusCode) return null;   // 404 is the ordinary answer for a file from elsewhere

			return ParseIdentifiedFile(JObject.Parse(await response.Content.ReadAsStringAsync()));
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Modrinth", "identifying a file by its checksum", ex);
			return null;
		}
	}

	/// <summary><see cref="IdentifyFileAsync"/>'s parsing half: a version object carries its own id and its project's.</summary>
	public static (string ProjectId, string VersionId)? ParseIdentifiedFile(JObject version)
	{
		string project = (string?)version["project_id"] ?? "";
		string id = (string?)version["id"] ?? "";
		return project.Length > 0 && id.Length > 0 ? (project, id) : null;
	}

	/// <summary>The SHA-1 of a file, lower-case hex — the form Modrinth's hash lookups expect.</summary>
	public static string Sha1Of(string path)
	{
		using var sha1 = System.Security.Cryptography.SHA1.Create();
		using FileStream stream = File.OpenRead(path);
		return Convert.ToHexString(sha1.ComputeHash(stream)).ToLowerInvariant();
	}

	// -------------------------------------------------------------------------
	// Search
	// -------------------------------------------------------------------------

	/// <summary>
	/// Searches Modrinth for Fabric mods matching <paramref name="term"/> that are built for
	/// <paramref name="gameVersion"/>, returning results in the same shape the Nexus search returns so the
	/// discovery list needs no idea which catalogue it is showing.
	///
	/// The game-version facet is not optional. Modrinth will happily return a mod with no build for the
	/// version being played, and installing one produces a mods folder where nothing loads — the failure this
	/// whole game is prone to. Better to find fewer mods than to offer ones that cannot work.
	/// </summary>
	public static async Task<(List<GameMod> Results, int Total)> SearchAsync(
		string term, string gameVersion, int offset, int limit, string loader = "fabric")
	{
		string facets = "[" +
			$"[\"versions:{gameVersion}\"]," +
			$"[\"categories:{loader}\"]," +
			"[\"project_type:mod\"]]";

		string url = $"{ApiBase}/search?query={Uri.EscapeDataString(term ?? "")}" +
					 $"&facets={Uri.EscapeDataString(facets)}" +
					 $"&offset={Math.Max(0, offset)}&limit={Math.Clamp(limit, 1, 100)}";

		return ParseSearch(JObject.Parse(await GetStringAsync(url)));
	}

	/// <summary>
	/// <see cref="SearchAsync"/>'s parsing half, split out so the mapping can be tested against a recorded
	/// response rather than the live service.
	/// </summary>
	public static (List<GameMod> Results, int Total) ParseSearch(JObject response)
	{
		var results = new List<GameMod>();

		foreach (JToken hit in response["hits"] as JArray ?? new JArray())
		{
			// The slug is preferred over the project id: it is what appears in the mod's URL, it is what a
			// user would quote asking for help, and it is what the API accepts anywhere an id is taken.
			string id = (string?)hit["slug"] ?? (string?)hit["project_id"] ?? "";
			if (id.Length == 0) continue;

			results.Add(new GameMod
			{
				IsSearchResult = true,
				ModrinthId     = id,
				Name           = (string?)hit["title"] ?? id,
				Author         = (string?)hit["author"] ?? "",
				Description    = (string?)hit["description"] ?? "",
				Downloads      = (long?)hit["downloads"] ?? -1,
				// Modrinth has no endorsements. Follows is the nearest thing it keeps — people who chose to
				// watch the mod — and it answers the same question a download count cannot: whether anyone
				// stuck around.
				Endorsements   = (long?)hit["follows"] ?? -1,
				LastUpdated    = ParseDate((string?)hit["date_modified"]),
				IsModpack      = string.Equals((string?)hit["project_type"], "modpack", StringComparison.OrdinalIgnoreCase),
				PackGameVersions = (hit["versions"] as JArray ?? new JArray()).Select(v => (string?)v ?? "").ToList()
			});
		}

		return (results, (int?)response["total_hits"] ?? results.Count);
	}

	/// <summary>
	/// Searches Modrinth for Fabric modpacks. Unlike a mod search there is no Minecraft version filter: a pack
	/// brings its own Minecraft, so the version the player is on has nothing to do with which packs will run.
	/// With no search words, the most downloaded come first — the ones worth hearing about first.
	/// </summary>
	public static async Task<(List<GameMod> Results, int Total)> SearchModpacksAsync(
		string term, int offset, int limit, string loader = "fabric")
	{
		string facets = $"[[\"project_type:modpack\"],[\"categories:{loader}\"]]";
		string index = string.IsNullOrWhiteSpace(term) ? "downloads" : "relevance";

		string url = $"{ApiBase}/search?query={Uri.EscapeDataString(term ?? "")}" +
					 $"&facets={Uri.EscapeDataString(facets)}&index={index}" +
					 $"&offset={Math.Max(0, offset)}&limit={Math.Clamp(limit, 1, 100)}";

		return ParseSearch(JObject.Parse(await GetStringAsync(url)));
	}

	/// <summary>Every version of a modpack project, newest first, parsed. See <see cref="ParseModpackVersions"/>.</summary>
	public static async Task<IReadOnlyList<ModpackVersion>> GetModpackVersionsAsync(string projectIdOrSlug) =>
		ParseModpackVersions(JArray.Parse(await GetStringAsync(
			$"{ApiBase}/project/{Uri.EscapeDataString(projectIdOrSlug)}/version")));

	/// <summary>
	/// A version listing turned into modpack versions: each with its <c>.mrpack</c> file, the Minecraft version and
	/// loaders it is for, and whether it is a release, beta or alpha. A version with no <c>.mrpack</c> file is left
	/// out — it cannot be installed as a pack whatever it says.
	/// </summary>
	public static IReadOnlyList<ModpackVersion> ParseModpackVersions(JArray versions)
	{
		var parsed = new List<ModpackVersion>();

		foreach (JToken version in versions)
		{
			JToken? file = (version["files"] as JArray ?? new JArray())
				.OrderByDescending(f => (bool?)f["primary"] == true)
				.FirstOrDefault(f => ((string?)f["filename"] ?? "").EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase));
			if (file is null) continue;

			parsed.Add(new ModpackVersion
			{
				Id = (string?)version["id"] ?? "",
				ProjectId = (string?)version["project_id"] ?? "",
				VersionNumber = (string?)version["version_number"] ?? "",
				VersionType = (string?)version["version_type"] ?? "release",
				GameVersions = (version["game_versions"] as JArray ?? new JArray()).Select(v => (string?)v ?? "").ToList(),
				Loaders = (version["loaders"] as JArray ?? new JArray()).Select(v => (string?)v ?? "").ToList(),
				Published = ParseDate((string?)version["date_published"]) ?? DateTimeOffset.MinValue,
				FileName = (string?)file["filename"] ?? "",
				Url = (string?)file["url"] ?? "",
				Sha1 = (string?)file["hashes"]?["sha1"] ?? "",
				Size = (long?)file["size"] ?? 0
			});
		}

		return parsed.OrderByDescending(v => v.Published).ToList();
	}

	/// <summary>
	/// Which version of a pack to install: the newest full release for Fabric, or — when the pack has never had a
	/// release — the newest version of any kind. An alpha is somebody's work in progress; a player asking for "the
	/// accessibility pack" should get the one its maker calls finished, even when an alpha is newer.
	/// </summary>
	public static ModpackVersion? ChooseVersionToInstall(IReadOnlyList<ModpackVersion> versions, string loader = "fabric")
	{
		List<ModpackVersion> forLoader = versions.Where(v => v.IsFor(loader)).OrderByDescending(v => v.Published).ToList();
		return forLoader.FirstOrDefault(v => v.IsRelease) ?? forLoader.FirstOrDefault();
	}

	/// <summary>
	/// The version an installed pack should be offered, or <c>null</c> when it is up to date.
	///
	/// <para>
	/// Only something NEWER than what is installed, on the same footing: someone on a release is offered releases
	/// only, because an alpha offered as "an update" to a working setup is a downgrade in everything but number.
	/// Someone who chose an alpha or beta is offered anything newer, releases included, since that is how a test
	/// build becomes a finished one. When the installed version is not in the listing at all (removed by its
	/// maker), there is nothing to compare against and nothing is offered.
	/// </para>
	/// </summary>
	public static ModpackVersion? ChooseUpdate(
		IReadOnlyList<ModpackVersion> versions, string installedVersionId, string loader = "fabric")
	{
		ModpackVersion? installed = versions.FirstOrDefault(v => v.Id == installedVersionId);
		if (installed is null) return null;

		return versions
			.Where(v => v.IsFor(loader) && v.Published > installed.Published && (v.IsRelease || !installed.IsRelease))
			.OrderByDescending(v => v.Published)
			.FirstOrDefault();
	}

	private static DateTimeOffset? ParseDate(string? value) =>
		DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
			System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
			out DateTimeOffset parsed)
			? parsed
			: null;

	/// <summary>Downloads a file to <paramref name="destinationFolder"/> and returns the path written.</summary>
	public static async Task<string> DownloadAsync(ModrinthFile file, string destinationFolder)
	{
		Directory.CreateDirectory(destinationFolder);
		string path = Path.Combine(destinationFolder, file.FileName);

		byte[] bytes = await KinetixHttp.Downloads.GetByteArrayAsync(file.Url);
		File.WriteAllBytes(path, bytes);

		return path;
	}

	private static async Task<string> GetStringAsync(string url)
	{
		return await KinetixHttp.Api.GetStringAsync(url);
	}
}
