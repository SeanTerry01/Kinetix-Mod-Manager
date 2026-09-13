using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KinetixModManager;

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
public sealed class MainWindow
{
	private readonly Gtk.ApplicationWindow _window;
	private readonly IAnnouncer _announcer;
	private readonly IDispatcher _ui = new GlibDispatcher();

	private readonly Gtk.Notebook _tabs = Gtk.Notebook.New();
	private readonly Gtk.ListBox _installed = Gtk.ListBox.New();
	private readonly Gtk.ListBox _results = Gtk.ListBox.New();
	private readonly Gtk.Entry _search = Gtk.Entry.New();
	private readonly Gtk.Label _status = Gtk.Label.New("");

	private readonly Gtk.ListBox _games = Gtk.ListBox.New();
	private readonly IGameLocator _locator = new LinuxGameLocator();

	private readonly List<ModRow> _rows = new();
	private readonly List<GameMod> _found = new();
	private readonly List<GameProfile> _gameList = new();
	private WebKitView? _web;

	/// <summary>Named rather than written as 3, because the last time a tab was inserted every number
	/// underneath it shifted and F6 quietly started addressing the wrong controls.</summary>
	private const int WikiTabIndex = 3;

	/// <summary>
	/// The Minecraft version searched and installed for.
	///
	/// Hard-coded, and it should not stay that way: it ought to come from the installed Fabric profile, or
	/// be chosen by the user. Written down here rather than buried in two string literals so that when it is
	/// fixed there is one place to fix.
	/// </summary>
	private const string MinecraftVersion = "1.21.1";

	/// <summary>Where the search box lives. Named for the same reason as <see cref="WikiTabIndex"/>.</summary>
	private const int FindModsTabIndex = 2;

	private readonly Gtk.Label _checkResult = Gtk.Label.New("Press the button to check whether your mods loaded.");

	private GameProfile _game = GameProfiles.Require(GameProfiles.Minecraft);
	private string _modsFolder = "";

	public MainWindow(Gtk.Application app)
	{
		_window = Gtk.ApplicationWindow.New(app);
		// Announcements go to the user's screen reader, with speech-dispatcher only as the fallback for
		// when there is no reader to ask. See OrcaAnnouncer for why that order matters so much.
		_announcer = new OrcaAnnouncer(_window, new SpeechDispatcherAnnouncer());
		_window.SetTitle("Kinetix Mod Manager");
		_window.SetDefaultSize(900, 620);

		var root = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
		root.SetMarginTop(8); root.SetMarginBottom(8);
		root.SetMarginStart(8); root.SetMarginEnd(8);

		_tabs.SetVexpand(true);
		_tabs.AppendPage(BuildGamesTab(), Gtk.Label.New("Games"));
		_tabs.AppendPage(BuildInstalledTab(), Gtk.Label.New("Installed Mods"));
		_tabs.AppendPage(BuildDiscoverTab(), Gtk.Label.New("Find Mods"));
		_tabs.AppendPage(BuildWikiTab(), Gtk.Label.New("Wiki"));
		_tabs.AppendPage(BuildCheckTab(), Gtk.Label.New("Check My Setup"));
		root.Append(_tabs);

		// A status line that is also spoken. On its own a label change is silent to a screen reader — Orca
		// has no reason to look at it — so anything worth putting here is worth saying out loud as well.
		_status.SetXalign(0);
		root.Append(_status);

		_window.SetChild(root);
		AddKeyboardShortcuts();

		LoadInstalled();
	}

	public void Present()
	{
		_window.Present();
		// Said rather than shown. The window title is announced by Orca on focus, but the count of what was
		// found is the thing the user actually opened the program to learn.
		Say($"{_rows.Count} Minecraft mods installed. Press F6 to move between the tabs and the list.");
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

		foreach (GameProfile game in GameProfiles.All)
		{
			_gameList.Add(game);
			string? install = _locator.InstallFolder(game);
			_games.Append(RowLabel(install is null
				? $"{game.DisplayName} — not installed"
				: $"{game.DisplayName} — {install}"));
		}

		var scroller = Gtk.ScrolledWindow.New();
		scroller.SetChild(_games);
		scroller.SetVexpand(true);
		box.Append(scroller);

		var hint = Gtk.Label.New("Press Enter on a game to load its mods.");
		hint.SetXalign(0);
		box.Append(hint);
		return box;
	}

	/// <summary>Makes <paramref name="game"/> the active one and loads its mods.</summary>
	private void SelectGame(GameProfile game)
	{
		_game = game;
		LoadInstalled();
		_tabs.SetCurrentPage(1);
		_installed.GrabFocus();
		Say($"{game.DisplayName}. {_rows.Count} mods installed.", interrupt: true);
	}

	private Gtk.Widget BuildInstalledTab()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 6);

		var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
		var toggle = Gtk.Button.NewWithLabel("Enable or disable (Space)");
		toggle.OnClicked += (_, _) => ToggleSelected();
		var refresh = Gtk.Button.NewWithLabel("Refresh (F5)");
		refresh.OnClicked += (_, _) => { LoadInstalled(); Say($"{_rows.Count} mods installed."); };
		buttons.Append(toggle);
		buttons.Append(refresh);
		box.Append(buttons);

		_installed.SetVexpand(true);
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

	private void LoadInstalled()
	{
		_rows.Clear();
		while (_installed.GetFirstChild() is { } child) _installed.Remove(child);

		_modsFolder = ModsFolderFor(_game);

		if (string.IsNullOrEmpty(_modsFolder) || !Directory.Exists(_modsFolder))
		{
			SetStatus($"{_game.DisplayName} is not installed, or its mods folder has not been created yet.");
			return;
		}

		// The same scan the WinForms build runs, not a second one written for this window. It was private to
		// the app until ModScanner moved to the core; until then this had to enumerate jars by hand and would
		// have drifted from the real thing the first time a rule changed.
		//
		// No removeSuperseded callback is passed, so this scan only reads. On Windows the same call deletes
		// the older of two duplicate installs; here it simply leaves both listed.
		List<GameMod> scanned = ModScanner.ScanMods(
			_modsFolder,
			new Newtonsoft.Json.Linq.JObject(),
			ScanContext.For(_modsFolder),
			_game.Id,
			(where, what) => DiagnosticLog.Write(where, what));

		foreach (GameMod mod in scanned)
			_rows.Add(new ModRow
			{
				JarPath = mod.FolderPath,
				Name    = mod.Name,
				Version = mod.Version,
				Enabled = MinecraftLayout.IsEnabledModFile(mod.FolderPath)
			});

		foreach (ModRow row in _rows) _installed.Append(RowLabel(row.Spoken));
		SetStatus($"{_rows.Count} mods in {_modsFolder}");
	}

	private void ToggleSelected()
	{
		int i = _installed.GetSelectedRow()?.GetIndex() ?? -1;
		if (i < 0 || i >= _rows.Count) { Say("No mod is selected.", interrupt: true); return; }

		ModRow row = _rows[i];
		// Every layout switches a mod off differently - a leading dot for Stardew, a tilde for The Witcher, a
		// move out of plugins for BepInEx, a suffix for Minecraft - and ModEnableState in the core is the one
		// place that knows which. Reimplementing any of it here is how the two front ends would start to
		// disagree about what "disabled" means.
		string target = ModEnableState.TargetPath(row.JarPath, !row.Enabled, _game.Id);

		try
		{
			File.Move(row.JarPath, target);
			LoadInstalled();
			_installed.SelectRow(_installed.GetRowAtIndex(Math.Min(i, Math.Max(0, _rows.Count - 1))));
			Say($"{row.Name} {(row.Enabled ? "disabled" : "enabled")}.", interrupt: true);
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Mods", $"toggling {row.JarPath}", ex);
			Say($"Could not change {row.Name}. {ex.Message}", interrupt: true);
		}
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
		var caption = Gtk.Label.NewWithMnemonic("_Search Modrinth:");
		caption.SetMnemonicWidget(_search);
		_search.SetHexpand(true);
		_search.OnActivate += (_, _) => _ = SearchAsync();
		var go = Gtk.Button.NewWithLabel("Search");
		go.OnClicked += (_, _) => _ = SearchAsync();
		var install = Gtk.Button.NewWithLabel("Install selected (Ctrl+I)");
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
		var open = Gtk.Button.NewWithLabel("Open this game's wiki");
		open.OnClicked += (_, _) => OpenWiki();
		var back = Gtk.Button.NewWithLabel("Back");
		back.OnClicked += (_, _) => { if (_web?.CanGoBack == true) _web.GoBack(); };
		var login = Gtk.Button.NewWithLabel("Log in to Nexus Mods");
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
			var problem = Gtk.Label.New("The in-app browser could not start. WebKitGTK 6.0 (the GTK4 build) is needed.");
			problem.SetWrap(true);
			box.Append(problem);
		}

		return box;
	}

	private void OpenWiki()
	{
		if (_web is null) { Say("The in-app browser is not available.", interrupt: true); return; }

		string url = _game.WikiArticleBase;
		if (string.IsNullOrWhiteSpace(url)) { Say($"{_game.DisplayName} has no wiki configured.", interrupt: true); return; }

		_web.Load(url);
		SetStatus($"Loading {url}");
		Say($"Opening the {_game.DisplayName} wiki. Tab into the page to read it.");
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

		var check = Gtk.Button.NewWithLabel("Check my setup");
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
		if (string.IsNullOrEmpty(mods) || !Directory.Exists(mods))
			return $"{_game.DisplayName} is not installed, or its mods folder has not been created yet.";

		if (!_game.IsMinecraft)
		{
			// The per-game launch checks live in the WinForms app still. Saying what IS known beats an
			// all-purpose "everything looks fine" that was never checked.
			string log = GameLogFiles.LoaderLogPath(_game, _locator.InstallFolder(_game) ?? "");
			if (string.IsNullOrEmpty(log))
				return $"{_rows.Count} mods installed for {_game.DisplayName}. This game keeps no loader log, so whether they loaded cannot be checked from here.";

			return File.Exists(log)
				? $"{_rows.Count} mods installed for {_game.DisplayName}. Its loader log is at {log}."
				: $"{_rows.Count} mods installed for {_game.DisplayName}. No loader log has been written yet, which usually means the game has not been run since the loader was installed.";
		}

		string root = _locator.InstallFolder(_game) ?? MinecraftLayout.DefaultRootFolder;
		MinecraftLaunchOutcome outcome = MinecraftLaunchLog.ReadLatest(root);

		if (outcome.NoLog)
			return $"{_rows.Count} mods installed. Minecraft has not been run yet, so there is nothing to check.";

		if (!outcome.FabricLoaded)
			return "The last time Minecraft ran, it ran WITHOUT your mods. The log shows a plain Minecraft start "
				 + "with no Fabric, which is why the game would have said nothing. Start the game from the manager "
				 + "rather than through the Minecraft launcher: choosing an installation and launching a world are "
				 + "separate things there, and the second quietly overrides the first.";

		string version = string.IsNullOrEmpty(outcome.GameVersion) ? "" : $" on Minecraft {outcome.GameVersion}";
		return $"All good. The last run loaded Fabric{version} with {outcome.ModCount} mods, "
			 + $"and there are {_rows.Count} in your mods folder now.";
	}

	/// <summary>
	/// Installs the selected search result.
	///
	/// Only the file-per-mod games for now, which today means Minecraft. The folder-shaped layouts still go
	/// through the archive pipeline in the WinForms app, and claiming otherwise here would install nothing
	/// and say it had worked.
	/// </summary>
	private async Task InstallSelectedAsync()
	{
		int i = _results.GetSelectedRow()?.GetIndex() ?? -1;
		if (i < 0 || i >= _found.Count) { Say("No mod is selected.", interrupt: true); return; }

		GameMod mod = _found[i];

		if (!_game.IsMinecraft)
		{
			Say($"Installing is only available for Minecraft in this build. {_game.DisplayName} mods still have to be installed from the Windows manager.", interrupt: true);
			return;
		}

		string mods = ModsFolderFor(_game);
		if (string.IsNullOrEmpty(mods)) { Say("The mods folder could not be found.", interrupt: true); return; }

		SetStatus($"Installing {mod.Name}…");
		Say($"Installing {mod.Name}.");

		try
		{
			string downloads = Path.Combine(Path.GetTempPath(), "kinetix-downloads");

			ModInstaller.InstallResult? result = await ModInstaller.InstallFromModrinthAsync(
				mod.ModrinthId ?? "", MinecraftVersion, mods, downloads);

			_ui.Post(() =>
			{
				if (result is null)
				{
					// A normal answer rather than a failure, and one the user has to hear plainly: a mod
					// built for another Minecraft version installs perfectly and then loads nothing at all.
					SetStatus($"No build of {mod.Name} for Minecraft {MinecraftVersion}.");
					Say($"{mod.Name} has no build for Minecraft {MinecraftVersion}, so it was not installed.", interrupt: true);
					return;
				}

				LoadInstalled();
				string replaced = result.Value.WasUpgrade
					? $" It replaced {result.Value.Replaced.Count} older copy." : "";
				SetStatus($"Installed {mod.Name}.");
				Say($"{mod.Name} installed.{replaced} {_rows.Count} mods now installed.", interrupt: true);
			});
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Install", $"installing {mod.Name}", ex);
			_ui.Post(() => { SetStatus("Install failed."); Say($"Installing {mod.Name} failed. {ex.Message}", interrupt: true); });
		}
	}

	/// <summary>Shows a mod's own page in the in-app browser, and moves focus there to read it.</summary>
	private void OpenModPage(GameMod mod)
	{
		if (_web is null) { Say("The in-app browser is not available.", interrupt: true); return; }

		string id = mod.ModrinthId ?? "";
		if (id.Length == 0) { Say($"There is no page for {mod.Name}.", interrupt: true); return; }

		_web.Load($"https://modrinth.com/mod/{id}");
		SetStatus($"Reading about {mod.Name}");

		// Switching tab and moving focus together, because doing only the first leaves the user on a page
		// they cannot get into, which is precisely the gap this came from.
		_tabs.SetCurrentPage(WikiTabIndex);
		_web.Widget.GrabFocus();
		Say($"Opening the page for {mod.Name}.");
	}

	private async Task SignInToNexusAsync()
	{
		if (_web is null) { Say("The in-app browser is not available, so signing in is not possible.", interrupt: true); return; }

		if (string.IsNullOrEmpty(NexusApplicationSlug))
		{
			// Said plainly rather than failing quietly. Nexus only permits single sign-on for applications
			// they have approved, and approval is what supplies this slug — a conversation with their
			// community managers, not something the code can arrange.
			SetStatus("Nexus sign-in is not set up yet.");
			Say("Nexus sign-in is not available yet. The manager has to be registered with Nexus Mods first.", interrupt: true);
			return;
		}

		SetStatus("Signing in to Nexus Mods…");
		Say("Opening the Nexus sign-in page. Approve the manager there, and it will finish by itself.");

		try
		{
			string key = await NexusSso.SignInAsync(
				NexusApplicationSlug,
				showApprovalPage: url => _ui.Post(() => _web.Load(url)));

			_ui.Post(() =>
			{
				SetStatus("Signed in to Nexus Mods.");
				// The key itself is never spoken or shown. It is a credential, and reading one aloud in a
				// room is its own kind of leak.
				Say($"Signed in to Nexus Mods. {key.Length} character key received and stored.", interrupt: true);
			});
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Nexus", "signing in", ex);
			_ui.Post(() => { SetStatus("Sign-in failed."); Say($"Nexus sign-in failed. {ex.Message}", interrupt: true); });
		}
	}

	/// <summary>
	/// The name Nexus knows this application by, issued when they approve it. Empty until then, and the
	/// sign-in button says so rather than failing in a way nobody could act on.
	/// </summary>
	private const string NexusApplicationSlug = "";

	private async Task SearchAsync()
	{
		string term = _search.GetBuffer().GetText();
		if (string.IsNullOrWhiteSpace(term)) { Say("Type something to search for first.", interrupt: true); return; }

		SetStatus($"Searching Modrinth for {term}…");
		Say($"Searching for {term}.");

		try
		{
			// Live, from the core, with no API key — which is the reason Minecraft is the sensible first
			// game for a Linux build. Every other supported game needs a Nexus key.
			var (results, total) = await ModrinthService.SearchAsync(term, MinecraftVersion, 0, 25);

			_ui.Post(() =>
			{
				_found.Clear(); _found.AddRange(results);
				while (_results.GetFirstChild() is { } child) _results.Remove(child);
				foreach (GameMod m in _found) _results.Append(RowLabel($"{m.Name} — {Trim(m.Description)}"));

				SetStatus($"{_found.Count} of {total} results for {term}");
				Say($"{_found.Count} results.");
			});
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Modrinth", $"searching for {term}", ex);
			_ui.Post(() => { SetStatus("Search failed."); Say($"Search failed. {ex.Message}", interrupt: true); });
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
		var keys = Gtk.EventControllerKey.New();
		keys.OnKeyPressed += (controller, args) =>
		{
			bool ctrl = (args.State & Gdk.ModifierType.ControlMask) != 0;

			switch (args.Keyval)
			{
				case 0xFFC3:                     // F6
					CycleFocus();
					return true;
				case 0xFFC2:                     // F5
					LoadInstalled();
					Say($"Refreshed. {_rows.Count} mods installed.", interrupt: true);
					return true;
				// Ctrl+I, the Windows manager's install shortcut. Started and not awaited: a key handler has
				// to answer now, and the install reports itself through the status line and the announcer.
				case 0x069 when ctrl:
					_ = InstallSelectedAsync();
					return true;
				case 0x066 when ctrl:            // Ctrl+F
					_tabs.SetCurrentPage(FindModsTabIndex);
					_search.GrabFocus();
					Say("Search Modrinth.", interrupt: true);
					return true;
				case 0x020 when _installed.HasFocus:   // Space
					ToggleSelected();
					return true;
			}
			return false;
		};
		_window.AddController(keys);
	}

	/// <summary>
	/// The things F6 moves between, for the tab that is showing: the tab strip first, then that tab's own
	/// controls in the order someone works through them.
	///
	/// Written as a list per tab rather than as a chain of ifs, because the chain is what broke. It had been
	/// "page 0 or everything else" from when there were two tabs, and adding the Games tab silently shifted
	/// every page number underneath it — so F6 on the Wiki tab was toggling the search box and results list
	/// belonging to a different tab, and the web view could not be reached at all. A list cannot drift like
	/// that: a tab either appears here or it does not.
	/// </summary>
	private List<Gtk.Widget> FocusStops() => _tabs.GetCurrentPage() switch
	{
		0 => new List<Gtk.Widget> { _tabs, _games },
		1 => new List<Gtk.Widget> { _tabs, _installed },
		2 => new List<Gtk.Widget> { _tabs, _search, _results },
		3 => _web is null
			 ? new List<Gtk.Widget> { _tabs }
			 : new List<Gtk.Widget> { _tabs, _web.Widget },
		4 => new List<Gtk.Widget> { _tabs, _checkResult },
		_ => new List<Gtk.Widget> { _tabs }
	};

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

		int current = stops.FindIndex(w => w.HasFocus);

		// Focus somewhere this tab does not list — a button in the toolbar, or nothing yet. Going to the
		// tab's main control is more useful than going to the strip the user has just come from.
		Gtk.Widget next = current < 0
			? stops[Math.Min(1, stops.Count - 1)]
			: stops[(current + 1) % stops.Count];

		next.GrabFocus();
	}
}
