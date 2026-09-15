using System;
using System.IO;

namespace KinetixModManager;

/// <summary>
/// Moving a path from under one folder to under another.
///
/// <para>
/// One line of code, and it exists because the obvious spelling of it is wrong in a way that is very hard to
/// see. Copying a tree was written four separate times as
/// <c>file.Replace(sourceFolder, destinationFolder)</c>, which is a <em>string</em> operation on something
/// that is not a string: it replaces every occurrence rather than the leading one, so a path that happens to
/// contain the source folder's spelling twice is rewritten in the middle as well as at the front and the file
/// lands somewhere nobody asked for. It also silently does nothing at all when the two spellings differ by a
/// trailing separator or by case, which copies a file to itself.
/// </para>
///
/// <para>
/// Neither failure throws, and both produce a mod that installed "successfully" into the wrong place. That is
/// the whole reason this is a named function with tests rather than a line repeated where it is needed.
/// </para>
/// </summary>
public static class PathRebase
{
	/// <summary>
	/// <paramref name="path"/>, as it would be if <paramref name="fromRoot"/> were
	/// <paramref name="toRoot"/> instead.
	///
	/// Works on the path's own structure rather than on its text: the part below the old root is taken by
	/// length once both have been made absolute, and joined to the new one.
	/// </summary>
	/// <exception cref="ArgumentException">
	/// When <paramref name="path"/> is not under <paramref name="fromRoot"/> at all. Deliberately loud:
	/// every caller is copying a tree it has just enumerated, so this can only mean the roots have got
	/// muddled, and carrying on would write files outside the folder the caller believes it is filling.
	/// </exception>
	public static string To(string fromRoot, string path, string toRoot)
	{
		if (string.IsNullOrEmpty(fromRoot)) throw new ArgumentException("No source folder given.", nameof(fromRoot));
		if (string.IsNullOrEmpty(path)) throw new ArgumentException("No path given.", nameof(path));
		if (string.IsNullOrEmpty(toRoot)) throw new ArgumentException("No destination folder given.", nameof(toRoot));

		string root = WithSeparator(Path.GetFullPath(fromRoot));
		string full = Path.GetFullPath(path);

		// The root itself rebases to the new root, which is what makes copying a tree's own top-level folder
		// work without the caller special-casing it.
		if (string.Equals(WithSeparator(full), root, PathComparison))
			return Path.GetFullPath(toRoot);

		if (!full.StartsWith(root, PathComparison))
			throw new ArgumentException($"'{path}' is not inside '{fromRoot}'.", nameof(path));

		string relative = full.Substring(root.Length);
		return Path.Combine(Path.GetFullPath(toRoot), relative);
	}

	/// <summary>
	/// The same, answering null instead of throwing, for a caller walking a tree it did not build and would
	/// rather skip an odd entry than stop.
	/// </summary>
	public static string? TryTo(string fromRoot, string path, string toRoot)
	{
		try { return To(fromRoot, path, toRoot); }
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Deploy", $"working out where {path} belongs under {toRoot}", ex);
			return null;
		}
	}

	/// <summary>
	/// How paths are compared here: by the rules of the filesystem the program is running on.
	///
	/// Windows and macOS do not care about case and Linux does, and getting this backwards is how a mod
	/// folder called <c>Mods</c> stops matching one called <c>mods</c> — or worse, starts matching it.
	/// </summary>
	private static StringComparison PathComparison =>
		System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux)
			? StringComparison.Ordinal
			: StringComparison.OrdinalIgnoreCase;

	private static string WithSeparator(string path) =>
		path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}
