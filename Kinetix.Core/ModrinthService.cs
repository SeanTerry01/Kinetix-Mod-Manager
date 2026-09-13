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
				LastUpdated    = ParseDate((string?)hit["date_modified"])
			});
		}

		return (results, (int?)response["total_hits"] ?? results.Count);
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
