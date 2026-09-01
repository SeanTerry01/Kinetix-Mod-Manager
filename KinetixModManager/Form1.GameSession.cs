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

/// <summary>Active-game switching, session close, game menus, and game-selection panel for Form1.</summary>
public partial class Form1
{
	/// <summary>
	/// Finds a menu item by its stable <see cref="ToolStripItem.Name"/> anywhere in <paramref name="root"/>'s
	/// dropdown tree, including nested submenus (unlike <c>DropDownItems[name]</c>, which searches only direct
	/// children). Used so per-game relabeling/visibility keeps working now that Mods items live in submenus.
	/// </summary>
	private static ToolStripItem? FindMenuItem(ToolStripMenuItem root, string name)
	{
		foreach (ToolStripItem item in root.DropDownItems)
		{
			if (item.Name == name) return item;
			if (item is ToolStripMenuItem sub && sub.HasDropDownItems)
			{
				ToolStripItem? found = FindMenuItem(sub, name);
				if (found != null) return found;
			}
		}
		return null;
	}

	/// <summary>
	/// Applies the per-game Mods-menu state for the active game: relabels the launch and accessibility-suite items
	/// with the game name, and shows the Skyrim/Fallout 4-only items and the load-order submenu only for those
	/// games. Called both on a game switch and once at startup (a game restored from settings doesn't go through
	/// SwitchActiveGame). Items are found recursively by their stable Name since they now live in submenus.
	/// </summary>
	private void ConfigureModsMenuForGame()
	{
		if (MainMenuStrip?.Items["menuMods"] is not ToolStripMenuItem modsMenu) return;
		string game = _settings.ActiveGame;
		bool bethesda = GameProfiles.IsAnyGame(game, GameProfiles.SkyrimSE, GameProfiles.Fallout4);
		string gameName = game == "None" ? "" : GameDisplayName();

		if (FindMenuItem(modsMenu, "menuLaunch") is ToolStripItem launchItem)
			launchItem.Text = Loc.T("menu.launch", gameName, GetShortcutString("LaunchGame"));
		if (FindMenuItem(modsMenu, "menuSuite") is ToolStripItem suiteItem)
			suiteItem.Text = Loc.T("menu.installSuite", gameName);
		// The config editor edits the game's own INIs for Skyrim/Fallout 4 and each mod's BepInEx config file for
		// Moonlight Peaks, so it is named for what it actually opens. Stardew mods keep their settings in JSON,
		// which this editor doesn't handle, so it is hidden there rather than shown as a dead command.
		GameProfile? profile = GameProfiles.Find(game);
		if (FindMenuItem(modsMenu, "menuEditGameIni") is ToolStripItem iniItem)
		{
			bool bepInEx = GameProfiles.IsGame(game, GameProfiles.MoonlightPeaks);
			// Any game that names configuration files of its own can be edited here — The Witcher 3's
			// user.settings and input.settings are INI files in all but their extension.
			iniItem.Visible = (profile?.ConfigFileNames.Count ?? 0) > 0 || bepInEx;
			iniItem.Text = bepInEx ? Loc.T("menu.editModConfigs") : Loc.T("menu.editGameIni");
		}
		// The save manager follows the saves: every game that keeps a save folder the manager can find, which is
		// the Bethesda pair and The Witcher 3.
		if (FindMenuItem(modsMenu, "menuSaveManager") is ToolStripItem saveItem)
			saveItem.Visible = !string.IsNullOrEmpty(profile?.SavesFolderName);
		// Skyrim SE / Fallout 4-only items: script extender, conflict winners, load-order rules, safety restore,
		// and the prepare/restore-for-update pair. All of them are about staged deployment or plugins.txt, which
		// no other game has.
		foreach (string name in new[] { "menuUninstallSE", "menuConflictWinners", "menuAddLoadRule",
			"menuManageLoadRules", "menuRestoreSafety", "menuPrepUpdate", "menuRestoreUpdate" })
			if (FindMenuItem(modsMenu, name) is ToolStripItem item) item.Visible = bethesda;
		// The whole load-order/files submenu is Skyrim/Fallout 4 only; hide it for Stardew rather than show a group
		// of "not applicable" items.
		if (FindMenuItem(modsMenu, "menuGroupLoadOrder") is ToolStripItem loadGroup)
			loadGroup.Visible = bethesda;
	}

	private void SwitchActiveGame(string game)
	{
		if (_settings.ActiveGame == game) return;
		// Refuse to load a session for a game that is not installed. Without this guard
		// CurrentModsPath falls back to the Stardew Mods path, so the session would silently
		// load with another game's mods. EnsureGameInstalledOrOfferPurchase announces the
		// situation and offers the Steam/GOG purchase flow; on failure we leave the current
		// session untouched.
		if (!EnsureGameInstalledOrOfferPurchase(game)) return;
		// Capture the closing session's sound theme before it is swapped out below, so the
		// disconnect cue can play in the theme of the game being closed rather than the new one.
		string closingTheme = _settings.CurrentTheme;
		_settings.ActiveGame = game;
		// Switch the sound theme to match the newly loaded game (None -> Default), unless the
		// user has opted into manual theme selection, in which case their choice is preserved.
		if (!_settings.AllowManualTheme)
		{
			_settings.CurrentTheme = AppSettings.ThemeForGame(game);
		}
		_settings.Save();

		// ActiveGame has just been set to the new game above, so GameDisplayName() reflects it (with the detected
		// Skyrim/Fallout edition + build). "None" has no game name and shows the bare app title.
		string gameName = game == "None" ? "" : GameDisplayName();
		Text = string.IsNullOrEmpty(gameName) ? Loc.T("ui.appTitle") : Loc.T("ui.appTitleGame", gameName);

		bool noGame = game == "None";
		if (noGame)
		{
			if (base.Controls.Contains(tableLayoutPanel))
			{
				base.Controls.Remove(tableLayoutPanel);
			}
			if (_gameSelectionPanel != null)
			{
				if (!base.Controls.Contains(_gameSelectionPanel))
				{
					base.Controls.Add(_gameSelectionPanel);
				}
				_gameSelectionPanel.Visible = true;
				_gameSelectionPanel.Enabled = true;
			}
		}
		else
		{
			if (_gameSelectionPanel != null && base.Controls.Contains(_gameSelectionPanel))
			{
				base.Controls.Remove(_gameSelectionPanel);
			}
			if (!base.Controls.Contains(tableLayoutPanel))
			{
				base.Controls.Add(tableLayoutPanel);
			}
			tableLayoutPanel.Visible = true;
		}

		UpdateGamesMenu();
		UpdateMenuState();

		if (noGame)
		{
			// Closing the session ends the Nexus connection for the game that was loaded.
			// Tear that state down (and play the disconnect cue) here, so a later program
			// exit with no game loaded does not replay a disconnect for an already-closed
			// session. See the FormClosing handler in Form1.cs.
			_nexusService.Disconnect();
			_soundEngine.Play("disconnect", closingTheme);
			if (_lstGames != null) _lstGames.Focus();
			Speak(Loc.T("session.closed"));
			return;
		}

		// Re-label the game-specific Mods items for the newly loaded game. Items are found by their stable
		// Name (set in SetupAccessibleUI), not their visible text, so this keeps working when the UI is localized.
		ConfigureModsMenuForGame();

		// The View menu's "Open Log" item targets a different log per game (SMAPI for Stardew, the
		// script extender log for Skyrim/FO4), so relabel it to match — found by its stable Name.
		if (MainMenuStrip?.Items["menuView"] is ToolStripMenuItem viewMenu &&
			viewMenu.DropDownItems["menuOpenLog"] is ToolStripItem logItem)
		{
			logItem.Text = GameProfiles.BaseId(game) switch
			{
				"StardewValley"  => Loc.T("menu.openSmapiLog", GetShortcutString("OpenLogFile")),
				"MoonlightPeaks" => Loc.T("menu.openBepInExLog", GetShortcutString("OpenLogFile")),
				"Witcher3"       => Loc.T("menu.openWitcherLog", GetShortcutString("OpenLogFile")),
				_                => Loc.T("menu.openGameLog", GetShortcutString("OpenLogFile"))
			};
		}

		tabWiki.Text = GameProfiles.BaseId(game) switch
		{
			"SkyrimSE" => Loc.T("tab.wikiSkyrim"),
			"Fallout4" => Loc.T("tab.wikiFallout"),
			"MoonlightPeaks" => Loc.T("tab.wikiMoonlight"),
			"Witcher3" => Loc.T("tab.wikiWitcher"),
			_ => Loc.T("tab.wikiStardew")
		};

		tabWalkthroughs.Text = GameProfiles.BaseId(game) switch
		{
			"SkyrimSE" => Loc.T("tab.walkSkyrim"),
			"Fallout4" => Loc.T("tab.walkFallout"),
			"MoonlightPeaks" => Loc.T("tab.walkMoonlight"),
			"Witcher3" => Loc.T("tab.walkWitcher"),
			_ => Loc.T("tab.walkStardew")
		};

		tabGameLog.Text = GameProfiles.BaseId(game) switch
		{
			"SkyrimSE" => Loc.T("tab.logsSkyrim"),
			"Fallout4" => Loc.T("tab.logsFallout"),
			"MoonlightPeaks" => Loc.T("tab.logsMoonlight"),
			"Witcher3" => Loc.T("tab.logsWitcher"),
			_ => Loc.T("tab.gameLog")
		};

		if (txtWikiSearch != null)
		{
			txtWikiSearch.AccessibleName = GameProfiles.BaseId(game) switch
			{
				"SkyrimSE" => Loc.T("ui.searchWikiSkyrim"),
				"Fallout4" => Loc.T("ui.searchWikiFallout"),
				"MoonlightPeaks" => Loc.T("ui.searchWikiMoonlight"),
				"Witcher3" => Loc.T("ui.searchWikiWitcher"),
				_ => Loc.T("ui.searchWikiStardew")
			};
		}

		if (GameProfiles.IsGame(game, GameProfiles.StardewValley))
		{
			if (!mainTabs.TabPages.Contains(tabSmapiLog))
				mainTabs.TabPages.Add(tabSmapiLog);
		}
		else
		{
			if (mainTabs.TabPages.Contains(tabSmapiLog))
				mainTabs.TabPages.Remove(tabSmapiLog);
		}

		// The Mod Priority, Plugin Order, and Creations tabs apply only to Skyrim SE / Fallout 4, and sit
		// right after the Installed tab (indexes 1, 2, 3) so the load order is next to the mod list.
		if (GameProfiles.IsAnyGame(game, GameProfiles.SkyrimSE, GameProfiles.Fallout4))
		{
			if (!mainTabs.TabPages.Contains(tabModPriority))
				mainTabs.TabPages.Insert(1, tabModPriority);
			if (!mainTabs.TabPages.Contains(tabPluginOrder))
				mainTabs.TabPages.Insert(2, tabPluginOrder);
			if (!mainTabs.TabPages.Contains(tabCreations))
				mainTabs.TabPages.Insert(3, tabCreations);
		}
		else
		{
			if (mainTabs.TabPages.Contains(tabModPriority))
				mainTabs.TabPages.Remove(tabModPriority);
			if (mainTabs.TabPages.Contains(tabPluginOrder))
				mainTabs.TabPages.Remove(tabPluginOrder);
			if (mainTabs.TabPages.Contains(tabCreations))
				mainTabs.TabPages.Remove(tabCreations);
		}

		// The Log tab sits at the end (parallel to Stardew's SMAPI Log tab). Every game whose loader keeps a log
		// the manager can read gets it: the script-extender log for Skyrim/Fallout 4, BepInEx's LogOutput.log for
		// Moonlight Peaks. Stardew has its own SMAPI Log tab instead.
		if (GameHasLogTab(game))
		{
			if (!mainTabs.TabPages.Contains(tabGameLog))
				mainTabs.TabPages.Add(tabGameLog);
		}
		else
		{
			if (mainTabs.TabPages.Contains(tabGameLog))
				mainTabs.TabPages.Remove(tabGameLog);
		}

		RefreshWikiCategories();
		PopulateWalkthroughs();
		PopulateModWikis();
		// Load the (now reset) active wiki's live categories; splitWiki already exists on a game switch.
		Fire(RefreshCategoriesForActiveWikiAsync(), "RefreshCategoriesForActiveWikiAsync");
		// Refresh the Discovery language and category lists for the new game (both, and their counts, are
		// game-specific — Skyrim's categories are nothing like Stardew Valley's).
		Fire(PopulateDiscoveryLanguagesAsync(), "PopulateDiscoveryLanguagesAsync");
		Fire(PopulateDiscoveryCategoriesAsync(), "PopulateDiscoveryCategoriesAsync");

		if (webViewWiki.CoreWebView2 != null)
		{
			webViewWiki.CoreWebView2.Navigate(CurrentWikiBaseUrl);
		}

		if (webViewWalkthrough.CoreWebView2 != null)
		{
			if (listWalkthroughs.SelectedItem is WalkthroughGuide guide)
			{
				webViewWalkthrough.CoreWebView2.Navigate(guide.Url);
			}
			else
			{
				webViewWalkthrough.CoreWebView2.Navigate("about:blank");
			}
		}

		mainTabs.SelectedIndex = 0;
		Speak(Loc.T("session.switched", gameName));

		// Focus is NOT taken here. Doing so made the screen reader announce the tab — "Installed Mods selected" —
		// while "Connecting to Nexus…" was still in flight, so the tab landed in the middle of the sequence
		// instead of at the end of it. It is taken by LandOnFirstTab once the refresh has finished, which is
		// after "Connected as …" and is the point at which the tab is actually worth landing on.
		//
		// The switch itself is the user's request, so the courtesy that leaves focus alone when they are already
		// moving around starts again from here: only wandering off DURING this load should cancel the landing.
		_userMovedSinceLoadBegan = false;
		_landOnFirstTabWhenReady = true;
		_landShouldSpeak = false;   // focus moves onto the strip here, and the reader announces that itself
		RefreshAllData(checkUpdates: _settings.CheckForUpdatesAtStartup);

		// A BepInEx game with no BepInEx installed loads none of its mods and says nothing about it in-game, so
		// the session says so here instead. Checked once per loaded session, after the data refresh so the mod
		// list is already on screen.
		_bepInExCheckedThisSession = false;
		Fire(CheckBepInExForSessionAsync(), "CheckBepInExForSessionAsync");
	}

	private void CloseGameSession()
	{
		SwitchActiveGame("None");
	}

	private void UpdateMenuState()
	{
		bool hasGame = _settings.ActiveGame != "None";
		if (_menuCloseSessionItem != null)
		{
			_menuCloseSessionItem.Visible = hasGame;
		}
		if (_menuCloseSeparator != null)
		{
			_menuCloseSeparator.Visible = hasGame;
		}
		// Enable/disable menus by stable Name (set in SetupAccessibleUI) rather than visible text, so this
		// keeps working once the UI is localized.
		if (MainMenuStrip != null)
		{
			if (MainMenuStrip.Items["menuMods"] is ToolStripMenuItem modsMenu) modsMenu.Enabled = hasGame;
			if (MainMenuStrip.Items["menuView"] is ToolStripMenuItem viewMenu) viewMenu.Enabled = hasGame;
			if (MainMenuStrip.Items["menuFile"] is ToolStripMenuItem fileMenu)
			{
				if (fileMenu.DropDownItems["menuRefreshAll"] is ToolStripItem refreshAll) refreshAll.Enabled = hasGame;
				if (fileMenu.DropDownItems["menuRefreshInstalled"] is ToolStripItem refreshInstalled) refreshInstalled.Enabled = hasGame;
			}
		}
	}

	private void InitializeGameSelectionPanel()
	{
		_gameSelectionPanel = new Panel
		{
			Dock = DockStyle.Fill,
			Visible = false,
			BackColor = Color.FromArgb(248, 250, 252) // Light slate gray background
		};

		TableLayoutPanel selectionLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 5,
			ColumnCount = 1,
			Padding = new Padding(20)
		};
		selectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 20f)); // Top spacer
		selectionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // Title
		selectionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // Prompt/Instruction
		selectionLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // List box
		selectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 80f)); // Bottom spacer/buttons

		Label lblTitle = new Label
		{
			Text = Loc.T("ui.appTitle"),
			Font = new Font("Segoe UI", 26f, FontStyle.Bold),
			ForeColor = Color.FromArgb(15, 23, 42), // Slate-900
			TextAlign = ContentAlignment.MiddleCenter,
			AutoSize = true,
			Anchor = AnchorStyles.None,
			Margin = new Padding(0, 0, 0, 10)
		};

		Label lblPrompt = new Label
		{
			Text = Loc.T("session.selectPrompt"),
			Font = new Font("Segoe UI", 14f, FontStyle.Regular),
			ForeColor = Color.FromArgb(71, 85, 105), // Slate-600
			TextAlign = ContentAlignment.MiddleCenter,
			AutoSize = true,
			Anchor = AnchorStyles.None,
			Margin = new Padding(0, 0, 0, 20)
		};

		_lstGames = new ListBox
		{
			Font = new Font("Segoe UI", 16f),
			Width = 400,
			Height = 150,
			Anchor = AnchorStyles.None,
			AccessibleName = Loc.T("session.selectGameList"),
			AccessibleDescription = Loc.T("session.selectGameDesc")
		};
		// Listed alphabetically.
		foreach (string name in GameProfiles.AllDisplayNames) _lstGames.Items.Add(name);
		WireAccessibleDialogList(_lstGames);
		_lstGames.SelectedIndex = 0;

		FlowLayoutPanel buttonLayout = new FlowLayoutPanel
		{
			FlowDirection = FlowDirection.LeftToRight,
			Anchor = AnchorStyles.None,
			AutoSize = true,
			Margin = new Padding(0, 20, 0, 0)
		};

		Button btnConfirm = new Button
		{
			Text = Loc.T("session.selectGame"),
			Font = new Font("Segoe UI", 12f, FontStyle.Bold),
			Width = 180,
			Height = 45,
			BackColor = Color.FromArgb(37, 99, 235), // Primary blue
			ForeColor = Color.White,
			FlatStyle = FlatStyle.Flat,
			AccessibleName = Loc.T("session.selectGame")
		};
		btnConfirm.FlatAppearance.BorderSize = 0;
		btnConfirm.Click += delegate
		{
			ConfirmGameSelection();
		};

		Button btnExit = new Button
		{
			Text = Loc.T("menu.exit"),
			Font = new Font("Segoe UI", 12f, FontStyle.Regular),
			Width = 120,
			Height = 45,
			BackColor = Color.FromArgb(226, 232, 240), // Light gray
			ForeColor = Color.FromArgb(71, 85, 105),
			FlatStyle = FlatStyle.Flat,
			AccessibleName = Loc.T("session.exitManager")
		};
		btnExit.FlatAppearance.BorderSize = 0;
		btnExit.Click += delegate
		{
			Application.Exit();
		};

		buttonLayout.Controls.Add(btnConfirm);
		buttonLayout.Controls.Add(btnExit);

		_lstGames.KeyDown += delegate(object? sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Return)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				ConfirmGameSelection();
			}
			else if (e.KeyCode == Keys.Escape)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				Application.Exit();
			}
		};

		_lstGames.DoubleClick += delegate
		{
			ConfirmGameSelection();
		};

		selectionLayout.Controls.Add(lblTitle, 0, 1);
		selectionLayout.Controls.Add(lblPrompt, 0, 2);
		selectionLayout.Controls.Add(_lstGames, 0, 3);
		selectionLayout.Controls.Add(buttonLayout, 0, 4);

		_gameSelectionPanel.Controls.Add(selectionLayout);
	}

	private void ConfirmGameSelection()
	{
		if (_lstGames.SelectedItem == null) return;
		string selection = _lstGames.SelectedItem.ToString() ?? "";
		string? gameId = GameProfiles.IdForDisplayName(selection);
		if (gameId == null) return;

		SwitchActiveGame(gameId);
	}
}
