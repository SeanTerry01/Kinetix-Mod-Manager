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

/// <summary>Mod installation, NXM handling, backups, and update-all for Form1.</summary>
public partial class Form1
{
	/// <summary>
	/// Downloads and installs every mod currently listed in the Updates tab, one at a time.
	/// Guarded by a flag to prevent concurrent runs.
	/// </summary>
	private async Task UpdateAllMods()
	{
		if (isUpdatingAll || listUpdates.Items.Count == 0)
		{
			return;
		}
		if (!_nexusService.IsPremium)
		{
			Speak("Update All is a Nexus Mods Premium feature. Free users must update mods individually via the browser.");
			MessageBox.Show("Updating multiple mods automatically requires a Nexus Mods Premium account. Free users must download and install updates one-by-one.", "Premium Feature Required", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		else
		{
			if (MessageBox.Show($"Update all {listUpdates.Items.Count} mods?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.No)
			{
				return;
			}
			isUpdatingAll = true;
			try
			{
				List<StardewMod> mods = listUpdates.Items.Cast<StardewMod>().ToList();
				for (int i = 0; i < mods.Count; i++)
				{
					SetStatus($"Updating ({i + 1}/{mods.Count}): {mods[i].Name}");
					await DownloadAndInstallUpdate(mods[i], silent: true);
				}
				Speak("All updates finished.");
			}
			catch (Exception ex)
			{
				LogError("Updates", "Update All failed: " + ex.Message);
				Speak("Update all failed: " + ex.Message);
			}
			finally
			{
				isUpdatingAll = false;
				_ = RefreshModList(checkUpdates: true);
			}
		}
	}

	/// <summary>Opens the Nexus Mods page for the selected mod in the default browser.</summary>
	private void OpenModPage()
	{
		ListBox listBox;
		if (mainTabs.SelectedIndex == (int)AppTab.Installed)
		{
			listBox = listInstalled;
		}
		else if (mainTabs.SelectedIndex == (int)AppTab.Updates)
		{
			listBox = listUpdates;
		}
		else
		{
			if (mainTabs.SelectedIndex != (int)AppTab.Discovery)
			{
				return;
			}
			listBox = listDiscovery;
		}
		if (listBox.SelectedItem is StardewMod stardewMod && !string.IsNullOrEmpty(stardewMod.NexusID))
		{
			Process.Start(new ProcessStartInfo($"https://www.nexusmods.com/{_nexusService.CurrentGameDomain}/mods/{stardewMod.NexusID}?tab=files")
			{
				UseShellExecute = true
			});
		}
	}

	/// <summary>
	/// Creates a timestamped .zip backup of a mod folder under the backups directory,
	/// then calls <see cref="PruneBackupsForMod"/> to enforce the per-mod backup limit.
	/// </summary>
	private void BackupMod(string folderPath, string modName)
	{
		try
		{
			ModFileSystem.CreateBackup(folderPath, modName, backupsPath);
			ModFileSystem.PruneBackups(modName, backupsPath, _settings.MaxBackupsPerMod);
			RefreshBackupsList();
		}
		catch (Exception ex)
		{
			LogError(modName, "Backup Error: " + ex.Message);
		}
	}

	/// <summary>Runs <see cref="ModFileSystem.PruneBackups"/> for every mod that has backups on disk.</summary>
	private void PruneAllBackups()
	{
		if (!Directory.Exists(backupsPath)) return;
		int before = Directory.GetFiles(backupsPath, "*.zip").Length;
		HashSet<string> modNames = new HashSet<string>();
		foreach (string f in Directory.GetFiles(backupsPath, "*.zip"))
			modNames.Add(Regex.Replace(Path.GetFileNameWithoutExtension(f), @"_\d{8}_\d{6}$", ""));
		foreach (string modName in modNames)
			ModFileSystem.PruneBackups(modName, backupsPath, _settings.MaxBackupsPerMod);
		int deleted = before - Directory.GetFiles(backupsPath, "*.zip").Length;
		RefreshBackupsList();
		Speak($"Pruning complete. Deleted {deleted} old backups.");
	}

	/// <summary>
	/// Parses a <c>nxm://</c> URL, fetches the download link from the Nexus API, downloads
	/// the file, and prompts the user to install it. Requires a valid API key.
	/// </summary>
	private async Task HandleNxmUrl(string url)
	{
		try
		{
			if (url.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
			{
				string withoutProtocol = url.Substring(6);
				int slashIndex = withoutProtocol.IndexOf('/');
				if (slashIndex > 0)
				{
					string gameDomain = withoutProtocol.Substring(0, slashIndex).ToLowerInvariant();
					string? targetGame = gameDomain switch
					{
						"stardewvalley" => "StardewValley",
						"skyrimspecialedition" => "SkyrimSE",
						"fallout4" => "Fallout4",
						_ => null
					};

					if (targetGame != null && _settings.ActiveGame != targetGame)
					{
						if (InvokeRequired)
						{
							Invoke(new Action(() => SwitchActiveGame(targetGame)));
						}
						else
						{
							SwitchActiveGame(targetGame);
						}
						await Task.Delay(500);
					}
				}
			}

			SetStatus("Parsing Nexus Link...");
			var (dlUri, realName) = await _nexusService.ResolveNxmUrlAsync(url);
			SetStatus("Downloading " + realName + "...");
			string path = Path.Combine(downloadsPath, realName);

			int lastReportedPercent = 0;
			var progress = new Progress<double>(pct =>
			{
				int percent = (int)Math.Round(pct);
				if (percent >= lastReportedPercent + 10)
				{
					lastReportedPercent = (percent / 10) * 10;
					SetStatus($"Downloading {realName}... {lastReportedPercent}%");
				}
				else
				{
					Invoke(delegate
					{
						string gameName = _settings.ActiveGame switch
						{
							"SkyrimSE" => "Skyrim Special Edition",
							"Fallout4" => "Fallout 4",
							_ => "Stardew Valley"
						};
						Text = $"{gameName} Kinetix Mod Manager - Status: Downloading {realName}... {percent}%";
					});
				}
			});

			await _nexusService.DownloadFileWithProgressAsync(dlUri, path, progress);
			_soundEngine.Play("connect");
			string? nexusId = null;
			try
			{
				var match = Regex.Match(url, @"/mods/(\d+)(?:/|$)", RegexOptions.IgnoreCase);
				if (match.Success)
				{
					nexusId = match.Groups[1].Value;
				}
			}
			catch { }
			if (MessageBox.Show("Downloaded " + realName + ". Install now?", "Success", MessageBoxButtons.YesNo) == DialogResult.Yes)
				_ = InstallFromZip(path, nexusId);
		}
		catch (Exception ex)
		{
			MessageBox.Show("NXM Error: " + ex.Message);
		}
	}

	/// <summary>Opens a file dialog to select a .zip file and installs it via <see cref="InstallFromZip"/>.</summary>
	private void ManualInstall()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog
		{
			InitialDirectory = downloadsPath,
			Filter = "Zips|*.zip"
		};
		if (openFileDialog.ShowDialog() == DialogResult.OK)
		{
			_ = InstallFromZip(openFileDialog.FileName);
		}
	}

	/// <summary>
	/// Compares two dot-separated version strings. Returns <c>true</c> if <paramref name="target"/>
	/// is numerically greater than <paramref name="current"/>.
	/// </summary>
	private bool IsNewerVersion(string? current, string? target)
	{
		if (string.IsNullOrEmpty(target))
		{
			return false;
		}
		if (string.IsNullOrEmpty(current))
		{
			return true;
		}
		string[] array = current.Split('.');
		string[] array2 = target.Split('.');
		for (int i = 0; i < Math.Max(array.Length, array2.Length); i++)
		{
			int result;
			int num = ((i < array.Length && int.TryParse(array[i], out result)) ? result : 0);
			int result2;
			int num2 = ((i < array2.Length && int.TryParse(array2[i], out result2)) ? result2 : 0);
			if (num2 > num)
			{
				return true;
			}
			if (num > num2)
			{
				return false;
			}
		}
		return false;
	}

	/// <summary>
	/// Extracts a .zip archive to a temp directory, validates all paths against the mods folder
	/// to prevent path traversal, then moves the contents into the Mods directory.
	/// Temp files are cleaned up in a <c>finally</c> block regardless of success or failure.
	/// </summary>
	private async Task InstallFromZip(string zipPath, string? nexusId = null)
	{
		try
		{
			string name = await ModFileSystem.ExtractModAsync(
				zipPath, _settings.CurrentModsPath, _allInstalledMods,
				backupsPath, _settings.MaxBackupsPerMod, _settings.ActiveGame, LogError, nexusId, _nexusService, null, _settings.CurrentGamePath);
			_soundEngine.Play("connect");

			if (_settings.ActiveGame == "SkyrimSE" || _settings.ActiveGame == "Fallout4")
			{
				string modDir = Path.Combine(_settings.CurrentModsPath, name);
				string gameData = Path.Combine(_settings.CurrentGamePath, "Data");
				ModFileSystem.DeployModFiles(modDir, gameData, true, LogError);
				ModFileSystem.SyncPluginsFile(modDir, _settings.ActiveGame, true, LogError);
			}

			await RefreshModList(checkUpdates: false);
			MessageBox.Show(name + " installed!");
		}
		catch (Exception ex)
		{
			MessageBox.Show("Install failed: " + ex.Message);
		}
	}
}
