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
///
/// One check lives only here and has no standalone report of its own: the Visual C++ runtime check, which is
/// about the machine rather than the active game. It has no report because there is nothing to browse — it
/// either finds one fault or says nothing — and it belongs in the pass a user runs when something is wrong but
/// they cannot tell what.
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

		ReportRow? runtimeRow = GatherRuntimeFinding();
		ReportRow? minecraftRow = GatherMinecraftLaunchFinding();
		(List<ReportRow> reqRows, _) = await GatherRequirementFindings();
		ReportRow? limitRow = GatherPluginLimitFinding();
		List<ReportRow> partRows = GatherMissingPartFindings();
		List<ReportRow> leftoverRows = GatherSupersededPartFindings();
		BrokenModFindings broken = await GatherBrokenModFindings();
		List<ReportRow> conflictRows = GatherFileConflictFindings();

		ResetStatus();

		// Tag each finding with its category so a single flat, arrowable list stays self-describing. The rows are
		// freshly gathered, so prefixing their Text in place is safe. Order is severity-first: missing requirements
		// and a breached plugin limit both stop the game loading, so they lead.
		var rows = new List<ReportRow>();
		// First of all, because it outranks everything below it: a broken C runtime stops native mods loading in
		// every game at once, and no amount of fixing requirements or conflicts will help until it is repaired.
		if (runtimeRow != null) rows.Add(runtimeRow);
		// Straight after the runtime, and for the same reason: it is the difference between "a mod is
		// misconfigured" and "no mod ran at all". Everything below is about a game that at least loaded them.
		if (minecraftRow != null) rows.Add(minecraftRow);
		foreach (ReportRow r in reqRows) { r.Text = Loc.T("health.rowReq", r.Text); rows.Add(r); }
		if (limitRow != null) { limitRow.Text = Loc.T("health.rowLimit", limitRow.Text); rows.Add(limitRow); }
		// With the missing requirements, because a half-installed mod is the same kind of problem: the mod is
		// there, it looks installed, and it cannot load. Engine Fixes without its preloader is the usual one.
		foreach (ReportRow r in partRows) { r.Text = Loc.T("health.rowParts", r.Text); rows.Add(r); }
		// Last of the mod-shape findings and deliberately so: nothing here is broken. A leftover file is tidying,
		// worth saying once and worth nobody's alarm.
		foreach (ReportRow r in leftoverRows) { r.Text = Loc.T("health.rowLeftover", r.Text); rows.Add(r); }
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
		if (runtimeRow != null)
			parts.Add(Loc.T("health.sumRuntime"));
		if (reqRows.Count > 0)
			parts.Add(Loc.T(reqRows.Count == 1 ? "health.sumReqOne" : "health.sumReqMany", reqRows.Count));
		if (limitRow != null)
			parts.Add(Loc.T("health.sumLimit"));
		if (partRows.Count > 0)
			parts.Add(Loc.T(partRows.Count == 1 ? "health.sumPartsOne" : "health.sumPartsMany", partRows.Count));
		if (leftoverRows.Count > 0)
			parts.Add(Loc.T(leftoverRows.Count == 1 ? "health.sumLeftoverOne" : "health.sumLeftoverMany", leftoverRows.Count));
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

		// The mod-part rows are the ones fixed from inside this list — Enter fetches a missing part or removes a
		// leftover one — so after each action they are checked again, and a fixed one leaves. Everything else here
		// either opens a page or goes off to search, and needs the network to re-check, so it is left as found.
		var partTexts = new HashSet<string>(partRows.Concat(leftoverRows).Select(r => r.Text));
		List<ReportRow> RebuildAfterAction()
		{
			var stillThere = new HashSet<string>(
				GatherMissingPartFindings().Select(r => Loc.T("health.rowParts", r.Text))
					.Concat(GatherSupersededPartFindings().Select(r => Loc.T("health.rowLeftover", r.Text))));
			rows.RemoveAll(r => partTexts.Contains(r.Text) && !stillThere.Contains(r.Text));
			return rows.ToList();
		}

		ShowReportDialog(Loc.T("health.title"), header, Loc.T("health.none"), rows, hint,
			// Hidden rows must leave the copy the rebuild works from too, or the next rebuild would bring them back.
			r => { IgnoreRequirementRow(r); rows.Remove(r); },
			listName: Loc.T("health.listName"), rebuildRows: RebuildAfterAction);
	}

	/// <summary>
	/// A Health Check finding for a broken Visual C++ runtime, or null when it is healthy or cannot be judged.
	///
	/// Unlike every other check here this one is not about the active game — it is about the machine, and a
	/// problem it finds affects all of them. It earns its place in a mod manager's health check because the mods
	/// most likely to be silenced by it are the accessibility mods, and their failure mode is silence: the game
	/// launches, says nothing, and leaves no error for a screen reader to find. Pointing at the runtime is the
	/// difference between a five-minute repair and a lost evening.
	/// </summary>
	/// <summary>
	/// Whether the last run of Minecraft actually had its mods, or <c>null</c> for any other game and when
	/// there is nothing to report.
	///
	/// <para>
	/// The single most valuable check this game has, because it is the one failure the game gives no sign of.
	/// A correctly installed Fabric, a correct mods folder and a launcher that started the vanilla profile
	/// produce a game that runs perfectly and never speaks — and nothing on screen, in the game or in the
	/// manager says why. It cost a real evening before the manager could answer it.
	/// </para>
	/// </summary>
	private ReportRow? GatherMinecraftLaunchFinding()
	{
		if (GameProfiles.Find(_settings.ActiveGame)?.IsMinecraft != true) return null;

		// This session's log: a modpack writes its own, in its own folder.
		string root = MinecraftGameFolder();
		MinecraftLaunchOutcome outcome = MinecraftLaunchLog.ReadLatest(root);

		// Never launched, or the log has been cleared: nothing to report either way, and inventing a warning
		// about a game the user has not played yet would be noise.
		if (outcome.NoLog) return null;

		if (outcome.FabricLoaded)
		{
			// Reported even when everything is fine. "Your mods did load, and here is how many" is worth
			// hearing in a game whose usual failure is silence — an all-clear that says nothing at all leaves
			// the user no better off than before they asked.
			return new ReportRow
			{
				Text = outcome.ModCount >= 0
					? Loc.T("health.mcLoaded", outcome.GameVersion, outcome.LoaderVersion, outcome.ModCount)
					: Loc.T("health.mcLoadedNoCount", outcome.GameVersion, outcome.LoaderVersion)
			};
		}

		return new ReportRow { Text = Loc.T("health.mcVanilla") };
	}

	private ReportRow? GatherRuntimeFinding()
	{
		VcRuntimeVerdict? verdict = VcRuntimeCheck.Inspect();
		if (verdict == null || verdict.IsConsistent) return null;

		string text = verdict.CoreMissing
			? Loc.T("health.runtimeMissing")
			: Loc.T("health.runtimeMismatch",
				string.Join(", ", verdict.Stale.Select(f => Loc.T("health.runtimeFile",
					f.Name, VcRuntimeCheck.Format(f.Version!)))),
				VcRuntimeCheck.Format(verdict.Expected!));

		return new ReportRow
		{
			Text = Loc.T("health.rowRuntime", text),
			OpenUrl = VcRuntimeCheck.DownloadUrl
		};
	}
}
