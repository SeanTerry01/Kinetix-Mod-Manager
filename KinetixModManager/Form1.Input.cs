
namespace KinetixModManager;

/// <summary>Keyboard input, focus cycling, and list key handling for Form1.</summary>
public partial class Form1
{
	/// <summary>
	/// The list a letter or digit was just typed into, and the row it was on then. Set as the character arrives, which is
	/// before the list acts on it, and cleared by the next key. See <see cref="TypedOntoTheSameRow"/>.
	/// </summary>
	private (ListBox List, int Index)? _typedIntoList;

	/// <summary>
	/// Watches keys on their way to any control, for <see cref="_typedIntoList"/>. A filter rather than the window's key
	/// preview, which is switched off while a prompt or in-window view is up (see RunOverlay), and Shortcut Customization,
	/// where this was found, is one. It only looks, and lets every message through.
	/// </summary>
	private sealed class TypedIntoListWatcher(Form1 form) : IMessageFilter
	{
		private const int KeyDown = 0x0100, Character = 0x0102;

		public bool PreFilterMessage(ref Message m)
		{
			if (m.Msg == KeyDown)
			{
				form._typedIntoList = null;
				form.NoteMoveInAOneItemList(m);
			}
			else if (m.Msg == Character && char.IsLetterOrDigit((char)(long)m.WParam) && Control.FromHandle(m.HWnd) is ListBox list)
				form._typedIntoList = (list, list.SelectedIndex);
			return false;
		}
	}

	/// <summary>
	/// An arrow, Home, End, Page Up or Page Down in a list or drop-down holding one item, already selected: nothing moves, so
	/// nothing was said, and the item could only be heard again by tabbing away and back (Sean, 2026-11-12). It is said
	/// again, with "1 of 1". Plain keys only: Ctrl with an arrow moves a mod in Mod Priority, and that says its own result.
	/// Said once the key has been dealt with, in case the control did make a change of its own.
	/// </summary>
	private void NoteMoveInAOneItemList(Message m)
	{
		var key = (Keys)(int)(long)m.WParam;
		if (key is not (Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown)) return;
		if (Control.ModifierKeys != Keys.None) return;

		// From the child handle: a drop-down that can be typed in takes its keys in an edit box inside it.
		switch (Control.FromChildHandle(m.HWnd))
		{
			case ListBox { Items.Count: 1, SelectedIndex: 0 } list:
				BeginInvoke(new Action(() =>
				{
					if (list.Focused && list.Items.Count == 1 && list.SelectedIndex == 0)
						Speak(RowThenPosition(list.SelectedItem, Loc.T("common.position", 1, 1)));
				}));
				break;
			case ComboBox { Items.Count: 1, SelectedIndex: 0, DroppedDown: false } combo:
				BeginInvoke(new Action(() =>
				{
					if (combo.ContainsFocus && combo.Items.Count == 1 && combo.SelectedIndex == 0)
						Speak(RowThenPosition(combo.GetItemText(combo.SelectedItem), Loc.T("common.position", 1, 1)));
				}));
				break;
		}
	}

	/// <summary>
	/// Whether this selection change is a typed letter or digit landing on the row it was already on: one only that row
	/// starts with. The list says its selection changed, the reader has nothing new to read and says nothing, and all that
	/// was heard was the position, with no name (Sean, in Shortcut Customization, 2026-11-11). Such a change names its row,
	/// as a move to any other row does. Read before the handler's first await, and used up by it.
	/// </summary>
	private bool TypedOntoTheSameRow(ListBox list)
	{
		bool same = _typedIntoList is { } typed && ReferenceEquals(typed.List, list) && typed.Index == list.SelectedIndex;
		_typedIntoList = null;
		return same;
	}

	private void Form1_KeyDown(object? sender, KeyEventArgs e)
	{
		// Recorded before anything else can return, so the held key is known even for presses this handler
		// otherwise ignores — an overlay opening mid-press would otherwise leave a stale key held here.
		bool isAutoRepeat = !_keyRepeat.IsFirstPress((int)e.KeyData);
		_lastKeyWasAutoRepeat = isAutoRepeat;

		// While a prompt or an in-window view is covering the window, the window's own shortcuts must stay out
		// of the way — F5 must not launch the game from behind a confirmation, and Escape belongs to whatever
		// is on top, which handles it itself. The overlay disables the controls beneath it, but key preview
		// reaches the form whatever has focus, so it has to be turned away here.
		if (OverlayIsOpen) return;

		// The user is driving. Startup finishes by putting focus on the first tab and saying so, which is right
		// when they have been waiting for it and wrong the moment they have started moving around themselves —
		// being pulled back to Installed Mods mid-keystroke is worse than never being told where you started.
		// Set here rather than on any key at all, so typing into a first-run wizard does not count as wandering
		// the main window.
		// ⚠️ Not a modifier on its own. Ctrl is how a screen-reader user stops speech, and pressed during the welcome it counted
		// as moving: the landing was called off and the session opened without saying where you were (2026-11-11).
		if (e.KeyCode is not (Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin))
		{
			_userMovedSinceLoadBegan = true;
			// Said now, ahead of whatever this key says: the user's own keys don't wait behind the game switch sound.
			ReleaseHeldSpeech();
		}

		// Every command below is a one-shot, and none of them wants to run again because a key was held a
		// moment too long: holding Refresh Everything started a refresh per repeat, and holding F1 would have
		// opened the manual as many times. Returning without marking the key handled leaves it to whatever has
		// focus, so a held Backspace in a text box still repeats the way typing is supposed to.
		if (isAutoRepeat) return;

		if (_settings.ActiveGame == GameProfiles.NoGame)
		{
			// On the game-selection screen most shortcuts act on a mod list that isn't loaded yet, but the
			// app-wide ones still make sense and are needed here — Settings in particular, since that is where
			// a game folder is set when auto-detection missed the install. Focus goes back to the game list
			// afterwards so the user lands where they left off.
			if (e.KeyCode == Keys.Escape)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				Application.Exit();
				return;
			}
			if (IsShortcut(e, "Manual") || IsShortcut(e, "ChangeLog") || IsShortcut(e, "Settings"))
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				if (IsShortcut(e, "Manual")) ShowManual();
				else if (IsShortcut(e, "ChangeLog")) ShowChangeLog();
				else ShowSettings();
				_lstGames?.Focus();
			}
			return;
		}
		if (IsShortcut(e, "Manual"))
		{
			e.SuppressKeyPress = true;
			ShowManual();
		}
		if (IsShortcut(e, "ChangeLog"))
		{
			e.SuppressKeyPress = true;
			ShowChangeLog();
		}
		if (IsShortcut(e, "ModDocs"))
		{
			e.SuppressKeyPress = true;
			ShowModDocs();
		}
		if (IsShortcut(e, "ContextHelp"))
		{
			e.SuppressKeyPress = true;
			ShowContextHelp();
		}
		if (IsShortcut(e, "ControlsHelp"))
		{
			e.SuppressKeyPress = true;
			ShowAccessibilityControls();
		}
		if (IsShortcut(e, "SearchHistory"))
		{
			e.SuppressKeyPress = true;
			ShowSearchHistoryDialog();
		}
		if (IsShortcut(e, "LaunchGame"))
		{
			e.SuppressKeyPress = true;
			LaunchGame();
		}
		if (IsShortcut(e, "OpenLogFile"))
		{
			e.SuppressKeyPress = true;
			OpenGameLog();
		}
		if (IsShortcut(e, "Settings"))
		{
			e.SuppressKeyPress = true;
			ShowSettings();
		}
		if (IsShortcut(e, "Login"))
		{
			e.SuppressKeyPress = true;
			if (mainTabs.SelectedTab == tabInstalled && listInstalled.Focused && listInstalled.SelectedItem is GameMod)
			{
				Fire(LinkModUpdateSource(), "LinkModUpdateSource");
			}
			else if (listLog.Focused)
			{
				Fire(UploadSmapiLog(), "UploadSmapiLog");
			}
			else
			{
				PromptForApiKey();
			}
		}
		if (IsShortcut(e, "InstallZip"))
		{
			e.SuppressKeyPress = true;
			ManualInstall();
		}
		if (IsShortcut(e, "OpenModPage"))
		{
			e.SuppressKeyPress = true;
			OpenModPage();
		}
		if (IsShortcut(e, "OpenConfig"))
		{
			e.SuppressKeyPress = true;
			OpenSelectedModConfig();
		}
		if (IsShortcut(e, "OpenManifest"))
		{
			e.SuppressKeyPress = true;
			OpenSelectedModManifest();
		}
		if (IsShortcut(e, "OpenConfigFile"))
		{
			e.SuppressKeyPress = true;
			OpenSelectedModConfigFile();
		}
		if (IsShortcut(e, "OpenDownloads"))
		{
			e.SuppressKeyPress = true;
			OpenFolder(downloadsPath);
		}
		if (IsShortcut(e, "OpenBackups"))
		{
			e.SuppressKeyPress = true;
			OpenFolder(backupsPath);
		}
		if (IsShortcut(e, "ManualID"))
		{
			e.SuppressKeyPress = true;
			Fire(LinkModUpdateSource(), "LinkModUpdateSource");
		}
		if (IsShortcut(e, "ChangeCategory"))
		{
			e.SuppressKeyPress = true;
			SetManualCategory();
		}
		if (IsShortcut(e, "BatchCategory"))
		{
			e.SuppressKeyPress = true;
			BatchManageCategory();
		}
		if (IsShortcut(e, "ShowDependencies"))
		{
			e.SuppressKeyPress = true;
			ShowDependencies();
		}
		if (IsShortcut(e, "MarkVersionInstalled"))
		{
			e.SuppressKeyPress = true;
			MarkSelectedUpdateAsInstalled();
		}
		if (IsShortcut(e, "QuickFix"))
		{
			e.SuppressKeyPress = true;
			QuickFixDependencies();
		}
		if (IsShortcut(e, "Endorse"))
		{
			e.SuppressKeyPress = true;
			EndorseSelectedMod();
		}
		if (IsShortcut(e, "ViewChangelog"))
		{
			e.SuppressKeyPress = true;
			ViewModChangelog();
		}
		if (IsShortcut(e, "ViewDescription"))
		{
			e.SuppressKeyPress = true;
			ViewModDescription();
		}
		if (IsShortcut(e, "CheckBrokenMods"))
		{
			e.SuppressKeyPress = true;
			Fire(ShowBrokenModsReport(), "ShowBrokenModsReport");
		}
		if (IsShortcut(e, "HealthCheck"))
		{
			e.SuppressKeyPress = true;
			Fire(RunHealthCheck(), "RunHealthCheck");
		}
		if (IsShortcut(e, "PluginSlots"))
		{
			e.SuppressKeyPress = true;
			AnnouncePluginSlotUsage();
		}
		if (IsShortcut(e, "SaveManager"))
		{
			e.SuppressKeyPress = true;
			Fire(ShowSaveManager(), "ShowSaveManager");
		}
		if (IsShortcut(e, "DownloadsHistory"))
		{
			e.SuppressKeyPress = true;
			ShowDownloadsHistory();
		}
		if (IsShortcut(e, "InstallPendingDownload"))
		{
			e.SuppressKeyPress = true;
			ShowPendingDownloads();
		}
		if (IsShortcut(e, "TrackedMods"))
		{
			e.SuppressKeyPress = true;
			Fire(ShowTrackedMods(), "ShowTrackedMods");
		}
		if (IsShortcut(e, "EditNote"))
		{
			e.SuppressKeyPress = true;
			SetModNote();
		}
		if (IsShortcut(e, "ExportCollection"))
		{
			e.SuppressKeyPress = true;
			Fire(ExportCollection(), "ExportCollection");
		}
		if (IsShortcut(e, "InstallCollection"))
		{
			e.SuppressKeyPress = true;
			Fire(InstallCollectionAsync(), "InstallCollectionAsync");
		}
		if (IsShortcut(e, "ApiCredits"))
		{
			e.SuppressKeyPress = true;
			ShowApiCredits();
		}
		if (IsShortcut(e, "DiagnoseAi"))
		{
			e.SuppressKeyPress = true;
			DiagnoseWithAi();
		}
		if (IsShortcut(e, "FileConflicts"))
		{
			e.SuppressKeyPress = true;
			ShowFileConflictsReport();
		}
		if (IsShortcut(e, "CheckRequirements"))
		{
			e.SuppressKeyPress = true;
			Fire(ShowRequirementsReport(), "ShowRequirementsReport");
		}
		if (IsShortcut(e, "Search"))
		{
			e.SuppressKeyPress = true;
			if (CurrentTab() == AppTab.Installed)
			{
				txtSearchInstalled.Focus();
			}
			else if (CurrentTab() == AppTab.SmapiLog)
			{
				txtSearchLog.Focus();
			}
			else if (CurrentTab() == AppTab.Discovery)
			{
				txtSearch.Focus();
			}
			else if (CurrentTab() == AppTab.Wiki)
			{
				txtWikiSearch.Focus();
			}
			else if (CurrentTab() == AppTab.GameLog)
			{
				txtSearchGameLog.Focus();
			}
		}
		if (IsShortcut(e, "UpdateAll"))
		{
			e.SuppressKeyPress = true;
			Fire(UpdateAllMods(), "UpdateAllMods");
		}
		if (IsShortcut(e, "SaveProfile"))
		{
			e.SuppressKeyPress = true;
			CreateProfileFromCurrent();
		}
		if (IsShortcut(e, "ReadDescription"))
		{
			e.SuppressKeyPress = true;
			ReadSelectedDescription();
		}
		if (IsShortcut(e, "DeleteOldBackups"))
		{
			e.SuppressKeyPress = true;
			PruneAllBackups();
		}
		if (IsShortcut(e, "RefreshAll"))
		{
			e.SuppressKeyPress = true;
			RequestManualRefresh(everything: true);
		}
		if (IsShortcut(e, "RefreshInstalled"))
		{
			e.SuppressKeyPress = true;
			RequestManualRefresh(everything: false);
		}
		if (IsShortcut(e, "CycleFocus"))
		{
			e.SuppressKeyPress = true;
			HandleCycleFocus();
		}
		if (IsShortcut(e, "AutoSort"))
		{
			e.SuppressKeyPress = true;
			Fire(AutoSortPluginsAsync(), "AutoSortPluginsAsync");
		}
		if (IsShortcut(e, "MoveByPlaces"))
		{
			e.SuppressKeyPress = true;
			MoveByPlaces();
		}
		if (IsShortcut(e, "OpenErrorLog"))
		{
			e.SuppressKeyPress = true;
			OpenErrorLog();
		}
		if (IsShortcut(e, "CancelDownloads"))
		{
			e.SuppressKeyPress = true;
			CancelRunningDownloads();
		}

		// Reading the suggested list is not curating, so it is never gated. Unmapped by default; this is here so
		// a key assigned in the shortcut manager works. See Form1.SuggestedMods.
		if (IsShortcut(e, "SuggestedMods"))
		{
			e.Handled = true;
			e.SuppressKeyPress = true;
			ShowSuggestedMods();
		}
		// The way in, and the only one of the three that works while curation is off — turning it on is precisely
		// what it is for. See Form1.Curator.
		if (IsShortcut(e, "CurationMode"))
		{
			e.Handled = true;
			e.SuppressKeyPress = true;
			ToggleCurationMode();
		}
		// The other two do nothing until curation is on. Checked here rather than inside each command so a key
		// that is switched off stays unhandled, and whatever else wants it can have it.
		if (_settings.CuratorMode && IsShortcut(e, "MarkSuggestion"))
		{
			e.Handled = true;
			e.SuppressKeyPress = true;
			ToggleModSuggestion();
		}
		if (_settings.CuratorMode && IsShortcut(e, "SuggestedList"))
		{
			e.Handled = true;
			e.SuppressKeyPress = true;
			ShowSuggestedModsReview();
		}
	}

	/// <summary>
	/// Cycles keyboard focus between the main tab headers and the primary control of the active tab.
	/// In the Wiki tab: cycles list → WebView → tab headers. In other tabs: toggles tab headers ↔ list.
	/// </summary>
	private void HandleCycleFocus()
	{
		// Use Control.Focused on the actual candidate controls rather than Form.ActiveControl:
		// ActiveControl only reports the immediate active *container* (the TabControl / TabPage),
		// not the deeply nested ListBox or WebView that truly holds focus, so identity checks
		// against it never matched and the cycle skipped the WebView step.
		bool onHeaders = mainTabs.Focused;

		// If focus is somewhere inside the TabControl but not on the headers
		if (!onHeaders)
		{
			// If we are in the Wiki tab, cycle between Results -> Web View -> Tabs
			if (CurrentTab() == AppTab.Wiki)
			{
				// If we're in the list, move to web view
				if (listWikiResults.Focused)
				{
					webViewWiki.Focus();
					Speak(Loc.T("modlist.wikiContent"));
				}
				else
				{
					// Otherwise (likely in WebView or search boxes), move to headers. The reader announces the tab
					// as focus arrives; see the last branch.
					mainTabs.Focus();
				}
			}
			else if (CurrentTab() == AppTab.Walkthroughs)
			{
				// If we're in the list, move to web view
				if (listWalkthroughs.Focused)
				{
					webViewWalkthrough.Focus();
					Speak(Loc.T("modlist.walkthroughContent"));
				}
				else
				{
					mainTabs.Focus();
				}
			}
			else
			{
				// In other tabs, just jump back to the Tab headers.
				//
				// ⚠️ Nothing said here. Focus really moves, so the reader announces the tab itself, and saying it as
				// well was heard as "Installed Mods Tab, Installed Mods tab selected" on every F6 back to the strip.
				// Measured with NVDA 2026-11-10. The tab is said by the app only where focus does NOT move, such as
				// leaving a menu (AnnounceFocusRestored) or startup's landing.
				mainTabs.Focus();
			}
		}
		else
		{
			// Focus is on the Tab headers, jump into the primary control of the current tab.
			// The list's Enter handler (List_Enter) announces the name/position, so focusing is
			// enough here — and it works the same whether the user arrived via F6, Tab, or click.
			switch (CurrentTab())
			{
				case AppTab.Installed:    listInstalled.Focus();   break;
				case AppTab.Updates:      listUpdates.Focus();     break;
				case AppTab.Backups:      listDeletedBackups.Focus(); break;
				case AppTab.Discovery:    listDiscovery.Focus();   break;
				// The results are hidden until a search has run, so focusing them did nothing, in silence.
				case AppTab.Wiki:         if (listWikiResults.CanFocus) listWikiResults.Focus(); else txtWikiSearch.Focus(); break;
				case AppTab.Walkthroughs: listWalkthroughs.Focus(); break;
				case AppTab.Profiles:     listProfiles.Focus();    break;
				case AppTab.ModPriority:  listModPriority.Focus(); break;
				case AppTab.PluginOrder:  listPluginOrder.Focus(); break;
				case AppTab.Creations:    listCreations.Focus();   break;
				case AppTab.GameLog:      listGameLog.Focus();     break;
				case AppTab.MinecraftPacks: listMinecraftPacks.Focus(); break;
				case AppTab.SmapiLog:     listLog.Focus();         break;
			}
		}
	}

	/// <summary>Speaks the full <see cref="object.ToString"/> description of the currently focused list item.</summary>
	private void ReadSelectedDescription()
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
		if (listBox.SelectedItem is GameMod selectedMod)
		{
			if (!string.IsNullOrEmpty(selectedMod.Description))
			{
				// Chunked so a long description is read in full — a single long spoken string can be clipped.
				SpeakLong(selectedMod.Description);
			}
			else
			{
				Speak(Loc.T("modlist.noDescription"));
			}
		}
	}

	/// <summary>
	/// The installed mod a command means on the tab in front, or null where there is none.
	///
	/// These commands are global shortcuts, and the Installed list keeps its selection when you leave it. So on the Backups
	/// tab, a key could open or edit whichever mod you were last on, one you could neither see nor hear.
	/// </summary>
	private GameMod? ModOnThisTab() => CurrentTab() switch
	{
		AppTab.Installed => listInstalled.SelectedItem as GameMod,
		AppTab.Updates => listUpdates.SelectedItem as GameMod,
		_ => null
	};

	/// <summary>The key for "tell me about this": the Applications key, or Shift+F10 on a keyboard without one.</summary>
	private static bool IsContextKey(KeyEventArgs e) => e.KeyCode == Keys.Apps || (e.KeyCode == Keys.F10 && e.Shift);

	/// <summary>Whether the key being handled is a repeat from being held, which the window hears about first.</summary>
	private bool _lastKeyWasAutoRepeat;

	private void List_KeyDown(object? sender, KeyEventArgs e)
	{
		if (!(sender is ListBox list))
		{
			return;
		}
		// The window turns a held key away from its own shortcuts and hands it on for typing, which left it reaching these.
		// Holding Space flipped a mod on and off with a rescan for every repeat, ending wherever it happened to stop.
		if (_lastKeyWasAutoRepeat && e.KeyCode is Keys.Space or Keys.Return or Keys.Delete or Keys.Apps)
		{
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
		if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z && !e.Control && !e.Alt)
		{
			char c = char.ToLower((char)e.KeyCode);
			_searchTimer.Stop();
			_searchBuffer += c;
			_searchTimer.Start();
			int searchStart = list.SelectedIndex;
			if (_searchBuffer.Length == 1)
			{
				searchStart++;
			}
			// A mod by its name and anything else by what the row says. Only mods used to be looked at, and this still claimed
			// the key, so on Backups, Profiles and the logs letters did nothing at all, the list box's own search included.
			static string RowText(object? row) => row is GameMod mod
				? (mod.IsGroup ? mod.GroupName : mod.Name) ?? ""
				: row?.ToString() ?? "";

			for (int i = 0; i < list.Items.Count; i++)
			{
				int rowIndex = (searchStart + i) % list.Items.Count;
				if (RowText(list.Items[rowIndex]).StartsWith(_searchBuffer, StringComparison.OrdinalIgnoreCase))
				{
					list.SelectedIndex = rowIndex;
					e.Handled = true;
					e.SuppressKeyPress = true;
					return;
				}
			}
			bool found = false;
			if (_searchBuffer.Length > 1)
			{
				string lastLetter = c.ToString();
				for (int j = 0; j < list.Items.Count; j++)
				{
					int nextRowIndex = (list.SelectedIndex + 1 + j) % list.Items.Count;
					if (RowText(list.Items[nextRowIndex]).StartsWith(lastLetter, StringComparison.OrdinalIgnoreCase))
					{
						list.SelectedIndex = nextRowIndex;
						found = true;
						break;
					}
				}
				_searchBuffer = lastLetter;
			}
			if (!found) Speak(Loc.T("common.noRowStartsWith", _searchBuffer));
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
		if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
		{
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
		if (list.Name == "listInstalled" && list.SelectedItem is GameMod selectedMod)
		{
			// Enter on a group opens or closes it, whichever it is not. It used to do nothing, in silence.
			bool enterOnGroup = selectedMod.IsGroup && e.KeyCode == Keys.Return;
			bool wantsOpen = e.KeyCode == Keys.Right || e.KeyValue == 187 || e.KeyCode == Keys.Add || (enterOnGroup && !selectedMod.IsExpanded);
			bool wantsClose = e.KeyCode == Keys.Left || e.KeyValue == 189 || e.KeyCode == Keys.Subtract || (enterOnGroup && selectedMod.IsExpanded);
			if (selectedMod.IsGroup)
			{
				string groupName = selectedMod.GroupName;
				// The screen reader re-reads the group line on selection (it states "Expanded"/"Collapsed") and
				// List_SelectedIndexChanged adds the position, which is all that should be heard here.
				if (wantsOpen)
				{
					if (!_expandedGroups.Contains(groupName))
					{
						_expandedGroups.Add(groupName);
						RebuildInstalledListBox();
					}
				}
				else if (wantsClose && _expandedGroups.Contains(groupName))
				{
					_expandedGroups.Remove(groupName);
					RebuildInstalledListBox();
				}
			}
			else if (selectedMod.IsSubMod && wantsClose)
			{
				// The same key the list grouped by, worked out the same way — a sub-mod has to name the group it
				// is under, and a disabled BepInEx mod does not live under the mods folder to be measured from.
				string groupKey = ModEnableState.InstalledGroupFolder(_settings.CurrentModsPath, selectedMod.FolderPath);
				if (_expandedGroups.Contains(groupKey))
				{
					_expandedGroups.Remove(groupKey);
					// Select the parent group directly in the rebuild. Passing its id avoids the previous
					// null-then-reselect, which briefly selected index 0 and made the screen reader announce
					// the first mod before landing on the collapsed group. The screen reader reads the group
					// line (stating "Collapsed") on selection, so suppress the rebuild's own speech.
					RebuildInstalledListBox("GROUP:" + groupKey);
				}
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			if (e.KeyCode == Keys.Space)
			{
				ToggleModStatus();
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			if (e.KeyCode == Keys.Delete)
			{
				DeleteSelectedMod();
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			// Enter on a mod says what it is, as the context key does. Not every keyboard has that key, so Shift+F10 does the
			// same, as it does throughout Windows.
			if (!selectedMod.IsGroup && (e.KeyCode == Keys.Return || IsContextKey(e)))
			{
				SpeakBox(Loc.T("modlist.detailsBox", selectedMod.Name, selectedMod.Author, selectedMod.Description), Loc.T("modlist.detailsTitle"));
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			else if (selectedMod.IsGroup && IsContextKey(e))
			{
				Speak(Loc.T("common.modGroupFirst"));
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			if (e.KeyCode == Keys.L && e.Control)
			{
				Fire(LinkModUpdateSource(), "LinkModUpdateSource");
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
		}
		if (list.Name == "listUpdates" && list.SelectedItem is GameMod modWithUpdate && e.KeyCode == Keys.Delete)
		{
			// The result is said before the question closes, and the row goes once it has been heard. See ActBeforeReturning.
			// Every mod from the same download is ignored with it, and the question names them: one download, one decision.
			List<GameMod> fromDownload = UpdateRowsFromTheSameDownload(modWithUpdate);
			string question = fromDownload.Count > 1
				? Loc.T("modlist.ignoreConfirmWithOthers", modWithUpdate.LatestVersion ?? "", modWithUpdate.Name, fromDownload.Count - 1, OthersFromTheSameDownload(fromDownload))
				: Loc.T("modlist.ignoreConfirm", modWithUpdate.LatestVersion ?? "", modWithUpdate.Name);
			SpeakBox(question, Loc.T("modlist.ignoreTitle"),
				MessageBoxButtons.YesNo, answer =>
				{
					if (answer != DialogResult.Yes) return null;
					foreach (GameMod row in fromDownload)
						_settings.IgnoredVersions[row.UniqueId] = row.LatestVersion ?? "";
					_settings.Save();
					// The last row going leaves nothing to land on, and the list says so itself once you are back on it. Saying
					// "List is empty." here as well had it said twice (heard on Ctrl+Shift+Y, 2026-11-12).
					return new ResultBeforeReturning(
						fromDownload.Count > 1 ? Loc.T("modlist.updateIgnoredWithOthers", fromDownload.Count - 1) : Loc.T("modlist.updateIgnored"),
						() => RemoveSettledUpdateRow(modWithUpdate));
				});
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
		if (list.Name is "listBackups" or "listDeletedBackups" && list.SelectedItem is BackupItem backupItem)
		{
			if (e.KeyCode == Keys.Delete)
			{
				DeleteSelectedBackup(list);
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
			if (e.KeyCode == Keys.Return)
			{
				// "Restoring…" is said before the question closes, so focus coming back doesn't cut it, nor it the reader's
				// reading of the row. See ActBeforeReturning.
				string name = BackupRestore.ModNameOf(backupItem.FullPath);
				if (!RefusedWhileModsAreChanging() && SpeakBox(Loc.T("modlist.restoreConfirm", name), Loc.T("modlist.restoreTitle"), MessageBoxButtons.YesNo,
						answer => answer == DialogResult.Yes ? new ResultBeforeReturning(Loc.T("backups.restoring", name)) : null)
					== DialogResult.Yes)
				{
					Fire(RestoreBackupAsync(backupItem, startSaid: true), "RestoreBackup");
				}
				e.Handled = true;
			}
		}
		if (list.Name == "listProfiles" && list.SelectedItem is ModProfile modProfile)
		{
			if (e.KeyCode == Keys.Return)
			{
				ApplyProfile(modProfile);
				e.Handled = true;
			}
			if (e.KeyCode == Keys.Delete)
			{
				// Said before the question closes, and the list refilled once that has been heard. See ActBeforeReturning.
				SpeakBox(Loc.T("modlist.deleteProfileConfirm", modProfile.Name), Loc.T("common.confirmDelete"),
					MessageBoxButtons.YesNo, answer =>
					{
						if (answer != DialogResult.Yes) return null;
						// From where it was read: this game's folder, or the old shared one.
						string path = modProfile.SourcePath.Length > 0 ? modProfile.SourcePath : ProfileStore.PathFor(profilesPath, modProfile.Name);
						if (File.Exists(path))
						{
							File.Delete(path);
						}
						return new ResultBeforeReturning(Loc.T("modlist.profileDeleted"), RefreshProfilesList);
					});
				e.Handled = true;
				e.SuppressKeyPress = true;
			}
		}
		if (list.Name == "listLog" && e.KeyCode == Keys.Return && list.SelectedItem is LogEntry logEntry)
		{
			// A SMAPI line can include one or more links (e.g. a "no longer compatible" line lists the
			// Nexus, GitHub, and smapi.io pages). Enter on a single-link line opens it directly (Nexus
			// pages on the Files tab, like the updates list); a multi-link line shows a picker so the
			// user chooses which to open. Lines without a link fall through to the view/detail behavior.
			List<string> logUrls = LogAnalyzer.ExtractUrls(logEntry.Text);
			if (logUrls.Count == 1)
			{
				OpenLogLink(logUrls[0]);
				e.Handled = true;
				return;
			}
			if (logUrls.Count > 1)
			{
				ShowLogLinkPicker(logUrls);
				e.Handled = true;
				return;
			}

			if (list.Items.Count < _fullLogEntries.Count)
			{
				txtSearchLog.Text = "";
				list.BeginUpdate();
				list.Items.Clear();
				foreach (LogEntry fullLogEntry in _fullLogEntries)
				{
					list.Items.Add(fullLogEntry);
				}
				list.SelectedItem = logEntry;
				list.EndUpdate();
				Speak(Loc.T("modlist.returnedFiltered"));
				e.Handled = true;
				return;
			}
			string logLine = logEntry.Text;
			string suggestedFix = LogAnalyzer.GetSuggestedFix(logLine);
			string detailText = logLine;
			if (!string.IsNullOrEmpty(suggestedFix))
			{
				detailText = detailText + Loc.T("modlist.suggestedFixSuffix", suggestedFix);
			}
			SpeakBox(detailText, Loc.T("modlist.logDetailTitle"));
			e.Handled = true;
		}
		// Re-read SMAPI-latest.txt on demand. SMAPI holds the log open for the whole game session, so this
		// pulls in lines written since the tab was last loaded (e.g. after triggering an error in-game)
		// without closing Stardew. The shared-read in RefreshSmapiLog means it succeeds mid-session.
		if (list.Name == "listLog" && IsShortcut(e, "RefreshLog"))
		{
			RefreshSmapiLog();
			Speak(listLog.Items.Count > 0
				? Loc.T("modlist.logRefreshed", listLog.Items.Count)
				: Loc.T("modlist.logRefreshedEmpty"));
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
		// Ctrl+C copies the selected log line(s) to the clipboard so the user can paste them elsewhere
		// (a forum post, the mod's Discord) without opening and searching SMAPI-latest.txt by hand.
		if (list.Name == "listLog" && e.Control && e.KeyCode == Keys.C)
		{
			var selected = list.SelectedItems.Count > 0
				? list.SelectedItems.Cast<object>()
				: (list.SelectedItem != null ? new[] { list.SelectedItem } : System.Array.Empty<object>());
			string copied = string.Join("\r\n", selected.Select(o => o.ToString()));
			if (!string.IsNullOrEmpty(copied))
			{
				Clipboard.SetText(copied);
				int count = copied.Split('\n').Length;
				Speak(count == 1 ? Loc.T("modlist.copiedOne") : Loc.T("modlist.copiedMany", count));
			}
			else
			{
				Speak(Loc.T("modlist.noLinesSelected"));
			}
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
		// Alt+O: run the same search again, this time asking the catalogues the preferred-source mode leaves
		// out. Only offered when there is somewhere else to ask that would actually answer — see
		// AnnounceOtherSources, which is what told the user the key was there.
		if (list.Name == "listDiscovery" && e.Alt && e.KeyCode == Keys.O)
		{
			Fire(RunDiscovery(alsoTheOthers: true), "RunDiscovery");
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
		if (e.KeyCode == Keys.Return && (list.Name == "listUpdates" || list.Name == "listDiscovery"))
		{
			// On the Discovery list's inline "Load more" row, Enter loads the next page of results;
			// on any real result it opens the Nexus page as usual.
			if (list.Name == "listDiscovery" && list.SelectedItem is DiscoveryLoadMoreRow)
			{
				Fire(RunDiscovery(loadMore: true), "RunDiscovery");
			}
			// A result the manager can fetch itself gets an offer to do so, rather than handing the user to a
			// web page whose download button they then have to find. Everything else opens its page, because
			// for those that IS the only way in — and that is the answer for every site added later too:
			// worst case it degrades to the page, which every site has.
			else if (list.Name == "listDiscovery" && list.SelectedItem is GameMod result && CanFetchDirectly(result))
			{
				Fire(OfferMinecraftSearchResultAsync(result), "OfferMinecraftSearchResultAsync");
			}
			// The Fabric loader and the Minecraft version are not mods and have no page to offer, so Enter does the
			// one thing they are for. See CheckMinecraftPlatformUpdatesAsync.
			else if (list.Name == "listUpdates" && list.SelectedItem is GameMod platform &&
					 (platform.UniqueId == FabricLoaderRowId || platform.UniqueId == MinecraftVersionRowId ||
					  platform.UniqueId == ModpackRowId))
			{
				Fire(DownloadAndInstallUpdate(platform), "DownloadAndInstallUpdate");
			}
			// An update the manager can fetch itself asks which the user wants, rather than assuming they pressed
			// Enter to go and read about a mod they already have.
			else if (list.Name == "listUpdates" && list.SelectedItem is GameMod pending && CanUpdateWithoutBrowser(pending))
			{
				Fire(OfferUpdateChoiceAsync(pending), "OfferUpdateChoiceAsync");
			}
			else
			{
				OpenModPage();
			}
			e.Handled = true;
		}
	}
}
