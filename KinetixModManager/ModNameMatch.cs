using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KinetixModManager;

/// <summary>
/// Deciding whether a Nexus search result is confidently the same mod as an installed one — the last resort
/// when nothing records where a mod came from. Getting this wrong is expensive: a mod linked to the wrong page
/// reports someone else's version, and "updating" it downloads an unrelated archive. So the bar is high, and
/// the rules are here (self-contained, BCL + <see cref="GameMod"/>) to keep them under test.
/// </summary>
public static class ModNameMatch
{
	/// <summary>
	/// Normalized form of a mod name for comparison: any leading content-type tag removed, then lower-cased
	/// down to letters and digits. Stardew mod folders are conventionally prefixed with the framework the pack
	/// targets — "[CP] Stoned Valley", "(CP)Antique crystal lighting fixtures", "[JA] Something" — which is
	/// never part of the mod's name on Nexus, so leaving it in stopped otherwise-exact matches from matching.
	/// Everything else is only stripped of punctuation and case, so two genuinely different mods stay different.
	/// </summary>
	public static string Normalize(string? name) =>
		new string(StripTypeTag(name).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

	/// <summary>
	/// The mod's name with any leading content-pack type tag removed — "[CP] Stoned Valley" becomes
	/// "Stoned Valley" — keeping spacing and capitals. This is the form to search Nexus with, since the tag is
	/// a local convention that never appears in the mod's name on the site.
	/// </summary>
	public static string StripTypeTag(string? name)
	{
		string text = (name ?? "").Trim();
		return Regex.Replace(text, @"^\s*[\[\(][^\]\)]{0,12}[\]\)]\s*", "").Trim();
	}

	/// <summary>
	/// Words that appear in an identifier without saying anything about which mod it is: reverse-domain
	/// prefixes, framework names, the game's own name, and generic component words. A mod is never identified
	/// by these, so they must not become search terms or be trusted as an author — "moonlightpeaks" appears in
	/// most Moonlight Peaks plugin GUIDs and would otherwise match half the site.
	/// </summary>
	private static readonly HashSet<string> IdentifierNoise = new(StringComparer.OrdinalIgnoreCase)
	{
		"com", "org", "net", "io", "github", "www",
		"bepinex", "bepis", "smapi", "skse", "f4se", "harmony", "plugin", "plugins", "mod", "mods",
		"core", "main", "lib", "library", "common", "shared", "patcher", "patch", "framework",
		"moonlight", "moonlightpeaks", "stardew", "stardewvalley", "skyrim", "skyrimse", "fallout", "fallout4"
	};

	/// <summary>The mod's folder name, with the leading dot that marks a disabled mod removed. Empty if unknown.</summary>
	private static string FolderName(GameMod mod)
	{
		string path = mod.FolderPath ?? "";
		if (path.Length == 0) return "";
		string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		return name.StartsWith(".") ? name.Substring(1) : name;
	}

	/// <summary>
	/// The meaningful parts of a dotted or underscored identifier — a BepInEx plugin GUID such as
	/// <c>padme4000.moonlightpeaks.alwaysshowvalue</c> or <c>com.lockyaw.moonlightpeaks.house_storage_anywhere</c> —
	/// with noise words and very short fragments dropped. What is left is usually the author's handle and the
	/// mod's real name, which is exactly what a search needs.
	/// </summary>
	private static List<string> IdentifierParts(string? identifier)
	{
		var parts = new List<string>();
		if (string.IsNullOrWhiteSpace(identifier)) return parts;

		foreach (string raw in identifier.Split(new[] { '.', ':', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
		{
			string part = raw.Trim();
			if (part.Length < 4) continue;
			if (IdentifierNoise.Contains(part)) continue;
			parts.Add(part);
		}
		return parts;
	}

	/// <summary>
	/// Turns a run-together identifier into spaced words a search engine can match — "BiggerStacks" becomes
	/// "Bigger Stacks", "house_storage_anywhere" becomes "house storage anywhere". Returns the input unchanged
	/// when there is nothing to split.
	/// </summary>
	public static string Humanize(string? identifier)
	{
		string text = (identifier ?? "").Replace('_', ' ').Replace('-', ' ').Trim();
		if (text.Length == 0) return "";
		// Split camel and Pascal case, but leave existing spacing and digit runs alone.
		text = Regex.Replace(text, @"(?<=[a-z0-9])(?=[A-Z])", " ");
		return Regex.Replace(text, @"\s+", " ").Trim();
	}

	/// <summary>
	/// Every name worth searching Nexus for when trying to identify <paramref name="installed"/>, best first.
	///
	/// A mod's declared name is only one of the names it goes by, and often not the one on its Nexus page. The
	/// folder it was installed into is frequently the page's exact title ("BiggerStacks", "TimeControl"), and a
	/// BepInEx plugin's GUID usually ends in the mod's real name ("elsia.modmenu" for a page called "Mod Menu").
	/// Searching each in turn is what makes auto-matching work for mods whose author named the plugin one thing
	/// and the page another.
	/// </summary>
	public static List<string> SearchAliases(GameMod installed)
	{
		var aliases = new List<string>();

		void Add(string? value)
		{
			string text = (value ?? "").Trim();
			if (text.Length < 3) return;
			// Compare on the normalized form so "BiggerStacks" and "Bigger Stacks" count as one alias.
			string key = Normalize(text);
			if (key.Length < 3) return;

			int existing = aliases.FindIndex(a => Normalize(a) == key);
			if (existing >= 0)
			{
				// Two spellings of one name: keep the spaced one. Nexus's name search matches words, so a page
				// called "Mod Menu" is found by "Mod Menu" and not by "modmenu" — and a plugin GUID only ever
				// gives us the run-together spelling.
				if (text.Contains(' ') && !aliases[existing].Contains(' ')) aliases[existing] = text;
				return;
			}
			aliases.Add(text);
		}

		Add(StripTypeTag(installed.Name));

		// The folder, whole and in parts. A folder named "MoonlightPeaks.ModMenu" carries the game's name and
		// the mod's; only the second identifies it, and only once split back into words.
		string folder = FolderName(installed);
		Add(folder);
		Add(Humanize(folder));
		foreach (string part in IdentifierParts(folder))
		{
			Add(part);
			Add(Humanize(part));
		}

		// The GUID's parts, last first: the trailing segment is the mod, the earlier ones are usually the author.
		List<string> guidParts = IdentifierParts(installed.UniqueId);
		for (int i = guidParts.Count - 1; i >= 0; i--)
		{
			Add(guidParts[i]);
			Add(Humanize(guidParts[i]));
		}

		return aliases;
	}

	/// <summary>
	/// Author names this mod might be published under: the recorded author, plus the parts of its GUID. A
	/// BepInEx plugin rarely records an author the manager can read, but its GUID is conventionally prefixed
	/// with the author's handle — "padme4000.moonlightpeaks.alwaysshowvalue" — which matches the Nexus page's
	/// author exactly often enough to be worth trusting as corroboration for a partial name match.
	/// </summary>
	private static List<string> AuthorCandidates(GameMod installed)
	{
		var authors = new List<string>();
		if (!string.IsNullOrWhiteSpace(installed.Author) &&
			!installed.Author.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
			authors.Add(installed.Author);

		authors.AddRange(IdentifierParts(installed.UniqueId));
		return authors;
	}

	/// <summary>
	/// Whether <paramref name="candidate"/> is confidently the same mod as <paramref name="installed"/>.
	///
	/// Two ways to be confident, both deliberately strict. Either one of the mod's known names matches the
	/// candidate's name exactly (ignoring case and punctuation), or one of them is contained in the other AND
	/// the author agrees. A bare partial name match is never enough on its own — that is how mods used to end
	/// up linked to the wrong Nexus page.
	/// </summary>
	public static bool IsConfident(GameMod installed, GameMod candidate)
	{
		string b = Normalize(candidate.Name);
		if (b.Length == 0) return false;

		List<string> aliases = SearchAliases(installed);
		if (aliases.Count == 0) return false;

		// An exact match on any name the mod goes by.
		foreach (string alias in aliases)
		{
			string a = Normalize(alias);
			if (a.Length == 0) continue;
			if (a == b) return true;
		}

		// Otherwise a partial match, but only with the author to back it up.
		if (!AuthorAgrees(installed, candidate)) return false;

		foreach (string alias in aliases)
		{
			string a = Normalize(alias);
			// Very short names are too easy to contain by accident, so they only ever match exactly (above).
			if (a.Length < 4 || b.Length < 4) continue;
			if (a.Contains(b) || b.Contains(a)) return true;
		}

		return false;
	}

	/// <summary>True when the candidate's author is one this mod could plausibly be published under.</summary>
	private static bool AuthorAgrees(GameMod installed, GameMod candidate)
	{
		string candidateAuthor = Normalize(candidate.Author);
		if (candidateAuthor.Length < 3) return false;

		foreach (string author in AuthorCandidates(installed))
		{
			string a = Normalize(author);
			if (a.Length < 3) continue;
			if (a == candidateAuthor) return true;
		}
		return false;
	}
}
