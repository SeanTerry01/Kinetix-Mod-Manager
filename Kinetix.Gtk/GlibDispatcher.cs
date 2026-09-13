using System;
using System.Threading;
using System.Threading.Tasks;
using KinetixModManager;

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
		if (IsOnUiThread) { action(); return; }

		GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
		{
			try { action(); }
			catch (Exception ex) { DiagnosticLog.WriteException("UI", "work posted to the main loop", ex); }
			return false;   // false = do not run again
		});
	}

	public Task<T> InvokeAsync<T>(Func<T> function)
	{
		if (IsOnUiThread) return Task.FromResult(function());

		var done = new TaskCompletionSource<T>();
		GLib.Functions.IdleAdd(GLib.Constants.PRIORITY_DEFAULT_IDLE, () =>
		{
			try { done.SetResult(function()); }
			catch (Exception ex) { done.SetException(ex); }
			return false;
		});
		return done.Task;
	}
}
