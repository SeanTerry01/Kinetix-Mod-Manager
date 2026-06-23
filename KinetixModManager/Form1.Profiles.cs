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

/// <summary>Mod profile save/apply/list management for Form1.</summary>
public partial class Form1
{
	/// <summary>
	/// Prompts for a profile name, then saves the current enabled/disabled state of every mod
	/// as a new <see cref="ModProfile"/> JSON file.
	/// </summary>
	private void CreateProfileFromCurrent()
	{
		string text = Interaction.InputBox(Loc.T("profiles.savePrompt"), Loc.T("profiles.saveTitle"));
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		ModProfile modProfile = new ModProfile
		{
			Name = text,
			ThemeOverride = _settings.CurrentTheme
		};
		foreach (StardewMod allInstalledMod in _allInstalledMods)
		{
			modProfile.ModStates[allInstalledMod.UniqueId] = allInstalledMod.IsEnabled;
		}
		// Capture the current Skyrim/Fallout 4 load order (mod priority and plugin order) so applying the
		// profile restores both.
		if (IsBethesdaGame)
		{
			EnsureModPriorityList();
			modProfile.ModPriority = new List<string>(_settings.ModPriority[_settings.ActiveGame]);
			if (_settings.PluginOrder.TryGetValue(_settings.ActiveGame, out List<string>? plugins) && plugins != null)
				modProfile.PluginOrder = new List<string>(plugins);
		}
		string contents = JsonConvert.SerializeObject(modProfile, Formatting.Indented);
		File.WriteAllText(Path.Combine(profilesPath, text + ".json"), contents);
		RefreshProfilesList();
		Speak(Loc.T("profiles.saved"));
	}

	/// <summary>Scans the profiles directory and repopulates <c>listProfiles</c>.</summary>
	private void RefreshProfilesList()
	{
		if (_settings.ActiveGame == "None") return;
		if (listProfiles == null)
		{
			return;
		}
		int oldIndex = listProfiles.SelectedIndex;
		string? oldName = (listProfiles.SelectedItem as ModProfile)?.Name;

		listProfiles.BeginUpdate();
		listProfiles.Items.Clear();
		if (Directory.Exists(profilesPath))
		{
			string[] files = Directory.GetFiles(profilesPath, "*.json");
			foreach (string path in files)
			{
				try
				{
					ModProfile? modProfile = JsonConvert.DeserializeObject<ModProfile>(File.ReadAllText(path));
					if (modProfile != null)
					{
						listProfiles.Items.Add(modProfile);
					}
				}
				catch (Exception ex)
				{
					LogError("Profiles", $"Failed to load profile '{Path.GetFileName(path)}': {ex.Message}");
				}
			}
		}

		if (listProfiles.Items.Count > 0)
		{
			int newIndex = 0;
			if (!string.IsNullOrEmpty(oldName))
			{
				for (int i = 0; i < listProfiles.Items.Count; i++)
				{
					if ((listProfiles.Items[i] as ModProfile)?.Name == oldName)
					{
						newIndex = i;
						break;
					}
				}
			}
			listProfiles.SelectedIndex = Math.Min(Math.Max(newIndex, oldIndex), listProfiles.Items.Count - 1);

			if (listProfiles.Focused && listProfiles.SelectedItem != null)
			{
				Speak(Loc.T("profiles.listItemPos", listProfiles.SelectedItem, listProfiles.SelectedIndex + 1, listProfiles.Items.Count));
			}
		}
		else if (listProfiles.Focused && oldIndex != -1)
		{
			Speak(Loc.T("common.listEmpty"));
		}
		listProfiles.EndUpdate();
	}

	/// <summary>
	/// Enables or disables mod folders on disk to match the saved state in <paramref name="profile"/>,
	/// then optionally switches the audio theme if the profile has a <see cref="ModProfile.ThemeOverride"/>.
	/// </summary>
	private void ApplyProfile(ModProfile profile)
	{
		if (SpeakBox(Loc.T("profiles.applyConfirm", profile.Name), Loc.T("profiles.applyTitle"), MessageBoxButtons.YesNo) == DialogResult.No)
		{
			return;
		}
		try
		{
			SetStatus(Loc.T("profiles.applying"));
			bool flag = false;
			bool flag2 = false;
			foreach (StardewMod allInstalledMod in _allInstalledMods)
			{
				if (profile.ModStates == null || !profile.ModStates.ContainsKey(allInstalledMod.UniqueId))
				{
					continue;
				}
				bool flag3 = profile.ModStates[allInstalledMod.UniqueId];
				if (allInstalledMod.IsEnabled != flag3)
				{
					// Asset deployment and plugins.txt are reconciled once by RefreshModList at the end
					// (a profile can flip many mods at once), so only the folder enable/disable happens here.
					string path = Path.GetDirectoryName(allInstalledMod.FolderPath) ?? "";
					string fileName = Path.GetFileName(allInstalledMod.FolderPath);
					string text = (flag3 ? Path.Combine(path, fileName.StartsWith(".") ? fileName.Substring(1) : fileName) : Path.Combine(path, "." + fileName));
					Directory.Move(allInstalledMod.FolderPath, text);
					allInstalledMod.FolderPath = text;
					allInstalledMod.IsEnabled = flag3;

					if (flag3)
					{
						flag = true;
					}
					else
					{
						flag2 = true;
					}
				}
			}
			// Restore the profile's saved load order (mod priority and plugin order) for Skyrim/Fallout 4.
			// The RefreshModList call below then reconciles assets and rewrites plugins.txt accordingly.
			if (IsBethesdaGame)
			{
				if (profile.ModPriority != null)
					_settings.ModPriority[_settings.ActiveGame] = new List<string>(profile.ModPriority);
				if (profile.PluginOrder != null)
					_settings.PluginOrder[_settings.ActiveGame] = new List<string>(profile.PluginOrder);
				if (profile.ModPriority != null || profile.PluginOrder != null)
					_settings.Save();
			}
			if (!string.IsNullOrEmpty(profile.ThemeOverride) && Directory.Exists(Path.Combine(themesPath, profile.ThemeOverride)))
			{
				_settings.CurrentTheme = profile.ThemeOverride;
				_settings.Save();
				Speak(Loc.T("profiles.themeSwitched", profile.ThemeOverride));
			}
			_ = RefreshModList(checkUpdates: false);
			if (flag)
			{
				_soundEngine.Play("enable");
			}
			else if (flag2)
			{
				_soundEngine.Play("disable");
			}
			else
			{
				_soundEngine.Play("connect");
			}
			SetStatus(Loc.T("profiles.applied", profile.Name));
		}
		catch (Exception ex)
		{
			SpeakBox(Loc.T("profiles.applyFailed", ex.Message));
		}
	}
}
