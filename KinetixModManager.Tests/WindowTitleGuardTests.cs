using System.Text.RegularExpressions;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// A test copy says "Test Mode:" at the front of its title. Only the status line used to add it, so the startup title
/// and a game being switched left it out: with no game loaded, a test copy said plain "Kinetix Mod Manager" the whole
/// time it was open. Every write of the window's title must go through WindowTitle. Read from source, because the app
/// can't be loaded here.
/// </summary>
public class WindowTitleGuardTests
{
	[Fact]
	public void EveryWriteOfTheWindowTitleSaysWhenItIsATestCopy()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		string app = Path.Combine(dir!.FullName, "KinetixModManager");

		// A statement, ending in ";". The game screen's heading label sets its Text to the app title too, but in an
		// initializer ending in ",", and a heading is not the window title.
		var titleWrite = new Regex(@"^\s*(this\.|base\.)?Text = .*(""ui\.appTitle""|""ui\.appTitleGame""|""status\.titleFormat"").*;\s*$");
		var writes = new List<string>();
		var missing = new List<string>();
		foreach (string file in Directory.GetFiles(app, "Form1*.cs"))
		{
			string[] lines = File.ReadAllLines(file);
			for (int i = 0; i < lines.Length; i++)
			{
				if (!titleWrite.IsMatch(lines[i])) continue;
				string where = $"{Path.GetFileName(file)}:{i + 1}";
				writes.Add(where);
				if (!lines[i].Contains("WindowTitle(", StringComparison.Ordinal)) missing.Add(where);
			}
		}

		Assert.True(writes.Count >= 3, "Expected the startup, game-switch and status title writes; the pattern no longer finds them.");
		Assert.True(missing.Count == 0, "These set the window title without WindowTitle: " + string.Join(", ", missing));
	}
}
