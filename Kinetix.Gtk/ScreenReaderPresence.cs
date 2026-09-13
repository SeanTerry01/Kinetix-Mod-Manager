using System;
using KinetixModManager;

namespace KinetixModManager.GtkHead;

/// <summary>
/// Whether a screen reader is actually running, which decides whether speech goes to it or to
/// speech-dispatcher.
///
/// <para>
/// The obvious property is a trap. <c>org.a11y.Status.ScreenReaderEnabled</c> sounds exactly like the
/// question being asked and answers <c>false</c> on a machine with Orca running and reading — it reflects
/// a desktop setting that Orca does not necessarily set, and is only reliable under a full GNOME session.
/// Trusting it would have sent every announcement to the fallback on precisely the machines that least
/// need one.
/// </para>
///
/// <para>
/// What is true is <c>org.a11y.Status.IsEnabled</c>: accessibility is switched on and the AT-SPI bus is
/// live. That is what gets checked, and a failure to answer is read as "no screen reader" so that the
/// fallback speaks rather than the program falling silent.
/// </para>
/// </summary>
public static class ScreenReaderPresence
{
	/// <summary>
	/// Asked once. A reader started after the program will not be noticed until the next launch, which is
	/// a real limitation and a small one — the alternative is a D-Bus signal subscription for a case that
	/// happens rarely, and the fallback still speaks in the meantime.
	/// </summary>
	private static bool? _cached;

	public static bool IsActive() => _cached ??= Detect();

	private static bool Detect()
	{
		try
		{
			using var proxy = Gio.DBusProxy.NewForBusSync(
				Gio.BusType.Session,
				Gio.DBusProxyFlags.DoNotAutoStart,
				null,
				"org.a11y.Bus",
				"/org/a11y/bus",
				"org.a11y.Status",
				null);

			using GLib.Variant? enabled = proxy.GetCachedProperty("IsEnabled");
			return enabled is not null && enabled.GetBoolean();
		}
		catch (Exception ex)
		{
			// No a11y bus, no session bus, or a D-Bus shape we did not expect. Assume no reader, so the
			// fallback speaks: being heard in the wrong voice is recoverable, being silent is not.
			DiagnosticLog.WriteException("Speech", "asking whether a screen reader is running", ex);
			return false;
		}
	}
}
