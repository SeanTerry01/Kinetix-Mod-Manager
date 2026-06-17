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
	/// <summary>Opens the navigable manual: MANUAL.md as a drill-down of sections and their sub-topics.</summary>
	private void ShowManual()
	{
		string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MANUAL.md");
		if (!File.Exists(path))
		{
			MessageBox.Show(Loc.T("manual.notFound"));
			return;
		}
		// The manual is one '#' title with '##' sections beneath it; surface those sections as the top level.
		List<DocNode> roots = NormalizeDocRoots(ParseDocTree(File.ReadAllLines(path)));
		CollapseRedundantLevels(roots);

		// Append a live "Current Key Mappings" entry reflecting the user's actual (possibly remapped) shortcuts.
		StringBuilder mappings = new StringBuilder();
		foreach (KeyValuePair<string, Keys> shortcut in _settings.Shortcuts)
			mappings.AppendLine($"* {shortcut.Key}: {GetShortcutString(shortcut.Key)}");
		roots.Add(new DocNode(Loc.T("manual.currentKeyMappings")) { Content = mappings.ToString() });

		ShowDocDrilldown(roots, Loc.T("manual.windowTitle"), Loc.T("manual.toc"), Loc.T("manual.topicInfo"));
	}

	/// <summary>Opens the navigable change log: each version is a top-level entry you open to read its changes.</summary>
	private void ShowChangeLog()
	{
		string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CHANGELOG.md");
		if (!File.Exists(path))
		{
			MessageBox.Show(Loc.T("changelog.notFound"));
			return;
		}
		// Each version is its own '#' heading; the "New in Version X" '##' under it is a redundant wrapper that
		// CollapseRedundantLevels removes, so opening a version lists its change categories directly.
		List<DocNode> roots = ParseDocTree(File.ReadAllLines(path));
		CollapseRedundantLevels(roots);
		ShowDocDrilldown(roots, Loc.T("changelog.windowTitle"), Loc.T("changelog.toc"), Loc.T("changelog.topicInfo"));
	}

	/// <summary>One heading in a document: its title, the body text directly beneath it (before any sub-heading),
	/// and its sub-headings as children. Built into a tree by <see cref="ParseDocTree"/>.</summary>
	private class DocNode
	{
		public string Label { get; }
		public string Content { get; set; } = "";
		public List<DocNode> Children { get; } = new();

		public DocNode(string label) { Label = label; }

		public override string ToString() => Label;
	}

	/// <summary>
	/// Parses Markdown into a tree keyed by heading level (one '#' is a parent of '##', and so on). Each heading
	/// becomes a node; the lines beneath it, up to the next heading of any level, become that node's own content.
	/// Lines before the first heading are dropped (the manual and change log both open with a heading).
	/// </summary>
	private static List<DocNode> ParseDocTree(string[] lines)
	{
		var roots = new List<DocNode>();
		var stack = new List<(int level, DocNode node)>();
		foreach (string raw in lines)
		{
			Match h = Regex.Match(raw, @"^(#{1,6})\s+(.*\S)\s*$");
			if (h.Success)
			{
				int level = h.Groups[1].Value.Length;
				var node = new DocNode(h.Groups[2].Value.Trim());
				// A new heading closes any open headings at the same or deeper level, then nests under whatever
				// shallower heading remains (or becomes a root if none does).
				while (stack.Count > 0 && stack[^1].level >= level) stack.RemoveAt(stack.Count - 1);
				if (stack.Count == 0) roots.Add(node);
				else stack[^1].node.Children.Add(node);
				stack.Add((level, node));
			}
			else if (stack.Count > 0)
			{
				stack[^1].node.Content += raw + "\n";
			}
		}
		return roots;
	}

	/// <summary>If a document has a single top-level node (the manual's one title), surfaces its children as the
	/// roots so the list doesn't open on a pointless one-item level. The title's own intro text, if any, becomes a
	/// leading "Introduction" entry so nothing is lost.</summary>
	private static List<DocNode> NormalizeDocRoots(List<DocNode> roots)
	{
		if (roots.Count != 1 || roots[0].Children.Count == 0) return roots;

		DocNode title = roots[0];
		var result = new List<DocNode>();
		if (!string.IsNullOrWhiteSpace(title.Content))
			result.Add(new DocNode(Loc.T("doc.intro")) { Content = title.Content });
		result.AddRange(title.Children);
		return result;
	}

	/// <summary>Removes redundant single-child wrapper levels: when a node has exactly one child that carries no
	/// text of its own, that wrapper is dropped and its children are promoted up (e.g. a change log version whose
	/// only child is "New in Version X" then lists categories — the wrapper just adds a needless extra step).</summary>
	private static void CollapseRedundantLevels(List<DocNode> nodes)
	{
		foreach (DocNode node in nodes)
		{
			while (node.Children.Count == 1 && string.IsNullOrWhiteSpace(node.Children[0].Content) && node.Children[0].Children.Count > 0)
			{
				List<DocNode> grandchildren = node.Children[0].Children;
				node.Children.Clear();
				node.Children.AddRange(grandchildren);
			}
			CollapseRedundantLevels(node.Children);
		}
	}

	/// <summary>
	/// Shows the shared, screen-reader-friendly document viewer: a drill-down list of headings on the left and the
	/// selected heading's text on the right. Up/Down move; Right or Enter opens a heading that has sub-topics;
	/// Left or Backspace goes back up; Tab reads the text; Escape closes. Hides the main window while open and
	/// restores it on close. Used by both the manual and the change log so they look and operate identically.
	/// The drill-down mirrors the Ctrl+H controls viewer (focus bounce on level change, breadcrumb as the list
	/// title, position spoken just after the item) so the two navigate the same way.
	/// </summary>
	private void ShowDocDrilldown(List<DocNode> roots, string windowTitle, string tocName, string contentName)
	{
		Hide();
		Form docForm = new Form
		{
			Text = windowTitle,
			Size = new Size(900, 600),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true,
			// Drilling between levels briefly bounces focus through the form to force a clean list re-read; a
			// blank accessible name keeps the screen reader from announcing the window title on every bounce.
			AccessibleName = " "
		};

		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1
		};
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));
		layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65f));

		ListBox list = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = tocName
		};
		TextBox tbContent = new TextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Vertical,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = contentName
		};

		layout.Controls.Add(list, 0, 0);
		layout.Controls.Add(tbContent, 1, 0);
		docForm.Controls.Add(layout);

		// --- Drill-down navigation state ---------------------------------------------------------------
		List<DocNode> currentNodes = new List<DocNode>();
		string currentCrumb = "";
		var backStack = new Stack<(List<DocNode> nodes, int index, string crumb)>();
		bool suppressIndexAnnounce = false;

		DocNode? Selected() => list.SelectedItem as DocNode;

		// "x of y" for the selected item, noting whether it opens into sub-topics so the user knows Right will drill.
		string PositionText(int i)
		{
			if (i < 0 || i >= currentNodes.Count) return "";
			bool hasChildren = currentNodes[i].Children.Count > 0;
			return Loc.T(hasChildren ? "doc.posGroup" : "common.position", i + 1, currentNodes.Count);
		}

		// Mirror the selected heading's own text into the content pane (silently — the pane isn't focused).
		void UpdateContent()
		{
			DocNode? n = Selected();
			tbContent.Text = n != null ? n.Content.Trim() : "";
			tbContent.SelectionStart = 0;
			tbContent.SelectionLength = 0;
		}

		// Speak the position just after the screen reader reads the item text — when arrowing within a level and
		// when focus returns to the list.
		async void AnnounceSelection()
		{
			int i = list.SelectedIndex;
			if (i < 0) return;
			await Task.Delay(100);
			if (!list.Focused || list.SelectedIndex != i) return;
			string pos = PositionText(i);
			if (pos.Length > 0) Speak(pos);
		}

		void ShowLevel(List<DocNode> nodes, string crumb, int selectIndex)
		{
			currentNodes = nodes;
			currentCrumb = crumb;
			// The breadcrumb path is the list's title, so the screen reader reads it on focus / tab-back.
			list.AccessibleName = string.IsNullOrEmpty(crumb) ? tocName : crumb;

			list.BeginUpdate();
			list.Items.Clear();
			foreach (DocNode n in nodes) list.Items.Add(n);
			list.EndUpdate();

			// Set the selection without letting the per-item handler announce it; focus handling announces.
			suppressIndexAnnounce = true;
			if (list.Items.Count > 0)
				list.SelectedIndex = Math.Clamp(selectIndex, 0, list.Items.Count - 1);
			suppressIndexAnnounce = false;
		}

		// Moves to a new level by rebuilding the list while it is briefly unfocused, then refocusing it, so the
		// screen reader gives its normal "list title (breadcrumb), then selected item" readout exactly once.
		void GoToLevel(List<DocNode> nodes, string crumb, int selectIndex)
		{
			docForm.ActiveControl = null;
			ShowLevel(nodes, crumb, selectIndex);
			list.Focus();
		}

		void DrillIn()
		{
			if (Selected() is DocNode n && n.Children.Count > 0)
			{
				backStack.Push((currentNodes, list.SelectedIndex, currentCrumb));
				string crumb = string.IsNullOrEmpty(currentCrumb) ? n.Label : $"{currentCrumb}, {n.Label}";
				GoToLevel(n.Children, crumb, 0);
			}
		}

		void DrillUp()
		{
			if (backStack.Count == 0) return;
			var (nodes, index, crumb) = backStack.Pop();
			GoToLevel(nodes, crumb, index);
		}

		list.SelectedIndexChanged += delegate
		{
			UpdateContent();
			if (suppressIndexAnnounce) return;
			if (list.Focused) AnnounceSelection();
		};

		// Re-announce the current item whenever the list regains focus (on open, or returning from the content
		// box), since the selection itself hasn't changed in those cases.
		list.GotFocus += delegate { AnnounceSelection(); };

		// Right/Enter opens the selected heading's sub-topics; Left/Backspace goes up a level.
		list.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Right || pe.KeyCode == Keys.Enter)
			{
				if (Selected() is DocNode n && n.Children.Count > 0)
				{
					pe.Handled = true;
					pe.SuppressKeyPress = true;
					DrillIn();
				}
			}
			else if (pe.KeyCode == Keys.Left || pe.KeyCode == Keys.Back)
			{
				pe.Handled = true;
				pe.SuppressKeyPress = true;
				DrillUp();
			}
		};

		// Tabbing into the content box should land the cursor at the start of the topic, not the bottom.
		tbContent.GotFocus += delegate
		{
			tbContent.SelectionStart = 0;
			tbContent.SelectionLength = 0;
			tbContent.ScrollToCaret();
		};

		docForm.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Escape)
			{
				docForm.Close();
			}
			else if (pe.KeyCode == Keys.F1 && pe.Shift)
			{
				pe.Handled = true;
				pe.SuppressKeyPress = true;
				Speak(Loc.T("doc.help"));
			}
		};

		docForm.FormClosing += delegate { Show(); };

		ShowLevel(roots, "", 0);
		docForm.Shown += delegate
		{
			list.Focus();
			Speak(Loc.T("doc.navHint"));
		};
		docForm.ShowDialog();
	}

	/// <summary>
	/// Shows a standard, screen-reader-friendly "About" dialog: a brief description of the program, its version,
	/// publisher, website, and licensing — the kind of dialog most applications expose from their Help menu.
	/// </summary>
	private void ShowAbout()
	{
		string about = Loc.T("about.body", NexusService.AppVersion).Replace("\n", Environment.NewLine);

		Form aboutForm = new Form
		{
			Text = Loc.T("about.windowTitle"),
			Size = new Size(600, 460),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true,
			MinimizeBox = false,
			MaximizeBox = false,
			FormBorderStyle = FormBorderStyle.FixedDialog
		};
		aboutForm.KeyDown += delegate (object? s, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape) aboutForm.Close();
		};

		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(15),
			ColumnCount = 1,
			RowCount = 2
		};
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55f));

		// Read-only multiline box so a screen reader can read or arrow through the text line by line.
		TextBox tbAbout = new TextBox
		{
			Dock = DockStyle.Fill,
			Multiline = true,
			ReadOnly = true,
			ScrollBars = ScrollBars.Vertical,
			Font = new Font("Segoe UI", 12f),
			Text = about,
			AccessibleName = Loc.T("about.title"),
			TabStop = true
		};
		tbAbout.GotFocus += delegate { tbAbout.Select(0, 0); };

		Button btnClose = new Button
		{
			Text = Loc.T("common.close"),
			Dock = DockStyle.Right,
			Width = 140,
			Height = 45,
			Font = new Font("Segoe UI", 12f, FontStyle.Bold),
			AccessibleName = Loc.T("about.close")
		};
		btnClose.Click += delegate { aboutForm.Close(); };

		layout.Controls.Add(tbAbout, 0, 0);
		layout.Controls.Add(btnClose, 0, 1);
		aboutForm.Controls.Add(layout);
		aboutForm.AcceptButton = btnClose;
		aboutForm.CancelButton = btnClose;
		aboutForm.Shown += delegate
		{
			tbAbout.Focus();
			Speak(Loc.T("about.spoken", NexusService.AppVersion));
		};
		aboutForm.ShowDialog();
	}

	/// <summary>One control line: a parsed key plus its description, or (when <see cref="Key"/> is null) a
	/// plain info/section-intro line shown verbatim.</summary>
	private class KbEntry
	{
		public string? Key { get; set; }
		public string Text { get; set; } = "";
	}

	/// <summary>A named group of control lines (e.g. a README sub-section like "Scanner" or "Combat").
	/// An empty <see cref="Name"/> is the un-sectioned bucket shown directly under its parent.</summary>
	private class KbSection
	{
		public string Name { get; set; } = "";
		public bool Gamepad { get; set; }
		public List<KbEntry> Entries { get; } = new();
	}

	/// <summary>One entry in the drill-down list: either a leaf (a key/info line) or a group with children.
	/// <see cref="Owner"/> is the mod the node belongs to, so the config editor works from anywhere inside it.</summary>
	private class NavNode
	{
		public string Label { get; }
		public ModKeybinds? Owner { get; }
		public List<NavNode> Children { get; } = new();

		/// <summary>An intro/info line: shown and read aloud, but not counted in the level's "x of y" position.</summary>
		public bool IsInfo { get; set; }

		public NavNode(string label, ModKeybinds? owner)
		{
			Label = label;
			Owner = owner;
		}

		public override string ToString() => Label;
	}

	/// <summary>A source of controls in the list (a mod, or the base-game reference), holding its sections.</summary>
	private class ModKeybinds
	{
		public string Name { get; }
		public string ConfigPath { get; set; } = "";
		public List<KbSection> Sections { get; } = new();

		public ModKeybinds(string name, string configPath = "")
		{
			Name = name;
			ConfigPath = configPath;
		}

		public bool HasContent => Sections.Any(s => s.Entries.Count > 0);

		public override string ToString() => Name;
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

	/// <summary>
	/// Extracts a mod's keyboard-controls block from a Markdown document (typically its README) into structured
	/// sections. Finds the first heading that names controls/keybinds, then groups everything beneath it by
	/// sub-heading until the next same-or-higher heading. Each content line is parsed into a key + description
	/// (or kept as a plain info line). Returns exactly what the author documented — no guessing, no hardcoding —
	/// so a mod that revises its README automatically shows the new controls.
	/// </summary>
	private static List<KbSection> ParseKeybindStructure(string filePath)
	{
		var sections = new List<KbSection>();
		try
		{
			bool inSection = false;
			int sectionLevel = 0;
			KbSection? current = null;

			void Push()
			{
				if (current != null && current.Entries.Count > 0) sections.Add(current);
				current = null;
			}

			foreach (string raw in File.ReadAllLines(filePath))
			{
				Match h = Regex.Match(raw, @"^(#{1,6})\s+(.*\S)\s*$");
				if (h.Success)
				{
					int level = h.Groups[1].Value.Length;
					string heading = h.Groups[2].Value.Trim();
					if (!inSection)
					{
						if (Regex.IsMatch(heading, @"\b(key\s*binds?|key\s*bindings?|controls?|hotkeys?|keyboard)\b", RegexOptions.IgnoreCase))
						{
							inSection = true;
							sectionLevel = level;
							current = new KbSection();           // un-named intro bucket
						}
					}
					else if (level <= sectionLevel)
					{
						Push();
						break;                                   // next sibling/parent heading ends the block
					}
					else
					{
						Push();
						current = new KbSection { Name = heading, Gamepad = IsGamepadHeading(heading) };
					}
					continue;
				}

				if (!inSection) continue;
				string text = Regex.Replace(raw.Trim(), @"^[-*+]\s+", "").Replace("`", "").Trim();
				if (text.Length == 0) continue;
				current ??= new KbSection();
				current.Entries.Add(MakeKbEntry(text));
			}
			Push();
		}
		catch { }
		return sections;
	}

	private static bool IsGamepadHeading(string heading) =>
		Regex.IsMatch(heading, @"\b(game\s*pad|controller)\b", RegexOptions.IgnoreCase);

	/// <summary>Splits a control line into a key token and description when it reads as "Key: action", else keeps
	/// it as a plain info line. Guards against prose sentences that merely contain a colon.</summary>
	private static KbEntry MakeKbEntry(string line)
	{
		int ci = line.IndexOf(": ", StringComparison.Ordinal);
		if (ci > 0 && ci <= 35)
		{
			string key = line.Substring(0, ci).Trim();
			string desc = line.Substring(ci + 1).Trim();
			// A real key token is short and not a sentence (no internal sentence punctuation).
			if (key.Length > 0 && !key.Contains(". ") && !key.Contains("; ") && !key.EndsWith("."))
				return new KbEntry { Key = key, Text = desc };
		}
		return new KbEntry { Text = line };
	}

	/// <summary>Speech-friendly rendering of a key token: a lone symbol key is fully named, otherwise only the
	/// connector "+" is spoken as "plus" so key names and word-internal hyphens (e.g. "D-pad") stay intact.</summary>
	private static string KeyToSpeech(string key)
	{
		if (string.IsNullOrWhiteSpace(key)) return key;
		string trimmed = key.Trim();
		if (trimmed.Length == 1 && !char.IsLetterOrDigit(trimmed[0])) return TranslatePunctuation(trimmed);
		return Regex.Replace(key.Replace("+", " plus "), @"\s+", " ").Trim();
	}

	/// <summary>Builds the display text for one entry: "spoken-key: description", or the verbatim info line.</summary>
	private static string EntryDisplay(KbEntry e) =>
		e.Key != null ? $"{KeyToSpeech(e.Key)}: {e.Text}" : e.Text;

	/// <summary>Returns the mod's Markdown docs: top-level *.md plus anything under its captured .kinetix_docs folder.</summary>
	private static IEnumerable<string> EnumerateModMarkdownDocs(string modDir)
	{
		var files = new List<string>();
		try
		{
			files.AddRange(Directory.EnumerateFiles(modDir, "*.md", SearchOption.TopDirectoryOnly));
			string docs = Path.Combine(modDir, ModFileSystem.DocsFolderName);
			if (Directory.Exists(docs))
				files.AddRange(Directory.EnumerateFiles(docs, "*.md", SearchOption.AllDirectories));
		}
		catch { }
		return files;
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
			Text = Loc.T("controls.windowTitle", gameName),
			Size = new Size(900, 600),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true,
			// Drilling between levels briefly bounces focus through the form to force a clean list re-read.
			// A blank accessible name keeps the screen reader from announcing the window title on every bounce
			// (the title bar text itself is unchanged for sighted users and NVDA+T).
			AccessibleName = " "
		};

		TableLayoutPanel mainFormLayout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 2,
			ColumnCount = 1
		};
		mainFormLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		mainFormLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));

		// A single accessible drill-down list. Each level is a flat ListBox (which screen readers read
		// reliably, unlike WinForms' TreeView): Enter or Right arrow opens the selected group, Left arrow or
		// Backspace goes back up a level. This gives the nested expand/collapse feel without the TreeView's
		// flaky sibling-navigation announcements.
		ListBox list = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = Loc.T("controls.treeName")
		};

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
			Text = Loc.T("controls.editConfigBtn"),
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			Enabled = false,
			AccessibleName = Loc.T("controls.editConfig")
		};

		Button btnClose = new Button
		{
			Text = Loc.T("controls.closeBtn"),
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			AccessibleName = Loc.T("controls.close")
		};

		bottomLayout.Controls.Add(btnEditConfig, 0, 0);
		bottomLayout.Controls.Add(btnClose, 1, 0);

		mainFormLayout.Controls.Add(list, 0, 0);
		mainFormLayout.Controls.Add(bottomLayout, 0, 1);
		controlsForm.Controls.Add(mainFormLayout);

		// --- Drill-down navigation state ---------------------------------------------------------------
		List<NavNode> currentNodes = new List<NavNode>();
		string currentCrumb = "";
		var backStack = new Stack<(List<NavNode> nodes, int index, string crumb)>();
		bool suppressIndexAnnounce = false;

		NavNode? Selected() => list.SelectedItem as NavNode;

		void UpdateEditButton()
		{
			ModKeybinds? owner = Selected()?.Owner;
			btnEditConfig.Enabled = owner != null && !string.IsNullOrEmpty(owner.ConfigPath) && File.Exists(owner.ConfigPath);
		}

		// "x of y" for the item at index i, counting only real controls — info/intro lines aren't counted and
		// yield an empty string (they're still read aloud by the screen reader, just without a position).
		string PositionText(int i)
		{
			if (i < 0 || i >= currentNodes.Count || currentNodes[i].IsInfo) return "";
			int total = 0, ordinal = 0;
			for (int k = 0; k < currentNodes.Count; k++)
			{
				if (currentNodes[k].IsInfo) continue;
				total++;
				if (k <= i) ordinal++;
			}
			NavNode n = currentNodes[i];
			return Loc.T(n.Children.Count > 0 ? "controls.posGroup" : "common.position", ordinal, total);
		}

		// Per-item position, spoken just after the screen reader reads the item text — used when arrowing
		// within a level and when focus returns to the list.
		async void AnnounceSelection()
		{
			int i = list.SelectedIndex;
			if (i < 0) return;
			await Task.Delay(100);
			if (!list.Focused || list.SelectedIndex != i) return;
			string pos = PositionText(i);
			if (pos.Length > 0) Speak(pos);
		}

		void ShowLevel(List<NavNode> nodes, string crumb, int selectIndex)
		{
			currentNodes = nodes;
			currentCrumb = crumb;
			// The breadcrumb path is the list's title, so the screen reader reads it on focus / tab-back.
			list.AccessibleName = string.IsNullOrEmpty(crumb) ? Loc.T("controls.rootLevel") : crumb;

			list.BeginUpdate();
			list.Items.Clear();
			foreach (NavNode n in nodes) list.Items.Add(n);
			list.EndUpdate();

			// Set the selection without letting the per-item handler announce it; focus handling announces.
			suppressIndexAnnounce = true;
			if (list.Items.Count > 0)
				list.SelectedIndex = Math.Clamp(selectIndex, 0, list.Items.Count - 1);
			suppressIndexAnnounce = false;
			UpdateEditButton();
		}

		// Moves to a new level by rebuilding the list while it is briefly unfocused, then refocusing it. The
		// refocus gives the screen reader's normal "list title (the breadcrumb path), then selected item"
		// readout exactly once — reliable order, no duplicate item — and the focus handler adds the position.
		// (Changing the selection while the list stayed focused is what caused the item to be read twice.)
		void GoToLevel(List<NavNode> nodes, string crumb, int selectIndex)
		{
			controlsForm.ActiveControl = null;
			ShowLevel(nodes, crumb, selectIndex);
			list.Focus();
		}

		void DrillIn()
		{
			if (Selected() is NavNode n && n.Children.Count > 0)
			{
				backStack.Push((currentNodes, list.SelectedIndex, currentCrumb));
				string crumb = string.IsNullOrEmpty(currentCrumb) ? n.Label : $"{currentCrumb}, {n.Label}";
				GoToLevel(n.Children, crumb, 0);
			}
		}

		void DrillUp()
		{
			if (backStack.Count == 0) return;
			var (nodes, index, crumb) = backStack.Pop();
			GoToLevel(nodes, crumb, index);
		}

		Action LoadControls = null!;

		ModKeybinds? OwnerOfSelection() => Selected()?.Owner;

		void TriggerConfigEdit(ModKeybinds mod)
		{
			if (string.IsNullOrEmpty(mod.ConfigPath) || !File.Exists(mod.ConfigPath)) return;
			OpenConfigEditor(mod.Name, mod.ConfigPath, () => LoadControls());
		}

		LoadControls = delegate
		{
			backStack.Clear();
			ShowLevel(BuildNavForest(), "", 0);
		};

		list.SelectedIndexChanged += delegate
		{
			UpdateEditButton();
			if (suppressIndexAnnounce) return;
			if (list.Focused) AnnounceSelection();
		};

		// Re-announce the current item whenever the list regains focus (on open, or returning from the
		// Edit Config / Close buttons), since the selection itself hasn't changed in those cases.
		list.GotFocus += delegate { AnnounceSelection(); };

		// Right/Enter opens the selected group; Left/Backspace goes up a level; Ctrl+E edits the owning
		// mod's config (also on the button). Plain Enter on a leaf does nothing.
		list.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Right || pe.KeyCode == Keys.Enter)
			{
				if (Selected() is NavNode n && n.Children.Count > 0)
				{
					pe.Handled = true;
					pe.SuppressKeyPress = true;
					DrillIn();
				}
			}
			else if (pe.KeyCode == Keys.Left || pe.KeyCode == Keys.Back)
			{
				pe.Handled = true;
				pe.SuppressKeyPress = true;
				DrillUp();
			}
			else if (pe.KeyCode == Keys.E && pe.Control)
			{
				ModKeybinds? mod = OwnerOfSelection();
				if (mod != null && !string.IsNullOrEmpty(mod.ConfigPath) && File.Exists(mod.ConfigPath))
				{
					pe.Handled = true;
					pe.SuppressKeyPress = true;
					TriggerConfigEdit(mod);
				}
			}
		};

		btnEditConfig.Click += delegate
		{
			ModKeybinds? mod = OwnerOfSelection();
			if (mod != null && !string.IsNullOrEmpty(mod.ConfigPath) && File.Exists(mod.ConfigPath))
				TriggerConfigEdit(mod);
		};

		btnClose.Click += delegate { controlsForm.Close(); };

		controlsForm.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Escape)
			{
				controlsForm.Close();
			}
			else if (pe.KeyCode == Keys.F1 && pe.Shift)
			{
				// Context help for this window, mirroring Shift+F1 in the main program.
				pe.Handled = true;
				pe.SuppressKeyPress = true;
				Speak(Loc.T("controls.help"));
			}
		};

		controlsForm.FormClosing += delegate { Show(); };

		LoadControls();
		controlsForm.Shown += delegate
		{
			list.Focus();
			Speak(Loc.T("controls.navHint"));
		};
		controlsForm.ShowDialog();
	}

	/// <summary>
	/// Builds the ordered control sources for the active game: the base-game vanilla reference first, then
	/// every installed mod whose own shipped docs/config actually document keybinds. Mod controls are never
	/// hardcoded — an out-of-date hardcoded key would silently mislead the user.
	/// </summary>
	private List<ModKeybinds> BuildControlSources()
	{
		var sources = new List<ModKeybinds>();

		var baseMod = new ModKeybinds("Base Game Controls (Vanilla Defaults)");
		var baseSection = new KbSection();
		foreach (string line in BaseGameControlLines(_settings.ActiveGame))
			baseSection.Entries.Add(MakeKbEntry(line));
		baseMod.Sections.Add(baseSection);
		sources.Add(baseMod);

		string modsFolder = _settings.CurrentModsPath;
		if (string.IsNullOrEmpty(modsFolder) || !Directory.Exists(modsFolder)) return sources;

		try
		{
			foreach (string dir in Directory.GetDirectories(modsFolder))
			{
				var mod = new ModKeybinds(ReadModDisplayName(dir));

				// Markdown docs the mod ships (e.g. README.md with a "Keybinds"/"Controls" section) become
				// structured sections — the authoritative, version-current list straight from the author.
				foreach (string mdFile in EnumerateModMarkdownDocs(dir))
					mod.Sections.AddRange(ParseKeybindStructure(mdFile));

				// HTML keybind guides and a SMAPI config.json fold into one flat (un-named) section.
				List<string> htmlConfigKeys = ReadHtmlAndConfigKeybinds(dir, out string configPath);
				var flat = new KbSection();
				foreach (string line in htmlConfigKeys)
					flat.Entries.Add(MakeKbEntry(line));
				if (flat.Entries.Count > 0) mod.Sections.Add(flat);
				if (!string.IsNullOrEmpty(configPath)) mod.ConfigPath = configPath;

				if (mod.HasContent) sources.Add(mod);
			}
		}
		catch { }

		return sources;
	}

	/// <summary>Builds the drill-down forest for the active game: one root node per control source.</summary>
	private List<NavNode> BuildNavForest()
	{
		var roots = new List<NavNode>();
		foreach (ModKeybinds mod in BuildControlSources())
			roots.Add(BuildNavNode(mod));
		return roots;
	}

	/// <summary>Builds the drill-down node for one source. A flat source (e.g. the base game, or a README that
	/// lists keys with no sub-sections) puts its lines straight under the mod node. A structured source groups
	/// its named README sections under "Keyboard Controls" / "Gamepad Controls", with any un-sectioned intro
	/// leading the keyboard category. Intro/info lines (no key) are kept and shown, but flagged so they aren't
	/// counted in the position. Every node carries its owning mod so the config editor works from anywhere.</summary>
	private static NavNode BuildNavNode(ModKeybinds mod)
	{
		var modNode = new NavNode(mod.Name, mod);

		var unnamed = mod.Sections.Where(s => string.IsNullOrEmpty(s.Name)).SelectMany(s => s.Entries).ToList();
		var keyboard = mod.Sections.Where(s => !string.IsNullOrEmpty(s.Name) && !s.Gamepad).ToList();
		var gamepad = mod.Sections.Where(s => s.Gamepad).ToList();

		// A flat source has no named sections: its lines (keys + any intro) sit directly under the mod node.
		if (keyboard.Count == 0 && gamepad.Count == 0)
		{
			foreach (KbEntry e in unnamed) modNode.Children.Add(Leaf(e, mod));
			return modNode;
		}

		// Structured source: the un-sectioned intro/keys lead the Keyboard category, then each named section.
		if (keyboard.Count > 0 || unnamed.Count > 0)
		{
			var kb = new NavNode("Keyboard Controls", mod);
			foreach (KbEntry e in unnamed) kb.Children.Add(Leaf(e, mod));
			foreach (KbSection sec in keyboard)
			{
				var sn = new NavNode(sec.Name, mod);
				foreach (KbEntry e in sec.Entries) sn.Children.Add(Leaf(e, mod));
				kb.Children.Add(sn);
			}
			modNode.Children.Add(kb);
		}

		if (gamepad.Count > 0)
		{
			var gp = new NavNode("Gamepad Controls", mod);
			foreach (KbEntry e in gamepad.SelectMany(s => s.Entries))
				gp.Children.Add(Leaf(e, mod));
			modNode.Children.Add(gp);
		}

		return modNode;
	}

	/// <summary>Makes a leaf node for a control line; intro/info lines (no parsed key) are flagged not-counted.</summary>
	private static NavNode Leaf(KbEntry e, ModKeybinds mod) =>
		new NavNode(EntryDisplay(e), mod) { IsInfo = e.Key == null };

	/// <summary>Reads a mod's display name from its manifest (SMAPI <c>manifest.json</c> or the manager's
	/// <c>.manager_manifest.json</c>), falling back to the folder name.</summary>
	private static string ReadModDisplayName(string dir)
	{
		foreach (string manifestName in new[] { "manifest.json", ".manager_manifest.json" })
		{
			string path = Path.Combine(dir, manifestName);
			if (!File.Exists(path)) continue;
			try
			{
				var manifest = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(path));
				if (manifest != null && manifest.TryGetValue("Name", StringComparison.OrdinalIgnoreCase, out var nameToken))
				{
					string name = nameToken.ToString();
					if (!string.IsNullOrWhiteSpace(name)) return name;
				}
			}
			catch { }
		}
		return Path.GetFileName(dir);
	}

	/// <summary>Reads keybind lines from a mod's HTML guide (docs/keybinds.html etc.) and SMAPI config.json,
	/// and reports the config.json path (if any) so it can be offered for editing.</summary>
	private static List<string> ReadHtmlAndConfigKeybinds(string dir, out string configPath)
	{
		var keys = new List<string>();
		configPath = "";

		string docsPath = Path.Combine(dir, "docs");
		if (!Directory.Exists(docsPath)) docsPath = Path.Combine(dir, "Docs");
		if (Directory.Exists(docsPath))
		{
			foreach (string htmlName in new[] { "keybinds.html", "keybindings.html", "controls.html" })
			{
				string htmlFile = Path.Combine(docsPath, htmlName);
				if (File.Exists(htmlFile)) { keys.AddRange(ParseKeybindsHtml(htmlFile)); break; }
			}
		}

		string cfg = Path.Combine(dir, "config.json");
		if (File.Exists(cfg))
		{
			configPath = cfg;
			try
			{
				var configObj = JsonConvert.DeserializeObject<JToken>(File.ReadAllText(cfg));
				if (configObj != null)
				{
					var jsonKeys = new List<string>();
					FindKeybindsInJson(configObj, "", jsonKeys);
					foreach (string jk in jsonKeys)
						if (!keys.Contains(jk)) keys.Add(jk);
				}
			}
			catch { }
		}
		return keys;
	}

	/// <summary>The hardcoded vanilla base-game controls (the only non-mod-sourced list), per active game.</summary>
	private static List<string> BaseGameControlLines(string activeGame)
	{
		if (activeGame == "SkyrimSE")
			return new List<string>
			{
				"W A S D: Move character forward, left, backward, right",
				"E: Interact, talk to NPCs, open doors, or loot",
				"R: Ready or sheathe weapons/magic",
				"Space: Jump",
				"Alt: Sprint",
				"Control: Sneak / Crouch",
				"Tab: Open character menu (Skills, Magic, Map, Inventory)",
				"Q: Open Favorites Menu",
				"1 to 8: Quick-equip item bound in Favorites Menu",
				"F5 and F9: Quick-save / Quick-load game",
			};
		if (activeGame == "Fallout4")
			return new List<string>
			{
				"W A S D: Move character forward, left, backward, right",
				"E: Interact, talk to NPCs, open doors, or loot",
				"Tab: Open Pip-Boy (hold to toggle flashlight)",
				"Q: Toggle V.A.T.S. targeting mode",
				"R: Reload weapon (hold to holster weapon)",
				"Space: Jump",
				"Shift: Sprint",
				"Control: Crouch / Sneak",
				"V: Toggle 3rd-person view / Hold for Settlement Workshop",
				"Escape: Pause menu (select 'Help' for in-game manual)",
				"1 to 0: Quick-equip favorited items",
				"M: Open Map",
				"I: Open Inventory",
				"J: Open Data/Quest journal",
				"O: Toggle Radio",
				"F5 and F9: Quick-save / Quick-load game",
			};
		return new List<string>
		{
			"W A S D: Move character up, left, down, right",
			"Arrow Keys: Navigate through game menus",
			"C or Right Click: Primary interact / action",
			"X or Right Click: Secondary interact / use tool",
			"1 to 0: Select active item in hotbar",
			"Escape or E: Open / close game menu",
		};
	}

	/// <summary>
	/// Opens a modal text editor with JSON syntax validation for editing one of the selected mod's files.
	/// <paramref name="fileLabel"/> names the file in the window title and spoken prompts (for example
	/// "Configuration" or "Manifest") and defaults to "Configuration" for existing callers.
	/// </summary>
	private void OpenConfigEditor(string modName, string configPath, Action onSaveSuccess, string fileLabel = "Configuration")
	{
		if (!File.Exists(configPath))
		{
			Speak(Loc.T("config.fileNotFound", fileLabel));
			return;
		}

		string originalJson = "";
		try
		{
			originalJson = File.ReadAllText(configPath);
		}
		catch
		{
			Speak(Loc.T("config.couldNotRead", fileLabel.ToLower()));
			return;
		}

		Form editorForm = new Form
		{
			Text = Loc.T("config.editorTitle", fileLabel, modName),
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
			AccessibleName = Loc.T("config.jsonEditorName", modName)
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
			Text = Loc.T("config.saveBtn"),
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			AccessibleName = Loc.T("config.saveChanges")
		};

		Button btnCancel = new Button
		{
			Text = Loc.T("config.cancelBtn"),
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			AccessibleName = Loc.T("config.cancelChanges")
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
				Speak(Loc.T("config.invalidJsonSpeak", ex.Message));
				MessageBox.Show(Loc.T("config.invalidJsonBox", ex.Message), Loc.T("config.jsonValidationTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			try
			{
				File.WriteAllText(configPath, editedText);
				Speak(Loc.T("config.saved", fileLabel));
				onSaveSuccess?.Invoke();
				editorForm.DialogResult = DialogResult.OK;
				editorForm.Close();
			}
			catch (Exception ex)
			{
				Speak(Loc.T("config.saveFailed"));
				MessageBox.Show(Loc.T("config.saveFailedBox", ex.Message), Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
				var res = MessageBox.Show(Loc.T("config.discardConfirm"), Loc.T("config.confirmCancelTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
				if (res == DialogResult.No)
				{
					pe.Cancel = true;
					return;
				}
				Speak(Loc.T("common.changesCancelled"));
			}
		};

		Speak(Loc.T("config.editing", fileLabel.ToLower(), modName));
		editorForm.ShowDialog();
	}
}
