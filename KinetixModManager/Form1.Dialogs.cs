using System.Text;

namespace KinetixModManager;

/// <summary>Sound demo, theme manager, and shortcut manager dialogs for Form1.</summary>
public partial class Form1
{
	/// <summary>Opens a modal dialog that lets the user browse and preview all available app sounds.</summary>
	private void ShowSoundDemo()
	{
		string previewTheme = _settings.CurrentTheme;
		// Shown inside the main window rather than as one of its own — see Form1.InlineView. The window used to
		// be hidden while this was open; there is nothing to hide now, and Escape is handled by the view.
		ShowInlineView(Loc.T("soundDemo.title"), (container, closeView) =>
		{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 3,
			Padding = new Padding(10)
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		ComboBox cmbTheme = new ComboBox
		{
			AccessibleName = Loc.T("soundDemo.themeName"),
			Dock = DockStyle.Fill,
			DropDownStyle = ComboBoxStyle.DropDownList,
			Font = new Font("Segoe UI", 12f)
		};
		cmbTheme.Items.AddRange(SoundThemes.Installed(_themeFolders.All).Cast<object>().ToArray());
		cmbTheme.SelectedItem = previewTheme;
		cmbTheme.SelectedIndexChanged += delegate
		{
			previewTheme = cmbTheme.SelectedItem?.ToString() ?? "Default";
		};
		tableLayoutPanel.Controls.Add(new Label
		{
			Text = Loc.T("soundDemo.previewTheme"),
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		}, 0, 0);
		tableLayoutPanel.Controls.Add(cmbTheme, 0, 1);
		ListBox lb = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = Loc.T("soundDemo.soundList")
		};
		foreach (string soundName in SoundEngine.SoundDescriptions.Keys)
		{
			lb.Items.Add(soundName);
		}
		// The list item text (the sound name) is already read by the screen reader on focus, so
		// our announcement is just the description plus list position to avoid speaking the name
		// twice. Spoken both when the selection changes by arrow key and when focus first lands on
		// the list, so tabbing to the list reads the full description, not just the name.
		Func<Task> announceSound = async () =>
		{
			if (lb.SelectedItem != null)
			{
				string key = lb.SelectedItem.ToString() ?? "";
				await Task.Delay(100);
				// Focus can have moved on during the wait, and speaking then would describe a list the user has
				// already left — the same guard the shared list handlers use.
				if (!lb.Focused) return;
				// The descriptions are sentences and end in a full stop, and the wording adds its own: it read
				// "…in Minecraft.. 1 of 7".
				Speak(Loc.T("soundDemo.announce", Loc.T("sound." + key).TrimEnd().TrimEnd('.'), lb.SelectedIndex + 1, lb.Items.Count));
			}
		};
		lb.SelectedIndexChanged += async delegate
		{
			// Gate on focus so seeding the initial selection (while focus is still on the theme
			// combo) does not speak over the dialog opening.
			if (lb.Focused) await announceSound();
		};
		// GotFocus, not Enter: Enter does not fire when focus is restored to a control it never really left —
		// coming back from a nested view, for one — and the list would then say nothing at all.
		lb.GotFocus += async delegate { await announceSound(); };
		lb.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			// Left/Right move the selection in a single-column list box exactly like Up/Down, which reads as the
			// list jumping about for no reason. Every list in the app suppresses them.
			if (pe.KeyCode == Keys.Left || pe.KeyCode == Keys.Right) { pe.Handled = pe.SuppressKeyPress = true; return; }
			if (pe.KeyCode == Keys.Return && lb.SelectedItem != null)
			{
				_soundEngine.Play(lb.SelectedItem.ToString() ?? "", previewTheme);
			}
		};
		tableLayoutPanel.Controls.Add(lb, 0, 2);
		container.Controls.Add(tableLayoutPanel);
		// Select the first sound on open so the list lands on a real item (and announces it)
		// rather than an empty selection.
		if (lb.Items.Count > 0) lb.SelectedIndex = 0;
		ApplyScreenReaderPauses(container);
		return lb;
		},
		hint: Loc.T("soundDemo.hint"));
	}

	/// <summary>
	/// Windows refused a change to a themes folder. It reached the generic "unexpected error" box, which said nothing
	/// about themes. Rare now that the user's own themes are made in their data folder (<see cref="SoundThemeFolders"/>);
	/// what is left is deleting a theme an older version made beside the program, on a copy that can't write there.
	/// </summary>
	private void SayThemeFolderRefused(Exception ex, string folder)
	{
		LogFailure("Themes", $"Could not change the themes folder {folder}", ex);
		_soundEngine.Play("error");
		SpeakBox(Loc.T("themeMgr.folderRefused", folder));
	}

	/// <summary>Opens the audio theme manager dialog for creating, renaming, and switching sound themes.</summary>
	private void ShowThemeManager()
	{
		// Declared out here because RefreshList (below) is a local function that uses them, and because the
		// "was anything saved?" flag has to outlive the view to decide what onClosed announces.
		string tempActiveTheme = _settings.CurrentTheme;
		bool saved = false;
		ListBox lb = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = Loc.T("themeMgr.installedThemes")
		};
		WireAccessibleDialogList(lb);

		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("themeMgr.title"), (container, closeView) =>
		{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(15),
			RowCount = 6
		};
		RefreshList();
		Button btnSetActive = new Button
		{
			Text = Loc.T("themeMgr.setActive"),
			Dock = DockStyle.Top,
			Height = 35
		};
		btnSetActive.Click += delegate
		{
			if (lb.SelectedItem != null)
			{
				tempActiveTheme = lb.SelectedItem.ToString() ?? "";
				Speak(Loc.T("themeMgr.activeChanged", tempActiveTheme));
			}
		};
		Button btnCreateTheme = new Button
		{
			Text = Loc.T("themeMgr.createNew"),
			Dock = DockStyle.Top,
			Height = 35
		};
		btnCreateTheme.Click += delegate
		{
			string? themeName = ShowTextPrompt(Loc.T("themeMgr.createTitle"), Loc.T("themeMgr.createPrompt"), "");
			if (themeName == null) { Speak(Loc.T("common.changesCancelled")); return; }

			themeName = themeName.Trim();
			if (themeName.Length == 0) { Speak(Loc.T("themeMgr.nameEmpty")); return; }

			// In the user's own folder, which they can always write to.
			string themeFolder = Path.Combine(_themeFolders.User, themeName);
			// Said rather than passed over in silence: a name that already exists used to leave the button looking
			// like it had done nothing, with no way to tell that from a failure. Either folder counts.
			if (_themeFolders.Exists(themeName)) { Speak(Loc.T("themeMgr.nameTaken", themeName)); return; }

			try
			{
				Directory.CreateDirectory(themeFolder);
				foreach (string key in SoundEngine.SoundDescriptions.Keys)
				{
					Directory.CreateDirectory(Path.Combine(themeFolder, key));
				}
				Directory.CreateDirectory(Path.Combine(themeFolder, "logo"));
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				SayThemeFolderRefused(ex, _themeFolders.User);
				return;
			}
			Speak(Loc.T("themeMgr.created"));
			LetSpeechFinish();
			OpenFolder(themeFolder);
			RefreshList();
		};
		Button btnAddMissingFolders = new Button
		{
			Text = Loc.T("themeMgr.addMissing"),
			Dock = DockStyle.Top,
			Height = 35
		};
		btnAddMissingFolders.Click += delegate
		{
			int foldersAdded = 0;
			// The user's own themes only. The ones beside the program come complete (SoundThemesTests holds them to it),
			// and on an installed copy could not be written to anyway.
			string[] directories = Directory.Exists(_themeFolders.User) ? Directory.GetDirectories(_themeFolders.User) : Array.Empty<string>();
			foreach (string themeFolder in directories)
			{
				foreach (string soundName in SoundEngine.SoundDescriptions.Keys)
				{
					string soundFolder = Path.Combine(themeFolder, soundName);
					if (!Directory.Exists(soundFolder))
					{
						try { Directory.CreateDirectory(soundFolder); }
						catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
						{
							SayThemeFolderRefused(ex, themeFolder);
							return;
						}
						foldersAdded++;
					}
				}
			}
			Speak(Loc.T("themeMgr.foldersAdded", foldersAdded));
		};
		Button btnDeleteTheme = new Button
		{
			Text = Loc.T("themeMgr.delete"),
			Dock = DockStyle.Top,
			Height = 35
		};
		btnDeleteTheme.Click += delegate
		{
			if (lb.SelectedItem != null)
			{
				string themeName = lb.SelectedItem.ToString() ?? "";
				// The user's own copy of a theme is theirs to delete, whatever it is called. Otherwise one the manager comes
				// with is not: it would be back on the next update, and on an installed copy the delete is refused anyway.
				string? userCopy = Directory.Exists(Path.Combine(_themeFolders.User, themeName))
					? Path.Combine(_themeFolders.User, themeName) : null;
				string? toDelete = userCopy ?? (SoundThemeFolders.ComesWithTheManager(themeName) ? null : _themeFolders.FolderOf(themeName));
				if (themeName == "Default" && userCopy == null)
				{
					SpeakBox(Loc.T("themeMgr.cannotDeleteDefault"));
				}
				else if (toDelete == null)
				{
					SpeakBox(Loc.T("themeMgr.cannotDeleteShipped", themeName));
				}
				else if (SpeakBox(Loc.T("themeMgr.confirmDelete", themeName), Loc.T("common.confirm"), MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					try { Directory.Delete(toDelete, recursive: true); }
					catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
					{
						SayThemeFolderRefused(ex, toDelete);
						return;
					}
					// A user's copy of a theme the manager also has leaves that one in place, so it stays the active choice.
					if (_themeFolders.Exists(themeName)) { RefreshList(); Speak(Loc.T("themeMgr.deleted")); return; }
					if (tempActiveTheme == themeName)
					{
						tempActiveTheme = "Default";
					}
					RefreshList();
					Speak(Loc.T("themeMgr.deleted"));
				}
			}
		};
		FlowLayoutPanel saveCancelBar = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			FlowDirection = FlowDirection.RightToLeft,
			Height = 50
		};
		Button btnSave = new Button
		{
			Text = Loc.T("common.saveAndClose"),
			Width = 120,
			Height = 35
		};
		btnSave.Click += delegate
		{
			_settings.CurrentTheme = tempActiveTheme;
			_settings.Save();
			saved = true;
			closeView();
		};
		Button btnCancel = new Button
		{
			Text = Loc.T("common.cancel"),
			Width = 100,
			Height = 35
		};
		btnCancel.Click += delegate
		{
			closeView();
		};
		saveCancelBar.Controls.AddRange(btnSave, btnCancel);
		tableLayoutPanel.Controls.Add(lb, 0, 0);
		tableLayoutPanel.Controls.Add(btnSetActive, 0, 1);
		tableLayoutPanel.Controls.Add(btnCreateTheme, 0, 2);
		tableLayoutPanel.Controls.Add(btnAddMissingFolders, 0, 3);
		tableLayoutPanel.Controls.Add(btnDeleteTheme, 0, 4);
		tableLayoutPanel.Controls.Add(saveCancelBar, 0, 5);
		container.Controls.Add(tableLayoutPanel);
		// Escape is handled by the view itself; "changes cancelled" is announced from sayOnClosing below so it is
		// said whichever way the view was left — Escape or the Cancel button.
		ApplyScreenReaderPauses(container);
		return lb;
		},
		hint: Loc.T("themeMgr.hint"),
		sayOnClosing: () => Loc.T(saved ? "themeMgr.saved" : "common.changesCancelled"));

		void RefreshList()
		{
			lb.Items.Clear();
			lb.Items.AddRange(SoundThemes.Installed(_themeFolders.All).Cast<object>().ToArray());
			lb.SelectedItem = tempActiveTheme;
		}
	}

	/// <summary>Opens the keyboard shortcut re-binding dialog.</summary>
	private void ShowShortcutManager()
	{
		// Declared out here because RefreshList (below) is a local function that uses them, and because the
		// "was anything saved?" flag has to outlive the view to decide what onClosed announces.
		Dictionary<string, int> tempShortcuts = new Dictionary<string, int>(_settings.Shortcuts);
		bool saved = false;
		ListBox lb = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = Loc.T("shortcutMgr.actionList")
		};

		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("shortcutMgr.title"), (container, closeView) =>
		{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(15),
			RowCount = 4
		};
		// Announce "X of Y" position the same way the main lists do, on focus and on arrow-key navigation.
		lb.SelectedIndexChanged += List_SelectedIndexChanged;
		lb.GotFocus += List_Enter;
		KeepLeftAndRightOffTheSelection(lb);
		RefreshList();

		// Remapping happens on the list itself rather than in a prompt of its own. The list already has focus,
		// so the next keystroke is already going there — putting it into a "waiting for the key" mode means no
		// second control to focus, nothing extra for the screen reader to announce, and the instruction is
		// heard exactly once. A window (and then a view) for this was what read the instruction twice.
		string? awaitingFor = null;

		void BeginRemap()
		{
			awaitingFor = ActionOfSelectedRow();
			if (awaitingFor == null) return;
			Speak(Loc.T("shortcutMgr.pressFor", ShortcutNames.For(awaitingFor)));
		}

		Button btnRemap = new Button
		{
			Text = Loc.T("shortcutMgr.remap"),
			Dock = DockStyle.Fill,
			Height = 35
		};
		btnRemap.Click += delegate
		{
			BeginRemap();
			// The keystroke has to land on the list, so send focus back there to catch it.
			if (awaitingFor != null) lb.Focus();
		};

		// Leaving the list stops the wait. It used to carry on, so a key pressed much later, on coming back, was taken as the
		// new shortcut.
		lb.LostFocus += delegate { awaitingFor = null; };

		lb.KeyDown += delegate (object? s, KeyEventArgs e)
		{
			if (awaitingFor == null)
			{
				// Enter starts a remap without reaching for the button — the quick route.
				if (e.KeyCode == Keys.Enter)
				{
					e.Handled = e.SuppressKeyPress = true;
					BeginRemap();
				}
				return;
			}

			// Waiting for a key. A modifier on its own is not a shortcut, so keep waiting for the real key.
			if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu) return;

			e.Handled = e.SuppressKeyPress = true;
			string action = awaitingFor;
			awaitingFor = null;

			// Escape leaves the waiting state rather than closing the whole list — the view's own Escape
			// handler stands down because this one has already marked the key handled.
			if (e.KeyCode == Keys.Escape)
			{
				Speak(Loc.T("shortcutMgr.remapCancelled", ShortcutNames.For(action)));
				return;
			}

			// Delete means no key at all, which there was otherwise no way to say.
			if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.None)
			{
				tempShortcuts[action] = Shortcut.None;
				RefreshList(action);
				Speak(Loc.T("shortcutMgr.cleared", ShortcutNames.For(action)));
				return;
			}

			switch (ShortcutRules.Check((int)e.KeyData, action, tempShortcuts, out string? takenBy))
			{
				case ShortcutCheck.AlreadyTaken:
					awaitingFor = action;
					Speak(Loc.T("shortcutMgr.taken", ShortcutNames.For(takenBy!)));
					return;
				case ShortcutCheck.WouldStealTyping:
					awaitingFor = action;
					Speak(Loc.T("shortcutMgr.needsModifier"));
					return;
			}

			tempShortcuts[action] = (int)e.KeyData;
			// Rebuild, landing back on the action just changed rather than at the top of 48 of them.
			RefreshList(action);
			// Read the key back from the same formatter the list rows use, so what is spoken is exactly what is
			// now shown against the action — and you can hear whether the combination you meant is what landed.
			Speak(Loc.T("shortcutMgr.remapped", ShortcutNames.For(action), GetShortcutStringForMap(tempShortcuts, action)));
		};
		Button btnResetDefaults = new Button
		{
			Text = Loc.T("shortcutMgr.reset"),
			Dock = DockStyle.Fill,
			Height = 35
		};
		btnResetDefaults.Click += delegate
		{
			if (SpeakBox(Loc.T("shortcutMgr.resetConfirm"), Loc.T("common.confirm"), MessageBoxButtons.YesNo) == DialogResult.Yes)
			{
				tempShortcuts.Clear();
				AppSettings appSettings = new AppSettings();
				appSettings.InitializeDefaults();
				foreach (KeyValuePair<string, int> shortcut in appSettings.Shortcuts)
				{
					tempShortcuts[shortcut.Key] = shortcut.Value;
				}
				RefreshList();
				Speak(Loc.T("shortcutMgr.resetDone"));
			}
		};
		FlowLayoutPanel saveCancelBar = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			FlowDirection = FlowDirection.RightToLeft,
			Height = 50
		};
		Button btnSave = new Button
		{
			Text = Loc.T("common.saveAndClose"),
			Width = 120,
			Height = 35
		};
		btnSave.Click += delegate
		{
			_settings.Shortcuts = tempShortcuts;
			_settings.Save();
			saved = true;
			RefreshShortcutLabels();
			closeView();
		};
		Button btnCancel = new Button
		{
			Text = Loc.T("common.cancel"),
			Width = 100,
			Height = 35
		};
		btnCancel.Click += delegate
		{
			closeView();
		};
		saveCancelBar.Controls.AddRange(btnSave, btnCancel);
		tableLayoutPanel.Controls.Add(lb, 0, 0);
		tableLayoutPanel.Controls.Add(btnRemap, 0, 1);
		tableLayoutPanel.Controls.Add(btnResetDefaults, 0, 2);
		tableLayoutPanel.Controls.Add(saveCancelBar, 0, 3);
		container.Controls.Add(tableLayoutPanel);
		// Escape is handled by the view itself; "changes cancelled" is announced from sayOnClosing below so it is
		// said whichever way the view was left — Escape or the Cancel button.
		ApplyScreenReaderPauses(container);
		return lb;
		},
		hint: Loc.T("shortcutMgr.hint"),
		sayOnClosing: () => Loc.T(saved ? "shortcutMgr.saved" : "common.changesCancelled"));

		/// <summary>
		/// Rebuilds the action list, landing back on the action named by <paramref name="selectAction"/> — or on
		/// whichever action was selected before, when none is named.
		///
		/// Rebuilding empties the list and loses the selection. With 48 actions that dropped you at the top after
		/// every single remap, so changing two shortcuts near the bottom meant arrowing all the way down twice.
		/// </summary>
		void RefreshList(string? selectAction = null)
		{
			string? keep = selectAction ?? ActionOfSelectedRow();

			lb.BeginUpdate();
			lb.Items.Clear();
			foreach (KeyValuePair<string, int> item in tempShortcuts)
			{
				lb.Items.Add(new ShortcutRow(item.Key, ShortcutNames.For(item.Key) + ": " + GetShortcutStringForMap(tempShortcuts, item.Key)));
			}
			lb.EndUpdate();

			if (keep == null) return;
			for (int i = 0; i < lb.Items.Count; i++)
			{
				if (!string.Equals(ActionOfRow(lb.Items[i]), keep, StringComparison.Ordinal)) continue;
				lb.SelectedIndex = i;
				return;
			}
		}

		/// <summary>The action identifier behind a row; what the row shows is its plain name, not the identifier.</summary>
		string ActionOfRow(object? row) => (row as ShortcutRow)?.Action ?? "";

		/// <summary>The action currently selected in the list, or null when nothing is.</summary>
		string? ActionOfSelectedRow() => lb.SelectedItem == null ? null : ActionOfRow(lb.SelectedItem);
	}

	/// <summary>
	/// One row of the Shortcut Customization list: the action it remaps, and what it reads as. The two used to be one
	/// string, "Action: Key", split back apart on the colon — which only worked while the row showed the identifier.
	/// </summary>
	private sealed record ShortcutRow(string Action, string Text)
	{
		public override string ToString() => Text;
	}

	/// <summary>
	/// Returns a human-readable key label for <paramref name="action"/> looked up in <paramref name="map"/>
	/// (used during the shortcut editor preview before changes are saved).
	/// </summary>
	private string GetShortcutStringForMap(Dictionary<string, int> map, string action)
	{
		if (!map.TryGetValue(action, out int value) || value == Shortcut.None)
			return Loc.T("shortcutMgr.unmapped");

		var label = new StringBuilder();
		if (Shortcut.HasControl(value)) label.Append("Ctrl+");
		if (Shortcut.HasShift(value)) label.Append("Shift+");
		if (Shortcut.HasAlt(value)) label.Append("Alt+");
		label.Append((Keys)Shortcut.KeyOf(value));

		return label.ToString();
	}
}
