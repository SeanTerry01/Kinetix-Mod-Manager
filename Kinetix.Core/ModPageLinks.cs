using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// Remembering where each installed mod's page is, so the answer survives closing the manager.
///
/// <para>
/// A companion to <c>mod_id_map.json</c> rather than a change to it. That file has held
/// <c>UniqueID -&gt; Nexus id</c> since long before there was more than one place a mod could come from, and
/// widening its values into objects would rewrite every user's file for a fact that fits perfectly well
/// beside it. Two small files that each say one thing are easier to reason about, and an unreadable one
/// costs the user a lookup rather than their mod list.
/// </para>
/// </summary>
public static class ModPageLinks
{
	/// <summary>The file, inside the manager's own data folder.</summary>
	public static string PathIn(string appDataFolder) => Path.Combine(appDataFolder, "mod_page_map.json");

	/// <summary>
	/// Every remembered page, keyed by mod UniqueID. An empty map for a file that is missing or unreadable —
	/// this is a convenience, and losing it should never stop a mod list being built.
	/// </summary>
	public static Dictionary<string, string> Load(string appDataFolder)
	{
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			string path = PathIn(appDataFolder);
			if (!File.Exists(path)) return map;

			JObject? doc = JObject.Parse(File.ReadAllText(path));
			if (doc == null) return map;

			foreach (JProperty property in doc.Properties())
			{
				string? url = (string?)property.Value;
				if (!string.IsNullOrWhiteSpace(url)) map[property.Name] = url!.Trim();
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("ModPageMap", "reading the remembered mod pages", ex); }

		return map;
	}

	/// <summary>
	/// Merges <paramref name="links"/> into the stored map. Merged rather than replaced: a check only covers
	/// the mods that were installed at the time, and rewriting the file from one of those would forget every
	/// mod the user has since switched off.
	/// </summary>
	public static void Save(string appDataFolder, IReadOnlyDictionary<string, string> links)
	{
		if (links.Count == 0) return;

		try
		{
			string path = PathIn(appDataFolder);
			JObject doc = (File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject()) ?? new JObject();
			foreach (var link in links)
				if (!string.IsNullOrWhiteSpace(link.Key) && !string.IsNullOrWhiteSpace(link.Value))
					doc[link.Key] = link.Value;

			Directory.CreateDirectory(appDataFolder);
			File.WriteAllText(path, doc.ToString(Formatting.Indented));
		}
		catch (Exception ex) { DiagnosticLog.WriteException("ModPageMap", "remembering where mods' pages are", ex); }
	}

	/// <summary>
	/// Fills in <see cref="GameMod.PageUrl"/> and <see cref="GameMod.SourceId"/> on freshly scanned mods from
	/// the remembered map, leaving alone any that already know. Returns how many were filled in.
	/// </summary>
	public static int Apply(IEnumerable<GameMod> mods, IReadOnlyDictionary<string, string> map)
	{
		if (map.Count == 0) return 0;

		int filled = 0;
		foreach (GameMod mod in mods)
		{
			if (string.IsNullOrEmpty(mod.UniqueId) || !string.IsNullOrEmpty(mod.PageUrl)) continue;
			if (!map.TryGetValue(mod.UniqueId, out string? url)) continue;

			mod.PageUrl = url;
			if (string.IsNullOrEmpty(mod.SourceId)) mod.SourceId = ModSources.ParsePage(url)?.SourceId ?? "";
			filled++;
		}

		return filled;
	}
}
