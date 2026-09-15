using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// Queries the official SMAPI mod-compatibility web API (the same service SMAPI itself uses) to find installed
/// Stardew Valley mods that the community wiki flags as broken, obsolete, or abandoned. It is best-effort: any
/// network or parse failure yields no results, so a broken-mod check simply reports nothing rather than erroring.
/// </summary>
public static class SmapiCompatibility
{
	/// <summary>One installed mod's compatibility verdict from the SMAPI list.</summary>
	public sealed class Result
	{
		/// <summary>Raw status word from the wiki: Ok, Optional, Unofficial, Workaround, Broken, Obsolete, Abandoned.</summary>
		public string Status = "";
		/// <summary>Human-readable summary (HTML) explaining the status, e.g. "broken, use X instead".</summary>
		public string Summary = "";
		/// <summary>True for the statuses that mean the mod won't work correctly as-is (Broken/Obsolete/Abandoned).</summary>
		public bool IsProblem =>
			Status.Equals("Broken", StringComparison.OrdinalIgnoreCase) ||
			Status.Equals("Obsolete", StringComparison.OrdinalIgnoreCase) ||
			Status.Equals("Abandoned", StringComparison.OrdinalIgnoreCase);
	}

	private const string ApiUrl = "https://smapi.io/api/v3.0/mods";

	/// <summary>
	/// Looks up the compatibility status of each supplied Stardew mod UniqueID in one request. Returns a map from
	/// the UniqueIDs that matched a wiki entry to their verdict; unmatched ids are omitted. Never throws.
	/// </summary>
	public static async Task<Dictionary<string, Result>> CheckAsync(IEnumerable<string> uniqueIds, Action<string, string> logError)
	{
		var result = new Dictionary<string, Result>(StringComparer.OrdinalIgnoreCase);
		var ids = uniqueIds.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		if (ids.Count == 0) return result;

		try
		{
			var body = new JObject
			{
				["mods"] = new JArray(ids.Select(id => new JObject { ["id"] = id })),
				["includeExtendedMetadata"] = true
			};
			using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
			{
				Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json")
			};
			using HttpResponseMessage response = await NexusService.HttpClient.SendAsync(request);
			if (!response.IsSuccessStatusCode)
			{
				logError("SMAPI", $"Compatibility lookup returned {(int)response.StatusCode}.");
				return result;
			}

			string json = await response.Content.ReadAsStringAsync();
			if (JToken.Parse(json) is not JArray entries) return result;

			foreach (JToken entry in entries)
			{
				// The request id echoes back at the top level; the wiki's matched ids are under metadata.id.
				string requestedId = (string?)entry["id"] ?? "";
				JToken? meta = entry["metadata"];
				string status = (string?)meta?["compatibilityStatus"] ?? "";
				if (string.IsNullOrEmpty(requestedId) || string.IsNullOrEmpty(status)) continue;
				result[requestedId] = new Result
				{
					Status = status,
					Summary = (string?)meta?["compatibilitySummary"] ?? ""
				};
			}
		}
		catch (Exception ex)
		{
			logError("SMAPI", "Compatibility lookup failed: " + ex.Message);
		}
		return result;
	}
}
