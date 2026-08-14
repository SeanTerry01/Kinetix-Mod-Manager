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
	/// Shows the Search History for the active game — a "Show" combo (All searches, or a specific date, newest
	/// first), a list of the terms in that scope (newest first, de-duplicated), and a Clear button. Choosing a
	/// term (Enter) closes it and re-runs that search on the Search for Mods tab; Delete removes one. The history
	/// is per game, so a Skyrim session only ever shows Skyrim searches (and likewise for Fallout 4 / Stardew).
	///
	/// Shown <em>inside</em> the main window rather than as a window of its own — see <see cref="ShowInlineView"/>
	/// for why that matters to how it reads aloud.
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

		// Shown inside the main window rather than as a window of its own — see Form1.InlineView. Everything
		// below is the same layout it always had; only where it lives has changed.
		ShowInlineView(Loc.T("history.viewTitle"), (container, closeView) =>
		{
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
			container.Controls.Add(layout);

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

			// Position on the way in and on every arrow move. Without this the term list read its rows and never
			// said where in the history they sat, so there was no way to tell a long list from a short one.
			WireAccessibleDialogList(lstTerms);

			bool building = true;
			cmbScope.SelectedIndexChanged += delegate
			{
				FillTerms();
				// Stay quiet during the initial build; the "opened" announcement already states the count.
				if (!building) Speak(Loc.T("history.countSpoken", lstTerms.Items.Count));
			};

			// Enter on a term re-runs that search; Enter handled here so it doesn't ding or pick a default button.
			// Delete removes just that term — a search typed wrong once shouldn't sit in the list forever, and the
			// only alternative was clearing the whole history to be rid of it.
			lstTerms.KeyDown += delegate (object? s, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Enter && lstTerms.SelectedItem is string term)
				{
					e.Handled = true;
					e.SuppressKeyPress = true;
					chosenTerm = term;
					closeView();
				}
				else if (e.KeyCode == Keys.Delete && lstTerms.SelectedItem is string doomed)
				{
					e.Handled = true;
					e.SuppressKeyPress = true;

					// A short caption naming the action — a prompt's caption is read out before the question, so it
					// has to be the shortest thing that says what is being asked.
					// Answering No says nothing: the term is still there and about to be read out as focus
					// returns to it, which says "not deleted" better than a message that talks over it.
					if (SpeakBox(Loc.T("history.deleteConfirm", doomed), Loc.T("history.deleteTitle"),
							MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
						return;

					int index = lstTerms.SelectedIndex;
					DateTime? day = (cmbScope.SelectedItem as HistoryScope)?.Day;

					SearchHistoryStore.RemoveTerm(game, doomed);
					entries = SearchHistoryStore.Load(game);

					// Rebuilding re-selects the first scope, which would otherwise announce a count over the top of
					// the deletion message; the flag keeps that quiet exactly as it does during the initial build.
					building = true;
					BuildScopes();
					// Stay on the same day when that day still has searches left in it.
					if (day != null)
					{
						for (int i = 0; i < cmbScope.Items.Count; i++)
						{
							if (cmbScope.Items[i] is HistoryScope candidate && candidate.Day == day)
							{
								cmbScope.SelectedIndex = i;
								break;
							}
						}
					}
					FillTerms();
					building = false;

					if (lstTerms.Items.Count == 0)
					{
						// Nothing left to put focus on in the list, so move it somewhere usable rather than
						// leaving it on an empty list.
						cmbScope.Focus();
						SpeakAfterPrompt(Loc.T("history.deletedNowEmpty", doomed));
						return;
					}

					// Land on whatever took the deleted row's place, so you can keep deleting without re-navigating.
					lstTerms.SelectedIndex = Math.Min(index, lstTerms.Items.Count - 1);
					SpeakAfterPrompt(Loc.T("history.deleted", doomed, lstTerms.Items.Count));
				}
			};

			btnClear.Click += delegate
			{
				var confirm = SpeakBox(Loc.T("history.clearConfirm"), Loc.T("history.clearTitle"),
					MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
				if (confirm != DialogResult.Yes) return;
				SearchHistoryStore.Clear(game);
				entries = new List<SearchHistoryEntry>();
				building = true;
				BuildScopes();
				FillTerms();
				building = false;
				SpeakAfterPrompt(Loc.T("history.cleared"));
			};

			// Escape is handled by the view itself (see Form1.InlineView), so there is no window-level handler here.

			BuildScopes();
			FillTerms();
			building = false;
			ApplyScreenReaderPauses(container);
			Speak(Loc.T("history.opened", lstTerms.Items.Count));
			return cmbScope;
		});

		// Re-run the chosen search after the view closes.
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
