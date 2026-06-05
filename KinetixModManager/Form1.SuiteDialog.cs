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
			Text = $"{gameName} Accessibility Suite Installer - Escape to Close",
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
			Text = $"{gameName} Accessibility Suite Status",
			Font = new Font("Segoe UI", 14f, FontStyle.Bold),
			AutoSize = true,
			Dock = DockStyle.Fill
		};
		layout.Controls.Add(lblTitle, 0, 0);

		ListBox lstStatus = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			AccessibleName = "Accessibility Mods Status"
		};

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
			suiteItems.Add(new SuiteItem("Accessible Tiles", HasModUniqueId("AccessibleTiles"), "Nexus", "10755"));
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
			suiteItems.Add(new SuiteItem("SSE Engine Fixes (Part 1)", HasModNameContains("SSE Engine Fixes") || HasModNameContains("EngineFixes"), "Nexus", "17230"));
			suiteItems.Add(new SuiteItem("SSE Engine Fixes (Part 2)", File.Exists(Path.Combine(gameFolder, "d3dx9_42.dll")), "Nexus", "17230"));
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
			string statusText = item.IsInstalled ? "Installed" : "Not Installed";
			lstStatus.Items.Add($"{item.Name}: {statusText}");
			if (!item.IsInstalled && (item.Type != "Loader" || game != "StardewValley"))
			{
				allModsInstalled = false;
			}
		}

		Button btnInstall = new Button
		{
			Text = allModsInstalled ? "Suite Installed" : "Install Missing Suite Mods",
			Enabled = !allModsInstalled,
			Height = 45,
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f, FontStyle.Bold)
		};

		lstStatus.KeyDown += (s, e) =>
		{
			if (e.KeyCode == Keys.Enter && btnInstall.Enabled)
			{
				btnInstall.PerformClick();
				e.Handled = true;
			}
		};

		layout.Controls.Add(lstStatus, 0, 1);
		layout.Controls.Add(btnInstall, 0, 2);

		bool isInstalling = false;
		btnInstall.Click += async delegate
		{
			if (isInstalling) return;
			isInstalling = true;
			btnInstall.Enabled = false;
			btnInstall.Text = "Installing...";
			dialog.UseWaitCursor = true;

			try
			{
				Speak("Starting accessibility suite installation.");
				
				if (!loaderInstalled && game == "StardewValley")
				{
					Speak("Warning: SMAPI is not detected. Please install it manually.");
					MessageBox.Show("We recommend installing SMAPI to run accessibility mods. Opening download link...", "SMAPI Missing");
					Process.Start(new ProcessStartInfo(suiteItems[0].Source) { UseShellExecute = true });
				}

				foreach (var item in suiteItems)
				{
					if (item.IsInstalled) continue;
					if (item.Type == "Loader" && game == "StardewValley") continue;

					SetStatus($"Downloading {item.Name}...");
					Speak($"Downloading {item.Name}...");

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
						Speak($"{item.Name} must be downloaded manually from Nexus. Opening browser.");
						Process.Start(new ProcessStartInfo($"https://www.nexusmods.com/{gameDomain}/mods/{item.Source}?tab=files") { UseShellExecute = true });
						MessageBox.Show($"Please click 'Manual Download' on the webpage for {item.Name}. After downloading, press Control+I in the mod manager to install the ZIP.", "Manual Download Required");
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
							
							Speak($"{item.Name} must be downloaded manually from Nexus. Opening browser.");
							Process.Start(new ProcessStartInfo(downloadUrl) { UseShellExecute = true });
							MessageBox.Show($"Please click 'Manual Download' on the webpage for {item.Name}. After downloading, extract its contents directly into your game install folder.", "Manual Download Required");
							continue;
						}

						try
						{
							byte[] bytes = await _nexusService.DownloadBytesAsync(downloadUrl);
							string tempPath = Path.Combine(downloadsPath, zipName);
							File.WriteAllBytes(tempPath, bytes);

							if (item.Type == "Loader")
							{
								SetStatus($"Installing {item.Name}...");
								Speak($"Installing {item.Name}...");
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
							Speak($"Failed to install {item.Name}.");
						}
					}
				}

				Speak("Accessibility suite setup complete. Refreshing mod list.");
				dialog.Close();
			}
			catch (Exception ex)
			{
				MessageBox.Show("Suite installation error: " + ex.Message);
			}
			finally
			{
				dialog.UseWaitCursor = false;
				btnInstall.Enabled = true;
				btnInstall.Text = "Install Missing Suite Mods";
				isInstalling = false;
			}
		};

		dialog.Controls.Add(layout);
		dialog.ShowDialog();
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
