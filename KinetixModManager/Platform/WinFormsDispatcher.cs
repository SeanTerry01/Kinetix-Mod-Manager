namespace KinetixModManager;

/// <summary>
/// The WinForms <see cref="IDispatcher"/>: hops back onto the UI thread through the main window's handle.
///
/// Every method tolerates the window having gone. Closing the manager while a download or a scan is still
/// running is ordinary rather than exceptional, and posting to a disposed handle throws — which is what once
/// crashed the app on the way out, leaving nothing behind but a log nobody opened.
/// </summary>
internal sealed class WinFormsDispatcher : IDispatcher
{
	private readonly Control _owner;

	public WinFormsDispatcher(Control owner) => _owner = owner;

	public bool IsOnUiThread => !_owner.InvokeRequired;

	public void Post(Action action)
	{
		if (action == null) return;

		try
		{
			if (!_owner.IsHandleCreated || _owner.IsDisposed) return;
			if (IsOnUiThread) { action(); return; }
			_owner.BeginInvoke(action);
		}
		// The window closed while work was in flight, or its handle went between the check and the call. Nothing to post to.
		catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
		{
			DiagnosticLog.WriteException("Dispatcher", "posting to a window that has gone", ex);
		}
	}

}
