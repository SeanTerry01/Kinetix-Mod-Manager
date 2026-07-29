using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// Reading the facts a mod's manifest (and its downloaded file name) can tell us about where the mod came
/// from. Self-contained (BCL + Newtonsoft) so the parsing rules can be unit tested — they were all written
/// from manifests found in the wild, which are hand-authored and forgiving in ways a strict reader is not.
/// </summary>
public static class ModManifest
{
	/// <summary>
	/// Reads a manifest field by name, ignoring case. Manifest field casing varies — SMAPI's own bundled
	/// Console Commands mod spells it <c>"UniqueId"</c>, not <c>"UniqueID"</c> — and SMAPI itself reads them
	/// case-insensitively. A case-sensitive lookup returned null for those mods, so they were treated as having
	/// no UniqueID at all and handed a fresh random one on every scan: their notes, categories, ignored
	/// versions and update links could never stick, and they showed up as impossible to update-check.
	/// </summary>
	public static JToken? Field(JObject manifest, string name) =>
		manifest.GetValue(name, StringComparison.OrdinalIgnoreCase);

	/// <summary>The manifest field as a string, or null when absent.</summary>
	public static string? String(JObject manifest, string name) => (string?)Field(manifest, name);

	/// <summary>
	/// True when a manifest declares update keys but none of them can be used — every entry is blank, or names
	/// a source with no id after it (<c>"Nexus: "</c>). A common author slip, and worth telling apart from a
	/// manifest with no <c>UpdateKeys</c> field at all: the mod does come from somewhere, its key just says
	/// nothing. Returns false when the field is absent, empty, or holds at least one usable key.
	/// </summary>
	public static bool HasUnusableUpdateKey(JToken? keys)
	{
		if (keys == null) return false;
		IEnumerable<JToken> tokens = keys.Type == JTokenType.Array
			? keys.Children()
			: keys.Type == JTokenType.String ? new List<JToken> { keys } : Enumerable.Empty<JToken>();

		bool any = false;
		foreach (JToken token in tokens)
		{
			any = true;
			string text = token.ToString().Trim();
			if (text.Length == 0) continue;
			// "Nexus:" / "GitHub:" with nothing after the colon is still unusable.
			string[] parts = text.Split(':');
			if (parts.Length >= 2 && parts[1].Trim().Length > 0) return false;
		}
		return any;
	}

	/// <summary>
	/// The Nexus mod id embedded in a downloaded file's name. Nexus names its downloads
	/// "Granny's Recipe Box-23737-1-0-2-1715181269.zip", where the number after the mod name is the mod id —
	/// often the only surviving record of where a mod came from, since a Stardew manifest need not carry an
	/// update key. Returns null when the name doesn't follow the convention (a browser-renamed file, say),
	/// because a wrong id is worse than none.
	/// </summary>
	public static string? ParseNexusIdFromFileName(string fileName)
	{
		Match m = Regex.Match(Path.GetFileName(fileName), @"-(\d{3,9})-");
		return m.Success ? m.Groups[1].Value : null;
	}
}
