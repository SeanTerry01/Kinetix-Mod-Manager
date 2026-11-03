namespace KinetixModManager.GtkHead;

/// <summary>
/// The one window. Two tabs: the mods that are installed, and searching Modrinth for more.
///
/// <para>
/// Every decision here is about what a screen reader will make of it, because that is the only interface
/// this program really has. A row is a single label holding a whole sentence rather than a grid of cells,
/// so arrowing down a list gives one fact per press instead of three. What Orca already says is deliberately
/// not said again - it reads the focused row and names the widget that takes focus, so the window announces
/// only what Orca cannot know. Nothing depends on a colour, a position, or a pointer.
/// </para>
///
/// <para>
/// It covers every game the manager supports, not one of them. That is the whole point of the program: an
/// accessible mod manager for games in general, which happens to be the only one of its kind. The games come
/// from <see cref="GameProfiles"/> rather than being named here, so a seventh game arrives in this window
/// the same way it arrives in the WinForms one — by being added to the table.
/// </para>
///
/// <para>
/// A separate matter, worth knowing and not worth confusing with the above: on Linux the accessibility mods
/// for Skyrim, Fallout 4 and The Witcher 3 speak by driving NVDA or JAWS, which do not exist inside a Proton
/// prefix, so those games may run and stay silent. That is a fact about the <em>games</em>, not about this
/// manager — managing their mods works regardless, and a user may well be modding on one machine and playing
/// on another. It belongs in front of the user as information, never as a reason to withhold the game.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
	private readonly Gtk.ApplicationWindow _window;
	private readonly IAnnouncer _announcer;
	private readonly IDispatcher _ui = new GlibDispatcher();

	/// <summary>
	/// The short cues, which are per game and always were — the theme follows whatever is loaded, so a
	/// Stardew session is told apart from a Skyrim one by ear before anything is read out. Only the playing
	/// is here; SoundThemes picks the file, so both heads fall back to the Default theme the same way.
	/// </summary>
	private readonly ISoundEngine _sound;

	private readonly Gtk.Notebook _tabs = Gtk.Notebook.New();
	private readonly Gtk.ListBox _installed = Gtk.ListBox.New();
	private readonly Gtk.ListBox _results = Gtk.ListBox.New();
	private readonly Gtk.Entry _search = Gtk.Entry.New();
	private readonly Gtk.Label _status = Gtk.Label.New("");

	private readonly Gtk.ListBox _games = Gtk.ListBox.New();
	private readonly IGameLocator _locator = new LinuxGameLocator();

	/// <summary>
	/// The installed list as the core describes it — the rows, and the sentence to say about them.
	///
	/// Built there rather than here because both are decisions, and this window got both wrong: it read
	/// every game's switched-off state with Minecraft's rule, and announced a game that was not installed
	/// as having zero mods. See InstalledModsView.
	/// </summary>
	private InstalledModsView _view = InstalledModsView.NotInstalled(null);
	private readonly List<GameMod> _found = new();
	private readonly List<GameProfile> _gameList = new();

	/// <summary>
	/// Every catalogue this head can search — all of them, now that Nexus's service turned out to be portable
	/// and moved to the core without a line changing. Which of them a given search asks is
	/// <see cref="ModSearchPlan"/>'s decision, from the user's mode and their per-game preference.
	/// </summary>
	private readonly IReadOnlyList<IModSource> _modSources;
	private WebKitView? _web;

	/// <summary>Named rather than written as 3, because the last time a tab was inserted every number
	/// underneath it shifted and F6 quietly started addressing the wrong controls.</summary>
	private const int WikiTabIndex = 3;

	private const int InstalledTabIndex = 1;

	/// <summary>
	/// The Minecraft version to search and install for: the one the user pinned, or the one Fabric is
	/// actually installed for.
	///
	/// It was a constant — 1.21.1 — which made every search and every install quietly wrong for anybody on a
	/// different version, and a mod built for another version installs perfectly and then loads nothing. The
	/// same pair the Windows head uses, and both halves of it are in the core.
	/// </summary>
	private string GameVersionInUse()
	{
		if (!_game.IsMinecraft) return "";

		string pinned = _settings.MinecraftGameVersion;
		if (pinned.Length > 0) return pinned;

		string root = _locator.InstallFolder(_game) ?? MinecraftLayout.DefaultRootFolder;
		return FabricInstaller.DetectInstalledGameVersion(root);
	}

	/// <summary>Where the search box lives. Named for the same reason as <see cref="WikiTabIndex"/>.</summary>
	private const int FindModsTabIndex = 2;

	private readonly Gtk.Label _checkResult = Gtk.Label.New(Loc.T("gtk.checkPrompt"));

	private readonly AppSettings _settings;

	/// <summary>
	/// What is changing mods on disk right now. While one runs, switching game is refused and closing the window asks
	/// first: these operations read the game's folder and Nexus page a step at a time, and nothing else may move those
	/// underneath them. Each action below takes this for as long as it runs.
	/// </summary>
	private readonly OperationGate _modOperations = new();

	/// <summary>Says so and returns true when mods are being changed, for an action that must not start in the middle of that.</summary>
	private bool RefusedWhileChanging()
	{
		if (!_modOperations.IsBusy) return false;

		Say(Loc.T("session.busy"), interrupt: true);
		return true;
	}

	/// <summary>A close that asked first, so the next one goes ahead. Any other key calls it off.</summary>
	private bool _closeArmed;

	/// <summary>The most recent settings save that failed, read straight after a save by whoever wants to know.</summary>
	private Exception? _lastSaveFailure;
	private bool _saveFailureReported;
	private bool _savingFromSettingsTab;

	/// <summary>
	/// Nexus, for the five games whose mods come from there. Built here rather than reached through a static
	/// because it carries the user's key and their rate-limit counters — state that belongs to one session.
	/// </summary>
	private readonly NexusService _nexus;

	/// <summary>
	/// The game in hand. Not defaulted to any particular one: Minecraft is an entry in the list like every
	/// other game, and a manager that always opened on it was telling five-sixths of its users they had
	/// started somewhere wrong.
	/// </summary>
	private GameProfile _game = GameProfiles.All[0];
	private string _modsFolder = "";

	public MainWindow(Gtk.Application app, AppSettings settings)
	{
		_settings = settings;
		_nexus = new NexusService(_settings);
		_modSources = new IModSource[]
		{
			// This head reads no failure flag of its own, so the source says when Nexus did not answer. Without that an
			// outage was announced as "0 results", a statement about the catalogue that nothing had learned.
			new NexusModSource(_nexus, _settings, noteWhenNoAnswer: true),
			new ModrinthModSource(),
			new CurseForgeModSource(() => _settings.ModSourceApiKey(ModSources.CurseForge)),
		};

		// The game the user was on last time. Before settings reached this head every run started on
		// Minecraft whatever you had been doing, which for anyone managing another game meant two keypresses
		// before the window was about what they opened it for.
		// What you were last using, or failing that the first game actually on this machine — which beats
		// opening on one that is not installed and reporting it as empty.
		_game = GameProfiles.Find(_settings.ActiveGame)
			?? GameProfiles.All.FirstOrDefault(g => !string.IsNullOrEmpty(_locator.InstallFolder(g)))
			?? GameProfiles.All[0];

		// Written to settings too, not only held here. NexusService asks settings which game it is on, and with "None"
		// there it falls back to Stardew: Search asked Stardew's catalogue, and update checks put Skyrim's mod ids on
		// Stardew's pages and offered unrelated versions as updates. Not saved, because nobody has chosen this yet.
		_settings.ActiveGame = _game.Id;
		_settings.SaveFailed += OnSettingsSaveFailed;

		_window = Gtk.ApplicationWindow.New(app);
		// Announcements go to the user's screen reader, with speech-dispatcher only as the fallback for
		// when there is no reader to ask. See OrcaAnnouncer for why that order matters so much.
		_announcer = new OrcaAnnouncer(_window, new SpeechDispatcherAnnouncer());
		_sound = new GStreamerSoundEngine(
			Path.Combine(AppContext.BaseDirectory, "sounds"),
			// Read fresh each time rather than captured, so switching games is heard on the very next cue and
			// a change in Settings takes effect without restarting.
			() => _settings.AllowManualTheme ? _settings.CurrentTheme : SoundThemes.ForGame(_game?.Id),
			() => _settings.EnableUiSounds,
			() => _settings.SoundVolume);
		_window.SetTitle(Loc.T("gtk.windowTitle"));
		_window.SetDefaultSize(900, 620);

		var root = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
		root.SetMarginTop(8); root.SetMarginBottom(8);
		root.SetMarginStart(8); root.SetMarginEnd(8);

		_tabs.SetVexpand(true);
		AddTab(BuildGamesTab(), Loc.T("gtk.tabGames"), _games);
		AddTab(BuildInstalledTab(), Loc.T("gtk.tabInstalled"), _installed);
		AddTab(BuildDiscoverTab(), Loc.T("gtk.tabFind"), _search, _results);

		// Built first, because the web view that is this tab's stop only exists once it has been.
		Gtk.Widget wikiTab = BuildWikiTab();
		AddTab(wikiTab, Loc.T("gtk.tabWiki"), _web is null ? Array.Empty<Gtk.Widget>() : new Gtk.Widget[] { _web.Widget });

		AddTab(BuildUpdatesTab(), Loc.T("gtk.tabUpdates"), _updates);
		AddTab(BuildProfilesTab(), Loc.T("gtk.tabProfiles"), _profileName, _profiles);
		AddTab(BuildDependenciesTab(), Loc.T("gtk.tabDependencies"), _dependencies);
		AddTab(BuildBackupsTab(), Loc.T("gtk.tabBackups"), _backups);
		AddTab(BuildLogTab(), Loc.T("gtk.tabLog"), _log);
		AddTab(BuildCheckTab(), Loc.T("gtk.tabCheck"), _checkResult);
		AddTab(BuildSettingsTab(), Loc.T("gtk.tabSettings"), _settingsModsEntry);
		// A tab left behind takes its question with it: the armed row is no longer in front of the user.
		_tabs.OnSwitchPage += (_, _) => CancelArmed();
		root.Append(_tabs);

		// A status line that is also spoken. On its own a label change is silent to a screen reader — Orca
		// has no reason to look at it — so anything worth putting here is worth saying out loud as well.
		_status.SetXalign(0);
		root.Append(_status);

		_window.SetChild(root);
		AddKeyboardShortcuts();

		// Closing used to end the process with no word, even half way through writing a backup that would then be listed
		// as a good one. Asked inline, like every other second thought here. Returning true keeps the window.
		_window.OnCloseRequest += (_, _) =>
		{
			if (!_modOperations.IsBusy || _closeArmed) return false;

			_closeArmed = true;
			Say(Loc.T("session.closeBusy") + " " + Loc.T("gtk.closeBusyArm"), interrupt: true);
			return true;
		};

		LoadInstalled();
		SweepLeftoversInBackground();
	}

	/// <summary>A tab, and the things F6 stops at on it after the tab strip. See <see cref="FocusStops"/>.</summary>
	private void AddTab(Gtk.Widget page, string title, params Gtk.Widget[] stops)
	{
		_tabs.AppendPage(page, Gtk.Label.New(title));
		_tabStops.Add(stops);
	}

	private readonly List<Gtk.Widget[]> _tabStops = new();

	/// <summary>
	/// Clears what a crash or a forced close left behind: part-finished downloads and unpacked folders nothing will ever
	/// clean up. Only ones at least a day old, so nothing another run might still be using is touched.
	/// </summary>
	private void SweepLeftoversInBackground()
	{
		string[] tempRoots = GameProfiles.All
			.Select(ModsFolderFor)
			.Where(folder => !string.IsNullOrEmpty(folder) && Directory.Exists(folder))
			.Select(ModArchive.TempRootFor)
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		_ = Task.Run(() =>
		{
			try
			{
				foreach (string root in tempRoots) ModArchive.SweepOldWorkFolders(root, TimeSpan.FromDays(1));
				foreach (string downloads in new[] { UpdateDownloadsFolder, InstallDownloadsFolder })
					ModArchive.SweepOldPartialDownloads(downloads, TimeSpan.FromDays(1));
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Install", "clearing old leftovers", ex); }
		});
	}

	private static string UpdateDownloadsFolder => Path.Combine(AppSettings.AppDataFolder, "downloads");

	private static string InstallDownloadsFolder => Path.Combine(Path.GetTempPath(), "kinetix-downloads");

	/// <summary>
	/// Tells the user their settings cannot be written, since every change made afterwards would be lost at exit. Once a
	/// run rather than per save, and not while the Settings tab is saving, because that says so itself.
	/// </summary>
	private void OnSettingsSaveFailed(Exception ex)
	{
		_lastSaveFailure = ex;
		if (_saveFailureReported || _savingFromSettingsTab) return;

		_saveFailureReported = true;
		_ui.Post(() =>
		{
			_sound.Play("error");
			Say(Loc.T("settings.saveFailedSpeak", ex.Message));
		});
	}

	public void Present()
	{
		_window.Present();

		// A damaged settings file costs keys and folders. Starting over in silence reads as losing everything for no reason.
		string? settingsProblem = _settings.LoadProblem switch
		{
			SettingsLoadProblem.RestoredFromBackup => Loc.T("settings.loadRestored"),
			SettingsLoadProblem.ResetToDefaults => Loc.T("settings.loadReset"),
			_ => null
		};
		if (settingsProblem != null)
		{
			_sound.Play("error");
			Say(settingsProblem);
		}

		// Said rather than shown. The window title is announced by Orca on focus, but the count of what was
		// found is the thing the user actually opened the program to learn.
		// The view's own sentence, which names the game and says what is actually true — the spike said
		// "Minecraft" here whatever was loaded, and said a count even when the game was not installed.
		Say(_view.Announcement + " " + Loc.T("gtk.f6Hint"));
	}

	// -------------------------------------------------------------------------
	// Installed
	// -------------------------------------------------------------------------

	/// <summary>
	/// Every supported game, whether or not it is installed, with what was found for each.
	///
	/// Uninstalled games are listed rather than hidden, and say so. A blind user who cannot see an empty
	/// list has no way to tell "this game is not supported" from "I have not installed it yet" — and the
	/// first is a reason to give up on the program while the second is not.
	/// </summary>
	private Gtk.Widget BuildGamesTab()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 6);

		_games.SetVexpand(true);
		_games.OnRowActivated += (_, args) =>
		{
			int i = args.Row?.GetIndex() ?? -1;
			if (i >= 0 && i < _gameList.Count) SelectGame(_gameList[i]);
		};

		// Built in the core so the sentence — including the note about a game that will not speak here — is
		// decided once and can be tested. warnWhereItWillNotSpeak is true because this is the Linux head;
		// on Windows every supported game speaks and the note would be noise on all six rows.
		IReadOnlyList<GameRow> rows = GamesView.Of(GameProfiles.All, _locator.InstallFolder, warnWhereItWillNotSpeak: true);

		foreach (GameRow row in rows)
		{
			_gameList.Add(row.Game);
			_games.Append(RowLabel(row.Spoken));
		}

		SetStatus(GamesView.Summarise(rows));

		var scroller = Gtk.ScrolledWindow.New();
		scroller.SetChild(_games);
		scroller.SetVexpand(true);
		box.Append(scroller);

		var hint = Gtk.Label.New(Loc.T("gtk.gamesHint"));
		hint.SetXalign(0);
		box.Append(hint);
		return box;
	}

	/// <summary>Makes <paramref name="game"/> the active one and loads its mods.</summary>
	private void SelectGame(GameProfile game)
	{
		if (RefusedWhileChanging()) return;

		_armedForDelete = null;
		_armedForRestore = null;
		_armedForProfile = null;
		_closeArmed = false;

		_game = game;
		_settings.ActiveGame = game.Id;

		// Every tab holds the old game's rows until told otherwise, and each acts on the current game's folders. So the
		// old game's backup was unpacked into the new game, its update installed there, and Save on the Settings tab
		// pointed the new game at the old game's mods folder. Status last, so what stays on the line is about the mods.
		// Unselected first, or each list would put its selection back at the same place in the new game's rows.
		_installed.UnselectAll();
		_backups.UnselectAll();
		_profiles.UnselectAll();
		ForgetTheOtherGamesRows();
		LoadBackups(announce: false);
		FillSettingsForGame();
		LoadInstalled();

		_tabs.SetCurrentPage(InstalledTabIndex);
		_installed.GrabFocus();
		Say(_view.Announcement, interrupt: true);

		// Remembered straight away rather than on exit: a manager that is closed by the window button, or by
		// the session ending, should still open on the game you were using. After the sentence above, which interrupts:
		// a failure said before it would be cut off.
		_settings.Save();
	}

	/// <summary>Empties the lists that only fill when asked, so nothing of the last game's can be acted on in this one.</summary>
	private void ForgetTheOtherGamesRows()
	{
		_found.Clear();
		ClearRows(_results);

		// Two more things that are about the last game, and that the user would otherwise read as being about this one.
		_search.GetBuffer().SetText("", 0);
		_web?.Load(BlankPage);

		_updatesView = ModUpdatesView.CannotCheck(null, "");
		ClearRows(_updates);

		_dependenciesView = DependenciesView.Of(Array.Empty<GameMod>());
		ClearRows(_dependencies);

		_logView = GameLogView.NoLog();
		ClearRows(_log);

		_checkResult.SetText(Loc.T("gtk.checkPrompt"));
	}

	private static void ClearRows(Gtk.ListBox list)
	{
		while (list.GetFirstChild() is { } child) list.Remove(child);
	}

	/// <summary>
	/// Replaces a list's rows, keeping the user's place in it.
	///
	/// Rebuilding a list takes away the row that had focus. Handed back, or the next key would land nowhere and the list's
	/// own keys would stop working until someone moved back into it. A list shorter than it was keeps them at its last row.
	/// </summary>
	private static void RefillRows(Gtk.ListBox list, IEnumerable<string> rows)
	{
		int place = list.GetSelectedRow()?.GetIndex() ?? -1;
		bool hadFocus = FocusIsIn(list);

		ClearRows(list);

		int count = 0;
		foreach (string row in rows)
		{
			list.Append(RowLabel(row));
			count++;
		}

		if (place >= 0 && count > 0)
		{
			Gtk.ListBoxRow? again = list.GetRowAtIndex(Math.Min(place, count - 1));
			list.SelectRow(again);
			if (hadFocus) again?.GrabFocus();
		}
		else if (hadFocus)
		{
			list.GrabFocus();
		}
	}

	private Gtk.Widget BuildInstalledTab()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 6);

		var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
		var toggle = Gtk.Button.NewWithLabel(Loc.T("gtk.toggleButton"));
		toggle.OnClicked += (_, _) => ToggleSelected();
		var refresh = Gtk.Button.NewWithLabel(Loc.T("gtk.refreshButton"));
		refresh.OnClicked += (_, _) => { LoadInstalled(); Say(_view.Announcement); };
		buttons.Append(toggle);
		buttons.Append(refresh);
		box.Append(buttons);

		_installed.SetVexpand(true);
		// Moving to another mod calls off an armed delete too — the mod being confirmed is no longer the one
		// under the cursor, and going ahead would remove something the user never pointed at.
		_installed.OnRowSelected += (_, _) => CancelArmedDelete();
		// Nothing is announced on selection, deliberately. Orca reads the row that takes focus already, and
		// the row is a single label carrying the whole sentence precisely so that what it reads is the
		// right thing. Saying it again here would be the same sentence twice. The Windows build has to
		// announce it, because Tolk gives it the reader's queue; on Linux the reader is already doing it.

		var scroller = Gtk.ScrolledWindow.New();
		scroller.SetChild(_installed);
		scroller.SetVexpand(true);
		box.Append(scroller);
		return box;
	}

	/// <summary>
	/// The mods the last scan found, for what wants them without reading the folder again. Empty when the scan failed or
	/// there was nothing to scan.
	/// </summary>
	private List<GameMod> _scanned = new();

	private void LoadInstalled()
	{
		ReadInstalledFolder();

		RefillRows(_installed, _view.Rows.Select(row => row.Spoken));
		SetStatus(_view.StatusLine);

		// What a profile would change, and which is the current one, both depend on what is on disk.
		LoadProfiles(setStatus: false);
	}

	/// <summary>Reads the folder into <see cref="_view"/>. Touches no widgets.</summary>
	private void ReadInstalledFolder()
	{
		_scanned = new List<GameMod>();
		_modsFolder = ModsFolderFor(_game);

		// Three different answers, and they must not sound alike. A game that is not here at all is a
		// reason to go and install one; a game that is here with no mods folder yet is a reason to install
		// a mod; and a game with an empty folder is neither. Announcing all three as "0 mods" is how a
		// blind user concludes the manager does not support their game.
		if (string.IsNullOrEmpty(_modsFolder))
		{
			_view = InstalledModsView.NotInstalled(_game);
			return;
		}

		if (!Directory.Exists(_modsFolder))
		{
			_view = InstalledModsView.NoModsFolder(_game, _modsFolder);
			return;
		}

		// The same scan the WinForms build runs, not a second one written for this window. It was private to
		// the app until ModScanner moved to the core; until then this had to enumerate jars by hand and would
		// have drifted from the real thing the first time a rule changed.
		try
		{
			_scanned = ModScanner.ScanMods(
				_modsFolder,
				new Newtonsoft.Json.Linq.JObject(),
				ScanContext.For(_modsFolder),
				_game.Id,
				(where, what) => DiagnosticLog.Write(where, what));

			_view = InstalledModsView.Of(_game, _modsFolder, _scanned);
		}
		catch (Exception ex)
		{
			// A folder that cannot be read is not an empty one. Left uncaught, this also ended the program at startup
			// with nothing said.
			DiagnosticLog.WriteException("Mods", $"reading {_modsFolder}", ex);
			_sound.Play("error");
			_view = InstalledModsView.ScanFailed(_game, _modsFolder, Loc.T("gtk.scanFailed", ex.Message));
		}
	}

	/// <summary>
	/// Says the list could not be read, after an action whose own sentence is about something else. The actions that
	/// announce the list itself do not need this.
	/// </summary>
	private void SayIfListFailed()
	{
		if (_view.Status == InstalledModsStatus.ScanFailed) Say(_view.Announcement);
	}

	/// <summary>The sentence for a refresh: the count only when there was a folder to count.</summary>
	private string RefreshedSentence() =>
		_view.Status == InstalledModsStatus.Ready ? Loc.T("gtk.refreshed", _view.Rows.Count) : _view.Announcement;

	private void ToggleSelected()
	{
		int i = _installed.GetSelectedRow()?.GetIndex() ?? -1;
		if (i < 0 || i >= _view.Rows.Count) { Say(Loc.T("gtk.noModSelected"), interrupt: true); return; }

		if (RefusedWhileChanging()) return;

		InstalledModRow row = _view.Rows[i];
		if (RefusedAsTheWholeModsFolder(row)) return;

		// The Witcher 3's files outside the mod, which can stay behind even though the folder itself moved.
		var stuck = new List<string>();

		try
		{
			// Every layout switches a mod off differently - a leading dot for Stardew, a tilde for The Witcher, a
			// move out of plugins for BepInEx, a suffix for Minecraft - and the core is the one place that knows
			// which. Reimplementing any of it here is how the two front ends would start to disagree about what
			// "disabled" means.
			ModOnDisk.SetEnabled(row.Path, !row.Enabled, _game.Id, WitcherGameFolder(), (where, what) => DiagnosticLog.Write(where, what), stuck);
		}
		catch (Exception ex)
		{
			_sound.Play("error");
			DiagnosticLog.WriteException("Mods", $"toggling {row.Path}", ex);

			string failed = Loc.T("gtk.toggleFailed", row.Name, ex.Message);
			if (stuck.Count > 0) failed += " " + Loc.T("gtk.toggleFilesLeftOut", string.Join(", ", stuck));
			Say(failed, interrupt: true);
			return;
		}

		// Past the point where anything can have failed, so a list that will not reload cannot be heard as the switch failing.
		// The cue lands while the reader is still saying the mod's name, which is the whole point of
		// having one: the fact arrives without waiting for the sentence.
		_sound.Play(stuck.Count > 0 ? "error" : row.Enabled ? "disable" : "enable");
		LoadInstalled();

		if (stuck.Count > 0)
			Say(Loc.T(row.Enabled ? "gtk.modDisabledPartly" : "gtk.modEnabledPartly", row.Name, string.Join(", ", stuck)), interrupt: true);
		else
			Say(Loc.T(row.Enabled ? "gtk.modDisabled" : "gtk.modEnabled", row.Name), interrupt: true);

		SayIfListFailed();
	}

	// -------------------------------------------------------------------------
	// Discover
	// -------------------------------------------------------------------------

	private Gtk.Widget BuildDiscoverTab()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 6);

		var bar = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
		// A visible label bound to the entry: that is what gives the entry an accessible name, and it is
		// why this is a Label with a mnemonic rather than placeholder text, which Orca does not treat as one.
		var caption = Gtk.Label.NewWithMnemonic(Loc.T("gtk.searchCaption"));
		caption.SetMnemonicWidget(_search);
		_search.SetHexpand(true);
		_search.OnActivate += (_, _) => _ = SearchAsync();
		var go = Gtk.Button.NewWithLabel(Loc.T("gtk.searchButton"));
		go.OnClicked += (_, _) => _ = SearchAsync();
		var install = Gtk.Button.NewWithLabel(Loc.T("gtk.installButton"));
		install.OnClicked += (_, _) => _ = InstallSelectedAsync();
		bar.Append(caption); bar.Append(_search); bar.Append(go); bar.Append(install);
		box.Append(bar);

		_results.SetVexpand(true);
		// Enter on a result opens that mod's page in the manager's own browser and puts focus in it. This is
		// the flow a search is actually for - find something, then read about it - and it was missing, which
		// is why searching appeared to lead nowhere: the web view existed but nothing reached it.
		_results.OnRowActivated += (_, args) =>
		{
			int i = args.Row?.GetIndex() ?? -1;
			if (i >= 0 && i < _found.Count) OpenModPage(_found[i]);
		};

		var scroller = Gtk.ScrolledWindow.New();
		scroller.SetChild(_results);
		scroller.SetVexpand(true);
		box.Append(scroller);
		return box;
	}

	/// <summary>
	/// The game's wiki, inside the window rather than in a browser.
	///
	/// The whole question this tab exists to answer is whether Orca reads a WebKitGTK page the way NVDA reads
	/// a WebView2 one — headings, links, and its own navigation keys — while the manager's keys still work
	/// around it. If it does, the wiki, the walkthroughs and the mod descriptions all follow the same path.
	/// </summary>
	private Gtk.Widget BuildWikiTab()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 6);

		var bar = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
		var open = Gtk.Button.NewWithLabel(Loc.T("gtk.openWikiButton"));
		open.OnClicked += (_, _) => OpenWiki();
		var back = Gtk.Button.NewWithLabel(Loc.T("gtk.backButton"));
		// Not past the blank page a change of game leaves, because behind that is the last game's wiki.
		back.OnClicked += (_, _) => { if (_web?.CanGoBack == true && _web.Uri != BlankPage) _web.GoBack(); };
		var login = Gtk.Button.NewWithLabel(Loc.T("gtk.nexusLoginButton"));
		login.OnClicked += (_, _) => _ = SignInToNexusAsync();
		bar.Append(open);
		bar.Append(back);
		bar.Append(login);
		box.Append(bar);

		try
		{
			_web = new WebKitView();
			_web.Widget.SetVexpand(true);
			// Explicit, because F6 hands focus here directly and a widget that cannot take it silently
			// swallows the key — which reads, from the outside, exactly like the web view not working.
			_web.Widget.SetCanFocus(true);
			_web.Widget.SetFocusable(true);
			box.Append(_web.Widget);
		}
		catch (Exception ex)
		{
			// A missing or mismatched WebKitGTK is worth saying out loud rather than showing an empty tab:
			// webkit2gtk-4.1 is the GTK3 build and will not embed here, and that is an easy mistake to make.
			DiagnosticLog.WriteException("Web", "creating the web view", ex);
			var problem = Gtk.Label.New(Loc.T("gtk.noBrowser"));
			problem.SetWrap(true);
			box.Append(problem);
		}

		return box;
	}

	/// <summary>What the web view shows after a change of game.</summary>
	private const string BlankPage = "about:blank";

	private void OpenWiki()
	{
		if (_web is null) { Say(Loc.T("gtk.browserUnavailable"), interrupt: true); return; }

		string url = _game.WikiArticleBase;
		if (string.IsNullOrWhiteSpace(url)) { Say(Loc.T("gtk.noWiki", _game.DisplayName), interrupt: true); return; }

		_web.Load(url);
		SetStatus(Loc.T("gtk.loadingUrl", url));
		Say(Loc.T("gtk.openingWiki", _game.DisplayName));
	}

	/// <summary>
	/// Signs in to Nexus, with the approval page shown inside this window rather than in a browser.
	///
	/// The point is what the user is spared. Today they leave the manager, find the right page on
	/// nexusmods.com, pick a long random string out of the page furniture and paste it back — which is
	/// unpleasant sighted and genuinely hostile by ear. Nexus's single sign-on exists so an application
	/// never has to ask: the user approves it on a Nexus page and the key arrives over a websocket.
	///
	/// The page is their own session with Nexus. It is shown here, and nothing reads what they type into
	/// it — the whole value of this flow is that the manager receives a revocable key and never a password.
	/// </summary>
	/// <summary>
	/// Answers the question the whole program exists for: did my mods actually load last time?
	///
	/// An unmodded game and a modded one that failed are indistinguishable to a player who cannot see the
	/// screen — both start, both play, and one of them simply never speaks. The log is the only evidence,
	/// and reading it by hand is not a reasonable thing to ask of someone whose reason for wanting the mod
	/// is that they cannot read the screen.
	///
	/// It says so even when everything is fine. An all-clear that reports nothing leaves the user no better
	/// off than before they asked.
	/// </summary>
	private Gtk.Widget BuildCheckTab()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 6);

		var check = Gtk.Button.NewWithLabel(Loc.T("gtk.checkButton"));
		check.OnClicked += (_, _) => RunSetupCheck();
		box.Append(check);

		_checkResult.SetXalign(0);
		_checkResult.SetWrap(true);
		_checkResult.SetSelectable(true);   // selectable, so a reader can walk the text rather than only hear it once
		_checkResult.SetVexpand(true);
		_checkResult.SetValign(Gtk.Align.Start);
		box.Append(_checkResult);
		return box;
	}

	private void RunSetupCheck()
	{
		string report = DescribeSetup();
		_checkResult.SetText(report);
		// Spoken as well as shown: a label changing is silent to a screen reader, and this is the answer
		// the user pressed the button for.
		Say(report, interrupt: true);
	}

	private string DescribeSetup()
	{
		string mods = ModsFolderFor(_game);
		// The scan failing is included, or the sentences below would count the empty list it left and report no mods.
		if (string.IsNullOrEmpty(mods) || !Directory.Exists(mods) || _view.Status == InstalledModsStatus.ScanFailed)
			return _view.Announcement;

		if (!_game.IsMinecraft)
		{
			// The per-game launch checks live in the WinForms app still. Saying what IS known beats an
			// all-purpose "everything looks fine" that was never checked.
			string log = GameLogFiles.LoaderLogPath(_game, _locator.InstallFolder(_game) ?? "");
			if (string.IsNullOrEmpty(log))
				return Loc.T("gtk.checkNoLoaderLog", _view.Rows.Count, _game.DisplayName);

			return File.Exists(log)
				? Loc.T("gtk.checkLoaderLogAt", _view.Rows.Count, _game.DisplayName, log)
				: Loc.T("gtk.checkNoLogYet", _view.Rows.Count, _game.DisplayName);
		}

		string root = _locator.InstallFolder(_game) ?? MinecraftLayout.DefaultRootFolder;
		MinecraftLaunchOutcome outcome = MinecraftLaunchLog.ReadLatest(root);

		if (outcome.NoLog)
			return Loc.T("gtk.checkNotRunYet", _view.Rows.Count);

		if (!outcome.FabricLoaded)
			return Loc.T("gtk.checkRanVanilla");

		string version = string.IsNullOrEmpty(outcome.GameVersion) ? "" : Loc.T("gtk.checkOnVersion", outcome.GameVersion);
		return Loc.T("gtk.checkAllGood", version, outcome.ModCount, _view.Rows.Count);
	}

	/// <summary>
	/// Installs the selected search result.
	///
	/// Judged by what the result's catalogue can hand over rather than by which game is loaded — the same
	/// question the Windows head asks. Today that means a Modrinth result, because the folder-shaped layouts
	/// still go through the archive pipeline in the WinForms app; claiming otherwise here would install
	/// nothing and say it had worked. A result the manager cannot fetch opens its page instead, which is the
	/// one thing every catalogue can do.
	/// </summary>
	private async Task InstallSelectedAsync()
	{
		int i = _results.GetSelectedRow()?.GetIndex() ?? -1;
		if (i < 0 || i >= _found.Count) { Say(Loc.T("gtk.noModSelected"), interrupt: true); return; }

		if (RefusedWhileChanging()) return;

		GameMod mod = _found[i];

		if (string.IsNullOrEmpty(mod.ModrinthId))
		{
			Say(Loc.T("gtk.installNotFromHere", mod.Name), interrupt: true);
			OpenModPage(mod);
			return;
		}

		string mods = ModsFolderFor(_game);
		if (string.IsNullOrEmpty(mods)) { Say(Loc.T("gtk.noModsFolder"), interrupt: true); return; }

		string version = GameVersionInUse();
		if (version.Length == 0)
		{
			// Modrinth cannot be asked without one, and guessing would install a mod built for another
			// version — which installs perfectly and then loads nothing at all.
			Say(Loc.T("gtk.noGameVersion"), interrupt: true);
			return;
		}

		using IDisposable busy = _modOperations.Begin();

		SetStatus(Loc.T("gtk.installingStatus", mod.Name));
		Say(Loc.T("gtk.installing", mod.Name));

		try
		{
			string backups = BackupsFolder;
			int keep = _settings.MaxBackupsPerMod;

			// Off the interface's thread, for the zipping and copying that come after the download. What it replaces is
			// backed up first and a failed backup stops the install, or a bad version could not be undone from this head.
			ModInstaller.InstallResult? result = await Task.Run(() => ModInstaller.InstallFromModrinthAsync(
				mod.ModrinthId ?? "", version, mods, InstallDownloadsFolder, backups, keep));

			_ui.Post(() =>
			{
				if (result is null)
				{
					// A normal answer rather than a failure, and one the user has to hear plainly: a mod
					// built for another Minecraft version installs perfectly and then loads nothing at all.
					SetStatus(Loc.T("gtk.noBuildStatus", mod.Name, version));
					Say(Loc.T("gtk.noBuild", mod.Name, version), interrupt: true);
					return;
				}

				LoadBackups(announce: false);
				LoadInstalled();

				// Not "installed": an older copy would not go, and Fabric will not start with two. The new copy was taken back
				// out, unless it wrote over a file of the same name, when it stays beside the stuck one. So the sentence names
				// what is stuck and says nothing about where the new one is.
				if (result.Value.CouldNotRemove.Count > 0)
				{
					_sound.Play("error");
					SetStatus(Loc.T("gtk.installFailedStatus"));
					Say(Loc.T("mc.install.oldCopyStuck", string.Join(", ", result.Value.CouldNotRemove)), interrupt: true);
					SayIfListFailed();
					return;
				}

				string replaced = result.Value.WasUpgrade
					? " " + Loc.T("gtk.replacedOlder", result.Value.Replaced.Count) : "";
				SetStatus(Loc.T("gtk.installedStatus", mod.Name));
				_sound.Play("load_complete");
				Say(Loc.T("gtk.installed", mod.Name, replaced, _view.Rows.Count), interrupt: true);
				SayIfListFailed();
			});
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Install", $"installing {mod.Name}", ex);
			_ui.Post(() =>
			{
				_sound.Play("error");
				SetStatus(Loc.T("gtk.installFailedStatus"));
				Say(Loc.T("gtk.installFailed", mod.Name, ex.Message), interrupt: true);
			});
		}
	}

	/// <summary>Shows a mod's own page in the in-app browser, and moves focus there to read it.</summary>
	private void OpenModPage(GameMod mod)
	{
		if (_web is null) { Say(Loc.T("gtk.browserUnavailable"), interrupt: true); return; }

		string id = mod.ModrinthId ?? "";
		if (id.Length == 0) { Say(Loc.T("gtk.noPageFor", mod.Name), interrupt: true); return; }

		_web.Load($"https://modrinth.com/mod/{id}");
		SetStatus(Loc.T("gtk.readingAbout", mod.Name));

		// Switching tab and moving focus together, because doing only the first leaves the user on a page
		// they cannot get into, which is precisely the gap this came from.
		_tabs.SetCurrentPage(WikiTabIndex);
		_web.Widget.GrabFocus();
		Say(Loc.T("gtk.openingPage", mod.Name));
	}

	private async Task SignInToNexusAsync()
	{
		if (_web is null) { Say(Loc.T("gtk.noBrowserForSignIn"), interrupt: true); return; }

		if (string.IsNullOrEmpty(NexusApplicationSlug))
		{
			// Said plainly rather than failing quietly. Nexus only permits single sign-on for applications
			// they have approved, and approval is what supplies this slug — a conversation with their
			// community managers, not something the code can arrange.
			SetStatus(Loc.T("gtk.nexusNotSetUpStatus"));
			Say(Loc.T("gtk.nexusNotSetUp"), interrupt: true);
			return;
		}

		SetStatus(Loc.T("gtk.signingIn"));
		Say(Loc.T("gtk.openingSignIn"));

		try
		{
			string key = await NexusSso.SignInAsync(
				NexusApplicationSlug,
				showApprovalPage: url => _ui.Post(() => _web.Load(url)));

			_ui.Post(() =>
			{
				SetStatus(Loc.T("gtk.signedInStatus"));
				// The key itself is never spoken or shown. It is a credential, and reading one aloud in a
				// room is its own kind of leak.
				Say(Loc.T("gtk.signedIn", key.Length), interrupt: true);
			});
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Nexus", "signing in", ex);
			_ui.Post(() => { SetStatus(Loc.T("gtk.signInFailedStatus")); Say(Loc.T("gtk.signInFailed", ex.Message), interrupt: true); });
		}
	}

	/// <summary>
	/// The name Nexus knows this application by, issued when they approve it. Empty until then, and the
	/// sign-in button says so rather than failing in a way nobody could act on.
	/// </summary>
	private const string NexusApplicationSlug = "";

	private async Task SearchAsync()
	{
		string term = _search.GetBuffer().GetText().Trim();
		if (string.IsNullOrWhiteSpace(term)) { Say(Loc.T("gtk.searchEmpty"), interrupt: true); return; }

		// Whichever catalogues the user has chosen for this game, decided the same way the Windows head
		// decides it. This used to call Modrinth directly, which is why searching read as Minecraft-only —
		// not because the other games have nowhere to search, but because this window knew one place.
		IReadOnlyList<IModSource> asking = ModSearchPlan.Choose(
			_modSources, _game.Id, _settings.ModSearchMode, _settings.PreferredModSourceFor(_game.Id));

		if (asking.Count == 0)
		{
			// Every supported game has a catalogue now, so this is the "no game loaded" case rather than a
			// missing front end. Said rather than shown as an empty list: a blind user cannot tell an empty
			// result from a search that never happened.
			SetStatus(Loc.T("search.noSourceForGame", _game.DisplayName));
			Say(Loc.T("search.noSourceForGame", _game.DisplayName), interrupt: true);
			return;
		}

		SetStatus(Loc.T("gtk.searchingStatus", string.Join(", ", asking.Select(s => s.Info.DisplayName)), term));
		Say(Loc.T("gtk.searching", term));

		string gameId = _game.Id;

		try
		{
			var query = new ModSearchQuery(gameId, term, 1, 25) { GameVersion = GameVersionInUse() };
			ModSearchResults found = await ModSearchPlan.SearchAsync(asking, query);

			_ui.Post(() =>
			{
				// The user has gone to another game while this ran. These are not that game's results, and Install would
				// put one into the wrong folder.
				if (_game.Id != gameId) return;

				_found.Clear(); _found.AddRange(found.Results);
				ClearRows(_results);
				foreach (GameMod m in _found) _results.Append(RowLabel(Trim(m.ToString())));

				// "0 results" straight after "Nexus did not answer" would take back what the note just said. Nobody having
				// answered is the only failure here, and the only time the error cue is true.
				if (found.NobodyAnswered(asking.Count))
				{
					foreach (string note in found.Notes) Say(note);
					_sound.Play("error");
					SetStatus(found.Notes[0]);
					return;
				}

				// Someone answered, and with nothing, which is a real answer the user asked for. It comes first, because "CurseForge
				// could not be reached" on its own left them not knowing Modrinth had been asked at all.
				if (_found.Count == 0 && found.Notes.Count > 0)
				{
					string nothing = Loc.T("gtk.resultsNoneFromTheRest", term);
					SetStatus(nothing);
					Say(nothing);
					foreach (string note in found.Notes) Say(note);
					return;
				}

				// A catalogue that could not answer says so out loud rather than leaving a quiet gap.
				foreach (string note in found.Notes) Say(note);

				SetStatus(Loc.T("gtk.resultsStatus", _found.Count, found.Total, term));
				Say(Loc.T("gtk.results", _found.Count));
			});
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Search", $"searching for {term}", ex);
			_ui.Post(() =>
			{
				if (_game.Id != gameId) return;

				_sound.Play("error");
				SetStatus(Loc.T("gtk.searchFailedStatus"));
				Say(Loc.T("gtk.searchFailed", ex.Message), interrupt: true);
			});
		}
	}

	// -------------------------------------------------------------------------
	// Shared
	// -------------------------------------------------------------------------

	/// <summary>
	/// Where <paramref name="game"/> keeps its mods, or empty when it cannot be found.
	///
	/// Three shapes, and the profile says which: Minecraft keeps its mods beside its launcher rather than in
	/// a game folder; the Bethesda games stage theirs in a manager-owned folder outside the game entirely;
	/// everything else keeps them under the install.
	/// </summary>
	private string ModsFolderFor(GameProfile game)
	{
		// A folder the user has pointed at wins over anything detected. It is the only way to manage a game
		// the locator cannot find — a GOG copy, a Heroic install, a library on a disk Steam does not know
		// about — and until settings reached this head there was no way to say so.
		if (_settings.GameModsPaths.TryGetValue(game.Id, out string? chosen) &&
			!string.IsNullOrWhiteSpace(chosen))
			return chosen;

		if (game.IsMinecraft)
		{
			string root = _locator.InstallFolder(game) ?? MinecraftLayout.DefaultRootFolder;
			return MinecraftLayout.ModsFolderFor(root);
		}

		string? install = _locator.InstallFolder(game);
		if (string.IsNullOrEmpty(install)) return "";

		if (!string.IsNullOrEmpty(game.StagingFolderName))
		{
			// Staged mods live beside the manager's own data, not in the game, so that a game update cannot
			// take them with it.
			string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			return Path.Combine(appData, "AudiVentureGames", "KinetixModManager", game.StagingFolderName);
		}

		return game.ModsFolderFor(install);
	}

	/// <summary>A row that is exactly one label, so a screen reader reads one sentence per arrow press.</summary>
	private static Gtk.Widget RowLabel(string text)
	{
		var label = Gtk.Label.New(text);
		label.SetXalign(0);
		label.SetMarginTop(4); label.SetMarginBottom(4);
		label.SetMarginStart(6); label.SetMarginEnd(6);
		label.SetEllipsize(Pango.EllipsizeMode.End);
		return label;
	}

	private static string Trim(string s) =>
		string.IsNullOrEmpty(s) ? "" : (s.Length <= 140 ? s : s.Substring(0, 140) + "…");

	private void SetStatus(string text) => _status.SetText(text);

	private void Say(string text, bool interrupt = false) => _announcer.Speak(text, interrupt);

	/// <summary>
	/// The keys the Windows build uses, kept the same on purpose: someone who has learned one should not have
	/// to learn the other. F6 cycles focus, F5 refreshes, Ctrl+F goes to the search box, Space toggles a mod.
	/// </summary>
	private void AddKeyboardShortcuts()
	{
		// First in line for every key, wherever the focus is, which is how a release is seen after the press moved the focus.
		var heldKeys = Gtk.EventControllerKey.New();
		heldKeys.SetPropagationPhase(Gtk.PropagationPhase.Capture);
		heldKeys.OnKeyPressed += (controller, args) =>
		{
			// Not counted as another key, or tapping Shift during a held Delete would make its next repeat look like a new press.
			if (IsModifierKey(args.Keyval))
			{
				_keyIsHeld = false;
				return false;
			}

			// By its place on the keyboard rather than its letter. Press Shift+A and let go of Shift first, and A's release
			// arrives as a different letter from its press.
			_keyIsHeld = !_keysDown.IsFirstPress((int)args.Keycode);
			_lastKeycode = args.Keycode;

			// What is waiting for a second press is waiting for a deliberate one, and this is also what covers the buttons:
			// holding Enter on Restore would otherwise arm it and then confirm it.
			return _keyIsHeld && SomethingIsArmed;
		};
		heldKeys.OnKeyReleased += (controller, args) =>
		{
			// Only that key's release, for the same reason as above: the filter clears on any.
			if (args.Keycode == _lastKeycode) _keysDown.Released();
		};
		_window.AddController(heldKeys);

		// A key let go while another window has the focus is never reported here.
		var windowFocus = Gtk.EventControllerFocus.New();
		windowFocus.OnLeave += (controller, args) => _keysDown.FocusLost();
		_window.AddController(windowFocus);

		var keys = Gtk.EventControllerKey.New();
		keys.OnKeyPressed += (controller, args) =>
		{
			bool ctrl = (args.State & Gdk.ModifierType.ControlMask) != 0;

			// Lowered because Caps Lock turns Ctrl+B into an uppercase keyval, which no case below would match.
			uint key = Gdk.Functions.KeyvalToLower(args.Keyval);

			// A modifier on its own is the start of a key, not another key. Counting it, the Alt of a second Alt+F4 called
			// off the close it was about to confirm.
			if (IsModifierKey(key)) return false;

			// Here rather than on each case below: the point is that ONLY a second press goes ahead, so every other key
			// has to be a way out. A second press never reaches this. It is taken by the button, or by the list's own keys.
			CancelArmed();

			// F6 only moves the focus, so it may run on while held as an arrow key does. The rest do something each time.
			if (key == 0xFFC3)
			{
				CycleFocus();
				return true;
			}

			if (_keyIsHeld) return false;

			switch (key)
			{
				case 0xFFC2:                     // F5
					LoadInstalled();
					Say(RefreshedSentence(), interrupt: true);
					return true;
				// Ctrl+I, the Windows manager's install shortcut. Started and not awaited: a key handler has
				// to answer now, and the install reports itself through the status line and the announcer.
				case 0x069 when ctrl:
					_ = InstallSelectedAsync();
					return true;
				case 0x066 when ctrl:            // Ctrl+F
					_tabs.SetCurrentPage(FindModsTabIndex);
					_search.GrabFocus();
					Say(Loc.T("gtk.searchFocused", _game.DisplayName), interrupt: true);
					return true;
			}

			return false;
		};
		_window.AddController(keys);

		// The list's own keys, on the list and in the capture phase, which runs whenever the focus is on the list or
		// anything in it, and before a row can act on the key.
		//
		// These were on the window, guarded by the list's has-focus. That belongs to the one focus widget, and arrowing
		// a list puts that on the row (gtk_list_box_update_cursor grabs it), so on a row the guard was false and these keys
		// did nothing.
		var listKeys = Gtk.EventControllerKey.New();
		listKeys.SetPropagationPhase(Gtk.PropagationPhase.Capture);
		listKeys.OnKeyPressed += (controller, args) =>
		{
			bool ctrl = (args.State & Gdk.ModifierType.ControlMask) != 0;

			// Lowered because Caps Lock turns Ctrl+B into an uppercase keyval, which no case below would match.
			uint key = Gdk.Functions.KeyvalToLower(args.Keyval);

			// Each of the keys below does something every time, so one still held does nothing and goes no further. It would
			// otherwise answer "press Delete again" by itself, or flip a mod on and off until let go.
			bool oneOfOurs = key == 0xFFFF || (key == 0x020 && !ctrl) || (ctrl && key is 0x062 or 0x06E);
			if (oneOfOurs && _keyIsHeld) return true;

			switch (key)
			{
				case 0xFFFF:                     // Delete — arms, then confirms. See ModActions.
					_armedForRestore = null;
					_armedForProfile = null;
					_closeArmed = false;
					DeletePressed();
					return true;
				case 0x020 when !ctrl:           // Space
					CancelArmed();
					ToggleSelected();
					return true;
				case 0x062 when ctrl:            // Ctrl+B, back up the selected mod
					CancelArmed();
					_ = BackupSelectedAsync();
					return true;
				case 0x06E when ctrl:            // Ctrl+N, read this mod's note
					CancelArmed();
					SpeakNoteForSelected();
					return true;
			}

			// Every other key is a way out too, and has to be called off here. The window's own check is too late for the keys
			// the list keeps for itself: it takes Return, and Up or Home on the first row, so that check never saw them, and
			// Delete, Enter, Delete deleted the mod. Returning false still leaves the key to do its usual work.
			if (!IsModifierKey(key)) CancelArmed();

			return false;
		};
		_installed.AddController(listKeys);

		// The same hole on the two other lists that arm. Their second press comes from a button, so here every key is a way out.
		foreach (Gtk.ListBox list in new[] { _backups, _profiles })
		{
			var anyKey = Gtk.EventControllerKey.New();
			anyKey.SetPropagationPhase(Gtk.PropagationPhase.Capture);
			anyKey.OnKeyPressed += (controller, args) =>
			{
				if (!IsModifierKey(args.Keyval)) CancelArmed();
				return false;
			};
			list.AddController(anyKey);
		}
	}

	/// <summary>Tells a press from a key being held, the same way the Windows head does. See <see cref="KeyRepeatFilter"/>.</summary>
	private readonly KeyRepeatFilter _keysDown = new();

	/// <summary>The key <see cref="_keysDown"/> was last told about.</summary>
	private uint _lastKeycode;

	/// <summary>Whether the key being handled right now was already down. Set first by the window's capture controller.</summary>
	private bool _keyIsHeld;

	/// <summary>Whether anything is waiting for a second press.</summary>
	private bool SomethingIsArmed =>
		_armedForDelete != null || _armedForRestore != null || _armedForProfile != null || _closeArmed;

	/// <summary>
	/// A modifier on its own: Shift, Control, Caps Lock, Alt, Super and the like, and AltGr. Such a key is the start of
	/// another key, so it must not call anything off.
	/// </summary>
	private static bool IsModifierKey(uint keyval) => keyval is (>= 0xFFE1 and <= 0xFFEE) or 0xFE03;

	/// <summary>Calls off whatever is waiting for a second press, saying so.</summary>
	private void CancelArmed()
	{
		CancelArmedDelete();
		CancelArmedRestore();
		CancelArmedProfile();

		if (_closeArmed)
		{
			_closeArmed = false;
			Say(Loc.T("gtk.closeCancelled"), interrupt: true);
		}
	}

	/// <summary>
	/// Whether the keyboard focus is on this widget or anywhere inside it. Not the same as HasFocus, which is true of one
	/// widget only: a list's focus is on a row, and an entry's on the text widget inside it.
	/// </summary>
	private static bool FocusIsIn(Gtk.Widget widget) => (widget.GetStateFlags() & Gtk.StateFlags.FocusWithin) != 0;

	/// <summary>
	/// The things F6 moves between, for the tab that is showing: the tab strip first, then that tab's own
	/// controls in the order someone works through them.
	///
	/// Written as a list per tab rather than as a chain of ifs, because the chain is what broke. It had been
	/// "page 0 or everything else" from when there were two tabs, and adding the Games tab silently shifted
	/// every page number underneath it — so F6 on the Wiki tab was toggling the search box and results list
	/// belonging to a different tab, and the web view could not be reached at all. A list cannot drift like
	/// that: a tab either appears here or it does not. So they are declared where the tab is added, by
	/// <see cref="AddTab"/>, and no page number is written down here for the next inserted tab to shift.
	/// </summary>
	private List<Gtk.Widget> FocusStops()
	{
		var stops = new List<Gtk.Widget> { _tabs };

		int page = _tabs.GetCurrentPage();
		if (page >= 0 && page < _tabStops.Count) stops.AddRange(_tabStops[page]);

		return stops;
	}

	/// <summary>
	/// Moves focus to the next stop, and says nothing about it.
	///
	/// The silence is deliberate: Orca names the widget that receives focus, so announcing it here arrives
	/// as a duplicate — and an assertive one, cutting off the reader's own description to repeat it.
	/// </summary>
	private void CycleFocus()
	{
		List<Gtk.Widget> stops = FocusStops();
		if (stops.Count == 0) return;

		// The last that holds the focus, because the notebook holds everything on its pages. It comes first, so it only
		// answers for focus somewhere this tab does not list, such as a button in the toolbar, and then the next stop is
		// the tab's main control, which is more useful than the strip the user has just come from.
		int current = stops.FindLastIndex(FocusIsIn);
		Gtk.Widget next = stops[(current + 1) % stops.Count];

		// Back to the row left behind, rather than the list as a whole with no row to read.
		if (next is Gtk.ListBox list && list.GetSelectedRow() is { } row) row.GrabFocus();
		else next.GrabFocus();
	}
}
