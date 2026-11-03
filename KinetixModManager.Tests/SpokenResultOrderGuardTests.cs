using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// What Sean heard on the test copy: "Installed Mods List list", "Settings saved" last of all, and "X is now disabled" cut
/// off by the row it moved to. Read from source, because the app can't be loaded here.
/// </summary>
public class SpokenResultOrderGuardTests
{
	private static string Repo()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		return dir!.FullName;
	}

	private static string App(string file) =>
		File.ReadAllText(Path.Combine(Repo(), "KinetixModManager", file)).ReplaceLineEndings("\n");

	private static IEnumerable<string> AppFiles() =>
		Directory.GetFiles(Path.Combine(Repo(), "KinetixModManager"), "Form1*.cs");

	[Fact]
	public void AListsNameLeavesTheWordListToTheReader()
	{
		using JsonDocument english = JsonDocument.Parse(App(Path.Combine("lang", "en.json")).TrimStart('﻿'));

		var named = new Regex(@"(?:AccessibleName = |listName: |ListBox[^\n]*AccessibleName = )Loc\.T\(""([^""]+)""");
		var offenders = new List<string>();
		int checkedNames = 0;
		foreach (string file in AppFiles())
		{
			foreach (Match match in named.Matches(File.ReadAllText(file)))
			{
				string key = match.Groups[1].Value;
				if (!english.RootElement.TryGetProperty(key, out JsonElement value)) continue;
				// A checkbox whose own subject is the game's plugin list. The reader says "check box" after it, not "list".
				if (key == "settings.protectPluginOrderName") continue;
				checkedNames++;
				if (Regex.IsMatch(value.GetString() ?? "", @"\blist\.?$", RegexOptions.IgnoreCase)) offenders.Add(key);
			}
		}

		Assert.True(checkedNames > 20, "The scan has stopped finding the names.");
		Assert.True(offenders.Count == 0, "A reader adds \"list\" itself, so these say it twice: " + string.Join(", ", offenders));
	}

	[Fact]
	public void WhenTheManagerSaysAListsNameItSaysListToo()
	{
		using JsonDocument english = JsonDocument.Parse(App(Path.Combine("lang", "en.json")).TrimStart('﻿'));
		Assert.Equal("{0} list. {1}", english.RootElement.GetProperty("common.listNameThenRest").GetString());
	}

	[Fact]
	public void NothingSpeaksRightBeforeARescanThatCouldSwallowIt()
	{
		// Same line or the next one: a rescan moving the row under the list silences everything just said.
		var pattern = new Regex(@"Speak\(Loc\.T\(""(modactions\.[^""]+|link\.success|updates\.leftDisabled)""[^\n]*\n\s*(?://[^\n]*\n\s*)*(?:await |Fire\()RefreshModList|(?:await |Fire\()RefreshModList[^\n]*\n\s*Speak\(Loc\.T\(""(modactions\.[^""]+|link\.success)""");
		var offenders = new List<string>();
		foreach (string file in AppFiles())
		{
			foreach (Match match in pattern.Matches(File.ReadAllText(file).ReplaceLineEndings("\n")))
				offenders.Add(Path.GetFileName(file) + ": " + match.Value);
		}

		Assert.True(offenders.Count == 0, "Use SayAndRefreshModList:\n" + string.Join("\n", offenders));
	}

	[Fact]
	public void AResultGivenToARowIsNotLostWithIt()
	{
		string helpers = App("Form1.Helpers.cs");
		Assert.Contains("replaceReader: announceRowName, carrying: carrying", helpers);
		// Giving way to newer speech must still say it, and the short repeat check must not drop it.
		Assert.Contains("if (carrying != null && !_shuttingDown) Speak(carrying);", helpers);
		Assert.Contains("if (carrying == null && ReferenceEquals(_lastPosList, list)", helpers);

		// The row did not move, or none is left: nothing took it, so it is said at the end.
		string modList = App("Form1.ModList.cs");
		int method = modList.IndexOf("private async Task SayAndRefreshModList", StringComparison.Ordinal);
		int finallyAt = modList.IndexOf("finally", method, StringComparison.Ordinal);
		Assert.True(method > 0 && finallyAt > method);
		Assert.Contains("SayWaitingResult();", modList.Substring(finallyAt, 120));
	}

	[Theory]
	[InlineData("Form1.Settings.cs", "savedMessage")]
	[InlineData("Form1.Dialogs.cs", "themeMgr.saved")]
	[InlineData("Form1.Dialogs.cs", "shortcutMgr.saved")]
	public void AViewsResultComesAheadOfTheWindowItGoesBackTo(string file, string wording)
	{
		Assert.Matches(@"sayOnClosing: \(\) => [^\n]*" + Regex.Escape(wording), App(file));
	}

	[Fact]
	public void ClosingWaitsForTheResultBeforeFocusGoesBack()
	{
		string overlay = App("Form1.InlinePrompt.cs");
		int loopEnds = overlay.IndexOf("beforeRestoring?.Invoke();", StringComparison.Ordinal);
		int restored = overlay.IndexOf("focusBefore.Focus();", StringComparison.Ordinal);
		Assert.True(loopEnds > 0 && restored > loopEnds, "The result has to be said before focus is restored.");

		string view = App("Form1.InlineView.cs");
		Assert.Contains("_announcer.IsSpeakingForCertain is null", view);
		Assert.Contains("LetSpeechFinish();", view);
		// A reader that can't say when it has finished still hears it, after.
		Assert.Contains("if (!saidOnClosing && sayOnClosing?.Invoke() is { } after) Speak(after);", view);
	}
}
