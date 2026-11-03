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
