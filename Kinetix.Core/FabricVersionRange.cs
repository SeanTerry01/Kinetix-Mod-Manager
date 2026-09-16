using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// Whether a version satisfies the range a Fabric mod declares — in practice, "will this mod let the game start
/// on this Minecraft version".
///
/// <para>
/// <b>Why this had to exist.</b> The manager offered to move a setup to a newer Minecraft version, judging each
/// installed mod only by whether the catalogue had a newer build of it, and warned that any mod without one would
/// "stop loading". That was wrong, and it cost Sean a game that would not start at all: Fabric does not skip a mod
/// whose declared Minecraft range excludes the version being run. It refuses to launch, listing every offender.
/// Two of his mods were fine on the new version without any new build, having asked for <c>&gt;=26.2</c>; four
/// demanded 26.2 exactly and stopped the game dead. Only what the mod itself declares can tell those apart.
/// </para>
///
/// <para>
/// Fabric's syntax is a subset of npm's: <c>*</c>, a bare version meaning exactly that version, the comparators
/// <c>&gt;=</c> <c>&gt;</c> <c>&lt;=</c> <c>&lt;</c> <c>=</c>, <c>~</c> (the same minor), <c>^</c> (the same
/// major), a space between terms meaning AND, and <c>||</c> between ranges meaning OR. A trailing <c>-</c>
/// (<c>~26.3-</c>) admits pre-releases of that version.
/// </para>
///
/// <para>
/// Unparseable input answers <c>true</c>, deliberately. This decides whether to switch somebody's mod off, and a
/// range this does not understand is not evidence that the mod is broken.
/// </para>
/// </summary>
public static class FabricVersionRange
{
	/// <summary>
	/// Whether a mod can be left alone when the game moves to <paramref name="gameVersion"/>, given what the
	/// catalogue says about the exact build installed and what the mod says about itself.
	///
	/// <para>
	/// The catalogue wins where it has an answer, because a mod's own declaration is optional and a mod that
	/// declares nothing accepts everything. Toolbar Sounds declares no Minecraft version, so it loaded on 26.3 and
	/// then its data pack would not parse — which the player meets as "errors in the currently selected data packs"
	/// and a world that will not open, with nothing naming the mod. Its catalogue entry listed 26.2 as its last
	/// supported version all along.
	/// </para>
	/// </summary>
	/// <param name="catalogueVersions">Game versions the installed build is published for; null or empty when unknown.</param>
	/// <param name="declaredRange">The mod's own <c>depends.minecraft</c> range, if it gave one.</param>
	public static bool SurvivesMove(IReadOnlyList<string>? catalogueVersions, string? declaredRange, string gameVersion)
	{
		if (catalogueVersions is { Count: > 0 })
			return catalogueVersions.Any(v => string.Equals(v?.Trim(), gameVersion?.Trim(), StringComparison.OrdinalIgnoreCase));

		return Accepts(declaredRange, gameVersion);
	}

	/// <summary>Whether <paramref name="version"/> satisfies <paramref name="range"/>.</summary>
	public static bool Accepts(string? range, string? version)
	{
		if (string.IsNullOrWhiteSpace(range) || string.IsNullOrWhiteSpace(version)) return true;

		// "||" separates whole alternatives: any one of them being satisfied is enough.
		return range.Split(new[] { "||" }, StringSplitOptions.None)
			.Any(alternative => AllTermsHold(alternative, version!));
	}

	/// <summary>Every space-separated comparator in one alternative has to hold at once.</summary>
	private static bool AllTermsHold(string alternative, string version)
	{
		string[] terms = alternative.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		return terms.Length != 0 && terms.All(term => TermHolds(term, version));
	}

	private static bool TermHolds(string term, string version)
	{
		if (term == "*" || term.Length == 0) return true;

		// Nothing numeric to compare against. Reading this as "the mod does not accept the version" would switch
		// off a mod on the strength of a range nobody understood; see the note on this class.
		if (!term.Any(char.IsDigit)) return true;

		if (term.StartsWith(">=", StringComparison.Ordinal)) return Compare(version, term[2..]) >= 0;
		if (term.StartsWith("<=", StringComparison.Ordinal)) return Compare(version, term[2..]) <= 0;
		if (term.StartsWith(">", StringComparison.Ordinal)) return Compare(version, term[1..]) > 0;
		if (term.StartsWith("<", StringComparison.Ordinal)) return Compare(version, term[1..]) < 0;
		if (term.StartsWith("=", StringComparison.Ordinal)) return Compare(version, term[1..]) == 0;

		// "~26.3" is 26.3 up to but not including 26.4; "^26.3" is 26.3 up to but not including 27.
		if (term.StartsWith("~", StringComparison.Ordinal) || term.StartsWith("^", StringComparison.Ordinal))
		{
			string floor = term[1..];
			return Compare(version, floor) >= 0 && Compare(version, CeilingFor(floor, sameMinor: term[0] == '~')) < 0;
		}

		// A bare version. "26.2.x" and "26.2.*" are the wildcard spellings of "any 26.2 patch".
		if (term.EndsWith(".x", StringComparison.OrdinalIgnoreCase) || term.EndsWith(".*", StringComparison.Ordinal))
		{
			string floor = term[..^2];
			return Compare(version, floor) >= 0 && Compare(version, CeilingFor(floor, sameMinor: true)) < 0;
		}

		return Compare(version, term) == 0;
	}

	/// <summary>The version one step above <paramref name="floor"/>: the next minor, or the next major.</summary>
	private static string CeilingFor(string floor, bool sameMinor)
	{
		List<long> parts = NumericParts(StripPreRelease(floor));
		while (parts.Count < 2) parts.Add(0);

		// A two-part Minecraft version ("26.3") has no patch segment, so "the same minor" means 26.3.x AND 26.3
		// itself, and the ceiling is 26.4. For a three-part version it is the next patch line.
		if (sameMinor) parts[1] += 1;
		else { parts[0] += 1; parts[1] = 0; }

		return string.Join('.', parts.Take(2));
	}

	/// <summary>
	/// Compares two versions numerically, segment by segment. A release outranks its own pre-release, so 26.3
	/// satisfies <c>&gt;=26.3-rc.3</c> — which is exactly the case PresenceFootsteps declares.
	/// </summary>
	public static int Compare(string left, string right)
	{
		List<long> a = NumericParts(StripPreRelease(left));
		List<long> b = NumericParts(StripPreRelease(right));

		for (int i = 0; i < Math.Max(a.Count, b.Count); i++)
		{
			long x = i < a.Count ? a[i] : 0;
			long y = i < b.Count ? b[i] : 0;
			if (x != y) return x < y ? -1 : 1;
		}

		string preA = PreRelease(left), preB = PreRelease(right);
		if (preA.Length == 0 && preB.Length == 0) return 0;
		if (preA.Length == 0) return 1;    // a release is newer than any pre-release of it
		if (preB.Length == 0) return -1;

		return string.Compare(preA, preB, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Everything before the first "-" or "+": "26.3-rc.3" and "1.5.1+26.2" both keep their numbers.</summary>
	private static string StripPreRelease(string version)
	{
		int cut = version.IndexOfAny(new[] { '-', '+' });
		return cut < 0 ? version.Trim() : version[..cut].Trim();
	}

	/// <summary>The pre-release tag, or "" for a plain release. A bare trailing "-" is the lowest of all.</summary>
	private static string PreRelease(string version)
	{
		int cut = version.IndexOf('-');
		return cut < 0 ? "" : version[(cut + 1)..].Trim();
	}

	private static List<long> NumericParts(string version)
	{
		var parts = new List<long>();
		foreach (string piece in version.Split('.'))
			parts.Add(long.TryParse(new string(piece.TakeWhile(char.IsDigit).ToArray()), out long n) ? n : 0);
		return parts;
	}
}
