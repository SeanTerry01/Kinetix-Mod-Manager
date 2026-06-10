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
			string text = Interaction.InputBox("Assign category for " + stardewMod.Name + ":", "Change Category", stardewMod.Category);
			if (!string.IsNullOrEmpty(text))
			{
				_settings.ModCategories[stardewMod.UniqueId] = text.Trim();
				_settings.Save();
				_ = RefreshModList(checkUpdates: false);
				Speak($"Category for {stardewMod.Name} set to {text}.");
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
			Speak("No mod selected.");
			return;
		}
		if (mod.IsGroup)
		{
			Speak("This is a mod group. Expand it and select an individual mod first.");
			return;
		}

		string manifestName = _settings.ActiveGame == "StardewValley" ? "manifest.json" : ".manager_manifest.json";
		string manifestPath = Path.Combine(mod.FolderPath, manifestName);
		if (!File.Exists(manifestPath))
		{
			Speak($"No manifest file found for {mod.Name}.");
			MessageBox.Show($"No manifest file ({manifestName}) was found for {mod.Name}.", "Manifest Not Found");
			return;
		}

		OpenConfigEditor(mod.Name, manifestPath, delegate
		{
			// Re-scan so a corrected version is re-read; the refresh's prune pass also drops the mod
			// from the updates list when its version now matches the latest. checkUpdates:false avoids
			// re-running the full Nexus/GitHub update query.
			_ = RefreshModList(checkUpdates: false);
		}, "Manifest");
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
			Speak("No mod selected.");
			return;
		}
		if (mod.IsGroup)
		{
			Speak("This is a mod group. Expand it and select an individual mod first.");
			return;
		}

		string configPath = Path.Combine(mod.FolderPath, "config.json");
		if (!File.Exists(configPath))
		{
			Speak($"No config file found for {mod.Name}. Most mods create their config file the first time the game runs with the mod enabled.");
			MessageBox.Show($"No config.json was found for {mod.Name}.\n\nMany mods only create their config file the first time you launch the game with the mod enabled. Run the game once, then try again.", "Config Not Found");
			return;
		}

		OpenConfigEditor(mod.Name, configPath, delegate { }, "Configuration");
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
			Speak("No mods in this category.");
			return;
		}
		DialogResult dialogResult = MessageBox.Show($"Batch Action for category '{category}':\n\nYES: Enable all {list.Count} mods.\nNO: Disable all {list.Count} mods.\nCANCEL: Do nothing.", "Batch Category Management", MessageBoxButtons.YesNoCancel);
		if (dialogResult == DialogResult.Cancel)
		{
			return;
		}
		bool flag = dialogResult == DialogResult.Yes;
		try
		{
			SetStatus($"Batch {(flag ? "Enabling" : "Disabling")} {list.Count} mods...");
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
			Speak($"Batch action complete. {list.Count} mods {(flag ? "Enabled" : "Disabled")}.");
		}
		catch (Exception ex)
		{
			MessageBox.Show("Batch action failed: " + ex.Message);
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
			MessageBox.Show("No dependencies.");
			return;
		}
		StringBuilder stringBuilder = new StringBuilder("Dependencies for " + stardewMod.Name + ":\n");
		foreach (ModDependency dependency in stardewMod.Dependencies)
		{
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(7, 3, stringBuilder2);
			handler.AppendLiteral("- ");
			handler.AppendFormatted(dependency.UniqueId);
			handler.AppendLiteral(": ");
			handler.AppendFormatted(dependency.IsPresent ? (dependency.IsEnabled ? (dependency.IsNewEnough ? "OK" : "Old") : "Disabled") : "Missing");
			handler.AppendLiteral(" (");
			handler.AppendFormatted(dependency.IsRequired ? "Req" : "Opt");
			handler.AppendLiteral(")");
			stringBuilder2.AppendLine(ref handler);
		}
		MessageBox.Show(stringBuilder.ToString(), "Dependencies");
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
				Speak("No missing required dependencies for this mod.");
				return;
			}
			ModDependency modDependency = list[0];
			if (MessageBox.Show("Search for missing dependency: " + modDependency.UniqueId + "?", "Quick-Fix", MessageBoxButtons.YesNo) == DialogResult.Yes)
			{
				mainTabs.SelectedIndex = (int)AppTab.Discovery;
				txtSearch.Text = modDependency.UniqueId;
				_ = RunDiscovery();
			}
		}
		else
		{
			if (mainTabs.SelectedIndex != (int)AppTab.SmapiLog || listLog.SelectedItem == null)
			{
				return;
			}
			string text = LogAnalyzer.ExtractMissingModId(listLog.SelectedItem.ToString() ?? "");
			if (!string.IsNullOrEmpty(text))
			{
				if (MessageBox.Show("Search for missing dependency: " + text + "?", "Quick-Fix from Log", MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					mainTabs.SelectedIndex = (int)AppTab.Discovery;
					txtSearch.Text = text;
					_ = RunDiscovery();
				}
			}
			else
			{
				Speak("Could not identify a missing mod in this log entry.");
			}
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
			
			if ((_settings.ActiveGame == "SkyrimSE" || _settings.ActiveGame == "Fallout4") && stardewMod.IsEnabled)
			{
				string gameData = Path.Combine(_settings.CurrentGamePath, "Data");
				ModFileSystem.DeployModFiles(stardewMod.FolderPath, gameData, false, LogError);
				ModFileSystem.SyncPluginsFile(stardewMod.FolderPath, _settings.ActiveGame, false, LogError);
			}

			Directory.Move(stardewMod.FolderPath, text);
			stardewMod.FolderPath = text;
			stardewMod.IsEnabled = !stardewMod.IsEnabled;

			if ((_settings.ActiveGame == "SkyrimSE" || _settings.ActiveGame == "Fallout4") && stardewMod.IsEnabled)
			{
				string gameData = Path.Combine(_settings.CurrentGamePath, "Data");
				ModFileSystem.DeployModFiles(stardewMod.FolderPath, gameData, true, LogError);
				ModFileSystem.SyncPluginsFile(stardewMod.FolderPath, _settings.ActiveGame, true, LogError);
			}

			_ = RefreshModList(checkUpdates: false);
			if (stardewMod.IsEnabled)
			{
				_soundEngine.Play("enable");
			}
			else
			{
				_soundEngine.Play("disable");
			}
			SetStatus(stardewMod.Name + " is now " + (stardewMod.IsEnabled ? "Enabled" : "Disabled"));
		}
		catch (Exception ex)
		{
			MessageBox.Show("Toggle failed: " + ex.Message);
		}
	}

	/// <summary>
	/// Creates a safety backup of the selected mod, then permanently deletes its folder
	/// after the user confirms.
	/// </summary>
	private void DeleteSelectedMod()
	{
		if (listInstalled.SelectedItem is StardewMod stardewMod && MessageBox.Show("Are you sure you want to PERMANENTLY DELETE " + stardewMod.Name + "?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
		{
			try
			{
				BackupMod(stardewMod.FolderPath, stardewMod.Name + "_Delete");
				
				if (_settings.ActiveGame == "SkyrimSE" || _settings.ActiveGame == "Fallout4")
				{
					string gameData = Path.Combine(_settings.CurrentGamePath, "Data");
					ModFileSystem.DeployModFiles(stardewMod.FolderPath, gameData, false, LogError);
					ModFileSystem.SyncPluginsFile(stardewMod.FolderPath, _settings.ActiveGame, false, LogError);
				}

				Directory.Delete(stardewMod.FolderPath, recursive: true);
				_soundEngine.Play("disable");
				SetStatus("Deleted " + stardewMod.Name);
				_ = RefreshModList(checkUpdates: false);
			}
			catch (Exception ex)
			{
				_soundEngine.Play("error");
				MessageBox.Show("Failed to delete mod: " + ex.Message);
			}
		}
	}

	/// <summary>Permanently deletes the selected backup archive after user confirmation.</summary>
	private void DeleteSelectedBackup()
	{
		if (listBackups.SelectedItem is BackupItem backupItem && MessageBox.Show("Permanently delete backup " + backupItem.Name + "?", "Confirm Delete", MessageBoxButtons.YesNo) == DialogResult.Yes)
		{
			try
			{
				File.Delete(backupItem.FullPath);
				_soundEngine.Play("disable");
				RefreshBackupsList();
			}
			catch (Exception ex)
			{
				MessageBox.Show("Delete failed: " + ex.Message);
			}
		}
	}
}
