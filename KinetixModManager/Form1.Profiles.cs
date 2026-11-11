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
		modProfile.Game = _settings.ActiveGame;

		// Asked, not overwritten without a word. A profile is somebody's setup, and the name is all that says which.
		string existingPath = ProfileStore.PathFor(profilesPath, text);
		if (existingPath.Length > 0 && File.Exists(existingPath) &&
			SpeakBox(Loc.T("profiles.overwriteConfirm", text), Loc.T("profiles.saveTitle"), MessageBoxButtons.YesNo) != DialogResult.Yes)
		{
			Speak(Loc.T("common.changesCancelled"));
			return;
		}

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

	/// <summary>
	/// The loaded copy's profiles: its own folder's, then any from the old shared folder that name one of its installed mods.
	/// Those were saved before profiles were kept per game, record no game, and are left where they are; a profile none of
	/// whose mods are here belongs to another game, and showing it here is how one game's load order reached another.
	/// </summary>
	private List<ModProfile> ProfilesForThisGame()
	{
		void Failed(string file, Exception ex) => LogFailure("Profiles", $"Failed to load profile '{file}'", ex);

		List<ModProfile> own = ProfileStore.LoadAll(profilesPath, Failed);
		var installed = new HashSet<string>(_allInstalledMods.Where(m => !m.IsGroup).Select(m => m.UniqueId),
			StringComparer.OrdinalIgnoreCase);
		foreach (ModProfile shared in ProfileStore.LoadAll(sharedProfilesPath, Failed))
		{
			if (own.Any(p => string.Equals(p.Name, shared.Name, StringComparison.OrdinalIgnoreCase))) continue;
			bool forThisGame = shared.Game != null
				? string.Equals(shared.Game, _settings.ActiveGame, StringComparison.OrdinalIgnoreCase)
				: shared.ModStates.Keys.Any(installed.Contains);
			if (forThisGame) own.Add(shared);
		}
		return own;
	}

	/// <summary>Scans the profiles directory and repopulates <c>listProfiles</c>.</summary>
	private void RefreshProfilesList()
	{
		if (_settings.ActiveGame == GameProfiles.NoGame) return;
		if (listProfiles == null)
		{
			return;
		}
		int oldIndex = listProfiles.SelectedIndex;
		string? oldName = (listProfiles.SelectedItem as ModProfile)?.Name;

		listProfiles.BeginUpdate();
		listProfiles.Items.Clear();
		foreach (ModProfile modProfile in ProfilesForThisGame())
		{
			listProfiles.Items.Add(modProfile);
		}

		if (listProfiles.Items.Count > 0)
		{
			int newIndex = -1;
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
			// The row found, wherever it is now. Taking the larger of the two landed on a different row whenever rows above had gone.
			listProfiles.SelectedIndex = newIndex >= 0 ? newIndex : Math.Min(Math.Max(oldIndex, 0), listProfiles.Items.Count - 1);

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
		if (RefusedWhileModsAreChanging()) return;
		if (SpeakBox(Loc.T("profiles.applyConfirm", profile.Name), Loc.T("profiles.applyTitle"), MessageBoxButtons.YesNo) == DialogResult.No)
		{
			return;
		}
		if (RefusedWhileModsAreChanging()) return;
		using IDisposable busy = BeginModOperation();
		try
		{
			SetStatus(Loc.T("profiles.applying"));
			bool anyEnabled = false;
			bool anyDisabled = false;
			// Only what actually differs. A mod the profile never mentioned is left alone rather than
			// switched off for not appearing, and one already in the right state is not renamed to the name
			// it already has - see ProfileStore.Changes.
			foreach (var change in ProfileStore.Changes(profile, _allInstalledMods))
			{
				// Left alone: its folder is the mods folder. See RefusedAsTheWholeModsFolder.
				if (IsTheWholeModsFolder(change.Mod)) continue;
				// Asset deployment and plugins.txt are reconciled once by RefreshModList at the end
				// (a profile can flip many mods at once), so only the folder enable/disable happens here.
				change.Mod.FolderPath = ModFileSystem.SetModEnabled(
					change.Mod.FolderPath, change.Enable, _settings.ActiveGame, locatedGameFolder: _settings.CurrentGamePath);
				change.Mod.IsEnabled = change.Enable;

				if (change.Enable) anyEnabled = true; else anyDisabled = true;
			}
			// Restore the profile's saved load order (mod priority and plugin order) for Skyrim/Fallout 4.
			// The RefreshModList call below then reconciles assets and rewrites plugins.txt accordingly.
			// Never another copy's order: its folder names are not this game's, and an order of names that match nothing is
			// thrown away and the list re-sorted, losing the order the user had tuned. See ProfilesForThisGame.
			bool orderIsThisGames = profile.Game == null || string.Equals(profile.Game, _settings.ActiveGame, StringComparison.OrdinalIgnoreCase);
			if (IsBethesdaGame && orderIsThisGames)
			{
				if (profile.ModPriority != null)
					_settings.ModPriority[_settings.ActiveGame] = new List<string>(profile.ModPriority);
				if (profile.PluginOrder != null)
					_settings.PluginOrder[_settings.ActiveGame] = new List<string>(profile.PluginOrder);
				if (profile.ModPriority != null || profile.PluginOrder != null)
					_settings.Save();
			}
			if (!string.IsNullOrEmpty(profile.ThemeOverride) && _themeFolders.Exists(profile.ThemeOverride))
			{
				_settings.CurrentTheme = profile.ThemeOverride;
				_settings.Save();
				Speak(Loc.T("profiles.themeSwitched", profile.ThemeOverride));
			}
			if (anyEnabled)
			{
				_soundEngine.Play("enable");
			}
			else if (anyDisabled)
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
