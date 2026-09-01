using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// Two accessible, list-driven health reports: a file-conflict report (which mods overwrite the same file on
/// Skyrim/Fallout 4, or duplicate UniqueIDs on Stardew) and a requirements check (missing required mods,
/// missing plugin masters, missing script extender, and each mod's Nexus "Requirements" tab). Both adapt to
/// the active game's mod system.
/// </summary>
public partial class Form1
{
	/// <summary>One row in a report list: the spoken/displayed text plus an optional Enter action.</summary>
	private sealed class ReportRow
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

	// ---------------------------------------------------------------------
	// File-conflict report
	// ---------------------------------------------------------------------

	/// <summary>
	/// Shows the file-conflict report. On Skyrim/Fallout 4 it lists every loose file provided by more than one
	/// enabled mod, naming the winner and the overridden mods. On Stardew (where mods are isolated) it instead
	/// lists mods that share a UniqueID, which breaks SMAPI. No game loaded shows an explanatory empty message.
	/// </summary>
	private void ShowFileConflictsReport()
	{
		List<ReportRow> rows = GatherFileConflictFindings();

		if (GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley))
			ShowReportDialog(Loc.T("reports.conflictTitle"), Loc.T("reports.dupHeader"),
				Loc.T("reports.dupNone"), rows, null);
		else if (!IsBethesdaGame)
			ShowReportDialog(Loc.T("reports.conflictTitle"), Loc.T("reports.conflictHeader"),
				Loc.T("reports.conflictNoneGame"), rows, null);
		else
			ShowReportDialog(Loc.T("reports.conflictTitle"), Loc.T("reports.conflictHeader"),
				Loc.T("reports.conflictNone"), rows, null);
	}

	/// <summary>
	/// Gathers the file-conflict findings for the active game without showing any UI, so both the standalone
	/// report and the Setup Health Check can share the same computation. Stardew: mods sharing a UniqueID.
	/// Skyrim/Fallout 4: each loose file provided by more than one enabled mod, naming winner and losers. Any
	/// other game returns an empty list.
	/// </summary>
	private List<ReportRow> GatherFileConflictFindings()
	{
		var rows = new List<ReportRow>();

		if (GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley))
		{
			foreach (var (uid, names) in ModHealth.FindDuplicateUniqueIds(_allInstalledMods))
				rows.Add(new ReportRow { Text = Loc.T("reports.dupId", uid, string.Join(", ", names)) });
			return rows;
		}

		if (!IsBethesdaGame) return rows;

		// Map a mod's priority key (folder name) to its friendly display name for the report.
		var nameByKey = _allInstalledMods.Where(m => !m.IsGroup)
			.GroupBy(PriorityKey, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
		string Disp(string key) => nameByKey.TryGetValue(key, out string? n) ? n : key;

		foreach (FileConflict c in _lastConflicts.OrderBy(c => c.RelativePath, StringComparer.OrdinalIgnoreCase))
			rows.Add(new ReportRow
			{
				Text = Loc.T("reports.conflictRow", c.RelativePath, Disp(c.Winner), string.Join(", ", c.Losers.Select(Disp)))
			});

		return rows;
	}

	// ---------------------------------------------------------------------
	// Requirements check
	// ---------------------------------------------------------------------

	/// <summary>
	/// Shows the requirements report. Stardew: each enabled mod's missing/disabled/outdated required dependencies
	/// (including a content pack's host). Skyrim/Fallout 4: plugins with missing masters, a missing script
	/// extender, and each Nexus-linked mod's online "Requirements" that aren't installed. Nexus lookups run one
	/// mod at a time with a progress status, so a large load order can take a moment.
	/// </summary>
	private async Task ShowRequirementsReport()
	{
		(List<ReportRow> rows, int hiddenCount) = await GatherRequirementFindings();

		string hint = rows.Count > 0 ? Loc.T("reports.reqActionHint") : null!;
		if (hiddenCount > 0)
			hint = (hint ?? "") + Loc.T("reports.reqHiddenSuffix", hiddenCount);

		ShowReportDialog(Loc.T("reports.reqTitle"), Loc.T("reports.reqHeader", GameDisplayName()),
			Loc.T("reports.reqNone"), rows, hint, IgnoreRequirementRow);
	}

	/// <summary>
	/// Gathers the requirement findings for the active game without showing any UI, returning the (collapsed,
	/// ignore-filtered) rows plus how many warnings were hidden by the user's ignore list. Shared by the
	/// standalone requirements report and the Setup Health Check. Skyrim/Fallout 4 lookups hit the Nexus
	/// "Requirements" tab one mod at a time (with a non-spoken progress status), so a large load order can take
	/// a moment; the caller is responsible for the overall spoken status.
	/// </summary>
	private async Task<(List<ReportRow> rows, int hiddenCount)> GatherRequirementFindings()
	{
		var enabled = _allInstalledMods.Where(m => !m.IsGroup && m.IsEnabled).ToList();
		var rows = new List<ReportRow>();

		if (GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley))
		{
			foreach (GameMod mod in enabled)
				foreach (ModDependency dep in mod.Dependencies.Where(d => d.IsRequired))
				{
					string depKey = $"dep|{mod.Name}|{dep.UniqueId}";
					if (!dep.IsPresent)
						rows.Add(new ReportRow
						{
							Text = Loc.T("reports.depMissing", mod.Name, dep.UniqueId),
							SearchTerm = ModNameMatch.SearchTermForIdentifier(dep.UniqueId),
							IgnoreKey = depKey
						});
					else if (!dep.IsEnabled)
						rows.Add(new ReportRow { Text = Loc.T("reports.depDisabled", mod.Name, dep.UniqueId), IgnoreKey = depKey });
					else if (!dep.IsNewEnough)
						rows.Add(new ReportRow { Text = Loc.T("reports.depOutdated", mod.Name, dep.UniqueId, dep.MinimumVersion ?? ""), IgnoreKey = depKey });
				}
		}
		else if (IsBethesdaGame)
		{
			// 1. Missing plugin masters (offline). Checked against the active load order (plugins.txt), which
			// includes enabled mods' plugins and active Creations, so an enabled Creation counts as present.
			var activePlugins = new HashSet<string>(
				ModFileSystem.ReadActivePlugins(_settings.ActiveGame, _settings.CurrentGamePath), StringComparer.OrdinalIgnoreCase);
			foreach (ModFileSystem.MasterIssue issue in
				ModFileSystem.FindMissingMasters(_settings.ActiveGame, enabled, activePlugins, _settings.CurrentGamePath))
				rows.Add(new ReportRow
				{
					Text = Loc.T(issue.PresentButInactive ? "reports.masterInactive" : "reports.missingMaster",
						issue.Plugin, issue.OwnerMod, issue.Master),
					IgnoreKey = $"master|{issue.Plugin}|{issue.Master}"
				});

			// 2. Script extender (SKSE/F4SE) missing, or installed for a game build the user is not running —
			// which looks identical from inside the game, because the extender simply never loads.
			string seId = GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.SkyrimSE) ? "30379" : "42147"; // SKSE64 / F4SE Nexus ids
			string seName = ScriptExtenderInfo.DisplayName(_settings.ActiveGame);
			// The same folder the suite installer works on, fallback included. The two screens answering "is the
			// script extender installed?" from different paths is how one could say yes while the other said no.
			string seGamePath = string.IsNullOrEmpty(_settings.CurrentGamePath)
				? DetectGameFolder(_settings.ActiveGame)
				: _settings.CurrentGamePath;
			ScriptExtenderStatus? se = ModFileSystem.ReadScriptExtenderStatus(_settings.ActiveGame, seGamePath);

			// "I looked there and it is not present" and "I do not know where your game is" are different
			// statements, and reporting the second as the first tells the user something untrue about their
			// install. Naming the folder that was searched is also what lets them see at a glance that it is the
			// wrong copy — the Steam one when they play the GOG one, say.
			if (string.IsNullOrEmpty(seGamePath) || !Directory.Exists(seGamePath))
				rows.Add(new ReportRow { Text = Loc.T("reports.gameFolderUnknown", GameProfiles.DisplayNameFor(_settings.ActiveGame)), IgnoreKey = "gameFolder" });
			else if (se == null)
				rows.Add(new ReportRow { Text = Loc.T("reports.seMissing", seName, seGamePath), IgnoreKey = "se" });
			else if (se.CanCompare && !se.Match)
				rows.Add(new ReportRow
				{
					Text = Loc.T("reports.seMismatch", seName, se.TargetVersion, se.GameVersion),
					IgnoreKey = "seMismatch"
				});

			// 2b. The Address Library, and the DLL plugins that depend on it. A matching script extender is only
			// the first link in the chain; everything below it fails silently, which is why this is checked at all.
			rows.AddRange(GatherDllPluginFindings(se, seGamePath));

			// 3. Nexus "Requirements" tab for each installed Nexus-linked mod (online, best-effort).
			var installedIds = new HashSet<string>(
				enabled.Where(m => !string.IsNullOrEmpty(m.NexusID)).Select(m => m.NexusID!),
				StringComparer.OrdinalIgnoreCase);
			var nexusMods = enabled.Where(m => !string.IsNullOrEmpty(m.NexusID)).ToList();
			for (int i = 0; i < nexusMods.Count; i++)
			{
				GameMod mod = nexusMods[i];
				SetStatus(Loc.T("reports.checkingReqs", i + 1, nexusMods.Count), speak: false);
				List<NexusService.ModRequirementInfo> reqs = await _nexusService.GetModRequirementsAsync(mod.NexusID!);
				foreach (NexusService.ModRequirementInfo req in reqs)
				{
					if (req.External) continue;                                   // off-Nexus: can't verify
					if (req.ModId == seId) continue;                              // the script extender is covered above
					if (IsVrOnlyRequirement(req.ModName)) continue;               // VR-only: N/A to the flat games we manage
					if (!string.IsNullOrEmpty(req.ModId) && installedIds.Contains(req.ModId)) continue; // satisfied
					string url = !string.IsNullOrEmpty(req.Url)
						? req.Url
						: $"https://www.nexusmods.com/{_nexusService.CurrentGameDomain}/mods/{req.ModId}";
					string note = string.IsNullOrWhiteSpace(req.Notes) ? "" : Loc.T("reports.nexusReqNote", req.Notes.Trim());
					string reqKey = $"nexus|{mod.Name}|{(string.IsNullOrEmpty(req.ModId) ? req.ModName : req.ModId)}";
						rows.Add(new ReportRow { Text = Loc.T("reports.nexusReqMissing", mod.Name, req.ModName, note), OpenUrl = url, IgnoreKey = reqKey });
				}
			}
			ResetStatus();
		}

		// Collapse identical lines (e.g. several mods needing the same missing requirement).
		rows = rows.GroupBy(r => r.Text).Select(g => g.First()).ToList();

		// Drop warnings the user has chosen to hide for this game (false positives the manager can't auto-detect,
		// e.g. a Nexus requirement that's optional or satisfied by an alternative). Delete in the dialog adds them.
		var ignored = new HashSet<string>(
			_settings.IgnoredRequirements.TryGetValue(_settings.ActiveGame, out var ig) ? ig : new List<string>(),
			StringComparer.OrdinalIgnoreCase);
		int hiddenCount = rows.RemoveAll(r => r.IgnoreKey != null && ignored.Contains(r.IgnoreKey));

		return (rows, hiddenCount);
	}

	/// <summary>Persists a requirement warning to this game's ignore list so it stops appearing in the report.</summary>
	private void IgnoreRequirementRow(ReportRow row)
	{
		if (string.IsNullOrEmpty(row.IgnoreKey)) return;
		if (!_settings.IgnoredRequirements.TryGetValue(_settings.ActiveGame, out var list))
			_settings.IgnoredRequirements[_settings.ActiveGame] = list = new List<string>();
		if (!list.Contains(row.IgnoreKey, StringComparer.OrdinalIgnoreCase))
		{
			list.Add(row.IgnoreKey);
			_settings.Save();
		}
	}

	/// <summary>Clears every hidden requirement warning for the active game (Mods menu), so they show again.</summary>
	private void ResetIgnoredRequirements()
	{
		if (_settings.IgnoredRequirements.TryGetValue(_settings.ActiveGame, out var list) && list.Count > 0)
		{
			int n = list.Count;
			list.Clear();
			_settings.Save();
			Speak(Loc.T("reports.reqResetDone", n));
		}
		else
		{
			Speak(Loc.T("reports.reqResetNone"));
		}
	}

	/// <summary>
	/// True for requirements that only apply to the VR editions (Skyrim VR / Fallout 4 VR) — e.g. "VR Address
	/// Library for SKSEVR". This manager targets the flat games, so such requirements are never relevant and
	/// would otherwise be reported as missing forever. Matched on the VR script-extender names, which are specific.
	/// </summary>
	private static bool IsVrOnlyRequirement(string modName) =>
		modName.Contains("SKSEVR", StringComparison.OrdinalIgnoreCase) ||
		modName.Contains("F4SEVR", StringComparison.OrdinalIgnoreCase) ||
		modName.Contains("VR Address Library", StringComparison.OrdinalIgnoreCase);

	// ---------------------------------------------------------------------
	// Verify a mod's files actually reached the game folder
	// ---------------------------------------------------------------------

	/// <summary>
	/// Checks every file the selected Skyrim/Fallout 4 mod ships against the game folder and reports whether each
	/// landed: in place (deployed and owned by this mod), overridden (a higher-priority mod owns that path), or
	/// missing (not in the game folder at all). Missing files mean the mod isn't actually active on disk even if
	/// its plugin is enabled — the decisive check when a mod "looks installed" but does nothing in game.
	/// </summary>
	private void VerifyModDeployment()
	{
		if (!IsBethesdaGame)
		{
			ShowReportDialog(Loc.T("reports.verifyTitle"), Loc.T("reports.verifyHeaderGeneric"),
				Loc.T("reports.verifyNotApplicable"), new List<ReportRow>(), null);
			return;
		}
		if (listInstalled.SelectedItem is not GameMod mod || mod.IsGroup || string.IsNullOrEmpty(mod.FolderPath))
		{
			Speak(Loc.T("reports.verifySelectMod"));
			return;
		}
		string gameRoot = _settings.CurrentGamePath;
		if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
		{
			Speak(Loc.T("reports.verifyNoGamePath"));
			return;
		}

		var manifest = DeploymentManifest.Load(_settings.ActiveGame);
		string modKey = PriorityKey(mod);
		string canonical = Path.GetFullPath(mod.FolderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

		int inPlace = 0, missing = 0, overridden = 0;
		var problems = new List<ReportRow>();
		foreach (string sourceFile in Directory.GetFiles(mod.FolderPath, "*.*", SearchOption.AllDirectories))
		{
			string rel = Path.GetFullPath(sourceFile).Substring(canonical.Length);
			if (Path.GetFileName(sourceFile).Equals(".manager_manifest.json", StringComparison.OrdinalIgnoreCase) ||
				rel.StartsWith(ModFileSystem.DocsFolderName, StringComparison.OrdinalIgnoreCase))
				continue;

			bool isRoot = rel.StartsWith("Root" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
						  rel.StartsWith("Root" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
			string destRel = isRoot ? rel.Substring(5) : Path.Combine("Data", rel);
			string destAbs = Path.Combine(gameRoot, destRel);

			if (File.Exists(destAbs))
			{
				if (manifest.Deployed.TryGetValue(destRel, out string? owner) &&
					!string.Equals(owner, modKey, StringComparison.OrdinalIgnoreCase))
				{
					overridden++;
					problems.Add(new ReportRow { Text = Loc.T("reports.verifyOverridden", destRel, owner) });
				}
				else inPlace++;
			}
			else
			{
				missing++;
				problems.Add(new ReportRow { Text = Loc.T("reports.verifyMissing", destRel) });
			}
		}

		string header = Loc.T("reports.verifyHeader", mod.Name, inPlace, missing, overridden);
		if (!mod.IsEnabled)
			problems.Insert(0, new ReportRow { Text = Loc.T("reports.verifyDisabledNote") });
		ShowReportDialog(Loc.T("reports.verifyTitle"), header, Loc.T("reports.verifyAllInPlace"), problems, null);
	}

	// ---------------------------------------------------------------------
	// Shared accessible report dialog
	// ---------------------------------------------------------------------

	/// <summary>
	/// Presents <paramref name="rows"/> in a modal, screen-reader-friendly list. The header is spoken on open
	/// along with the item count; Escape closes; Enter on a row runs its action (search Discovery for a missing
	/// mod, or open a page). When <paramref name="onIgnore"/> is supplied, Delete on a row with an IgnoreKey hides
	/// it (used by the requirements report). An empty result shows <paramref name="emptyMessage"/> as the only row.
	/// </summary>
	private void ShowReportDialog(string title, string header, string emptyMessage, List<ReportRow> rows, string? actionHint,
			Action<ReportRow>? onIgnore = null, string? listName = null, string? openingNote = null)
		{
			// Shown inside the main window rather than as one of its own — see Form1.InlineView.
			ShowInlineView(title, (container, closeView) =>
			{
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

			layout.Controls.Add(new Label { Text = header, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8) }, 0, 0);

			// Give the list its own accessible name — deliberately distinct from BOTH the window title (which the
			// screen reader already announces when the dialog opens) and the header (spoken once in the opening
			// announcement). Reusing either would make the screen reader say it a second time the instant focus lands
			// on the list. Callers pass a name like "Broken Mods List"; the default covers reports that don't.
			var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = listName ?? Loc.T("reports.listGeneric"), IntegralHeight = false, HorizontalScrollbar = true };
			bool hasRows = rows.Count > 0;
			// With findings, add them and pre-select the first so it's the focused (and thus spoken) item on open —
			// with a single finding there's nothing to arrow onto, so without an initial selection the lone row is
			// never read. With no findings, leave the list genuinely empty: the empty message is already spoken in the
			// opening announcement, and an empty list makes the screen reader say "List is empty" (nothing to arrow
			// through) rather than reading that same message again as a lone, navigable row.
			if (hasRows)
			{
				foreach (ReportRow r in rows) list.Items.Add(r);
				list.SelectedIndex = 0;
			}
			layout.Controls.Add(list, 0, 1);
			container.Controls.Add(layout);

			WireAccessibleDialogList(list);
			// Escape is handled by the view itself (see Form1.InlineView).
			list.KeyDown += (_, e) =>
			{
				if (list.SelectedItem is not ReportRow row) return;

				// F9 asks the configured AI provider about the selected finding.
				if (e.KeyCode == Keys.F9 && hasRows && _aiService.IsConfigured)
				{
					e.Handled = e.SuppressKeyPress = true;
					closeView();
					AskAiAbout(title, Loc.T("ai.aboutReportRow", title, row.Text));
					return;
				}

				// Delete hides a requirement warning the manager can't auto-resolve (e.g. an optional/alternative-
				// satisfied Nexus requirement). Only rows that opted in (IgnoreKey set) and reports that supplied an
				// onIgnore handler are ignorable.
				if (e.KeyCode == Keys.Delete && onIgnore != null && !string.IsNullOrEmpty(row.IgnoreKey))
				{
					e.Handled = e.SuppressKeyPress = true;
					onIgnore(row);
					int idx = list.SelectedIndex;
					list.Items.Remove(row);
					if (list.Items.Count == 0) { Speak(Loc.T("reports.ignoredLast")); closeView(); return; }
					list.SelectedIndex = Math.Min(idx, list.Items.Count - 1);
					Speak(Loc.T("reports.ignored", list.Items.Count));
					return;
				}

				if (e.KeyCode != Keys.Enter) return;
				if (row.OnEnter != null)
				{
					// Close first: the action opens its own dialog (and may refresh the lists behind this one).
					e.Handled = e.SuppressKeyPress = true;
					closeView();
					_ = row.OnEnter();
					return;
				}
				if (!string.IsNullOrEmpty(row.SearchTerm))
				{
					if (SpeakBox(Loc.T("reports.searchConfirm", row.SearchTerm), Loc.T("reports.searchTitle"),
							MessageBoxButtons.YesNo) == DialogResult.Yes)
					{
						closeView();
						SelectTab(AppTab.Discovery);
						txtSearch.Text = row.SearchTerm;
						Fire(RunDiscovery(), "RunDiscovery");
					}
				}
				else if (!string.IsNullOrEmpty(row.OpenUrl))
				{
					try { Process.Start(new ProcessStartInfo(row.OpenUrl) { UseShellExecute = true }); }
					catch (Exception ex) { DiagnosticLog.WriteException("Reports", $"opening {row.OpenUrl}", ex); }
				}
			};

			return list;
			},
			// Through the hint, so it follows the title rather than arriving ahead of it. A report's header
			// describes its findings rather than repeating its title, so unlike the other views it is kept whole.
			hint: ReportOpening(header, emptyMessage, rows, actionHint, openingNote));
		}

	/// <summary>
	/// What a report says on the way in, after its title: what it looked for, how much it found, and what the keys
	/// do — or, when it found nothing, why that is good news.
	/// </summary>
	private string ReportOpening(string header, string emptyMessage, List<ReportRow> rows, string? actionHint, string? openingNote)
	{
		bool hasRows = rows.Count > 0;
		// The header may already end in a period (the broken-mods and verify headers do); strip it before adding
		// the ". " separator so screen readers don't speak a stray ".." pause. Likewise say "1 item", not
		// "1 items", for a single finding.
		string spokenHeader = header.EndsWith(".") ? header[..^1] : header;
		string opening = spokenHeader + ". " + (hasRows
			? Loc.T(rows.Count == 1 ? "reports.itemCountOne" : "reports.itemCount", rows.Count)
				+ (actionHint != null ? ". " + actionHint : "")
			: emptyMessage);
		if (hasRows && _aiService.IsConfigured) opening += ". " + Loc.T("ai.reportHint");
		// A finding-level advisory (e.g. the loose-files explanation): spoken as context in the opening so the
		// list rows themselves can stay short and scannable, rather than each carrying a paragraph.
		if (hasRows && !string.IsNullOrEmpty(openingNote)) opening += ". " + openingNote;
		return opening;
	}

	/// <summary>
	/// The findings below the script extender: the Address Library's coverage of the game build, and the DLL
	/// plugins the script extender actually refused the last time the game ran.
	///
	/// These deserve their own check because they are the only failures in the whole chain that leave no trace a
	/// player can find. A missing mod is absent from a list; a broken load order stops the game starting. A DLL
	/// plugin that will not load produces a game that launches normally and simply does less than it should —
	/// and when the plugin is the accessibility mod, "does less" means says nothing at all.
	/// </summary>
	private List<ReportRow> GatherDllPluginFindings(ScriptExtenderStatus? se, string gamePath)
	{
		var rows = new List<ReportRow>();
		if (se == null) return rows;   // no script extender at all is already reported above

		// 0. The game did not finish starting. First because it outranks everything below it by a distance: the
		// rest of this method describes a game that runs and does less than it should, and this one describes a
		// game that does not run.
		(string? stuckOn, bool stillUp) = PluginThatStoppedTheGame();
		if (stuckOn != null)
			rows.Add(new ReportRow
			{
				Text = Loc.T(stillUp ? "reports.pluginStuckGame" : "reports.pluginStoppedGame",
					stuckOn, GameProfiles.DisplayNameFor(_settings.ActiveGame)),
				SearchTerm = ModNameMatch.SearchTermForIdentifier(stuckOn),
				IgnoreKey = $"stuck|{stuckOn}"
			});

		// 1. Address Library coverage. Only when it is installed: a missing one is a missing requirement, which
		// the suite check already reports, and saying both would be two rows for one thing.
		var lib = ScriptExtenderPlugins.ReadAddressLibrary(_settings.ActiveGame, gamePath, se.GameVersion);
		if (lib.CanJudge && !lib.CoversGame)
			rows.Add(new ReportRow
			{
				Text = Loc.T("reports.addrLibBehind", se.GameVersion, lib.NewestBuild),
				OpenUrl = $"https://www.nexusmods.com/{_nexusService.CurrentGameDomain}/mods/" +
					(GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.SkyrimSE) ? "32444" : "47327"),
				IgnoreKey = "addrLib"
			});

		// 2. What the script extender's own log says was refused last run. Historical rather than predictive —
		// it describes the last launch — but it is the only place that names the individual mods, and it catches
		// reasons the manager has no way to anticipate.
		foreach (PluginLoadFailure fail in ReadLastPluginFailures())
		{
			// A plugin refused for want of an Address Library is the row above, said again per mod; naming them
			// is the point, but the fix is the same one and does not need repeating on every line.
			string key = fail.Kind switch
			{
				PluginFailureKind.AddressLibrary => "reports.pluginAddrLib",
				PluginFailureKind.GameVersion    => "reports.pluginGameVersion",
				_                                => "reports.pluginOther"
			};
			rows.Add(new ReportRow
			{
				Text = Loc.T(key, fail.Name, se.GameVersion, fail.Reason),
				SearchTerm = ModNameMatch.SearchTermForIdentifier(fail.Name),
				IgnoreKey = $"plugin|{fail.PluginFile}"
			});
		}

		return rows;
	}

	/// <summary>
	/// How long the script extender's log has to have been untouched before a half-finished load counts as
	/// finished-with rather than still going. Loading a plugin is the work of moments, so a log that has not been
	/// written to in a minute is not a load in progress.
	/// </summary>
	private const int LogGoneQuietSeconds = 60;

	/// <summary>
	/// The plugin the game was loading when it got no further, and whether the game is still up.
	///
	/// Both answers matter, because the failure has two faces and a player meets both. The game can die outright —
	/// a plugin puts up an error box and terminates it — and the game can simply <em>stay</em> there, a process
	/// with no window, using no processor time, blocked inside a plugin that never returned. That second one is
	/// the worse of the two to be told nothing about: the manager sees a live game process and says the game is
	/// running, which is true and useless, while nothing at all is on screen.
	///
	/// What separates a stuck launch from a healthy one is not the process but the <b>log going quiet</b>. A
	/// launch in progress writes continuously, a line per plugin; loading a plugin takes moments. So a log with a
	/// plugin still outstanding that has not been written to for <see cref="LogGoneQuietSeconds"/> is not a load
	/// in progress, whatever the process list says. Without that test the check would have to stay silent
	/// whenever the game was up — which is precisely when a stuck launch needs explaining.
	/// </summary>
	private (string? Plugin, bool GameStillRunning) PluginThatStoppedTheGame()
	{
		try
		{
			GameProfile? profile = GameProfiles.Find(_settings.ActiveGame);
			if (profile == null) return (null, false);

			string path = Path.Combine(GameLogFolder(), PrimaryGameLogName());
			if (!File.Exists(path)) return (null, false);

			// Shared read: a stuck game still holds its log open.
			string? plugin = ScriptExtenderPlugins.PluginLeftLoading(ReadAllLinesShared(path));
			if (plugin == null) return (null, false);

			bool running = IsGameProcessRunning(profile.GameExeName);
			if (!running) return (plugin, false);

			// The game is up. Only a log that has stopped being written proves it is stuck rather than starting.
			DateTime lastWrite = File.GetLastWriteTimeUtc(path);
			bool quiet = (DateTime.UtcNow - lastWrite).TotalSeconds >= LogGoneQuietSeconds;
			return quiet ? (plugin, true) : (null, false);
		}
		catch (Exception ex)
		{
			LogFailure("Reports", "Could not read the script extender log", ex);
			return (null, false);
		}
	}

	/// <summary>Reads the script extender's log for the plugins it refused, or an empty list if there is no log
	/// yet (the game has never been run through it) or it cannot be read.</summary>
	private List<PluginLoadFailure> ReadLastPluginFailures()
	{
		try
		{
			string path = Path.Combine(GameLogFolder(), PrimaryGameLogName());
			if (!File.Exists(path)) return new List<PluginLoadFailure>();
			// Shared read: the game may be running and holding the log open.
			return ScriptExtenderPlugins.ParseLoadFailures(ReadAllLinesShared(path));
		}
		catch (Exception ex)
		{
			LogFailure("Reports", "Could not read the script extender log", ex);
			return new List<PluginLoadFailure>();
		}
	}
	}
