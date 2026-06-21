using System;
using System.Collections.Generic;
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
		var rows = new List<ReportRow>();

		if (_settings.ActiveGame == "StardewValley")
		{
			foreach (var (uid, names) in ModHealth.FindDuplicateUniqueIds(_allInstalledMods))
				rows.Add(new ReportRow { Text = Loc.T("reports.dupId", uid, string.Join(", ", names)) });

			ShowReportDialog(Loc.T("reports.conflictTitle"), Loc.T("reports.dupHeader"),
				Loc.T("reports.dupNone"), rows, null);
			return;
		}

		if (!IsBethesdaGame)
		{
			ShowReportDialog(Loc.T("reports.conflictTitle"), Loc.T("reports.conflictHeader"),
				Loc.T("reports.conflictNoneGame"), rows, null);
			return;
		}

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

		ShowReportDialog(Loc.T("reports.conflictTitle"), Loc.T("reports.conflictHeader"),
			Loc.T("reports.conflictNone"), rows, null);
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
		var enabled = _allInstalledMods.Where(m => !m.IsGroup && m.IsEnabled).ToList();
		var rows = new List<ReportRow>();

		if (_settings.ActiveGame == "StardewValley")
		{
			foreach (GameMod mod in enabled)
				foreach (ModDependency dep in mod.Dependencies.Where(d => d.IsRequired))
				{
					string depKey = $"dep|{mod.Name}|{dep.UniqueId}";
					if (!dep.IsPresent)
						rows.Add(new ReportRow { Text = Loc.T("reports.depMissing", mod.Name, dep.UniqueId), SearchTerm = dep.UniqueId, IgnoreKey = depKey });
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
				ModFileSystem.ReadActivePlugins(_settings.ActiveGame), StringComparer.OrdinalIgnoreCase);
			foreach (ModFileSystem.MasterIssue issue in
				ModFileSystem.FindMissingMasters(_settings.ActiveGame, enabled, activePlugins, _settings.CurrentGamePath))
				rows.Add(new ReportRow
				{
					Text = Loc.T(issue.PresentButInactive ? "reports.masterInactive" : "reports.missingMaster",
						issue.Plugin, issue.OwnerMod, issue.Master),
					IgnoreKey = $"master|{issue.Plugin}|{issue.Master}"
				});

			// 2. Script extender (SKSE/F4SE) not installed at all.
			string seId = _settings.ActiveGame == "SkyrimSE" ? "30379" : "42147"; // SKSE64 / F4SE Nexus ids
			bool seInstalled = ModFileSystem.IsScriptExtenderInstalled(_settings.ActiveGame, _settings.CurrentGamePath);
			if (!seInstalled)
				rows.Add(new ReportRow { Text = Loc.T("reports.seMissing", _settings.ActiveGame == "SkyrimSE" ? "SKSE" : "F4SE"), IgnoreKey = "se" });

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

		string hint = rows.Count > 0 ? Loc.T("reports.reqActionHint") : null!;
		if (hiddenCount > 0)
			hint = (hint ?? "") + Loc.T("reports.reqHiddenSuffix", hiddenCount);

		ShowReportDialog(Loc.T("reports.reqTitle"), Loc.T("reports.reqHeader", GameDisplayName()),
			Loc.T("reports.reqNone"), rows, hint, IgnoreRequirementRow);
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
		Action<ReportRow>? onIgnore = null)
	{
		var f = new Form
		{
			Text = title,
			Size = new Size(760, 520),
			StartPosition = FormStartPosition.CenterParent,
			KeyPreview = true,
			MinimizeBox = false,
			MaximizeBox = false
		};
		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

		layout.Controls.Add(new Label { Text = header, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8) }, 0, 0);

		var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = header, IntegralHeight = false, HorizontalScrollbar = true };
		bool hasRows = rows.Count > 0;
		if (!hasRows)
			list.Items.Add(new ReportRow { Text = emptyMessage });
		else
			foreach (ReportRow r in rows) list.Items.Add(r);
		layout.Controls.Add(list, 0, 1);
		f.Controls.Add(layout);

		list.GotFocus += List_Enter;
		f.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
		list.KeyDown += (_, e) =>
		{
			if (list.SelectedItem is not ReportRow row) return;

			// Delete hides a requirement warning the manager can't auto-resolve (e.g. an optional/alternative-
			// satisfied Nexus requirement). Only rows that opted in (IgnoreKey set) and reports that supplied an
			// onIgnore handler are ignorable.
			if (e.KeyCode == Keys.Delete && onIgnore != null && !string.IsNullOrEmpty(row.IgnoreKey))
			{
				e.Handled = e.SuppressKeyPress = true;
				onIgnore(row);
				int idx = list.SelectedIndex;
				list.Items.Remove(row);
				if (list.Items.Count == 0) { Speak(Loc.T("reports.ignoredLast")); f.Close(); return; }
				list.SelectedIndex = Math.Min(idx, list.Items.Count - 1);
				Speak(Loc.T("reports.ignored", list.Items.Count));
				return;
			}

			if (e.KeyCode != Keys.Enter) return;
			if (!string.IsNullOrEmpty(row.SearchTerm))
			{
				if (MessageBox.Show(f, Loc.T("reports.searchConfirm", row.SearchTerm), Loc.T("reports.searchTitle"),
						MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					f.Close();
					SelectTab(AppTab.Discovery);
					txtSearch.Text = row.SearchTerm;
					_ = RunDiscovery();
				}
			}
			else if (!string.IsNullOrEmpty(row.OpenUrl))
			{
				try { Process.Start(new ProcessStartInfo(row.OpenUrl) { UseShellExecute = true }); } catch { }
			}
		};

		f.Shown += (_, _) =>
		{
			string opening = header + ". " + (hasRows
				? Loc.T("reports.itemCount", rows.Count) + (actionHint != null ? ". " + actionHint : "")
				: emptyMessage);
			Speak(opening);
			list.Focus();
		};
		f.ShowDialog(this);
	}
}
