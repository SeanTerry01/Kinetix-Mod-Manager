// Backups and the download history: what was kept, when, and how large.
//
// Lifted out of Form1 by Phase 2 of the core split. These are plain data - what a row holds and how
// it reads aloud - so they belong beside the rules rather than inside the window that happens to
// show them today. While they were private to Form1 no second front end could name them at all.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>Metadata stored alongside each snapshot's copied files.</summary>
public sealed class SafetyBackupMeta
{
	public string Reason { get; set; } = "";
	public DateTime CreatedUtc { get; set; }
	public List<string> ModPriority { get; set; } = new List<string>();
	public List<string> PluginOrder { get; set; } = new List<string>();
}

/// <summary>One restorable snapshot in the restore list.</summary>
public sealed class SafetyBackupItem
{
	public required string Dir;
	public required SafetyBackupMeta Meta;
	public string Summary = "";
	public override string ToString() => Summary;
}

/// <summary>One row in the downloads list: a downloaded archive with its size and date.</summary>
public sealed class DownloadItem
{
	public required string FullPath;
	public required long Size;
	public required DateTime Date;
	public string Summary = "";
	public override string ToString() => Summary;
}
