using System;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// Accessible wrappers around <see cref="MessageBox.Show(string)"/>. The manager speaks every prompt's message
/// through the screen reader itself (via Tolk), the same way the rest of the app announces lists and statuses,
/// rather than relying on the screen reader to auto-read the dialog — that auto-read is unreliable (and absent
/// entirely under SAPI), which is why some prompts only announced their focused button.
///
/// The message is spoken non-interrupting (queued), so it never cuts off whatever the screen reader is already
/// saying and, on the rare setup where the reader does auto-read the dialog, simply follows it. No-owner overloads
/// deliberately omit the owner so the box still parents to the active window (e.g. a modal child dialog), exactly
/// as the bare <see cref="MessageBox.Show(string)"/> calls they replaced did.
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
	/// The reader's reaction is not something the app can suppress or wait on — <c>Tolk.IsSpeaking</c> does not
	/// answer this question — so it is waited out. 400ms is the figure the startup landing already uses, arrived
	/// at the same way and against the same readers.
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
	/// Speaks a message after a modal prompt has closed, once focus has settled back on the window underneath.
	///
	/// Closing a prompt hands focus back, and the screen reader responds by re-reading that window — its title,
	/// then whatever control has focus. Anything spoken the instant the prompt returns is flushed by that, which
	/// is how the result of an action ("Deleted X. 15 searches left.") went missing while the window's title was
	/// read instead. This waits for that re-announcement to begin and then interrupts it: what just happened
	/// matters, the title of a window the user is already in does not.
	/// </summary>
	private void SpeakAfterPrompt(string text)
	{
		var timer = new System.Windows.Forms.Timer { Interval = 400 };
		timer.Tick += (s, e) =>
		{
			timer.Stop();
			timer.Dispose();
			Speak(text, interrupt: true);
		};
		timer.Start();
	}

	// Every prompt goes through ShowPrompt, which lays the question over the window that raised it instead of
	// opening one of its own. The MessageBoxIcon arguments are accepted and ignored: an icon says "warning" or
	// "question" to someone looking at it and nothing at all to someone listening, and the wording of every
	// prompt already carries that. See Form1.InlinePrompt.

	private DialogResult SpeakBox(string text) =>
		ShowPrompt(null, text, Loc.T("ui.appTitle"), MessageBoxButtons.OK);

	private DialogResult SpeakBox(string text, string caption) =>
		ShowPrompt(null, text, caption, MessageBoxButtons.OK);

	private DialogResult SpeakBox(string text, string caption, MessageBoxButtons buttons) =>
		ShowPrompt(null, text, caption, buttons);

	private DialogResult SpeakBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) =>
		ShowPrompt(null, text, caption, buttons);

	private DialogResult SpeakBox(IWin32Window owner, string text, string caption, MessageBoxButtons buttons) =>
		ShowPrompt(owner, text, caption, buttons);

	private DialogResult SpeakBox(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) =>
		ShowPrompt(owner, text, caption, buttons);
}
