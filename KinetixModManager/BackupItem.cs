namespace KinetixModManager;

/// <summary>Represents a single backup archive in the backups list.</summary>
public class BackupItem
{
	/// <summary>Display name of the backed-up mod (timestamp suffix stripped).</summary>
	public string Name { get; set; } = "";

	/// <summary>Full path to the .zip archive on disk.</summary>
	public string FullPath { get; set; } = "";

	/// <inheritdoc/>
	public override string ToString() => Name;
}
