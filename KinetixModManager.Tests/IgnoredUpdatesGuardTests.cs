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
		Assert.Contains("foreach (IgnoredUpdateRow r in together) _settings.IgnoredVersions.Remove(r.Key);", App("Form1.IgnoredUpdates.cs"));
	}

	[Fact]
	public void OneDownloadIsIgnoredRecordedAndUndoneAsAWhole()
	{
		// Bush Bloom Mod and its three examples, Cape Stardew's five parts: ignoring one left the others offered (Sean, 2026-11-12).
		string input = App("Form1.Input.cs");
		Assert.Contains("List<GameMod> fromDownload = UpdateRowsFromTheSameDownload(modWithUpdate);", input);
		Assert.Contains("foreach (GameMod row in fromDownload)", input);
		string updates = App("Form1.Updates.cs");
		Assert.Contains("List<GameMod> fromDownload = UpdateRowsFromTheSameDownload(mod);", updates);
		int remove = updates.IndexOf("RemoveSettledUpdateRow(GameMod mod)", StringComparison.Ordinal);
		Assert.Contains("UpdateRowsFromTheSameDownload(mod)", updates.Substring(remove, 400));
		Assert.Contains("r.DownloadKey.Equals(row.DownloadKey, StringComparison.OrdinalIgnoreCase)", App("Form1.IgnoredUpdates.cs"));
		// One record covers the download, so its row names the others it settles (it named only the one, 2026-11-12).
		Assert.Contains("Loc.T(\"ignoredUpdates.recordedRowWithOthers\"", App("Form1.IgnoredUpdates.cs"));
		Assert.Contains("Loc.T(\"ignoredUpdates.recordForgottenWithOthers\"", App("Form1.IgnoredUpdates.cs"));

		// Each question naming the others still says where to undo it.
		string en = File.ReadAllText(Path.Combine(Root(), "KinetixModManager", "lang", "en.json"));
		foreach (string key in new[] { "\"modlist.ignoreConfirmWithOthers\"", "\"updates.markConfirmWithOthers\"" })
		{
			int at = en.IndexOf(key, StringComparison.Ordinal);
			Assert.True(at >= 0, key);
			Assert.Contains("Ignored Updates", en.Substring(at, en.IndexOf('\n', at) - at));
		}
	}

	[Fact]
	public void AVersionRecordedWithCtrlShiftYCanBeUndoneThere()
	{
		// Ctrl+Shift+Y hid an update as surely as an ignore, and had no way back (Sean, 2026-11-12).
		string updates = App("Form1.Updates.cs");
		// With the names of exactly the rows it took off the list: counting every mod in the download said "3 other mods" of 3.
		Assert.Contains("NoteMarked(DownloadKey(mod), fromDownload.Select(r => r.Name));", updates);
		// A real install of the download makes the record a known one, which is no longer the user's to undo.
		int record = updates.IndexOf("private void RecordInstalledDownloadVersion(", StringComparison.Ordinal);
		Assert.Contains("ForgetMark(key)", updates.Substring(record, 600));
		string screen = App("Form1.IgnoredUpdates.cs");
		Assert.Contains("_settings.MarkedInstalledVersions.TryGetValue(_settings.ActiveGame, out var marks)", screen);
		Assert.Contains("ForgetInstalledDownloadVersion(row.Key);", screen);
	}

	[Fact]
	public void TheQuestionThatRecordsAVersionSaysWhereToUndoIt()
	{
		string en = File.ReadAllText(Path.Combine(Root(), "KinetixModManager", "lang", "en.json"));
		int at = en.IndexOf("\"updates.markConfirm\"", StringComparison.Ordinal);
		Assert.True(at >= 0);
		Assert.Contains("Ignored Updates", en.Substring(at, en.IndexOf('\n', at) - at));
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
