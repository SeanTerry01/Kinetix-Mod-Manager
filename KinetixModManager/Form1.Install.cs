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
			Speak(Loc.T("updateAll.premiumSpeak"));
			MessageBox.Show(Loc.T("updateAll.premiumBox"), Loc.T("updateAll.premiumTitle"), MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		else
		{
			if (MessageBox.Show(Loc.T("updateAll.confirm", listUpdates.Items.Count), Loc.T("common.confirm"), MessageBoxButtons.YesNo) == DialogResult.No)
			{
				return;
			}
			isUpdatingAll = true;
			try
			{
				List<StardewMod> mods = listUpdates.Items.Cast<StardewMod>().ToList();
				for (int i = 0; i < mods.Count; i++)
				{
					SetStatus(Loc.T("updateAll.updatingStatus", i + 1, mods.Count, mods[i].Name));
					await DownloadAndInstallUpdate(mods[i], silent: true);
				}
				Speak(Loc.T("updateAll.finished"));
			}
			catch (Exception ex)
			{
				LogError("Updates", "Update All failed: " + ex.Message);
				Speak(Loc.T("updateAll.failed", ex.Message));
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
		if (CurrentTab() == AppTab.Installed)
		{
			listBox = listInstalled;
		}
		else if (CurrentTab() == AppTab.Updates)
		{
			listBox = listUpdates;
		}
		else
		{
			if (CurrentTab() != AppTab.Discovery)
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
		Speak(Loc.T("backups.pruneComplete", deleted));
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

			SetStatus(Loc.T("download.parsingLink"));
			var (dlUri, realName) = await _nexusService.ResolveNxmUrlAsync(url);
			SetStatus(Loc.T("download.downloading", realName));
			string path = Path.Combine(downloadsPath, realName);

			int lastReportedPercent = 0;
			var progress = new Progress<double>(pct =>
			{
				int percent = (int)Math.Round(pct);
				if (percent >= lastReportedPercent + 10)
				{
					lastReportedPercent = (percent / 10) * 10;
					SetStatus(Loc.T("download.downloadingPct", realName, lastReportedPercent));
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
						Text = Loc.T("download.titleStatus", gameName, realName, percent);
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
			// The download was started from the browser (Mod Manager Download button), so the browser
			// owns the foreground by now. Pull the manager to the front first, otherwise this prompt can
			// open behind the browser and never receive keyboard / screen-reader focus.
			ForceToForeground();
			if (MessageBox.Show(this, Loc.T("download.installNow", realName), Loc.T("download.successTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
				_ = InstallFromZip(path, nexusId);
		}
		catch (Exception ex)
		{
			MessageBox.Show(Loc.T("download.nxmError", ex.Message));
		}
	}

	/// <summary>
	/// Forces the main window to the foreground and gives it focus. Needed when something the user did in
	/// another app (e.g. clicking "Mod Manager Download" in their browser) hands control back to us and we
	/// need to show a prompt: a background app can't simply <c>Activate()</c> itself, so we restore it if
	/// minimised and use the well-known TopMost flip to pull the window — and any dialog we then open — to
	/// the front where it receives keyboard and screen-reader focus.
	/// </summary>
	private void ForceToForeground()
	{
		try
		{
			if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
			bool wasTopMost = TopMost;
			TopMost = true;
			Activate();
			BringToFront();
			TopMost = wasTopMost;
			Focus();
		}
		catch { /* foreground hint is best-effort */ }
	}

	/// <summary>Opens a file dialog to select a .zip file and installs it via <see cref="InstallFromZip"/>.</summary>
	private void ManualInstall()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog
		{
			InitialDirectory = downloadsPath,
			Filter = Loc.T("install.zipFilter")
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
				backupsPath, _settings.MaxBackupsPerMod, _settings.ActiveGame, LogError, nexusId, _nexusService, null, _settings.CurrentGamePath,
				ShowFomodWizardAsync);
			_soundEngine.Play("connect");

			await RefreshModList(checkUpdates: false);

			// RefreshModList already added the new mod to the priority/plugin order and wrote plugins.txt.
			// Re-sync assets with forceRelink so the new mod's files are linked even on a reinstall that
			// reuses the folder name (where ownership is unchanged), then refresh the priority list.
			if (IsBethesdaGame)
			{
				SyncBethesdaDeployment(new HashSet<string>(new[] { name }, StringComparer.OrdinalIgnoreCase));
				RefreshModPriorityList();
			}
			MessageBox.Show(Loc.T("install.installed", name));
		}
		catch (OperationCanceledException)
		{
			// User cancelled the FOMOD option wizard; it already announced the cancellation.
		}
		catch (Exception ex)
		{
			MessageBox.Show(Loc.T("install.failed", ex.Message));
		}
	}
}
