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

		return GameProfiles.Find(game)?.DefaultInstallFolder ?? "";
	}

	private string DetectInstalledGameFolder(string game)
	{
		GameProfile? profile = GameProfiles.Find(game);
		if (profile == null) return "";

		string steamAppId = profile.SteamAppId;
		string? gogProductId = profile.GogProductId;

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

		// The per-game uninstall key above is often missing or stale (reinstalls, manual library
		// moves, installs that never write InstallLocation). Steam's own libraryfolders.vdf lists
		// every library on every drive, so parse it to find the game wherever it actually lives.
		string steamLib = DetectSteamLibraryGameFolder(steamAppId);
		if (!string.IsNullOrEmpty(steamLib)) return steamLib;

		// Games that aren't sold on GOG have no product id and skip this entirely.
		try
		{
			if (!string.IsNullOrEmpty(gogProductId))
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
		}
		catch { }

		string fallback = profile.DefaultInstallFolder;

		if (Directory.Exists(fallback))
			return fallback;

		return "";
	}

	/// <summary>
	/// Finds the install folder for a Steam app by reading Steam's own library records, so games on
	/// any drive (or in a custom-named library folder) are detected. Returns "" if Steam, the library
	/// list, or the game's manifest can't be found — e.g. under Proton, where Steam runs natively on
	/// Linux and leaves no install record in the Wine prefix's registry.
	/// </summary>
	private string DetectSteamLibraryGameFolder(string steamAppId)
	{
		try
		{
			string steamPath = GetSteamInstallPath();
			if (string.IsNullOrEmpty(steamPath)) return "";
			return SteamLibraryLocator.FindGameFolder(steamPath, steamAppId) ?? "";
		}
		catch { }
		return "";
	}

	/// <summary>Reads the Steam client install path from the registry (per-user first, then machine-wide).</summary>
	private string GetSteamInstallPath()
	{
		try
		{
			using var userKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
			string? p = userKey?.GetValue("SteamPath")?.ToString();
			if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
		}
		catch { }

		try
		{
			using var machineKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
				?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
			string? p = machineKey?.GetValue("InstallPath")?.ToString();
			if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p;
		}
		catch { }

		return "";
	}

	private bool IsGameInstalled(string game)
	{
		if (GameProfiles.Find(game) == null) return false;

		string gamePath = _settings.GamePaths.TryGetValue(game, out string? p) ? p : "";

		if (FolderContainsGameExe(game, gamePath))
			return true;

		if (game == "StardewValley")
		{
			string stardewMods = _settings.GameModsPaths.TryGetValue("StardewValley", out string? sp) ? sp : "";
			if (!string.IsNullOrEmpty(stardewMods) && Directory.Exists(stardewMods))
			{
				string parent = Path.GetDirectoryName(stardewMods) ?? "";
				if (FolderContainsGameExe(game, parent))
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
	/// another game's mods. Announces the situation and offers three choices: locate an existing install
	/// folder (for copies auto-detection missed, e.g. under Proton), view store links to buy it, or
	/// cancel. Returns <c>true</c> only when loading may proceed (game installed, "None", or the user
	/// located a valid folder), and <c>false</c> when the caller must abort the load.
	/// </summary>
	private bool EnsureGameInstalledOrOfferPurchase(string game)
	{
		if (game == "None" || IsGameInstalled(game)) return true;

		string targetName = GameProfiles.Find(game)?.DisplayName ?? game;

		GameNotInstalledChoice choice = ShowGameNotInstalledDialog(targetName);

		if (choice == GameNotInstalledChoice.Locate)
		{
			// User already owns the game but auto-detection missed it (common under Proton, or a
			// non-standard install location): let them point the manager straight at the folder.
			return TryLocateGameFolder(game, targetName);
		}

		if (choice == GameNotInstalledChoice.Purchase)
		{
			ShowStoreSelectionDialog(targetName);
		}

		return false;
	}

	private enum GameNotInstalledChoice { Cancel, Locate, Purchase }

	/// <summary>
	/// Tells the user the game wasn't detected and offers three accessible choices: locate the existing
	/// install folder, view store links to buy it, or cancel. Returns which the user chose.
	/// </summary>
	private GameNotInstalledChoice ShowGameNotInstalledDialog(string gameName)
	{
		GameNotInstalledChoice choice = GameNotInstalledChoice.Cancel;

		Form dialog = new Form
		{
			Text = Loc.T("session.notInstalledDialogTitle", gameName),
			Size = new Size(480, 260),
			StartPosition = FormStartPosition.CenterScreen,
			FormBorderStyle = FormBorderStyle.FixedDialog,
			MaximizeBox = false,
			MinimizeBox = false,
			KeyPreview = true
		};
		dialog.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) dialog.Close(); };

		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(15),
			RowCount = 2,
			ColumnCount = 1
		};
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65f));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35f));

		Label lbl = new Label
		{
			Text = Loc.T("session.notInstalledPrompt", gameName),
			Font = new Font("Segoe UI", 11f),
			Dock = DockStyle.Fill
		};
		layout.Controls.Add(lbl, 0, 0);

		FlowLayoutPanel buttons = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false
		};

		Button btnLocate = new Button
		{
			Text = Loc.T("session.locateButton"),
			AutoSize = true,
			Height = 40,
			Font = new Font("Segoe UI", 11f, FontStyle.Bold)
		};
		Button btnPurchase = new Button
		{
			Text = Loc.T("session.purchaseButton"),
			AutoSize = true,
			Height = 40,
			Font = new Font("Segoe UI", 11f, FontStyle.Bold)
		};
		Button btnCancel = new Button
		{
			Text = Loc.T("session.cancelButton"),
			AutoSize = true,
			Height = 40,
			Font = new Font("Segoe UI", 11f, FontStyle.Bold)
		};

		btnLocate.Click += (s, e) => { choice = GameNotInstalledChoice.Locate; dialog.Close(); };
		btnPurchase.Click += (s, e) => { choice = GameNotInstalledChoice.Purchase; dialog.Close(); };
		btnCancel.Click += (s, e) => { choice = GameNotInstalledChoice.Cancel; dialog.Close(); };

		buttons.Controls.AddRange(new Control[] { btnLocate, btnPurchase, btnCancel });
		dialog.CancelButton = btnCancel;
		layout.Controls.Add(buttons, 0, 1);
		dialog.Controls.Add(layout);

		Speak(Loc.T("session.notInstalledSpeak", gameName));
		dialog.Shown += (s, e) => { btnLocate.Focus(); };
		ApplyScreenReaderPauses(dialog);
		dialog.ShowDialog();
		return choice;
	}

	/// <summary>
	/// Opens a folder picker so the user can point at an already-installed copy of <paramref name="game"/>.
	/// Saves the path and returns <c>true</c> only when the chosen folder actually contains the game's
	/// program file, so a wrong folder never loads a broken session.
	/// </summary>
	private bool TryLocateGameFolder(string game, string targetName)
	{
		using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog
		{
			Description = Loc.T("session.locateBrowseDesc", targetName),
			UseDescriptionForTitle = true
		};

		if (folderBrowserDialog.ShowDialog() != DialogResult.OK) return false;

		string chosen = folderBrowserDialog.SelectedPath;
		if (!FolderContainsGameExe(game, chosen))
		{
			Speak(Loc.T("session.locateInvalidSpeak", targetName));
			SpeakBox(
				Loc.T("session.locateInvalidBox", targetName),
				Loc.T("session.notInstalledTitle"),
				MessageBoxButtons.OK,
				MessageBoxIcon.Warning);
			return false;
		}

		_settings.GamePaths[game] = chosen;
		_settings.Save();
		Speak(Loc.T("session.locatedSpeak", targetName));
		return true;
	}

	/// <summary>
	/// True when <paramref name="path"/> holds the game's own executable or its mod loader's launcher. Games
	/// whose loader has no launcher of its own (BepInEx, which hooks the game through a winhttp.dll shim) are
	/// identified by the game executable alone.
	/// </summary>
	private bool FolderContainsGameExe(string game, string path)
	{
		if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return false;

		GameProfile? profile = GameProfiles.Find(game);
		if (profile == null) return false;

		if (File.Exists(Path.Combine(path, profile.GameExeName))) return true;
		return !string.IsNullOrEmpty(profile.LoaderExeName) &&
			   File.Exists(Path.Combine(path, profile.LoaderExeName));
	}

	private void UpdateGamesMenu()
	{
		if (_menuGames == null) return;
		_menuGames.DropDownItems.Clear();

		var installed = GameProfiles.All.ToDictionary(g => g.Id, g => IsGameInstalled(g.Id));

		// When nothing at all is detected, list every game rather than an empty menu: the user may own one the
		// detection missed, and picking it leads to the "locate the folder" flow.
		if (installed.Values.All(v => !v))
		{
			foreach (string id in GameProfiles.AllIds) installed[id] = true;
		}

		// GameProfiles.All is already in alphabetical display order.
		foreach (GameProfile profile in GameProfiles.All)
		{
			if (!installed[profile.Id] && _settings.ActiveGame != profile.Id) continue;
			string gameId = profile.Id;
			_menuGames.DropDownItems.Add(profile.DisplayName, null, delegate { SwitchActiveGame(gameId); });
		}

		// Close session option is in the File menu
	}

	private void ShowNoGamesInstalledFlow()
	{
		Speak(Loc.T("games.noneDetectedSpeak"));

		DialogResult result = SpeakBox(
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
		foreach (string name in GameProfiles.AllDisplayNames) lstGames.Items.Add(name);
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
		ApplyScreenReaderPauses(dialog);
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
		// Only offer the stores that actually sell this game — Moonlight Peaks, for one, is Steam only, and
		// offering a GOG page that doesn't exist just sends the user to a dead end.
		GameProfile? storeProfile = GameProfiles.All.FirstOrDefault(g => g.DisplayName == gameName);
		lstStores.Items.Add("Steam");
		if (storeProfile == null || !string.IsNullOrEmpty(storeProfile.GogStoreUrl))
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
			GameProfile? chosen = GameProfiles.All.FirstOrDefault(g => g.DisplayName == gameName);
			string url = chosen == null ? "" : (isGog ? chosen.GogStoreUrl ?? "" : chosen.SteamStoreUrl);

			if (!string.IsNullOrEmpty(url))
			{
				try
				{
					Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
					Speak(Loc.T("store.openingPage", gameName, store));
				}
				catch (Exception ex)
				{
					SpeakBox(Loc.T("store.couldNotOpenLink", ex.Message));
				}
			}
			dialog.Close();
		};

		layout.Controls.Add(btnOpen, 0, 2);
		dialog.Controls.Add(layout);

		Speak(Loc.T("store.whereBuy", gameName));
		dialog.Shown += (s, e) => { lstStores.Focus(); };
		ApplyScreenReaderPauses(dialog);
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

			GameProfile profile = GameProfiles.Require(game);

			// Start through the mod loader's launcher where there is one (SMAPI, SKSE, F4SE). BepInEx has none —
			// it hooks the game through a winhttp.dll shim — so for those games the game's own exe IS the modded
			// launch, and falling back to it is not a degraded path.
			string exeName = string.IsNullOrEmpty(profile.LoaderExeName) ? profile.GameExeName : profile.LoaderExeName;

			string exePath = Path.Combine(gamePath, exeName);

			if (!File.Exists(exePath))
			{
				exePath = Path.Combine(gamePath, profile.GameExeName);
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
				// Starting a BepInEx game without BepInEx runs the game with none of its mods — nothing in the
				// game says so, so the manager does, before it happens.
				if (!ConfirmBepInExBeforeLaunch(gamePath)) { SetStatus(Loc.T("launch.cancelled")); return; }

				string gameName = GameDisplayName();
				// Warn if the installed script extender won't load because it doesn't match the game's build —
				// the common reason MCM and other SKSE/F4SE features disappear after a game update.
				var seVer = ModFileSystem.CheckScriptExtenderVersion(game, gamePath);
				if (seVer.HasValue && !seVer.Value.Match)
				{
					string seName = game == "SkyrimSE" ? "SKSE" : "F4SE";
					Speak(Loc.T("launch.seMismatchSpeak", seName));
					var choice = SpeakBox(
						Loc.T("launch.seMismatchBox", seName, seVer.Value.ExtenderVersion, seVer.Value.GameVersion),
						Loc.T("launch.seMismatchTitle", seName),
						MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
					if (choice == DialogResult.No) { SetStatus(Loc.T("launch.cancelled")); return; }
				}

				// SetStatus speaks by default, so it announces the launch on its own.
				SetStatus(Loc.T("launch.launching", gameName));

				// Skyrim SE and Fallout 4 rewrite plugins.txt as they run — starting a new game deactivates every
				// Creation and reshuffles the load order. Write the manager's order out one last time and, unless
				// the user has turned the guard off, mark the file read-only so the game keeps the order it is
				// given. Whatever happens, the order is verified again once the game exits (below).
				if (IsBethesdaGame)
				{
					SyncBethesdaPlugins();
					ModFileSystem.SetPluginsTxtProtection(game, _settings.ProtectPluginOrder, LogError);
				}

				// Prefer letting Steam start it where that applies. A Steam game started from its own exe
				// notices it wasn't launched by Steam and restarts itself — which means the game loads twice,
				// mods announce themselves twice, and there is a stretch in the middle where no process of the
				// game exists at all. Asking Steam in the first place simply avoids all of that. Mods are
				// unaffected: BepInEx loads through a DLL beside the exe, whoever starts it.
				if (TryLaunchViaSteam(profile, gamePath))
				{
					_ = TrackGameSessionAsync(null, profile, exePath);
				}
				else
				{
					Process p = new Process();
					p.StartInfo = new ProcessStartInfo(exePath)
					{
						WorkingDirectory = Path.GetDirectoryName(exePath)
					};
					p.Start();
					_ = TrackGameSessionAsync(p, profile, exePath);
				}
			}
			else
			{
				SpeakBox(Loc.T("launch.exeNotFound", exeName, gamePath));
			}
		}
		catch (Exception ex)
		{
			SpeakBox(Loc.T("launch.failed", FriendlyError(ex)));
		}
	}

	/// <summary>
	/// Asks Steam to start the game, returning <c>true</c> when that was done.
	///
	/// Only for games the manager starts through their own executable. Where a mod loader has a launcher of its
	/// own — SMAPI, SKSE, F4SE — that launcher must be the thing that runs, or the mods don't load at all, so
	/// those are never routed through Steam. BepInEx has no launcher (it hooks the game through a DLL beside the
	/// exe, which loads however the game is started), so Moonlight Peaks both can and should go through Steam.
	///
	/// Declines for a copy that isn't a Steam install, so a GOG or otherwise non-Steam game still starts directly.
	/// </summary>
	private bool TryLaunchViaSteam(GameProfile profile, string gamePath)
	{
		try
		{
			if (!string.IsNullOrEmpty(profile.LoaderExeName)) return false;
			if (string.IsNullOrEmpty(profile.SteamAppId)) return false;
			if (string.IsNullOrEmpty(GetSteamInstallPath())) return false;

			// Confirm this really is the Steam copy: Steam's own library records have to point at the same folder
			// the session is using. Without this a non-Steam copy sitting elsewhere would be abandoned in favour
			// of whatever Steam happens to have installed.
			string steamFolder = DetectSteamLibraryGameFolder(profile.SteamAppId);
			if (string.IsNullOrEmpty(steamFolder)) return false;

			string a = Path.GetFullPath(gamePath).TrimEnd(Path.DirectorySeparatorChar);
			string b = Path.GetFullPath(steamFolder).TrimEnd(Path.DirectorySeparatorChar);
			if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return false;

			Process.Start(new ProcessStartInfo($"steam://rungameid/{profile.SteamAppId}") { UseShellExecute = true });
			return true;
		}
		catch (Exception ex)
		{
			// Steam refused or isn't reachable — fall back to starting the executable directly.
			LogError("LaunchGame", "Could not launch through Steam: " + ex.Message);
			return false;
		}
	}

	/// <summary>
	/// How long to wait for the game's process to appear. Generous, because when the launch is handed to Steam
	/// and Steam isn't running yet, it has to start up and sign in before the game even begins loading.
	/// </summary>
	private const int GameAppearGraceSeconds = 60;

	/// <summary>
	/// How long the game must be absent before a young session is called closed.
	///
	/// A Steam game started from its own executable restarts itself through Steam, and on Moonlight Peaks that
	/// restart was measured at about 15 seconds — during which no process of the game exists at all. The first
	/// run gets far enough to load BepInEx and for mods to announce themselves, so it cannot be told apart from
	/// a real session by how long it lived. Waiting this long before believing the game has closed is what
	/// stops the manager announcing a shutdown in the middle of the restart.
	/// </summary>
	private const int GameRestartSettleSeconds = 45;

	/// <summary>
	/// How long the game must be absent before an established session is called closed. Short, because a Steam
	/// restart only ever happens at startup — once the game has been up for a while, its process disappearing
	/// really does mean the user quit, and they should hear so promptly.
	/// </summary>
	private const int GameCloseSettleSeconds = 8;

	/// <summary>How long a session counts as "young", i.e. still within range of a startup restart.</summary>
	private const int GameYoungSessionSeconds = 180;

	/// <summary>
	/// A launcher we started that hands off to the game exits within seconds (a script extender's loader
	/// injects and quits). One that is still alive after this long is not a launcher but the game's host —
	/// SMAPI runs Stardew Valley inside its own process — so its exit is the end of the session.
	/// </summary>
	private const int GameLauncherLifetimeSeconds = 30;

	/// <summary>True when at least one process for <paramref name="gameExeName"/> is running.</summary>
	private static bool IsGameProcessRunning(string gameExeName)
	{
		Process[] found = Array.Empty<Process>();
		try
		{
			found = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(gameExeName));
			return found.Length > 0;
		}
		catch
		{
			return false;
		}
		finally
		{
			foreach (Process proc in found) proc.Dispose();
		}
	}

	/// <summary>
	/// Follows a launched game until it has really exited, then announces that and returns the status bar to rest.
	///
	/// The process the manager starts is often not the process the user ends up playing. A Steam game launched
	/// from its own executable typically re-launches itself through Steam and the copy we started exits within
	/// seconds — so treating our process handle ending as "the game closed" announced the game shut down while it
	/// was still loading, and then re-announced the Nexus connection over the top of it. That is why this watches
	/// for the game <em>by name</em> after our handle goes away, and only calls it closed once nothing by that
	/// name is running any more.
	/// </summary>
	private async Task TrackGameSessionAsync(Process? launched, GameProfile profile, string launchedExePath)
	{
		string gameExeName = profile.GameExeName;
		bool launchedIsGameExe = string.Equals(
			Path.GetFileNameWithoutExtension(launchedExePath),
			Path.GetFileNameWithoutExtension(gameExeName),
			StringComparison.OrdinalIgnoreCase);

		DateTime launchedAt = DateTime.UtcNow;
		bool announcedRunning = false;

		void AnnounceRunningOnce()
		{
			if (announcedRunning) return;
			announcedRunning = true;
			SetStatus(Loc.T("launch.gameRunning"));
		}

		try
		{
			// Give it a moment to get going before saying it is up. Ask by name as well as by our own handle,
			// since a handoff to Steam's copy may already have happened.
			await Task.Delay(3000);
			bool ourProcessAlive = false;
			try { ourProcessAlive = launched != null && !launched.HasExited; } catch { }
			if (ourProcessAlive || IsGameProcessRunning(gameExeName)) AnnounceRunningOnce();

			TimeSpan ourProcessLived = TimeSpan.Zero;
			if (launched != null)
			{
				try { await launched.WaitForExitAsync(); } catch { }
				ourProcessLived = DateTime.UtcNow - launchedAt;
			}

			// What we started has gone. Was it the game itself, or something that starts the game? Handing the
			// launch to Steam leaves us no process of our own, so there is nothing to have hosted the game.
			bool weHostedTheGame =
				launched != null &&
				!launchedIsGameExe &&
				ourProcessLived >= TimeSpan.FromSeconds(GameLauncherLifetimeSeconds) &&
				!IsGameProcessRunning(gameExeName);

			if (!weHostedTheGame)
			{
				// Either a launcher that handed off, or the game's own executable — which may restart itself
				// through Steam. Wait for the game to appear, then follow it by name, tolerating it vanishing
				// and coming back.
				DateTime appearDeadline = DateTime.UtcNow.AddSeconds(GameAppearGraceSeconds);
				while (DateTime.UtcNow < appearDeadline && !IsGameProcessRunning(gameExeName))
					await Task.Delay(1000);

				DateTime? absentSince = IsGameProcessRunning(gameExeName) ? null : DateTime.UtcNow;
				while (true)
				{
					if (IsGameProcessRunning(gameExeName))
					{
						AnnounceRunningOnce();
						absentSince = null;
					}
					else
					{
						absentSince ??= DateTime.UtcNow;
						// A startup restart is only plausible early on, so a young session is given long enough
						// for one; an established session that has gone is simply over.
						bool young = (DateTime.UtcNow - launchedAt).TotalSeconds < GameYoungSessionSeconds;
						int settleSeconds = young ? GameRestartSettleSeconds : GameCloseSettleSeconds;
						if ((DateTime.UtcNow - absentSince.Value).TotalSeconds >= settleSeconds) break;
					}
					await Task.Delay(2000);
				}
			}
		}
		catch (Exception ex)
		{
			LogError("LaunchGame", "Could not follow the game process: " + ex.Message);
		}
		finally
		{
			try { launched?.Dispose(); } catch { }
		}

		SetStatus(Loc.T("launch.gameClosed"));
		// If the game did manage to rewrite plugins.txt (the guard is off, or the file was writable),
		// put the manager's order — including the active Creations — back now that it has let go.
		RestorePluginOrderAfterPlay();
		await Task.Delay(5000);
		// Return the title to its resting state silently: "game closed" has already been spoken, and speaking
		// the Nexus connection on top of it just sounds like something else happened.
		ResetStatus();
	}
}
