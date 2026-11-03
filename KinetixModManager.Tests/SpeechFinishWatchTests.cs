using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Pins what counts as a sentence having finished. Getting this wrong in one direction cuts the sentence off, which
/// is the whole bug it exists to fix; in the other, the program hangs on a reader that has already gone quiet.
/// </summary>
public class SpeechFinishWatchTests
{
	private const int Start = 1000;
	private const int Cap = 10000;

	private static SpeechFinishWatch NewWatch() => new(Start, Cap);

	[Fact]
	public void ASentenceThatHasNotStartedYetIsNotFinished()
	{
		// Measured: NVDA reports speaking 8 to 17ms after being handed a sentence. The answers before that are no.
		SpeechFinishWatch watch = NewWatch();

		Assert.False(watch.IsFinished(Start, speaking: false));
		Assert.False(watch.IsFinished(Start + 17, speaking: false));
		Assert.False(watch.IsFinished(Start + SpeechFinishWatch.StartGraceMs - 1, speaking: false));
	}

	[Fact]
	public void AReaderThatNeverStartsIsNotWaitedOnForLong()
	{
		// NVDA with speech switched off, or set to speak on demand, says no forever.
		SpeechFinishWatch watch = NewWatch();

		Assert.True(watch.IsFinished(Start + SpeechFinishWatch.StartGraceMs, speaking: false));
	}

	[Fact]
	public void ItIsNotFinishedWhileSpeechContinues()
	{
		SpeechFinishWatch watch = NewWatch();

		foreach (int later in new[] { 10, 500, 2000, 9000 })
			Assert.False(watch.IsFinished(Start + later, speaking: true));
	}

	[Fact]
	public void ItFinishesOnceSpeechHasStoppedForTheQuietPeriod()
	{
		SpeechFinishWatch watch = NewWatch();
		watch.IsFinished(Start + 10, speaking: true);
		long ended = Start + 2000;
		watch.IsFinished(ended, speaking: true);

		Assert.False(watch.IsFinished(ended + 1, speaking: false));
		Assert.False(watch.IsFinished(ended + SpeechFinishWatch.QuietMs - 1, speaking: false));
		Assert.True(watch.IsFinished(ended + SpeechFinishWatch.QuietMs, speaking: false));
	}

	[Fact]
	public void ABreathBetweenQueuedSentencesDoesNotEndTheWait()
	{
		SpeechFinishWatch watch = NewWatch();
		watch.IsFinished(Start + 10, speaking: true);
		watch.IsFinished(Start + 1000, speaking: true);

		Assert.False(watch.IsFinished(Start + 1000 + SpeechFinishWatch.QuietMs - 1, speaking: false));

		// The next sentence starts, which puts the quiet period back to nothing.
		Assert.False(watch.IsFinished(Start + 1200, speaking: true));
		Assert.False(watch.IsFinished(Start + 1200 + SpeechFinishWatch.QuietMs - 1, speaking: false));
		Assert.True(watch.IsFinished(Start + 1200 + SpeechFinishWatch.QuietMs, speaking: false));
	}

	[Fact]
	public void TheStartGraceDoesNotCutOffASentenceThatIsLongerThanIt()
	{
		SpeechFinishWatch watch = NewWatch();
		watch.IsFinished(Start + 10, speaking: true);

		// Well past the grace, and well inside a quiet gap: only the quiet period may end this now.
		Assert.False(watch.IsFinished(Start + SpeechFinishWatch.StartGraceMs + 500, speaking: true));
		Assert.False(watch.IsFinished(Start + SpeechFinishWatch.StartGraceMs + 501, speaking: false));
	}

	[Fact]
	public void AReaderStuckSayingYesCannotHoldTheProgramForever()
	{
		SpeechFinishWatch watch = NewWatch();

		Assert.False(watch.IsFinished(Start + Cap - 1, speaking: true));
		Assert.True(watch.IsFinished(Start + Cap, speaking: true));
	}

	private sealed class AnnouncerWithoutAnAnswer : IAnnouncer
	{
		public void Speak(string text, bool interrupt = false) { }
		public void Silence() { }
		public bool IsSpeaking => true;
		public bool IsAvailable => true;
		public void Dispose() { }
	}

	[Fact]
	public void AReaderThatCannotSayReportsThatItCannotSay()
	{
		// Every other reader relies on this default to keep the timing it has always had. A false here instead of a
		// null would read as "finished", and every sentence would be cut off at once.
		IAnnouncer announcer = new AnnouncerWithoutAnAnswer();

		Assert.Null(announcer.IsSpeakingForCertain);
	}
}

/// <summary>
/// Guards the two things in this area nothing else would catch: the bundled client being replaced by one that cannot
/// answer, which would silently bring the old timing back, and a sentence being thrown away by a view closing.
/// </summary>
public class NvdaSpeechGuardTests
{
	private static string RepositoryRoot()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx")))
			dir = dir.Parent;
		return dir?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
	}

	[Theory]
	[InlineData("nvdaControllerClient64.dll")]
	[InlineData("nvdaControllerClient.dll")]
	public void TheBundledClientCanAskNvdaWhetherItIsSpeaking(string dll)
	{
		byte[] bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "KinetixModManager", "lib", dll));

		Assert.Contains("nvdaController_isSpeaking", Encoding.ASCII.GetString(bytes));
	}

	[Fact]
	public void ASentenceSpokenAsAViewClosesIsAllowedToFinish()
	{
		// closeView hands focus back, and focus arriving throws away whatever the reader still has queued. The window
		// looked at is short on purpose: this is about the sentence that belongs to the closing itself.
		var offenders = new List<string>();
		string app = Path.Combine(RepositoryRoot(), "KinetixModManager");
		var speaks = new Regex(@"(?<![\w.])Speak\(");

		foreach (string file in Directory.GetFiles(app, "Form1*.cs"))
		{
			string[] lines = File.ReadAllLines(file);
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i].Split("//")[0];
				int close = line.IndexOf("closeView();", StringComparison.Ordinal);
				if (close < 0) continue;

				// Before the call on its own line, then the few statements above it, stopping at any brace or return:
				// what is beyond one of those is on some other path, or in another handler entirely.
				var before = new List<string> { line[..close] };
				for (int back = i - 1; back >= 0 && back >= i - 3; back--)
				{
					string above = lines[back].Split("//")[0];
					if (above.Contains("return;") || above.Contains('{') || above.Contains('}')) break;
					before.Add(above);
				}

				string context = string.Concat(before);
				if (speaks.IsMatch(context) && !context.Contains("LetSpeechFinish("))
					offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
			}
		}

		Assert.True(offenders.Count == 0,
			"These speak and then close, so the sentence is lost. Call LetSpeechFinish() before closeView():\n"
			+ string.Join("\n", offenders));
	}

	[Fact]
	public void NothingIsSaidInFrontOfAPromptThatWouldCutItOff()
	{
		// A prompt opens by speaking its question with an interrupt, so a sentence right before it is thrown away, and
		// the sentence is left in the code looking as if it does something. What needs saying goes in the prompt.
		// LetSpeechFinish between the two is allowed: that is a run of progress lines being heard out, and it does not
		// match here because it is the statement next to the prompt.
		var offenders = new List<string>();
		string app = Path.Combine(RepositoryRoot(), "KinetixModManager");
		var says = new Regex(@"(?<![\w.])(?:Speak|SetStatus)\(.*\);$");
		var asks = new Regex(@"(?<![\w.])(?:SpeakBox|ShowPrompt)\(");
		var saysThenAsks = new Regex(@"(?<![\w.])Speak\(.*\);\s*(?:\S.*)?(?<![\w.])SpeakBox\(");

		foreach (string file in Directory.GetFiles(app, "Form1*.cs"))
		{
			string[] lines = File.ReadAllLines(file);
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i].Split("//")[0].Trim();
				if (saysThenAsks.IsMatch(line)) { offenders.Add($"{Path.GetFileName(file)}:{i + 1}"); continue; }
				if (!says.IsMatch(line) || line.Contains("speak: false") || line.Contains('{') || line.Contains('}')) continue;

				int next = i + 1;
				while (next < lines.Length && lines[next].Split("//")[0].Trim().Length == 0) next++;
				// An "else" means only one of the two ever runs, so nothing is cut off.
				if (next < lines.Length && asks.IsMatch(lines[next]) && !lines[next].TrimStart().StartsWith("else"))
					offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
			}
		}

		Assert.True(offenders.Count == 0,
			"These speak right before a prompt, which cuts the sentence off. Put what it says in the prompt:\n"
			+ string.Join("\n", offenders));
	}
}
