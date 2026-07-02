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
	/// message loop, which keeps pumping while the modal box is open, so the message is spoken just after the
	/// dialog appears (and after the reader has read the button), landing reliably every time.
	/// </summary>
	private void SpeakPrompt(string text)
	{
		var timer = new System.Windows.Forms.Timer { Interval = 200 };
		timer.Tick += (s, e) =>
		{
			timer.Stop();
			timer.Dispose();
			Speak(text, interrupt: false);
		};
		timer.Start();
	}

	private DialogResult SpeakBox(string text)
	{
		SpeakPrompt(text);
		return MessageBox.Show(text);
	}

	private DialogResult SpeakBox(string text, string caption)
	{
		SpeakPrompt(text);
		return MessageBox.Show(text, caption);
	}

	private DialogResult SpeakBox(string text, string caption, MessageBoxButtons buttons)
	{
		SpeakPrompt(text);
		return MessageBox.Show(text, caption, buttons);
	}

	private DialogResult SpeakBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
	{
		SpeakPrompt(text);
		return MessageBox.Show(text, caption, buttons, icon);
	}

	private DialogResult SpeakBox(IWin32Window owner, string text, string caption, MessageBoxButtons buttons)
	{
		SpeakPrompt(text);
		return MessageBox.Show(owner, text, caption, buttons);
	}

	private DialogResult SpeakBox(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
	{
		SpeakPrompt(text);
		return MessageBox.Show(owner, text, caption, buttons, icon);
	}
}
