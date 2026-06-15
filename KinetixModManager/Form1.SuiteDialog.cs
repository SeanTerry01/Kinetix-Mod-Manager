using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
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

/// <summary>The accessibility-mod suite installer dialog and its helpers for Form1.</summary>
public partial class Form1
{
	private void ShowAccessibilitySuiteDialog()
	{
		string game = _settings.ActiveGame;
		string gameName = game switch
		{
			"SkyrimSE" => "Skyrim Special Edition",
			"Fallout4" => "Fallout 4",
			_ => "Stardew Valley"
		};

		Form dialog = new Form
		{
			Text = Loc.T("suite.installerTitle", gameName),
			Size = new Size(500, 450),
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
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 15f));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65f));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));

		Label lblTitle = new Label
		{
			Text = Loc.T("suite.statusTitle", gameName),
			Font = new Font("Segoe UI", 14f, FontStyle.Bold),
			AutoSize = true,
			Dock = DockStyle.Fill
		};
		layout.Controls.Add(lblTitle, 0, 0);

		ListBox lstStatus = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			AccessibleName = Loc.T("suite.statusName"),
			AccessibleDescription = Loc.T("suite.statusDesc")
		};
		// Announce the position on focus the same way the main form's lists do (GotFocus so it
		// also fires when focus returns to the list after another dialog closes).
		lstStatus.GotFocus += List_Enter;

		bool loaderInstalled = false;
		bool allModsInstalled = true;
		var suiteItems = new List<SuiteItem>();

		if (game == "StardewValley")
		{
			string gameFolder = DetectGameFolder("StardewValley");
			string smapiPath = Path.Combine(gameFolder, "StardewModdingAPI.exe");
			loaderInstalled = File.Exists(smapiPath);

			suiteItems.Add(new SuiteItem("SMAPI (Mod Loader)", loaderInstalled, "Loader", "https://smapi.io"));
			suiteItems.Add(new SuiteItem("Stardew Access", HasModUniqueId("StardewAccess"), "GitHub", "stardew-access/stardew-access"));
			suiteItems.Add(new SuiteItem("Kokoro Library", HasModUniqueId("Kokoro"), "GitHubStatic", "https://github.com/Shockah/Stardew-Valley-Mods/releases/download/release%2Fkokoro%2F3.0.0/Kokoro.3.0.0.zip"));
			suiteItems.Add(new SuiteItem("Project Fluent", HasModUniqueId("ProjectFluent"), "GitHubStatic", "https://github.com/Shockah/Stardew-Valley-Mods/releases/download/release%2Fproject-fluent%2F2.0.0/ProjectFluent.2.0.0.zip"));
		}
		else if (game == "SkyrimSE")
		{
			string gameFolder = string.IsNullOrEmpty(_settings.CurrentGamePath) ? DetectGameFolder("SkyrimSE") : _settings.CurrentGamePath;
			loaderInstalled = File.Exists(Path.Combine(gameFolder, "skse64_loader.exe"));

			suiteItems.Add(new SuiteItem("SKSE64 (Script Extender)", loaderInstalled, "Loader", "https://skse.silverlock.org"));
			suiteItems.Add(new SuiteItem("Address Library for SKSE Plugins", HasModNameContains("Address Library") || HasModNameContains("AddressLibrary"), "Nexus", "32444"));
			suiteItems.Add(new SuiteItem("SkyUI", HasModNameContains("SkyUI"), "Nexus", "12604"));
			suiteItems.Add(new SuiteItem("Better MessageBox Controls", HasModNameContains("Better MessageBox Controls") || HasModNameContains("BetterMessageBoxControls"), "Nexus", "1428"));
			suiteItems.Add(new SuiteItem("UIExtensions", HasModNameContains("UIExtensions"), "Nexus", "17561"));
			suiteItems.Add(new SuiteItem("powerofthree's Papyrus Extender", HasModNameContains("Papyrus Extender") || HasModNameContains("PapyrusExtender"), "Nexus", "22854"));
			suiteItems.Add(new SuiteItem("powerofthree's Tweaks", HasModNameContains("powerofthree's Tweaks") || HasModNameContains("powerofthree'sTweaks") || HasModNameContains("po3's Tweaks"), "Nexus", "51073"));
			suiteItems.Add(new SuiteItem("Dylbills Papyrus Functions", HasModNameContains("Dylbills Papyrus Functions") || HasModNameContains("DylbillsPapyrusFunctions") || HasModNameContains("DbMiscFunctions"), "Nexus", "65410"));
			// SSE Engine Fixes ships as two files on the same Nexus page: the main SKSE plugin
			// (installs like a normal mod) and a "Preloader" whose d3dx9_42.dll must sit in the
			// game root. Treat them as one entry that is only "installed" when BOTH are present;
			// the installer auto-handles both parts (see InstallEngineFixesAsync).
			suiteItems.Add(new SuiteItem("SSE Engine Fixes",
				(HasModNameContains("SSE Engine Fixes") || HasModNameContains("EngineFixes"))
					&& File.Exists(Path.Combine(gameFolder, "d3dx9_42.dll")),
				"Nexus", "17230"));
			suiteItems.Add(new SuiteItem("Media Keys Fix", HasModNameContains("Media Keys Fix") || HasModNameContains("MediaKeysFix"), "Nexus", "92948"));
			suiteItems.Add(new SuiteItem("Stay At The System Page - AE", HasModNameContains("Stay At The System Page") || HasModNameContains("StayAtTheSystemPage"), "Nexus", "67883"));
			suiteItems.Add(new SuiteItem("Skyrim Access", HasModNameContains("Skyrim Access") || HasModNameContains("SkyrimAccess") || HasModNameContains("SkyrimTTS"), "Nexus", "181131"));
			suiteItems.Add(new SuiteItem("SkyrimAccessibility", HasModNameContains("SkyrimAccessibility") || HasModNameContains("Skyrim Accessibility"), "GitHub", "DioKyrie-Git/SkyrimAccessibility"));
		}
		else
		{
			string gameFolder = string.IsNullOrEmpty(_settings.CurrentGamePath) ? DetectGameFolder("Fallout4") : _settings.CurrentGamePath;
			loaderInstalled = File.Exists(Path.Combine(gameFolder, "f4se_loader.exe"));

			suiteItems.Add(new SuiteItem("F4SE (Script Extender)", loaderInstalled, "Loader", "https://f4se.silverlock.org"));
			suiteItems.Add(new SuiteItem("Mod Configuration Menu (MCM)", HasModNameContains("Mod Configuration Menu") || HasModNameContains("MCM"), "Nexus", "21497"));
			suiteItems.Add(new SuiteItem("Fallout 4 Access", HasModNameContains("Fallout4Access") || HasModNameContains("Fallout 4 Access"), "Nexus", "100314"));
		}

		foreach (var item in suiteItems)
		{
			string statusText = item.IsInstalled ? Loc.T("suite.installed") : Loc.T("suite.notInstalled");
			lstStatus.Items.Add(Loc.T("suite.statusLine", item.Name, statusText));
			// SMAPI (the Stardew "Loader") now installs automatically too, so a missing loader
			// should also enable the Install button rather than being treated as already handled.
			if (!item.IsInstalled)
			{
				allModsInstalled = false;
			}
		}
		if (lstStatus.Items.Count > 0)
		{
			lstStatus.SelectedIndex = 0;
		}

		Button btnInstall = new Button
		{
			Text = allModsInstalled ? Loc.T("suite.suiteInstalled") : Loc.T("suite.installMissing"),
			Enabled = !allModsInstalled,
			Height = 45,
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f, FontStyle.Bold)
		};

		// Resolves the best "go download it yourself" page for a suite entry. Nexus mods open straight to
		// their Files tab; loaders open their official site; GitHub mods open their Releases page; and
		// GitHubStatic entries are a direct zip link.
		string SuiteItemUrl(SuiteItem item)
		{
			switch (item.Type)
			{
				case "Nexus":
					string domain = game switch
					{
						"SkyrimSE" => "skyrimspecialedition",
						"Fallout4" => "fallout4",
						_          => "stardewvalley"
					};
					return $"https://www.nexusmods.com/{domain}/mods/{item.Source}?tab=files";
				case "GitHub":
					return $"https://github.com/{item.Source}/releases";
				default: // "Loader" (site URL) and "GitHubStatic" (direct zip link)
					return item.Source;
			}
		}

		// Enter on a list item opens that single mod's download page, so the user can grab missing mods
		// one at a time at their own pace instead of running the whole bulk install. (The bulk install is
		// still available on the button below via Tab.)
		lstStatus.KeyDown += (s, e) =>
		{
			if (e.KeyCode != Keys.Enter) return;
			int idx = lstStatus.SelectedIndex;
			if (idx < 0 || idx >= suiteItems.Count) return;

			e.Handled = true;
			e.SuppressKeyPress = true;
			var item = suiteItems[idx];
			string url = SuiteItemUrl(item);
			Speak(item.IsInstalled
				? Loc.T("suite.openInstalledPage", item.Name)
				: Loc.T("suite.openDownloadPage", item.Name));
			try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
			catch (Exception ex) { LogError(item.Name, "Failed to open download page: " + ex.Message); }
		};

		layout.Controls.Add(lstStatus, 0, 1);
		layout.Controls.Add(btnInstall, 0, 2);

		bool isInstalling = false;
		btnInstall.Click += async delegate
		{
			if (isInstalling) return;
			isInstalling = true;
			btnInstall.Enabled = false;
			btnInstall.Text = Loc.T("suite.installing");
			dialog.UseWaitCursor = true;

			try
			{
				Speak(Loc.T("suite.startInstall"));
				
				// SMAPI is the Stardew mod loader; install it first (and automatically) so the
				// accessibility mods below have something to load them. Other games' loaders are
				// handled inside the loop via the script-extender installer.
				if (!loaderInstalled && game == "StardewValley")
				{
					loaderInstalled = await InstallSmapiAsync(DetectGameFolder("StardewValley"));
				}

				foreach (var item in suiteItems)
				{
					if (item.IsInstalled) continue;
					if (item.Type == "Loader" && game == "StardewValley") continue;

					// SSE Engine Fixes is a two-part install (main mod + root-folder preloader);
					// handle both parts together so the user never has to place the DLL manually.
					if (game == "SkyrimSE" && item.Source == "17230")
					{
						await InstallEngineFixesAsync();
						continue;
					}

					SetStatus(Loc.T("suite.downloading", item.Name));
					Speak(Loc.T("suite.downloading", item.Name));

					string? downloadUrl = null;
					string zipName = $"{item.Name.Replace(" ", "")}_Install" + (item.Type == "Loader" ? ".7z" : ".zip");

					if (item.Type == "Loader")
					{
						if (game == "SkyrimSE")
						{
							downloadUrl = await GetSkse64DownloadUrl();
						}
						else if (game == "Fallout4")
						{
							downloadUrl = await GetF4seDownloadUrl();
						}
					}
					else if (item.Type == "GitHub")
					{
						downloadUrl = await GetGitHubLatestReleaseZipUrl(item.Source);
					}
					else if (item.Type == "GitHubStatic")
					{
						downloadUrl = item.Source;
					}
					else if (item.Type == "Nexus")
					{
						if (_nexusService.IsPremium)
						{
							try
							{
								var tempMod = new GameMod { NexusID = item.Source, Name = item.Name };
								string tempPath = await _nexusService.DownloadModUpdateAsync(tempMod, downloadsPath);
								await InstallFromZip(tempPath, item.Source);
								continue;
							}
							catch (Exception ex)
							{
								LogError(item.Name, $"Nexus download failed: {ex.Message}");
							}
						}
						
						string gameDomain = game switch
						{
							"SkyrimSE" => "skyrimspecialedition",
							"Fallout4" => "fallout4",
							_ => "stardewvalley"
						};
						Speak(Loc.T("suite.manualDownloadSpeak", item.Name));
						Process.Start(new ProcessStartInfo($"https://www.nexusmods.com/{gameDomain}/mods/{item.Source}?tab=files") { UseShellExecute = true });
						MessageBox.Show(Loc.T("suite.manualDownloadBox1", item.Name), Loc.T("suite.manualDownloadTitle"));
						continue;
					}

					if (!string.IsNullOrEmpty(downloadUrl))
					{
						if (downloadUrl.Contains("nexusmods.com"))
						{
							string nexusId = "42147"; // F4SE mod ID
							if (_nexusService.IsPremium)
							{
								try
								{
									var tempMod = new GameMod { NexusID = nexusId, Name = item.Name };
									string tempPath = await _nexusService.DownloadModUpdateAsync(tempMod, downloadsPath);
									if (item.Type == "Loader")
									{
										await ModFileSystem.InstallScriptExtenderAsync(tempPath, _settings.CurrentGamePath, game, LogError, _nexusService);
									}
									else
									{
										await InstallFromZip(tempPath, nexusId);
									}
									continue;
								}
								catch (Exception ex)
								{
									LogError(item.Name, $"Nexus download failed: {ex.Message}");
								}
							}
							
							Speak(Loc.T("suite.manualDownloadSpeak", item.Name));
							Process.Start(new ProcessStartInfo(downloadUrl) { UseShellExecute = true });
							MessageBox.Show(Loc.T("suite.manualDownloadBox2", item.Name), Loc.T("suite.manualDownloadTitle"));
							continue;
						}

						try
						{
							byte[] bytes = await _nexusService.DownloadBytesAsync(downloadUrl);
							string tempPath = Path.Combine(downloadsPath, zipName);
							File.WriteAllBytes(tempPath, bytes);

							if (item.Type == "Loader")
							{
								SetStatus(Loc.T("suite.installingItem", item.Name));
								Speak(Loc.T("suite.installingItem", item.Name));
								await ModFileSystem.InstallScriptExtenderAsync(tempPath, _settings.CurrentGamePath, game, LogError, _nexusService);
							}
							else
							{
								await InstallFromZip(tempPath);
							}
						}
						catch (Exception ex)
						{
							LogError(item.Name, $"Download or extraction failed: {ex.Message}");
							Speak(Loc.T("suite.failedInstallItem", item.Name));
						}
					}
				}

				Speak(Loc.T("suite.setupComplete"));
				dialog.Close();
			}
			catch (Exception ex)
			{
				MessageBox.Show(Loc.T("suite.installErrorBox", ex.Message));
			}
			finally
			{
				dialog.UseWaitCursor = false;
				btnInstall.Enabled = true;
				btnInstall.Text = Loc.T("suite.installMissing");
				isInstalling = false;
			}
		};

		// Focusing the list raises its Enter handler (List_Enter), which announces the position
		// after the screen reader reads the list name and selected item — same as every other list.
		dialog.Shown += (s, e) => lstStatus.Focus();

		dialog.Controls.Add(layout);
		dialog.ShowDialog();
	}

	/// <summary>
	/// Installs both halves of SSE Engine Fixes (Nexus mod 17230): the main SKSE plugin into the
	/// mods folder, and the "Preloader" <c>d3dx9_42.dll</c> into the game root. Premium users get a
	/// fully automatic install; everyone else is sent to the files page with correct instructions
	/// for both files. Only the missing half is fetched.
	/// </summary>
	private async Task InstallEngineFixesAsync()
	{
		string gameFolder = string.IsNullOrEmpty(_settings.CurrentGamePath) ? DetectGameFolder("SkyrimSE") : _settings.CurrentGamePath;
		bool mainInstalled = HasModNameContains("SSE Engine Fixes") || HasModNameContains("EngineFixes");
		bool preloaderInstalled = File.Exists(Path.Combine(gameFolder, "d3dx9_42.dll"));

		if (_nexusService.IsPremium)
		{
			if (!mainInstalled)
			{
				try
				{
					SetStatus(Loc.T("suite.engineFixesDownloading"));
					Speak(Loc.T("suite.engineFixesDownloading"));
					var mainMod = new GameMod { NexusID = "17230", Name = "SSE Engine Fixes Part 1" };
					string mainZip = await _nexusService.DownloadModUpdateAsync(mainMod, downloadsPath);
					await InstallFromZip(mainZip, "17230");
				}
				catch (Exception ex)
				{
					LogError("SSE Engine Fixes", $"Main file download failed: {ex.Message}");
					Speak(Loc.T("suite.engineFixesMainFailed"));
				}
			}

			if (!preloaderInstalled)
			{
				try
				{
					SetStatus(Loc.T("suite.engineFixesPreloaderInstalling"));
					Speak(Loc.T("suite.engineFixesPreloaderInstalling"));
					var preMod = new GameMod { NexusID = "17230", Name = "SSE Engine Fixes Part 2" };
					string preZip = await _nexusService.DownloadModUpdateAsync(preMod, downloadsPath);
					await ModFileSystem.InstallEnginePreloaderAsync(preZip, gameFolder, LogError, _nexusService);
				}
				catch (Exception ex)
				{
					LogError("SSE Engine Fixes", $"Preloader download failed: {ex.Message}");
					Speak(Loc.T("suite.engineFixesPreloaderFailed"));
				}
			}
			return;
		}

		// Non-premium accounts cannot download through the Nexus API, so guide the manual install
		// for whichever parts are still missing.
		Speak(Loc.T("suite.engineFixesManualSpeak"));
		Process.Start(new ProcessStartInfo("https://www.nexusmods.com/skyrimspecialedition/mods/17230?tab=files") { UseShellExecute = true });
		MessageBox.Show(
			Loc.T("suite.engineFixesManualBox"),
			Loc.T("suite.manualDownloadTitle"));
	}

	/// <summary>
	/// Downloads the latest SMAPI installer from its GitHub release and runs it unattended against the
	/// detected Stardew Valley folder, so the user never has to drive SMAPI's interactive console
	/// installer. SMAPI's installer accepts <c>--install</c>, <c>--game-path</c> and <c>--no-prompt</c>
	/// for exactly this scripted scenario. Falls back to opening smapi.io in the browser whenever the
	/// game folder, download, or installer can't be resolved, or the result can't be confirmed. Returns
	/// true only when <c>StardewModdingAPI.exe</c> is present in the game folder afterwards.
	/// </summary>
	private async Task<bool> InstallSmapiAsync(string gameFolder)
	{
		// SMAPI can only install into a real Stardew Valley folder; bail to the manual flow otherwise.
		if (string.IsNullOrEmpty(gameFolder) || !File.Exists(Path.Combine(gameFolder, "Stardew Valley.exe")))
		{
			Speak(Loc.T("suite.smapiNotFoundSpeak"));
			Process.Start(new ProcessStartInfo("https://smapi.io") { UseShellExecute = true });
			MessageBox.Show(
				Loc.T("suite.smapiNotFoundBox"),
				Loc.T("suite.smapiInstallTitle"));
			return false;
		}

		string tempDir = Path.Combine(Path.GetTempPath(), "SMAPI_" + Path.GetRandomFileName());
		try
		{
			SetStatus(Loc.T("suite.smapiDownloading"));
			Speak(Loc.T("suite.smapiDownloading"));

			string? url = await GetSmapiInstallerZipUrl();
			if (string.IsNullOrEmpty(url)) throw new Exception("Could not resolve the SMAPI download URL.");

			byte[] bytes = await _nexusService.DownloadBytesAsync(url);
			Directory.CreateDirectory(tempDir);
			string zipPath = Path.Combine(tempDir, "SMAPI-installer.zip");
			File.WriteAllBytes(zipPath, bytes);

			string extractDir = Path.Combine(tempDir, "extracted");
			await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractDir));

			// The installer sits at "<top-level installer folder>/internal/windows/SMAPI.Installer.exe";
			// search for it so we don't depend on the version number in the folder name.
			string marker = Path.Combine("internal", "windows");
			string? installerExe = Directory.EnumerateFiles(extractDir, "SMAPI.Installer.exe", SearchOption.AllDirectories)
					.FirstOrDefault(p => p.Contains(marker, StringComparison.OrdinalIgnoreCase))
				?? Directory.EnumerateFiles(extractDir, "SMAPI.Installer.exe", SearchOption.AllDirectories).FirstOrDefault();
			if (installerExe == null) throw new Exception("SMAPI.Installer.exe was not found in the download.");

			SetStatus(Loc.T("suite.smapiInstallingStatus"));
			Speak(Loc.T("suite.smapiInstalling"));

			var psi = new ProcessStartInfo(installerExe)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = Path.GetDirectoryName(installerExe)!
			};
			psi.ArgumentList.Add("--install");
			psi.ArgumentList.Add("--game-path");
			psi.ArgumentList.Add(gameFolder);
			psi.ArgumentList.Add("--no-prompt");

			await Task.Run(() =>
			{
				using Process? proc = Process.Start(psi);
				proc?.WaitForExit();
			});

			bool installed = File.Exists(Path.Combine(gameFolder, "StardewModdingAPI.exe"));
			if (installed)
			{
				Speak(Loc.T("suite.smapiInstalled"));
			}
			else
			{
				Speak(Loc.T("suite.smapiUnconfirmed"));
				Process.Start(new ProcessStartInfo("https://smapi.io") { UseShellExecute = true });
			}
			return installed;
		}
		catch (Exception ex)
		{
			LogError("SMAPI", "Automatic SMAPI install failed: " + ex.Message);
			Speak(Loc.T("suite.smapiAutoFailed"));
			Process.Start(new ProcessStartInfo("https://smapi.io") { UseShellExecute = true });
			return false;
		}
		finally
		{
			try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
		}
	}

	private bool HasModUniqueId(string uniqueId)
	{
		return _allInstalledMods.Any(m => m.UniqueId.Equals(uniqueId, StringComparison.OrdinalIgnoreCase));
	}

	private bool HasModNameContains(string subStr)
	{
		return _allInstalledMods.Any(m => m.Name.Contains(subStr, StringComparison.OrdinalIgnoreCase) || 
										  m.UniqueId.Contains(subStr, StringComparison.OrdinalIgnoreCase));
	}

	private class SuiteItem
	{
		public string Name { get; }
		public bool IsInstalled { get; }
		public string Type { get; }
		public string Source { get; }

		public SuiteItem(string name, bool isInstalled, string type, string source)
		{
			Name = name;
			IsInstalled = isInstalled;
			Type = type;
			Source = source;
		}
	}
}
