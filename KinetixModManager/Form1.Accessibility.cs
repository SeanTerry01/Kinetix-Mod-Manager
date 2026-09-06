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
			SpeakBox(Loc.T("manual.notFound"));
			return;
		}
		// The manual is one '#' title with '##' sections beneath it; surface those sections as the top level.
		List<DocNode> roots = DocOutline.NormalizeRoots(DocOutline.ParseTree(File.ReadAllLines(path)), Loc.T("doc.intro"));
		DocOutline.CollapseRedundantLevels(roots);

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
			SpeakBox(Loc.T("changelog.notFound"));
			return;
		}
		// Each version is its own '#' heading; the "New in Version X" '##' under it is a redundant wrapper that
		// DocOutline.CollapseRedundantLevels removes, so opening a version lists its change categories directly.
		List<DocNode> roots = DocOutline.ParseTree(File.ReadAllLines(path));
		DocOutline.CollapseRedundantLevels(roots);
		ShowDocDrilldown(roots, Loc.T("changelog.windowTitle"), Loc.T("changelog.toc"), Loc.T("changelog.topicInfo"));
	}

	/// <summary>
	/// Shows the shared, screen-reader-friendly document viewer: a drill-down list of headings on the left and the
	/// selected heading's text on the right. Up/Down move; Right or Enter opens a heading that has sub-topics;
	/// Left or Backspace goes back up; Tab reads the text; Escape closes. Used by both the manual and the change
	/// log so they look and operate identically. The drill-down mirrors the Ctrl+H controls viewer (focus bounce
	/// on level change, breadcrumb as the list title, position spoken just after the item) so the two navigate
	/// the same way.
	/// </summary>
	private void ShowDocDrilldown(List<DocNode> roots, string windowTitle, string tocName, string contentName)
	{
		// Shown inside the main window rather than as one of its own — see Form1.InlineView. The window this
		// used to be hid the main one behind it; there is nothing to hide now, and nothing to restore on the
		// way out. The blank accessible name the window carried for the focus bounce is handled by
		// ShowInlineView, which does the same for the main window while any view is up.
		ShowInlineView(windowTitle, (container, closeView) =>
		{
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
		container.Controls.Add(layout);

		// --- Drill-down navigation state ---------------------------------------------------------------
		List<DocNode> currentNodes = new List<DocNode>();
		string currentCrumb = "";
		var backStack = new Stack<(List<DocNode> nodes, int index, string crumb)>();
		bool suppressIndexAnnounce = false;

		DocNode? Selected() => list.SelectedItem as DocNode;

		// "x of y" for the selected item. An item that opens into sub-topics says so and says which keys move in
		// and back out — the same hint a mod group carries in the installed list, for the same reason: the item
		// is a door, and nothing else about it says that it is one.
		string PositionText(int i) =>
			Announcements.OutlinePosition(i, currentNodes.Count,
				n => currentNodes[n].Children.Count > 0,
				(key, position, total) => Loc.T(key, position, total));

		// Mirror the selected heading's own text into the content pane (silently — the pane isn't focused).
		// DocOutline.ContentText normalises the line endings: parsing Markdown leaves bare line feeds behind, and a
		// multiline TextBox only breaks a line on a carriage-return + line-feed pair, so without this a whole
		// section arrived as one unbroken line with nothing to arrow through.
		void UpdateContent()
		{
			tbContent.Text = DocOutline.ContentText(Selected());
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
		// The focus is bounced through the main window now that the view lives inside it — the same bounce, and
		// ShowInlineView keeps the window from announcing itself as focus passes through.
		void GoToLevel(List<DocNode> nodes, string crumb, int selectIndex)
		{
			ActiveControl = null;
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

		// --- Search (Ctrl+F, then F3 / Shift+F3) --------------------------------------------------------
		// The whole document is searched at once, not the section on screen: the point of searching a manual is
		// to find the section you did not know to open.
		List<DocMatch> matches = new List<DocMatch>();
		int matchIndex = -1;
		string searchPhrase = "";
		// Set while a search is placing the caret, so the content box's "start at the top" rule stands aside for
		// the one case that has already decided where the caret belongs.
		bool caretPlacedBySearch = false;

		// Opens the level holding a match, rebuilding the way back so Left still walks out of it one level at a
		// time — arriving somewhere by search should leave the viewer in the state it would be in had you got
		// there by opening sections yourself.
		void OpenLevelOf(DocMatch m)
		{
			backStack.Clear();
			List<DocNode> level = roots;
			string crumb = "";
			for (int depth = 0; depth + 1 < m.Path.Count; depth++)
			{
				DocNode parent = m.Path[depth];
				backStack.Push((level, level.IndexOf(parent), crumb));
				crumb = crumb.Length == 0 ? parent.Label : $"{crumb}, {parent.Label}";
				level = parent.Children;
			}

			// Focus ends in the content box, not the list: the user asked for a line, so the line is where they
			// should land. The bounce through the window is the same one GoToLevel uses, so the reader re-reads
			// the rebuilt level rather than believing it is still showing the old one.
			ActiveControl = null;
			ShowLevel(level, crumb, Math.Max(level.IndexOf(m.Node), 0));
			UpdateContent();
			caretPlacedBySearch = true;
			tbContent.Focus();
		}

		// Puts the caret on the matching line and reads out where we have landed. The line is spoken here rather
		// than left to the screen reader: moving a caret programmatically is not a keystroke, and a reader has no
		// reason to say anything about it.
		//
		// Everything is said as ONE utterance, prefix included. Two calls would not survive each other: the second
		// interrupts, so a "back to the first result" spoken separately would be cut off by the result it was
		// introducing and never heard.
		void GoToMatch(int index, string prefix = "")
		{
			if (index < 0 || index >= matches.Count) return;
			matchIndex = index;
			DocMatch m = matches[index];

			OpenLevelOf(m);
			// Counted from the text, not asked of the box: a wrapped TextBox numbers the lines it draws, not the
			// lines it was given, so its own line-to-character lookup would miss by however much has wrapped
			// above. See DocOutline.CharOffsetOfLine.
			tbContent.SelectionStart = Math.Min(DocOutline.CharOffsetOfLine(m.Node, m.LineIndex), tbContent.TextLength);
			tbContent.SelectionLength = 0;
			tbContent.ScrollToCaret();

			string where = Loc.T("docsearch.atMatch", index + 1, matches.Count, m.Section);
			SpeakLong((prefix.Length > 0 ? prefix + " " : "") + where + " " + m.Line);
		}

		// F3 / Shift+F3. Wraps around, saying so, rather than stopping dead at the last match — a manual is a
		// loop you are scanning, not a file you are editing, and silence at the end reads as a broken key.
		void StepMatch(int delta)
		{
			if (matches.Count == 0)
			{
				Speak(Loc.T(searchPhrase.Length == 0 ? "docsearch.noSearchYet" : "docsearch.noneLeft", searchPhrase));
				return;
			}
			int next = matchIndex + delta;
			string prefix = "";
			if (next < 0) { next = matches.Count - 1; prefix = Loc.T("docsearch.wrappedToEnd"); }
			else if (next >= matches.Count) { next = 0; prefix = Loc.T("docsearch.wrappedToStart"); }
			GoToMatch(next, prefix);
		}

		// Ctrl+F. Asking again replaces the previous search outright — the results list is the search, so there
		// is nothing to keep once a new phrase is typed.
		void RunSearch()
		{
			string? typed = ShowTextPrompt(Loc.T("docsearch.promptTitle"), Loc.T("docsearch.promptLabel"), searchPhrase);
			if (typed == null) return;                       // cancelled, as distinct from cleared
			typed = typed.Trim();
			if (typed.Length == 0) { Speak(Loc.T("docsearch.nothingTyped")); return; }

			searchPhrase = typed;
			matches = DocOutline.Find(roots, typed);
			matchIndex = -1;

			if (matches.Count == 0)
			{
				Speak(Loc.T("docsearch.noResults", typed));
				return;
			}

			DocMatch? picked = ShowDocSearchResults(matches, typed);
			// Backing out of the results keeps the search armed, so F3 still steps through what was found.
			if (picked == null) { Speak(Loc.T("docsearch.keptResults", matches.Count)); return; }
			GoToMatch(matches.IndexOf(picked));
		}

		// Ctrl+F and F3 work from the list and the content box alike, so a search never depends on which half of
		// the viewer focus happens to be in.
		void WireSearchKeys(Control c) => c.KeyDown += delegate (object? s, KeyEventArgs e)
		{
			if (e.Handled) return;
			if (e.Control && e.KeyCode == Keys.F)
			{
				e.Handled = e.SuppressKeyPress = true;
				RunSearch();
			}
			else if (e.KeyCode == Keys.F3)
			{
				e.Handled = e.SuppressKeyPress = true;
				StepMatch(e.Shift ? -1 : 1);
			}
		};

		WireSearchKeys(list);
		WireSearchKeys(tbContent);

		list.SelectedIndexChanged += delegate
		{
			UpdateContent();
			if (suppressIndexAnnounce) return;
			if (list.Focused) AnnounceSelection();
		};

		// Re-announce the current item whenever the list regains focus (on open, or returning from the content
		// box), since the selection itself hasn't changed in those cases.
		list.GotFocus += delegate { AnnounceSelection(); };

		// Right/Enter opens the selected heading's sub-topics; Left/Backspace goes up a level. Right/Left are
		// always swallowed (even on a leaf or at the root) so the ListBox never falls back to its default of
		// treating them like Down/Up; only up/down move the list.
		list.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Right || pe.KeyCode == Keys.Enter)
			{
				pe.Handled = true;
				pe.SuppressKeyPress = true;
				DrillIn();   // no-op on a leaf
			}
			else if (pe.KeyCode == Keys.Left || pe.KeyCode == Keys.Back)
			{
				pe.Handled = true;
				pe.SuppressKeyPress = true;
				DrillUp();
			}
		};

		// Tabbing into the content box should land the cursor at the start of the topic, not the bottom. A search
		// that has just placed the caret on a match is the exception — it put it exactly where it belongs.
		tbContent.GotFocus += delegate
		{
			if (caretPlacedBySearch) { caretPlacedBySearch = false; return; }
			tbContent.SelectionStart = 0;
			tbContent.SelectionLength = 0;
			tbContent.ScrollToCaret();
		};

		// Enter on a line holding a link offers to open it in the browser, the same as a mod's description and
		// the game logs. A manual full of Nexus and GitHub addresses is of little use if the only way to follow
		// one is to write it down and type it in again.
		tbContent.KeyDown += delegate (object? s, KeyEventArgs e)
		{
			if (e.Handled || e.KeyCode != Keys.Enter) return;
			if (TryOpenLinkOnCaretLine(tbContent)) e.Handled = e.SuppressKeyPress = true;
		};

		// Escape is wired by the view itself, on every control, so it closes from the list or the text box alike.
		// Shift+F1 needs the same treatment: the window's key preview is switched off while a view is up, so a
		// form-level handler would never see it.
		AttachViewHelp(container, () => Speak(Loc.T("doc.help")));

		ShowLevel(roots, "", 0);
		ApplyScreenReaderPauses(container);
		return list;
		},
		hint: Loc.T("doc.navHint"));
	}

	/// <summary>
	/// Shows a standard, screen-reader-friendly "About" dialog: a brief description of the program, its version,
	/// publisher, website, and licensing — the kind of dialog most applications expose from their Help menu.
	/// </summary>
	/// <summary>
	/// The supported games as a sentence — "Fallout 4, Moonlight Peaks, Skyrim Special Edition and Stardew
	/// Valley". Built from <see cref="GameProfiles"/> so it is right by construction whenever a game is added.
	/// </summary>
	private static string SupportedGamesInProse()
	{
		var names = GameProfiles.AllDisplayNames;
		if (names.Count == 0) return "";
		if (names.Count == 1) return names[0];
		return Loc.T("common.listAnd", string.Join(", ", names.Take(names.Count - 1)), names[^1]);
	}

	private void ShowAbout()
	{
		// The supported games are listed from the registry rather than written into the text, so adding a game
		// updates this on its own — the sentence had gone stale once already.
		string about = Loc.T("about.body", NexusService.AppVersion, SupportedGamesInProse())
			.Replace("\n", Environment.NewLine);

		// Shown inside the main window rather than as one of its own — see Form1.InlineView. Escape is handled
		// by the view, and there is no longer a window to hide the main one behind.
		ShowInlineView(Loc.T("about.viewTitle"), (container, closeView) =>
		{
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
			// Named deliberately, and not after the heading. Leaving it unnamed does not make the reader silent —
			// it makes the reader go looking, and what it finds is the main window's search box behind the view,
			// so this box announced itself as "Search". A name of its own stops the search; naming it for what it
			// contains keeps it from repeating the heading. See SilentAccessibleName in Form1.Helpers.
			AccessibleName = Loc.T("about.bodyName"),
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
		btnClose.Click += delegate { closeView(); };

		layout.Controls.Add(tbAbout, 0, 0);
		layout.Controls.Add(btnClose, 0, 1);
		container.Controls.Add(layout);
		// Focus lands on the text. The view's heading already says "About Kinetix Mod Manager" and the version is
		// in the text itself, so a separate spoken line would only repeat it.
		return tbAbout;
		});
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
		catch (Exception ex) { DiagnosticLog.WriteException("Controls", $"reading the key bindings out of {filePath}", ex); }
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
		catch (Exception ex) { DiagnosticLog.WriteException("Controls", "reading a mod's key-binding documentation", ex); }
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
		catch (Exception ex) { DiagnosticLog.WriteException("ModDocs", $"listing the documentation in {modDir}", ex); }
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
		string gameName = GameProfiles.DisplayNameFor(_settings.ActiveGame);

		// Shown inside the main window rather than as one of its own — see Form1.InlineView. The window this used
		// to be hid the main one behind it and carried a blank accessible name so the focus bounce between levels
		// stayed quiet; ShowInlineView does that for the main window now, for the whole time a view is up.
		ShowInlineView(Loc.T("controls.windowTitle", gameName), (container, closeView) =>
		{
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
		container.Controls.Add(mainFormLayout);

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
		// The focus is bounced through the main window now that the view lives inside it — the same bounce, and
		// ShowInlineView keeps the window from announcing itself as focus passes through.
		void GoToLevel(List<NavNode> nodes, string crumb, int selectIndex)
		{
			ActiveControl = null;
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

			// A BepInEx plugin's settings are an INI in all but name, so they open in the INI editor — the JSON
			// editor would reject the whole file as malformed. Both are in-window views that stack on top of this
			// one, so Escape from either comes back to the controls list; the list is rebuilt afterwards so an
			// edited key reads correctly straight away.
			if (mod.ConfigPath.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
			{
				ShowIniEditor(mod.ConfigPath, mod.Name);
				LoadControls();
				return;
			}

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
		// mod's config (also on the button). Right/Left are always swallowed (even on a leaf or at the root) so
		// the ListBox never falls back to its default of treating them like Down/Up; only up/down move the list.
		list.KeyDown += delegate(object? s, KeyEventArgs pe)
		{
			if (pe.KeyCode == Keys.Right || pe.KeyCode == Keys.Enter)
			{
				pe.Handled = true;
				pe.SuppressKeyPress = true;
				DrillIn();   // no-op on a leaf
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

		btnClose.Click += delegate { closeView(); };

		// Escape is wired by the view itself, on every control, so it closes from the list or either button.
		// Shift+F1 (context help, mirroring the main program) needs wiring the same way: the window's key preview
		// is switched off while a view is up, so a form-level handler would never see it.
		AttachViewHelp(container, () => Speak(Loc.T("controls.help")));

		LoadControls();
		ApplyScreenReaderPauses(container);
		return list;
		},
		hint: Loc.T("controls.navHint"));
	}

	/// <summary>
	/// Builds the ordered control sources for the active game: the base-game vanilla reference first, then
	/// every installed mod whose own shipped docs/config actually document keybinds. Mod controls are never
	/// hardcoded — an out-of-date hardcoded key would silently mislead the user.
	/// </summary>
	/// <param name="witcherBindings">
	/// The Witcher 3's bindings when that is the active game, empty otherwise. When they are present the game's
	/// own entry has already been built by <see cref="BuildWitcher3GameNode"/>, and what is left to do here is
	/// give each mod that owns actions in that file an entry of its own.
	/// </param>
	private List<ModKeybinds> BuildControlSources(List<Witcher3Binding>? witcherBindings = null)
	{
		var sources = new List<ModKeybinds>();
		witcherBindings ??= new List<Witcher3Binding>();

		// The game's own controls. Read from the game where a keybind export exists, otherwise the short
		// hardcoded list, otherwise nothing at all — a game we have no list for gets no entry rather than
		// another game's keys, because a blind player cannot see that the keys being read out are the wrong
		// game's. (Moonlight Peaks used to fall through to Stardew Valley's.)
		if (witcherBindings.Count == 0)
		{
			ModKeybinds? gameControls = BuildExportedGameControls() ?? BuildHardcodedGameControls();
			if (gameControls != null) sources.Add(gameControls);
		}

		// The installed mods come from the manager's own scan rather than a directory walk of our own. Walking
		// the Mods folder assumed every mod is a folder directly inside it, and two kinds of mod are not: a
		// Stardew mod may sit a level deeper (Stardew Access ships as Mods\StardewAccess\StardewAccess, which is
		// why it was missing from this list entirely), and a BepInEx plugin is identified from its DLL rather
		// than a manifest. ScanMods already knows all of that, and its names are the ones the rest of the
		// manager shows.
		try
		{
			foreach (GameMod installed in _allInstalledMods)
			{
				if (installed.IsGroup) continue;
				string dir = installed.FolderPath;
				if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

				var mod = new ModKeybinds(installed.Name);

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

				// Skyrim/Fallout 4 mods expose their keybinds through MCM Helper, not a README, so read those too.
				mod.Sections.AddRange(ParseMcmKeybinds(dir));

				if (mod.HasContent) sources.Add(mod);
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Controls", "building the controls list for a mod", ex); }

		// A BepInEx plugin keeps its settings outside its own folder — BepInEx writes one .cfg per plugin into
		// BepInEx\config — so nothing above can find them. They fold into the source of the same name where
		// there is one (a plugin that also ships a README), and stand on their own where there is not.
		foreach ((string label, string path) in ModFileSystem.BepInExConfigFiles(_settings.CurrentGamePath))
		{
			List<KbSection> keys = ParseBepInExKeybinds(path);
			if (keys.Count == 0) continue;

			ModKeybinds? existing = sources.FirstOrDefault(s => s.Name.Equals(label, StringComparison.OrdinalIgnoreCase));
			if (existing != null)
			{
				existing.Sections.AddRange(keys);
				if (string.IsNullOrEmpty(existing.ConfigPath)) existing.ConfigPath = path;
				continue;
			}

			// The .cfg is what Edit Config opens for these — in the INI editor, not the JSON one (see
			// TriggerConfigEdit in the viewer), since that is the format BepInEx writes.
			var mod = new ModKeybinds(label, path);
			mod.Sections.AddRange(keys);
			sources.Add(mod);
		}

		// A Witcher 3 mod's keys live in the game's own input.settings, not in anything the mod ships, so
		// nothing above can find them. They fold into the mod's existing entry where it has one — a mod with a
		// README should not appear twice — and stand alone where it does not, which is the usual case: a
		// Witcher 3 mod is a folder of compiled scripts with no documentation in it at all.
		foreach (string modName in Witcher3Controls.ModsWithBindings(witcherBindings))
		{
			ModKeybinds source = BuildWitcher3ModSource(witcherBindings, modName);
			if (!source.HasContent) continue;

			ModKeybinds? existing = sources.FirstOrDefault(s => IsSameMod(s.Name, modName));
			if (existing != null) existing.Sections.AddRange(source.Sections);
			else sources.Add(source);
		}

		return sources;
	}

	/// <summary>
	/// Whether two names refer to the same mod, allowing for the ways one mod gets named twice: the manager
	/// shows a Witcher 3 mod by its folder (<c>modWitcherAccess</c>), while its bindings are attributed from the
	/// same folder read as words ("Witcher Access").
	/// </summary>
	private static bool IsSameMod(string a, string b)
	{
		static string Normalise(string name)
		{
			string bare = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
			return bare.StartsWith("mod", StringComparison.Ordinal) && bare.Length > 3 ? bare.Substring(3) : bare;
		}

		return Normalise(a).Length > 0 && Normalise(a) == Normalise(b);
	}

	/// <summary>
	/// The active game's own controls, read from a keybind export — the player's real bindings when the export
	/// plugin has written them, otherwise the snapshot of stock bindings bundled with the manager.
	///
	/// Which of the two it is gets said, as the first line of the list: someone who has remapped a key and is
	/// being shown the stock one needs to be able to tell that these are defaults rather than conclude the
	/// manager is wrong about their game. That line carries no key, so it reads out but isn't counted in the
	/// list's "x of y".
	///
	/// <c>null</c> when the game has no export of either kind, leaving the hardcoded list to answer.
	/// </summary>
	private ModKeybinds? BuildExportedGameControls()
	{
		GameKeybindExport? export = GameKeybindExport.Load(
			GameProfiles.Find(_settings.ActiveGame), _settings.CurrentGamePath, AppContext.BaseDirectory);
		if (export == null) return null;

		var mod = new ModKeybinds(Loc.T(export.IsLive ? "controls.gameKeysLive" : "controls.gameKeysDefault"));
		var section = new KbSection();

		section.Entries.Add(new KbEntry
		{
			Text = export.IsLive
				? Loc.T("controls.gameKeysLiveInfo", export.GeneratedUtc?.ToLocalTime().ToString("d MMMM yyyy") ?? "")
				: Loc.T("controls.gameKeysDefaultInfo")
		});

		foreach (GameKeyBinding binding in export.Bindings)
			section.Entries.Add(new KbEntry
			{
				Key = FriendlyCombo(binding),
				// Every action on the key, because one key routinely does several things on different screens.
				Text = binding.Actions.Count > 0 ? string.Join(", ", binding.Actions) : Loc.T("controls.gameKeysUnnamed")
			});

		mod.Sections.Add(section);
		return mod;
	}

	/// <summary>The short hardcoded vanilla list, for a game with no keybind export. <c>null</c> when there
	/// is no list for the active game either.</summary>
	private ModKeybinds? BuildHardcodedGameControls()
	{
		List<string> lines = BaseGameControlLines(_settings.ActiveGame);
		if (lines.Count == 0) return null;

		var mod = new ModKeybinds("Base Game Controls (Vanilla Defaults)");
		var section = new KbSection();
		foreach (string line in lines) section.Entries.Add(MakeKbEntry(line));
		mod.Sections.Add(section);
		return mod;
	}

	/// <summary>
	/// A binding rendered for reading aloud: "Control+Alpha1" becomes "Control plus 1".
	///
	/// The export deals in Unity <c>KeyCode</c> names, which are identifiers rather than labels — <c>Alpha1</c>
	/// is the 1 key, <c>UpArrow</c> is the up arrow — and reading those out verbatim would be a small puzzle
	/// every time. The <c>+</c> is left in for <see cref="KeyToSpeech"/> to turn into "plus".
	/// </summary>
	private static string FriendlyCombo(GameKeyBinding binding) =>
		binding.Modifiers.Length > 0
			? binding.Modifiers + "+" + FriendlyKeyName(binding.Key)
			: FriendlyKeyName(binding.Key);

	/// <summary>One <c>KeyCode</c> name as a person would say it: "Alpha1" to "1", "PageDown" to "Page Down".</summary>
	private static string FriendlyKeyName(string key)
	{
		if (key.StartsWith("Alpha", StringComparison.Ordinal) && key.Length > 5 && key.Skip(5).All(char.IsDigit))
			return key.Substring(5);
		if (key.StartsWith("Keypad", StringComparison.Ordinal) && key.Length > 6)
			return "Keypad " + FriendlyKeyName(key.Substring(6));
		return HumanizePropertyName(key);
	}

	/// <summary>
	/// The keybinds a BepInEx plugin exposes through its config file.
	///
	/// BepInEx writes each setting as the author's description (<c>##</c> lines), then generated metadata
	/// (<c>#</c> lines: the setting's type, its default, and for an enum every value it will accept), then
	/// <c>Name = Value</c>. For a key, that accepted-values line is the whole Unity KeyCode enum — several
	/// hundred names on a single line. Only the type is read from the metadata and everything else is dropped,
	/// so an entry is the key the bind is currently set to and what it does, and nothing else.
	///
	/// The type is what identifies a keybind, not the section it sits in: a <c>[Keys]</c> section is a
	/// convention some plugins follow and others don't, and keys turn up alongside ordinary settings.
	///
	/// Returned as one flat section so the keys sit directly under the plugin in the drill-down. A plugin
	/// usually has a handful, and making the user open a category to reach three keys is a step for nothing.
	/// </summary>
	private static List<KbSection> ParseBepInExKeybinds(string configPath)
	{
		var section = new KbSection();

		foreach (BepInExSetting setting in BepInExConfigSchema.Read(configPath).Where(s => s.IsKey))
		{
			section.Entries.Add(new KbEntry
			{
				Key = setting.Value.Length > 0 ? setting.Value : "None",
				// The author's first sentence says what the key does; the rest is usually detail about why,
				// which belongs in the mod documentation viewer rather than in a list of controls.
				Text = FirstSentence(setting.Description) ?? HumanizePropertyName(setting.Key)
			});
		}

		return section.Entries.Count > 0 ? new List<KbSection> { section } : new List<KbSection>();
	}

	/// <summary>The first sentence of a setting's description, or null when it has none.</summary>
	private static string? FirstSentence(string description)
	{
		string text = description.Trim();
		if (text.Length == 0) return null;

		int stop = text.IndexOf(". ", StringComparison.Ordinal);
		if (stop > 0) text = text.Substring(0, stop);
		return text.TrimEnd('.').Trim() is { Length: > 0 } trimmed ? trimmed : null;
	}

	/// <summary>
	/// Reads keybinds a mod exposes through MCM Helper (used by Skyrim and Fallout 4 mods such as Fallout 4 Access
	/// and XDI) into structured sections — the same shape README-sourced controls use, so they fold into the same
	/// drill-down. Each <c>MCM\Config\&lt;mod&gt;\config.json</c> lists <c>keyinput</c> controls grouped by
	/// <c>section</c> headings; the actual key for each is read from the INI (the user's in-game remap in
	/// <c>&lt;game&gt;\Data\MCM\Settings\&lt;mod&gt;.ini</c> if present, otherwise the mod's shipped default
	/// <c>settings.ini</c>) and decoded from its "virtual-key,modifier" pair into a readable combo. Read-only:
	/// the keys reflect exactly what MCM has, so they stay correct even after the user remaps in-game.
	/// </summary>
	private List<KbSection> ParseMcmKeybinds(string modDir)
	{
		var sections = new List<KbSection>();
		string mcmConfigRoot = Path.Combine(modDir, "MCM", "Config");
		if (!Directory.Exists(mcmConfigRoot)) return sections;

		foreach (string configDir in Directory.GetDirectories(mcmConfigRoot))
		{
			string configPath = Path.Combine(configDir, "config.json");
			if (!File.Exists(configPath)) continue;
			try
			{
				JObject root = JObject.Parse(File.ReadAllText(configPath));
				string modName = root.Value<string>("modName") ?? Path.GetFileName(configDir);

				// Key values live in the INI; defaults load first, then the user's override wins.
				var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				LoadIniInto(Path.Combine(configDir, "settings.ini"), values);
				if (!string.IsNullOrEmpty(_settings.CurrentGamePath))
					LoadIniInto(Path.Combine(_settings.CurrentGamePath, "Data", "MCM", "Settings", modName + ".ini"), values);

				// Controls live under pages[].content[] (Fallout 4 Access) or a top-level content[] (XDI).
				var contentArrays = new List<JArray>();
				if (root["pages"] is JArray pages)
					foreach (JToken page in pages)
						if (page["content"] is JArray pc) contentArrays.Add(pc);
				if (root["content"] is JArray topContent) contentArrays.Add(topContent);

				KbSection? current = null;
				void Push()
				{
					if (current != null && current.Entries.Count > 0) sections.Add(current);
				}

				foreach (JArray content in contentArrays)
				{
					foreach (JToken item in content)
					{
						string type = item.Value<string>("type") ?? "";
						if (type == "section")
						{
							Push();
							current = new KbSection { Name = item.Value<string>("text")?.Trim() ?? "" };
						}
						else if (type == "keyinput")
						{
							string label = item.Value<string>("text")?.Trim() ?? "";
							if (label.Length == 0) continue;
							current ??= new KbSection();
							current.Entries.Add(new KbEntry { Key = ResolveMcmKey(item.Value<string>("id") ?? "", values), Text = label });
						}
					}
				}
				Push();
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Controls", "reading the key bindings out of an MCM config", ex); }
		}
		return sections;
	}

	/// <summary>Loads an MCM INI into <paramref name="values"/> keyed by "section|name" (case-insensitive), so a
	/// later call (the user override) overwrites the defaults for the same setting. Comments and blanks ignored.</summary>
	private static void LoadIniInto(string path, Dictionary<string, string> values)
	{
		if (!File.Exists(path)) return;
		try
		{
			string section = "";
			foreach (string raw in File.ReadAllLines(path))
			{
				string line = raw.Trim();
				if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
				if (line.StartsWith("[") && line.EndsWith("]"))
				{
					section = line.Substring(1, line.Length - 2).Trim();
					continue;
				}
				int eq = line.IndexOf('=');
				if (eq <= 0) continue;
				values[section + "|" + line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Settings", $"reading the INI file {path}", ex); }
	}

	/// <summary>Resolves an MCM keyinput's "settingName:iniSection" id to a readable key combo by looking the
	/// "virtual-key,modifier" pair up in the loaded INI values. Returns "Unassigned" when no key is bound.</summary>
	private static string ResolveMcmKey(string id, Dictionary<string, string> values)
	{
		int colon = id.IndexOf(':');
		string settingName = colon >= 0 ? id.Substring(0, colon) : id;
		string iniSection = colon >= 0 ? id.Substring(colon + 1) : "";
		if (!values.TryGetValue(iniSection + "|" + settingName, out string? raw) || string.IsNullOrWhiteSpace(raw))
			return "Unassigned";

		string[] parts = raw.Split(',');
		if (!int.TryParse(parts[0].Trim(), out int vk) || vk <= 0) return "Unassigned";
		int mods = parts.Length > 1 && int.TryParse(parts[1].Trim(), out int m) ? m : 0;
		return DecodeVirtualKey(vk, mods);
	}

	/// <summary>Decodes a Windows virtual-key code plus an MCM modifier bitfield (1=shift, 2=ctrl, 4=alt) into a
	/// readable combo like "Ctrl + Shift + Page Down".</summary>
	private static string DecodeVirtualKey(int vk, int modifiers)
	{
		var parts = new List<string>();
		if ((modifiers & 2) != 0) parts.Add("Ctrl");
		if ((modifiers & 1) != 0) parts.Add("Shift");
		if ((modifiers & 4) != 0) parts.Add("Alt");
		parts.Add(VirtualKeyName(vk));
		return string.Join(" + ", parts);
	}

	/// <summary>Maps a Windows virtual-key code to a readable key name, covering the keys mods actually bind.</summary>
	private static string VirtualKeyName(int vk)
	{
		if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();          // A-Z
		if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();          // 0-9 (top row)
		if (vk >= 0x60 && vk <= 0x69) return "Numpad " + (vk - 0x60);        // Numpad 0-9
		if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x6F);              // F1-F24

		return vk switch
		{
			0x01 => "Left Mouse Button",
			0x02 => "Right Mouse Button",
			0x04 => "Middle Mouse Button",
			0x05 => "Mouse Button 4",
			0x06 => "Mouse Button 5",
			0x08 => "Backspace",
			0x09 => "Tab",
			0x0D => "Enter",
			0x10 => "Shift",
			0x11 => "Ctrl",
			0x12 => "Alt",
			0x13 => "Pause",
			0x14 => "Caps Lock",
			0x1B => "Escape",
			0x20 => "Space",
			0x21 => "Page Up",
			0x22 => "Page Down",
			0x23 => "End",
			0x24 => "Home",
			0x25 => "Left Arrow",
			0x26 => "Up Arrow",
			0x27 => "Right Arrow",
			0x28 => "Down Arrow",
			0x2C => "Print Screen",
			0x2D => "Insert",
			0x2E => "Delete",
			0x6A => "Numpad Multiply",
			0x6B => "Numpad Plus",
			0x6D => "Numpad Minus",
			0x6E => "Numpad Decimal",
			0x6F => "Numpad Divide",
			0x90 => "Num Lock",
			0x91 => "Scroll Lock",
			0xBA => ";",
			0xBB => "=",
			0xBC => ",",
			0xBD => "-",
			0xBE => ".",
			0xBF => "/",
			0xC0 => "`",
			0xDB => "[",
			0xDC => "\\",
			0xDD => "]",
			0xDE => "'",
			_ => "Key " + vk
		};
	}

	/// <summary>Builds the drill-down forest for the active game: one root node per control source.</summary>
	private List<NavNode> BuildNavForest()
	{
		var roots = new List<NavNode>();

		// The Witcher 3's own controls are built here rather than as an ordinary source, because they are the
		// one game's list that needs to be deeper than a source can describe: two ways in, then situations, then
		// keys, then what a key does. Its mods still come through BuildControlSources like everyone else's.
		List<Witcher3Binding> witcher = ReadWitcher3Bindings();
		if (witcher.Count > 0) roots.Add(BuildWitcher3GameNode(witcher));

		foreach (ModKeybinds mod in BuildControlSources(witcher))
			roots.Add(BuildNavNode(mod));

		return roots;
	}

	/// <summary>
	/// The active game's bindings straight from The Witcher 3's <c>input.settings</c>, or an empty list for any
	/// other game. This game needs no export plugin: it writes every binding, including the player's own
	/// remappings, to a plain file in their documents folder.
	/// </summary>
	private List<Witcher3Binding> ReadWitcher3Bindings()
	{
		GameProfile? profile = GameProfiles.Find(_settings.ActiveGame);
		if (profile == null || !profile.IsWitcher3) return new List<Witcher3Binding>();

		return Witcher3InputSettings.ReadDetailed(
			Witcher3InputSettings.PathFor(profile, _settings.CurrentGamePath),
			Path.Combine(_settings.CurrentGamePath, "mods"));
	}

	/// <summary>
	/// The Witcher 3's own controls as a drill-down: the same bindings offered "By situation" and "By key",
	/// because those are two different questions and this game answers neither one well flat.
	///
	/// A key that does one thing in a situation is a line to be read. A key that does seventy-five — which the
	/// interact key genuinely does, the game having bound every interaction verb it owns to it — becomes a group
	/// to open, so the list stays one row per key and the seventy-five are there for whoever wants them.
	/// </summary>
	private NavNode BuildWitcher3GameNode(List<Witcher3Binding> bindings)
	{
		var owner = new ModKeybinds(Loc.T("controls.gameKeysLive"));
		var root = new NavNode(owner.Name, owner);

		DateTime? written = null;
		try
		{
			GameProfile? profile = GameProfiles.Find(_settings.ActiveGame);
			string path = profile == null ? "" : Witcher3InputSettings.PathFor(profile, _settings.CurrentGamePath);
			if (path.Length > 0 && File.Exists(path)) written = File.GetLastWriteTime(path);
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Controls", "reading The Witcher 3's own key bindings", ex); }

		// Which bindings these are, said before them: the game rewrites this file whenever a key is remapped, so
		// its date is honestly "when these bindings were last changed".
		root.Children.Add(new NavNode(
			Loc.T("controls.gameKeysLiveInfo", written?.ToString("d MMMM yyyy") ?? ""), owner) { IsInfo = true });

		List<Witcher3Binding> gameOwn = bindings.Where(b => b.ModName.Length == 0).ToList();

		var bySituation = new NavNode(Loc.T("controls.w3BySituation"), owner);
		foreach (Witcher3Situation situation in Witcher3Controls.BySituation(gameOwn))
		{
			var node = new NavNode(Loc.T("controls.w3SituationGroup", situation.Name, situation.Count), owner);
			foreach (Witcher3KeyControls key in situation.Keys)
				node.Children.Add(KeyNode(key, owner, withSituations: false));
			bySituation.Children.Add(node);
		}

		var byKey = new NavNode(Loc.T("controls.w3ByKey"), owner);
		foreach (Witcher3KeyControls key in Witcher3Controls.ByKey(gameOwn))
			byKey.Children.Add(KeyNode(key, owner, withSituations: true));

		if (bySituation.Children.Count > 0) root.Children.Add(bySituation);
		if (byKey.Children.Count > 0) root.Children.Add(byKey);
		return root;
	}

	/// <summary>
	/// One key in the Witcher 3 list: a line when it does a single thing, a group when it does several.
	///
	/// <paramref name="withSituations"/> is what tells the two views apart. Inside a situation the situation is
	/// already known — repeating it on every line would be the same three words over and over — while in the
	/// by-key view it is the entire reason the key has more than one entry.
	/// </summary>
	private static NavNode KeyNode(Witcher3KeyControls key, ModKeybinds owner, bool withSituations)
	{
		string ActionText(Witcher3Binding b) => withSituations
			? Loc.T("controls.w3ActionInSituation", b.Action, Witcher3Controls.SituationFor(b.Context))
			: b.Action;

		if (key.Bindings.Count == 1)
			return new NavNode(Loc.T("controls.w3KeyRow", KeyToSpeech(key.Key), ActionText(key.Bindings[0])), owner);

		var group = new NavNode(Loc.T("controls.w3KeyGroup", KeyToSpeech(key.Key), key.Bindings.Count), owner);
		foreach (Witcher3Binding b in key.Bindings)
			group.Children.Add(new NavNode(ActionText(b), owner));
		return group;
	}

	/// <summary>
	/// A Witcher 3 mod's own bindings as an ordinary control source, so they sit beside the mod's other
	/// documentation rather than inside the game's list.
	///
	/// Splitting them out is the point. A mod's actions are declared in the same file as the game's and land on
	/// the same keys, so a single list had "Home: Toggle Hud, Announce, Keys First, Hist First, Map Announce" —
	/// five unrelated things on one line, from two different programs, and no way to tell which was whose.
	/// </summary>
	private static ModKeybinds BuildWitcher3ModSource(List<Witcher3Binding> bindings, string modName)
	{
		var mod = new ModKeybinds(modName);
		var section = new KbSection();

		foreach (Witcher3Binding b in Witcher3Controls.ForMod(bindings, modName))
			section.Entries.Add(new KbEntry { Key = b.Key, Text = b.Action });

		mod.Sections.Add(section);
		return mod;
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
			// Searched right through the docs folder, not just its top level. Stardew Access keeps its keybinding
			// page at docs\compiled-docs\keybindings.html, so looking only in docs\ found nothing and the mod was
			// left with the bare property names from its config file instead of the author's own descriptions.
			foreach (string htmlName in new[] { "keybinds.html", "keybindings.html", "controls.html" })
			{
				string? htmlFile = null;
				try { htmlFile = Directory.EnumerateFiles(docsPath, htmlName, SearchOption.AllDirectories).FirstOrDefault(); }
				catch (Exception ex) { DiagnosticLog.WriteException("Controls", $"looking for {htmlName} under {docsPath}", ex); }
				if (htmlFile != null) { keys.AddRange(ParseKeybindsHtml(htmlFile)); break; }
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
			catch (Exception ex) { DiagnosticLog.WriteException("Controls", $"reading the key bindings out of {cfg}", ex); }
		}
		return keys;
	}

	/// <summary>
	/// The hardcoded vanilla base-game controls (the only non-mod-sourced list), per active game.
	///
	/// A game with no list of its own returns nothing, and the caller then shows no base-game entry at all.
	/// This used to end in a fallback to Stardew Valley's keys, so Moonlight Peaks — added long after this was
	/// written — presented Stardew's controls as its own. A player who cannot see the screen has no way to tell
	/// that the keys being read out belong to a different game, so silence is the only safe default.
	/// </summary>
	private static List<string> BaseGameControlLines(string activeGame)
	{
		if (GameProfiles.IsGame(activeGame, GameProfiles.SkyrimSE))
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
		// The Witcher 3 normally answers this question properly: it writes every binding into input.settings,
		// which the controls list reads instead of this. These stock defaults are the stand-in for a copy that
		// has never been launched, so that file doesn't exist yet.
		if (GameProfiles.IsGame(activeGame, GameProfiles.Witcher3))
			return new List<string>
			{
				"W A S D: Move Geralt forward, left, backward, right",
				"Left Shift: Sprint (hold); gallop while riding",
				"Left Mouse Button: Fast attack",
				"Right Mouse Button: Strong attack; hold to focus and guard",
				"E: Interact — talk, loot, open doors, mount Roach",
				"Q: Cast the selected sign (hold to choose one)",
				"Tab: Radial menu — signs, bombs, potions and oils",
				"Space: Dodge; double-tap a direction to roll",
				"1 and 2: Draw steel sword / silver sword",
				"R F T Y: Drink the potion in each quick slot",
				"I J K L: Inventory, Journal, Character, Alchemy",
				"M: Map, B: Bestiary, G: Glossary",
				"V: Highlight the tracked objective (Witcher Senses)",
				"C: Sheathe weapon",
				"F5 and F8: Quick-save / Quick-load game",
			};
		if (GameProfiles.IsGame(activeGame, GameProfiles.Fallout4))
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
		if (GameProfiles.IsGame(activeGame, GameProfiles.StardewValley))
			return new List<string>
			{
				"W A S D: Move character up, left, down, right",
				"Arrow Keys: Navigate through game menus",
				"C or Right Click: Primary interact / action",
				"X or Right Click: Secondary interact / use tool",
				"1 to 0: Select active item in hotbar",
				"Escape or E: Open / close game menu",
			};
		return new List<string>();
	}

	/// <summary>
	/// Opens a modal text editor with JSON syntax validation for editing one of the selected mod's files.
	/// <paramref name="fileLabel"/> names the file in the window title and spoken prompts (for example
	/// "Configuration" or "Manifest") and defaults to "Configuration" for existing callers.
	/// </summary>
	/// <param name="validateJson">
	/// Whether the text must parse as JSON before it can be saved. True for a mod's config.json or its manifest,
	/// where a syntax error stops the mod loading. False for a file that is simply not JSON — a BepInEx .cfg, which
	/// is what a Moonlight Peaks mod's settings live in — where the check would refuse to save a perfectly good file.
	/// </param>
	private void OpenConfigEditor(string modName, string configPath, Action onSaveSuccess, string fileLabel = "Configuration",
		bool validateJson = true)
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

		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("config.editorTitle", fileLabel, modName), (container, closeView) =>
		{
		bool saved = false;

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
			// Names what the control is, not what the view is called. It used to be "JSON Editor for <mod>", which
			// made the mod's name the third thing said in a row on the way in. A focusable control still needs a
			// real name — see the About/Donate text boxes, which borrowed "Search" from behind the view when left
			// without one — so this is a name, just not an echo of the heading.
			AccessibleName = Loc.T(validateJson ? "config.jsonEditorName" : "config.textEditorName")
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
		container.Controls.Add(mainLayout);

		Action saveAction = delegate
		{
			string editedText = tbJson.Text;
			if (validateJson)
			{
				try
				{
					// Validate JSON formatting
					JsonConvert.DeserializeObject<JToken>(editedText);
				}
				catch (Exception ex)
				{
					Speak(Loc.T("config.invalidJsonSpeak", ex.Message));
					SpeakBox(Loc.T("config.invalidJsonBox", ex.Message), Loc.T("config.jsonValidationTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}
			}

			try
			{
				File.WriteAllText(configPath, editedText);
				Speak(Loc.T("config.saved", fileLabel));
				onSaveSuccess?.Invoke();
				saved = true;
				closeView();
			}
			catch (Exception ex)
			{
				Speak(Loc.T("config.saveFailed"));
				SpeakBox(Loc.T("config.saveFailedBox", FriendlyError(ex)), Loc.T("common.error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		};

		// Leaving with unsaved edits asks first, and answering No has to KEEP the editor open. A view's own
		// Escape can only close, so Escape is claimed here instead: marking the key handled makes the view's
		// generic Escape stand down (see AttachEscape), leaving this in charge of whether it closes at all.
		void TryClose()
		{
			if (!saved && tbJson.Text != originalJson)
			{
				if (SpeakBox(Loc.T("config.discardConfirm"), Loc.T("config.confirmCancelTitle"),
						MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
					return;
				Speak(Loc.T("common.changesCancelled"));
			}
			closeView();
		}

		btnSave.Click += delegate { saveAction(); };
		btnCancel.Click += delegate { TryClose(); };

		// The form used to catch these for the whole window via KeyPreview; without a form of its own they are
		// wired to the controls that can hold focus.
		void WireEditorKeys(Control c)
		{
			c.KeyDown += delegate (object? s, KeyEventArgs pe)
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
					TryClose();
				}
			};
		}
		WireEditorKeys(tbJson);
		WireEditorKeys(btnSave);
		WireEditorKeys(btnCancel);

		return tbJson;
		},
		// Through the hint, not a Speak in here: anything spoken while the view is being built lands BEFORE the
		// title, so this said "Editing configuration for <mod>", then heard the title say the same thing again,
		// then the editor's own name say it a third time. The title names the view; this adds only the keys.
		hint: Loc.T("config.hint"));
	}
}
