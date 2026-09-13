using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
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

	/// <summary>Modrinth asks for a descriptive User-Agent and is entitled to one.</summary>
	private static readonly string UserAgent =
		"KinetixModManager/" +
		(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.1") +
		" (github.com/SeanTerry01/Kinetix-Mod-Manager)";

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

		using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
		client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

		byte[] bytes = await client.GetByteArrayAsync(file.Url);
		File.WriteAllBytes(path, bytes);

		return path;
	}

	private static async Task<string> GetStringAsync(string url)
	{
		using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
		client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
		return await client.GetStringAsync(url);
	}
}
