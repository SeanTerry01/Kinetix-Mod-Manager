using System.Text.RegularExpressions;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// A test copy says "Test Mode:" at the front of its title. Only the status line used to add it, so the startup title,
/// a game being switched and the download progress left it out: with no game loaded, a test copy said plain "Kinetix Mod
/// Manager" the whole time it was open, and every download dropped the prefix while it ran. Every write of the window's
/// title must go through WindowTitle. Read from source, because the app can't be loaded here.
/// </summary>
public class WindowTitleGuardTests
{
	/// <summary>Writes that are not a new title: the designer's placeholder, and putting back a title read from the window.</summary>
	private static readonly string[] NotANewTitle = { "this.Text = \"Form1\";", "Text = originalTitle;" };

	[Fact]
	public void EveryWriteOfTheWindowTitleSaysWhenItIsATestCopy()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		string app = Path.Combine(dir!.FullName, "KinetixModManager");

		// A bare statement setting Text in the window's own files is the window's title. A control's text is written through
		// the control ("label.Text = ...") or in an initializer, which ends in "," rather than ";".
		var titleWrite = new Regex(@"^\s*(this\.|base\.)?Text = .*;\s*$");
		var writes = new List<string>();
		var missing = new List<string>();
		foreach (string file in Directory.GetFiles(app, "Form1*.cs"))
		{
			string[] lines = File.ReadAllLines(file);
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i];
				if (!titleWrite.IsMatch(line) || NotANewTitle.Contains(line.Trim())) continue;
				string where = $"{Path.GetFileName(file)}:{i + 1}";
				writes.Add(where);
				if (!line.Contains("WindowTitle(", StringComparison.Ordinal)) missing.Add(where);
			}
		}

		Assert.True(writes.Count >= 4, "Expected the startup, game-switch, status and progress title writes; the pattern no longer finds them.");
		Assert.True(missing.Count == 0, "These set the window title without WindowTitle: " + string.Join(", ", missing));
	}
}
