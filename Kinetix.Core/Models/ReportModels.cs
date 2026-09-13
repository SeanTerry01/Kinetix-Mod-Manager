// Rows for Check My Setup, and what a scan for broken mods found.
//
// Lifted out of Form1 by Phase 2 of the core split. These are plain data - what a row holds and how
// it reads aloud - so they belong beside the rules rather than inside the window that happens to
// show them today. While they were private to Form1 no second front end could name them at all.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>One row in a report list: the spoken/displayed text plus an optional Enter action.</summary>
public sealed class ReportRow
{
	public string Text = "";
	/// <summary>A term to search for in the Discovery tab on Enter (e.g. a missing mod's UniqueID), or null.</summary>
	public string? SearchTerm;
	/// <summary>A page to open in the browser on Enter, or null.</summary>
	public string? OpenUrl;
	/// <summary>Stable key identifying a requirement warning so it can be hidden with Delete, or null if not ignorable.</summary>
	public string? IgnoreKey;
	/// <summary>An action to run on Enter (e.g. set this mod's update link), or null. Runs after the report closes.</summary>
	public Func<Task>? OnEnter;
	public override string ToString() => Text;
}

/// <summary>The result of the broken-mod check: the finding rows, an optional spoken advisory, and whether the
/// community compatibility data simply couldn't be loaded (offline / not applicable to this game).</summary>
public sealed class BrokenModFindings
{
	public List<ReportRow> Rows = new();
	/// <summary>A finding-level advisory spoken once as context (e.g. the loose-files note), or null.</summary>
	public string? OpeningNote;
	/// <summary>True when the compatibility list couldn't be loaded or the game has no such data.</summary>
	public bool DataUnavailable;
}
