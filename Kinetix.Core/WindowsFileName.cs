using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// The characters Windows forbids in a file or folder name, stated outright rather than asked of the
/// host operating system.
///
/// <see cref="System.IO.Path.GetInvalidFileNameChars"/> answers for the machine the code is running on,
/// which is the wrong question here. On Windows it returns 41 characters; on Linux it returns two — the
/// directory separator and NUL — because Linux genuinely permits the rest. So a sanitiser built on it
/// keeps working in the sense that it does not crash, and silently stops sanitising, which is the worse
/// of the two failures: a mod folder called <c>Skyrim: Reloaded? &lt;best&gt;</c> would be created
/// happily and then be unopenable by the thing that has to read it.
///
/// And something does have to read it. The games this manager mods are Windows games; on Linux they run
/// under Proton, inside a prefix that presents these folders to the game through Wine's filesystem
/// emulation. A name Linux accepts but Windows does not is a name the game cannot open, so the rule that
/// matters belongs to the game, not to the host. Hard-coding the set is what keeps the manager's answer
/// the same on both.
///
/// The list is Windows' own: the control characters 0–31, plus the nine printable characters reserved by
/// the Win32 path syntax.
/// </summary>
public static class WindowsFileName
{
	/// <summary>
	/// Every character Windows refuses in a file or folder name, whatever host we are running on.
	/// </summary>
	public static readonly IReadOnlyList<char> InvalidChars =
		Enumerable.Range(0, 32).Select(c => (char)c)
			.Concat(new[] { '"', '<', '>', '|', ':', '*', '?', '\\', '/' })
			.ToArray();

	private static readonly HashSet<char> Invalid = new(InvalidChars);

	/// <summary>Whether Windows would refuse a name containing <paramref name="c"/>.</summary>
	public static bool IsInvalid(char c) => Invalid.Contains(c);

	/// <summary>
	/// <paramref name="name"/> with every character Windows forbids removed. Trailing dots and spaces are
	/// left alone, because callers disagree about them — see each caller's own trimming.
	/// </summary>
	public static string StripInvalid(string name) =>
		string.IsNullOrEmpty(name) ? "" : new string(name.Where(c => !Invalid.Contains(c)).ToArray());
}
