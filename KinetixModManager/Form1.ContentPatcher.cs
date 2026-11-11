namespace KinetixModManager;

/// <summary>
/// The accessible settings editor for a Content Patcher content pack.
///
/// Most Stardew content mods are Content Patcher packs, and a good many let the player configure them. The
/// trouble is where that configuration ends up: the author declares each setting and the values it accepts in
/// <c>content.json</c>, but Content Patcher then generates a <c>config.json</c> holding only the answers. Open
/// that in a text editor and a setting reads <c>"ObeliskOptions": "vanilla"</c> — with nothing to say that
/// glass, garden, Yri and Juffuffles are the alternatives. Changing it means guessing, or going and reading the
/// author's file yourself, or using an in-game menu.
///
/// So this puts the two halves back together: a list of the mod's settings, each showing its current answer,
/// where Enter offers the values the author actually allows. Nothing has to be typed and nothing has to be
/// guessed at.
/// </summary>
public partial class Form1
{

	/// <summary>
	/// The settings a Stardew mod exposes through Content Patcher, or an empty list when it is not a Content
	/// Patcher pack or has nothing to configure. Most packs have nothing, which is not a failure.
	/// </summary>
	private static List<CpConfigOption> ContentPatcherSettingsFor(string modFolder)
	{
		string? contentJson = ContentPatcherConfig.FindContentJson(modFolder);
		return contentJson == null ? new List<CpConfigOption>() : ContentPatcherConfig.ReadSchema(contentJson);
	}

	/// <summary>
	/// Shows the mod's Content Patcher settings and lets them be changed.
	///
	/// Each change is written to the pack's <c>config.json</c> as it is made, the way the INI editor saves, so
	/// there is no separate save step to remember and no unsaved state to lose on Escape.
	/// </summary>
	private void ShowContentPatcherConfig(string modName, string modFolder, List<CpConfigOption> schema)
	{
		string configPath = Path.Combine(modFolder, "config.json");

		// The pack's own answers. A pack that has never been run has no config file yet: every setting then
		// shows the author's default, and the file is created as soon as one is changed.
		Dictionary<string, string> values = ContentPatcherConfig.ReadValues(configPath);

		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("cpconfig.title", modName), (container, closeView) =>
		{
			ListBox list = NewSettingsList();

			void Reload(int selectIndex)
			{
				list.BeginUpdate();
				list.Items.Clear();
				foreach (CpConfigOption option in schema)
				{
					bool hasAnswer = values.TryGetValue(option.Name, out string? saved) && saved.Length > 0;
					list.Items.Add(new CpRow
					{
						Option = option,
						Value = hasAnswer ? saved! : option.Default,
						IsDefault = !hasAnswer
					});
				}
				list.EndUpdate();
				if (list.Items.Count > 0)
					list.SelectedIndex = Math.Clamp(selectIndex, 0, list.Items.Count - 1);
			}
			Reload(0);

			Button btnClose = AddSettingsListLayout(container, list, Loc.T("cpconfig.closeBtn"), Loc.T("cpconfig.closeName"));

			// Writes one answer through to the pack's config, reporting rather than failing silently.
			bool Apply(CpConfigOption option, string newValue)
			{
				// Written first. Changing this first meant that after a failed write, which the user was told about, the next
				// successful edit saved the value that had failed.
				string? had = values.TryGetValue(option.Name, out string? current) ? current : null;
				values[option.Name] = newValue;
				if (ContentPatcherConfig.WriteValues(configPath, values)) return true;

				if (had == null) values.Remove(option.Name);
				else values[option.Name] = had;
				Speak(Loc.T("cpconfig.saveFailed", option.Name));
				return false;
			}

			void EditSelected()
			{
				if (list.SelectedItem is not CpRow row) return;

				string? chosen = row.Option.HasChoices
					? ChooseAllowedValue(row)
					: ShowTextPrompt(
						Loc.T("cpconfig.title", modName),
						Loc.T("cpconfig.freeTextPrompt", row.Option.Name, DescriptionSentence(row.Option)),
						row.Value);

				if (chosen == null || chosen == row.Value) return;
				if (!Apply(row.Option, chosen)) return;

				int index = list.SelectedIndex;
				Reload(index);
				SayOnReturnTo(list, Loc.T("cpconfig.setTo", row.Option.Name, chosen.Length > 0 ? chosen : Loc.T("cpconfig.valueEmpty")));
			}

			// Delete puts a setting back to what the author intended, which is the one edit that is hard to make
			// by hand: the default lives in content.json, not in the file being edited. The setting is deleted
			// rather than overwritten with today's default — see WriteValues for why that matters later.
			void ResetSelected()
			{
				if (list.SelectedItem is not CpRow row) return;
				if (row.IsDefault)
				{
					Speak(Loc.T("cpconfig.alreadyDefault", row.Option.Name));
					return;
				}

				values.Remove(row.Option.Name);
				if (!ContentPatcherConfig.WriteValues(configPath, values, remove: new[] { row.Option.Name }))
				{
					Speak(Loc.T("cpconfig.saveFailed", row.Option.Name));
					return;
				}

				int index = list.SelectedIndex;
				Reload(index);
				Speak(Loc.T("cpconfig.reset", row.Option.Name, row.Option.Default));
			}

			btnClose.Click += delegate { closeView(); };
			list.KeyDown += delegate (object? s, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Enter)
				{
					e.Handled = e.SuppressKeyPress = true;
					EditSelected();
				}
				else if (e.KeyCode == Keys.Delete)
				{
					e.Handled = e.SuppressKeyPress = true;
					ResetSelected();
				}
			};
			// Escape is handled by the view itself (see Form1.InlineView).

			return list;
		},
		hint: Loc.T("cpconfig.hint", schema.Count));
	}

	/// <summary>
	/// Offers the values the author allows for one setting. The author's own explanation is said once as the
	/// chooser opens — the moment it is actually wanted, and it keeps the settings list itself short enough to
	/// arrow through comfortably.
	/// </summary>
	private string? ChooseAllowedValue(CpRow row) => row.Option.AllowMultiple ? ChooseSeveralValues(row) : ShowChoiceList(
		Loc.T("cpconfig.chooseTitle", row.Option.Name),
		Loc.T("cpconfig.chooseListName", row.Option.Name),
		row.Option.AllowedValues,
		row.Value,
		Loc.T("cpconfig.chooseHint", DescriptionSentence(row.Option), row.Value));

	/// <summary>
	/// The chooser for a setting that takes several values at once. Enter switches the selected one on or off, and Done saves.
	/// With only the one-value chooser, picking anything replaced all the others.
	/// </summary>
	private string? ChooseSeveralValues(CpRow row)
	{
		CpConfigOption option = row.Option;
		var on = new HashSet<string>(ContentPatcherConfig.SplitValues(row.Value), StringComparer.OrdinalIgnoreCase);
		string RowText(string value) => Loc.T(on.Contains(value) ? "cpconfig.valueOn" : "cpconfig.valueOff", value);
		string? result = null;

		ShowInlineView(Loc.T("cpconfig.chooseTitle", option.Name), (container, closeView) =>
		{
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			var list = new ListBox
			{
				Dock = DockStyle.Fill,
				Font = new Font("Segoe UI", 12f),
				AccessibleName = Loc.T("cpconfig.chooseListName", option.Name),
				IntegralHeight = false,
				HorizontalScrollbar = true
			};
			foreach (string value in option.AllowedValues) list.Items.Add(RowText(value));
			if (list.Items.Count > 0) list.SelectedIndex = 0;

			var done = new Button { Text = Loc.T("cpconfig.doneButton"), AutoSize = true };
			layout.Controls.Add(list, 0, 0);
			layout.Controls.Add(done, 0, 1);
			container.Controls.Add(layout);
			WireAccessibleDialogList(list);

			list.KeyDown += delegate (object? s, KeyEventArgs e)
			{
				if (e.KeyCode != Keys.Enter || list.SelectedIndex < 0) return;
				e.Handled = e.SuppressKeyPress = true;

				string value = option.AllowedValues[list.SelectedIndex];
				if (!on.Remove(value)) on.Add(value);
				list.Items[list.SelectedIndex] = RowText(value);
				Speak(RowText(value), interrupt: true);
			};

			done.Click += delegate
			{
				if (on.Count == 0 && !option.AllowBlank)
				{
					Speak(Loc.T("cpconfig.needOne"));
					return;
				}
				result = ContentPatcherConfig.JoinValues(option.AllowedValues, on);
				closeView();
			};

			return list;
		},
		hint: Loc.T("cpconfig.severalHint", DescriptionSentence(option)));

		return result;
	}

	/// <summary>
	/// The author's description of a setting, ended with a full stop so the reader pauses before whatever
	/// follows it, or an empty string when they wrote none. Only about a fifth of settings carry one.
	/// </summary>
	private static string DescriptionSentence(CpConfigOption option)
	{
		string text = option.Description.Trim();
		if (text.Length == 0) return "";
		return text.EndsWith(".", StringComparison.Ordinal) ? text : text + ".";
	}
}
