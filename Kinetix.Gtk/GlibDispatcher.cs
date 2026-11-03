namespace KinetixModManager.GtkHead;

/// <summary>
/// The GTK <see cref="IDispatcher"/>: gets work back onto the thread that owns the widgets.
///
/// GTK's rule is the same as WinForms' — touch widgets only from the thread that made them — so this is the
/// same contract with a different spelling. <c>GLib.Functions.IdleAdd</c> is the equivalent of
/// <c>Control.BeginInvoke</c>: it queues the callback on the main loop and returns at once.
/// </summary>
public sealed class GlibDispatcher : IDispatcher
{
	private readonly int _uiThreadId = Environment.CurrentManagedThreadId;

	public bool IsOnUiThread => Environment.CurrentManagedThreadId == _uiThreadId;

	public void Post(Action action)
	{
		if (action == null) return;

		// Running it here when we are already on the main loop is not merely an optimisation: queuing it
		// would defer it past whatever is posted next, and two announcements meant to be heard in order
		// would swap. The WinForms implementation makes the same point.
		//
		// Guarded just as the queued path is. After an await this head is already back on the main loop, so this is the
		// path nearly every action takes, and a throw from here landed in the caller's own catch: the backup list failing
		// to reload after a good delete was announced as the delete having failed.
		if (IsOnUiThread) { RunGuarded(action); return; }

		GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
		{
			RunGuarded(action);
			return false;   // false = do not run again
		});
	}

	private static void RunGuarded(Action action)
	{
		try { action(); }
		catch (Exception ex) { DiagnosticLog.WriteException("UI", "work posted to the main loop", ex); }
	}

}
