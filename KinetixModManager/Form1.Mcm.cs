using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// The accessible settings editor for a Skyrim / Fallout 4 mod's Mod Configuration Menu.
///
/// These settings normally live behind an in-game menu, which means starting the game, loading a save and
/// working through a menu system that was never designed to be listened to. The settings themselves are only
/// INI entries, and the menu that describes them is a JSON file the mod ships — so the same thing the manager
/// does for Content Patcher packs works here: read the author's own labels, explanations and control types, and
/// present them as a list where each setting is chosen rather than typed.
///
/// Changes are written to the player's own settings file under <c>Data\MCM\Settings\</c>, never to the mod's
/// shipped defaults, so nothing is lost by reinstalling the mod and nothing of the mod's own is modified.
/// </summary>
public partial class Form1
{
	/// <summary>One row: an MCM control and its current value, or a heading with no value of its own.</summary>
	private sealed class McmRow : IListHeadingRow
	{
		public required McmControl Control { get; init; }
		public required string Value { get; init; }

		/// <summary>A heading names the group of settings under it; the rows are numbered within that group.</summary>
		public bool IsHeading => Control.Kind == McmControlKind.NotASetting;

		public override string ToString() => Control.Kind switch
		{
			McmControlKind.NotASetting => Loc.T("mcm.rowHeading", Control.Label),
			McmControlKind.Key => Loc.T("mcm.rowKey", Control.Label, Display),
			_ => Loc.T("mcm.row", Control.Label, Display)
		};

		/// <summary>The value as it should be read out: on/off for a toggle, a key name for a binding.</summary>
		private string Display
		{
			get
			{
				if (Value.Length == 0) return Loc.T("mcm.valueUnset");

				return Control.Kind switch
				{
					McmControlKind.Toggle => McmToggleIsOn(Value) ? Loc.T("mcm.on") : Loc.T("mcm.off"),
					McmControlKind.Key => McmKeyText(Value),
					_ => Value
				};
			}
		}
	}

	/// <summary>
	/// An MCM key binding as a person would say it: "Page Up" rather than "33,0".
	///
	/// MCM stores a binding as a Windows virtual-key code and a modifier bitfield, which is exactly right for
	/// the game and useless to read aloud. The controls list already decodes these; this is the same decoding,
	/// so a key reads identically wherever it appears.
	/// </summary>
	private static string McmKeyText(string raw)
	{
		string[] parts = raw.Split(',');
		if (!int.TryParse(parts[0].Trim(), out int virtualKey) || virtualKey <= 0)
			return Loc.T("mcm.keyUnassigned");

		int modifiers = parts.Length > 1 && int.TryParse(parts[1].Trim(), out int m) ? m : 0;
		return DecodeVirtualKey(virtualKey, modifiers);
	}

	/// <summary>
	/// MCM writes a toggle as 0 or 1, but a hand-edited file may hold true or false. Both are understood so a
	/// setting someone edited by hand still reads correctly.
	/// </summary>
	private static bool McmToggleIsOn(string value) =>
		value.Trim() is "1" || value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Shows a mod's Mod Configuration Menu settings and lets them be changed. Each change is written as it is
	/// made, matching the INI editor, so there is no save step to remember.
	/// </summary>
	private void ShowMcmSettings(string modName, McmMenu menu)
	{
		McmSettings values = McmSettings.Load(menu);

		// The same snapshot the INI editor takes: these are game-folder settings files, and a bad change should
		// be one restore away.
		CreateSafetyBackup(Loc.T("safety.reasonIni", modName));

		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("mcm.title", modName), (container, closeView) =>
		{
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			var list = new ListBox
			{
				Dock = DockStyle.Fill,
				Font = new Font("Segoe UI", 12f),
				// Silent for the same reason as the Stardew settings list: the view's title and hint have already
				// named it twice on the way in. See SilentAccessibleName and Form1.StardewConfig.
				AccessibleName = SilentAccessibleName,
				IntegralHeight = false,
				HorizontalScrollbar = true
			};

			void Reload(int selectIndex)
			{
				list.BeginUpdate();
				list.Items.Clear();
				foreach (McmControl control in menu.Controls)
				{
					// Spacers and paragraphs of menu prose carry nothing to read or change; headings stay,
					// because they are how the author grouped what follows.
					if (control.Kind == McmControlKind.NotASetting && control.Label.Length == 0) continue;
					list.Items.Add(new McmRow { Control = control, Value = values.Get(control) });
				}
				list.EndUpdate();
				if (list.Items.Count > 0)
					list.SelectedIndex = Math.Clamp(selectIndex, 0, list.Items.Count - 1);
			}
			Reload(0);

			var btnClose = new Button { Text = Loc.T("mcm.closeBtn"), AutoSize = true, AccessibleName = Loc.T("mcm.closeName") };
			var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
			buttons.Controls.Add(btnClose);

			layout.Controls.Add(list, 0, 0);
			layout.Controls.Add(buttons, 0, 1);
			container.Controls.Add(layout);

			WireAccessibleDialogList(list);

			void EditSelected()
			{
				if (list.SelectedItem is not McmRow row) return;
				McmControl control = row.Control;

				if (control.Kind == McmControlKind.NotASetting)
				{
					Speak(Loc.T("mcm.headingNotEditable"));
					return;
				}

				// A key binding is shown but not rebound here: MCM stores it as a virtual-key and modifier pair
				// that the game's own capture screen writes, and inventing those numbers from outside is how a
				// mod ends up with a binding it cannot use.
				if (control.Kind == McmControlKind.Key)
				{
					Speak(Loc.T("mcm.keyNotEditable"));
					return;
				}

				string? chosen = EditMcmControl(control, row.Value);
				if (chosen == null || chosen == row.Value) return;

				if (!McmSettings.Save(menu, control, chosen))
				{
					Speak(Loc.T("mcm.saveFailed", control.Label));
					return;
				}

				values.Set(control, chosen);
				int index = list.SelectedIndex;
				Reload(index);
				Speak(Loc.T("mcm.setTo", control.Label, (list.SelectedItem as McmRow)?.ToString() ?? chosen));
			}

			btnClose.Click += delegate { closeView(); };
			list.KeyDown += delegate (object? s, KeyEventArgs e)
			{
				if (e.KeyCode != Keys.Enter) return;
				e.Handled = e.SuppressKeyPress = true;
				EditSelected();
			};
			// Escape is handled by the view itself (see Form1.InlineView).

			return list;
		},
		hint: Loc.T("mcm.hint", menu.Settings.Count()));
	}

	/// <summary>
	/// Changes one MCM setting, returning the new value or <c>null</c> if the user backed out. A toggle and a
	/// list are picked from; a number is typed and checked against the author's range before it is accepted.
	/// </summary>
	private string? EditMcmControl(McmControl control, string current)
	{
		if (control.Kind == McmControlKind.Toggle)
		{
			string? picked = ShowChoiceList(
				Loc.T("mcm.chooseTitle", control.Label),
				Loc.T("mcm.chooseListName", control.Label),
				new[] { Loc.T("mcm.on"), Loc.T("mcm.off") },
				McmToggleIsOn(current) ? Loc.T("mcm.on") : Loc.T("mcm.off"),
				Loc.T("mcm.chooseHint", control.Help));

			// Written back in the form MCM itself uses, whatever the file happened to hold before.
			if (picked == null) return null;
			return picked == Loc.T("mcm.on") ? "1" : "0";
		}

		if (control.Kind == McmControlKind.Choice && control.Options.Count > 0)
			return ChooseMcmOption(control, current);

		string prompt = control.Kind == McmControlKind.Number && (control.Min != null || control.Max != null)
			? Loc.T("mcm.promptRange", control.Label, control.Help,
				FormatNumber(control.Min), FormatNumber(control.Max))
			: Loc.T("mcm.prompt", control.Label, control.Help);

		while (true)
		{
			string? typed = ShowTextPrompt(Loc.T("mcm.chooseTitle", control.Label), prompt, current);
			if (typed == null) return null;
			if (control.Kind != McmControlKind.Number) return typed;

			if (!double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
			{
				Speak(Loc.T("mcm.invalidNumber"));
				current = typed;
				continue;
			}

			if ((control.Min != null && number < control.Min) || (control.Max != null && number > control.Max))
			{
				Speak(Loc.T("mcm.invalidRange", FormatNumber(control.Min), FormatNumber(control.Max)));
				current = typed;
				continue;
			}

			return typed;
		}
	}

	/// <summary>
	/// Offers a list control's options and returns the answer in the form the file already uses.
	///
	/// MCM stores a list choice as a number — the position in the author's list — but a file that was written by
	/// hand may hold the text instead. Rather than guess, the answer is written back in whichever form the
	/// current value is already in, so the manager never changes the shape of what the mod is reading.
	/// </summary>
	private string? ChooseMcmOption(McmControl control, string current)
	{
		bool storedAsNumber = int.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index);

		string currentLabel = storedAsNumber && index >= 0 && index < control.Options.Count
			? control.Options[index]
			: current;

		string? picked = ShowChoiceList(
			Loc.T("mcm.chooseTitle", control.Label),
			Loc.T("mcm.chooseListName", control.Label),
			control.Options,
			currentLabel,
			Loc.T("mcm.chooseHint", control.Help));

		if (picked == null) return null;

		// An empty current value means the setting has never been written; MCM's own form is the number.
		if (!storedAsNumber && current.Length > 0) return picked;

		int chosenIndex = control.Options.FindIndex(o => o.Equals(picked, StringComparison.OrdinalIgnoreCase));
		return chosenIndex >= 0 ? chosenIndex.ToString(CultureInfo.InvariantCulture) : picked;
	}

	/// <summary>A range bound for reading out, or "any" where the author set none.</summary>
	private static string FormatNumber(double? value) =>
		value == null ? Loc.T("mcm.rangeUnbounded") : value.Value.ToString("0.####", CultureInfo.InvariantCulture);
}
