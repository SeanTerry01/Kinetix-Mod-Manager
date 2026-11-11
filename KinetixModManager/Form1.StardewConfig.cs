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

	/// <summary>
	/// Shows a mod's settings and lets them be changed, saving each change as it is made. Returns false when the
	/// mod has nothing that can be edited this way, leaving the caller to fall back to the JSON editor.
	/// </summary>
	private bool ShowStardewModSettings(string modName, string modFolder, string? configFile = null)
	{
		List<StardewSetting> settings = StardewModConfig.Read(modFolder, configFile);
		if (settings.Count == 0) return false;

		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("sdvconfig.title", modName), (container, closeView) =>
		{
			ListBox list = NewSettingsList();

			void Reload(int selectIndex)
			{
				settings = StardewModConfig.Read(modFolder, configFile);
				list.BeginUpdate();
				list.Items.Clear();
				foreach (StardewSetting setting in settings) list.Items.Add(new StardewRow { Setting = setting });
				list.EndUpdate();
				if (list.Items.Count > 0)
					list.SelectedIndex = Math.Clamp(selectIndex, 0, list.Items.Count - 1);
			}
			Reload(0);

			Button btnClose = AddSettingsListLayout(container, list, Loc.T("sdvconfig.closeBtn"), Loc.T("sdvconfig.closeName"));

			void EditSelected()
			{
				if (list.SelectedItem is not StardewRow row) return;
				StardewSetting setting = row.Setting;

				int index = list.SelectedIndex;

				// Applied and said while the chooser is still open, and the list reloaded once that has been heard, so the
				// reader reads the changed row as focus comes back. See ActBeforeReturning.
				ResultBeforeReturning? Act(string chosen)
				{
					if (chosen == setting.Value) return null;
					if (!StardewModConfig.Write(modFolder, setting, chosen, configFile))
						return new ResultBeforeReturning(Loc.T("sdvconfig.saveFailed", setting.Label));
					return new ResultBeforeReturning(Loc.T("sdvconfig.setTo", setting.Label, chosen), () => Reload(index), NamesRow: true);
				}

				EditStardewSetting(setting, Act);
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
	/// <param name="apply">Saves the value and says what to say, once, as the chooser or box closes. See <see cref="ActBeforeReturning"/>.</param>
	private string? EditStardewSetting(StardewSetting setting, Func<string, ResultBeforeReturning?> apply)
	{
		if (setting.IsBoolean)
			return ShowChoiceList(
				Loc.T("sdvconfig.chooseTitle", setting.Label),
				Loc.T("sdvconfig.chooseListName", setting.Label),
				new[] { "true", "false" },
				setting.Value,
				Loc.T("sdvconfig.chooseHint", setting.Description, setting.Value),
				apply);

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
				Loc.T("sdvconfig.chooseKeyHint", setting.Description, setting.Value),
				p => p == Loc.T("sdvconfig.otherValue") ? null : apply(p));

			if (picked == null) return null;
			if (picked != Loc.T("sdvconfig.otherValue")) return picked;

			// Fall through to typing, starting from what they already had.
			return ShowTextPrompt(
				Loc.T("sdvconfig.chooseTitle", setting.Label),
				Loc.T("sdvconfig.prompt", setting.Label, setting.Description),
				setting.Value, actOnAnswer: apply);
		}

		string prompt = setting.Kind == StardewValueKind.Number
			? Loc.T("sdvconfig.promptNumber", setting.Label, setting.Description)
			: Loc.T("sdvconfig.prompt", setting.Label, setting.Description);

		string current = setting.Value;
		while (true)
		{
			// Checked as the box closes; one that won't do is said once it has gone, and the box opens again with it in.
			string? typed = ShowTextPrompt(Loc.T("sdvconfig.chooseTitle", setting.Label), prompt, current,
				actOnAnswer: t => StardewModConfig.IsValid(setting, t) ? apply(t) : null);
			if (typed == null) return null;
			if (StardewModConfig.IsValid(setting, typed)) return typed;

			Speak(Loc.T("sdvconfig.invalidNumber"));
			current = typed;
		}
	}
}
