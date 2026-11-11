using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Ignoring an update was one way only: nothing in the manager could undo it (Sean, 2026-11-11). These pin the way back, and
/// that the question which ignores says where it is. The app can't be loaded here, so its source and strings are read.
/// </summary>
public sealed class IgnoredUpdatesGuardTests
{
	private static string Root()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		return dir!.FullName;
	}

	private static string App(string file) =>
		File.ReadAllText(Path.Combine(Root(), "KinetixModManager", file)).ReplaceLineEndings("\n");

	[Fact]
	public void TheMenuOpensTheScreenThatRemovesAnIgnore()
	{
		Assert.Contains("ShowIgnoredUpdates();", App("Form1.UI.cs"));
		Assert.Contains("_settings.IgnoredVersions.Remove(row.Key);", App("Form1.IgnoredUpdates.cs"));
	}

	[Fact]
	public void TheQuestionThatIgnoresSaysWhereToUndoIt()
	{
		string en = File.ReadAllText(Path.Combine(Root(), "KinetixModManager", "lang", "en.json"));
		int at = en.IndexOf("\"modlist.ignoreConfirm\"", StringComparison.Ordinal);
		Assert.True(at >= 0);
		string line = en.Substring(at, en.IndexOf('\n', at) - at);
		Assert.Contains("Ignored Updates", line);
	}
}
