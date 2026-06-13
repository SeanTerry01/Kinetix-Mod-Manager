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

		string gameName = game switch
		{
			"SkyrimSE" => "Skyrim Special Edition",
			"Fallout4" => "Fallout 4",
			"StardewValley" => "Stardew Valley",
			_ => ""
		};
		Text = string.IsNullOrEmpty(gameName) ? "Kinetix Mod Manager" : $"{gameName} Kinetix Mod Manager";

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
			Speak("Game session closed. Returned to game selection screen.");
			return;
		}

		if (MainMenuStrip != null)
		{
			foreach (ToolStripItem item in MainMenuStrip.Items)
			{
				if (item is ToolStripMenuItem subMenu && subMenu.Text == "&Mods")
				{
					foreach (ToolStripItem subItem in subMenu.DropDownItems)
					{
						if (subItem != null && subItem.Text != null)
						{
							if (subItem.Text.StartsWith("Launch "))
							{
								subItem.Text = $"Launch {gameName} (" + GetShortcutString("LaunchGame") + ")";
							}
							else if (subItem.Text.Contains("Accessibility Suite"))
							{
								subItem.Text = $"Install {gameName} Accessibility Suite";
							}
						}
					}
				}
			}
		}

		tabWiki.Text = game switch
		{
			"SkyrimSE" => "Skyrim Wiki",
			"Fallout4" => "Fallout 4 Wiki",
			_ => "Stardew Wiki"
		};

		tabWalkthroughs.Text = game switch
		{
			"SkyrimSE" => "Skyrim Walkthroughs",
			"Fallout4" => "Fallout 4 Walkthroughs",
			_ => "Stardew Walkthroughs"
		};

		if (txtWikiSearch != null)
		{
			txtWikiSearch.AccessibleName = game switch
			{
				"SkyrimSE" => "Search Skyrim Wiki",
				"Fallout4" => "Search Fallout 4 Wiki",
				_ => "Search Stardew Wiki"
			};
		}

		if (game == "StardewValley")
		{
			if (!mainTabs.TabPages.Contains(tabSmapiLog))
				mainTabs.TabPages.Add(tabSmapiLog);
		}
		else
		{
			if (mainTabs.TabPages.Contains(tabSmapiLog))
				mainTabs.TabPages.Remove(tabSmapiLog);
		}

		RefreshWikiCategories();
		PopulateWalkthroughs();
		PopulateModWikis();
		// Load the (now reset) active wiki's live categories; splitWiki already exists on a game switch.
		_ = RefreshCategoriesForActiveWikiAsync();
		// Refresh the Discovery language list for the new game (languages and counts are game-specific).
		_ = PopulateDiscoveryLanguagesAsync();

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
		mainTabs.Focus();
		Speak($"Switched to {gameName}. Loading mod list.");
		RefreshAllData(checkUpdates: _settings.CheckForUpdatesAtStartup);
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
		if (MainMenuStrip != null)
		{
			foreach (ToolStripItem item in MainMenuStrip.Items)
			{
				if (item is ToolStripMenuItem menu)
				{
					if (menu.Text == "&Mods" || menu.Text == "&View")
					{
						menu.Enabled = hasGame;
					}
					else if (menu.Text == "&File")
					{
						foreach (ToolStripItem subItem in menu.DropDownItems)
						{
							if (subItem != null && subItem.Text != null)
							{
								if (subItem.Text.StartsWith("Refresh"))
								{
									subItem.Enabled = hasGame;
								}
							}
						}
					}
				}
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
			Text = "Kinetix Mod Manager",
			Font = new Font("Segoe UI", 26f, FontStyle.Bold),
			ForeColor = Color.FromArgb(15, 23, 42), // Slate-900
			TextAlign = ContentAlignment.MiddleCenter,
			AutoSize = true,
			Anchor = AnchorStyles.None,
			Margin = new Padding(0, 0, 0, 10)
		};

		Label lblPrompt = new Label
		{
			Text = "Select a game to manage:",
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
			AccessibleName = "Select Game List",
			AccessibleDescription = "Choose Stardew Valley, Skyrim Special Edition, or Fallout 4 to manage."
		};
		// Listed alphabetically.
		_lstGames.Items.Add("Fallout 4");
		_lstGames.Items.Add("Skyrim Special Edition");
		_lstGames.Items.Add("Stardew Valley");
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
			Text = "Select Game",
			Font = new Font("Segoe UI", 12f, FontStyle.Bold),
			Width = 180,
			Height = 45,
			BackColor = Color.FromArgb(37, 99, 235), // Primary blue
			ForeColor = Color.White,
			FlatStyle = FlatStyle.Flat,
			AccessibleName = "Select Game"
		};
		btnConfirm.FlatAppearance.BorderSize = 0;
		btnConfirm.Click += delegate
		{
			ConfirmGameSelection();
		};

		Button btnExit = new Button
		{
			Text = "Exit",
			Font = new Font("Segoe UI", 12f, FontStyle.Regular),
			Width = 120,
			Height = 45,
			BackColor = Color.FromArgb(226, 232, 240), // Light gray
			ForeColor = Color.FromArgb(71, 85, 105),
			FlatStyle = FlatStyle.Flat,
			AccessibleName = "Exit Manager"
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
		string gameId = selection switch
		{
			"Skyrim Special Edition" => "SkyrimSE",
			"Fallout 4" => "Fallout4",
			_ => "StardewValley"
		};

		SwitchActiveGame(gameId);
	}
}
