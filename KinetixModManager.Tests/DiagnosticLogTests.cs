using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers the one log a user is asked for when something went wrong.
///
/// The bug this exists to prevent is not a formatting slip, it is a log that turns out to be useless at the
/// moment it is needed. There were two files before: ordinary failures went to the one the menu opens, crashes
/// went to a <c>crash_log.txt</c> that nothing in the app ever opened or mentioned. A user who crashed sent a log
/// with no crash in it, while the file holding the answer sat unread beside it for months.
///
/// So what is asserted here is what makes an entry usable by someone who was not there: the exception's type and
/// stack rather than its message alone, every inner exception rather than the outermost one, and what the app was
/// trying to do at the time.
/// </summary>
public class DiagnosticLogTests
{
	private const string Stamp = "2026-09-01 14:07:02";

	// -------------------------------------------------------------------------
	// What an entry has to contain to be worth having
	// -------------------------------------------------------------------------

	[Fact]
	public void AFailureRecordsWhatWasBeingDoneNotJustWhatWentWrong()
	{
		// A stack trace says where the code was. Only this says what the user had asked for, which is the half
		// that makes a report reproducible.
		string text = DiagnosticLog.FormatFailure("Updates", "updating all mods", new InvalidOperationException("boom"), Stamp, 7);

		Assert.Contains("updating all mods", text);
		Assert.Contains("Updates", text);
		Assert.Contains(Stamp, text);
		Assert.Contains("thread 7", text);
	}

	[Fact]
	public void TheExceptionTypeSurvivesAndNotJustItsMessage()
	{
		// "Object reference not set to an instance of an object" and "Access to the path is denied" are the same
		// sentence to a reader who cannot see which type raised them; the type is often the whole diagnosis.
		string text = DiagnosticLog.FormatFailure("Nexus", "downloading a file", new UnauthorizedAccessException("denied"), Stamp, 1);

		Assert.Contains("System.UnauthorizedAccessException", text);
		Assert.Contains("denied", text);
	}

	[Fact]
	public void EveryInnerExceptionIsWrittenOut()
	{
		// The outermost message is routinely the least informative in the chain. Reduced to it, a report says
		// "One or more errors occurred" and nobody can act on it.
		var inner = new FileNotFoundException("the archive is gone", "mod.7z");
		var middle = new InvalidOperationException("could not unpack", inner);
		var outer = new Exception("install failed", middle);

		string text = DiagnosticLog.FormatFailure("Install", "installing a mod", outer, Stamp, 1);

		Assert.Contains("install failed", text);
		Assert.Contains("caused by", text);
		Assert.Contains("could not unpack", text);
		Assert.Contains("the archive is gone", text);
		Assert.Contains("System.IO.FileNotFoundException", text);
	}

	[Fact]
	public void EveryBranchOfAnAggregateIsNamedNotOnlyTheFirst()
	{
		// Following InnerException alone reaches exactly one branch, which on a failed batch is one arbitrary mod
		// out of ten — and the one it reaches is not the one the user noticed.
		var agg = new AggregateException(
			new TimeoutException("mod A timed out"),
			new UnauthorizedAccessException("mod B was locked"),
			new IOException("mod C vanished"));

		string text = DiagnosticLog.FormatFailure("Updates", "updating 3 mods", agg, Stamp, 1);

		Assert.Contains("mod A timed out", text);
		Assert.Contains("mod B was locked", text);
		Assert.Contains("mod C vanished", text);
	}

	[Fact]
	public void TheStackTraceIsKeptWhenThereIsOne()
	{
		Exception caught;
		try { throw new InvalidOperationException("thrown for real"); }
		catch (Exception ex) { caught = ex; }

		string text = DiagnosticLog.FormatFailure("Test", "raising an exception", caught, Stamp, 1);

		Assert.Contains("at ", text);
		Assert.Contains(nameof(TheStackTraceIsKeptWhenThereIsOne), text);
	}

	[Fact]
	public void ACrashHandlerHandedNothingStillWritesAnEntry()
	{
		// AppDomain.UnhandledException can carry something that is not an Exception at all. Writing nothing there
		// would lose the one record that a crash happened.
		string text = DiagnosticLog.FormatFailure("Unhandled", "an error the app could not recover from", null, Stamp, 1);

		Assert.Contains("an error the app could not recover from", text);
		Assert.Contains("no exception object", text);
	}

	[Fact]
	public void TheTimestampIsAFullDateInAFixedFormat()
	{
		// A log reaches whoever is diagnosing it days later and often spans several sessions, so a bare
		// "14:07:02" cannot be placed against "it broke on Tuesday". Fixed format, because a log written on a
		// machine with a different locale still has to be readable.
		string text = DiagnosticLog.FormatFailure("Test", "anything", new Exception("x"), Stamp, 1);

		Assert.Matches(new Regex(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\]"), text);
	}

	// -------------------------------------------------------------------------
	// The file itself
	// -------------------------------------------------------------------------

	[Fact]
	public void TheRolledOverLogSitsBesideTheLiveOne()
	{
		// One previous generation is kept rather than deleted. The cause of a visible break is often in the
		// session before it — something failed quietly last time — and throwing that away loses the evidence.
		string previous = DiagnosticLog.PreviousPath(@"C:\data\mod_manager_log.txt");

		Assert.Equal(@"C:\data\mod_manager_log.1.txt", previous);
	}

	[Fact]
	public void StartingWritesAHeaderThatSaysWhichBuildProducedTheLog()
	{
		string dir = Path.Combine(Path.GetTempPath(), "kmm-log-" + Guid.NewGuid().ToString("N"));
		string path = Path.Combine(dir, "mod_manager_log.txt");
		try
		{
			DiagnosticLog.Start(path, new[] { "manager   : 1.5.2.0", "game      : SkyrimSE" });
			DiagnosticLog.Write("Updates", "something went wrong");

			string text = File.ReadAllText(path);

			// Without the version, a report can be actively misleading: the bug described may already be fixed.
			Assert.Contains("session started", text);
			Assert.Contains("1.5.2.0", text);
			Assert.Contains("SkyrimSE", text);
			Assert.Contains("Updates: something went wrong", text);
		}
		finally
		{
			// Put the static back, so nothing later in the run inherits a path into a folder about to be deleted.
			DiagnosticLog.Start("", Array.Empty<string>());
			try { Directory.Delete(dir, true); } catch { }
		}
	}

	[Fact]
	public void WritingBeforeTheLogIsStartedIsIgnoredRatherThanThrowing()
	{
		// The crash handler runs when the app is already in trouble. It must never be the thing that throws.
		DiagnosticLog.Start("", Array.Empty<string>());

		DiagnosticLog.Write("Test", "no path set");
		DiagnosticLog.WriteException("Test", "no path set", new Exception("x"));
	}
}

/// <summary>
/// Guards the rule that makes the log worth anything on the paths that matter: work the app starts and
/// deliberately does not wait for must go through <c>Fire</c>, which records a failure in it.
///
/// The manager does this in about fifty places — a keypress starts an update check or an install and returns at
/// once, so the window stays responsive. Written as a bare discard, an exception in that work has no caller to
/// receive it: nothing is thrown where anybody is looking, nothing is written, and the operation simply never
/// finishes. That is the exact shape of "I pressed update mods and it stopped", reported with nothing to show
/// for it.
///
/// A guard rather than a note in a comment, because the bare form is the one that comes naturally and a single
/// new one puts a silent hole back in the log.
/// </summary>
public class BackgroundWorkGuardTests
{
	/// <summary>Calls whose result is deliberately discarded and which are not Tasks, so cannot hide a failure.</summary>
	private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
	{
		"UncheckedModCount",   // returns a count; the discard is of a value, not of pending work
		"Observe"              // Fire's own inner helper, which is what does the observing
	};

	[Fact]
	public void NoWorkIsStartedWithoutSomewhereForItsFailureToGo()
	{
		var offenders = new List<string>();
		// The discard has to be the whole target, not the tail of a name: "int unchecked_ = UncheckedModCount()"
		// is an ordinary assignment and matching it would make this fail on innocent code.
		var discard = new Regex(@"(?<![A-Za-z0-9_])_ = ([A-Za-z_][A-Za-z0-9_]*)\(");

		foreach (string file in Directory.EnumerateFiles(SourceFolder(), "*.cs"))
			foreach (string line in File.ReadAllLines(file))
			{
				Match m = discard.Match(line);
				if (!m.Success || Allowed.Contains(m.Groups[1].Value)) continue;
				offenders.Add($"{Path.GetFileName(file)}: {line.Trim()}");
			}

		Assert.True(offenders.Count == 0,
			"Work started with a bare discard cannot report its own failure. Use Fire(work, \"what\") instead:"
			+ Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}

	/// <summary>The app's source folder, found from the test binary rather than hard-coded.</summary>
	private static string SourceFolder()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "KinetixModManager")))
			dir = dir.Parent;

		Assert.NotNull(dir);
		return Path.Combine(dir!.FullName, "KinetixModManager");
	}
}

/// <summary>
/// Guards the other half of the rule: a failure that is caught must not then be thrown away.
///
/// 93 places used to swallow an exception and carry on with an empty <c>catch</c>. Most had a good reason to
/// carry on — an unreadable folder really does read the same as an empty one — but "carry on" and "say nothing
/// at all" are different decisions, and only the first of them was ever intended. The result was a manager that
/// could fail to deploy a file, fail to write a mod's details, or fail to load the screen-reader bridge, and
/// leave not one word anywhere about it.
///
/// Carrying on is still fine. Doing it silently is not.
/// </summary>
public class SilentFailureGuardTests
{
	/// <summary>
	/// The one thing an empty catch is still allowed to be: a cancellation. A user backing out of a wizard and a
	/// token signalled at shutdown are not failures, and logging them would be noise about working software.
	/// </summary>
	private static readonly Regex Cancellation = new(@"catch\s*\(\s*OperationCanceledException", RegexOptions.Compiled);

	/// <summary>A catch whose body holds nothing but whitespace and comments.</summary>
	private static readonly Regex EmptyCatch =
		new(@"catch\s*(?:\([^)]*\))?\s*\{(?<body>[^{}]*)\}", RegexOptions.Compiled | RegexOptions.Singleline);

	private static readonly Regex Comment = new(@"//[^\n]*|/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

	[Fact]
	public void NothingFailsWithoutSayingSo()
	{
		var offenders = new List<string>();

		foreach (string file in Directory.EnumerateFiles(SourceFolder(), "*.cs"))
		{
			// The log itself is the one thing that has to swallow. It is called from the crash handler, when the
			// app is already in trouble; a logger that throws while recording a crash destroys the very record
			// that was being written, and there is nowhere left to report its own failure to anyway.
			if (Path.GetFileName(file) == "DiagnosticLog.cs") continue;

			string text = File.ReadAllText(file);
			foreach (Match m in EmptyCatch.Matches(text))
			{
				if (Comment.Replace(m.Groups["body"].Value, "").Trim().Length > 0) continue;
				if (Cancellation.IsMatch(m.Value)) continue;

				int line = text.Take(m.Index).Count(c => c == '\n') + 1;
				offenders.Add($"{Path.GetFileName(file)}:{line}");
			}
		}

		Assert.True(offenders.Count == 0,
			"A caught failure that is written down nowhere cannot be reported, reproduced or fixed. Record it with "
			+ "DiagnosticLog.WriteException(area, whatWasBeingAttempted, ex) and then carry on as before:"
			+ Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}

	/// <summary>The app's source folder, found from the test binary rather than hard-coded.</summary>
	private static string SourceFolder()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "KinetixModManager")))
			dir = dir.Parent;

		Assert.NotNull(dir);
		return Path.Combine(dir!.FullName, "KinetixModManager");
	}
}
