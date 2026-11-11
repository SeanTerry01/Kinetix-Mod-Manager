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
	///
	/// <paramref name="hint"/> is spoken once after the title, for a view whose keys are worth a word on the way
	/// in — the drill-downs say how to move between levels. It must not be spoken by the view itself: anything
	/// said while <paramref name="build"/> runs lands before the title.
	/// </summary>
	/// <param name="whyEscapeWaits">What to say instead of closing, while there is a reason to stay open. Null to close.</param>
	/// <param name="sayOnClosing">
	/// The result of the view, asked for once it has been closed: "Settings saved". It comes first, ahead of the reader
	/// describing the window the view goes back to, and is heard out before that starts, because the focus going back
	/// throws away whatever is still being said. A reader that can't say when it has finished hears it after instead, as
	/// waiting would be a guess, and saying it first would lose it.
	/// </param>
	/// <param name="actBeforeReturning">
	/// For a view whose result changes the list it was opened from: asked once the view has been closed, and its result
	/// said and its change made before focus goes back. See <see cref="ActBeforeReturning"/>.
	/// </param>
	private void ShowInlineView(string title, Func<Panel, Action, Control> build, Action? onClosed = null,
		string? hint = null, Func<string?>? whyEscapeWaits = null, Func<string?>? sayOnClosing = null,
		Func<ResultBeforeReturning?>? actBeforeReturning = null)
	{
		// Before the view is built, for the same reason a prompt waits before its buttons appear: once focus has
		// landed inside the view the reader is already describing what it landed on.
		//
		// The opening key's character goes first of all, before the wait pumps it to the control behind. See
		// DropCharacterOfOpeningKey: Ctrl+P typed a P into the mod list.
		BeginOpeningOverlay();
		WaitOutReaderReaction();

		Form host = this;
		bool closed = false;
		void Close() => closed = true;
		bool saidOnClosing = false;
		bool acted = false;

		// The heading is PAINTED, not built as a control. See the comment on the Paint handler below for why.
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

		// Shift+F1 works in every view. A view with help of its own attached it above, and that one takes the key first.
		AttachViewHelp(view, () => SpeakLong(string.IsNullOrEmpty(hint) ? Loc.T("view.defaultHelp") : hint, interrupt: true));

		// A view that moves focus itself — the drill-downs bounce it through the window to force the reader to
		// re-read a rebuilt list — leaves focus on the form for an instant, and the reader announces the form by
		// name when that happens. Unnamed, it would fall back to the window's caption, so drilling in would say
		// "Kinetix Mod Manager" every time. RunOverlay blanks it for the whole time anything is over the window,
		// prompts included, and puts it back exactly as found.
		//
		// The view says its name once, on the way in — "Settings", then the reader's own announcement of
		// whatever focus landed on. This is the one announcement the title is worth: it tells the user which
		// screen opened. It is said deliberately, once, rather than left for the reader to borrow from a
		// heading on every focus change, which is what made the title such noise before. Queued rather than
		// interrupting, so it follows whatever was being said as the view opened instead of cutting it off.
		RunOverlay(host, view, focusFirst, finished: () => closed,
			onEscape: () =>
			{
				if (whyEscapeWaits?.Invoke() is { } reason) Speak(reason);
				else Close();
			},
			afterShown: () =>
			{
				Speak(title);
				// SpeakLong, not Speak: a hint is usually a sentence, but a view whose opening IS its content
				// (a mod's description, an AI answer) passes a passage, and a screen reader silently truncates a
				// single very long utterance. Chunked and queued it reads whole, and a short hint is one chunk,
				// so nothing changes for the ordinary case.
				if (!string.IsNullOrEmpty(hint)) SpeakLong(hint, interrupt: false);
			},
			beforeRestoring: () =>
			{
				if (actBeforeReturning != null) acted = ActBeforeReturning(actBeforeReturning);
				if (_announcer.IsSpeakingForCertain is null || sayOnClosing?.Invoke() is not { } first) return;
				saidOnClosing = true;
				Speak(first);
				LetSpeechFinish();
			});

		onClosed?.Invoke();
		if (actBeforeReturning != null && !acted) ActAfterReturning(actBeforeReturning);

		// As a prompt does: what is said straight after this returns, onto a list, rides on the list's return announcement
		// rather than talking over the reader and losing the position. A fallback, for results not yet said before
		// returning; those that are (sayOnClosing, actBeforeReturning) leave nothing for it. See Speak and ShowPrompt.
		if (Control.FromHandle(GetFocus()) is ListBox onto && ReferenceEquals(_arrivalPendingFor, onto) && !IsDisposed)
		{
			_promptClosedOnto = onto;
			BeginInvoke(new Action(() => { if (ReferenceEquals(_promptClosedOnto, onto)) _promptClosedOnto = null; }));
		}

		if (!saidOnClosing && sayOnClosing?.Invoke() is { } after) Speak(after);
	}

	/// <summary>
	/// Asks for a line of text inside the window, returning what was typed or <c>null</c> if it was cancelled.
	///
	/// The in-window replacement for <c>Interaction.InputBox</c>, which opened a window of its own and so cost the
	/// spoken window title on the way in and another on the way out — and whose text box carried no accessible
	/// name, so a screen reader announced it as nothing but "edit" followed by "OK", with the question never
	/// attached to the field being answered. As of 2026-08-17 every caller has moved here and none remain.
	///
	/// Enter accepts, Escape cancels. Cancelling is distinct from clearing the box, which InputBox cannot express
	/// — it reports an emptied box and a cancelled one identically, so callers there have to treat "" as "leave
	/// it alone" and can never accept a deliberately empty answer.
	/// </summary>
	/// <param name="secret">
	/// For keys and passwords: the box is protected, with a Show key button after it. See
	/// <see cref="WithShowHideButton"/>.
	/// </param>
	/// <param name="actOnAnswer">As for <see cref="ShowChoiceList"/>: applies what was typed and says what to say.</param>
	/// <param name="sayOnCancel">
	/// What to say when the box is left with Escape or Cancel — usually "Changes cancelled." Said the way a result is, before
	/// focus goes back (see ShowInlineView's sayOnClosing). A plain Speak after the box returned talked over the reader's
	/// reading of the row, and dropped its position.
	/// </param>
	private string? ShowTextPrompt(string title, string prompt, string initial, bool secret = false,
		Func<string, ResultBeforeReturning?>? actOnAnswer = null, string? sayOnCancel = null)
	{
		string? answer = null;

		ShowInlineView(title, (container, closeView) =>
		{
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			layout.Controls.Add(new Label
			{
				Text = prompt,
				AutoSize = true,
				Font = new Font("Segoe UI", 11f),
				Margin = new Padding(0, 0, 0, 8)
			}, 0, 0);

			// The prompt is the box's accessible name, so the reader says what is being asked for when focus
			// lands in it rather than leaving the question to the label alone.
			var input = new TextBox
			{
				Text = initial,
				Width = 480,
				Font = new Font("Segoe UI", 11f),
				AccessibleName = prompt
			};
			layout.Controls.Add(secret ? WithShowHideButton(input) : input, 0, 1);

			var ok = new Button { Text = Loc.T("prompt.ok"), AutoSize = true, MinimumSize = new Size(110, 36) };
			var cancel = new Button { Text = Loc.T("prompt.cancel"), AutoSize = true, MinimumSize = new Size(110, 36) };
			var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 10, 0, 0) };
			buttons.Controls.AddRange(new Control[] { ok, cancel });
			layout.Controls.Add(buttons, 0, 2);

			container.Controls.Add(layout);

			void Accept()
			{
				answer = input.Text;
				closeView();
			}

			ok.Click += delegate { Accept(); };
			cancel.Click += delegate { closeView(); };
			input.KeyDown += delegate (object? s, KeyEventArgs e)
			{
				if (e.KeyCode != Keys.Enter) return;
				e.Handled = true;
				e.SuppressKeyPress = true;
				Accept();
			};

			// Select-all so typing replaces the current value, which is what an editor of an existing setting
			// almost always wants; End then leaves the caret ready to amend instead.
			input.SelectAll();
			return input;
		},
		sayOnClosing: sayOnCancel == null ? null : () => answer == null ? sayOnCancel : null,
		actBeforeReturning: actOnAnswer == null ? null : () => answer == null ? null : actOnAnswer(answer));

		return answer;
	}

	/// <summary>
	/// Makes <paramref name="box"/> a protected box and puts a Show key button straight after it, returning the
	/// pair to add to the layout in the box's place.
	///
	/// <para>
	/// A plain box read an API key out in full every time focus landed on it — twice in one Settings visit while
	/// the November 2026 checklist was being run, and into the reader's speech history each time. A protected box
	/// is read as "protected" and its characters are not spoken. The button is for checking what was pasted: press
	/// it, Shift+Tab back, and the box reads as text until it is pressed again.
	/// </para>
	///
	/// <para>
	/// The button's own name changes between "Show key" and "Hide key", and the reader says a focused control's
	/// new name by itself, so nothing is spoken here as well.
	/// </para>
	///
	/// <para>
	/// ⚠️ Set visibility on what this returns, not on the box: a visible box inside a hidden row is still hidden,
	/// and the button would be left behind on its own.
	/// </para>
	/// </summary>
	private static TableLayoutPanel WithShowHideButton(TextBox box)
	{
		box.UseSystemPasswordChar = true;

		// A box that filled its cell keeps doing so with the button beside it; one with a set width keeps that width.
		bool fill = box.Dock == DockStyle.Fill;
		var row = new TableLayoutPanel
		{
			ColumnCount = 2,
			RowCount = 1,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Dock = fill ? DockStyle.Fill : DockStyle.None,
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			AccessibleName = SilentAccessibleName
		};
		row.ColumnStyles.Add(fill ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));
		row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

		var toggle = new Button { Text = Loc.T("secret.show"), AutoSize = true, Anchor = AnchorStyles.Left };
		toggle.Click += delegate
		{
			bool showing = !box.UseSystemPasswordChar;
			box.UseSystemPasswordChar = showing;
			toggle.Text = showing ? Loc.T("secret.show") : Loc.T("secret.hide");
		};

		row.Controls.Add(box, 0, 0);
		row.Controls.Add(toggle, 1, 0);
		return row;
	}

	/// <summary>
	/// Offers a list of values and returns the chosen one, or <c>null</c> if the user backed out.
	///
	/// Opens on <paramref name="current"/> where it is one of the choices, so arrowing moves to the neighbouring
	/// values rather than starting from the top of the list every time. The match ignores case, because a mod
	/// author's spelling of a value and the spelling saved in a settings file disagree more often than not — one
	/// mod offers "True, False" while the next offers "true, false" for the same idea.
	///
	/// Shared by every "pick a value the author allows" screen: Content Patcher settings, BepInEx settings, and
	/// the Mod Configuration Menu.
	/// </summary>
	/// <param name="actOnChoice">
	/// For a choice that changes the list the screen was opened from: applies it and says what to say. See
	/// <see cref="ActBeforeReturning"/>. Not called when nothing was chosen.
	/// </param>
	/// <param name="sayOnCancel">As for <see cref="ShowTextPrompt"/>: said, before focus goes back, when nothing was chosen.</param>
	private string? ShowChoiceList(string title, string listName, IReadOnlyList<string> values, string current, string hint,
		Func<string, ResultBeforeReturning?>? actOnChoice = null, string? sayOnCancel = null)
	{
		string? chosen = null;

		ShowInlineView(title, (container, closeView) =>
		{
			var list = new ListBox
			{
				Dock = DockStyle.Fill,
				Font = new Font("Segoe UI", 12f),
				AccessibleName = listName,
				IntegralHeight = false,
				HorizontalScrollbar = true
			};

			foreach (string value in values) list.Items.Add(value);

			int index = -1;
			for (int i = 0; i < values.Count; i++)
				if (string.Equals(values[i], current, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
			if (list.Items.Count > 0) list.SelectedIndex = Math.Max(index, 0);

			container.Controls.Add(list);
			WireAccessibleDialogList(list);

			list.KeyDown += delegate (object? s, KeyEventArgs e)
			{
				if (e.KeyCode != Keys.Enter) return;
				e.Handled = true;
				e.SuppressKeyPress = true;
				chosen = list.SelectedItem as string;
				closeView();
			};

			return list;
		},
		hint: hint,
		sayOnClosing: sayOnCancel == null ? null : () => chosen == null ? sayOnCancel : null,
		actBeforeReturning: actOnChoice == null ? null : () => chosen == null ? null : actOnChoice(chosen));

		return chosen;
	}

	/// <summary>
	/// Wires Shift+F1 on every control inside a view, so context help works wherever focus happens to be.
	///
	/// A view cannot use the window's own key preview for this — an overlay switches it off while it is up (see
	/// <see cref="RunOverlay"/>) — so the key has to be caught on the controls that can hold focus, exactly as
	/// Escape is. A control that has already dealt with the key keeps it.
	/// </summary>
	private static void AttachViewHelp(Control root, Action onHelp)
	{
		foreach (Control child in root.Controls)
		{
			child.KeyDown += delegate (object? s, KeyEventArgs e)
			{
				if (e.Handled || e.KeyCode != Keys.F1 || !e.Shift) return;
				e.Handled = true;
				e.SuppressKeyPress = true;
				onHelp();
			};
			if (child.HasChildren) AttachViewHelp(child, onHelp);
		}
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
