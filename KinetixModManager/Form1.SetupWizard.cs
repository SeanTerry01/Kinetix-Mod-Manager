using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// First-run setup wizard (auto-shown on a fresh install, re-openable from Help). Rather than dropping a brand-new
/// user into the Settings dialog, this presents the three things needed to get going — choose a game, set the Nexus
/// API key, and confirm the game folder — as a spoken checklist that shows each step's live done / not-done status.
/// Enter on a step launches its action (the game chooser or Settings); the checklist refreshes as steps complete,
/// so a screen-reader user always knows what is left.
/// </summary>
public partial class Form1
{
	private bool HasGameChosen => _settings.ActiveGame != "None";
	private bool HasApiKey => !string.IsNullOrEmpty(_settings.ApiKey);
	private bool HasGameFolder => HasGameChosen && !string.IsNullOrEmpty(_settings.CurrentGamePath) && Directory.Exists(_settings.CurrentGamePath);

	private void ShowSetupWizard()
	{
		var f = new Form
		{
			Text = Loc.T("wizard.title"),
			Size = new Size(720, 460),
			StartPosition = FormStartPosition.CenterParent,
			KeyPreview = true,
			MinimizeBox = false,
			MaximizeBox = false
		};
		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		layout.Controls.Add(new Label { Text = Loc.T("wizard.header"), AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8) }, 0, 0);

		var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = Loc.T("wizard.listName"), IntegralHeight = false, HorizontalScrollbar = true };
		layout.Controls.Add(list, 0, 1);
		f.Controls.Add(layout);

		string Row(bool done, string labelKey) =>
			Loc.T("wizard.row", Loc.T(labelKey), Loc.T(done ? "wizard.done" : "wizard.todo"));

		void Rebuild(int keepSelection)
		{
			list.BeginUpdate();
			list.Items.Clear();
			list.Items.Add(Row(HasGameChosen, "wizard.stepGame"));
			list.Items.Add(Row(HasApiKey, "wizard.stepKey"));
			list.Items.Add(Row(HasGameFolder, "wizard.stepFolder"));
			list.SelectedIndex = Math.Min(Math.Max(keepSelection, 0), list.Items.Count - 1);
			list.EndUpdate();
		}
		Rebuild(0);

		list.GotFocus += List_Enter;
		f.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
		list.KeyDown += (_, e) =>
		{
			if (e.KeyCode != Keys.Enter) return;
			e.Handled = e.SuppressKeyPress = true;
			int step = list.SelectedIndex;

			if (step == 0)
			{
				// Choosing a game is a main-window action (the game list sits behind the wizard); drop the user there.
				f.Close();
				if (!HasGameChosen && _lstGames != null) { _lstGames.Focus(); Speak(Loc.T("wizard.goPickGame")); }
				return;
			}
			if (step == 2 && !HasGameChosen)
			{
				Speak(Loc.T("wizard.needGameFirst"));
				return;
			}
			// Steps 1 (API key) and 2 (game folder) are both configured in Settings.
			ShowSettings();
			Rebuild(step);
			Speak(Loc.T("wizard.row", Loc.T(step == 1 ? "wizard.stepKey" : "wizard.stepFolder"),
				Loc.T((step == 1 ? HasApiKey : HasGameFolder) ? "wizard.done" : "wizard.todo")));
		};

		f.Shown += (_, _) =>
		{
			bool allDone = HasGameChosen && HasApiKey && HasGameFolder;
			string opening = Loc.T("wizard.header") + " "
				+ (allDone ? Loc.T("wizard.allDone") : Loc.T("wizard.openingHint"));
			Speak(opening);
			list.Focus();
		};
		StyleDialog(f);
		f.ShowDialog(this);
	}
}
