using System;
using System.Threading.Tasks;

namespace KinetixModManager;

/// <summary>
/// Getting back onto the thread that owns the user interface.
///
/// Every toolkit has this rule and every toolkit spells it differently: WinForms has
/// <c>Control.Invoke</c> and <c>BeginInvoke</c>, GTK has <c>GLib.Idle.Add</c> and its main context. The rule
/// itself — touch widgets only from the thread that made them — is universal, so the need is core and only
/// the spelling is platform work.
///
/// It matters here more than the usual amount because so much of this program is background work that ends
/// in something being said: a download finishes, a scan completes, an update check comes back. Each of those
/// has to cross back before it can announce itself.
/// </summary>
public interface IDispatcher
{
	/// <summary>
	/// Runs <paramref name="action"/> on the UI thread and returns without waiting.
	///
	/// Implementations must swallow the case where there is no longer anywhere to post to. Closing the
	/// window while background work is in flight is ordinary, not exceptional, and it has already crashed
	/// this app once on exit — see the note on <c>MenuDeactivate</c> in <c>Form1.UI.cs</c>.
	/// </summary>
	void Post(Action action);

	/// <summary>Runs <paramref name="function"/> on the UI thread and returns what it produced.</summary>
	Task<T> InvokeAsync<T>(Func<T> function);

	/// <summary>
	/// Whether the caller is already on the UI thread, so it can skip the hop.
	///
	/// Worth asking rather than posting unconditionally: posting from the UI thread defers the work to the
	/// next message, which is enough to reorder two announcements that were meant to be heard in sequence.
	/// </summary>
	bool IsOnUiThread { get; }
}
