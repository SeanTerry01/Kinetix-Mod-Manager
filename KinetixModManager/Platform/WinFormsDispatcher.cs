using System;
using System.Threading.Tasks;
using System.Windows.Forms;

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
		catch (ObjectDisposedException) { /* window closed while work was in flight; nothing to post to */ }
		catch (InvalidOperationException) { /* handle went away between the check and the call */ }
	}

	public Task<T> InvokeAsync<T>(Func<T> function)
	{
		if (IsOnUiThread) return Task.FromResult(function());

		var done = new TaskCompletionSource<T>();
		try
		{
			_owner.BeginInvoke(new Action(() =>
			{
				try { done.SetResult(function()); }
				catch (Exception ex) { done.SetException(ex); }
			}));
		}
		catch (Exception ex) { done.SetException(ex); }
		return done.Task;
	}
}
