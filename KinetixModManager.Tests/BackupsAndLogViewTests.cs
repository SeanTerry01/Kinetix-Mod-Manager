using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="BackupsView"/> and <see cref="GameLogView"/> — the two screens that are read after
/// something has already gone wrong, which is what shapes both of them.
/// </summary>
public class BackupsAndLogViewTests : IDisposable
{
	static BackupsAndLogViewTests() => Loc.Init("en");

	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kinetix-bl-" + Guid.NewGuid().ToString("N"));

	public BackupsAndLogViewTests() => Directory.CreateDirectory(_dir);

	public void Dispose()
	{
		try { Directory.Delete(_dir, true); } catch { }
	}

	private BackupItem Backup(string modName, DateTime taken, int bytes = 2048)
	{
		string path = Path.Combine(_dir, $"{modName}_{taken:yyyyMMdd_HHmmss}.zip");
		File.WriteAllBytes(path, new byte[bytes]);
		File.SetLastWriteTime(path, taken);
		return new BackupItem { Name = Path.GetFileName(path), FullPath = path };
	}

	// ---------------------------------------------------------------------
	// Backups
	// ---------------------------------------------------------------------

	[Fact]
	public void TheNewestBackupComesFirst()
	{
		// A backups list is opened after something went wrong, and the copy wanted is almost always the one
		// taken just before the thing that broke it. Sorting by name would bury it among identical names.
		var view = BackupsView.Of(new[]
		{
			Backup("Automate", DateTime.Now.AddDays(-10)),
			Backup("Automate", DateTime.Now.AddMinutes(-5)),
			Backup("Automate", DateTime.Now.AddDays(-2)),
		});

		Assert.True(view.Rows[0].TakenAt > view.Rows[1].TakenAt);
		Assert.True(view.Rows[1].TakenAt > view.Rows[2].TakenAt);
	}

	[Fact]
	public void ARowSaysTheModThenWhen()
	{
		var view = BackupsView.Of(new[] { Backup("Content Patcher", DateTime.Now.AddHours(-3)) });

		Assert.StartsWith("Content Patcher", view.Rows[0].Spoken);
		Assert.Contains("hours ago", view.Rows[0].Spoken);
	}

	[Fact]
	public void TheDateIsSaidTheWayAPersonWouldSayIt()
	{
		// Not an ISO stamp read out digit by digit.
		Assert.Contains("just now", BackupsView.Of(new[] { Backup("A", DateTime.Now) }).Rows[0].Spoken);
		Assert.Contains("days ago", BackupsView.Of(new[] { Backup("A", DateTime.Now.AddDays(-3)) }).Rows[0].Spoken);
	}

	[Fact]
	public void NoBackupsSaysWhereTheyComeFromRatherThanSayingZero()
	{
		string said = BackupsView.Of(Array.Empty<BackupItem>()).Announcement;

		Assert.Contains("no backups", said);
		Assert.Contains("automatically", said);
	}

	[Fact]
	public void TheSummaryCountsBackupsAndModsSeparately()
	{
		// Five copies of one mod is a very different thing from one copy of five.
		var view = BackupsView.Of(new[]
		{
			Backup("Automate", DateTime.Now.AddDays(-1)),
			Backup("Automate", DateTime.Now.AddDays(-2)),
			Backup("Lookup Anything", DateTime.Now.AddDays(-3)),
		});

		Assert.Equal(3, view.Rows.Count);
		Assert.Equal(2, view.ModCount);
	}

	[Fact]
	public void RestoringSaysWhetherItWouldOverwriteSomething()
	{
		// "Restore this" and "replace what you have with this" are the same action, and only the second is
		// honest about it.
		BackupRow row = BackupsView.Of(new[] { Backup("Automate", DateTime.Now) }).Rows[0];

		Assert.Contains("replace", BackupsView.DescribeRestoring(row, somethingIsThere: true));
		Assert.DoesNotContain("replace", BackupsView.DescribeRestoring(row, somethingIsThere: false));
	}

	[Fact]
	public void ABackupOnADriveThatHasGoneAwayIsStillListed()
	{
		// The user may be about to plug it back in. It costs the row its date, not its existence.
		var view = BackupsView.Of(new[]
		{
			new BackupItem { Name = "Gone_20260101_000000.zip", FullPath = "/mnt/removed/Gone_20260101_000000.zip" },
		});

		Assert.Single(view.Rows);
	}

	// ---------------------------------------------------------------------
	// The log
	// ---------------------------------------------------------------------

	private static readonly string[] Log =
	{
		"[15:41:12 INFO  SMAPI] SMAPI 4.1.10 with Stardew Valley 1.6.15 on Linux",
		"[15:41:13 DEBUG SMAPI] Loading mods...",
		"[15:41:14 WARN  SMAPI] Some mods could not be added to your save",
		"[15:41:15 ERROR SMAPI] Failed loading mod 'Automate': missing dependency",
		"[15:41:16 INFO  game] Done.",
	};

	[Fact]
	public void TheLogOpensOnProblemsRatherThanEverything()
	{
		// A SMAPI log is thousands of lines of a game starting normally. A list that begins at line one asks
		// the user to arrow through all of it to reach the lines that explain the crash.
		var view = GameLogView.Of(Log, LogFilter.Problems);

		Assert.Equal(2, view.Rows.Count);
		Assert.Equal(1, view.ErrorCount);
		Assert.Equal(1, view.WarningCount);
	}

	[Fact]
	public void ErrorsOnlyDropsTheWarnings()
	{
		var view = GameLogView.Of(Log, LogFilter.ErrorsOnly);

		Assert.Single(view.Rows);
		Assert.True(view.Rows[0].IsError);
	}

	[Fact]
	public void EverythingKeepsEveryLineThatIsNotBlank()
	{
		var view = GameLogView.Of(Log, LogFilter.Everything);

		Assert.Equal(Log.Length, view.Rows.Count);
	}

	[Fact]
	public void NoLogAndANothingWrongLogSaySomethingDifferent()
	{
		// One is a game that has not been run since the loader was installed; the other is a game that ran
		// fine. An empty list cannot tell them apart.
		string noLog = GameLogView.NoLog().Announcement;
		string clean = GameLogView.Of(new[] { "[15:41:12 INFO SMAPI] fine" }, LogFilter.Problems).Announcement;

		Assert.NotEqual(noLog, clean);
		Assert.Contains("no log", noLog, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("Nothing wrong", clean);
	}

	[Fact]
	public void TheSummarySaysHowManyLinesWereRead()
	{
		// "Nothing wrong" over a log nobody read would be a lie of omission — the same rule the updates list
		// follows.
		Assert.Contains("5", GameLogView.Of(Log, LogFilter.Problems).Announcement);
	}

	[Fact]
	public void ARowDropsTheTimestampAndLeadsWithTheLevel()
	{
		// The timestamp is at the front of every line and the same on most of them. Hearing it before every
		// message is the fastest way to make a log unlistenable.
		GameLogRow row = GameLogView.Of(Log, LogFilter.ErrorsOnly).Rows[0];

		Assert.DoesNotContain("15:41:15", row.Spoken);
		Assert.StartsWith("ERROR", row.Spoken);
		Assert.Contains("Automate", row.Spoken);
	}

	[Fact]
	public void ARowCarriesItsSuggestedFixRatherThanHidingIt()
	{
		// A log line a user cannot act on is noise however accurately it is read, and a fix one keypress away
		// is never heard by the person working through forty lines to find out why their game will not start.
		var view = GameLogView.Of(
			new[] { "[15:41:15 ERROR SMAPI] Failed loading mod 'X': missing dependency" },
			LogFilter.ErrorsOnly);

		string fix = LogAnalyzer.GetSuggestedFix("[15:41:15 ERROR SMAPI] Failed loading mod 'X': missing dependency");
		if (fix.Length > 0) Assert.Contains(fix, view.Rows[0].Spoken);
	}

	[Fact]
	public void AnEmptyLogIsNotACrash()
	{
		var view = GameLogView.Of(Array.Empty<string>(), LogFilter.Everything);

		Assert.Empty(view.Rows);
		Assert.False(string.IsNullOrWhiteSpace(view.Announcement));
	}
}
