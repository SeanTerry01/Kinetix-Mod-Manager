using System.Text.RegularExpressions;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Startup with no game loaded opens on the game list, which gets focus as the window activates. If the list is free to
/// speak at that moment, "1 of 6" is the first thing heard, and it also ends the swallow holding the screen reader off.
/// Read from source, because the app can't be loaded here.
/// </summary>
public class StartupSpeechGuardTests
{
	private static string Form1()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		return File.ReadAllText(Path.Combine(dir!.FullName, "KinetixModManager", "Form1.cs")).ReplaceLineEndings("\n");
	}

	[Fact]
	public void ListsAreQuietFromBeforeTheWindowExists()
	{
		Assert.Contains("private bool _startupSpeechInProgress = true;", Form1());
	}

	/// <summary>
	/// The hold is for the position a list says by itself. An arrow pressed while the welcome was still being said, which
	/// takes about 8 seconds, gave "Minecraft" with no "2 of 6".
	/// </summary>
	[Fact]
	public void ListsSpeakForAUserWhoHasStartedMoving()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		string helpers = File.ReadAllText(Path.Combine(dir!.FullName, "KinetixModManager", "Form1.Helpers.cs")).ReplaceLineEndings("\n");

		Assert.Contains("private bool StartupHoldsTheLists => _startupSpeechInProgress && !_userMovedSinceLoadBegan;", helpers);
		foreach (string handler in new[] { "private async void List_Enter(", "private async void List_SelectedIndexChanged(" })
		{
			int start = helpers.IndexOf(handler, StringComparison.Ordinal);
			Assert.True(start > 0, handler + " not found");
			int gate = helpers.IndexOf("if (StartupHoldsTheLists) return;", start, StringComparison.Ordinal);
			int nextMethod = helpers.IndexOf("\n\tprivate ", start + handler.Length, StringComparison.Ordinal);
			Assert.True(gate > start && gate < nextMethod, handler + " must check StartupHoldsTheLists.");
		}
		Assert.DoesNotContain("if (_startupSpeechInProgress) return;", helpers);
	}

	[Fact]
	public void TheOpeningEndsOnlyOnceItHasBeenSaid()
	{
		string source = Form1();
		MatchCollection assignments = Regex.Matches(source, @"_startupSpeechInProgress = (true|false);");
		Assert.Equal(2, assignments.Count); // the field itself, and the one place it ends

		int shown = source.IndexOf("base.Shown += async delegate", StringComparison.Ordinal);
		int instructions = source.IndexOf("Speak(Loc.T(\"app.welcome\"));", shown, StringComparison.Ordinal);
		int ends = source.IndexOf("form._startupSpeechInProgress = false;", StringComparison.Ordinal);
		Assert.True(shown > 0 && instructions > shown && ends > instructions, "The list must stay quiet until the instructions are queued.");
	}
}
