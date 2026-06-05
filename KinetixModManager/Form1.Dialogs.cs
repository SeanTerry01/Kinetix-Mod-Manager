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
			Text = "Sound Demo - Escape to Close",
			Size = new Size(500, 600),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true
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
			Text = "Preview Theme:",
			AutoSize = true,
			Padding = new Padding(0, 5, 0, 0)
		}, 0, 0);
		tableLayoutPanel.Controls.Add(cmbTheme, 0, 1);
		ListBox lb = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = "Sound List"
		};
		foreach (string key2 in SoundEngine.SoundDescriptions.Keys)
		{
			lb.Items.Add(key2);
		}
		lb.SelectedIndexChanged += async delegate
		{
			if (lb.SelectedItem != null)
			{
				string key = lb.SelectedItem.ToString() ?? "";
				await Task.Delay(100);
				Speak($"{key}. {SoundEngine.SoundDescriptions[key]}. {lb.SelectedIndex + 1} of {lb.Items.Count}");
			}
		};
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
		demoForm.ShowDialog();
	}

	/// <summary>Opens the audio theme manager dialog for creating, renaming, and switching sound themes.</summary>
	private void ShowThemeManager()
	{
		Form f = new Form
		{
			Text = "Audio Theme Manager - Escape to Cancel",
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
			AccessibleName = "Installed Themes"
		};
		RefreshList();
		Button button = new Button
		{
			Text = "Set Selected as Active Theme",
			Dock = DockStyle.Top,
			Height = 35
		};
		button.Click += delegate
		{
			if (lb.SelectedItem != null)
			{
				tempActiveTheme = lb.SelectedItem.ToString() ?? "";
				Speak("Active theme changed to " + tempActiveTheme + ". Press Save to confirm.");
			}
		};
		Button button2 = new Button
		{
			Text = "Create New Theme",
			Dock = DockStyle.Top,
			Height = 35
		};
		button2.Click += delegate
		{
			string text = Interaction.InputBox("Enter name for new theme:", "Create Theme");
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
					Speak("Theme created. Drop your .ogg files into the theme folders.");
					Process.Start("explorer.exe", text2);
					RefreshList();
				}
			}
		};
		Button button3 = new Button
		{
			Text = "Add Missing Folders to All Themes",
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
			Speak($"Updated themes. Added {num} missing folders.");
		};
		Button button4 = new Button
		{
			Text = "Delete Theme",
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
					MessageBox.Show("Cannot delete Default theme.");
				}
				else if (MessageBox.Show("Delete theme '" + text + "'?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					Directory.Delete(Path.Combine(themesPath, text), recursive: true);
					if (tempActiveTheme == text)
					{
						tempActiveTheme = "Default";
					}
					RefreshList();
					Speak("Theme deleted.");
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
			Text = "Save and Close",
			Width = 120,
			Height = 35
		};
		button5.Click += delegate
		{
			_settings.CurrentTheme = tempActiveTheme;
			_settings.Save();
			f.Close();
			Speak("Theme settings saved.");
		};
		Button button6 = new Button
		{
			Text = "Cancel",
			Width = 100,
			Height = 35
		};
		button6.Click += delegate
		{
			Speak("Changes cancelled.");
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
				Speak("Changes cancelled.");
				f.Close();
			}
		};
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
			Text = "Shortcut Customization - Escape to Cancel",
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
			AccessibleName = "Action List"
		};
		RefreshList();
		Button button = new Button
		{
			Text = "Remap Selected Action",
			Dock = DockStyle.Fill,
			Height = 35
		};
		button.Click += delegate
		{
			if (lb.SelectedItem != null)
			{
				string action = (lb.SelectedItem.ToString() ?? "").Split(':')[0].Trim();
				Speak("Press the new key combination for " + action + "...");
				Form prompt = new Form
				{
					Text = "Press Keys...",
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
						Speak(action + " remapped. Press Save to confirm.");
						RefreshList();
					}
				};
				prompt.ShowDialog();
			}
		};
		Button button2 = new Button
		{
			Text = "Reset All to Defaults",
			Dock = DockStyle.Fill,
			Height = 35
		};
		button2.Click += delegate
		{
			if (MessageBox.Show("Reset all shortcuts to defaults?", "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes)
			{
				tempShortcuts.Clear();
				AppSettings appSettings = new AppSettings();
				appSettings.InitializeDefaults();
				foreach (KeyValuePair<string, Keys> shortcut in appSettings.Shortcuts)
				{
					tempShortcuts[shortcut.Key] = shortcut.Value;
				}
				RefreshList();
				Speak("Reset to defaults. Press Save to confirm.");
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
			Text = "Save and Close",
			Width = 120,
			Height = 35
		};
		button3.Click += delegate
		{
			_settings.Shortcuts = tempShortcuts;
			_settings.Save();
			f.Close();
			Speak("Shortcuts saved.");
			SetupAccessibleUI();
		};
		Button button4 = new Button
		{
			Text = "Cancel",
			Width = 100,
			Height = 35
		};
		button4.Click += delegate
		{
			Speak("Changes cancelled.");
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
				Speak("Changes cancelled.");
				f.Close();
			}
		};
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
				return "Unmapped";
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
