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
		Hide();
		string previewTheme = _settings.CurrentTheme;
		Form demoForm = new Form
		{
			Text = Loc.T("soundDemo.title"),
			Size = new Size(500, 600),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true
		};
		// Escape closes from anywhere in the dialog (the form has KeyPreview), not only from the sound list.
		demoForm.KeyDown += delegate (object? s, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape) demoForm.Close();
		};
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
		cmbTheme.Items.AddRange(Directory.GetDirectories(themesPath).Select(Path.GetFileName).Cast<object>()
			.ToArray());
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
		// the list (Enter), so tabbing to the list reads the full description, not just the name.
		Func<Task> announceSound = async () =>
		{
			if (lb.SelectedItem != null)
			{
				string key = lb.SelectedItem.ToString() ?? "";
				await Task.Delay(100);
				Speak(Loc.T("soundDemo.announce", Loc.T("sound." + key), lb.SelectedIndex + 1, lb.Items.Count));
			}
		};
		lb.SelectedIndexChanged += async delegate
		{
			// Gate on focus so seeding the initial selection (while focus is still on the theme
			// combo) does not speak over the dialog opening.
			if (lb.Focused) await announceSound();
		};
		lb.Enter += async delegate { await announceSound(); };
		lb.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Return && lb.SelectedItem != null)
			{
				_soundEngine.Play(lb.SelectedItem.ToString() ?? "", previewTheme);
			}
			if (pe.KeyCode == Keys.Escape)
			{
				demoForm.Close();
			}
		};
		demoForm.FormClosing += delegate
		{
			Show();
		};
		tableLayoutPanel.Controls.Add(lb, 0, 2);
		demoForm.Controls.Add(tableLayoutPanel);
		// Select the first sound on open so the list lands on a real item (and announces it)
		// rather than an empty selection.
		if (lb.Items.Count > 0) lb.SelectedIndex = 0;
		ApplyScreenReaderPauses(demoForm);
		demoForm.ShowDialog();
	}

	/// <summary>Opens the audio theme manager dialog for creating, renaming, and switching sound themes.</summary>
	private void ShowThemeManager()
	{
		Form f = new Form
		{
			Text = Loc.T("themeMgr.title"),
			Size = new Size(500, 600),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(15),
			RowCount = 6
		};
		string tempActiveTheme = _settings.CurrentTheme;
		ListBox lb = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = Loc.T("themeMgr.installedThemes")
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
			string text = Interaction.InputBox(Loc.T("themeMgr.createPrompt"), Loc.T("themeMgr.createTitle"));
			if (!string.IsNullOrEmpty(text))
			{
				string text2 = Path.Combine(themesPath, text);
				if (!Directory.Exists(text2))
				{
					Directory.CreateDirectory(text2);
					foreach (string key in SoundEngine.SoundDescriptions.Keys)
					{
						Directory.CreateDirectory(Path.Combine(text2, key));
					}
					Directory.CreateDirectory(Path.Combine(text2, "logo"));
					Speak(Loc.T("themeMgr.created"));
					Process.Start("explorer.exe", text2);
					RefreshList();
				}
			}
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
			f.Close();
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
			Speak(Loc.T("common.changesCancelled"));
			f.Close();
		};
		flowLayoutPanel.Controls.AddRange(button5, button6);
		tableLayoutPanel.Controls.Add(lb, 0, 0);
		tableLayoutPanel.Controls.Add(button, 0, 1);
		tableLayoutPanel.Controls.Add(button2, 0, 2);
		tableLayoutPanel.Controls.Add(button3, 0, 3);
		tableLayoutPanel.Controls.Add(button4, 0, 4);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 5);
		f.Controls.Add(tableLayoutPanel);
		f.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Escape)
			{
				Speak(Loc.T("common.changesCancelled"));
				f.Close();
			}
		};
		ApplyScreenReaderPauses(f);
		f.ShowDialog();
		void RefreshList()
		{
			lb.Items.Clear();
			lb.Items.AddRange(Directory.GetDirectories(themesPath).Select(Path.GetFileName).Cast<object>()
				.ToArray());
			lb.SelectedItem = tempActiveTheme;
		}
	}

	/// <summary>Opens the keyboard shortcut re-binding dialog.</summary>
	private void ShowShortcutManager()
	{
		Form f = new Form
		{
			Text = Loc.T("shortcutMgr.title"),
			Size = new Size(500, 600),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(15),
			RowCount = 4
		};
		Dictionary<string, Keys> tempShortcuts = new Dictionary<string, Keys>(_settings.Shortcuts);
		ListBox lb = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = Loc.T("shortcutMgr.actionList")
		};
		RefreshList();
		Button button = new Button
		{
			Text = Loc.T("shortcutMgr.remap"),
			Dock = DockStyle.Fill,
			Height = 35
		};
		button.Click += delegate
		{
			if (lb.SelectedItem != null)
			{
				string action = (lb.SelectedItem.ToString() ?? "").Split(':')[0].Trim();
				Speak(Loc.T("shortcutMgr.pressFor", action));
				Form prompt = new Form
				{
					Text = Loc.T("shortcutMgr.pressKeys"),
					Size = new Size(300, 150),
					StartPosition = FormStartPosition.CenterParent,
					FormBorderStyle = FormBorderStyle.FixedDialog,
					KeyPreview = true
				};
				prompt.KeyDown += delegate(object? ps, KeyEventArgs e)
				{
					if (e.KeyCode != Keys.ControlKey && e.KeyCode != Keys.ShiftKey && e.KeyCode != Keys.Menu)
					{
						tempShortcuts[action] = e.KeyData;
						prompt.Close();
						Speak(Loc.T("shortcutMgr.remapped", action));
						RefreshList();
					}
				};
				prompt.ShowDialog();
			}
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
				foreach (KeyValuePair<string, Keys> shortcut in appSettings.Shortcuts)
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
			f.Close();
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
			Speak(Loc.T("common.changesCancelled"));
			f.Close();
		};
		flowLayoutPanel.Controls.AddRange(button3, button4);
		tableLayoutPanel.Controls.Add(lb, 0, 0);
		tableLayoutPanel.Controls.Add(button, 0, 1);
		tableLayoutPanel.Controls.Add(button2, 0, 2);
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 3);
		f.Controls.Add(tableLayoutPanel);
		f.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Escape)
			{
				Speak(Loc.T("common.changesCancelled"));
				f.Close();
			}
		};
		ApplyScreenReaderPauses(f);
		f.ShowDialog();
		void RefreshList()
		{
			lb.Items.Clear();
			foreach (KeyValuePair<string, Keys> item in tempShortcuts)
			{
				lb.Items.Add(item.Key + ": " + GetShortcutStringForMap(tempShortcuts, item.Key));
			}
		}
	}

	/// <summary>
	/// Returns a human-readable key label for <paramref name="action"/> looked up in <paramref name="map"/>
	/// (used during the shortcut editor preview before changes are saved).
	/// </summary>
	private string GetShortcutStringForMap(Dictionary<string, Keys> map, string action)
	{
		if (map.TryGetValue(action, out var value))
		{
			if (value == Keys.None)
			{
				return Loc.T("shortcutMgr.unmapped");
			}
			StringBuilder stringBuilder = new StringBuilder();
			if ((value & Keys.Control) == Keys.Control)
			{
				stringBuilder.Append("Ctrl+");
			}
			if ((value & Keys.Shift) == Keys.Shift)
			{
				stringBuilder.Append("Shift+");
			}
			if ((value & Keys.Alt) == Keys.Alt)
			{
				stringBuilder.Append("Alt+");
			}
			stringBuilder.Append(value & Keys.KeyCode);
			return stringBuilder.ToString();
		}
		return "Unmapped";
	}
}
