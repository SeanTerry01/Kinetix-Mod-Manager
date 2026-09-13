using System;
using System.Threading.Tasks;

namespace KinetixModManager;

/// <summary>
/// What a progress announcer needs from whatever is showing the progress: the user's chosen feedback mode,
/// the sentence that opens an operation, and somewhere to put a live percentage.
///
/// Three members rather than a reference to the window, because only the third of them is genuinely a
/// window's job. Splitting it this way is what let the announcer itself - the deciles, the tone throttling,
/// the rule about when it is worth crossing to the UI thread at all - move into the core, where a second
/// front end gets all of that behaviour by implementing three methods it already has.
/// </summary>
public interface IProgressDisplay
{
	/// <summary>Which channels the user wants: tones, speech, both, or neither.</summary>
	ProgressFeedback Mode { get; }

	/// <summary>The sentence that names the operation once, at the start - "Downloading SkyUI".</summary>
	string OpeningPhrase(string name, string phraseKey);

	/// <summary>Shows the live percentage somewhere the user can find it. The title bar, in the WinForms head.</summary>
	void ShowProgress(string name, string phraseKey, int percent);

	/// <summary>
	/// Puts that display back to normal once the operation is over.
	///
	/// Its own member rather than ShowProgress(100), because leaving a finished percentage on screen is not
	/// merely untidy here: the moment anything makes the screen reader read the window out, a stale
	/// "Downloading Project Fluent 100%" arrives in the middle of whatever the user is now doing. It once
	/// landed between a prompt's question and its answer.
	/// </summary>
	void ResetProgress();
}

/// <summary>
/// One place that turns a stream of percentage updates into the feedback the user actually hears and sees.
/// Implements <see cref="IProgress{T}"/> so it can be handed straight to the download and extract helpers.
/// On the first update it speaks the operation name once ("Installing My Big Mod, 0 percent"); after that it
/// speaks only bare deciles ("10 percent", "20 percent", …) so it never repeats the name. A rising synthesized
/// tone tracks the percentage continuously (throttled so a fast download doesn't machine-gun beeps), and the
/// title bar's percentage stays live throughout. Which of these channels are active is decided by
/// <see cref="IProgressDisplay.Mode"/>, so a user can pick tones, speech, both, or off (e.g. to defer to
/// their screen reader's own progress-bar beeps). All five download/install call sites share this so they
/// behave identically.
///
/// It took a Form1 until Phase 3 of the core split, which meant none of the above could be tested without
/// standing up a window. It now takes the four things it actually needs, and ProgressAnnouncerTests covers
/// the decile rule, the throttle and the reset directly.
/// </summary>
public sealed class ProgressAnnouncer : IProgress<double>
{
	private const long ToneThrottleMs = 45;

	private readonly IProgressDisplay _display;
	private readonly IAnnouncer _announcer;
	private readonly ISoundEngine _sounds;
	private readonly IDispatcher _ui;
	private readonly string _name;
	private readonly string _phraseKey;

	private bool _opened;
	private bool _done;
	private int _lastSpokenDecile = -1;
	private int _lastTonePct = -1;
	private int _lastTitlePct = -1;
	private long _lastToneTicks;

	public ProgressAnnouncer(IProgressDisplay display, IAnnouncer announcer, ISoundEngine sounds,
		IDispatcher ui, string name, string phraseKey)
	{
		_display   = display;
		_announcer = announcer;
		_sounds    = sounds;
		_ui        = ui;
		_name      = name;
		_phraseKey = phraseKey;
	}

	private ProgressFeedback Mode => _display.Mode;
	private bool TonesOn  => Mode is ProgressFeedback.Tones  or ProgressFeedback.Both;
	private bool SpeechOn => Mode is ProgressFeedback.Speech or ProgressFeedback.Both;

	/// <summary>Receives a 0–100 percentage (called on a background thread by the download/extract helpers).</summary>
	public void Report(double value)
	{
		int pct = (int)Math.Round(value);
		if (pct < 0) pct = 0; else if (pct > 100) pct = 100;

		// First update: announce the name once, prime the tone, and show the title.
		if (!_opened)
		{
			_opened = true;
			_lastSpokenDecile = 0;
			_lastTonePct = pct;
			_lastTitlePct = pct;
			_lastToneTicks = Environment.TickCount64;
			if (TonesOn) _sounds.PlayTone(pct);
			_ui.Post(() =>
			{
				if (SpeechOn)
					_announcer.Speak(_display.OpeningPhrase(_name, _phraseKey) + ", " + Loc.T("progress.percentSpoken", 0));
				_display.ShowProgress(_name, _phraseKey, pct);
			});
			return;
		}

		// Tones follow the percentage closely, but throttled so a fast download doesn't flood playback.
		if (TonesOn && pct != _lastTonePct && Environment.TickCount64 - _lastToneTicks >= ToneThrottleMs)
		{
			_lastTonePct = pct;
			_lastToneTicks = Environment.TickCount64;
			_sounds.PlayTone(pct);
		}

		// Speech is deciles only (10, 20, …, 90). 100% is left to the caller's success message / closing tone.
		int decile = (pct / 10) * 10;
		bool speakNow = SpeechOn && decile > _lastSpokenDecile && decile < 100;
		if (speakNow) _lastSpokenDecile = decile;

		// Only touch the UI thread when there is actually something to change — Report can fire thousands of
		// times for a large download, so marshaling the title on every byte would flood the message queue.
		bool titleChanged = pct != _lastTitlePct;
		if (titleChanged) _lastTitlePct = pct;
		if (!speakNow && !titleChanged) return;

		_ui.Post(() =>
		{
			if (speakNow) _announcer.Speak(Loc.T("progress.percentSpoken", decile));
			if (titleChanged) _display.ShowProgress(_name, _phraseKey, pct);
		});
	}

	/// <summary>
	/// Marks the operation finished, playing the closing 100% tone. The spoken "done" is intentionally left to
	/// the caller's own success message (e.g. "X installed!") so there is never a redundant "100 percent".
	/// </summary>
	public void Complete()
	{
		if (_done) return;
		_done = true;
		if (TonesOn) _sounds.PlayTone(100);

		// And put the title back. It was left reading "Downloading Project Fluent... 100%" long after the
		// download had finished — which is only untidy until something makes the screen reader read the
		// window out, and then it is a whole stale sentence in the middle of what the user is doing. That is
		// exactly what landed between a prompt's question and its answer.
		_ui.Post(_display.ResetProgress);
	}

}
