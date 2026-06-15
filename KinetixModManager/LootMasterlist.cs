using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YamlDotNet.RepresentationModel;

namespace KinetixModManager;

/// <summary>
/// A best-effort reader for LOOT's community masterlist. It extracts the parts that drive load ordering —
/// the group graph and per-plugin <c>group</c> assignments and <c>after</c> rules — so the auto-sort can
/// layer LOOT's curated ordering on top of the dependency-based sort. It is deliberately NOT a full
/// reimplementation of libloot's engine (no plugin-override priority graph, no user-metadata merging); any
/// parse failure simply yields <c>null</c>, leaving the dependency-only sort in place.
/// </summary>
public class LootMasterlist
{
	/// <summary>Group name → order index (lower loads earlier), from a topological sort of the group graph.</summary>
	public Dictionary<string, int> GroupOrder { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Plugin file name → its assigned group.</summary>
	public Dictionary<string, string> PluginGroup { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Plugin file name → plugins it must load after.</summary>
	public Dictionary<string, List<string>> PluginAfter { get; } = new(StringComparer.OrdinalIgnoreCase);

	private int _defaultGroupIndex;

	/// <summary>The group order index for a plugin (its group's index, or the default group's).</summary>
	public int GroupIndexFor(string pluginName)
	{
		if (PluginGroup.TryGetValue(pluginName, out string? g) && GroupOrder.TryGetValue(g, out int idx))
			return idx;
		return _defaultGroupIndex;
	}

	private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

	/// <summary>
	/// Loads the masterlist for <paramref name="game"/>: refreshes the cached copy from GitHub when online
	/// and stale, then parses the cache. Returns <c>null</c> (so the caller falls back to the dependency
	/// sort) when the game is unsupported, no cache exists, or parsing fails.
	/// </summary>
	public static async Task<LootMasterlist?> LoadAsync(string game, Action<string, string> logError)
	{
		string? repo = game switch
		{
			"SkyrimSE" => "skyrimse",
			"Fallout4" => "fallout4",
			_ => null
		};
		if (repo == null) return null;

		string cacheDir = Path.Combine(AppSettings.AppDataFolder, "masterlists", game);
		string mlPath = Path.Combine(cacheDir, "masterlist.yaml");
		string prePath = Path.Combine(cacheDir, "prelude.yaml");

		try
		{
			Directory.CreateDirectory(cacheDir);
			bool stale = !File.Exists(mlPath) || (DateTime.UtcNow - File.GetLastWriteTimeUtc(mlPath)) > CacheLifetime;
			if (stale)
			{
				await TryDownloadAsync($"https://raw.githubusercontent.com/loot/{repo}/HEAD/masterlist.yaml", mlPath, logError);
				await TryDownloadAsync("https://raw.githubusercontent.com/loot/prelude/HEAD/prelude.yaml", prePath, logError);
			}
		}
		catch (Exception ex) { logError("LOOT", "Masterlist refresh failed: " + ex.Message); }

		if (!File.Exists(mlPath)) return null;

		try
		{
			string masterlistText = File.ReadAllText(mlPath);
			string preludeText = File.Exists(prePath) ? File.ReadAllText(prePath) : "";
			string merged = ApplyPrelude(masterlistText, preludeText);
			return Parse(merged);
		}
		catch (Exception ex)
		{
			logError("LOOT", "Masterlist parse failed: " + ex.Message);
			return null;
		}
	}

	private static async Task TryDownloadAsync(string url, string destPath, Action<string, string> logError)
	{
		try
		{
			using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
			client.DefaultRequestHeaders.UserAgent.ParseAdd($"KinetixModManager/{NexusService.AppVersion}");
			byte[] bytes = await client.GetByteArrayAsync(url);
			if (bytes.Length > 0) File.WriteAllBytes(destPath, bytes);
		}
		catch (Exception ex)
		{
			// Offline or transient failure: keep any existing cached copy and carry on.
			logError("LOOT", $"Could not download {url}: {ex.Message}");
		}
	}

	/// <summary>
	/// Reproduces libloot's prelude substitution: the masterlist's top-level <c>prelude:</c> mapping is
	/// replaced with the contents of the prelude file (which defines the shared YAML anchors the masterlist
	/// references). When the masterlist has no <c>prelude:</c> key, it is returned unchanged.
	/// </summary>
	private static string ApplyPrelude(string masterlist, string prelude)
	{
		if (string.IsNullOrWhiteSpace(prelude)) return masterlist;
		string[] lines = masterlist.Replace("\r\n", "\n").Split('\n');
		var sb = new StringBuilder();
		bool replaced = false;
		int i = 0;
		while (i < lines.Length)
		{
			if (!replaced && Regex.IsMatch(lines[i], @"^prelude:\s*$"))
			{
				sb.Append("prelude:\n");
				foreach (string pl in prelude.Replace("\r\n", "\n").Split('\n'))
					sb.Append("  ").Append(pl).Append('\n');
				replaced = true;
				i++;
				// Skip the masterlist's original (indented) prelude block.
				while (i < lines.Length && (lines[i].Length == 0 || lines[i][0] == ' ' || lines[i][0] == '\t')) i++;
				continue;
			}
			sb.Append(lines[i]).Append('\n');
			i++;
		}
		return sb.ToString();
	}

	private static LootMasterlist Parse(string yamlText)
	{
		var result = new LootMasterlist();
		var stream = new YamlStream();
		stream.Load(new StringReader(yamlText));
		if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
			return result;

		// Groups: build the after-graph, then topologically sort to assign order indices.
		var groupAfter = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		if (TryGet(root, "groups") is YamlSequenceNode groups)
		{
			foreach (YamlNode item in groups)
			{
				if (item is not YamlMappingNode g) continue;
				string? name = ScalarOf(TryGet(g, "name"));
				if (string.IsNullOrEmpty(name)) continue;
				if (!groupAfter.ContainsKey(name)) groupAfter[name] = new List<string>();
				foreach (string a in NameList(TryGet(g, "after"))) groupAfter[name].Add(a);
			}
		}
		AssignGroupOrder(groupAfter, result.GroupOrder);
		result._defaultGroupIndex = result.GroupOrder.TryGetValue("default", out int di) ? di : result.GroupOrder.Count;

		// Plugins: record group assignment and after-rules (exact names only; regex entries are skipped).
		if (TryGet(root, "plugins") is YamlSequenceNode plugins)
		{
			foreach (YamlNode item in plugins)
			{
				if (item is not YamlMappingNode p) continue;
				string? name = ScalarOf(TryGet(p, "name"));
				if (string.IsNullOrEmpty(name) || LooksLikeRegex(name)) continue;
				string? group = ScalarOf(TryGet(p, "group"));
				if (!string.IsNullOrEmpty(group)) result.PluginGroup[name] = group;
				var after = NameList(TryGet(p, "after"));
				if (after.Count > 0) result.PluginAfter[name] = after;
			}
		}
		return result;
	}

	/// <summary>Kahn topological sort of the group graph (edges: a group loads after its listed groups).</summary>
	private static void AssignGroupOrder(Dictionary<string, List<string>> groupAfter, Dictionary<string, int> order)
	{
		var names = groupAfter.Keys.ToList();
		var indeg = names.ToDictionary(n => n, _ => 0, StringComparer.OrdinalIgnoreCase);
		var dependents = names.ToDictionary(n => n, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
		foreach (string g in names)
			foreach (string a in groupAfter[g])
				if (indeg.ContainsKey(a))
				{
					dependents[a].Add(g);
					indeg[g]++;
				}

		var ready = names.Where(n => indeg[n] == 0).ToList();
		int next = 0;
		while (ready.Count > 0)
		{
			ready.Sort(StringComparer.OrdinalIgnoreCase); // deterministic tie-break
			string n = ready[0];
			ready.RemoveAt(0);
			order[n] = next++;
			foreach (string d in dependents[n])
				if (--indeg[d] == 0) ready.Add(d);
		}
		// Any groups left in a cycle: append in name order so they still get an index.
		foreach (string n in names)
			if (!order.ContainsKey(n)) order[n] = next++;
	}

	// --- small YAML navigation helpers (anchors/aliases are already resolved by YamlStream) ---

	private static YamlNode? TryGet(YamlMappingNode map, string key)
	{
		foreach (var kv in map.Children)
			if (kv.Key is YamlScalarNode s && string.Equals(s.Value, key, StringComparison.Ordinal))
				return kv.Value;
		return null;
	}

	private static string? ScalarOf(YamlNode? node) => (node as YamlScalarNode)?.Value;

	/// <summary>An <c>after</c> value is a sequence whose items are either scalars or maps with a <c>name</c>.</summary>
	private static List<string> NameList(YamlNode? node)
	{
		var list = new List<string>();
		if (node is YamlSequenceNode seq)
		{
			foreach (YamlNode item in seq)
			{
				string? name = item is YamlMappingNode m ? ScalarOf(TryGet(m, "name")) : ScalarOf(item);
				if (!string.IsNullOrEmpty(name) && !LooksLikeRegex(name)) list.Add(name);
			}
		}
		return list;
	}

	/// <summary>Heuristic: a masterlist name is a regex unless it is a plain plugin file name.</summary>
	private static bool LooksLikeRegex(string name) =>
		!Regex.IsMatch(name, @"^[^\\/:*?""<>|]+\.(esp|esm|esl)$", RegexOptions.IgnoreCase);
}
