using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// Encapsulates all Nexus Mods and GitHub API communication.
/// Owns the shared <see cref="HttpClient"/>, the API rate-limit semaphore, and the app version
/// string. Form1 delegates every network call here and handles all resulting UI updates.
/// </summary>
public class NexusService
{
	/// <summary>Assembly version string used in every User-Agent header (e.g. "1.0.1").</summary>
	public static readonly string AppVersion =
		Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.1";

	/// <summary>
	/// Shared HTTP client for Nexus API calls (30-second timeout).
	/// Exposed as <c>public</c> so Form1 can reuse it for wiki and SMAPI log upload calls
	/// that do not require an API key.
	/// </summary>
	public static readonly HttpClient HttpClient = new HttpClient(
		new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
		})
	{
		Timeout = TimeSpan.FromSeconds(30)
	};

	static NexusService()
	{
		HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"KinetixModManager/{AppVersion}");
	}

	private readonly SemaphoreSlim _apiSemaphore = new SemaphoreSlim(5);
	private readonly AppSettings _settings;

	/// <summary>Nexus username returned by the last successful <see cref="ValidateAsync"/> call.</summary>
	public string NexusUser { get; private set; } = "Unknown User";

	/// <summary>Whether the validated account has Nexus premium (required for automated downloads).</summary>
	public bool IsPremium { get; private set; }

	/// <summary>The API key that was last successfully validated; used to skip re-validating an unchanged key.</summary>
	private string _validatedKey = "";

	/// <summary>
	/// True once the current API key has been validated this session and hasn't changed since. Nexus's API is
	/// stateless (no persistent connection), so re-validating an unchanged key on every mod-list refresh is
	/// redundant — callers check this to validate once, then trust it. Automatically becomes false if the key
	/// changes (the stored validated key no longer matches settings).
	/// </summary>
	public bool IsValidated => !string.IsNullOrEmpty(_validatedKey) && _validatedKey == _settings.ApiKey;

	// -------------------------------------------------------------------------
	// API rate-limit tracking
	// -------------------------------------------------------------------------
	// Every Nexus v1 REST response carries the caller's remaining quota in x-rl-* headers. We capture
	// the latest values off any v1 response we already make (validate, version checks, mod details,
	// endorse) so the user can ask "how many API requests do I have left?" without spending one.

	/// <summary>Requests remaining in the current rolling hour, or -1 if not yet known.</summary>
	public int HourlyRemaining { get; private set; } = -1;
	/// <summary>The per-hour request limit reported by Nexus, or -1 if not yet known.</summary>
	public int HourlyLimit { get; private set; } = -1;
	/// <summary>Requests remaining in the current rolling day, or -1 if not yet known.</summary>
	public int DailyRemaining { get; private set; } = -1;
	/// <summary>The per-day request limit reported by Nexus, or -1 if not yet known.</summary>
	public int DailyLimit { get; private set; } = -1;

	/// <summary>True once at least one v1 response has reported the account's remaining quota.</summary>
	public bool HasRateLimitInfo => HourlyRemaining >= 0 || DailyRemaining >= 0;

	/// <summary>Initialises the service with the live application settings.</summary>
	public NexusService(AppSettings settings) => _settings = settings;

	/// <summary>
	/// Clears the cached authentication state from the last <see cref="ValidateAsync"/> call.
	/// Called when the active game session is closed so the manager no longer reports a
	/// connected Nexus user. The API key in settings is left untouched.
	/// </summary>
	public void Disconnect()
	{
		NexusUser = "Unknown User";
		IsPremium = false;
		_validatedKey = "";
		HourlyRemaining = HourlyLimit = DailyRemaining = DailyLimit = -1;
	}

	// With no game loaded ("None") these still have to return something usable — parts of the UI read them
	// before a session exists — so they fall back to Stardew Valley, the manager's original game, exactly as
	// they did when each game was spelled out here. Every known game now comes from the one registry.
	public string CurrentGameDomain =>
		GameProfiles.Find(_settings.ActiveGame)?.NexusDomain ?? "stardewvalley";

	public string CurrentGameId =>
		GameProfiles.Find(_settings.ActiveGame)?.NexusGameId ?? "1303";

	// -------------------------------------------------------------------------
	// Authentication
	// -------------------------------------------------------------------------

	/// <summary>
	/// Calls <c>/users/validate.json</c> to confirm the stored API key.
	/// On success, updates <see cref="NexusUser"/> and <see cref="IsPremium"/>.
	/// </summary>
	/// <returns><c>true</c> if the key is valid; <c>false</c> on any failure.</returns>
	public async Task<bool> ValidateAsync()
	{
		try
		{
			using var req = BuildRequest(HttpMethod.Get, "https://api.nexusmods.com/v1/users/validate.json");
			var resp = await HttpClient.SendAsync(req);
			CaptureRateLimit(resp);
			if (!resp.IsSuccessStatusCode) { _validatedKey = ""; return false; }
			JObject json = JObject.Parse(await resp.Content.ReadAsStringAsync());
			NexusUser = json["name"]?.ToString() ?? "User";
			IsPremium  = (bool)(json["is_premium"] ?? (JToken)false);
			_validatedKey = _settings.ApiKey;
			return true;
		}
		catch { _validatedKey = ""; return false; }
	}

	// -------------------------------------------------------------------------
	// Update checking
	// -------------------------------------------------------------------------

	/// <summary>
	/// Fetches the latest version string for a mod from the Nexus REST API.
	/// Internally rate-limited by a 5-slot semaphore with a random jitter delay
	/// to stay within Nexus API quotas.
	/// </summary>
	/// <param name="nexusId">Nexus Mods numeric mod ID.</param>
	/// <returns>The latest version string, or <c>null</c> on failure or non-success status.</returns>
	public async Task<string?> GetLatestVersionAsync(string nexusId)
	{
		await _apiSemaphore.WaitAsync();
		try
		{
			await Task.Delay(Random.Shared.Next(100, 1000));
			using var req = BuildRequest(HttpMethod.Get,
				$"https://api.nexusmods.com/v1/games/{CurrentGameDomain}/mods/{nexusId}.json");
			var resp = await HttpClient.SendAsync(req);
			CaptureRateLimit(resp);
			if (!resp.IsSuccessStatusCode) return null;
			return ((string?)JObject.Parse(await resp.Content.ReadAsStringAsync())["version"]) ?? "0";
		}
		catch { return null; }
		finally { _apiSemaphore.Release(); }
	}

	/// <summary>
	/// The versions of the files a mod page currently offers as its main download, for a check that the page's
	/// own version number has already said nothing about.
	///
	/// Costs a second request, so it is asked only when the first answer was "up to date" — see
	/// <c>Form1.CheckForUpdates</c> — and only while there is room in the quota to spend. A thorough check is
	/// worth an extra call; running the user out of API calls and failing every later check is not, so a low
	/// remaining allowance answers with nothing and the check falls back to what the page said.
	/// </summary>
	public async Task<List<string>> GetMainFileVersionsAsync(string nexusId)
	{
		if (!HasQuotaToSpare) return new List<string>();

		await _apiSemaphore.WaitAsync();
		try
		{
			await Task.Delay(Random.Shared.Next(100, 1000));
			return ModPartRules.MainFileVersions(await GetModFilesAsync(nexusId));
		}
		catch { return new List<string>(); }
		finally { _apiSemaphore.Release(); }
	}

	/// <summary>
	/// Whether there is enough of the Nexus quota left to spend a request on a thoroughness check rather than on
	/// something the user asked for. Unknown counts as yes: the headers are only absent before the first call.
	/// </summary>
	private bool HasQuotaToSpare =>
		(DailyRemaining < 0 || DailyRemaining > 100) && (HourlyRemaining < 0 || HourlyRemaining > 20);

	/// <summary>
	/// What the SMAPI web API knows about one installed mod: the suggested update (when one is offered) and
	/// the mod-database entry it was matched to. <see cref="Known"/> is <c>true</c> when smapi.io recognised
	/// the mod's UniqueID at all — that mod has been version-checked even if its manifest carries no update
	/// key, so it must not be reported to the user as "not checked".
	/// </summary>
	public sealed class SmapiModInfo
	{
		public string? Version;
		public string? Url;
		public string? NexusId;
		public string? GitHubRepo;
		public string? Name;
		public bool Known;
	}

	/// <summary>
	/// Coerces a version string into the form the SMAPI web API accepts. See <see cref="SmapiVersion.Sanitize"/>
	/// for why every version sent to smapi.io has to go through it.
	/// </summary>
	public static string SanitizeModVersion(string? version) => SmapiVersion.Sanitize(version);

	/// <summary>
	/// Queries the SMAPI web API (smapi.io) for available updates, exactly as SMAPI itself does. Unlike the
	/// per-mod Nexus check, this resolves updates by each mod's UniqueID against SMAPI's crowdsourced mod
	/// database, so it also finds mods whose <c>manifest.json</c> has a missing or broken update key (for
	/// example <c>Nexus:???</c> or no key at all). Returns a map of mod UniqueID (case-insensitive) to what
	/// the service knows about it, or <c>null</c> if the service could not be reached — in which case callers
	/// fall back to the manifest-based Nexus check. Include an entry with id "SMAPI" to also receive SMAPI's
	/// own update.
	/// </summary>
	/// <param name="mods">Installed mods as (UniqueID, installed version, update keys) tuples.</param>
	/// <param name="smapiVersion">Installed SMAPI version, sent as the API's apiVersion (must be valid semver).</param>
	/// <param name="gameVersion">Installed Stardew Valley version, used by the API to filter compatible updates.</param>
	public async Task<Dictionary<string, SmapiModInfo>?> GetSmapiUpdatesAsync(
		IEnumerable<(string Id, string Version, IEnumerable<string> UpdateKeys)> mods,
		string smapiVersion, string gameVersion)
	{
		// apiVersion/gameVersion are validated by the service too, and a bad one fails the whole request the
		// same way — StardewModdingAPI.dll's file version is commonly four-part ("4.1.10.0"), for instance.
		string api  = SanitizeModVersion(smapiVersion);
		string game = SanitizeModVersion(gameVersion);
		if (string.IsNullOrEmpty(api))  api  = "4.0.0";
		if (string.IsNullOrEmpty(game)) game = "1.6.15";

		var entries = new List<JObject>();
		foreach (var m in mods)
		{
			if (string.IsNullOrEmpty(m.Id)) continue;
			var keys = new JArray();
			foreach (string k in m.UpdateKeys)
				if (!string.IsNullOrWhiteSpace(k)) keys.Add(k);

			entries.Add(new JObject
			{
				["id"]               = m.Id,
				["updateKeys"]       = keys,
				["installedVersion"] = SanitizeModVersion(m.Version),
				["isBroken"]         = false
			});
		}
		if (entries.Count == 0) return new Dictionary<string, SmapiModInfo>(StringComparer.OrdinalIgnoreCase);

		var result = new Dictionary<string, SmapiModInfo>(StringComparer.OrdinalIgnoreCase);
		bool reachable = await FetchSmapiEntriesAsync(entries, api, game, result);
		return reachable ? result : null;
	}

	/// <summary>
	/// Posts one batch of mod entries to smapi.io and merges the results into <paramref name="result"/>.
	/// If the service answers with no entries at all for a batch of more than one mod, the batch is split in
	/// half and each half retried: the API discards every result when a single entry displeases it, so
	/// halving isolates the offender instead of losing the whole check. Returns <c>false</c> only when the
	/// service could not be reached (so the caller can fall back to the Nexus check).
	/// </summary>
	private async Task<bool> FetchSmapiEntriesAsync(
		List<JObject> entries, string apiVersion, string gameVersion, Dictionary<string, SmapiModInfo> result)
	{
		JArray? arr;
		try
		{
			var body = new JObject
			{
				["mods"]                    = new JArray(entries.Cast<object>().ToArray()),
				["apiVersion"]              = apiVersion,
				["gameVersion"]             = gameVersion,
				["platform"]                = "Windows",
				["includeExtendedMetadata"] = true
			};

			using var req = new HttpRequestMessage(HttpMethod.Post, "https://smapi.io/api/v3.0/mods");
			req.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
			req.Headers.UserAgent.ParseAdd($"KinetixModManager/{AppVersion}");

			using var resp = await HttpClient.SendAsync(req);
			if (!resp.IsSuccessStatusCode) return false;
			arr = JArray.Parse(await resp.Content.ReadAsStringAsync());
		}
		catch { return false; }

		if (arr.Count == 0 && entries.Count > 1)
		{
			int half = entries.Count / 2;
			bool a = await FetchSmapiEntriesAsync(entries.GetRange(0, half), apiVersion, gameVersion, result);
			bool b = await FetchSmapiEntriesAsync(entries.GetRange(half, entries.Count - half), apiVersion, gameVersion, result);
			return a || b;
		}

		foreach (JToken entry in arr)
		{
			string? id = (string?)entry["id"];
			if (string.IsNullOrEmpty(id)) continue;

			var info = new SmapiModInfo();
			if (entry["metadata"] is JObject meta)
			{
				// An empty "id" array means the service has no database entry for this mod, so it was not
				// really checked; anything else means it was matched and version-checked.
				info.Known      = meta["id"] is JArray ids && ids.Count > 0;
				info.Name       = (string?)meta["name"];
				info.NexusId    = ((int?)meta["nexusID"])?.ToString();
				info.GitHubRepo = (string?)meta["gitHubRepo"];
			}
			if (entry["suggestedUpdate"] is JObject suggested)
			{
				info.Version = (string?)suggested["version"];
				info.Url     = (string?)suggested["url"];
				if (!string.IsNullOrEmpty(info.Version)) info.Known = true;
			}
			result[id!] = info;
		}
		return true;
	}

	// -------------------------------------------------------------------------
	// Discovery / search
	// -------------------------------------------------------------------------

	/// <summary>
	/// Searches for mods on Nexus via the GraphQL v2 API.
	/// Builds the appropriate query based on <paramref name="searchType"/>.
	/// </summary>
	/// <param name="searchType">
	/// One of: <c>"Search"</c>, <c>"Most Popular"</c>, <c>"Recent"</c>, <c>"Endorsed"</c>.
	/// </param>
	/// <param name="searchTerm">Free-text search query (used when <paramref name="searchType"/> is "Search").</param>
	/// <param name="page">1-based page number.</param>
	/// <param name="pageSize">Number of results per page.</param>
	/// <returns>
	/// A list of <see cref="GameMod"/> search result objects and the total result count.
	/// Returns an empty list on failure.
	/// </returns>
	/// <summary>The Nexus GraphQL <c>mods</c> query silently caps <c>count</c> at 80 per request, so a larger
	/// requested page size (e.g. 100) has to be assembled from several requests or it returns only 80.</summary>
	private const int MaxModsPerRequest = 80;

	public async Task<(List<GameMod> Results, int Total)> SearchModsAsync(
		string searchType, string searchTerm, int page, int pageSize, string? language = null,
		string? category = null)
	{
		int baseOffset = (page - 1) * pageSize;
		var all = new List<GameMod>();
		int total = 0;

		// Fetch in chunks no larger than the API cap until we've gathered the requested page size or run out.
		while (all.Count < pageSize)
		{
			int chunk = Math.Min(MaxModsPerRequest, pageSize - all.Count);
			var (results, t) = await FetchModsPageAsync(searchType, searchTerm, baseOffset + all.Count, chunk, language, category);
			if (t > 0) total = t;               // keep a known total if a later chunk fails/returns nothing
			all.AddRange(results);
			if (results.Count < chunk) break;   // reached the end of the available results
		}

		return (all, total);
	}

	/// <summary>Reads a whole-number field from a GraphQL node, or <c>-1</c> when it is absent or unparseable.</summary>
	private static long ReadCount(JToken? token)
	{
		if (token == null || token.Type == JTokenType.Null) return -1;
		return long.TryParse(token.ToString(), out long value) && value >= 0 ? value : -1;
	}

	/// <summary>
	/// Reads a timestamp field from a GraphQL node, or <c>null</c> when it is absent or unparseable.
	///
	/// <para>
	/// Newtonsoft turns an ISO timestamp into a <see cref="JTokenType.Date"/> token while parsing, so the value
	/// is taken from the token directly rather than round-tripped through its string form, which would be
	/// rendered in the machine's own locale and then have to be read back in the invariant one.
	/// </para>
	/// </summary>
	private static DateTimeOffset? ReadDate(JToken? token)
	{
		if (token == null || token.Type == JTokenType.Null) return null;
		try
		{
			if (token.Type == JTokenType.Date) return token.ToObject<DateTimeOffset>();
			return DateTimeOffset.TryParse(token.ToString(), CultureInfo.InvariantCulture,
				DateTimeStyles.RoundtripKind, out DateTimeOffset value) ? value : null;
		}
		catch { return null; }
	}

	/// <summary>Runs a single Nexus GraphQL search request for <paramref name="count"/> mods starting at
	/// <paramref name="offset"/> (<paramref name="count"/> must not exceed <see cref="MaxModsPerRequest"/>).</summary>
	private async Task<(List<GameMod> Results, int Total)> FetchModsPageAsync(
		string searchType, string searchTerm, int offset, int count, string? language = null,
		string? category = null)
	{
		int pageSize = count;
		string gqlQuery;
		object variables;

		// Build the filter as a dictionary so the optional language clause can be added conditionally.
		// An empty/null language means "Any language" — no languageName clause is sent.
		var filter = new Dictionary<string, object>
		{
			["gameId"] = new[] { new { value = CurrentGameId, op = "EQUALS" } }
		};
		if (!string.IsNullOrEmpty(language))
			filter["languageName"] = new[] { new { value = language, op = "EQUALS" } };

		// A category narrows whatever mode is running rather than being a mode of its own, so it is added here
		// alongside the language and applies equally to a search, a catalogue listing or a popularity sort.
		// Unlike languageName, every mod on Nexus has a category — it is required to upload one — so this filter
		// does not quietly hide most of the catalogue the way the language filter can.
		if (!string.IsNullOrEmpty(category))
			filter["categoryName"] = new[] { new { value = category, op = "EQUALS" } };

		if (searchType == "Search")
		{
			filter["name"] = new[] { new { value = searchTerm, op = "WILDCARD" } };
			gqlQuery = @"query SearchMods($filter: ModsFilter, $count: Int, $offset: Int) {
				mods(filter: $filter, count: $count, offset: $offset) {
					nodes { modId name summary author version endorsements downloads updatedAt }
					totalCount
				}
			}";
			variables = new { filter, count = pageSize, offset };
		}
		else
		{
			// "All" lists the game's entire catalogue in alphabetical order, which is what makes it usable as a
			// catalogue: you can work down it and know where you got to. The other modes are "best first", so
			// they sort descending.
			(string Field, string Direction) sort = searchType switch
			{
				"Most Popular" => ("downloads",    "DESC"),
				"Recent"       => ("updatedAt",    "DESC"),
				"All"          => ("name",         "ASC"),
				// Browsing a category you don't know yet wants the best-known mods in it on the first page, not
				// whichever happens to start with A.
				NexusCategoryBrowse => ("downloads", "DESC"),
				_              => ("endorsements", "DESC")   // Trending
			};
			gqlQuery = @"query ListMods($filter: ModsFilter, $sort: [ModsSort!], $count: Int, $offset: Int) {
				mods(filter: $filter, sort: $sort, count: $count, offset: $offset) {
					nodes { modId name summary author version endorsements downloads updatedAt }
					totalCount
				}
			}";
			variables = new
			{
				filter,
				sort   = new[] { new Dictionary<string, object> { { sort.Field, new { direction = sort.Direction } } } },
				count  = pageSize,
				offset
			};
		}

		try
		{
			using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.nexusmods.com/v2/graphql");
			req.Headers.Add("apikey", _settings.ApiKey);
			req.Headers.Add("User-Agent", $"KinetixModManager/{AppVersion}");
			req.Content = new StringContent(
				JsonConvert.SerializeObject(new { query = gqlQuery, variables }),
				Encoding.UTF8, "application/json");

			var resp = await HttpClient.SendAsync(req);
			if (!resp.IsSuccessStatusCode) return (new(), 0);

			JObject data = JObject.Parse(await resp.Content.ReadAsStringAsync());
			if (data["errors"] != null)
				throw new Exception(data["errors"]?[0]?["message"]?.ToString() ?? "GraphQL error");

			JToken? modsData = data["data"]?["mods"];
			JArray nodes     = (modsData?["nodes"] as JArray) ?? new JArray();
			int total        = modsData?["totalCount"] != null ? (int)modsData["totalCount"]! : 0;

			var results = new List<GameMod>();
			foreach (var node in nodes)
			{
				results.Add(new GameMod
				{
					Name         = node["name"]?.ToString()    ?? "Unknown",
					Author       = node["author"]?.ToString()  ?? "Unknown",
					Version      = node["version"]?.ToString() ?? "0",
					Description  = node["summary"]?.ToString() ?? "",
					NexusID      = node["modId"]?.ToString(),
					UniqueId     = node["modId"]?.ToString()   ?? Guid.NewGuid().ToString(),
					// -1 keeps "the API didn't say" distinct from a genuine zero, so a brand-new mod with no
					// downloads yet reads as "0 downloads" rather than silently omitting the figure.
					Downloads    = ReadCount(node["downloads"]),
					Endorsements = ReadCount(node["endorsements"]),
					// How long the mod has been left alone is the one thing the counts cannot say, and Nexus
					// reports it on the same node — no second request, no extra rate-limit cost.
					LastUpdated  = ReadDate(node["updatedAt"]),
					IsSearchResult = true
				});
			}
			return (results, total);
		}
		catch { return (new(), 0); }
	}

	/// <summary>
	/// How many mods the active game has in total, ignoring any language filter, or <c>-1</c> when it can't be
	/// determined.
	///
	/// This exists to explain a genuinely baffling result. Nexus only knows a mod's language if its author filled
	/// that field in, and most don't — of Moonlight Peaks' 80 mods, 9 declare a language and only 5 say English.
	/// So a search with the language set to English silently hides 71 mods that are, in fact, in English. Knowing
	/// the unfiltered total lets the manager say "showing 5 of 80" instead of leaving the user to conclude the
	/// mods they are looking for aren't on Nexus.
	/// </summary>
	public async Task<int> GetUnfilteredModCountAsync()
	{
		try
		{
			const string gql = @"query ModCount($filter: ModsFilter) {
				mods(filter: $filter, count: 0) { totalCount }
			}";
			var variables = new
			{
				filter = new Dictionary<string, object>
				{
					["gameId"] = new[] { new { value = CurrentGameId, op = "EQUALS" } }
				}
			};

			using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.nexusmods.com/v2/graphql");
			req.Headers.Add("apikey", _settings.ApiKey);
			req.Headers.Add("User-Agent", $"KinetixModManager/{AppVersion}");
			req.Content = new StringContent(
				JsonConvert.SerializeObject(new { query = gql, variables }), Encoding.UTF8, "application/json");

			var resp = await HttpClient.SendAsync(req);
			if (!resp.IsSuccessStatusCode) return -1;

			JObject data = JObject.Parse(await resp.Content.ReadAsStringAsync());
			JToken? count = data["data"]?["mods"]?["totalCount"];
			return count != null ? (int)count : -1;
		}
		catch { return -1; }
	}

	/// <summary>
	/// The search type that browses one Nexus category rather than the whole catalogue. Shared with the UI so
	/// the two cannot drift apart on the spelling of a magic string.
	/// </summary>
	public const string NexusCategoryBrowse = "Nexus Categories";

	/// <summary>
	/// Returns the Nexus categories that have mods for the active game, with a count for each, ordered
	/// most-populated first. Returns an empty list on failure.
	///
	/// <para>
	/// The same facet trick as <see cref="GetModLanguagesAsync"/>, and for the same reason: asking the catalogue
	/// what it actually contains gives the categories this game really uses, with real counts, rather than a
	/// fixed list of every category Nexus has ever defined — most of which would be empty for any one game and
	/// would be a long walk through nothing.
	/// </para>
	/// </summary>
	public async Task<List<(string Name, int Count)>> GetModCategoriesAsync()
	{
		var categories = new List<(string, int)>();
		try
		{
			const string gql = @"query ModCategories($filter: ModsFilter) {
				mods(filter: $filter, count: 0, facets: { categoryName: [""*""] }) {
					facets { facet value count }
				}
			}";
			var variables = new
			{
				filter = new Dictionary<string, object>
				{
					["gameId"] = new[] { new { value = CurrentGameId, op = "EQUALS" } }
				}
			};

			using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.nexusmods.com/v2/graphql");
			req.Headers.Add("User-Agent", $"KinetixModManager/{AppVersion}");
			if (!string.IsNullOrEmpty(_settings.ApiKey)) req.Headers.Add("apikey", _settings.ApiKey);
			req.Content = new StringContent(
				JsonConvert.SerializeObject(new { query = gql, variables }),
				Encoding.UTF8, "application/json");

			var resp = await HttpClient.SendAsync(req);
			if (!resp.IsSuccessStatusCode) return categories;

			JObject data = JObject.Parse(await resp.Content.ReadAsStringAsync());
			JArray facets = (data["data"]?["mods"]?["facets"] as JArray) ?? new JArray();
			foreach (var f in facets)
			{
				if (f["facet"]?.ToString() != "categoryName") continue;
				string name = f["value"]?.ToString() ?? "";
				int count = f["count"] != null ? (int)f["count"]! : 0;
				if (name.Length > 0) categories.Add((name, count));
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Nexus", "asking Nexus for the category list", ex); }
		return categories;
	}

	/// <summary>
	/// Returns the languages that have mods for the active game, with a count for each, as reported by the
	/// Nexus GraphQL language facet (already ordered most-common first). Returns an empty list on failure.
	/// </summary>
	public async Task<List<(string Name, int Count)>> GetModLanguagesAsync()
	{
		var languages = new List<(string, int)>();
		try
		{
			const string gql = @"query ModLanguages($filter: ModsFilter) {
				mods(filter: $filter, count: 0, facets: { languageName: [""*""] }) {
					facets { facet value count }
				}
			}";
			var variables = new
			{
				filter = new Dictionary<string, object>
				{
					["gameId"] = new[] { new { value = CurrentGameId, op = "EQUALS" } }
				}
			};

			using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.nexusmods.com/v2/graphql");
			req.Headers.Add("User-Agent", $"KinetixModManager/{AppVersion}");
			if (!string.IsNullOrEmpty(_settings.ApiKey)) req.Headers.Add("apikey", _settings.ApiKey);
			req.Content = new StringContent(
				JsonConvert.SerializeObject(new { query = gql, variables }),
				Encoding.UTF8, "application/json");

			var resp = await HttpClient.SendAsync(req);
			if (!resp.IsSuccessStatusCode) return languages;

			JObject data = JObject.Parse(await resp.Content.ReadAsStringAsync());
			JArray facets = (data["data"]?["mods"]?["facets"] as JArray) ?? new JArray();
			foreach (var f in facets)
			{
				if (f["facet"]?.ToString() != "languageName") continue;
				string name = f["value"]?.ToString() ?? "";
				int count = f["count"] != null ? (int)f["count"]! : 0;
				if (name.Length > 0) languages.Add((name, count));
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Nexus", "asking Nexus for the language list", ex); }
		return languages;
	}

	// -------------------------------------------------------------------------
	// NXM protocol download
	// -------------------------------------------------------------------------

	/// <summary>
	/// Resolves an <c>nxm://</c> URL to an actual CDN download URI and the real file name.
	/// </summary>
	/// <returns>
	/// A tuple of (downloadUri, fileName, displayName): the file name is what the archive is saved as, and the
	/// display name is what the mod is called when the manager talks about it. They are not the same thing — see
	/// <see cref="ModDisplayName"/>.
	/// </returns>
	/// <exception cref="Exception">Thrown if the API call fails or the response is malformed.</exception>
	public async Task<(string Uri, string FileName, string DisplayName)> ResolveNxmUrlAsync(string nxmUrl)
	{
		if (!NxmLink.TryParse(nxmUrl, out NxmLink link))
			throw new ArgumentException($"'{nxmUrl}' is not a download link this manager understands.", nameof(nxmUrl));

		string modId  = link.ModId;
		string fileId = link.FileId;

		// The game comes from the link, never from the loaded session — see NxmLink. Asking under the wrong game's
		// name is what produced the "Current JsonReader item is not an array" report: Nexus answers an unknown
		// mod-in-game pairing with an error object, not the array of download servers.
		string linkUrl = $"https://api.nexusmods.com/v1/games/{link.GameDomain}/mods/{modId}/files/{fileId}/download_link.json{link.Query}";
		using var linkReq = BuildRequest(HttpMethod.Get, linkUrl);
		string body = await (await HttpClient.SendAsync(linkReq)).Content.ReadAsStringAsync();

		// A success is an array of mirrors. Anything else is Nexus explaining itself — an expired key, a file that
		// needs the website, a mod that is not in that game — and its own words beat a parser error every time.
		JToken answer;
		try { answer = JToken.Parse(body); }
		catch (JsonReaderException) { throw new Exception("Nexus sent a reply that could not be read. Try the download again."); }

		if (answer is not JArray mirrors || mirrors.Count == 0)
		{
			string? explanation = (answer as JObject)?["message"]?.ToString();
			throw new Exception(string.IsNullOrWhiteSpace(explanation)
				? "Nexus did not offer a download for that file. The link may have expired — press Mod Manager Download again."
				: $"Nexus refused the download: {explanation}");
		}

		string dlUri = mirrors[0]["URI"]?.ToString() ?? "";

		// Extract the real file name from the resolved CDN URL to avoid the 30-second timeout of files.json
		string? realName = null;
		if (!string.IsNullOrEmpty(dlUri))
		{
			try
			{
				Uri dlParsed = new Uri(dlUri);
				realName = Path.GetFileName(dlParsed.LocalPath);
			}
			catch (Exception ex)
			{
				// The name is only a nicety here — the download itself is unaffected, and the caller falls back to
				// naming the file after the mod.
				DiagnosticLog.WriteException("Nexus", $"reading the file name out of the download address {dlUri}", ex);
			}
		}

		// What to call the mod out loud. Usually the file name says it, but some content servers answer with an
		// opaque id and nothing else — "99824770-6ed9-4868-9f98-b54fb58ecad6" — which must never be read out as
		// though it were a name. That is the only case worth a second API call, so it is the only case that makes
		// one: the mod page's own name is fetched, and every other download stays as fast as it was.
		string display = ModDisplayName.Clean(realName, modId);
		if (display.Length == 0)
		{
			JObject? details = await GetModDetailsAsync(modId, link.GameDomain);
			display = details?["name"]?.ToString()?.Trim() ?? "";
		}
		if (display.Length == 0) display = $"mod {modId}";

		// The file keeps whatever name the server gave it: the mod id and version inside a Nexus file name are what
		// later tell the update check which release is installed. Only a name that is unusable as a file — nameless,
		// or with no extension for Downloads History to recognise — is replaced, and then it is built from the mod's
		// name so the downloads folder stays readable.
		if (string.IsNullOrEmpty(realName) || Path.GetExtension(realName).Length == 0)
		{
			// Underscores, never "-<modId>-": that shape is how a Nexus file name states its version, and inventing
			// one here would record a version this download never had.
			realName = $"{SanitiseFileName(display)}_{modId}_file_{fileId}.zip";
		}

		return (dlUri, realName, display);
	}

	/// <summary>Strips the characters Windows will not accept in a file name, for a name built from a mod's title.</summary>
	private static string SanitiseFileName(string name)
	{
		foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, ' ');
		return name.Replace('.', ' ').Trim();
	}

	/// <summary>Downloads the raw bytes at <paramref name="uri"/> using a high-timeout HTTP client.</summary>
	public async Task<byte[]> DownloadBytesAsync(string uri)
	{
		using var handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
		};
		using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
		client.DefaultRequestHeaders.Add("User-Agent", $"KinetixModManager/{AppVersion}");
		return await client.GetByteArrayAsync(uri);
	}

	/// <summary>
	/// Downloads the file at <paramref name="uri"/> directly to <paramref name="destinationPath"/>
	/// while reporting progress to <paramref name="progress"/>.
	/// </summary>
	public async Task DownloadFileWithProgressAsync(string uri, string destinationPath, IProgress<double>? progress = null)
	{
		using var handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
		};
		using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
		client.DefaultRequestHeaders.Add("User-Agent", $"KinetixModManager/{AppVersion}");

		try
		{
			using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
			response.EnsureSuccessStatusCode();

			long? totalBytes = response.Content.Headers.ContentLength;
			using var contentStream = await response.Content.ReadAsStreamAsync();
			using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

			var buffer = new byte[8192];
			long totalRead = 0;
			// HttpClient.Timeout only covers receiving the response headers (we use ResponseHeadersRead); it does
			// NOT cover reading the content stream. So a CDN that stalls mid-transfer would hang ReadAsync forever
			// (the classic "stuck at 94%"). Enforce our own per-read watchdog: if no bytes arrive within this
			// window, abort with a timeout the caller can report instead of hanging indefinitely.
			TimeSpan stallTimeout = TimeSpan.FromSeconds(90);
			while (true)
			{
				int read;
				using (var cts = new CancellationTokenSource(stallTimeout))
				{
					try { read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token); }
					catch (OperationCanceledException)
					{
						throw new TimeoutException($"The download stalled — no data was received for {stallTimeout.TotalSeconds:0} seconds. Check your connection and try the download again.");
					}
				}
				if (read <= 0) break;

				await fileStream.WriteAsync(buffer.AsMemory(0, read));
				totalRead += read;
				if (totalBytes.HasValue && progress != null)
				{
					double pct = (double)totalRead / totalBytes.Value * 100.0;
					progress.Report(pct);
				}
			}
		}
		catch
		{
			// Don't leave a half-written file behind for a later install to pick up as if it were complete.
			try { if (File.Exists(destinationPath)) File.Delete(destinationPath); }
			catch (Exception ex) { DiagnosticLog.WriteException("Nexus", $"removing the partial download {destinationPath}", ex); }
			throw;
		}
	}

	// -------------------------------------------------------------------------
	// Automated mod updates (premium only)
	// -------------------------------------------------------------------------

	/// <summary>
	/// Downloads the latest file for <paramref name="mod"/> from the Nexus REST API,
	/// saves it to <paramref name="downloadsPath"/>, and returns the saved file path.
	/// Uses a dedicated <see cref="HttpClient"/> with a 10-minute timeout for large files.
	/// </summary>
	/// <returns>The absolute path to the downloaded zip file.</returns>
	/// <exception cref="Exception">Thrown on any API or I/O failure.</exception>
	public async Task<string> DownloadModUpdateAsync(GameMod mod, string downloadsPath, IProgress<double>? progress = null)
	{
		using var handler = new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
		};
		using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
		client.DefaultRequestHeaders.Add("apikey", _settings.ApiKey);
		client.DefaultRequestHeaders.Add("User-Agent", $"KinetixModManager/{AppVersion}");
		client.DefaultRequestHeaders.Add("Accept", "application/json");

		// 1. Get file list
		string filesUrl = $"https://api.nexusmods.com/v1/games/{CurrentGameDomain}/mods/{mod.NexusID}/files.json";
		var filesResp = await client.GetAsync(filesUrl);
		if (!filesResp.IsSuccessStatusCode)
			throw new Exception($"Nexus rejected the file list request (Status: {filesResp.StatusCode}).");

		JObject filesData = JObject.Parse(await filesResp.Content.ReadAsStringAsync());
		var files = filesData["files"] as JArray;
		JToken? selectedFile = (files != null && files.Count > 0) ? SelectUpdateFile(files, mod) : null;

		if (selectedFile == null)
			throw new Exception("No files found on the Nexus page.");

		string fileId   = selectedFile["file_id"]?.ToString() ?? "";
		string fileName = selectedFile["file_name"]?.ToString() ?? $"{mod.NexusID}_update.zip";

		// 2. Get download link
		string dlUrl = $"https://api.nexusmods.com/v1/games/{CurrentGameDomain}/mods/{mod.NexusID}/files/{fileId}/download_link.json";
		var linkResp = await client.GetAsync(dlUrl);
		if (!linkResp.IsSuccessStatusCode)
			throw new Exception("Nexus denied the download link. This mod might require manual interaction on the website.");

		string finalUri = JArray.Parse(await linkResp.Content.ReadAsStringAsync())[0]["URI"]?.ToString() ?? "";

		// 3. Download and save using streams
		string tempPath = Path.Combine(downloadsPath, fileName);
		using (var response = await client.GetAsync(finalUri, HttpCompletionOption.ResponseHeadersRead))
		{
			response.EnsureSuccessStatusCode();
			long? totalBytes = response.Content.Headers.ContentLength;
			using var contentStream = await response.Content.ReadAsStreamAsync();
			using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

			var buffer = new byte[8192];
			long totalRead = 0;
			// As in DownloadFileWithProgressAsync: HttpClient.Timeout doesn't cover content-stream reads, so guard
			// each read with a stall watchdog to avoid hanging forever on a stalled CDN connection.
			TimeSpan stallTimeout = TimeSpan.FromSeconds(90);
			while (true)
			{
				int read;
				using (var cts = new CancellationTokenSource(stallTimeout))
				{
					try { read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token); }
					catch (OperationCanceledException)
					{
						throw new TimeoutException($"The download stalled — no data was received for {stallTimeout.TotalSeconds:0} seconds. Check your connection and try the update again.");
					}
				}
				if (read <= 0) break;

				await fileStream.WriteAsync(buffer.AsMemory(0, read));
				totalRead += read;
				if (totalBytes.HasValue && progress != null)
				{
					double pct = (double)totalRead / totalBytes.Value * 100.0;
					progress.Report(pct);
				}
			}
		}
		return tempPath;
	}

	/// <summary>
	/// Every file on a mod's Files tab.
	///
	/// Open to <b>every</b> account, premium or not — it is only <c>download_link.json</c> that is premium-gated.
	/// That distinction is what lets the manager work out precisely which file a free user needs and send them
	/// straight to it, instead of opening the Files tab and leaving them to pick the right build by hand.
	///
	/// Returns an empty list rather than throwing: not knowing the file list is a reason to fall back to opening
	/// the mod page, not a reason to fail an install the user asked for.
	/// </summary>
	public async Task<List<NexusFileInfo>> GetModFilesAsync(string modId)
	{
		try
		{
			using var req = BuildRequest(HttpMethod.Get,
				$"https://api.nexusmods.com/v1/games/{CurrentGameDomain}/mods/{modId}/files.json");
			using var resp = await HttpClient.SendAsync(req);
			CaptureRateLimit(resp);
			if (!resp.IsSuccessStatusCode) return new List<NexusFileInfo>();

			return ModPartRules.ParseFilesJson(await resp.Content.ReadAsStringAsync());
		}
		catch
		{
			return new List<NexusFileInfo>();
		}
	}

	/// <summary>
	/// Downloads one named file from a mod page — the file the caller has already decided on, rather than
	/// whichever one <see cref="SelectUpdateFile"/> would guess at.
	///
	/// Premium only, because <c>download_link.json</c> is: a free account gets a 403 here and must go through
	/// the website so Nexus can mint the key an <c>nxm://</c> link carries. Callers check
	/// <see cref="IsPremium"/> first and send everyone else to <see cref="ModPartRules.FilePageUrl"/>.
	/// </summary>
	public async Task<string> DownloadFileByIdAsync(
		string modId, string fileId, string fileName, string downloadsPath, IProgress<double>? progress = null)
	{
		using var req = BuildRequest(HttpMethod.Get,
			$"https://api.nexusmods.com/v1/games/{CurrentGameDomain}/mods/{modId}/files/{fileId}/download_link.json");
		using var linkResp = await HttpClient.SendAsync(req);
		if (!linkResp.IsSuccessStatusCode)
			throw new Exception("Nexus denied the download link. This mod might require manual interaction on the website.");

		string uri = JArray.Parse(await linkResp.Content.ReadAsStringAsync())[0]["URI"]?.ToString() ?? "";
		if (string.IsNullOrEmpty(uri)) throw new Exception("Nexus returned no download address for that file.");

		string safeName = string.IsNullOrEmpty(fileName) ? $"{modId}_{fileId}.zip" : fileName;
		foreach (char c in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(c, '_');

		Directory.CreateDirectory(downloadsPath);
		string destination = Path.Combine(downloadsPath, safeName);
		await DownloadFileWithProgressAsync(uri, destination, progress);
		return destination;
	}

	/// <summary>
	/// Fetches details for a specific mod from the Nexus Mods API.
	/// </summary>
	/// <param name="nexusId">Nexus Mods numeric mod ID.</param>
	/// <param name="gameDomain">
	/// The game to ask about, for callers holding a mod that is not from the loaded session — a download started
	/// from the browser for another game. Defaults to the active game, which is what every other caller wants.
	/// </param>
	public async Task<JObject?> GetModDetailsAsync(string nexusId, string? gameDomain = null)
	{
		await _apiSemaphore.WaitAsync();
		try
		{
			using var req = BuildRequest(HttpMethod.Get,
				$"https://api.nexusmods.com/v1/games/{(string.IsNullOrEmpty(gameDomain) ? CurrentGameDomain : gameDomain)}/mods/{nexusId}.json");
			var resp = await HttpClient.SendAsync(req);
			CaptureRateLimit(resp);
			if (!resp.IsSuccessStatusCode) return null;
			return JObject.Parse(await resp.Content.ReadAsStringAsync());
		}
		catch { return null; }
		finally { _apiSemaphore.Release(); }
	}

	/// <summary>
	/// Fetches the numeric mod ids the connected user is tracking on Nexus, filtered to <paramref name="domain"/>
	/// (the tracked list spans every game, so it is filtered here to the active game). Returns an empty list on any
	/// failure. One API call regardless of how many mods are tracked.
	/// </summary>
	public async Task<List<int>> GetTrackedModIdsAsync(string domain)
	{
		await _apiSemaphore.WaitAsync();
		try
		{
			using var req = BuildRequest(HttpMethod.Get, "https://api.nexusmods.com/v1/user/tracked_mods.json");
			var resp = await HttpClient.SendAsync(req);
			CaptureRateLimit(resp);
			if (!resp.IsSuccessStatusCode) return new List<int>();

			var ids = new List<int>();
			foreach (JToken t in JArray.Parse(await resp.Content.ReadAsStringAsync()))
			{
				if (!string.Equals((string?)t["domain_name"], domain, StringComparison.OrdinalIgnoreCase)) continue;
				if ((int?)t["mod_id"] is int id) ids.Add(id);
			}
			return ids;
		}
		catch { return new List<int>(); }
		finally { _apiSemaphore.Release(); }
	}

	/// <summary>
	/// Fetches the set of mod ids for the active game that Nexus updated within <paramref name="period"/> ("1d",
	/// "1w", or "1m"). One API call that lets the tracked-mods check find recently-updated mods without a per-mod
	/// request. Returns an empty set on any failure.
	/// </summary>
	public async Task<HashSet<int>> GetRecentlyUpdatedModIdsAsync(string period)
	{
		await _apiSemaphore.WaitAsync();
		try
		{
			using var req = BuildRequest(HttpMethod.Get,
				$"https://api.nexusmods.com/v1/games/{CurrentGameDomain}/mods/updated.json?period={period}");
			var resp = await HttpClient.SendAsync(req);
			CaptureRateLimit(resp);
			var set = new HashSet<int>();
			if (!resp.IsSuccessStatusCode) return set;

			foreach (JToken u in JArray.Parse(await resp.Content.ReadAsStringAsync()))
				if ((int?)u["mod_id"] is int id) set.Add(id);
			return set;
		}
		catch { return new HashSet<int>(); }
		finally { _apiSemaphore.Release(); }
	}

	/// <summary>
	/// Fetches a mod's changelogs from Nexus — a JSON object keyed by version, each value an array of change
	/// lines (e.g. <c>{ "1.2.0": ["Fixed X", "Added Y"], "1.1.0": [...] }</c>). Returns null on any failure.
	/// </summary>
	public async Task<JObject?> GetChangelogsAsync(string nexusId)
	{
		await _apiSemaphore.WaitAsync();
		try
		{
			using var req = BuildRequest(HttpMethod.Get,
				$"https://api.nexusmods.com/v1/games/{CurrentGameDomain}/mods/{nexusId}/changelogs.json");
			var resp = await HttpClient.SendAsync(req);
			CaptureRateLimit(resp);
			if (!resp.IsSuccessStatusCode) return null;
			return JObject.Parse(await resp.Content.ReadAsStringAsync());
		}
		catch { return null; }
		finally { _apiSemaphore.Release(); }
	}

	/// <summary>One mod listed on another mod's Nexus "Requirements" tab.</summary>
	public sealed class ModRequirementInfo
	{
		/// <summary>Nexus numeric mod id of the required mod (empty for off-Nexus/external requirements).</summary>
		public string ModId { get; set; } = "";
		/// <summary>Display name of the required mod.</summary>
		public string ModName { get; set; } = "";
		/// <summary>Page/download URL for the requirement.</summary>
		public string Url { get; set; } = "";
		/// <summary>The mod author's note about this requirement (e.g. "only for VR" or "or SKSE"); may be empty.</summary>
		public string Notes { get; set; } = "";
		/// <summary>True when the requirement is hosted off Nexus (we can't check whether it's installed).</summary>
		public bool External { get; set; }
	}

	/// <summary>
	/// Reads the mods listed on <paramref name="nexusId"/>'s Nexus "Requirements" tab via the v2 GraphQL
	/// <c>modRequirements.nexusRequirements</c> field. Returns an empty list on any failure (no key, network
	/// error, or a mod with no declared requirements), so callers can treat it as best-effort.
	/// </summary>
	public async Task<List<ModRequirementInfo>> GetModRequirementsAsync(string nexusId)
	{
		var result = new List<ModRequirementInfo>();
		if (string.IsNullOrEmpty(nexusId) || string.IsNullOrEmpty(_settings.ApiKey)) return result;
		if (!int.TryParse(nexusId, out int modIdNum) || !int.TryParse(CurrentGameId, out int gameIdNum)) return result;

		// modId/gameId are integers we control, so inline them — no user input, no injection surface.
		string query = $"query {{ mod(modId: {modIdNum}, gameId: {gameIdNum}) {{ modRequirements {{ nexusRequirements {{ nodes {{ modId modName url externalRequirement notes }} }} }} }} }}";

		await _apiSemaphore.WaitAsync();
		try
		{
			using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.nexusmods.com/v2/graphql");
			req.Headers.Add("apikey", _settings.ApiKey);
			req.Headers.Add("User-Agent", $"KinetixModManager/{AppVersion}");
			req.Content = new StringContent(JsonConvert.SerializeObject(new { query }), Encoding.UTF8, "application/json");

			var resp = await HttpClient.SendAsync(req);
			if (!resp.IsSuccessStatusCode) return result;

			JObject data = JObject.Parse(await resp.Content.ReadAsStringAsync());
			JArray nodes = (data["data"]?["mod"]?["modRequirements"]?["nexusRequirements"]?["nodes"] as JArray) ?? new JArray();
			foreach (JToken n in nodes)
			{
				result.Add(new ModRequirementInfo
				{
					ModId    = n["modId"]?.ToString() ?? "",
					ModName  = n["modName"]?.ToString() ?? "",
					Url      = n["url"]?.ToString() ?? "",
					Notes    = n["notes"]?.ToString() ?? "",
					External = ((bool?)n["externalRequirement"]) ?? false
				});
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Nexus", "asking Nexus for a mod's requirements", ex); }
		finally { _apiSemaphore.Release(); }
		return result;
	}

	// -------------------------------------------------------------------------
	// App self-update
	// -------------------------------------------------------------------------

	/// <summary>
	/// Checks the GitHub releases API for the latest Kinetix Mod Manager release information.
	/// </summary>
	public async Task<AppReleaseInfo?> GetLatestAppReleaseAsync()
	{
		try
		{
			using var req = new HttpRequestMessage(HttpMethod.Get,
				"https://api.github.com/repos/SeanTerry01/Kinetix-Mod-Manager/releases/latest");
			req.Headers.UserAgent.ParseAdd($"KinetixModManager/{AppVersion}");
			using var resp = await HttpClient.SendAsync(req);
			if (!resp.IsSuccessStatusCode) return null;

			var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
			string? tag = json["tag_name"]?.ToString();
			if (tag == null) return null;

			var info = new AppReleaseInfo { TagName = tag };

			var assets = json["assets"] as JArray;
			if (assets != null)
			{
				foreach (var token in assets)
				{
					if (token is JObject assetObj)
					{
						string? name = assetObj["name"]?.ToString();
						if (name != null && (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
						{
							info.DownloadUrl = assetObj["browser_download_url"]?.ToString() ?? "";
							info.FileName = name;
							break;
						}
					}
				}
			}

			return info;
		}
		catch { return null; }
	}

	public class AppReleaseInfo
	{
		public string TagName { get; set; } = "";
		public string DownloadUrl { get; set; } = "";
		public string FileName { get; set; } = "";
	}

	// -------------------------------------------------------------------------
	// Endorsements
	// -------------------------------------------------------------------------

	/// <summary>The outcome of an endorse/abstain attempt, so the caller can speak the right message.</summary>
	public enum EndorseOutcome
	{
		/// <summary>The mod was endorsed.</summary>
		Endorsed,
		/// <summary>Endorsement was withdrawn (abstained).</summary>
		Abstained,
		/// <summary>Nexus requires the mod to have been used a while before endorsing (TOO_SOON_AFTER_DOWNLOAD).</summary>
		TooSoon,
		/// <summary>Nexus has no record of this account downloading the mod (NOT_DOWNLOADED_MOD).</summary>
		NotDownloaded,
		/// <summary>You cannot endorse your own mod (IS_OWN_MOD).</summary>
		OwnMod,
		/// <summary>No API key, no network, or an unrecognised error.</summary>
		Failed
	}

	/// <summary>
	/// Returns whether the account currently endorses <paramref name="nexusId"/>: <c>true</c> if endorsed,
	/// <c>false</c> if not (undecided or abstained), or <c>null</c> if the status can't be determined (no key,
	/// network error, or a response without an endorsement field). Reads the <c>endorsement.endorse_status</c>
	/// field of the mod details, so it costs one API request. Lets the toggle decide which way to flip.
	/// </summary>
	public async Task<bool?> IsModEndorsedAsync(string nexusId)
	{
		JObject? details = await GetModDetailsAsync(nexusId);
		string? status = details?["endorsement"]?["endorse_status"]?.ToString();
		if (string.IsNullOrEmpty(status)) return null;
		return status.Equals("Endorsed", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Endorses (or abstains from) a mod via the Nexus v1 REST API. Nexus only allows endorsing a mod the
	/// account has downloaded and used for a short while, so the distinct <see cref="EndorseOutcome"/> values
	/// let the caller explain a refusal rather than just failing silently. Best-effort: any network/parse
	/// failure returns <see cref="EndorseOutcome.Failed"/>.
	/// </summary>
	/// <param name="nexusId">Nexus numeric mod id.</param>
	/// <param name="endorse">True to endorse, false to withdraw a prior endorsement.</param>
	/// <param name="version">The installed mod version (Nexus records the endorsement against it).</param>
	public async Task<EndorseOutcome> SetEndorsementAsync(string nexusId, bool endorse, string? version = null)
	{
		if (string.IsNullOrEmpty(nexusId) || string.IsNullOrEmpty(_settings.ApiKey)) return EndorseOutcome.Failed;

		await _apiSemaphore.WaitAsync();
		try
		{
			string action = endorse ? "endorse" : "abstain";
			using var req = BuildRequest(HttpMethod.Post,
				$"https://api.nexusmods.com/v1/games/{CurrentGameDomain}/mods/{nexusId}/{action}.json");
			req.Content = new StringContent(
				JsonConvert.SerializeObject(new { version = string.IsNullOrEmpty(version) ? "1.0.0" : version }),
				Encoding.UTF8, "application/json");

			var resp = await HttpClient.SendAsync(req);
			CaptureRateLimit(resp);
			string body = await resp.Content.ReadAsStringAsync();
			if (resp.IsSuccessStatusCode)
				return endorse ? EndorseOutcome.Endorsed : EndorseOutcome.Abstained;

			// A refusal comes back as { "message": "CODE" }; map the documented codes to outcomes.
			string code = "";
			try { code = JObject.Parse(body)["message"]?.ToString() ?? ""; }
			catch (Exception ex) { DiagnosticLog.WriteException("Nexus", "reading the endorsement response", ex); }
			return code.ToUpperInvariant() switch
			{
				"TOO_SOON_AFTER_DOWNLOAD" => EndorseOutcome.TooSoon,
				"NOT_DOWNLOADED_MOD"      => EndorseOutcome.NotDownloaded,
				"IS_OWN_MOD"              => EndorseOutcome.OwnMod,
				_                         => EndorseOutcome.Failed
			};
		}
		catch { return EndorseOutcome.Failed; }
		finally { _apiSemaphore.Release(); }
	}

	// -------------------------------------------------------------------------
	// Helpers
	// -------------------------------------------------------------------------

	/// <summary>
	/// Records the account's remaining API quota from a v1 REST response's <c>x-rl-*</c> headers. Called after
	/// any v1 request; the v2 GraphQL endpoint does not send these, so it is not used there. Missing headers
	/// leave the previous value untouched.
	/// </summary>
	private void CaptureRateLimit(HttpResponseMessage resp)
	{
		HourlyRemaining = ReadIntHeader(resp, "x-rl-hourly-remaining", HourlyRemaining);
		HourlyLimit     = ReadIntHeader(resp, "x-rl-hourly-limit",     HourlyLimit);
		DailyRemaining  = ReadIntHeader(resp, "x-rl-daily-remaining",  DailyRemaining);
		DailyLimit      = ReadIntHeader(resp, "x-rl-daily-limit",      DailyLimit);
	}

	/// <summary>Reads a single integer response header, returning <paramref name="fallback"/> if absent/unparseable.</summary>
	private static int ReadIntHeader(HttpResponseMessage resp, string name, int fallback)
	{
		if (resp.Headers.TryGetValues(name, out var values))
			foreach (string v in values)
				if (int.TryParse(v, out int n)) return n;
		return fallback;
	}

	/// <summary>
	/// Narrows a mod page's files to the ones built for the store this copy of the game came from, where the page
	/// tells them apart at all.
	///
	/// A page that ships one file per store is a trap for every "just take the main download" rule: the file the
	/// author flags as primary is the Steam one, so a GOG copy is handed a build compiled against an exe it does
	/// not have, which installs without complaint and then never loads. The updater walks into that as readily as
	/// the first install did.
	///
	/// Only where the page actually splits, and only where the store is known — otherwise the list is returned
	/// untouched, so a page with one build for everybody behaves exactly as it always has.
	/// </summary>
	private List<JToken> KeepThisCopysStore(List<JToken> candidates)
	{
		GamePlatform platform = _settings.InstallFor(_settings.ActiveGame)?.Platform
			?? GameProfiles.PlatformOf(_settings.ActiveGame);
		// The file's NAME only, never its description — KeepStoreBuilds explains at length why, and the update
		// path and the part picker ask the same question of the same pages, so they ask it in one place.
		return ModPartRules.KeepStoreBuilds(candidates, platform, f => $"{f["name"]} {f["file_name"]}");
	}

	/// <summary>
	/// Chooses which file of a mod's Nexus file list to download for an update. The Nexus API refuses to generate
	/// a download link for <b>archived</b> files (their <c>category_name</c> is null/empty), which is the usual
	/// cause of "Nexus denied the download link" — so those are excluded first. Among the rest it prefers, in
	/// order: a genuine Part 1 / Part 2 file when the mod name says so (mods that ship two separate downloads on
	/// one page); the author-flagged primary file; a MAIN file matching the known latest version, then the newest
	/// MAIN file; and finally the newest remaining file. This replaces a naive "take files[0]", which could land
	/// on an old or archived file and get denied.
	///
	/// Before any of that, the list is narrowed to this copy's store where the page splits by one — see
	/// <see cref="KeepThisCopysStore"/>. It has to come first, because the author-flagged primary file is the
	/// Steam one and every rule below would otherwise reach it before the store was ever considered.
	/// </summary>
	private JToken? SelectUpdateFile(JArray files, GameMod mod)
	{
		var candidates = files
			.Where(f => !string.IsNullOrEmpty(f["category_name"]?.ToString()))
			.ToList();
		if (candidates.Count == 0) candidates = files.ToList(); // nothing categorised: fall back to the raw list

		candidates = KeepThisCopysStore(candidates);

		// Honour the Part 1 / Part 2 naming convention for mods that genuinely ship two separate downloads.
		if (mod.Name.Contains("Part 2", StringComparison.OrdinalIgnoreCase))
		{
			JToken? part2 = candidates.FirstOrDefault(f => FileMentions(f, "Part 2", "Part2", "Preloader"));
			if (part2 != null) return part2;
		}
		else if (mod.Name.Contains("Part 1", StringComparison.OrdinalIgnoreCase))
		{
			JToken? part1 = candidates.FirstOrDefault(f => FileMentions(f, "Part 1", "Part1"));
			if (part1 != null) return part1;
		}

		// The author-flagged primary file is the safest single "main download".
		JToken? primary = candidates.FirstOrDefault(f => (bool?)f["is_primary"] == true);
		if (primary != null) return primary;

		// Otherwise prefer a MAIN-category file matching the latest known version, then the newest MAIN file.
		var mainFiles = candidates
			.Where(f => string.Equals(f["category_name"]?.ToString(), "MAIN", StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (mainFiles.Count > 0)
		{
			if (!string.IsNullOrEmpty(mod.LatestVersion))
			{
				JToken? versionMatch = mainFiles.FirstOrDefault(f =>
					string.Equals(f["version"]?.ToString(),     mod.LatestVersion, StringComparison.OrdinalIgnoreCase) ||
					string.Equals(f["mod_version"]?.ToString(), mod.LatestVersion, StringComparison.OrdinalIgnoreCase));
				if (versionMatch != null) return versionMatch;
			}
			return mainFiles.OrderByDescending(f => (long?)f["uploaded_timestamp"] ?? 0L).First();
		}

		// No MAIN file at all: take the newest non-archived file.
		return candidates.OrderByDescending(f => (long?)f["uploaded_timestamp"] ?? 0L).First();
	}

	/// <summary>True if any of <paramref name="needles"/> appears in a file's name, file_name, or description.</summary>
	private static bool FileMentions(JToken file, params string[] needles)
	{
		string haystack = (file["name"]?.ToString() ?? "") + " " +
						  (file["file_name"]?.ToString() ?? "") + " " +
						  (file["description"]?.ToString() ?? "");
		return needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Builds an authenticated <see cref="HttpRequestMessage"/> with the API key and User-Agent headers set.
	/// </summary>
	private HttpRequestMessage BuildRequest(HttpMethod method, string url)
	{
		var req = new HttpRequestMessage(method, url);
		req.Headers.Add("apikey", _settings.ApiKey);
		req.Headers.Add("User-Agent", $"KinetixModManager/{AppVersion}");
		return req;
	}
}
