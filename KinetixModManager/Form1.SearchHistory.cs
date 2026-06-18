using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>The accessible "Search History" dialog for the active game's mod searches.</summary>
public partial class Form1
{
	/// <summary>A scope shown in the dialog's "Show" combo: either every search, or one specific day.</summary>
	private class HistoryScope
	{
		public string Label { get; }
		public DateTime? Day { get; }   // null = all searches
		public HistoryScope(string label, DateTime? day) { Label = label; Day = day; }
		public override string ToString() => Label;
	}

	/// <summary>
	/// Opens the Search History window for the active game: a "Show" combo (All searches, or a specific date,
	/// newest first), a list of the terms in that scope (newest first, de-duplicated), and a Clear button.
	/// Choosing a term (Enter) closes the window and re-runs that search on the Search for Mods tab. The history
	/// is per game, so a Skyrim session only ever shows Skyrim searches (and likewise for Fallout 4 / Stardew).
	/// </summary>
	private void ShowSearchHistoryDialog()
	{
		string game = _settings.ActiveGame;
		if (game == "None") return;

		var entries = SearchHistoryStore.Load(game);   // newest first
		if (entries.Count == 0)
		{
			Speak(_settings.SaveSearchHistory ? Loc.T("history.empty") : Loc.T("history.emptyDisabled"));
			return;
		}

		string? chosenTerm = null;

		Form dialog = new Form
		{
			Text = Loc.T("history.windowTitle"),
			Size = new Size(700, 560),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true,
			MinimizeBox = false,
			MaximizeBox = false
		};

		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(12),
			ColumnCount = 1,
			RowCount = 3
		};
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));

		ComboBox cmbScope = new ComboBox
		{
			Dock = DockStyle.Top,
			Font = new Font("Segoe UI", 12f),
			DropDownStyle = ComboBoxStyle.DropDownList,
			AccessibleName = Loc.T("history.scopeName")
		};
		ListBox lstTerms = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = Loc.T("history.listName")
		};
		Button btnClear = new Button
		{
			Text = Loc.T("history.clearBtn"),
			Dock = DockStyle.Right,
			Width = 200,
			Height = 45,
			Font = new Font("Segoe UI", 11f),
			AccessibleName = Loc.T("history.clearName")
		};

		layout.Controls.Add(cmbScope, 0, 0);
		layout.Controls.Add(lstTerms, 0, 1);
		layout.Controls.Add(btnClear, 0, 2);
		dialog.Controls.Add(layout);

		// Build the "Show" combo: All searches first, then each distinct day, newest first.
		void BuildScopes()
		{
			cmbScope.Items.Clear();
			cmbScope.Items.Add(new HistoryScope(Loc.T("history.allSearches"), null));
			foreach (DateTime day in entries.Select(e => e.Date.Date).Distinct().OrderByDescending(d => d))
				cmbScope.Items.Add(new HistoryScope(FriendlyDay(day), day));
			if (cmbScope.Items.Count > 0) cmbScope.SelectedIndex = 0;
		}

		// Fill the term list for the selected scope, newest first and de-duplicated (case-insensitive).
		void FillTerms()
		{
			lstTerms.Items.Clear();
			if (cmbScope.SelectedItem is not HistoryScope scope) return;
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var e in entries)
			{
				if (scope.Day != null && e.Date.Date != scope.Day.Value) continue;
				if (seen.Add(e.Term)) lstTerms.Items.Add(e.Term);
			}
			if (lstTerms.Items.Count > 0) lstTerms.SelectedIndex = 0;
		}

		bool building = true;
		cmbScope.SelectedIndexChanged += delegate
		{
			FillTerms();
			// Stay quiet during the initial build; the "opened" announcement already states the count.
			if (!building) Speak(Loc.T("history.countSpoken", lstTerms.Items.Count));
		};

		// Enter on a term re-runs that search; Enter handled here so it doesn't ding or pick a default button.
		lstTerms.KeyDown += delegate (object? s, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Enter && lstTerms.SelectedItem is string term)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				chosenTerm = term;
				dialog.Close();
			}
		};

		btnClear.Click += delegate
		{
			var confirm = MessageBox.Show(Loc.T("history.clearConfirm"), Loc.T("history.windowTitle"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
			if (confirm != DialogResult.Yes) return;
			SearchHistoryStore.Clear(game);
			entries = new List<SearchHistoryEntry>();
			BuildScopes();
			FillTerms();
			Speak(Loc.T("history.cleared"));
		};

		dialog.KeyDown += delegate (object? s, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape) dialog.Close();
		};

		BuildScopes();
		FillTerms();
		building = false;
		dialog.Shown += delegate
		{
			cmbScope.Focus();
			Speak(Loc.T("history.opened", lstTerms.Items.Count));
		};
		dialog.ShowDialog();

		// Re-run the chosen search after the dialog closes.
		if (!string.IsNullOrEmpty(chosenTerm))
		{
			mainTabs.SelectedTab = tabDiscovery;
			cmbDiscoveryType.SelectedItem = "Search";
			txtSearch.Text = chosenTerm;
			txtSearch.Focus();
			_ = RunDiscovery();
		}
	}

	/// <summary>A speech-friendly label for a search day: Today, Yesterday, or the long date.</summary>
	private static string FriendlyDay(DateTime day)
	{
		if (day == DateTime.Today) return Loc.T("history.today");
		if (day == DateTime.Today.AddDays(-1)) return Loc.T("history.yesterday");
		return day.ToString("D");
	}
}
