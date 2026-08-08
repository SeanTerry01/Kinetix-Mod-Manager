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
