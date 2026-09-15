using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// Lists you work through rather than pick from.
///
/// <para>
/// Sean's rule, 2026-09-15: a list you can <em>do</em> things in — install a download, link a mod to its page — leaves
/// you where you were until there is nothing left in it. Those lists used to close before running the action, so
/// every mod fixed meant opening the list again and finding your place. A list you <em>choose</em> from (a search
/// result, a value, a profile) still closes, because choosing is what it is for.
/// </para>
///
/// <para>
/// <b>The ordering problem this solves.</b> An action usually opens something over the list — a confirmation, the
/// install wizard, a second list to pick from — and when that closes, focus comes back to the list and the row under
/// the cursor is read out. If the list is only brought up to date afterwards, the row read out is the one just dealt
/// with, and the correction arrives as a second announcement cutting off the first. That is the same order bug that
/// cost eight attempts on the installed mods list (see <c>RunOverlay</c>'s <c>_restoreInstalledSelectionTo</c>). So a
/// staying list registers a refresh here, and <c>RunOverlay</c> runs it before focus lands — while the list is still
/// unfocused, where rebuilding it says nothing.
/// </para>
/// </summary>
public partial class Form1
{
	/// <summary>
	/// Refreshes to run just before focus returns to a list from anything opened over it. Each answers whether focus
	/// should still go back there: false means the list has closed itself (it emptied, or it was a picker whose job
	/// is done), and the view closing will put focus back where it came from instead.
	/// </summary>
	private readonly Dictionary<ListBox, Func<bool>> _refreshBeforeFocusReturns = new();

	/// <summary>Registers <paramref name="refresh"/> for <paramref name="list"/> for as long as the list exists.</summary>
	private void RefreshBeforeFocusReturns(ListBox list, Func<bool> refresh)
	{
		_refreshBeforeFocusReturns[list] = refresh;
		list.Disposed += (_, _) => _refreshBeforeFocusReturns.Remove(list);
	}

	/// <summary>
	/// Runs the refresh registered for a list focus is about to return to, and says whether focus should still go
	/// there. A list with nothing registered always wants it back; a refresh that fails is logged and keeps the list.
	/// </summary>
	private bool RefreshListFocusReturnsTo(Control? focusBefore)
	{
		if (focusBefore is not ListBox list || !_refreshBeforeFocusReturns.TryGetValue(list, out Func<bool>? refresh))
			return true;
		try { return refresh(); }
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("UI", "bringing a list up to date before returning to it", ex);
			return true;
		}
	}

	/// <summary>
	/// Replaces a list's rows without announcing anything, keeping the cursor on the same row when it is still there
	/// and on the same position when it is not — the row that took the place of the one just dealt with.
	/// </summary>
	/// <param name="sameRow">Whether two rows stand for the same thing; rows are usually rebuilt, so not by reference.</param>
	private void ReplaceRowsSilently<T>(ListBox list, IReadOnlyList<T> rows, Func<T, T, bool> sameRow) where T : class
	{
		int at = list.SelectedIndex;
		T? was = list.SelectedItem as T;

		_movingListSilently = true;
		try
		{
			list.BeginUpdate();
			list.Items.Clear();
			foreach (T row in rows) list.Items.Add(row);
			list.EndUpdate();

			if (list.Items.Count == 0) return;
			int same = was == null ? -1 : rows.ToList().FindIndex(r => sameRow(r, was));
			list.SelectedIndex = same >= 0 ? same : Math.Clamp(at, 0, list.Items.Count - 1);
			AlignListCaretToSelection(list);
		}
		finally { _movingListSilently = false; }
	}
}
