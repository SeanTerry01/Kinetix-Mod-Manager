namespace KinetixModManager;

/// <summary>
/// Prompts, and the timing around them. A prompt is a panel inside the window, not a window of its own: see
/// Form1.InlinePrompt. The manager speaks each question itself rather than relying on the reader to find it.
/// </summary>
public partial class Form1
{
	/// <summary>
	/// When the window last pulled itself to the foreground, or -1 if it never has this session. Set by
	/// <see cref="ForceToForeground"/>.
	/// </summary>
	private long _foregroundTakenAt = -1;

	/// <summary>
	/// How long a screen reader takes to finish reacting to a window becoming the foreground one.
	///
	/// The reader's reaction is not something the app can suppress or ask about — even a reader that can say it is
	/// speaking says no until it has begun — so it is waited out. 400ms is the figure the startup landing already
	/// uses, arrived at the same way and against the same readers.
	/// </summary>
	private const int ReaderReactionMs = 400;

	/// <summary>
	/// How much of the screen reader's reaction to a foreground grab is still to come, in milliseconds; 0 when
	/// nothing grabbed the foreground recently.
	///
	/// The reason anything needs this: a prompt raised straight after <see cref="ForceToForeground"/> was
	/// announcing only its buttons. Nothing was wrong with the prompt — the reader had been handed a
	/// foreground-change event a few milliseconds earlier, works that out on its own schedule, and speaks the
	/// result, wiping whatever the app had said in between. Speaking sooner cannot win that race, because the
	/// reader's announcement is the one that lands last. The question therefore has to wait for the window to
	/// stop being news before it is asked.
	///
	/// This is the same shape as the fix for the opening announcement at startup, and for the same reason.
	/// </summary>
	private int RemainingReaderReaction()
	{
		if (_foregroundTakenAt < 0) return 0;
		long since = Environment.TickCount64 - _foregroundTakenAt;
		return since >= ReaderReactionMs ? 0 : (int)(ReaderReactionMs - since);
	}

	/// <summary>
	/// Holds here until the screen reader has finished reacting to a foreground grab, pumping messages while it
	/// waits. Returns at once when nothing grabbed the foreground.
	///
	/// ⚠️ Called BEFORE anything is put on screen, and that placement is the whole point. Waiting afterwards
	/// looks equivalent and is not: by then focus has already landed on the new screen, and the reader has
	/// already started announcing whatever it landed on. Delaying only the app's own sentence therefore does not
	/// protect it, it merely moves it behind the reader's — a prompt read out "Yes, button, Alt+Y" and then, a
	/// beat later, the question it was answering. Waiting first means focus arrives into a settled room, and the
	/// app's sentence can interrupt the reader's reaction to that focus, which is what always made the question
	/// come first.
	///
	/// Pumped rather than slept through: the window has to keep answering Windows for the whole wait, and this
	/// runs on the UI thread. DoEvents on every pass services the queue; the short rest between passes stops it
	/// spinning a core for nothing.
	/// </summary>
	private void WaitOutReaderReaction()
	{
		long until = Environment.TickCount64 + RemainingReaderReaction();
		while (Environment.TickCount64 < until)
		{
			if (_shuttingDown || IsDisposed) return;
			Application.DoEvents();
			System.Threading.Thread.Sleep(15);
		}
	}

	/// <summary>
	/// Lets what has been said finish before something takes the focus, because a focus change throws away whatever
	/// the reader still has queued. Call it where a sentence must be heard and the next line moves focus away: a view
	/// closing, a browser opening, a prompt at the end of a run of progress lines.
	///
	/// A reader that cannot say when it has finished returns from this at once, exactly as if it were not here.
	///
	/// ⚠️ Never put a headline in front of a prompt, with this or without it. Without it the prompt's question cuts
	/// the headline off, so it is never heard. With it, the same thing is said twice. What needs saying goes in the
	/// prompt itself. A run of progress lines that happens to end at a prompt is different, and is what this is for.
	///
	/// Any key press ends the wait, and is used up doing so: the user has heard enough, and the key must not also
	/// act on a view that is about to close. See <see cref="PreFilterMessage"/>. Pumped rather than awaited because
	/// most callers are ordinary event handlers, for the same reason as <see cref="WaitOutReaderReaction"/>.
	/// </summary>
	private void LetSpeechFinish(int maxMs = 60000)
	{
		if (InvokeRequired || _announcer.IsSpeakingForCertain is null) return;

		bool alreadyHolding = _holdingForSpeech;
		if (!alreadyHolding) _speechHoldSkipped = false;
		_holdingForSpeech = true;
		try
		{
			var watch = new SpeechFinishWatch(Environment.TickCount64, maxMs);
			while (_announcer.IsSpeakingForCertain is bool speaking
				&& !watch.IsFinished(Environment.TickCount64, speaking))
			{
				if (_shuttingDown || IsDisposed || _speechHoldSkipped) return;
				Application.DoEvents();
				System.Threading.Thread.Sleep(15);
			}
		}
		finally
		{
			_holdingForSpeech = alreadyHolding;
		}
	}

	/// <summary>
	/// Keeps a view open until the rescan it started has finished, so the list is settled before focus goes back to it. A
	/// rescan runs on another thread, and one ending after the arrival moved the row and said a second position over it.
	/// Keys are held as in <see cref="LetSpeechFinish"/>, and one ends the wait.
	/// </summary>
	private void LetRefreshFinish(int maxMs = 30000)
	{
		if (InvokeRequired) return;

		bool alreadyHolding = _holdingForSpeech;
		if (!alreadyHolding) _speechHoldSkipped = false;
		_holdingForSpeech = true;
		try
		{
			long giveUpAt = Environment.TickCount64 + maxMs;
			while (Volatile.Read(ref _refreshInFlight) > 0 && Environment.TickCount64 < giveUpAt)
			{
				if (_shuttingDown || IsDisposed || _speechHoldSkipped) return;
				Application.DoEvents();
				System.Threading.Thread.Sleep(15);
			}
		}
		finally
		{
			_holdingForSpeech = alreadyHolding;
		}
	}

	/// <summary>True while <see cref="LetSpeechFinish"/> is waiting, so a key press can end the wait.</summary>
	private bool _holdingForSpeech;

	/// <summary>Set when a key press has ended the wait. See <see cref="LetSpeechFinish"/>.</summary>
	private bool _speechHoldSkipped;

	/// <summary>
	/// Speaks a modal prompt's message so the screen reader reads it reliably. The announcement is posted on a
	/// short timer instead of spoken immediately, because a modal <see cref="MessageBox"/> grabs focus the moment
	/// it shows and the screen reader's announcement of the focused button flushes any speech queued *before* the
	/// box appeared — which is why the message was lost and only "Yes"/"OK" was heard. The timer ticks on the UI
	/// message loop, which keeps pumping while the modal box is open, so the message lands reliably every time.
	///
	/// It interrupts rather than queues. What the reader says on its own when a prompt opens is the window's
	/// caption and the name of the focused button — "Yes, Alt+Y" — neither of which is the thing being asked.
	/// Queueing behind all that meant hearing the answer choices before ever hearing the question. The question
	/// now arrives first; the buttons are still there to Tab through, and are what the answer keys act on anyway.
	/// </summary>
	private void SpeakPrompt(string text)
	{
		// 200ms for the box's own focus grab, plus whatever is left of the reader's reaction if the window was
		// pulled to the front to show it.
		var timer = new System.Windows.Forms.Timer { Interval = 200 + RemainingReaderReaction() };
		timer.Tick += (s, e) =>
		{
			timer.Stop();
			timer.Dispose();
			Speak(text, interrupt: true);
		};
		timer.Start();
	}

	/// <summary>
	/// Speaks a message after a prompt has closed, once focus has settled back on what was underneath.
	///
	/// Focus going back makes the reader announce whatever it landed on, and anything said the instant the prompt returns
	/// is flushed by that. This waits for that announcement to begin, and then interrupts it, because what just happened
	/// matters more. The wait is the reader reacting rather than speaking, so isSpeaking cannot replace it.
	/// </summary>
	private void SpeakAfterPrompt(string text)
	{
		// Onto a list, the result rides on the list's return announcement, which the prompt's closing has already set off.
		// See Speak. The wait below is only for anywhere else.
		if (_promptClosedOnto is { } onto && HandToArrival(onto, text, namesRow: false)) return;

		var timer = new System.Windows.Forms.Timer { Interval = 400 };
		timer.Tick += (s, e) =>
		{
			timer.Stop();
			timer.Dispose();
			Speak(text, interrupt: true);
		};
		timer.Start();
	}

	// Every prompt goes through ShowPrompt, which lays the question over the window instead of opening one of its own.
	private DialogResult SpeakBox(string text, string? caption = null, MessageBoxButtons buttons = MessageBoxButtons.OK) =>
		ShowPrompt(text, caption ?? Loc.T("ui.appTitle"), buttons);

	/// <summary>
	/// A question whose answer changes the list it was asked from. <paramref name="actOnAnswer"/> does what the answer asks
	/// and says what to say, and the list change it hands back is made once that has been heard, before the user is put
	/// back on the list. See <see cref="ActBeforeReturning"/>. Returns null to say nothing.
	/// </summary>
	private DialogResult SpeakBox(string text, string caption, MessageBoxButtons buttons,
		Func<DialogResult, ResultBeforeReturning?> actOnAnswer) =>
		ShowPrompt(text, caption, buttons, actOnAnswer);
}
