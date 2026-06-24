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

		string configPath = Path.Combine(mod.FolderPath, "config.json");
		if (!File.Exists(configPath))
		{
			Speak(Loc.T("modactions.noConfigSpeak", mod.Name));
			SpeakBox(Loc.T("modactions.noConfigBox", mod.Name), Loc.T("modactions.noConfigTitle"));
			return;
		}

		OpenConfigEditor(mod.Name, configPath, delegate { }, Loc.T("config.labelConfiguration"));
	}

	/// <summary>
	/// Enables or disables all mods in the currently selected category at once,
	/// after prompting the user to confirm the batch operation.
	/// </summary>
	private void BatchManageCategory()
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
					string path = Path.GetDirectoryName(item.FolderPath) ?? "";
					string fileName = Path.GetFileName(item.FolderPath);
					string text = (flag ? Path.Combine(path, fileName.Substring(1)) : Path.Combine(path, "." + fileName));
					Directory.Move(item.FolderPath, text);
					item.FolderPath = text;
					item.IsEnabled = flag;
				}
			}
			_ = RefreshModList(checkUpdates: false);
			if (flag)
			{
				_soundEngine.Play("enable");
			}
			else
			{
				_soundEngine.Play("disable");
			}
			Speak(Loc.T("modactions.batchComplete", list.Count, flag ? Loc.T("modactions.batchEnabled") : Loc.T("modactions.batchDisabled")));
		}
		catch (Exception ex)
		{
			SpeakBox(Loc.T("modactions.batchFailedBox", ex.Message));
		}
	}

	/// <summary>
	/// Displays a pop-up dialog listing all dependencies declared by the selected mod,
	/// annotated with their present/missing/version status.
	/// </summary>
	private void ShowDependencies()
	{
		if (!(listInstalled.SelectedItem is StardewMod stardewMod))
		{
			return;
		}
		if (stardewMod.Dependencies.Count == 0)
		{
			SpeakBox(Loc.T("modactions.noDependencies"));
			return;
		}
		StringBuilder stringBuilder = new StringBuilder(Loc.T("modactions.dependenciesHeader", stardewMod.Name));
		foreach (ModDependency dependency in stardewMod.Dependencies)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 3, stringBuilder2);
			handler.AppendLiteral("- ");
			handler.AppendFormatted(dependency.UniqueId);
			handler.AppendLiteral(": ");
			handler.AppendFormatted(dependency.IsPresent ? (dependency.IsEnabled ? (dependency.IsNewEnough ? Loc.T("modactions.depOK") : Loc.T("modactions.depOld")) : Loc.T("modactions.disabled")) : Loc.T("modactions.depMissing"));
			handler.AppendLiteral(" (");
			handler.AppendFormatted(dependency.IsRequired ? Loc.T("modactions.depReq") : Loc.T("modactions.depOpt"));
			handler.AppendLiteral(")");
			stringBuilder2.AppendLine(ref handler);
		}
		SpeakBox(stringBuilder.ToString(), Loc.T("modactions.dependenciesTitle"));
	}

	/// <summary>
	/// Identifies the first missing required dependency for the selected mod (or the selected
	/// SMAPI log entry) and offers to search for it in the Discovery tab.
	/// </summary>
	private void QuickFixDependencies()
	{
		if (listInstalled.SelectedItem is StardewMod stardewMod)
		{
			List<ModDependency> list = stardewMod.Dependencies.Where((ModDependency d) => d.IsRequired && !d.IsPresent).ToList();
			if (list.Count == 0)
			{
				Speak(Loc.T("modactions.noMissingDeps"));
				return;
			}
			ModDependency modDependency = list[0];
			if (SpeakBox(Loc.T("modactions.searchDepConfirm", modDependency.UniqueId), Loc.T("modactions.quickFixTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
			{
				SelectTab(AppTab.Discovery);
				txtSearch.Text = modDependency.UniqueId;
				_ = RunDiscovery();
			}
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
	/// Toggles the selected mod between enabled and disabled by renaming its folder
	/// with or without a leading dot.
	/// </summary>
	private void ToggleModStatus()
	{
		if (!(listInstalled.SelectedItem is StardewMod stardewMod))
		{
			return;
		}
		try
		{
			string path = Path.GetDirectoryName(stardewMod.FolderPath) ?? "";
			string fileName = Path.GetFileName(stardewMod.FolderPath);
			string text = (stardewMod.IsEnabled ? Path.Combine(path, "." + fileName) : Path.Combine(path, fileName.StartsWith(".") ? fileName.Substring(1) : fileName));

			Directory.Move(stardewMod.FolderPath, text);
			stardewMod.FolderPath = text;
			stardewMod.IsEnabled = !stardewMod.IsEnabled;

			// Asset deployment and plugins.txt are reconciled inside RefreshModList, which re-scans the
			// toggled enabled set and rewrites both for Skyrim/Fallout 4.
			_ = RefreshModList(checkUpdates: false);
			if (stardewMod.IsEnabled)
			{
				_soundEngine.Play("enable");
			}
			else
			{
				_soundEngine.Play("disable");
			}
			SetStatus(Loc.T("modactions.toggleStatus", stardewMod.Name, stardewMod.IsEnabled ? Loc.T("modactions.enabled") : Loc.T("modactions.disabled")));
		}
		catch (Exception ex)
		{
			SpeakBox(Loc.T("modactions.toggleFailedBox", ex.Message));
		}
	}

	/// <summary>
	/// Creates a safety backup of the selected mod, then permanently deletes its folder
	/// after the user confirms.
	/// </summary>
	private void DeleteSelectedMod()
	{
		if (listInstalled.SelectedItem is StardewMod stardewMod && SpeakBox(Loc.T("modactions.deleteConfirm", stardewMod.Name), Loc.T("common.confirmDelete"), MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
		{
			try
			{
				BackupMod(stardewMod.FolderPath, stardewMod.Name + "_Delete");

				// ForceDelete (via ModFileSystem) clears read-only attributes first; a plain Directory.Delete throws
				// "Access to the path '…' is denied" on mods that ship a read-only file such as SkyPatcher's DLL.
				ModFileSystem.DeleteModFolder(stardewMod.FolderPath);
				// RefreshModList below re-scans without the deleted mod and reconciles deployment and
				// plugins.txt, pruning its files/plugins and restoring any provider it had overridden.
				_soundEngine.Play("disable");
				SetStatus(Loc.T("modactions.deletedStatus", stardewMod.Name));
				_ = RefreshModList(checkUpdates: false);
			}
			catch (Exception ex)
			{
				_soundEngine.Play("error");
				SpeakBox(Loc.T("modactions.deleteFailedBox", ex.Message));
			}
		}
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
				SpeakBox(Loc.T("modactions.deleteBackupFailedBox", ex.Message));
			}
		}
	}
}
