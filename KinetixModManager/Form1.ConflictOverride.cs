using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// Per-file conflict winner override (Mods menu). When two enabled Skyrim SE / Fallout 4 mods provide the same
/// loose file, the higher-priority mod wins by default. This lets the user force a specific mod to win a specific
/// file regardless of mod priority — the one thing numeric priority can't express (you may want mod A's textures
/// but mod B's meshes even though A outranks B overall). Enter cycles a file's winner through the mods that provide
/// it; Delete reverts it to automatic priority order. Overrides persist per game and re-apply on every deployment.
/// </summary>
public partial class Form1
{
	/// <summary>One row in the conflict resolver: a contested file path (its winner/summary is recomputed live).</summary>
	private sealed class ConflictRow
	{
		public required string Path;
		public string Summary = "";
		public override string ToString() => Summary;
	}

	private void ShowConflictOverride()
	{
		if (!IsBethesdaGame)
		{
			Speak(Loc.T("conflictfix.notApplicable"));
			return;
		}

		// Reconcile deployment first so the conflict list reflects the current mods and any existing overrides.
		SyncBethesdaDeployment();
		if (_lastConflicts.Count == 0)
		{
			Speak(Loc.T("conflictfix.none"));
			return;
		}

		ShowConflictOverrideDialog();
	}

	private void ShowConflictOverrideDialog()
	{
		string game = _settings.ActiveGame;

		// Map a mod's priority key (folder name) to its friendly display name, and to its priority rank for cycling.
		var nameByKey = _allInstalledMods.Where(m => !m.IsGroup)
			.GroupBy(PriorityKey, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
		string Disp(string key) => nameByKey.TryGetValue(key, out string? n) ? n : key;

		var f = new Form
		{
			Text = Loc.T("conflictfix.title"),
			Size = new Size(780, 520),
			StartPosition = FormStartPosition.CenterParent,
			KeyPreview = true,
			MinimizeBox = false,
			MaximizeBox = false
		};
		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		layout.Controls.Add(new Label { Text = Loc.T("conflictfix.header", GameDisplayName()), AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8) }, 0, 0);

		var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = Loc.T("conflictfix.listName"), IntegralHeight = false, HorizontalScrollbar = true };
		foreach (FileConflict c in _lastConflicts.OrderBy(c => c.RelativePath, StringComparer.OrdinalIgnoreCase))
			list.Items.Add(new ConflictRow { Path = c.RelativePath, Summary = BuildConflictSummary(c.RelativePath, Disp) });
		if (list.Items.Count > 0) list.SelectedIndex = 0;
		layout.Controls.Add(list, 0, 1);
		f.Controls.Add(layout);

		WireAccessibleDialogList(list);
		f.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
		list.KeyDown += (_, e) =>
		{
			if (list.SelectedItem is not ConflictRow row) return;

			if (e.KeyCode == Keys.Enter)
			{
				e.Handled = e.SuppressKeyPress = true;
				CycleConflictWinner(row, Disp);
				RefreshConflictRow(list, row, Disp);
			}
			else if (e.KeyCode == Keys.Delete)
			{
				e.Handled = e.SuppressKeyPress = true;
				RevertConflictWinner(row, Disp);
				RefreshConflictRow(list, row, Disp);
			}
		};

		f.Shown += (_, _) =>
		{
			string opening = Loc.T("conflictfix.header", GameDisplayName()) + " "
				+ Loc.T(_lastConflicts.Count == 1 ? "conflictfix.countOne" : "conflictfix.count", _lastConflicts.Count)
				+ " " + Loc.T("conflictfix.actionHint");
			Speak(opening);
			list.Focus();
		};
		StyleDialog(f);
		f.ShowDialog(this);
	}

	/// <summary>The providers of a contested path in priority order (highest first); index 0 is the natural winner.</summary>
	private List<string> ConflictProvidersInPriority(FileConflict c)
	{
		var set = new HashSet<string>(c.Losers, StringComparer.OrdinalIgnoreCase) { c.Winner };
		var order = _settings.ModPriority.TryGetValue(_settings.ActiveGame, out var o) ? o : new List<string>();
		var ranked = order.Where(k => set.Contains(k)).ToList();
		// Any provider missing from the priority list (shouldn't normally happen) is appended so it's still cyclable.
		foreach (string k in set) if (!ranked.Contains(k, StringComparer.OrdinalIgnoreCase)) ranked.Add(k);
		return ranked;
	}

	/// <summary>Builds a row's spoken/displayed text from the live conflict + override state for a path.</summary>
	private string BuildConflictSummary(string path, Func<string, string> disp)
	{
		FileConflict? c = _lastConflicts.FirstOrDefault(x => string.Equals(x.RelativePath, path, StringComparison.OrdinalIgnoreCase));
		if (c == null) return path;
		int providerCount = 1 + c.Losers.Count;
		bool forced = _settings.FileWinnerOverrides.TryGetValue(_settings.ActiveGame, out var ov) &&
					  ov.ContainsKey(path);
		string forcedTag = forced ? Loc.T("conflictfix.forcedTag") : "";
		return Loc.T("conflictfix.row", path, disp(c.Winner), providerCount, forcedTag);
	}

	private void CycleConflictWinner(ConflictRow row, Func<string, string> disp)
	{
		FileConflict? c = _lastConflicts.FirstOrDefault(x => string.Equals(x.RelativePath, row.Path, StringComparison.OrdinalIgnoreCase));
		if (c == null) return;
		List<string> providers = ConflictProvidersInPriority(c);
		if (providers.Count < 2) return;

		int idx = providers.FindIndex(k => string.Equals(k, c.Winner, StringComparison.OrdinalIgnoreCase));
		string next = providers[(Math.Max(idx, 0) + 1) % providers.Count];

		// Index 0 is the natural priority winner, so choosing it means "no override": clear any stored one instead of
		// persisting a redundant entry. Any other choice is stored as an explicit override.
		if (string.Equals(next, providers[0], StringComparison.OrdinalIgnoreCase))
			ClearOverride(row.Path);
		else
			SetOverride(row.Path, next);

		SyncBethesdaDeployment();
		Speak(Loc.T("conflictfix.changed", row.Path, disp(next)));
	}

	private void RevertConflictWinner(ConflictRow row, Func<string, string> disp)
	{
		bool had = _settings.FileWinnerOverrides.TryGetValue(_settings.ActiveGame, out var ov) && ov.ContainsKey(row.Path);
		if (!had)
		{
			Speak(Loc.T("conflictfix.noOverride", row.Path));
			return;
		}
		ClearOverride(row.Path);
		SyncBethesdaDeployment();
		FileConflict? c = _lastConflicts.FirstOrDefault(x => string.Equals(x.RelativePath, row.Path, StringComparison.OrdinalIgnoreCase));
		Speak(Loc.T("conflictfix.reverted", row.Path, c != null ? disp(c.Winner) : ""));
	}

	private void RefreshConflictRow(ListBox list, ConflictRow row, Func<string, string> disp)
	{
		row.Summary = BuildConflictSummary(row.Path, disp);
		int i = list.Items.IndexOf(row);
		if (i >= 0)
		{
			// Rewrite the item in place so the list reflects the new winner without losing the selection.
			list.Items[i] = row;
			list.SelectedIndex = i;
		}
	}

	private void SetOverride(string path, string winnerKey)
	{
		if (!_settings.FileWinnerOverrides.TryGetValue(_settings.ActiveGame, out var ov))
			_settings.FileWinnerOverrides[_settings.ActiveGame] = ov = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		ov[path] = winnerKey;
		_settings.Save();
	}

	private void ClearOverride(string path)
	{
		if (_settings.FileWinnerOverrides.TryGetValue(_settings.ActiveGame, out var ov) && ov.Remove(path))
		{
			if (ov.Count == 0) _settings.FileWinnerOverrides.Remove(_settings.ActiveGame);
			_settings.Save();
		}
	}
}
