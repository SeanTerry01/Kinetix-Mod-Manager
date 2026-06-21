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

	/// <summary>
	/// Verifies <paramref name="game"/> is installed before a session is allowed to load. When it
	/// is not, the session must NOT load: <see cref="AppSettings.CurrentModsPath"/> falls back to the
	/// Stardew Valley Mods path when a game's own path is unset, so loading anyway would silently show
	/// another game's mods. Announces the situation, offers to purchase the game, and on "Yes" opens the
	/// Steam/GOG store picker. Returns <c>true</c> only when loading may proceed (game installed, or
	/// "None"), and <c>false</c> when the caller must abort the load.
	/// </summary>
	private bool EnsureGameInstalledOrOfferPurchase(string game)
	{
		if (game == "None" || IsGameInstalled(game)) return true;

		string targetName = game switch
		{
			"SkyrimSE" => "Skyrim Special Edition",
			"Fallout4" => "Fallout 4",
			"StardewValley" => "Stardew Valley",
			_ => game
		};

		Speak(Loc.T("session.notInstalledSpeak", targetName));

		DialogResult choice = MessageBox.Show(
			Loc.T("session.notInstalledBox", targetName),
			Loc.T("session.notInstalledTitle"),
			MessageBoxButtons.YesNo,
			MessageBoxIcon.Warning);

		if (choice == DialogResult.Yes)
		{
			ShowStoreSelectionDialog(targetName);
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

		// Listed alphabetically.
		if (falloutInstalled || _settings.ActiveGame == "Fallout4")
		{
			_menuGames.DropDownItems.Add("Fallout 4", null, delegate { SwitchActiveGame("Fallout4"); });
		}
		if (skyrimInstalled || _settings.ActiveGame == "SkyrimSE")
		{
			_menuGames.DropDownItems.Add("Skyrim Special Edition", null, delegate { SwitchActiveGame("SkyrimSE"); });
		}
		if (stardewInstalled || _settings.ActiveGame == "StardewValley")
		{
			_menuGames.DropDownItems.Add("Stardew Valley", null, delegate { SwitchActiveGame("StardewValley"); });
		}

		// Close session option is in the File menu
	}

	private void ShowNoGamesInstalledFlow()
	{
		Speak(Loc.T("games.noneDetectedSpeak"));

		DialogResult result = MessageBox.Show(
			Loc.T("games.noneDetectedBox"),
			Loc.T("games.noneDetectedTitle"),
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
			Text = Loc.T("store.purchaseDialogTitle"),
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
			Text = Loc.T("store.selectGameLabel"),
			Font = new Font("Segoe UI", 11f, FontStyle.Bold),
			Dock = DockStyle.Fill
		};
		layout.Controls.Add(lbl, 0, 0);

		ListBox lstGames = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			AccessibleName = Loc.T("store.selectGamePurchase")
		};
		// Listed alphabetically.
		lstGames.Items.Add("Fallout 4");
		lstGames.Items.Add("Skyrim Special Edition");
		lstGames.Items.Add("Stardew Valley");
		layout.Controls.Add(lstGames, 0, 1);

		Button btnSelect = new Button
		{
			Text = Loc.T("store.viewStores"),
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

		Speak(Loc.T("store.selectGameSpeak"));
		dialog.Shown += (s, e) => { lstGames.Focus(); };
		dialog.ShowDialog();
	}

	private void ShowStoreSelectionDialog(string gameName)
	{
		Form dialog = new Form
		{
			Text = Loc.T("store.purchaseTitle", gameName),
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
			Text = Loc.T("store.whereBuyLabel", gameName),
			Font = new Font("Segoe UI", 11f, FontStyle.Bold),
			Dock = DockStyle.Fill
		};
		layout.Controls.Add(lbl, 0, 0);

		ListBox lstStores = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			AccessibleName = Loc.T("store.selectStorePage")
		};
		lstStores.Items.Add("Steam");
		lstStores.Items.Add("GOG (DRM-Free)");
		layout.Controls.Add(lstStores, 0, 1);

		Button btnOpen = new Button
		{
			Text = Loc.T("store.openStorePage"),
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
					Speak(Loc.T("store.openingPage", gameName, store));
				}
				catch (Exception ex)
				{
					MessageBox.Show(Loc.T("store.couldNotOpenLink", ex.Message));
				}
			}
			dialog.Close();
		};

		layout.Controls.Add(btnOpen, 0, 2);
		dialog.Controls.Add(layout);

		Speak(Loc.T("store.whereBuy", gameName));
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
				string gameName = GameDisplayName();
				// Warn if the installed script extender won't load because it doesn't match the game's build —
				// the common reason MCM and other SKSE/F4SE features disappear after a game update.
				var seVer = ModFileSystem.CheckScriptExtenderVersion(game, gamePath);
				if (seVer.HasValue && !seVer.Value.Match)
				{
					string seName = game == "SkyrimSE" ? "SKSE" : "F4SE";
					Speak(Loc.T("launch.seMismatchSpeak", seName));
					var choice = MessageBox.Show(
						Loc.T("launch.seMismatchBox", seName, seVer.Value.ExtenderVersion, seVer.Value.GameVersion),
						Loc.T("launch.seMismatchTitle", seName),
						MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
					if (choice == DialogResult.No) { SetStatus(Loc.T("launch.cancelled")); return; }
				}

				SetStatus(Loc.T("launch.launching", gameName));
				Speak(Loc.T("launch.launching", gameName));

				Process p = new Process();
				p.StartInfo = new ProcessStartInfo(exePath)
				{
					WorkingDirectory = Path.GetDirectoryName(exePath)
				};
				p.EnableRaisingEvents = true;
				p.Exited += async delegate
				{
					SetStatus(Loc.T("launch.gameClosed"));
					await Task.Delay(5000);
					SetStatus(Loc.T("status.connectedAs", _nexusService.NexusUser));
				};
				p.Start();
				Task.Run(async delegate
				{
					await Task.Delay(3000);
					if (!p.HasExited)
					{
						SetStatus(Loc.T("launch.gameRunning"));
					}
				});
			}
			else
			{
				MessageBox.Show(Loc.T("launch.exeNotFound", exeName, gamePath));
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(Loc.T("launch.failed", ex.Message));
		}
	}
}
