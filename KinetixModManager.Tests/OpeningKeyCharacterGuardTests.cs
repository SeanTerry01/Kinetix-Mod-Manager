using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// A shortcut that opens a prompt or a view does it inside its KeyDown, and the prompt or view pumps messages before
/// KeyDown returns, so the character the key makes is delivered to the control behind it. Ctrl+P typed a P into the mod
/// list, and closing Settings came back on the next mod starting with P. Both ways in must throw that character away
/// before they pump anything. Read from source, because the app can't be loaded here.
/// </summary>
public class OpeningKeyCharacterGuardTests
{
	private static string Source(string file)
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		return File.ReadAllText(Path.Combine(dir!.FullName, "KinetixModManager", file)).ReplaceLineEndings("\n");
	}

	[Theory]
	[InlineData("Form1.InlineView.cs", "private void ShowInlineView(")]
	[InlineData("Form1.InlinePrompt.cs", "private DialogResult ShowPrompt(")]
	public void TheOpeningKeysCharacterGoesBeforeAnythingPumps(string file, string method)
	{
		string source = Source(file);
		int start = source.IndexOf(method, StringComparison.Ordinal);
		Assert.True(start > 0, $"{method} not found in {file}");

		int drop = source.IndexOf("BeginOpeningOverlay();", start, StringComparison.Ordinal);
		int wait = source.IndexOf("WaitOutReaderReaction();", start, StringComparison.Ordinal);
		int overlay = source.IndexOf("RunOverlay(", start, StringComparison.Ordinal);
		Assert.True(drop > start && wait > drop && overlay > wait,
			$"{method} must call BeginOpeningOverlay before WaitOutReaderReaction and RunOverlay.");
	}

	[Fact]
	public void OpeningAnOverlayDropsTheCharacterAndCountsItself()
	{
		string source = Source("Form1.InlinePrompt.cs");
		int start = source.IndexOf("private void BeginOpeningOverlay()", StringComparison.Ordinal);
		Assert.True(start > 0);
		int end = source.IndexOf("\n\t}", start, StringComparison.Ordinal);
		string body = source.Substring(start, end - start);
		Assert.Contains("_overlaysStarted++;", body);
		Assert.Contains("DropCharacterOfOpeningKey();", body);
		Assert.DoesNotContain("BeginOpeningOverlay();", body);
	}
}
