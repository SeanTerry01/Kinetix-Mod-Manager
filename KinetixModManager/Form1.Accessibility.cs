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

/// <summary>Manual viewer, keybind parsing, accessibility controls, and config editor for Form1.</summary>
public partial class Form1
{
	/// <summary>Opens a modal window that displays MANUAL.md content, split into navigable sections.</summary>
	private void ShowManual()
	{
		string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MANUAL.md");
		if (!File.Exists(path))
		{
			MessageBox.Show("Manual not found.");
			return;
		}
		string[] array = File.ReadAllLines(path);
		Dictionary<string, string> sections = new Dictionary<string, string>();
		string key = "General";
		StringBuilder stringBuilder = new StringBuilder();
		string[] array2 = array;
		foreach (string text in array2)
		{
			if (text.StartsWith("#"))
			{
				if (stringBuilder.Length > 0)
				{
					sections[key] = stringBuilder.ToString();
				}
				key = text.TrimStart('#').Trim();
				stringBuilder.Clear();
			}
			else
			{
				stringBuilder.AppendLine(text);
			}
		}
		if (stringBuilder.Length > 0)
		{
			sections[key] = stringBuilder.ToString();
		}
		StringBuilder stringBuilder2 = new StringBuilder();
		foreach (KeyValuePair<string, Keys> shortcut in _settings.Shortcuts)
		{
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(8, 2, stringBuilder3);
			handler.AppendLiteral("* **");
			handler.AppendFormatted(shortcut.Key);
			handler.AppendLiteral("**: ");
			handler.AppendFormatted(GetShortcutString(shortcut.Key));
			stringBuilder3.AppendLine(ref handler);
		}
		sections["Current Key Mappings"] = stringBuilder2.ToString();
		Hide();
		Form manualForm = new Form
		{
			Text = "User Manual - Press Escape to Close",
			Size = new Size(800, 600),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));
		ListBox lbToc = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = "Table of Contents"
		};
		foreach (string key3 in sections.Keys)
		{
			lbToc.Items.Add(key3);
		}
		TextBox tbContent = new TextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Vertical,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = "Topic Information"
		};
		lbToc.SelectedIndexChanged += async delegate
		{
			if (lbToc.SelectedItem != null)
			{
				string key2 = lbToc.SelectedItem.ToString() ?? "";
				tbContent.Text = sections[key2].Trim();
				await Task.Delay(150);
				Speak($"{lbToc.SelectedIndex + 1} of {lbToc.Items.Count}");
			}
		};
		manualForm.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Escape)
			{
				manualForm.Close();
			}
		};
		manualForm.FormClosing += delegate
		{
			Show();
		};
		tableLayoutPanel.Controls.Add(lbToc, 0, 0);
		tableLayoutPanel.Controls.Add(tbContent, 1, 0);
		manualForm.Controls.Add(tableLayoutPanel);
		if (lbToc.Items.Count > 0)
		{
			lbToc.SelectedIndex = 0;
		}
		manualForm.ShowDialog();
	}

	private class ModKeybinds
	{
		public string Name { get; }
		public List<string> Keybinds { get; }
		public string ConfigPath { get; set; } = "";

		public ModKeybinds(string name, List<string> keybinds, string configPath = "")
		{
			Name = name;
			Keybinds = keybinds;
			ConfigPath = configPath;
		}

		public override string ToString()
		{
			return Name;
		}
	}

	private static List<string> ParseKeybindsHtml(string filePath)
	{
		List<string> results = new List<string>();
		try
		{
			string html = File.ReadAllText(filePath);
			// Replace block tags with newlines
			html = Regex.Replace(html, @"<tr[^>]*>", "\n", RegexOptions.IgnoreCase);
			html = Regex.Replace(html, @"<li[^>]*>", "\n", RegexOptions.IgnoreCase);
			html = Regex.Replace(html, @"<p[^>]*>", "\n", RegexOptions.IgnoreCase);
			html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
			
			// Put separator between table cells
			html = Regex.Replace(html, @"</td>\s*<td[^>]*>", " : ", RegexOptions.IgnoreCase);
			
			// Strip remaining HTML tags
			string plainText = Regex.Replace(html, @"<[^>]*>", "");
			
			// Decode HTML entities
			plainText = WebUtility.HtmlDecode(plainText);
			
			// Split into lines
			string[] lines = plainText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var line in lines)
			{
				string trimmed = line.Trim();
				if (string.IsNullOrEmpty(trimmed)) continue;
				
				// Keep lines that have a colon or separator and look like a keybind description
				if (trimmed.Contains(":") || trimmed.Contains("-"))
				{
					trimmed = Regex.Replace(trimmed, @"\s+", " ");
					if (trimmed.Length > 3 && trimmed.Length < 150)
					{
						results.Add(trimmed);
					}
				}
			}
		}
		catch { }
		return results;
	}

	private static void FindKeybindsInJson(JToken token, string parentPath, List<string> results)
	{
		if (token == null) return;
		if (token.Type == JTokenType.Object)
		{
			foreach (var prop in ((JObject)token).Properties())
			{
				FindKeybindsInJson(prop.Value, string.IsNullOrEmpty(parentPath) ? prop.Name : $"{parentPath}.{prop.Name}", results);
			}
		}
		else if (token.Type == JTokenType.Array)
		{
			var arr = (JArray)token;
			for (int i = 0; i < arr.Count; i++)
			{
				FindKeybindsInJson(arr[i], $"{parentPath}[{i}]", results);
			}
		}
		else if (token.Type == JTokenType.String || token.Type == JTokenType.Null)
		{
			string value = token.ToString() ?? "";
			string lastProp = parentPath.Substring(parentPath.LastIndexOf('.') + 1);
			string lastPropLower = lastProp.ToLowerInvariant();
			
			if (lastPropLower.Contains("key") || 
				lastPropLower.Contains("bind") || 
				lastPropLower.Contains("button") || 
				lastPropLower.Contains("hotkey") || 
				lastPropLower.Contains("shortcut") || 
				lastPropLower.Contains("trigger"))
			{
				if (value.Length < 40 && !value.Contains("/") && !value.Contains("\\"))
				{
					string displayValue = string.IsNullOrWhiteSpace(value) || value.Equals("None", StringComparison.OrdinalIgnoreCase) ? "None" : value;
					string displayName = HumanizePropertyName(parentPath);
					results.Add($"{displayValue}: {displayName}");
				}
			}
		}
	}

	private static string HumanizePropertyName(string path)
	{
		string name = path.Substring(path.LastIndexOf('.') + 1);
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < name.Length; i++)
		{
			if (i > 0 && char.IsUpper(name[i]) && (!char.IsUpper(name[i - 1]) || (i < name.Length - 1 && !char.IsUpper(name[i + 1]))))
			{
				sb.Append(' ');
			}
			sb.Append(name[i]);
		}
		return sb.ToString();
	}

	private static string GetSpeakableKeybind(string keybind)
	{
		if (string.IsNullOrEmpty(keybind)) return "";
		int colonIndex = keybind.IndexOf(':');
		if (colonIndex < 0)
		{
			return TranslatePunctuation(keybind);
		}
		
		string keyPart = keybind.Substring(0, colonIndex);
		string descPart = keybind.Substring(colonIndex);
		
		return TranslatePunctuation(keyPart) + descPart;
	}

	private static string TranslatePunctuation(string text)
	{
		var replacements = new Dictionary<string, string>
		{
			{ "[", "Left Bracket" },
			{ "]", "Right Bracket" },
			{ "(", "Left Parenthesis" },
			{ ")", "Right Parenthesis" },
			{ ",", " Comma " },
			{ ".", " Period " },
			{ "/", " Slash " },
			{ "\\", " Backslash " },
			{ "+", " Plus " },
			{ "-", " Minus " },
			{ "?", " Question Mark " },
			{ "<", " Less Than " },
			{ ">", " Greater Than " },
			{ "|", " Vertical Bar " },
			{ ";", " Semicolon " },
			{ "'", " Apostrophe " },
			{ "\"", " Quote " },
			{ "!", " Exclamation Point " },
			{ "@", " At Symbol " },
			{ "#", " Hash " },
			{ "$", " Dollar Sign " },
			{ "%", " Percent " },
			{ "^", " Caret " },
			{ "*", " Star " },
			{ "_", " Underscore " },
			{ "=", " Equals " },
			{ "~", " Tilde " },
			{ "`", " Grave Accent " }
		};

		string result = text;
		foreach (var pair in replacements)
		{
			result = result.Replace(pair.Key, pair.Value);
		}
		
		return Regex.Replace(result, @"\s+", " ").Trim();
	}

	/// <summary>
	/// Displays a modal ListBox of accessibility and game controls for the currently active game.
	/// </summary>
	private void ShowAccessibilityControls()
	{
		string gameName = _settings.ActiveGame switch
		{
			"SkyrimSE" => "Skyrim Special Edition",
			"Fallout4" => "Fallout 4",
			_ => "Stardew Valley"
		};

		Hide();
		Form controlsForm = new Form
		{
			Text = $"Accessibility Controls for {gameName} - Press Escape to Close",
			Size = new Size(900, 600),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true
		};

		TableLayoutPanel mainFormLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 2,
			ColumnCount = 1
		};
		mainFormLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 90f));
		mainFormLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));

		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65f));

		ListBox lbMods = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = "Available Mods and Guides"
		};

		ListBox lbControls = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = "Controls and Descriptions"
		};

		tableLayoutPanel.Controls.Add(lbMods, 0, 0);
		tableLayoutPanel.Controls.Add(lbControls, 1, 0);

		TableLayoutPanel bottomLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1
		};
		bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

		Button btnEditConfig = new Button
		{
			Text = "Edit Mod Configuration (Enter)",
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			Enabled = false,
			AccessibleName = "Edit Selected Mod Configuration"
		};

		Button btnClose = new Button
		{
			Text = "Close (Escape)",
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			AccessibleName = "Close Controls Guide"
		};

		bottomLayout.Controls.Add(btnEditConfig, 0, 0);
		bottomLayout.Controls.Add(btnClose, 1, 0);

		mainFormLayout.Controls.Add(tableLayoutPanel, 0, 0);
		mainFormLayout.Controls.Add(bottomLayout, 0, 1);
		controlsForm.Controls.Add(mainFormLayout);

		List<ModKeybinds> modControlsList = new List<ModKeybinds>();

		Action LoadModsAndControls = null!;

		// Helper to trigger config editor
		Action<ModKeybinds> triggerConfigEdit = delegate(ModKeybinds mod)
		{
			if (string.IsNullOrEmpty(mod.ConfigPath) || !File.Exists(mod.ConfigPath)) return;
			OpenConfigEditor(mod.Name, mod.ConfigPath, delegate
			{
				LoadModsAndControls();
			});
		};

		LoadModsAndControls = delegate
		{
			string selectedModName = "";
			if (lbMods.SelectedItem is ModKeybinds currentMod)
			{
				selectedModName = currentMod.Name;
			}

			modControlsList.Clear();
			lbMods.Items.Clear();
			lbControls.Items.Clear();

			// 1. Add Base Game Controls
			List<string> baseControls = new List<string>();
			if (_settings.ActiveGame == "SkyrimSE")
			{
				baseControls.Add("W A S D: Move character forward, left, backward, right");
				baseControls.Add("E: Interact, talk to NPCs, open doors, or loot");
				baseControls.Add("R: Ready or sheathe weapons/magic");
				baseControls.Add("Space: Jump");
				baseControls.Add("Alt: Sprint");
				baseControls.Add("Control: Sneak / Crouch");
				baseControls.Add("Tab: Open character menu (Skills, Magic, Map, Inventory)");
				baseControls.Add("Q: Open Favorites Menu");
				baseControls.Add("1 to 8: Quick-equip item bound in Favorites Menu");
				baseControls.Add("F5 and F9: Quick-save / Quick-load game");
			}
			else if (_settings.ActiveGame == "Fallout4")
			{
				baseControls.Add("W A S D: Move character forward, left, backward, right");
				baseControls.Add("E: Interact, talk to NPCs, open doors, or loot");
				baseControls.Add("Tab: Open Pip-Boy (hold to toggle flashlight)");
				baseControls.Add("Q: Toggle V.A.T.S. targeting mode");
				baseControls.Add("R: Reload weapon (hold to holster weapon)");
				baseControls.Add("Space: Jump");
				baseControls.Add("Shift: Sprint");
				baseControls.Add("Control: Crouch / Sneak");
				baseControls.Add("V: Toggle 3rd-person view / Hold for Settlement Workshop");
				baseControls.Add("Escape: Pause menu (select 'Help' for in-game manual)");
				baseControls.Add("1 to 0: Quick-equip favorited items");
				baseControls.Add("M: Open Map");
				baseControls.Add("I: Open Inventory");
				baseControls.Add("J: Open Data/Quest journal");
				baseControls.Add("O: Toggle Radio");
				baseControls.Add("F5 and F9: Quick-save / Quick-load game");
			}
			else
			{
				baseControls.Add("W A S D: Move character up, left, down, right");
				baseControls.Add("Arrow Keys: Navigate through game menus");
				baseControls.Add("C or Right Click: Primary interact / action");
				baseControls.Add("X or Right Click: Secondary interact / use tool");
				baseControls.Add("1 to 0: Select active item in hotbar");
				baseControls.Add("Escape or E: Open / close game menu");
			}
			modControlsList.Add(new ModKeybinds("Base Game Controls", baseControls));

			// 2. Add Core Accessibility Mod Custom References
			if (_settings.ActiveGame == "SkyrimSE")
			{
				List<string> skyrimAccessControls = new List<string>();
				skyrimAccessControls.Add("V: Open Interact Menu (talk, loot, open, auto-lockpick near target)");
				skyrimAccessControls.Add("O: Open Fast Travel Menu (accessible list of known locations)");
				skyrimAccessControls.Add("L: Open Accessibility Settings and Help Menu");
				modControlsList.Add(new ModKeybinds("Skyrim Access & Accessibility", skyrimAccessControls));
			}
			else if (_settings.ActiveGame == "Fallout4")
			{
				List<string> fo4AccessControls = new List<string>();
				fo4AccessControls.Add("Tab: Narrate Pip-Boy menus and options");
				modControlsList.Add(new ModKeybinds("Fallout 4 Access", fo4AccessControls));
			}
			else
			{
				List<string> stardewAccessControls = new List<string>();
				stardewAccessControls.Add("Left Control Plus Enter OR Left Bracket: Emulate left mouse click");
				stardewAccessControls.Add("Left Shift Plus Enter OR Right Bracket: Emulate right mouse click");
				stardewAccessControls.Add("Q: Speak current game time, season, and weather");
				stardewAccessControls.Add("R: Speak current gold");
				stardewAccessControls.Add("H: Speak current health and stamina");
				stardewAccessControls.Add("K: Speak player's coordinates and tile info");
				stardewAccessControls.Add("Y: Speak chat/status log messages");
				stardewAccessControls.Add("V: Open auto-travel warp menu (requires warp mod)");
				stardewAccessControls.Add("Left Control Plus Space: Toggle character creation controls visibility");
				modControlsList.Add(new ModKeybinds("Stardew Access", stardewAccessControls));
			}

			// 3. Dynamic Mod Folder Scanning for Custom Config Keybinds and HTML Guides
			string modsFolder = _settings.CurrentModsPath;
			if (!string.IsNullOrEmpty(modsFolder) && Directory.Exists(modsFolder))
			{
				try
				{
					string[] directories = Directory.GetDirectories(modsFolder);
					foreach (string dir in directories)
					{
						string modName = Path.GetFileName(dir);
						
						string manifestPath = Path.Combine(dir, "manifest.json");
						if (File.Exists(manifestPath))
						{
							try
							{
								string manifestJson = File.ReadAllText(manifestPath);
								var manifest = JsonConvert.DeserializeObject<JObject>(manifestJson);
								if (manifest != null && manifest.TryGetValue("Name", StringComparison.OrdinalIgnoreCase, out var nameToken))
								{
									modName = nameToken.ToString();
								}
							}
							catch { }
						}
						
						List<string> modKeys = new List<string>();
						
						string docsPath = Path.Combine(dir, "docs");
						if (!Directory.Exists(docsPath)) docsPath = Path.Combine(dir, "Docs");
						
						if (Directory.Exists(docsPath))
						{
							string htmlFile = Path.Combine(docsPath, "keybinds.html");
							if (!File.Exists(htmlFile)) htmlFile = Path.Combine(docsPath, "keybindings.html");
							if (!File.Exists(htmlFile)) htmlFile = Path.Combine(docsPath, "controls.html");
							
							if (File.Exists(htmlFile))
							{
								var htmlKeys = ParseKeybindsHtml(htmlFile);
								if (htmlKeys.Count > 0)
								{
									modKeys.AddRange(htmlKeys);
								}
							}
						}
						
						string configPath = Path.Combine(dir, "config.json");
						if (File.Exists(configPath))
						{
							try
							{
								string configJson = File.ReadAllText(configPath);
								var configObj = JsonConvert.DeserializeObject<JToken>(configJson);
								if (configObj != null)
								{
									List<string> jsonKeys = new List<string>();
									FindKeybindsInJson(configObj, "", jsonKeys);
									
									foreach (var jk in jsonKeys)
									{
										if (!modKeys.Any(k => k.IndexOf(jk, StringComparison.OrdinalIgnoreCase) >= 0 || jk.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
										{
											modKeys.Add(jk);
										}
									}
								}
							}
							catch { }
						}
						
						if (modKeys.Count > 0)
						{
							var existing = modControlsList.FirstOrDefault(m => m.Name.Equals(modName, StringComparison.OrdinalIgnoreCase) || 
																			   m.Name.IndexOf(modName, StringComparison.OrdinalIgnoreCase) >= 0 ||
																			   modName.IndexOf(m.Name, StringComparison.OrdinalIgnoreCase) >= 0);
							if (existing != null)
							{
								if (File.Exists(configPath))
								{
									existing.ConfigPath = configPath;
								}
								foreach (var mk in modKeys)
								{
									if (!existing.Keybinds.Contains(mk))
									{
										existing.Keybinds.Add(mk);
									}
								}
							}
							else
							{
								modControlsList.Add(new ModKeybinds(modName, modKeys, File.Exists(configPath) ? configPath : ""));
							}
						}
					}
				}
				catch { }
			}

			foreach (var mc in modControlsList)
			{
				lbMods.Items.Add(mc);
			}

			// Restore selection if matching
			int restoreIndex = 0;
			if (!string.IsNullOrEmpty(selectedModName))
			{
				for (int i = 0; i < lbMods.Items.Count; i++)
				{
					if ((lbMods.Items[i] as ModKeybinds)?.Name == selectedModName)
					{
						restoreIndex = i;
						break;
					}
				}
			}

			if (lbMods.Items.Count > 0)
			{
				lbMods.SelectedIndex = restoreIndex;
			}
		};

		lbMods.SelectedIndexChanged += async delegate
		{
			if (lbMods.SelectedItem is ModKeybinds selectedMod)
			{
				lbControls.Items.Clear();
				foreach (var kb in selectedMod.Keybinds)
				{
					lbControls.Items.Add(GetSpeakableKeybind(kb));
				}
				
				bool hasConfig = !string.IsNullOrEmpty(selectedMod.ConfigPath) && File.Exists(selectedMod.ConfigPath);
				btnEditConfig.Enabled = hasConfig;

				if (lbMods.Focused)
				{
					await Task.Delay(100);
					Speak($"{lbMods.SelectedIndex + 1} of {lbMods.Items.Count}");
				}
			}
		};

		lbControls.SelectedIndexChanged += async delegate
		{
			if (lbControls.SelectedItem != null && lbControls.Focused)
			{
				await Task.Delay(100);
				Speak($"{lbControls.SelectedIndex + 1} of {lbControls.Items.Count}");
			}
		};

		lbMods.Enter += delegate
		{
			if (lbMods.Items.Count > 0 && lbMods.SelectedIndex == -1)
			{
				lbMods.SelectedIndex = 0;
			}
			if (lbMods.SelectedItem is ModKeybinds selectedMod)
			{
				Speak($"Mod list. {lbMods.SelectedIndex + 1} of {lbMods.Items.Count}");
			}
		};

		lbControls.Enter += delegate
		{
			if (lbControls.Items.Count > 0 && lbControls.SelectedIndex == -1)
			{
				lbControls.SelectedIndex = 0;
			}
			if (lbControls.SelectedItem != null)
			{
				Speak($"Controls list. {lbControls.SelectedIndex + 1} of {lbControls.Items.Count}");
			}
		};

		lbMods.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Right)
			{
				pe.Handled = true;
				lbControls.Focus();
				if (lbControls.Items.Count > 0)
				{
					if (lbControls.SelectedIndex < 0) lbControls.SelectedIndex = 0;
					Speak($"Entered controls. 1 of {lbControls.Items.Count}");
				}
				else
				{
					Speak("No keybinds defined for this mod.");
				}
			}
			else if (pe.KeyCode == Keys.Enter || (pe.KeyCode == Keys.E && pe.Control))
			{
				pe.Handled = true;
				pe.SuppressKeyPress = true;
				if (lbMods.SelectedItem is ModKeybinds selectedMod && !string.IsNullOrEmpty(selectedMod.ConfigPath))
				{
					triggerConfigEdit(selectedMod);
				}
			}
		};

		lbControls.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Left || pe.KeyCode == Keys.Back)
			{
				pe.Handled = true;
				lbMods.Focus();
				Speak($"Returned to mod list. {lbMods.SelectedIndex + 1} of {lbMods.Items.Count}");
			}
		};

		btnEditConfig.Click += delegate
		{
			if (lbMods.SelectedItem is ModKeybinds selectedMod && !string.IsNullOrEmpty(selectedMod.ConfigPath))
			{
				triggerConfigEdit(selectedMod);
			}
		};

		btnClose.Click += delegate
		{
			controlsForm.Close();
		};

		controlsForm.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Escape)
			{
				controlsForm.Close();
			}
		};

		controlsForm.FormClosing += delegate
		{
			Show();
		};

		// Run the initial load
		LoadModsAndControls();

		Speak($"Accessibility Controls for {gameName} opened. Select a mod using Up and Down arrows, then press Tab or Right Arrow to browse its controls.");
		controlsForm.ShowDialog();
	}

	/// <summary>
	/// Opens a modal text editor with syntax validation for editing the selected mod's configuration file.
	/// </summary>
	private void OpenConfigEditor(string modName, string configPath, Action onSaveSuccess)
	{
		if (!File.Exists(configPath))
		{
			Speak("Configuration file not found.");
			return;
		}

		string originalJson = "";
		try
		{
			originalJson = File.ReadAllText(configPath);
		}
		catch
		{
			Speak("Could not read configuration file.");
			return;
		}

		Form editorForm = new Form
		{
			Text = $"Edit Configuration - {modName} - Ctrl+S to Save, Escape to Cancel",
			Size = new Size(800, 600),
			StartPosition = FormStartPosition.CenterParent,
			KeyPreview = true
		};

		TableLayoutPanel mainLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 2,
			ColumnCount = 1
		};
		mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 90f));
		mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));

		TextBox tbJson = new TextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			ScrollBars = ScrollBars.Both,
			Font = new Font("Consolas", 11f),
			Text = originalJson,
			AccessibleName = $"JSON Editor for {modName}"
		};

		TableLayoutPanel buttonLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1
		};
		buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

		Button btnSave = new Button
		{
			Text = "Save (Ctrl+S)",
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			AccessibleName = "Save Changes"
		};

		Button btnCancel = new Button
		{
			Text = "Cancel (Escape)",
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			AccessibleName = "Cancel Changes"
		};

		buttonLayout.Controls.Add(btnSave, 0, 0);
		buttonLayout.Controls.Add(btnCancel, 1, 0);

		mainLayout.Controls.Add(tbJson, 0, 0);
		mainLayout.Controls.Add(buttonLayout, 0, 1);
		editorForm.Controls.Add(mainLayout);

		Action saveAction = delegate
		{
			string editedText = tbJson.Text;
			try
			{
				// Validate JSON formatting
				JsonConvert.DeserializeObject<JToken>(editedText);
			}
			catch (Exception ex)
			{
				Speak($"Invalid JSON syntax. Please correct it. Details: {ex.Message}");
				MessageBox.Show($"Invalid JSON syntax:\n{ex.Message}", "JSON Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			try
			{
				File.WriteAllText(configPath, editedText);
				Speak("Configuration saved.");
				onSaveSuccess?.Invoke();
				editorForm.DialogResult = DialogResult.OK;
				editorForm.Close();
			}
			catch (Exception ex)
			{
				Speak("Failed to save configuration.");
				MessageBox.Show($"Failed to save file:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		};

		btnSave.Click += delegate { saveAction(); };
		btnCancel.Click += delegate { editorForm.Close(); };

		editorForm.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.S && pe.Control)
			{
				pe.Handled = true;
				pe.SuppressKeyPress = true;
				saveAction();
			}
			else if (pe.KeyCode == Keys.Escape)
			{
				pe.Handled = true;
				pe.SuppressKeyPress = true;
				editorForm.Close();
			}
		};

		editorForm.FormClosing += delegate(object? s, FormClosingEventArgs pe)
		{
			if (editorForm.DialogResult != DialogResult.OK && tbJson.Text != originalJson)
			{
				var res = MessageBox.Show("Discard unsaved changes?", "Confirm Cancel", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
				if (res == DialogResult.No)
				{
					pe.Cancel = true;
					return;
				}
				Speak("Changes cancelled.");
			}
		};

		Speak($"Editing configuration for {modName}. Press Control S to save, or Escape to cancel.");
		editorForm.ShowDialog();
	}
}
