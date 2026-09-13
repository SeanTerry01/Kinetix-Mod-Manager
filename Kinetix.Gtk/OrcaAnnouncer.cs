using System;
using System.Runtime.InteropServices;
using KinetixModManager;

namespace KinetixModManager.GtkHead;

/// <summary>
/// The Linux <see cref="IAnnouncer"/> that a screen-reader user actually wants: it asks <em>their</em>
/// screen reader to say something, rather than saying it itself.
///
/// <para>
/// The first version of this spoke to speech-dispatcher directly, and it was wrong in a way that only
/// shows up when a real user tries it. speech-dispatcher is the layer <em>underneath</em> Orca, so going
/// straight to it produced a second voice at the daemon's default rate, in the daemon's default voice,
/// ignoring every preference the user had set in Orca — and unstoppable, because Orca's interrupt key
/// only silences Orca. The reported symptom was exactly that: "it speaks through speech dispatcher which
/// is slow speech rate and all that and I can't interrupt it."
/// </para>
///
/// <para>
/// <c>gtk_accessible_announce</c> is the right call. It posts the text through AT-SPI as an announcement
/// on the window, and Orca reads it in the user's own voice, at their own rate, obeying their own
/// interrupt key. It is the true counterpart to Tolk on Windows: Tolk does not speak either, it asks
/// NVDA or JAWS to.
/// </para>
///
/// <para>
/// speech-dispatcher remains, but only as the fallback for when no screen reader is running at all —
/// because an announcement with nothing listening is silence, and silence is this program's worst
/// failure. That is the rule the user gave, and it is the right one: everything through Orca unless
/// Orca is not there.
/// </para>
/// </summary>
public sealed class OrcaAnnouncer : IAnnouncer
{
	// GtkAccessibleAnnouncementPriority. HIGH is the assertive one: it interrupts what the reader is
	// saying, which is what interrupt: true has always meant at the call sites.
	private const uint PriorityMedium = 1;
	private const uint PriorityHigh = 2;

	[DllImport("libgtk-4.so.1", EntryPoint = "gtk_accessible_announce", CharSet = CharSet.Ansi)]
	private static extern void GtkAccessibleAnnounce(IntPtr accessible, string message, uint priority);

	private readonly Gtk.Widget _widget;
	private readonly IAnnouncer _fallback;
	private readonly bool _screenReaderRunning;

	/// <param name="widget">The window. AT-SPI announcements are posted against a widget's accessible.</param>
	/// <param name="fallback">Used only when no screen reader is running.</param>
	public OrcaAnnouncer(Gtk.Widget widget, IAnnouncer fallback)
	{
		_widget = widget;
		_fallback = fallback;
		_screenReaderRunning = ScreenReaderPresence.IsActive();

		DiagnosticLog.Write("Speech", _screenReaderRunning
			? "a screen reader is running; announcements go through AT-SPI"
			: "no screen reader detected; falling back to speech-dispatcher");
	}

	/// <summary>
	/// True either way: a screen reader is listening, or speech-dispatcher will say it instead. False only
	/// when neither is true, which is what the caller has to tell the user about by some other route.
	/// </summary>
	public bool IsAvailable => _screenReaderRunning || _fallback.IsAvailable;

	/// <summary>
	/// Always false. When Orca is speaking, Orca knows and we do not; AT-SPI offers no way to ask. This is
	/// the honest answer, and <see cref="IAnnouncer.IsSpeaking"/> is documented as "probably not" rather
	/// than fact for exactly this sort of reason.
	/// </summary>
	public bool IsSpeaking => false;

	public void Speak(string text, bool interrupt = false)
	{
		if (string.IsNullOrEmpty(text)) return;

		if (!_screenReaderRunning) { _fallback.Speak(text, interrupt); return; }

		try
		{
			GtkAccessibleAnnounce(_widget.Handle.DangerousGetHandle(), text,
				interrupt ? PriorityHigh : PriorityMedium);
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Speech", "announcing through AT-SPI", ex);
		}
	}

	/// <summary>
	/// Nothing to do when a screen reader is in charge, and that is correct rather than a gap: the user's
	/// own interrupt key silences their reader, and a program that reached in to stop their speech would
	/// be taking away a control they already have. The Windows build silences because Tolk hands us the
	/// queue; here the queue is Orca's.
	/// </summary>
	public void Silence()
	{
		if (!_screenReaderRunning) _fallback.Silence();
	}

	public void Dispose() => _fallback.Dispose();
}
