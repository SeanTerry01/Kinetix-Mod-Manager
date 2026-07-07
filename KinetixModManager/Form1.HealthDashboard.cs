using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// The "Check My Setup" command (Setup Health Dashboard). It runs every problem-detecting check the manager
/// already has — missing requirements (plugin masters, script extender, Nexus requirements), known-broken or
/// incompatible mods, and file conflicts — in one pass, then presents a single accessible list with one spoken
/// summary that breaks the total down by category. It reuses the same finding rows the standalone reports build,
/// so each row keeps its own Enter action (search for / open a missing mod), Delete-to-ignore for requirement
/// warnings, and F9 "ask the AI about this" — making the dashboard a superset of the individual reports rather
/// than a replacement for them.
/// </summary>
public partial class Form1
{
	/// <summary>
	/// Runs the aggregated setup health check for the active game and shows the combined report. Broken-mod and
	/// Nexus-requirement lookups can touch the network, so the whole pass runs under one spoken status; an empty
	/// result reports a clean bill of health. Rows are ordered severity-first: missing requirements (which can stop
	/// the game loading) before known-broken mods before file conflicts.
	/// </summary>
	private async Task RunHealthCheck()
	{
		if (_settings.ActiveGame == "None")
		{
			Speak(Loc.T("health.noGame"));
			return;
		}

		SetStatus(Loc.T("health.checking"), speak: true);

		(List<ReportRow> reqRows, _) = await GatherRequirementFindings();
		BrokenModFindings broken = await GatherBrokenModFindings();
		List<ReportRow> conflictRows = GatherFileConflictFindings();

		ResetStatus();

		// Tag each finding with its category so a single flat, arrowable list stays self-describing. The rows are
		// freshly gathered, so prefixing their Text in place is safe. Order is severity-first.
		var rows = new List<ReportRow>();
		foreach (ReportRow r in reqRows) { r.Text = Loc.T("health.rowReq", r.Text); rows.Add(r); }
		foreach (ReportRow r in broken.Rows) { r.Text = Loc.T("health.rowBroken", r.Text); rows.Add(r); }
		foreach (ReportRow r in conflictRows) { r.Text = Loc.T("health.rowConflict", r.Text); rows.Add(r); }

		// A note appended to the spoken summary when a check couldn't reach its online data (e.g. offline), so the
		// user knows an all-clear may be incomplete rather than assuming everything was verified.
		string dataNote = broken.DataUnavailable ? " " + Loc.T("health.dataNote") : "";

		if (rows.Count == 0)
		{
			ShowReportDialog(Loc.T("health.title"), Loc.T("health.title"),
				Loc.T("health.none") + dataNote, rows, null, listName: Loc.T("health.listName"));
			return;
		}

		// Per-category breakdown for the spoken summary, listing only the categories that actually found something,
		// in the same severity-first order as the rows.
		var parts = new List<string>();
		if (reqRows.Count > 0)
			parts.Add(Loc.T(reqRows.Count == 1 ? "health.sumReqOne" : "health.sumReqMany", reqRows.Count));
		if (broken.Rows.Count > 0)
			parts.Add(Loc.T(broken.Rows.Count == 1 ? "health.sumBrokenOne" : "health.sumBrokenMany", broken.Rows.Count));
		if (conflictRows.Count > 0)
			parts.Add(Loc.T(conflictRows.Count == 1 ? "health.sumConflictOne" : "health.sumConflictMany", conflictRows.Count));

		string totalPhrase = Loc.T(rows.Count == 1 ? "health.problemsOne" : "health.problemsMany", rows.Count);
		string header = Loc.T("health.headerProblems", totalPhrase, string.Join(", ", parts)) + dataNote;

		// Rows carry their own Enter actions (search a missing mod, open a Nexus page). Reuse the requirement ignore
		// handler so Delete still hides a false-positive requirement warning from the dashboard too; broken/conflict
		// rows have no IgnoreKey, so Delete is a no-op on them.
		bool anyAction = rows.Any(r => !string.IsNullOrEmpty(r.SearchTerm) || !string.IsNullOrEmpty(r.OpenUrl));
		string? hint = anyAction ? Loc.T("reports.reqActionHint") : null;

		ShowReportDialog(Loc.T("health.title"), header, Loc.T("health.none"), rows, hint,
			IgnoreRequirementRow, listName: Loc.T("health.listName"));
	}
}
