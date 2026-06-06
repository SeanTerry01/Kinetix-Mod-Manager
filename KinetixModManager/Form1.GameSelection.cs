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

/// <summary>Game folder detection, installation checks, game menu, and launching for Form1.</summary>
public partial class Form1
{
	/// <summary>Locates and launches StardewModdingAPI.exe or Stardew Valley.exe via the configured mods path.</summary>
	private string DetectGameFolder(string game)
	{
		string folder = DetectInstalledGameFolder(game);
		if (!string.IsNullOrEmpty(folder)) return folder;

		return game switch
		{
			"SkyrimSE" => @"C:\Program Files (x86)\Steam\steamapps\common\Skyrim Special Edition",
			"Fallout4" => @"C:\Program Files (x86)\Steam\steamapps\common\Fallout 4",
			_ => @"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley"
		};
	}

	private string DetectInstalledGameFolder(string game)
	{
		string steamAppId = game switch
		{
			"SkyrimSE" => "489830",
			"Fallout4" => "377160",
			_ => "413150" // Stardew Valley
		};

		string gogProductId = game switch
		{
			"SkyrimSE" => "1711230643",
			"Fallout4" => "1998527297",
			_ => "1453375253"
		};

		try
		{
			using var steamKey = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {steamAppId}");
			if (steamKey != null)
			{
				string? path = steamKey.GetValue("InstallLocation")?.ToString();
				if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
					return path;
			}
		}
		catch { }

		try
		{
			string[] gogKeys = {
				$@"SOFTWARE\GOG.com\Games\{gogProductId}",
				$@"SOFTWARE\WOW6432Node\GOG.com\Games\{gogProductId}"
			};
			foreach (var subkey in gogKeys)
			{
				using var gogKey = Registry.LocalMachine.OpenSubKey(subkey);
				if (gogKey != null)
				{
					string? path = gogKey.GetValue("path")?.ToString() ?? gogKey.GetValue("InstallPath")?.ToString();
					if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
						return path;
				}
			}
		}
		catch { }

		string fallback = game switch
		{
			"SkyrimSE" => @"C:\Program Files (x86)\Steam\steamapps\common\Skyrim Special Edition",
			"Fallout4" => @"C:\Program Files (x86)\Steam\steamapps\common\Fallout 4",
			_ => @"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley"
		};

		if (Directory.Exists(fallback))
			return fallback;

		return "";
	}

	private bool IsGameInstalled(string game)
	{
		string gamePath = game switch
		{
			"SkyrimSE" => _settings.GamePaths.TryGetValue("SkyrimSE", out string? p) ? p : "",
			"Fallout4" => _settings.GamePaths.TryGetValue("Fallout4", out string? p) ? p : "",
			_ => _settings.GamePaths.TryGetValue("StardewValley", out string? p) ? p : ""
		};

		if (!string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath))
		{
			string checkExe = game switch
			{
				"SkyrimSE" => "SkyrimSE.exe",
				"Fallout4" => "Fallout4.exe",
				_ => "Stardew Valley.exe"
			};
			if (File.Exists(Path.Combine(gamePath, checkExe)) || File.Exists(Path.Combine(gamePath, game switch { "SkyrimSE" => "skse64_loader.exe", "Fallout4" => "f4se_loader.exe", _ => "StardewModdingAPI.exe" })))
				return true;
		}

		if (game == "StardewValley")
		{
			string stardewMods = _settings.GameModsPaths.TryGetValue("StardewValley", out string? p) ? p : "";
			if (!string.IsNullOrEmpty(stardewMods) && Directory.Exists(stardewMods))
			{
				string parent = Path.GetDirectoryName(stardewMods) ?? "";
				if (File.Exists(Path.Combine(parent, "Stardew Valley.exe")) || File.Exists(Path.Combine(parent, "StardewModdingAPI.exe")))
					return true;
			}
		}

		string detected = DetectInstalledGameFolder(game);
		if (!string.IsNullOrEmpty(detected) && Directory.Exists(detected))
		{
			return true;
		}

		return false;
	}

	private void UpdateGamesMenu()
	{
		if (_menuGames == null) return;
		_menuGames.DropDownItems.Clear();

		bool stardewInstalled = IsGameInstalled("StardewValley");
		bool skyrimInstalled = IsGameInstalled("SkyrimSE");
		bool falloutInstalled = IsGameInstalled("Fallout4");

		if (!stardewInstalled && !skyrimInstalled && !falloutInstalled)
		{
			stardewInstalled = true;
			skyrimInstalled = true;
			falloutInstalled = true;
		}

		if (stardewInstalled || _settings.ActiveGame == "StardewValley")
		{
			_menuGames.DropDownItems.Add("Stardew Valley", null, delegate { SwitchActiveGame("StardewValley"); });
		}
		if (skyrimInstalled || _settings.ActiveGame == "SkyrimSE")
		{
			_menuGames.DropDownItems.Add("Skyrim Special Edition", null, delegate { SwitchActiveGame("SkyrimSE"); });
		}
		if (falloutInstalled || _settings.ActiveGame == "Fallout4")
		{
			_menuGames.DropDownItems.Add("Fallout 4", null, delegate { SwitchActiveGame("Fallout4"); });
		}

		// Close session option is in the File menu
	}

	private void ShowNoGamesInstalledFlow()
	{
		Speak("No supported games were detected on your PC. Kinetix Mod Manager supports Stardew Valley, Skyrim Special Edition, and Fallout 4.");

		DialogResult result = MessageBox.Show(
			"No supported games were detected on your PC.\nKinetix Mod Manager supports:\n- Stardew Valley\n- Skyrim Special Edition\n- Fallout 4\n\nWould you like to purchase any of these games?",
			"No Games Detected",
			MessageBoxButtons.YesNo,
			MessageBoxIcon.Information
		);

		if (result == DialogResult.Yes)
		{
			ShowPurchaseGameDialog();
		}
	}

	private void ShowPurchaseGameDialog()
	{
		Form dialog = new Form
		{
			Text = "Select Game to Purchase - Escape to Close",
			Size = new Size(400, 300),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true
		};
		dialog.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) dialog.Close(); };

		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(15),
			RowCount = 3
		};
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60f));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));

		Label lbl = new Label
		{
			Text = "Select a game to view store links:",
			Font = new Font("Segoe UI", 11f, FontStyle.Bold),
			Dock = DockStyle.Fill
		};
		layout.Controls.Add(lbl, 0, 0);

		ListBox lstGames = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			AccessibleName = "Select game to purchase"
		};
		lstGames.Items.Add("Stardew Valley");
		lstGames.Items.Add("Skyrim Special Edition");
		lstGames.Items.Add("Fallout 4");
		layout.Controls.Add(lstGames, 0, 1);

		Button btnSelect = new Button
		{
			Text = "View Stores",
			Height = 40,
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f, FontStyle.Bold)
		};

		lstGames.KeyDown += (s, e) =>
		{
			if (e.KeyCode == Keys.Enter)
			{
				btnSelect.PerformClick();
				e.Handled = true;
			}
		};

		btnSelect.Click += (s, e) =>
		{
			if (lstGames.SelectedIndex == -1) return;
			string selectedGame = lstGames.SelectedItem?.ToString() ?? "";
			dialog.Close();
			ShowStoreSelectionDialog(selectedGame);
		};

		layout.Controls.Add(btnSelect, 0, 2);
		dialog.Controls.Add(layout);

		Speak("Select a game to view store links. Use arrow keys to navigate and press Enter to select.");
		dialog.Shown += (s, e) => { lstGames.Focus(); };
		dialog.ShowDialog();
	}

	private void ShowStoreSelectionDialog(string gameName)
	{
		Form dialog = new Form
		{
			Text = $"Purchase {gameName} - Escape to Close",
			Size = new Size(400, 300),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true
		};
		dialog.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) dialog.Close(); };

		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(15),
			RowCount = 3
		};
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60f));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));

		Label lbl = new Label
		{
			Text = $"Where would you like to buy {gameName}?",
			Font = new Font("Segoe UI", 11f, FontStyle.Bold),
			Dock = DockStyle.Fill
		};
		layout.Controls.Add(lbl, 0, 0);

		ListBox lstStores = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			AccessibleName = "Select store page"
		};
		lstStores.Items.Add("Steam");
		lstStores.Items.Add("GOG (DRM-Free)");
		layout.Controls.Add(lstStores, 0, 1);

		Button btnOpen = new Button
		{
			Text = "Open Store Page",
			Height = 40,
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f, FontStyle.Bold)
		};

		lstStores.KeyDown += (s, e) =>
		{
			if (e.KeyCode == Keys.Enter)
			{
				btnOpen.PerformClick();
				e.Handled = true;
			}
		};

		btnOpen.Click += (s, e) =>
		{
			if (lstStores.SelectedIndex == -1) return;
			string store = lstStores.SelectedItem?.ToString() ?? "";
			bool isGog = store.StartsWith("GOG", StringComparison.OrdinalIgnoreCase);
			string url = "";

			if (gameName == "Stardew Valley")
			{
				url = isGog ? "https://www.gog.com/game/stardew_valley" 
				            : "https://store.steampowered.com/app/413150/Stardew_Valley/";
			}
			else if (gameName == "Skyrim Special Edition")
			{
				url = isGog ? "https://www.gog.com/game/the_elder_scrolls_v_skyrim_special_edition" 
				            : "https://store.steampowered.com/app/489830/The_Elder_Scrolls_V_Skyrim_Special_Edition/";
			}
			else if (gameName == "Fallout 4")
			{
				url = isGog ? "https://www.gog.com/game/fallout_4_game_of_the_year_edition" 
				            : "https://store.steampowered.com/app/377160/Fallout_4/";
			}

			if (!string.IsNullOrEmpty(url))
			{
				try
				{
					Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
					Speak($"Opening store page for {gameName} on {store}.");
				}
				catch (Exception ex)
				{
					MessageBox.Show("Could not open link: " + ex.Message);
				}
			}
			dialog.Close();
		};

		layout.Controls.Add(btnOpen, 0, 2);
		dialog.Controls.Add(layout);

		Speak($"Where would you like to buy {gameName}? Use arrow keys to navigate and press Enter to select.");
		dialog.Shown += (s, e) => { lstStores.Focus(); };
		dialog.ShowDialog();
	}

	/// <summary>Locates and launches the active game's executable or mod loader.</summary>
	private void LaunchGame()
	{
		try
		{
			string game = _settings.ActiveGame;
			string gamePath = _settings.CurrentGamePath;
			if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
			{
				gamePath = DetectGameFolder(game);
			}

			string exeName = game switch
			{
				"SkyrimSE" => "skse64_loader.exe",
				"Fallout4" => "f4se_loader.exe",
				_ => "StardewModdingAPI.exe"
			};

			string exePath = Path.Combine(gamePath, exeName);

			if (!File.Exists(exePath))
			{
				if (game == "SkyrimSE") exePath = Path.Combine(gamePath, "SkyrimSE.exe");
				else if (game == "Fallout4") exePath = Path.Combine(gamePath, "Fallout4.exe");
				else exePath = Path.Combine(gamePath, "Stardew Valley.exe");
			}

			if (!File.Exists(exePath))
			{
				if (game == "StardewValley")
				{
					string parent = Path.GetDirectoryName(_settings.CurrentModsPath) ?? "";
					exePath = Path.Combine(parent, "StardewModdingAPI.exe");
					if (!File.Exists(exePath))
					{
						exePath = Path.Combine(parent, "Stardew Valley", "StardewModdingAPI.exe");
					}
				}
			}

			if (File.Exists(exePath))
			{
				string gameName = game switch
				{
					"SkyrimSE" => "Skyrim Special Edition",
					"Fallout4" => "Fallout 4",
					_ => "Stardew Valley"
				};
				SetStatus($"Launching {gameName}...");
				Speak($"Launching {gameName}...");

				Process p = new Process();
				p.StartInfo = new ProcessStartInfo(exePath)
				{
					WorkingDirectory = Path.GetDirectoryName(exePath)
				};
				p.EnableRaisingEvents = true;
				p.Exited += async delegate
				{
					SetStatus("Game closed.");
					await Task.Delay(5000);
					SetStatus("Connected as " + _nexusService.NexusUser);
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
				MessageBox.Show($"{exeName} or game executable not found at: {gamePath}. Please configure the correct Game Path in Settings.");
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("Launch failed: " + ex.Message);
		}
	}
}
