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

/// <summary>The Settings dialog for Form1.</summary>
public partial class Form1
{
	/// <summary>
	/// Opens the Settings dialog (Ctrl+P) where the user can set the Mods path, API key,
	/// audio theme, volume, and other preferences. Changes are validated before saving.
	/// </summary>
	private void ShowSettings()
	{
		if (_isSettingsOpen)
		{
			return;
		}
		_isSettingsOpen = true;

		// True once Save has run, so the "changes cancelled" announcement on the way out knows to stay quiet.
		bool saved = false;

		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("settings.title"), (container, closeView) =>
		{
		// Settings are grouped into tabs to keep the dialog readable as it grows. Each tab is a single-column
		// TableLayoutPanel (same layout mechanics the dialog used when it was one long table), and the Save/Cancel
		// buttons live outside the tabs so they're always reachable. Tab order within a tab follows add order.
		// The strip is silent rather than unnamed: an unnamed container borrows a name from whatever sits behind it,
		// which is how this one once announced itself as "Search". See SilentAccessibleName; the tab names themselves
		// are announced by AccessibleTabControl.
		TabControl tabs = new AccessibleTabControl { Dock = DockStyle.Fill, AccessibleName = SilentAccessibleName };
		TableLayoutPanel NewTab(string title)
		{
			var page = new TabPage(title);
			var tlp = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, AutoScroll = true };
			page.Controls.Add(tlp);
			tabs.TabPages.Add(page);
			return tlp;
		}
		TableLayoutPanel tabPaths   = NewTab(Loc.T("settings.tabPaths"));
		TableLayoutPanel tabStartup = NewTab(Loc.T("settings.tabStartup"));
		TableLayoutPanel tabAudio   = NewTab(Loc.T("settings.tabAudio"));
		TableLayoutPanel tabDisplay = NewTab(Loc.T("settings.tabDisplay"));
		TableLayoutPanel tabMods    = NewTab(Loc.T("settings.tabMods"));
		TableLayoutPanel tabAi      = NewTab(Loc.T("settings.tabAi"));
		TableLayoutPanel tabLang    = NewTab(Loc.T("settings.tabLanguage"));
		int pr = 0, sr = 0, ar = 0, dr = 0, mr = 0, lr = 0, air = 0; // per-tab row counters

		tabPaths.Controls.Add(new Label
		{
			Text = Loc.T("settings.configurePaths") + ":",
			AutoSize = true
		}, 0, pr++);

		ComboBox cmbSettingsGame = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 350,
			Font = new Font("Segoe UI", 10f),
			AccessibleName = Loc.T("settings.configurePaths")
		};
		// One entry per COPY, not per game, so someone who owns Skyrim twice can set each copy's folders. A game
		// with no copy recorded still gets an entry under its own name — typing the folder in by hand is how a
		// user whose install detection missed (under Wine, say) gets started, and that must keep working.
		var pathTargets = new List<(string Label, string Key)>();
		foreach (GameProfile profile in GameProfiles.All)
		{
			var copies = _settings.InstallsOf(profile.Id);
			if (copies.Count == 0)
			{
				pathTargets.Add((profile.DisplayName, profile.Id));
				continue;
			}
			bool label = copies.Count > 1;
			foreach (GameInstall copy in copies) pathTargets.Add((copy.DisplayName(label), copy.Key));
		}

		cmbSettingsGame.Items.AddRange(pathTargets.Select(t => t.Label).ToArray());
		int activeTarget = pathTargets.FindIndex(t => t.Key == _settings.ActiveGame);
		if (activeTarget < 0) activeTarget = pathTargets.FindIndex(t => GameProfiles.IsGame(t.Key, GameProfiles.StardewValley));
		cmbSettingsGame.SelectedIndex = Math.Max(0, activeTarget);
		tabPaths.Controls.Add(cmbSettingsGame, 0, pr++);

		tabPaths.Controls.Add(new Label
		{
			Text = Loc.T("settings.modsPath") + ":",
			AutoSize = true,
			Padding = new Padding(0, 10, 0, 0)
		}, 0, pr++);

		Panel panelMods = new Panel
		{
			Dock = DockStyle.Fill,
			Height = 35
		};
		TextBox tPath = new TextBox
		{
			Width = 350,
			Font = new Font("Segoe UI", 10f),
			AccessibleName = Loc.T("settings.modsPath")
		};
		Button btnBrowseMods = new Button
		{
			Text = Loc.T("common.browse"),
			Left = 360,
			Width = 80
		};
		btnBrowseMods.Click += delegate
		{
			using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
			if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
			{
				tPath.Text = folderBrowserDialog.SelectedPath;
			}
		};
		panelMods.Controls.AddRange(tPath, btnBrowseMods);
		tabPaths.Controls.Add(panelMods, 0, pr++);

		Label lblGamePath = new Label
		{
			Text = Loc.T("settings.gamePath") + ":",
			AutoSize = true,
			Padding = new Padding(0, 10, 0, 0)
		};
		tabPaths.Controls.Add(lblGamePath, 0, pr++);

		Panel panelGame = new Panel
		{
			Dock = DockStyle.Fill,
			Height = 35
		};
		TextBox tGamePath = new TextBox
		{
			Width = 350,
			Font = new Font("Segoe UI", 10f),
			AccessibleName = Loc.T("settings.gamePath")
		};
		Button btnBrowseGame = new Button
		{
			Text = Loc.T("common.browse"),
			Left = 360,
			Width = 80
		};
		btnBrowseGame.Click += delegate
		{
			using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
			if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
			{
				tGamePath.Text = folderBrowserDialog.SelectedPath;
			}
		};
		panelGame.Controls.AddRange(tGamePath, btnBrowseGame);
		tabPaths.Controls.Add(panelGame, 0, pr++);

		// Where this copy's mods are staged. Only the games that stage mods outside themselves have a choice to
		// make here — every other game's loader reads a fixed folder inside the install, so there is nothing to
		// decide. Acting on it immediately (rather than on Save) is deliberate: it moves files, so it needs its
		// own confirmation and its own spoken result, which a Save button covering a dozen settings cannot give.
		CheckBox cModsInGame = new CheckBox
		{
			Text = Loc.T("settings.modsInGameFolder"),
			AutoSize = true,
			AccessibleName = Loc.T("settings.modsInGameFolder"),
			AccessibleDescription = Loc.T("settings.modsInGameFolderDesc")
		};
		tabPaths.Controls.Add(cModsInGame, 0, pr++);

		var tempModsPaths = new Dictionary<string, string>(_settings.GameModsPaths);
		var tempGamePaths = new Dictionary<string, string>(_settings.GamePaths);
		string currentEditingGame = pathTargets[cmbSettingsGame.SelectedIndex].Key;

		tPath.Text = tempModsPaths.TryGetValue(currentEditingGame, out string? initialMods) ? initialMods : "";
		tGamePath.Text = tempGamePaths.TryGetValue(currentEditingGame, out string? initialGame) ? initialGame : "";

		// Set while the checkbox is being brought in line with the selected copy, so re-selecting a copy doesn't
		// read as the user asking to move that copy's mods.
		bool syncingModsInGame = false;

		// Stardew Valley is the one game whose mods path implies its game folder (the Mods folder sits inside the
		// install), so it alone hides the separate game-folder field.
		Action updateVisibility = () =>
		{
			bool isStardew = GameProfiles.IsGame(currentEditingGame, GameProfiles.StardewValley);
			lblGamePath.Visible = !isStardew;
			panelGame.Visible = !isStardew;

			GameInstall? copy = _settings.InstallFor(currentEditingGame);
			GameProfile? profile = GameProfiles.Find(currentEditingGame);
			bool stages = profile != null && !string.IsNullOrEmpty(profile.StagingFolderName);

			syncingModsInGame = true;
			cModsInGame.Visible = stages;
			cModsInGame.Enabled = stages && copy != null;
			cModsInGame.Checked = copy?.ModsInGameFolder ?? false;
			syncingModsInGame = false;
		};
		updateVisibility();

		cModsInGame.CheckedChanged += delegate
		{
			if (syncingModsInGame) return;
			// The move needs the copy's game folder, and the field may hold an edit that hasn't been saved yet.
			tempGamePaths[currentEditingGame] = tGamePath.Text.Trim();
			if (!TryMoveModsFolder(currentEditingGame, cModsInGame.Checked, tempGamePaths[currentEditingGame]))
			{
				// Declined or failed — put the box back where it was without re-triggering this handler.
				syncingModsInGame = true;
				cModsInGame.Checked = !cModsInGame.Checked;
				syncingModsInGame = false;
				return;
			}
			tempModsPaths[currentEditingGame] = _settings.GameModsPaths[currentEditingGame];
			tPath.Text = tempModsPaths[currentEditingGame];
		};

		cmbSettingsGame.SelectedIndexChanged += delegate
		{
			string lastGameKey = currentEditingGame;
			tempModsPaths[lastGameKey] = tPath.Text.Trim();
			tempGamePaths[lastGameKey] = tGamePath.Text.Trim();

			int index = cmbSettingsGame.SelectedIndex;
			if (index < 0 || index >= pathTargets.Count) return;
			string newGameKey = pathTargets[index].Key;

			currentEditingGame = newGameKey;
			tPath.Text = tempModsPaths.TryGetValue(newGameKey, out string? p) ? p : "";
			tGamePath.Text = tempGamePaths.TryGetValue(newGameKey, out string? gp) ? gp : "";

			updateVisibility();
			Speak(Loc.T("settings.editingPaths", pathTargets[index].Label));
		};

		// The Nexus key is only shown to people it can do something for.
		//
		// Not every supported game uses Nexus — Minecraft's mods come from Modrinth — and a key is useless for
		// those. Worse than useless, before this: an empty key blocked the whole Save, so somebody who only
		// plays Minecraft could not change their sound volume without first pasting a Nexus key they will never
		// use. The key is asked for when the loaded game actually needs it, and not before.
		GameProfile? sessionGame = GameProfiles.Find(_settings.ActiveGame);
		bool sessionUsesNexus = sessionGame is null || sessionGame.ModSource == ModSource.Nexus;
		bool keyIsRequired = sessionGame is not null && sessionGame.ModSource == ModSource.Nexus;

		Label lblKey = new Label
		{
			Text = Loc.T("settings.apiKey") + ":",
			AutoSize = true,
			Padding = new Padding(0, 10, 0, 0),
			Visible = sessionUsesNexus
		};
		tabPaths.Controls.Add(lblKey, 0, pr++);

		TextBox tKey = new TextBox
		{
			Text = _settings.ApiKey,
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 10f),
			AccessibleName = Loc.T("settings.apiKey"),
			Visible = sessionUsesNexus,
			// Said on the box itself rather than as a label beside it: a label is only found by someone already
			// browsing for it, and the point is to reassure whoever has just landed on the field and is
			// wondering whether they have to fill it in.
			AccessibleDescription = sessionGame is null ? Loc.T("settings.apiKeySkipHint") : ""
		};
		tabPaths.Controls.Add(tKey, 0, pr++);

		// A game that does not use Nexus says so once, in place of the field, rather than leaving a gap.
		if (!sessionUsesNexus)
		{
			tabPaths.Controls.Add(new Label
			{
				Text = Loc.T("settings.apiKeyNotNeeded", sessionGame!.DisplayName),
				AutoSize = true,
				Padding = new Padding(0, 10, 0, 0)
			}, 0, pr++);
		}

		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight,
			Padding = new Padding(0, 10, 0, 0),
			AutoSize = true
		};
		CheckBox cSplash = new CheckBox
		{
			Text = Loc.T("settings.showSplash"),
			Checked = _settings.ShowSplashScreen,
			AutoSize = true,
			AccessibleName = Loc.T("settings.showSplashName")
		};
		CheckBox cRandomLogo = new CheckBox
		{
			Text = Loc.T("settings.randomLogo"),
			Checked = _settings.RandomLogoStartup,
			AutoSize = true,
			AccessibleName = Loc.T("settings.randomLogoName"),
			Visible = _settings.ShowSplashScreen
		};
		CheckBox cUpdates = new CheckBox
		{
			Text = Loc.T("settings.checkUpdates"),
			Checked = _settings.CheckForUpdatesAtStartup,
			AutoSize = true,
			AccessibleName = Loc.T("settings.checkUpdatesName")
		};
		CheckBox cManagerUpdates = new CheckBox
		{
			Text = Loc.T("settings.checkManagerUpdates"),
			Checked = _settings.CheckForManagerUpdatesAtStartup,
			AutoSize = true,
			AccessibleName = Loc.T("settings.checkManagerUpdatesName")
		};
		cManagerUpdates.CheckedChanged += delegate
		{
			Speak(cManagerUpdates.Checked ? Loc.T("settings.managerUpdatesOn") : Loc.T("settings.managerUpdatesOff"));
		};
		cSplash.CheckedChanged += delegate
		{
			Speak(cSplash.Checked ? Loc.T("settings.splashOn") : Loc.T("settings.splashOff"));
		};
		cRandomLogo.CheckedChanged += delegate
		{
			Speak(cRandomLogo.Checked ? Loc.T("settings.randomLogoOn") : Loc.T("settings.randomLogoOff"));
		};
		cUpdates.CheckedChanged += delegate
		{
			Speak(cUpdates.Checked ? Loc.T("settings.updatesOn") : Loc.T("settings.updatesOff"));
		};
		flowLayoutPanel.Controls.AddRange(cSplash, cUpdates, cManagerUpdates);
		tabStartup.Controls.Add(flowLayoutPanel, 0, sr++);

		// Spoken-message toggles: the startup welcome/hint and the shutdown goodbye. Both default on.
		CheckBox cStartupMsg = new CheckBox
		{
			Text = Loc.T("settings.speakStartup"),
			Checked = _settings.SpeakStartupMessage,
			AutoSize = true,
			Padding = new Padding(0, 8, 0, 0),
			AccessibleName = Loc.T("settings.speakStartupName")
		};
		CheckBox cShutdownMsg = new CheckBox
		{
			Text = Loc.T("settings.speakShutdown"),
			Checked = _settings.SpeakShutdownMessage,
			AutoSize = true,
			Padding = new Padding(0, 4, 0, 0),
			AccessibleName = Loc.T("settings.speakShutdownName")
		};
		cStartupMsg.CheckedChanged += delegate
		{
			Speak(cStartupMsg.Checked ? Loc.T("settings.speakStartupOn") : Loc.T("settings.speakStartupOff"));
		};
		cShutdownMsg.CheckedChanged += delegate
		{
			Speak(cShutdownMsg.Checked ? Loc.T("settings.speakShutdownOn") : Loc.T("settings.speakShutdownOff"));
		};
		tabStartup.Controls.Add(cStartupMsg, 0, sr++);
		tabStartup.Controls.Add(cShutdownMsg, 0, sr++);

		// Suppress the logo audio preview while the list is repopulated programmatically (e.g.
		// when the theme changes or the manual-theme box is unchecked); only a deliberate logo
		// selection or Space press should play a logo.
		bool suppressLogoPreview = false;
		ComboBox cmbLogo = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 300,
			AccessibleName = Loc.T("settings.selectLogo"),
			Visible = _settings.ShowSplashScreen
		};
		cmbLogo.SelectedIndexChanged += delegate
		{
			// Only a deliberate change previews; repopulating the list sets suppressLogoPreview while it works.
			if (!suppressLogoPreview)
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
		Label lblSelectLogo = new Label
		{
			Text = Loc.T("settings.selectLogo") + ":",
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0),
			Visible = _settings.ShowSplashScreen
		};
		// Master switch for the manager's UI sound effects. Sits first on the tab; when unchecked every other
		// audio control except the download/install feedback selector is hidden (that feedback is governed
		// separately by ProgressFeedback and stays available even with UI sounds off).
		CheckBox cEnableSounds = new CheckBox
		{
			Text = Loc.T("settings.enableSounds"),
			Checked = _settings.EnableUiSounds,
			AutoSize = true,
			AccessibleName = Loc.T("settings.enableSoundsName")
		};
		tabAudio.Controls.Add(cEnableSounds, 0, ar++);

		// The logo controls only matter when the splash screen is shown (it's what plays the logo sound), so they
		// appear only while "Show Splash Screen" is checked and follow it live. Random comes before the selector.
		tabAudio.Controls.Add(cRandomLogo, 0, ar++);
		tabAudio.Controls.Add(lblSelectLogo, 0, ar++);
		tabAudio.Controls.Add(cmbLogo, 0, ar++);
		cSplash.CheckedChanged += delegate
		{
			// Logo controls require both UI sounds and the splash screen to be enabled.
			bool logo = cEnableSounds.Checked && cSplash.Checked;
			cRandomLogo.Visible = logo;
			lblSelectLogo.Visible = logo;
			cmbLogo.Visible = logo;
		};

		FlowLayoutPanel flowLayoutPanel2 = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight
		};
		flowLayoutPanel2.Controls.Add(new Label
		{
			Text = Loc.T("settings.volume"),
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		});
		// A dropdown rather than a NumericUpDown spinner: the WinForms spinner announces its name twice to screen
		// readers (the control and its inner edit box both report it) and there's no reliable way to suppress it.
		ComboBox nVol = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 70,
			AccessibleName = Loc.T("settings.volumeName")
		};
		for (int v = 0; v <= 100; v++) nVol.Items.Add(v);
		nVol.SelectedItem = Math.Clamp(_settings.SoundVolume, 0, 100);
		if (nVol.SelectedIndex < 0) nVol.SelectedIndex = nVol.Items.Count - 1;
		flowLayoutPanel2.Controls.Add(nVol);
		tabAudio.Controls.Add(flowLayoutPanel2, 0, ar++);

		// --- Display (low-vision) tab: high-contrast colours and larger text ---
		tabDisplay.Controls.Add(new Label { Text = Loc.T("settings.displayIntro"), AutoSize = true }, 0, dr++);

		tabDisplay.Controls.Add(new Label
		{
			Text = Loc.T("settings.contrast") + ":",
			AutoSize = true,
			Padding = new Padding(0, 10, 0, 0)
		}, 0, dr++);
		var contrastOptions = new[] { DisplayContrast.Off, DisplayContrast.WhiteOnBlack, DisplayContrast.YellowOnBlack, DisplayContrast.BlackOnYellow };
		ComboBox cmbContrast = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 260,
			AccessibleName = Loc.T("settings.contrastName")
		};
		foreach (DisplayContrast opt in contrastOptions) cmbContrast.Items.Add(Loc.T("settings.contrast." + opt));
		cmbContrast.SelectedIndex = Math.Max(0, Array.IndexOf(contrastOptions, _settings.DisplayContrast));
		tabDisplay.Controls.Add(cmbContrast, 0, dr++);

		tabDisplay.Controls.Add(new Label
		{
			Text = Loc.T("settings.textSize") + ":",
			AutoSize = true,
			Padding = new Padding(0, 10, 0, 0)
		}, 0, dr++);
		var textSizeOptions = new[] { TextSize.Normal, TextSize.Large, TextSize.ExtraLarge };
		ComboBox cmbTextSize = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 260,
			AccessibleName = Loc.T("settings.textSizeName")
		};
		foreach (TextSize opt in textSizeOptions) cmbTextSize.Items.Add(Loc.T("settings.textSize." + opt));
		cmbTextSize.SelectedIndex = Math.Max(0, Array.IndexOf(textSizeOptions, _settings.TextSize));
		tabDisplay.Controls.Add(cmbTextSize, 0, dr++);

		FlowLayoutPanel flowLayoutPanel3 = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight
		};
		flowLayoutPanel3.Controls.Add(new Label
		{
			Text = Loc.T("settings.maxBackups"),
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		});
		ComboBox nPrune = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 70,
			AccessibleName = Loc.T("settings.maxBackupsName")
		};
		for (int v = 1; v <= 50; v++) nPrune.Items.Add(v);
		nPrune.SelectedItem = Math.Clamp(_settings.MaxBackupsPerMod, 1, 50);
		if (nPrune.SelectedIndex < 0) nPrune.SelectedIndex = 0;
		flowLayoutPanel3.Controls.Add(nPrune);
		tabMods.Controls.Add(flowLayoutPanel3, 0, mr++);

		FlowLayoutPanel flowLayoutPanel4 = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight
		};
		flowLayoutPanel4.Controls.Add(new Label
		{
			Text = Loc.T("settings.currentTheme"),
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		});
		ComboBox cTheme = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 150
		};
		cTheme.Items.AddRange(SoundThemes.Installed(themesPath).Cast<object>().ToArray());
		cTheme.SelectedItem = _settings.CurrentTheme;
		cTheme.SelectedIndexChanged += delegate
		{
			RefreshLogoList(cTheme.SelectedItem?.ToString() ?? "Default");
		};

		// When unchecked, the sound theme is decided entirely by the loaded game and the theme
		// dropdown is disabled. When checked, the dropdown selection is honoured and persists.
		CheckBox cManualTheme = new CheckBox
		{
			Text = Loc.T("settings.manualTheme"),
			AutoSize = true,
			Checked = _settings.AllowManualTheme,
			Padding = new Padding(10, 4, 0, 0),
			AccessibleName = Loc.T("settings.manualThemeName")
		};
		cTheme.Enabled = _settings.AllowManualTheme;
		cManualTheme.CheckedChanged += delegate
		{
			cTheme.Enabled = cManualTheme.Checked;
			if (!cManualTheme.Checked)
			{
				// Reverting to game-driven: reflect the active game's theme in the dropdown.
				string gameTheme = AppSettings.ThemeForGame(_settings.ActiveGame);
				if (cTheme.Items.Contains(gameTheme)) cTheme.SelectedItem = gameTheme;
				RefreshLogoList(gameTheme);
			}
			Speak(cManualTheme.Checked
				? Loc.T("settings.manualThemeOn")
				: Loc.T("settings.manualThemeOff"));
		};
		// Checkbox sits before the theme dropdown so it reads and tabs first.
		flowLayoutPanel4.Controls.Add(cManualTheme);
		flowLayoutPanel4.Controls.Add(cTheme);

		tabAudio.Controls.Add(flowLayoutPanel4, 0, ar++);
		RefreshLogoList(_settings.CurrentTheme);

		FlowLayoutPanel flowLang = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight,
			Padding = new Padding(0, 5, 0, 0),
			AutoSize = true
		};
		flowLang.Controls.Add(new Label
		{
			Text = Loc.T("settings.language") + ":",
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		});
		ComboBox cmbLanguage = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 220,
			AccessibleName = Loc.T("settings.language")
		};
		// "Automatic (follow Windows)" first, then every language file shipped in the lang/ folder.
		cmbLanguage.Items.Add(new LanguageChoice { Code = "", Display = Loc.T("settings.languageAuto") });
		foreach (LanguageChoice choice in Loc.AvailableLanguages())
			cmbLanguage.Items.Add(choice);
		cmbLanguage.SelectedIndex = 0;
		for (int i = 1; i < cmbLanguage.Items.Count; i++)
		{
			if (cmbLanguage.Items[i] is LanguageChoice lc &&
				lc.Code.Equals(_settings.Language, StringComparison.OrdinalIgnoreCase))
			{
				cmbLanguage.SelectedIndex = i;
				break;
			}
		}
		flowLang.Controls.Add(cmbLanguage);
		tabLang.Controls.Add(flowLang, 0, lr++);

		FlowLayoutPanel flowPageSize = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight,
			Padding = new Padding(0, 5, 0, 0),
			AutoSize = true
		};
		flowPageSize.Controls.Add(new Label
		{
			Text = Loc.T("settings.resultsPerLoad"),
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		});
		ComboBox cmbPageSize = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 70,
			AccessibleName = Loc.T("settings.resultsPerLoadName")
		};
		cmbPageSize.Items.AddRange(DiscoveryPageSizeOptions.Cast<object>().ToArray());
		cmbPageSize.SelectedItem = _settings.DiscoverySearchPageSize;
		if (cmbPageSize.SelectedIndex < 0) cmbPageSize.SelectedItem = 20;
		flowPageSize.Controls.Add(cmbPageSize);
		tabMods.Controls.Add(flowPageSize, 0, mr++);

		CheckBox cSearchHistory = new CheckBox
		{
			Text = Loc.T("settings.saveSearchHistory"),
			Checked = _settings.SaveSearchHistory,
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0),
			AccessibleName = Loc.T("settings.saveSearchHistoryName")
		};
		cSearchHistory.CheckedChanged += delegate
		{
			Speak(cSearchHistory.Checked ? Loc.T("settings.searchHistoryOn") : Loc.T("settings.searchHistoryOff"));
		};
		tabMods.Controls.Add(cSearchHistory, 0, mr++);

		// What to do when a browser download turns out to be for a game other than the loaded one. The file is
		// downloaded whichever way this is set — an nxm link's key expires within minutes, so only the install can
		// be deferred — and this decides whether the manager also leaves the session the user is in.
		FlowLayoutPanel flowCrossGame = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight,
			Padding = new Padding(0, 5, 0, 0),
			AutoSize = true
		};
		flowCrossGame.Controls.Add(new Label
		{
			Text = Loc.T("settings.crossGameDownloads"),
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		});
		ComboBox cmbCrossGame = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 260,
			AccessibleName = Loc.T("settings.crossGameDownloadsName"),
			AccessibleDescription = Loc.T("settings.crossGameDownloadsDesc")
		};
		cmbCrossGame.Items.AddRange(new object[]
		{
			new SettingChoice<CrossGameDownloadAction>(CrossGameDownloadAction.Ask,              Loc.T("settings.crossGameAsk")),
			new SettingChoice<CrossGameDownloadAction>(CrossGameDownloadAction.SwitchAndInstall, Loc.T("settings.crossGameSwitch")),
			new SettingChoice<CrossGameDownloadAction>(CrossGameDownloadAction.SaveForLater,     Loc.T("settings.crossGameSave"))
		});
		for (int i = 0; i < cmbCrossGame.Items.Count; i++)
		{
			if (cmbCrossGame.Items[i] is SettingChoice<CrossGameDownloadAction> cg && cg.Value == _settings.CrossGameDownloads)
			{
				cmbCrossGame.SelectedIndex = i;
				break;
			}
		}
		if (cmbCrossGame.SelectedIndex < 0) cmbCrossGame.SelectedIndex = 0;
		flowCrossGame.Controls.Add(cmbCrossGame);
		tabMods.Controls.Add(flowCrossGame, 0, mr++);

		// Archive invalidation (loose-file loading). The INI on disk is the source of truth: the box reflects the
		// current state and, on Save, only writes when the user changed it. Shown only for games where it applies
		// (Fallout 4); ArchiveInvalidationIniPath returns null otherwise, which hides the control.
		bool archiveGame = ModFileSystem.ArchiveInvalidationIniPath(_settings.ActiveGame, _settings.CurrentGamePath) != null;
		CheckBox cArchiveInvalidation = new CheckBox
		{
			Text = Loc.T("settings.archiveInvalidation"),
			Checked = archiveGame && ModFileSystem.IsArchiveInvalidationEnabled(_settings.ActiveGame, _settings.CurrentGamePath),
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0),
			AccessibleName = Loc.T("settings.archiveInvalidationName"),
			Visible = archiveGame
		};
		cArchiveInvalidation.CheckedChanged += delegate
		{
			Speak(cArchiveInvalidation.Checked ? Loc.T("settings.archiveInvalidationOn") : Loc.T("settings.archiveInvalidationOff"));
		};
		tabMods.Controls.Add(cArchiveInvalidation, 0, mr++);

		// Guard against the game rewriting its own plugins.txt (Skyrim SE / Fallout 4 deactivate Creations and
		// reorder plugins when a new game is started). Only meaningful for those two games, so hidden elsewhere.
		bool pluginGuardGame = GameProfiles.IsAnyGame(_settings.ActiveGame, GameProfiles.SkyrimSE, GameProfiles.Fallout4);
		CheckBox cProtectPlugins = new CheckBox
		{
			Text = Loc.T("settings.protectPluginOrder"),
			Checked = _settings.ProtectPluginOrder,
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0),
			AccessibleName = Loc.T("settings.protectPluginOrderName"),
			AccessibleDescription = Loc.T("settings.protectPluginOrderDesc"),
			Visible = pluginGuardGame
		};
		cProtectPlugins.CheckedChanged += delegate
		{
			Speak(cProtectPlugins.Checked ? Loc.T("settings.protectPluginOrderOn") : Loc.T("settings.protectPluginOrderOff"));
		};
		tabMods.Controls.Add(cProtectPlugins, 0, mr++);

		FlowLayoutPanel flowProgress = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight,
			Padding = new Padding(0, 5, 0, 0),
			AutoSize = true
		};
		flowProgress.Controls.Add(new Label
		{
			Text = Loc.T("settings.progressFeedback"),
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		});
		ComboBox cmbProgress = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 140,
			AccessibleName = Loc.T("settings.progressFeedbackName")
		};
		cmbProgress.Items.AddRange(new object[]
		{
			new ProgressFeedbackChoice(ProgressFeedback.Off,    Loc.T("progress.feedbackOff")),
			new ProgressFeedbackChoice(ProgressFeedback.Tones,  Loc.T("progress.feedbackTones")),
			new ProgressFeedbackChoice(ProgressFeedback.Speech, Loc.T("progress.feedbackSpeech")),
			new ProgressFeedbackChoice(ProgressFeedback.Both,   Loc.T("progress.feedbackBoth"))
		});
		for (int i = 0; i < cmbProgress.Items.Count; i++)
		{
			if (cmbProgress.Items[i] is ProgressFeedbackChoice pc && pc.Value == _settings.ProgressFeedback)
			{
				cmbProgress.SelectedIndex = i;
				break;
			}
		}
		if (cmbProgress.SelectedIndex < 0) cmbProgress.SelectedIndex = cmbProgress.Items.Count - 1;
		flowProgress.Controls.Add(cmbProgress);
		tabAudio.Controls.Add(flowProgress, 0, ar++);

		// Shows or hides the audio controls based on the master "Enable UI sounds" switch. The logo controls
		// additionally depend on the splash screen being enabled; the download/install feedback selector is left
		// alone because it has its own setting and applies even when UI sounds are off.
		void UpdateAudioVisibility()
		{
			bool sounds = cEnableSounds.Checked;
			bool logo = sounds && cSplash.Checked;
			cRandomLogo.Visible = logo;
			lblSelectLogo.Visible = logo;
			cmbLogo.Visible = logo;
			flowLayoutPanel2.Visible = sounds; // volume
			flowLayoutPanel4.Visible = sounds; // sound theme
		}
		cEnableSounds.CheckedChanged += delegate
		{
			UpdateAudioVisibility();
			Speak(cEnableSounds.Checked ? Loc.T("settings.uiSoundsOn") : Loc.T("settings.uiSoundsOff"));
		};
		UpdateAudioVisibility();

		// ----- AI tab: provider, model, per-provider API key, and a connection test -----
		tabAi.Controls.Add(new Label { Text = Loc.T("settings.aiIntro"), AutoSize = true, MaximumSize = new Size(480, 0) }, 0, air++);

		CheckBox cAiEnabled = new CheckBox
		{
			Text = Loc.T("settings.aiEnable"),
			Checked = _settings.AiEnabled,
			AutoSize = true,
			Padding = new Padding(0, 8, 0, 0)
		};
		tabAi.Controls.Add(cAiEnabled, 0, air++);

		Label lblAiProvider = new Label { Text = Loc.T("settings.aiProvider") + ":", AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
		tabAi.Controls.Add(lblAiProvider, 0, air++);
		ComboBox cmbAiProvider = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 350,
			Font = new Font("Segoe UI", 10f),
			AccessibleName = Loc.T("settings.aiProvider")
		};
		foreach (AiProviderInfo prov in AiService.Providers) cmbAiProvider.Items.Add(prov);
		tabAi.Controls.Add(cmbAiProvider, 0, air++);

		Label lblAiBaseUrl = new Label { Text = Loc.T("settings.aiBaseUrl") + ":", AutoSize = true, Padding = new Padding(0, 10, 0, 0), Visible = false };
		tabAi.Controls.Add(lblAiBaseUrl, 0, air++);
		TextBox tAiBaseUrl = new TextBox
		{
			Width = 350,
			Font = new Font("Segoe UI", 10f),
			AccessibleName = Loc.T("settings.aiBaseUrl"),
			Text = _settings.AiCustomBaseUrl,
			Visible = false
		};
		tabAi.Controls.Add(tAiBaseUrl, 0, air++);

		Label lblAiModel = new Label { Text = Loc.T("settings.aiModel") + ":", AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
		tabAi.Controls.Add(lblAiModel, 0, air++);
		ComboBox cmbAiModel = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 350,
			Font = new Font("Segoe UI", 10f),
			AccessibleName = Loc.T("settings.aiModel")
		};
		tabAi.Controls.Add(cmbAiModel, 0, air++);

		Button btnAiRefresh = new Button { Text = Loc.T("settings.aiRefresh"), AutoSize = true, Padding = new Padding(0, 4, 0, 0) };
		tabAi.Controls.Add(btnAiRefresh, 0, air++);

		Label lblAiCustom = new Label { Text = Loc.T("settings.aiCustomModel") + ":", AutoSize = true, Padding = new Padding(0, 10, 0, 0), Visible = false };
		tabAi.Controls.Add(lblAiCustom, 0, air++);
		TextBox tAiCustomModel = new TextBox
		{
			Width = 350,
			Font = new Font("Segoe UI", 10f),
			AccessibleName = Loc.T("settings.aiCustomModel"),
			Visible = false
		};
		tabAi.Controls.Add(tAiCustomModel, 0, air++);

		Label lblAiKey = new Label { Text = Loc.T("settings.aiKey") + ":", AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
		tabAi.Controls.Add(lblAiKey, 0, air++);
		TextBox tAiKey = new TextBox
		{
			Width = 350,
			Font = new Font("Segoe UI", 10f),
			AccessibleName = Loc.T("settings.aiKey")
		};
		tabAi.Controls.Add(tAiKey, 0, air++);

		Label lblAiKeyHelp = new Label { Text = "", AutoSize = true, MaximumSize = new Size(480, 0), Padding = new Padding(0, 4, 0, 0) };
		tabAi.Controls.Add(lblAiKeyHelp, 0, air++);

		// Opens the selected provider's own key page. The help text above says where to go; this saves finding
		// it. Hidden for the custom endpoint, which could be any service and so has no page to send anyone to.
		Button btnAiGetKey = new Button
		{
			Text = Loc.T("settings.aiGetKey"),
			AutoSize = true,
			Padding = new Padding(0, 4, 0, 0),
			AccessibleName = Loc.T("settings.aiGetKeyName")
		};
		btnAiGetKey.Click += delegate
		{
			string url = (cmbAiProvider.SelectedItem as AiProviderInfo)?.KeyUrl ?? "";
			if (string.IsNullOrEmpty(url)) return;
			try
			{
				Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
				Speak(Loc.T("settings.aiGetKeyOpening", (cmbAiProvider.SelectedItem as AiProviderInfo)?.Display ?? ""));
			}
			catch (Exception ex)
			{
				SpeakBox(Loc.T("store.couldNotOpenLink", ex.Message));
			}
		};
		tabAi.Controls.Add(btnAiGetKey, 0, air++);

		Button btnAiTest = new Button { Text = Loc.T("settings.aiTest"), AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
		tabAi.Controls.Add(btnAiTest, 0, air++);

		void UpdateAiCustomVisibility()
		{
			bool custom = cAiEnabled.Checked && (cmbAiModel.SelectedItem as AiModelOption)?.Id == AiService.CustomModelId;
			lblAiCustom.Visible = custom;
			tAiCustomModel.Visible = custom;
		}
		// Show the provider/model/key controls only while AI is enabled — an unchecked box means nothing else
		// on the tab is relevant.
		void UpdateAiVisibility()
		{
			bool on = cAiEnabled.Checked;
			lblAiProvider.Visible = cmbAiProvider.Visible = on;
			bool needsBase = on && (cmbAiProvider.SelectedItem as AiProviderInfo)?.NeedsBaseUrl == true;
			lblAiBaseUrl.Visible = tAiBaseUrl.Visible = needsBase;
			lblAiModel.Visible = cmbAiModel.Visible = on;
			lblAiKey.Visible = tAiKey.Visible = on;
			lblAiKeyHelp.Visible = on;
			// Only shown when the selected provider actually has a key page of its own.
			btnAiGetKey.Visible = on &&
				!string.IsNullOrEmpty((cmbAiProvider.SelectedItem as AiProviderInfo)?.KeyUrl);
			btnAiRefresh.Visible = on;
			btnAiTest.Visible = on;
			UpdateAiCustomVisibility(); // the Custom row also depends on the selected model
		}
		// Fills the model dropdown from the provider's curated list, or from a live list fetched via
		// "Refresh model list", always ending with a "Custom" entry. Selects wantModel, or Custom if it isn't
		// in the list (and drops the raw id into the Custom box).
		void PopulateAiModels(AiProviderInfo prov, string wantModel, IReadOnlyList<AiModelOption>? liveModels = null)
		{
			cmbAiModel.Items.Clear();
			// Prefer a freshly fetched list, then the persisted cache from a previous "Refresh model list", and only
			// then the small curated fallback. Using the cache is what lets a saved model that isn't in the curated
			// list still show up (and stay selected) after Settings is reopened.
			IReadOnlyList<AiModelOption> source = liveModels
				?? (_settings.AiModelCache.TryGetValue(prov.Id, out var cached) && cached.Count > 0
					? cached.Select(c => new AiModelOption(c.Id, c.Display)).ToList()
					: prov.Models);
			var list = source.Where(m => m.Id != AiService.CustomModelId).ToList();
			foreach (AiModelOption m in list) cmbAiModel.Items.Add(m);
			AiModelOption custom = new AiModelOption(AiService.CustomModelId, Loc.T("settings.aiCustomEntry"));
			cmbAiModel.Items.Add(custom);

			AiModelOption? match = list.FirstOrDefault(m => string.Equals(m.Id, wantModel, StringComparison.OrdinalIgnoreCase));
			if (match != null)
			{
				cmbAiModel.SelectedItem = match;
			}
			else
			{
				cmbAiModel.SelectedItem = custom;
				if (!string.IsNullOrEmpty(wantModel) && wantModel != AiService.CustomModelId) tAiCustomModel.Text = wantModel;
			}
			lblAiKeyHelp.Text = prov.KeyHelp;
			UpdateAiCustomVisibility();
		}
		cmbAiProvider.SelectedIndexChanged += delegate
		{
			if (cmbAiProvider.SelectedItem is AiProviderInfo p)
			{
				PopulateAiModels(p, p.DefaultModel);
				tAiKey.Text = _settings.AiApiKeys.TryGetValue(p.Id, out string? k) ? k : "";
				UpdateAiVisibility();
			}
		};
		cmbAiModel.SelectedIndexChanged += delegate { UpdateAiCustomVisibility(); };

		// Initial load: select the saved provider, then override with the saved model + key (the handler above
		// fills in provider defaults when the selection first changes).
		AiProviderInfo initialProv = AiService.FindProvider(_settings.AiProvider) ?? AiService.Providers[0];
		cmbAiProvider.SelectedItem = initialProv;
		PopulateAiModels(initialProv, string.IsNullOrEmpty(_settings.AiModel) ? initialProv.DefaultModel : _settings.AiModel);
		tAiKey.Text = _settings.AiApiKeys.TryGetValue(initialProv.Id, out string? initialKey) ? initialKey : "";

		cAiEnabled.CheckedChanged += delegate
		{
			UpdateAiVisibility();
			Speak(cAiEnabled.Checked ? Loc.T("settings.aiOn") : Loc.T("settings.aiOff"));
		};
		UpdateAiVisibility();

		// Fetch the provider's live model catalog with the entered key and repopulate the dropdown, keeping the
		// current selection if it still exists.
		btnAiRefresh.Click += async delegate
		{
			if (cmbAiProvider.SelectedItem is not AiProviderInfo prov) return;
			string key = tAiKey.Text.Trim();
			if (string.IsNullOrEmpty(key)) { Speak(Loc.T("settings.aiRefreshNeedsKey")); return; }
			string current = (cmbAiModel.SelectedItem as AiModelOption)?.Id ?? "";
			if (current == AiService.CustomModelId) current = tAiCustomModel.Text.Trim();
			string baseUrl = prov.NeedsBaseUrl ? tAiBaseUrl.Text.Trim() : "";
			btnAiRefresh.Enabled = false;
			Speak(Loc.T("settings.aiRefreshWorking"));
			try
			{
				var live = await _aiService.ListModelsAsync(prov.Id, key, baseUrl);
				if (live.Count == 0) { Speak(Loc.T("settings.aiRefreshNone")); return; }

				// Compare against the previously cached list so we can tell the user what changed (new / removed models).
				_settings.AiModelCache.TryGetValue(prov.Id, out var previous);
				var oldIds = new HashSet<string>(previous?.Select(c => c.Id) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
				var newIds = new HashSet<string>(live.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
				int added = newIds.Count(id => !oldIds.Contains(id));
				int removed = oldIds.Count(id => !newIds.Contains(id));

				// Remember the fetched list (persisted on Save) so the dropdown keeps showing the real models next time.
				_settings.AiModelCache[prov.Id] = live.Select(m => new AiModelChoice { Id = m.Id, Display = m.Display }).ToList();

				PopulateAiModels(prov, string.IsNullOrEmpty(current) ? prov.DefaultModel : current, live);
				_soundEngine.Play("load_complete");
				// First-ever refresh for this provider: just report the count. Otherwise report the added/removed diff.
				if (previous != null && previous.Count > 0 && (added > 0 || removed > 0))
					Speak(Loc.T("settings.aiRefreshChanged", live.Count, added, removed));
				else if (previous != null && previous.Count > 0)
					Speak(Loc.T("settings.aiRefreshNoChange", live.Count));
				else
					Speak(Loc.T("settings.aiRefreshOk", live.Count));
			}
			catch (Exception ex)
			{
				_soundEngine.Play("error");
				SpeakBox(Loc.T("settings.aiRefreshFail", ex.Message), Loc.T("ai.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			finally { btnAiRefresh.Enabled = true; }
		};

		btnAiTest.Click += async delegate
		{
			if (cmbAiProvider.SelectedItem is not AiProviderInfo prov) return;
			string model = (cmbAiModel.SelectedItem as AiModelOption)?.Id ?? "";
			if (model == AiService.CustomModelId) model = tAiCustomModel.Text.Trim();
			string key = tAiKey.Text.Trim();
			if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(model))
			{
				Speak(Loc.T("settings.aiTestNeedsKey"));
				return;
			}
			string testBaseUrl = prov.NeedsBaseUrl ? tAiBaseUrl.Text.Trim() : "";
			btnAiTest.Enabled = false;
			Speak(Loc.T("settings.aiTestWorking"));
			try
			{
				// 256 tokens of headroom so a "thinking" model still has room to emit the visible reply.
				await _aiService.AskAsync(prov.Id, model, key, "You are a connection test.", "Reply with the single word OK.", 256, testBaseUrl);
				// A test that passed is an operation that finished, not a connection the app now holds.
				_soundEngine.Play("load_complete");
				Speak(Loc.T("settings.aiTestOk"));
			}
			catch (Exception ex)
			{
				_soundEngine.Play("error");
				SpeakBox(Loc.T("settings.aiTestFail", ex.Message), Loc.T("ai.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			finally { btnAiTest.Enabled = true; }
		};

		FlowLayoutPanel flowLayoutPanel5 = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			FlowDirection = FlowDirection.RightToLeft,
			Height = 45
		};
		Button button3 = new Button
		{
			Text = Loc.T("settings.save"),
			Width = 120,
			Height = 35
		};
		button3.Click += delegate
		{
			tempModsPaths[currentEditingGame] = tPath.Text.Trim();
			tempGamePaths[currentEditingGame] = tGamePath.Text.Trim();

			if (_settings.ActiveGame != "None")
			{
				// TryGetValue, not the indexer: the active copy's key is not guaranteed to be in these maps —
				// a second copy detected this session may not have been given a path yet, and throwing here
				// would take the whole Settings dialog down rather than saving what the user typed.
				tempModsPaths.TryGetValue(_settings.ActiveGame, out string? activeMods);
				if (!string.IsNullOrEmpty(activeMods) && !Directory.Exists(activeMods))
				{
					Speak(Loc.T("settings.errModsInvalidSpeak"));
					SpeakBox(Loc.T("settings.errModsInvalidBox"));
					return;
				}

				tempGamePaths.TryGetValue(_settings.ActiveGame, out string? activeGamePath);
				if (!GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley) && !string.IsNullOrEmpty(activeGamePath) && !Directory.Exists(activeGamePath))
				{
					Speak(Loc.T("settings.errGameInvalidSpeak"));
					SpeakBox(Loc.T("settings.errGameInvalidBox"));
					return;
				}
			}

			string text2 = tKey.Text.Trim();

			// Required only when the loaded game actually needs it. Everything below is the whole of Save, so
			// demanding a key unconditionally meant a Minecraft player could not change any setting at all
			// until they supplied one that would never be used.
			if (keyIsRequired && string.IsNullOrEmpty(text2))
			{
				Speak(Loc.T("settings.errApiKeySpeak"));
				SpeakBox(Loc.T("settings.errApiKeyBox"));
			}
			else
			{
				_settings.GameModsPaths = tempModsPaths;
				_settings.GamePaths = tempGamePaths;
				if (tempModsPaths.TryGetValue(GameProfiles.StardewValley, out string? sdPath))
				{
					_settings.ModsPath = sdPath;
				}

				// A game folder typed in here is the answer for that copy too, or the session and the recorded
				// copy would disagree about where its saves, INIs and per-player data live.
				foreach (GameInstall copy in _settings.GameInstalls)
					if (tempGamePaths.TryGetValue(copy.Key, out string? typed) && !string.IsNullOrEmpty(typed))
						copy.Folder = typed;

				_settings.ApiKey = text2;
				_settings.ShowSplashScreen = cSplash.Checked;
				_settings.RandomLogoStartup = cRandomLogo.Checked;
				_settings.SelectedLogoFile = cmbLogo.SelectedItem?.ToString() ?? "";
				_settings.CheckForUpdatesAtStartup = cUpdates.Checked;
				_settings.CheckForManagerUpdatesAtStartup = cManagerUpdates.Checked;
				_settings.SpeakStartupMessage = cStartupMsg.Checked;
				_settings.SpeakShutdownMessage = cShutdownMsg.Checked;
				_settings.SaveSearchHistory = cSearchHistory.Checked;
				_settings.EnableUiSounds = cEnableSounds.Checked;
				if (nVol.SelectedItem is int volValue) _settings.SoundVolume = volValue;
				if (nPrune.SelectedItem is int backupValue) _settings.MaxBackupsPerMod = backupValue;
				if (cmbProgress.SelectedItem is ProgressFeedbackChoice pfc)
				{
					_settings.ProgressFeedback = pfc.Value;
				}
				if (cmbCrossGame.SelectedItem is SettingChoice<CrossGameDownloadAction> cgc)
				{
					_settings.CrossGameDownloads = cgc.Value;
				}
				if (cmbPageSize.SelectedItem is int pageSize)
				{
					_settings.DiscoverySearchPageSize = pageSize;
					// Reflect the newly saved default in the Discovery tab's selector so it stays in sync.
					if (cmbDiscoveryPageSize != null) cmbDiscoveryPageSize.SelectedItem = pageSize;
				}
				string chosenLang = (cmbLanguage.SelectedItem as LanguageChoice)?.Code ?? "";
				bool langChanged = !chosenLang.Equals(_settings.Language, StringComparison.OrdinalIgnoreCase);
				_settings.Language = chosenLang;
				_settings.AllowManualTheme = cManualTheme.Checked;
				_settings.CurrentTheme = cManualTheme.Checked
					? (cTheme.SelectedItem?.ToString() ?? "Default")
					: AppSettings.ThemeForGame(_settings.ActiveGame);

				// AI settings (per-provider key stored encrypted; empty key clears it).
				_settings.AiEnabled = cAiEnabled.Checked;
				_settings.AiCustomBaseUrl = tAiBaseUrl.Text.Trim();
				if (cmbAiProvider.SelectedItem is AiProviderInfo aiProv)
				{
					_settings.AiProvider = aiProv.Id;
					string chosenAiModel = (cmbAiModel.SelectedItem as AiModelOption)?.Id ?? "";
					if (chosenAiModel == AiService.CustomModelId) chosenAiModel = tAiCustomModel.Text.Trim();
					_settings.AiModel = chosenAiModel;
					string aiKey = tAiKey.Text.Trim();
					if (string.IsNullOrEmpty(aiKey)) _settings.AiApiKeys.Remove(aiProv.Id);
					else _settings.AiApiKeys[aiProv.Id] = aiKey;
				}

				_settings.DisplayContrast = contrastOptions[Math.Max(0, cmbContrast.SelectedIndex)];
				_settings.TextSize = textSizeOptions[Math.Max(0, cmbTextSize.SelectedIndex)];

				// Apply archive invalidation to disk only when the user actually flipped the toggle. The INI is the
				// source of truth (not an AppSettings flag), so we avoid rewriting it when nothing changed.
				if (cArchiveInvalidation.Visible)
				{
					bool currentInvalidation = ModFileSystem.IsArchiveInvalidationEnabled(_settings.ActiveGame, _settings.CurrentGamePath);
					if (cArchiveInvalidation.Checked != currentInvalidation)
						ModFileSystem.SetArchiveInvalidation(_settings.ActiveGame, _settings.CurrentGamePath, cArchiveInvalidation.Checked, LogError);
				}

				// Apply the plugins.txt guard straight away, so turning it off also clears the read-only flag the
				// manager set rather than leaving the file locked for other tools.
				if (cProtectPlugins.Visible)
				{
					_settings.ProtectPluginOrder = cProtectPlugins.Checked;
					ModFileSystem.SetPluginsTxtProtection(_settings.ActiveGame, _settings.CurrentGamePath, cProtectPlugins.Checked, LogError);
				}

				_settings.Save();
				// Re-theme the main window immediately so a changed contrast/text-size takes effect without a restart.
				ApplyDisplayTheme();
				// A changed game path can change the detected edition/build, so drop the cached display name.
				InvalidateGameDisplayName();
				UpdateGamesMenu();
				saved = true;
				closeView();
				Task.Delay(100).ContinueWith(delegate
				{
					Invoke(delegate
					{
						Fire(RefreshModList(checkUpdates: false), "RefreshModList");
					});
				});
				Speak(langChanged
					? Loc.T("settings.saved") + " " + Loc.T("settings.languageRestart")
					: Loc.T("settings.saved"));
			}
		};
		Button button4 = new Button
		{
			Text = Loc.T("common.cancel"),
			Width = 100,
			Height = 35
		};
		button4.Click += delegate
		{
			closeView();
		};
		flowLayoutPanel5.Controls.AddRange(button3, button4);
		container.Controls.Add(flowLayoutPanel5);
		container.Controls.Add(tabs);
		// The buttons panel is added first (so the tabs dock-fill above it), which would otherwise make the Save
		// button the first tab stop and the window's initial focus — the screen reader then announces "Save" even
		// after we move focus to the tabs. Put the tab strip first in tab order so initial focus lands on it and
		// the active tab is what gets announced.
		tabs.TabIndex = 0;
		flowLayoutPanel5.TabIndex = 1;
		// Escape is handled by the view itself; "changes cancelled" is announced from onClosed below, so it is
		// said whichever way Settings was left — Escape or the Cancel button — and not at all after a Save.

		// Focus lands on the tab strip so the user can arrow between tabs before tabbing into the first setting,
		// rather than dropping straight onto one control. The active tab is NOT spoken here: the screen reader
		// announces it on its own, and saying it as well had it read out twice — "Paths & Account Tab" from us,
		// then "Paths & Account selected" from the reader.

		// Add the "name then pause then value" reading to every combo/checkbox/list in the view.
		ApplyScreenReaderPauses(container);

		// These two are local functions of the view, not of the method: they use controls built above, which now
		// live inside this lambda. C# allows a local function to be used before it is declared, so the handlers
		// wired further up still reach them.
		void PreviewLogo()
		{
			if (cmbLogo.SelectedItem != null)
			{
				_soundEngine.PlayLogoSound(_settings.CurrentTheme, cmbLogo.SelectedItem.ToString() ?? "");
			}
		}
		void RefreshLogoList(string theme)
		{
			// Repopulating the list changes the selection, which would otherwise fire the
			// preview; suppress it so only deliberate user selection plays a logo.
			suppressLogoPreview = true;
			try
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
			finally
			{
				suppressLogoPreview = false;
			}
		}

		return tabs;
		},
		onClosed: () =>
		{
			_isSettingsOpen = false;
			// Don't leave a logo preview playing after Settings has gone.
			_soundEngine.StopLogoSound();
			if (!saved) Speak(Loc.T("common.changesCancelled"));
		});
	}
}
