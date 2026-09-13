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
		string? text = ShowTextPrompt(Loc.T("profiles.saveTitle"), Loc.T("profiles.savePrompt"), "");
		if (text == null) { Speak(Loc.T("common.changesCancelled")); return; }

		text = text.Trim();
		if (text.Length == 0) { Speak(Loc.T("profiles.nameEmpty")); return; }

		// The Skyrim/Fallout 4 load order is captured too, so applying the profile restores both. Null for
		// the other games, which is what leaves their order alone rather than clearing it.
		List<string>? priority = null;
		List<string>? plugins = null;
		if (IsBethesdaGame)
		{
			EnsureModPriorityList();
			priority = new List<string>(_settings.ModPriority[_settings.ActiveGame]);
			if (_settings.PluginOrder.TryGetValue(_settings.ActiveGame, out List<string>? saved) && saved != null)
				plugins = new List<string>(saved);
		}

		ModProfile modProfile = ProfileStore.Capture(text, _allInstalledMods, _settings.CurrentTheme, priority, plugins);

		if (ProfileStore.Save(profilesPath, modProfile).Length == 0)
		{
			// The name survived the length check but nothing usable was left of it once the characters a
			// file name cannot hold were removed.
			Speak(Loc.T("profiles.nameEmpty"));
			return;
		}

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
		foreach (ModProfile modProfile in ProfileStore.LoadAll(profilesPath,
			(file, ex) => LogFailure("Profiles", $"Failed to load profile '{file}'", ex)))
		{
			listProfiles.Items.Add(modProfile);
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
	private async void ApplyProfile(ModProfile profile)
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
			// Only what actually differs. A mod the profile never mentioned is left alone rather than
			// switched off for not appearing, and one already in the right state is not renamed to the name
			// it already has - see ProfileStore.Changes.
			foreach (var change in ProfileStore.Changes(profile, _allInstalledMods))
			{
				// Asset deployment and plugins.txt are reconciled once by RefreshModList at the end
				// (a profile can flip many mods at once), so only the folder enable/disable happens here.
				change.Mod.FolderPath = ModFileSystem.SetModEnabled(
					change.Mod.FolderPath, change.Enable, _settings.ActiveGame);
				change.Mod.IsEnabled = change.Enable;

				if (change.Enable) flag = true; else flag2 = true;
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
				_soundEngine.Play("load_complete");
			}
			// Announced, then the title goes back to resting — "Applied profile X" is something that happened,
			// not the state the program is in.
			Speak(Loc.T("profiles.applied", profile.Name));
			await RefreshModList(checkUpdates: false);
			ResetStatus();
		}
		catch (Exception ex)
		{
			ResetStatus();
			SpeakBox(Loc.T("profiles.applyFailed", FriendlyError(ex)));
		}
	}
}
