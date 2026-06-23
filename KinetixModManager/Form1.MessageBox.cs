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
	private DialogResult SpeakBox(string text)
	{
		Speak(text, interrupt: false);
		return MessageBox.Show(text);
	}

	private DialogResult SpeakBox(string text, string caption)
	{
		Speak(text, interrupt: false);
		return MessageBox.Show(text, caption);
	}

	private DialogResult SpeakBox(string text, string caption, MessageBoxButtons buttons)
	{
		Speak(text, interrupt: false);
		return MessageBox.Show(text, caption, buttons);
	}

	private DialogResult SpeakBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
	{
		Speak(text, interrupt: false);
		return MessageBox.Show(text, caption, buttons, icon);
	}

	private DialogResult SpeakBox(IWin32Window owner, string text, string caption, MessageBoxButtons buttons)
	{
		Speak(text, interrupt: false);
		return MessageBox.Show(owner, text, caption, buttons);
	}

	private DialogResult SpeakBox(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
	{
		Speak(text, interrupt: false);
		return MessageBox.Show(owner, text, caption, buttons, icon);
	}
}
