using System.Diagnostics;
using System.Text;

namespace KinetixModManager;

/// <summary>Shortcut resolution, status/speech output, list events, discovery, and loading helpers for Form1.</summary>
public partial class Form1
{
	/// <summary>
	/// An accessible name that gives a screen reader nothing to say — for a container whose own name is noise.
	///
	/// A tab strip is the case that matters. The screen reader announces the strip and then the tab, every time
	/// the selection moves, so anything the strip is called is repeated on every single tab change. What you
	/// want to hear is the tab.
	///
	/// Getting to silence took three tries, and the two obvious ones both fail:
	///   * No name at all — the reader will not accept that a control has no name, so it goes looking and reads
	///     whatever text it finds nearby. On the main window that produced "Search" (from the search box on the
	///     Installed tab) in front of every tab name.
	///   * An empty string — treated the same as no name, so the same guesswork happens.
	/// A single space is a name, so nothing is inferred, and there is nothing in it to pronounce.
	///
	/// Only for containers that are announced <em>alongside</em> their contents. A control the user lands on in
	/// its own right — a list, a text box, a button — must always have a real name.
	/// </summary>
	private const string SilentAccessibleName = " ";

	/// <summary>
	/// A human-readable key label for <paramref name="action"/> (e.g. "Ctrl+R").
	///
	/// The shortcut is stored as a plain number now that settings live in the core, so the conversion to a
	/// <see cref="Keys"/> happens here — at the WinForms head's own edge, which is where a Windows type
	/// belongs. The numbers are identical either way; see <see cref="Shortcut"/>.
	/// </summary>
	/// <summary>Whether a command has a key at all. A cleared one would be read into a sentence as "Press Unmapped".</summary>
	private bool ShortcutIsSet(string action) =>
		_settings.Shortcuts.TryGetValue(action, out int value) && value != Shortcut.None;

	private string GetShortcutString(string action)
	{
		if (!_settings.Shortcuts.TryGetValue(action, out int value) || value == Shortcut.None)
			return Loc.T("shortcutMgr.unmapped");

		var label = new StringBuilder();
		if (Shortcut.HasControl(value)) label.Append("Ctrl+");
		if (Shortcut.HasShift(value)) label.Append("Shift+");
		if (Shortcut.HasAlt(value)) label.Append("Alt+");
		label.Append((Keys)Shortcut.KeyOf(value));

		return label.ToString();
	}

	/// <summary>Where each label built by <see cref="ShortcutLabel"/> came from, so it can be built again.</summary>
	private readonly Dictionary<string, (string Key, string Action)> _shortcutLabels = new();

	/// <summary>A menu or button label that names its shortcut, remembered so a remap can correct it.</summary>
	/// <remarks>
	/// A command with no key gets its label without the key: "Refresh All", not "Refresh All (Unmapped)", which three
	/// menu items read by default. The phrase is filled with a marker that is then cut out with its brackets, so no
	/// second phrase is needed per label and a translation that puts the key somewhere else still works.
	/// </remarks>
	private string ShortcutLabel(string key, string action)
	{
		const string noKey = "\u0001";
		string text = ShortcutIsSet(action)
			? Loc.T(key, GetShortcutString(action))
			: System.Text.RegularExpressions.Regex.Replace(Loc.T(key, noKey), @"\s*[\(\[]\u0001[\)\]]", "").Replace(noKey, "").Trim();
		_shortcutLabels[text] = (key, action);
		return text;
	}

	/// <summary>
	/// Corrects every label that names a shortcut, after the shortcuts have changed. The keys themselves need nothing:
	/// each press is checked against the settings as it happens.
	///
	/// ⚠️ Never SetupAccessibleUI for this. It adds a second interface beside the first, and left focus on the old
	/// lists while every field pointed at new, empty ones, until the manager was restarted.
	/// </summary>
	private void RefreshShortcutLabels()
	{
		var before = new Dictionary<string, (string Key, string Action)>(_shortcutLabels);
		_shortcutLabels.Clear();

		string Relabelled(string text) =>
			before.TryGetValue(text, out var label) ? ShortcutLabel(label.Key, label.Action) : text;

		void Walk(ToolStripItemCollection items)
		{
			foreach (ToolStripItem item in items)
			{
				item.Text = Relabelled(item.Text ?? "");
				if (item is ToolStripMenuItem { HasDropDownItems: true } menu) Walk(menu.DropDownItems);
			}
		}

		if (MainMenuStrip != null) Walk(MainMenuStrip.Items);
		btnPruneBackups.Text = Relabelled(btnPruneBackups.Text);

		// These few are named for the game or a mode, so they are built where that is known rather than remembered.
		ConfigureModsMenuForGame();
		ConfigureLogMenuItemForGame();
	}

	/// <summary>Returns <c>true</c> if <paramref name="e"/> matches the configured shortcut for <paramref name="action"/>.</summary>
	private bool IsShortcut(KeyEventArgs e, string action)
	{
		if (!_settings.Shortcuts.TryGetValue(action, out int value) || value == Shortcut.None) return false;

		return e.KeyData == (Keys)value;
	}

	/// <summary>
	/// Applies the current search query, category and status filters to the installed mods list, then rebuilds the
	/// list box via <see cref="RebuildInstalledListBox"/> and says how many mods came back.
	///
	/// <para>
	/// The rebuild is delegated rather than done here, and that is the whole point of this method. It used to fill
	/// the list itself, from a flat <c>Where</c> over every mod — which quietly threw the grouping away. Narrowing
	/// by anything at all, a status as much as a search, replaced 147 grouped rows with 198 loose ones, and
	/// widening back to All did not bring the grouping back, because nothing here ever knew how to build it.
	/// <see cref="RebuildInstalledListBox"/> reads the same three controls and groups mods by folder whatever the
	/// filters say, so grouping is never lost and never has to be rebuilt from scratch.
	/// </para>
	/// </summary>
	private void FilterInstalledMods(bool readerNamesTheChange = false)
	{
		RebuildInstalledListBox();

		// The counts, and only the counts. Which sorting the user has just moved to is the screen reader's to say —
		// it names the new value of the dropdown as they arrow onto it — and the manager saying it as well was
		// heard as the sorting twice over, once at each end: "All Mods. 198 mods found. 147 rows, including 31 mod
		// groups. All Mods."
		//
		// The total comes from the rebuild rather than from the rows, because a collapsed group holds its matching
		// mods off screen and a group header is a row that is not a mod. Wherever any group is on show, the rows
		// and groups are named as well: a group is one row standing for several mods, so the two counts differ —
		// fewer rows than mods while groups are collapsed, more once they are expanded — and either reading is
		// puzzling without the other.
		int groups = listInstalled.Items.OfType<GameMod>().Count(m => m.IsGroup);
		string counts = groups == 0
			? Loc.T(_installedMatchCount == 1 ? "modlist.foundOne" : "modlist.found", _installedMatchCount)
			: Loc.T("modlist.foundWithGroups", _installedMatchCount, listInstalled.Items.Count, groups);

		if (!readerNamesTheChange)
		{
			Speak(counts);
			return;
		}

		Fire(SpeakAfterReaderNamesTheChangeAsync(counts), "SpeakAfterReaderNamesTheChangeAsync");
	}

	/// <summary>
	/// Says <paramref name="counts"/> once the screen reader has had its say about the dropdown the user just
	/// changed, so the sorting is heard before the numbers rather than after them.
	///
	/// <para>
	/// The wait is the reader's <em>reaction</em> time, not its speaking time — how long it takes to notice the
	/// selection changed and begin describing it, measured at around 70ms. That does not vary with the user's
	/// speech rate. Asking whether the reader is speaking cannot replace it, even where the reader can answer: until
	/// it has begun the answer is no, the same as when it has nothing to say. Announcing straight away instead put
	/// the numbers ahead of the reader every time, which is the whole problem this exists to fix.
	/// </para>
	///
	/// <para>
	/// An announcement overtaken by a further change is dropped rather than spoken late: arrowing down the
	/// dropdown should describe where the user stopped, not narrate every option they passed through.
	/// </para>
	/// </summary>
	private async Task SpeakAfterReaderNamesTheChangeAsync(string counts)
	{
		int generation = ++_filterAnnounceGeneration;
		await Task.Delay(400);

		if (generation != _filterAnnounceGeneration || _shuttingDown) return;

		Speak(counts);
	}

	/// <summary>Which filter announcement is the current one. See <see cref="SpeakAfterReaderNamesTheChangeAsync"/>.</summary>
	private int _filterAnnounceGeneration;

	/// <summary>
	/// Wires the standard accessible behaviour onto a modal dialog's list: announce the "X of Y" position both when
	/// the list gains focus (<see cref="List_Enter"/>) and as the selection moves with the arrow keys
	/// (<see cref="List_SelectedIndexChanged"/>), and suppress Left/Right, which would otherwise move the selection
	/// like Up/Down in a single-column list box and read confusingly. Every new dialog list should use this so it
	/// behaves like the rest of the app. Callers add their own KeyDown handler for Enter/Delete actions.
	/// </summary>
	private void WireAccessibleDialogList(ListBox list)
	{
		list.GotFocus += List_Enter;
		list.SelectedIndexChanged += List_SelectedIndexChanged;
		KeepLeftAndRightOffTheSelection(list);
	}

	/// <summary>
	/// The list a mod's settings view shows, unfilled and not yet wired, so that filling it says nothing: the caller
	/// selects its first row and then hands it to <see cref="AddSettingsListLayout"/>.
	/// </summary>
	private static ListBox NewSettingsList()
	{
		var list = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			// Deliberately silent. The rule is that a control the user lands on keeps a real name, but this list IS the
			// view, and the view has just said "<mod> Settings" as its title and "N settings, press Enter…" as its hint.
			// A name here is the same sentence a third time, which is the repetition the silent name exists to stop.
			AccessibleName = SilentAccessibleName,
			IntegralHeight = false,
			HorizontalScrollbar = true
		};
		return list;
	}

	/// <summary>
	/// Lays out a settings view: <paramref name="list"/> filling the space with a Close button below it, in that tab
	/// order, and gives the list the announcements every list has. Returns the button for the caller to connect.
	/// </summary>
	private Button AddSettingsListLayout(Control container, ListBox list, string closeText, string closeName)
	{
		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

		var btnClose = new Button { Text = closeText, AutoSize = true, AccessibleName = closeName };
		var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
		buttons.Controls.Add(btnClose);

		layout.Controls.Add(list, 0, 0);
		layout.Controls.Add(buttons, 0, 1);
		container.Controls.Add(layout);

		WireAccessibleDialogList(list);
		return btnClose;
	}

	/// <summary>A list box moves its selection on Left and Right as well as Up and Down, which here reads as losing your place.</summary>
	private static void KeepLeftAndRightOffTheSelection(ListBox list)
	{
		list.KeyDown += (_, e) =>
		{
			if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) { e.Handled = e.SuppressKeyPress = true; }
		};
	}

	/// <summary>
	/// Whether a list must stay quiet because startup is still saying its opening.
	///
	/// ⚠️ Only until the user presses a key. The hold is there for the position a list says by itself as the window
	/// appears, which came ahead of the welcome. A move the user made is not that. The opening takes as long as the
	/// welcome takes to say, 8 seconds measured with NVDA on 2026-11-10, and an arrow pressed in that time said
	/// "Minecraft" with no "2 of 6". Having moved, they also never heard the position at the end of the opening,
	/// which is left out for anyone already moving.
	/// </summary>
	private bool StartupHoldsTheLists => _startupSpeechInProgress && !_userMovedSinceLoadBegan;

	private async void List_Enter(object? sender, EventArgs e)
	{
		// Startup owns the floor until it has said its opening, unless the user has started moving. Checked before the
		// flags are consumed, so a deliberate announcement armed for afterwards survives. See _startupSpeechInProgress.
		if (StartupHoldsTheLists) return;

		// Consumed before any early return, for the same reason as in List_SelectedIndexChanged.
		bool announceRowName = _announceRowNameOnNextChange;
		bool announceListName = _announceListNameOnNextChange;
		_announceRowNameOnNextChange = false;
		_announceListNameOnNextChange = false;

		if (sender is not ListBox listBox) return;

		// Focus put back by a question over a screen that is closing next. See _nextOverlayReturnsQuietly.
		if (ReferenceEquals(_arrivingQuietly, listBox))
		{
			_arrivingQuietly = null;
			return;
		}

		if (listBox.Items.Count == 0)
		{
			AnnounceListEmpty(listBox);
			return;
		}
		if (listBox.SelectedIndex == -1)
		{
			// Selecting an item raises SelectedIndexChanged, which announces the position itself — and needs the
			// flags back, because that announcement is now the one this focus change is going to produce.
			_announceRowNameOnNextChange = announceRowName;
			_announceListNameOnNextChange = announceListName;
			listBox.SelectedIndex = 0;
			return;
		}
		// An item is already selected, so focusing did not raise SelectedIndexChanged. Announce the
		// position here, after a short delay so the screen reader speaks the list name and selected
		// item first — putting "X of Y" at the end, matching the arrow-key path (List_SelectedIndexChanged).
		//
		// A programmatic focus change does not wait: the reader's announcement of it is wrong and has to be caught
		// as it starts, which SpeakListPosition does across a short window of its own. See it for why.
		await Task.Delay(announceRowName ? 0 : 100);
		if (!listBox.Focused) return;
		// The Discovery "Load more" row carries no position; its row text is read by the screen reader.
		if (listBox.SelectedItem is DiscoveryLoadMoreRow) return;
		string text = PositionTextFor(listBox);

		// "The screen reader speaks the list name and selected item first" holds when the USER moved focus here —
		// tabbing in, or clicking. It does not hold when the program put focus back, which is what closing a view
		// does: the reader treats it as focus never having left and says nothing, so all that was heard was a
		// position with no idea which mod it belonged to. Editing a mod's settings and pressing Escape landed
		// exactly there. When the program moved focus, the row names itself.
		if (announceRowName && listBox.SelectedItem != null)
			text = RowThenPosition(listBox.SelectedItem, text);

		// Focus arriving at a list is announced by a screen reader as its name, then the row it landed on, then
		// where that row sits — "Installed Mods list. Stardew Access… 129 of 149". When the reader's version of that
		// has to be swallowed (see SpeakListPosition), the name goes with it, and a list that no longer says what it
		// is leaves the user to work it out from the row. So the name is put back at the front, in the same order
		// the reader would have used. Only on the way in: moving within a list must not repeat it.
		if (announceListName)
			text = ListNameThenRest(listBox, text);

		SpeakListPosition(listBox, text, replaceReader: announceRowName);
	}

	/// <summary>
	/// The list whose reader-replacing announcement is under way: set while <see cref="SpeakListPosition"/> swallows the
	/// reader, cleared when it speaks or gives way.
	/// </summary>
	private ListBox? _arrivalPendingFor;
	/// <summary>A result for that announcement to open with. See <see cref="SayOnReturnTo"/>.</summary>
	private (ListBox List, string Text, bool NamesRow)? _resultForArrival;

	private (string Text, bool NamesRow)? TakeResultForArrival(ListBox list)
	{
		if (ReferenceEquals(_arrivalPendingFor, list)) _arrivalPendingFor = null;
		if (_resultForArrival is not { } waiting || !ReferenceEquals(waiting.List, list)) return null;
		_resultForArrival = null;
		return (waiting.Text, waiting.NamesRow);
	}

	// SayOnReturnTo stood here: a view's result riding on the list's return announcement, "FriendableZuzuNPCs set to Stella.
	// 4 of 12", with the reader's own reading swallowed. With NVDA 2026.3 the result is now said before the view closes and
	// nothing is swallowed (ActBeforeReturning); other readers still ride on the arrival, through ActAfterReturning.
	// ⚠️ A plain Speak just after a view returns is still wrong: it claims the floor from the return announcement.

	/// <summary>
	/// Gives <paramref name="text"/> to the return announcement under way on <paramref name="list"/>, after anything already
	/// given to it. False when there is none, and the caller says it itself.
	/// </summary>
	private bool HandToArrival(ListBox list, string text, bool namesRow)
	{
		if (!ReferenceEquals(_arrivalPendingFor, list) || !list.Focused) return false;
		_resultForArrival = _resultForArrival is { } before && ReferenceEquals(before.List, list)
			? (list, EndSentence(before.Text) + " " + text, before.NamesRow && namesRow)
			: (list, text, namesRow);
		return true;
	}

	/// <summary>Text with closing punctuation, so what is said after it does not run into it.</summary>
	private static string EndSentence(string text)
	{
		string said = text.TrimEnd();
		return said.Length > 0 && !".!?…".Contains(said[^1]) ? said + "." : said;
	}

	/// <summary>
	/// The list a prompt has just closed back onto, from its closing until the next message is handled. See
	/// <see cref="Speak(string, bool)"/>.
	/// </summary>
	private ListBox? _promptClosedOnto;

	[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
	private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

	/// <summary>LB_SETCARETINDEX — moves a multi-selection list box's focus rectangle to an item.</summary>
	private const int LB_SETCARETINDEX = 0x019E;

	/// <summary>LB_SETCURSEL — sets a single-selection list box's current item, caret included.</summary>
	private const int LB_SETCURSEL = 0x0186;

	/// <summary>
	/// Makes a list's own idea of its current row agree with what is selected, so a screen reader reads the right
	/// one when focus arrives.
	///
	/// A list box tracks two things: what is selected, and which row is <em>current</em> — where the focus
	/// rectangle sits. A keypress moves both. Setting the selection from code, especially while the list does not
	/// have focus, can leave the second behind, and the stale one is what the reader announces on focus, with
	/// "not selected" attached because the selection is elsewhere. Heard leaving a settings screen, that is a mod
	/// you were never on being read out ahead of the one you were.
	///
	/// The message depends on the kind of list, and using the wrong one does nothing at all: a single-selection
	/// list box <b>ignores</b> LB_SETCARETINDEX — its current row and its selection are the same thing, moved with
	/// LB_SETCURSEL — while a multi-selection one keeps them apart and needs LB_SETCARETINDEX. Both are sent as
	/// messages rather than through <see cref="ListBox.SelectedIndex"/> deliberately: the value is not changing, so
	/// nothing should raise a change event and nothing should be announced twice.
	/// </summary>
	private static void AlignListCaretToSelection(ListBox list)
	{
		if (list.IsDisposed || !list.IsHandleCreated) return;
		int index = list.SelectedIndex;
		if (index < 0 || index >= list.Items.Count) return;

		SendMessage(list.Handle,
			list.SelectionMode == SelectionMode.One ? LB_SETCURSEL : LB_SETCARETINDEX,
			(IntPtr)index, IntPtr.Zero);
	}

	/// <summary>
	/// "&lt;the row&gt;. &lt;position&gt;" for an announcement the program is making itself.
	///
	/// Row text already ends in a full stop — one is added deliberately so the reader pauses before whatever
	/// follows — so joining with another produced "Enabled. . 129 of 149", heard as a stumble. The row's own stop
	/// is dropped and the join puts one back, leaving exactly one pause where the pause was wanted.
	/// </summary>
	private static string RowThenPosition(object? row, string position)
	{
		string text = (row?.ToString() ?? "").TrimEnd();
		if (text.EndsWith(".", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1).TrimEnd();
		// A heading has no position, so the row is all there is to say — joining it to nothing would leave a
		// dangling separator hanging off the end of the sentence.
		if (position.Length == 0) return text;
		return text.Length == 0 ? position : Loc.T("common.rowThenPosition", text, position);
	}

	/// <summary>
	/// The "X of Y" a list should say for the row it is on, or <c>""</c> when it should say none.
	///
	/// In a list divided by headings the count is per section and the headings are not numbered — see
	/// <see cref="ListSections"/> for why. A list with no headings is counted whole, exactly as before.
	/// </summary>
	private string PositionTextFor(ListBox list)
	{
		if (list.SelectedIndex < 0) return "";

		// Discovery keeps its own count: its inline "Load more" row is an action, not a result, and must not be
		// counted among them.
		if (list.Name == "listDiscovery")
			return Loc.T("common.position", list.SelectedIndex + 1, DiscoveryResultCount());

		var headings = new bool[list.Items.Count];
		bool anyHeading = false;
		for (int i = 0; i < headings.Length; i++)
			anyHeading |= headings[i] = ListSections.IsHeading(list.Items[i]);

		if (!anyHeading)
			return Loc.T("common.position", list.SelectedIndex + 1, list.Items.Count);

		(int position, int total) = ListSections.PositionWithinSection(headings, list.SelectedIndex);
		return position == 0 ? "" : Loc.T("common.position", position, total);
	}

	/// <summary>
	/// "&lt;the list's name&gt;. &lt;the rest&gt;" — what a screen reader says when focus lands on a list, in the
	/// order it says it.
	///
	/// The name comes from <see cref="Control.AccessibleName"/>, the same string the reader would have read, so the
	/// two can never drift apart. A container deliberately silenced with <see cref="SilentAccessibleName"/> stays
	/// silent, and a list with no name of its own adds nothing rather than an empty pause.
	///
	/// ⚠️ A list's name leaves out the word "list", because the reader adds it: "Installed Mods List" was heard as
	/// "Installed Mods List list". The wording here says it, to match.
	/// </summary>
	private static string ListNameThenRest(ListBox list, string rest)
	{
		string name = (list.AccessibleName ?? "").Trim();
		if (name.Length == 0) return rest;
		if (name.EndsWith(".", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 1).TrimEnd();
		if (name.Length == 0) return rest;
		return rest.Length == 0 ? name : Loc.T("common.listNameThenRest", name, rest);
	}

	private ListBox? _lastPosList;
	private int _lastPosIndex = -1;
	private long _lastPosTicks;

	/// <summary>
	/// Speaks a list's "X of Y" position, skipping a repeat of the same list+index within a short window. This
	/// stops a double announcement when a list receives focus twice in quick succession (e.g. a dialog that
	/// explicitly focuses its list on top of the focus it gets naturally on open). Arrowing changes the index, so
	/// normal navigation is never suppressed.
	/// </summary>
	/// <param name="carrying">
	/// A result that <paramref name="text"/> opens with. It is not to be lost with the row, so if this call gives way to
	/// something newer, or finds it has nothing to add, the result is said anyway. See <see cref="SayAndRefreshModList"/>.
	/// </param>
	private async void SpeakListPosition(ListBox list, string text, bool replaceReader = false, string? carrying = null)
	{
		if (_shuttingDown) return;
		// A heading gets no position and, when the user arrowed onto it themselves, the reader has already read
		// the row. There is nothing left to say, and saying an empty utterance would cut the reader off to do it.
		if (text.Length == 0) return;

		long now = Environment.TickCount64;
		if (carrying == null && ReferenceEquals(_lastPosList, list) && _lastPosIndex == list.SelectedIndex && now - _lastPosTicks < 700)
			return;
		_lastPosList = list;
		_lastPosIndex = list.SelectedIndex;
		_lastPosTicks = now;

		// Claim this announcement — after the de-duplication above, never before it. A call that turns out to be a
		// repeat says nothing, and if claiming came first it would also have cancelled the announcement it was a
		// repeat of, leaving silence where there should have been one of them.
		//
		// Anything that claims after this has, by definition, newer information about where the user is, which makes
		// this one a description of a row they have already left. The wrong answer is to say it anyway and let the
		// newer one interrupt: the interruption is audible, and what is heard is a mod that was never selected being
		// read out and cut off mid-sentence. A superseded announcement abandons instead, silently, and the last word
		// belongs to whichever call knew the most.
		int generation = ++_speakListGeneration;

		if (!replaceReader)
		{
			Speak(text);
			return;
		}

		// The program moved focus or the selection, and the reader's announcement of that is wrong — it reads a row
		// the user was never on. It cannot be prevented from this side, so it is swallowed instead.
		//
		// Silencing once is not enough, and interrupting once is worse: the reader is set off by the same focus
		// change this is, so it can start a moment before or a moment after. Waiting long enough to be sure it had
		// started meant hearing the first syllable of it ("insta…") before the cut. Silencing repeatedly across a
		// short window catches it whenever it begins, and each pass lands too soon after the last for a recognisable
		// sound to escape. Then the truth is said once, and it is the only thing heard.
		// Open to a result for as long as the swallow runs. See SayOnReturnTo: a view closing back onto this list sets
		// this off synchronously, and the view's caller has its result to say only once that has returned.
		_arrivalPendingFor = list;

		void GiveWay()
		{
			(string Text, bool NamesRow)? result = TakeResultForArrival(list);
			if (_shuttingDown) return;
			if (carrying != null) Speak(carrying);
			if (result is { } r) Speak(r.Text);
		}

		// With a reader that can say when it is speaking, the reader is not swallowed at all unless a result has to come
		// first. See LetReaderReadTheRowAsync.
		if (_announcer.IsSpeakingForCertain is not null && carrying == null)
		{
			// One turn of the message loop, so the caller that closed the view or prompt can hand over its result first:
			// it runs straight after the overlay returns, inside the same message. The reader takes about 65ms to start
			// (traced 2026-11-10), so a swallow chosen after this still catches it as it begins.
			await Task.Yield();
			if (_shuttingDown || list.IsDisposed || generation != _speakListGeneration)
			{
				GiveWay();
				return;
			}
			if (_resultForArrival is not { } waiting || !ReferenceEquals(waiting.List, list))
			{
				await LetReaderReadTheRowAsync(list, text, generation, GiveWay);
				return;
			}
		}

		for (int i = 0; i < 12; i++)
		{
			if (_shuttingDown || list.IsDisposed || generation != _speakListGeneration)
			{
				GiveWay();
				return;
			}
			SilenceSpeech();
			await Task.Delay(25);
		}
		if (_shuttingDown || list.IsDisposed || !list.Focused || generation != _speakListGeneration)
		{
			GiveWay();
			return;
		}

		// What happened, then where that leaves you, in one sentence. The position is taken now, after whatever the
		// result's caller did to the list, and a result that names the row stands in for the row's own text.
		if (TakeResultForArrival(list) is { } result)
			text = WithWhereYouAre(list, result.Text, result.NamesRow);
		Speak(text, interrupt: true);
	}

	/// <summary>
	/// Focus has come back to a list. The reader reads the row itself, and only the position is added once it has finished.
	///
	/// <para>
	/// The swallow in <see cref="SpeakListPosition"/> was built for a reader that read the WRONG row on the way back. The
	/// cause of that turned out to be the manager's own call order (August 2026), fixed by putting the selection right
	/// before focus returns, and the reader has read the right row since. But the swallow stayed, so every return was the
	/// reader's row, cut off within 25ms, then the same row again from the manager. Too short to hear, but it is in the
	/// reader's speech history as a double, and Sean asked for those to go (2026-11-10). Nothing is cut off now, so
	/// nothing is said twice.
	/// </para>
	///
	/// <para>
	/// Only with a reader that can say when it is speaking (NVDA 2026.3 and newer). Others keep the swallow, because
	/// without that answer there is no telling when the reader's row has finished or whether it came at all.
	/// </para>
	///
	/// <para>
	/// A reader that says nothing, because focus never really moved, is waited for only <see
	/// cref="SpeechFinishWatch.StartGraceMs"/>, and then the whole announcement is said here as before. A result handed
	/// over while the reader is talking follows the row, with the position.
	/// </para>
	/// </summary>
	private async Task LetReaderReadTheRowAsync(ListBox list, string wholeAnnouncement, int generation, Action giveWay)
	{
		var watch = new SpeechFinishWatch(Environment.TickCount64, maxMs: 6000);
		bool readerSpoke = false;
		while (true)
		{
			if (_shuttingDown || list.IsDisposed || generation != _speakListGeneration)
			{
				giveWay();
				return;
			}
			// The reader went away mid-wait. Say everything rather than guess.
			if (_announcer.IsSpeakingForCertain is not bool speaking) break;
			readerSpoke |= speaking;
			if (watch.IsFinished(Environment.TickCount64, speaking)) break;
			await Task.Delay(20);
		}

		if (_shuttingDown || list.IsDisposed || !list.Focused || generation != _speakListGeneration)
		{
			giveWay();
			return;
		}

		string position = PositionTextFor(list);
		string say = readerSpoke ? position : wholeAnnouncement;
		if (TakeResultForArrival(list) is { } late)
			say = readerSpoke ? (position.Length == 0 ? late.Text : EndSentence(late.Text) + " " + position)
				: WithWhereYouAre(list, late.Text, late.NamesRow);
		if (say.Length > 0) Speak(say);
	}

	/// <summary>
	/// A result, then the row the list is on now and its position. Worked out at the time it is said, after whatever the
	/// result did to the list: an update row settled by a prompt has gone, and the row it was on is no longer the one.
	/// </summary>
	private string WithWhereYouAre(ListBox list, string result, bool namesRow)
	{
		// A result naming the mod the list is on needs only the position: "Updating (1/4): DaisyNiko's Tilesheets" was
		// followed by "DaisyNiko's Tilesheets by DaisyNiko…", the name twice and run together. Measured 2026-11-10.
		if (list.SelectedItem is GameMod { Name.Length: > 0 } on && result.Contains(on.Name, StringComparison.OrdinalIgnoreCase))
			namesRow = true;

		string position = PositionTextFor(list);
		string where = !namesRow && list.SelectedItem != null ? RowThenPosition(list.SelectedItem, position) : position;
		if (where.Length == 0) return result;

		string said = result.TrimEnd();
		return (said.Length > 0 && ".!?…".Contains(said[^1]) ? said : said + ".") + " " + where;
	}

	/// <summary>
	/// Says something immediately after a window that is not ours has closed — a file picker, or anything else
	/// shown with <c>ShowDialog</c> — so it is not talked over by the screen reader re-reading the window
	/// underneath.
	///
	/// <para>
	/// Closing a real window makes the reader announce whatever is revealed, which for us is the main window's
	/// whole caption: "Moonlight Peaks Kinetix Mod Manager - Status: Connected as …". That lands on top of the
	/// result of whatever the user just did — "Exported 11 suggestions to …" was cut off by it — and it is the
	/// exact cost that moved every other screen in this app inside the main window. A file picker cannot be moved
	/// inside it: choosing a path is the operating system's job.
	/// </para>
	///
	/// <para>
	/// So the same trick as <see cref="SpeakListPosition"/>: silence repeatedly across a short window, catching
	/// the caption whenever the reader starts it. The window is longer here than for a list, because a closing
	/// window and the focus change behind it take longer to work through than a selection moving.
	/// </para>
	///
	/// <para>
	/// ⚠️ But the caption is not <em>wrong</em>, only badly timed — unlike the list case, where what the reader
	/// said was about a row the user was never on. Swallowing it and stopping there answered "what did that do?"
	/// and lost "and where am I now?", which is the question the caption and the focused control exist to answer.
	/// So the result is said first, then whatever focus came back to. (The window's caption was said between them
	/// until 2026-11-10 — see <see cref="SpeakWithBearings"/>.)
	/// </para>
	/// </summary>
	private async void SpeakAfterForeignWindow(string text)
	{
		if (_shuttingDown || string.IsNullOrWhiteSpace(text)) return;
		if (!await SettleAfterForeignWindowAsync()) return;
		SpeakWithBearings(text);
	}

	/// <summary>
	/// Absorbs a screen-reader announcement the app is about to replace, and reports whether the caller still
	/// owns the floor afterwards.
	///
	/// <para>
	/// Two occasions need it, and both are the reader dutifully describing a window change the app is about to
	/// say something better about. A file picker closing makes it re-read the main window's caption, which landed
	/// on top of the result of whatever the user just did — and swallowed a prompt's question outright, leaving
	/// only the name of the focused button audible. The main window first appearing makes it read that same
	/// caption, ahead of the welcome.
	/// </para>
	///
	/// <para>
	/// <paramref name="passes"/> sets how long the field is held clear, at 25ms each. The default covers a window
	/// closing; startup needs longer, because the reader gets to the caption later when the app is still coming
	/// up. Nothing is lost by overshooting — the app says its own piece the moment the window ends.
	/// </para>
	///
	/// <para>
	/// Returns <c>false</c> when something else has claimed the floor in the meantime, in which case the caller
	/// should say nothing: whatever claimed it knows more than this call did.
	/// </para>
	/// </summary>
	/// <remarks>
	/// ⚠️ <b>The answer must never decide whether the user's work happens.</b> It reports one thing only:
	/// whether this announcement still owns the floor. Seven callers used it as <c>if (!settled) return;</c>
	/// placed between a file dialog and the work that dialog was for — so anything else that happened to speak
	/// during the half second it waits (a refresh finishing, a status line, an update check) abandoned the
	/// export or import entirely. No file, no error, no sound: the user named a file, chose a folder, pressed
	/// Save, and nothing was ever written. Do the work first, then ask this about the talking.
	/// </remarks>
	private async Task<bool> SettleAfterForeignWindowAsync(int passes = 20)
	{
		// Claimed so that anything the app deliberately says next abandons this rather than being swallowed by
		// it — Speak() bumps the same counter. See the note in Speak.
		int generation = ++_speakListGeneration;

		for (int i = 0; i < passes; i++)
		{
			if (_shuttingDown || generation != _speakListGeneration) return false;
			SilenceSpeech();
			await Task.Delay(25);
		}

		return !_shuttingDown && generation == _speakListGeneration;
	}

	/// <summary>
	/// Says a result and then where the user now is: whatever focus came back to.
	///
	/// The orientation is the half a plain swallow throws away. Tolk queues, so the two simply follow one another.
	///
	/// The window's caption used to be said between them, on the grounds that the reader's own reading of it was
	/// only badly timed. Sean asked for it to go (2026-11-10): coming back from a file picker to the window you
	/// opened it from, the full title is a long sentence telling you nothing you did not know, standing between
	/// the result and the row you are on.
	/// </summary>
	private void SpeakWithBearings(string text)
	{
		if (_shuttingDown || string.IsNullOrWhiteSpace(text)) return;

		Speak(text, interrupt: true);

		// The reader's own reading of the row was swallowed with the rest of its reaction, so the row is said here too.
		// A position alone left "12 of 149" with nothing saying which mod that is.
		if (Control.FromHandle(GetFocus()) is ListBox list && list.Items.Count > 0 && list.SelectedItem != null)
			Speak(RowThenPosition(list.SelectedItem, PositionTextFor(list)));
		else
			AnnounceFocusRestored();
	}

	/// <summary>Which announcement is the current one. See <see cref="SpeakListPosition"/>.</summary>
	private int _speakListGeneration;

	/// <summary>
	/// Moves a list to a row after removing one, and says what happened and where that leaves you, in one sentence.
	///
	/// A reader says nothing when the program moves the selection. So "Save deleted. 14 remaining." never said which save
	/// you were now on, and with a separate position announcement, one could cut off or drop the other.
	/// </summary>
	/// <summary>
	/// A row the user answered Yes to deleting, as a result for <see cref="ActBeforeReturning"/>: the sentence, and the
	/// change it waits for, which takes the row out and lands on the one that moved up into its place. The last row going
	/// closes the screen, with <paramref name="lastSaid"/>, because there is nothing left to land on.
	/// </summary>
	private ResultBeforeReturning RowRemoved(ListBox list, object row, Action closeView, string said, string lastSaid)
	{
		bool last = list.Items.Count <= 1;
		return new ResultBeforeReturning(last ? lastSaid : said, () =>
		{
			int index = list.SelectedIndex;
			list.Items.Remove(row);
			if (list.Items.Count == 0) { closeView(); return; }
			list.SelectedIndex = Math.Min(Math.Max(index, 0), list.Items.Count - 1);
			AlignListCaretToSelection(list);
		});
	}

	private void SayResultAndLandOn(ListBox list, int index, string result)
	{
		_movingListSilently = true;
		try { list.SelectedIndex = Math.Min(index, list.Items.Count - 1); }
		finally { _movingListSilently = false; }
		AlignListCaretToSelection(list);
		Speak(result + " " + RowThenPosition(list.SelectedItem, PositionTextFor(list)));
	}

	/// <summary>
	/// Runs work that speaks, once the reader has finished describing the window a file picker has just uncovered. Started
	/// straight away, its first words were lost to the window's caption, and a question could lose all but its "Yes".
	/// </summary>
	private async Task AfterForeignWindow(Func<Task> work)
	{
		await SettleAfterForeignWindowAsync();
		await work();
	}

	/// <summary>When <see cref="SpeakIfReaderIsFree"/> last spoke, for a reader that cannot say whether it still is.</summary>
	private long _lastBackgroundSpeechTicks = long.MinValue / 2;

	/// <summary>
	/// Says a running commentary line, unless the reader is still on the last one. For work that reports as it goes, where
	/// each line would otherwise queue behind the rest and be heard long after it stopped being true. A reader that
	/// cannot say gets a gap of at least two seconds instead.
	/// </summary>
	private void SpeakIfReaderIsFree(string text)
	{
		long now = Environment.TickCount64;
		if (_announcer.IsSpeakingForCertain ?? now - _lastBackgroundSpeechTicks < 2000) return;

		_lastBackgroundSpeechTicks = now;
		Speak(text);
	}

	/// <summary>Opens an address in the browser, saying so if it cannot. Thrown instead, this ended whatever called it.</summary>
	private void OpenLink(string url)
	{
		try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
		catch (Exception ex)
		{
			LogFailure("Files", $"Could not open {url}", ex);
			Speak(Loc.T("store.couldNotOpenLink", FriendlyError(ex)));
		}
	}

	/// <summary>Stops whatever the screen reader is currently saying, if one is listening.</summary>
	private void SilenceSpeech() => _announcer.Silence();

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern IntPtr GetFocus();

	/// <summary>
	/// Set immediately before the program moves a list's selection itself, so the next announcement says which row
	/// it landed on rather than its position alone. See <see cref="List_SelectedIndexChanged"/> for why a
	/// programmatic move needs this and a keyboard one must not have it.
	/// </summary>
	private bool _announceRowNameOnNextChange;

	/// <summary>
	/// Set immediately before the program moves <em>focus</em> into a list, so the announcement opens with the
	/// list's name the way a screen reader's own would. Separate from <see cref="_announceRowNameOnNextChange"/>
	/// because the two happen at different moments: a rebuild moves the selection inside a list the user is already
	/// in, where naming it again would be a repetition of something they have not left.
	/// </summary>
	private bool _announceListNameOnNextChange;

	/// <summary>
	/// True while the program is moving a list's selection to a place it should already have been, with nothing to
	/// announce about the move itself. See <see cref="ReselectMod"/>, which holds it across the assignment.
	/// </summary>
	private bool _movingListSilently;

	private ListBox? _lastEmptyList;
	private long _lastEmptyTicks;

	/// <summary>
	/// Speaks "List is empty" for a focused, empty list. Two things make this reliable: it waits a moment first so
	/// the screen reader reads the list's <em>name</em> before we add the empty status (otherwise an immediate Tolk
	/// call wins the race and you hear "List is empty. Available updates list." in the wrong order); and it
	/// de-duplicates, because a focus change and a window activation can both target the same empty list at once.
	/// </summary>
	private async void AnnounceListEmpty(ListBox list)
	{
		if (IsLoading || _shuttingDown) return;
		await Task.Delay(120);
		if (list.IsDisposed || !list.Focused || list.Items.Count != 0 || IsLoading) return;

		long now = Environment.TickCount64;
		if (ReferenceEquals(_lastEmptyList, list) && now - _lastEmptyTicks < 1000) return;
		_lastEmptyList = list;
		_lastEmptyTicks = now;
		Speak(Loc.T("common.listEmpty"));
	}

	/// <summary>
	/// Puts the user on the first tab at the end of startup and says which tab that is.
	///
	/// <para>
	/// The window's caption is deliberately not repeated. The welcome has already named the app and the line
	/// after it named the session, so a third reading of "&lt;game&gt; Kinetix Mod Manager" would be the same
	/// information a third time — and the title is a keypress away in the screen reader whenever it is wanted.
	/// What is missing at this point is not what the program is called but where you now are.
	/// </para>
	///
	/// <para>
	/// The tab strip, not the mod list: it is where Tab and the arrow keys both do something useful next, and it
	/// is one F6 from the list.
	/// </para>
	///
	/// <para>
	/// ⚠️ Whether the app says the tab or leaves it to the screen reader depends on where focus already was, and
	/// the two cases really do differ. Focus arriving at the tab strip from somewhere else — the game-selection
	/// list, on a session switch — is a move the reader announces itself, as "Installed Mods selected"; saying it
	/// too was heard as the tab twice. Focus that was already on the strip, which is where startup leaves it,
	/// raises nothing for the reader to describe, so there the app has to say it or nothing is said at all.
	/// Neither swallowing the reader nor speaking unconditionally works: a swallow here would land while
	/// "Connected as …" is very likely still being spoken, and clip it.
	/// </para>
	/// </summary>
	private void LandOnFirstTab()
	{
		if (_shuttingDown || mainTabs == null) return;

		// Two reasons to leave focus exactly where it is. The window is usable while the session loads, so
		// somebody may already be several keystrokes into moving around — being hauled back to Installed Mods
		// mid-keystroke is worse than never being told where you began. And a first-run wizard or a Settings
		// screen opened during startup owns the window; taking focus out from under it would be worse still.
		if (_userMovedSinceLoadBegan || OverlayIsOpen) return;

		SelectTab(AppTab.Installed);
		mainTabs.Focus();

		// Whether to say it is decided by whoever armed the landing, not guessed at here. Startup leaves focus on
		// the strip already, so nothing moves and the reader has nothing to report — the app must say it or the
		// session opens in silence. A session switch drags focus off the game-selection list onto the strip, which
		// the reader announces itself; saying it too was heard as the tab twice.
		if (_landShouldSpeak)
			Speak(Loc.T("common.tabSuffix", mainTabs.SelectedTab?.Text ?? ""));
	}

	/// <summary>
	/// When the manager regains the foreground (e.g. Alt+Tab back in), the child control gets focus again without
	/// reliably re-firing GotFocus, so an empty list would never re-announce that it is empty. If focus has landed
	/// on an empty list, announce it here. Non-empty lists are left to the screen reader, which reads them itself.
	/// </summary>
	private async void Form1_Activated(object? sender, EventArgs e)
	{
		await Task.Delay(50); // let Windows restore focus to the child control first
		if (Control.FromHandle(GetFocus()) is ListBox { Items.Count: 0 } list && list.Focused)
			AnnounceListEmpty(list);
	}

	/// <summary>
	/// Re-announces whatever control focus was restored to. Used when focus returns by a path that does not
	/// raise GotFocus — notably exiting the MenuStrip's Alt menu mode, which keeps the underlying control's
	/// focus and so never re-fires the event. Handles a focused list (announces its position via
	/// <see cref="List_Enter"/>) and the main tab strip (announces the selected tab); otherwise does nothing.
	/// </summary>
	private void AnnounceFocusRestored()
	{
		// Nothing ambient once the user has asked to leave. Announcements like this are queued and can land in
		// the middle of the goodbye — a menu closing, for instance, posts one for after the current message.
		if (_shuttingDown) return;

		Control? focused = Control.FromHandle(GetFocus());
		if (focused is ListBox list)
		{
			List_Enter(list, EventArgs.Empty);
		}
		else if (focused is TabControl || mainTabs.Focused)
		{
			// Leaving the menu with the tab strip focused never re-fires the tab's own announcement, so say it here.
			Speak(Loc.T("common.tabSuffix", mainTabs.SelectedTab?.Text ?? ""));
		}
	}

	/// <summary>The status the title bar should rest at between operations: the Nexus connection when logged in,
	/// otherwise a neutral "Ready". Only this should linger in the title — transient operation statuses
	/// (installing, rebuilding, downloading, …) reset to it when they finish so the title never shows a stale
	/// message long after the work is done.</summary>
	private string RestingStatus() =>
		// A Nexus account is worth resting on only in a session that uses Nexus. Announcing "Connected as
		// Sean" while managing Minecraft states a fact about a service this game has nothing to do with, and
		// it is the line the user sees most often — it is what the title says whenever nothing else is
		// happening. The account is still signed in; it is simply not what this session is about.
		// Minecraft's account is Microsoft's: the title rests on that connection, or on which mode is in use.
		MinecraftRestingStatus() ??
		(_nexusService.UsesNexus &&
		!string.IsNullOrEmpty(_nexusService.NexusUser) && _nexusService.NexusUser != "Unknown User"
			? Loc.T("status.connectedAs", _nexusService.NexusUser)
			: Loc.T("status.ready"));

	/// <summary>Returns the title bar to the resting status after a transient operation, without speaking it.</summary>
	private void ResetStatus() => SetStatus(RestingStatus(), speak: false);

	/// <summary>
	/// What the title bar is to say, with "Test Mode:" in front when this is a test copy. Every write of the window's
	/// title goes through here.
	///
	/// ⚠️ Only the status line used to add it. The startup title and a game being switched did not, so a test copy with
	/// no game loaded said plain "Kinetix Mod Manager" for as long as it was open, and every copy said so for the first
	/// announcement of the window, which is the one moment the title is read without being asked for.
	/// </summary>
	private static string WindowTitle(string title) => TestMode.IsOn ? Loc.T("test.titlePrefix", title) : title;

	private void SetStatus(string text, bool speak = true)
	{
		if (base.InvokeRequired)
		{
			Invoke(delegate
			{
				SetStatus(text, speak);
			});
		}
		else
		{
			Text = WindowTitle(Loc.T("status.titleFormat", GameDisplayName(), text));
			if (speak)
			{
				Speak(text);
			}
		}
	}

	private async void List_SelectedIndexChanged(object? sender, EventArgs e)
	{
		// Startup owns the floor until it has said its opening, unless the user has started moving. See
		// StartupHoldsTheLists.
		if (StartupHoldsTheLists) return;

		// Consumed here, before any early return, so a flag set for a move that turned out not to be announced
		// cannot survive to put a name in front of the next one the user makes with the arrow keys.
		bool announceRowName = _announceRowNameOnNextChange;
		bool announceListName = _announceListNameOnNextChange;
		_announceRowNameOnNextChange = false;
		_announceListNameOnNextChange = false;

		// A move made only to put the list right before the user gets back to it is not news in itself — the
		// arrival is what gets announced, a moment later and with everything already correct. Read here, before
		// the first await, because it is only held across the assignment that raised this event.
		if (_movingListSilently) return;

		// Not silenced during an update check. That can take minutes on a large setup, and for all of it, arrowing any list
		// gave no position, with no reason ever given.
		if (!(sender is ListBox { SelectedItem: not null } list) || !list.Focused)
		{
			return;
		}
		bool typedOntoSameRow = TypedOntoTheSameRow(list);
		// No wait for a move the program made — see SpeakListPosition for why the reader has to be caught as it
		// starts rather than waited for.
		await Task.Delay(announceRowName ? 0 : 100);
		if (!list.Focused) return;
		// The Discovery list's inline "Load more" row is an action, not a numbered result: the screen
		// reader already reads its row text on focus, so add no position announcement.
		if (list.SelectedItem is DiscoveryLoadMoreRow) return;
		// Excludes that row from the Discovery count so positions read "20 of 20", not "20 of 21", and counts
		// per section in a list divided by headings.
		string text = PositionTextFor(list);

		// The row's own text is normally the screen reader's job: it reads the row the user arrowed onto, and this
		// adds the position a moment later. But a reader only announces a row the USER moved to — when the program
		// moves the selection, it says nothing at all, and the position was being read out with no idea which mod
		// it belonged to. So a programmatic move says the row itself first, and only a programmatic one, or an
		// ordinary arrow-key press would hear the name twice.
		//
		// The swallow below can't tell the reader's voice from ours, so a result said just before this was cut off. It is
		// said here instead, as the opening of this sentence. See SayAndRefreshModList.
		string? carrying = null;
		if (announceRowName)
		{
			if (ReferenceEquals(list, listInstalled) && _resultForRestoredRow != null)
			{
				carrying = _resultForRestoredRow;
				// The row is only named if it is not the mod the result has just named.
				bool sameMod = list.SelectedItem is GameMod landedOn && landedOn.UniqueId == _resultAboutModId;
				text = RowThenPosition(carrying, sameMod ? text : RowThenPosition(list.SelectedItem, text));
				_resultForRestoredRow = null;
			}
			else
				text = RowThenPosition(list.SelectedItem, text);
		}
		// The reader says nothing for a row it is already on. See TypedOntoTheSameRow.
		else if (typedOntoSameRow)
			text = RowThenPosition(list.SelectedItem, text);
		if (list.Name == "listLog")
		{
			string lineText = list.SelectedItem.ToString() ?? "";
			string suggestedFix = LogAnalyzer.GetSuggestedFix(lineText);
			if (!string.IsNullOrEmpty(suggestedFix))
			{
				text = text + Loc.T("helpers.suggestedFixSuffix", suggestedFix);
			}
			// Let a screen-reader user know this line is actionable (e.g. a SMAPI update notice),
			// and whether Enter opens a page directly or offers a choice of several links.
			int linkCount = LogAnalyzer.ExtractUrls(lineText).Count;
			if (linkCount == 1)
			{
				text = text + Loc.T("helpers.pressEnterOpenPage");
			}
			else if (linkCount > 1)
			{
				text = text + Loc.T("helpers.pressEnterChoose", linkCount);
			}
		}
		// Only when this change is standing in for a focus arrival — see List_Enter, which hands the flags over
		// when it finds nothing selected yet and lets selecting row 0 do the announcing.
		if (announceListName)
			text = ListNameThenRest(list, text);
		SpeakListPosition(list, text, replaceReader: announceRowName, carrying: carrying);
	}

	/// <summary>
	/// Speaks a short description of the currently active tab's purpose and available keyboard shortcuts.
	/// </summary>
	private void ShowContextHelp()
	{
		string text = "";
		switch (CurrentTab())
		{
		case AppTab.Installed:
			text = Loc.T("help.installed", GetShortcutString("Search"), GetShortcutString("ChangeCategory"), GetShortcutString("BatchCategory"), GetShortcutString("OpenModPage"), GetShortcutString("ShowDependencies"), GetShortcutString("QuickFix"), GetShortcutString("ManualID"), GetShortcutString("InstallZip"), GetShortcutString("SaveProfile"), GetShortcutString("ReadDescription"), GetShortcutString("OpenConfig"), GetShortcutString("OpenConfigFile"), GetShortcutString("OpenManifest"), GetShortcutString("LaunchGame"));
			break;
		case AppTab.Updates:
			text = Loc.T("help.updates", GetShortcutString("UpdateAll"), GetShortcutString("ReadDescription"), GetShortcutString("LaunchGame"), GetShortcutString("MarkVersionInstalled"));
			break;
		case AppTab.Backups:
			text = Loc.T("help.backups", GetShortcutString("DeleteOldBackups"), GetShortcutString("OpenBackups"));
			break;
		case AppTab.Discovery:
			text = Loc.T("help.discovery", GetShortcutString("ReadDescription"));
			break;
		case AppTab.Wiki:
			text = Loc.T("help.wiki", GameDisplayName());
			break;
		case AppTab.Walkthroughs:
			string activeGameWalkthroughTitle = GameProfiles.BaseId(_settings.ActiveGame) switch
			{
				"SkyrimSE" => "Skyrim",
				_ => GameProfiles.DisplayNameFor(_settings.ActiveGame)
			};
			text = Loc.T("help.walkthroughs", activeGameWalkthroughTitle, ShortcutIsSet("CycleFocus") ? GetShortcutString("CycleFocus") + " or Tab" : "Tab");
			break;
		case AppTab.Profiles:
			text = Loc.T("help.profiles");
			break;
		case AppTab.ModPriority:
			text = Loc.T("help.modPriority", GetShortcutString("MoveByPlaces"));
			break;
		case AppTab.PluginOrder:
			text = Loc.T("help.pluginOrder", GetShortcutString("AutoSort"), GetShortcutString("PluginSlots"), GetShortcutString("MoveByPlaces"));
			break;
		case AppTab.Creations:
			text = Loc.T("help.creations");
			break;
		case AppTab.MinecraftPacks:
			text = Loc.T("help.minecraftPacks");
			break;
		case AppTab.GameLog:
			text = Loc.T("help.gameLog", GetShortcutString("RefreshLog"), GetShortcutString("OpenLogFile"));
			break;
		case AppTab.SmapiLog:
			text = Loc.T("help.smapiLog", GetShortcutString("QuickFix"), GetShortcutString("RefreshLog"), GetShortcutString("Login"), GetShortcutString("OpenLogFile"));
			break;
		}
		if (!string.IsNullOrEmpty(text))
		{
			Speak(text);
		}
	}

	/// <summary>True when the Discovery list currently ends with the inline "Load more" row.</summary>
	private bool DiscoveryHasLoadMoreRow() =>
		listDiscovery.Items.Count > 0 &&
		listDiscovery.Items[listDiscovery.Items.Count - 1] is DiscoveryLoadMoreRow;

	/// <summary>Removes the inline "Load more" row if present (it is always the last item).</summary>
	private void RemoveDiscoveryLoadMoreRow()
	{
		if (DiscoveryHasLoadMoreRow())
			listDiscovery.Items.RemoveAt(listDiscovery.Items.Count - 1);
	}

	/// <summary>
	/// The number of real search results in the Discovery list, i.e. the item count minus the inline
	/// "Load more" row if it is present. Used so the row is not counted in the spoken "X of Y".
	/// </summary>
	private int DiscoveryResultCount() =>
		listDiscovery.Items.Count - (DiscoveryHasLoadMoreRow() ? 1 : 0);

	/// <summary>
	/// What the manager says it is about to fetch, in the words of the mode doing the fetching.
	///
	/// <para>
	/// The two modes with something particular to name say it: a search names the term, and a category browse
	/// names the category. Hearing "Listing Armour mods" rather than a generic line is the difference between
	/// knowing the dropdown took your choice and having to go back and check it.
	/// </para>
	///
	/// <para>
	/// The mode names themselves are never spoken. They are labels chosen to read well in a dropdown, not
	/// sentences — which is what made the old single template produce "Starting mod Most Popular...".
	/// </para>
	/// </summary>
	private static string DiscoveryStartPhrase(string searchType, string searchTerm, string category) => searchType switch
	{
		"Search" => searchTerm.Length > 0
			? Loc.T("discovery.startSearchFor", searchTerm)
			: Loc.T("discovery.startSearchBlank"),
		"All"          => Loc.T("discovery.startAll"),
		"Trending"     => Loc.T("discovery.startTrending"),
		"Most Popular" => Loc.T("discovery.startPopular"),
		"Recent"       => Loc.T("discovery.startRecent"),
		NexusService.NexusCategoryBrowse => Loc.T("discovery.startCategory", category),
		// Not reachable while the type list and this switch agree, but a mode added to one and not the other
		// should still say something rather than nothing at all.
		_ => Loc.T("discovery.startGeneric")
	};

	/// <summary>
	/// Searches whichever catalogues the user has chosen for the loaded game and populates
	/// <c>listDiscovery</c>. Pass <paramref name="loadMore"/> as <c>true</c> to append the
	/// next page of results instead of starting fresh, and <paramref name="alsoTheOthers"/>
	/// to include the sources the preferred-source mode would otherwise have left out.
	/// </summary>
	private async Task RunDiscovery(bool loadMore = false, bool alsoTheOthers = false)
	{
		// Enter twice on "Load more" jumped a page and added the rows twice.
		if (loadMore && _discoveryRunning > 0) return;

		// Only a game whose mods come from Nexus needs a Nexus key to search. Minecraft's come from Modrinth,
		// which has no accounts — and this used to send a Minecraft player away to log in to a service their
		// game has nothing to do with, rather than searching.
		if (_nexusService.UsesNexus && string.IsNullOrEmpty(_settings.ApiKey))
		{
			Speak(ShortcutIsSet("Login") ? Loc.T("discovery.loginFirst", GetShortcutString("Login")) : Loc.T("discovery.loginFirstNoKey"));
			return;
		}
		if (!loadMore)
		{
			_currentDiscoveryPage = 1;
			listDiscovery.Items.Clear();
			// Lock the page size for this whole search series; honour the tab's session-only
			// selector, falling back to the persisted default.
			_currentDiscoveryPageSize = cmbDiscoveryPageSize.SelectedItem is int n ? n : _settings.DiscoverySearchPageSize;
		}
		else _currentDiscoveryPage++;

		string searchType = cmbDiscoveryType.SelectedItem?.ToString() ?? "Search";
		string searchTerm = txtSearch.Text.Trim();
		string language = (cmbDiscoveryLanguage?.SelectedItem as LanguageOption)?.Name ?? _settings.DiscoveryLanguage;
		// The category applies only to the mode that owns the selector. In any other mode the control is not on
		// screen, and a filter narrowing your results with nothing anywhere to say so is exactly how the language
		// filter used to read as "these mods aren't on Nexus".
		bool browsingCategory = searchType == NexusService.NexusCategoryBrowse;
		string category = browsingCategory
			? (cmbDiscoveryCategory?.SelectedItem as CategoryOption)?.Name ?? ""
			: "";

		// Browsing a category with no category chosen has nothing to browse, and would silently behave as an
		// ordinary popularity listing. Say so instead of quietly doing something else.
		if (!loadMore && browsingCategory && category.Length == 0)
		{
			Speak(Loc.T("discovery.pickCategoryFirst"));
			LetSpeechFinish();
			cmbDiscoveryCategory?.Focus();
			return;
		}

		// Record real text searches (not "load more" pages or the browse modes) to the active game's history.
		if (!loadMore && searchType == "Search" && searchTerm.Length > 0 && _settings.SaveSearchHistory)
			SearchHistoryStore.Add(_settings.ActiveGame, searchTerm);
		// One sentence per mode, rather than one template with the mode's name dropped into it. The template read
		// "Starting mod Recent..." and "Starting mod Nexus Categories...", which is the wiring showing through:
		// those names are labels on a dropdown, not things you can start. Each mode asks the catalogue a different
		// question, so each says what it is about to fetch -- and the two that have something specific to name,
		// a search term and a category, name it.
		string phrase = loadMore ? Loc.T("discovery.loadingMore") : DiscoveryStartPhrase(searchType, searchTerm, category);
		Speak(phrase);
		SetStatus(phrase, speak: false);
		// A newer search replaces this one. Its results, or results arriving after a game switch, must not land in the list.
		// Claimed here and not at the top, or a search refused above would have orphaned the running one.
		int search = ++_discoverySearch;
		string searchGame = _settings.ActiveGame;
		_discoveryRunning++;
		try
		{
			int pageSize = _currentDiscoveryPageSize;
			// Modpacks are Modrinth's alone and are not filtered by the Minecraft version in use — a pack brings its
			// own — so they skip the catalogue plan entirely.
			var (results, total) = SearchingFollowed
				? (loadMore ? (new List<GameMod>(), 0) : await ListFollowedAsync())
				: SearchingForModpacks
				? await ModrinthService.SearchModpacksAsync(searchTerm, (_currentDiscoveryPage - 1) * pageSize, pageSize)
				: await SearchActiveGameCatalogueAsync(
					searchType, searchTerm, _currentDiscoveryPage, pageSize, language, category, alsoTheOthers);
			if (search != _discoverySearch || _settings.ActiveGame != searchGame) return;
			// A page that failed to arrive wasn't read, so the next Load more asks for it again.
			if (loadMore && _nexusService.LastSearchFailed && results.Count == 0) _currentDiscoveryPage--;
			int offset = (_currentDiscoveryPage - 1) * pageSize;

			// Drop the old inline "Load more" row (always last) before appending; firstNewIndex is then
			// the slot of the first freshly loaded result, which we select so focus lands on it.
			RemoveDiscoveryLoadMoreRow();
			int firstNewIndex = listDiscovery.Items.Count;
			MarkAlreadyInstalled(results);
			MarkAlreadyDownloaded(results);
			foreach (var mod in results) listDiscovery.Items.Add(mod);

			// Re-add the inline "Load more" row only while this page returned results AND more remain.
			// It is excluded from the spoken "X of Y" count and, on Enter, triggers RunDiscovery(loadMore: true).
			if (results.Count > 0 && (offset + results.Count) < total)
				listDiscovery.Items.Add(new DiscoveryLoadMoreRow());

			// A language filter can hide most of a game's mods without saying so, because Nexus only knows a
			// mod's language when its author filled that field in — and most don't. On a fresh, language-filtered
			// search, check what the catalogue holds without the filter and say so when it is more, so a thin
			// result reads as "the filter is narrow" rather than "these mods aren't on Nexus".
			string hiddenByLanguage = "";
			// Nexus games only. Minecraft searches Modrinth, which has no language filter, and the count asked Nexus with no
			// game: "Found 13688 modpacks... That is 13688 of this game's 985558 mods. The English language filter hides the
			// rest", the whole of Nexus (heard 2026-11-11).
			if (!loadMore && !string.IsNullOrEmpty(language) && _nexusService.UsesNexus)
			{
				// A name search is compared with the same search unfiltered. Against the whole catalogue it said "27 of this
				// game's 35416 mods", as though the filter hid 35,000 matches.
				bool named = searchType == "Search" && searchTerm.Length > 0;
				int unfiltered = await _nexusService.GetUnfilteredModCountAsync(named ? searchTerm : null);
				if (search != _discoverySearch || _settings.ActiveGame != searchGame) return;
				if (unfiltered > total)
					hiddenByLanguage = " " + Loc.T(named ? "discovery.languageHidingSearch" : "discovery.languageHiding", total, unfiltered, language);
			}

			if (results.Count > 0)
			{
				// How many matched, not how many fit on the page: "Found 20 mods" was said for 27.
				// And packs are called packs: a modpack search said "Found 48 mods".
				string found = loadMore ? Loc.T(SearchingForModpacks ? "discovery.addedPacks" : "discovery.added", results.Count)
					: SearchingForModpacks ? Loc.T(total > results.Count ? "discovery.foundPacksListed" : "discovery.foundPacks", total, results.Count)
					: total > results.Count ? Loc.T("discovery.foundListed", total, results.Count)
					: Loc.T(results.Count == 1 ? "discovery.foundOne" : "discovery.found", results.Count);
				Speak(found + hiddenByLanguage);
				// Fresh search lands on the first result; Load more lands on the first new result.
				listDiscovery.SelectedIndex = loadMore ? firstNewIndex : 0;
			}
			else
			{
				Speak(_nexusService.LastSearchFailed
					? Loc.T("discovery.noAnswer")
					: (loadMore ? Loc.T(SearchingForModpacks ? "discovery.noMorePacks" : "discovery.noMore")
						: Loc.T(SearchingForModpacks ? "discovery.nonePacks" : "discovery.none")) + hiddenByLanguage);
			}
		}
		catch (Exception ex)
		{
			if (loadMore) _currentDiscoveryPage--;
			SpeakBox(Loc.T("discovery.errorBox", FriendlyError(ex)));
		}
		finally
		{
			_discoveryRunning--;
			// "Running search…" / "Loading more…" are transient; return the title to the resting status
			// (Nexus connection or "Ready") so it never sits on a stale "Loading more…" forever.
			if (search == _discoverySearch) ResetStatus();
		}
	}

	private int _discoverySearch;
	private int _discoveryRunning;

	/// <summary>
	/// Shows the Language filter for a game that searches Nexus and hides it for one that doesn't, and says which.
	///
	/// <para>
	/// Minecraft searches Modrinth, which has no language to filter by, so the filter did nothing there. It was still on
	/// screen, read "English (515182)", and the list of languages was asked of Nexus with no game, so the counts were the
	/// whole of Nexus (heard 2026-11-11).
	/// </para>
	/// </summary>
	private bool ShowLanguageFilterForThisGame()
	{
		bool nexus = _nexusService.UsesNexus;
		if (cmbDiscoveryLanguage != null) cmbDiscoveryLanguage.Visible = nexus;
		return nexus;
	}

	/// <summary>
	/// Fills the Discovery "Language" dropdown with the languages that actually have mods for the active game
	/// (most common first, with counts), and selects the user's saved language preference. Falls back to the
	/// starter list if the facet query returns nothing.
	/// </summary>
	private async Task PopulateDiscoveryLanguagesAsync()
	{
		if (cmbDiscoveryLanguage == null) return;
		if (!ShowLanguageFilterForThisGame()) return;
		// Taken before the request, which asks about the game open when it is made.
		string game = _settings.ActiveGame;
		var langs = await _nexusService.GetModLanguagesAsync();
		if (langs.Count == 0) return; // network failure etc. — keep whatever is already there
		// A quick switch has two of these in flight, and the one for the game left can answer last. The open game's own fills it.
		if (!string.Equals(_settings.ActiveGame, game, StringComparison.Ordinal)) return;

		string desired = _settings.DiscoveryLanguage; // "" = Any language
		_suppressDiscoveryLanguageEvent = true;
		cmbDiscoveryLanguage.Items.Clear();
		cmbDiscoveryLanguage.Items.Add(new LanguageOption { Name = "" }); // Any language
		int selectIndex = 0;
		foreach (var (name, count) in langs)
		{
			cmbDiscoveryLanguage.Items.Add(new LanguageOption { Name = name, Count = count });
			if (string.Equals(name, desired, StringComparison.OrdinalIgnoreCase))
				selectIndex = cmbDiscoveryLanguage.Items.Count - 1;
		}
		cmbDiscoveryLanguage.SelectedIndex = selectIndex;
		_suppressDiscoveryLanguageEvent = false;
	}

	/// <summary>
	/// Flags the search results that are mods the user already has, so a row can say so before they act on it.
	///
	/// <para>
	/// Matched on the Nexus id first, which is an exact fact and settles it outright. A mod with no id recorded
	/// falls back to <see cref="ModNameMatch.IsConfident"/> — the same deliberately strict rules Auto Match uses,
	/// where a partial name match only counts with the author agreeing. That matters because plenty of installed
	/// mods have never been linked to a Nexus page, and those are exactly the ones a user is most likely to go
	/// looking for and re-download.
	/// </para>
	///
	/// <para>
	/// A wrong "Installed" is worse than a missing one — it would talk somebody out of a mod they do not have —
	/// so nothing looser than those two tests is used.
	/// </para>
	/// </summary>
	private void MarkAlreadyInstalled(List<GameMod> results)
	{
		if (results.Count == 0 || _allInstalledMods.Count == 0) return;

		var installedIds = new HashSet<string>(
			_allInstalledMods
				.Where(m => !m.IsGroup && !string.IsNullOrWhiteSpace(m.NexusID))
				.Select(m => m.NexusID!.Trim()),
			StringComparer.OrdinalIgnoreCase);

		var unmatched = _allInstalledMods
			.Where(m => !m.IsGroup && string.IsNullOrWhiteSpace(m.NexusID))
			.ToList();

		foreach (GameMod result in results)
		{
			if (!string.IsNullOrWhiteSpace(result.NexusID) && installedIds.Contains(result.NexusID.Trim()))
			{
				result.IsInstalled = true;
				continue;
			}

			result.IsInstalled = unmatched.Any(m => ModNameMatch.IsConfident(m, result));
		}
	}

	/// <summary>
	/// Stamps each search result with when its archive was last downloaded, read from the active game's downloads
	/// folder.
	///
	/// <para>
	/// The folder is the record, deliberately, rather than a ledger written at download time. An archive the user
	/// has deleted stops being claimed the moment it goes, which is what makes the row's promise a true one: the
	/// point of hearing "you downloaded this three weeks ago" is to go and find the file, and a ledger would go on
	/// pointing at files that are no longer there. It also means every download already sitting in the folder
	/// counts, including the ones fetched long before this was written.
	/// </para>
	///
	/// <para>
	/// The newest archive wins when a mod has several, which is the usual case for a mod updated more than once —
	/// the question being answered is "when did I last take a copy", not "when did I first".
	/// </para>
	/// </summary>
	private void MarkAlreadyDownloaded(List<GameMod> results)
	{
		if (results.Count == 0) return;

		var newest = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
		try
		{
			foreach (string file in Directory.EnumerateFiles(downloadsPath))
			{
				string? modId = ModDisplayName.ModIdFromArchiveName(Path.GetFileName(file));
				if (modId == null) continue;

				DateTime when = File.GetLastWriteTime(file);
				if (!newest.TryGetValue(modId, out DateTime already) || when > already)
					newest[modId] = when;
			}
		}
		catch (Exception ex)
		{
			// Not being able to read the folder is a reason to say nothing about downloads, never a reason to
			// fail a search the user asked for.
			DiagnosticLog.WriteException("Discovery", $"reading the downloads folder {downloadsPath}", ex);
			return;
		}

		foreach (GameMod result in results)
		{
			result.LastDownloaded =
				!string.IsNullOrWhiteSpace(result.NexusID) && newest.TryGetValue(result.NexusID.Trim(), out DateTime when)
					? new DateTimeOffset(when)
					: null;
		}
	}

	/// <summary>
	/// Re-marks the Discovery results against the installed list as it now stands, and rewrites the rows so a mod
	/// that has just arrived stops inviting you to fetch it again.
	///
	/// <para>
	/// Results are marked when they are fetched, but nothing is installed <em>from</em> that list — Enter on a
	/// result opens its Nexus page, and the mod comes back through the download handler some time later, usually
	/// while the user is still in their browser. So the moment worth re-checking is the one after a rescan, which
	/// is where this is called from.
	/// </para>
	///
	/// <para>
	/// Two rules from hard experience govern the rebuild. It must <b>keep the user's place</b>, because losing it
	/// means finding it again by ear. And the move must be <b>silent</b>: a selection the program sets is not
	/// announced by the screen reader, so a bare position would be read against no mod at all. The one case worth
	/// speaking is the row the user is actually sitting on changing meaning under them, which is said in full.
	/// </para>
	/// </summary>
	private void RefreshDiscoveryInstalledMarks()
	{
		if (listDiscovery == null || listDiscovery.Items.Count == 0) return;

		List<GameMod> results = listDiscovery.Items.OfType<GameMod>().ToList();
		if (results.Count == 0) return;

		bool[] before = results.Select(r => r.IsInstalled).ToArray();
		DateTimeOffset?[] downloadedBefore = results.Select(r => r.LastDownloaded).ToArray();
		MarkAlreadyInstalled(results);
		// A mod arriving in the downloads folder changes its row just as installing it does, and a rescan is the
		// moment both become true — the download that led to the install landed first.
		MarkAlreadyDownloaded(results);

		// Nothing became installed or downloaded since these results were fetched, so leave the list completely
		// alone. A rebuild that changes nothing is still a rebuild, and this runs after every rescan.
		if (!results.Where((r, i) => r.IsInstalled != before[i] || r.LastDownloaded != downloadedBefore[i]).Any()) return;

		int at = listDiscovery.SelectedIndex;
		bool selectedRowChanged =
			at >= 0 && at < results.Count && results[at].IsInstalled != before[at];
		bool hadLoadMore = DiscoveryHasLoadMoreRow();

		_movingListSilently = true;
		try
		{
			listDiscovery.BeginUpdate();
			listDiscovery.Items.Clear();
			foreach (GameMod result in results) listDiscovery.Items.Add(result);
			// Put the "Load more" row back where it was, or the rest of the results become unreachable.
			if (hadLoadMore) listDiscovery.Items.Add(new DiscoveryLoadMoreRow());
			listDiscovery.EndUpdate();

			if (listDiscovery.Items.Count > 0)
				listDiscovery.SelectedIndex = Math.Clamp(at, 0, listDiscovery.Items.Count - 1);
			// The selection and the focus rectangle are two different things, and only the second is what a screen
			// reader reads when focus next arrives. See AlignListCaretToSelection.
			AlignListCaretToSelection(listDiscovery);
		}
		finally { _movingListSilently = false; }

		// Said only when the user is in the list AND it is their own row that changed. Anywhere else, the install
		// has already announced itself and this would be a second announcement about something they cannot see.
		if (selectedRowChanged && listDiscovery.Focused && listDiscovery.SelectedItem is GameMod current)
			Speak(Loc.T("discovery.nowInstalled", current.Name), interrupt: false);
	}

	/// <summary>
	/// Fills the Discovery "Category" dropdown with the Nexus categories that actually have mods for the active
	/// game, most-populated first and with counts. Keeps whatever is already there if the facet returns nothing,
	/// so a network failure leaves a usable "Any category" rather than an empty control.
	///
	/// <para>
	/// The selection is deliberately not carried over between games or sessions: a category is a choice about the
	/// search in front of you, unlike the language, which is a standing preference worth remembering.
	/// </para>
	/// </summary>
	private async Task PopulateDiscoveryCategoriesAsync()
	{
		if (cmbDiscoveryCategory == null) return;
		// Here too, because startup comes through here and not through the game switch that fills the languages.
		ShowLanguageFilterForThisGame();

		// Fetched once per game rather than on every data refresh. A game's categories do not change while the
		// manager is open, and RefreshAllData runs often enough that asking each time would be a request spent
		// on an answer we already have.
		string game = _settings.ActiveGame;
		if (string.Equals(_discoveryCategoriesGame, game, StringComparison.Ordinal)) return;

		var categories = await _nexusService.GetModCategoriesAsync();
		// The game left can answer after the game switched to, which would leave its categories here and mark them fetched.
		if (cmbDiscoveryCategory == null || categories.Count == 0 || !string.Equals(_settings.ActiveGame, game, StringComparison.Ordinal)) return;
		_discoveryCategoriesGame = game;

		cmbDiscoveryCategory.Items.Clear();
		cmbDiscoveryCategory.Items.Add(new CategoryOption { Name = "" });   // Any category
		foreach (var (name, count) in categories)
			cmbDiscoveryCategory.Items.Add(new CategoryOption { Name = name, Count = count });

		cmbDiscoveryCategory.SelectedIndex = 0;
	}

	/// <summary>
	/// Shows the Nexus category selector only while the search type it belongs to is chosen, and says so when it
	/// appears.
	///
	/// <para>
	/// A control arriving in the middle of a toolbar is invisible to somebody working along it by Tab, so its
	/// arrival is announced rather than left to be discovered. Nothing is said when it goes: the type they just
	/// chose is the answer, and a line about a control disappearing is noise on every other switch.
	/// </para>
	/// </summary>
	private void UpdateDiscoveryCategoryVisibility(bool announce)
	{
		if (cmbDiscoveryCategory == null || _lblDiscoveryCategory == null) return;

		bool wanted = string.Equals(
			cmbDiscoveryType.SelectedItem?.ToString(), NexusService.NexusCategoryBrowse, StringComparison.Ordinal);
		if (cmbDiscoveryCategory.Visible == wanted) return;

		_lblDiscoveryCategory.Visible = wanted;
		cmbDiscoveryCategory.Visible = wanted;

		if (wanted && announce) Speak(Loc.T("discovery.categoryListShown"));
	}

	/// <summary>
	/// Says <paramref name="text"/> through whatever is listening — see <see cref="IAnnouncer"/>, which on
	/// Windows is Tolk and reloads itself if the reader has restarted underneath us.
	/// </summary>
	private void Speak(string text) => Speak(text, interrupt: false);

	/// <summary>
	/// Says <paramref name="text"/> through the active screen reader. When <paramref name="interrupt"/>
	/// is true, it cuts off any in-progress/queued speech first — used to take full control of the wording
	/// and ordering of an announcement (e.g. so a list's title is spoken before its first item).
	/// </summary>
	private void Speak(string text, bool interrupt)
	{
		// Straight after a prompt closes onto a list, what is said is the answer's result, and it rides on the list's own
		// return announcement rather than claiming the floor from it. Claiming it stopped the swallow, so the reader's
		// reading of the row came through after ours and the row was heard twice, or ours named a row that the answer
		// had just removed. Measured with NVDA 2026-11-10 on Delete and Ctrl+Shift+Y in Updates. See SayOnReturnTo.
		if (_promptClosedOnto is { } onto && HandToArrival(onto, text, namesRow: false)) return;

		// Anything handed over and still waiting was said before this, so it goes first. Left to the announcement it was
		// given to, it came out after: deleting a mod finished before the swallow did, and "Deleted Chat Commands" was heard
		// ahead of "Deleting Chat Commands. Backing it up first". Measured 2026-11-10.
		if (_resultForArrival is { } waiting)
		{
			_resultForArrival = null;
			// Ended first, or the two run together: "Updating (1/1): VoiceOverFrameworkMod Download cancelled."
			text = EndSentence(waiting.Text) + " " + text;
		}

		// Saying something deliberately ends any window in which the reader is being silenced.
		//
		// SpeakListPosition swallows the reader across ~300ms when the program has moved focus, and Tolk.Silence()
		// cannot tell the reader's voice from ours — so anything the app said during that window was cut off with
		// it. Changing a setting showed this plainly: choosing a value closed the chooser, which armed the swallow,
		// and "MenuClosedAnnouncements set to false" was then silenced before it could be heard, leaving the user
		// to arrow off the row and back to learn what the value had become. Claiming the generation here abandons
		// the swallow instead, and lets what we actually meant to say through.
		_speakListGeneration++;

		// If the screen reader was unloaded externally (e.g., NVDA restarted),
		// attempt a silent reload before speaking so users don't lose announcements.
		// Reloading a bridge that has gone away, and the reporting when it will not come back, both belong to
		// the implementation now; this asks only whether anyone is listening. See IAnnouncer.
		if (_announcer.IsAvailable)
		{
			_announcer.Speak(text, interrupt);
			return;
		}

		// Nothing was said, and saying so is the whole point. Everything above this line reports the failure
		// to the log, which is exactly where the one person who needs to know will not look — they are waiting
		// for the app to speak.
		ReportSpeechUnavailable();
	}

	/// <summary>Set once the user has been told speech is not working, so they are told once and not per phrase.</summary>
	private bool _speechFailureReported;

	/// <summary>
	/// Tells the user, once per session, that the screen-reader bridge is not answering — through the two
	/// channels that do not depend on it.
	///
	/// The first is a sound, because the audio engine is NAudio and has nothing to do with Tolk, so it still
	/// works when Tolk does not. The second is an ordinary message box: Tolk failing is not the same as the
	/// screen reader failing — usually it means <c>Tolk.dll</c> is missing or blocked, while NVDA or JAWS is
	/// running perfectly well and reading window contents over MSAA and UIA as it always does. A plain WinForms
	/// dialog is therefore very likely to be read aloud even in the exact case where nothing the app says
	/// through Tolk can be.
	///
	/// Once only. A failure here repeats on every single phrase, and a dialog per phrase would be a far worse
	/// accessibility bug than the one it is reporting.
	/// </summary>
	private void ReportSpeechUnavailable()
	{
		if (_speechFailureReported) return;
		_speechFailureReported = true;

		try { _soundEngine?.Play("error"); }
		catch (Exception ex) { DiagnosticLog.WriteException("Speech", "sounding the speech-failure alert", ex); }

		try
		{
			MessageBox.Show(this, Loc.T("speech.unavailableBody", DiagnosticLog.Path),
				Loc.T("speech.unavailableTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Speech", "showing the speech-failure notice", ex); }
	}

	/// <summary>
	/// Speaks a potentially long passage (e.g. a mod description or AI answer) in sentence-sized chunks queued
	/// back-to-back. Screen readers can silently cut off a single very long spoken string; splitting it into
	/// several queued utterances makes the whole thing read. When <paramref name="interrupt"/> is true the first
	/// chunk cuts off any in-progress speech; otherwise every chunk simply queues (used when the passage should
	/// follow an earlier announcement, e.g. after "Analyzing…"). The rest always queue in order.
	/// </summary>
	private void SpeakLong(string text, bool interrupt = true)
	{
		if (string.IsNullOrWhiteSpace(text)) return;
		System.Collections.Generic.List<string> chunks = ChunkForSpeech(text, 300);
		for (int i = 0; i < chunks.Count; i++)
			Speak(chunks[i], interrupt: interrupt && i == 0);
	}

	/// <summary>
	/// Normalizes lone LF or CR line endings to CRLF. A multiline WinForms TextBox only renders a line break on
	/// CRLF, so raw "\n" text (from HTML, API responses, etc.) otherwise runs together on one line.
	/// </summary>
	private static string NormalizeNewlines(string s) =>
		string.IsNullOrEmpty(s) ? "" : System.Text.RegularExpressions.Regex.Replace(s, @"\r\n?|\n", "\r\n");

	/// <summary>Splits text into chunks no longer than <paramref name="maxLen"/>, breaking on sentence/line
	/// boundaries where possible and hard-splitting any single piece that's still too long.</summary>
	private static System.Collections.Generic.List<string> ChunkForSpeech(string text, int maxLen)
	{
		var result = new System.Collections.Generic.List<string>();
		string[] parts = System.Text.RegularExpressions.Regex.Split(text.Trim(), @"(?<=[\.\!\?])\s+|\r?\n+");
		var sb = new StringBuilder();
		void Flush() { if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); } }
		foreach (string raw in parts)
		{
			string piece = raw.Trim();
			if (piece.Length == 0) continue;
			if (piece.Length > maxLen)
			{
				Flush();
				for (int i = 0; i < piece.Length; i += maxLen)
					result.Add(piece.Substring(i, Math.Min(maxLen, piece.Length - i)));
				continue;
			}
			if (sb.Length + piece.Length + 1 > maxLen) Flush();
			if (sb.Length > 0) sb.Append(' ');
			sb.Append(piece);
		}
		Flush();
		return result;
	}

	/// <summary>
	/// Waits for a just-spoken announcement to finish before continuing — before the startup loading speaks over
	/// the welcome, or before exit tears Tolk down mid-sentence.
	///
	/// Always yields, never blocks. There was a <c>Thread.Sleep</c> version of this for use while closing, on the
	/// reasoning that a pause during exit costs nothing; it cost the message itself. Speech synthesis needs the
	/// UI thread's message loop to be running, so blocking that thread to wait for speech means the speech does
	/// not start until the wait is over.
	///
	/// A reader that can say when it has finished (<see cref="IAnnouncer.IsSpeakingForCertain"/>) is simply asked,
	/// so this ends with the sentence at any speech rate, and <paramref name="minMs"/> does not apply.
	///
	/// Any other reader cannot say: <see cref="IAnnouncer.IsSpeaking"/> is only ever "probably not", and most
	/// return false even while talking. So a fixed minimum sized to the sentence is waited out regardless, which
	/// is a guess at how fast the user has their reader set, then it stops at the hard cap.
	/// </summary>
	private async Task WaitForSpeechAsync(int minMs = 2500, int maxMs = 12000)
	{
		if (_announcer.IsSpeakingForCertain is not null)
		{
			var watch = new SpeechFinishWatch(Environment.TickCount64, maxMs);
			while (_announcer.IsSpeakingForCertain is bool speaking
				&& !watch.IsFinished(Environment.TickCount64, speaking))
			{
				await Task.Delay(25);
			}
			return;
		}

		const int step = 100;
		int elapsed = 0;
		while (elapsed < maxMs)
		{
			await Task.Delay(step);
			elapsed += step;
			if (!_announcer.IsSpeaking && elapsed >= minMs) break;
		}
	}

	/// <summary>
	/// Appends a sentence period to the accessible name of value/state-bearing controls (combo boxes, check boxes,
	/// list boxes, radio buttons, sliders) anywhere under <paramref name="root"/>, so a screen reader pauses
	/// between the field name and the value or state it reads next — "Sound volume. 100" rather than the two run
	/// together. Controls with no explicit AccessibleName use their visible Text as the basis (the visible text is
	/// left unchanged). Idempotent: a name already ending in a period is skipped, so it's safe to re-run.
	/// </summary>
	private static void ApplyScreenReaderPauses(Control root)
	{
		foreach (Control c in root.Controls)
		{
			switch (c)
			{
				case CheckBox:
				case RadioButton:
					// Text is a real label for these, so fall back to it when there's no explicit name.
					AddReadingPause(c, !string.IsNullOrWhiteSpace(c.AccessibleName) ? c.AccessibleName : c.Text);
					break;
				case ComboBox:
				case ListBox:
				case TrackBar:
					// Their Text is the current value/selection, NOT a label — only add the pause to an explicit
					// accessible name, otherwise we'd name the field after its own value and a screen reader would
					// then announce that value twice (the "name" and the selection).
					if (!string.IsNullOrWhiteSpace(c.AccessibleName)) AddReadingPause(c, c.AccessibleName);
					break;
				default:
					if (c.HasChildren) ApplyScreenReaderPauses(c);
					break;
			}
		}
	}

	/// <summary>Trims trailing whitespace/colon from <paramref name="basis"/> and, if it isn't already a sentence,
	/// sets <paramref name="c"/>'s accessible name to it followed by a period (the reading pause).</summary>
	private static void AddReadingPause(Control c, string basis)
	{
		basis = (basis ?? "").TrimEnd().TrimEnd(':').TrimEnd();
		if (basis.Length > 0 && !basis.EndsWith("."))
			c.AccessibleName = basis + ".";
	}

	/// <summary>
	/// Plays a periodic loading sound while anything that started a <see cref="LoadingPulse"/> is still running.
	/// Exits and plays the completion sound once all pending checks finish.
	/// </summary>
	private async Task RunLoadingLoop()
	{
		// Something starting within a pulse of the last one finishing finds this loop still going, and it carries on.
		if (Interlocked.Exchange(ref _pulseRunning, 1) == 1) return;
		try
		{
			while (IsLoading)
			{
				_soundEngine.Play("loading_indicator");
				await Task.Delay(1500);
			}
		}
		finally
		{
			Volatile.Write(ref _pulseRunning, 0);
		}
	}

	private int _pulseRunning;
	private int _loadingCount;
	private LoadingPulse? _updateCheckPulse;

	/// <summary>Whether anything long-running is under way. Was one flag, so a BepInEx install finishing ended an update check's pulse.</summary>
	private bool IsLoading => Volatile.Read(ref _loadingCount) > 0;

	private LoadingPulse StartLoadingPulse()
	{
		Interlocked.Increment(ref _loadingCount);
		Fire(RunLoadingLoop(), "RunLoadingLoop");
		return new LoadingPulse(this);
	}

	/// <summary>One operation's share of the pulse. Ending it twice counts once, as failure paths may well do.</summary>
	private sealed class LoadingPulse(Form1 owner)
	{
		private int _ended;

		public void End()
		{
			if (Interlocked.Exchange(ref _ended, 1) == 0) Interlocked.Decrement(ref owner._loadingCount);
		}
	}
}
