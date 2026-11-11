using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// A letter only one row starts with, pressed on that row, said only "25 of 57": the list reported a selection change, NVDA
/// had nothing new to read, and the name was never heard (Sean, in Shortcut Customization, 2026-11-11). Every handler that
/// says a list position asks <c>TypedOntoTheSameRow</c>, before its wait, and the character is watched by a message filter
/// because the window's key preview is off while a view is up. The app can't be loaded here, so its source is read.
/// </summary>
public sealed class TypedLetterGuardTests
{
	private static string App(string file)
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		return File.ReadAllText(Path.Combine(dir!.FullName, "KinetixModManager", file)).ReplaceLineEndings("\n");
	}

	private static string Body(string file, string signature, int length)
	{
		string source = App(file);
		int at = source.IndexOf(signature, StringComparison.Ordinal);
		Assert.True(at >= 0, signature);
		return source.Substring(at, Math.Min(length, source.Length - at));
	}

	[Theory]
	[InlineData("Form1.Helpers.cs", "private async void List_SelectedIndexChanged(")]
	[InlineData("Form1.LoadOrder.cs", "private async void ListModPriority_SelectedIndexChanged(")]
	[InlineData("Form1.Wiki.cs", "private async void Wiki_SelectedIndexChanged(")]
	public void EveryPositionHandlerAsksBeforeItWaits(string file, string signature)
	{
		string body = Body(file, signature, 2500);
		int asked = body.IndexOf("TypedOntoTheSameRow(", StringComparison.Ordinal);
		int waited = body.IndexOf("await Task.Delay(", StringComparison.Ordinal);
		Assert.True(asked >= 0, "the handler never asks whether a letter landed on the same row");
		Assert.True(waited > asked, "asked after the wait, when the next key may already have cleared it");
	}

	[Fact]
	public void TheCharacterIsWatchedByAFilterNotTheKeyPreview()
	{
		Assert.Contains("Application.AddMessageFilter(new TypedIntoListWatcher(this));", App("Form1.UI.cs"));
		Assert.DoesNotContain("KeyPress += Form1_KeyPress", App("Form1.UI.cs"));
	}
}
