using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using DavyKager;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewMod = KinetixModManager.GameMod;

namespace KinetixModManager;

public partial class Form1 : Form, IMessageFilter
{
	/// <summary>
	/// Named indices for the main tab control. Use instead of magic numbers throughout
	/// so that reordering tabs produces a compile error rather than a silent bug.
	/// </summary>
	private enum AppTab
	{
		Installed = 0,
		Updates   = 1,
		Backups   = 2,
		Discovery = 3,
		Wiki      = 4,
		Walkthroughs = 5,
		Profiles  = 6,
		SmapiLog  = 7,
		ModPriority = 8,
		PluginOrder = 9,
		Creations = 10,
		GameLog = 11
	}

	/// <summary>
	/// The logical <see cref="AppTab"/> for the currently selected tab, resolved by tab-page reference
	/// rather than physical index. Mod Priority (Skyrim/Fallout 4) and SMAPI Log (Stardew) are added
	/// conditionally and at different positions, so the physical index of a tab is not a reliable id.
	/// </summary>
	private AppTab CurrentTab()
	{
		TabPage? t = mainTabs.SelectedTab;
		if (t == tabUpdates) return AppTab.Updates;
		if (t == tabBackups) return AppTab.Backups;
		if (t == tabDiscovery) return AppTab.Discovery;
		if (t == tabWiki) return AppTab.Wiki;
		if (t == tabWalkthroughs) return AppTab.Walkthroughs;
		if (t == tabProfiles) return AppTab.Profiles;
		if (t == tabModPriority) return AppTab.ModPriority;
		if (t == tabPluginOrder) return AppTab.PluginOrder;
		if (t == tabCreations) return AppTab.Creations;
		if (t == tabGameLog) return AppTab.GameLog;
		if (t == tabSmapiLog) return AppTab.SmapiLog;
		return AppTab.Installed;
	}

	/// <summary>Selects the given logical tab by reference, if that tab is currently present.</summary>
	private void SelectTab(AppTab tab)
	{
		TabPage? page = tab switch
		{
			AppTab.Installed    => tabInstalled,
			AppTab.Updates      => tabUpdates,
			AppTab.Backups      => tabBackups,
			AppTab.Discovery    => tabDiscovery,
			AppTab.Wiki         => tabWiki,
			AppTab.Walkthroughs => tabWalkthroughs,
			AppTab.Profiles     => tabProfiles,
			AppTab.ModPriority  => tabModPriority,
			AppTab.PluginOrder  => tabPluginOrder,
			AppTab.Creations    => tabCreations,
			AppTab.GameLog      => tabGameLog,
			AppTab.SmapiLog     => tabSmapiLog,
			_                   => null
		};
		if (page != null && mainTabs.TabPages.Contains(page)) mainTabs.SelectedTab = page;
	}

	/// <summary>
	/// Handles F6 to cycle keyboard focus between the main tab control and the active tab's primary list.
	/// Delegates to <see cref="HandleCycleFocus"/>.
	/// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F6)
        {
            HandleCycleFocus();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

	/// <summary>Nexus Mods and GitHub HTTP service.</summary>
	private NexusService _nexusService = null!;

	/// <summary>AI provider service for AI-assisted features (opt-in log diagnosis).</summary>
	private AiService _aiService = null!;

	private AppSettings _settings;
	private ToolStripMenuItem _menuGames = null!;

	private static string dataBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudiVentureGames", "KinetixModManager");

	private string downloadsPath => DownloadsPathFor(_settings.ActiveGame);

	/// <summary>
	/// The downloads folder belonging to one copy of one game. Separate from <see cref="downloadsPath"/> because a
	/// download can arrive for a game that is not the loaded one: a mod downloaded from the browser has to be filed
	/// under the game it is <em>for</em>, which is also what puts it in that game's Downloads History, waiting, the
	/// next time the user loads it.
	/// </summary>
	private static string DownloadsPathFor(string installKey)
	{
		string path = Path.Combine(dataBasePath, "downloads", installKey);
		if (!Directory.Exists(path))
		{
			try { Directory.CreateDirectory(path); } catch { }
		}
		return path;
	}

	private string backupsPath
	{
		get
		{
			string path = Path.Combine(dataBasePath, "backups", _settings.ActiveGame);
			if (!Directory.Exists(path))
			{
				try { Directory.CreateDirectory(path); } catch { }
			}
			return path;
		}
	}

	private string profilesPath = Path.Combine(dataBasePath, "profiles");

	private string themesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds");

	private bool isUpdatingAll;

	private CancellationTokenSource _pipeCts = new CancellationTokenSource();

	private int _activeChecks;

	// 0 = no update-check batch running, 1 = one in flight. Guards against overlapping
	// RefreshModList(checkUpdates:true) calls (e.g. a startup check still running when the user
	// triggers a manual one). Two concurrent batches corrupt _activeChecks, which makes the
	// completion cue/announcement repeat and piles duplicate entries into listUpdates. Acquired
	// in RefreshModList; released by the final CheckForUpdates completion (or in RefreshModList
	// itself when no checks end up being launched).
	private int _updateCheckRunning;

	// Counts mod-list refreshes so an older, slower one cannot publish its results over a newer one. A refresh
	// is asynchronous and reads the active game's folder at the start, so switching games mid-refresh left two
	// passes in flight — and whichever finished last won, which was routinely the one for the game the user had
	// just left. Each pass takes a number on entry and checks it still holds the newest before touching the list.
	private int _refreshGeneration;

	// How many mod-list refreshes are running. Used only to answer "is one already going?" when the user's
	// Refresh command arrives twice for one press, which a held key makes routine.
	private int _refreshInFlight;

	/// <summary>True while the goodbye message and disconnect cue are playing, so a second Alt+F4 is ignored.</summary>
	private bool _shuttingDown;

	/// <summary>True once the shutdown sequence has finished and the window may actually close.</summary>
	private bool _readyToClose;

	/// <summary>
	/// Says goodbye, plays the disconnect cue, and only then lets the window close — each step waiting for the
	/// one before it to finish, so nothing is spoken over and nothing is cut off.
	///
	/// Every wait here yields rather than blocks. A blocking wait on the UI thread stops the message loop, and
	/// both halves of this need it running: speech synthesis to produce audio at all, and the sound engine to
	/// report back when the cue has finished.
	/// </summary>
	private async Task RunShutdownSequenceAsync()
	{
		try
		{
			_pipeCts.Cancel();

			// Say goodbye while the window is still up, and wait for it.
			//
			// The window must NOT be hidden first. Hiding it moves focus to whatever is behind, and the screen
			// reader announces that new window — cutting off the message it was already speaking, so the goodbye
			// was never heard at all. Tolk is also unloaded at the end of this method, and unloading it
			// mid-sentence would cut the message off just as surely; hence waiting here rather than later.
			if (Tolk.IsLoaded() && _settings.SpeakShutdownMessage)
			{
				Speak(Loc.T("app.shuttingDown"), interrupt: true);
				await WaitForSpeechAsync(minMs: 1800, maxMs: 6000);
			}

			// Now out of sight, with the message safely delivered.
			//
			// Cancelling the close to keep the message loop alive leaves a window the user has just asked to
			// close still sitting there with focus in it, and a screen reader reads what is focused when a
			// window it expected to go away does not — which is how a mod name or a tab name ended up over the
			// top of the disconnect cue. Hidden, there is nothing to read; a hidden form still pumps messages,
			// which is all the cue below needs.
			Hide();

			// Only when a session is still open: closing one (Ctrl+Shift+C) already plays this, so exiting
			// afterwards would sound the disconnect for a session that was torn down some time ago.
			if (_settings.ActiveGame != "None")
			{
				await _soundEngine.PlayAsync("disconnect");
			}
		}
		catch (Exception ex)
		{
			// Nothing here is worth trapping the user in a window they asked to close.
			LogError("Shutdown", "Shutdown sequence failed: " + ex.Message);
		}
		finally
		{
			try { if (Tolk.IsLoaded()) Tolk.Unload(); } catch { }
			_readyToClose = true;
		}
	}

	private int _currentDiscoveryPage = 1;

	/// <summary>
	/// Page size locked in at the start of the current Discovery search series. Captured on a fresh
	/// search and reused for every "Load more" so the page/offset maths stay aligned even if the user
	/// changes the results-per-load selector mid-session (the change then takes effect on the next search).
	/// </summary>
	private int _currentDiscoveryPageSize = 20;

	/// <summary>The selectable "results per load" values, shared by the Discovery tab and Settings combos.</summary>
	private static readonly int[] DiscoveryPageSizeOptions = { 10, 20, 30, 50, 100 };

	private bool _isLoading;

	// Set while expanding/collapsing a mod group so RebuildInstalledListBox does not speak the selected
	// item itself. The screen reader already reads the group line (which states "Expanded"/"Collapsed")
	// when the selection changes, and List_SelectedIndexChanged announces the position, so the rebuild's
	// own announcement would just repeat the whole group line.

	private bool _isSettingsOpen;

	private List<StardewMod> _allInstalledMods = new List<StardewMod>();

	private List<LogEntry> _fullLogEntries = new List<LogEntry>();

	private HashSet<string> _expandedGroups = new HashSet<string>();

	private string _searchBuffer = "";

	private System.Windows.Forms.Timer _searchTimer = new System.Windows.Forms.Timer
	{
		Interval = 1000
	};

	/// <summary>Audio engine for all app sound playback.</summary>
	private SoundEngine _soundEngine = null!;

	// WinForms controls are assigned in SetupAccessibleUI(), not the constructor.
	// null! suppressions are the accepted WinForms pattern for nullable-enabled projects.
	private TabControl mainTabs = null!;
	private TableLayoutPanel tableLayoutPanel = null!;
	private Panel _gameSelectionPanel = null!;
	private ListBox _lstGames = null!;
	private ToolStripMenuItem _menuFile = null!;
	private ToolStripMenuItem _menuCloseSessionItem = null!;
	private ToolStripSeparator _menuCloseSeparator = null!;
	private TabPage tabInstalled = null!;
	private TabPage tabUpdates = null!;
	private TabPage tabDiscovery = null!;
	private TabPage tabBackups = null!;
	private TabPage tabProfiles = null!;
	private TabPage tabModPriority = null!;
	private TabPage tabPluginOrder = null!;
	private TabPage tabCreations = null!;
	private TabPage tabGameLog = null!;
	private TabPage tabSmapiLog = null!;
	private TabPage tabWiki = null!;
	private TabPage tabWalkthroughs = null!;
	private ListBox listInstalled = null!;
	private ListBox listUpdates = null!;
	private ListBox listDiscovery = null!;
	private ListBox listBackups = null!;
	private ListBox listProfiles = null!;
	private ListBox listModPriority = null!;
	private ListBox listPluginOrder = null!;
	private ListBox listCreations = null!;
	private ListBox listGameLog = null!;
	private ComboBox cmbGameLog = null!;
	private ComboBox cmbGameLogFilter = null!;
	private TextBox txtSearchGameLog = null!;
	/// <summary>All lines of the currently selected game log, before filter/search are applied.</summary>
	private List<string> _gameLogLines = new List<string>();
	private ListBox listLog = null!;
	private TextBox txtSearch = null!;
	private TextBox txtSearchInstalled = null!;
	private TextBox txtSearchLog = null!;
	private ComboBox cmbDiscoveryType = null!;
	private ComboBox cmbDiscoveryLanguage = null!;
	/// <summary>Nexus category filter on the Discovery tab. Session-only: a category is a browsing choice for
	/// the search in front of you, not a standing preference the way the language is.</summary>
	private ComboBox cmbDiscoveryCategory = null!;
	/// <summary>The "Nexus category:" label, hidden and shown with the combo it names.</summary>
	private Label _lblDiscoveryCategory = null!;
	/// <summary>Which game's categories <c>cmbDiscoveryCategory</c> currently holds, so the list is fetched once
	/// per game rather than on every data refresh.</summary>
	private string _discoveryCategoriesGame = "";
	/// <summary>
	/// Set once, at startup, so the first completed mod-list refresh puts the user on the first tab and says so.
	///
	/// A one-shot flag rather than something the refresh always does: every later refresh happens while the user
	/// is somewhere of their own choosing, and taking focus back to the tab strip each time would be intolerable.
	/// </summary>
	private bool _landOnFirstTabAfterStartup;
	// Results-per-load selector on the Discovery tab. Seeded from the saved
	// DiscoverySearchPageSize but its own changes are session-only (not persisted); only the
	// matching combo in Settings persists. See AppSettings.DiscoverySearchPageSize.
	private ComboBox cmbDiscoveryPageSize = null!;
	// Suppresses the language combo's change handler while its list is rebuilt (e.g. on game switch).
	private bool _suppressDiscoveryLanguageEvent;

	/// <summary>
	/// Set while the Installed tab's filter dropdowns are being repopulated from code, so their change events
	/// don't rebuild the mod list. Refilling the Category list raises SelectedIndexChanged — on the Clear and
	/// again on the restored selection — and each one rebuilt the list and announced the focused mod, on top of
	/// the rebuild the refresh does itself. Toggling a mod therefore read the same entry two or three times.
	/// </summary>
	private bool _suppressInstalledFilterEvent;
	private ComboBox cmbLogFilter = null!;
	private ComboBox cmbCategoryFilter = null!;
	private ComboBox cmbStatusFilter = null!;
	private Button btnSearch = null!;
	private Button btnPruneBackups = null!;
	private TextBox txtWikiSearch = null!;
	private ComboBox cmbWikiCategories = null!;
	private ComboBox cmbModWikis = null!;
	// The wiki currently driving Search / Categories / the embedded view. Defaults to the base game wiki.
	private ModWikiLink? _activeWiki;
	// Suppresses the cmbModWikis SelectedIndexChanged handler while the list is rebuilt on game switch.
	private bool _suppressModWikiEvent;
	private ListBox listWikiResults = null!;
	private WebView2 webViewWiki = null!;
	private ListBox listWalkthroughs = null!;
	private WebView2 webViewWalkthrough = null!;
	// Guards so the in-page keyboard handlers (F6 / Ctrl+Home) are only wired up once,
	// since WebView initialisation can be requested from several navigation paths.
	private bool _wikiAccessibilityAttached;
	private bool _walkthroughAccessibilityAttached;
	// Cached one-time WebView2 initialisation task (see EnsureWebViewsInitializedAsync).
	private Task? _webViewInitTask;
	private SplitContainer splitWiki = null!;
	private SplitContainer splitWalkthroughs = null!;
	private Stack<WikiNavigationState> wikiNavStack = new Stack<WikiNavigationState>();
	private IContainer components = null!;

	/// <summary>
	/// Auto-detects the Stardew Valley Mods folder from common Steam installation paths.
	/// Does nothing if <see cref="AppSettings.ModsPath"/> is already set and exists.
	/// </summary>
	private void DetectModsPath()
	{
		if (string.IsNullOrEmpty(_settings.GameModsPaths["StardewValley"]) || !Directory.Exists(_settings.GameModsPaths["StardewValley"]))
		{
			// Prefer the game folder Steam actually reports (any drive / custom library), then fall back
			// to the well-known fixed locations for non-Steam or unusual setups.
			string detectedStardew = DetectInstalledGameFolder("StardewValley");
			string detectedMods = string.IsNullOrEmpty(detectedStardew) ? "" : Path.Combine(detectedStardew, "Mods");
			string[] array = new string[]
			{
				detectedMods,
				"C:\\Program Files (x86)\\Steam\\steamapps\\common\\Stardew Valley\\Mods",
				"C:\\Program Files\\Steam\\steamapps\\common\\Stardew Valley\\Mods",
				"D:\\SteamLibrary\\steamapps\\common\\Stardew Valley\\Mods",
				"E:\\SteamLibrary\\steamapps\\common\\Stardew Valley\\Mods",
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "StardewValley", "Mods")
			};
			foreach (string text in array)
			{
				if (Directory.Exists(text))
				{
					_settings.GameModsPaths["StardewValley"] = text;
					_settings.ModsPath = text;
					_settings.Save();
					break;
				}
			}
		}

		// Register every copy of every game that is actually on disk before working out where their mods go —
		// including a second copy of a game already known about, which is the case this whole pass exists for.
		// Only additional copies are worth mentioning: on a first run everything is new, and reading the whole
		// library back at someone is noise. Announced from the Shown handler, after the welcome.
		_newlyDetectedCopies = RefreshDetectedGameInstalls()
			.Where(key => GameProfiles.BaseId(key) != key)
			.ToList();

		// Every copy of every other game. Stardew is handled above because its mods path is also mirrored into
		// the legacy ModsPath field; the rest fall into the patterns ResolveModsFolder describes.
		foreach (GameInstall install in _settings.GameInstalls)
		{
			// Only Stardew's FIRST copy is handled above (it is the one mirrored into the legacy ModsPath field);
			// a second Stardew copy is an ordinary install like any other and resolves here.
			if (install.Key == GameProfiles.StardewValley) continue;

			string current = _settings.GameModsPaths.TryGetValue(install.Key, out string? existing) ? existing : "";
			if (!string.IsNullOrEmpty(current) && Directory.Exists(current)) continue;

			if (!Directory.Exists(install.Folder)) continue;

			_settings.GamePaths[install.Key] = install.Folder;

			GameProfile profile = GameProfiles.Require(install.GameId);
			string modsFolder = ResolveModsFolder(install);
			_settings.GameModsPaths[install.Key] = modsFolder;

			if (string.IsNullOrEmpty(modsFolder)) continue;

			// A staging folder is the manager's own, so it is created here. So is The Witcher 3's mods folder:
			// the game reads it itself with no loader involved, but a copy that has never been modded doesn't
			// have one yet, and the session would otherwise point at a folder that isn't there. A BepInEx
			// plugins folder is NOT created — it appears when BepInEx is installed, which the manager offers.
			bool createNow = profile.IsBethesda || profile.IsWitcher3;
			if (createNow && !Directory.Exists(modsFolder))
			{
				try { Directory.CreateDirectory(modsFolder); } catch { }
			}
		}

		_settings.Save();
	}

	/// <summary>
	/// Copies of a game found during startup that the manager had not seen before and that are not the game's
	/// first copy — i.e. someone owns that game twice. Announced once, after the welcome message.
	/// </summary>
	private List<string> _newlyDetectedCopies = new List<string>();

	/// <summary>
	/// Tells the user about a second copy of a game that has just turned up, and where it is. Nothing is chosen
	/// for them: both copies are in the Games menu now, each with its own mods, and which one they want is a
	/// question only they can answer.
	/// </summary>
	private void AnnounceNewlyDetectedCopies()
	{
		if (_newlyDetectedCopies.Count == 0) return;

		var lines = new List<string>();
		foreach (string key in _newlyDetectedCopies)
		{
			GameInstall? copy = _settings.InstallFor(key);
			if (copy == null) continue;
			lines.Add(Loc.T("copies.foundLine", copy.DisplayName(withPlatform: true), copy.Folder));
		}
		_newlyDetectedCopies.Clear();
		if (lines.Count == 0) return;

		string message = Loc.T("copies.foundBox", string.Join("\n", lines));
		Speak(Loc.T("copies.foundSpeak", lines.Count));
		SpeakBox(message, Loc.T("copies.foundTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
	}

	/// <summary>The folder a copy's mods are staged in when they live inside the game rather than under %AppData%.</summary>
	private const string InGameModsFolderName = "KinetixMods";

	/// <summary>
	/// Where <paramref name="install"/>'s mods live. The single answer to that question, so the setup pass, the
	/// Settings checkbox and the move that follows it cannot disagree about it.
	///
	/// Three shapes: a game that stages its mods outside itself keeps them either in the game folder or under
	/// <c>%AppData%</c>, the copy's own choice; every other game keeps them wherever its loader looks, which is
	/// inside the install and not a choice at all.
	/// </summary>
	private string ResolveModsFolder(GameInstall install)
	{
		GameProfile profile = GameProfiles.Require(install.GameId);

		if (string.IsNullOrEmpty(profile.StagingFolderName))
			return profile.ModsFolderFor(install.Folder);

		if (install.ModsInGameFolder && !string.IsNullOrEmpty(install.Folder))
			return Path.Combine(install.Folder, InGameModsFolderName);

		return Path.Combine(dataBasePath, StagingFolderNameFor(install, profile));
	}

	/// <summary>
	/// The <c>%AppData%</c> staging folder's name for a copy. The game's first copy keeps the plain name it has
	/// always had — that folder is full of someone's mods — and a further copy is suffixed so two copies of one
	/// game never stage into the same folder and quietly merge their mod lists.
	/// </summary>
	private static string StagingFolderNameFor(GameInstall install, GameProfile profile)
	{
		string name = profile.StagingFolderName ?? "";
		return install.Key == install.GameId || name.Length == 0
			? name
			: name + "_" + install.Key.Substring(install.GameId.Length + 1);
	}

	/// <summary>
	/// Initialises the application: loads settings, sets up the UI, registers the nxm:// protocol
	/// handler, and starts the named pipe server. Handles an optional startup nxm:// URL in
	/// <paramref name="args"/>.
	/// </summary>
	public Form1(string[] args)
	{
		Form1 form = this;
		InitializeComponent();
        Application.AddMessageFilter(this);
        _settings = AppSettings.Load();
		// Unless the user has opted into manual theme selection, the sound theme follows the
		// loaded game. A normal launch restores the persisted active game without going through
		// SwitchActiveGame, so apply the mapping here too.
		if (!_settings.AllowManualTheme)
		{
			_settings.CurrentTheme = AppSettings.ThemeForGame(_settings.ActiveGame);
		}
		_soundEngine    = new SoundEngine(themesPath, _settings);
		_nexusService   = new NexusService(_settings);
		_aiService      = new AiService(_settings);
		if (string.IsNullOrEmpty(_settings.ApiKey) && File.Exists("nexus_key.txt"))
		{
			_settings.ApiKey = File.ReadAllText("nexus_key.txt").Trim();
			_settings.Save();
		}
		DetectModsPath();
		try
		{
			Tolk.Load();
			Tolk.TrySAPI(trySAPI: true);
			// The welcome / shortcut-hint announcement is spoken from the Shown handler (below) so we can wait
			// for it to finish before the startup loading speaks over it.
		}
		catch (Exception ex)
		{
			SpeakBox(Loc.T("app.tolkFailed", ex.Message));
		}
		// Migrate old root backups/downloads files to StardewValley game subfolder if present
		try
		{
			string oldBackups = Path.Combine(dataBasePath, "backups");
			if (Directory.Exists(oldBackups))
			{
				string stardewBackups = Path.Combine(oldBackups, "StardewValley");
				if (!Directory.Exists(stardewBackups)) Directory.CreateDirectory(stardewBackups);
				
				foreach (string file in Directory.GetFiles(oldBackups, "*.zip"))
				{
					string dest = Path.Combine(stardewBackups, Path.GetFileName(file));
					if (!File.Exists(dest))
					{
						File.Move(file, dest);
					}
				}
			}

			string oldDownloads = Path.Combine(dataBasePath, "downloads");
			if (Directory.Exists(oldDownloads))
			{
				string stardewDownloads = Path.Combine(oldDownloads, "StardewValley");
				if (!Directory.Exists(stardewDownloads)) Directory.CreateDirectory(stardewDownloads);
				
				foreach (string file in Directory.GetFiles(oldDownloads, "*.*"))
				{
					if (File.Exists(file))
					{
						string dest = Path.Combine(stardewDownloads, Path.GetFileName(file));
						if (!File.Exists(dest))
						{
							File.Move(file, dest);
						}
					}
				}
			}
		}
		catch (Exception ex) { LogError("Migration", "Failed to migrate backups/downloads folders: " + ex.Message); }

		SetupAccessibleUI();
		// Add the "name, pause, then value/state" reading to the main window's combos, checkboxes, and lists so a
		// screen reader doesn't run the field name straight into its value. Dialogs do the same when they open.
		ApplyScreenReaderPauses(this);
		// Apply the low-vision display settings (high-contrast colours / larger text) to the whole window once the
		// UI exists. No-op at the defaults, so normal-vision users see no change.
		ApplyDisplayTheme();
		if (!Directory.Exists(downloadsPath))
		{
			Directory.CreateDirectory(downloadsPath);
		}
		if (!Directory.Exists(backupsPath))
		{
			Directory.CreateDirectory(backupsPath);
		}
		if (!Directory.Exists(profilesPath))
		{
			Directory.CreateDirectory(profilesPath);
		}
		if (!Directory.Exists(themesPath))
		{
			Directory.CreateDirectory(themesPath);
		}
		RegisterNxmProtocol();
		_ = StartNamedPipeServer(_pipeCts.Token);
		base.FormClosing += async (object? _, FormClosingEventArgs e) =>
		{
			// The sequence has already run; this is the close it asked for.
			if (form._readyToClose) return;

			// Windows is logging off or the process is being ended from outside. There is no time to be granted
			// — the OS closes us whatever we say — so do the essential teardown and get out of the way rather
			// than talking into a window that is about to vanish.
			if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing)
			{
				form._readyToClose = true;
				form._pipeCts.Cancel();
				if (Tolk.IsLoaded()) Tolk.Unload();
				return;
			}

			// Already saying goodbye: swallow the repeat rather than cutting the message off half-spoken.
			if (form._shuttingDown) { e.Cancel = true; return; }

			// Keep the window — and with it the message loop — alive while we speak and play the cue.
			//
			// This is the whole fix. The announcement used to be made from inside this handler and then waited
			// on with Thread.Sleep, which blocks the UI thread; speech synthesis needs that thread to keep
			// pumping, so the message did not actually start until the sleep was over and the window was
			// already going. Cancelling the close and running the sequence with the pump alive means it is
			// spoken when the user asks to exit, which is when it is useful.
			//
			// Cancel must be set before the first await, or the close completes while we are still suspended.
			e.Cancel = true;
			form._shuttingDown = true;
			await form.RunShutdownSequenceAsync();
			form.Close();
		};
		base.Shown += async delegate
		{
			// The startup order, deliberately: the welcome, then which session is loaded, then everything that
			// loading a session says for itself. Each step waits for the one before it, so nothing is talked over.
			// The welcome is toggleable in Settings (Startup tab).
			if (form._settings!.SpeakStartupMessage)
			{
				// The screen reader reads the new window's caption -- "The Witcher 3: Wild Hunt Kinetix Mod
				// Manager" -- the moment it appears. Waiting for it and then interrupting was what cut it off
				// half-said. It is swallowed outright instead, and the app says the same thing better: the
				// welcome first, then which session is loaded, in a sentence rather than a title bar.
				//
				// A longer window than the default: at startup the reader gets to the caption later, with the
				// app still coming up around it.
				await form.SettleAfterForeignWindowAsync(passes: 60);

				form.Speak(Loc.T("app.started"), interrupt: true);
				await WaitForSpeechAsync();

				// Only when there is one. Starting with no session goes to the game list, which announces itself.
				if (form._settings.ActiveGame != "None")
				{
					form.Speak(Loc.T("app.sessionLoaded", form.GameDisplayName()));
					await WaitForSpeechAsync();
				}
			}
			if (form._settings.CheckForManagerUpdatesAtStartup)
				await form.CheckForAppUpdates(manual: false);

			// Before the first-run wizard and before any session loads: if they own a game twice, that changes
			// what the Games menu says and which mods they are about to see.
			form.AnnounceNewlyDetectedCopies();

			bool stardewInstalled = form.IsGameInstalled("StardewValley");
			bool skyrimInstalled = form.IsGameInstalled("SkyrimSE");
			bool falloutInstalled = form.IsGameInstalled("Fallout4");
			bool anyInstalled = stardewInstalled || skyrimInstalled || falloutInstalled;

			// First launch: open Settings (modal) so a brand-new user can set their game folders and Nexus key
			// right away — the Settings dialog works without a game loaded, and previously you had to pick a game
			// first before it would appear. Only for a genuinely fresh setup; existing users are marked done
			// silently so they aren't nagged after upgrading.
			if (!form._settings!.HasCompletedFirstRun)
			{
				bool fresh = string.IsNullOrEmpty(form._settings.ApiKey) && form._settings.ActiveGame == "None";
				form._settings.HasCompletedFirstRun = true;
				form._settings.Save();
				if (fresh)
				{
					// Guide a brand-new user through the setup checklist rather than dropping them into raw Settings.
					form.ShowSetupWizard();
				}
			}

			if (form._settings.ActiveGame == "None")
			{
				if (form._lstGames != null)
				{
					form._lstGames.Focus();
				}
				Speak(Loc.T("app.welcome"));
			}
			else if (form.IsGameInstalled(form._settings.ActiveGame))
			{
				// Armed before the refresh, spent by it: the refresh is what says "Connecting…" and "Connected
				// as …", so landing on the first tab has to happen at its end rather than here. See LandOnFirstTab.
				form._landOnFirstTabAfterStartup = true;
				form.RefreshAllData(form._settings.CheckForUpdatesAtStartup);
			}
			else if (anyInstalled)
			{
				// The previously active game is no longer installed (e.g. settings carried over
				// from another PC) but other supported games are. Return to the game-selection
				// screen rather than loading another game's mods by mistake.
				form.SwitchActiveGame("None");
			}

			// If no supported game is installed at all, guide the user to purchase one. This also
			// covers a saved-but-uninstalled active game with no other game present (in which case
			// we deliberately neither refreshed nor reset above, so no wrong mods were loaded).
			if (!anyInstalled && form._settings.ActiveGame != "None")
			{
				form.ShowNoGamesInstalledFlow();
			}

			if (args.Length != 0 && args[0].StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
			{
				await form.HandleNxmUrl(args[0]);
			}
		};
	}

	/// <summary>
	/// IMessageFilter implementation that intercepts WM_KEYDOWN at the Windows message pump level
	/// so F6 is caught even when the out-of-process WebView2 window has keyboard focus.
	/// </summary>
    public bool PreFilterMessage(ref Message m)
    {
        // Intercept WM_KEYDOWN before it reaches the WebView2 host window.
        // WebView2 runs in a separate process and would otherwise swallow F6 or Shift+Tab.
        if (m.Msg == 0x0100)
        {
            Keys key = (Keys)m.WParam.ToInt32();
            bool isWikiFocused = webViewWiki != null && webViewWiki.ContainsFocus;
            bool isWalkthroughFocused = webViewWalkthrough != null && webViewWalkthrough.ContainsFocus;

            if (key == Keys.F6 && (isWikiFocused || isWalkthroughFocused))
            {
                // BeginInvoke ensures the focus change happens after the current message is fully processed.
                BeginInvoke(new Action(HandleCycleFocus));
                return true; // Prevent the browser from receiving this F6
            }

            if (key == Keys.Tab && Control.ModifierKeys.HasFlag(Keys.Shift) && (isWikiFocused || isWalkthroughFocused))
            {
                if (isWikiFocused)
                {
                    BeginInvoke(new Action(() =>
                    {
                        listWikiResults.Focus();
                        Speak(Loc.T("common.wikiResultsList"));
                    }));
                }
                else if (isWalkthroughFocused)
                {
                    BeginInvoke(new Action(() =>
                    {
                        listWalkthroughs.Focus();
                        Speak(Loc.T("common.walkthroughGuidesList"));
                    }));
                }
                return true; // Prevent the browser from trapping Shift+Tab
            }
        }
        return false;
    }

	/// <summary>
	/// Runs a named pipe server loop that receives nxm:// URLs forwarded by a second app instance
	/// and calls <see cref="HandleNxmUrl"/>. Exits cleanly when <paramref name="token"/> is cancelled.
	/// </summary>
    private async Task StartNamedPipeServer(CancellationToken token)
	{
		try
		{
			while (!token.IsCancellationRequested)
			{
				try
				{
					using NamedPipeServerStream server = new NamedPipeServerStream("KinetixModManager-Nexus-Handler", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
					await server.WaitForConnectionAsync(token);
					using StreamReader reader = new StreamReader(server);
					// A 5-second read timeout prevents a misbehaving client from blocking
					// this loop indefinitely after connecting but never sending data.
					using var readCts = CancellationTokenSource.CreateLinkedTokenSource(token);
					readCts.CancelAfter(TimeSpan.FromSeconds(5));
					string url = await reader.ReadLineAsync(readCts.Token) ?? "";
					if (!string.IsNullOrEmpty(url))
					{
						// BeginInvoke dispatches async work to the UI thread without
						// blocking the pipe-server loop or silently discarding the Task.
						BeginInvoke(async () =>
						{
							Activate();
							await HandleNxmUrl(url);
						});
					}
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (Exception)
				{
					if (!token.IsCancellationRequested)
					{
						try
						{
							await Task.Delay(1000, token);
						}
						catch (OperationCanceledException)
						{
							break;
						}
						continue;
					}
					break;
				}
			}
		}
		catch (OperationCanceledException)
		{
			// Expected during application shutdown when the cancellation token is signalled.
		}
	}

	/// <summary>
	/// Writes the <c>nxm://</c> URL protocol handler to the Windows Registry so that clicking
	/// Nexus Mods download buttons opens this application.
	/// </summary>
	private void RegisterNxmProtocol()
	{
		try
		{
			string executablePath = Application.ExecutablePath;
			using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Classes\\nxm");
			registryKey.SetValue("", "Nexus Mod Manager Link");
			registryKey.SetValue("URL Protocol", "");
			using RegistryKey registryKey2 = registryKey.CreateSubKey("shell\\open\\command");
			string? obj = registryKey2.GetValue("")?.ToString() ?? "";
			string text = "\"" + executablePath + "\" \"%1\"";
			if (obj != text)
			{
				registryKey2.SetValue("", text);
			}
		}
		catch (Exception ex)
		{
			LogError("System", "Protocol Registration Error: " + ex.Message);
		}
	}












	private string errorLogPath => Path.Combine(dataBasePath, "mod_manager_log.txt");

	/// <summary>
	/// Opens the manager's own error log in Notepad, and says so when there is nothing to open — a keypress that
	/// does nothing in silence is indistinguishable from one that failed. Shared by the File menu item and the
	/// shortcut, which is what this exists for: the shortcut used to look for the file by a bare relative name,
	/// so it searched the install folder instead of the app data folder and could never find it.
	/// </summary>
	private void OpenErrorLog()
	{
		if (!File.Exists(errorLogPath))
		{
			Speak(Loc.T("menu.errorLogEmpty"));
			return;
		}
		Process.Start(new ProcessStartInfo("notepad.exe", errorLogPath) { UseShellExecute = true });
	}

	/// <summary>Appends a timestamped error line to <c>mod_manager_log.txt</c> in the app data directory.</summary>
	private void LogError(string mod, string msg)
	{
		try
		{
			File.AppendAllText(errorLogPath, $"[{DateTime.Now:HH:mm:ss}] {mod}: {msg}\n");
		}
		catch
		{
		}
	}






	protected override void Dispose(bool disposing)
	{
		if (disposing && components != null)
		{
			components.Dispose();
		}
		base.Dispose(disposing);
	}

	private void InitializeComponent()
	{
		this.components = new System.ComponentModel.Container();
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		base.ClientSize = new System.Drawing.Size(800, 450);
		this.Text = "Form1";
	}
}

public class WikiNavigationState
{
	public string Title { get; set; } = "";
	public List<object> Results { get; set; } = new List<object>();
	public int SelectedIndex { get; set; }
}

public class WikiResult
{
	public string Title { get; set; } = "";
	public bool IsCategory { get; set; }
	public override string ToString()
	{
		string displayTitle = Title.StartsWith("Category:") ? Title.Substring(9) : Title;
		return IsCategory ? "[Category] " + displayTitle : displayTitle;
	}
}

public class WalkthroughGuide
{
	public string Title { get; set; } = "";
	public string Url { get; set; } = "";
	public override string ToString() => Title;
}

public class LanguageOption
{
	/// <summary>Nexus language name (e.g. "English"). Empty string means "Any language" (no filter).</summary>
	public string Name { get; set; } = "";
	/// <summary>Number of mods in this language for the active game; 0 hides the count.</summary>
	public int Count { get; set; }
	public override string ToString() =>
		string.IsNullOrEmpty(Name) ? Loc.T("ui.anyLanguage") : (Count > 0 ? $"{Name} ({Count})" : Name);
}

/// <summary>One entry in the Discovery tab's category selector.</summary>
public class CategoryOption
{
	/// <summary>Nexus category name (e.g. "Armour"). Empty string means "Any category" (no filter).</summary>
	public string Name { get; set; } = "";
	/// <summary>Number of mods in this category for the active game; 0 hides the count.</summary>
	public int Count { get; set; }
	public override string ToString() =>
		string.IsNullOrEmpty(Name) ? Loc.T("ui.anyCategory") : (Count > 0 ? $"{Name} ({Count})" : Name);
}

public class ModWikiLink
{
	/// <summary>Display name shown in the Mod Wikis dropdown.</summary>
	public string Title { get; set; } = "";
	/// <summary>Landing page navigated to when this wiki is selected.</summary>
	public string Url { get; set; } = "";
	/// <summary>
	/// MediaWiki <c>api.php</c> endpoint used for in-app Search and Categories. Empty means this wiki is
	/// "browse-only" — its host exposes no usable MediaWiki API, so it only opens in the embedded browser.
	/// </summary>
	public string ApiUrl { get; set; } = "";
	/// <summary>Article URL prefix (e.g. <c>https://x.wiki.gg/wiki/</c>) used to open a search/category result.</summary>
	public string ArticleBase { get; set; } = "";
	/// <summary>True for the base game wiki.</summary>
	public bool IsGameWiki { get; set; }
	/// <summary>
	/// MediaWiki <c>acprefix</c> used to scope the live category list to one game on multi-game wikis
	/// (e.g. "Skyrim" on UESP, "Fallout 4" on the Fallout wiki). Empty means list all categories.
	/// </summary>
	public string CategoryPrefix { get; set; } = "";
	public override string ToString() => Title;
}
