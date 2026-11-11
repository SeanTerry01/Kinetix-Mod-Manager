using System.Diagnostics;

namespace KinetixModManager;

/// <summary>Mod actions: categories, dependencies, enable/disable, and deletion for Form1.</summary>
public partial class Form1
{
	/// <summary>Prompts the user to assign a custom category string to the selected mod.</summary>
	private void SetManualCategory()
	{
		if (ModOnThisTab() is not GameMod selectedMod)
		{
			Speak(Loc.T("common.noModSelected"));
			return;
		}
		if (selectedMod.IsGroup)
		{
			Speak(Loc.T("common.modGroupFirst"));
			return;
		}

		// Said while the box is still open, and the row changed once that has been heard, so the reader reads the new
		// category as focus comes back (see ActBeforeReturning). Said after it, the result raced the reader's reading of
		// the old row and the position was lost.
		bool changed = false;
		string? text = ShowTextPrompt(
			Loc.T("modactions.changeCategoryTitle"),
			Loc.T("modactions.changeCategoryPrompt", selectedMod.Name),
			selectedMod.Category,
			actOnAnswer: typed =>
			{
				string chosen = typed.Trim();
				// An emptied box now means something, and the obvious thing: put the mod back where it was before a
				// category was ever assigned. The old input box could not tell that from a cancel.
				if (chosen.Length == 0)
				{
					if (!_settings.ModCategories.Remove(selectedMod.UniqueId))
						return new ResultBeforeReturning(Loc.T("common.changesCancelled"));
					_settings.Save();
					changed = true;
					return new ResultBeforeReturning(Loc.T("modactions.categoryCleared", selectedMod.Name));
				}
				_settings.ModCategories[selectedMod.UniqueId] = chosen;
				_settings.Save();
				changed = true;
				return new ResultBeforeReturning(Loc.T("modactions.categorySet", selectedMod.Name, chosen), () =>
				{
					selectedMod.Category = chosen;
					RebuildInstalledListBox(selectedMod.UniqueId);
				});
			},
			sayOnCancel: Loc.T("common.changesCancelled"));

		// From disk as well, quietly: a cleared category falls back to one only the scan can work out.
		if (text != null && changed) Fire(RefreshModList(checkUpdates: false), "RefreshModList");
	}

	/// <summary>
	/// Prompts for a personal free-text note on the selected mod (e.g. "keep disabled until year 2"). The note is
	/// spoken whenever the mod is selected in the list. Emptying the box clears the note, after a confirmation —
	/// kept not because an emptied box is ambiguous (it no longer is) but because a note is typed by hand and
	/// nothing here can bring it back.
	/// </summary>
	private void SetModNote()
	{
		if (ModOnThisTab() is not GameMod mod)
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
		// A note typed is said while the box is still open and the row changed after, like a category. Clearing one asks
		// first, so that question comes once the box has gone. Escape is said by the box as it closes.
		string? typed = ShowTextPrompt(Loc.T("modactions.noteTitle"), Loc.T("modactions.notePrompt", mod.Name), existing,
			actOnAnswer: t =>
			{
				string note = t.Trim();
				if (note.Length == 0) return null;
				_settings.ModNotes[mod.UniqueId] = note;
				_settings.Save();
				return new ResultBeforeReturning(Loc.T("modactions.noteSet", mod.Name), () =>
				{
					mod.Note = note;
					RebuildInstalledListBox(mod.UniqueId);
				});
			},
			sayOnCancel: Loc.T("common.changesCancelled"));

		// Escape is now distinguishable from an emptied box, which is what the old input box could never manage.
		if (typed == null) return;

		string text = typed.Trim();

		if (text.Length == 0)
		{
			// The confirmation stays, but for a different reason than before: not because we cannot tell what was
			// meant, but because clearing throws away a sentence typed by hand and nothing else here can undo it.
			if (_settings.ModNotes.ContainsKey(mod.UniqueId) &&
				SpeakBox(Loc.T("modactions.noteClearConfirm", mod.Name), Loc.T("modactions.noteTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
			{
				_settings.ModNotes.Remove(mod.UniqueId);
				_settings.Save();
				Fire(SayAndRefreshModList(Loc.T("modactions.noteCleared", mod.Name), mod), "RefreshModList");
			}
			else
			{
				Speak(Loc.T("common.changesCancelled"));
			}
			return;
		}
		// Already saved and said by the box. See above.
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
		if (!(ModOnThisTab() is GameMod mod))
		{
			Speak(Loc.T("common.noModSelected"));
			return;
		}
		if (mod.IsGroup)
		{
			Speak(Loc.T("common.modGroupFirst"));
			return;
		}

		string manifestName = GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley) ? "manifest.json" : ".manager_manifest.json";
		string manifestPath = Path.Combine(mod.FolderPath, manifestName);
		if (!File.Exists(manifestPath))
		{
			SpeakBox(Loc.T("modactions.noManifestBox", manifestName, mod.Name), Loc.T("modactions.noManifestTitle"));
			return;
		}

		// As with the settings editor: back to the mod whose manifest this is, however the editor was left.
		EditModKeepingPlace(mod, () => OpenConfigEditor(mod.Name, manifestPath, delegate
		{
			// Re-scan so a corrected version is re-read; the refresh's prune pass also drops the mod
			// from the updates list when its version now matches the latest. checkUpdates:false avoids
			// re-running the full Nexus/GitHub update query.
			Fire(RefreshModList(checkUpdates: false), "RefreshModList");
		}, Loc.T("config.labelManifest")));
	}

	/// <summary>
	/// The BepInEx settings file belonging to <paramref name="mod"/>, or <c>null</c> when it has none.
	///
	/// BepInEx names these files after the plugin's id rather than its folder, and writes the plugin's name and
	/// id into the header of each — so the match is made on the id first (exact, and what BepInEx itself uses)
	/// and on the displayed name second, for a plugin too old to have written an id.
	/// </summary>
	private string? FindBepInExConfigFor(GameMod mod)
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

	/// <summary>
	/// Opens the selected mod's <c>config.json</c> in the JSON editor itself, bypassing the settings list.
	///
	/// The pair to <see cref="OpenSelectedModManifest"/>, and the answer to a gap the settings list created: once a
	/// mod's settings could be offered as choices, there was no longer any way to reach the file behind them — and
	/// a list can only offer what a mod declares. A setting an author never described, or a value outside the ones
	/// they listed, is only reachable in the file.
	/// </summary>
	private void OpenSelectedModConfigFile()
	{
		if (!(ModOnThisTab() is GameMod mod))
		{
			Speak(Loc.T("common.noModSelected"));
			return;
		}
		if (mod.IsGroup)
		{
			Speak(Loc.T("common.modGroupFirst"));
			return;
		}

		// A BepInEx game does not keep a mod's settings beside the mod, and does not write JSON: BepInEx puts one
		// .cfg per plugin into BepInEx\config. Looking for config.json in the mod's folder therefore found nothing
		// on Moonlight Peaks and reported every mod as having no config file, when nearly all of them have one.
		// The same search Ctrl+E uses answers this, so the two agree about which file a mod's settings live in.
		string? bepInExConfig = FindBepInExConfigFor(mod);
		string configPath = bepInExConfig ?? Path.Combine(mod.FolderPath, "config.json");
		if (!File.Exists(configPath))
		{
			// Most mods write their config only after the game has run once with the mod enabled, so "not there
			// yet" is the usual reason rather than anything being wrong.
			SpeakBox(
				IsBepInExGame
					? Loc.T("modactions.noConfigFileBepInExBox", mod.Name, GetShortcutString("OpenConfig"))
					: Loc.T("modactions.noConfigFileBox", mod.Name, GetShortcutString("OpenConfig")),
				Loc.T("modactions.noConfigTitle"));
			return;
		}

		EditModKeepingPlace(mod, () => OpenConfigEditor(mod.Name, configPath, delegate
		{
			// The file may have changed a version or a name the list shows, so re-read it — the same reason the
			// manifest editor refreshes.
			Fire(RefreshModList(checkUpdates: false), "RefreshModList");
		}, Loc.T("config.labelConfiguration"),
		// A BepInEx .cfg is not JSON, and checking it as JSON would refuse to save a perfectly good file.
		validateJson: bepInExConfig == null));
	}

	private void OpenSelectedModConfig()
	{
		if (!(ModOnThisTab() is GameMod mod))
		{
			Speak(Loc.T("common.noModSelected"));
			return;
		}
		if (mod.IsGroup)
		{
			Speak(Loc.T("common.modGroupFirst"));
			return;
		}

		// Come back to the mod the settings were opened from, whichever of the editors below it turned out to
		// need and however the user left it. Saving refreshes the list, which used to leave them somewhere else
		// entirely — halfway through working down a long list, that means finding their place again every time.
		EditModKeepingPlace(mod, () => OpenModSettingsFor(mod));
	}

	/// <summary>
	/// The player's <c>user.settings</c> — where The Witcher 3 keeps every mod's settings, in the player's
	/// documents rather than anywhere near the mod.
	/// </summary>
	private string Witcher3UserSettingsPath()
	{
		GameProfile? profile = GameProfiles.Find(_settings.ActiveGame);
		string userData = profile?.UserDataDirectoryFor(_settings.CurrentGamePath) ?? "";
		return userData.Length == 0 ? "" : Path.Combine(userData, Witcher3UserConfig.UserSettingsFileName);
	}

	/// <summary>
	/// Checks the two ways a Witcher 3 settings edit is silently thrown away, and says so before the user spends
	/// time making one. Returns false when they choose not to go on.
	///
	/// The game rewrites user.settings wholesale when it exits, so anything changed while it is running is
	/// overwritten — the edit appears to work and is simply gone. And if the file has been made read-only (a
	/// trick people are told to use to stop the game rewriting it), the game itself silently fails to save any
	/// in-game change too. Both are quiet failures, which is why they are worth interrupting for.
	/// </summary>
	private bool WarnBeforeEditingWitcherSettings()
	{
		GameProfile? profile = GameProfiles.Find(_settings.ActiveGame);
		if (profile != null && IsGameProcessRunning(profile.GameExeName))
		{
			if (SpeakBox(Loc.T("witcher.settingsGameRunningBox"), Loc.T("witcher.settingsWarningTitle"),
					MessageBoxButtons.YesNo) != DialogResult.Yes)
			{
				Speak(Loc.T("witcher.settingsNotOpened"));
				return false;
			}
		}

		string path = Witcher3UserSettingsPath();
		try
		{
			if (path.Length > 0 && File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
			{
				if (SpeakBox(Loc.T("witcher.settingsReadOnlyBox"), Loc.T("witcher.settingsWarningTitle"),
						MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
					Speak(Loc.T("witcher.settingsReadOnlyCleared"));
				}
			}
		}
		catch (Exception ex) { LogFailure(path, "Could not check user.settings", ex); }

		return true;
	}

	private void OpenModSettingsFor(GameMod mod)
	{
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

		// A Witcher 3 mod declares its Options → Mods menu in the game's config matrix, and the game keeps the
		// values in the player's own user.settings — neither of which is in the mod folder, which is why the
		// settings key used to find nothing at all for these mods.
		if (GameProfiles.Find(_settings.ActiveGame)?.IsWitcher3 == true)
		{
			McmMenu? witcherMenu = Witcher3UserConfig.ReadMenu(
				mod.FolderPath, _settings.CurrentGamePath, Witcher3UserSettingsPath());
			if (witcherMenu != null && witcherMenu.Settings.Any())
			{
				if (!WarnBeforeEditingWitcherSettings()) return;
				ShowMcmSettings(mod.Name, witcherMenu);
				return;
			}
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

		// A Fabric mod keeps its settings outside its jar too, in .minecraft\config. The file is flat JSON of
		// the same shape an ordinary Stardew mod writes, so the settings LIST is reused rather than dropping
		// the user into raw JSON: a yes/no is offered as two choices, and a number is checked for being one.
		if (GameProfiles.Find(_settings.ActiveGame)?.IsMinecraft == true)
		{
			string minecraftConfig = FindMinecraftConfigFor(mod);
			if (minecraftConfig.Length > 0 &&
				ShowStardewModSettings(mod.Name, mod.FolderPath, minecraftConfig))
				return;
		}

		string configPath = Path.Combine(mod.FolderPath, "config.json");
		if (!File.Exists(configPath))
		{
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
		// IsGame, so a second copy of the game gets the settings list too rather than the raw JSON editor.
		if (GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley) &&
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
		List<GameMod> list = _allInstalledMods.Where((GameMod m) => category == "All Categories" || m.Category == category).ToList();
		if (list.Count == 0)
		{
			Speak(Loc.T("modactions.noModsInCategory"));
			return;
		}
		if (RefusedWhileModsAreChanging()) return;
		DialogResult dialogResult = SpeakBox(Loc.T("modactions.batchBox", category, list.Count), Loc.T("modactions.batchTitle"), MessageBoxButtons.YesNoCancel);
		if (dialogResult == DialogResult.Cancel)
		{
			return;
		}
		if (RefusedWhileModsAreChanging()) return;
		using IDisposable busy = BeginModOperation();
		bool flag = dialogResult == DialogResult.Yes;
		try
		{
			SetStatus(Loc.T("modactions.batchStatus", flag ? Loc.T("modactions.batchEnabling") : Loc.T("modactions.batchDisabling"), list.Count));
			foreach (GameMod item in list)
			{
				// Left alone: its folder is the mods folder. See RefusedAsTheWholeModsFolder.
				if (item.IsEnabled != flag && !IsTheWholeModsFolder(item))
				{
					item.FolderPath = ModFileSystem.SetModEnabled(item.FolderPath, flag, _settings.ActiveGame,
						locatedGameFolder: _settings.CurrentGamePath);
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
			// "Enabling 12 mods…" describes work that is now finished, so the title must not keep saying it.
			await SayAndRefreshModList(Loc.T("modactions.batchComplete", list.Count, flag ? Loc.T("modactions.batchEnabled") : Loc.T("modactions.batchDisabled")));
			ResetStatus();
		}
		catch (Exception ex)
		{
			ResetStatus();
			SpeakBox(Loc.T("modactions.batchFailedBox", FriendlyError(ex)));
			// Some were renamed before it stopped: the list is made to match what is on disk.
			await RefreshModList(checkUpdates: false);
		}
	}

	/// <summary>
	/// Identifies the missing required dependencies for the selected mod (via the one-click resolver) or, when the
	/// SMAPI Log tab is active, diagnoses the selected log line and offers to search for a named missing mod.
	/// </summary>
	private void QuickFixDependencies()
	{
		if (CurrentTab() == AppTab.Installed && listInstalled.SelectedItem is GameMod)
		{
			Fire(ResolveDependenciesAsync(), "ResolveDependenciesAsync");
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
					// The log names the mod by its identifier, and the prompt above quotes it back because that is
					// what the user can see in the log. The SEARCH has to be the mod's actual name: Nexus has never
					// heard of "Digus.ProducerFrameworkMod", only of "Producer Framework Mod".
					txtSearch.Text = ModNameMatch.SearchTermForIdentifier(text);
					Fire(RunDiscovery(), "RunDiscovery");
				}
				return;
			}
			// Otherwise explain what the line means and how to fix it.
			string diagnosis = LogAnalyzer.Diagnose(line);
			SpeakBox(diagnosis, Loc.T("modactions.diagnoseTitle"));
		}
	}

	/// <summary>
	/// Toggles the selected mod between enabled and disabled. How that is done on disk depends on the game —
	/// see <see cref="ModFileSystem.SetModEnabled"/>.
	/// </summary>
	/// <summary>
	/// Whether <paramref name="mod"/> is files loose in the mods folder itself, and so refused, said, for anything that renames
	/// or deletes its folder. Stardew lists a Mods\manifest.json as a mod whose folder is the whole of Mods: Space renamed
	/// Mods to .Mods and every mod went, and Delete deleted them all. The Linux head always checked; this one didn't.
	/// </summary>
	private bool RefusedAsTheWholeModsFolder(GameMod mod)
	{
		if (!ModOnDisk.IsTheModsFolderItself(mod.FolderPath, _settings.CurrentModsPath)) return false;
		_soundEngine.Play("error");
		Speak(Loc.T("modactions.rowIsModsFolder", mod.Name));
		return true;
	}

	/// <summary>The same, quietly, for a batch that goes on with every other mod.</summary>
	private bool IsTheWholeModsFolder(GameMod mod) => ModOnDisk.IsTheModsFolderItself(mod.FolderPath, _settings.CurrentModsPath);

	private async void ToggleModStatus()
	{
		if (!(listInstalled.SelectedItem is GameMod selectedMod))
		{
			return;
		}
		// Renaming a folder that is being zipped or replaced is how a mod ends up half of each.
		if (ModOperationRunning)
		{
			Speak(Loc.T("session.busy"));
			return;
		}
		// A group row's folder is the whole mods folder, so switching one "off" renamed that, and every mod in the game
		// vanished from the list with no way back from inside the manager.
		if (selectedMod.IsGroup)
		{
			Speak(Loc.T("common.modGroupFirst"));
			return;
		}
		if (RefusedAsTheWholeModsFolder(selectedMod)) return;
		try
		{
			selectedMod.FolderPath = ModFileSystem.SetModEnabled(
				selectedMod.FolderPath, !selectedMod.IsEnabled, _settings.ActiveGame, LogError, _settings.CurrentGamePath);
			selectedMod.IsEnabled = !selectedMod.IsEnabled;

			if (selectedMod.IsEnabled)
			{
				_soundEngine.Play("enable");
			}
			else
			{
				_soundEngine.Play("disable");
			}
			// The sound above says the key press worked. This is only spoken, not set as the status: "X is now disabled"
			// is news about a moment, and leaving it in the title bar leaves it reading as the current state of the
			// program long after it stopped being true.
			//
			// Asset deployment and plugins.txt are reconciled inside RefreshModList, which re-scans the
			// toggled enabled set and rewrites both for Skyrim/Fallout 4.
			await SayAndRefreshModList(
				WithHeldOffNote(Loc.T("modactions.toggleStatus", selectedMod.Name, selectedMod.IsEnabled ? Loc.T("modactions.enabled") : Loc.T("modactions.disabled"))),
				selectedMod);
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
		if (listInstalled.SelectedItem is not GameMod selectedMod || selectedMod.IsGroup) return;
		if (RefusedAsTheWholeModsFolder(selectedMod)) return;
		// Pressed again during the backup, this used to ask again and delete twice.
		if (ModOperationRunning)
		{
			Speak(Loc.T("session.busy"));
			return;
		}

		// Warn if other installed mods declare a required dependency on this one — deleting it would break them.
		// The check is offline (manifest UniqueIDs on Stardew, plugin masters on Skyrim/Fallout 4).
		string confirm = Loc.T("modactions.deleteConfirm", selectedMod.Name);
		List<string> dependents = GetReverseDependents(selectedMod);
		if (dependents.Count > 0)
			confirm += Loc.T("modactions.deleteDependentsWarning", dependents.Count,
				string.Join("\n", dependents.Take(10)));

		// "Deleting…" is said before the question closes, so focus coming back doesn't cut it, nor it the reader's reading
		// of the row. See ActBeforeReturning.
		if (SpeakBox(confirm, Loc.T("common.confirmDelete"), MessageBoxButtons.YesNo,
				answer => answer == DialogResult.Yes ? new ResultBeforeReturning(Loc.T("modactions.deletingStatus", selectedMod.Name)) : null)
			!= DialogResult.Yes)
			return;

		using IDisposable busy = BeginModOperation();
		try
		{
			// Deleting keeps a backup first, and zipping a large mod is not instant. Say what is happening and
			// report progress the way downloads and installs do, so a long pause is never mistaken for a hang.
			// Already said by the question, so only shown here.
			SetStatus(Loc.T("modactions.deletingStatus", selectedMod.Name), speak: false);
			if (!await BackupModWithProgressAsync(selectedMod.FolderPath, selectedMod.Name + "_Delete", selectedMod.Name))
			{
				// The whole point of backing up first is that the mod can be got back. If that did not work,
				// deleting it destroys something with no copy anywhere — so it stops here and says so, rather
				// than going ahead and reporting that a backup was kept.
				ResetStatus();
				_soundEngine.Play("error");
				SpeakBox(Loc.T("modactions.deleteNoBackupBox", selectedMod.Name), Loc.T("modactions.deleteNoBackupTitle"),
					MessageBoxButtons.OK);
				return;
			}

			// ForceDelete (via ModFileSystem) clears read-only attributes first; a plain Directory.Delete throws
			// "Access to the path '…' is denied" on mods that ship a read-only file such as SkyPatcher's DLL.
			// A mod that installed itself with a setup program usually leaves an uninstaller, and that knows far
			// more about what it put where than the manager ever can. Run it in preference to guessing.
			await RunModUninstallerIfAnyAsync(selectedMod);

			// A Witcher 3 mod's dlc and menu-config parts live in the game folder, not in the mod folder, and the
			// record of them is inside the folder about to be deleted — so they go first. The core's, shared with the
			// Linux head (see Witcher3ModFiles): this head had its own copy, which deleted the folder whatever was left.
			string witcherGame = GameProfiles.Find(_settings.ActiveGame)?.IsWitcher3 == true
				?Witcher3ModFiles.GameFolderFor(Path.GetDirectoryName(selectedMod.FolderPath.TrimEnd(Path.DirectorySeparatorChar)) ?? "",
					_settings.CurrentGamePath)
				: "";
			IReadOnlyList<string> leftInGame = Witcher3ModFiles.RemoveExtras(
				selectedMod.FolderPath, _settings.ActiveGame, witcherGame, LogError);
			if (leftInGame.Count > 0)
			{
				// ⚠️ The folder stays. Its record is then the only list of files still loading in the game, and deleting it
				// leaves them there with nothing that knows they are the mod's.
				ResetStatus();
				_soundEngine.Play("error");
				SpeakBox(Loc.T("modactions.deleteLeftInGame", selectedMod.Name, string.Join(", ", leftInGame.Take(10))),
					Loc.T("common.error"), MessageBoxButtons.OK);
				return;
			}

			await Task.Run(() => ModFileSystem.DeleteModFolder(selectedMod.FolderPath));
			// The Witcher 3 lists its mods in a file of its own, and an entry outliving the folder keeps a deleted
			// mod in the game's own menu.
			Witcher3ModFiles.Forget(selectedMod.FolderPath, _settings.ActiveGame, witcherGame, LogError);
			// RefreshModList below re-scans without the deleted mod and reconciles deployment and
			// plugins.txt, pruning its files/plugins and restoring any provider it had overridden.
			_soundEngine.Play("disable");
			await SayAndRefreshModList(Loc.T("modactions.deletedStatus", selectedMod.Name));
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
	private async Task RunModUninstallerIfAnyAsync(GameMod mod)
	{
		if (GameProfiles.Find(_settings.ActiveGame)?.IsWitcher3 != true) return;

		var uninstaller = ModFileSystem.FindUninstallerInsideGame(
			_settings.CurrentGamePath, Path.GetFileName(mod.FolderPath.TrimEnd(Path.DirectorySeparatorChar)));
		if (uninstaller == null) return;

		if (SpeakBox(Loc.T("modactions.runUninstallerBox", uninstaller.DisplayName),
				Loc.T("modactions.runUninstallerTitle"),
				MessageBoxButtons.YesNo) != DialogResult.Yes)
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

			// The wait above gives up after a minute, so being here does not mean it is gone.
			Speak(Loc.T(ModFileSystem.UninstallerStillRegistered(uninstaller) ? "modactions.uninstallerNotFinished" : "modactions.uninstallerFinished", uninstaller.DisplayName));
		}
		catch (Exception ex)
		{
			LogFailure("Delete", "Could not run the mod's uninstaller", ex);
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

		GameMod? mod = SelectedNexusMod();
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
	/// <param name="list">Which of the Backups tab's two lists it was pressed in.</param>
	private void DeleteSelectedBackup(ListBox list)
	{
		if (list.SelectedItem is not BackupItem backupItem) return;

		// Said before the question closes, and the list refilled once that has been heard. See ActBeforeReturning. It said
		// nothing at all before: the row just went.
		string? failure = null;
		SpeakBox(Loc.T("modactions.deleteBackupConfirm", backupItem.ToString()), Loc.T("common.confirmDelete"),
			MessageBoxButtons.YesNo, answer =>
			{
				if (answer != DialogResult.Yes) return null;
				try { File.Delete(backupItem.FullPath); }
				catch (Exception ex)
				{
					// A box, so after the question has gone rather than over it.
					failure = Loc.T("modactions.deleteBackupFailedBox", FriendlyError(ex));
					return null;
				}
				_soundEngine.Play("disable");
				return new ResultBeforeReturning(Loc.T("modactions.backupDeleted"), () =>
				{
					// Behind the question the list has no focus, and a refill without it goes to the newest. Put back near
					// where the deleted row was instead, on the one that moved up into its place.
					int index = list.SelectedIndex;
					RefreshBackupsList();
					if (list.Items.Count == 0) return;
					list.SelectedIndex = Math.Min(Math.Max(index, 0), list.Items.Count - 1);
					AlignListCaretToSelection(list);
				});
			});
		if (failure != null) SpeakBox(failure);
	}
}
