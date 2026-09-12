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
