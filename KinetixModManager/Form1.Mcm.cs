using System.Globalization;

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

	/// <summary>
	/// Shows a mod's Mod Configuration Menu settings and lets them be changed. Each change is written as it is
	/// made, matching the INI editor, so there is no save step to remember.
	/// </summary>
	private void ShowMcmSettings(string modName, McmMenu menu)
	{
		McmSettings values = McmSettings.Load(menu);

		// The same snapshot the INI editor takes, a bad change being one restore away. On the first real change, because
		// each opening pushed an older one out, so eight looks at a menu could lose the snapshot from before a purge. It
		// holds the mod's own settings file: without it, restoring undid nothing here and still said "restored".
		bool snapshotTaken = false;

		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("mcm.title", modName), (container, closeView) =>
		{
			ListBox list = NewSettingsList();

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

			Button btnClose = AddSettingsListLayout(container, list, Loc.T("mcm.closeBtn"), Loc.T("mcm.closeName"));

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

				int index = list.SelectedIndex;

				// Applied and said while the chooser is still open, and the list reloaded once that has been heard, so the
				// reader reads the changed row as focus comes back. See ActBeforeReturning.
				ResultBeforeReturning? Act(string chosen)
				{
					if (chosen == row.Value) return null;

					if (!snapshotTaken)
					{
						CreateSafetyBackup(Loc.T("safety.reasonIni", modName), menu.UserIniPath);
						snapshotTaken = true;
					}

					if (!McmSettings.Save(menu, control, chosen))
						return new ResultBeforeReturning(Loc.T("mcm.saveFailed", control.Label));

					values.Set(control, chosen);
					// The row says the new value, so the result only says what changed.
					return new ResultBeforeReturning(Loc.T("mcm.setTo", control.Label), () => Reload(index));
				}

				EditMcmControl(control, row.Value, Act);
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
	/// <param name="apply">
	/// Saves the new value, in the form the file uses, and says what to say. Called once, as the chooser or the box closes,
	/// and only for a value that can be saved. See <see cref="ActBeforeReturning"/>.
	/// </param>
	private string? EditMcmControl(McmControl control, string current, Func<string, ResultBeforeReturning?> apply)
	{
		if (control.Kind == McmControlKind.Toggle)
		{
			// Written back in the form MCM itself uses, whatever the file happened to hold before.
			string Stored(string picked) => picked == Loc.T("mcm.on") ? "1" : "0";

			string? picked = ShowChoiceList(
				Loc.T("mcm.chooseTitle", control.Label),
				Loc.T("mcm.chooseListName", control.Label),
				new[] { Loc.T("mcm.on"), Loc.T("mcm.off") },
				McmValue.McmToggleIsOn(current) ? Loc.T("mcm.on") : Loc.T("mcm.off"),
				Loc.T("mcm.chooseHint", control.Help),
				p => apply(Stored(p)));

			return picked == null ? null : Stored(picked);
		}

		if (control.Kind == McmControlKind.Choice && control.Options.Count > 0)
			return ChooseMcmOption(control, current, apply);

		string prompt = control.Kind == McmControlKind.Number && (control.Min != null || control.Max != null)
			? Loc.T("mcm.promptRange", control.Label, control.Help,
				FormatNumber(control.Min), FormatNumber(control.Max))
			: Loc.T("mcm.prompt", control.Label, control.Help);

		while (true)
		{
			// Checked before it is saved, as the box closes. A number that won't do is said once the box has gone, and the
			// box opens again with it in.
			string? problem = null;
			string? typed = ShowTextPrompt(Loc.T("mcm.chooseTitle", control.Label), prompt, current, actOnAnswer: t =>
			{
				problem = NumberProblem(control, t);
				return problem == null ? apply(t) : null;
			});
			if (typed == null) return null;
			if (problem == null) return typed;

			Speak(problem);
			current = typed;
		}
	}

	/// <summary>Why <paramref name="typed"/> won't do for a number setting, or null when it will, or the setting isn't a number.</summary>
	private static string? NumberProblem(McmControl control, string typed)
	{
		if (control.Kind != McmControlKind.Number) return null;
		if (!double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
			return Loc.T("mcm.invalidNumber");
		if ((control.Min != null && number < control.Min) || (control.Max != null && number > control.Max))
			return Loc.T("mcm.invalidRange", FormatNumber(control.Min), FormatNumber(control.Max));
		return null;
	}

	/// <summary>
	/// Offers a list control's options and returns the answer in the form the file already uses.
	///
	/// MCM stores a list choice as a number — the position in the author's list — but a file that was written by
	/// hand may hold the text instead. Rather than guess, the answer is written back in whichever form the
	/// current value is already in, so the manager never changes the shape of what the mod is reading.
	/// </summary>
	private string? ChooseMcmOption(McmControl control, string current, Func<string, ResultBeforeReturning?> apply)
	{
		bool storedAsNumber = int.TryParse(current, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index);

		string currentLabel = storedAsNumber && index >= 0 && index < control.Options.Count
			? control.Options[index]
			: current;

		string Stored(string picked)
		{
			// An empty current value means the setting has never been written; MCM's own form is the number.
			if (!storedAsNumber && current.Length > 0) return picked;

			int chosenIndex = control.Options.FindIndex(o => o.Equals(picked, StringComparison.OrdinalIgnoreCase));
			return chosenIndex >= 0 ? chosenIndex.ToString(CultureInfo.InvariantCulture) : picked;
		}

		string? picked = ShowChoiceList(
			Loc.T("mcm.chooseTitle", control.Label),
			Loc.T("mcm.chooseListName", control.Label),
			control.Options,
			currentLabel,
			Loc.T("mcm.chooseHint", control.Help),
			p => apply(Stored(p)));

		return picked == null ? null : Stored(picked);
	}

	/// <summary>A range bound for reading out, or "any" where the author set none.</summary>
	private static string FormatNumber(double? value) =>
		value == null ? Loc.T("mcm.rangeUnbounded") : value.Value.ToString("0.####", CultureInfo.InvariantCulture);
}
