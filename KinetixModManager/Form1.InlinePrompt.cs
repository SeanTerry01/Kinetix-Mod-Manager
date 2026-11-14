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

	[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
	private struct NativeMsg
	{
		public IntPtr hwnd;
		public uint message;
		public IntPtr wParam;
		public IntPtr lParam;
		public uint time;
		public int ptX;
		public int ptY;
	}

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern bool PeekMessage(out NativeMsg msg, IntPtr hWnd, uint filterMin, uint filterMax, uint remove);

	private const uint WM_CHAR = 0x0102;
	private const uint WM_SYSCHAR = 0x0106;
	private const uint PM_REMOVE = 0x0001;

	/// <summary>
	/// Throws away the character of the key that is opening this prompt or view, before anything pumps messages.
	///
	/// ⚠️ A shortcut is acted on in its KeyDown, and by then Windows has already queued the character the key makes.
	/// <c>SuppressKeyPress</c> removes that character only once KeyDown RETURNS, and a prompt or view does not return
	/// until it closes: its own pumping (the reader wait, then the overlay's loop) delivers the character first, to
	/// the control that had focus. On the mod list that is the list's type-to-find, so Ctrl+P for Settings moved the
	/// selection to the next mod starting with P, and Escape came back to Peehell's Big Cellar instead of the mod
	/// the user was on. Measured with NVDA 2026-11-10: row 1 came back as row 21, then 22; F1, with no letter, came
	/// back to row 1.
	///
	/// Only characters already queued are removed. A key pressed after this has not been translated yet, so typing
	/// into the view that opens is untouched.
	/// </summary>
	/// <summary>
	/// How many prompts and views have begun to open, counted before anything pumps. The menu bar reads this to tell
	/// a menu closed with nothing chosen from one whose choice opened a screen. See the MenuDeactivate handler.
	/// </summary>
	private int _overlaysStarted;

	/// <summary>Where focus was, and how many overlays had started, when the menu bar was entered.</summary>
	private Control? _focusWhenMenuOpened;
	private int _overlaysStartedWhenMenuOpened;

	/// <summary>
	/// The first thing a prompt or view does, before it pumps a single message: counts itself as started, and drops
	/// the character of the key that opened it. See <see cref="DropCharacterOfOpeningKey"/>.
	/// </summary>
	private void BeginOpeningOverlay()
	{
		_overlaysStarted++;
		DropCharacterOfOpeningKey();
	}

	private static void DropCharacterOfOpeningKey()
	{
		while (PeekMessage(out _, IntPtr.Zero, WM_CHAR, WM_CHAR, PM_REMOVE)) { }
		while (PeekMessage(out _, IntPtr.Zero, WM_SYSCHAR, WM_SYSCHAR, PM_REMOVE)) { }
	}

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
	/// The window a prompt appears inside. There is only this one, unless it has gone or is being hidden on the way out.
	/// </summary>
	private Form? PromptHost() => !IsDisposed && Visible ? this : null;

	/// <summary>
	/// Shows a prompt inside the window and waits for an answer.
	///
	/// Falls back to a real <see cref="MessageBox"/> when there is no window to host it, or when called from a
	/// background thread — a prompt that cannot be shown must never be a prompt that is silently skipped.
	/// </summary>
	/// <param name="actOnAnswer">
	/// For a question whose answer changes the list it was asked from. See <see cref="ActBeforeReturning"/>.
	/// </param>
	private DialogResult ShowPrompt(string text, string caption, MessageBoxButtons buttons,
		Func<DialogResult, ResultBeforeReturning?>? actOnAnswer = null)
	{
		// A manager started from a desktop shortcut is out of sight, and a question needs answering. See Form1.LaunchShortcut.
		RevealFromLaunchOnly(userAsked: false);
		Form? host = PromptHost();
		if (host == null || host.InvokeRequired)
		{
			SpeakPrompt(text);
			DialogResult boxAnswer = MessageBox.Show(text, caption, buttons);
			if (actOnAnswer?.Invoke(boxAnswer) is { } fromBox) { fromBox.ThenChange?.Invoke(); Speak(fromBox.Say); }
			return boxAnswer;
		}

		// Before the prompt exists at all. See WaitOutReaderReaction: once the buttons are on screen and one of
		// them has focus, the reader is already talking about it, and no amount of delaying our own sentence puts
		// it back in front.
		BeginOpeningOverlay();
		WaitOutReaderReaction();

		PromptChoice[] choices = ChoicesFor(buttons);
		DialogResult? answer = null;

		Panel overlay = BuildPromptPanel(text, caption, choices, result => answer = result, out Button firstButton);
		// Its layout panels have no names of their own, and Windows lent them one from the window: the buttons of
		// "Confirm Delete" sat in a pane called "Search:". Measured with NVDA 2026-11-10. A view did this already.
		SilenceUnnamedContainers(overlay);

		bool actedInside = false;
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
			},
			beforeRestoring: actOnAnswer == null ? null : () =>
				actedInside = ActBeforeReturning(() => actOnAnswer(answer ?? EscapeResult(choices))));

		// What the caller says next is the answer's result. Held open until the next message, which covers the code that
		// runs straight on from this return. See Speak.
		if (Control.FromHandle(GetFocus()) is ListBox onto && ReferenceEquals(_arrivalPendingFor, onto) && !IsDisposed)
		{
			_promptClosedOnto = onto;
			BeginInvoke(new Action(() => { if (ReferenceEquals(_promptClosedOnto, onto)) _promptClosedOnto = null; }));
		}

		DialogResult final = answer ?? EscapeResult(choices);
		if (actOnAnswer != null && !actedInside) ActAfterReturning(() => actOnAnswer(final));
		return final;
	}

	/// <summary>
	/// What a question or a choice did, to be said before the user is put back on the list, and the change to that list,
	/// to be made once it has been said.
	/// </summary>
	/// <param name="Say">The result: "Update ignored."</param>
	/// <param name="ThenChange">
	/// What moves the list: removing the settled row, reloading the settings. Made after <paramref name="Say"/> has been
	/// heard, so nothing the list does cuts it off, and before focus goes back, so the reader reads the row the user lands on.
	/// </param>
	/// <param name="NamesRow">The result names the row the user lands back on, as "X set to Y" does. Used the old way only.</param>
	private sealed record ResultBeforeReturning(string Say, Action? ThenChange = null, bool NamesRow = false);

	/// <summary>
	/// Says a result while the question or screen is still open, waits for the reader to finish it, then makes the change to
	/// the list behind. Returns false when it did nothing, so the caller does it the old way instead.
	///
	/// <para>
	/// Sean's choice for every result on a return to a list (2026-11-11), called "way 2": the result first, then the reader's
	/// own reading of the row, then the position. Before, the manager cut the reader off and said all three in one sentence,
	/// which was heard once but left the reader's cut line in its speech history, so every return read there as a double.
	/// Now nothing is cut off. The cost, agreed: a result that names its row hears the name again in the row.
	/// </para>
	///
	/// <para>
	/// Only with a reader that can say when it has finished (NVDA 2026.3 and newer). With any other, the result can't be
	/// waited for, so focus coming back would cut it off; there the old way stands (<see cref="ActAfterReturning"/>).
	/// </para>
	/// </summary>
	private bool ActBeforeReturning(Func<ResultBeforeReturning?> act)
	{
		if (_announcer.IsSpeakingForCertain is null) return false;

		ResultBeforeReturning? outcome = act();
		if (outcome == null) return true;

		if (outcome.Say.Length > 0)
		{
			Speak(outcome.Say);
			LetSpeechFinish();
		}
		// Behind the overlay, which is still up, with the list's own announcements held back: what is read is the arrival.
		_movingListSilently = true;
		try { outcome.ThenChange?.Invoke(); }
		finally { _movingListSilently = false; }
		return true;
	}

	/// <summary>
	/// The old way, for a reader that can't say when it has finished: the change is made after focus has gone back, and the
	/// result rides on the list's return announcement, which swallows the reader's and says result, row and position.
	/// </summary>
	private void ActAfterReturning(Func<ResultBeforeReturning?> act)
	{
		if (act() is not { } outcome) return;
		// The return announcement says where this leaves the user, so the move itself says nothing.
		_movingListSilently = true;
		try { outcome.ThenChange?.Invoke(); }
		finally { _movingListSilently = false; }
		if (outcome.Say.Length == 0) return;
		ListBox? onto = _promptClosedOnto ?? Control.FromHandle(GetFocus()) as ListBox;
		if (onto != null && HandToArrival(onto, outcome.Say, outcome.NamesRow)) return;
		// Nothing under way to carry it, so where the user is goes with it.
		SpeakAfterPrompt(Control.FromHandle(GetFocus()) is ListBox { SelectedItem: not null } list
			? WithWhereYouAre(list, outcome.Say, outcome.NamesRow)
			: outcome.Say);
	}

	/// <summary>
	/// Set just before a question is asked over a screen that will close as soon as it is answered. That question then
	/// puts focus back without announcing the row it lands on, because the screen beneath closes next and says where the
	/// user really is. Used up by the next overlay to open.
	///
	/// <para>
	/// The question used to be asked after the first screen had gone. Focus went back to the list behind for a moment,
	/// the question then disabled it, and NVDA read it late: "Minecraft setups list unavailable, Simply Optimized
	/// Continued… unavailable" before the newer-world question (heard 2026-11-11).
	/// </para>
	/// </summary>
	private bool _nextOverlayReturnsQuietly;

	/// <summary>
	/// Set by an answer acted on before its question closes (see <see cref="ActBeforeReturning"/>), when what it starts will say
	/// where the user lands once it is done. That question then puts focus back without announcing the row.
	///
	/// <para>
	/// Deleting a mod said "Deleting… Backing it up first", then held the arrival on the list until that had been heard, by
	/// which time the delete had finished: "Deleted PatcherTest. Installed Mods list, PatcherTest… Enabled", the mod just
	/// deleted read as there (heard 2026-11-11). The rescan after it says where the user is.
	/// </para>
	/// </summary>
	private bool _returnQuietlyFromThisAnswer;

	/// <summary>The list such a question has just put focus back on, whose arrival List_Enter leaves unsaid. Used up by it.</summary>
	private ListBox? _arrivingQuietly;

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
		Func<bool> finished, Action onEscape, Action? afterShown = null, Action? beforeRestoring = null)
	{
		// As for a prompt: a view opened over a manager nobody can see would wait for an answer forever.
		RevealFromLaunchOnly(userAsked: false);

		// Remember what to put back: the overlay borrows the window's focus, its Enter/Escape handling, its
		// name and the enabled state of everything already in it.
		Control? focusBefore = host.ActiveControl;
		// Taken now, so it belongs to this overlay and no other. See _nextOverlayReturnsQuietly.
		bool returnQuietly = _nextOverlayReturnsQuietly;
		_nextOverlayReturnsQuietly = false;
		bool keyPreviewBefore = host.KeyPreview;
		string? hostNameBefore = host.AccessibleName;
		var disabled = new List<Control>();

		// An overlay can open inside LetSpeechFinish's pump: a download arriving, an update check finishing. The hold
		// swallows every key, and the wait cannot resume until this overlay is answered, so with it left on nothing
		// could answer the prompt and nothing could end the wait.
		bool holdingBefore = _holdingForSpeech;
		_holdingForSpeech = false;

		_overlayDepth++;
		try
		{
			// The window's own shortcuts must not fire while the overlay is up. Escape and Enter go to the overlay's
			// own controls instead, below.
			host.KeyPreview = false;

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
			// ⚠️ NVDA can still say "unavailable" for the control just left: it speaks a state change on what it still takes for
			// the focus before it has handled the focus move, and switching things off below is such a change. Traced 2026-11-11
			// on Delete Old Backups' button: its stateChange reached NVDA before the move did. A 120 ms pause here, pumped, did
			// not stop it, and was taken out.
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

			if (!host.IsDisposed) beforeRestoring?.Invoke();
		}
		finally
		{
			_overlayDepth--;
			_holdingForSpeech = holdingBefore;
			try
			{
				if (!host.IsDisposed)
				{
					// What was behind the overlay comes back on first, so that focus can go straight back to it while
					// the overlay is still there. See the removal below for why that order matters.
					foreach (Control restore in disabled)
						if (!restore.IsDisposed) restore.Enabled = true;

					host.KeyPreview = keyPreviewBefore;

					// A list being worked through is brought up to date before focus goes back to it, or the row read
					// out would be the one just dealt with. It may instead close itself, having nothing left in it,
					// and then there is nowhere here to return to. See Form1.StayingLists.
					if (focusBefore != null && !focusBefore.IsDisposed && RefreshListFocusReturnsTo(focusBefore) &&
						!focusBefore.IsDisposed && focusBefore.CanFocus)
					{
						// Going back to a list is a focus change the user did not make, and a screen reader
						// announces nothing for those — it sees focus as never having left. Without this, closing
						// a view read out "twelve of a hundred and forty-seven" and never said which mod that
						// was; the only way to hear it was to arrow off the row and back. The flag is consumed by
						// whichever of the list's handlers speaks first, and only ever by a list.
						// Quietly: a question asked over another screen that closes straight after, which says where the user
						// lands. Announced here as well, the row of that screen was read on the way past.
						// Or set by the answer, while this overlay was the one up. See _returnQuietlyFromThisAnswer.
						bool quiet = returnQuietly || _returnQuietlyFromThisAnswer;
						_returnQuietlyFromThisAnswer = false;
						ListBox? returningTo = quiet ? null : focusBefore as ListBox;
						// And the position its GotFocus would say: "Changes cancelled. 1 of 1", the 1 of 1 being the list of
						// worlds that was closing (heard 2026-11-11).
						if (quiet && focusBefore is ListBox quietList) _arrivingQuietly = quietList;
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
							// opens with the list's name — "Installed Mods list" — as it would have done had the user
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

					// Focus has to leave the overlay BEFORE the overlay leaves the window. Removing a container
					// while something inside it still has focus makes WinForms park that control's handle on its
					// hidden holding window — and the focus goes with it, so the screen reader dutifully
					// announced "WindowsFormsParkingWindow".
					//
					// ⚠️ Removed only AFTER focus has gone back, not before. Clearing the active control to get focus
					// out first put it on the form for a moment, and the reader read the form as it passed: the whole
					// title bar after every view closed, or "window" from inside another view, ahead of the row.
					// Measured with NVDA 2026-11-10. Focus now goes straight from the overlay to where it was, and
					// this only catches the case where there was nowhere to go back to.
					if (host.ActiveControl != null && overlay.Contains(host.ActiveControl))
						host.ActiveControl = null;

					host.Controls.Remove(overlay);
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
				// Escape on an open dropdown is the dropdown's: it closes the list and keeps the view. Taking it here
				// closed all of Settings and threw away every edit.
				if (s is ComboBox { DroppedDown: true }) return;
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

		// A text box rather than a label, because a label cannot take focus, and so the question could only ever be heard
		// the once. When something cut it off, someone was answering a delete or restore question they had not heard. Shift+Tab
		// from the choices reaches it now, and the arrow keys read it, which matters most for a long one.
		layout.Controls.Add(new TextBox
		{
			Text = text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
			Font = new Font("Segoe UI", 12f),
			Dock = DockStyle.Fill,
			Multiline = true,
			ReadOnly = true,
			BorderStyle = BorderStyle.None,
			BackColor = SystemColors.Control,
			ScrollBars = ScrollBars.Vertical,
			AccessibleName = caption
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
				// Handled here, because a window with no default button does nothing with Enter.
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
	/// Speaks a prompt's question, interrupting, the instant focus has landed on its first choice. Focus has just set the
	/// reader off describing that choice, and cutting it off is what puts the question first.
	///
	/// Just the question: giving the buttons their names back a moment later is itself a change the reader reports, so it
	/// announces "Yes, Alt Y" on its own straight afterwards. Saying it here as well had it read out twice. A prompt raised
	/// straight after the window pulled itself to the front has already waited, in <see cref="WaitOutReaderReaction"/>.
	/// </summary>
	private void SpeakPromptQuestion(string text) => Speak(text, interrupt: true);

	/// <summary>
	/// Gives the choice buttons their real accessible names back, shortly after the question has been spoken. The change is
	/// reported by the reader, which is what puts the choice after the question, and Tabbing between them then says "Yes"
	/// and "No" as it should.
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
