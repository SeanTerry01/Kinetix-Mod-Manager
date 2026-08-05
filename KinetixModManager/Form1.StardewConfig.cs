using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// The settings editor for an ordinary Stardew mod — one that isn't a Content Patcher pack.
///
/// Less can be known here than anywhere else the manager edits settings, because a normal SMAPI mod declares
/// its options in code rather than in a file. What is recoverable still removes most of the guesswork: the
/// value already in the file says whether a setting is a yes/no, a number or text, and many mods ship the
/// labels and explanations for their in-game settings menu in a translation file. So a setting that used to
/// read <c>"AutomationInterval": 60</c> can read "Automation interval: 60" with the author's own sentence
/// about what it does, and a yes/no setting is picked from a list rather than typed.
/// </summary>
public partial class Form1
{
	/// <summary>One row: a setting and its current value, read as "Label: value".</summary>
	private sealed class StardewRow
	{
		public required StardewSetting Setting { get; init; }
		public override string ToString() => Loc.T("sdvconfig.row", Setting.Label, Display);

		private string Display => Setting.Value.Length > 0 ? Setting.Value : Loc.T("sdvconfig.valueEmpty");
	}

	/// <summary>
	/// Shows a mod's settings and lets them be changed, saving each change as it is made. Returns false when the
	/// mod has nothing that can be edited this way, leaving the caller to fall back to the JSON editor.
	/// </summary>
	private bool ShowStardewModSettings(string modName, string modFolder)
	{
		List<StardewSetting> settings = StardewModConfig.Read(modFolder);
		if (settings.Count == 0) return false;

		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("sdvconfig.title", modName), (container, closeView) =>
		{
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			var list = new ListBox
			{
				Dock = DockStyle.Fill,
				Font = new Font("Segoe UI", 12f),
				AccessibleName = Loc.T("sdvconfig.listName", modName),
				IntegralHeight = false,
				HorizontalScrollbar = true
			};

			void Reload(int selectIndex)
			{
				settings = StardewModConfig.Read(modFolder);
				list.BeginUpdate();
				list.Items.Clear();
				foreach (StardewSetting setting in settings) list.Items.Add(new StardewRow { Setting = setting });
				list.EndUpdate();
				if (list.Items.Count > 0)
					list.SelectedIndex = Math.Clamp(selectIndex, 0, list.Items.Count - 1);
			}
			Reload(0);

			var btnClose = new Button { Text = Loc.T("sdvconfig.closeBtn"), AutoSize = true, AccessibleName = Loc.T("sdvconfig.closeName") };
			var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
			buttons.Controls.Add(btnClose);

			layout.Controls.Add(list, 0, 0);
			layout.Controls.Add(buttons, 0, 1);
			container.Controls.Add(layout);

			WireAccessibleDialogList(list);

			void EditSelected()
			{
				if (list.SelectedItem is not StardewRow row) return;
				StardewSetting setting = row.Setting;

				string? chosen = EditStardewSetting(setting);
				if (chosen == null || chosen == setting.Value) return;

				if (!StardewModConfig.Write(modFolder, setting, chosen))
				{
					Speak(Loc.T("sdvconfig.saveFailed", setting.Label));
					return;
				}

				int index = list.SelectedIndex;
				Reload(index);
				Speak(Loc.T("sdvconfig.setTo", setting.Label, chosen));
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
		hint: Loc.T("sdvconfig.hint", settings.Count));

		return true;
	}

	/// <summary>
	/// Changes one setting, returning the new value or <c>null</c> if the user backed out. A yes/no setting is
	/// picked from a list; anything else is typed, and checked against the type the file already uses so a
	/// number cannot quietly become text the mod refuses to read.
	/// </summary>
	private string? EditStardewSetting(StardewSetting setting)
	{
		if (setting.IsBoolean)
			return ShowChoiceList(
				Loc.T("sdvconfig.chooseTitle", setting.Label),
				Loc.T("sdvconfig.chooseListName", setting.Label),
				new[] { "true", "false" },
				setting.Value,
				Loc.T("sdvconfig.chooseHint", setting.Description, setting.Value));

		// A key binding is the one free-text setting a list can be built for without the mod telling us
		// anything, because SMAPI's key names are the same for every mod. "Type something else" stays on the
		// end, since a mod may accept a combination or a name the list doesn't carry.
		if (StardewModConfig.LooksLikeAKeyBinding(setting))
		{
			var choices = new List<string>(StardewModConfig.KeyNames) { Loc.T("sdvconfig.otherValue") };

			string? picked = ShowChoiceList(
				Loc.T("sdvconfig.chooseTitle", setting.Label),
				Loc.T("sdvconfig.chooseListName", setting.Label),
				choices,
				setting.Value,
				Loc.T("sdvconfig.chooseKeyHint", setting.Description, setting.Value));

			if (picked == null) return null;
			if (picked != Loc.T("sdvconfig.otherValue")) return picked;

			// Fall through to typing, starting from what they already had.
			return ShowTextPrompt(
				Loc.T("sdvconfig.chooseTitle", setting.Label),
				Loc.T("sdvconfig.prompt", setting.Label, setting.Description),
				setting.Value);
		}

		string prompt = setting.Kind == StardewValueKind.Number
			? Loc.T("sdvconfig.promptNumber", setting.Label, setting.Description)
			: Loc.T("sdvconfig.prompt", setting.Label, setting.Description);

		string current = setting.Value;
		while (true)
		{
			string? typed = ShowTextPrompt(Loc.T("sdvconfig.chooseTitle", setting.Label), prompt, current);
			if (typed == null) return null;
			if (StardewModConfig.IsValid(setting, typed)) return typed;

			Speak(Loc.T("sdvconfig.invalidNumber"));
			current = typed;
		}
	}
}
