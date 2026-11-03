namespace KinetixModManager.GtkHead;

/// <summary>
/// The Settings screen, and the first one this head has had.
///
/// <para>
/// It could not exist before: <c>AppSettings</c> lived in the WinForms project, so the GTK window had
/// nowhere to keep anything and recomputed the world from the game locator on every run. Everything here is
/// therefore less about the controls than about there finally being something behind them.
/// </para>
///
/// <para>
/// It is deliberately not the WinForms Settings window rebuilt. That one is 1,349 lines across seven tabs,
/// and most of what it holds is either Windows-only or about a game this platform cannot usefully manage.
/// What is here is what a Linux user can actually act on, in one flat list: where the mods for the loaded
/// game are, whether there are sounds and how loud, which catalogue to search, and the language. A screen
/// with six things on it that all work is worth more than one with forty where half say "not on this
/// platform".
/// </para>
///
/// <para>
/// One column, one control per row, each with its own label — because a reader moves through this with Tab
/// and every stop needs to say what it is without the user having to hunt for a heading above it.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
	// Held rather than local to the building of the tab, because this tab outlives a game switch and its fields are
	// about one game. Filled once at startup and never again, Save wrote the text still in the box as the NEW game's mods
	// folder, so saving a sound setting pointed one game at another's mods.
	private readonly Gtk.Entry _settingsModsEntry = Gtk.Entry.New();
	private readonly Gtk.Label _settingsDetected = Gtk.Label.New("");
	private readonly Gtk.Box _settingsForThisGame = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
	private Gtk.DropDown? _settingsSource;
	private IReadOnlyList<ModSourceInfo> _settingsSearchable = Array.Empty<ModSourceInfo>();

	private Gtk.Widget BuildSettingsTab()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 8);
		box.SetMarginTop(6); box.SetMarginBottom(6);
		box.SetMarginStart(6); box.SetMarginEnd(6);

		box.Append(Heading(Loc.T("gtk.settingsIntro")));

		// --- where this game's mods are -------------------------------------
		var modsLabel = Gtk.Label.NewWithMnemonic(Loc.T("gtk.settingsModsFolder"));
		modsLabel.SetXalign(0);
		box.Append(modsLabel);

		// Said on the control itself rather than only in the label, because the placeholder is the thing that
		// answers "what happens if I leave this empty" and a label above cannot.
		_settingsModsEntry.SetPlaceholderText(Loc.T("gtk.settingsModsFolderHint"));
		modsLabel.SetMnemonicWidget(_settingsModsEntry);
		box.Append(_settingsModsEntry);

		_settingsDetected.SetXalign(0);
		_settingsDetected.SetWrap(true);
		box.Append(_settingsDetected);

		// --- sound ----------------------------------------------------------
		var sounds = Gtk.CheckButton.NewWithMnemonic(Loc.T("gtk.settingsSounds"));
		sounds.SetActive(_settings.EnableUiSounds);
		box.Append(sounds);

		var volumeLabel = Gtk.Label.NewWithMnemonic(Loc.T("gtk.settingsVolume"));
		volumeLabel.SetXalign(0);
		box.Append(volumeLabel);

		// A spin button rather than a slider: a slider announces a percentage that a reader has to catch on
		// the way past, while this one can be typed into and arrows one step at a time.
		var volume = Gtk.SpinButton.NewWithRange(0, 100, 5);
		volume.SetValue(_settings.SoundVolume);
		volumeLabel.SetMnemonicWidget(volume);
		box.Append(volume);

		// --- where mods are searched for, and the warning that matters most --
		box.Append(_settingsForThisGame);

		FillSettingsForGame();

		// --- save -----------------------------------------------------------
		var save = Gtk.Button.NewWithLabel(Loc.T("gtk.settingsSave"));
		save.OnClicked += (_, _) =>
		{
			// Moving the mods folder under something working in it is how a mod ends up half in each.
			if (RefusedWhileChanging()) return;

			string typed = _settingsModsEntry.GetText().Trim();
			if (typed.Length == 0) _settings.GameModsPaths.Remove(_game.Id);
			else _settings.GameModsPaths[_game.Id] = typed;

			_settings.EnableUiSounds = sounds.GetActive();
			_settings.SoundVolume = (int)volume.GetValue();

			if (_settingsSource != null && _settingsSearchable.Count > 1)
				_settings.PreferredModSource[_game.Id] = _settingsSearchable[(int)_settingsSource.GetSelected()].Id;

			// Save reports a failure through an event rather than by throwing, and the event has run by the time this returns.
			_lastSaveFailure = null;
			_savingFromSettingsTab = true;
			_settings.Save();
			_savingFromSettingsTab = false;

			// Reloading is not tidiness: the mods folder may have just changed, and a Settings screen that
			// saved a path without the list behind it agreeing would be telling the user two things.
			LoadInstalled();
			_settingsDetected.SetText(DescribeDetectedFolder());

			if (_lastSaveFailure != null)
			{
				// They apply until the manager closes, but "saved" would be untrue, and is what they would rely on.
				_sound.Play("error");
				Say(Loc.T("settings.saveFailedSpeak", _lastSaveFailure.Message) + " " + _view.Announcement, interrupt: true);
				return;
			}

			_sound.Play("load_complete");
			Say(Loc.T("gtk.settingsSaved") + " " + _view.Announcement, interrupt: true);
		};
		box.Append(save);

		var scroller = Gtk.ScrolledWindow.New();
		scroller.SetChild(box);
		scroller.SetVexpand(true);
		return scroller;
	}

	/// <summary>Puts the game in hand's values in everything on this tab that is about one game.</summary>
	private void FillSettingsForGame()
	{
		_settingsModsEntry.SetText(_settings.GameModsPaths.TryGetValue(_game.Id, out string? saved) ? saved : "");
		_settingsDetected.SetText(DescribeDetectedFolder());

		while (_settingsForThisGame.GetFirstChild() is { } child) _settingsForThisGame.Remove(child);

		_settingsSearchable = ModSources.SearchableFor(_game.Id);
		_settingsSource = null;

		if (_settingsSearchable.Count > 1)
		{
			var sourceLabel = Gtk.Label.NewWithMnemonic(Loc.T("gtk.settingsSource"));
			sourceLabel.SetXalign(0);
			_settingsForThisGame.Append(sourceLabel);

			_settingsSource = Gtk.DropDown.NewFromStrings(_settingsSearchable.Select(s => s.DisplayName).ToArray());

			string preferred = _settings.PreferredModSourceFor(_game.Id);
			int index = _settingsSearchable.ToList().FindIndex(s => s.Id == preferred);
			_settingsSource.SetSelected((uint)Math.Max(0, index));
			sourceLabel.SetMnemonicWidget(_settingsSource);
			_settingsForThisGame.Append(_settingsSource);
		}
		else
		{
			// One catalogue is not a choice, and a dropdown holding one item suggests the user is missing
			// something. The Windows head says the same thing in the same case.
			_settingsForThisGame.Append(Wrapped(Loc.T("gtk.settingsOneSourceOnly",
				_settingsSearchable.Count > 0 ? _settingsSearchable[0].DisplayName : "", _game.DisplayName)));
		}

		if (!_game.AccessModSpeaksOnLinux)
			_settingsForThisGame.Append(Wrapped(Loc.T("gtk.settingsWillNotSpeak", _game.DisplayName)));
	}

	/// <summary>What the locator found on its own, so the user can see whether they need to type anything.</summary>
	private string DescribeDetectedFolder()
	{
		string found = ModsFolderFor(_game);

		return string.IsNullOrEmpty(found)
			? Loc.T("gtk.settingsNothingDetected", _game.DisplayName)
			: Loc.T("gtk.settingsDetected", found);
	}

	private static Gtk.Widget Heading(string text)
	{
		var label = Gtk.Label.New(text);
		label.SetXalign(0);
		label.SetWrap(true);
		return label;
	}

	private static Gtk.Widget Wrapped(string text) => Heading(text);
}
