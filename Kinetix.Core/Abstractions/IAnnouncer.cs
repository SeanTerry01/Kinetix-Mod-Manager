using System;

namespace KinetixModManager;

/// <summary>
/// Saying something to the person using the program.
///
/// <para>
/// This is the most important interface in the manager, because speech is not a feature here — it is the
/// output device. A sighted user who loses this seam loses a convenience; the people this program is built
/// for lose the program. Everything else in these abstractions could be got wrong and recovered from.
/// </para>
///
/// <para>
/// On Windows the implementation is Tolk, a bridge that finds whichever screen reader is running — NVDA,
/// JAWS, Window-Eyes, or SAPI as a last resort — and hands it the text. Tolk is a Windows-only native
/// library and there is no equivalent on Linux: the implementation there would talk to speech-dispatcher,
/// which is what Orca itself speaks through. Neither detail belongs anywhere in the program but the one
/// class implementing this.
/// </para>
///
/// <para>
/// The shape of the interface is taken from what the code already does across its ~537 call sites rather
/// than from what looked tidy, which is why it is this small: after all that use, the manager only ever
/// asks for four things.
/// </para>
/// </summary>
public interface IAnnouncer : IDisposable
{
	/// <summary>
	/// Says <paramref name="text"/>.
	///
	/// <paramref name="interrupt"/> cuts off whatever is being spoken first, rather than queueing behind it.
	/// That distinction carries real weight: it is how the manager takes control of the order of an
	/// announcement — a list's title before its first item, a chosen value before the row it was chosen on —
	/// instead of letting the reader finish a sentence the user has already moved past.
	///
	/// Implementations must not throw. A failure to speak is reported through <see cref="IsAvailable"/> and
	/// handled by the caller; it is never an exception, because there is no call site in the program where
	/// the right answer to "the reader did not answer" is to abandon what the user asked for.
	/// </summary>
	void Speak(string text, bool interrupt = false);

	/// <summary>
	/// Stops what is being spoken and says nothing in its place.
	///
	/// Note for anyone implementing this: on Windows it cannot tell the program's own speech from the
	/// screen reader's, so silencing also cuts off whatever the reader was saying of its own accord. Code
	/// that silences on a timer has to account for that, and there is a hard-won comment about it in
	/// <c>Form1.Helpers.cs</c>.
	/// </summary>
	void Silence();

	/// <summary>
	/// Whether speech is being produced right now.
	///
	/// Treat a <c>false</c> here as "probably not" rather than as fact. The Windows implementation can only
	/// answer reliably for its own SAPI voice; most screen readers do not report it back through the bridge
	/// at all, so callers wait on timing rather than on this. speech-dispatcher answers this properly, so a
	/// Linux implementation can do better — and code above this interface should be written so that better
	/// is an improvement rather than a behaviour change.
	/// </summary>
	bool IsSpeaking { get; }

	/// <summary>
	/// Whether anything is listening at all.
	///
	/// <c>false</c> means every <see cref="Speak"/> call is going nowhere, which for this program's users is
	/// indistinguishable from the program having frozen. Whoever holds this is responsible for saying so by
	/// some other route — see <c>ReportSpeechUnavailable</c>, which does it with a sound and a plain dialog,
	/// both of which work when this does not.
	/// </summary>
	bool IsAvailable { get; }

	// IDisposable, because a speech connection is a resource wherever it is opened: Tolk has to be unloaded,
	// and speech-dispatcher has to be closed. Disposing mid-sentence cuts the sentence off, so the shutdown
	// path says its goodbye and waits for it before letting go of this - see Form1's closing sequence.
}
