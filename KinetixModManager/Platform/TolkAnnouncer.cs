using System;
using DavyKager;

namespace KinetixModManager;

/// <summary>
/// The Windows <see cref="IAnnouncer"/>: speech through Tolk, which finds whichever screen reader is
/// running and hands it the text.
///
/// <para>
/// Tolk is a native DLL loaded by P/Invoke and resolved from <c>lib\</c> at startup. Everything awkward
/// about it is kept in this one class so that nothing above the interface has to know: that it can be
/// unloaded from underneath the program when NVDA restarts, that <see cref="IsSpeaking"/> is only truthful
/// about its own SAPI voice, and that <see cref="Silence"/> cannot tell the program's speech from the
/// reader's own.
/// </para>
///
/// <para>
/// Nothing here throws. A screen-reader bridge that stops answering is a condition to report, not an
/// exception to propagate — the caller's right response is to tell the user some other way, never to
/// abandon whatever they were doing.
/// </para>
/// </summary>
internal sealed class TolkAnnouncer : IAnnouncer
{
	/// <summary>
	/// Reloads Tolk when it has gone away, which happens in ordinary use: restarting NVDA takes the bridge
	/// with it, and without this the manager would fall permanently silent after something the user did
	/// deliberately and would not connect to the symptom.
	/// </summary>
	private static void EnsureLoaded()
	{
		if (Tolk.IsLoaded()) return;

		try
		{
			Tolk.Load();
			Tolk.TrySAPI(trySAPI: true);
		}
		catch (Exception ex)
		{
			// Worth recording above almost anything else here: a bridge that will not load means the app
			// says nothing at all from this point on, and for the person relying on it the entire symptom
			// is silence — with no error to see, by definition.
			DiagnosticLog.WriteException("Speech", "reloading the screen-reader bridge", ex);
		}
	}

	public bool IsAvailable
	{
		get
		{
			EnsureLoaded();
			return Tolk.IsLoaded();
		}
	}

	public bool IsSpeaking
	{
		get
		{
			// Tolk answers this reliably only for its own SAPI voice; most readers do not report it back at
			// all. False therefore means "probably not", and callers that need certainty use timing instead.
			try { return Tolk.IsLoaded() && Tolk.IsSpeaking(); }
			catch { return false; }
		}
	}

	public void Speak(string text, bool interrupt = false)
	{
		EnsureLoaded();
		if (!Tolk.IsLoaded()) return;

		try { Tolk.Output(text, interrupt); }
		catch (Exception ex) { DiagnosticLog.WriteException("Speech", "speaking", ex); }
	}

	public void Silence()
	{
		if (!Tolk.IsLoaded()) return;

		try { Tolk.Silence(); }
		catch (Exception ex) { DiagnosticLog.WriteException("Speech", "silencing the reader", ex); }
	}

	/// <summary>
	/// Releases the bridge. Call it last: unloading Tolk mid-sentence cuts the sentence off, which is why the
	/// shutdown path speaks its goodbye and waits for it before disposing this.
	/// </summary>
	public void Dispose()
	{
		if (!Tolk.IsLoaded()) return;

		try { Tolk.Unload(); }
		catch (Exception ex) { DiagnosticLog.WriteException("Speech", "unloading the screen-reader bridge", ex); }
	}
}
