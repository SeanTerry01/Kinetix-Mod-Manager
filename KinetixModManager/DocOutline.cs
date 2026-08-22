using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KinetixModManager;

/// <summary>One heading in a document: its title, the body text directly beneath it (before any sub-heading),
/// and its sub-headings as children. Built into a tree by <see cref="DocOutline.ParseTree"/>.</summary>
public sealed class DocNode
{
	public string Label { get; }
	public string Content { get; set; } = "";
	public List<DocNode> Children { get; } = new();

	public DocNode(string label) { Label = label; }

	public override string ToString() => Label;
}

/// <summary>One line of a document that contains the phrase being searched for.</summary>
public sealed class DocMatch
{
	/// <summary>The nodes from the top level down to the one holding this line, so the viewer can walk back to
	/// it — the drill-down navigates by level, and a node alone does not say which levels lead there.</summary>
	public required IReadOnlyList<DocNode> Path { get; init; }

	/// <summary>The section this line is in, as the breadcrumb reads — "Launching the Game, Before It Starts".</summary>
	public required string Section { get; init; }

	/// <summary>Which line of the containing node's text this is, counted as the content pane counts lines.</summary>
	public required int LineIndex { get; init; }

	/// <summary>The whole line, as it appears in the content pane.</summary>
	public required string Line { get; init; }

	/// <summary>Enough of the line to recognise it by, centred on the phrase.</summary>
	public required string Snippet { get; init; }

	/// <summary>The node holding the line.</summary>
	public DocNode Node => Path[^1];
}

/// <summary>
/// Turning a Markdown document into the outline the manual (F1), change log (F2) and mod-documentation (F3)
/// viewers navigate, and finding a phrase anywhere inside it.
///
/// The viewer is a drill-down: a list of headings, each opening into its sub-headings, with the selected
/// heading's own text in a pane beside it. That is a good way to read a document whose shape you already know
/// and a poor way to answer "where does it say anything about F4SE?" — the table of contents can only offer the
/// headings someone thought to write, and everything else is reachable only by opening sections one at a time.
/// <see cref="Find"/> is the other half: it searches every line of every section at once and reports where each
/// hit lives, so the viewer can go straight there.
///
/// The line numbering matters more than it looks. A match says "line 12 of this section", and the viewer puts
/// the caret on line 12 of the pane — so both have to count lines the same way, which is what
/// <see cref="ContentText"/> exists to guarantee.
/// </summary>
public static class DocOutline
{
	/// <summary>
	/// Parses Markdown into a tree keyed by heading level (one '#' is a parent of '##', and so on). Each heading
	/// becomes a node; the lines beneath it, up to the next heading of any level, become that node's own content.
	/// Lines before the first heading are dropped (the manual and change log both open with a heading).
	/// </summary>
	public static List<DocNode> ParseTree(IEnumerable<string> lines)
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
	/// leading entry called <paramref name="introLabel"/> so nothing is lost.</summary>
	public static List<DocNode> NormalizeRoots(List<DocNode> roots, string introLabel)
	{
		if (roots.Count != 1 || roots[0].Children.Count == 0) return roots;

		DocNode title = roots[0];
		var result = new List<DocNode>();
		if (!string.IsNullOrWhiteSpace(title.Content))
			result.Add(new DocNode(introLabel) { Content = title.Content });
		result.AddRange(title.Children);
		return result;
	}

	/// <summary>Removes redundant single-child wrapper levels: when a node has exactly one child that carries no
	/// text of its own, that wrapper is dropped and its children are promoted up (e.g. a change log version whose
	/// only child is "New in Version X" then lists categories — the wrapper just adds a needless extra step).</summary>
	public static void CollapseRedundantLevels(List<DocNode> nodes)
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
	/// A node's text exactly as the content pane shows it.
	///
	/// Line endings are normalised to CRLF because a multiline TextBox only breaks a line on the pair — with a
	/// bare line feed, which is what parsing Markdown leaves behind, a whole section arrives as one enormous
	/// line with nothing to arrow through. Both the viewer and the search index go through here, so "line 12"
	/// means the same thing to each; anything that reformats the text on the way to the pane without coming
	/// through here would land a search result on the wrong line.
	/// </summary>
	public static string ContentText(DocNode? node) =>
		node == null ? "" : Regex.Replace(node.Content.Trim(), @"\r\n?|\n", "\r\n");

	/// <summary>The lines of a node's text, indexed the way the content pane indexes them.</summary>
	public static string[] ContentLines(DocNode? node) =>
		ContentText(node).Split(new[] { "\r\n" }, StringSplitOptions.None);

	/// <summary>
	/// Where line <paramref name="lineIndex"/> begins, counted in characters from the start of the node's text.
	///
	/// This is how a search result is turned into a caret position, and it is counted here rather than asked of
	/// the text box on purpose. A multiline TextBox wraps long lines, and its own
	/// <c>GetFirstCharIndexFromLine</c> counts the <em>wrapped</em> lines on screen — so with word wrap on, line
	/// 3 of a manual section is nothing like the box's line 3, and a result would put the caret an arbitrary
	/// distance from the text it promised. Counting characters is unaffected by how the text happens to be laid
	/// out, so the caret lands on the line the result named however wide the window is.
	/// </summary>
	public static int CharOffsetOfLine(DocNode? node, int lineIndex)
	{
		string text = ContentText(node);
		string[] lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);

		int offset = 0;
		for (int i = 0; i < lineIndex && i < lines.Length; i++)
			offset += lines[i].Length + 2;   // + the CRLF that ContentText guarantees between lines

		// The last line has no CRLF after it, so a line index past the end overshoots. Clamped, because the
		// answer is fed straight to a caret and a caret past the end of the text is not a position.
		return Math.Min(offset, text.Length);
	}

	/// <summary>
	/// Every line in the tree containing <paramref name="phrase"/>, in the order the document reads — a section's
	/// own text before the sections nested inside it. Matching ignores case, because nobody searching a manual is
	/// trying to distinguish "F4SE" from "f4se".
	/// </summary>
	public static List<DocMatch> Find(IReadOnlyList<DocNode> roots, string phrase)
	{
		var found = new List<DocMatch>();
		if (string.IsNullOrWhiteSpace(phrase)) return found;

		void Walk(IReadOnlyList<DocNode> nodes, List<DocNode> path, string crumb)
		{
			foreach (DocNode node in nodes)
			{
				path.Add(node);
				string here = crumb.Length == 0 ? node.Label : $"{crumb}, {node.Label}";

				string[] lines = ContentLines(node);
				for (int i = 0; i < lines.Length; i++)
				{
					if (lines[i].IndexOf(phrase, StringComparison.OrdinalIgnoreCase) < 0) continue;
					found.Add(new DocMatch
					{
						Path      = path.ToList(),   // a copy: path is mutated as the walk continues
						Section   = here,
						LineIndex = i,
						Line      = lines[i].Trim(),
						Snippet   = Snippet(lines[i], phrase)
					});
				}

				Walk(node.Children, path, here);
				path.RemoveAt(path.Count - 1);
			}
		}

		Walk(roots, new List<DocNode>(), "");
		return found;
	}

	/// <summary>
	/// Rewrites Markdown links so the address survives into the text a reader arrows through — but only where
	/// the address is one a browser could actually be sent to.
	///
	/// <c>[F4SE site](https://f4se.silverlock.org)</c> becomes <c>F4SE site (https://f4se.silverlock.org)</c>.
	/// The words alone read better, and that is what this used to produce; the trouble is that a document read
	/// line by line has no other way to offer a link at all. Pressing Enter on a line opens the address on it,
	/// and an address that was deleted on the way in is one nobody can follow.
	///
	/// A link pointing anywhere else — another heading (<c>#installing</c>), a file in the same repository
	/// (<c>docs/keys.md</c>), an e-mail address — keeps only its words, exactly as before. Those cannot be opened
	/// from here whatever we do with them, so reading their targets aloud would be noise charged against nothing.
	/// Mod documentation written for GitHub is full of them.
	/// </summary>
	public static string RewriteLinksForReading(string markdown)
	{
		return Regex.Replace(markdown, @"\[([^\]]+)\]\(([^)]*)\)", m =>
		{
			string text = m.Groups[1].Value.Trim();
			string url = m.Groups[2].Value.Trim();

			if (!Regex.IsMatch(url, @"^https?://", RegexOptions.IgnoreCase)) return text;
			// A link whose words are already the address (a bare URL that a tool has auto-linked) would
			// otherwise be read out twice in a row.
			if (string.Equals(text, url, StringComparison.OrdinalIgnoreCase)) return url;
			return text.Length == 0 ? url : $"{text} ({url})";
		});
	}

	/// <summary>
	/// Takes the Markdown punctuation off a line so it can be read out as a result.
	///
	/// A manual line looks like <c>*   **F5**: Launch the game.</c>, and a screen reader reads those asterisks —
	/// so a list of results arrives as "star star F five star star colon". The content pane still shows the line
	/// as written, because that is the document; this is only for the row that stands in for it in a list, where
	/// the markup is nothing but noise between the reader and the words they are looking for.
	/// </summary>
	public static string StripMarkupForReading(string line)
	{
		string s = line.Trim();
		s = Regex.Replace(s, @"^\s*(?:[*+-]|\d+\.)\s+", "");    // leading list marker
		s = Regex.Replace(s, @"^#{1,6}\s+", "");                // a heading used as body text
		s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]*\)", "$1");   // [text](url) -> text
		// An address kept for the text pane to offer (see RewriteLinksForReading) is not wanted in a results
		// row: the row exists to be scanned, and the link is still there on the line it takes you to.
		s = Regex.Replace(s, @"\s*\(https?://[^)\s]*\)", "");
		s = s.Replace("**", "").Replace("__", "").Replace("`", "");
		return Regex.Replace(s, @"\s{2,}", " ").Trim();
	}

	/// <summary>
	/// Enough of a matching line to tell it apart from the others, centred on the phrase.
	///
	/// A manual's lines are whole paragraphs, and a results list that reads a paragraph per row is not a list
	/// anyone can scan — especially by ear, where there is no skimming ahead. The whole line is one Enter away,
	/// and that is what Enter is for.
	/// </summary>
	public static string Snippet(string line, string phrase, int max = 140)
	{
		string s = StripMarkupForReading(line);
		if (s.Length <= max) return s;

		int at = s.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
		if (at < 0) at = 0;

		// Start a little before the phrase so it is heard in context, then back up to a word boundary so the
		// snippet doesn't open mid-word. The backing-up is bounded: a line can be one enormous unbroken token
		// (a URL, or a table rule), and walking to the start of that would push the phrase — the only reason
		// this row is in the list — back out of the window.
		int start = Math.Max(0, at - max / 3);
		int floor = Math.Max(0, start - 24);
		while (start > floor && !char.IsWhiteSpace(s[start - 1])) start--;

		int length = Math.Min(max, s.Length - start);
		string cut = s.Substring(start, length).Trim();
		if (start > 0) cut = "… " + cut;
		if (start + length < s.Length) cut += " …";
		return cut;
	}
}
