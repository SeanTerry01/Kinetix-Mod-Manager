using System;
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
	/// Whether <paramref name="candidate"/> is confidently the same mod as <paramref name="installed"/>. Only an
	/// exact normalized name match, or a partial name match backed up by the same author, is accepted. The
	/// original rule accepted any top search result whose name merely contained (or was contained by) the mod's
	/// name, which is how mods ended up linked to the wrong Nexus page.
	/// </summary>
	public static bool IsConfident(GameMod installed, GameMod candidate)
	{
		string a = Normalize(installed.Name);
		string b = Normalize(candidate.Name);
		if (a.Length == 0 || b.Length == 0) return false;
		if (a.Length < 4 || b.Length < 4) return a == b;
		if (a == b) return true;

		bool sameAuthor = !string.IsNullOrWhiteSpace(installed.Author) &&
						  !string.IsNullOrWhiteSpace(candidate.Author) &&
						  Normalize(installed.Author) == Normalize(candidate.Author);
		return sameAuthor && (a.Contains(b) || b.Contains(a));
	}
}
