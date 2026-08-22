using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="DocOutline"/> — the outline the F1 manual, F2 change log and F3 mod-documentation viewers
/// navigate, and the phrase search over it.
///
/// The load-bearing claim here is that a search result and the viewer count lines the same way. A result says
/// "line 12 of this section" and the viewer puts the caret on line 12 of the content pane; if the two ever
/// disagree, Enter on a result lands somewhere else in the section and nothing about it looks broken from the
/// outside — the caret simply sits on the wrong sentence. <see cref="DocOutline.ContentText"/> is the single
/// definition both sides use, and <see cref="AResultsLineIndexAddressesTheSameLineTheContentPaneShows"/> is what
/// holds them together.
///
/// The other reason line endings appear in so many of these: parsing Markdown leaves bare line feeds behind, and
/// a multiline WinForms TextBox only breaks a line on a carriage-return + line-feed pair. Before this, a manual
/// section arrived in the pane as one unbroken line — which a sighted reader sees as odd wrapping and a screen
/// reader user experiences as a section with no lines in it at all.
/// </summary>
public class DocOutlineTests
{
	/// <summary>A miniature of the real manual: one title, sections, sub-sections, and a link to follow.</summary>
	private static string[] SampleDoc() => new[]
	{
		"# Kinetix Mod Manager Manual",
		"The manual for the manager.",
		"",
		"## Launching the Game",
		"Press F5 to launch.",
		"",
		"### Script Extender Check",
		"The installed F4SE must match your game's version.",
		"See https://f4se.silverlock.org for downloads.",
		"",
		"## Installing Mods",
		"Drop an archive on the window.",
		"F4SE is installed from the suite instead."
	};

	private static List<DocNode> ParsedSample()
	{
		List<DocNode> roots = DocOutline.NormalizeRoots(DocOutline.ParseTree(SampleDoc()), "Introduction");
		DocOutline.CollapseRedundantLevels(roots);
		return roots;
	}

	// ---------------------------------------------------------------------
	// The outline
	// ---------------------------------------------------------------------

	[Fact]
	public void TheOneTitleIsUnwrappedSoTheListDoesNotOpenOnASingleItem()
	{
		List<DocNode> roots = ParsedSample();

		Assert.Equal(new[] { "Introduction", "Launching the Game", "Installing Mods" }, roots.Select(n => n.Label));
	}

	/// <summary>The title's own text would be lost by unwrapping it, so it becomes the leading entry instead.</summary>
	[Fact]
	public void TheTitlesOwnTextSurvivesAsTheIntroduction()
	{
		DocNode intro = ParsedSample()[0];

		Assert.Equal("Introduction", intro.Label);
		Assert.Contains("The manual for the manager.", intro.Content);
	}

	[Fact]
	public void DeeperHeadingsBecomeChildrenOfTheHeadingAboveThem()
	{
		DocNode launching = ParsedSample().Single(n => n.Label == "Launching the Game");

		Assert.Equal(new[] { "Script Extender Check" }, launching.Children.Select(c => c.Label));
		Assert.Contains("Press F5 to launch.", launching.Content);
	}

	/// <summary>A document with no single title (the change log, whose every version is a top-level heading) is
	/// left exactly as parsed.</summary>
	[Fact]
	public void ADocumentWithSeveralTopLevelHeadingsIsLeftAlone()
	{
		var lines = new[] { "# Version 1.5.1", "Fixes.", "# Version 1.5.0", "Two new games." };

		List<DocNode> roots = DocOutline.NormalizeRoots(DocOutline.ParseTree(lines), "Introduction");

		Assert.Equal(new[] { "Version 1.5.1", "Version 1.5.0" }, roots.Select(n => n.Label));
	}

	/// <summary>A wrapper heading with one child and nothing to say is a needless extra keypress on the way in.</summary>
	[Fact]
	public void ASingleChildWrapperWithNoTextOfItsOwnIsRemoved()
	{
		var lines = new[] { "# Version 1.5.0", "## New in Version 1.5.0", "### Games", "Two of them.", "### Fixes", "Several." };

		List<DocNode> roots = DocOutline.ParseTree(lines);
		DocOutline.CollapseRedundantLevels(roots);

		Assert.Equal(new[] { "Games", "Fixes" }, roots.Single().Children.Select(c => c.Label));
	}

	// ---------------------------------------------------------------------
	// Line endings — what the content pane can actually show
	// ---------------------------------------------------------------------

	/// <summary>Parsing joins content with bare line feeds; a multiline TextBox breaks only on CRLF.</summary>
	[Fact]
	public void ContentIsHandedOverWithLineBreaksATextBoxWillHonour()
	{
		DocNode node = ParsedSample().Single(n => n.Label == "Installing Mods");

		Assert.Contains("\r\n", DocOutline.ContentText(node));
		Assert.DoesNotContain("\n", DocOutline.ContentText(node).Replace("\r\n", ""));
	}

	[Fact]
	public void AlreadyNormalisedContentIsNotDoubledUp()
	{
		var node = new DocNode("X") { Content = "first\r\nsecond" };

		Assert.Equal("first\r\nsecond", DocOutline.ContentText(node));
		Assert.Equal(new[] { "first", "second" }, DocOutline.ContentLines(node));
	}

	[Fact]
	public void AnEmptyOrMissingNodeHasNothingToShow()
	{
		Assert.Equal("", DocOutline.ContentText(null));
		Assert.Equal("", DocOutline.ContentText(new DocNode("Empty")));
	}

	// ---------------------------------------------------------------------
	// Searching
	// ---------------------------------------------------------------------

	/// <summary>A phrase in two different sections is two results, each naming where it was found. The section is
	/// the breadcrumb the viewer would show, so the unwrapped title is not part of it.</summary>
	[Fact]
	public void EveryLineHoldingThePhraseIsFoundWhicheverSectionItIsIn()
	{
		List<DocMatch> hits = DocOutline.Find(ParsedSample(), "installed");

		Assert.Equal(
			new[] { "Launching the Game, Script Extender Check", "Installing Mods" },
			hits.Select(h => h.Section));
	}

	/// <summary>Nobody searching a manual is trying to tell "F4SE" from "f4se".</summary>
	[Fact]
	public void MatchingIgnoresCase()
	{
		int upper = DocOutline.Find(ParsedSample(), "F4SE").Count;

		Assert.True(upper > 0);
		Assert.Equal(upper, DocOutline.Find(ParsedSample(), "f4se").Count);
		Assert.Equal(upper, DocOutline.Find(ParsedSample(), "F4se").Count);
	}

	/// <summary>Results are stepped through with F3, so their order has to be the order the document reads:
	/// a section's own text, then the sections nested inside it, then the next section along.</summary>
	[Fact]
	public void ResultsComeBackInTheOrderTheDocumentReads()
	{
		var lines = new[]
		{
			"# Doc", "## First", "hit one", "### Nested", "hit two", "## Second", "hit three"
		};
		List<DocNode> roots = DocOutline.ParseTree(lines);

		List<DocMatch> hits = DocOutline.Find(roots, "hit");

		Assert.Equal(new[] { "hit one", "hit two", "hit three" }, hits.Select(h => h.Line));
	}

	/// <summary>
	/// The claim the whole feature rests on: the line index a result carries addresses the same line the content
	/// pane will be showing. The pane is filled from <see cref="DocOutline.ContentText"/> and the caret is placed
	/// by line number, so indexing anything else — the raw content, say — would land the caret on the wrong line.
	/// </summary>
	[Fact]
	public void AResultsLineIndexAddressesTheSameLineTheContentPaneShows()
	{
		DocMatch hit = DocOutline.Find(ParsedSample(), "silverlock").Single();

		string[] paneLines = DocOutline.ContentLines(hit.Node);

		Assert.Equal(hit.Line, paneLines[hit.LineIndex].Trim());
	}

	/// <summary>
	/// The other half of landing on the right line: the character offset the caret is set to must be the start of
	/// the very text the result promised. This is counted from the text rather than asked of the text box because
	/// a wrapped box numbers the lines it draws, not the lines it was handed.
	/// </summary>
	[Fact]
	public void TheCaretOffsetForAResultLandsExactlyOnItsLine()
	{
		foreach (DocMatch hit in DocOutline.Find(ParsedSample(), "F4SE"))
		{
			string text = DocOutline.ContentText(hit.Node);
			int offset = DocOutline.CharOffsetOfLine(hit.Node, hit.LineIndex);

			Assert.Equal(hit.Line, text.Substring(offset, hit.Line.Length));
		}
	}

	[Fact]
	public void TheFirstLineStartsAtTheVeryBeginning()
	{
		var node = new DocNode("X") { Content = "first\nsecond\nthird" };

		Assert.Equal(0, DocOutline.CharOffsetOfLine(node, 0));
		Assert.Equal(7, DocOutline.CharOffsetOfLine(node, 1));    // "first" + CRLF
		Assert.Equal(15, DocOutline.CharOffsetOfLine(node, 2));   // + "second" + CRLF
	}

	/// <summary>A line index past the end must not run off the text; it stops at the end instead.</summary>
	[Fact]
	public void AnOutOfRangeLineStopsAtTheEndOfTheText()
	{
		var node = new DocNode("X") { Content = "first\nsecond" };

		Assert.InRange(DocOutline.CharOffsetOfLine(node, 99), 0, DocOutline.ContentText(node).Length);
	}

	/// <summary>The path is what the viewer walks back down to reopen the level holding the match.</summary>
	[Fact]
	public void AResultCarriesTheWayBackToTheSectionHoldingIt()
	{
		DocMatch hit = DocOutline.Find(ParsedSample(), "silverlock").Single();

		Assert.Equal(
			new[] { "Launching the Game", "Script Extender Check" },
			hit.Path.Select(n => n.Label));
		Assert.Equal("Script Extender Check", hit.Node.Label);
	}

	[Fact]
	public void APhraseThatIsNotThereFindsNothingRatherThanEverything()
	{
		Assert.Empty(DocOutline.Find(ParsedSample(), "Morrowind"));
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void AnEmptySearchIsNotASearchForEveryLine(string phrase) =>
		Assert.Empty(DocOutline.Find(ParsedSample(), phrase));

	// ---------------------------------------------------------------------
	// Snippets
	// ---------------------------------------------------------------------

	[Fact]
	public void AShortLineIsItsOwnSnippet()
	{
		Assert.Equal("Press F5 to launch.", DocOutline.Snippet("  Press F5 to launch.  ", "F5"));
	}

	/// <summary>A results row is listened to, and a screen reader reads Markdown punctuation aloud.</summary>
	[Theory]
	[InlineData("*   **F5**: Launch the active game.", "F5: Launch the active game.")]
	[InlineData("-   A plain bullet.", "A plain bullet.")]
	[InlineData("1.  A numbered step.", "A numbered step.")]
	[InlineData("### A heading used as text", "A heading used as text")]
	[InlineData("See the [F4SE site](https://f4se.silverlock.org) for builds.", "See the F4SE site for builds.")]
	[InlineData("Set `EnableUiSounds` to __false__.", "Set EnableUiSounds to false.")]
	public void AResultRowIsStrippedOfMarkdownPunctuation(string line, string expected) =>
		Assert.Equal(expected, DocOutline.Snippet(line, "x"));

	// ---------------------------------------------------------------------
	// Links kept for reading
	// ---------------------------------------------------------------------

	/// <summary>
	/// A document read line by line has no way to offer a link except by having the address on the line, because
	/// Enter opens whatever address the line holds. The mod-documentation viewer used to delete every address
	/// while tidying the Markdown for reading, which left its links unreachable — and accessibility mods do link
	/// to things worth reaching.
	/// </summary>
	[Fact]
	public void AWebAddressIsKeptBesideItsWordsSoTheLineCanBeOpened() =>
		Assert.Equal(
			"See the F4SE site (https://f4se.silverlock.org) for builds.",
			DocOutline.RewriteLinksForReading("See the [F4SE site](https://f4se.silverlock.org) for builds."));

	/// <summary>
	/// A link to another heading or a file in the same repository cannot be opened from here whatever we do with
	/// it, so reading its target aloud would be noise charged against nothing. Mod documentation written for
	/// GitHub is full of these, which is why keeping every address indiscriminately was not the answer.
	/// </summary>
	[Theory]
	[InlineData("Jump to [Installing](#installing) first.", "Jump to Installing first.")]
	[InlineData("The [key list](docs/keys.md) has more.", "The key list has more.")]
	[InlineData("Mail [the author](mailto:someone@example.com).", "Mail the author.")]
	public void AnAddressNothingCouldOpenIsStillDropped(string markdown, string expected) =>
		Assert.Equal(expected, DocOutline.RewriteLinksForReading(markdown));

	/// <summary>An auto-linked bare address would otherwise be read out twice in a row.</summary>
	[Fact]
	public void AnAddressThatIsAlsoItsOwnWordsIsSaidOnce() =>
		Assert.Equal(
			"https://f4se.silverlock.org",
			DocOutline.RewriteLinksForReading("[https://f4se.silverlock.org](https://f4se.silverlock.org)"));

	/// <summary>The address the rewrite leaves behind has to be the one Enter will find on that line.</summary>
	[Fact]
	public void TheKeptAddressIsRecoverableFromTheLine()
	{
		string line = DocOutline.RewriteLinksForReading("Get it from [Nexus](https://www.nexusmods.com/fallout4/mods/42147) today.");

		Match found = Regex.Match(line, @"https?://[^\s)\]]+");

		Assert.True(found.Success);
		Assert.Equal("https://www.nexusmods.com/fallout4/mods/42147", found.Value);
	}

	/// <summary>A results row is scanned, not followed — the link is still on the line it takes you to.</summary>
	[Fact]
	public void AKeptAddressIsLeftOutOfTheResultsRow() =>
		Assert.Equal(
			"See the F4SE site for builds.",
			DocOutline.Snippet("See the F4SE site (https://f4se.silverlock.org) for builds.", "F4SE"));

	/// <summary>Stripping is for the row only — the pane still shows the document as it is written.</summary>
	[Fact]
	public void TheLineItselfIsKeptExactlyAsTheDocumentWroteIt()
	{
		var node = new DocNode("X") { Content = "*   **F4SE** is the script extender." };

		DocMatch hit = DocOutline.Find(new[] { node }, "script extender").Single();

		Assert.Equal("*   **F4SE** is the script extender.", hit.Line);
		Assert.Equal("F4SE is the script extender.", hit.Snippet);
	}

	/// <summary>A manual's lines are whole paragraphs; a result row that reads one is not a row anyone can scan.</summary>
	[Fact]
	public void ALongLineIsCutDownToSomethingAResultsListCanRead()
	{
		string line = new string('a', 200) + " needle " + new string('b', 200);

		string snippet = DocOutline.Snippet(line, "needle");

		Assert.Contains("needle", snippet);
		Assert.True(snippet.Length < line.Length);
	}

	/// <summary>The point of the window is that the phrase is inside it, wherever in the line it fell.</summary>
	[Theory]
	[InlineData(0)]
	[InlineData(150)]
	[InlineData(390)]
	public void TheSnippetAlwaysContainsThePhraseItWasCutAround(int offset)
	{
		string line = new string('a', offset) + " needle " + new string('b', 400 - offset);

		Assert.Contains("needle", DocOutline.Snippet(line, "needle"));
	}

	/// <summary>Cutting mid-word would leave the row opening on a fragment of one.</summary>
	[Fact]
	public void ASnippetOpensOnAWholeWord()
	{
		string line = string.Join(" ", Enumerable.Repeat("elephant", 40)) + " needle";

		string snippet = DocOutline.Snippet(line, "needle");

		// Whatever the leading marker, the first real word must be a whole "elephant", not part of one.
		string firstWord = snippet.TrimStart('…', ' ').Split(' ')[0];
		Assert.Equal("elephant", firstWord);
	}

	// ---------------------------------------------------------------------
	// The shipped manual itself
	// ---------------------------------------------------------------------

	/// <summary>
	/// The real MANUAL.md, not a fixture: the outline is only worth anything if it works on the document it was
	/// built for. This catches a manual restructured into a shape the parser cannot navigate — every top-level
	/// section must be reachable and the search must find a phrase that is genuinely in there.
	/// </summary>
	[Fact]
	public void TheShippedManualParsesIntoANavigableOutlineAndCanBeSearched()
	{
		string path = Path.Combine(RepoRoot(), "MANUAL.md");
		Assert.True(File.Exists(path), "MANUAL.md not found at " + path);

		List<DocNode> roots = DocOutline.NormalizeRoots(DocOutline.ParseTree(File.ReadAllLines(path)), "Introduction");
		DocOutline.CollapseRedundantLevels(roots);

		Assert.True(roots.Count > 5, "the manual should have several top-level sections");
		Assert.All(roots, n => Assert.False(string.IsNullOrWhiteSpace(n.Label)));

		List<DocMatch> hits = DocOutline.Find(roots, "script extender");
		Assert.NotEmpty(hits);
		Assert.All(hits, h => Assert.Contains("script extender", h.Line, System.StringComparison.OrdinalIgnoreCase));
		// Every hit has to be addressable, or Enter on it goes nowhere.
		Assert.All(hits, h => Assert.InRange(h.LineIndex, 0, DocOutline.ContentLines(h.Node).Length - 1));
	}

	/// <summary>Walks up from the test binary to the repository root, where MANUAL.md lives.</summary>
	private static string RepoRoot()
	{
		var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MANUAL.md"))) dir = dir.Parent;
		return dir?.FullName ?? Directory.GetCurrentDirectory();
	}
}
