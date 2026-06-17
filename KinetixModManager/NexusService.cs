using System;
using System.Collections.Generic;
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
	}

	public string CurrentGameDomain => _settings.ActiveGame switch
	{
		"SkyrimSE" => "skyrimspecialedition",
		"Fallout4" => "fallout4",
		_ => "stardewvalley"
	};

	public string CurrentGameId => _settings.ActiveGame switch
	{
		"SkyrimSE" => "1704",
		"Fallout4" => "1151",
		_ => "1303"
	};

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
			if (!resp.IsSuccessStatusCode) return false;
			JObject json = JObject.Parse(await resp.Content.ReadAsStringAsync());
			NexusUser = json["name"]?.ToString() ?? "User";
			IsPremium  = (bool)(json["is_premium"] ?? (JToken)false);
			return true;
		}
		catch { return false; }
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
			if (!resp.IsSuccessStatusCode) return null;
			return ((string?)JObject.Parse(await resp.Content.ReadAsStringAsync())["version"]) ?? "0";
		}
		catch { return null; }
		finally { _apiSemaphore.Release(); }
	}

	/// <summary>One mod's suggested update as resolved by the SMAPI web API.</summary>
	public readonly record struct SmapiUpdate(string Version, string Url);

	/// <summary>
	/// Queries the SMAPI web API (smapi.io) for available updates, exactly as SMAPI itself does. Unlike the
	/// per-mod Nexus check, this resolves updates by each mod's UniqueID against SMAPI's crowdsourced mod
	/// database, so it also finds mods whose <c>manifest.json</c> has a missing or broken update key (for
	/// example <c>Nexus:???</c> or no key at all). Returns a map of mod UniqueID (case-insensitive) to its
	/// suggested update, or <c>null</c> if the service could not be reached — in which case callers fall back
	/// to the manifest-based Nexus check. Include an entry with id "SMAPI" to also receive SMAPI's own update.
	/// </summary>
	/// <param name="mods">Installed mods as (UniqueID, installed version, update keys) tuples.</param>
	/// <param name="smapiVersion">Installed SMAPI version, sent as the API's apiVersion (must be valid semver).</param>
	/// <param name="gameVersion">Installed Stardew Valley version, used by the API to filter compatible updates.</param>
	public async Task<Dictionary<string, SmapiUpdate>?> GetSmapiUpdatesAsync(
		IEnumerable<(string Id, string Version, IEnumerable<string> UpdateKeys)> mods,
		string smapiVersion, string gameVersion)
	{
		try
		{
			var modArray = new JArray();
			foreach (var m in mods)
			{
				if (string.IsNullOrEmpty(m.Id)) continue;
				var keys = new JArray();
				foreach (string k in m.UpdateKeys)
					if (!string.IsNullOrWhiteSpace(k)) keys.Add(k);

				modArray.Add(new JObject
				{
					["id"]               = m.Id,
					["updateKeys"]       = keys,
					["installedVersion"] = string.IsNullOrEmpty(m.Version) ? "0.0.0" : m.Version,
					["isBroken"]         = false
				});
			}

			var body = new JObject
			{
				["mods"]                    = modArray,
				["apiVersion"]              = string.IsNullOrEmpty(smapiVersion) ? "4.0.0" : smapiVersion,
				["gameVersion"]             = string.IsNullOrEmpty(gameVersion) ? "1.6.15" : gameVersion,
				["platform"]                = "Windows",
				["includeExtendedMetadata"] = true
			};

			using var req = new HttpRequestMessage(HttpMethod.Post, "https://smapi.io/api/v3.0/mods");
			req.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
			req.Headers.UserAgent.ParseAdd($"KinetixModManager/{AppVersion}");

			using var resp = await HttpClient.SendAsync(req);
			if (!resp.IsSuccessStatusCode) return null;

			var arr = JArray.Parse(await resp.Content.ReadAsStringAsync());
			var result = new Dictionary<string, SmapiUpdate>(StringComparer.OrdinalIgnoreCase);
			foreach (JToken entry in arr)
			{
				string? id = (string?)entry["id"];
				JToken? suggested = entry["suggestedUpdate"];
				if (string.IsNullOrEmpty(id) || suggested == null || suggested.Type != JTokenType.Object)
					continue;

				string? version = (string?)suggested["version"];
				string? url = (string?)suggested["url"];
				if (!string.IsNullOrEmpty(version))
					result[id!] = new SmapiUpdate(version!, url ?? "");
			}
			return result;
		}
		catch { return null; }
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
		string searchType, string searchTerm, int page, int pageSize, string? language = null)
	{
		int baseOffset = (page - 1) * pageSize;
		var all = new List<GameMod>();
		int total = 0;

		// Fetch in chunks no larger than the API cap until we've gathered the requested page size or run out.
		while (all.Count < pageSize)
		{
			int chunk = Math.Min(MaxModsPerRequest, pageSize - all.Count);
			var (results, t) = await FetchModsPageAsync(searchType, searchTerm, baseOffset + all.Count, chunk, language);
			if (t > 0) total = t;               // keep a known total if a later chunk fails/returns nothing
			all.AddRange(results);
			if (results.Count < chunk) break;   // reached the end of the available results
		}

		return (all, total);
	}

	/// <summary>Runs a single Nexus GraphQL search request for <paramref name="count"/> mods starting at
	/// <paramref name="offset"/> (<paramref name="count"/> must not exceed <see cref="MaxModsPerRequest"/>).</summary>
	private async Task<(List<GameMod> Results, int Total)> FetchModsPageAsync(
		string searchType, string searchTerm, int offset, int count, string? language = null)
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

		if (searchType == "Search")
		{
			filter["name"] = new[] { new { value = searchTerm, op = "WILDCARD" } };
			gqlQuery = @"query SearchMods($filter: ModsFilter, $count: Int, $offset: Int) {
				mods(filter: $filter, count: $count, offset: $offset) {
					nodes { modId name summary author version }
					totalCount
				}
			}";
			variables = new { filter, count = pageSize, offset };
		}
		else
		{
			string sortField = searchType switch
			{
				"Most Popular" => "downloads",
				"Recent"       => "updatedAt",
				_              => "endorsements"
			};
			gqlQuery = @"query ListMods($filter: ModsFilter, $sort: [ModsSort!], $count: Int, $offset: Int) {
				mods(filter: $filter, sort: $sort, count: $count, offset: $offset) {
					nodes { modId name summary author version }
					totalCount
				}
			}";
			variables = new
			{
				filter,
				sort   = new[] { new Dictionary<string, object> { { sortField, new { direction = "DESC" } } } },
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
					IsSearchResult = true
				});
			}
			return (results, total);
		}
		catch { return (new(), 0); }
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
		catch { /* fall through to whatever was collected (possibly empty) */ }
		return languages;
	}

	// -------------------------------------------------------------------------
	// NXM protocol download
	// -------------------------------------------------------------------------

	/// <summary>
	/// Resolves an <c>nxm://</c> URL to an actual CDN download URI and the real file name.
	/// </summary>
	/// <returns>
	/// A tuple of (downloadUri, fileName), where fileName is the display name from the mod files list.
	/// </returns>
	/// <exception cref="Exception">Thrown if the API call fails or the response is malformed.</exception>
	public async Task<(string Uri, string FileName)> ResolveNxmUrlAsync(string nxmUrl)
	{
		Uri parsed     = new Uri(nxmUrl);
		string[] parts = parsed.AbsolutePath.Split('/');
		string modId   = parts[2];
		string fileId  = parts[4];

		// Get CDN download link
		string linkUrl = $"https://api.nexusmods.com/v1/games/{CurrentGameDomain}/mods/{modId}/files/{fileId}/download_link.json{parsed.Query}";
		using var linkReq = BuildRequest(HttpMethod.Get, linkUrl);
		string dlUri = JArray.Parse(await (await HttpClient.SendAsync(linkReq)).Content.ReadAsStringAsync())
			[0]["URI"]?.ToString() ?? "";

		// Extract the real file name from the resolved CDN URL to avoid the 30-second timeout of files.json
		string? realName = null;
		if (!string.IsNullOrEmpty(dlUri))
		{
			try
			{
				Uri dlParsed = new Uri(dlUri);
				realName = Path.GetFileName(dlParsed.LocalPath);
			}
			catch
			{
				// Ignore
			}
		}

		if (string.IsNullOrEmpty(realName))
		{
			realName = $"{modId}_file_{fileId}.zip";
		}

		return (dlUri, realName);
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

		using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
		response.EnsureSuccessStatusCode();

		long? totalBytes = response.Content.Headers.ContentLength;
		using var contentStream = await response.Content.ReadAsStreamAsync();
		using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

		var buffer = new byte[8192];
		long totalRead = 0;
		int read;
		while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
		{
			await fileStream.WriteAsync(buffer, 0, read);
			totalRead += read;
			if (totalBytes.HasValue && progress != null)
			{
				double pct = (double)totalRead / totalBytes.Value * 100.0;
				progress.Report(pct);
			}
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
		var files = filesData["files"] as Newtonsoft.Json.Linq.JArray;
		Newtonsoft.Json.Linq.JToken? selectedFile = null;

		if (files != null && files.Count > 0)
		{
			if (mod.Name.Contains("Part 2", StringComparison.OrdinalIgnoreCase))
			{
				selectedFile = files.FirstOrDefault(f => 
					(f["name"]?.ToString() ?? "").Contains("Part 2", StringComparison.OrdinalIgnoreCase) ||
					(f["name"]?.ToString() ?? "").Contains("Preloader", StringComparison.OrdinalIgnoreCase) ||
					(f["file_name"]?.ToString() ?? "").Contains("Part 2", StringComparison.OrdinalIgnoreCase) ||
					(f["file_name"]?.ToString() ?? "").Contains("Part2", StringComparison.OrdinalIgnoreCase) ||
					(f["description"]?.ToString() ?? "").Contains("Part 2", StringComparison.OrdinalIgnoreCase)
				);
			}
			else if (mod.Name.Contains("Part 1", StringComparison.OrdinalIgnoreCase))
			{
				selectedFile = files.FirstOrDefault(f => 
					(f["name"]?.ToString() ?? "").Contains("Part 1", StringComparison.OrdinalIgnoreCase) ||
					(f["file_name"]?.ToString() ?? "").Contains("Part 1", StringComparison.OrdinalIgnoreCase) ||
					(f["file_name"]?.ToString() ?? "").Contains("Part1", StringComparison.OrdinalIgnoreCase)
				);
			}
			selectedFile ??= files[0];
		}

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
			int read;
			while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
			{
				await fileStream.WriteAsync(buffer, 0, read);
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
	/// Fetches details for a specific mod from the Nexus Mods API.
	/// </summary>
	public async Task<JObject?> GetModDetailsAsync(string nexusId)
	{
		await _apiSemaphore.WaitAsync();
		try
		{
			using var req = BuildRequest(HttpMethod.Get,
				$"https://api.nexusmods.com/v1/games/{CurrentGameDomain}/mods/{nexusId}.json");
			var resp = await HttpClient.SendAsync(req);
			if (!resp.IsSuccessStatusCode) return null;
			return JObject.Parse(await resp.Content.ReadAsStringAsync());
		}
		catch { return null; }
		finally { _apiSemaphore.Release(); }
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
	// Helpers
	// -------------------------------------------------------------------------

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
