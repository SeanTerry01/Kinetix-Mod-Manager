using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>One file attached to a GitHub release.</summary>
public sealed record GitHubAsset(string Name, string Url, long Size);

/// <summary>A GitHub release, and the files published with it.</summary>
public sealed record GitHubRelease(string TagName, IReadOnlyList<GitHubAsset> Assets);

/// <summary>
/// Installing a mod from a GitHub repository the user can name.
///
/// <para>
/// GitHub is not a mod site and has no catalogue to search, which is why it is not one of the catalogues in
/// the source chooser. It is on the list anyway because a great many mods are released there and nowhere
/// else, and the manager has downloaded from it for years — the Accessibility Suite, SMAPI and BepInEx all
/// arrive this way. What it could not do until now was take a repository the user names.
/// </para>
///
/// <para>
/// The whole difficulty is picking the right file. A release commonly publishes several, and the wrong one
/// installs perfectly and does nothing.
/// </para>
/// </summary>
public static class GitHubReleases
{
	/// <summary>
	/// A repository as <c>owner/repo</c>, from whatever the user typed or pasted — the bare pair, the mod's
	/// page, its releases page, a link to one release, or the API URL.
	///
	/// Accepting a pasted URL matters more here than it looks: the user is coming from a browser, and asking
	/// somebody to read a URL and retype two words out of the middle of it is asking them to do by hand what
	/// a regular expression does exactly.
	/// </summary>
	public static string? ParseRepo(string? input)
	{
		if (string.IsNullOrWhiteSpace(input)) return null;

		string text = input.Trim();

		// The API's own address first, because its path carries an extra "repos" segment that the general
		// pattern below would otherwise read as the owner's name.
		Match api = Regex.Match(
			text, @"api\.github\.com/repos/(?<owner>[^/\s]+)/(?<repo>[^/\s?#]+)", RegexOptions.IgnoreCase);
		if (api.Success) return api.Groups["owner"].Value + "/" + Trim(api.Groups["repo"].Value);

		// Both the https form and the ssh one, which puts a colon where the browser puts a slash. A user who
		// copies a clone command rather than the page address has still said which repository they mean.
		Match url = Regex.Match(
			text, @"github\.com[:/](?<owner>[^/\s:]+)/(?<repo>[^/\s?#]+)", RegexOptions.IgnoreCase);
		if (url.Success) return url.Groups["owner"].Value + "/" + Trim(url.Groups["repo"].Value);

		Match bare = Regex.Match(text, @"^(?<owner>[A-Za-z0-9._-]+)/(?<repo>[A-Za-z0-9._-]+)$");
		return bare.Success ? bare.Groups["owner"].Value + "/" + Trim(bare.Groups["repo"].Value) : null;

		// A repository cloned by URL ends ".git"; the same repository named on the API does not.
		static string Trim(string repo) =>
			repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? repo[..^4] : repo;
	}

	/// <summary>The release API endpoint for a repository's newest release.</summary>
	public static string LatestReleaseApiUrl(string repo) => $"https://api.github.com/repos/{repo}/releases/latest";

	/// <summary>Reads one release out of GitHub's JSON, or null when the body is not one.</summary>
	public static GitHubRelease? Parse(string json)
	{
		try
		{
			JObject doc = JObject.Parse(json);
			string tag = (string?)doc["tag_name"] ?? "";

			var assets = new List<GitHubAsset>();
			if (doc["assets"] is JArray list)
			{
				foreach (JToken asset in list)
				{
					string name = (string?)asset["name"] ?? "";
					string url = (string?)asset["browser_download_url"] ?? "";
					if (name.Length > 0 && url.Length > 0)
						assets.Add(new GitHubAsset(name, url, (long?)asset["size"] ?? 0));
				}
			}

			return new GitHubRelease(tag, assets);
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("GitHub", "reading a release", ex);
			return null;
		}
	}

	/// <summary>Files that are published beside a mod and are never the mod.</summary>
	private static readonly string[] NotAMod =
	{
		".md", ".txt", ".json", ".asc", ".sig", ".sha1", ".sha256", ".pdf", ".png", ".jpg",
	};

	/// <summary>
	/// Jars a Fabric mod publishes alongside the real one. Installing <c>-sources</c> gives a mods folder that
	/// looks right, a game that starts, and no mod — the exact failure Minecraft support exists to prevent, so
	/// it is worth naming them rather than taking the first <c>.jar</c> in the list.
	/// </summary>
	private static readonly string[] NotTheModJar = { "-sources", "-dev", "-javadoc", "-slim", "-shadow" };

	/// <summary>
	/// The file to install, or null when the release publishes nothing the manager can use.
	/// </summary>
	/// <param name="wantJar">
	/// True for Minecraft, where a mod IS a <c>.jar</c> rather than an archive holding one. Elsewhere a jar is
	/// not a mod at all, so it is not considered.
	/// </param>
	public static GitHubAsset? PickModAsset(GitHubRelease release, bool wantJar)
	{
		var usable = release.Assets
			.Where(a => !NotAMod.Any(ext => a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
			.ToList();

		if (wantJar)
		{
			var jars = usable.Where(a => a.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)).ToList();

			// The real mod first, and a suffixed jar only if it is all there is — a release that publishes
			// nothing but a sources jar is one the manager should not silently install, but saying "nothing
			// usable" about a file that is right there is its own kind of unhelpful.
			return jars.FirstOrDefault(a => !NotTheModJar.Any(s => a.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
				?? jars.FirstOrDefault();
		}

		// In preference order rather than whichever came first: a release publishing both a .zip and a .7z is
		// publishing the same mod twice, and the zip is the one every extractor handles without a thought.
		foreach (string ext in new[] { ".zip", ".7z", ".rar" })
		{
			GitHubAsset? found = usable.FirstOrDefault(a => a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
			if (found != null) return found;
		}

		return null;
	}
}
