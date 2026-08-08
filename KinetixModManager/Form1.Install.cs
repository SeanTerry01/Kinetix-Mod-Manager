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
			SpeakBox(Loc.T("updateAll.premiumBox"), Loc.T("updateAll.premiumTitle"), MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}
		else
		{
			// Several installed mods can come from one Nexus download (e.g. Cape Stardew bundles 5 sub-mods in one
			// zip, all sharing a mod id). Installing that one download updates them all at once, so collapse update
			// rows that share a source to a single download — but keep genuine multi-part mods (Part 1 / Part 2)
			// distinct so both parts still get fetched.
			int totalMods = listUpdates.Items.Count;
			var groupSizes = listUpdates.Items.Cast<StardewMod>()
				.GroupBy(UpdateGroupKey)
				.ToDictionary(g => g.Key, g => g.Count());
			var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			List<StardewMod> mods = listUpdates.Items.Cast<StardewMod>().Where(m => seenSources.Add(UpdateGroupKey(m))).ToList();

			// When some updates are bundled, the download count is lower than the mod count. Say both so "1 download"
			// never looks like it is skipping the other mods it actually updates.
			string confirmMsg = mods.Count == totalMods
				? Loc.T("updateAll.confirm", totalMods)
				: Loc.T("updateAll.confirmGrouped", totalMods, mods.Count);
			if (SpeakBox(confirmMsg, Loc.T("common.confirm"), MessageBoxButtons.YesNo) == DialogResult.No)
			{
				return;
			}
			isUpdatingAll = true;
			try
			{
				for (int i = 0; i < mods.Count; i++)
				{
					// A bundle download updates every mod in it, so name that when it happens.
					int members = groupSizes.TryGetValue(UpdateGroupKey(mods[i]), out int c) ? c : 1;
					SetStatus(members > 1
						? Loc.T("updateAll.updatingBundle", i + 1, mods.Count, mods[i].Name, members)
						: Loc.T("updateAll.updatingStatus", i + 1, mods.Count, mods[i].Name));
					await DownloadAndInstallUpdate(mods[i], silent: true);
				}
				Speak(Loc.T("updateAll.finished"));
			}
			catch (Exception ex)
			{
				LogError("Updates", "Update All failed: " + ex.Message);
				Speak(Loc.T("updateAll.failed", FriendlyError(ex)));
			}
			finally
			{
				isUpdatingAll = false;
				// A final re-scan reflects the new versions and leaves the updates list as the batch left it
				// (successful mods already removed, any failures still shown). No online re-check: the user just
				// checked and updated, so re-querying Nexus here is redundant and only adds delay and speech.
				_ = RefreshModList(checkUpdates: false);
			}
		}
	}

	/// <summary>
	/// The key that groups update rows sharing one download source, used to de-duplicate Update All. Mods with
	/// the same Nexus id (or GitHub repo) collapse to one download — except that a "Part 1" / "Part 2" name marks
	/// a mod that ships two separate files on one page, which must stay distinct. Unmatched mods key on their
	/// UniqueID so they are never merged with anything else.
	/// </summary>
	private static string UpdateGroupKey(StardewMod m)
	{
		string part = m.Name.Contains("Part 2", StringComparison.OrdinalIgnoreCase) ? "|p2"
			: m.Name.Contains("Part 1", StringComparison.OrdinalIgnoreCase) ? "|p1" : "";
		if (!string.IsNullOrEmpty(m.NexusID)) return "nexus:" + m.NexusID + part;
		if (!string.IsNullOrEmpty(m.GitHubRepo)) return "github:" + m.GitHubRepo.ToLowerInvariant() + part;
		return "uid:" + m.UniqueId;
	}

	/// <summary>
	/// Returns the mod selected in whichever Nexus-aware list is active (Installed, Updates, or Discovery),
	/// or <c>null</c> if no such tab is active or nothing is selected. Shared by the Nexus actions that work
	/// on the current selection (open page, endorse).
	/// </summary>
	private StardewMod? SelectedNexusMod()
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
		else if (CurrentTab() == AppTab.Discovery)
		{
			listBox = listDiscovery;
		}
		else
		{
			return null;
		}
		return listBox.SelectedItem as StardewMod;
	}

	/// <summary>Opens the Nexus Mods page for the selected mod in the default browser.</summary>
	private void OpenModPage()
	{
		if (SelectedNexusMod() is StardewMod stardewMod && !string.IsNullOrEmpty(stardewMod.NexusID))
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

	/// <summary>
	/// Backs a mod up with the user's chosen progress feedback, off the UI thread.
	///
	/// Zipping a large mod takes long enough to matter, and doing it on the UI thread froze the window with
	/// nothing said — indistinguishable from a crash if you can't see the screen. <paramref name="displayName"/>
	/// is what the progress announcement calls the mod, so it is the mod's name rather than the backup's
	/// internal file stem.
	/// </summary>
	private async Task BackupModWithProgressAsync(string folderPath, string modName, string displayName)
	{
		try
		{
			ProgressAnnouncer progress = NewProgress(displayName, installing: true);
			await Task.Run(() => ModFileSystem.CreateBackup(folderPath, modName, backupsPath, progress));
			progress.Complete();

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
		if (!Directory.Exists(backupsPath))
		{
			Speak(Loc.T("backups.pruneNone"));
			return;
		}

		int before = Directory.GetFiles(backupsPath, "*.zip").Length;
		HashSet<string> modNames = new HashSet<string>();
		foreach (string f in Directory.GetFiles(backupsPath, "*.zip"))
			modNames.Add(Regex.Replace(Path.GetFileNameWithoutExtension(f), @"_\d{8}_\d{6}$", ""));

		if (modNames.Count == 0)
		{
			Speak(Loc.T("backups.pruneNone"));
			return;
		}

		foreach (string modName in modNames)
			ModFileSystem.PruneBackups(modName, backupsPath, _settings.MaxBackupsPerMod);
		int deleted = before - Directory.GetFiles(backupsPath, "*.zip").Length;

		if (deleted > 0)
		{
			RefreshBackupsList();
			Speak(Loc.T("backups.pruneComplete", deleted));
			return;
		}

		// Nothing was over the limit — and nothing ever will be, because the same trim runs automatically every
		// time a backup is made. Reporting "deleted 0 backups" and stopping made the command look broken when it
		// had simply found nothing to do. So say that plainly, and offer the clean-up the user actually came
		// here for: keep the newest backup of each mod and let the older ones go.
		int extra = modNames.Sum(name =>
			Math.Max(0, Directory.GetFiles(backupsPath, name + "_*.zip").Length - 1));

		if (extra == 0)
		{
			Speak(Loc.T("backups.pruneAlreadyMinimal", _settings.MaxBackupsPerMod));
			return;
		}

		if (SpeakBox(Loc.T("backups.trimOffer", _settings.MaxBackupsPerMod, extra),
				Loc.T("backups.trimTitle"), MessageBoxButtons.YesNo) != DialogResult.Yes)
		{
			SpeakAfterPrompt(Loc.T("backups.trimCancelled"));
			return;
		}

		foreach (string modName in modNames)
			ModFileSystem.PruneBackups(modName, backupsPath, 1);
		int trimmed = before - Directory.GetFiles(backupsPath, "*.zip").Length;

		RefreshBackupsList();
		SpeakAfterPrompt(Loc.T("backups.trimComplete", trimmed));
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
			SetStatus(Loc.T("download.downloading", realName), speak: false);
			string path = Path.Combine(downloadsPath, realName);

			ProgressAnnouncer progress = NewProgress(realName, installing: false);
			await _nexusService.DownloadFileWithProgressAsync(dlUri, path, progress);
			progress.Complete();
			_soundEngine.Play("load_complete");
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
			// If this download is a newer version of a mod you already have, treat it as an update and skip the
			// "overwrite the installed copy?" prompt — that confirmation is meant for re-installing the same (or an
			// older) copy, not for a genuine upgrade.
			bool isUpgrade = false;
			if (!string.IsNullOrEmpty(nexusId))
			{
				GameMod? installed = _allInstalledMods.FirstOrDefault(m => m.NexusID == nexusId);
				if (installed != null)
				{
					try
					{
						var details = await _nexusService.GetModDetailsAsync(nexusId);
						isUpgrade = IsNewerVersion(installed.Version, details?["version"]?.ToString());
					}
					catch { /* version lookup is best-effort; fall back to prompting */ }
				}
			}

			ForceToForeground();
			if (SpeakBox(this, Loc.T("download.installNow", realName), Loc.T("download.successTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
				_ = InstallFromZip(path, nexusId, confirmReinstall: !isUpgrade);
		}
		catch (TimeoutException)
		{
			SetStatus(Loc.T("download.timedOut"), speak: false);
			SpeakBox(Loc.T("download.timedOut"));
		}
		catch (Exception ex)
		{
			SpeakBox(Loc.T("download.nxmError", FriendlyError(ex)));
		}
	}

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();
	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);
	[System.Runtime.InteropServices.DllImport("kernel32.dll")]
	private static extern uint GetCurrentThreadId();
	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(IntPtr hWnd);
	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern bool BringWindowToTop(IntPtr hWnd);
	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
	[System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
	private static extern bool SystemParametersInfoGet(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);
	[System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
	private static extern bool SystemParametersInfoSet(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

	private const uint SPI_GETFOREGROUNDLOCKTIMEOUT = 0x2000;
	private const uint SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001;
	private const uint SPIF_SENDCHANGE = 0x0002;
	private const int SW_SHOW = 5;

	/// <summary>
	/// Forces the main window to the foreground and gives it focus. Needed when something the user did in
	/// another app (e.g. clicking "Mod Manager Download" in their browser) hands control back to us and we need
	/// to show a prompt. A background process normally <b>cannot</b> steal focus — Windows' "focus stealing
	/// prevention" silently ignores <c>Activate()</c>/<c>SetForegroundWindow</c> on many machines (it depends on
	/// the per-PC <c>ForegroundLockTimeout</c> and which app sent the last input), which is why the old TopMost
	/// flip worked on some computers but not others. To make it reliable everywhere we (1) temporarily zero the
	/// foreground-lock timeout and (2) attach our input thread to the current foreground window's thread, so the
	/// system treats our <c>SetForegroundWindow</c> as legitimate. Both changes are undone afterward.
	/// </summary>
	private void ForceToForeground()
	{
		try
		{
			IntPtr hWnd = Handle;
			if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;

			IntPtr foreground = GetForegroundWindow();
			uint foreThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
			uint thisThread = GetCurrentThreadId();

			uint origTimeout = 0;
			bool gotTimeout = SystemParametersInfoGet(SPI_GETFOREGROUNDLOCKTIMEOUT, 0, ref origTimeout, 0);
			SystemParametersInfoSet(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, IntPtr.Zero, SPIF_SENDCHANGE);

			bool attached = foreThread != 0 && foreThread != thisThread && AttachThreadInput(thisThread, foreThread, true);
			try
			{
				ShowWindow(hWnd, SW_SHOW);
				BringWindowToTop(hWnd);
				SetForegroundWindow(hWnd);
				Activate();
			}
			finally
			{
				if (attached) AttachThreadInput(thisThread, foreThread, false);
				if (gotTimeout) SystemParametersInfoSet(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, new IntPtr(origTimeout), SPIF_SENDCHANGE);
			}
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
			_ = InstallFromZip(openFileDialog.FileName, confirmReinstall: true);
		}
	}

	/// <summary>
	/// Confirmation shown by <see cref="InstallFromZip"/> when an install would overwrite a mod that's already
	/// installed. Returns true to overwrite. On decline it announces that the existing copy was kept. Marshals to
	/// the UI thread since the install pipeline may call it from a background continuation.
	/// </summary>
	private bool ConfirmOverwrite(string modName, string version)
	{
		bool Ask()
		{
			bool yes = SpeakBox(this,
				Loc.T("install.reinstallConfirm", modName, string.IsNullOrWhiteSpace(version) ? "?" : version),
				Loc.T("install.reinstallTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
			if (!yes) Speak(Loc.T("install.reinstallKept", modName));
			return yes;
		}
		return InvokeRequired ? (bool)Invoke(Ask) : Ask();
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
	/// Runs a mod's own installer and waits for it to finish, returning whether it completed.
	///
	/// Some mods can only be installed by the program their author ships — The Witcher 3's accessibility mod puts
	/// files in four different places, and no amount of copying folders about reproduces that. So the manager
	/// does what it can do well: it fetches the download, unpacks it, finds the installer, asks before running
	/// anything, and waits. When the installer closes, the manager takes over again and re-scans, so the mod
	/// turns up in the list exactly as any other install does.
	///
	/// Always asks first. Running an executable out of a downloaded archive is not something to do on the user's
	/// behalf without saying so.
	/// </summary>
	private async Task<bool> RunModInstallerAsync(string installerPath)
	{
		string installerName = Path.GetFileName(installerPath);

		if (SpeakBox(Loc.T("install.runInstallerBox", installerName), Loc.T("install.runInstallerTitle"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			Speak(Loc.T("install.installerDeclined"));
			return false;
		}

		SetStatus(Loc.T("install.installerRunning", installerName));

		try
		{
			using var installer = new Process();
			installer.StartInfo = new ProcessStartInfo(installerPath)
			{
				WorkingDirectory = Path.GetDirectoryName(installerPath) ?? "",
				// Through the shell, so an installer that asks for administrator rights gets its prompt rather
				// than simply failing to start.
				UseShellExecute = true
			};

			if (!installer.Start()) return false;
			await installer.WaitForExitAsync();

			// A cancelled installer exits non-zero. Treating that as a successful install would leave the manager
			// announcing a mod the user just decided not to install.
			if (installer.ExitCode != 0)
			{
				_soundEngine.Play("error");
				Speak(Loc.T("install.installerCancelled"));
				return false;
			}
		}
		catch (Exception ex)
		{
			LogError("Install", "Could not run the mod's installer: " + ex.Message);
			_soundEngine.Play("error");
			SpeakBox(Loc.T("install.installerFailedBox", FriendlyError(ex)));
			return false;
		}

		Speak(Loc.T("install.installerFinished"));
		return true;
	}

	/// <summary>
	/// Extracts a .zip archive to a temp directory, validates all paths against the mods folder
	/// to prevent path traversal, then moves the contents into the Mods directory.
	/// Temp files are cleaned up in a <c>finally</c> block regardless of success or failure.
	/// </summary>
	private async Task InstallFromZip(string zipPath, string? nexusId = null, bool silent = false, bool confirmReinstall = false)
	{
		// Install progress runs even in a silent batch (Update All), following the user's tones/speech/both/off
		// setting via ProgressAnnouncer. The silent flag suppresses only the per-mod spoken chatter and the
		// per-mod "installed" message box (see below) — not the progress feedback.
		ProgressAnnouncer? installProgress = NewProgress(Path.GetFileNameWithoutExtension(zipPath), installing: true);
		// Only interactive installs (manual Ctrl+I, Mod Manager Download) ask before overwriting; updates
		// deliberately overwrite without prompting.
		Func<string, string, bool>? confirmOverwrite = confirmReinstall ? ConfirmOverwrite : null;
		try
		{
			string name = await ModFileSystem.ExtractModAsync(
				zipPath, _settings.CurrentModsPath, _allInstalledMods,
				backupsPath, _settings.MaxBackupsPerMod, _settings.ActiveGame, LogError, nexusId, _nexusService, null, _settings.CurrentGamePath,
				ShowFomodWizardAsync, installProgress, confirmOverwrite, RunModInstallerAsync);
			installProgress?.Complete();
			_soundEngine.Play("load_complete");

			await RefreshModList(checkUpdates: false);

			// Remember which Nexus page this download came from, for every mod it installed. Stardew mods keep
			// their update key in the author's own manifest.json, which the manager must not rewrite — and one
			// archive routinely installs several mods where only one (or none) declares a key. Recording the id
			// against each installed mod's UniqueID is what makes them all updatable afterwards.
			if (!string.IsNullOrEmpty(nexusId) && GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley))
			{
				LinkModsInstalledFrom(name, nexusId!, zipPath);
			}
			else if (!string.IsNullOrEmpty(nexusId))
			{
				// The other games write the Nexus id into the mod's own manifest as they install, so linking is
				// already done — but the RELEASE that was installed still has to be recorded, and only the
				// download's file name knows it. Without this the update check falls back to comparing the
				// version the mod declares against the version on its page, and for the many mods whose authors
				// never bump the number in the mod itself, that offers the same update forever.
				if (ModFileSystem.ExtractVersionFromFileName(zipPath, nexusId) is string installedRelease)
					RecordInstalledDownloadVersion("Nexus:" + nexusId, installedRelease);
			}

			// RefreshModList already added the new mod to the priority/plugin order and wrote plugins.txt.
			// Re-sync assets with forceRelink so the new mod's files are linked even on a reinstall that
			// reuses the folder name (where ownership is unchanged), then refresh the priority list.
			if (IsBethesdaGame)
			{
				SyncBethesdaDeployment(new HashSet<string>(new[] { name }, StringComparer.OrdinalIgnoreCase));
				RefreshModPriorityList();
			}
			// In a silent batch (Update All) don't pop a modal box per mod — the batch's status line and the
			// end-of-run "All updates finished" cover it; a per-mod box would force a click on every mod.
			if (!silent) SpeakBox(Loc.T("install.installed", name));
		}
		catch (OperationCanceledException)
		{
			// User cancelled the FOMOD option wizard; it already announced the cancellation.
		}
		catch (UnauthorizedAccessException ex)
		{
			// A denied path is almost always an external lock: the game still running, antivirus/Controlled Folder
			// Access guarding the mods folder, or a file held open elsewhere. Say so rather than a bare path error.
			AiInstallFailure(Loc.T("install.failedAccess", ex.Message), Path.GetFileNameWithoutExtension(zipPath));
		}
		catch (ModFileSystem.ModArchiveContentException ex)
		{
			// The archive was fine but holds no mod for this game — say what to do about it, and keep the raw
			// detail (what the archive did contain) in the error log rather than in the spoken message.
			LogError(Path.GetFileNameWithoutExtension(zipPath), ex.Message);
			AiInstallFailure(Loc.T("install.failed", FriendlyError(ex)), Path.GetFileNameWithoutExtension(zipPath));
		}
		catch (Exception ex)
		{
			AiInstallFailure(Loc.T("install.failed", ex.Message), Path.GetFileNameWithoutExtension(zipPath));
		}
		finally
		{
			// Don't leave a "Downloading..." / "Installing..." status sitting in the title afterward.
			ResetStatus();
		}
	}
}
