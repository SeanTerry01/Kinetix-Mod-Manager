using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ProgressAnnouncer"/>, which until Phase 3 could not be tested at all: it held a Form1,
/// so exercising it meant standing up a window.
///
/// What it decides is worth pinning. A download reports progress thousands of times, and the rules about what
/// reaches the user — speak only whole deciles, throttle the tone, cross to the UI thread only when something
/// actually changed — are the difference between useful feedback and a screen reader talking over itself for
/// the length of a transfer.
/// </summary>
public class ProgressAnnouncerTests
{
	private sealed class FakeAnnouncer : IAnnouncer
	{
		public List<string> Spoken { get; } = new();
		public void Speak(string text, bool interrupt = false) => Spoken.Add(text);
		public void Silence() { }
		public bool IsSpeaking => false;
		public bool IsAvailable => true;
		public void Dispose() { }
	}

	private sealed class FakeSounds : ISoundEngine
	{
		public List<int> Tones { get; } = new();
		public void Play(string name, string? themeOverride = null) { }
		public Task PlayAsync(string name, string? themeOverride = null) => Task.CompletedTask;
		public void PlayTone(int percent) => Tones.Add(percent);
		public void PlayLogoSound(string theme, string file) { }
		public void StopLogoSound() { }
	}

	/// <summary>Runs posted work immediately, which is what the UI thread does from the UI thread.</summary>
	private sealed class ImmediateDispatcher : IDispatcher
	{
		public void Post(Action action) => action();
		public Task<T> InvokeAsync<T>(Func<T> function) => Task.FromResult(function());
		public bool IsOnUiThread => true;
	}

	private sealed class FakeDisplay : IProgressDisplay
	{
		public ProgressFeedback Mode { get; set; } = ProgressFeedback.Both;
		public List<int> Shown { get; } = new();
		public int Resets { get; private set; }
		public string OpeningPhrase(string name, string phraseKey) => "Downloading " + name;
		public void ShowProgress(string name, string phraseKey, int percent) => Shown.Add(percent);
		public void ResetProgress() => Resets++;
	}

	private static (ProgressAnnouncer P, FakeAnnouncer A, FakeSounds S, FakeDisplay D) Build(
		ProgressFeedback mode = ProgressFeedback.Both)
	{
		var a = new FakeAnnouncer(); var s = new FakeSounds();
		var d = new FakeDisplay { Mode = mode };
		return (new ProgressAnnouncer(d, a, s, new ImmediateDispatcher(), "SkyUI", "progress.downloadingName"),
			a, s, d);
	}

	[Fact]
	public void TheOperationIsNamedOnceAtTheStartRatherThanOnEveryUpdate()
	{
		var (p, a, _, _) = Build();

		p.Report(0);
		p.Report(1);
		p.Report(2);

		Assert.Single(a.Spoken);
		Assert.Contains("SkyUI", a.Spoken[0]);
	}

	[Fact]
	public void OnlyWholeDecilesAreSpoken()
	{
		// The point of the rule: a large download reports progress thousands of times, and a reader that
		// announced each one would be unusable and would drown out everything else.
		var (p, a, _, _) = Build();

		for (int i = 0; i <= 35; i++) p.Report(i);

		// The opening line, then 10, 20 and 30 - not 35 separate announcements.
		Assert.Equal(4, a.Spoken.Count);
	}

	[Fact]
	public void OneHundredPercentIsNotSpoken()
	{
		// Deliberate: the caller says "X installed!" afterwards, and "100 percent" before it is redundant.
		var (p, a, _, _) = Build();

		for (int i = 0; i <= 100; i++) p.Report(i);

		Assert.DoesNotContain(a.Spoken, t => t.Contains("100"));
	}

	[Fact]
	public void TonesOnlyModeSaysNothingAndSpeechOnlyModePlaysNothing()
	{
		var (tones, tonesA, tonesS, _) = Build(ProgressFeedback.Tones);
		for (int i = 0; i <= 30; i++) tones.Report(i);
		Assert.Empty(tonesA.Spoken);
		Assert.NotEmpty(tonesS.Tones);

		var (speech, speechA, speechS, _) = Build(ProgressFeedback.Speech);
		for (int i = 0; i <= 30; i++) speech.Report(i);
		Assert.NotEmpty(speechA.Spoken);
		Assert.Empty(speechS.Tones);
	}

	[Fact]
	public void OffMeansOffOnBothChannels()
	{
		// For users who prefer their screen reader's own progress-bar beeps.
		var (p, a, s, _) = Build(ProgressFeedback.Off);

		for (int i = 0; i <= 100; i++) p.Report(i);

		Assert.Empty(a.Spoken);
		Assert.Empty(s.Tones);
	}

	[Fact]
	public void FinishingPutsTheDisplayBackRatherThanLeavingAStalePercentage()
	{
		// A finished percentage left on the title bar is read out whole the next time anything makes the
		// reader announce the window - once, between a prompt's question and its answer.
		var (p, _, _, d) = Build();

		p.Report(50);
		p.Complete();

		Assert.Equal(1, d.Resets);
	}

	[Fact]
	public void FinishingTwiceOnlyCountsOnce()
	{
		var (p, _, s, d) = Build();

		p.Report(50);
		p.Complete();
		p.Complete();

		Assert.Equal(1, d.Resets);
		Assert.Single(s.Tones.FindAll(t => t == 100));
	}

	[Fact]
	public void APercentageOutsideTheRangeIsBroughtBackIntoIt()
	{
		var (p, _, _, d) = Build();

		p.Report(-5);
		p.Report(140);

		Assert.All(d.Shown, v => Assert.InRange(v, 0, 100));
	}
}
