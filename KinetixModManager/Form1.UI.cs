using Microsoft.Web.WebView2.WinForms;

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
		string gameName = _settings.ActiveGame == GameProfiles.NoGame ? "" : GameDisplayName();
		Text = string.IsNullOrEmpty(gameName) ? Loc.T("ui.appTitle") : Loc.T("ui.appTitleGame", gameName);
		base.Size = new Size(1000, 700);
		base.KeyPreview = true;
		base.KeyDown += Form1_KeyDown;
		// Alt+Tab away mid-press and the release lands in another window, so the key would still look held when
		// the user came back and pressed it again.
		base.Deactivate += delegate { _keyRepeat.FocusLost(); };
		MenuStrip menuStrip = new MenuStrip();
		// Exiting the Alt menu (e.g. Alt then Escape) restores focus to the underlying list without
		// raising GotFocus, because the menu uses a special input mode that never takes the list's
		// focus. Re-announce the focused list when the menu deactivates so the user still hears their
		// position. BeginInvoke defers until focus has actually been restored.
		//
		// Guarded, because closing the window raises this event on the way out: disposing the form disposes the
		// menu strip, which takes Windows out of menu mode and fires MenuDeactivate one last time — by which
		// point the window has no handle left to post to, and BeginInvoke throws. That crashed the manager on
		// exit, and the only record of it was a crash log nothing in the app ever opened.
		menuStrip.MenuDeactivate += (s, e) =>
		{
			if (IsDisposed || Disposing || !IsHandleCreated) return;
			BeginInvoke(new Action(AnnounceFocusRestored));
		};
		_menuFile = new ToolStripMenuItem(Loc.T("menu.file")) { Name = "menuFile" };
		_menuFile.DropDownItems.Add(ShortcutLabel("menu.refreshAll", "RefreshAll"), null, delegate
		{
			RequestManualRefresh(everything: true);
		}).Name = "menuRefreshAll";
		_menuFile.DropDownItems.Add(ShortcutLabel("menu.refreshInstalled", "RefreshInstalled"), null, delegate
		{
			RequestManualRefresh(everything: false);
		}).Name = "menuRefreshInstalled";
		_menuFile.DropDownItems.Add(Loc.T("menu.checkManagerUpdates"), null, async delegate
		{
			await CheckForAppUpdates(manual: true);
		});
		if (TestMode.IsOn) _menuFile.DropDownItems.Add(BuildTestModeMenu());
		// Beside Settings rather than inside it: a key belongs to the user's account with a mod site, not to
		// the game they happen to have open, and the settings window is organised per game.
		_menuFile.DropDownItems.Add(Loc.T("menu.modSourceKeys"), null, delegate
		{
			ShowModSourceKeys();
		}).Name = "menuModSourceKeys";
		_menuFile.DropDownItems.Add(ShortcutLabel("menu.settings", "Settings"), null, delegate
		{
			ShowSettings();
		});
		_menuFile.DropDownItems.Add(new ToolStripSeparator());

		_menuCloseSessionItem = new ToolStripMenuItem(Loc.T("menu.closeSession"), null, delegate { CloseGameSession(); })
		{
			ShortcutKeys = Keys.Control | Keys.Shift | Keys.C,
			Visible = (_settings.ActiveGame != GameProfiles.NoGame)
		};
		_menuFile.DropDownItems.Add(_menuCloseSessionItem);

		_menuCloseSeparator = new ToolStripSeparator
		{
			Visible = (_settings.ActiveGame != GameProfiles.NoGame)
		};
		_menuFile.DropDownItems.Add(_menuCloseSeparator);

		_menuFile.DropDownItems.Add(Loc.T("menu.exit"), null, delegate
		{
			Application.Exit();
		});

		_menuGames = new ToolStripMenuItem(Loc.T("menu.games"));
		UpdateGamesMenu();

		ToolStripMenuItem menuMods = new ToolStripMenuItem(Loc.T("menu.mods")) { Name = "menuMods" };
		// The Mods menu is grouped into submenus so a screen-reader user isn't faced with one 40-item list. The most
		// common actions stay at the top level; everything else is filed under a themed submenu. Items keep their
		// stable Names (some are relabeled/hidden per game in SwitchActiveGame, which finds them recursively).

		// Common, frequently-used actions at the top level.
		menuMods.DropDownItems.Add(ShortcutLabel("menu.installZip", "InstallZip"), null, delegate
		{
			ManualInstall();
		});
		// Beside "install a file", because it is the same act with the finding-the-file part done for you.
		menuMods.DropDownItems.Add(Loc.T("menu.installGitHub"), null, delegate
		{
			Fire(InstallFromGitHubAsync(), "InstallFromGitHubAsync");
		}).Name = "menuInstallGitHub";
		menuMods.DropDownItems.Add(ShortcutLabel("menu.markVersionInstalled", "MarkVersionInstalled"), null,
			delegate { MarkSelectedUpdateAsInstalled(); }).Name = "menuMarkVersionInstalled";
		menuMods.DropDownItems.Add(ShortcutLabel("menu.updateAll", "UpdateAll"), null, async delegate
		{
			await UpdateAllMods();
		});
		menuMods.DropDownItems.Add(Loc.T("menu.launch", gameName, GetShortcutString("LaunchGame")), null, delegate
		{
			LaunchGame();
		}).Name = "menuLaunch";
		menuMods.DropDownItems.Add(new ToolStripSeparator());

		// Submenu: actions on the currently selected mod.
		var grpSelected = new ToolStripMenuItem(Loc.T("menu.groupSelectedMod")) { Name = "menuGroupSelectedMod" };
		grpSelected.DropDownItems.Add(ShortcutLabel("menu.editNote", "EditNote"), null, delegate { SetModNote(); });
		grpSelected.DropDownItems.Add(ShortcutLabel("menu.editConfig", "OpenConfig"), null, delegate { OpenSelectedModConfig(); });
		grpSelected.DropDownItems.Add(ShortcutLabel("menu.editConfigFile", "OpenConfigFile"), null, delegate { OpenSelectedModConfigFile(); });
		grpSelected.DropDownItems.Add(ShortcutLabel("menu.editManifest", "OpenManifest"), null, delegate { OpenSelectedModManifest(); });
		grpSelected.DropDownItems.Add(ShortcutLabel("menu.viewDependencies", "ShowDependencies"), null, delegate { ShowDependencies(); });
		grpSelected.DropDownItems.Add(ShortcutLabel("menu.resolveDependencies", "QuickFix"), null, delegate { QuickFixDependencies(); });
		grpSelected.DropDownItems.Add(ShortcutLabel("menu.viewChangelog", "ViewChangelog"), null, delegate { ViewModChangelog(); });
		grpSelected.DropDownItems.Add(ShortcutLabel("menu.viewDescription", "ViewDescription"), null, delegate { ViewModDescription(); });
		grpSelected.DropDownItems.Add(ShortcutLabel("menu.endorse", "Endorse"), null, delegate { EndorseSelectedMod(); });
		grpSelected.DropDownItems.Add(Loc.T("menu.verifyDeployment"), null, delegate { VerifyModDeployment(); }).Name = "menuVerifyDeployment";
		menuMods.DropDownItems.Add(grpSelected);

		// Submenu: installing and updating mods.
		var grpInstall = new ToolStripMenuItem(Loc.T("menu.groupInstall")) { Name = "menuGroupInstall" };
		grpInstall.DropDownItems.Add(Loc.T("menu.autoMatch"), null, async delegate { await AutoMatchNexusIDs(); });
		grpInstall.DropDownItems.Add(ShortcutLabel("menu.pendingDownloads", "InstallPendingDownload"), null, delegate { ShowPendingDownloads(); });
		grpInstall.DropDownItems.Add(ShortcutLabel("menu.downloadsHistory", "DownloadsHistory"), null, delegate { ShowDownloadsHistory(); });
		grpInstall.DropDownItems.Add(Loc.T("menu.tidyFolders"), null, delegate { TidyModFolderNames(); });
		grpInstall.DropDownItems.Add(ShortcutLabel("menu.trackedMods", "TrackedMods"), null, async delegate { await ShowTrackedMods(); });
		menuMods.DropDownItems.Add(grpInstall);

		// Submenu: building a list of mods worth suggesting to other players. Everything but the on/off switch is
		// disabled until curation is on — shown rather than hidden, so the two commands are discoverable and the
		// switch that wakes them is sitting directly above. See Form1.Curator.
		var grpCurate = new ToolStripMenuItem(Loc.T("menu.groupCurate")) { Name = "menuGroupCurate" };
		// The list itself first and always available: it is the thing this submenu is named after, and reading it
		// has nothing to do with whether you are building one.
		grpCurate.DropDownItems.Add(ShortcutLabel("menu.suggestedMods", "SuggestedMods"), null,
			delegate { ShowSuggestedMods(); }).Name = "menuSuggestedMods";
		// Import and export sit together, and neither is gated on curation mode. Taking in somebody else's list is
		// how a player gets one at all, and separating the pair so that one of them came and went with a setting
		// was worse than the alternative — an export with nothing marked simply says so.
		grpCurate.DropDownItems.Add(Loc.T("menu.importSuggestions"), null,
			async delegate { await ImportSuggestions(); }).Name = "menuImportSuggestions";
		grpCurate.DropDownItems.Add(Loc.T("menu.exportSuggestions"), null,
			async delegate { await ExportSuggestions(); }).Name = "menuExportSuggestions";
		grpCurate.DropDownItems.Add(new ToolStripSeparator());
		grpCurate.DropDownItems.Add(ShortcutLabel("menu.curationMode", "CurationMode"), null,
			delegate { ToggleCurationMode(); }).Name = "menuCurationMode";
		grpCurate.DropDownItems.Add(ShortcutLabel("menu.markSuggestion", "MarkSuggestion"), null,
			delegate { ToggleModSuggestion(); }).Name = "menuMarkSuggestion";
		grpCurate.DropDownItems.Add(ShortcutLabel("menu.suggestedList", "SuggestedList"), null,
			delegate { ShowSuggestedModsReview(); }).Name = "menuSuggestedList";
		grpCurate.DropDownItems.Add(Loc.T("menu.manageCategories"), null,
			delegate { ShowCategoryManager(); }).Name = "menuManageCategories";
		menuMods.DropDownItems.Add(grpCurate);

		// Curation can be switched on from the keyboard without the menu having been opened, so the tick and the
		// greying are brought up to date as the menu opens rather than only when the switch itself is used.
		menuMods.DropDownOpening += delegate { UpdateCuratorMenuState(); };

		// Submenu: profiles and shareable collections.
		var grpProfiles = new ToolStripMenuItem(Loc.T("menu.groupProfiles")) { Name = "menuGroupProfiles" };
		grpProfiles.DropDownItems.Add(ShortcutLabel("menu.saveProfile", "SaveProfile"), null, delegate { CreateProfileFromCurrent(); });
		grpProfiles.DropDownItems.Add(ShortcutLabel("menu.exportCollection", "ExportCollection"), null, async delegate { await ExportCollection(); });
		grpProfiles.DropDownItems.Add(ShortcutLabel("menu.installCollection", "InstallCollection"), null, async delegate { await InstallCollectionAsync(); });
		grpProfiles.DropDownItems.Add(Loc.T("menu.importMO2"), null, delegate { ImportFromMO2(); }).Name = "menuImportMO2";
		menuMods.DropDownItems.Add(grpProfiles);

		// Submenu: health checks and reports.
		var grpHealth = new ToolStripMenuItem(Loc.T("menu.groupHealth")) { Name = "menuGroupHealth" };
		grpHealth.DropDownItems.Add(ShortcutLabel("menu.healthCheck", "HealthCheck"), null, async delegate { await RunHealthCheck(); });
		grpHealth.DropDownItems.Add(ShortcutLabel("menu.checkRequirements", "CheckRequirements"), null, async delegate { await ShowRequirementsReport(); });
		grpHealth.DropDownItems.Add(ShortcutLabel("menu.checkBrokenMods", "CheckBrokenMods"), null, async delegate { await ShowBrokenModsReport(); });
		grpHealth.DropDownItems.Add(ShortcutLabel("menu.fileConflicts", "FileConflicts"), null, delegate { ShowFileConflictsReport(); });
		grpHealth.DropDownItems.Add(Loc.T("menu.updateCoverage"), null, async delegate { await ShowUpdateCoverageReport(); }).Name = "menuUpdateCoverage";
		grpHealth.DropDownItems.Add(ShortcutLabel("menu.pluginSlots", "PluginSlots"), null, delegate { AnnouncePluginSlotUsage(); }).Name = "menuPluginSlots";
		grpHealth.DropDownItems.Add(Loc.T("menu.resetIgnoredReqs"), null, delegate { ResetIgnoredRequirements(); });
		menuMods.DropDownItems.Add(grpHealth);

		// Submenu: Skyrim/Fallout 4 load order and loose-file deployment.
		var grpLoad = new ToolStripMenuItem(Loc.T("menu.groupLoadOrder")) { Name = "menuGroupLoadOrder" };
		grpLoad.DropDownItems.Add(ShortcutLabel("menu.autoSort", "AutoSort"), null, async delegate { await AutoSortPluginsAsync(); }).Name = "menuAutoSort";
		grpLoad.DropDownItems.Add(Loc.T("menu.conflictWinners"), null, delegate { ShowConflictOverride(); }).Name = "menuConflictWinners";
		grpLoad.DropDownItems.Add(Loc.T("menu.addLoadRule"), null, delegate { AddLoadOrderRule(); }).Name = "menuAddLoadRule";
		grpLoad.DropDownItems.Add(Loc.T("menu.manageLoadRules"), null, delegate { ShowLoadOrderRules(); }).Name = "menuManageLoadRules";
		grpLoad.DropDownItems.Add(Loc.T("menu.rebuildDeploy"), null, delegate { RebuildDeployment(); }).Name = "menuRebuildDeploy";
		grpLoad.DropDownItems.Add(Loc.T("menu.purgeDeploy"), null, delegate { PurgeDeployment(); }).Name = "menuPurgeDeploy";
		grpLoad.DropDownItems.Add(Loc.T("menu.exportLoadOrder"), null, async delegate { await ExportLoadOrder(); }).Name = "menuExportLoadOrder";
		grpLoad.DropDownItems.Add(Loc.T("menu.importLoadOrder"), null, async delegate { await ImportLoadOrder(); }).Name = "menuImportLoadOrder";
		menuMods.DropDownItems.Add(grpLoad);

		// Submenu: game launch/setup and maintenance operations.
		var grpGame = new ToolStripMenuItem(Loc.T("menu.groupGame")) { Name = "menuGroupGame" };
		grpGame.DropDownItems.Add(Loc.T("menu.installSuite", gameName), null, delegate { ShowAccessibilitySuiteDialog(); }).Name = "menuSuite";
		grpGame.DropDownItems.Add(Loc.T("menu.uninstallScriptExtender"), null, delegate { UninstallScriptExtenderCommand(); }).Name = "menuUninstallSE";
		// Minecraft only: the one game the manager starts itself, and so the one where who you play as, and
		// whether that session can reach a server, are the manager's business. See Form1.MinecraftAccount.
		// Its words carry the current mode — "online play, connected as Sean" — refreshed as the menu opens, so the
		// state is heard on the way past without opening anything.
		ToolStripItem minecraftAccountItem = grpGame.DropDownItems.Add(MinecraftAccountMenuText(), null,
			delegate { Fire(ShowMinecraftAccountAsync(), "ShowMinecraftAccountAsync"); });
		minecraftAccountItem.Name = "menuMinecraftAccount";
		grpGame.DropDownOpening += delegate { minecraftAccountItem.Text = MinecraftAccountMenuText(); };
		// Minecraft only: which Minecraft this session is — the player's own or a modpack — and bringing packs across
		// from the Modrinth App, the way Mod Organizer 2 setups are brought across for Skyrim and Fallout 4.
		grpGame.DropDownItems.Add(Loc.T("menu.chooseMinecraftSetup"), null, delegate
		{
			IReadOnlyList<MinecraftPack> packs = MinecraftModpacks.FindInstalled(MinecraftModpacks.PacksFolder);
			if (packs.Count == 0) { Speak(Loc.T("mc.pack.none")); return; }
			if (ChooseMinecraftSetup(packs) is { } chosen) SwitchToMinecraftSetup(chosen);
		}).Name = "menuChooseMinecraftSetup";
		grpGame.DropDownItems.Add(Loc.T("menu.importModrinthApp"), null, delegate
		{
			Fire(ImportFromModrinthAppAsync(), "ImportFromModrinthAppAsync");
		}).Name = "menuImportModrinthApp";
		grpGame.DropDownItems.Add(Loc.T("menu.removeUnusedMinecraftVersions"), null, delegate
		{
			Fire(RemoveUnusedMinecraftVersionsAsync(), "RemoveUnusedMinecraftVersionsAsync");
		}).Name = "menuRemoveUnusedMinecraftVersions";
		grpGame.DropDownItems.Add(Loc.T("menu.removeModrinthAppLeftovers"), null, delegate
		{
			Fire(RemoveModrinthAppLeftoversAsync(), "RemoveModrinthAppLeftoversAsync");
		}).Name = "menuRemoveModrinthAppLeftovers";
		grpGame.DropDownItems.Add(Loc.T("menu.editGameIni"), null, delegate { EditGameIni(); }).Name = "menuEditGameIni";
		grpGame.DropDownItems.Add(ShortcutLabel("menu.saveManager", "SaveManager"), null, async delegate { await ShowSaveManager(); }).Name = "menuSaveManager";
		grpGame.DropDownItems.Add(Loc.T("menu.prepUpdate"), null, delegate { PrepareForGameUpdate(); }).Name = "menuPrepUpdate";
		grpGame.DropDownItems.Add(Loc.T("menu.restoreUpdate"), null, delegate { RestoreAfterUpdate(); }).Name = "menuRestoreUpdate";
		grpGame.DropDownItems.Add(Loc.T("menu.restoreSafety"), null, delegate { ShowRestoreSafetyBackup(); }).Name = "menuRestoreSafety";
		menuMods.DropDownItems.Add(grpGame);

		ToolStripMenuItem menuView = new ToolStripMenuItem(Loc.T("menu.view")) { Name = "menuView" };
		menuView.DropDownItems.Add(ShortcutLabel("menu.apiCredits", "ApiCredits"), null, delegate
		{
			ShowApiCredits();
		});
		menuView.DropDownItems.Add(ShortcutLabel("menu.diagnoseAi", "DiagnoseAi"), null, delegate
		{
			DiagnoseWithAi();
		});
		menuView.DropDownItems.Add(Loc.T("menu.askAi"), null, delegate
		{
			AskFreeformAi();
		});
		menuView.DropDownItems.Add(ShortcutLabel("menu.openDownloads", "OpenDownloads"), null, delegate
		{
			OpenFolder(downloadsPath);
		});
		menuView.DropDownItems.Add(ShortcutLabel("menu.openBackups", "OpenBackups"), null, delegate
		{
			OpenFolder(backupsPath);
		});
		menuView.DropDownItems.Add(Loc.T("menu.freeUpSpace"), null, delegate
		{
			FreeUpSpace();
		});
		menuView.DropDownItems.Add(ShortcutLabel("menu.openSmapiLog", "OpenLogFile"), null, delegate
		{
			OpenGameLog();
		}).Name = "menuOpenLog";
		menuView.DropDownItems.Add(ShortcutLabel("menu.openErrorLog", "OpenErrorLog"), null, delegate
		{
			OpenErrorLog();
		});
		ToolStripMenuItem menuHelp = new ToolStripMenuItem(Loc.T("menu.help"));
		menuHelp.DropDownItems.Add(Loc.T("menu.setupWizard"), null, delegate
		{
			ShowSetupWizard();
		});
		menuHelp.DropDownItems.Add(ShortcutLabel("menu.userManual", "Manual"), null, delegate
		{
			ShowManual();
		});
		menuHelp.DropDownItems.Add(ShortcutLabel("menu.changeLog", "ChangeLog"), null, delegate
		{
			ShowChangeLog();
		});
		menuHelp.DropDownItems.Add(ShortcutLabel("menu.modDocs", "ModDocs"), null, delegate
		{
			ShowModDocs();
		});
		menuHelp.DropDownItems.Add(ShortcutLabel("menu.accessibilityControls", "ControlsHelp"), null, delegate
		{
			ShowAccessibilityControls();
		});
		menuHelp.DropDownItems.Add(Loc.T("menu.soundDemo"), null, delegate
		{
			ShowSoundDemo();
		});
		menuHelp.DropDownItems.Add(Loc.T("menu.themeManager"), null, delegate
		{
			ShowThemeManager();
		});
		menuHelp.DropDownItems.Add(Loc.T("menu.shortcutCustomization"), null, delegate
		{
			ShowShortcutManager();
		});
		menuHelp.DropDownItems.Add(Loc.T("menu.donate"), null, delegate
		{
			ShowDonate();
		});
		menuHelp.DropDownItems.Add(Loc.T("about.title"), null, delegate
		{
			ShowAbout();
		});
		menuStrip.Items.Add(_menuFile);
		menuStrip.Items.Add(_menuGames);
		menuStrip.Items.Add(menuMods);
		menuStrip.Items.Add(menuView);
		menuStrip.Items.Add(menuHelp);
		base.MainMenuStrip = menuStrip;
		tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 1,
			ColumnCount = 1
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.Padding = new Padding(0, 25, 0, 0);
		mainTabs = new AccessibleTabControl
		{
			Dock = DockStyle.Fill,
			AccessibleName = SilentAccessibleName
		};
		tabInstalled = new TabPage(Loc.T("tab.installed"));
		TableLayoutPanel installedLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 2,
			ColumnCount = 1
		};
		installedLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		installedLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		FlowLayoutPanel installedFilterBar = new FlowLayoutPanel
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
		txtSearchInstalled.KeyDown += (_, e) =>
		{
			if (e.KeyCode != Keys.Down) return;
			// Moving to the list within the pause must land on what was typed for, not on the list before it.
			if (_installedSearchPause.Enabled) { _installedSearchPause.Stop(); FilterInstalledMods(); }
			if (listInstalled.Items.Count == 0) return;
			e.Handled = e.SuppressKeyPress = true;
			listInstalled.Focus();
		};
		// Each letter regrouped the list and queued a count to be read, so the reader went on reading counts after typing stopped.
		_installedSearchPause = new System.Windows.Forms.Timer { Interval = 300 };
		_installedSearchPause.Tick += delegate
		{
			_installedSearchPause.Stop();
			// A screen opened during the pause: the list is kept right, but a count would be read over that screen.
			if (OverlayIsOpen) RebuildInstalledListBox();
			else FilterInstalledMods();
		};
		txtSearchInstalled.TextChanged += delegate
		{
			_installedSearchPause.Stop();
			_installedSearchPause.Start();
		};
		cmbCategoryFilter = new ComboBox
		{
			AccessibleName = Loc.T("ui.categoryFilterName"),
			Width = 150,
			Font = new Font("Segoe UI", 12f),
			DropDownStyle = ComboBoxStyle.DropDownList
		};
		cmbCategoryFilter.Items.Add("All Categories");
		cmbCategoryFilter.SelectedIndex = 0;
		cmbCategoryFilter.SelectedIndexChanged += delegate
		{
			if (_suppressInstalledFilterEvent) return;
			// The reader names the dropdown's new value itself, so the counts wait their turn behind it.
			FilterInstalledMods(readerNamesTheChange: true);
		};
		installedFilterBar.Controls.Add(new Label
		{
			Text = Loc.T("ui.searchLabel"),
			AutoSize = true,
			Padding = new Padding(5, 8, 0, 0)
		});
		installedFilterBar.Controls.Add(txtSearchInstalled);
		installedFilterBar.Controls.Add(new Label
		{
			Text = Loc.T("ui.categoryLabel"),
			AutoSize = true,
			Padding = new Padding(10, 8, 0, 0)
		});
		installedFilterBar.Controls.Add(cmbCategoryFilter);
		cmbStatusFilter = new ComboBox
		{
			Width = 150,
			Font = new Font("Segoe UI", 12f),
			DropDownStyle = ComboBoxStyle.DropDownList,
			AccessibleName = Loc.T("ui.statusFilterName")
		};
		// Order/text supplied here so the selection can be matched by index regardless of language. The last two
		// filter by shape rather than by state: whether a mod stands on its own or shares a folder with others.
		foreach (string label in new[]
		{
			Loc.T("status.all"), Loc.T("status.enabled"), Loc.T("status.disabled"), Loc.T("status.hasNote"),
			Loc.T("status.singlesOnly"), Loc.T("status.groupsOnly")
		})
			cmbStatusFilter.Items.Add(label);
		cmbStatusFilter.SelectedIndex = 0;
		cmbStatusFilter.SelectedIndexChanged += delegate
		{
			if (_suppressInstalledFilterEvent) return;
			// The reader names the dropdown's new value itself, so the counts wait their turn behind it.
			FilterInstalledMods(readerNamesTheChange: true);
		};
		installedFilterBar.Controls.Add(new Label
		{
			Text = Loc.T("ui.statusLabel"),
			AutoSize = true,
			Padding = new Padding(10, 8, 0, 0)
		});
		installedFilterBar.Controls.Add(cmbStatusFilter);
		listInstalled = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listInstalled",
			Font = new Font("Segoe UI", 12f)
		};
		listInstalled.AccessibleName = Loc.T("ui.installedList");
		installedLayout.Controls.Add(installedFilterBar, 0, 0);
		installedLayout.Controls.Add(listInstalled, 0, 1);
		tabInstalled.Controls.Add(installedLayout);
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
		TableLayoutPanel backupsLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 2,
			ColumnCount = 1
		};
		backupsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f));
		backupsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		btnPruneBackups = new Button
		{
			Text = ShortcutLabel("ui.pruneBackups", "DeleteOldBackups"),
			Width = 250,
			Height = 35
		};
		btnPruneBackups.Click += delegate
		{
			PruneAllBackups();
		};
		backupsLayout.Controls.Add(btnPruneBackups, 0, 0);
		listBackups = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listBackups",
			Font = new Font("Segoe UI", 12f)
		};
		listBackups.AccessibleName = Loc.T("ui.backupsList");
		backupsLayout.Controls.Add(listBackups, 0, 1);
		tabBackups.Controls.Add(backupsLayout);
		tabDiscovery = new TabPage(Loc.T("tab.discovery"));
		TableLayoutPanel discoveryLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 2,
			ColumnCount = 1
		};
		discoveryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
		discoveryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		FlowLayoutPanel discoverySearchBar = new FlowLayoutPanel
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
		ComboBox.ObjectCollection discoveryTypeItems = cmbDiscoveryType.Items;
		// "All" browses the game's whole catalogue alphabetically — the way to find a specific mod you already
		// know exists, rather than whatever is currently popular.
		// "Nexus Categories" browses the category chosen beside it, best-known first — the one-step way in when
		// what you want is "show me the armour mods" rather than a search or a popularity chart.
		object[] comboChoices = new string[6]
			{ "Search", "All", "Trending", "Most Popular", "Recent", NexusService.NexusCategoryBrowse };
		discoveryTypeItems.AddRange(comboChoices);
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
		// Shown only while the Nexus Categories type is chosen, and the filter applies only then. Hiding it in the
		// other modes and still applying it would be the worst of both: a filter narrowing your results with no
		// control anywhere to say so, which is precisely how the language filter used to look like "these mods
		// aren't on Nexus".
		cmbDiscoveryCategory = new ComboBox
		{
			Width = 190,
			Font = new Font("Segoe UI", 12f),
			DropDownStyle = ComboBoxStyle.DropDownList,
			AccessibleName = Loc.T("ui.filterCategory")
		};
		// A starter entry so the control means something before the game's real categories have been fetched.
		cmbDiscoveryCategory.Items.Add(new CategoryOption { Name = "" });   // Any category
		cmbDiscoveryCategory.SelectedIndex = 0;

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
				// Before the wait: after it, this event has already finished, and the box beeped.
				e.SuppressKeyPress = true;
				await RunDiscovery();
			}
			else if (e.KeyCode == Keys.Down && listDiscovery.Items.Count > 0)
			{
				// The results are otherwise half a dozen Tabs away, past every filter.
				e.Handled = e.SuppressKeyPress = true;
				listDiscovery.Focus();
			}
		};
		discoverySearchBar.Controls.Add(new Label
		{
			Text = Loc.T("ui.searchTypeLabel"),
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		});
		discoverySearchBar.Controls.Add(txtSearch);
		// Search history sits right after the search box and before the search-type selector, per user preference.
		discoverySearchBar.Controls.Add(btnHistory);
		discoverySearchBar.Controls.Add(cmbDiscoveryType);
		// Minecraft only: search for mods, or for modpacks — whole setups that install as packs of their own.
		_lblDiscoveryContent = new Label { Text = Loc.T("ui.searchForLabel"), AutoSize = true, Padding = new Padding(10, 5, 0, 0) };
		cmbDiscoveryContent = new ComboBox
		{
			Width = 130,
			Font = new Font("Segoe UI", 12f),
			DropDownStyle = ComboBoxStyle.DropDownList,
			AccessibleName = Loc.T("ui.searchForName")
		};
		cmbDiscoveryContent.Items.AddRange(new object[]
			{ Loc.T("ui.searchForMods"), Loc.T("ui.searchForModpacks"), Loc.T("ui.searchForFollowed") });
		cmbDiscoveryContent.SelectedIndex = 0;
		bool minecraftSearch = GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.Minecraft);
		_lblDiscoveryContent.Visible = cmbDiscoveryContent.Visible = minecraftSearch;
		discoverySearchBar.Controls.Add(_lblDiscoveryContent);
		discoverySearchBar.Controls.Add(cmbDiscoveryContent);
		_lblDiscoveryCategory = new Label { Text = Loc.T("ui.nexusCategoryLabel"), AutoSize = true, Padding = new Padding(10, 5, 0, 0) };
		discoverySearchBar.Controls.Add(_lblDiscoveryCategory);
		discoverySearchBar.Controls.Add(cmbDiscoveryCategory);
		discoverySearchBar.Controls.Add(new Label { Text = Loc.T("ui.languageLabel"), AutoSize = true, Padding = new Padding(10, 5, 0, 0) });
		discoverySearchBar.Controls.Add(cmbDiscoveryLanguage);
		discoverySearchBar.Controls.Add(new Label { Text = Loc.T("ui.resultsPerLoadLabel"), AutoSize = true, Padding = new Padding(10, 5, 0, 0) });
		discoverySearchBar.Controls.Add(cmbDiscoveryPageSize);
		discoverySearchBar.Controls.Add(btnSearch);
		// The category selector belongs to one search type, so it comes and goes with the type. Set once here for
		// the starting type (silently -- nothing has changed yet from the user's point of view), and on each change.
		cmbDiscoveryType.SelectedIndexChanged += delegate { UpdateDiscoveryCategoryVisibility(announce: true); };
		UpdateDiscoveryCategoryVisibility(announce: false);
		listDiscovery = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listDiscovery",
			Font = new Font("Segoe UI", 12f)
		};
		listDiscovery.AccessibleName = Loc.T("ui.discoveryList");

		// Two rows: the search bar and the results list. "Load more" is an inline row at the bottom
		// of the list itself (see DiscoveryLoadMoreRow), so there is no separate button row.
		discoveryLayout.RowCount = 2;
		discoveryLayout.RowStyles.Clear();
		discoveryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f)); // Search bar
		discoveryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // List

		discoveryLayout.Controls.Add(discoverySearchBar, 0, 0);
		discoveryLayout.Controls.Add(listDiscovery, 0, 1);

		tabDiscovery.Controls.Add(discoveryLayout);
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
		BuildMinecraftPacksTab();
		tabSmapiLog = new TabPage(Loc.T("tab.smapiLog"));
		TableLayoutPanel smapiLogLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 2,
			ColumnCount = 1
		};
		smapiLogLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		smapiLogLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		FlowLayoutPanel smapiLogFilterBar = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight
		};
		cmbLogFilter = new ComboBox
		{
			AccessibleName = Loc.T("ui.logFilterName"),
			Width = 180,
			Font = new Font("Segoe UI", 12f),
			DropDownStyle = ComboBoxStyle.DropDownList
		};
		ComboBox.ObjectCollection logFilterItems = cmbLogFilter.Items;
		comboChoices = new string[4] { "Errors and Warnings", "Errors Only", "Full Log", "Links Only" };
		logFilterItems.AddRange(comboChoices);
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
		smapiLogFilterBar.Controls.Add(new Label
		{
			Text = Loc.T("ui.filterLabel"),
			AutoSize = true,
			Padding = new Padding(5, 8, 0, 0)
		});
		smapiLogFilterBar.Controls.Add(cmbLogFilter);
		smapiLogFilterBar.Controls.Add(new Label
		{
			Text = Loc.T("ui.searchLabel"),
			AutoSize = true,
			Padding = new Padding(10, 8, 0, 0)
		});
		smapiLogFilterBar.Controls.Add(txtSearchLog);
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
		smapiLogLayout.Controls.Add(smapiLogFilterBar, 0, 0);
		smapiLogLayout.Controls.Add(listLog, 0, 1);
		tabSmapiLog.Controls.Add(smapiLogLayout);

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

		string initialWikiTitle = GameProfiles.BaseId(_settings.ActiveGame) switch
		{
			"SkyrimSE" => Loc.T("tab.wikiSkyrim"),
			"Fallout4" => Loc.T("tab.wikiFallout"),
			"MoonlightPeaks" => Loc.T("tab.wikiMoonlight"),
			"Witcher3" => Loc.T("tab.wikiWitcher"),
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
		string searchLabel = GameProfiles.BaseId(_settings.ActiveGame) switch
		{
			"SkyrimSE" => Loc.T("ui.searchWikiSkyrim"),
			"Fallout4" => Loc.T("ui.searchWikiFallout"),
			"MoonlightPeaks" => Loc.T("ui.searchWikiMoonlight"),
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
			if (e.KeyCode == Keys.Down && listWikiResults.CanFocus && listWikiResults.Items.Count > 0)
			{
				e.Handled = e.SuppressKeyPress = true;
				listWikiResults.Focus();
				return;
			}
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
				else Fire(LoadWikiPage(res.Title), "LoadWikiPage");
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
		Fire(InitializeAndLoadInitialPagesAsync(), "InitializeAndLoadInitialPagesAsync");
		
		splitWiki.Panel1.Controls.Add(listWikiResults);
		splitWiki.Panel2.Controls.Add(webViewWiki);
		tableLayoutPanelWiki.Controls.Add(flowLayoutPanelWikiTop, 0, 0);
		tableLayoutPanelWiki.Controls.Add(splitWiki, 0, 1);
		tabWiki.Controls.Add(tableLayoutPanelWiki);

		// Now that splitWiki exists, load the default (main game) wiki's live categories. PopulateModWikis above
		// deliberately doesn't do this, since it runs before splitWiki is created.
		Fire(RefreshCategoriesForActiveWikiAsync(), "RefreshCategoriesForActiveWikiAsync");

		// Walkthroughs Tab Setup
		string initialWalkthroughTitle = GameProfiles.BaseId(_settings.ActiveGame) switch
		{
			"SkyrimSE" => Loc.T("tab.walkSkyrim"),
			"Fallout4" => Loc.T("tab.walkFallout"),
			"MoonlightPeaks" => Loc.T("tab.walkMoonlight"),
			"Witcher3" => Loc.T("tab.walkWitcher"),
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
		// Minecraft Packs sits right after Installed in every Minecraft session — the player's own and each pack's.
		// Added here as well as in SwitchActiveGame, because startup does not go through SwitchActiveGame.
		if (GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.Minecraft))
			mainTabs.TabPages.Add(tabMinecraftPacks);
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
		if (GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley))
		{
			mainTabs.TabPages.Add(tabSmapiLog);
		}
		else if (GameHasLogTab(_settings.ActiveGame))
		{
			mainTabs.TabPages.Add(tabGameLog);
		}

		tableLayoutPanel.Controls.Add(mainTabs, 0, 0);

		InitializeGameSelectionPanel();
		base.Controls.Add(menuStrip);

		bool noGame = _settings.ActiveGame == GameProfiles.NoGame;
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
		// Apply the per-game Mods-menu labels/visibility for the game restored from settings (startup doesn't go
		// through SwitchActiveGame when the active game is unchanged).
		ConfigureModsMenuForGame();
		ConfigureLogMenuItemForGame();
		// Same reason, for the tab labels: the designer builds the tabs with Stardew's names, and a
		// session that is restored rather than switched into never reaches the code that renames them.
		ApplyGameTabLabels(_settings.ActiveGame);
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
		KeepLeftAndRightOffTheSelection(listModPriority);
		listPluginOrder.KeyDown += ListPluginOrder_KeyDown;
		listPluginOrder.SelectedIndexChanged += ListModPriority_SelectedIndexChanged;
		listPluginOrder.GotFocus += List_Enter;
		KeepLeftAndRightOffTheSelection(listPluginOrder);
		listPluginOrder.GotFocus += ListPluginOrder_WarnOnFocus;
		listCreations.KeyDown += ListCreations_KeyDown;
		listCreations.SelectedIndexChanged += ListModPriority_SelectedIndexChanged;
		listCreations.GotFocus += List_Enter;
		KeepLeftAndRightOffTheSelection(listCreations);
		listGameLog.KeyDown += ListGameLog_KeyDown;
		listGameLog.SelectedIndexChanged += List_SelectedIndexChanged;
		listGameLog.GotFocus += List_Enter;
		KeepLeftAndRightOffTheSelection(listGameLog);
		_searchTimer.Tick += delegate
		{
			_searchTimer.Stop();
			_searchBuffer = "";
		};
		listWikiResults.SelectedIndexChanged += Wiki_SelectedIndexChanged;
		listWikiResults.GotFocus += List_Enter;
		KeepLeftAndRightOffTheSelection(listWikiResults);
		listWikiResults.DoubleClick += async delegate(object? s, EventArgs e)
		{
			if (listWikiResults.SelectedItem is WikiResult res)
			{
				if (res.IsCategory) await LoadWikiCategory(res.Title);
				else Fire(LoadWikiPage(res.Title), "LoadWikiPage");
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
		KeepLeftAndRightOffTheSelection(listWalkthroughs);
		PopulateWalkthroughs();
	}
}
