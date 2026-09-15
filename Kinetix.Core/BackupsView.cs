using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>One kept backup, and the sentence that describes it.</summary>
public sealed record BackupRow(string Path, string ModName, DateTime TakenAt, long Bytes)
{
	/// <summary>
	/// What the reader says for this row.
	///
	/// The mod's name first, then when it was taken, because a backups list is read to answer "which copy of
	/// this mod do I want" and the date is the only thing separating five rows that otherwise say the same
	/// name. The size comes last: it decides nothing, and it is the part a listener can stop paying attention
	/// to once they have heard the date.
	/// </summary>
	public string Spoken => Loc.T("backups.row", ModName, Describe(TakenAt), Size(Bytes));

	/// <summary>A date said the way a person would say it, not an ISO stamp read out digit by digit.</summary>
	private static string Describe(DateTime when)
	{
		TimeSpan ago = DateTime.Now - when;

		if (ago.TotalMinutes < 2) return Loc.T("backups.justNow");
		if (ago.TotalHours < 1) return Loc.T("backups.minutesAgo", (int)ago.TotalMinutes);
		if (ago.TotalHours < 24) return Loc.T("backups.hoursAgo", (int)ago.TotalHours);
		if (ago.TotalDays < 30) return Loc.T("backups.daysAgo", (int)ago.TotalDays);

		return when.ToString("d MMMM yyyy");
	}

	private static string Size(long bytes) =>
		bytes >= 1024L * 1024 ? Loc.T("backups.megabytes", bytes / (1024.0 * 1024)) :
		bytes > 0 ? Loc.T("backups.kilobytes", Math.Max(1, bytes / 1024)) : "";

	public override string ToString() => Spoken;
}

/// <summary>
/// The kept backups, newest first.
///
/// <para>
/// Newest first is the whole ordering decision and it is not arbitrary: a backups list is opened after
/// something went wrong, and the copy wanted is almost always the one taken just before the thing that broke
/// it. Sorting by name would put the right answer somewhere in the middle of a list of identical names.
/// </para>
/// </summary>
public sealed class BackupsView
{
	private BackupsView(IReadOnlyList<BackupRow> rows) => Rows = rows;

	public IReadOnlyList<BackupRow> Rows { get; }

	/// <summary>How many different mods are represented, which is not the same as how many backups there are.</summary>
	public int ModCount => Rows.Select(r => r.ModName).Distinct(StringComparer.OrdinalIgnoreCase).Count();

	public static BackupsView Of(IEnumerable<BackupItem> backups)
	{
		var rows = new List<BackupRow>();

		foreach (BackupItem item in backups)
		{
			// A caller-supplied list, so nothing here may be assumed. An entry with no path at all is not a
			// backup and is dropped rather than throwing halfway through building the list — losing the
			// screen over one bad row would be the worse trade, and this screen is read after something has
			// already gone wrong.
			if (item == null || string.IsNullOrWhiteSpace(item.FullPath)) continue;

			long bytes = 0;
			DateTime taken = DateTime.MinValue;

			try
			{
				var info = new FileInfo(item.FullPath);
				if (info.Exists) { bytes = info.Length; taken = info.LastWriteTime; }
			}
			catch (Exception ex)
			{
				// A backup on a drive that has gone away is still worth listing — the user may be about to
				// plug it back in — so this costs its date, not its row.
				DiagnosticLog.WriteException("Backup", $"measuring {item.FullPath}", ex);
			}

			rows.Add(new BackupRow(item.FullPath, BackupStore.ModNameFromFileName(item.FullPath), taken, bytes));
		}

		return new BackupsView(rows.OrderByDescending(r => r.TakenAt).ToList());
	}

	/// <summary>The sentence to say when the list appears.</summary>
	public string Announcement =>
		Rows.Count == 0
			? Loc.T("backups.none")
			: Loc.T("backups.count", Rows.Count, ModCount);

	/// <summary>
	/// What restoring <paramref name="row"/> would do to what is installed now, as a sentence to confirm
	/// before anything is written.
	///
	/// Restoring is an overwrite. Saying so beforehand is the difference between a user choosing to replace
	/// what they have and discovering afterwards that they did.
	/// </summary>
	public static string DescribeRestoring(BackupRow row, bool somethingIsThere) =>
		somethingIsThere
			? Loc.T("backups.restoreOver", row.ModName)
			: Loc.T("backups.restoreFresh", row.ModName);
}
