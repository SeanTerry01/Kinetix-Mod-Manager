using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.VisualBasic;
using System.IO.Compression;
using System.Threading;
using Microsoft.Win32;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using DavyKager; 
using NAudio.Wave;
using NAudio.Vorbis;
using System.Diagnostics;

namespace StardewAccessibleManager
{
    public partial class Form1 : Form
    {
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler() 
        { 
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate 
        });

        private AppSettings _settings = null!;
        private string downloadsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "downloads");
        private string backupsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
        private string profilesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");
        private string themesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds");
        
        private bool isPremium = false;
        private string nexusUser = "Unknown User";
        private readonly SemaphoreSlim _apiSemaphore = new SemaphoreSlim(5);
        private bool isUpdatingAll = false;
        private CancellationTokenSource _pipeCts = new CancellationTokenSource();
        
        private int _activeChecks = 0;
        private bool _isLoading = false;
        private bool _isSettingsOpen = false;
        private List<StardewMod> _allInstalledMods = new List<StardewMod>();
        private List<LogEntry> _fullLogEntries = new List<LogEntry>();
        private HashSet<string> _expandedGroups = new HashSet<string>();
        
        // Multi-letter search buffer
        private string _searchBuffer = "";
        private System.Windows.Forms.Timer _searchTimer = new System.Windows.Forms.Timer { Interval = 1000 };

        private Dictionary<string, string> _soundDescriptions = new Dictionary<string, string>
        {
            { "connect", "Played when successfully connected to Nexus Mods." },
            { "disconnect", "Played when you are disconnected, need to enter an API key, or when the program closes." },
            { "enable", "Played when one or more mods are enabled." },
            { "disable", "Played when one or more mods are disabled or deleted." },
            { "error", "Played when an error occurs, such as a failed download." },
            { "loading_indicator", "A pulsing sound that plays while the manager is checking for updates in the background." },
            { "load_complete", "Played when the manager has finished checking all mods for updates." }
        };

        // UI Components
        private TabControl mainTabs = null!;
        private TabPage tabInstalled = null!;
        private TabPage tabUpdates = null!;
        private TabPage tabDiscovery = null!;
        private TabPage tabBackups = null!;
        private TabPage tabProfiles = null!;
        private TabPage tabSmapiLog = null!;
        
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
        private Button btnPruneBackups = null!;

        private void DetectModsPath()
        {
            if (!string.IsNullOrEmpty(_settings.ModsPath) && Directory.Exists(_settings.ModsPath)) return;

            string[] commonPaths = {
                @"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\Mods",
                @"C:\Program Files\Steam\steamapps\common\Stardew Valley\Mods",
                @"D:\SteamLibrary\steamapps\common\Stardew Valley\Mods",
                @"E:\SteamLibrary\steamapps\common\Stardew Valley\Mods",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "StardewValley", "Mods")
            };

            foreach (string path in commonPaths)
            {
                if (Directory.Exists(path))
                {
                    _settings.ModsPath = path;
                    _settings.Save();
                    return;
                }
            }
        }

        public Form1(string[] args)
        {
            InitializeComponent();
            _settings = AppSettings.Load();

            if (string.IsNullOrEmpty(_settings.ApiKey) && File.Exists("nexus_key.txt"))
            {
                _settings.ApiKey = File.ReadAllText("nexus_key.txt").Trim();
                _settings.Save();
            }

            DetectModsPath();
            
            try {
                Tolk.Load();
                Tolk.TrySAPI(true);
                Speak("Mod Manager Started. Press F1 for the manual, or Shift + F1 at any time to hear a list of shortcuts for the selected tab.");
            } catch (Exception ex) {
                MessageBox.Show("Tolk could not be loaded. Please ensure all native DLLs (Tolk.dll, nvdaControllerClient.dll, etc.) are in the application folder or lib folder.\nError: " + ex.Message);
            }

            SetupAccessibleUI();
            
            if (!Directory.Exists(downloadsPath)) Directory.CreateDirectory(downloadsPath);
            if (!Directory.Exists(backupsPath)) Directory.CreateDirectory(backupsPath);
            if (!Directory.Exists(profilesPath)) Directory.CreateDirectory(profilesPath);
            if (!Directory.Exists(themesPath)) Directory.CreateDirectory(themesPath);
            
            RegisterNxmProtocol();
            _ = StartNamedPipeServer(_pipeCts.Token);

            this.FormClosing += (s, e) => {
                _pipeCts.Cancel();
                
                PlayAppSound("disconnect");
                Thread.Sleep(800); 

                if (Tolk.IsLoaded()) Tolk.Unload();
            };

            this.Shown += async (s, e) => {
                CheckForAppUpdates(false);
                RefreshModList(_settings.CheckForUpdatesAtStartup);
                RefreshBackupsList();
                RefreshProfilesList();
                RefreshSmapiLog();
                if (args.Length > 0 && args[0].StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleNxmUrl(args[0]);
                }
            };
        }

        private async Task StartNamedPipeServer(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using (var server = new NamedPipeServerStream("StardewAccessibleManager-Nexus-Handler", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                        {
                            await server.WaitForConnectionAsync(token);
                            using (var reader = new StreamReader(server))
                            {
                                string? url = await reader.ReadLineAsync();
                                if (!string.IsNullOrEmpty(url))
                                {
                                    this.Invoke(new Action(async () => {
                                        this.Activate();
                                        await HandleNxmUrl(url);
                                    }));
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception)
                    {
                        if (token.IsCancellationRequested) break;
                        try { await Task.Delay(1000, token); } catch (OperationCanceledException) { break; }
                    }
                }
            }
            catch (OperationCanceledException) { } // Silent exit on close
        }

        private void RegisterNxmProtocol()
        {
            try
            {
                string exePath = Application.ExecutablePath;
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\nxm"))
                {
                    key.SetValue("", "Nexus Mod Manager Link");
                    key.SetValue("URL Protocol", "");
                    using (var shellKey = key.CreateSubKey(@"shell\open\command"))
                    {
                        string currentVal = shellKey.GetValue("")?.ToString() ?? "";
                        string targetVal = $"\"{exePath}\" \"%1\"";
                        if (currentVal != targetVal)
                        {
                            shellKey.SetValue("", targetVal);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("System", "Protocol Registration Error: " + ex.Message);
            }
        }

        private void SetupAccessibleUI()
        {
            this.Text = "Stardew Valley Accessible Mod Manager";
            this.Size = new Size(1000, 700);
            this.KeyPreview = true;
            this.KeyDown += Form1_KeyDown;

            MenuStrip menu = new MenuStrip();
            var fileMenu = new ToolStripMenuItem("&File");
            fileMenu.DropDownItems.Add($"Refresh All ({GetShortcutString("RefreshAll")})", null, (s, e) => { RefreshModList(true); RefreshBackupsList(); RefreshProfilesList(); RefreshSmapiLog(); });
            fileMenu.DropDownItems.Add($"Refresh Installed Only ({GetShortcutString("RefreshInstalled")})", null, (s, e) => RefreshModList(false));
            fileMenu.DropDownItems.Add("Check for Manager Updates", null, (s, e) => CheckForAppUpdates(true));
            fileMenu.DropDownItems.Add($"Settings ({GetShortcutString("Settings")})", null, (s, e) => ShowSettings());
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("Exit", null, (s, e) => Application.Exit());

            var modsMenu = new ToolStripMenuItem("&Mods");
            modsMenu.DropDownItems.Add($"Save Current Setup as Profile ({GetShortcutString("SaveProfile")})", null, (s, e) => CreateProfileFromCurrent());
            modsMenu.DropDownItems.Add($"Install from Zip ({GetShortcutString("InstallZip")})", null, (s, e) => ManualInstall());
            modsMenu.DropDownItems.Add($"Update All Available ({GetShortcutString("UpdateAll")})", null, (s, e) => UpdateAllMods());
            modsMenu.DropDownItems.Add($"Launch Stardew Valley ({GetShortcutString("LaunchGame")})", null, (s, e) => LaunchGame());

            var viewMenu = new ToolStripMenuItem("&View");
            viewMenu.DropDownItems.Add($"Open Downloads Folder ({GetShortcutString("OpenDownloads")})", null, (s, e) => System.Diagnostics.Process.Start("explorer.exe", downloadsPath));
            viewMenu.DropDownItems.Add($"Open Backups Folder ({GetShortcutString("OpenBackups")})", null, (s, e) => System.Diagnostics.Process.Start("explorer.exe", backupsPath));
            viewMenu.DropDownItems.Add($"Open SMAPI Log File ({GetShortcutString("OpenLogFile")})", null, (s, e) => OpenRawSmapiLog());
            viewMenu.DropDownItems.Add($"Open Error Log ({GetShortcutString("OpenErrorLog")})", null, (s, e) => {
                if (File.Exists("mod_manager_log.txt"))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", "mod_manager_log.txt") { UseShellExecute = true });
            });

            var helpMenu = new ToolStripMenuItem("&Help");
            helpMenu.DropDownItems.Add($"User Manual ({GetShortcutString("Manual")})", null, (s, e) => ShowManual());
            helpMenu.DropDownItems.Add("Sound Demo", null, (s, e) => ShowSoundDemo());
            helpMenu.DropDownItems.Add("Audio Theme Manager", null, (s, e) => ShowThemeManager());
            helpMenu.DropDownItems.Add("Shortcut Customization", null, (s, e) => ShowShortcutManager());

            menu.Items.Add(fileMenu);
            menu.Items.Add(modsMenu);
            menu.Items.Add(viewMenu);
            menu.Items.Add(helpMenu);
            this.MainMenuStrip = menu;

            TableLayoutPanel mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 1 };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainLayout.Padding = new Padding(0, 25, 0, 0); 

            mainTabs = new TabControl { Dock = DockStyle.Fill };
            
            // Tab: Installed
            tabInstalled = new TabPage("Installed Mods");
            TableLayoutPanel installedLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            installedLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            installedLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            FlowLayoutPanel searchInstalledPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            txtSearchInstalled = new TextBox { Width = 200, Font = new Font("Segoe UI", 12), AccessibleName = "Search Installed Mods" };
            txtSearchInstalled.TextChanged += (s, e) => FilterInstalledMods();
            
            cmbCategoryFilter = new ComboBox { Width = 150, Font = new Font("Segoe UI", 12), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbCategoryFilter.Items.Add("All Categories");
            cmbCategoryFilter.SelectedIndex = 0;
            cmbCategoryFilter.SelectedIndexChanged += (s, e) => FilterInstalledMods();

            searchInstalledPanel.Controls.Add(new Label { Text = "Search:", AutoSize = true, Padding = new Padding(5, 8, 0, 0) });
            searchInstalledPanel.Controls.Add(txtSearchInstalled);
            searchInstalledPanel.Controls.Add(new Label { Text = "Category:", AutoSize = true, Padding = new Padding(10, 8, 0, 0) });
            searchInstalledPanel.Controls.Add(cmbCategoryFilter);

            listInstalled = new ListBox { Dock = DockStyle.Fill, Name = "listInstalled", Font = new Font("Segoe UI", 12) };
            listInstalled.AccessibleName = "Installed Mods List";
            installedLayout.Controls.Add(searchInstalledPanel, 0, 0);
            installedLayout.Controls.Add(listInstalled, 0, 1);
            tabInstalled.Controls.Add(installedLayout);

            tabUpdates = new TabPage("Updates Available");
            listUpdates = new ListBox { Dock = DockStyle.Fill, Name = "listUpdates", Font = new Font("Segoe UI", 12) };
            listUpdates.AccessibleName = "Available Updates List";
            tabUpdates.Controls.Add(listUpdates);

            tabBackups = new TabPage("Backups");
            TableLayoutPanel backupLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            backupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            backupLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            btnPruneBackups = new Button { Text = $"Prune Old Backups ({GetShortcutString("PruneBackups")})", Width = 250, Height = 35 };
            btnPruneBackups.Click += (s, e) => PruneAllBackups();
            backupLayout.Controls.Add(btnPruneBackups, 0, 0);
            listBackups = new ListBox { Dock = DockStyle.Fill, Name = "listBackups", Font = new Font("Segoe UI", 12) };
            listBackups.AccessibleName = "Mod Backups List";
            backupLayout.Controls.Add(listBackups, 0, 1);
            tabBackups.Controls.Add(backupLayout);

            tabDiscovery = new TabPage("Find New Mods");
            TableLayoutPanel discoveryLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            discoveryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            discoveryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            FlowLayoutPanel searchPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(5) };
            txtSearch = new TextBox { Width = 250, Font = new Font("Segoe UI", 12), AccessibleName = "Search Mod Name" };
            cmbDiscoveryType = new ComboBox { Width = 150, Font = new Font("Segoe UI", 12), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbDiscoveryType.Items.AddRange(new string[] { "Search", "Trending", "Most Popular", "Recent" });
            cmbDiscoveryType.SelectedIndex = 0;
            btnSearch = new Button { Text = "Go", Height = 30, Width = 60 };
            btnSearch.Click += async (s, e) => await RunDiscovery();
            txtSearch.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) await RunDiscovery(); };
            searchPanel.Controls.Add(new Label { Text = "Search/Type:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            searchPanel.Controls.Add(txtSearch);
            searchPanel.Controls.Add(cmbDiscoveryType);
            searchPanel.Controls.Add(btnSearch);
            listDiscovery = new ListBox { Dock = DockStyle.Fill, Name = "listDiscovery", Font = new Font("Segoe UI", 12) };
            listDiscovery.AccessibleName = "Mod Discovery Results";
            discoveryLayout.Controls.Add(searchPanel, 0, 0);
            discoveryLayout.Controls.Add(listDiscovery, 0, 1);
            tabDiscovery.Controls.Add(discoveryLayout);

            tabProfiles = new TabPage("Profiles");
            listProfiles = new ListBox { Dock = DockStyle.Fill, Name = "listProfiles", Font = new Font("Segoe UI", 12) };
            listProfiles.AccessibleName = "Mod Profiles List";
            tabProfiles.Controls.Add(listProfiles);

            tabSmapiLog = new TabPage("SMAPI Log");
            TableLayoutPanel logLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            logLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            FlowLayoutPanel logFilterPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            cmbLogFilter = new ComboBox { Width = 180, Font = new Font("Segoe UI", 12), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbLogFilter.Items.AddRange(new string[] { "Errors and Warnings", "Errors Only", "Full Log" });
            cmbLogFilter.SelectedIndex = 0;
            cmbLogFilter.SelectedIndexChanged += (s, e) => RefreshSmapiLog();
            
            txtSearchLog = new TextBox { Width = 180, Font = new Font("Segoe UI", 12), AccessibleName = "Search Log Entries" };
            txtSearchLog.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SearchSmapiLog(); } };

            logFilterPanel.Controls.Add(new Label { Text = "Filter:", AutoSize = true, Padding = new Padding(5, 8, 0, 0) });
            logFilterPanel.Controls.Add(cmbLogFilter);
            logFilterPanel.Controls.Add(new Label { Text = "Search:", AutoSize = true, Padding = new Padding(10, 8, 0, 0) });
            logFilterPanel.Controls.Add(txtSearchLog);

            listLog = new ListBox { Dock = DockStyle.Fill, Name = "listLog", Font = new Font("Segoe UI", 12) };
            listLog.AccessibleName = "Parsed SMAPI Log Entries";
            logLayout.Controls.Add(logFilterPanel, 0, 0);
            logLayout.Controls.Add(listLog, 0, 1);
            tabSmapiLog.Controls.Add(logLayout);

            mainTabs.TabPages.Add(tabInstalled);
            mainTabs.TabPages.Add(tabUpdates);
            mainTabs.TabPages.Add(tabBackups);
            mainTabs.TabPages.Add(tabDiscovery);
            mainTabs.TabPages.Add(tabProfiles);
            mainTabs.TabPages.Add(tabSmapiLog);

            mainLayout.Controls.Add(mainTabs, 0, 0);
            this.Controls.Add(mainLayout);
            this.Controls.Add(menu);

            listInstalled.KeyDown += List_KeyDown;
            listUpdates.KeyDown += List_KeyDown;
            listDiscovery.KeyDown += List_KeyDown;
            listBackups.KeyDown += List_KeyDown;
            listProfiles.KeyDown += List_KeyDown;
            listLog.KeyDown += List_KeyDown;

            listInstalled.SelectedIndexChanged += List_SelectedIndexChanged;
            listUpdates.SelectedIndexChanged += List_SelectedIndexChanged;
            listDiscovery.SelectedIndexChanged += List_SelectedIndexChanged;
            listBackups.SelectedIndexChanged += List_SelectedIndexChanged;
            listProfiles.SelectedIndexChanged += List_SelectedIndexChanged;
            listLog.SelectedIndexChanged += List_SelectedIndexChanged;

            listInstalled.Enter += List_Enter;
            listUpdates.Enter += List_Enter;
            listDiscovery.Enter += List_Enter;
            listBackups.Enter += List_Enter;
            listProfiles.Enter += List_Enter;
            listLog.Enter += List_Enter;

            _searchTimer.Tick += (s, e) => { _searchTimer.Stop(); _searchBuffer = ""; };
        }

        private string GetShortcutString(string action)
        {
            if (_settings.Shortcuts.TryGetValue(action, out Keys keys))
            {
                if (keys == Keys.None) return "Unmapped";
                var sb = new StringBuilder();
                if ((keys & Keys.Control) == Keys.Control) sb.Append("Ctrl+");
                if ((keys & Keys.Shift) == Keys.Shift) sb.Append("Shift+");
                if ((keys & Keys.Alt) == Keys.Alt) sb.Append("Alt+");
                sb.Append(keys & Keys.KeyCode);
                return sb.ToString();
            }
            return "Unmapped";
        }

        private bool IsShortcut(KeyEventArgs e, string action)
        {
            if (!_settings.Shortcuts.TryGetValue(action, out Keys assigned)) return false;
            if (assigned == Keys.None) return false;
            return e.KeyData == assigned;
        }

        private void FilterInstalledMods()
        {
            string query = txtSearchInstalled.Text.Trim().ToLower();
            string category = cmbCategoryFilter.SelectedItem?.ToString() ?? "All Categories";

            listInstalled.BeginUpdate();
            listInstalled.Items.Clear();
            
            var matches = _allInstalledMods.Where(m => 
                (m.Name.ToLower().Contains(query) || m.Author.ToLower().Contains(query)) &&
                (category == "All Categories" || m.Category == category)
            ).ToList();

            foreach (var mod in matches) listInstalled.Items.Add(mod);
            listInstalled.EndUpdate();
            
            if (!string.IsNullOrEmpty(query) || category != "All Categories")
            {
                Speak($"{matches.Count} mods found.");
            }
        }

        private void List_Enter(object? sender, EventArgs e)
        {
            if (sender is ListBox list) {
                if (list.Items.Count > 0 && list.SelectedIndex == -1) list.SelectedIndex = 0;
                if (list.Items.Count == 0 && !_isLoading) Speak("List is empty.");
            }
        }

        private void SetStatus(string text)
        {
            if (this.InvokeRequired) { this.Invoke(new Action(() => SetStatus(text))); return; }
            string fullStatus = "Stardew Valley Accessible Mod Manager - Status: " + text;
            this.Text = fullStatus;
            Speak(text);
        }

        private async void List_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (sender is ListBox list && list.SelectedItem != null && !_isLoading) {
                await Task.Delay(100); 
                string text = $"{list.SelectedIndex + 1} of {list.Items.Count}";
                
                if (list.Name == "listLog")
                {
                    string logLine = list.SelectedItem.ToString()!;
                    string fix = LogAnalyzer.GetSuggestedFix(logLine);
                    if (!string.IsNullOrEmpty(fix)) text += ". Suggested Fix: " + fix;
                }

                Speak(text);
            }
        }

        private void ShowContextHelp()
        {
            string h = "";
            switch (mainTabs.SelectedIndex) {
                case 0: h = $"Installed Mods: Space to Toggle. Delete to remove mod. {GetShortcutString("Search")} to Search. {GetShortcutString("ChangeCategory")} to change Category. {GetShortcutString("BatchCategory")} to batch toggle Category. {GetShortcutString("OpenModPage")} for Nexus. {GetShortcutString("ShowDependencies")} for dependencies. {GetShortcutString("QuickFix")} to Quick-Fix. {GetShortcutString("ManualID")} for Nexus ID. {GetShortcutString("InstallZip")} to install zip. {GetShortcutString("SaveProfile")} to save as profile. {GetShortcutString("ReadDescription")} to read description. {GetShortcutString("LaunchGame")} to launch game."; break;
                case 1: h = $"Updates: Enter for Nexus page. Delete to Ignore this update. {GetShortcutString("UpdateAll")} to Update All (Premium). {GetShortcutString("ReadDescription")} to read description. {GetShortcutString("LaunchGame")} to launch game."; break;
                case 2: h = $"Backups: Enter to restore. Delete to remove zip. {GetShortcutString("PruneBackups")} to prune old backups. {GetShortcutString("OpenBackups")} to open backups folder."; break;
                case 3: h = $"Discovery: Enter for Nexus page. {GetShortcutString("ReadDescription")} to read summary. Tab to search."; break;
                case 4: h = "Profiles: Enter to apply profile. Delete to remove profile."; break;
                case 5: h = $"SMAPI Log: Search box available. Enter on search result to jump to it in full view. {GetShortcutString("QuickFix")} to Quick-Fix detected issue. {GetShortcutString("Login")} to upload to SMAPI.io. {GetShortcutString("OpenLogFile")} to open raw file."; break;
            }
            if (!string.IsNullOrEmpty(h)) Speak(h);
        }

        private async Task RunDiscovery()
        {
            if (string.IsNullOrEmpty(_settings.ApiKey)) { Speak("Please login first."); return; }
            string type = cmbDiscoveryType.SelectedItem?.ToString() ?? "Search";
            string query = txtSearch.Text.Trim();
            listDiscovery.Items.Clear();
            Speak($"Starting mod {type}...");
            SetStatus($"Running {type}...");
            try {
                string url = "";
                if (type == "Search") url = $"https://api.nexusmods.com/v1/games/stardewvalley/mods/search.json?q={Uri.EscapeDataString(query)}";
                else if (type == "Trending") url = "https://api.nexusmods.com/v1/games/stardewvalley/mods/trending.json";
                else if (type == "Most Popular") url = "https://api.nexusmods.com/v1/games/stardewvalley/mods/popular.json";
                else if (type == "Recent") url = "https://api.nexusmods.com/v1/games/stardewvalley/mods/latest_added.json";

                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("apikey", _settings.ApiKey); req.Headers.Add("User-Agent", "StardewAccessibleManager/1.0.0");
                var res = await _httpClient.SendAsync(req);
                if (res.IsSuccessStatusCode) {
                    var data = JArray.Parse(await res.Content.ReadAsStringAsync());
                    foreach (var item in data) {
                        listDiscovery.Items.Add(new StardewMod {
                            Name = (string?)item["name"] ?? "Unknown", 
                            Author = (string?)item["author"] ?? "Unknown", 
                            Version = (string?)item["version"] ?? "0",
                            Description = (string?)item["summary"] ?? "", 
                            NexusID = item["mod_id"]?.ToString(),
                            UniqueId = item["mod_id"]?.ToString() ?? Guid.NewGuid().ToString(),
                            FolderPath = "",
                            Category = MapNexusCategory((int?)item["category_id"] ?? 0)
                        });
                    }
                    if (data.Count > 0) { Speak($"Found {data.Count} mods."); listDiscovery.SelectedIndex = 0; }
                    else Speak("No mods found.");
                } else Speak("Discovery failed.");
            } catch (Exception ex) { MessageBox.Show("Discovery Error: " + ex.Message); }
            finally { SetStatus("Connected as " + nexusUser); }
        }

        private string MapNexusCategory(int id)
        {
            switch (id) {
                case 1: return "Expansion";
                case 2: return "NPC";
                case 3: return "Portrait";
                case 4: return "Map";
                case 5: return "Crafting";
                case 6: return "Gameplay";
                case 7: return "Visual";
                case 8: return "Audio";
                default: return "General";
            }
        }

        private string DetectCategory(string name, string desc)
        {
            string full = (name + " " + desc).ToLower();
            if (full.Contains("expansion") || full.Contains("content pack")) return "Expansion";
            if (full.Contains("npc") || full.Contains("character")) return "NPC";
            if (full.Contains("portrait") || full.Contains("sprite")) return "Portrait";
            if (full.Contains("farm") || full.Contains("map") || full.Contains("location")) return "Map";
            if (full.Contains("craft") || full.Contains("machine") || full.Contains("item")) return "Crafting";
            if (full.Contains("audio") || full.Contains("music") || full.Contains("sound")) return "Audio";
            if (full.Contains("visual") || full.Contains("recolor") || full.Contains("texture")) return "Visual";
            return "General";
        }

        private void Speak(string text) { if (Tolk.IsLoaded()) Tolk.Output(text); }

        private async Task RunLoadingLoop() { while (_isLoading) { PlayAppSound("loading_indicator"); await Task.Delay(1500); } }

        private void ShowSoundDemo()
        {
            this.Hide();
            string previewTheme = _settings.CurrentTheme;
            Form demoForm = new Form { Text = "Sound Demo - Escape to Close", Size = new Size(500, 600), StartPosition = FormStartPosition.CenterScreen, KeyPreview = true };
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            ComboBox cmbTheme = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 12) };
            cmbTheme.Items.AddRange(Directory.GetDirectories(themesPath).Select(Path.GetFileName).Cast<object>().ToArray());
            cmbTheme.SelectedItem = previewTheme;
            cmbTheme.SelectedIndexChanged += (s, pe) => previewTheme = cmbTheme.SelectedItem?.ToString() ?? "Default";

            layout.Controls.Add(new Label { Text = "Preview Theme:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, 0, 0);
            layout.Controls.Add(cmbTheme, 0, 1);

            ListBox lb = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12), AccessibleName = "Sound List" };
            foreach (var key in _soundDescriptions.Keys) lb.Items.Add(key);
            lb.SelectedIndexChanged += async (s, pe) => {
                if (lb.SelectedItem != null) {
                    string key = lb.SelectedItem.ToString()!;
                    await Task.Delay(100); Speak($"{key}. {_soundDescriptions[key]}. {lb.SelectedIndex + 1} of {lb.Items.Count}");
                }
            };
            lb.KeyDown += (s, pe) => {
                if (pe.KeyCode == Keys.Enter && lb.SelectedItem != null) PlayAppSound(lb.SelectedItem.ToString()!, previewTheme);
                if (pe.KeyCode == Keys.Escape) demoForm.Close();
            };
            demoForm.FormClosing += (s, pe) => this.Show();
            layout.Controls.Add(lb, 0, 2);
            demoForm.Controls.Add(layout);
            demoForm.ShowDialog();
        }

        private void ShowThemeManager()
        {
            Form f = new Form { Text = "Audio Theme Manager - Escape to Cancel", Size = new Size(500, 600), StartPosition = FormStartPosition.CenterScreen, KeyPreview = true };
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), RowCount = 6 };
            string tempActiveTheme = _settings.CurrentTheme;

            ListBox lb = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12), AccessibleName = "Installed Themes" };
            void RefreshList() {
                lb.Items.Clear();
                lb.Items.AddRange(Directory.GetDirectories(themesPath).Select(Path.GetFileName).Cast<object>().ToArray());
                lb.SelectedItem = tempActiveTheme;
            }
            RefreshList();

            Button bSetActive = new Button { Text = "Set Selected as Active Theme", Dock = DockStyle.Top, Height = 35 };
            bSetActive.Click += (s, pe) => {
                if (lb.SelectedItem != null) {
                    tempActiveTheme = lb.SelectedItem.ToString()!;
                    Speak($"Active theme changed to {tempActiveTheme}. Press Save to confirm.");
                }
            };

            Button bCreate = new Button { Text = "Create New Theme", Dock = DockStyle.Top, Height = 35 };
            bCreate.Click += (s, pe) => {
                string name = Interaction.InputBox("Enter name for new theme:", "Create Theme");
                if (!string.IsNullOrEmpty(name)) {
                    string path = Path.Combine(themesPath, name);
                    if (!Directory.Exists(path)) {
                        Directory.CreateDirectory(path);
                        foreach (var sub in _soundDescriptions.Keys) Directory.CreateDirectory(Path.Combine(path, sub));
                        Directory.CreateDirectory(Path.Combine(path, "logo"));
                        Speak("Theme created. Drop your .ogg files into the theme folders.");
                        System.Diagnostics.Process.Start("explorer.exe", path);
                        RefreshList();
                    }
                }
            };

            Button bUpdate = new Button { Text = "Add Missing Folders to All Themes", Dock = DockStyle.Top, Height = 35 };
            bUpdate.Click += (s, pe) => {
                int added = 0;
                foreach (var themeDir in Directory.GetDirectories(themesPath)) {
                    foreach (var soundKey in _soundDescriptions.Keys) {
                        string subPath = Path.Combine(themeDir, soundKey);
                        if (!Directory.Exists(subPath)) {
                            Directory.CreateDirectory(subPath);
                            added++;
                        }
                    }
                }
                Speak($"Updated themes. Added {added} missing folders.");
            };

            Button bDelete = new Button { Text = "Delete Theme", Dock = DockStyle.Top, Height = 35 };
            bDelete.Click += (s, pe) => {
                if (lb.SelectedItem != null) {
                    string theme = lb.SelectedItem.ToString()!;
                    if (theme == "Default") { MessageBox.Show("Cannot delete Default theme."); return; }
                    if (MessageBox.Show($"Delete theme '{theme}'?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                        Directory.Delete(Path.Combine(themesPath, theme), true);
                        if (tempActiveTheme == theme) tempActiveTheme = "Default";
                        RefreshList(); Speak("Theme deleted.");
                    }
                }
            };

            FlowLayoutPanel pBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 50 };
            Button bSave = new Button { Text = "Save and Close", Width = 120, Height = 35 };
            bSave.Click += (s, pe) => {
                _settings.CurrentTheme = tempActiveTheme;
                _settings.Save(); f.Close(); Speak("Theme settings saved.");
            };
            Button bCancel = new Button { Text = "Cancel", Width = 100, Height = 35 };
            bCancel.Click += (s, pe) => { Speak("Changes cancelled."); f.Close(); };
            pBtns.Controls.AddRange(new Control[] { bSave, bCancel });

            layout.Controls.Add(lb, 0, 0);
            layout.Controls.Add(bSetActive, 0, 1);
            layout.Controls.Add(bCreate, 0, 2);
            layout.Controls.Add(bUpdate, 0, 3);
            layout.Controls.Add(bDelete, 0, 4);
            layout.Controls.Add(pBtns, 0, 5);
            f.Controls.Add(layout);
            f.KeyDown += (s, pe) => { if (pe.KeyCode == Keys.Escape) { Speak("Changes cancelled."); f.Close(); } };
            f.ShowDialog();
        }

        private void ShowShortcutManager()
        {
            Form f = new Form { Text = "Shortcut Customization - Escape to Cancel", Size = new Size(500, 600), StartPosition = FormStartPosition.CenterScreen, KeyPreview = true };
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), RowCount = 4 };
            
            var tempShortcuts = new Dictionary<string, Keys>(_settings.Shortcuts);

            ListBox lb = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12), AccessibleName = "Action List" };
            void RefreshList() {
                lb.Items.Clear();
                foreach (var s in tempShortcuts) lb.Items.Add($"{s.Key}: {GetShortcutStringForMap(tempShortcuts, s.Key)}");
            }
            RefreshList();

            Button bRemap = new Button { Text = "Remap Selected Action", Dock = DockStyle.Fill, Height = 35 };
            bRemap.Click += (s, pe) => {
                if (lb.SelectedItem != null) {
                    string action = lb.SelectedItem.ToString()!.Split(':')[0].Trim();
                    Speak($"Press the new key combination for {action}...");
                    Form prompt = new Form { Text = "Press Keys...", Size = new Size(300, 150), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, KeyPreview = true };
                    prompt.KeyDown += (ps, pe2) => {
                        if (pe2.KeyCode == Keys.ControlKey || pe2.KeyCode == Keys.ShiftKey || pe2.KeyCode == Keys.Menu) return;
                        tempShortcuts[action] = pe2.KeyData;
                        prompt.Close();
                        Speak($"{action} remapped. Press Save to confirm.");
                        RefreshList();
                    };
                    prompt.ShowDialog();
                }
            };

            Button bReset = new Button { Text = "Reset All to Defaults", Dock = DockStyle.Fill, Height = 35 };
            bReset.Click += (s, pe) => {
                if (MessageBox.Show("Reset all shortcuts to defaults?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                    tempShortcuts.Clear();
                    AppSettings dummy = new AppSettings();
                    dummy.InitializeDefaults();
                    foreach(var d in dummy.Shortcuts) tempShortcuts[d.Key] = d.Value;
                    RefreshList(); Speak("Reset to defaults. Press Save to confirm.");
                }
            };

            FlowLayoutPanel pBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 50 };
            Button bSave = new Button { Text = "Save and Close", Width = 120, Height = 35 };
            bSave.Click += (s, pe) => {
                _settings.Shortcuts = tempShortcuts;
                _settings.Save(); f.Close(); Speak("Shortcuts saved."); SetupAccessibleUI();
            };
            Button bCancel = new Button { Text = "Cancel", Width = 100, Height = 35 };
            bCancel.Click += (s, pe) => { Speak("Changes cancelled."); f.Close(); };
            pBtns.Controls.AddRange(new Control[] { bSave, bCancel });

            layout.Controls.Add(lb, 0, 0);
            layout.Controls.Add(bRemap, 0, 1);
            layout.Controls.Add(bReset, 0, 2);
            layout.Controls.Add(pBtns, 0, 3);
            f.Controls.Add(layout);
            f.KeyDown += (s, pe) => { if (pe.KeyCode == Keys.Escape) { Speak("Changes cancelled."); f.Close(); } };
            f.ShowDialog();
        }

        private string GetShortcutStringForMap(Dictionary<string, Keys> map, string action)
        {
            if (map.TryGetValue(action, out Keys keys)) {
                if (keys == Keys.None) return "Unmapped";
                var sb = new StringBuilder();
                if ((keys & Keys.Control) == Keys.Control) sb.Append("Ctrl+");
                if ((keys & Keys.Shift) == Keys.Shift) sb.Append("Shift+");
                if ((keys & Keys.Alt) == Keys.Alt) sb.Append("Alt+");
                sb.Append(keys & Keys.KeyCode);
                return sb.ToString();
            }
            return "Unmapped";
        }

        private void ShowManual()
        {
            string manualPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MANUAL.md");
            if (!File.Exists(manualPath)) { MessageBox.Show("Manual not found."); return; }
            string[] lines = File.ReadAllLines(manualPath);
            Dictionary<string, string> sections = new Dictionary<string, string>();
            string currentSection = "General"; StringBuilder sectionContent = new StringBuilder();
            foreach (var line in lines) {
                if (line.StartsWith("#")) {
                    if (sectionContent.Length > 0) sections[currentSection] = sectionContent.ToString();
                    currentSection = line.TrimStart('#').Trim(); sectionContent.Clear();
                } else sectionContent.AppendLine(line);
            }
            if (sectionContent.Length > 0) sections[currentSection] = sectionContent.ToString();
            
            var sbShortcuts = new StringBuilder();
            foreach (var s in _settings.Shortcuts) sbShortcuts.AppendLine($"* **{s.Key}**: {GetShortcutString(s.Key)}");
            sections["Current Key Mappings"] = sbShortcuts.ToString();

            this.Hide();
            Form manualForm = new Form { Text = "User Manual - Press Escape to Close", Size = new Size(800, 600), StartPosition = FormStartPosition.CenterScreen, KeyPreview = true };
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            ListBox lbToc = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12), AccessibleName = "Table of Contents" };
            foreach (var key in sections.Keys) lbToc.Items.Add(key);
            TextBox tbContent = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 12), AccessibleName = "Topic Information" };
            lbToc.SelectedIndexChanged += async (s, pe) => {
                if (lbToc.SelectedItem != null) {
                    string key = lbToc.SelectedItem.ToString()!; tbContent.Text = sections[key].Trim();
                    await Task.Delay(150); Speak($"{lbToc.SelectedIndex + 1} of {lbToc.Items.Count}"); 
                }
            };
            manualForm.KeyDown += (s, pe) => { if (pe.KeyCode == Keys.Escape) manualForm.Close(); };
            manualForm.FormClosing += (s, pe) => this.Show();
            layout.Controls.Add(lbToc, 0, 0); layout.Controls.Add(tbContent, 1, 0);
            manualForm.Controls.Add(layout);
            if (lbToc.Items.Count > 0) lbToc.SelectedIndex = 0;
            manualForm.ShowDialog();
        }

        private void LaunchGame()
        {
            try {
                string gameDir = Path.GetDirectoryName(_settings.ModsPath) ?? "";
                string smapiPath = Path.Combine(gameDir, "StardewModdingAPI.exe");
                if (!File.Exists(smapiPath)) {
                    string[] possible = { smapiPath, Path.Combine(gameDir, "Stardew Valley", "StardewModdingAPI.exe"),
                        @"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley\StardewModdingAPI.exe",
                        @"D:\SteamLibrary\steamapps\common\Stardew Valley\StardewModdingAPI.exe" };
                    smapiPath = possible.FirstOrDefault(p => File.Exists(p)) ?? "";
                }
                if (!string.IsNullOrEmpty(smapiPath)) {
                    SetStatus("Launching SMAPI...");
                    Process p = new Process();
                    p.StartInfo = new ProcessStartInfo(smapiPath) { WorkingDirectory = Path.GetDirectoryName(smapiPath) };
                    p.EnableRaisingEvents = true;
                    p.Exited += async (s, pe) => {
                        SetStatus("Game closed.");
                        await Task.Delay(5000);
                        SetStatus("Connected as " + nexusUser);
                    };
                    p.Start();
                    Task.Run(async () => {
                        await Task.Delay(3000);
                        if (!p.HasExited) SetStatus("Game is loaded and running.");
                    });
                } else MessageBox.Show("StardewModdingAPI.exe not found.");
            } catch (Exception ex) { MessageBox.Show("Launch failed: " + ex.Message); }
        }

        private void OpenRawSmapiLog()
        {
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs", "SMAPI-latest.txt");
            if (File.Exists(logPath)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", logPath) { UseShellExecute = true });
            else MessageBox.Show("SMAPI log not found.");
        }

        private void RefreshSmapiLog()
        {
            if (listLog == null) return;
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs", "SMAPI-latest.txt");
            if (!File.Exists(logPath)) return;

            _fullLogEntries.Clear();
            string filter = cmbLogFilter.SelectedItem?.ToString() ?? "Errors and Warnings";

            try {
                string[] lines = File.ReadAllLines(logPath);
                for (int i = 0; i < lines.Length; i++) {
                    string line = lines[i];
                    bool isError = line.Contains("[ERROR]");
                    bool isWarn = line.Contains("[WARN]");

                    bool shouldAdd = false;
                    if (filter == "Full Log") shouldAdd = true;
                    else if (filter == "Errors Only" && isError) shouldAdd = true;
                    else if (filter == "Errors and Warnings" && (isError || isWarn)) shouldAdd = true;
                    
                    if (shouldAdd) _fullLogEntries.Add(new LogEntry { Text = line, Index = i });
                }
            } catch { }

            listLog.BeginUpdate();
            listLog.Items.Clear();
            foreach (var entry in _fullLogEntries) listLog.Items.Add(entry);
            listLog.EndUpdate();
        }

        private void SearchSmapiLog()
        {
            string query = txtSearchLog.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(query)) {
                RefreshSmapiLog();
                return;
            }

            var matches = _fullLogEntries.Where(e => e.Text.ToLower().Contains(query)).ToList();
            listLog.BeginUpdate();
            listLog.Items.Clear();
            foreach (var m in matches) listLog.Items.Add(m);
            listLog.EndUpdate();
            Speak($"Found {matches.Count} results. Enter to jump to line in full view.");
            if (listLog.Items.Count > 0) listLog.SelectedIndex = 0;
        }

        private async Task UploadSmapiLog()
        {
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs", "SMAPI-latest.txt");
            if (!File.Exists(logPath)) return;

            SetStatus("Uploading log to SMAPI.io...");
            try {
                string logText = File.ReadAllText(logPath);
                var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("input", logText) });
                var res = await _httpClient.PostAsync("https://smapi.io/log/", content);
                if (res.IsSuccessStatusCode) {
                    string url = res.RequestMessage?.RequestUri?.ToString() ?? "https://smapi.io/log/";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                    Speak("Log uploaded successfully.");
                }
            } catch (Exception ex) { MessageBox.Show("Upload failed: " + ex.Message); }
            finally { SetStatus("Connected as " + nexusUser); }
        }

        private void CreateProfileFromCurrent()
        {
            string name = Interaction.InputBox("Enter name for this profile:", "Save Profile");
            if (string.IsNullOrEmpty(name)) return;

            var profile = new ModProfile { Name = name, ThemeOverride = _settings.CurrentTheme };
            foreach (StardewMod mod in _allInstalledMods) {
                profile.ModStates[mod.UniqueId] = mod.IsEnabled;
            }

            string json = JsonConvert.SerializeObject(profile, Formatting.Indented);
            File.WriteAllText(Path.Combine(profilesPath, name + ".json"), json);
            RefreshProfilesList();
            Speak("Profile saved.");
        }

        private void RefreshProfilesList()
        {
            if (listProfiles == null) return;
            listProfiles.BeginUpdate();
            listProfiles.Items.Clear();
            if (Directory.Exists(profilesPath)) {
                foreach (var file in Directory.GetFiles(profilesPath, "*.json")) {
                    try {
                        var p = JsonConvert.DeserializeObject<ModProfile>(File.ReadAllText(file));
                        if (p != null) listProfiles.Items.Add(p);
                    } catch { }
                }
            }
            listProfiles.EndUpdate();
        }

        private void ApplyProfile(ModProfile profile)
        {
            if (MessageBox.Show($"Apply profile '{profile.Name}'? This will enable and disable mods to match the saved setup.", "Apply Profile", MessageBoxButtons.YesNo) == DialogResult.No) return;

            try {
                SetStatus("Applying profile...");
                bool anyEnabled = false;
                bool anyDisabled = false;

                foreach (StardewMod mod in _allInstalledMods) {
                    if (profile.ModStates != null && profile.ModStates.ContainsKey(mod.UniqueId)) {
                        bool shouldEnable = profile.ModStates[mod.UniqueId];
                        if (mod.IsEnabled != shouldEnable) {
                            string parent = Path.GetDirectoryName(mod.FolderPath) ?? "";
                            string folderName = Path.GetFileName(mod.FolderPath);
                            string newPath = shouldEnable ? Path.Combine(parent, folderName.Substring(1)) : Path.Combine(parent, "." + folderName);
                            Directory.Move(mod.FolderPath, newPath);
                            mod.FolderPath = newPath;
                            mod.IsEnabled = shouldEnable;
                            if (shouldEnable) anyEnabled = true; else anyDisabled = true;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(profile.ThemeOverride) && Directory.Exists(Path.Combine(themesPath, profile.ThemeOverride)))
                {
                    _settings.CurrentTheme = profile.ThemeOverride;
                    _settings.Save();
                     Speak($"Theme switched to: {profile.ThemeOverride}");
                }

                RefreshModList(false);
                if (anyEnabled) PlayAppSound("enable");
                else if (anyDisabled) PlayAppSound("disable");
                else PlayAppSound("connect");

                SetStatus("Profile applied: " + profile.Name);
            } catch (Exception ex) { MessageBox.Show("Failed to apply profile: " + ex.Message); }
        }

        private async void UpdateAllMods()
        {
            if (isUpdatingAll || listUpdates.Items.Count == 0) return;
            if (!isPremium)
            {
                Speak("Update All is a Nexus Mods Premium feature. Free users must update mods individually via the browser.");
                MessageBox.Show("Updating multiple mods automatically requires a Nexus Mods Premium account. Free users must download and install updates one-by-one.", "Premium Feature Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show($"Update all {listUpdates.Items.Count} mods?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.No) return;
            isUpdatingAll = true;
            try {
                var mods = listUpdates.Items.Cast<StardewMod>().ToList();
                for (int i = 0; i < mods.Count; i++) {
                    SetStatus($"Updating ({i + 1}/{mods.Count}): {mods[i].Name}");
                    await DownloadAndInstallUpdate(mods[i], true);
                }
                Speak("All updates finished.");
            } finally { isUpdatingAll = false; RefreshModList(true); }
        }

        private void OpenModPage()
        {
            ListBox list;
            if (mainTabs.SelectedIndex == 0) list = listInstalled;
            else if (mainTabs.SelectedIndex == 1) list = listUpdates;
            else if (mainTabs.SelectedIndex == 3) list = listDiscovery;
            else return;

            if (list.SelectedItem is StardewMod mod && !string.IsNullOrEmpty(mod.NexusID))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"https://www.nexusmods.com/stardewvalley/mods/{mod.NexusID}") { UseShellExecute = true });
        }

        private void BackupMod(string folderPath, string modName)
        {
            try { if (!Directory.Exists(folderPath)) return;
                ZipFile.CreateFromDirectory(folderPath, Path.Combine(backupsPath, $"{modName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip"));
                PruneBackupsForMod(modName);
                RefreshBackupsList();
            } catch (Exception ex) { LogError(modName, "Backup Error: " + ex.Message); }
        }

        private void PruneBackupsForMod(string modName)
        {
            if (!Directory.Exists(backupsPath)) return;
            var files = Directory.GetFiles(backupsPath, $"{modName}_*.zip")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .ToList();

            if (files.Count > _settings.MaxBackupsPerMod)
            {
                for (int i = _settings.MaxBackupsPerMod; i < files.Count; i++)
                {
                    try { files[i].Delete(); } catch { }
                }
            }
        }

        private void PruneAllBackups()
        {
            if (!Directory.Exists(backupsPath)) return;
            var files = Directory.GetFiles(backupsPath, "*.zip");
            var groups = new Dictionary<string, List<FileInfo>>();

            foreach (var f in files)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (name.Length > 16 && name[name.Length - 16] == '_') name = name.Substring(0, name.Length - 16);
                if (!groups.ContainsKey(name)) groups[name] = new List<FileInfo>();
                groups[name].Add(new FileInfo(f));
            }

            int deletedCount = 0;
            foreach (var g in groups)
            {
                var sorted = g.Value.OrderByDescending(f => f.CreationTime).ToList();
                if (sorted.Count > _settings.MaxBackupsPerMod)
                {
                    for (int i = _settings.MaxBackupsPerMod; i < sorted.Count; i++)
                    {
                        try { sorted[i].Delete(); deletedCount++; } catch { }
                    }
                }
            }
            RefreshBackupsList();
            Speak($"Pruning complete. Deleted {deletedCount} old backups.");
        }

        private async Task HandleNxmUrl(string url)
        {
            try {
                SetStatus("Parsing Nexus Link...");
                var uri = new Uri(url); var segs = uri.AbsolutePath.Split('/');
                string modId = segs[2]; string fileId = segs[4];
                string apiUrl = $"https://api.nexusmods.com/v1/games/stardewvalley/mods/{modId}/files/{fileId}/download_link.json{uri.Query}";
                var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                req.Headers.Add("apikey", _settings.ApiKey); req.Headers.Add("User-Agent", "StardewAccessibleManager/1.0.0");
                var res = await _httpClient.SendAsync(req);
                string dlUri = JArray.Parse(await res.Content.ReadAsStringAsync())[0]["URI"]?.ToString() ?? "";
                var mReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.nexusmods.com/v1/games/stardewvalley/mods/{modId}/files.json");
                mReq.Headers.Add("apikey", _settings.ApiKey); mReq.Headers.Add("User-Agent", "StardewAccessibleManager/1.0.0");
                var mRes = await _httpClient.SendAsync(mReq);
                var filesData = JObject.Parse(await mRes.Content.ReadAsStringAsync());
                string realName = filesData["files"]?.FirstOrDefault(f => f["file_id"]?.ToString() == fileId)?["file_name"]?.ToString() ?? $"{modId}_file_{fileId}.zip";
                SetStatus($"Downloading {realName}...");
                byte[] bytes = await _httpClient.GetByteArrayAsync(dlUri);
                string path = Path.Combine(downloadsPath, realName); File.WriteAllBytes(path, bytes);
                PlayAppSound("connect");
                if (MessageBox.Show($"Downloaded {realName}. Install now?", "Success", MessageBoxButtons.YesNo) == DialogResult.Yes) InstallFromZip(path);
            } catch (Exception ex) { MessageBox.Show("NXM Error: " + ex.Message); }
            finally { SetStatus("Connected as " + nexusUser); }
        }

        private void ManualInstall()
        {
            using (OpenFileDialog ofd = new OpenFileDialog { InitialDirectory = downloadsPath, Filter = "Zips|*.zip" })
                if (ofd.ShowDialog() == DialogResult.OK) InstallFromZip(ofd.FileName);
        }

        private bool IsNewerVersion(string? current, string? target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            if (string.IsNullOrEmpty(current)) return true;
            var c = current.Split('.'); var t = target.Split('.');
            for (int i = 0; i < Math.Max(c.Length, t.Length); i++) {
                int cn = i < c.Length && int.TryParse(c[i], out int x) ? x : 0;
                int tn = i < t.Length && int.TryParse(t[i], out int y) ? y : 0;
                if (tn > cn) return true; if (cn > tn) return false;
            }
            return false;
        }

        private void InstallFromZip(string zipPath)
        {
            try {
                string temp = Path.Combine(Path.GetTempPath(), "StardewExtract_" + Path.GetRandomFileName());
                Directory.CreateDirectory(temp); ZipFile.ExtractToDirectory(zipPath, temp);
                string? mPath = Directory.GetFiles(temp, "manifest.json", SearchOption.AllDirectories).FirstOrDefault();
                if (string.IsNullOrEmpty(mPath)) throw new Exception("No manifest.json found.");
                var m = JObject.Parse(File.ReadAllText(mPath)); 
                string uid = (string?)m["UniqueID"] ?? Guid.NewGuid().ToString(); 
                string name = (string?)m["Name"] ?? "Unknown"; 
                string ver = (string?)m["Version"] ?? "0";
                var existing = _allInstalledMods.FirstOrDefault(mod => mod.UniqueId == uid);
                if (existing != null) {
                    if (existing.Version == ver && MessageBox.Show("Reinstall same version?", "Match", MessageBoxButtons.YesNo) == DialogResult.No) return;
                    if (!IsNewerVersion(existing.Version, ver) && MessageBox.Show("Install older version?", "Downgrade", MessageBoxButtons.YesNo) == DialogResult.No) return;
                    BackupMod(existing.FolderPath, name); Directory.Delete(existing.FolderPath, true);
                }
                string modRoot = Path.GetDirectoryName(mPath) ?? temp; 
                string target = Path.Combine(_settings.ModsPath, Path.GetFileName(modRoot));
                if (modRoot.TrimEnd('\\') == temp.TrimEnd('\\')) target = Path.Combine(_settings.ModsPath, name.Replace(" ", ""));
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.CreateDirectory(target);
                foreach (string dir in Directory.GetDirectories(modRoot, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(dir.Replace(modRoot, target));
                foreach (string file in Directory.GetFiles(modRoot, "*.*", SearchOption.AllDirectories)) File.Copy(file, file.Replace(modRoot, target), true);
                Directory.Delete(temp, true); PlayAppSound("connect"); MessageBox.Show($"{name} installed!"); RefreshModList(false); 
            } catch (Exception ex) { MessageBox.Show("Install failed: " + ex.Message); }
        }

        private void LogError(string mod, string msg) { try { File.AppendAllText("mod_manager_log.txt", $"[{DateTime.Now:HH:mm:ss}] {mod}: {msg}\n"); } catch { } }

        private void PlayAppSound(string name, string? themeOverride = null)
        {
            Task.Run(() => {
                try {
                    string theme = themeOverride ?? _settings.CurrentTheme;
                    string p = Path.Combine(themesPath, theme, name);
                    if (!Directory.Exists(p)) p = Path.Combine(themesPath, "Default", name); 

                    if (Directory.Exists(p)) {
                        string[] f = Directory.GetFiles(p, "*.ogg");
                        if (f.Length > 0) {
                            using (var r = new VorbisWaveReader(f[0])) using (var o = new WaveOutEvent()) {
                                o.Volume = _settings.SoundVolume / 100f;
                                o.Init(r); o.Play(); while (o.PlaybackState == PlaybackState.Playing) Thread.Sleep(100);
                            }
                        }
                    }
                } catch { }
            });
        }

        private async void RefreshModList(bool checkUpdates)
        {
            if (string.IsNullOrEmpty(_settings.ApiKey)) { 
                if (!_isSettingsOpen) {
                    SetStatus("Authentication Required");
                    PlayAppSound("disconnect"); 
                    ShowSettings(); 
                }
                return; 
            }
            
            SetStatus("Connecting to Nexus...");
            if (!await ValidateNexusConnection()) { 
                SetStatus("Authentication Failed - Check API Key"); 
                PlayAppSound("error"); 
                return; 
            }
            
            SetStatus("Connected as " + nexusUser); 
            PlayAppSound("connect");
            listInstalled.BeginUpdate(); if (checkUpdates) listUpdates.BeginUpdate();
            listInstalled.Items.Clear(); if (checkUpdates) listUpdates.Items.Clear();
            _allInstalledMods.Clear();
            if (!Directory.Exists(_settings.ModsPath)) { listInstalled.EndUpdate(); if (checkUpdates) listUpdates.EndUpdate(); MessageBox.Show("Mods path invalid."); return; }
            var manifests = Directory.GetFiles(_settings.ModsPath, "manifest.json", SearchOption.AllDirectories);
            string mapPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mod_id_map.json");
            var mapObj = (File.Exists(mapPath) ? JObject.Parse(File.ReadAllText(mapPath)) : new JObject()) ?? new JObject();
            
            HashSet<string> uniqueCategories = new HashSet<string>();

            foreach (string p in manifests) {
                try {
                    var m = JObject.Parse(File.ReadAllText(p));
                    var mod = new StardewMod {
                        Name = (string?)m["Name"] ?? "Unknown", 
                        Version = (string?)m["Version"] ?? "0", 
                        Author = (string?)m["Author"] ?? "User", 
                        UniqueId = (string?)m["UniqueID"] ?? Guid.NewGuid().ToString(),
                        Description = (string?)m["Description"] ?? "", 
                        NexusID = ParseNexusId(m["UpdateKeys"]), 
                        FolderPath = Path.GetDirectoryName(p) ?? "", 
                        IsEnabled = !Path.GetFileName(Path.GetDirectoryName(p) ?? "").StartsWith(".")
                    };
                    if (m["Dependencies"] is JArray deps) {
                        foreach (var d in deps) {
                            if (d == null) continue;
                            mod.Dependencies.Add(new ModDependency { 
                                UniqueId = (string?)d["UniqueID"] ?? "Unknown", 
                                MinimumVersion = (string?)d["MinimumVersion"], 
                                IsRequired = (bool?)d["IsRequired"] ?? true 
                            });
                        }
                    }
                    string? modId = mod.UniqueId;
                    if (modId != null && mapObj.TryGetValue(modId, out JToken? token)) 
                    {
                        mod.NexusID = token?.ToString();
                    }
                    
                    if (_settings.ModCategories.ContainsKey(mod.UniqueId)) mod.Category = _settings.ModCategories[mod.UniqueId];
                    else mod.Category = DetectCategory(mod.Name, mod.Description);
                    
                    uniqueCategories.Add(mod.Category);
                    _allInstalledMods.Add(mod);
                } catch (Exception ex) { LogError(p, "Parse Error: " + ex.Message); }
            }

            cmbCategoryFilter.BeginUpdate();
            string currentSelection = cmbCategoryFilter.SelectedItem?.ToString() ?? "All Categories";
            cmbCategoryFilter.Items.Clear();
            cmbCategoryFilter.Items.Add("All Categories");
            foreach (var cat in uniqueCategories.OrderBy(c => c)) cmbCategoryFilter.Items.Add(cat);
            if (cmbCategoryFilter.Items.Contains(currentSelection)) cmbCategoryFilter.SelectedItem = currentSelection;
            else cmbCategoryFilter.SelectedIndex = 0;
            cmbCategoryFilter.EndUpdate();

            foreach (var mod in _allInstalledMods) {
                foreach (var dep in mod.Dependencies) {
                    var match = _allInstalledMods.FirstOrDefault(m => m.UniqueId == dep.UniqueId);
                    if (match != null) { dep.IsPresent = true; dep.IsEnabled = match.IsEnabled; dep.IsNewEnough = IsNewerVersion(dep.MinimumVersion, match.Version) || dep.MinimumVersion == match.Version; }
                }
            }

            RebuildInstalledListBox();
            listInstalled.EndUpdate(); if (checkUpdates) listUpdates.BeginUpdate();
            if (checkUpdates) { var groups = _allInstalledMods.Where(m => !string.IsNullOrEmpty(m.NexusID)).GroupBy(m => m.NexusID).ToList(); _isLoading = true; _activeChecks = groups.Count; Speak("Checking for updates."); _ = RunLoadingLoop(); foreach (var g in groups) _ = CheckForUpdates(g.ToList()); }
        }

        private void RebuildInstalledListBox()
        {
            string query = txtSearchInstalled.Text.Trim().ToLower();
            string category = cmbCategoryFilter.SelectedItem?.ToString() ?? "All Categories";
            bool isSearching = !string.IsNullOrEmpty(query) || category != "All Categories";

            listInstalled.BeginUpdate();
            var selectedMod = listInstalled.SelectedItem as StardewMod;
            listInstalled.Items.Clear();

            // Group by the immediate folder name under the "Mods" folder
            var groups = _allInstalledMods.Where(m => !m.IsGroup).GroupBy(m => {
                string rel = Path.GetRelativePath(_settings.ModsPath, m.FolderPath);
                int idx = rel.IndexOf(Path.DirectorySeparatorChar);
                return idx == -1 ? rel : rel.Substring(0, idx);
            }).OrderBy(g => g.Key);

            foreach (var g in groups)
            {
                var folderMods = g.ToList();
                var visibleMods = folderMods.Where(m => 
                    (string.IsNullOrEmpty(query) || m.Name.ToLower().Contains(query) || m.Author.ToLower().Contains(query)) &&
                    (category == "All Categories" || m.Category == category)
                ).ToList();

                if (visibleMods.Count == 0) continue;

                if (isSearching || folderMods.Count == 1)
                {
                    foreach (var m in visibleMods)
                    {
                        m.IsSubMod = false; m.IsGroup = false;
                        listInstalled.Items.Add(m);
                    }
                }
                else
                {
                    bool isExpanded = _expandedGroups.Contains(g.Key);
                    var groupEntry = new StardewMod { 
                        IsGroup = true, 
                        GroupName = g.Key, 
                        IsExpanded = isExpanded,
                        UniqueId = "GROUP:" + g.Key,
                        SubMods = folderMods,
                        FolderPath = Path.Combine(_settings.ModsPath, g.Key)
                    };
                    
                    listInstalled.Items.Add(groupEntry);

                    if (isExpanded)
                    {
                        foreach (var m in visibleMods)
                        {
                            m.IsSubMod = true; m.IsGroup = false;
                            listInstalled.Items.Add(m);
                        }
                    }
                }
            }

            if (selectedMod != null)
            {
                for (int i = 0; i < listInstalled.Items.Count; i++)
                {
                    var item = listInstalled.Items[i] as StardewMod;
                    if (item != null && item.UniqueId == selectedMod.UniqueId)
                    {
                        listInstalled.SelectedIndex = i;
                        break;
                    }
                }
            }

            listInstalled.EndUpdate();
        }

        private void RefreshBackupsList()
        {
            if (listBackups == null) return;
            listBackups.BeginUpdate(); listBackups.Items.Clear();
            if (Directory.Exists(backupsPath)) {
                var files = Directory.GetFiles(backupsPath, "*.zip");
                foreach (var file in files) {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (name.Length > 16 && name[name.Length - 16] == '_') name = name.Substring(0, name.Length - 16);
                    listBackups.Items.Add(new BackupItem { Name = name, FullPath = file });
                }
            }
            listBackups.EndUpdate();
        }

        private async Task<bool> ValidateNexusConnection()
        {
            try {
                var req = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/users/validate.json");
                req.Headers.Add("apikey", _settings.ApiKey); req.Headers.Add("User-Agent", "StardewAccessibleManager/1.0.0");
                var res = await _httpClient.SendAsync(req);
                if (res.IsSuccessStatusCode) {
                    var d = JObject.Parse(await res.Content.ReadAsStringAsync());
                    nexusUser = d["name"]?.ToString() ?? "User"; isPremium = (bool)(d["is_premium"] ?? false); return true;
                }
            } catch { }
            return false;
        }

        private void PromptForApiKey() { string v = Interaction.InputBox("Paste API Key:", "Nexus Login", _settings.ApiKey); if (!string.IsNullOrEmpty(v)) { _settings.ApiKey = v.Trim(); _settings.Save(); RefreshModList(true); } }

        private void SetManualNexusId() {
            if (listInstalled.SelectedItem is StardewMod mod) {
                string? currentId = mod.NexusID ?? "";
                string v = Interaction.InputBox($"Nexus ID for {mod.Name}:", "Manual ID", currentId);
                if (!string.IsNullOrEmpty(v) && long.TryParse(v, out _)) {
                    string mapPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mod_id_map.json");
                    var map = (File.Exists(mapPath) ? JObject.Parse(File.ReadAllText(mapPath)) : new JObject()) ?? new JObject();
                    map[mod.UniqueId] = v.Trim(); File.WriteAllText(mapPath, map.ToString()); RefreshModList(false); 
                }
            }
        }

        private void SetManualCategory()
        {
            if (listInstalled.SelectedItem is StardewMod mod)
            {
                string v = Interaction.InputBox($"Assign category for {mod.Name}:", "Change Category", mod.Category);
                if (!string.IsNullOrEmpty(v))
                {
                    _settings.ModCategories[mod.UniqueId] = v.Trim();
                    _settings.Save();
                    RefreshModList(false);
                    Speak($"Category for {mod.Name} set to {v}.");
                }
            }
        }

        private void BatchManageCategory()
        {
            string category = cmbCategoryFilter.SelectedItem?.ToString() ?? "All Categories";
            var targetMods = _allInstalledMods.Where(m => category == "All Categories" || m.Category == category).ToList();
            
            if (targetMods.Count == 0) { Speak("No mods in this category."); return; }

            DialogResult dr = MessageBox.Show($"Batch Action for category '{category}':\n\nYES: Enable all {targetMods.Count} mods.\nNO: Disable all {targetMods.Count} mods.\nCANCEL: Do nothing.", "Batch Category Management", MessageBoxButtons.YesNoCancel);
            
            if (dr == DialogResult.Cancel) return;
            bool shouldEnable = (dr == DialogResult.Yes);

            try {
                SetStatus($"Batch {(shouldEnable ? "Enabling" : "Disabling")} {targetMods.Count} mods...");
                foreach (var mod in targetMods)
                {
                    if (mod.IsEnabled != shouldEnable)
                    {
                        string parent = Path.GetDirectoryName(mod.FolderPath) ?? "";
                        string folderName = Path.GetFileName(mod.FolderPath);
                        string newPath = shouldEnable ? Path.Combine(parent, folderName.Substring(1)) : Path.Combine(parent, "." + folderName);
                        Directory.Move(mod.FolderPath, newPath);
                        mod.FolderPath = newPath;
                        mod.IsEnabled = shouldEnable;
                    }
                }
                RefreshModList(false);
                if (shouldEnable) PlayAppSound("enable"); else PlayAppSound("disable");
                Speak($"Batch action complete. {targetMods.Count} mods {(shouldEnable ? "Enabled" : "Disabled")}.");
            } catch (Exception ex) { MessageBox.Show("Batch action failed: " + ex.Message); }
        }

        private void ShowDependencies() {
            if (listInstalled.SelectedItem is StardewMod mod) {
                if (mod.Dependencies.Count == 0) { MessageBox.Show("No dependencies."); return; }
                var sb = new StringBuilder($"Dependencies for {mod.Name}:\n");
                foreach (var d in mod.Dependencies) sb.AppendLine($"- {d.UniqueId}: {(d.IsPresent ? (d.IsEnabled ? (d.IsNewEnough ? "OK" : "Old") : "Disabled") : "Missing")} ({(d.IsRequired ? "Req" : "Opt")})");
                MessageBox.Show(sb.ToString(), "Dependencies");
            }
        }

        private void QuickFixDependencies()
        {
            if (listInstalled.SelectedItem is StardewMod mod)
            {
                var missing = mod.Dependencies.Where(d => d.IsRequired && !d.IsPresent).ToList();
                if (missing.Count == 0)
                {
                    Speak("No missing required dependencies for this mod.");
                    return;
                }

                var dep = missing[0];
                if (MessageBox.Show($"Search for missing dependency: {dep.UniqueId}?", "Quick-Fix", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    mainTabs.SelectedIndex = 3; // Discovery
                    txtSearch.Text = dep.UniqueId;
                    _ = RunDiscovery();
                }
            }
            else if (mainTabs.SelectedIndex == 5 && listLog.SelectedItem != null)
            {
                string line = listLog.SelectedItem.ToString()!;
                string missingId = LogAnalyzer.ExtractMissingModId(line);
                if (!string.IsNullOrEmpty(missingId))
                {
                    if (MessageBox.Show($"Search for missing dependency: {missingId}?", "Quick-Fix from Log", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        mainTabs.SelectedIndex = 3; // Discovery
                        txtSearch.Text = missingId;
                        _ = RunDiscovery();
                    }
                }
                else Speak("Could not identify a missing mod in this log entry.");
            }
        }

        private void ToggleModStatus() {
            if (listInstalled.SelectedItem is StardewMod mod) {
                try {
                    string parent = Path.GetDirectoryName(mod.FolderPath) ?? ""; 
                    string name = Path.GetFileName(mod.FolderPath);
                    string newPath = mod.IsEnabled ? Path.Combine(parent, "." + name) : Path.Combine(parent, name.StartsWith(".") ? name.Substring(1) : name);
                    Directory.Move(mod.FolderPath, newPath); mod.FolderPath = newPath; mod.IsEnabled = !mod.IsEnabled;
                    RefreshModList(false);
                    if (mod.IsEnabled) PlayAppSound("enable"); else PlayAppSound("disable");
                    SetStatus($"{mod.Name} is now {(mod.IsEnabled ? "Enabled" : "Disabled")}");
                } catch (Exception ex) { MessageBox.Show("Toggle failed: " + ex.Message); }
            }
        }

        private void DeleteSelectedMod() {
            if (listInstalled.SelectedItem is StardewMod mod) {
                if (MessageBox.Show($"Are you sure you want to PERMANENTLY DELETE {mod.Name}?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) {
                    try { BackupMod(mod.FolderPath, mod.Name + "_Delete"); Directory.Delete(mod.FolderPath, true); PlayAppSound("disable"); SetStatus($"Deleted {mod.Name}"); RefreshModList(false); 
                    } catch (Exception ex) { PlayAppSound("error"); MessageBox.Show("Failed to delete mod: " + ex.Message); }
                }
            }
        }

        private void DeleteSelectedBackup() {
            if (listBackups.SelectedItem is BackupItem item) {
                if (MessageBox.Show($"Permanently delete backup {item.Name}?", "Confirm Delete", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                    try { File.Delete(item.FullPath); PlayAppSound("disable"); RefreshBackupsList(); } catch (Exception ex) { MessageBox.Show("Delete failed: " + ex.Message); }
                }
            }
        }

        private void ShowSettings()
        {
            if (_isSettingsOpen) return;
            _isSettingsOpen = true;

            Form f = new Form { Text = "Settings - Escape to Cancel", Size = new Size(500, 750), StartPosition = FormStartPosition.CenterScreen, KeyPreview = true };
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(15), RowCount = 12 };
            
            layout.Controls.Add(new Label { Text = "Mods Folder Path:", AutoSize = true }, 0, 0);
            Panel pPath = new Panel { Dock = DockStyle.Fill, Height = 35 };
            TextBox tPath = new TextBox { Text = _settings.ModsPath, Width = 350, Font = new Font("Segoe UI", 10), AccessibleName = "Mods Folder Path" };
            Button bPath = new Button { Text = "Browse", Left = 360, Width = 80 };
            bPath.Click += (s, pe) => { using (FolderBrowserDialog fbd = new FolderBrowserDialog()) if (fbd.ShowDialog() == DialogResult.OK) tPath.Text = fbd.SelectedPath; };
            pPath.Controls.AddRange(new Control[] { tPath, bPath });
            layout.Controls.Add(pPath, 0, 1);

            layout.Controls.Add(new Label { Text = "Nexus API Key:", AutoSize = true, Padding = new Padding(0, 10, 0, 0) }, 0, 2);
            TextBox tKey = new TextBox { Text = _settings.ApiKey, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10), AccessibleName = "Nexus API Key" };
            layout.Controls.Add(tKey, 0, 3);

            FlowLayoutPanel pOpts = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 10, 0, 0), AutoSize = true };
            CheckBox cSplash = new CheckBox { Text = "Show Splash Screen on Startup", Checked = _settings.ShowSplashScreen, AutoSize = true, AccessibleName = "Show Splash Screen" };
            CheckBox cRandomLogo = new CheckBox { Text = "Random Logo at Startup", Checked = _settings.RandomLogoStartup, AutoSize = true, AccessibleName = "Random Logo Startup", Visible = _settings.ShowSplashScreen };
            CheckBox cUpdates = new CheckBox { Text = "Check for Mod Updates at Startup", Checked = _settings.CheckForUpdatesAtStartup, AutoSize = true, AccessibleName = "Check Updates at Startup" };
            
            cSplash.CheckedChanged += (s, pe) => { 
                Speak(cSplash.Checked ? "Splash Screen Enabled" : "Splash Screen Disabled");
                cRandomLogo.Visible = cSplash.Checked;
            };
            cRandomLogo.CheckedChanged += (s, pe) => Speak(cRandomLogo.Checked ? "Random Logo Enabled" : "Random Logo Disabled");
            cUpdates.CheckedChanged += (s, pe) => Speak(cUpdates.Checked ? "Auto-updates Enabled" : "Auto-updates Disabled");
            
            pOpts.Controls.AddRange(new Control[] { cSplash, cRandomLogo, cUpdates });
            layout.Controls.Add(pOpts, 0, 4);

            layout.Controls.Add(new Label { Text = "Select Specific Logo:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, 0, 5);
            ComboBox cmbLogo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, AccessibleName = "Select Specific Logo" };
            
            void PreviewLogo() {
                if (cmbLogo.SelectedItem != null) {
                    string theme = _settings.CurrentTheme;
                    string file = cmbLogo.SelectedItem.ToString()!;
                    Task.Run(() => {
                        try {
                            string p = Path.Combine(themesPath, theme, "logo", file);
                            if (File.Exists(p)) {
                                using (var r = new VorbisWaveReader(p)) using (var o = new WaveOutEvent()) {
                                    o.Volume = (float)_settings.SoundVolume / 100f;
                                    o.Init(r); o.Play(); while (o.PlaybackState == PlaybackState.Playing) Thread.Sleep(100);
                                }
                            }
                        } catch { }
                    });
                }
            }

            void RefreshLogoList(string theme) {
                cmbLogo.Items.Clear();
                string path = Path.Combine(themesPath, theme, "logo");
                if (Directory.Exists(path)) {
                    var files = Directory.GetFiles(path, "*.ogg").Select(Path.GetFileName).Cast<object>().ToArray();
                    cmbLogo.Items.AddRange(files);
                    if (!string.IsNullOrEmpty(_settings.SelectedLogoFile) && cmbLogo.Items.Contains(_settings.SelectedLogoFile))
                        cmbLogo.SelectedItem = _settings.SelectedLogoFile;
                    else if (cmbLogo.Items.Count > 0) cmbLogo.SelectedIndex = 0;
                }
            }
            
            cmbLogo.SelectedIndexChanged += (s, pe) => {
                if (f.Visible) PreviewLogo();
            };
            cmbLogo.KeyDown += (s, pe) => {
                if (pe.KeyCode == Keys.Space && cmbLogo.SelectedItem != null) {
                    pe.Handled = true; pe.SuppressKeyPress = true;
                    PreviewLogo();
                }
            };
            layout.Controls.Add(cmbLogo, 0, 6);

            FlowLayoutPanel pVol = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            pVol.Controls.Add(new Label { Text = "Sound Volume (0-100):", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            NumericUpDown nVol = new NumericUpDown { Value = _settings.SoundVolume, Minimum = 0, Maximum = 100, Width = 60 };
            pVol.Controls.Add(nVol);
            layout.Controls.Add(pVol, 0, 7);

            FlowLayoutPanel pPrune = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            pPrune.Controls.Add(new Label { Text = "Max Backups per Mod:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            NumericUpDown nPrune = new NumericUpDown { Value = _settings.MaxBackupsPerMod, Minimum = 1, Maximum = 50, Width = 60 };
            pPrune.Controls.Add(nPrune);
            layout.Controls.Add(pPrune, 0, 8);

            FlowLayoutPanel pTheme = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            pTheme.Controls.Add(new Label { Text = "Current Audio Theme:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            ComboBox cTheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            cTheme.Items.AddRange(Directory.GetDirectories(themesPath).Select(Path.GetFileName).Cast<object>().ToArray());
            cTheme.SelectedItem = _settings.CurrentTheme;
            cTheme.SelectedIndexChanged += (s, pe) => RefreshLogoList(cTheme.SelectedItem?.ToString() ?? "Default");
            pTheme.Controls.Add(cTheme);
            layout.Controls.Add(pTheme, 0, 9);
            RefreshLogoList(_settings.CurrentTheme);

            Button bClearIgnored = new Button { Text = "Clear All Ignored Updates", Dock = DockStyle.Top, Height = 35 };
            bClearIgnored.Click += (s, pe) => { _settings.IgnoredVersions.Clear(); _settings.Save(); Speak("All ignored updates cleared."); RefreshModList(true); };
            layout.Controls.Add(bClearIgnored, 0, 10);

            FlowLayoutPanel pBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 50 };
            Button bSave = new Button { Text = "Save Settings", Width = 120, Height = 35 };
            bSave.Click += (s, pe) => {
                string mPath = tPath.Text.Trim();
                string aKey = tKey.Text.Trim();

                if (string.IsNullOrEmpty(mPath) || string.IsNullOrEmpty(aKey))
                {
                    Speak("Error: Mods path and API key are both required.");
                    MessageBox.Show("Please provide both the Stardew Valley Mods path and your Nexus API Key.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _settings.ModsPath = mPath; _settings.ApiKey = aKey;
                _settings.ShowSplashScreen = cSplash.Checked; 
                _settings.RandomLogoStartup = cRandomLogo.Checked;
                _settings.SelectedLogoFile = cmbLogo.SelectedItem?.ToString() ?? "";
                _settings.CheckForUpdatesAtStartup = cUpdates.Checked;
                _settings.SoundVolume = (int)nVol.Value;
                _settings.MaxBackupsPerMod = (int)nPrune.Value;
                _settings.CurrentTheme = cTheme.SelectedItem?.ToString() ?? "Default";
                _settings.Save(); 
                f.Close(); 
                
                // Use a slight delay to ensure the form is fully closed before refreshing
                Task.Delay(100).ContinueWith(_ => this.Invoke(new Action(() => RefreshModList(false))));
                Speak("Settings saved.");
            };
            Button bCancel = new Button { Text = "Cancel", Width = 100, Height = 35 };
            bCancel.Click += (s, pe) => { Speak("Changes cancelled."); f.Close(); };
            pBtns.Controls.AddRange(new Control[] { bSave, bCancel });

            f.FormClosing += (s, pe) => _isSettingsOpen = false;
            f.Controls.Add(pBtns);
            f.Controls.Add(layout);
            f.KeyDown += (s, pe) => { if (pe.KeyCode == Keys.Escape) { Speak("Changes cancelled."); f.Close(); } };
            f.ShowDialog();
        }

        private void Form1_KeyDown(object? sender, KeyEventArgs e) {
            if (IsShortcut(e, "Manual")) { e.SuppressKeyPress = true; ShowManual(); }
            if (IsShortcut(e, "ContextHelp")) { e.SuppressKeyPress = true; ShowContextHelp(); }
            if (IsShortcut(e, "LaunchGame")) { e.SuppressKeyPress = true; LaunchGame(); }
            if (IsShortcut(e, "OpenLogFile")) { e.SuppressKeyPress = true; OpenRawSmapiLog(); }
            if (IsShortcut(e, "Settings")) { e.SuppressKeyPress = true; ShowSettings(); }
            if (IsShortcut(e, "Login")) { e.SuppressKeyPress = true; PromptForApiKey(); }
            if (IsShortcut(e, "InstallZip")) { e.SuppressKeyPress = true; ManualInstall(); }
            if (IsShortcut(e, "OpenModPage")) { e.SuppressKeyPress = true; OpenModPage(); }
            if (IsShortcut(e, "OpenDownloads")) { e.SuppressKeyPress = true; System.Diagnostics.Process.Start("explorer.exe", downloadsPath); }
            if (IsShortcut(e, "OpenBackups")) { e.SuppressKeyPress = true; System.Diagnostics.Process.Start("explorer.exe", backupsPath); }
            if (IsShortcut(e, "ManualID")) { e.SuppressKeyPress = true; SetManualNexusId(); }
            if (IsShortcut(e, "ChangeCategory")) { 
                e.SuppressKeyPress = true; 
                if (e.Shift) BatchManageCategory(); else SetManualCategory(); 
            }
            if (IsShortcut(e, "ShowDependencies")) { e.SuppressKeyPress = true; ShowDependencies(); }
            if (IsShortcut(e, "QuickFix")) { e.SuppressKeyPress = true; QuickFixDependencies(); }
            if (IsShortcut(e, "Search")) { 
                e.SuppressKeyPress = true; 
                if (mainTabs.SelectedIndex == 0) txtSearchInstalled.Focus(); 
                else if (mainTabs.SelectedIndex == 5) txtSearchLog.Focus();
            }
            if (IsShortcut(e, "UpdateAll")) { e.SuppressKeyPress = true; UpdateAllMods(); }
            if (IsShortcut(e, "SaveProfile")) { e.SuppressKeyPress = true; CreateProfileFromCurrent(); }
            if (IsShortcut(e, "ReadDescription")) { e.SuppressKeyPress = true; ReadSelectedDescription(); }
            if (IsShortcut(e, "PruneBackups")) { e.SuppressKeyPress = true; PruneAllBackups(); }
            if (IsShortcut(e, "RefreshAll")) { e.SuppressKeyPress = true; RefreshModList(true); RefreshBackupsList(); RefreshProfilesList(); RefreshSmapiLog(); Speak("Refreshing everything."); }
            if (IsShortcut(e, "RefreshInstalled")) { e.SuppressKeyPress = true; RefreshModList(false); Speak("Refreshed installed mods."); }
            if (IsShortcut(e, "OpenErrorLog")) {
                if (File.Exists("mod_manager_log.txt"))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", "mod_manager_log.txt") { UseShellExecute = true });
            }
        }

        private void ReadSelectedDescription()
        {
            ListBox list;
            if (mainTabs.SelectedIndex == 0) list = listInstalled;
            else if (mainTabs.SelectedIndex == 1) list = listUpdates;
            else if (mainTabs.SelectedIndex == 3) list = listDiscovery;
            else return;

            if (list.SelectedItem is StardewMod mod)
            {
                if (!string.IsNullOrEmpty(mod.Description)) Speak(mod.Description);
                else Speak("No description available for this mod.");
            }
        }

        private async void List_KeyDown(object? sender, KeyEventArgs e) {
            if (sender is ListBox list) {
                // Multi-letter Search & Custom First-Letter Navigation
                if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z && !e.Control && !e.Alt)
                {
                    char targetChar = char.ToLower((char)e.KeyCode);
                    _searchTimer.Stop();
                    _searchBuffer += targetChar;
                    _searchTimer.Start();

                    int startIdx = list.SelectedIndex;
                    // If buffer is just one letter and same as before, cycle through single letters
                    if (_searchBuffer.Length == 1) startIdx++;

                    for (int i = 0; i < list.Items.Count; i++)
                    {
                        int checkIdx = (startIdx + i) % list.Items.Count;
                        if (list.Items[checkIdx] is StardewMod m)
                        {
                            string nameToCheck = (m.IsGroup ? m.GroupName : m.Name).ToLower();
                            if (!string.IsNullOrEmpty(nameToCheck) && nameToCheck.StartsWith(_searchBuffer))
                            {
                                list.SelectedIndex = checkIdx;
                                e.Handled = true; e.SuppressKeyPress = true;
                                return;
                            }
                        }
                    }
                    
                    // If no multi-letter match, try just the latest letter (fallback to standard cycling)
                    if (_searchBuffer.Length > 1)
                    {
                        string lastLetter = targetChar.ToString();
                        for (int i = 0; i < list.Items.Count; i++)
                        {
                            int checkIdx = (list.SelectedIndex + 1 + i) % list.Items.Count;
                            if (list.Items[checkIdx] is StardewMod m && (m.IsGroup ? m.GroupName : m.Name).ToLower().StartsWith(lastLetter))
                            {
                                list.SelectedIndex = checkIdx;
                                break;
                            }
                        }
                        _searchBuffer = lastLetter; // Reset buffer to just the last letter
                    }
                    
                    e.Handled = true; e.SuppressKeyPress = true;
                    return;
                }

                // Prevent Left/Right arrow from moving selection in the listbox entirely
                if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) {
                    e.Handled = true; e.SuppressKeyPress = true;
                }

                if (list.Name == "listInstalled" && list.SelectedItem is StardewMod mod) {
                    bool isExpand = (e.KeyCode == Keys.Right || e.KeyValue == 187 || e.KeyCode == Keys.Add);
                    bool isCollapse = (e.KeyCode == Keys.Left || e.KeyValue == 189 || e.KeyCode == Keys.Subtract);

                    if (mod.IsGroup)
                    {
                        string groupName = mod.GroupName;
                        if (isExpand) { 
                            if (!_expandedGroups.Contains(groupName)) { 
                                _expandedGroups.Add(groupName); 
                                RebuildInstalledListBox(); Speak("Expanded."); 
                            }
                        }
                        else if (isCollapse) { 
                            if (_expandedGroups.Contains(groupName)) { 
                                _expandedGroups.Remove(groupName); 
                                RebuildInstalledListBox(); Speak("Collapsed."); 
                            }
                        }
                    }
                    else if (mod.IsSubMod && isCollapse)
                    {
                        // Identify parent group from the folder path
                        string rel = Path.GetRelativePath(_settings.ModsPath, mod.FolderPath);
                        int idx = rel.IndexOf(Path.DirectorySeparatorChar);
                        string groupName = idx == -1 ? rel : rel.Substring(0, idx);

                        if (_expandedGroups.Contains(groupName))
                        {
                            _expandedGroups.Remove(groupName);
                            // Create a temporary "marker" to restore selection to the group header
                            var groupHeader = new StardewMod { UniqueId = "GROUP:" + groupName };
                            listInstalled.SelectedItem = null; // Clear sub-mod selection
                            listInstalled.Tag = groupHeader; // Use Tag as a hint for Rebuild
                            RebuildInstalledListBox();
                            // Find and select the group header
                            for (int i = 0; i < listInstalled.Items.Count; i++) {
                                if ((listInstalled.Items[i] as StardewMod)?.UniqueId == "GROUP:" + groupName) {
                                    listInstalled.SelectedIndex = i;
                                    break;
                                }
                            }
                            Speak($"{groupName} Collapsed.");
                        }
                        e.Handled = true; e.SuppressKeyPress = true;
                    }
                    if (e.KeyCode == Keys.Space) { ToggleModStatus(); e.Handled = true; e.SuppressKeyPress = true; }
                    if (e.KeyCode == Keys.Delete) { DeleteSelectedMod(); e.Handled = true; e.SuppressKeyPress = true; }
                    if (e.KeyCode == Keys.Apps) { MessageBox.Show($"Mod: {mod.Name}\nAuthor: {mod.Author}\nDescription: {mod.Description}", "Details"); e.Handled = true; }
                }
                if (list.Name == "listUpdates" && list.SelectedItem is StardewMod umod) {
                    if (e.KeyCode == Keys.Delete) {
                        if (MessageBox.Show($"Ignore version {umod.LatestVersion} for {umod.Name}?", "Ignore Update", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                            _settings.IgnoredVersions[umod.UniqueId] = umod.LatestVersion!;
                            _settings.Save();
                            listUpdates.Items.Remove(umod);
                            Speak("Update ignored.");
                        }
                        e.Handled = true; e.SuppressKeyPress = true;
                    }
                }
                if (list.Name == "listBackups" && list.SelectedItem is BackupItem item) {
                    if (e.KeyCode == Keys.Delete) { DeleteSelectedBackup(); e.Handled = true; e.SuppressKeyPress = true; }
                    if (e.KeyCode == Keys.Enter) { if (MessageBox.Show($"Restore backup {item.Name}?", "Confirm Restore", MessageBoxButtons.YesNo) == DialogResult.Yes) InstallFromZip(item.FullPath); e.Handled = true; }
                }
                if (list.Name == "listProfiles" && list.SelectedItem is ModProfile p) {
                    if (e.KeyCode == Keys.Enter) { ApplyProfile(p); e.Handled = true; }
                    if (e.KeyCode == Keys.Delete) {
                        if (MessageBox.Show($"Delete profile '{p.Name}'?", "Confirm Delete", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                            string profPath = Path.Combine(profilesPath, p.Name + ".json");
                            if (File.Exists(profPath)) File.Delete(profPath); 
                            RefreshProfilesList(); Speak("Profile deleted.");
                        }
                        e.Handled = true; e.SuppressKeyPress = true;
                    }
                }
                if (list.Name == "listLog" && e.KeyCode == Keys.Enter && list.SelectedItem is LogEntry entry) {
                    // Check if we are currently searching
                    if (list.Items.Count < _fullLogEntries.Count) {
                        // Jump back to full list
                        txtSearchLog.Text = "";
                        list.BeginUpdate();
                        list.Items.Clear();
                        foreach (var f in _fullLogEntries) list.Items.Add(f);
                        list.SelectedItem = entry;
                        list.EndUpdate();
                        Speak("Returned to filtered view. Scroll down to read more.");
                        e.Handled = true;
                        return;
                    }

                    string logLine = entry.Text;
                    string fix = LogAnalyzer.GetSuggestedFix(logLine);
                    string detail = logLine;
                    if (!string.IsNullOrEmpty(fix)) detail += "\n\nSUGGESTED FIX:\n" + fix;
                    MessageBox.Show(detail, "Log Detail"); e.Handled = true;
                }
                if (list.Name == "listLog" && IsShortcut(e, "Login")) { await UploadSmapiLog(); e.Handled = true; e.SuppressKeyPress = true; }

                if (e.KeyCode == Keys.Enter && (list.Name == "listUpdates" || list.Name == "listDiscovery")) { OpenModPage(); e.Handled = true; }
            }
        }

        private async Task CheckForUpdates(List<StardewMod> group) {
            await _apiSemaphore.WaitAsync();
            try {
                await Task.Delay(new Random().Next(100, 1000));
                var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.nexusmods.com/v1/games/stardewvalley/mods/{group[0].NexusID}.json");
                req.Headers.Add("apikey", _settings.ApiKey); req.Headers.Add("User-Agent", "StardewAccessibleManager/1.0.0");
                var res = await _httpClient.SendAsync(req);
                if (res.IsSuccessStatusCode) {
                    string ver = (string?)JObject.Parse(await res.Content.ReadAsStringAsync())["version"] ?? "0";
                    
                    if (_settings.IgnoredVersions.TryGetValue(group[0].UniqueId, out string? ignored) && ignored == ver)
                    {
                        return; 
                    }

                    StardewMod? rep = null;
                    foreach (var m in group) { m.LatestVersion = ver; if (IsNewerVersion(m.Version, ver)) rep ??= m; }
                    if (rep != null) this.Invoke(new Action(() => { listUpdates.BeginUpdate(); if (!listUpdates.Items.Contains(rep)) listUpdates.Items.Add(rep); listUpdates.EndUpdate(); }));
                }
            } finally { 
                _apiSemaphore.Release(); int remaining = Interlocked.Decrement(ref _activeChecks);
                if (remaining <= 0) { 
                    _isLoading = false; 
                    PlayAppSound("load_complete"); 
                    if (listUpdates.Items.Count > 0) Speak($"Update check complete. Found {listUpdates.Items.Count} mod updates."); 
                    else Speak("Update check complete. All mods are up-to-date."); 
                }
            }
        }

        private async Task DownloadAndInstallUpdate(StardewMod mod, bool silent = false) {
            if (!isPremium) { if (!silent) OpenModPage(); return; }
            try {
                var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.nexusmods.com/v1/games/stardewvalley/mods/{mod.NexusID}/files.json");
                req.Headers.Add("apikey", _settings.ApiKey); req.Headers.Add("User-Agent", "StardewAccessibleManager/1.0.0");
                var res = await _httpClient.SendAsync(req);
                var files = (JArray)JObject.Parse(await res.Content.ReadAsStringAsync())["files"]!;
                var f = files[0]; string fId = f["file_id"]!.ToString();
                string realName = f["file_name"]?.ToString() ?? $"{mod.NexusID}_update.zip";
                var lReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.nexusmods.com/v1/games/stardewvalley/mods/{mod.NexusID}/files/{fId}/download_link.json");
                lReq.Headers.Add("apikey", _settings.ApiKey); lReq.Headers.Add("User-Agent", "StardewAccessibleManager/1.0.0");
                var lRes = await _httpClient.SendAsync(lReq);
                string uri = JArray.Parse(await lRes.Content.ReadAsStringAsync())[0]["URI"]!.ToString();
                byte[] bytes = await _httpClient.GetByteArrayAsync(uri);
                string zipPath = Path.Combine(Path.GetTempPath(), realName); File.WriteAllBytes(zipPath, bytes);
                var existing = Directory.GetDirectories(_settings.ModsPath).FirstOrDefault(d => File.Exists(Path.Combine(d, "manifest.json")) && (string?)JObject.Parse(File.ReadAllText(Path.Combine(d, "manifest.json")))["UniqueID"] == mod.UniqueId);
                if (existing != null) { BackupMod(existing, mod.Name); Directory.Delete(existing, true); }
                ZipFile.ExtractToDirectory(zipPath, _settings.ModsPath, true); File.Delete(zipPath);
                if (!silent) { PlayAppSound("connect"); MessageBox.Show($"{mod.Name} updated!"); RefreshModList(true); }
            } catch (Exception ex) { MessageBox.Show("Update failed: " + ex.Message); }
        }

        private async void CheckForAppUpdates(bool manual)
        {
            if (manual) Speak("Checking for manager updates...");
            try
            {
                // Replace with your GitHub details once the repo is created
                string user = "SeanTerry01";
                string repo = "StardewAccessibleManager";
                
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StardewAccessibleManager/1.0.0");
                var res = await _httpClient.GetStringAsync($"https://api.github.com/repos/{user}/{repo}/releases/latest");
                var json = JObject.Parse(res);
                string remoteTag = json["tag_name"]?.ToString() ?? "v1.0.0";
                string remoteVer = remoteTag.StartsWith("v") ? remoteTag.Substring(1) : remoteTag;
                string localVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

                if (IsNewerVersion(localVer, remoteVer))
                {
                    PlayAppSound("connect");
                    Speak($"A new version of the manager is available: {remoteTag}.");
                    if (MessageBox.Show($"Version {remoteTag} is available! Would you like to open the download page?", "Update Available", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"https://github.com/{user}/{repo}/releases/latest") { UseShellExecute = true });
                    }
                }
                else if (manual)
                {
                    Speak("The manager is up to date.");
                    MessageBox.Show("You are running the latest version of Stardew Accessible Manager.", "Up to Date");
                }
            }
            catch (Exception ex)
            {
                if (manual)
                {
                    Speak("Update check failed.");
                    MessageBox.Show("Could not check for updates: " + ex.Message);
                }
            }
        }

        private string? ParseNexusId(JToken? keys) {
            if (keys == null) return null;
            IEnumerable<JToken>? list;
            if (keys.Type == JTokenType.Array) list = keys.Children();
            else if (keys.Type == JTokenType.String) list = new List<JToken> { keys };
            else return null;
            foreach (var k in list) {
                string s = k.ToString();
                if (s.Contains("Nexus:", StringComparison.OrdinalIgnoreCase)) {
                    var parts = s.Split(':');
                    if (parts.Length < 2) continue;
                    string id = parts[1].Trim();
                    if (id.Contains("@")) id = id.Split('@')[0].Trim();
                    if (long.TryParse(id, out _)) return id;
                }
            }
            return null;
        }
    }

    public class BackupItem { 
        public string Name { get; set; } = null!; 
        public string FullPath { get; set; } = null!; 
        public override string ToString() { return Name; } 
    }

    public class LogEntry {
        public string Text { get; set; } = "";
        public int Index { get; set; }
        public override string ToString() => Text;
    }
}