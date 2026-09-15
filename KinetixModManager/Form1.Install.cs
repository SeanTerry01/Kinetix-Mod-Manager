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
				LogFailure("Updates", "Update All failed", ex);
				Speak(Loc.T("updateAll.failed", FriendlyError(ex)));
			}
			finally
			{
				isUpdatingAll = false;
				// A final re-scan reflects the new versions and leaves the updates list as the batch left it
				// (successful mods already removed, any failures still shown). No online re-check: the user just
				// checked and updated, so re-querying Nexus here is redundant and only adds delay and speech.
				Fire(RefreshModList(checkUpdates: false), "RefreshModList");
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
		if (SelectedNexusMod() is not StardewMod stardewMod) return;

		// Whichever catalogue the mod came from. Before this the key did nothing at all on a Minecraft
		// result: it required a Nexus id, and a Modrinth mod has none — so the one action available on a
		// search result was silently unavailable for a whole game.
		string url =
			!string.IsNullOrEmpty(stardewMod.ModrinthId)
				? $"https://modrinth.com/mod/{stardewMod.ModrinthId}"
			: !string.IsNullOrEmpty(stardewMod.NexusID)
				? $"https://www.nexusmods.com/{_nexusService.CurrentGameDomain}/mods/{stardewMod.NexusID}?tab=files"
			: "";

		if (url.Length == 0) { Speak(Loc.T("modpage.noPage", stardewMod.Name)); return; }

		try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
		catch (Exception ex) { LogFailure(stardewMod.Name, "Failed to open the mod page", ex); }
	}

	/// <summary>
	/// Creates a timestamped .zip backup of a mod folder under the backups directory,
	/// then calls <see cref="PruneBackupsForMod"/> to enforce the per-mod backup limit.
	/// </summary>
	private void BackupMod(string folderPath, string modName)
	{
		try
		{
			BackupStore.CreateBackup(folderPath, modName, backupsPath);
			BackupStore.PruneBackups(modName, backupsPath, _settings.MaxBackupsPerMod);
			RefreshBackupsList();
		}
		catch (Exception ex)
		{
			LogFailure(modName, "Backup Error", ex);
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
			ProgressAnnouncer progress = NewProgress(displayName, "progress.backingUpName");
			await Task.Run(() => BackupStore.CreateBackup(folderPath, modName, backupsPath, progress));
			progress.Complete();

			BackupStore.PruneBackups(modName, backupsPath, _settings.MaxBackupsPerMod);
			RefreshBackupsList();
		}
		catch (Exception ex)
		{
			LogFailure(modName, "Backup Error", ex);
		}
	}

	/// <summary>Runs <see cref="BackupStore.PruneBackups"/> for every mod that has backups on disk.</summary>
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
			BackupStore.PruneBackups(modName, backupsPath, _settings.MaxBackupsPerMod);
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
			BackupStore.PruneBackups(modName, backupsPath, 1);
		int trimmed = before - Directory.GetFiles(backupsPath, "*.zip").Length;

		RefreshBackupsList();
		SpeakAfterPrompt(Loc.T("backups.trimComplete", trimmed));
	}

	/// <summary>
	/// Handles a <c>nxm://</c> link: works out which game and which copy it is for, downloads the file into that
	/// copy's downloads folder, and then either installs it or leaves it there for later. Requires a valid API key.
	///
	/// <para>
	/// The link names its own game (see <see cref="NxmLink"/>), so a download no longer depends on that game being
	/// the loaded one — the case this used to fail at, with a JSON parser error, was somebody browsing Nexus with
	/// another game open or no session at all. Downloading is game-agnostic; only <em>installing</em> needs a
	/// session, because that is what has a mods folder, a deployment and a load order. So the two are separated
	/// here: download first, always, then decide about the session.
	/// </para>
	/// </summary>
	private async Task HandleNxmUrl(string url)
	{
		try
		{
			if (!NxmLink.TryParse(url, out NxmLink link))
			{
				OnUi(() => SpeakBox(Loc.T("nxm.badLink")));
				return;
			}

			GameProfile? game = GameProfiles.FindByNexusDomain(link.GameDomain);
			if (game == null)
			{
				// Naming the domain is the most useful thing available: it is the game's name on Nexus, which is
				// what the user was just looking at.
				OnUi(() => SpeakBox(Loc.T("nxm.unsupportedGame", link.GameDomain)));
				return;
			}

			// The link names a game; a mod is installed into a copy. Establish which copy before downloading, so
			// the file is filed correctly whatever the user decides afterwards.
			string? targetKey = ResolveDownloadTargetCopy(game);
			if (targetKey == null) return;

			bool alreadyLoaded = GameProfiles.IsGame(_settings.ActiveGame, game.Id) && targetKey == _settings.ActiveGame;

			SetStatus(Loc.T("download.parsingLink"));
			var (dlUri, fileName, modName) = await _nexusService.ResolveNxmUrlAsync(url);
			SetStatus(Loc.T("download.downloading", modName), speak: false);
			string path = Path.Combine(DownloadsPathFor(targetKey), fileName);

			// modName is what the user hears throughout; fileName is only ever a path. See ModDisplayName.
			ProgressAnnouncer progress = NewProgress(modName, installing: false);
			await _nexusService.DownloadFileWithProgressAsync(dlUri, path, progress);
			progress.Complete();
			_soundEngine.Play("load_complete");

			if (!alreadyLoaded && !await OfferToSwitchForDownload(game, targetKey, modName))
				return;

			await OfferToInstallDownload(link, path, modName);
		}
		catch (TimeoutException)
		{
			SetStatus(Loc.T("download.timedOut"), speak: false);
			OnUi(() => SpeakBox(Loc.T("download.timedOut")));
		}
		catch (Exception ex)
		{
			OnUi(() => SpeakBox(Loc.T("download.nxmError", FriendlyError(ex))));
		}
	}

	/// <summary>
	/// Runs <paramref name="work"/> on the UI thread. A nxm link can arrive on the named-pipe thread — a second
	/// instance forwarding the browser's click — and everything below this speaks or shows a view.
	/// </summary>
	private void OnUi(Action work)
	{
		if (InvokeRequired) Invoke(work);
		else work();
	}

	/// <inheritdoc cref="OnUi(Action)"/>
	private T OnUi<T>(Func<T> work) => InvokeRequired ? (T)Invoke(work)! : work();

	/// <summary>
	/// Which copy of <paramref name="game"/> a download is for, or <c>null</c> if it cannot be answered — the game
	/// is not set up, or the user backed out of the question.
	///
	/// The loaded copy wins without asking. Otherwise, one copy answers itself, and two or more is a genuine
	/// question: nothing in the link says which, and guessing files somebody's mod under a copy they were not
	/// thinking of.
	/// </summary>
	private string? ResolveDownloadTargetCopy(GameProfile game)
	{
		if (GameProfiles.IsGame(_settings.ActiveGame, game.Id))
			return _settings.ActiveGame;

		List<GameInstall> copies = _settings.InstallsOf(game.Id);
		if (copies.Count == 0)
		{
			OnUi(() => SpeakBox(Loc.T("nxm.gameNotSetUp", game.DisplayName)));
			return null;
		}
		if (copies.Count == 1) return copies[0].Key;

		return OnUi(() =>
		{
			ForceToForeground();
			List<string> labels = copies.Select(c => c.DisplayName(withPlatform: true)).ToList();
			string? picked = ShowChoiceList(
				Loc.T("nxm.chooseCopyTitle"),
				Loc.T("nxm.chooseCopyListName", game.DisplayName),
				labels,
				labels[0],
				Loc.T("nxm.chooseCopyHint", game.DisplayName));
			int index = picked == null ? -1 : labels.IndexOf(picked);
			return index < 0 ? null : copies[index].Key;
		});
	}

	/// <summary>
	/// Asks what to do about a download for a game other than the loaded one, and switches to it if that is the
	/// answer. Returns <c>true</c> when the caller should go on to install, <c>false</c> when the file has been left
	/// where it is on purpose or the user cancelled.
	///
	/// The file is downloaded by the time this runs, so every answer keeps it — "cancel" cancels the interruption,
	/// not the download, and says so.
	/// </summary>
	private async Task<bool> OfferToSwitchForDownload(GameProfile game, string targetKey, string fileName)
	{
		string targetName = _settings.InstallFor(targetKey)?.DisplayName(withPlatform: _settings.InstallsOf(game.Id).Count > 1)
			?? game.DisplayName;

		CrossGameDownloadAction choice = _settings.CrossGameDownloads;
		if (choice == CrossGameDownloadAction.Ask)
		{
			// With no session open there is nothing to interrupt, so the question is only worth asking when it
			// would actually cost the user something.
			if (_settings.ActiveGame == "None")
			{
				choice = CrossGameDownloadAction.SwitchAndInstall;
			}
			else
			{
				string? picked = OnUi(() =>
				{
					ForceToForeground();
					List<string> options = new()
					{
						Loc.T("nxm.actionSwitch", targetName),
						Loc.T("nxm.actionSave", targetName)
					};
					return ShowChoiceList(
						Loc.T("nxm.crossGameTitle"),
						Loc.T("nxm.chooseActionListName"),
						options,
						options[0],
						Loc.T("nxm.crossGameHint", fileName, targetName, GameDisplayName()));
				});

				if (picked == null)
				{
					OnUi(() => Speak(Loc.T("nxm.keptDownload", targetName)));
					return false;
				}
				choice = picked == Loc.T("nxm.actionSave", targetName)
					? CrossGameDownloadAction.SaveForLater
					: CrossGameDownloadAction.SwitchAndInstall;
			}
		}

		if (choice == CrossGameDownloadAction.SaveForLater)
		{
			OnUi(() => SpeakBox(Loc.T("nxm.savedFor", fileName, targetName)));
			return false;
		}

		OnUi(() =>
		{
			Speak(Loc.T("nxm.switching", targetName));
			SwitchActiveGame(targetKey);
		});

		// SwitchActiveGame refuses a game that is no longer installed, having said so itself; carrying on would
		// install into whatever session survived that refusal.
		if (!GameProfiles.IsGame(_settings.ActiveGame, game.Id))
		{
			OnUi(() => SpeakBox(Loc.T("nxm.savedFor", fileName, targetName)));
			return false;
		}

		// Give the switch's own mod-list refresh a moment before the install prompt lands on top of it. This is the
		// wait the old code used before resolving the download, where it was load-bearing and raced; here nothing
		// depends on it finishing — a list still refreshing only means the "already installed?" question gets asked.
		await Task.Delay(500);
		return true;
	}

	/// <summary>
	/// Prompts to install a file that has finished downloading, and installs it on a Yes.
	///
	/// If this is a newer version of a mod already installed, the "overwrite the installed copy?" confirmation is
	/// skipped — that question is meant for re-installing the same or an older copy, not for a genuine upgrade. The
	/// check is best-effort in both directions: a mod list still refreshing after a game switch simply means the
	/// question gets asked, which is the safe way to be wrong.
	/// </summary>
	private async Task OfferToInstallDownload(NxmLink link, string path, string modName)
	{
		string? nexusId = Regex.IsMatch(link.ModId, @"^\d+$") ? link.ModId : null;

		bool isUpgrade = false;
		if (!string.IsNullOrEmpty(nexusId))
		{
			GameMod? installed = _allInstalledMods.FirstOrDefault(m => m.NexusID == nexusId);
			if (installed != null)
			{
				try
				{
					var details = await _nexusService.GetModDetailsAsync(nexusId, link.GameDomain);
					isUpgrade = IsNewerVersion(installed.Version, details?["version"]?.ToString());
				}
				catch (Exception ex) { DiagnosticLog.WriteException("Nexus", $"looking up the current version of mod {nexusId}", ex); }
			}
		}

		// The download was started from the browser (Mod Manager Download button), so the browser owns the
		// foreground by now. Pull the manager to the front first, otherwise this prompt can open behind the browser
		// and never receive keyboard / screen-reader focus.
		bool install = OnUi(() =>
		{
			ForceToForeground();
			return SpeakBox(this, Loc.T("download.installNow", modName), Loc.T("download.successTitle"),
				MessageBoxButtons.YesNo) == DialogResult.Yes;
		});

		// Started on the UI thread: a nxm link can arrive on the named-pipe thread, and the install reports itself
		// through the window as it goes. The name is carried across so the install talks about the same mod the
		// download did, even where the file name it arrived under says nothing.
		if (install) OnUi(() => { Fire(InstallFromZip(path, nexusId, confirmReinstall: !isUpgrade, displayName: modName), "InstallFromZip"); });
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
		// Stamped before the window moves, not after: the screen reader starts reacting the instant the
		// foreground changes, and what is being timed is how long that reaction runs for. See ReaderIsReacting.
		_foregroundTakenAt = Environment.TickCount64;
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
		catch (Exception ex) { DiagnosticLog.WriteException("UI", "bringing the manager to the front", ex); }
	}

	/// <summary>Opens a file dialog to select a .zip file and installs it via <see cref="InstallFromZip"/>.</summary>
	private void ManualInstall()
	{
		bool minecraft = GameProfiles.Find(_settings.ActiveGame)?.IsMinecraft == true;

		using OpenFileDialog openFileDialog = new OpenFileDialog
		{
			InitialDirectory = downloadsPath,
			// A Minecraft mod arrives as a .jar and is not an archive to unpack — it IS the mod — so the
			// picker has to offer them or the file the user just downloaded cannot even be selected.
			Filter = Loc.T(minecraft ? "install.jarFilter" : "install.zipFilter")
		};
		if (openFileDialog.ShowDialog() == DialogResult.OK)
		{
			if (minecraft &&
				openFileDialog.FileName.EndsWith(MinecraftLayout.ModExtension, StringComparison.OrdinalIgnoreCase))
			{
				Fire(InstallMinecraftJarAsync(openFileDialog.FileName), "InstallMinecraftJarAsync");
				return;
			}

			// A Nexus download carries its mod id in its own file name, and this is the moment that is known for
			// certain — afterwards nothing on disk says where the mod came from, and it takes a name search
			// against Nexus to guess it back. Installing by hand used to throw it away, so a mod installed this
			// way could not be checked for updates until the user ran Auto-match. Read straight from the name,
			// it also records which release is installed, so the mod compares against what it actually has.
			Fire(InstallFromZip(openFileDialog.FileName,
				ModManifest.NexusIdFromDownloadName(openFileDialog.FileName), confirmReinstall: true), "InstallFromZip");
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
	/// Compares two dot-separated version strings; true when <paramref name="target"/> is the greater.
	/// Kept as a wrapper so the dozens of call sites around the app read as they did; the rule itself is
	/// <see cref="ModVersions.IsNewer"/> in the core, where the update decision that depends on it also lives.
	/// </summary>
	private bool IsNewerVersion(string? current, string? target) => ModVersions.IsNewer(current, target);

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
			LogFailure("Install", "Could not run the mod's installer", ex);
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
	/// <param name="partOfMultiPartMod">
	/// True when this archive is one of several separate downloads from the same Nexus page. Its siblings carry
	/// the same Nexus id, so that id must not be used to decide which installed mod is being replaced — it would
	/// name a sibling and delete it. See <c>FindExistingInstall</c>.
	/// </param>
	/// <param name="displayName">
	/// What to call the mod out loud, where the caller already knows — a download whose file name turned out to be
	/// an opaque id, so the name came from the mod's page instead. Left null, the name is read out of the archive's
	/// own name, which is right for a mod picked off disk.
	/// </param>
	/// <param name="gitHubRepo">
	/// The <c>owner/repo</c> this archive came from, when it came from GitHub rather than a mod site. Recorded
	/// with the install so the mod can be checked for updates afterwards — without it a mod installed from a
	/// repository the user named is one the manager can never tell them about again.
	/// </param>
	private async Task InstallFromZip(string zipPath, string? nexusId = null, bool silent = false,
		bool confirmReinstall = false, bool partOfMultiPartMod = false, string? displayName = null,
		string? gitHubRepo = null)
	{
		// Never the raw file name: a Nexus download is called "Skyrim Access-181131-1-2-3-1723456789.7z", and the
		// mod id, version and timestamp on the end of that are not part of what the mod is called. See ModDisplayName.
		string spokenName = !string.IsNullOrWhiteSpace(displayName)
			? displayName!.Trim()
			: ModDisplayName.ForSpeech(Path.GetFileNameWithoutExtension(zipPath), nexusId);

		// Install progress runs even in a silent batch (Update All), following the user's tones/speech/both/off
		// setting via ProgressAnnouncer. The silent flag suppresses only the per-mod spoken chatter and the
		// per-mod "installed" message box (see below) — not the progress feedback.
		ProgressAnnouncer? installProgress = NewProgress(spokenName, installing: true);
		// Only interactive installs (manual Ctrl+I, Mod Manager Download) ask before overwriting; updates
		// deliberately overwrite without prompting.
		Func<string, string, bool>? confirmOverwrite = confirmReinstall ? ConfirmOverwrite : null;
		try
		{
			string name = await ModFileSystem.ExtractModAsync(
				zipPath, _settings.CurrentModsPath, _allInstalledMods,
				backupsPath, _settings.MaxBackupsPerMod, _settings.ActiveGame, LogError, nexusId, _nexusService, gitHubRepo, _settings.CurrentGamePath,
				ShowFomodWizardAsync, installProgress, confirmOverwrite, RunModInstallerAsync,
				matchExistingByNexusId: !partOfMultiPartMod);
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
				if (ModScanner.ExtractVersionFromFileName(zipPath, nexusId) is string installedRelease)
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
			// The folder a mod landed in is not necessarily what it is called: Stardew names its folder from the
			// mod's own manifest (already the real name), while the Bethesda games name it from the download.
			if (!silent) SpeakBox(Loc.T("install.installed", ModDisplayName.ForSpeech(name, nexusId, spokenName)));
		}
		catch (OperationCanceledException)
		{
			// User cancelled the FOMOD option wizard; it already announced the cancellation.
		}
		catch (UnauthorizedAccessException ex)
		{
			// A denied path is almost always an external lock: the game still running, antivirus/Controlled Folder
			// Access guarding the mods folder, or a file held open elsewhere. Say so rather than a bare path error.
			AiInstallFailure(Loc.T("install.failedAccess", ex.Message), spokenName);
		}
		catch (ModArchiveContentException ex)
		{
			// The archive was fine but holds no mod for this game — say what to do about it, and keep the raw
			// detail (what the archive did contain) in the error log rather than in the spoken message.
			LogError(spokenName, ex.Message);
			AiInstallFailure(Loc.T("install.failed", FriendlyError(ex)), spokenName);
		}
		catch (Exception ex)
		{
			AiInstallFailure(Loc.T("install.failed", ex.Message), spokenName);
		}
		finally
		{
			// Don't leave a "Downloading..." / "Installing..." status sitting in the title afterward.
			ResetStatus();
		}
	}
}
