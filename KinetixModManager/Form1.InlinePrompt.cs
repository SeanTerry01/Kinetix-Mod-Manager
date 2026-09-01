using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// The manager's confirmation prompts, shown <em>inside</em> the window that raised them rather than as a
/// separate message box.
///
/// A <see cref="MessageBox"/> is a window of its own, and that is what made prompts noisy to listen to. Opening
/// one made the screen reader announce a new window — its caption, then the name of the focused button — before
/// the question was ever heard, and closing it made the reader re-read the window underneath, talking over
/// whatever the action had just reported. Neither is something the app can suppress from the outside; both stop
/// happening once no window is created.
///
/// So a prompt is now a panel laid over the window it belongs to. Focus moves into it, the rest of the window is
/// disabled behind it, and the question is spoken followed by the choice sitting under your fingers — "Delete
/// "auto" from the search history? Yes, Alt Y." The keys are unchanged: the access keys work, Enter takes the
/// focused choice, Escape cancels.
/// </summary>
public partial class Form1
{
	/// <summary>
	/// Blocks the calling thread until a message arrives in its queue. This is what a normal Windows message
	/// loop waits on, and using it keeps a prompt's nested loop idle instead of spinning.
	/// </summary>
	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern bool WaitMessage();

	/// <summary>One choice on a prompt: the label (with its &amp; access key) and what it answers.</summary>
	private readonly record struct PromptChoice(string Label, DialogResult Result);

	/// <summary>The choices a prompt offers, in order. The first is the one focus starts on.</summary>
	private static PromptChoice[] ChoicesFor(MessageBoxButtons buttons) => buttons switch
	{
		MessageBoxButtons.OKCancel => new[]
		{
			new PromptChoice(Loc.T("prompt.ok"), DialogResult.OK),
			new PromptChoice(Loc.T("prompt.cancel"), DialogResult.Cancel)
		},
		MessageBoxButtons.YesNo => new[]
		{
			new PromptChoice(Loc.T("prompt.yes"), DialogResult.Yes),
			new PromptChoice(Loc.T("prompt.no"), DialogResult.No)
		},
		MessageBoxButtons.YesNoCancel => new[]
		{
			new PromptChoice(Loc.T("prompt.yes"), DialogResult.Yes),
			new PromptChoice(Loc.T("prompt.no"), DialogResult.No),
			new PromptChoice(Loc.T("prompt.cancel"), DialogResult.Cancel)
		},
		MessageBoxButtons.RetryCancel => new[]
		{
			new PromptChoice(Loc.T("prompt.retry"), DialogResult.Retry),
			new PromptChoice(Loc.T("prompt.cancel"), DialogResult.Cancel)
		},
		MessageBoxButtons.AbortRetryIgnore => new[]
		{
			new PromptChoice(Loc.T("prompt.abort"), DialogResult.Abort),
			new PromptChoice(Loc.T("prompt.retry"), DialogResult.Retry),
			new PromptChoice(Loc.T("prompt.ignore"), DialogResult.Ignore)
		},
		_ => new[] { new PromptChoice(Loc.T("prompt.ok"), DialogResult.OK) }
	};

	/// <summary>What Escape answers: Cancel where there is one, otherwise No, otherwise the only choice.</summary>
	private static DialogResult EscapeResult(PromptChoice[] choices)
	{
		foreach (DialogResult preferred in new[] { DialogResult.Cancel, DialogResult.No })
			if (choices.Any(c => c.Result == preferred)) return preferred;
		return choices[^1].Result;
	}

	/// <summary>Collects every button under <paramref name="root"/>, in the order they were added.</summary>
	private static void CollectButtons(Control root, List<Button> found)
	{
		foreach (Control child in root.Controls)
		{
			if (child is Button button) found.Add(button);
			if (child.HasChildren) CollectButtons(child, found);
		}
	}

	/// <summary>
	/// The window a prompt should appear inside: the caller's own window if it named one, otherwise whichever
	/// window of ours is active, falling back to the main window.
	/// </summary>
	private Form? PromptHost(IWin32Window? owner)
	{
		if (owner is Form named && !named.IsDisposed && named.Visible) return named;

		Form? active = Form.ActiveForm;
		if (active != null && !active.IsDisposed && active.Visible) return active;

		return !IsDisposed && Visible ? this : null;
	}

	/// <summary>
	/// Shows a prompt inside <paramref name="owner"/> (or the active window) and waits for an answer.
	///
	/// Falls back to a real <see cref="MessageBox"/> when there is no window to host it, or when called from a
	/// background thread — a prompt that cannot be shown must never be a prompt that is silently skipped.
	/// </summary>
	private DialogResult ShowPrompt(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons)
	{
		Form? host = PromptHost(owner);
		if (host == null || host.InvokeRequired)
		{
			SpeakPrompt(text);
			return MessageBox.Show(text, caption, buttons);
		}

		// Before the prompt exists at all. See WaitOutReaderReaction: once the buttons are on screen and one of
		// them has focus, the reader is already talking about it, and no amount of delaying our own sentence puts
		// it back in front.
		WaitOutReaderReaction();

		PromptChoice[] choices = ChoicesFor(buttons);
		DialogResult? answer = null;

		Panel overlay = BuildPromptPanel(text, caption, choices, result => answer = result, out Button firstButton);

		RunOverlay(host, overlay, firstButton,
			finished: () => answer != null,
			onEscape: () => answer = EscapeResult(choices),
			// The question, at once and interrupting. Focus has just landed on the first choice and the reader
			// has begun saying so; cutting that off is exactly what puts the question first. Restoring the
			// buttons' real names a moment later is then what makes the reader announce the focused choice —
			// "Yes, button, Alt+Y" — after the question rather than in front of it.
			afterShown: () =>
			{
				SpeakPromptQuestion(text);
				RestoreChoiceNames(overlay, choices);
			});

		return answer ?? EscapeResult(choices);
	}

	/// <summary>How many overlays (prompts or in-window views) are currently up. See <see cref="RunOverlay"/>.</summary>
	private int _overlayDepth;

	/// <summary>True while a prompt or an in-window view is covering the window.</summary>
	private bool OverlayIsOpen => _overlayDepth > 0;

	/// <summary>
	/// Puts <paramref name="overlay"/> over <paramref name="host"/>, hands it the keyboard, and waits until
	/// <paramref name="finished"/> says it is done — then puts the window back exactly as it was. This is the
	/// whole of what makes something modal without being a window, and both the prompts and the in-window views
	/// are built on it.
	/// </summary>
	private void RunOverlay(Form host, Panel overlay, Control focusFirst,
		Func<bool> finished, Action onEscape, Action? afterShown = null)
	{
		// Remember what to put back: the overlay borrows the window's focus, its Enter/Escape handling, its
		// name and the enabled state of everything already in it.
		Control? focusBefore = host.ActiveControl;
		bool keyPreviewBefore = host.KeyPreview;
		IButtonControl? acceptBefore = host.AcceptButton;
		IButtonControl? cancelBefore = host.CancelButton;
		string? hostNameBefore = host.AccessibleName;
		var disabled = new List<Control>();

		_overlayDepth++;
		try
		{
			// The host's own Escape/Enter handling must not fire while the overlay is up — several windows close
			// themselves on Escape, which would otherwise close the window out from under what is on top of it.
			// Escape is delivered to the overlay's own controls instead, below.
			host.KeyPreview = false;
			host.AcceptButton = null;
			host.CancelButton = null;

			// The window answers to nothing while something is over it. A screen reader that is made to describe
			// the window — by focus touching the form, or by the window being pulled to the front — reads its
			// accessible name, and with none it falls back to the caption. That caption is the title bar, which
			// carries the game and the live status, so the reader announced a whole sentence of it between a
			// prompt's question and its answer. An in-window view has done this for a while; a prompt had not.
			host.AccessibleName = " ";

			host.Controls.Add(overlay);
			overlay.BringToFront();
			StylePromptPanel(overlay);
			AttachEscape(overlay, onEscape);

			// Focus moves into the overlay BEFORE anything behind it is disabled. Disabling the control that
			// currently has focus makes the screen reader announce it as "unavailable" — heard as a stray word
			// in front of whatever the overlay was opened to say. Moving focus out of the way first means the
			// control being switched off is not the one being spoken about.
			if (!focusFirst.IsDisposed && focusFirst.CanFocus) focusFirst.Focus();

			// Disable what is already there rather than hiding it, so nothing behind the overlay can be reached
			// by keyboard. Only controls that were enabled are recorded, so nothing gets switched ON afterwards
			// that the window had deliberately switched off.
			foreach (Control existing in host.Controls)
			{
				if (existing == overlay || !existing.Enabled) continue;
				existing.Enabled = false;
				disabled.Add(existing);
			}

			afterShown?.Invoke();

			// A nested message loop. It ends when the caller says so, or if the window underneath goes away.
			//
			// The wait between rounds is WaitMessage, NOT a sleep. Sleeping leaves the thread holding the
			// message queue while not pumping it, so for that whole slice the window answers nothing Windows
			// asks of it — which is felt as everything crawling, right down to Alt+Tab, because switching
			// windows needs this one to respond. WaitMessage instead parks the thread in the same wait a normal
			// message loop uses: nothing is burned while idle, and the moment a key, click or timer arrives it
			// wakes and is pumped immediately.
			while (!finished())
			{
				if (host.IsDisposed || !host.Visible) { onEscape(); break; }
				Application.DoEvents();
				if (!finished() && !host.IsDisposed) WaitMessage();
			}
		}
		finally
		{
			_overlayDepth--;
			try
			{
				if (!host.IsDisposed)
				{
					// Focus has to leave the overlay BEFORE the overlay leaves the window. Removing a container
					// while something inside it still has focus makes WinForms park that control's handle on its
					// hidden holding window — and the focus goes with it, so the screen reader dutifully
					// announced "WindowsFormsParkingWindow". Clearing the active control moves focus to the form
					// without focusing anything in particular, which raises no GotFocus and so leaves the
					// carefully ordered announcements below exactly as they were.
					if (host.ActiveControl != null && overlay.Contains(host.ActiveControl))
						host.ActiveControl = null;

					host.Controls.Remove(overlay);
					foreach (Control restore in disabled)
						if (!restore.IsDisposed) restore.Enabled = true;

					host.KeyPreview = keyPreviewBefore;
					host.AcceptButton = acceptBefore;
					host.CancelButton = cancelBefore;

					if (focusBefore != null && !focusBefore.IsDisposed && focusBefore.CanFocus)
					{
						// Going back to a list is a focus change the user did not make, and a screen reader
						// announces nothing for those — it sees focus as never having left. Without this, closing
						// a view read out "twelve of a hundred and forty-seven" and never said which mod that
						// was; the only way to hear it was to arrow off the row and back. The flag is consumed by
						// whichever of the list's handlers speaks first, and only ever by a list.
						ListBox? returningTo = focusBefore as ListBox;
						if (returningTo != null)
						{
							// The list must already be on the right mod before anything looks at it. A view that
							// rebuilt the list on its way out leaves the selection somewhere else entirely, and
							// putting it back after the overlay closed — which is where the callers used to do it —
							// is one step too late: focus has already returned by then, so both the screen reader
							// and our own announcement describe the mod the list happened to be sitting on, and the
							// correction that follows arrives as a second announcement cutting off the first.
							if (ReferenceEquals(returningTo, listInstalled))
							{
								ReselectMod(_restoreInstalledSelectionTo, announce: false);
								_restoreInstalledSelectionTo = null;
							}

							// Before focus lands, not after: the reader reads whichever row the list calls current
							// the moment focus arrives, so correcting it afterwards would be too late to stop the
							// wrong mod being announced.
							AlignListCaretToSelection(returningTo);
							_announceRowNameOnNextChange = true;
							// Focus is arriving, not just moving inside a list already under it, so the announcement
							// opens with the list's name — "Installed Mods List" — as it would have done had the user
							// tabbed in. The reader's own attempt at that gets swallowed along with the wrong row it
							// insists on reading first, so ours has to carry the name or the list stops saying what
							// it is on the way back from a view.
							_announceListNameOnNextChange = true;
						}
						focusBefore.Focus();
						// And again once focus is really there, in case taking focus is itself what disturbed it.
						// Sending the same row twice raises no events and costs nothing.
						if (returningTo != null) AlignListCaretToSelection(returningTo);

						// Focus does not always come back from anywhere: removing the overlay can leave it on the
						// list already, and focusing a control that already has focus raises no GotFocus, so the
						// announcement set up above would never be spent and the list would come back in silence.
						// The flag still being set is exactly the evidence that nothing announced the arrival, so
						// announce it here instead. If GotFocus did fire it consumed the flag and this does nothing.
						if (returningTo != null && _announceListNameOnNextChange)
							List_Enter(returningTo, EventArgs.Empty);
					}
				}
			}
			catch (Exception ex) { DiagnosticLog.WriteException("UI", "putting the window back after a prompt closed", ex); }
			if (!host.IsDisposed) host.AccessibleName = hostNameBefore;
			overlay.Dispose();
		}
	}

	/// <summary>
	/// Wires Escape on every control inside an overlay. The window's own key preview is switched off while an
	/// overlay is up, so Escape has to be caught on the controls that can actually hold focus — and it must work
	/// from all of them, not just whichever one happens to be first.
	/// </summary>
	private static void AttachEscape(Control root, Action onEscape)
	{
		foreach (Control child in root.Controls)
		{
			child.KeyDown += delegate (object? s, KeyEventArgs e)
			{
				// A control that has already dealt with Escape keeps it. This is what lets a view put itself
				// into a mode Escape should leave rather than close out of — capturing a keystroke, say.
				// Handlers added while the view was built run before this one, so their Handled flag is seen.
				if (e.Handled || e.KeyCode != Keys.Escape) return;
				e.Handled = true;
				e.SuppressKeyPress = true;
				onEscape();
			};
			if (child.HasChildren) AttachEscape(child, onEscape);
		}
	}

	/// <summary>Builds the prompt's panel: the caption, the question, and a row of choices.</summary>
	private Panel BuildPromptPanel(string text, string caption, PromptChoice[] choices,
		Action<DialogResult> answer, out Button firstButton)
	{
		var overlay = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = SystemColors.Control,
			Padding = new Padding(20),
			// Named so the screen reader has something sensible if it ever reaches the container itself.
			AccessibleName = caption,
			AccessibleRole = AccessibleRole.Pane
		};

		var layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3
		};
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

		layout.Controls.Add(new Label
		{
			Text = caption,
			Font = new Font("Segoe UI", 14f, FontStyle.Bold),
			AutoSize = true,
			Dock = DockStyle.Top,
			Margin = new Padding(0, 0, 0, 10)
		}, 0, 0);

		layout.Controls.Add(new Label
		{
			Text = text,
			Font = new Font("Segoe UI", 12f),
			Dock = DockStyle.Fill,
			AccessibleName = text
		}, 0, 1);

		var row = new FlowLayoutPanel
		{
			Dock = DockStyle.Bottom,
			FlowDirection = FlowDirection.LeftToRight,
			AutoSize = true,
			WrapContents = false
		};

		DialogResult escape = EscapeResult(choices);
		Button? first = null;

		foreach (PromptChoice choice in choices)
		{
			// Named blank for now, and it is worth being honest about what that does and does not achieve.
			//
			// It does NOT silence the button. A screen reader with no accessible name to read falls back to the
			// control's visible text, so focus landing here still produces "Yes, button, Alt+Y" — proved by a
			// user's own NVDA speech history, which showed exactly that line arriving ahead of the question.
			// What the blank name buys is a SHORTER announcement to interrupt, and a name change a moment later
			// (see RestoreChoiceNames) that the reader reports — which is what puts the choice AFTER the question
			// instead of in front of it.
			//
			// The thing that actually orders these is the interrupt in SpeakPromptQuestion, fired the instant
			// focus lands. Anything that delays it puts the button first again.
			var button = new Button
			{
				Text = choice.Label,
				AutoSize = true,
				MinimumSize = new Size(120, 42),
				Font = new Font("Segoe UI", 11f, FontStyle.Bold),
				Margin = new Padding(0, 0, 10, 0),
				AccessibleName = " "
			};
			DialogResult chosen = choice.Result;
			button.Click += delegate { answer(chosen); };
			button.KeyDown += delegate (object? s, KeyEventArgs e)
			{
				// Enter is handled here rather than left to the window's default button, because the prompt
				// deliberately clears that while it is up — otherwise Enter would fire whatever the window
				// behind considers its default action.
				if (e.KeyCode == Keys.Enter)
				{
					e.Handled = true;
					e.SuppressKeyPress = true;
					answer(chosen);
					return;
				}
				if (e.KeyCode != Keys.Escape) return;
				e.Handled = true;
				e.SuppressKeyPress = true;
				answer(escape);
			};
			row.Controls.Add(button);
			first ??= button;
		}

		layout.Controls.Add(row, 0, 2);
		overlay.Controls.Add(layout);

		firstButton = first!;
		return overlay;
	}

	/// <summary>Applies the user's contrast and text-size settings to a prompt, as dialogs get.</summary>
	private void StylePromptPanel(Panel overlay)
	{
		if (_settings.DisplayContrast == DisplayContrast.Off && _settings.TextSize == TextSize.Normal) return;
		var colors = ContrastColors(_settings.DisplayContrast);
		float factor = TextScaleFactor();
		if (colors is { } c) { overlay.BackColor = c.Back; overlay.ForeColor = c.Fore; }
		var scratch = new Dictionary<Control, float>();
		foreach (Control child in overlay.Controls)
			ThemeControlTree(child, colors, factor, scratch);
	}

	/// <summary>
	/// Speaks a prompt's question. Immediately in the ordinary case: the choice buttons start out unnamed (see
	/// BuildPromptPanel), so there is nothing from the screen reader to talk over or be cut off by.
	///
	/// The exception is a prompt raised just after the manager pulled itself to the front — the question asked
	/// when a browser download lands. The reader is working out what to say about the new foreground window while
	/// this runs, and finishes after it, wiping the question and leaving only the choices; that was heard as a
	/// prompt that read out "Yes. No." and never said what it was asking. Speaking earlier cannot win that race,
	/// so the question waits for it to pass instead. See <see cref="WhenReaderHasSettled"/>.
	///
	/// Just the question — the choice is not appended. Giving the buttons their names back a moment later is
	/// itself a change the screen reader reports, so it announces "Yes, Alt Y" on its own straight afterwards;
	/// saying it here as well had it read out twice.
	/// </summary>
	private void SpeakPromptQuestion(string text) => Speak(text, interrupt: true);

	/// <summary>
	/// Gives the choice buttons their real accessible names back, shortly after the prompt has been announced.
	///
	/// They open unnamed so the reader stays quiet while the question is read. By the time this runs the reader
	/// has already taken the name it was going to announce for the initial focus, so restoring them now changes
	/// nothing that has been said — it only means Tabbing between the choices from here on announces "Yes" and
	/// "No" as it should.
	/// </summary>
	private static void RestoreChoiceNames(Control root, PromptChoice[] choices)
	{
		var timer = new System.Windows.Forms.Timer { Interval = 700 };
		timer.Tick += (s, e) =>
		{
			timer.Stop();
			timer.Dispose();
			try
			{
				var buttons = new List<Button>();
				CollectButtons(root, buttons);
				for (int i = 0; i < buttons.Count && i < choices.Length; i++)
					buttons[i].AccessibleName = choices[i].Label.Replace("&", "");
			}
			catch (Exception ex) { DiagnosticLog.WriteException("UI", "restoring the names of a prompt's buttons", ex); }
		};
		timer.Start();
	}
}
