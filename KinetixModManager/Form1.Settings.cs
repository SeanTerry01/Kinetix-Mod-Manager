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
		Form f = new Form
		{
			Text = Loc.T("settings.title"),
			Size = new Size(540, 620),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true
		};

		// Settings are grouped into tabs to keep the dialog readable as it grows. Each tab is a single-column
		// TableLayoutPanel (same layout mechanics the dialog used when it was one long table), and the Save/Cancel
		// buttons live outside the tabs so they're always reachable. Tab order within a tab follows add order.
		TabControl tabs = new TabControl { Dock = DockStyle.Fill };
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
		TableLayoutPanel tabMods    = NewTab(Loc.T("settings.tabMods"));
		TableLayoutPanel tabLang    = NewTab(Loc.T("settings.tabLanguage"));
		int pr = 0, sr = 0, ar = 0, mr = 0, lr = 0; // per-tab row counters

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
		cmbSettingsGame.Items.AddRange(new string[] { "Stardew Valley", "Skyrim Special Edition", "Fallout 4" });
		cmbSettingsGame.SelectedItem = _settings.ActiveGame switch
		{
			"SkyrimSE" => "Skyrim Special Edition",
			"Fallout4" => "Fallout 4",
			_ => "Stardew Valley"
		};
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

		var tempModsPaths = new Dictionary<string, string>(_settings.GameModsPaths);
		var tempGamePaths = new Dictionary<string, string>(_settings.GamePaths);
		string currentEditingGame = _settings.ActiveGame;

		tPath.Text = _settings.CurrentModsPath;
		tGamePath.Text = _settings.CurrentGamePath;

		Action updateVisibility = () =>
		{
			bool isStardew = (cmbSettingsGame.SelectedIndex == 0);
			lblGamePath.Visible = !isStardew;
			panelGame.Visible = !isStardew;
		};
		updateVisibility();

		cmbSettingsGame.SelectedIndexChanged += delegate
		{
			string lastGameKey = currentEditingGame;
			tempModsPaths[lastGameKey] = tPath.Text.Trim();
			tempGamePaths[lastGameKey] = tGamePath.Text.Trim();

			string newGameKey = cmbSettingsGame.SelectedIndex switch
			{
				1 => "SkyrimSE",
				2 => "Fallout4",
				_ => "StardewValley"
			};

			currentEditingGame = newGameKey;
			tPath.Text = tempModsPaths.TryGetValue(newGameKey, out string? p) ? p : "";
			tGamePath.Text = tempGamePaths.TryGetValue(newGameKey, out string? gp) ? gp : "";
			
			updateVisibility();
			Speak(Loc.T("settings.editingPaths", cmbSettingsGame.SelectedItem));
		};

		tabPaths.Controls.Add(new Label
		{
			Text = Loc.T("settings.apiKey") + ":",
			AutoSize = true,
			Padding = new Padding(0, 10, 0, 0)
		}, 0, pr++);
		TextBox tKey = new TextBox
		{
			Text = _settings.ApiKey,
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 10f),
			AccessibleName = Loc.T("settings.apiKey")
		};
		tabPaths.Controls.Add(tKey, 0, pr++);

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
			if (f.Visible && !suppressLogoPreview)
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
		cTheme.Items.AddRange(Directory.GetDirectories(themesPath).Select(Path.GetFileName).Cast<object>()
			.ToArray());
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
				string activeMods = tempModsPaths[_settings.ActiveGame];
				if (!string.IsNullOrEmpty(activeMods) && !Directory.Exists(activeMods))
				{
					Speak(Loc.T("settings.errModsInvalidSpeak"));
					SpeakBox(Loc.T("settings.errModsInvalidBox"));
					return;
				}

				string activeGamePath = tempGamePaths[_settings.ActiveGame];
				if (_settings.ActiveGame != "StardewValley" && !string.IsNullOrEmpty(activeGamePath) && !Directory.Exists(activeGamePath))
				{
					Speak(Loc.T("settings.errGameInvalidSpeak"));
					SpeakBox(Loc.T("settings.errGameInvalidBox"));
					return;
				}
			}

			string text2 = tKey.Text.Trim();
			if (string.IsNullOrEmpty(text2))
			{
				Speak(Loc.T("settings.errApiKeySpeak"));
				SpeakBox(Loc.T("settings.errApiKeyBox"));
			}
			else
			{
				_settings.GameModsPaths = tempModsPaths;
				_settings.GamePaths = tempGamePaths;
				if (tempModsPaths.TryGetValue("StardewValley", out string? sdPath))
				{
					_settings.ModsPath = sdPath;
				}

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
				_settings.Save();
				// A changed game path can change the detected edition/build, so drop the cached display name.
				InvalidateGameDisplayName();
				UpdateGamesMenu();
				f.Close();
				Task.Delay(100).ContinueWith(delegate
				{
					Invoke(delegate
					{
						_ = RefreshModList(checkUpdates: false);
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
			Speak(Loc.T("common.changesCancelled"));
			f.Close();
		};
		flowLayoutPanel5.Controls.AddRange(button3, button4);
		f.FormClosing += delegate
		{
			_isSettingsOpen = false;
		};
		f.Controls.Add(flowLayoutPanel5);
		f.Controls.Add(tabs);
		// The buttons panel is added first (so the tabs dock-fill above it), which would otherwise make the Save
		// button the first tab stop and the window's initial focus — the screen reader then announces "Save" even
		// after we move focus to the tabs. Put the tab strip first in tab order so initial focus lands on it and
		// the active tab is what gets announced.
		tabs.TabIndex = 0;
		flowLayoutPanel5.TabIndex = 1;
		f.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Escape)
			{
				Speak(Loc.T("common.changesCancelled"));
				f.Close();
			}
		};
		// Land focus on the tab strip so the screen reader announces the tabs (and the user can arrow between
		// them) before tabbing into the first setting, rather than dropping straight onto one control.
		f.Shown += delegate { tabs.Focus(); };
		// Add the "name then pause then value" reading to every combo/checkbox/list in the dialog.
		ApplyScreenReaderPauses(f);
		f.ShowDialog();
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
	}
}
