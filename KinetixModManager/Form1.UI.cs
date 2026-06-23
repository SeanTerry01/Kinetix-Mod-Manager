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
		// Startup title: the active game with its detected Skyrim/Fallout edition + build (see GameDisplayName).
		string gameName = _settings.ActiveGame == "None" ? "" : GameDisplayName();
		Text = string.IsNullOrEmpty(gameName) ? Loc.T("ui.appTitle") : Loc.T("ui.appTitleGame", gameName);
		base.Size = new Size(1000, 700);
		base.KeyPreview = true;
		base.KeyDown += Form1_KeyDown;
		MenuStrip menuStrip = new MenuStrip();
		// Exiting the Alt menu (e.g. Alt then Escape) restores focus to the underlying list without
		// raising GotFocus, because the menu uses a special input mode that never takes the list's
		// focus. Re-announce the focused list when the menu deactivates so the user still hears their
		// position. BeginInvoke defers until focus has actually been restored.
		menuStrip.MenuDeactivate += (s, e) => BeginInvoke(new Action(AnnounceFocusedList));
		_menuFile = new ToolStripMenuItem(Loc.T("menu.file")) { Name = "menuFile" };
		_menuFile.DropDownItems.Add(Loc.T("menu.refreshAll", GetShortcutString("RefreshAll")), null, delegate
		{
			RefreshAllData(checkUpdates: true);
		}).Name = "menuRefreshAll";
		_menuFile.DropDownItems.Add(Loc.T("menu.refreshInstalled", GetShortcutString("RefreshInstalled")), null, delegate
		{
			_ = RefreshModList(checkUpdates: false);
		}).Name = "menuRefreshInstalled";
		_menuFile.DropDownItems.Add(Loc.T("menu.checkManagerUpdates"), null, async delegate
		{
			await CheckForAppUpdates(manual: true);
		});
		_menuFile.DropDownItems.Add(Loc.T("menu.settings", GetShortcutString("Settings")), null, delegate
		{
			ShowSettings();
		});
		_menuFile.DropDownItems.Add(new ToolStripSeparator());

		_menuCloseSessionItem = new ToolStripMenuItem(Loc.T("menu.closeSession"), null, delegate { CloseGameSession(); })
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

		_menuFile.DropDownItems.Add(Loc.T("menu.exit"), null, delegate
		{
			Application.Exit();
		});

		_menuGames = new ToolStripMenuItem(Loc.T("menu.games"));
		UpdateGamesMenu();

		ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem(Loc.T("menu.mods")) { Name = "menuMods" };
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.saveProfile", GetShortcutString("SaveProfile")), null, delegate
		{
			CreateProfileFromCurrent();
		});
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.installZip", GetShortcutString("InstallZip")), null, delegate
		{
			ManualInstall();
		});
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.updateAll", GetShortcutString("UpdateAll")), null, async delegate
		{
			await UpdateAllMods();
		});
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.launch", gameName, GetShortcutString("LaunchGame")), null, delegate
		{
			LaunchGame();
		}).Name = "menuLaunch";
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.installSuite", gameName), null, delegate
		{
			ShowAccessibilitySuiteDialog();
		}).Name = "menuSuite";
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.uninstallScriptExtender"), null, delegate
		{
			UninstallScriptExtenderCommand();
		}).Name = "menuUninstallSE";
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.autoMatch"), null, async delegate
		{
			await AutoMatchNexusIDs();
		});
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.autoSort", GetShortcutString("AutoSort")), null, async delegate
		{
			await AutoSortPluginsAsync();
		}).Name = "menuAutoSort";
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.rebuildDeploy"), null, delegate
		{
			RebuildDeployment();
		}).Name = "menuRebuildDeploy";
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.exportLoadOrder"), null, delegate
		{
			ExportLoadOrder();
		}).Name = "menuExportLoadOrder";
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.importLoadOrder"), null, delegate
		{
			ImportLoadOrder();
		}).Name = "menuImportLoadOrder";
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.importMO2"), null, delegate
		{
			ImportFromMO2();
		}).Name = "menuImportMO2";
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.editConfig", GetShortcutString("OpenConfig")), null, delegate
		{
			OpenSelectedModConfig();
		});
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.editManifest", GetShortcutString("OpenManifest")), null, delegate
		{
			OpenSelectedModManifest();
		});
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.fileConflicts", GetShortcutString("FileConflicts")), null, delegate
		{
			ShowFileConflictsReport();
		});
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.checkRequirements", GetShortcutString("CheckRequirements")), null, async delegate
		{
			await ShowRequirementsReport();
		});
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.verifyDeployment"), null, delegate
		{
			VerifyModDeployment();
		});
		toolStripMenuItem2.DropDownItems.Add(Loc.T("menu.resetIgnoredReqs"), null, delegate
		{
			ResetIgnoredRequirements();
		});

		ToolStripMenuItem toolStripMenuItem3 = new ToolStripMenuItem(Loc.T("menu.view")) { Name = "menuView" };
		toolStripMenuItem3.DropDownItems.Add(Loc.T("menu.openDownloads", GetShortcutString("OpenDownloads")), null, delegate
		{
			Process.Start("explorer.exe", downloadsPath);
		});
		toolStripMenuItem3.DropDownItems.Add(Loc.T("menu.openBackups", GetShortcutString("OpenBackups")), null, delegate
		{
			Process.Start("explorer.exe", backupsPath);
		});
		toolStripMenuItem3.DropDownItems.Add(Loc.T("menu.openSmapiLog", GetShortcutString("OpenLogFile")), null, delegate
		{
			OpenGameLog();
		}).Name = "menuOpenLog";
		toolStripMenuItem3.DropDownItems.Add(Loc.T("menu.openErrorLog", GetShortcutString("OpenErrorLog")), null, delegate
		{
			if (File.Exists(errorLogPath))
			{
				Process.Start(new ProcessStartInfo("notepad.exe", errorLogPath)
				{
					UseShellExecute = true
				});
			}
		});
		ToolStripMenuItem toolStripMenuItem4 = new ToolStripMenuItem(Loc.T("menu.help"));
		toolStripMenuItem4.DropDownItems.Add(Loc.T("menu.userManual", GetShortcutString("Manual")), null, delegate
		{
			ShowManual();
		});
		toolStripMenuItem4.DropDownItems.Add(Loc.T("menu.changeLog", GetShortcutString("ChangeLog")), null, delegate
		{
			ShowChangeLog();
		});
		toolStripMenuItem4.DropDownItems.Add(Loc.T("menu.modDocs", GetShortcutString("ModDocs")), null, delegate
		{
			ShowModDocs();
		});
		toolStripMenuItem4.DropDownItems.Add(Loc.T("menu.accessibilityControls", GetShortcutString("ControlsHelp")), null, delegate
		{
			ShowAccessibilityControls();
		});
		toolStripMenuItem4.DropDownItems.Add(Loc.T("menu.soundDemo"), null, delegate
		{
			ShowSoundDemo();
		});
		toolStripMenuItem4.DropDownItems.Add(Loc.T("menu.themeManager"), null, delegate
		{
			ShowThemeManager();
		});
		toolStripMenuItem4.DropDownItems.Add(Loc.T("menu.shortcutCustomization"), null, delegate
		{
			ShowShortcutManager();
		});
		toolStripMenuItem4.DropDownItems.Add(Loc.T("about.title"), null, delegate
		{
			ShowAbout();
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
		tabInstalled = new TabPage(Loc.T("tab.installed"));
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
			AccessibleName = Loc.T("ui.searchInstalled")
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
			Text = Loc.T("ui.searchLabel"),
			AutoSize = true,
			Padding = new Padding(5, 8, 0, 0)
		});
		flowLayoutPanel.Controls.Add(txtSearchInstalled);
		flowLayoutPanel.Controls.Add(new Label
		{
			Text = Loc.T("ui.categoryLabel"),
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
		listInstalled.AccessibleName = Loc.T("ui.installedList");
		tableLayoutPanel2.Controls.Add(flowLayoutPanel, 0, 0);
		tableLayoutPanel2.Controls.Add(listInstalled, 0, 1);
		tabInstalled.Controls.Add(tableLayoutPanel2);
		tabUpdates = new TabPage(Loc.T("tab.updates"));
		listUpdates = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listUpdates",
			Font = new Font("Segoe UI", 12f)
		};
		listUpdates.AccessibleName = Loc.T("ui.updatesList");
		tabUpdates.Controls.Add(listUpdates);
		tabBackups = new TabPage(Loc.T("tab.backups"));
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
			Text = Loc.T("ui.pruneBackups", GetShortcutString("DeleteOldBackups")),
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
		listBackups.AccessibleName = Loc.T("ui.backupsList");
		tableLayoutPanel3.Controls.Add(listBackups, 0, 1);
		tabBackups.Controls.Add(tableLayoutPanel3);
		tabDiscovery = new TabPage(Loc.T("tab.discovery"));
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
			AccessibleName = Loc.T("ui.searchModName")
		};
		cmbDiscoveryType = new ComboBox
		{
			Width = 150,
			Font = new Font("Segoe UI", 12f),
			DropDownStyle = ComboBoxStyle.DropDownList,
			AccessibleName = Loc.T("ui.searchTypeName")
		};
		ComboBox.ObjectCollection items = cmbDiscoveryType.Items;
		object[] items2 = new string[4] { "Search", "Trending", "Most Popular", "Recent" };
		items.AddRange(items2);
		cmbDiscoveryType.SelectedIndex = 0;
		cmbDiscoveryLanguage = new ComboBox
		{
			Width = 170,
			Font = new Font("Segoe UI", 12f),
			DropDownStyle = ComboBoxStyle.DropDownList,
			AccessibleName = Loc.T("ui.filterLanguage")
		};
		// Starter options so the control is usable before the dynamic, game-specific list loads.
		_suppressDiscoveryLanguageEvent = true;
		cmbDiscoveryLanguage.Items.Add(new LanguageOption { Name = "" });        // Any language
		cmbDiscoveryLanguage.Items.Add(new LanguageOption { Name = "English" });
		cmbDiscoveryLanguage.SelectedIndex =
			string.IsNullOrEmpty(_settings.DiscoveryLanguage) ? 0 : 1;
		_suppressDiscoveryLanguageEvent = false;
		cmbDiscoveryLanguage.SelectedIndexChanged += delegate
		{
			if (_suppressDiscoveryLanguageEvent) return;
			if (cmbDiscoveryLanguage.SelectedItem is LanguageOption opt)
			{
				_settings.DiscoveryLanguage = opt.Name;
				_settings.Save();
			}
		};
		cmbDiscoveryPageSize = new ComboBox
		{
			Width = 70,
			Font = new Font("Segoe UI", 12f),
			DropDownStyle = ComboBoxStyle.DropDownList,
			AccessibleName = Loc.T("ui.resultsPerLoad")
		};
		cmbDiscoveryPageSize.Items.AddRange(DiscoveryPageSizeOptions.Cast<object>().ToArray());
		// Seed from the saved default; changes made here are session-only and never written back.
		cmbDiscoveryPageSize.SelectedItem = _settings.DiscoverySearchPageSize;
		if (cmbDiscoveryPageSize.SelectedIndex < 0) cmbDiscoveryPageSize.SelectedItem = 20;
		btnSearch = new Button
		{
			Text = Loc.T("ui.go"),
			Height = 30,
			Width = 60
		};
		btnSearch.Click += async delegate
		{
			await RunDiscovery();
		};
		Button btnHistory = new Button
		{
			Text = Loc.T("ui.historyBtn"),
			Height = 30,
			Width = 90,
			AccessibleName = Loc.T("ui.historyName")
		};
		btnHistory.Click += delegate { ShowSearchHistoryDialog(); };
		txtSearch.KeyDown += async delegate(object? s, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				await RunDiscovery();
			}
		};
		flowLayoutPanel2.Controls.Add(new Label
		{
			Text = Loc.T("ui.searchTypeLabel"),
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		});
		flowLayoutPanel2.Controls.Add(txtSearch);
		// Search history sits right after the search box and before the search-type selector, per user preference.
		flowLayoutPanel2.Controls.Add(btnHistory);
		flowLayoutPanel2.Controls.Add(cmbDiscoveryType);
		flowLayoutPanel2.Controls.Add(new Label { Text = Loc.T("ui.languageLabel"), AutoSize = true, Padding = new Padding(10, 5, 0, 0) });
		flowLayoutPanel2.Controls.Add(cmbDiscoveryLanguage);
		flowLayoutPanel2.Controls.Add(new Label { Text = Loc.T("ui.resultsPerLoadLabel"), AutoSize = true, Padding = new Padding(10, 5, 0, 0) });
		flowLayoutPanel2.Controls.Add(cmbDiscoveryPageSize);
		flowLayoutPanel2.Controls.Add(btnSearch);
		listDiscovery = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listDiscovery",
			Font = new Font("Segoe UI", 12f)
		};
		listDiscovery.AccessibleName = Loc.T("ui.discoveryList");

		// Two rows: the search bar and the results list. "Load more" is an inline row at the bottom
		// of the list itself (see DiscoveryLoadMoreRow), so there is no separate button row.
		tableLayoutPanel4.RowCount = 2;
		tableLayoutPanel4.RowStyles.Clear();
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f)); // Search bar
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // List

		tableLayoutPanel4.Controls.Add(flowLayoutPanel2, 0, 0);
		tableLayoutPanel4.Controls.Add(listDiscovery, 0, 1);

		tabDiscovery.Controls.Add(tableLayoutPanel4);
		tabProfiles = new TabPage(Loc.T("tab.profiles"));
		listProfiles = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listProfiles",
			Font = new Font("Segoe UI", 12f)
		};
		listProfiles.AccessibleName = Loc.T("ui.profilesList");
		tabProfiles.Controls.Add(listProfiles);
		tabModPriority = new TabPage(Loc.T("tab.modPriority"));
		listModPriority = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listModPriority",
			Font = new Font("Segoe UI", 12f),
			AccessibleName = Loc.T("ui.modPriorityList"),
			AccessibleDescription = Loc.T("ui.modPriorityDesc")
		};
		tabModPriority.Controls.Add(listModPriority);
		tabPluginOrder = new TabPage(Loc.T("tab.pluginOrder"));
		listPluginOrder = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listPluginOrder",
			Font = new Font("Segoe UI", 12f),
			AccessibleName = Loc.T("ui.pluginOrderList"),
			AccessibleDescription = Loc.T("ui.pluginOrderDesc")
		};
		tabPluginOrder.Controls.Add(listPluginOrder);
		tabCreations = new TabPage(Loc.T("tab.creations"));
		listCreations = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listCreations",
			Font = new Font("Segoe UI", 12f),
			AccessibleName = Loc.T("ui.creationsList"),
			AccessibleDescription = Loc.T("ui.creationsDesc")
		};
		tabCreations.Controls.Add(listCreations);
		tabSmapiLog = new TabPage(Loc.T("tab.smapiLog"));
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
		cmbLogFilter.SelectedIndexChanged += async delegate
		{
			RefreshSmapiLog();
			// Announce how many lines the chosen filter shows, as the Bethesda Log tab does. The screen
			// reader reads the filter name itself, so only the count is spoken — after a short delay so it
			// comes after the name, not before.
			if (cmbLogFilter.Focused)
			{
				await Task.Delay(100);
				if (cmbLogFilter.Focused) Speak(Loc.T("gamelog.foundResults", listLog.Items.Count));
			}
		};
		// Tabbing onto the filter does not raise SelectedIndexChanged, so announce the count on focus too.
		cmbLogFilter.GotFocus += async delegate
		{
			await Task.Delay(100);
			if (cmbLogFilter.Focused) Speak(Loc.T("gamelog.foundResults", listLog.Items.Count));
		};
		txtSearchLog = new TextBox
		{
			Width = 180,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = Loc.T("ui.searchLog")
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
			Text = Loc.T("ui.filterLabel"),
			AutoSize = true,
			Padding = new Padding(5, 8, 0, 0)
		});
		flowLayoutPanel3.Controls.Add(cmbLogFilter);
		flowLayoutPanel3.Controls.Add(new Label
		{
			Text = Loc.T("ui.searchLabel"),
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
		listLog.AccessibleName = Loc.T("ui.logList");
		tableLayoutPanel5.Controls.Add(flowLayoutPanel3, 0, 0);
		tableLayoutPanel5.Controls.Add(listLog, 0, 1);
		tabSmapiLog.Controls.Add(tableLayoutPanel5);

		// Game Log tab (Skyrim SE / Fallout 4): a picker over every .log in the script-extender folder,
		// plus a keyword filter and search. Mirrors the SMAPI log tab's layout and keyboard conventions.
		tabGameLog = new TabPage(Loc.T("tab.gameLog"));
		TableLayoutPanel tableLayoutGameLog = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 2,
			ColumnCount = 1
		};
		tableLayoutGameLog.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		tableLayoutGameLog.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		FlowLayoutPanel flowGameLog = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight
		};
		cmbGameLog = new ComboBox
		{
			Width = 200,
			Font = new Font("Segoe UI", 12f),
			DropDownStyle = ComboBoxStyle.DropDownList,
			AccessibleName = Loc.T("ui.gameLogPicker")
		};
		cmbGameLog.SelectedIndexChanged += async delegate
		{
			LoadSelectedGameLog();
			// Announce only the line count (the screen reader already reads the file name), after a short
			// delay so the name is spoken first and the count comes after it, not before.
			if (cmbGameLog.Focused)
			{
				await Task.Delay(100);
				if (cmbGameLog.Focused) Speak(Loc.T("gamelog.foundResults", listGameLog.Items.Count));
			}
		};
		// Tabbing onto the combo does not raise SelectedIndexChanged, so announce the current log's line
		// count on focus too, after the screen reader has read the selected file name.
		cmbGameLog.GotFocus += async delegate
		{
			await Task.Delay(100);
			if (cmbGameLog.Focused) Speak(Loc.T("gamelog.foundResults", listGameLog.Items.Count));
		};
		cmbGameLogFilter = new ComboBox
		{
			Width = 180,
			Font = new Font("Segoe UI", 12f),
			DropDownStyle = ComboBoxStyle.DropDownList,
			AccessibleName = Loc.T("ui.gameLogFilterName")
		};
		cmbGameLogFilter.Items.AddRange(new object[] { Loc.T("gamelog.filterAll"), Loc.T("gamelog.filterErrors") });
		cmbGameLogFilter.SelectedIndex = 0;
		cmbGameLogFilter.SelectedIndexChanged += async delegate
		{
			ApplyGameLogView();
			if (cmbGameLogFilter.Focused)
			{
				await Task.Delay(100);
				if (cmbGameLogFilter.Focused) Speak(Loc.T("gamelog.foundResults", listGameLog.Items.Count));
			}
		};
		cmbGameLogFilter.GotFocus += async delegate
		{
			await Task.Delay(100);
			if (cmbGameLogFilter.Focused) Speak(Loc.T("gamelog.foundResults", listGameLog.Items.Count));
		};
		txtSearchGameLog = new TextBox
		{
			Width = 180,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = Loc.T("ui.searchGameLog")
		};
		txtSearchGameLog.KeyDown += delegate(object? s, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				e.SuppressKeyPress = true;
				SearchGameLog();
			}
		};
		flowGameLog.Controls.Add(new Label { Text = Loc.T("ui.logFileLabel"), AutoSize = true, Padding = new Padding(5, 8, 0, 0) });
		flowGameLog.Controls.Add(cmbGameLog);
		flowGameLog.Controls.Add(new Label { Text = Loc.T("ui.filterLabel"), AutoSize = true, Padding = new Padding(10, 8, 0, 0) });
		flowGameLog.Controls.Add(cmbGameLogFilter);
		flowGameLog.Controls.Add(new Label { Text = Loc.T("ui.searchLabel"), AutoSize = true, Padding = new Padding(10, 8, 0, 0) });
		flowGameLog.Controls.Add(txtSearchGameLog);
		listGameLog = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listGameLog",
			Font = new Font("Segoe UI", 12f),
			SelectionMode = SelectionMode.MultiExtended,
			AccessibleName = Loc.T("ui.gameLogList")
		};
		tableLayoutGameLog.Controls.Add(flowGameLog, 0, 0);
		tableLayoutGameLog.Controls.Add(listGameLog, 0, 1);
		tabGameLog.Controls.Add(tableLayoutGameLog);

		string initialWikiTitle = _settings.ActiveGame switch
		{
			"SkyrimSE" => Loc.T("tab.wikiSkyrim"),
			"Fallout4" => Loc.T("tab.wikiFallout"),
			_ => Loc.T("tab.wikiStardew")
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
			"SkyrimSE" => Loc.T("ui.searchWikiSkyrim"),
			"Fallout4" => Loc.T("ui.searchWikiFallout"),
			_ => Loc.T("ui.searchWikiStardew")
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
			AccessibleName = Loc.T("ui.wikiCategories")
		};
		RefreshWikiCategories();
		cmbWikiCategories.SelectedIndexChanged += async delegate
		{
			// The category combo can change (e.g. while its items are being rebuilt) before splitWiki exists
			// during the initial UI build; guard against that to avoid a null dereference.
			if (splitWiki == null) return;
			// Index 0 is the "Select Category"/"No categories" placeholder, never a real category.
			if (cmbWikiCategories.SelectedIndex > 0 && cmbWikiCategories.SelectedItem is string cat && cat != "Select Category")
			{
				splitWiki.Visible = true;
				await LoadWikiCategory(cat);
			}
			else
			{
				splitWiki.Visible = false;
				listWikiResults.Items.Clear();
				wikiNavStack.Clear();
			}
		};
		cmbModWikis = new ComboBox
		{
			Width = 240,
			Font = new Font("Segoe UI", 12f),
			DropDownStyle = ComboBoxStyle.DropDownList,
			AccessibleName = Loc.T("ui.modWikis")
		};
		PopulateModWikis();
		cmbModWikis.SelectedIndexChanged += async delegate
		{
			if (_suppressModWikiEvent) return;
			if (cmbModWikis.SelectedItem is ModWikiLink link)
			{
				await OnModWikiSelected(link);
			}
		};
		flowLayoutPanelWikiTop.Controls.Add(new Label { Text = Loc.T("ui.searchLabel"), AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
		flowLayoutPanelWikiTop.Controls.Add(txtWikiSearch);
		flowLayoutPanelWikiTop.Controls.Add(new Label { Text = Loc.T("ui.modWikisLabel"), AutoSize = true, Padding = new Padding(10, 5, 0, 0) });
		flowLayoutPanelWikiTop.Controls.Add(cmbModWikis);
		flowLayoutPanelWikiTop.Controls.Add(new Label { Text = Loc.T("ui.categoriesLabel"), AutoSize = true, Padding = new Padding(10, 5, 0, 0) });
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
			AccessibleName = Loc.T("ui.wikiResults")
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
			AccessibleName = Loc.T("ui.wikiContent")
		};
		_ = InitializeAndLoadInitialPagesAsync();
		
		splitWiki.Panel1.Controls.Add(listWikiResults);
		splitWiki.Panel2.Controls.Add(webViewWiki);
		tableLayoutPanelWiki.Controls.Add(flowLayoutPanelWikiTop, 0, 0);
		tableLayoutPanelWiki.Controls.Add(splitWiki, 0, 1);
		tabWiki.Controls.Add(tableLayoutPanelWiki);

		// Now that splitWiki exists, load the default (main game) wiki's live categories. PopulateModWikis above
		// deliberately doesn't do this, since it runs before splitWiki is created.
		_ = RefreshCategoriesForActiveWikiAsync();

		// Walkthroughs Tab Setup
		string initialWalkthroughTitle = _settings.ActiveGame switch
		{
			"SkyrimSE" => Loc.T("tab.walkSkyrim"),
			"Fallout4" => Loc.T("tab.walkFallout"),
			_ => Loc.T("tab.walkStardew")
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
			AccessibleName = Loc.T("ui.walkthroughList")
		};
		webViewWalkthrough = new WebView2
		{
			Dock = DockStyle.Fill,
			AccessibleName = Loc.T("ui.walkthroughContent")
		};
		splitWalkthroughs.Panel1.Controls.Add(listWalkthroughs);
		splitWalkthroughs.Panel2.Controls.Add(webViewWalkthrough);
		tabWalkthroughs.Controls.Add(splitWalkthroughs);

		mainTabs.TabPages.Add(tabInstalled);
		// Mod Priority, Plugin Order, and Creations sit right after Installed (Skyrim/Fallout 4 only) so the load
		// order controls are next to the mod list. This must match the order SwitchActiveGame inserts them in,
		// otherwise the Creations tab would be missing when the app starts straight into a Bethesda session
		// (startup calls RefreshAllData, not SwitchActiveGame).
		if (IsBethesdaGame)
		{
			mainTabs.TabPages.Add(tabModPriority);
			mainTabs.TabPages.Add(tabPluginOrder);
			mainTabs.TabPages.Add(tabCreations);
		}
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
		else if (IsBethesdaGame)
		{
			mainTabs.TabPages.Add(tabGameLog);
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
		// Re-announce an empty list when the manager is brought back to the foreground (Alt+Tab), where the
		// child control regains focus without reliably re-firing GotFocus.
		Activated += Form1_Activated;
		listInstalled.GotFocus += List_Enter;
		listUpdates.GotFocus += List_Enter;
		listDiscovery.GotFocus += List_Enter;
		listBackups.GotFocus += List_Enter;
		listProfiles.GotFocus += List_Enter;
		listLog.GotFocus += List_Enter;
		listModPriority.KeyDown += ListModPriority_KeyDown;
		listModPriority.SelectedIndexChanged += ListModPriority_SelectedIndexChanged;
		listModPriority.GotFocus += List_Enter;
		listPluginOrder.KeyDown += ListPluginOrder_KeyDown;
		listPluginOrder.SelectedIndexChanged += ListModPriority_SelectedIndexChanged;
		listPluginOrder.GotFocus += List_Enter;
		listCreations.KeyDown += ListCreations_KeyDown;
		listCreations.SelectedIndexChanged += ListModPriority_SelectedIndexChanged;
		listCreations.GotFocus += List_Enter;
		listGameLog.KeyDown += ListGameLog_KeyDown;
		listGameLog.SelectedIndexChanged += List_SelectedIndexChanged;
		listGameLog.GotFocus += List_Enter;
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
