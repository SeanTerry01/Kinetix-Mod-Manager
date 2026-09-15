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
			Dock = DockStyle.Fill,
			DropDownStyle = ComboBoxStyle.DropDownList,
			Font = new Font("Segoe UI", 12f)
		};
		cmbTheme.Items.AddRange(SoundThemes.Installed(themesPath).Cast<object>().ToArray());
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
		foreach (string key2 in SoundEngine.SoundDescriptions.Keys)
		{
			lb.Items.Add(key2);
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
				Speak(Loc.T("soundDemo.announce", Loc.T("sound." + key), lb.SelectedIndex + 1, lb.Items.Count));
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
		});
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
		Button button = new Button
		{
			Text = Loc.T("themeMgr.setActive"),
			Dock = DockStyle.Top,
			Height = 35
		};
		button.Click += delegate
		{
			if (lb.SelectedItem != null)
			{
				tempActiveTheme = lb.SelectedItem.ToString() ?? "";
				Speak(Loc.T("themeMgr.activeChanged", tempActiveTheme));
			}
		};
		Button button2 = new Button
		{
			Text = Loc.T("themeMgr.createNew"),
			Dock = DockStyle.Top,
			Height = 35
		};
		button2.Click += delegate
		{
			string? text = ShowTextPrompt(Loc.T("themeMgr.createTitle"), Loc.T("themeMgr.createPrompt"), "");
			if (text == null) { Speak(Loc.T("common.changesCancelled")); return; }

			text = text.Trim();
			if (text.Length == 0) { Speak(Loc.T("themeMgr.nameEmpty")); return; }

			string text2 = Path.Combine(themesPath, text);
			// Said rather than passed over in silence: a name that already exists used to leave the button looking
			// like it had done nothing, with no way to tell that from a failure.
			if (Directory.Exists(text2)) { Speak(Loc.T("themeMgr.nameTaken", text)); return; }

			Directory.CreateDirectory(text2);
			foreach (string key in SoundEngine.SoundDescriptions.Keys)
			{
				Directory.CreateDirectory(Path.Combine(text2, key));
			}
			Directory.CreateDirectory(Path.Combine(text2, "logo"));
			Speak(Loc.T("themeMgr.created"));
			Process.Start("explorer.exe", text2);
			RefreshList();
		};
		Button button3 = new Button
		{
			Text = Loc.T("themeMgr.addMissing"),
			Dock = DockStyle.Top,
			Height = 35
		};
		button3.Click += delegate
		{
			int num = 0;
			string[] directories = Directory.GetDirectories(themesPath);
			foreach (string path in directories)
			{
				foreach (string key2 in SoundEngine.SoundDescriptions.Keys)
				{
					string path2 = Path.Combine(path, key2);
					if (!Directory.Exists(path2))
					{
						Directory.CreateDirectory(path2);
						num++;
					}
				}
			}
			Speak(Loc.T("themeMgr.foldersAdded", num));
		};
		Button button4 = new Button
		{
			Text = Loc.T("themeMgr.delete"),
			Dock = DockStyle.Top,
			Height = 35
		};
		button4.Click += delegate
		{
			if (lb.SelectedItem != null)
			{
				string text = lb.SelectedItem.ToString() ?? "";
				if (text == "Default")
				{
					SpeakBox(Loc.T("themeMgr.cannotDeleteDefault"));
				}
				else if (SpeakBox(Loc.T("themeMgr.confirmDelete", text), Loc.T("common.confirm"), MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					Directory.Delete(Path.Combine(themesPath, text), recursive: true);
					if (tempActiveTheme == text)
					{
						tempActiveTheme = "Default";
					}
					RefreshList();
					Speak(Loc.T("themeMgr.deleted"));
				}
			}
		};
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			FlowDirection = FlowDirection.RightToLeft,
			Height = 50
		};
		Button button5 = new Button
		{
			Text = Loc.T("common.saveAndClose"),
			Width = 120,
			Height = 35
		};
		button5.Click += delegate
		{
			_settings.CurrentTheme = tempActiveTheme;
			_settings.Save();
			saved = true;
			closeView();
			Speak(Loc.T("themeMgr.saved"));
		};
		Button button6 = new Button
		{
			Text = Loc.T("common.cancel"),
			Width = 100,
			Height = 35
		};
		button6.Click += delegate
		{
			closeView();
		};
		flowLayoutPanel.Controls.AddRange(button5, button6);
		tableLayoutPanel.Controls.Add(lb, 0, 0);
		tableLayoutPanel.Controls.Add(button, 0, 1);
		tableLayoutPanel.Controls.Add(button2, 0, 2);
		tableLayoutPanel.Controls.Add(button3, 0, 3);
		tableLayoutPanel.Controls.Add(button4, 0, 4);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 5);
		container.Controls.Add(tableLayoutPanel);
		// Escape is handled by the view itself; "changes cancelled" is announced from onClosed below so it is
		// said whichever way the view was left — Escape or the Cancel button.
		ApplyScreenReaderPauses(container);
		return lb;
		},
		onClosed: () => { if (!saved) Speak(Loc.T("common.changesCancelled")); });

		void RefreshList()
		{
			lb.Items.Clear();
			lb.Items.AddRange(SoundThemes.Installed(themesPath).Cast<object>().ToArray());
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
		RefreshList();

		// Remapping happens on the list itself rather than in a prompt of its own. The list already has focus,
		// so the next keystroke is already going there — putting it into a "waiting for the key" mode means no
		// second control to focus, nothing extra for the screen reader to announce, and the instruction is
		// heard exactly once. A window (and then a view) for this was what read the instruction twice.
		string? awaitingFor = null;

		void BeginRemap()
		{
			if (lb.SelectedItem == null) return;
			awaitingFor = (lb.SelectedItem.ToString() ?? "").Split(':')[0].Trim();
			Speak(Loc.T("shortcutMgr.pressFor", awaitingFor));
		}

		Button button = new Button
		{
			Text = Loc.T("shortcutMgr.remap"),
			Dock = DockStyle.Fill,
			Height = 35
		};
		button.Click += delegate
		{
			BeginRemap();
			// The keystroke has to land on the list, so send focus back there to catch it.
			if (awaitingFor != null) lb.Focus();
		};

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
				Speak(Loc.T("shortcutMgr.remapCancelled", action));
				return;
			}

			tempShortcuts[action] = (int)e.KeyData;
			// Rebuild, landing back on the action just changed rather than at the top of 48 of them.
			RefreshList(action);
			// Read the key back from the same formatter the list rows use, so what is spoken is exactly what is
			// now shown against the action — and you can hear whether the combination you meant is what landed.
			Speak(Loc.T("shortcutMgr.remapped", action, GetShortcutStringForMap(tempShortcuts, action)));
		};
		Button button2 = new Button
		{
			Text = Loc.T("shortcutMgr.reset"),
			Dock = DockStyle.Fill,
			Height = 35
		};
		button2.Click += delegate
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
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			FlowDirection = FlowDirection.RightToLeft,
			Height = 50
		};
		Button button3 = new Button
		{
			Text = Loc.T("common.saveAndClose"),
			Width = 120,
			Height = 35
		};
		button3.Click += delegate
		{
			_settings.Shortcuts = tempShortcuts;
			_settings.Save();
			saved = true;
			closeView();
			Speak(Loc.T("shortcutMgr.saved"));
			SetupAccessibleUI();
		};
		Button button4 = new Button
		{
			Text = Loc.T("common.cancel"),
			Width = 100,
			Height = 35
		};
		button4.Click += delegate
		{
			closeView();
		};
		flowLayoutPanel.Controls.AddRange(button3, button4);
		tableLayoutPanel.Controls.Add(lb, 0, 0);
		tableLayoutPanel.Controls.Add(button, 0, 1);
		tableLayoutPanel.Controls.Add(button2, 0, 2);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 3);
		container.Controls.Add(tableLayoutPanel);
		// Escape is handled by the view itself; "changes cancelled" is announced from onClosed below so it is
		// said whichever way the view was left — Escape or the Cancel button.
		ApplyScreenReaderPauses(container);
		return lb;
		},
		onClosed: () => { if (!saved) Speak(Loc.T("common.changesCancelled")); });

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
				lb.Items.Add(item.Key + ": " + GetShortcutStringForMap(tempShortcuts, item.Key));
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

		/// <summary>The action name from a row, which is stored as "Action: Key".</summary>
		string ActionOfRow(object? row) => (row?.ToString() ?? "").Split(':')[0].Trim();

		/// <summary>The action currently selected in the list, or null when nothing is.</summary>
		string? ActionOfSelectedRow() => lb.SelectedItem == null ? null : ActionOfRow(lb.SelectedItem);
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
