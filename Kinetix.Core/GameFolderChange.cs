namespace KinetixModManager;

/// <summary>What changing a game's folder in Settings means for the mod files the manager put in the old one.</summary>
public static class GameFolderChange
{
	/// <summary>
	/// Whether the old folder still holds the game's deployed files. If it is gone, the game was moved and they moved with it.
	/// A different spelling of the same folder is not a change.
	/// </summary>
	public static bool LeavesFilesBehind(string? oldPath, string? newPath, bool oldFolderExists)
	{
		if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath) || !oldFolderExists) return false;
		return !string.Equals(Normalise(oldPath), Normalise(newPath), StringComparison.OrdinalIgnoreCase);
	}

	private static string Normalise(string path) =>
		Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
}
