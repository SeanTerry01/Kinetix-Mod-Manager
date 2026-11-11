namespace KinetixModManager;

/// <summary>Represents a single backup archive in the backups list.</summary>
public class BackupItem
{
	/// <summary>Display name of the backed-up mod (timestamp suffix stripped).</summary>
	public string Name { get; set; } = "";

	/// <summary>Full path to the .zip archive on disk.</summary>
	public string FullPath { get; set; } = "";

	/// <summary>
	/// The row as it is read out: the mod, when the backup was taken, and its size. See <see cref="BackupRow.Spoken"/>.
	///
	/// ⚠️ Not <see cref="Name"/>. The Windows list showed that, and two backups of one mod were heard as "Chat Commands" and
	/// "Chat Commands_Delete", with no way to tell which was which and the delete marker read out. Measured with NVDA
	/// 2026-11-10. The Linux head already said it this way.
	///
	/// Worked out once: a list box asks for a row's text every time it paints.
	/// </summary>
	public override string ToString() =>
		_spoken ??= BackupsView.Of(new[] { this }).Rows.FirstOrDefault()?.Spoken ?? Name;

	private string? _spoken;
}
