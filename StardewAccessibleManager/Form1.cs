using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using DavyKager;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using NAudio.Vorbis;
using NAudio.Wave;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StardewAccessibleManager;

public class Form1 : Form
{
	private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
	{
		AutomaticDecompression = (DecompressionMethods.GZip | DecompressionMethods.Deflate)
	});

	private static readonly string _appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.1";

	private AppSettings _settings;

	private static string dataBasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudiVentureGames", "StardewAccessibleManager");

	private string downloadsPath = Path.Combine(dataBasePath, "downloads");

	private string backupsPath = Path.Combine(dataBasePath, "backups");

	private string profilesPath = Path.Combine(dataBasePath, "profiles");

	private string themesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sounds");

	private bool isPremium;

	private string nexusUser = "Unknown User";

	private readonly SemaphoreSlim _apiSemaphore = new SemaphoreSlim(5);

	private bool isUpdatingAll;

	private CancellationTokenSource _pipeCts = new CancellationTokenSource();

	private int _activeChecks;

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

	private TabControl mainTabs;

	private TabPage tabInstalled;

	private TabPage tabUpdates;

	private TabPage tabDiscovery;

	private TabPage tabBackups;

	private TabPage tabProfiles;

	private TabPage tabSmapiLog;

	private TabPage tabWiki;

	private ListBox listInstalled;

	private ListBox listUpdates;

	private ListBox listDiscovery;

	private ListBox listBackups;

	private ListBox listProfiles;

	private ListBox listLog;

	private TextBox txtSearch;

	private TextBox txtSearchInstalled;

	private TextBox txtSearchLog;

	private ComboBox cmbDiscoveryType;

	private ComboBox cmbLogFilter;

	private ComboBox cmbCategoryFilter;

	private Button btnSearch;

	private Button btnLoadMoreDiscovery;

	private Button btnPruneBackups;

	private TextBox txtWikiSearch;

	private ComboBox cmbWikiCategories;

	private ListBox listWikiResults;

	private WebView2 webViewWiki;

	private SplitContainer splitWiki;

	private Stack<WikiNavigationState> wikiNavStack = new Stack<WikiNavigationState>();

	private IContainer components;

	private void DetectModsPath()
	{
		if (!string.IsNullOrEmpty(_settings.ModsPath) && Directory.Exists(_settings.ModsPath))
		{
			return;
		}
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
				_settings.ModsPath = text;
				_settings.Save();
				break;
			}
		}
	}

	public Form1(string[] args)
	{
		Form1 form = this;
		InitializeComponent();
		_settings = AppSettings.Load();
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
		StartNamedPipeServer(_pipeCts.Token);
		base.FormClosing += delegate
		{
			form._pipeCts.Cancel();
			form.PlayAppSound("disconnect");
			Thread.Sleep(800);
			if (Tolk.IsLoaded())
			{
				Tolk.Unload();
			}
		};
		base.Shown += async delegate
		{
			form.CheckForAppUpdates(manual: false);
			form.RefreshModList(form._settings.CheckForUpdatesAtStartup);
			form.RefreshBackupsList();
			form.RefreshProfilesList();
			form.RefreshSmapiLog();
			if (args.Length != 0 && args[0].StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
			{
				await form.HandleNxmUrl(args[0]);
			}
		};
	}

	private async Task StartNamedPipeServer(CancellationToken token)
	{
		_ = 2;
		try
		{
			while (!token.IsCancellationRequested)
			{
				try
				{
					using NamedPipeServerStream server = new NamedPipeServerStream("StardewAccessibleManager-Nexus-Handler", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
					await server.WaitForConnectionAsync(token);
					using StreamReader reader = new StreamReader(server);
					string url = await reader.ReadLineAsync();
					if (!string.IsNullOrEmpty(url))
					{
						Invoke(async delegate
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
		}
	}

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

	private void SetupAccessibleUI()
	{
		Text = "Stardew Valley Accessible Mod Manager";
		base.Size = new Size(1000, 700);
		base.KeyPreview = true;
		base.KeyDown += Form1_KeyDown;
		MenuStrip menuStrip = new MenuStrip();
		ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem("&File");
		toolStripMenuItem.DropDownItems.Add("Refresh All (" + GetShortcutString("RefreshAll") + ")", null, delegate
		{
			RefreshModList(checkUpdates: true);
			RefreshBackupsList();
			RefreshProfilesList();
			RefreshSmapiLog();
		});
		toolStripMenuItem.DropDownItems.Add("Refresh Installed Only (" + GetShortcutString("RefreshInstalled") + ")", null, delegate
		{
			RefreshModList(checkUpdates: false);
		});
		toolStripMenuItem.DropDownItems.Add("Check for Manager Updates", null, delegate
		{
			CheckForAppUpdates(manual: true);
		});
		toolStripMenuItem.DropDownItems.Add("Settings (" + GetShortcutString("Settings") + ")", null, delegate
		{
			ShowSettings();
		});
		toolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
		toolStripMenuItem.DropDownItems.Add("Exit", null, delegate
		{
			Application.Exit();
		});
		ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem("&Mods");
		toolStripMenuItem2.DropDownItems.Add("Save Current Setup as Profile (" + GetShortcutString("SaveProfile") + ")", null, delegate
		{
			CreateProfileFromCurrent();
		});
		toolStripMenuItem2.DropDownItems.Add("Install from Zip (" + GetShortcutString("InstallZip") + ")", null, delegate
		{
			ManualInstall();
		});
		toolStripMenuItem2.DropDownItems.Add("Update All Available (" + GetShortcutString("UpdateAll") + ")", null, delegate
		{
			UpdateAllMods();
		});
		toolStripMenuItem2.DropDownItems.Add("Launch Stardew Valley (" + GetShortcutString("LaunchGame") + ")", null, delegate
		{
			LaunchGame();
		});
		ToolStripMenuItem toolStripMenuItem3 = new ToolStripMenuItem("&View");
		toolStripMenuItem3.DropDownItems.Add("Open Downloads Folder (" + GetShortcutString("OpenDownloads") + ")", null, delegate
		{
			Process.Start("explorer.exe", downloadsPath);
		});
		toolStripMenuItem3.DropDownItems.Add("Open Backups Folder (" + GetShortcutString("OpenBackups") + ")", null, delegate
		{
			Process.Start("explorer.exe", backupsPath);
		});
		toolStripMenuItem3.DropDownItems.Add("Open SMAPI Log File (" + GetShortcutString("OpenLogFile") + ")", null, delegate
		{
			OpenRawSmapiLog();
		});
		toolStripMenuItem3.DropDownItems.Add("Open Error Log (" + GetShortcutString("OpenErrorLog") + ")", null, delegate
		{
			if (File.Exists(errorLogPath))
			{
				Process.Start(new ProcessStartInfo("notepad.exe", errorLogPath)
				{
					UseShellExecute = true
				});
			}
		});
		ToolStripMenuItem toolStripMenuItem4 = new ToolStripMenuItem("&Help");
		toolStripMenuItem4.DropDownItems.Add("User Manual (" + GetShortcutString("Manual") + ")", null, delegate
		{
			ShowManual();
		});
		toolStripMenuItem4.DropDownItems.Add("Sound Demo", null, delegate
		{
			ShowSoundDemo();
		});
		toolStripMenuItem4.DropDownItems.Add("Audio Theme Manager", null, delegate
		{
			ShowThemeManager();
		});
		toolStripMenuItem4.DropDownItems.Add("Shortcut Customization", null, delegate
		{
			ShowShortcutManager();
		});
		menuStrip.Items.Add(toolStripMenuItem);
		menuStrip.Items.Add(toolStripMenuItem2);
		menuStrip.Items.Add(toolStripMenuItem3);
		menuStrip.Items.Add(toolStripMenuItem4);
		base.MainMenuStrip = menuStrip;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 1,
			ColumnCount = 1
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.Padding = new Padding(0, 25, 0, 0);
		mainTabs = new TabControl
		{
			Dock = DockStyle.Fill
		};
		tabInstalled = new TabPage("Installed Mods");
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 2,
			ColumnCount = 1
		};
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight
		};
		txtSearchInstalled = new TextBox
		{
			Width = 200,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = "Search Installed Mods"
		};
		txtSearchInstalled.TextChanged += delegate
		{
			FilterInstalledMods();
		};
		cmbCategoryFilter = new ComboBox
		{
			Width = 150,
			Font = new Font("Segoe UI", 12f),
			DropDownStyle = ComboBoxStyle.DropDownList
		};
		cmbCategoryFilter.Items.Add("All Categories");
		cmbCategoryFilter.SelectedIndex = 0;
		cmbCategoryFilter.SelectedIndexChanged += delegate
		{
			FilterInstalledMods();
		};
		flowLayoutPanel.Controls.Add(new Label
		{
			Text = "Search:",
			AutoSize = true,
			Padding = new Padding(5, 8, 0, 0)
		});
		flowLayoutPanel.Controls.Add(txtSearchInstalled);
		flowLayoutPanel.Controls.Add(new Label
		{
			Text = "Category:",
			AutoSize = true,
			Padding = new Padding(10, 8, 0, 0)
		});
		flowLayoutPanel.Controls.Add(cmbCategoryFilter);
		listInstalled = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listInstalled",
			Font = new Font("Segoe UI", 12f)
		};
		listInstalled.AccessibleName = "Installed Mods List";
		tableLayoutPanel2.Controls.Add(flowLayoutPanel, 0, 0);
		tableLayoutPanel2.Controls.Add(listInstalled, 0, 1);
		tabInstalled.Controls.Add(tableLayoutPanel2);
		tabUpdates = new TabPage("Updates Available");
		listUpdates = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listUpdates",
			Font = new Font("Segoe UI", 12f)
		};
		listUpdates.AccessibleName = "Available Updates List";
		tabUpdates.Controls.Add(listUpdates);
		tabBackups = new TabPage("Backups");
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 2,
			ColumnCount = 1
		};
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		btnPruneBackups = new Button
		{
			Text = "Prune Old Backups (" + GetShortcutString("PruneBackups") + ")",
			Width = 250,
			Height = 35
		};
		btnPruneBackups.Click += delegate
		{
			PruneAllBackups();
		};
		tableLayoutPanel3.Controls.Add(btnPruneBackups, 0, 0);
		listBackups = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listBackups",
			Font = new Font("Segoe UI", 12f)
		};
		listBackups.AccessibleName = "Mod Backups List";
		tableLayoutPanel3.Controls.Add(listBackups, 0, 1);
		tabBackups.Controls.Add(tableLayoutPanel3);
		tabDiscovery = new TabPage("Find New Mods");
		TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 2,
			ColumnCount = 1
		};
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		FlowLayoutPanel flowLayoutPanel2 = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight,
			Padding = new Padding(5)
		};
		txtSearch = new TextBox
		{
			Width = 250,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = "Search Mod Name"
		};
		cmbDiscoveryType = new ComboBox
		{
			Width = 150,
			Font = new Font("Segoe UI", 12f),
			DropDownStyle = ComboBoxStyle.DropDownList
		};
		ComboBox.ObjectCollection items = cmbDiscoveryType.Items;
		object[] items2 = new string[4] { "Search", "Trending", "Most Popular", "Recent" };
		items.AddRange(items2);
		cmbDiscoveryType.SelectedIndex = 0;
		btnSearch = new Button
		{
			Text = "Go",
			Height = 30,
			Width = 60
		};
		btnSearch.Click += async delegate
		{
			await RunDiscovery();
		};
		btnLoadMoreDiscovery = new Button
		{
			Text = "Load More",
			Height = 30,
			Width = 100,
			Visible = false,
			AccessibleName = "Load more discovery results"
		};
		btnLoadMoreDiscovery.Click += async delegate
		{
			await RunDiscovery(loadMore: true);
		};
		txtSearch.KeyDown += async delegate(object? s, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				await RunDiscovery();
			}
		};
		flowLayoutPanel2.Controls.Add(new Label
		{
			Text = "Search/Type:",
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		});
		flowLayoutPanel2.Controls.Add(txtSearch);
		flowLayoutPanel2.Controls.Add(cmbDiscoveryType);
		flowLayoutPanel2.Controls.Add(btnSearch);
		flowLayoutPanel2.Controls.Add(btnLoadMoreDiscovery);
		listDiscovery = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listDiscovery",
			Font = new Font("Segoe UI", 12f)
		};
		listDiscovery.AccessibleName = "Mod Discovery Results";
		
		// Adjust table layout for 3 rows
		tableLayoutPanel4.RowCount = 3;
		tableLayoutPanel4.RowStyles.Clear();
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f)); // Search bar
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // List
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f)); // Load More button
		
		tableLayoutPanel4.Controls.Add(flowLayoutPanel2, 0, 0);
		tableLayoutPanel4.Controls.Add(listDiscovery, 0, 1);
		tableLayoutPanel4.Controls.Add(btnLoadMoreDiscovery, 0, 2);
		btnLoadMoreDiscovery.Dock = DockStyle.Fill;

		tabDiscovery.Controls.Add(tableLayoutPanel4);
		tabProfiles = new TabPage("Profiles");
		listProfiles = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listProfiles",
			Font = new Font("Segoe UI", 12f)
		};
		listProfiles.AccessibleName = "Mod Profiles List";
		tabProfiles.Controls.Add(listProfiles);
		tabSmapiLog = new TabPage("SMAPI Log");
		TableLayoutPanel tableLayoutPanel5 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 2,
			ColumnCount = 1
		};
		tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		tableLayoutPanel5.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		FlowLayoutPanel flowLayoutPanel3 = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight
		};
		cmbLogFilter = new ComboBox
		{
			Width = 180,
			Font = new Font("Segoe UI", 12f),
			DropDownStyle = ComboBoxStyle.DropDownList
		};
		ComboBox.ObjectCollection items3 = cmbLogFilter.Items;
		items2 = new string[3] { "Errors and Warnings", "Errors Only", "Full Log" };
		items3.AddRange(items2);
		cmbLogFilter.SelectedIndex = 0;
		cmbLogFilter.SelectedIndexChanged += delegate
		{
			RefreshSmapiLog();
		};
		txtSearchLog = new TextBox
		{
			Width = 180,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = "Search Log Entries"
		};
		txtSearchLog.KeyDown += delegate(object? s, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				e.SuppressKeyPress = true;
				SearchSmapiLog();
			}
		};
		flowLayoutPanel3.Controls.Add(new Label
		{
			Text = "Filter:",
			AutoSize = true,
			Padding = new Padding(5, 8, 0, 0)
		});
		flowLayoutPanel3.Controls.Add(cmbLogFilter);
		flowLayoutPanel3.Controls.Add(new Label
		{
			Text = "Search:",
			AutoSize = true,
			Padding = new Padding(10, 8, 0, 0)
		});
		flowLayoutPanel3.Controls.Add(txtSearchLog);
		listLog = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listLog",
			Font = new Font("Segoe UI", 12f)
		};
		listLog.AccessibleName = "Parsed SMAPI Log Entries";
		tableLayoutPanel5.Controls.Add(flowLayoutPanel3, 0, 0);
		tableLayoutPanel5.Controls.Add(listLog, 0, 1);
		tabSmapiLog.Controls.Add(tableLayoutPanel5);

		tabWiki = new TabPage("Stardew Wiki");
		TableLayoutPanel tableLayoutPanelWiki = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 2,
			ColumnCount = 1
		};
		tableLayoutPanelWiki.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
		tableLayoutPanelWiki.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		FlowLayoutPanel flowLayoutPanelWikiTop = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight,
			Padding = new Padding(5)
		};
		txtWikiSearch = new TextBox
		{
			Width = 200,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = "Search Stardew Wiki"
		};
		txtWikiSearch.KeyDown += async delegate(object? s, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				e.SuppressKeyPress = true;
				await SearchWiki(txtWikiSearch.Text.Trim());
			}
		};
		cmbWikiCategories = new ComboBox
		{
			Width = 200,
			Font = new Font("Segoe UI", 12f),
			DropDownStyle = ComboBoxStyle.DropDownList,
			AccessibleName = "Wiki Categories"
		};
		cmbWikiCategories.Items.AddRange(new string[] { "Select Category", "Villagers", "Crops", "Fish", "Artisan Goods", "Cooking", "Mining", "Animals" });
		cmbWikiCategories.SelectedIndex = 0;
		cmbWikiCategories.SelectedIndexChanged += async delegate
		{
			if (cmbWikiCategories.SelectedIndex > 0)
			{
				splitWiki.Visible = true;
				await LoadWikiCategory(cmbWikiCategories.SelectedItem.ToString());
			}
			else
			{
				splitWiki.Visible = false;
				listWikiResults.Items.Clear();
				wikiNavStack.Clear();
			}
		};
		flowLayoutPanelWikiTop.Controls.Add(new Label { Text = "Search:", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
		flowLayoutPanelWikiTop.Controls.Add(txtWikiSearch);
		flowLayoutPanelWikiTop.Controls.Add(new Label { Text = "Categories:", AutoSize = true, Padding = new Padding(10, 5, 0, 0) });
		flowLayoutPanelWikiTop.Controls.Add(cmbWikiCategories);

		splitWiki = new SplitContainer
		{
			Dock = DockStyle.Fill,
			Orientation = Orientation.Vertical,
			SplitterDistance = 300,
			Visible = false
		};		listWikiResults = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listWikiResults",
			Font = new Font("Segoe UI", 12f),
			AccessibleName = "Wiki Results"
		};
		listWikiResults.KeyDown += async delegate(object? s, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return && listWikiResults.SelectedItem is WikiResult res)
			{
				e.SuppressKeyPress = true;
				if (res.IsCategory) await LoadWikiCategory(res.Title);
				else LoadWikiPage(res.Title);
			}
			else if (e.KeyCode == Keys.Back)
			{
				e.SuppressKeyPress = true;
				NavigateBackWiki();
			}
		};
		webViewWiki = new WebView2
		{
			Dock = DockStyle.Fill,
			AccessibleName = "Wiki Content View"
		};
		InitializeWebView();
		
		splitWiki.Panel1.Controls.Add(listWikiResults);
		splitWiki.Panel2.Controls.Add(webViewWiki);
		tableLayoutPanelWiki.Controls.Add(flowLayoutPanelWikiTop, 0, 0);
		tableLayoutPanelWiki.Controls.Add(splitWiki, 0, 1);
		tabWiki.Controls.Add(tableLayoutPanelWiki);

		mainTabs.TabPages.Add(tabInstalled);
		mainTabs.TabPages.Add(tabUpdates);
		mainTabs.TabPages.Add(tabBackups);
		mainTabs.TabPages.Add(tabDiscovery);
		mainTabs.TabPages.Add(tabWiki);
		mainTabs.TabPages.Add(tabProfiles);
		mainTabs.TabPages.Add(tabSmapiLog);
		tableLayoutPanel.Controls.Add(mainTabs, 0, 0);
		base.Controls.Add(tableLayoutPanel);
		base.Controls.Add(menuStrip);
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
		_searchTimer.Tick += delegate
		{
			_searchTimer.Stop();
			_searchBuffer = "";
		};
		listWikiResults.SelectedIndexChanged += Wiki_SelectedIndexChanged;
		listWikiResults.DoubleClick += async delegate(object? s, EventArgs e)
		{
			if (listWikiResults.SelectedItem is WikiResult res)
			{
				if (res.IsCategory) await LoadWikiCategory(res.Title);
				else LoadWikiPage(res.Title);
			}
		};
	}

	private async void InitializeWebView()
	{
		try
		{
			string webViewDataPath = Path.Combine(dataBasePath, "WebView2Data");
			if (!Directory.Exists(webViewDataPath)) Directory.CreateDirectory(webViewDataPath);
			var env = await CoreWebView2Environment.CreateAsync(null, webViewDataPath);
			await webViewWiki.EnsureCoreWebView2Async(env);
		}
		catch (Exception ex)
		{
			LogError("Wiki", "WebView2 Init Error: " + ex.Message);
		}
	}

	private async Task<bool> SearchWiki(string query)
	{
		if (string.IsNullOrEmpty(query)) return false;
		splitWiki.Visible = true;
		SetStatus("Searching Wiki...");
		try
		{
			string url = $"https://stardewvalleywiki.com/mediawiki/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&format=json";
			string json = await _httpClient.GetStringAsync(url);
			JObject data = JObject.Parse(json);
			JArray results = (JArray)data["query"]["search"];
			
			List<WikiResult> wikiResults = new List<WikiResult>();
			foreach (var item in results)
			{
				wikiResults.Add(new WikiResult { Title = item["title"].ToString(), IsCategory = false });
			}
			
			PushWikiState("Search: " + query, wikiResults.Cast<object>().ToList());
			UpdateWikiList(wikiResults);
			Speak($"Found {wikiResults.Count} results.");
		}
		catch (Exception ex)
		{
			LogError("Wiki", "Search Error: " + ex.Message);
			Speak("Wiki search failed.");
		}
		return true;
	}

	private async Task<bool> LoadWikiCategory(string category)
	{
		SetStatus("Loading Category: " + category);
		try
		{
			string catTitle = category.StartsWith("Category:") ? category : "Category:" + category;
			string url = $"https://stardewvalleywiki.com/mediawiki/api.php?action=query&list=categorymembers&cmtitle={Uri.EscapeDataString(catTitle)}&cmlimit=500&format=json";
			string json = await _httpClient.GetStringAsync(url);
			JObject data = JObject.Parse(json);
			JArray members = (JArray)data["query"]["categorymembers"];
			
			List<WikiResult> wikiResults = new List<WikiResult>();
			foreach (var item in members)
			{
				string title = item["title"].ToString();
				bool isCat = title.StartsWith("Category:");
				wikiResults.Add(new WikiResult { Title = title, IsCategory = isCat });
			}
			
			PushWikiState(category, wikiResults.Cast<object>().ToList());
			UpdateWikiList(wikiResults);
			Speak($"{category} loaded with {wikiResults.Count} items.");
		}
		catch (Exception ex)
		{
			LogError("Wiki", "Category Error: " + ex.Message);
			Speak("Failed to load wiki category.");
		}
		return true;
	}

	private async Task<bool> LoadWikiPage(string title)
	{
		if (webViewWiki.CoreWebView2 == null) InitializeWebView();
		string url = "https://stardewvalleywiki.com/" + Uri.EscapeDataString(title.Replace(" ", "_"));
		webViewWiki.CoreWebView2.Navigate(url);
		Speak("Loading page: " + title);
		return true;
	}

	private void PushWikiState(string title, List<object> results)
	{
		if (wikiNavStack.Count > 0)
		{
			wikiNavStack.Peek().SelectedIndex = listWikiResults.SelectedIndex;
		}
		wikiNavStack.Push(new WikiNavigationState { Title = title, Results = results });
	}

	private void NavigateBackWiki()
	{
		if (wikiNavStack.Count > 1)
		{
			wikiNavStack.Pop();
			var state = wikiNavStack.Peek();
			UpdateWikiList(state.Results.Cast<WikiResult>().ToList());
			if (state.SelectedIndex >= 0 && state.SelectedIndex < listWikiResults.Items.Count)
				listWikiResults.SelectedIndex = state.SelectedIndex;
			Speak("Back to " + state.Title);
		}
		else Speak("Already at top level.");
	}

	private void UpdateWikiList(List<WikiResult> results)
	{
		listWikiResults.BeginUpdate();
		listWikiResults.Items.Clear();
		foreach (var res in results) listWikiResults.Items.Add(res);
		listWikiResults.EndUpdate();
		if (listWikiResults.Items.Count > 0) listWikiResults.SelectedIndex = 0;
	}

	private async void Wiki_SelectedIndexChanged(object? sender, EventArgs e)
	{
		if (listWikiResults.SelectedItem is WikiResult res)
		{
			await Task.Delay(100);
			Speak($"{listWikiResults.SelectedIndex + 1} of {listWikiResults.Items.Count}");
		}
	}

	private string GetShortcutString(string action)
	{
		if (_settings.Shortcuts.TryGetValue(action, out var value))
		{
			if (value == Keys.None)
			{
				return "Unmapped";
			}
			StringBuilder stringBuilder = new StringBuilder();
			if ((value & Keys.Control) == Keys.Control)
			{
				stringBuilder.Append("Ctrl+");
			}
			if ((value & Keys.Shift) == Keys.Shift)
			{
				stringBuilder.Append("Shift+");
			}
			if ((value & Keys.Alt) == Keys.Alt)
			{
				stringBuilder.Append("Alt+");
			}
			stringBuilder.Append(value & Keys.KeyCode);
			return stringBuilder.ToString();
		}
		return "Unmapped";
	}

	private bool IsShortcut(KeyEventArgs e, string action)
	{
		if (!_settings.Shortcuts.TryGetValue(action, out var value))
		{
			return false;
		}
		if (value == Keys.None)
		{
			return false;
		}
		return e.KeyData == value;
	}

	private void FilterInstalledMods()
	{
		string query = txtSearchInstalled.Text.Trim().ToLower();
		string category = cmbCategoryFilter.SelectedItem?.ToString() ?? "All Categories";
		listInstalled.BeginUpdate();
		listInstalled.Items.Clear();
		List<StardewMod> list = _allInstalledMods.Where((StardewMod m) => (m.Name.ToLower().Contains(query) || m.Author.ToLower().Contains(query)) && (category == "All Categories" || m.Category == category)).ToList();
		foreach (StardewMod item in list)
		{
			listInstalled.Items.Add(item);
		}
		listInstalled.EndUpdate();
		if (!string.IsNullOrEmpty(query) || category != "All Categories")
		{
			Speak($"{list.Count} mods found.");
		}
	}

	private void List_Enter(object? sender, EventArgs e)
	{
		if (sender is ListBox listBox)
		{
			if (listBox.Items.Count > 0 && listBox.SelectedIndex == -1)
			{
				listBox.SelectedIndex = 0;
			}
			if (listBox.Items.Count == 0 && !_isLoading)
			{
				Speak("List is empty.");
			}
		}
	}

	private void SetStatus(string text)
	{
		if (base.InvokeRequired)
		{
			Invoke(delegate
			{
				SetStatus(text);
			});
		}
		else
		{
			string text2 = "Stardew Valley Accessible Mod Manager - Status: " + text;
			Text = text2;
			Speak(text);
		}
	}

	private async void List_SelectedIndexChanged(object? sender, EventArgs e)
	{
		if (!(sender is ListBox { SelectedItem: not null } list) || _isLoading)
		{
			return;
		}
		await Task.Delay(100);
		string text = $"{list.SelectedIndex + 1} of {list.Items.Count}";
		if (list.Name == "listLog")
		{
			string suggestedFix = LogAnalyzer.GetSuggestedFix(list.SelectedItem.ToString());
			if (!string.IsNullOrEmpty(suggestedFix))
			{
				text = text + ". Suggested Fix: " + suggestedFix;
			}
		}
		Speak(text);
	}

	private void ShowContextHelp()
	{
		string text = "";
		switch (mainTabs.SelectedIndex)
		{
		case 0:
			text = $"Installed Mods: Space to Toggle. Delete to remove mod. {GetShortcutString("Search")} to Search. {GetShortcutString("ChangeCategory")} to change Category. {GetShortcutString("BatchCategory")} to batch toggle Category. {GetShortcutString("OpenModPage")} for Nexus. {GetShortcutString("ShowDependencies")} for dependencies. {GetShortcutString("QuickFix")} to Quick-Fix. {GetShortcutString("ManualID")} for Nexus ID. {GetShortcutString("InstallZip")} to install zip. {GetShortcutString("SaveProfile")} to save as profile. {GetShortcutString("ReadDescription")} to read description. {GetShortcutString("LaunchGame")} to launch game.";
			break;
		case 1:
			text = $"Updates: Enter for Nexus page. Delete to Ignore this update. {GetShortcutString("UpdateAll")} to Update All (Premium). {GetShortcutString("ReadDescription")} to read description. {GetShortcutString("LaunchGame")} to launch game.";
			break;
		case 2:
			text = $"Backups: Enter to restore. Delete to remove zip. {GetShortcutString("PruneBackups")} to prune old backups. {GetShortcutString("OpenBackups")} to open backups folder.";
			break;
		case 3:
			text = "Discovery: Enter for Nexus page. " + GetShortcutString("ReadDescription") + " to read summary. Tab to search.";
			break;
		case 4:
			text = "Profiles: Enter to apply profile. Delete to remove profile.";
			break;
		case 5:
			text = "SMAPI Log: Search box available. Enter on search result to jump to it in full view. " + GetShortcutString("QuickFix") + " to Quick-Fix detected issue. " + GetShortcutString("Login") + " to upload to SMAPI.io. " + GetShortcutString("OpenLogFile") + " to open raw file.";
			break;
		case 6:
			text = "Stardew Wiki: Type in Search box and press Enter. Or select a Category. In Results: Enter to open page or sub-category. Backspace to go back up. Tab into the web view to read content with screen reader commands (H for headings, T for tables).";
			break;
		}
		if (!string.IsNullOrEmpty(text))
		{
			Speak(text);
		}
	}

	private async Task RunDiscovery(bool loadMore = false)
	{
		if (string.IsNullOrEmpty(_settings.ApiKey))
		{
			Speak("Please login first.");
			return;
		}
		if (!loadMore)
		{
			_currentDiscoveryPage = 1;
			listDiscovery.Items.Clear();
		}
		else
		{
			_currentDiscoveryPage++;
		}
		string text = cmbDiscoveryType.SelectedItem?.ToString() ?? "Search";
		string stringToEscape = txtSearch.Text.Trim();
		Speak((loadMore ? "Loading more " : "Starting mod ") + text + "...");
		SetStatus((loadMore ? "Loading more " : "Running ") + text + "...");
		try
		{
			int count = 20;
			int offset = (_currentDiscoveryPage - 1) * count;
			string query = "";
			object variables = null;

			if (text == "Search")
			{
				query = @"query SearchMods($filter: ModsFilter, $count: Int, $offset: Int) {
					mods(filter: $filter, count: $count, offset: $offset) {
						nodes { modId name summary author version }
						totalCount
					}
				}";
				variables = new 
				{ 
					filter = new { 
						gameId = new[] { new { value = "1303", op = "EQUALS" } }, 
						name = new[] { new { value = stringToEscape, op = "WILDCARD" } } 
					},
					count,
					offset
				};
			}
			else
			{
				string sortField = text switch { "Most Popular" => "downloads", "Recent" => "updatedAt", _ => "endorsements" };
				query = @"query ListMods($filter: ModsFilter, $sort: [ModsSort!], $count: Int, $offset: Int) {
					mods(filter: $filter, sort: $sort, count: $count, offset: $offset) {
						nodes { modId name summary author version }
						totalCount
					}
				}";
				variables = new 
				{ 
					filter = new { gameId = new[] { new { value = "1303", op = "EQUALS" } } },
					sort = new[] { new Dictionary<string, object> { { sortField, new { direction = "DESC" } } } },
					count,
					offset
				};
			}

			var requestBody = new { query, variables };
			HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.nexusmods.com/v2/graphql");
			httpRequestMessage.Headers.Add("apikey", _settings.ApiKey);
			httpRequestMessage.Headers.Add("User-Agent", $"StardewAccessibleManager/{_appVersion}");
			httpRequestMessage.Content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

			HttpResponseMessage httpResponseMessage = await _httpClient.SendAsync(httpRequestMessage);
			if (httpResponseMessage.IsSuccessStatusCode)
			{
				string jsonResponse = await httpResponseMessage.Content.ReadAsStringAsync();
				JObject data = JObject.Parse(jsonResponse);
				if (data["errors"] != null)
				{
					Speak("API Error: " + data["errors"][0]["message"].ToString());
					return;
				}

				JToken modsData = data["data"]["mods"];
				JArray nodes = (JArray)modsData["nodes"];
				int totalCount = modsData["totalCount"] != null ? (int)modsData["totalCount"] : 0;

				foreach (var item in nodes)
				{
					listDiscovery.Items.Add(new StardewMod
					{
						Name = item["name"]?.ToString() ?? "Unknown",
						Author = item["author"]?.ToString() ?? "Unknown",
						Version = item["version"]?.ToString() ?? "0",
						Description = item["summary"]?.ToString() ?? "",
						NexusID = item["modId"]?.ToString(),
						UniqueId = item["modId"]?.ToString() ?? Guid.NewGuid().ToString(),
						IsSearchResult = true
					});
				}

				btnLoadMoreDiscovery.Visible = (offset + nodes.Count) < totalCount;

				if (nodes.Count > 0)
				{
					Speak((loadMore ? "Added " : "Found ") + nodes.Count + " mods.");
					if (!loadMore) listDiscovery.SelectedIndex = 0;
				}
				else
				{
					Speak(loadMore ? "No more mods found." : "No mods found.");
				}
			}
			else
			{
				Speak($"Discovery failed with status code: {(int)httpResponseMessage.StatusCode}");
				LogError("Nexus", $"GraphQL failed: {httpResponseMessage.StatusCode}");
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("Discovery Error: " + ex.Message);
		}
	}

	private string MapNexusCategory(int id)
	{
		return id switch
		{
			1 => "Expansion", 
			2 => "NPC", 
			3 => "Portrait", 
			4 => "Map", 
			5 => "Crafting", 
			6 => "Gameplay", 
			7 => "Visual", 
			8 => "Audio", 
			_ => "General", 
		};
	}

	private string DetectCategory(string name, string desc)
	{
		string text = (name + " " + desc).ToLower();
		if (text.Contains("expansion") || text.Contains("content pack"))
		{
			return "Expansion";
		}
		if (text.Contains("npc") || text.Contains("character"))
		{
			return "NPC";
		}
		if (text.Contains("portrait") || text.Contains("sprite"))
		{
			return "Portrait";
		}
		if (text.Contains("farm") || text.Contains("map") || text.Contains("location"))
		{
			return "Map";
		}
		if (text.Contains("craft") || text.Contains("machine") || text.Contains("item"))
		{
			return "Crafting";
		}
		if (text.Contains("audio") || text.Contains("music") || text.Contains("sound"))
		{
			return "Audio";
		}
		if (text.Contains("visual") || text.Contains("recolor") || text.Contains("texture"))
		{
			return "Visual";
		}
		return "General";
	}

	private void Speak(string text)
	{
		if (Tolk.IsLoaded())
		{
			Tolk.Output(text);
		}
	}

	private async Task RunLoadingLoop()
	{
		while (_isLoading)
		{
			PlayAppSound("loading_indicator");
			await Task.Delay(1500);
		}
	}

	private void ShowSoundDemo()
	{
		Hide();
		string previewTheme = _settings.CurrentTheme;
		Form demoForm = new Form
		{
			Text = "Sound Demo - Escape to Close",
			Size = new Size(500, 600),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 3,
			Padding = new Padding(10)
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		ComboBox cmbTheme = new ComboBox
		{
			Dock = DockStyle.Fill,
			DropDownStyle = ComboBoxStyle.DropDownList,
			Font = new Font("Segoe UI", 12f)
		};
		cmbTheme.Items.AddRange(Directory.GetDirectories(themesPath).Select(Path.GetFileName).Cast<object>()
			.ToArray());
		cmbTheme.SelectedItem = previewTheme;
		cmbTheme.SelectedIndexChanged += delegate
		{
			previewTheme = cmbTheme.SelectedItem?.ToString() ?? "Default";
		};
		tableLayoutPanel.Controls.Add(new Label
		{
			Text = "Preview Theme:",
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		}, 0, 0);
		tableLayoutPanel.Controls.Add(cmbTheme, 0, 1);
		ListBox lb = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = "Sound List"
		};
		foreach (string key2 in _soundDescriptions.Keys)
		{
			lb.Items.Add(key2);
		}
		lb.SelectedIndexChanged += async delegate
		{
			if (lb.SelectedItem != null)
			{
				string key = lb.SelectedItem.ToString();
				await Task.Delay(100);
				Speak($"{key}. {_soundDescriptions[key]}. {lb.SelectedIndex + 1} of {lb.Items.Count}");
			}
		};
		lb.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Return && lb.SelectedItem != null)
			{
				PlayAppSound(lb.SelectedItem.ToString(), previewTheme);
			}
			if (pe.KeyCode == Keys.Escape)
			{
				demoForm.Close();
			}
		};
		demoForm.FormClosing += delegate
		{
			Show();
		};
		tableLayoutPanel.Controls.Add(lb, 0, 2);
		demoForm.Controls.Add(tableLayoutPanel);
		demoForm.ShowDialog();
	}

	private void ShowThemeManager()
	{
		Form f = new Form
		{
			Text = "Audio Theme Manager - Escape to Cancel",
			Size = new Size(500, 600),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(15),
			RowCount = 6
		};
		string tempActiveTheme = _settings.CurrentTheme;
		ListBox lb = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = "Installed Themes"
		};
		RefreshList();
		Button button = new Button
		{
			Text = "Set Selected as Active Theme",
			Dock = DockStyle.Top,
			Height = 35
		};
		button.Click += delegate
		{
			if (lb.SelectedItem != null)
			{
				tempActiveTheme = lb.SelectedItem.ToString();
				Speak("Active theme changed to " + tempActiveTheme + ". Press Save to confirm.");
			}
		};
		Button button2 = new Button
		{
			Text = "Create New Theme",
			Dock = DockStyle.Top,
			Height = 35
		};
		button2.Click += delegate
		{
			string text = Interaction.InputBox("Enter name for new theme:", "Create Theme");
			if (!string.IsNullOrEmpty(text))
			{
				string text2 = Path.Combine(themesPath, text);
				if (!Directory.Exists(text2))
				{
					Directory.CreateDirectory(text2);
					foreach (string key in _soundDescriptions.Keys)
					{
						Directory.CreateDirectory(Path.Combine(text2, key));
					}
					Directory.CreateDirectory(Path.Combine(text2, "logo"));
					Speak("Theme created. Drop your .ogg files into the theme folders.");
					Process.Start("explorer.exe", text2);
					RefreshList();
				}
			}
		};
		Button button3 = new Button
		{
			Text = "Add Missing Folders to All Themes",
			Dock = DockStyle.Top,
			Height = 35
		};
		button3.Click += delegate
		{
			int num = 0;
			string[] directories = Directory.GetDirectories(themesPath);
			foreach (string path in directories)
			{
				foreach (string key2 in _soundDescriptions.Keys)
				{
					string path2 = Path.Combine(path, key2);
					if (!Directory.Exists(path2))
					{
						Directory.CreateDirectory(path2);
						num++;
					}
				}
			}
			Speak($"Updated themes. Added {num} missing folders.");
		};
		Button button4 = new Button
		{
			Text = "Delete Theme",
			Dock = DockStyle.Top,
			Height = 35
		};
		button4.Click += delegate
		{
			if (lb.SelectedItem != null)
			{
				string text = lb.SelectedItem.ToString();
				if (text == "Default")
				{
					MessageBox.Show("Cannot delete Default theme.");
				}
				else if (MessageBox.Show("Delete theme '" + text + "'?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					Directory.Delete(Path.Combine(themesPath, text), recursive: true);
					if (tempActiveTheme == text)
					{
						tempActiveTheme = "Default";
					}
					RefreshList();
					Speak("Theme deleted.");
				}
			}
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			FlowDirection = FlowDirection.RightToLeft,
			Height = 50
		};
		Button button5 = new Button
		{
			Text = "Save and Close",
			Width = 120,
			Height = 35
		};
		button5.Click += delegate
		{
			_settings.CurrentTheme = tempActiveTheme;
			_settings.Save();
			f.Close();
			Speak("Theme settings saved.");
		};
		Button button6 = new Button
		{
			Text = "Cancel",
			Width = 100,
			Height = 35
		};
		button6.Click += delegate
		{
			Speak("Changes cancelled.");
			f.Close();
		};
		flowLayoutPanel.Controls.AddRange(button5, button6);
		tableLayoutPanel.Controls.Add(lb, 0, 0);
		tableLayoutPanel.Controls.Add(button, 0, 1);
		tableLayoutPanel.Controls.Add(button2, 0, 2);
		tableLayoutPanel.Controls.Add(button3, 0, 3);
		tableLayoutPanel.Controls.Add(button4, 0, 4);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 5);
		f.Controls.Add(tableLayoutPanel);
		f.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Escape)
			{
				Speak("Changes cancelled.");
				f.Close();
			}
		};
		f.ShowDialog();
		void RefreshList()
		{
			lb.Items.Clear();
			lb.Items.AddRange(Directory.GetDirectories(themesPath).Select(Path.GetFileName).Cast<object>()
				.ToArray());
			lb.SelectedItem = tempActiveTheme;
		}
	}

	private void ShowShortcutManager()
	{
		Form f = new Form
		{
			Text = "Shortcut Customization - Escape to Cancel",
			Size = new Size(500, 600),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(15),
			RowCount = 4
		};
		Dictionary<string, Keys> tempShortcuts = new Dictionary<string, Keys>(_settings.Shortcuts);
		ListBox lb = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = "Action List"
		};
		RefreshList();
		Button button = new Button
		{
			Text = "Remap Selected Action",
			Dock = DockStyle.Fill,
			Height = 35
		};
		button.Click += delegate
		{
			if (lb.SelectedItem != null)
			{
				string action = lb.SelectedItem.ToString().Split(':')[0].Trim();
				Speak("Press the new key combination for " + action + "...");
				Form prompt = new Form
				{
					Text = "Press Keys...",
					Size = new Size(300, 150),
					StartPosition = FormStartPosition.CenterParent,
					FormBorderStyle = FormBorderStyle.FixedDialog,
					KeyPreview = true
				};
				prompt.KeyDown += delegate(object? ps, KeyEventArgs e)
				{
					if (e.KeyCode != Keys.ControlKey && e.KeyCode != Keys.ShiftKey && e.KeyCode != Keys.Menu)
					{
						tempShortcuts[action] = e.KeyData;
						prompt.Close();
						Speak(action + " remapped. Press Save to confirm.");
						RefreshList();
					}
				};
				prompt.ShowDialog();
			}
		};
		Button button2 = new Button
		{
			Text = "Reset All to Defaults",
			Dock = DockStyle.Fill,
			Height = 35
		};
		button2.Click += delegate
		{
			if (MessageBox.Show("Reset all shortcuts to defaults?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
			{
				tempShortcuts.Clear();
				AppSettings appSettings = new AppSettings();
				appSettings.InitializeDefaults();
				foreach (KeyValuePair<string, Keys> shortcut in appSettings.Shortcuts)
				{
					tempShortcuts[shortcut.Key] = shortcut.Value;
				}
				RefreshList();
				Speak("Reset to defaults. Press Save to confirm.");
			}
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			FlowDirection = FlowDirection.RightToLeft,
			Height = 50
		};
		Button button3 = new Button
		{
			Text = "Save and Close",
			Width = 120,
			Height = 35
		};
		button3.Click += delegate
		{
			_settings.Shortcuts = tempShortcuts;
			_settings.Save();
			f.Close();
			Speak("Shortcuts saved.");
			SetupAccessibleUI();
		};
		Button button4 = new Button
		{
			Text = "Cancel",
			Width = 100,
			Height = 35
		};
		button4.Click += delegate
		{
			Speak("Changes cancelled.");
			f.Close();
		};
		flowLayoutPanel.Controls.AddRange(button3, button4);
		tableLayoutPanel.Controls.Add(lb, 0, 0);
		tableLayoutPanel.Controls.Add(button, 0, 1);
		tableLayoutPanel.Controls.Add(button2, 0, 2);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 3);
		f.Controls.Add(tableLayoutPanel);
		f.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Escape)
			{
				Speak("Changes cancelled.");
				f.Close();
			}
		};
		f.ShowDialog();
		void RefreshList()
		{
			lb.Items.Clear();
			foreach (KeyValuePair<string, Keys> item in tempShortcuts)
			{
				lb.Items.Add(item.Key + ": " + GetShortcutStringForMap(tempShortcuts, item.Key));
			}
		}
	}

	private string GetShortcutStringForMap(Dictionary<string, Keys> map, string action)
	{
		if (map.TryGetValue(action, out var value))
		{
			if (value == Keys.None)
			{
				return "Unmapped";
			}
			StringBuilder stringBuilder = new StringBuilder();
			if ((value & Keys.Control) == Keys.Control)
			{
				stringBuilder.Append("Ctrl+");
			}
			if ((value & Keys.Shift) == Keys.Shift)
			{
				stringBuilder.Append("Shift+");
			}
			if ((value & Keys.Alt) == Keys.Alt)
			{
				stringBuilder.Append("Alt+");
			}
			stringBuilder.Append(value & Keys.KeyCode);
			return stringBuilder.ToString();
		}
		return "Unmapped";
	}

	private void ShowManual()
	{
		string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MANUAL.md");
		if (!File.Exists(path))
		{
			MessageBox.Show("Manual not found.");
			return;
		}
		string[] array = File.ReadAllLines(path);
		Dictionary<string, string> sections = new Dictionary<string, string>();
		string key = "General";
		StringBuilder stringBuilder = new StringBuilder();
		string[] array2 = array;
		foreach (string text in array2)
		{
			if (text.StartsWith("#"))
			{
				if (stringBuilder.Length > 0)
				{
					sections[key] = stringBuilder.ToString();
				}
				key = text.TrimStart('#').Trim();
				stringBuilder.Clear();
			}
			else
			{
				stringBuilder.AppendLine(text);
			}
		}
		if (stringBuilder.Length > 0)
		{
			sections[key] = stringBuilder.ToString();
		}
		StringBuilder stringBuilder2 = new StringBuilder();
		foreach (KeyValuePair<string, Keys> shortcut in _settings.Shortcuts)
		{
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(8, 2, stringBuilder3);
			handler.AppendLiteral("* **");
			handler.AppendFormatted(shortcut.Key);
			handler.AppendLiteral("**: ");
			handler.AppendFormatted(GetShortcutString(shortcut.Key));
			stringBuilder3.AppendLine(ref handler);
		}
		sections["Current Key Mappings"] = stringBuilder2.ToString();
		Hide();
		Form manualForm = new Form
		{
			Text = "User Manual - Press Escape to Close",
			Size = new Size(800, 600),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));
		ListBox lbToc = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = "Table of Contents"
		};
		foreach (string key3 in sections.Keys)
		{
			lbToc.Items.Add(key3);
		}
		TextBox tbContent = new TextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Vertical,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = "Topic Information"
		};
		lbToc.SelectedIndexChanged += async delegate
		{
			if (lbToc.SelectedItem != null)
			{
				string key2 = lbToc.SelectedItem.ToString();
				tbContent.Text = sections[key2].Trim();
				await Task.Delay(150);
				Speak($"{lbToc.SelectedIndex + 1} of {lbToc.Items.Count}");
			}
		};
		manualForm.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Escape)
			{
				manualForm.Close();
			}
		};
		manualForm.FormClosing += delegate
		{
			Show();
		};
		tableLayoutPanel.Controls.Add(lbToc, 0, 0);
		tableLayoutPanel.Controls.Add(tbContent, 1, 0);
		manualForm.Controls.Add(tableLayoutPanel);
		if (lbToc.Items.Count > 0)
		{
			lbToc.SelectedIndex = 0;
		}
		manualForm.ShowDialog();
	}

	private void LaunchGame()
	{
		try
		{
			string path = Path.GetDirectoryName(_settings.ModsPath) ?? "";
			string text = Path.Combine(path, "StardewModdingAPI.exe");
			if (!File.Exists(text))
			{
				text = new string[4]
				{
					text,
					Path.Combine(path, "Stardew Valley", "StardewModdingAPI.exe"),
					"C:\\Program Files (x86)\\Steam\\steamapps\\common\\Stardew Valley\\StardewModdingAPI.exe",
					"D:\\SteamLibrary\\steamapps\\common\\Stardew Valley\\StardewModdingAPI.exe"
				}.FirstOrDefault((string path2) => File.Exists(path2)) ?? "";
			}
			if (!string.IsNullOrEmpty(text))
			{
				SetStatus("Launching SMAPI...");
				Process p = new Process();
				p.StartInfo = new ProcessStartInfo(text)
				{
					WorkingDirectory = Path.GetDirectoryName(text)
				};
				p.EnableRaisingEvents = true;
				p.Exited += async delegate
				{
					SetStatus("Game closed.");
					await Task.Delay(5000);
					SetStatus("Connected as " + nexusUser);
				};
				p.Start();
				Task.Run(async delegate
				{
					await Task.Delay(3000);
					if (!p.HasExited)
					{
						SetStatus("Game is loaded and running.");
					}
				});
			}
			else
			{
				MessageBox.Show("StardewModdingAPI.exe not found.");
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("Launch failed: " + ex.Message);
		}
	}

	private void OpenRawSmapiLog()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs", "SMAPI-latest.txt");
		if (File.Exists(text))
		{
			Process.Start(new ProcessStartInfo("notepad.exe", text)
			{
				UseShellExecute = true
			});
		}
		else
		{
			MessageBox.Show("SMAPI log not found.");
		}
	}

	private void RefreshSmapiLog()
	{
		if (listLog == null)
		{
			return;
		}
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs", "SMAPI-latest.txt");
		if (!File.Exists(path))
		{
			return;
		}
		_fullLogEntries.Clear();
		string text = cmbLogFilter.SelectedItem?.ToString() ?? "Errors and Warnings";
		try
		{
			string[] array = File.ReadAllLines(path);
			for (int i = 0; i < array.Length; i++)
			{
				string text2 = array[i];
				bool flag = text2.Contains("[ERROR]");
				bool flag2 = text2.Contains("[WARN]");
				bool flag3 = false;
				if (text == "Full Log")
				{
					flag3 = true;
				}
				else if (text == "Errors Only" && flag)
				{
					flag3 = true;
				}
				else if (text == "Errors and Warnings" && (flag || flag2))
				{
					flag3 = true;
				}
				if (flag3)
				{
					_fullLogEntries.Add(new LogEntry
					{
						Text = text2,
						Index = i
					});
				}
			}
		}
		catch
		{
		}
		listLog.BeginUpdate();
		listLog.Items.Clear();
		foreach (LogEntry fullLogEntry in _fullLogEntries)
		{
			listLog.Items.Add(fullLogEntry);
		}
		listLog.EndUpdate();
	}

	private void SearchSmapiLog()
	{
		string query = txtSearchLog.Text.Trim().ToLower();
		if (string.IsNullOrEmpty(query))
		{
			RefreshSmapiLog();
			return;
		}
		List<LogEntry> list = _fullLogEntries.Where((LogEntry e) => e.Text.ToLower().Contains(query)).ToList();
		listLog.BeginUpdate();
		listLog.Items.Clear();
		foreach (LogEntry item in list)
		{
			listLog.Items.Add(item);
		}
		listLog.EndUpdate();
		Speak($"Found {list.Count} results. Enter to jump to line in full view.");
		if (listLog.Items.Count > 0)
		{
			listLog.SelectedIndex = 0;
		}
	}

	private async Task UploadSmapiLog()
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs", "SMAPI-latest.txt");
		if (!File.Exists(path))
		{
			return;
		}
		SetStatus("Uploading log to SMAPI.io...");
		try
		{
			string value = File.ReadAllText(path);
			FormUrlEncodedContent content = new FormUrlEncodedContent(new KeyValuePair<string, string>[1]
			{
				new KeyValuePair<string, string>("input", value)
			});
			HttpResponseMessage httpResponseMessage = await _httpClient.PostAsync("https://smapi.io/log/", content);
			if (httpResponseMessage.IsSuccessStatusCode)
			{
				Process.Start(new ProcessStartInfo(httpResponseMessage.RequestMessage?.RequestUri?.ToString() ?? "https://smapi.io/log/")
				{
					UseShellExecute = true
				});
				Speak("Log uploaded successfully.");
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("Upload failed: " + ex.Message);
		}
		finally
		{
		}
	}

	private void CreateProfileFromCurrent()
	{
		string text = Interaction.InputBox("Enter name for this profile:", "Save Profile");
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		ModProfile modProfile = new ModProfile
		{
			Name = text,
			ThemeOverride = _settings.CurrentTheme
		};
		foreach (StardewMod allInstalledMod in _allInstalledMods)
		{
			modProfile.ModStates[allInstalledMod.UniqueId] = allInstalledMod.IsEnabled;
		}
		string contents = JsonConvert.SerializeObject(modProfile, Formatting.Indented);
		File.WriteAllText(Path.Combine(profilesPath, text + ".json"), contents);
		RefreshProfilesList();
		Speak("Profile saved.");
	}

	private void RefreshProfilesList()
	{
		if (listProfiles == null)
		{
			return;
		}
		listProfiles.BeginUpdate();
		listProfiles.Items.Clear();
		if (Directory.Exists(profilesPath))
		{
			string[] files = Directory.GetFiles(profilesPath, "*.json");
			foreach (string path in files)
			{
				try
				{
					ModProfile modProfile = JsonConvert.DeserializeObject<ModProfile>(File.ReadAllText(path));
					if (modProfile != null)
					{
						listProfiles.Items.Add(modProfile);
					}
				}
				catch
				{
				}
			}
		}
		listProfiles.EndUpdate();
	}

	private void ApplyProfile(ModProfile profile)
	{
		if (MessageBox.Show("Apply profile '" + profile.Name + "'? This will enable and disable mods to match the saved setup.", "Apply Profile", MessageBoxButtons.YesNo) == DialogResult.No)
		{
			return;
		}
		try
		{
			SetStatus("Applying profile...");
			bool flag = false;
			bool flag2 = false;
			foreach (StardewMod allInstalledMod in _allInstalledMods)
			{
				if (profile.ModStates == null || !profile.ModStates.ContainsKey(allInstalledMod.UniqueId))
				{
					continue;
				}
				bool flag3 = profile.ModStates[allInstalledMod.UniqueId];
				if (allInstalledMod.IsEnabled != flag3)
				{
					string path = Path.GetDirectoryName(allInstalledMod.FolderPath) ?? "";
					string fileName = Path.GetFileName(allInstalledMod.FolderPath);
					string text = (flag3 ? Path.Combine(path, fileName.Substring(1)) : Path.Combine(path, "." + fileName));
					Directory.Move(allInstalledMod.FolderPath, text);
					allInstalledMod.FolderPath = text;
					allInstalledMod.IsEnabled = flag3;
					if (flag3)
					{
						flag = true;
					}
					else
					{
						flag2 = true;
					}
				}
			}
			if (!string.IsNullOrEmpty(profile.ThemeOverride) && Directory.Exists(Path.Combine(themesPath, profile.ThemeOverride)))
			{
				_settings.CurrentTheme = profile.ThemeOverride;
				_settings.Save();
				Speak("Theme switched to: " + profile.ThemeOverride);
			}
			RefreshModList(checkUpdates: false);
			if (flag)
			{
				PlayAppSound("enable");
			}
			else if (flag2)
			{
				PlayAppSound("disable");
			}
			else
			{
				PlayAppSound("connect");
			}
			SetStatus("Profile applied: " + profile.Name);
		}
		catch (Exception ex)
		{
			MessageBox.Show("Failed to apply profile: " + ex.Message);
		}
	}

	private async void UpdateAllMods()
	{
		if (isUpdatingAll || listUpdates.Items.Count == 0)
		{
			return;
		}
		if (!isPremium)
		{
			Speak("Update All is a Nexus Mods Premium feature. Free users must update mods individually via the browser.");
			MessageBox.Show("Updating multiple mods automatically requires a Nexus Mods Premium account. Free users must download and install updates one-by-one.", "Premium Feature Required", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		else
		{
			if (MessageBox.Show($"Update all {listUpdates.Items.Count} mods?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.No)
			{
				return;
			}
			isUpdatingAll = true;
			try
			{
				List<StardewMod> mods = listUpdates.Items.Cast<StardewMod>().ToList();
				for (int i = 0; i < mods.Count; i++)
				{
					SetStatus($"Updating ({i + 1}/{mods.Count}): {mods[i].Name}");
					await DownloadAndInstallUpdate(mods[i], silent: true);
				}
				Speak("All updates finished.");
			}
			finally
			{
				isUpdatingAll = false;
				RefreshModList(checkUpdates: true);
			}
		}
	}

	private void OpenModPage()
	{
		ListBox listBox;
		if (mainTabs.SelectedIndex == 0)
		{
			listBox = listInstalled;
		}
		else if (mainTabs.SelectedIndex == 1)
		{
			listBox = listUpdates;
		}
		else
		{
			if (mainTabs.SelectedIndex != 3)
			{
				return;
			}
			listBox = listDiscovery;
		}
		if (listBox.SelectedItem is StardewMod stardewMod && !string.IsNullOrEmpty(stardewMod.NexusID))
		{
			Process.Start(new ProcessStartInfo("https://www.nexusmods.com/stardewvalley/mods/" + stardewMod.NexusID + "?tab=files")
			{
				UseShellExecute = true
			});
		}
	}

	private void BackupMod(string folderPath, string modName)
	{
		try
		{
			if (Directory.Exists(folderPath))
			{
				ZipFile.CreateFromDirectory(folderPath, Path.Combine(backupsPath, $"{modName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip"));
				PruneBackupsForMod(modName);
				RefreshBackupsList();
			}
		}
		catch (Exception ex)
		{
			LogError(modName, "Backup Error: " + ex.Message);
		}
	}

	private void PruneBackupsForMod(string modName)
	{
		if (!Directory.Exists(backupsPath))
		{
			return;
		}
		List<FileInfo> list = (from f in Directory.GetFiles(backupsPath, modName + "_*.zip")
			select new FileInfo(f) into f
			orderby f.CreationTime descending
			select f).ToList();
		if (list.Count <= _settings.MaxBackupsPerMod)
		{
			return;
		}
		for (int num = _settings.MaxBackupsPerMod; num < list.Count; num++)
		{
			try
			{
				list[num].Delete();
			}
			catch
			{
			}
		}
	}

	private void PruneAllBackups()
	{
		if (!Directory.Exists(backupsPath))
		{
			return;
		}
		string[] files = Directory.GetFiles(backupsPath, "*.zip");
		Dictionary<string, List<FileInfo>> dictionary = new Dictionary<string, List<FileInfo>>();
		string[] array = files;
		foreach (string text in array)
		{
			string text2 = Path.GetFileNameWithoutExtension(text);
			if (text2.Length > 16 && text2[text2.Length - 16] == '_')
			{
				text2 = text2.Substring(0, text2.Length - 16);
			}
			if (!dictionary.ContainsKey(text2))
			{
				dictionary[text2] = new List<FileInfo>();
			}
			dictionary[text2].Add(new FileInfo(text));
		}
		int num = 0;
		foreach (KeyValuePair<string, List<FileInfo>> item in dictionary)
		{
			List<FileInfo> list = item.Value.OrderByDescending((FileInfo f) => f.CreationTime).ToList();
			if (list.Count <= _settings.MaxBackupsPerMod)
			{
				continue;
			}
			for (int num2 = _settings.MaxBackupsPerMod; num2 < list.Count; num2++)
			{
				try
				{
					list[num2].Delete();
					num++;
				}
				catch
				{
				}
			}
		}
		RefreshBackupsList();
		Speak($"Pruning complete. Deleted {num} old backups.");
	}

	private async Task HandleNxmUrl(string url)
	{
		_ = 4;
		try
		{
			SetStatus("Parsing Nexus Link...");
			Uri uri = new Uri(url);
			string[] array = uri.AbsolutePath.Split('/');
			string modId = array[2];
			string fileId = array[4];
			string requestUri = $"https://api.nexusmods.com/v1/games/stardewvalley/mods/{modId}/files/{fileId}/download_link.json{uri.Query}";
			HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
			httpRequestMessage.Headers.Add("apikey", _settings.ApiKey);
			httpRequestMessage.Headers.Add("User-Agent", $"StardewAccessibleManager/{_appVersion}");
			string dlUri = JArray.Parse(await (await _httpClient.SendAsync(httpRequestMessage)).Content.ReadAsStringAsync())[0]["URI"]?.ToString() ?? "";
			HttpRequestMessage httpRequestMessage2 = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/games/stardewvalley/mods/" + modId + "/files.json");
			httpRequestMessage2.Headers.Add("apikey", _settings.ApiKey);
			httpRequestMessage2.Headers.Add("User-Agent", "StardewAccessibleManager/1.0.0");
			JObject jObject = JObject.Parse(await (await _httpClient.SendAsync(httpRequestMessage2)).Content.ReadAsStringAsync());
			string realName = jObject["files"]?.FirstOrDefault((JToken f) => f["file_id"]?.ToString() == fileId)?["file_name"]?.ToString() ?? (modId + "_file_" + fileId + ".zip");
			SetStatus("Downloading " + realName + "...");
			byte[] bytes = await _httpClient.GetByteArrayAsync(dlUri);
			string text = Path.Combine(downloadsPath, realName);
			File.WriteAllBytes(text, bytes);
			PlayAppSound("connect");
			if (MessageBox.Show("Downloaded " + realName + ". Install now?", "Success", MessageBoxButtons.YesNo) == DialogResult.Yes)
			{
				InstallFromZip(text);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("NXM Error: " + ex.Message);
		}
		finally
		{
		}
	}

	private void ManualInstall()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog
		{
			InitialDirectory = downloadsPath,
			Filter = "Zips|*.zip"
		};
		if (openFileDialog.ShowDialog() == DialogResult.OK)
		{
			InstallFromZip(openFileDialog.FileName);
		}
	}

	private bool IsNewerVersion(string? current, string? target)
	{
		if (string.IsNullOrEmpty(target))
		{
			return false;
		}
		if (string.IsNullOrEmpty(current))
		{
			return true;
		}
		string[] array = current.Split('.');
		string[] array2 = target.Split('.');
		for (int i = 0; i < Math.Max(array.Length, array2.Length); i++)
		{
			int result;
			int num = ((i < array.Length && int.TryParse(array[i], out result)) ? result : 0);
			int result2;
			int num2 = ((i < array2.Length && int.TryParse(array2[i], out result2)) ? result2 : 0);
			if (num2 > num)
			{
				return true;
			}
			if (num > num2)
			{
				return false;
			}
		}
		return false;
	}

	private void InstallFromZip(string zipPath)
	{
		try
		{
			string text = Path.Combine(Path.GetTempPath(), "StardewExtract_" + Path.GetRandomFileName());
			Directory.CreateDirectory(text);
			ZipFile.ExtractToDirectory(zipPath, text);
			string[] manifests = Directory.GetFiles(text, "manifest.json", SearchOption.AllDirectories);
			if (manifests.Length == 0)
			{
				throw new Exception("No manifest.json found.");
			}

			string sourceFolder = "";
			string targetFolderName = "";
			bool isGroup = manifests.Length > 1;

			if (isGroup)
			{
				// For groups, find the top-most folder that contains all manifests
				string commonPath = Path.GetDirectoryName(manifests[0]) ?? text;
				foreach (string m in manifests)
				{
					string dir = Path.GetDirectoryName(m) ?? text;
					while (!dir.StartsWith(commonPath))
					{
						commonPath = Path.GetDirectoryName(commonPath) ?? text;
					}
				}
				sourceFolder = commonPath;
				targetFolderName = Path.GetFileName(sourceFolder);
				// If the common path is the root extract folder, use the zip name or a default
				if (sourceFolder.TrimEnd('\\') == text.TrimEnd('\\'))
				{
					targetFolderName = Path.GetFileNameWithoutExtension(zipPath);
				}
			}
			else
			{
				string text2 = manifests[0];
				JObject jObject = JObject.Parse(File.ReadAllText(text2));
				string uid = ((string?)jObject["UniqueID"]) ?? Guid.NewGuid().ToString();
				string text3 = ((string?)jObject["Name"]) ?? "Unknown";
				string text4 = ((string?)jObject["Version"]) ?? "0";
				
				StardewMod stardewMod = _allInstalledMods.FirstOrDefault((StardewMod mod) => mod.UniqueId == uid);
				if (stardewMod != null)
				{
					if ((stardewMod.Version == text4 && MessageBox.Show("Reinstall same version?", "Match", MessageBoxButtons.YesNo) == DialogResult.No) || (!IsNewerVersion(stardewMod.Version, text4) && MessageBox.Show("Install older version?", "Downgrade", MessageBoxButtons.YesNo) == DialogResult.No))
					{
						return;
					}
					// If this single mod is currently inside a group folder, we should ideally update the whole group
					// but since we only have a single mod zip, we just replace its specific folder.
					BackupMod(stardewMod.FolderPath, text3);
					Directory.Delete(stardewMod.FolderPath, recursive: true);
				}
				sourceFolder = Path.GetDirectoryName(text2) ?? text;
				targetFolderName = Path.GetFileName(sourceFolder);
				if (sourceFolder.TrimEnd('\\') == text.TrimEnd('\\'))
				{
					targetFolderName = text3.Replace(" ", "");
				}
			}

			string text6 = Path.Combine(_settings.ModsPath, targetFolderName);
			
			// If it's a group and the target folder exists, back it up
			if (isGroup && Directory.Exists(text6))
			{
				BackupMod(text6, targetFolderName);
				Directory.Delete(text6, recursive: true);
			}
			else if (Directory.Exists(text6))
			{
				// For single mod, if folder name matches (even if UID didn't match earlier)
				Directory.Delete(text6, recursive: true);
			}

			Directory.CreateDirectory(text6);
			string[] directories = Directory.GetDirectories(sourceFolder, "*", SearchOption.AllDirectories);
			for (int num = 0; num < directories.Length; num++)
			{
				Directory.CreateDirectory(directories[num].Replace(sourceFolder, text6));
			}
			string[] files = Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories);
			foreach (string obj in files)
			{
				File.Copy(obj, obj.Replace(sourceFolder, text6), overwrite: true);
			}
			Directory.Delete(text, recursive: true);
			PlayAppSound("connect");
			MessageBox.Show((isGroup ? "Mod Group " : "") + targetFolderName + " installed!");
			RefreshModList(checkUpdates: false);
		}
		catch (Exception ex)
		{
			MessageBox.Show("Install failed: " + ex.Message);
		}
	}

	private string errorLogPath => Path.Combine(dataBasePath, "mod_manager_log.txt");

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

	private void PlayAppSound(string name, string? themeOverride = null)
	{
		Task.Run(delegate
		{
			try
			{
				string path = themeOverride ?? _settings.CurrentTheme;
				string path2 = Path.Combine(themesPath, path, name);
				if (!Directory.Exists(path2))
				{
					path2 = Path.Combine(themesPath, "Default", name);
				}
				if (Directory.Exists(path2))
				{
					string[] files = Directory.GetFiles(path2, "*.ogg");
					if (files.Length != 0)
					{
						using (VorbisWaveReader waveProvider = new VorbisWaveReader(files[0]))
						{
							using WaveOutEvent waveOutEvent = new WaveOutEvent();
							waveOutEvent.Volume = (float)_settings.SoundVolume / 100f;
							waveOutEvent.Init(waveProvider);
							waveOutEvent.Play();
							while (waveOutEvent.PlaybackState == PlaybackState.Playing)
							{
								Thread.Sleep(100);
							}
							return;
						}
					}
				}
			}
			catch
			{
			}
		});
	}

	private async void RefreshModList(bool checkUpdates)
	{
		if (string.IsNullOrEmpty(_settings.ApiKey))
		{
			if (!_isSettingsOpen)
			{
				SetStatus("Authentication Required");
				PlayAppSound("disconnect");
				ShowSettings();
			}
			return;
		}
		SetStatus("Connecting to Nexus...");
		if (!(await ValidateNexusConnection()))
		{
			SetStatus("Authentication Failed - Check API Key");
			PlayAppSound("error");
			return;
		}
		SetStatus("Connected as " + nexusUser);
		PlayAppSound("connect");
		listInstalled.BeginUpdate();
		if (checkUpdates)
		{
			listUpdates.BeginUpdate();
		}
		listInstalled.Items.Clear();
		if (checkUpdates)
		{
			listUpdates.Items.Clear();
		}
		_allInstalledMods.Clear();
		if (!Directory.Exists(_settings.ModsPath))
		{
			listInstalled.EndUpdate();
			if (checkUpdates)
			{
				listUpdates.EndUpdate();
			}
			MessageBox.Show("Mods path invalid.");
			return;
		}
		string[] files = Directory.GetFiles(_settings.ModsPath, "manifest.json", SearchOption.AllDirectories);
		string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mod_id_map.json");
		JObject jObject = (File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject()) ?? new JObject();
		HashSet<string> hashSet = new HashSet<string>();
		string[] array = files;
		foreach (string text in array)
		{
			try
			{
				JObject jObject2 = JObject.Parse(File.ReadAllText(text));
				StardewMod stardewMod = new StardewMod
				{
					Name = (((string?)jObject2["Name"]) ?? "Unknown"),
					Version = (((string?)jObject2["Version"]) ?? "0"),
					Author = (((string?)jObject2["Author"]) ?? "User"),
					UniqueId = (((string?)jObject2["UniqueID"]) ?? Guid.NewGuid().ToString()),
					Description = (((string?)jObject2["Description"]) ?? ""),
					NexusID = ParseNexusId(jObject2["UpdateKeys"]),
					FolderPath = (Path.GetDirectoryName(text) ?? ""),
					IsEnabled = !Path.GetFileName(Path.GetDirectoryName(text) ?? "").StartsWith(".")
				};
				if (jObject2["Dependencies"] is JArray jArray)
				{
					foreach (JToken item in jArray)
					{
						if (item != null)
						{
							stardewMod.Dependencies.Add(new ModDependency
							{
								UniqueId = (((string?)item["UniqueID"]) ?? "Unknown"),
								MinimumVersion = (string?)item["MinimumVersion"],
								IsRequired = (((bool?)item["IsRequired"]) ?? true)
							});
						}
					}
				}
				string uniqueId = stardewMod.UniqueId;
				if (uniqueId != null && jObject.TryGetValue(uniqueId, out JToken value))
				{
					stardewMod.NexusID = value?.ToString();
				}
				if (_settings.ModCategories.ContainsKey(stardewMod.UniqueId))
				{
					stardewMod.Category = _settings.ModCategories[stardewMod.UniqueId];
				}
				else
				{
					stardewMod.Category = DetectCategory(stardewMod.Name, stardewMod.Description);
				}
				hashSet.Add(stardewMod.Category);
				_allInstalledMods.Add(stardewMod);
			}
			catch (Exception ex)
			{
				LogError(text, "Parse Error: " + ex.Message);
			}
		}
		cmbCategoryFilter.BeginUpdate();
		string text2 = cmbCategoryFilter.SelectedItem?.ToString() ?? "All Categories";
		cmbCategoryFilter.Items.Clear();
		cmbCategoryFilter.Items.Add("All Categories");
		foreach (string item2 in hashSet.OrderBy((string c) => c))
		{
			cmbCategoryFilter.Items.Add(item2);
		}
		if (cmbCategoryFilter.Items.Contains(text2))
		{
			cmbCategoryFilter.SelectedItem = text2;
		}
		else
		{
			cmbCategoryFilter.SelectedIndex = 0;
		}
		cmbCategoryFilter.EndUpdate();
		foreach (StardewMod allInstalledMod in _allInstalledMods)
		{
			foreach (ModDependency dep in allInstalledMod.Dependencies)
			{
				StardewMod stardewMod2 = _allInstalledMods.FirstOrDefault((StardewMod m) => m.UniqueId == dep.UniqueId);
				if (stardewMod2 != null)
				{
					dep.IsPresent = true;
					dep.IsEnabled = stardewMod2.IsEnabled;
					dep.IsNewEnough = IsNewerVersion(dep.MinimumVersion, stardewMod2.Version) || dep.MinimumVersion == stardewMod2.Version;
				}
			}
		}
		RebuildInstalledListBox();
		listInstalled.EndUpdate();
		if (checkUpdates)
		{
			listUpdates.BeginUpdate();
		}
		if (!checkUpdates)
		{
			return;
		}
		List<IGrouping<string, StardewMod>> list = (from m in _allInstalledMods
			where !string.IsNullOrEmpty(m.NexusID)
			group m by m.NexusID).ToList();
		_isLoading = true;
		_activeChecks = list.Count;
		Speak("Checking for updates.");
		RunLoadingLoop();
		foreach (IGrouping<string, StardewMod> item3 in list)
		{
			CheckForUpdates(item3.ToList());
		}
	}

	private void RebuildInstalledListBox()
	{
		string query = txtSearchInstalled.Text.Trim().ToLower();
		string category = cmbCategoryFilter.SelectedItem?.ToString() ?? "All Categories";
		bool flag = !string.IsNullOrEmpty(query) || category != "All Categories";
		listInstalled.BeginUpdate();
		StardewMod stardewMod = listInstalled.SelectedItem as StardewMod;
		listInstalled.Items.Clear();
		foreach (IGrouping<string, StardewMod> item2 in from g in _allInstalledMods.Where((StardewMod m) => !m.IsGroup).GroupBy(delegate(StardewMod m)
			{
				string relativePath = Path.GetRelativePath(_settings.ModsPath, m.FolderPath);
				int num2 = relativePath.IndexOf(Path.DirectorySeparatorChar);
				return (num2 != -1) ? relativePath.Substring(0, num2) : relativePath;
			})
			orderby g.Key
			select g)
		{
			List<StardewMod> list = item2.ToList();
			List<StardewMod> list2 = list.Where((StardewMod m) => (string.IsNullOrEmpty(query) || m.Name.ToLower().Contains(query) || m.Author.ToLower().Contains(query)) && (category == "All Categories" || m.Category == category)).ToList();
			if (list2.Count == 0)
			{
				continue;
			}
			if (flag || list.Count == 1)
			{
				foreach (StardewMod item3 in list2)
				{
					item3.IsSubMod = false;
					item3.IsGroup = false;
					listInstalled.Items.Add(item3);
				}
				continue;
			}
			bool flag2 = _expandedGroups.Contains(item2.Key);
			StardewMod item = new StardewMod
			{
				IsGroup = true,
				GroupName = item2.Key,
				IsExpanded = flag2,
				UniqueId = "GROUP:" + item2.Key,
				SubMods = list,
				FolderPath = Path.Combine(_settings.ModsPath, item2.Key)
			};
			listInstalled.Items.Add(item);
			if (!flag2)
			{
				continue;
			}
			foreach (StardewMod item4 in list2)
			{
				item4.IsSubMod = true;
				item4.IsGroup = false;
				listInstalled.Items.Add(item4);
			}
		}
		if (stardewMod != null)
		{
			for (int num = 0; num < listInstalled.Items.Count; num++)
			{
				if (listInstalled.Items[num] is StardewMod stardewMod2 && stardewMod2.UniqueId == stardewMod.UniqueId)
				{
					listInstalled.SelectedIndex = num;
					break;
				}
			}
		}
		listInstalled.EndUpdate();
	}

	private void RefreshBackupsList()
	{
		if (listBackups == null)
		{
			return;
		}
		listBackups.BeginUpdate();
		listBackups.Items.Clear();
		if (Directory.Exists(backupsPath))
		{
			string[] files = Directory.GetFiles(backupsPath, "*.zip");
			foreach (string text in files)
			{
				string text2 = Path.GetFileNameWithoutExtension(text);
				if (text2.Length > 16 && text2[text2.Length - 16] == '_')
				{
					text2 = text2.Substring(0, text2.Length - 16);
				}
				listBackups.Items.Add(new BackupItem
				{
					Name = text2,
					FullPath = text
				});
			}
		}
		listBackups.EndUpdate();
	}

	private async Task<bool> ValidateNexusConnection()
	{
		_ = 1;
		try
		{
			HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/users/validate.json");
			httpRequestMessage.Headers.Add("apikey", _settings.ApiKey);
			httpRequestMessage.Headers.Add("User-Agent", $"StardewAccessibleManager/{_appVersion}");
			HttpResponseMessage httpResponseMessage = await _httpClient.SendAsync(httpRequestMessage);
			if (httpResponseMessage.IsSuccessStatusCode)
			{
				JObject jObject = JObject.Parse(await httpResponseMessage.Content.ReadAsStringAsync());
				nexusUser = jObject["name"]?.ToString() ?? "User";
				isPremium = (bool)(jObject["is_premium"] ?? ((JToken)false));
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	private void PromptForApiKey()
	{
		string text = Interaction.InputBox("Paste API Key:", "Nexus Login", _settings.ApiKey);
		if (!string.IsNullOrEmpty(text))
		{
			_settings.ApiKey = text.Trim();
			_settings.Save();
			RefreshModList(checkUpdates: true);
		}
	}

	private void SetManualNexusId()
	{
		if (listInstalled.SelectedItem is StardewMod stardewMod)
		{
			string defaultResponse = stardewMod.NexusID ?? "";
			string text = Interaction.InputBox("Nexus ID for " + stardewMod.Name + ":", "Manual ID", defaultResponse);
			if (!string.IsNullOrEmpty(text) && long.TryParse(text, out var _))
			{
				string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mod_id_map.json");
				JObject jObject = (File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject()) ?? new JObject();
				jObject[stardewMod.UniqueId] = text.Trim();
				File.WriteAllText(path, jObject.ToString());
				RefreshModList(checkUpdates: false);
			}
		}
	}

	private void SetManualCategory()
	{
		if (listInstalled.SelectedItem is StardewMod stardewMod)
		{
			string text = Interaction.InputBox("Assign category for " + stardewMod.Name + ":", "Change Category", stardewMod.Category);
			if (!string.IsNullOrEmpty(text))
			{
				_settings.ModCategories[stardewMod.UniqueId] = text.Trim();
				_settings.Save();
				RefreshModList(checkUpdates: false);
				Speak($"Category for {stardewMod.Name} set to {text}.");
			}
		}
	}

	private void BatchManageCategory()
	{
		string category = cmbCategoryFilter.SelectedItem?.ToString() ?? "All Categories";
		List<StardewMod> list = _allInstalledMods.Where((StardewMod m) => category == "All Categories" || m.Category == category).ToList();
		if (list.Count == 0)
		{
			Speak("No mods in this category.");
			return;
		}
		DialogResult dialogResult = MessageBox.Show($"Batch Action for category '{category}':\n\nYES: Enable all {list.Count} mods.\nNO: Disable all {list.Count} mods.\nCANCEL: Do nothing.", "Batch Category Management", MessageBoxButtons.YesNoCancel);
		if (dialogResult == DialogResult.Cancel)
		{
			return;
		}
		bool flag = dialogResult == DialogResult.Yes;
		try
		{
			SetStatus($"Batch {(flag ? "Enabling" : "Disabling")} {list.Count} mods...");
			foreach (StardewMod item in list)
			{
				if (item.IsEnabled != flag)
				{
					string path = Path.GetDirectoryName(item.FolderPath) ?? "";
					string fileName = Path.GetFileName(item.FolderPath);
					string text = (flag ? Path.Combine(path, fileName.Substring(1)) : Path.Combine(path, "." + fileName));
					Directory.Move(item.FolderPath, text);
					item.FolderPath = text;
					item.IsEnabled = flag;
				}
			}
			RefreshModList(checkUpdates: false);
			if (flag)
			{
				PlayAppSound("enable");
			}
			else
			{
				PlayAppSound("disable");
			}
			Speak($"Batch action complete. {list.Count} mods {(flag ? "Enabled" : "Disabled")}.");
		}
		catch (Exception ex)
		{
			MessageBox.Show("Batch action failed: " + ex.Message);
		}
	}

	private void ShowDependencies()
	{
		if (!(listInstalled.SelectedItem is StardewMod stardewMod))
		{
			return;
		}
		if (stardewMod.Dependencies.Count == 0)
		{
			MessageBox.Show("No dependencies.");
			return;
		}
		StringBuilder stringBuilder = new StringBuilder("Dependencies for " + stardewMod.Name + ":\n");
		foreach (ModDependency dependency in stardewMod.Dependencies)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 3, stringBuilder2);
			handler.AppendLiteral("- ");
			handler.AppendFormatted(dependency.UniqueId);
			handler.AppendLiteral(": ");
			handler.AppendFormatted(dependency.IsPresent ? (dependency.IsEnabled ? (dependency.IsNewEnough ? "OK" : "Old") : "Disabled") : "Missing");
			handler.AppendLiteral(" (");
			handler.AppendFormatted(dependency.IsRequired ? "Req" : "Opt");
			handler.AppendLiteral(")");
			stringBuilder2.AppendLine(ref handler);
		}
		MessageBox.Show(stringBuilder.ToString(), "Dependencies");
	}

	private void QuickFixDependencies()
	{
		if (listInstalled.SelectedItem is StardewMod stardewMod)
		{
			List<ModDependency> list = stardewMod.Dependencies.Where((ModDependency d) => d.IsRequired && !d.IsPresent).ToList();
			if (list.Count == 0)
			{
				Speak("No missing required dependencies for this mod.");
				return;
			}
			ModDependency modDependency = list[0];
			if (MessageBox.Show("Search for missing dependency: " + modDependency.UniqueId + "?", "Quick-Fix", MessageBoxButtons.YesNo) == DialogResult.Yes)
			{
				mainTabs.SelectedIndex = 3;
				txtSearch.Text = modDependency.UniqueId;
				RunDiscovery();
			}
		}
		else
		{
			if (mainTabs.SelectedIndex != 5 || listLog.SelectedItem == null)
			{
				return;
			}
			string text = LogAnalyzer.ExtractMissingModId(listLog.SelectedItem.ToString());
			if (!string.IsNullOrEmpty(text))
			{
				if (MessageBox.Show("Search for missing dependency: " + text + "?", "Quick-Fix from Log", MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					mainTabs.SelectedIndex = 3;
					txtSearch.Text = text;
					RunDiscovery();
				}
			}
			else
			{
				Speak("Could not identify a missing mod in this log entry.");
			}
		}
	}

	private void ToggleModStatus()
	{
		if (!(listInstalled.SelectedItem is StardewMod stardewMod))
		{
			return;
		}
		try
		{
			string path = Path.GetDirectoryName(stardewMod.FolderPath) ?? "";
			string fileName = Path.GetFileName(stardewMod.FolderPath);
			string text = (stardewMod.IsEnabled ? Path.Combine(path, "." + fileName) : Path.Combine(path, fileName.StartsWith(".") ? fileName.Substring(1) : fileName));
			Directory.Move(stardewMod.FolderPath, text);
			stardewMod.FolderPath = text;
			stardewMod.IsEnabled = !stardewMod.IsEnabled;
			RefreshModList(checkUpdates: false);
			if (stardewMod.IsEnabled)
			{
				PlayAppSound("enable");
			}
			else
			{
				PlayAppSound("disable");
			}
			SetStatus(stardewMod.Name + " is now " + (stardewMod.IsEnabled ? "Enabled" : "Disabled"));
		}
		catch (Exception ex)
		{
			MessageBox.Show("Toggle failed: " + ex.Message);
		}
	}

	private void DeleteSelectedMod()
	{
		if (listInstalled.SelectedItem is StardewMod stardewMod && MessageBox.Show("Are you sure you want to PERMANENTLY DELETE " + stardewMod.Name + "?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
		{
			try
			{
				BackupMod(stardewMod.FolderPath, stardewMod.Name + "_Delete");
				Directory.Delete(stardewMod.FolderPath, recursive: true);
				PlayAppSound("disable");
				SetStatus("Deleted " + stardewMod.Name);
				RefreshModList(checkUpdates: false);
			}
			catch (Exception ex)
			{
				PlayAppSound("error");
				MessageBox.Show("Failed to delete mod: " + ex.Message);
			}
		}
	}

	private void DeleteSelectedBackup()
	{
		if (listBackups.SelectedItem is BackupItem backupItem && MessageBox.Show("Permanently delete backup " + backupItem.Name + "?", "Confirm Delete", MessageBoxButtons.YesNo) == DialogResult.Yes)
		{
			try
			{
				File.Delete(backupItem.FullPath);
				PlayAppSound("disable");
				RefreshBackupsList();
			}
			catch (Exception ex)
			{
				MessageBox.Show("Delete failed: " + ex.Message);
			}
		}
	}

	private void ShowSettings()
	{
		if (_isSettingsOpen)
		{
			return;
		}
		_isSettingsOpen = true;
		Form f = new Form
		{
			Text = "Settings - Escape to Cancel",
			Size = new Size(500, 750),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(15),
			RowCount = 12
		};
		tableLayoutPanel.Controls.Add(new Label
		{
			Text = "Mods Folder Path:",
			AutoSize = true
		}, 0, 0);
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			Height = 35
		};
		TextBox tPath = new TextBox
		{
			Text = _settings.ModsPath,
			Width = 350,
			Font = new Font("Segoe UI", 10f),
			AccessibleName = "Mods Folder Path"
		};
		Button button = new Button
		{
			Text = "Browse",
			Left = 360,
			Width = 80
		};
		button.Click += delegate
		{
			using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
			if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
			{
				tPath.Text = folderBrowserDialog.SelectedPath;
			}
		};
		panel.Controls.AddRange(tPath, button);
		tableLayoutPanel.Controls.Add(panel, 0, 1);
		tableLayoutPanel.Controls.Add(new Label
		{
			Text = "Nexus API Key:",
			AutoSize = true,
			Padding = new Padding(0, 10, 0, 0)
		}, 0, 2);
		TextBox tKey = new TextBox
		{
			Text = _settings.ApiKey,
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 10f),
			AccessibleName = "Nexus API Key"
		};
		tableLayoutPanel.Controls.Add(tKey, 0, 3);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight,
			Padding = new Padding(0, 10, 0, 0),
			AutoSize = true
		};
		CheckBox cSplash = new CheckBox
		{
			Text = "Show Splash Screen on Startup",
			Checked = _settings.ShowSplashScreen,
			AutoSize = true,
			AccessibleName = "Show Splash Screen"
		};
		CheckBox cRandomLogo = new CheckBox
		{
			Text = "Random Logo at Startup",
			Checked = _settings.RandomLogoStartup,
			AutoSize = true,
			AccessibleName = "Random Logo Startup",
			Visible = _settings.ShowSplashScreen
		};
		CheckBox cUpdates = new CheckBox
		{
			Text = "Check for Mod Updates at Startup",
			Checked = _settings.CheckForUpdatesAtStartup,
			AutoSize = true,
			AccessibleName = "Check Updates at Startup"
		};
		cSplash.CheckedChanged += delegate
		{
			Speak(cSplash.Checked ? "Splash Screen Enabled" : "Splash Screen Disabled");
			cRandomLogo.Visible = cSplash.Checked;
		};
		cRandomLogo.CheckedChanged += delegate
		{
			Speak(cRandomLogo.Checked ? "Random Logo Enabled" : "Random Logo Disabled");
		};
		cUpdates.CheckedChanged += delegate
		{
			Speak(cUpdates.Checked ? "Auto-updates Enabled" : "Auto-updates Disabled");
		};
		flowLayoutPanel.Controls.AddRange(cSplash, cRandomLogo, cUpdates);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 4);
		tableLayoutPanel.Controls.Add(new Label
		{
			Text = "Select Specific Logo:",
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		}, 0, 5);
		ComboBox cmbLogo = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 300,
			AccessibleName = "Select Specific Logo"
		};
		cmbLogo.SelectedIndexChanged += delegate
		{
			if (f.Visible)
			{
				PreviewLogo();
			}
		};
		cmbLogo.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Space && cmbLogo.SelectedItem != null)
			{
				pe.Handled = true;
				pe.SuppressKeyPress = true;
				PreviewLogo();
			}
		};
		tableLayoutPanel.Controls.Add(cmbLogo, 0, 6);
		FlowLayoutPanel flowLayoutPanel2 = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight
		};
		flowLayoutPanel2.Controls.Add(new Label
		{
			Text = "Sound Volume (0-100):",
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		});
		NumericUpDown nVol = new NumericUpDown
		{
			Value = _settings.SoundVolume,
			Minimum = 0m,
			Maximum = 100m,
			Width = 60
		};
		flowLayoutPanel2.Controls.Add(nVol);
		tableLayoutPanel.Controls.Add(flowLayoutPanel2, 0, 7);
		FlowLayoutPanel flowLayoutPanel3 = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight
		};
		flowLayoutPanel3.Controls.Add(new Label
		{
			Text = "Max Backups per Mod:",
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		});
		NumericUpDown nPrune = new NumericUpDown
		{
			Value = _settings.MaxBackupsPerMod,
			Minimum = 1m,
			Maximum = 50m,
			Width = 60
		};
		flowLayoutPanel3.Controls.Add(nPrune);
		tableLayoutPanel.Controls.Add(flowLayoutPanel3, 0, 8);
		FlowLayoutPanel flowLayoutPanel4 = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight
		};
		flowLayoutPanel4.Controls.Add(new Label
		{
			Text = "Current Audio Theme:",
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		});
		ComboBox cTheme = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 150
		};
		cTheme.Items.AddRange(Directory.GetDirectories(themesPath).Select(Path.GetFileName).Cast<object>()
			.ToArray());
		cTheme.SelectedItem = _settings.CurrentTheme;
		cTheme.SelectedIndexChanged += delegate
		{
			RefreshLogoList(cTheme.SelectedItem?.ToString() ?? "Default");
		};
		flowLayoutPanel4.Controls.Add(cTheme);
		tableLayoutPanel.Controls.Add(flowLayoutPanel4, 0, 9);
		RefreshLogoList(_settings.CurrentTheme);
		Button button2 = new Button
		{
			Text = "Clear All Ignored Updates",
			Dock = DockStyle.Top,
			Height = 35
		};
		button2.Click += delegate
		{
			_settings.IgnoredVersions.Clear();
			_settings.Save();
			Speak("All ignored updates cleared.");
			RefreshModList(checkUpdates: true);
		};
		tableLayoutPanel.Controls.Add(button2, 0, 10);
		FlowLayoutPanel flowLayoutPanel5 = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			FlowDirection = FlowDirection.RightToLeft,
			Height = 50
		};
		Button button3 = new Button
		{
			Text = "Save Settings",
			Width = 120,
			Height = 35
		};
		button3.Click += delegate
		{
			string text = tPath.Text.Trim();
			string text2 = tKey.Text.Trim();
			if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(text2))
			{
				Speak("Error: Mods path and API key are both required.");
				MessageBox.Show("Please provide both the Stardew Valley Mods path and your Nexus API Key.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			}
			else
			{
				_settings.ModsPath = text;
				_settings.ApiKey = text2;
				_settings.ShowSplashScreen = cSplash.Checked;
				_settings.RandomLogoStartup = cRandomLogo.Checked;
				_settings.SelectedLogoFile = cmbLogo.SelectedItem?.ToString() ?? "";
				_settings.CheckForUpdatesAtStartup = cUpdates.Checked;
				_settings.SoundVolume = (int)nVol.Value;
				_settings.MaxBackupsPerMod = (int)nPrune.Value;
				_settings.CurrentTheme = cTheme.SelectedItem?.ToString() ?? "Default";
				_settings.Save();
				f.Close();
				Task.Delay(100).ContinueWith(delegate
				{
					Invoke(delegate
					{
						RefreshModList(checkUpdates: false);
					});
				});
				Speak("Settings saved.");
			}
		};
		Button button4 = new Button
		{
			Text = "Cancel",
			Width = 100,
			Height = 35
		};
		button4.Click += delegate
		{
			Speak("Changes cancelled.");
			f.Close();
		};
		flowLayoutPanel5.Controls.AddRange(button3, button4);
		f.FormClosing += delegate
		{
			_isSettingsOpen = false;
		};
		f.Controls.Add(flowLayoutPanel5);
		f.Controls.Add(tableLayoutPanel);
		f.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Escape)
			{
				Speak("Changes cancelled.");
				f.Close();
			}
		};
		f.ShowDialog();
		void PreviewLogo()
		{
			if (cmbLogo.SelectedItem != null)
			{
				string theme = _settings.CurrentTheme;
				string file = cmbLogo.SelectedItem.ToString();
				Task.Run(delegate
				{
					try
					{
						string text = Path.Combine(themesPath, theme, "logo", file);
						if (File.Exists(text))
						{
							using (VorbisWaveReader waveProvider = new VorbisWaveReader(text))
							{
								using WaveOutEvent waveOutEvent = new WaveOutEvent();
								waveOutEvent.Volume = (float)_settings.SoundVolume / 100f;
								waveOutEvent.Init(waveProvider);
								waveOutEvent.Play();
								while (waveOutEvent.PlaybackState == PlaybackState.Playing)
								{
									Thread.Sleep(100);
								}
								return;
							}
						}
					}
					catch
					{
					}
				});
			}
		}
		void RefreshLogoList(string theme)
		{
			cmbLogo.Items.Clear();
			string path = Path.Combine(themesPath, theme, "logo");
			if (Directory.Exists(path))
			{
				object[] items = Directory.GetFiles(path, "*.ogg").Select(Path.GetFileName).Cast<object>()
					.ToArray();
				cmbLogo.Items.AddRange(items);
				if (!string.IsNullOrEmpty(_settings.SelectedLogoFile) && cmbLogo.Items.Contains(_settings.SelectedLogoFile))
				{
					cmbLogo.SelectedItem = _settings.SelectedLogoFile;
				}
				else if (cmbLogo.Items.Count > 0)
				{
					cmbLogo.SelectedIndex = 0;
				}
			}
		}
	}

	private void Form1_KeyDown(object? sender, KeyEventArgs e)
	{
		if (IsShortcut(e, "Manual"))
		{
			e.SuppressKeyPress = true;
			ShowManual();
		}
		if (IsShortcut(e, "ContextHelp"))
		{
			e.SuppressKeyPress = true;
			ShowContextHelp();
		}
		if (IsShortcut(e, "LaunchGame"))
		{
			e.SuppressKeyPress = true;
			LaunchGame();
		}
		if (IsShortcut(e, "OpenLogFile"))
		{
			e.SuppressKeyPress = true;
			OpenRawSmapiLog();
		}
		if (IsShortcut(e, "Settings"))
		{
			e.SuppressKeyPress = true;
			ShowSettings();
		}
		if (IsShortcut(e, "Login"))
		{
			e.SuppressKeyPress = true;
			PromptForApiKey();
		}
		if (IsShortcut(e, "InstallZip"))
		{
			e.SuppressKeyPress = true;
			ManualInstall();
		}
		if (IsShortcut(e, "OpenModPage"))
		{
			e.SuppressKeyPress = true;
			OpenModPage();
		}
		if (IsShortcut(e, "OpenDownloads"))
		{
			e.SuppressKeyPress = true;
			Process.Start("explorer.exe", downloadsPath);
		}
		if (IsShortcut(e, "OpenBackups"))
		{
			e.SuppressKeyPress = true;
			Process.Start("explorer.exe", backupsPath);
		}
		if (IsShortcut(e, "ManualID"))
		{
			e.SuppressKeyPress = true;
			SetManualNexusId();
		}
		if (IsShortcut(e, "ChangeCategory"))
		{
			e.SuppressKeyPress = true;
			if (e.Shift)
			{
				BatchManageCategory();
			}
			else
			{
				SetManualCategory();
			}
		}
		if (IsShortcut(e, "ShowDependencies"))
		{
			e.SuppressKeyPress = true;
			ShowDependencies();
		}
		if (IsShortcut(e, "QuickFix"))
		{
			e.SuppressKeyPress = true;
			QuickFixDependencies();
		}
		if (IsShortcut(e, "Search"))
		{
			e.SuppressKeyPress = true;
			if (mainTabs.SelectedIndex == 0)
			{
				txtSearchInstalled.Focus();
			}
			else if (mainTabs.SelectedIndex == 5)
			{
				txtSearchLog.Focus();
			}
		}
		if (IsShortcut(e, "UpdateAll"))
		{
			e.SuppressKeyPress = true;
			UpdateAllMods();
		}
		if (IsShortcut(e, "SaveProfile"))
		{
			e.SuppressKeyPress = true;
			CreateProfileFromCurrent();
		}
		if (IsShortcut(e, "ReadDescription"))
		{
			e.SuppressKeyPress = true;
			ReadSelectedDescription();
		}
		if (IsShortcut(e, "PruneBackups"))
		{
			e.SuppressKeyPress = true;
			PruneAllBackups();
		}
		if (IsShortcut(e, "RefreshAll"))
		{
			e.SuppressKeyPress = true;
			RefreshModList(checkUpdates: true);
			RefreshBackupsList();
			RefreshProfilesList();
			RefreshSmapiLog();
			Speak("Refreshing everything.");
		}
		if (IsShortcut(e, "RefreshInstalled"))
		{
			e.SuppressKeyPress = true;
			RefreshModList(checkUpdates: false);
			Speak("Refreshed installed mods.");
		}
		if (IsShortcut(e, "OpenErrorLog") && File.Exists("mod_manager_log.txt"))
		{
			Process.Start(new ProcessStartInfo("notepad.exe", "mod_manager_log.txt")
			{
				UseShellExecute = true
			});
		}
	}

	private void ReadSelectedDescription()
	{
		ListBox listBox;
		if (mainTabs.SelectedIndex == 0)
		{
			listBox = listInstalled;
		}
		else if (mainTabs.SelectedIndex == 1)
		{
			listBox = listUpdates;
		}
		else
		{
			if (mainTabs.SelectedIndex != 3)
			{
				return;
			}
			listBox = listDiscovery;
		}
		if (listBox.SelectedItem is StardewMod stardewMod)
		{
			if (!string.IsNullOrEmpty(stardewMod.Description))
			{
				Speak(stardewMod.Description);
			}
			else
			{
				Speak("No description available for this mod.");
			}
		}
	}

	private async void List_KeyDown(object? sender, KeyEventArgs e)
	{
		if (!(sender is ListBox list))
		{
			return;
		}
		if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z && !e.Control && !e.Alt)
		{
			char c = char.ToLower((char)e.KeyCode);
			_searchTimer.Stop();
			_searchBuffer += c;
			_searchTimer.Start();
			int num = list.SelectedIndex;
			if (_searchBuffer.Length == 1)
			{
				num++;
			}
			for (int i = 0; i < list.Items.Count; i++)
			{
				int num2 = (num + i) % list.Items.Count;
				if (list.Items[num2] is StardewMod stardewMod)
				{
					string text = (stardewMod.IsGroup ? stardewMod.GroupName : stardewMod.Name).ToLower();
					if (!string.IsNullOrEmpty(text) && text.StartsWith(_searchBuffer))
					{
						list.SelectedIndex = num2;
						e.Handled = true;
						e.SuppressKeyPress = true;
						return;
					}
				}
			}
			if (_searchBuffer.Length > 1)
			{
				string text2 = c.ToString();
				for (int j = 0; j < list.Items.Count; j++)
				{
					int num3 = (list.SelectedIndex + 1 + j) % list.Items.Count;
					if (list.Items[num3] is StardewMod stardewMod2 && (stardewMod2.IsGroup ? stardewMod2.GroupName : stardewMod2.Name).ToLower().StartsWith(text2))
					{
						list.SelectedIndex = num3;
						break;
					}
				}
				_searchBuffer = text2;
			}
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
		if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
		{
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
		if (list.Name == "listInstalled" && list.SelectedItem is StardewMod stardewMod3)
		{
			bool flag = e.KeyCode == Keys.Right || e.KeyValue == 187 || e.KeyCode == Keys.Add;
			bool flag2 = e.KeyCode == Keys.Left || e.KeyValue == 189 || e.KeyCode == Keys.Subtract;
			if (stardewMod3.IsGroup)
			{
				string groupName = stardewMod3.GroupName;
				if (flag)
				{
					if (!_expandedGroups.Contains(groupName))
					{
						_expandedGroups.Add(groupName);
						RebuildInstalledListBox();
						Speak("Expanded.");
					}
				}
				else if (flag2 && _expandedGroups.Contains(groupName))
				{
					_expandedGroups.Remove(groupName);
					RebuildInstalledListBox();
					Speak("Collapsed.");
				}
			}
			else if (stardewMod3.IsSubMod && flag2)
			{
				string relativePath = Path.GetRelativePath(_settings.ModsPath, stardewMod3.FolderPath);
				int num4 = relativePath.IndexOf(Path.DirectorySeparatorChar);
				string text3 = ((num4 == -1) ? relativePath : relativePath.Substring(0, num4));
				if (_expandedGroups.Contains(text3))
				{
					_expandedGroups.Remove(text3);
					StardewMod tag = new StardewMod
					{
						UniqueId = "GROUP:" + text3
					};
					listInstalled.SelectedItem = null;
					listInstalled.Tag = tag;
					RebuildInstalledListBox();
					for (int k = 0; k < listInstalled.Items.Count; k++)
					{
						if ((listInstalled.Items[k] as StardewMod)?.UniqueId == "GROUP:" + text3)
						{
							listInstalled.SelectedIndex = k;
							break;
						}
					}
					Speak(text3 + " Collapsed.");
				}
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			if (e.KeyCode == Keys.Space)
			{
				ToggleModStatus();
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			if (e.KeyCode == Keys.Delete)
			{
				DeleteSelectedMod();
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			if (e.KeyCode == Keys.Apps)
			{
				MessageBox.Show($"Mod: {stardewMod3.Name}\nAuthor: {stardewMod3.Author}\nDescription: {stardewMod3.Description}", "Details");
				e.Handled = true;
			}
		}
		if (list.Name == "listUpdates" && list.SelectedItem is StardewMod stardewMod4 && e.KeyCode == Keys.Delete)
		{
			if (MessageBox.Show($"Ignore version {stardewMod4.LatestVersion} for {stardewMod4.Name}?", "Ignore Update", MessageBoxButtons.YesNo) == DialogResult.Yes)
			{
				_settings.IgnoredVersions[stardewMod4.UniqueId] = stardewMod4.LatestVersion;
				_settings.Save();
				listUpdates.Items.Remove(stardewMod4);
				Speak("Update ignored.");
			}
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
		if (list.Name == "listBackups" && list.SelectedItem is BackupItem backupItem)
		{
			if (e.KeyCode == Keys.Delete)
			{
				DeleteSelectedBackup();
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			if (e.KeyCode == Keys.Return)
			{
				if (MessageBox.Show("Restore backup " + backupItem.Name + "?", "Confirm Restore", MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					InstallFromZip(backupItem.FullPath);
				}
				e.Handled = true;
			}
		}
		if (list.Name == "listProfiles" && list.SelectedItem is ModProfile modProfile)
		{
			if (e.KeyCode == Keys.Return)
			{
				ApplyProfile(modProfile);
				e.Handled = true;
			}
			if (e.KeyCode == Keys.Delete)
			{
				if (MessageBox.Show("Delete profile '" + modProfile.Name + "'?", "Confirm Delete", MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					string path = Path.Combine(profilesPath, modProfile.Name + ".json");
					if (File.Exists(path))
					{
						File.Delete(path);
					}
					RefreshProfilesList();
					Speak("Profile deleted.");
				}
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
		}
		if (list.Name == "listLog" && e.KeyCode == Keys.Return && list.SelectedItem is LogEntry logEntry)
		{
			if (list.Items.Count < _fullLogEntries.Count)
			{
				txtSearchLog.Text = "";
				list.BeginUpdate();
				list.Items.Clear();
				foreach (LogEntry fullLogEntry in _fullLogEntries)
				{
					list.Items.Add(fullLogEntry);
				}
				list.SelectedItem = logEntry;
				list.EndUpdate();
				Speak("Returned to filtered view. Scroll down to read more.");
				e.Handled = true;
				return;
			}
			string text4 = logEntry.Text;
			string suggestedFix = LogAnalyzer.GetSuggestedFix(text4);
			string text5 = text4;
			if (!string.IsNullOrEmpty(suggestedFix))
			{
				text5 = text5 + "\n\nSUGGESTED FIX:\n" + suggestedFix;
			}
			MessageBox.Show(text5, "Log Detail");
			e.Handled = true;
		}
		if (list.Name == "listLog" && IsShortcut(e, "Login"))
		{
			await UploadSmapiLog();
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
		if (e.KeyCode == Keys.Return && (list.Name == "listUpdates" || list.Name == "listDiscovery"))
		{
			OpenModPage();
			e.Handled = true;
		}
	}

	private async Task CheckForUpdates(List<StardewMod> group)
	{
		await _apiSemaphore.WaitAsync();
		try
		{
			await Task.Delay(new Random().Next(100, 1000));
			HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/games/stardewvalley/mods/" + group[0].NexusID + ".json");
			httpRequestMessage.Headers.Add("apikey", _settings.ApiKey);
			httpRequestMessage.Headers.Add("User-Agent", $"StardewAccessibleManager/{_appVersion}");
			HttpResponseMessage httpResponseMessage = await _httpClient.SendAsync(httpRequestMessage);
			if (!httpResponseMessage.IsSuccessStatusCode)
			{
				return;
			}
			string text = ((string?)JObject.Parse(await httpResponseMessage.Content.ReadAsStringAsync())["version"]) ?? "0";
			if (_settings.IgnoredVersions.TryGetValue(group[0].UniqueId, out string value) && value == text)
			{
				return;
			}
			StardewMod rep = null;
			foreach (StardewMod item in group)
			{
				item.LatestVersion = text;
				if (IsNewerVersion(item.Version, text) && rep == null)
				{
					rep = item;
				}
			}
			if (rep == null)
			{
				return;
			}
			Invoke(delegate
			{
				listUpdates.BeginUpdate();
				if (!listUpdates.Items.Contains(rep))
				{
					rep.IsUpdateResult = true;
					listUpdates.Items.Add(rep);
				}
				listUpdates.EndUpdate();
			});
		}
		finally
		{
			_apiSemaphore.Release();
			if (Interlocked.Decrement(ref _activeChecks) <= 0)
			{
				_isLoading = false;
				PlayAppSound("load_complete");
				if (listUpdates.Items.Count > 0)
				{
					Speak($"Update check complete. Found {listUpdates.Items.Count} mod updates.");
				}
				else
				{
					Speak("Update check complete. All mods are up-to-date.");
				}
			}
		}
	}

	private async Task DownloadAndInstallUpdate(StardewMod mod, bool silent = false)
	{
		if (!isPremium)
		{
			if (!silent)
			{
				OpenModPage();
			}
			return;
		}
		try
		{
			HttpRequestMessage httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://api.nexusmods.com/v1/games/stardewvalley/mods/" + mod.NexusID + "/files.json");
			httpRequestMessage.Headers.Add("apikey", _settings.ApiKey);
			httpRequestMessage.Headers.Add("User-Agent", $"StardewAccessibleManager/{_appVersion}");
			JToken jToken = ((JArray)JObject.Parse(await (await _httpClient.SendAsync(httpRequestMessage)).Content.ReadAsStringAsync())["files"])[0];
			string value = jToken["file_id"].ToString();
			string realName = jToken["file_name"]?.ToString() ?? (mod.NexusID + "_update.zip");
			HttpRequestMessage httpRequestMessage2 = new HttpRequestMessage(HttpMethod.Get, $"https://api.nexusmods.com/v1/games/stardewvalley/mods/{mod.NexusID}/files/{value}/download_link.json");
			httpRequestMessage2.Headers.Add("apikey", _settings.ApiKey);
			httpRequestMessage2.Headers.Add("User-Agent", $"StardewAccessibleManager/{_appVersion}");
			string requestUri = JArray.Parse(await (await _httpClient.SendAsync(httpRequestMessage2)).Content.ReadAsStringAsync())[0]["URI"].ToString();
			byte[] bytes = await _httpClient.GetByteArrayAsync(requestUri);
			string text = Path.Combine(Path.GetTempPath(), realName);
			File.WriteAllBytes(text, bytes);
			string text2 = Directory.GetDirectories(_settings.ModsPath).FirstOrDefault((string d) => File.Exists(Path.Combine(d, "manifest.json")) && (string?)JObject.Parse(File.ReadAllText(Path.Combine(d, "manifest.json")))["UniqueID"] == mod.UniqueId);
			if (text2 != null)
			{
				BackupMod(text2, mod.Name);
				Directory.Delete(text2, recursive: true);
			}
			ZipFile.ExtractToDirectory(text, _settings.ModsPath, overwriteFiles: true);
			File.Delete(text);
			if (!silent)
			{
				PlayAppSound("connect");
				MessageBox.Show(mod.Name + " updated!");
				RefreshModList(checkUpdates: true);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("Update failed: " + ex.Message);
		}
	}

	private async void CheckForAppUpdates(bool manual)
	{
		if (manual)
		{
			Speak("Checking for manager updates...");
		}
		try
		{
			string user = "SeanTerry01";
			string repo = "Stardew-Accessible-Manager";
			_httpClient.DefaultRequestHeaders.UserAgent.Clear();
			_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"StardewAccessibleManager/{_appVersion}");
			string text = JObject.Parse(await _httpClient.GetStringAsync($"https://api.github.com/repos/{user}/{repo}/releases/latest"))["tag_name"]?.ToString() ?? $"v{_appVersion}";
			string target = (text.StartsWith("v") ? text.Substring(1) : text);
			string current = _appVersion;
			if (IsNewerVersion(current, target))
			{
				PlayAppSound("connect");
				Speak("A new version of the manager is available: " + text + ".");
				if (MessageBox.Show("Version " + text + " is available! Would you like to open the download page?", "Update Available", MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					Process.Start(new ProcessStartInfo($"https://github.com/{user}/{repo}/releases/latest")
					{
						UseShellExecute = true
					});
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

	private string? ParseNexusId(JToken? keys)
	{
		if (keys == null)
		{
			return null;
		}
		IEnumerable<JToken> enumerable;
		if (keys.Type == JTokenType.Array)
		{
			enumerable = keys.Children();
		}
		else
		{
			if (keys.Type != JTokenType.String)
			{
				return null;
			}
			enumerable = new List<JToken> { keys };
		}
		foreach (JToken item in enumerable)
		{
			string text = item.ToString();
			if (!text.Contains("Nexus:", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string[] array = text.Split(':');
			if (array.Length >= 2)
			{
				string text2 = array[1].Trim();
				if (text2.Contains("@"))
				{
					text2 = text2.Split('@')[0].Trim();
				}
				if (long.TryParse(text2, out var _))
				{
					return text2;
				}
			}
		}
		return null;
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
