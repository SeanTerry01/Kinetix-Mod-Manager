using System;
using System.Drawing;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// In-window views: a screenful of the manager (a list, a report, a viewer) shown <em>inside</em> the main
/// window instead of as a window of its own.
///
/// A separate window costs something every time focus crosses it. Opening one makes the screen reader read the
/// new window's title before anything in it; closing one makes it read the title of the window underneath,
/// over the top of whatever was being said. Both are the reader doing its job correctly — the problem is that
/// a window was created for something that was never really a separate place.
///
/// This is the same machinery the prompts use (<see cref="RunOverlay"/>): the view is a panel over the window,
/// everything behind it is disabled, Escape closes it, and focus returns to exactly where it came from.
/// </summary>
public partial class Form1
{
	/// <summary>
	/// Shows an in-window view built by <paramref name="build"/> and returns when it closes.
	///
	/// <paramref name="build"/> is handed the container to fill and a <c>close</c> callback to call when the
	/// view is done with — the direct equivalent of a dialog's <c>Close()</c>. Whatever control it returns is
	/// the one focus starts on.
	///
	/// <paramref name="onClosed"/> runs once the view has gone, however it went — a button, or Escape. It is
	/// what a dialog would have done in its FormClosing handler, and is the place for anything that has to
	/// happen on the way out no matter which route was taken.
	/// </summary>
	private void ShowInlineView(string title, Func<Panel, Action, Control> build, Action? onClosed = null)
	{
		Form host = this;
		bool closed = false;
		void Close() => closed = true;

		var view = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = SystemColors.Control,
			Padding = new Padding(12),
			AccessibleName = title,
			AccessibleRole = AccessibleRole.Pane
		};

		var layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2
		};
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

		layout.Controls.Add(new Label
		{
			Text = title,
			Font = new Font("Segoe UI", 14f, FontStyle.Bold),
			AutoSize = true,
			Dock = DockStyle.Top,
			Margin = new Padding(0, 0, 0, 8)
		}, 0, 0);

		var content = new Panel { Dock = DockStyle.Fill };
		layout.Controls.Add(content, 0, 1);
		view.Controls.Add(layout);

		Control focusFirst = build(content, Close);

		RunOverlay(host, view, focusFirst, finished: () => closed, onEscape: Close);

		onClosed?.Invoke();
	}
}
