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
			Size = new Size(500, 800),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(15),
			RowCount = 16
		};

		tableLayoutPanel.Controls.Add(new Label
		{
			Text = Loc.T("settings.configurePaths") + ":",
			AutoSize = true
		}, 0, 0);

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
		tableLayoutPanel.Controls.Add(cmbSettingsGame, 0, 1);

		tableLayoutPanel.Controls.Add(new Label
		{
			Text = Loc.T("settings.modsPath") + ":",
			AutoSize = true,
			Padding = new Padding(0, 10, 0, 0)
		}, 0, 2);

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
		tableLayoutPanel.Controls.Add(panelMods, 0, 3);

		Label lblGamePath = new Label
		{
			Text = Loc.T("settings.gamePath") + ":",
			AutoSize = true,
			Padding = new Padding(0, 10, 0, 0)
		};
		tableLayoutPanel.Controls.Add(lblGamePath, 0, 4);

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
		tableLayoutPanel.Controls.Add(panelGame, 0, 5);

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

		tableLayoutPanel.Controls.Add(new Label
		{
			Text = Loc.T("settings.apiKey") + ":",
			AutoSize = true,
			Padding = new Padding(0, 10, 0, 0)
		}, 0, 6);
		TextBox tKey = new TextBox
		{
			Text = _settings.ApiKey,
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 10f),
			AccessibleName = Loc.T("settings.apiKey")
		};
		tableLayoutPanel.Controls.Add(tKey, 0, 7);

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
		cSplash.CheckedChanged += delegate
		{
			Speak(cSplash.Checked ? Loc.T("settings.splashOn") : Loc.T("settings.splashOff"));
			cRandomLogo.Visible = cSplash.Checked;
		};
		cRandomLogo.CheckedChanged += delegate
		{
			Speak(cRandomLogo.Checked ? Loc.T("settings.randomLogoOn") : Loc.T("settings.randomLogoOff"));
		};
		cUpdates.CheckedChanged += delegate
		{
			Speak(cUpdates.Checked ? Loc.T("settings.updatesOn") : Loc.T("settings.updatesOff"));
		};
		flowLayoutPanel.Controls.AddRange(cSplash, cRandomLogo, cUpdates);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 8);

		tableLayoutPanel.Controls.Add(new Label
		{
			Text = Loc.T("settings.selectLogo") + ":",
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		}, 0, 9);
		// Suppress the logo audio preview while the list is repopulated programmatically (e.g.
		// when the theme changes or the manual-theme box is unchecked); only a deliberate logo
		// selection or Space press should play a logo.
		bool suppressLogoPreview = false;
		ComboBox cmbLogo = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList,
			Width = 300,
			AccessibleName = Loc.T("settings.selectLogo")
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
		tableLayoutPanel.Controls.Add(cmbLogo, 0, 10);

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
		NumericUpDown nVol = new NumericUpDown
		{
			Value = _settings.SoundVolume,
			Minimum = 0m,
			Maximum = 100m,
			Width = 60
		};
		flowLayoutPanel2.Controls.Add(nVol);
		tableLayoutPanel.Controls.Add(flowLayoutPanel2, 0, 11);

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
		NumericUpDown nPrune = new NumericUpDown
		{
			Value = _settings.MaxBackupsPerMod,
			Minimum = 1m,
			Maximum = 50m,
			Width = 60
		};
		flowLayoutPanel3.Controls.Add(nPrune);
		tableLayoutPanel.Controls.Add(flowLayoutPanel3, 0, 12);

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

		tableLayoutPanel.Controls.Add(flowLayoutPanel4, 0, 13);
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
		tableLayoutPanel.Controls.Add(flowLang, 0, 14);

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
		tableLayoutPanel.Controls.Add(flowPageSize, 0, 15);

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
					MessageBox.Show(Loc.T("settings.errModsInvalidBox"));
					return;
				}

				string activeGamePath = tempGamePaths[_settings.ActiveGame];
				if (_settings.ActiveGame != "StardewValley" && !string.IsNullOrEmpty(activeGamePath) && !Directory.Exists(activeGamePath))
				{
					Speak(Loc.T("settings.errGameInvalidSpeak"));
					MessageBox.Show(Loc.T("settings.errGameInvalidBox"));
					return;
				}
			}

			string text2 = tKey.Text.Trim();
			if (string.IsNullOrEmpty(text2))
			{
				Speak(Loc.T("settings.errApiKeySpeak"));
				MessageBox.Show(Loc.T("settings.errApiKeyBox"));
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
				_settings.SoundVolume = (int)nVol.Value;
				_settings.MaxBackupsPerMod = (int)nPrune.Value;
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
		f.Controls.Add(tableLayoutPanel);
		f.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Escape)
			{
				Speak(Loc.T("common.changesCancelled"));
				f.Close();
			}
		};
		// Land focus on the first real setting (the game selector) instead of the Save button,
		// which Windows would otherwise pick as the default focus.
		f.Shown += delegate { cmbSettingsGame.Focus(); };
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
