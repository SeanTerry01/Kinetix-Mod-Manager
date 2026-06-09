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
		SmapiLog  = 7
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

	private AppSettings _settings;
	private ToolStripMenuItem _menuGames = null!;

	private static string dataBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudiVentureGames", "KinetixModManager");

	private string downloadsPath
	{
		get
		{
			string path = Path.Combine(dataBasePath, "downloads", _settings.ActiveGame);
			if (!Directory.Exists(path))
			{
				try { Directory.CreateDirectory(path); } catch { }
			}
			return path;
		}
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

	private int _currentDiscoveryPage = 1;

	private bool _isLoading;

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
	private TabPage tabSmapiLog = null!;
	private TabPage tabWiki = null!;
	private TabPage tabWalkthroughs = null!;
	private ListBox listInstalled = null!;
	private ListBox listUpdates = null!;
	private ListBox listDiscovery = null!;
	private ListBox listBackups = null!;
	private ListBox listProfiles = null!;
	private ListBox listLog = null!;
	private TextBox txtSearch = null!;
	private TextBox txtSearchInstalled = null!;
	private TextBox txtSearchLog = null!;
	private ComboBox cmbDiscoveryType = null!;
	private ComboBox cmbLogFilter = null!;
	private ComboBox cmbCategoryFilter = null!;
	private Button btnSearch = null!;
	private Button btnLoadMoreDiscovery = null!;
	private Button btnPruneBackups = null!;
	private TextBox txtWikiSearch = null!;
	private ComboBox cmbWikiCategories = null!;
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
			string[] array = new string[5]
			{
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

		if (string.IsNullOrEmpty(_settings.GameModsPaths["SkyrimSE"]) || !Directory.Exists(_settings.GameModsPaths["SkyrimSE"]))
		{
			string folder = DetectGameFolder("SkyrimSE");
			if (Directory.Exists(folder))
			{
				_settings.GamePaths["SkyrimSE"] = folder;
				string localSkyrimMods = Path.Combine(dataBasePath, "SkyrimSEMods");
				if (!Directory.Exists(localSkyrimMods)) Directory.CreateDirectory(localSkyrimMods);
				_settings.GameModsPaths["SkyrimSE"] = localSkyrimMods;
				_settings.Save();
			}
		}

		if (string.IsNullOrEmpty(_settings.GameModsPaths["Fallout4"]) || !Directory.Exists(_settings.GameModsPaths["Fallout4"]))
		{
			string folder = DetectGameFolder("Fallout4");
			if (Directory.Exists(folder))
			{
				_settings.GamePaths["Fallout4"] = folder;
				string localFallout4Mods = Path.Combine(dataBasePath, "Fallout4Mods");
				if (!Directory.Exists(localFallout4Mods)) Directory.CreateDirectory(localFallout4Mods);
				_settings.GameModsPaths["Fallout4"] = localFallout4Mods;
				_settings.Save();
			}
		}
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
			Speak("Mod Manager Started. Press F1 for the manual, or Shift + F1 at any time to hear a list of shortcuts for the selected tab.");
		}
		catch (Exception ex)
		{
			MessageBox.Show("Tolk could not be loaded. Please ensure all native DLLs (Tolk.dll, nvdaControllerClient.dll, etc.) are in the application folder or lib folder.\nError: " + ex.Message);
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
		base.FormClosing += delegate
		{
			form._pipeCts.Cancel();
			// Only disconnect on exit if a game session is still open. Closing a session
			// (Ctrl+Shift+C) already disconnects, so exiting with no game loaded must not
			// replay a disconnect for a session that was already torn down.
			if (form._settings!.ActiveGame != "None")
			{
				form._soundEngine.Play("disconnect");
				Thread.Sleep(800); // let the disconnect cue finish before the process exits
			}
			if (Tolk.IsLoaded())
			{
				Tolk.Unload();
			}
		};
		base.Shown += async delegate
		{
			await form.CheckForAppUpdates(manual: false);

			bool stardewInstalled = form.IsGameInstalled("StardewValley");
			bool skyrimInstalled = form.IsGameInstalled("SkyrimSE");
			bool falloutInstalled = form.IsGameInstalled("Fallout4");
			bool anyInstalled = stardewInstalled || skyrimInstalled || falloutInstalled;

			if (form._settings!.ActiveGame == "None")
			{
				if (form._lstGames != null)
				{
					form._lstGames.Focus();
				}
				Speak("Welcome to Kinetix Mod Manager. Please select a game to manage from the list. Use arrow keys to select, and press Enter to confirm.");
			}
			else if (form.IsGameInstalled(form._settings.ActiveGame))
			{
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
                        Speak("Wiki Results List");
                    }));
                }
                else if (isWalkthroughFocused)
                {
                    BeginInvoke(new Action(() =>
                    {
                        listWalkthroughs.Focus();
                        Speak("Walkthrough Guides List");
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
