namespace KinetixModManager;

/// <summary>
/// Whether files are there, answered from one listing of each folder instead of one question to the disk for each file.
///
/// <para>
/// A deployment pass asked <see cref="File.Exists(string)"/> once for every file it had put in the game. Measured on
/// 2026-11-11 with 60 mods and 24,000 files, that was 2.1 of the 2.4 seconds a single Ctrl+Up on Mod Priority took, and
/// a player with 60 mods heard the manager slow down as they moved one. Each folder is read the first time a file in it
/// is asked about, so a folder changed earlier in the pass is read as it is now.
/// </para>
///
/// <para>
/// ⚠️ Each path is meant to be asked about once, before it is changed. A file this pass creates or removes after its
/// folder was read is not seen.
/// </para>
/// </summary>
public sealed class FilesPresent
{
	/// <summary>File names by folder. Null means the folder couldn't be listed, and its files are asked about one by one.</summary>
	private readonly Dictionary<string, HashSet<string>?> _byFolder = new(NameComparer);

	/// <summary>Windows doesn't tell names apart by case, and neither does Wine; Linux does.</summary>
	private static StringComparer NameComparer =>
		OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

	public bool Contains(string path)
	{
		string? folder = Path.GetDirectoryName(path);
		if (string.IsNullOrEmpty(folder)) return File.Exists(path);

		if (!_byFolder.TryGetValue(folder, out HashSet<string>? names))
		{
			names = List(folder);
			_byFolder[folder] = names;
		}
		return names == null ? File.Exists(path) : names.Contains(Path.GetFileName(path));
	}

	private static HashSet<string>? List(string folder)
	{
		var names = new HashSet<string>(NameComparer);
		try
		{
			if (!Directory.Exists(folder)) return names;
			// Files only, hidden and system ones included, as File.Exists sees them. A folder of the same name is not a file.
			foreach (string file in Directory.EnumerateFiles(folder))
				names.Add(Path.GetFileName(file));
			return names;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}
}
