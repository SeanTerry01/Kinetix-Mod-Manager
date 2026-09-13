using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Guards the per-game names of the Wiki, Walkthroughs and Log tabs, and of the wiki search box.
///
/// <para>
/// Two ways these go wrong, and a Minecraft session hit both at once. The tabs are built by the designer with
/// Stardew Valley's names, and every switch that renames them ends in a <c>_ =&gt;</c> default of Stardew — so a
/// game the switch does not list does not get a blank tab, it silently gets another game's name. A sighted user
/// might shrug at that; a blind one is told the guide being read out belongs to a game they are not playing.
/// </para>
///
/// <para>
/// The second is worse because it affects every game rather than a new one. The renaming used to live inside
/// <c>SwitchActiveGame</c>, which returns immediately when asked for the game that is already loaded. That is
/// exactly what startup does — the active game is restored from settings before the window is built — so a
/// session reopened rather than switched into never renamed anything and kept the designer's Stardew labels
/// whatever game was actually loaded. Hence <c>ApplyGameTabLabels</c>, and hence a guard that it is still called
/// from somewhere other than the switch.
/// </para>
/// </summary>
public class PerGameTabLabelGuardTests
{
	[Fact]
	public void EveryPerGameLabelSwitchNamesEveryGame()
	{
		string body = ApplyGameTabLabelsBody();

		// Stardew Valley is the `_ =>` default rather than a listed case, so it is the one id not required here.
		string[] required = GameProfiles.AllIds.Where(id => id != GameProfiles.StardewValley).ToArray();
		Assert.True(required.Length >= 4, "Fewer games in the registry than this guard was written against.");

		var missing = new List<string>();
		string[] switches = SwitchBlocksIn(body).ToArray();

		// A guard that has stopped finding the switches it exists to check passes for the wrong reason. Four is
		// what is there now: the Wiki tab, the Walkthroughs tab, the Log tab and the search box's accessible name.
		Assert.True(switches.Length >= 4,
			$"Found {switches.Length} per-game switches in ApplyGameTabLabels; expected at least 4. Has it been restructured?");

		foreach (string block in switches)
		{
			// The label the switch produces, for a message that says which one is wrong rather than just "a switch".
			string what = Regex.Match(block, @"tab\.(\w+)|ui\.(\w+)").Value;

			foreach (string id in required)
			{
				if (!block.Contains($"\"{id}\""))
					missing.Add($"ApplyGameTabLabels: the switch producing {what} does not name {id}, so it falls through to Stardew Valley's label.");
			}
		}

		Assert.True(missing.Count == 0, string.Join("\n", missing));
	}

	[Fact]
	public void TabLabelsAreAppliedOutsideTheGameSwitchToo()
	{
		string session = File.ReadAllText(Path.Combine(AppRoot(), "Form1.GameSession.cs"));

		// The premise: SwitchActiveGame does nothing when the game asked for is the one already loaded. If that
		// early return ever goes away this guard is no longer needed - but until it does, it is.
		Assert.Contains("if (_settings.ActiveGame == game) return;", WithoutComments(session));

		var callers = new List<string>();
		foreach (string file in AppSourceFiles())
		{
			string source = WithoutComments(File.ReadAllText(file));
			// The declaration is the one mention that is not a call.
			foreach (Match unused in Regex.Matches(source, @"(?<!private void )\bApplyGameTabLabels\s*\("))
				callers.Add(Path.GetFileName(file));
		}

		Assert.True(callers.Any(f => f != "Form1.GameSession.cs"),
			"ApplyGameTabLabels is only called from Form1.GameSession.cs. A session restored at startup never "
			+ "reaches SwitchActiveGame, so every tab would keep the designer's Stardew Valley label. It must also "
			+ "be called where the window is built. Callers found: "
			+ (callers.Count == 0 ? "none" : string.Join(", ", callers.Distinct())));
	}

	/// <summary>The body of <c>ApplyGameTabLabels</c>, brace-matched out of Form1.GameSession.cs.</summary>
	private static string ApplyGameTabLabelsBody()
	{
		string source = WithoutComments(File.ReadAllText(Path.Combine(AppRoot(), "Form1.GameSession.cs")));
		int at = source.IndexOf("private void ApplyGameTabLabels(", StringComparison.Ordinal);
		Assert.True(at >= 0, "ApplyGameTabLabels is gone from Form1.GameSession.cs — has the renaming moved?");
		return BlockAt(source, source.IndexOf('{', at));
	}

	/// <summary>Each <c>switch { ... }</c> block in <paramref name="body"/>, braces included.</summary>
	private static IEnumerable<string> SwitchBlocksIn(string body)
	{
		int from = 0;
		while (true)
		{
			int at = body.IndexOf("switch", from, StringComparison.Ordinal);
			if (at < 0) yield break;
			int open = body.IndexOf('{', at);
			if (open < 0) yield break;
			string block = BlockAt(body, open);
			yield return block;
			from = open + block.Length;
		}
	}

	/// <summary>The braced block starting at <paramref name="open"/>, matched to its closing brace.</summary>
	private static string BlockAt(string source, int open)
	{
		Assert.True(open >= 0 && source[open] == '{', "Expected a braced block.");
		int depth = 0;
		for (int i = open; i < source.Length; i++)
		{
			if (source[i] == '{') depth++;
			else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
		}

		throw new InvalidOperationException("Unbalanced braces while reading a block.");
	}

	private static string WithoutComments(string source) =>
		Regex.Replace(source, @"//[^\n]*", match => new string(' ', match.Length));

	private static IEnumerable<string> AppSourceFiles()
	{
		string root = AppRoot();
		return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
			.Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
					 && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
	}

	private static string AppRoot()
	{
		var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
		while (dir != null)
		{
			string candidate = Path.Combine(dir.FullName, "KinetixModManager");
			if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "Form1.Helpers.cs")))
				return candidate;
			dir = dir.Parent;
		}

		throw new DirectoryNotFoundException("Could not find the KinetixModManager source folder from " + Directory.GetCurrentDirectory());
	}
}
