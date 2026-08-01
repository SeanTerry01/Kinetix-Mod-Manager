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
		var timer = new System.Windows.Forms.Timer { Interval = 200 };
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
