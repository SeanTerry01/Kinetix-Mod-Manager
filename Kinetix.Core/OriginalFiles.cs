namespace KinetixModManager;

/// <summary>
/// Files a mod replaced that were there before the manager was: usually the game's own.
///
/// <para>
/// A mod that shipped <c>binkw64.dll</c> overwrote the game's copy with nothing kept, and removing the mod then
/// deleted its own copy, leaving the game without the file at all. "Purge", which promises the folder goes back to
/// how it was, did exactly the same. Only verifying the game files through the store could fix it. So the original is
/// now moved here first, and goes back when the mod's copy leaves.
/// </para>
/// </summary>
public sealed class OriginalFiles
{
	private readonly string _folder;

	/// <param name="folder">Where originals wait. Empty means nothing is kept.</param>
	public OriginalFiles(string folder) => _folder = folder ?? "";

	/// <summary>
	/// Moves the file at <paramref name="path"/> aside, unless an original for that place is already kept: whatever is
	/// there now is then only another mod's copy. Throws if it cannot be kept, so the caller does not overwrite it.
	/// </summary>
	/// <param name="path">The file about to be replaced.</param>
	/// <param name="relative">Where it is within the game folder, which is what identifies it.</param>
	public void Keep(string path, string relative)
	{
		if (_folder.Length == 0 || !File.Exists(path) || StoredAt(relative) is not { } stored) return;
		if (File.Exists(stored)) return;

		Directory.CreateDirectory(Path.GetDirectoryName(stored)!);
		File.Move(path, stored);
	}

	/// <summary>Puts a kept original back at <paramref name="path"/>. False when none was kept for that place.</summary>
	public bool Restore(string path, string relative)
	{
		if (_folder.Length == 0 || StoredAt(relative) is not { } stored || !File.Exists(stored)) return false;

		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.Move(stored, path, overwrite: true);
		return true;
	}

	/// <summary>Where an original is kept, or null for a path that tries to leave the folder.</summary>
	private string? StoredAt(string relative)
	{
		string root = Path.GetFullPath(_folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		string full = Path.GetFullPath(Path.Combine(root, relative));
		return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
	}
}
