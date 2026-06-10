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

/// <summary>Programmatic WinForms UI construction (tabs, lists, menus, handlers) for Form1.</summary>
public partial class Form1
{
	/// <summary>
	/// Builds the entire WinForms UI programmatically: tab control, list boxes, buttons, labels,
	/// and all event handlers. Called once from the constructor.
	/// </summary>
	private void SetupAccessibleUI()
	{
		string gameName = _settings.ActiveGame switch
		{
			"SkyrimSE" => "Skyrim Special Edition",
			"Fallout4" => "Fallout 4",
			"StardewValley" => "Stardew Valley",
			_ => ""
		};
		Text = string.IsNullOrEmpty(gameName) ? "Kinetix Mod Manager" : $"{gameName} Kinetix Mod Manager";
		base.Size = new Size(1000, 700);
		base.KeyPreview = true;
		base.KeyDown += Form1_KeyDown;
		MenuStrip menuStrip = new MenuStrip();
		// Exiting the Alt menu (e.g. Alt then Escape) restores focus to the underlying list without
		// raising GotFocus, because the menu uses a special input mode that never takes the list's
		// focus. Re-announce the focused list when the menu deactivates so the user still hears their
		// position. BeginInvoke defers until focus has actually been restored.
		menuStrip.MenuDeactivate += (s, e) => BeginInvoke(new Action(AnnounceFocusedList));
		_menuFile = new ToolStripMenuItem("&File");
		_menuFile.DropDownItems.Add("Refresh All (" + GetShortcutString("RefreshAll") + ")", null, delegate
		{
			RefreshAllData(checkUpdates: true);
		});
		_menuFile.DropDownItems.Add("Refresh Installed Only (" + GetShortcutString("RefreshInstalled") + ")", null, delegate
		{
			_ = RefreshModList(checkUpdates: false);
		});
		_menuFile.DropDownItems.Add("Check for Manager Updates", null, async delegate
		{
			await CheckForAppUpdates(manual: true);
		});
		_menuFile.DropDownItems.Add("Settings (" + GetShortcutString("Settings") + ")", null, delegate
		{
			ShowSettings();
		});
		_menuFile.DropDownItems.Add(new ToolStripSeparator());

		_menuCloseSessionItem = new ToolStripMenuItem("Close Current Game Session", null, delegate { CloseGameSession(); })
		{
			ShortcutKeys = Keys.Control | Keys.Shift | Keys.C,
			Visible = (_settings.ActiveGame != "None")
		};
		_menuFile.DropDownItems.Add(_menuCloseSessionItem);

		_menuCloseSeparator = new ToolStripSeparator
		{
			Visible = (_settings.ActiveGame != "None")
		};
		_menuFile.DropDownItems.Add(_menuCloseSeparator);

		_menuFile.DropDownItems.Add("Exit", null, delegate
		{
			Application.Exit();
		});

		_menuGames = new ToolStripMenuItem("&Games");
		UpdateGamesMenu();

		ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem("&Mods");
		toolStripMenuItem2.DropDownItems.Add("Save Current Setup as Profile (" + GetShortcutString("SaveProfile") + ")", null, delegate
		{
			CreateProfileFromCurrent();
		});
		toolStripMenuItem2.DropDownItems.Add("Install from Zip (" + GetShortcutString("InstallZip") + ")", null, delegate
		{
			ManualInstall();
		});
		toolStripMenuItem2.DropDownItems.Add("Update All Available (" + GetShortcutString("UpdateAll") + ")", null, async delegate
		{
			await UpdateAllMods();
		});
		toolStripMenuItem2.DropDownItems.Add($"Launch {gameName} (" + GetShortcutString("LaunchGame") + ")", null, delegate
		{
			LaunchGame();
		});
		toolStripMenuItem2.DropDownItems.Add($"Install {gameName} Accessibility Suite", null, delegate
		{
			ShowAccessibilitySuiteDialog();
		});
		toolStripMenuItem2.DropDownItems.Add("Auto-match Nexus IDs for All Mods", null, async delegate
		{
			await AutoMatchNexusIDs();
		});
		toolStripMenuItem2.DropDownItems.Add("Edit Selected Mod's Config File (" + GetShortcutString("OpenConfig") + ")", null, delegate
		{
			OpenSelectedModConfig();
		});
		toolStripMenuItem2.DropDownItems.Add("Edit Selected Mod's Manifest File (" + GetShortcutString("OpenManifest") + ")", null, delegate
		{
			OpenSelectedModManifest();
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
		toolStripMenuItem4.DropDownItems.Add("Accessibility Controls (" + GetShortcutString("ControlsHelp") + ")", null, delegate
		{
			ShowAccessibilityControls();
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
		menuStrip.Items.Add(_menuFile);
		menuStrip.Items.Add(_menuGames);
		menuStrip.Items.Add(toolStripMenuItem2);
		menuStrip.Items.Add(toolStripMenuItem3);
		menuStrip.Items.Add(toolStripMenuItem4);
		base.MainMenuStrip = menuStrip;
		tableLayoutPanel = new TableLayoutPanel
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
		items2 = new string[4] { "Errors and Warnings", "Errors Only", "Full Log", "Links Only" };
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
			Font = new Font("Segoe UI", 12f),
			// Allow standard multi-selection (Shift/Ctrl + arrows) so lines can be selected and copied
			// to the clipboard with Ctrl+C, without having to open SMAPI-latest.txt by hand.
			SelectionMode = SelectionMode.MultiExtended
		};
		listLog.AccessibleName = "Parsed SMAPI Log Entries";
		tableLayoutPanel5.Controls.Add(flowLayoutPanel3, 0, 0);
		tableLayoutPanel5.Controls.Add(listLog, 0, 1);
		tabSmapiLog.Controls.Add(tableLayoutPanel5);

		string initialWikiTitle = _settings.ActiveGame switch
		{
			"SkyrimSE" => "Skyrim Wiki",
			"Fallout4" => "Fallout 4 Wiki",
			_ => "Stardew Wiki"
		};
		tabWiki = new TabPage(initialWikiTitle);
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
		string searchLabel = _settings.ActiveGame switch
		{
			"SkyrimSE" => "Search Skyrim Wiki",
			"Fallout4" => "Search Fallout 4 Wiki",
			_ => "Search Stardew Wiki"
		};
		txtWikiSearch = new TextBox
		{
			Width = 200,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = searchLabel
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
		RefreshWikiCategories();
		cmbWikiCategories.SelectedIndexChanged += async delegate
		{
			if (cmbWikiCategories.SelectedIndex > 0)
			{
				splitWiki.Visible = true;
				await LoadWikiCategory(cmbWikiCategories.SelectedItem?.ToString() ?? "");
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
			Visible = false,
			// Keep the splitter out of the keyboard tab order; otherwise Tab from the
			// results list lands on the bare splitter (announced as a generic "pane")
			// before reaching the WebView.
			TabStop = false
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
				else _ = LoadWikiPage(res.Title);
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
		_ = InitializeAndLoadInitialPagesAsync();
		
		splitWiki.Panel1.Controls.Add(listWikiResults);
		splitWiki.Panel2.Controls.Add(webViewWiki);
		tableLayoutPanelWiki.Controls.Add(flowLayoutPanelWikiTop, 0, 0);
		tableLayoutPanelWiki.Controls.Add(splitWiki, 0, 1);
		tabWiki.Controls.Add(tableLayoutPanelWiki);

		// Walkthroughs Tab Setup
		string initialWalkthroughTitle = _settings.ActiveGame switch
		{
			"SkyrimSE" => "Skyrim Walkthroughs",
			"Fallout4" => "Fallout 4 Walkthroughs",
			_ => "Stardew Walkthroughs"
		};
		tabWalkthroughs = new TabPage(initialWalkthroughTitle);
		splitWalkthroughs = new SplitContainer
		{
			Dock = DockStyle.Fill,
			Orientation = Orientation.Vertical,
			SplitterDistance = 300,
			// See splitWiki above: keep the splitter out of the tab order so Tab moves
			// straight from the guides list to the WebView.
			TabStop = false
		};
		listWalkthroughs = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listWalkthroughs",
			Font = new Font("Segoe UI", 12f),
			AccessibleName = "Walkthrough Guides List"
		};
		webViewWalkthrough = new WebView2
		{
			Dock = DockStyle.Fill,
			AccessibleName = "Walkthrough Content View"
		};
		splitWalkthroughs.Panel1.Controls.Add(listWalkthroughs);
		splitWalkthroughs.Panel2.Controls.Add(webViewWalkthrough);
		tabWalkthroughs.Controls.Add(splitWalkthroughs);

		mainTabs.TabPages.Add(tabInstalled);
		mainTabs.TabPages.Add(tabUpdates);
		mainTabs.TabPages.Add(tabBackups);
		mainTabs.TabPages.Add(tabDiscovery);
		mainTabs.TabPages.Add(tabWiki);
		mainTabs.TabPages.Add(tabWalkthroughs);
		mainTabs.TabPages.Add(tabProfiles);
		if (_settings.ActiveGame == "StardewValley")
		{
			mainTabs.TabPages.Add(tabSmapiLog);
		}

		tableLayoutPanel.Controls.Add(mainTabs, 0, 0);

		InitializeGameSelectionPanel();
		base.Controls.Add(menuStrip);

		bool noGame = _settings.ActiveGame == "None";
		if (noGame)
		{
			if (!base.Controls.Contains(_gameSelectionPanel))
			{
				base.Controls.Add(_gameSelectionPanel);
			}
			_gameSelectionPanel.Visible = true;
			_gameSelectionPanel.Enabled = true;
		}
		else
		{
			if (!base.Controls.Contains(tableLayoutPanel))
			{
				base.Controls.Add(tableLayoutPanel);
			}
			tableLayoutPanel.Visible = true;
		}

		UpdateGamesMenu();
		UpdateMenuState();
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
		// GotFocus (not Enter) so the position is re-announced whenever focus lands on the list,
		// including when it is restored after a modal dialog or menu closes — Enter does not fire
		// in that case because the form's ActiveControl never changed while the dialog was open.
		listInstalled.GotFocus += List_Enter;
		listUpdates.GotFocus += List_Enter;
		listDiscovery.GotFocus += List_Enter;
		listBackups.GotFocus += List_Enter;
		listProfiles.GotFocus += List_Enter;
		listLog.GotFocus += List_Enter;
		_searchTimer.Tick += delegate
		{
			_searchTimer.Stop();
			_searchBuffer = "";
		};
		listWikiResults.SelectedIndexChanged += Wiki_SelectedIndexChanged;
		listWikiResults.GotFocus += List_Enter;
		listWikiResults.DoubleClick += async delegate(object? s, EventArgs e)
		{
			if (listWikiResults.SelectedItem is WikiResult res)
			{
				if (res.IsCategory) await LoadWikiCategory(res.Title);
				else _ = LoadWikiPage(res.Title);
			}
		};

		listWalkthroughs.SelectedIndexChanged += List_SelectedIndexChanged;
		listWalkthroughs.SelectedIndexChanged += async delegate
		{
			if (listWalkthroughs.SelectedItem is WalkthroughGuide guide)
			{
				await EnsureWebViewsInitializedAsync();
				webViewWalkthrough.CoreWebView2?.Navigate(guide.Url);
			}
		};
		listWalkthroughs.GotFocus += List_Enter;
		PopulateWalkthroughs();
	}
}
