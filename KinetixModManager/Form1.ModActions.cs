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

/// <summary>Mod actions: categories, dependencies, enable/disable, and deletion for Form1.</summary>
public partial class Form1
{
	/// <summary>Prompts the user to assign a custom category string to the selected mod.</summary>
	private void SetManualCategory()
	{
		if (listInstalled.SelectedItem is StardewMod stardewMod)
		{
			string text = Interaction.InputBox(Loc.T("modactions.changeCategoryPrompt", stardewMod.Name), Loc.T("modactions.changeCategoryTitle"), stardewMod.Category);
			if (!string.IsNullOrEmpty(text))
			{
				_settings.ModCategories[stardewMod.UniqueId] = text.Trim();
				_settings.Save();
				_ = RefreshModList(checkUpdates: false);
				Speak(Loc.T("modactions.categorySet", stardewMod.Name, text));
			}
		}
	}

	/// <summary>
	/// Prompts for a personal free-text note on the selected mod (e.g. "keep disabled until year 2"). The note is
	/// spoken whenever the mod is selected in the list. Submitting an empty box offers to clear an existing note —
	/// confirmed first, since the input box can't tell an emptied field from a cancel.
	/// </summary>
	private void SetModNote()
	{
		if (listInstalled.SelectedItem is not StardewMod mod)
		{
			Speak(Loc.T("common.noModSelected"));
			return;
		}
		if (mod.IsGroup)
		{
			Speak(Loc.T("common.modGroupFirst"));
			return;
		}

		string existing = _settings.ModNotes.TryGetValue(mod.UniqueId, out string? current) ? current : "";
		string text = Interaction.InputBox(Loc.T("modactions.notePrompt", mod.Name), Loc.T("modactions.noteTitle"), existing).Trim();

		if (string.IsNullOrEmpty(text))
		{
			// InputBox returns "" for both Cancel and an emptied field, so confirm before clearing an existing note.
			if (_settings.ModNotes.ContainsKey(mod.UniqueId) &&
				SpeakBox(Loc.T("modactions.noteClearConfirm", mod.Name), Loc.T("modactions.noteTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
			{
				_settings.ModNotes.Remove(mod.UniqueId);
				_settings.Save();
				_ = RefreshModList(checkUpdates: false);
				Speak(Loc.T("modactions.noteCleared", mod.Name));
			}
			return;
		}

		_settings.ModNotes[mod.UniqueId] = text;
		_settings.Save();
		_ = RefreshModList(checkUpdates: false);
		Speak(Loc.T("modactions.noteSet", mod.Name));
	}

	/// <summary>
	/// Opens the selected mod's manifest in the built-in JSON editor (Ctrl+S to save) so the user can edit
	/// it directly — most often to correct a version number a mod author forgot to bump, which otherwise
	/// leaves the mod perpetually flagged by, or mismatched in, update checks. Uses <c>manifest.json</c> for
	/// Stardew Valley and the manager-maintained <c>.manager_manifest.json</c> for Skyrim/Fallout 4. A
	/// successful save re-scans the installed list so the corrected version is picked up immediately.
	/// </summary>
	private void OpenSelectedModManifest()
	{
		if (!(listInstalled.SelectedItem is StardewMod mod))
		{
			Speak(Loc.T("common.noModSelected"));
			return;
		}
		if (mod.IsGroup)
		{
			Speak(Loc.T("common.modGroupFirst"));
			return;
		}

		string manifestName = _settings.ActiveGame == "StardewValley" ? "manifest.json" : ".manager_manifest.json";
		string manifestPath = Path.Combine(mod.FolderPath, manifestName);
		if (!File.Exists(manifestPath))
		{
			Speak(Loc.T("modactions.noManifestSpeak", mod.Name));
			SpeakBox(Loc.T("modactions.noManifestBox", manifestName, mod.Name), Loc.T("modactions.noManifestTitle"));
			return;
		}

		OpenConfigEditor(mod.Name, manifestPath, delegate
		{
			// Re-scan so a corrected version is re-read; the refresh's prune pass also drops the mod
			// from the updates list when its version now matches the latest. checkUpdates:false avoids
			// re-running the full Nexus/GitHub update query.
			_ = RefreshModList(checkUpdates: false);
		}, Loc.T("config.labelManifest"));
	}

	/// <summary>
	/// Opens the selected mod's <c>config.json</c> in the built-in JSON editor (Ctrl+S to save) so the user
	/// can change mod settings directly from the manager (for example, the screen the Stardew Valley "Skip
	/// Intro" mod skips to). Most mods only generate their config file the first time the game runs with the
	/// mod enabled, so this reports gracefully when no config exists yet.
	/// </summary>
	/// <summary>
	/// The BepInEx settings file belonging to <paramref name="mod"/>, or <c>null</c> when it has none.
	///
	/// BepInEx names these files after the plugin's id rather than its folder, and writes the plugin's name and
	/// id into the header of each — so the match is made on the id first (exact, and what BepInEx itself uses)
	/// and on the displayed name second, for a plugin too old to have written an id.
	/// </summary>
	private string? FindBepInExConfigFor(StardewMod mod)
	{
		if (!IsBepInExGame) return null;

		foreach ((string label, string path) in ModFileSystem.BepInExConfigFiles(_settings.CurrentGamePath))
		{
			BepInExPluginInfo? info = BepInExPlugin.ReadFromConfig(path);

			if (info != null && info.Guid.Length > 0 &&
				info.Guid.Equals(mod.UniqueId, StringComparison.OrdinalIgnoreCase))
				return path;

			if (label.Equals(mod.Name, StringComparison.OrdinalIgnoreCase))
				return path;
		}

		return null;
	}

	private void OpenSelectedModConfig()
	{
		if (!(listInstalled.SelectedItem is StardewMod mod))
		{
			Speak(Loc.T("common.noModSelected"));
			return;
		}
		if (mod.IsGroup)
		{
			Speak(Loc.T("common.modGroupFirst"));
			return;
		}

		// A Content Patcher pack declares what each of its settings accepts, so it gets the settings editor
		// rather than raw JSON: the choices are offered instead of typed. Checked before the config file exists,
		// because a pack that has never been run has a schema but no config yet — and that is precisely when
		// being shown the author's defaults and allowed values is most useful.
		List<CpConfigOption> contentPatcherSettings = ContentPatcherSettingsFor(mod.FolderPath);
		if (contentPatcherSettings.Count > 0)
		{
			ShowContentPatcherConfig(mod.Name, mod.FolderPath, contentPatcherSettings);
			return;
		}

		// A Skyrim/Fallout 4 mod with a Mod Configuration Menu declares its settings the same way, so it gets
		// the same treatment — labels, explanations and choices read from the menu the mod ships, rather than
		// the settings being reachable only from inside the game.
		List<McmMenu> mcmMenus = McmConfigSchema.ReadMenus(mod.FolderPath, _settings.CurrentGamePath);
		if (mcmMenus.Count > 0 && mcmMenus.Any(m => m.Settings.Any()))
		{
			McmMenu menu = mcmMenus.First(m => m.Settings.Any());
			ShowMcmSettings(mod.Name, menu);
			return;
		}

		// A BepInEx mod keeps its settings OUTSIDE its own folder — BepInEx writes one file per plugin into
		// BepInEx\config — so looking for a config beside the mod, as every other game needs, finds nothing and
		// reports the mod as having no settings when nearly all of them do.
		string? bepInExConfig = FindBepInExConfigFor(mod);
		if (bepInExConfig != null)
		{
			ShowIniEditor(bepInExConfig, mod.Name);
			return;
		}

		string configPath = Path.Combine(mod.FolderPath, "config.json");
		if (!File.Exists(configPath))
		{
			Speak(Loc.T("modactions.noConfigSpeak", mod.Name));
			// A Bethesda mod's settings are often only reachable from an in-game menu built in the game's own
			// scripting, which no outside program can read — worth saying, so "no settings" isn't mistaken for
			// the manager having failed to look.
			SpeakBox(
				IsBethesdaGame
					? Loc.T("modactions.noConfigBethesdaBox", mod.Name)
					: Loc.T("modactions.noConfigBox", mod.Name),
				Loc.T("modactions.noConfigTitle"));
			return;
		}

		// An ordinary Stardew mod declares its options in code, so nothing on disk says what a setting accepts.
		// The value already in the file still says whether it is a yes/no, a number or text, and many mods ship
		// the labels for their in-game settings menu — enough for a settings list rather than raw JSON. A mod
		// holding nothing editable that way falls through to the JSON editor below.
		if (_settings.ActiveGame == GameProfiles.StardewValley &&
			ShowStardewModSettings(mod.Name, mod.FolderPath))
			return;

		OpenConfigEditor(mod.Name, configPath, delegate { }, Loc.T("config.labelConfiguration"));
	}

	/// <summary>
	/// Enables or disables all mods in the currently selected category at once,
	/// after prompting the user to confirm the batch operation.
	/// </summary>
	private async void BatchManageCategory()
	{
		string category = cmbCategoryFilter.SelectedItem?.ToString() ?? "All Categories";
		List<StardewMod> list = _allInstalledMods.Where((StardewMod m) => category == "All Categories" || m.Category == category).ToList();
		if (list.Count == 0)
		{
			Speak(Loc.T("modactions.noModsInCategory"));
			return;
		}
		DialogResult dialogResult = SpeakBox(Loc.T("modactions.batchBox", category, list.Count), Loc.T("modactions.batchTitle"), MessageBoxButtons.YesNoCancel);
		if (dialogResult == DialogResult.Cancel)
		{
			return;
		}
		bool flag = dialogResult == DialogResult.Yes;
		try
		{
			SetStatus(Loc.T("modactions.batchStatus", flag ? Loc.T("modactions.batchEnabling") : Loc.T("modactions.batchDisabling"), list.Count));
			foreach (StardewMod item in list)
			{
				if (item.IsEnabled != flag)
				{
					item.FolderPath = ModFileSystem.SetModEnabled(item.FolderPath, flag, _settings.ActiveGame);
					item.IsEnabled = flag;
				}
			}
			if (flag)
			{
				_soundEngine.Play("enable");
			}
			else
			{
				_soundEngine.Play("disable");
			}
			Speak(Loc.T("modactions.batchComplete", list.Count, flag ? Loc.T("modactions.batchEnabled") : Loc.T("modactions.batchDisabled")));

			// "Enabling 12 mods…" describes work that is now finished, so the title must not keep saying it.
			await RefreshModList(checkUpdates: false);
			ResetStatus();
		}
		catch (Exception ex)
		{
			ResetStatus();
			SpeakBox(Loc.T("modactions.batchFailedBox", FriendlyError(ex)));
		}
	}

	/// <summary>
	/// Identifies the missing required dependencies for the selected mod (via the one-click resolver) or, when the
	/// SMAPI Log tab is active, diagnoses the selected log line and offers to search for a named missing mod.
	/// </summary>
	private void QuickFixDependencies()
	{
		if (CurrentTab() == AppTab.Installed && listInstalled.SelectedItem is StardewMod)
		{
			_ = ResolveDependenciesAsync();
		}
		else
		{
			if (CurrentTab() != AppTab.SmapiLog || listLog.SelectedItem == null)
			{
				return;
			}
			string line = listLog.SelectedItem.ToString() ?? "";
			// If the line names a missing dependency, offer the actionable Discovery search directly.
			string text = LogAnalyzer.ExtractMissingModId(line);
			if (!string.IsNullOrEmpty(text))
			{
				if (SpeakBox(Loc.T("modactions.searchDepConfirm", text), Loc.T("modactions.quickFixLogTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					SelectTab(AppTab.Discovery);
					txtSearch.Text = text;
					_ = RunDiscovery();
				}
				return;
			}
			// Otherwise explain what the line means and how to fix it.
			string diagnosis = LogAnalyzer.Diagnose(line);
			Speak(Loc.T("modactions.diagnosing"));
			SpeakBox(diagnosis, Loc.T("modactions.diagnoseTitle"));
		}
	}

	/// <summary>
	/// Toggles the selected mod between enabled and disabled. How that is done on disk depends on the game —
	/// see <see cref="ModFileSystem.SetModEnabled"/>.
	/// </summary>
	private async void ToggleModStatus()
	{
		if (!(listInstalled.SelectedItem is StardewMod stardewMod))
		{
			return;
		}
		try
		{
			stardewMod.FolderPath = ModFileSystem.SetModEnabled(
				stardewMod.FolderPath, !stardewMod.IsEnabled, _settings.ActiveGame, LogError);
			stardewMod.IsEnabled = !stardewMod.IsEnabled;

			if (stardewMod.IsEnabled)
			{
				_soundEngine.Play("enable");
			}
			else
			{
				_soundEngine.Play("disable");
			}
			// Spoken straight away — waiting for the list to rebuild would delay the one confirmation that
			// says the key press worked. It is only spoken, not set as the status: "X is now disabled" is
			// news about a moment, and leaving it in the title bar leaves it reading as the current state of
			// the program long after it stopped being true.
			Speak(Loc.T("modactions.toggleStatus", stardewMod.Name, stardewMod.IsEnabled ? Loc.T("modactions.enabled") : Loc.T("modactions.disabled")));

			// Asset deployment and plugins.txt are reconciled inside RefreshModList, which re-scans the
			// toggled enabled set and rewrites both for Skyrim/Fallout 4.
			await RefreshModList(checkUpdates: false);
			ResetStatus();
		}
		catch (Exception ex)
		{
			ResetStatus();
			SpeakBox(Loc.T("modactions.toggleFailedBox", FriendlyError(ex)));
		}
	}

	/// <summary>
	/// Creates a safety backup of the selected mod, then permanently deletes its folder
	/// after the user confirms.
	/// </summary>
	private async void DeleteSelectedMod()
	{
		if (listInstalled.SelectedItem is not StardewMod stardewMod || stardewMod.IsGroup) return;

		// Warn if other installed mods declare a required dependency on this one — deleting it would break them.
		// The check is offline (manifest UniqueIDs on Stardew, plugin masters on Skyrim/Fallout 4).
		string confirm = Loc.T("modactions.deleteConfirm", stardewMod.Name);
		List<string> dependents = GetReverseDependents(stardewMod);
		if (dependents.Count > 0)
			confirm += Loc.T("modactions.deleteDependentsWarning", dependents.Count,
				string.Join("\n", dependents.Take(10)));

		if (SpeakBox(confirm, Loc.T("common.confirmDelete"), MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
			return;

		try
		{
			// Deleting keeps a backup first, and zipping a large mod is not instant. Say what is happening and
			// report progress the way downloads and installs do, so a long pause is never mistaken for a hang.
			SetStatus(Loc.T("modactions.deletingStatus", stardewMod.Name));
			await BackupModWithProgressAsync(stardewMod.FolderPath, stardewMod.Name + "_Delete", stardewMod.Name);

			// ForceDelete (via ModFileSystem) clears read-only attributes first; a plain Directory.Delete throws
			// "Access to the path '…' is denied" on mods that ship a read-only file such as SkyPatcher's DLL.
			// A mod that installed itself with a setup program usually leaves an uninstaller, and that knows far
			// more about what it put where than the manager ever can. Run it in preference to guessing.
			await RunModUninstallerIfAnyAsync(stardewMod);

			// A Witcher 3 mod's dlc and menu-config parts live in the game folder, not in the mod folder, and the
			// record of them is inside the folder about to be deleted — so they go first.
			ModFileSystem.RemoveWitcher3Extras(
				stardewMod.FolderPath, _settings.ActiveGame, _settings.CurrentGamePath, LogError);

			await Task.Run(() => ModFileSystem.DeleteModFolder(stardewMod.FolderPath));
			// The Witcher 3 lists its mods in a file of its own, and an entry outliving the folder keeps a deleted
			// mod in the game's own menu.
			ModFileSystem.ForgetWitcherMod(stardewMod.FolderPath, _settings.ActiveGame);
			// RefreshModList below re-scans without the deleted mod and reconciles deployment and
			// plugins.txt, pruning its files/plugins and restoring any provider it had overridden.
			_soundEngine.Play("disable");
			Speak(Loc.T("modactions.deletedStatus", stardewMod.Name));
			await RefreshModList(checkUpdates: false);
			ResetStatus();
		}
		catch (Exception ex)
		{
			ResetStatus();
			_soundEngine.Play("error");
			SpeakBox(Loc.T("modactions.deleteFailedBox", FriendlyError(ex)));
		}
	}

	/// <summary>
	/// Runs the mod's own uninstaller, if it registered one, and waits for it to finish.
	///
	/// This is the other half of installing a mod by running its author's program. Such a mod puts files in
	/// places the manager never saw — beside the game's executable, in the game's config folder — and deleting
	/// the mod folder leaves every one of them behind, still loaded by the game. The installer's own uninstaller
	/// holds the list of what it wrote, so it is asked to do the job.
	///
	/// Asked first, always, and a "no" simply falls through to removing the mod folder as usual.
	/// </summary>
	private async Task RunModUninstallerIfAnyAsync(StardewMod mod)
	{
		if (GameProfiles.Find(_settings.ActiveGame)?.IsWitcher3 != true) return;

		var uninstaller = ModFileSystem.FindUninstallerInsideGame(
			_settings.CurrentGamePath, Path.GetFileName(mod.FolderPath.TrimEnd(Path.DirectorySeparatorChar)));
		if (uninstaller == null) return;

		if (SpeakBox(Loc.T("modactions.runUninstallerBox", uninstaller.DisplayName),
				Loc.T("modactions.runUninstallerTitle"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
			return;

		SetStatus(Loc.T("modactions.uninstallerRunning", uninstaller.DisplayName));

		try
		{
			// Split the registered command into its executable and any arguments it already carries.
			string command = uninstaller.Command.Trim();
			string exe = command;
			string args = "";
			if (command.StartsWith("\""))
			{
				int close = command.IndexOf('"', 1);
				if (close > 0)
				{
					exe = command.Substring(1, close - 1);
					args = command.Substring(close + 1).Trim();
				}
			}

			using var process = new Process();
			process.StartInfo = new ProcessStartInfo(exe)
			{
				// Silent, because the alternative is a series of graphical prompts in another window. The manager
				// has already asked the only question that matters.
				Arguments = (args + " /SILENT /NORESTART").Trim(),
				UseShellExecute = true
			};
			process.Start();
			await process.WaitForExitAsync();

			// An Inno Setup uninstaller relaunches itself from a temporary copy and the process we started exits
			// at once, so its exit means nothing. The registry entry going away is what actually says "done".
			for (int waited = 0; waited < 120 && ModFileSystem.UninstallerStillRegistered(uninstaller); waited++)
				await Task.Delay(500);

			Speak(Loc.T("modactions.uninstallerFinished", uninstaller.DisplayName));
		}
		catch (Exception ex)
		{
			LogError("Delete", "Could not run the mod's uninstaller: " + ex.Message);
			SpeakBox(Loc.T("modactions.uninstallerFailedBox", FriendlyError(ex)));
		}
	}

	/// <summary>
	/// Toggles the Nexus endorsement of the mod selected in the active Nexus-aware list (Installed, Updates,
	/// or Discovery): if it isn't endorsed it offers to endorse it, and if it is, it offers to withdraw the
	/// endorsement (abstain). The action is confirmed with a yes/no prompt first, then the result is spoken.
	/// Nexus only allows endorsing a mod the account has downloaded and used for a short while, so a refusal
	/// is voiced with its reason rather than failing silently.
	/// </summary>
	private async void EndorseSelectedMod()
	{
		if (string.IsNullOrEmpty(_settings.ApiKey))
		{
			Speak(Loc.T("endorse.noLogin"));
			return;
		}

		StardewMod? mod = SelectedNexusMod();
		if (mod == null || string.IsNullOrEmpty(mod.NexusID))
		{
			Speak(Loc.T("endorse.noNexusMod"));
			return;
		}

		// Find out which way to flip. A toggle with an unknown current state would be confusing, so abort
		// rather than guess if Nexus can't tell us.
		Speak(Loc.T("endorse.checking", mod.Name));
		bool? alreadyEndorsed = await _nexusService.IsModEndorsedAsync(mod.NexusID);
		if (alreadyEndorsed == null)
		{
			_soundEngine.Play("error");
			Speak(Loc.T("endorse.statusUnknown"));
			return;
		}

		bool endorse = !alreadyEndorsed.Value;
		string prompt = endorse ? Loc.T("endorse.confirmEndorse", mod.Name) : Loc.T("endorse.confirmAbstain", mod.Name);
		string title  = endorse ? Loc.T("endorse.confirmEndorseTitle") : Loc.T("endorse.confirmAbstainTitle");
		if (SpeakBox(prompt, title, MessageBoxButtons.YesNo) != DialogResult.Yes)
		{
			Speak(Loc.T("endorse.cancelled"));
			return;
		}

		Speak(Loc.T(endorse ? "endorse.working" : "endorse.workingRemove", mod.Name));
		NexusService.EndorseOutcome outcome = await _nexusService.SetEndorsementAsync(mod.NexusID, endorse, mod.Version);

		string message = outcome switch
		{
			NexusService.EndorseOutcome.Endorsed      => Loc.T("endorse.success", mod.Name),
			NexusService.EndorseOutcome.Abstained     => Loc.T("endorse.abstained", mod.Name),
			NexusService.EndorseOutcome.TooSoon       => Loc.T("endorse.tooSoon"),
			NexusService.EndorseOutcome.NotDownloaded => Loc.T("endorse.notDownloaded"),
			NexusService.EndorseOutcome.OwnMod        => Loc.T("endorse.ownMod"),
			_                                         => Loc.T("endorse.failed", mod.Name)
		};

		bool success = outcome is NexusService.EndorseOutcome.Endorsed or NexusService.EndorseOutcome.Abstained;
		_soundEngine.Play(success ? "enable" : "error");
		Speak(message);
	}

	/// <summary>
	/// Speaks the account's remaining Nexus API quota (requests left this hour and today), captured from the
	/// last API response's rate-limit headers — so checking it costs no request of its own.
	/// </summary>
	private void ShowApiCredits()
	{
		if (string.IsNullOrEmpty(_settings.ApiKey))
		{
			Speak(Loc.T("credits.noLogin"));
			return;
		}
		if (!_nexusService.HasRateLimitInfo)
		{
			Speak(Loc.T("credits.unknown"));
			return;
		}
		Speak(Loc.T("credits.report", _nexusService.HourlyRemaining, _nexusService.DailyRemaining));
	}

	/// <summary>Permanently deletes the selected backup archive after user confirmation.</summary>
	private void DeleteSelectedBackup()
	{
		if (listBackups.SelectedItem is BackupItem backupItem && SpeakBox(Loc.T("modactions.deleteBackupConfirm", backupItem.Name), Loc.T("common.confirmDelete"), MessageBoxButtons.YesNo) == DialogResult.Yes)
		{
			try
			{
				File.Delete(backupItem.FullPath);
				_soundEngine.Play("disable");
				RefreshBackupsList();
			}
			catch (Exception ex)
			{
				SpeakBox(Loc.T("modactions.deleteBackupFailedBox", FriendlyError(ex)));
			}
		}
	}
}
