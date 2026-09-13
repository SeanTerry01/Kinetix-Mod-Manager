using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// Persistent load-order rules (Mods menu, Skyrim SE / Fallout 4). Numeric plugin order is a single list, but
/// sometimes you want a standing constraint — "always load this patch after that mod" — that survives an auto-sort.
/// These MO2/LOOT-style rules ("load X after Y") are stored per game and folded into the plugin auto-sort (F8)
/// alongside master dependencies and LOOT's own rules. Add a rule from the Plugin Order tab (subject = selected
/// plugin), and manage/remove rules from the rules list.
/// </summary>
public partial class Form1
{
	/// <summary>Creates a rule for the plugin currently selected on the Plugin Order tab, via a target picker.</summary>
	private void AddLoadOrderRule()
	{
		if (!IsBethesdaGame)
		{
			Speak(Loc.T("rules.notApplicable"));
			return;
		}
		if (listPluginOrder?.SelectedItem is not PluginEntry subject)
		{
			Speak(Loc.T("rules.selectFirst"));
			return;
		}

		string game = _settings.ActiveGame;
		var others = (_settings.PluginOrder.TryGetValue(game, out var order) ? order : new List<string>())
			.Where(n => !string.Equals(n, subject.Name, StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (others.Count == 0)
		{
			Speak(Loc.T("rules.noOthers"));
			return;
		}

		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("rules.addTitle", subject.Name), (container, closeView) =>
		{
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			layout.Controls.Add(new Label { Text = Loc.T("rules.addHeader", subject.Name), AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8) }, 0, 0);

			var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = Loc.T("rules.pickListName"), IntegralHeight = false, HorizontalScrollbar = true };
			foreach (string n in others) list.Items.Add(n);
			if (list.Items.Count > 0) list.SelectedIndex = 0;
			layout.Controls.Add(list, 0, 1);
			container.Controls.Add(layout);

			WireAccessibleDialogList(list);
			// Escape is handled by the view itself (see Form1.InlineView).
			list.KeyDown += (_, e) =>
			{
				if (list.SelectedItem is not string target) return;

				// Enter: subject loads AFTER target. B: subject loads BEFORE target (stored as target-after-subject).
				if (e.KeyCode == Keys.Enter)
				{
					e.Handled = e.SuppressKeyPress = true;
					if (AddRule(subject.Name, target)) Speak(Loc.T("rules.addedAfter", subject.Name, target));
					else Speak(Loc.T("rules.duplicate"));
					closeView();
				}
				else if (e.KeyCode == Keys.B)
				{
					e.Handled = e.SuppressKeyPress = true;
					if (AddRule(target, subject.Name)) Speak(Loc.T("rules.addedBefore", subject.Name, target));
					else Speak(Loc.T("rules.duplicate"));
					closeView();
				}
			};

			return list;
		},
		// The heading here instructs rather than repeats the title, so it is kept whole — only its order changes.
		hint: Loc.T("rules.addHeader", subject.Name) + " " + Loc.T("rules.addHint", subject.Name) + " "
			+ Loc.T(others.Count == 1 ? "rules.pickCountOne" : "rules.pickCount", others.Count));
	}

	/// <summary>Adds a "plugin loads after 'after'" rule for the active game; returns false if it already exists.</summary>
	private bool AddRule(string plugin, string after)
	{
		if (!_settings.LoadOrderRules.TryGetValue(_settings.ActiveGame, out var rules))
			_settings.LoadOrderRules[_settings.ActiveGame] = rules = new List<LoadOrderRule>();
		if (rules.Any(r => string.Equals(r.Plugin, plugin, StringComparison.OrdinalIgnoreCase) &&
						   string.Equals(r.After, after, StringComparison.OrdinalIgnoreCase)))
			return false;
		rules.Add(new LoadOrderRule { Plugin = plugin, After = after });
		_settings.Save();
		return true;
	}

	/// <summary>Lists the active game's load-order rules; Delete removes one. Rules apply on the next auto-sort (F8).</summary>
	private void ShowLoadOrderRules()
	{
		if (!IsBethesdaGame)
		{
			Speak(Loc.T("rules.notApplicable"));
			return;
		}
		if (!_settings.LoadOrderRules.TryGetValue(_settings.ActiveGame, out var rules) || rules.Count == 0)
		{
			Speak(Loc.T("rules.none"));
			return;
		}

		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("rules.manageTitle"), (container, closeView) =>
		{
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			layout.Controls.Add(new Label { Text = Loc.T("rules.manageHeader", GameDisplayName()), AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8) }, 0, 0);

			var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = Loc.T("rules.listName"), IntegralHeight = false, HorizontalScrollbar = true };
			foreach (LoadOrderRule r in rules) list.Items.Add(new RuleItem { Rule = r, Summary = Loc.T("rules.row", r.Plugin, r.After) });
			if (list.Items.Count > 0) list.SelectedIndex = 0;
			layout.Controls.Add(list, 0, 1);
			container.Controls.Add(layout);

			WireAccessibleDialogList(list);
			// Escape is handled by the view itself (see Form1.InlineView).
			list.KeyDown += (_, e) =>
			{
				if (e.KeyCode != Keys.Delete || list.SelectedItem is not RuleItem item) return;
				e.Handled = e.SuppressKeyPress = true;
				rules.Remove(item.Rule);
				if (rules.Count == 0) _settings.LoadOrderRules.Remove(_settings.ActiveGame);
				_settings.Save();

				int idx = list.SelectedIndex;
				list.Items.Remove(item);
				_soundEngine.Play("disable");
				if (list.Items.Count == 0) { Speak(Loc.T("rules.removedLast")); closeView(); return; }
				list.SelectedIndex = Math.Min(idx, list.Items.Count - 1);
				Speak(Loc.T("rules.removed", list.Items.Count));
			};

			return list;
		},
		// Through the hint, and without the old heading clause ("Load order rules for <game>."), which was the
		// title's own words again. See Form1.InlineView: anything spoken during build lands ahead of the title.
		hint: Loc.T(rules.Count == 1 ? "rules.countOne" : "rules.count", rules.Count) + " " + Loc.T("rules.manageHint"));
	}

}
