using System;
using System.Runtime.InteropServices;

namespace KinetixModManager;

/// <summary>
/// The Linux <see cref="IAnnouncer"/>: speech through speech-dispatcher, which is what Orca itself speaks
/// through and what the accessibility mods for Minecraft and Stardew Valley use on this platform.
///
/// <para>
/// It is the counterpart to <c>TolkAnnouncer</c> and it is deliberately not a port of it. Tolk hands text to
/// whichever screen reader is running; speech-dispatcher <em>is</em> the speech layer, sitting in front of
/// eSpeak NG or whatever else is configured. The practical difference for the code above this class is that
/// there is no bridge to be unloaded from underneath it, and that stopping speech is a real operation rather
/// than a best effort.
/// </para>
///
/// <para>
/// Connecting is deferred until the first thing needs saying. speech-dispatcher starts on demand, so opening
/// a connection at startup would spawn the daemon for a session that might never speak — and would put a
/// socket connection in the way of the program starting at all if it failed.
/// </para>
/// </summary>
public sealed class SpeechDispatcherAnnouncer : IAnnouncer
{
	private const string Lib = "libspeechd.so.2";

	// SPDConnectionMode: 0 = single, 1 = threaded. Threaded gives us callbacks, which we do not use yet.
	private const int ModeSingle = 0;

	// SPDPriority. IMPORTANT is not "louder" — it is the one priority that cancels what is already being
	// said, which is exactly what interrupt means at the call sites above.
	private const int PriorityImportant = 1;
	private const int PriorityText = 3;

	[DllImport(Lib, EntryPoint = "spd_open", CharSet = CharSet.Ansi)]
	private static extern IntPtr SpdOpen(string clientName, string? connectionName, string? userName, int mode);

	[DllImport(Lib, EntryPoint = "spd_close")]
	private static extern void SpdClose(IntPtr connection);

	[DllImport(Lib, EntryPoint = "spd_say", CharSet = CharSet.Ansi)]
	private static extern int SpdSay(IntPtr connection, int priority, string text);

	[DllImport(Lib, EntryPoint = "spd_cancel")]
	private static extern int SpdCancel(IntPtr connection);

	[DllImport(Lib, EntryPoint = "spd_stop")]
	private static extern int SpdStop(IntPtr connection);

	private IntPtr _connection = IntPtr.Zero;
	private bool _openFailed;
	private bool _disposed;

	private bool Connect()
	{
		if (_connection != IntPtr.Zero) return true;

		// Only tried once. A daemon that is not there will not appear because we asked a second time, and
		// retrying on every phrase would stall the interface at exactly the moment it should be responsive.
		if (_openFailed || _disposed) return false;

		try
		{
			_connection = SpdOpen("KinetixModManager", "main", null, ModeSingle);
		}
		catch (DllNotFoundException ex)
		{
			DiagnosticLog.WriteException("Speech", "loading libspeechd", ex);
			_connection = IntPtr.Zero;
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Speech", "opening a speech-dispatcher connection", ex);
			_connection = IntPtr.Zero;
		}

		if (_connection == IntPtr.Zero)
		{
			_openFailed = true;
			// The same failure as Tolk not loading, and it matters for the same reason: from here the program
			// says nothing, and to the person relying on it the entire symptom is silence.
			DiagnosticLog.Write("Speech", "speech-dispatcher did not answer; speech is unavailable this session");
		}

		return _connection != IntPtr.Zero;
	}

	public bool IsAvailable => Connect();

	/// <summary>
	/// Always false, honestly.
	///
	/// speech-dispatcher can report this properly through its threaded mode's callbacks, which this does not
	/// use yet. <see cref="IAnnouncer.IsSpeaking"/> is documented as "probably not" rather than fact for
	/// exactly this kind of reason, and the callers that need certainty wait on a timer instead — so saying
	/// false is correct-but-unhelpful rather than wrong. Worth revisiting: this is the one place where Linux
	/// can do better than Windows, not worse.
	/// </summary>
	public bool IsSpeaking => false;

	public void Speak(string text, bool interrupt = false)
	{
		if (string.IsNullOrEmpty(text) || !Connect()) return;

		try
		{
			if (interrupt) SpdCancel(_connection);
			SpdSay(_connection, interrupt ? PriorityImportant : PriorityText, text);
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Speech", "speaking", ex);
		}
	}

	public void Silence()
	{
		if (_connection == IntPtr.Zero) return;

		try { SpdStop(_connection); }
		catch (Exception ex) { DiagnosticLog.WriteException("Speech", "stopping speech", ex); }
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		if (_connection == IntPtr.Zero) return;

		try { SpdClose(_connection); }
		catch (Exception ex) { DiagnosticLog.WriteException("Speech", "closing the speech connection", ex); }
		_connection = IntPtr.Zero;
	}
}
