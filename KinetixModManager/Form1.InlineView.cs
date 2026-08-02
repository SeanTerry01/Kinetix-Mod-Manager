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
	/// <summary>The margin around an in-window view, and the gap the painted heading sits in.</summary>
	private const int MARGIN = 12;

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

		// The heading is PAINTED, not built as a control — see the comment on MARGIN below for why nothing that
		// holds the title as text can live in this view.
		var headingFont = new Font("Segoe UI", 14f * TextScaleFactor(), FontStyle.Bold);
		int headingHeight = TextRenderer.MeasureText(title, headingFont).Height + 8;

		var view = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = SystemColors.Control,
			// Room at the top for the painted heading, then the usual margin on every side.
			Padding = new Padding(MARGIN, MARGIN + headingHeight, MARGIN, MARGIN),
			// Deliberately unnamed. A named container is announced by the screen reader every time focus enters
			// it — which for a view built from tabs or several controls meant hearing the view's title again on
			// every Tab and every tab change. A Panel has no visible Text for the reader to fall back to, so a
			// blank name really does silence it.
			AccessibleName = " ",
			AccessibleRole = AccessibleRole.Pane
		};

		// The title, drawn straight onto the panel.
		//
		// This started as a Label, and the screen reader read it out again on every Tab press and every tab
		// change in Settings. Two things were tried and neither stopped it: blanking AccessibleName (Windows
		// treats a blank name as "none given" and falls back to the control's visible Text), and overriding the
		// control's accessibility object to answer an empty name and no role. What remains after both is the
		// label's own window text — a Label is a real window, its caption is the title string, and a reader that
		// finds no accessible name falls back to asking the window what its text is. There is no way to leave the
		// text on a control and keep it from being reachable.
		//
		// So the view does not contain the title at all. Painted pixels have nothing to announce, and the heading
		// still reads normally on screen. Its size is applied here because the display theme scales the fonts of
		// controls, and this is no longer one.
		view.Paint += delegate (object? s, PaintEventArgs e)
		{
			TextRenderer.DrawText(e.Graphics, title, headingFont, new Point(MARGIN, MARGIN / 2), view.ForeColor);
		};
		view.Disposed += delegate { headingFont.Dispose(); };

		var content = new Panel { Dock = DockStyle.Fill };
		view.Controls.Add(content);

		Control focusFirst = build(content, Close);
		SilenceUnnamedContainers(view);

		// The view says its name once, on the way in — "Settings", then the reader's own announcement of whatever
		// focus landed on. This is the one announcement the title is worth: it tells the user which screen opened.
		// It is said deliberately, once, rather than left for the reader to borrow from a heading on every focus
		// change, which is what made the title such noise before. Queued rather than interrupting, so it follows
		// whatever was being said as the view opened instead of cutting it off.
		RunOverlay(host, view, focusFirst, finished: () => closed, onEscape: Close,
			afterShown: () => Speak(title));

		onClosed?.Invoke();
	}

	/// <summary>
	/// Gives every unnamed container inside a view a blank accessible name.
	///
	/// A container with no name of its own is not simply left unnamed by the screen reader — the reader goes
	/// looking for one nearby, and what is nearby is the main window sitting behind the overlay. That is how the
	/// Settings tab strip came to be announced as "Search:", the label of the main window's search box: the tab
	/// strip had no name, so the reader borrowed the nearest one it could find. Before the heading was removed it
	/// borrowed that instead, which is why the view kept saying its own title on every tab change.
	///
	/// A blank name is still a name, so the search stops there. These containers only group controls on screen and
	/// have nothing of their own to say; the controls inside them are named individually.
	///
	/// TabPages are left alone — a page's Text is the name of its tab, and blanking it would leave the tabs
	/// themselves unnamed. Anything the view named deliberately is left alone too.
	/// </summary>
	private static void SilenceUnnamedContainers(Control root)
	{
		foreach (Control child in root.Controls)
		{
			// TabPage derives from Panel, hence the explicit exception.
			if (string.IsNullOrEmpty(child.AccessibleName) && child is Panel or TabControl && child is not TabPage)
				child.AccessibleName = " ";

			if (child.HasChildren) SilenceUnnamedContainers(child);
		}
	}
}
