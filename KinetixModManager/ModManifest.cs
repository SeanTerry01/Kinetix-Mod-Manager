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

	/// <summary>
	/// The Nexus mod id in a file whose name is unmistakably a Nexus download, or <c>null</c> for anything else.
	///
	/// <para>
	/// Deliberately stricter than <see cref="ParseNexusIdFromFileName"/>, because of who is asking. That one
	/// reads archives in the manager's own downloads folder, where something else already vouches for the file:
	/// the mods inside it are checked against the mods on disk before its id is believed. This one is asked
	/// about a file the user picked from anywhere, so nothing vouches for it, and reading
	/// <c>"ModBackup-12345-old.zip"</c> as mod 12345 would quietly offer another mod's updates for this one.
	/// </para>
	///
	/// <para>
	/// Nexus names a download <c>&lt;name&gt;-&lt;mod id&gt;-&lt;version, dashes for dots&gt;[-&lt;timestamp&gt;]</c>,
	/// so everything after the id is digits and dashes and nothing else. A file renamed by a browser — the
	/// <c>" (1)"</c> a second download picks up — fails that test and is treated as unknown, which costs only
	/// the automatic link and never records a wrong one.
	/// </para>
	/// </summary>
	public static string? NexusIdFromDownloadName(string fileName)
	{
		string name = Path.GetFileNameWithoutExtension(fileName ?? "");

		// Every "-digits-" in the name is a candidate, overlapping ones included: a title that itself ends in a
		// number puts a second candidate in the name, and the two are not distinguishable by shape. So they are
		// all collected, and an id is returned only when exactly one of them can be the mod's — with two, the
		// name genuinely does not say which, and picking the earlier one is a guess wearing a fact's clothes.
		var ids = new List<string>();
		foreach (Match m in Regex.Matches(name, @"(?=-(\d{3,9})-)"))
		{
			string tail = name.Substring(m.Index + m.Groups[1].Value.Length + 2);
			if (Regex.IsMatch(tail, @"^\d+(-\d+)*$")) ids.Add(m.Groups[1].Value);
		}

		return ids.Count == 1 ? ids[0] : null;
	}
}
