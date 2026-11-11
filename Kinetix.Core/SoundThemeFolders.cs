namespace KinetixModManager;

/// <summary>
/// Where sound themes are kept: the user's own in their data folder, and the ones that came with the manager beside
/// the program.
///
/// <para>
/// There used to be one folder, beside the program. On an installed copy that is under Program Files, where Windows
/// refuses an ordinary user, so making, filling or deleting a theme failed there. The user's themes now live where
/// the rest of their data does, and are read first. Themes made beside the program by an older version, which a
/// portable copy allowed, are still read from there and can still be deleted there.
/// </para>
/// </summary>
public sealed class SoundThemeFolders
{
	/// <param name="user">The folder the user's own themes are made in.</param>
	/// <param name="shipped">The folder beside the program, holding the themes the manager comes with.</param>
	public SoundThemeFolders(string user, string shipped)
	{
		User = user;
		Shipped = shipped;
		All = new[] { user, shipped };
	}

	/// <summary>The user's own themes, in their data folder. Every new theme is made here.</summary>
	public string User { get; }

	/// <summary>The themes beside the program.</summary>
	public string Shipped { get; }

	/// <summary>Both, in the order a theme is looked for: the user's first.</summary>
	public IReadOnlyList<string> All { get; }

	/// <summary>The folders for this copy of the manager, given the folder the program runs from.</summary>
	public static SoundThemeFolders ForProgramIn(string programFolder) =>
		new(Path.Combine(AppSettings.AppDataFolder, "sounds"), Path.Combine(programFolder, "sounds"));

	/// <summary>The folder holding <paramref name="theme"/>, the user's copy first, or null when it is nowhere.</summary>
	public string? FolderOf(string theme)
	{
		if (string.IsNullOrWhiteSpace(theme)) return null;
		foreach (string root in All)
		{
			string folder = Path.Combine(root, theme);
			try { if (Directory.Exists(folder)) return folder; }
			catch (Exception ex) { DiagnosticLog.WriteException("Sound", $"looking for the {theme} theme in {root}", ex); }
		}
		return null;
	}

	/// <summary>Whether <paramref name="theme"/> is on disk in either folder.</summary>
	public bool Exists(string theme) => FolderOf(theme) != null;

	/// <summary>
	/// Whether <paramref name="theme"/> is one the manager comes with: Default and each game's own. Those are not the
	/// user's to delete, and on an installed copy could not be anyway.
	/// </summary>
	public static bool ComesWithTheManager(string theme) =>
		string.Equals(theme, SoundThemes.DefaultTheme, StringComparison.OrdinalIgnoreCase)
		|| GameProfiles.All.Any(g => string.Equals(g.SoundTheme, theme, StringComparison.OrdinalIgnoreCase));
}
