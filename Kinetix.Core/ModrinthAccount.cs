using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>Who a Modrinth key belongs to.</summary>
public sealed record ModrinthUser(string Id, string Username);

/// <summary>
/// The parts of Modrinth that belong to the user's own account: who they are, and what they follow.
///
/// <para>
/// Everything else the manager does with Modrinth — searching, downloading, updating, modpacks — needs no
/// account at all. This is only what a personal access token adds, and it is optional in every sense: without
/// one nothing is missing that the rest of the manager uses.
/// </para>
///
/// <para>
/// ⚠️ A personal token, pasted in, rather than a "sign in with Modrinth" button. Modrinth's sign-in for other
/// programs requires the program to hold a client secret, checked on every sign-in (its own server code:
/// <c>authenticate_client_token_request</c>), and a secret shipped inside a desktop program is not a secret —
/// Modrinth's guide warns that an exposed one can get the application disabled. A token the user makes on the
/// site, with the permissions they choose, has no such problem.
/// </para>
///
/// <para>
/// The token goes in the <c>Authorization</c> header as it is — Modrinth wants no "Bearer" in front of it — and
/// nothing here ever puts it into a message, a log line or an exception.
/// </para>
/// </summary>
public static class ModrinthAccount
{
	private const string ApiBase = "https://api.modrinth.com/v2";

	/// <summary>Where a user makes a token. Signing in first is part of the same page.</summary>
	public const string TokensPageUrl = "https://modrinth.com/settings/pats";

	/// <summary>Where a new user makes an account.</summary>
	public const string SignUpUrl = "https://modrinth.com/auth/sign-up";

	/// <summary>
	/// Who the token belongs to, or <c>null</c> when Modrinth does not accept it. Throws only when Modrinth
	/// could not be asked at all, so "not accepted" and "no connection" stay two different sentences.
	/// </summary>
	public static async Task<ModrinthUser?> GetUserAsync(string token)
	{
		using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"{ApiBase}/user", token);
		if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return null;
		response.EnsureSuccessStatusCode();

		return ParseUser(JObject.Parse(await response.Content.ReadAsStringAsync()));
	}

	/// <summary><see cref="GetUserAsync"/>'s parsing half.</summary>
	public static ModrinthUser? ParseUser(JObject user)
	{
		string id = (string?)user["id"] ?? "";
		string name = (string?)user["username"] ?? "";
		return id.Length > 0 ? new ModrinthUser(id, name.Length > 0 ? name : id) : null;
	}

	/// <summary>
	/// The projects the user follows — mods and modpacks alike — as search results, so the search tab can list
	/// and install them exactly as it does anything it found. Needs the token's "read user data" permission.
	/// </summary>
	public static async Task<List<GameMod>> GetFollowedAsync(string token, string userId)
	{
		using HttpResponseMessage response =
			await SendAsync(HttpMethod.Get, $"{ApiBase}/user/{Uri.EscapeDataString(userId)}/follows", token);
		response.EnsureSuccessStatusCode();

		return ParseProjects(JArray.Parse(await response.Content.ReadAsStringAsync()));
	}

	/// <summary>
	/// Full project records turned into search results. A project record is not a search hit — <c>title</c> and
	/// <c>followers</c> rather than a hit's <c>title</c> and <c>follows</c>, and <c>game_versions</c> rather than
	/// <c>versions</c> — so it has its own reader. Only Minecraft mods and modpacks are kept: a user can follow
	/// resource packs and shaders too, and those do not install as either.
	/// </summary>
	public static List<GameMod> ParseProjects(JArray projects)
	{
		var results = new List<GameMod>();

		foreach (JToken project in projects)
		{
			string type = (string?)project["project_type"] ?? "";
			if (type is not ("mod" or "modpack")) continue;

			string id = (string?)project["slug"] ?? (string?)project["id"] ?? "";
			if (id.Length == 0) continue;

			results.Add(new GameMod
			{
				IsSearchResult = true,
				ModrinthId = id,
				Name = (string?)project["title"] ?? id,
				Description = (string?)project["description"] ?? "",
				Downloads = (long?)project["downloads"] ?? -1,
				Endorsements = (long?)project["followers"] ?? -1,
				IsModpack = type == "modpack",
				PackGameVersions = (project["game_versions"] as JArray ?? new JArray()).Select(v => (string?)v ?? "").ToList()
			});
		}

		return results.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	/// <summary>
	/// Follows or stops following a project. Returns false when Modrinth refused — usually a token without the
	/// "write user data" permission — and throws only when Modrinth could not be reached.
	/// </summary>
	public static async Task<bool> SetFollowingAsync(string token, string projectIdOrSlug, bool follow)
	{
		using HttpResponseMessage response = await SendAsync(follow ? HttpMethod.Post : HttpMethod.Delete,
			$"{ApiBase}/project/{Uri.EscapeDataString(projectIdOrSlug)}/follow", token);

		if (response.IsSuccessStatusCode) return true;
		// Already following, or already not: the state the user asked for is the state it is in.
		if (response.StatusCode == HttpStatusCode.BadRequest) return true;
		return false;
	}

	private static Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token)
	{
		var request = new HttpRequestMessage(method, url);
		request.Headers.TryAddWithoutValidation("Authorization", token);
		return KinetixHttp.Api.SendAsync(request);
	}
}
