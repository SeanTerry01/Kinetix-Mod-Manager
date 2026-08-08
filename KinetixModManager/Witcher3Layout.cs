using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// Which folders inside an extracted Witcher 3 archive are actually mods.
///
/// The engine loads a folder under <c>&lt;game&gt;\mods</c> only when its name begins with <c>mod</c>, and says
/// nothing whatever when it doesn't — so getting this wrong produces a mod that installs cleanly, appears in the
/// list, and never runs. Pure and BCL-only so the rules can be tested against real archive layouts.
/// </summary>
public static class Witcher3Layout
{
	/// <summary>The prefix the engine requires. A folder without it is walked straight past.</summary>
	public const string ModPrefix = "mod";

	/// <summary>
	/// True for a folder that is the archive reproducing the game's own <c>mods</c> directory rather than a mod.
	///
	/// This is the trap: "mods" begins with "mod", so the obvious prefix test accepts it. An archive laid out as
	/// <c>mods\modFoo\content</c> then installs the WRAPPER, landing the real mod at
	/// <c>&lt;game&gt;\mods\mods\modFoo</c> — one level too deep for the engine, which only ever looks at
	/// <c>mods\mod*</c>. The mod is on disk, listed by the manager, and dead.
	/// </summary>
	public static bool IsModsWrapperFolder(string folderName) =>
		string.Equals(folderName, "mods", StringComparison.OrdinalIgnoreCase);

	/// <summary>True when <paramref name="folderName"/> is one the engine would load as a mod.</summary>
	public static bool IsModFolderName(string folderName) =>
		!string.IsNullOrEmpty(folderName)
		&& folderName.StartsWith(ModPrefix, StringComparison.OrdinalIgnoreCase)
		&& !IsModsWrapperFolder(folderName);

	/// <summary>
	/// Words that run together in a folder name and read badly split by the general rules alone. Only the ones
	/// that come up across many mods earn a place here — <c>sharedutils</c> is the Witcher's shared-utilities
	/// framework, which a great many mods ship pieces of.
	/// </summary>
	private static readonly Dictionary<string, string> KnownWords = new(StringComparer.OrdinalIgnoreCase)
	{
		["sharedutils"]   = "Shared Utils",
		["sharedimports"] = "Shared Imports"
	};

	/// <summary>
	/// A readable name for a mod folder: <c>modRandomEncountersReworked</c> reads as "Random Encounters
	/// Reworked", <c>mod_sharedutils_glossary</c> as "Shared Utils: Glossary".
	///
	/// The Witcher gives the manager nothing else to go on. A mod folder carries no manifest, no author and no
	/// version — the engine only ever needed the folder's name — so a list of these mods is a list of folder
	/// names, and the raw ones are hard to listen to.
	/// </summary>
	public static string DisplayNameFromFolder(string folderName)
	{
		if (string.IsNullOrWhiteSpace(folderName)) return folderName ?? "";

		string name = folderName.Trim();
		if (name.StartsWith("~", StringComparison.Ordinal)) name = name.Substring(1);   // the disabled marker

		if (name.StartsWith(ModPrefix, StringComparison.OrdinalIgnoreCase) && name.Length > ModPrefix.Length)
			name = name.Substring(ModPrefix.Length);
		name = name.TrimStart('_', '-', ' ');
		if (name.Length == 0) return folderName;

		string[] segments = name.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(PrettifySegment)
			.Where(s => s.Length > 0)
			.ToArray();
		if (segments.Length == 0) return folderName;

		// A framework piece reads as "family: part" — the family is the useful half when several are installed.
		return segments.Length > 1 && KnownWords.ContainsKey(name.Split('_', '-')[0])
			? segments[0] + ": " + string.Join(" ", segments.Skip(1))
			: string.Join(" ", segments);
	}

	/// <summary>
	/// The family a framework mod belongs to — <c>mod_sharedutils_glossary</c> gives "Shared Utils" — or
	/// <c>null</c> for a mod that stands on its own.
	///
	/// The Witcher installs every mod as a sibling folder, so nothing about the layout says which folders are
	/// really one thing. A framework that ships as ten <c>mod_&lt;family&gt;_*</c> folders therefore fills ten
	/// rows of the list. The shared prefix is the author's own statement that they belong together, so grouping
	/// on it needs no guessing.
	/// </summary>
	public static string? FamilyKey(string folderName)
	{
		if (string.IsNullOrWhiteSpace(folderName)) return null;

		string name = folderName.Trim();
		if (name.StartsWith("~", StringComparison.Ordinal)) name = name.Substring(1);
		if (!name.StartsWith(ModPrefix, StringComparison.OrdinalIgnoreCase)) return null;

		string rest = name.Substring(ModPrefix.Length);
		// Only the underscore form marks a family. CamelCase mod names run words together for readability, not
		// to say anything about kinship — modRandomEncountersReworked is one mod, not the "Random" family.
		if (!rest.StartsWith("_", StringComparison.Ordinal)) return null;

		string[] parts = rest.TrimStart('_').Split('_', StringSplitOptions.RemoveEmptyEntries);
		return parts.Length >= 2 ? PrettifySegment(parts[0]) : null;
	}

	/// <summary>Turns one folder-name segment into words: known run-together names first, then camelCase.</summary>
	private static string PrettifySegment(string segment)
	{
		if (segment.Length == 0) return "";
		if (KnownWords.TryGetValue(segment, out string? known)) return known;

		var words = new List<string>();
		var current = new System.Text.StringBuilder();
		for (int i = 0; i < segment.Length; i++)
		{
			char c = segment[i];

			// Two kinds of word boundary, and both are needed. The plain one is a capital after a lower-case
			// letter: "AudioRemaster" is two words. The other is the last capital of a run, when a lower-case
			// letter follows it — that capital starts a new word rather than ending the acronym, which is what
			// makes "FMCAudioRemaster" read as "FMC Audio Remaster" instead of "FMCAudio Remaster". Without the
			// run-aware case an acronym swallows the word after it; without the plain case every capital would
			// split and "FMC" would come out as "F M C".
			bool boundary = i > 0 && char.IsUpper(c) &&
				(!char.IsUpper(segment[i - 1]) || (i + 1 < segment.Length && char.IsLower(segment[i + 1])));

			if (boundary && current.Length > 0)
			{
				words.Add(current.ToString());
				current.Clear();
			}
			current.Append(c);
		}
		if (current.Length > 0) words.Add(current.ToString());

		return string.Join(" ", words.Select(w =>
			w.Length > 0 && char.IsLower(w[0]) ? char.ToUpperInvariant(w[0]) + w.Substring(1) : w));
	}

	/// <summary>
	/// The mod folders among <paramref name="directories"/> — every directory in the extracted archive, in any
	/// order. A folder nested inside one already chosen is that mod's own contents, not a second mod; a
	/// <c>mods</c> wrapper is stepped through rather than taken.
	///
	/// Shallowest first, so a mod is always chosen before anything inside it.
	/// </summary>
	public static List<string> SelectModFolders(IEnumerable<string> directories)
	{
		var found = new List<string>();

		foreach (string dir in directories
			.Where(d => !string.IsNullOrEmpty(d))
			.OrderBy(d => d.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
			.ThenBy(d => d, StringComparer.OrdinalIgnoreCase))
		{
			if (!IsModFolderName(Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))))
				continue;

			// Already inside a mod we took — these are its contents (a mod's own modFoo\content\modBar).
			if (found.Any(f => dir.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
				continue;

			found.Add(dir);
		}

		return found;
	}
}
