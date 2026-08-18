using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using DavyKager;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewMod = KinetixModManager.GameMod;

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

	/// <summary>Returns a human-readable key label for <paramref name="action"/> (e.g. "Ctrl+R").</summary>
	private string GetShortcutString(string action)
	{
		if (_settings.Shortcuts.TryGetValue(action, out var value))
		{
			if (value == Keys.None)
			{
				return "Unmapped";
			}
			StringBuilder stringBuilder = new StringBuilder();
			if ((value & Keys.Control) == Keys.Control)
			{
				stringBuilder.Append("Ctrl+");
			}
			if ((value & Keys.Shift) == Keys.Shift)
			{
				stringBuilder.Append("Shift+");
			}
			if ((value & Keys.Alt) == Keys.Alt)
			{
				stringBuilder.Append("Alt+");
			}
			stringBuilder.Append(value & Keys.KeyCode);
			return stringBuilder.ToString();
		}
		return "Unmapped";
	}

	/// <summary>Returns <c>true</c> if <paramref name="e"/> matches the configured shortcut for <paramref name="action"/>.</summary>
	private bool IsShortcut(KeyEventArgs e, string action)
	{
		if (!_settings.Shortcuts.TryGetValue(action, out var value))
		{
			return false;
		}
		if (value == Keys.None)
		{
			return false;
		}
		return e.KeyData == value;
	}

	/// <summary>
	/// Applies the current search query and category filter to the installed mods list,
	/// then rebuilds the list box via <see cref="RebuildInstalledListBox"/>.
	/// </summary>
	private void FilterInstalledMods()
	{
		string query = txtSearchInstalled.Text.Trim().ToLower();
		string category = cmbCategoryFilter.SelectedItem?.ToString() ?? "All Categories";
		// Status filter by combo index (language-independent): 0 All, 1 Enabled, 2 Disabled, 3 Has Note.
		int status = cmbStatusFilter?.SelectedIndex ?? 0;
		bool StatusMatch(StardewMod m) => status switch
		{
			1 => m.IsEnabled,
			2 => !m.IsEnabled,
			3 => !string.IsNullOrWhiteSpace(m.Note),
			_ => true
		};
		// Keep the user's place where the mod they were on survives the filter. Typing in the search box narrows
		// the list under them, and landing back at the top each keystroke loses a mod they had just found.
		string? selectedBefore = (listInstalled.SelectedItem as StardewMod)?.UniqueId;

		listInstalled.BeginUpdate();
		listInstalled.Items.Clear();
		List<StardewMod> list = _allInstalledMods.Where((StardewMod m) => (m.Name.ToLower().Contains(query) || m.Author.ToLower().Contains(query) || m.Note.ToLower().Contains(query)) && (category == "All Categories" || m.Category == category) && StatusMatch(m)).ToList();
		foreach (StardewMod item in list)
		{
			listInstalled.Items.Add(item);
		}
		listInstalled.EndUpdate();
		ReselectMod(selectedBefore);
		if (!string.IsNullOrEmpty(query) || category != "All Categories" || status != 0)
		{
			Speak(Loc.T("discovery.modsFound", list.Count));
		}
	}

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
		list.KeyDown += (_, e) =>
		{
			if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) { e.Handled = e.SuppressKeyPress = true; }
		};
	}

	private async void List_Enter(object? sender, EventArgs e)
	{
		// Consumed before any early return, for the same reason as in List_SelectedIndexChanged.
		bool announceRowName = _announceRowNameOnNextChange;
		bool announceListName = _announceListNameOnNextChange;
		_announceRowNameOnNextChange = false;
		_announceListNameOnNextChange = false;

		if (sender is not ListBox listBox) return;

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
		int itemCount = listBox.Name == "listDiscovery" ? DiscoveryResultCount() : listBox.Items.Count;
		string text = Loc.T("common.position", listBox.SelectedIndex + 1, itemCount);

		// "The screen reader speaks the list name and selected item first" holds when the USER moved focus here —
		// tabbing in, or clicking. It does not hold when the program put focus back, which is what closing a view
		// does: the reader treats it as focus never having left and says nothing, so all that was heard was a
		// position with no idea which mod it belonged to. Editing a mod's settings and pressing Escape landed
		// exactly there. When the program moved focus, the row names itself.
		if (announceRowName && listBox.SelectedItem != null)
			text = RowThenPosition(listBox.SelectedItem, text);

		// Focus arriving at a list is announced by a screen reader as its name, then the row it landed on, then
		// where that row sits — "Installed Mods List. Stardew Access… 129 of 149". When the reader's version of that
		// has to be swallowed (see SpeakListPosition), the name goes with it, and a list that no longer says what it
		// is leaves the user to work it out from the row. So the name is put back at the front, in the same order
		// the reader would have used. Only on the way in: moving within a list must not repeat it.
		if (announceListName)
			text = ListNameThenRest(listBox, text);

		SpeakListPosition(listBox, text, replaceReader: announceRowName);
	}

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
		return text.Length == 0 ? position : Loc.T("common.rowThenPosition", text, position);
	}

	/// <summary>
	/// "&lt;the list's name&gt;. &lt;the rest&gt;" — what a screen reader says when focus lands on a list, in the
	/// order it says it.
	///
	/// The name comes from <see cref="Control.AccessibleName"/>, the same string the reader would have read, so the
	/// two can never drift apart. A container deliberately silenced with <see cref="SilentAccessibleName"/> stays
	/// silent, and a list with no name of its own adds nothing rather than an empty pause.
	/// </summary>
	private static string ListNameThenRest(ListBox list, string rest)
	{
		string name = (list.AccessibleName ?? "").Trim();
		if (name.Length == 0) return rest;
		if (name.EndsWith(".", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 1).TrimEnd();
		return name.Length == 0 ? rest : Loc.T("common.listNameThenRest", name, rest);
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
	private async void SpeakListPosition(ListBox list, string text, bool replaceReader = false)
	{
		if (_shuttingDown) return;

		long now = Environment.TickCount64;
		if (ReferenceEquals(_lastPosList, list) && _lastPosIndex == list.SelectedIndex && now - _lastPosTicks < 700)
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
		for (int i = 0; i < 12; i++)
		{
			if (_shuttingDown || list.IsDisposed || generation != _speakListGeneration) return;
			SilenceSpeech();
			await Task.Delay(25);
		}
		if (_shuttingDown || list.IsDisposed || !list.Focused || generation != _speakListGeneration) return;
		Speak(text, interrupt: true);
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
	/// So all three are said here, in the order they are wanted: the result first, then the window, then whatever
	/// focus came back to. Tolk queues, so they are simply spoken one after another.
	/// </para>
	/// </summary>
	private async void SpeakAfterForeignWindow(string text)
	{
		if (_shuttingDown || string.IsNullOrWhiteSpace(text)) return;
		if (!await SettleAfterForeignWindowAsync()) return;
		SpeakWithBearings(text);
	}

	/// <summary>
	/// Absorbs the screen reader's re-read of the main window after a foreign window has closed, and reports
	/// whether the caller still owns the announcement afterwards.
	///
	/// <para>
	/// Awaited by anything that means to speak, or to show something that speaks, in the moment after a file
	/// picker closes. A prompt opened straight after one had its question cut off and left only the name of the
	/// focused button audible — "…Connected as SeanTerry01", then "Yes", with the question itself never heard.
	/// Clearing the field first is what lets whatever comes next be the thing that is heard, whether that is one
	/// sentence from <see cref="SpeakWithBearings"/> or a whole prompt.
	/// </para>
	///
	/// <para>
	/// Returns <c>false</c> when something else has claimed the floor in the meantime, in which case the caller
	/// should say nothing: whatever claimed it knows more than this call did.
	/// </para>
	/// </summary>
	private async Task<bool> SettleAfterForeignWindowAsync()
	{
		// Claimed so that anything the app deliberately says next abandons this rather than being swallowed by
		// it — Speak() bumps the same counter. See the note in Speak.
		int generation = ++_speakListGeneration;

		for (int i = 0; i < 20; i++)
		{
			if (_shuttingDown || generation != _speakListGeneration) return false;
			SilenceSpeech();
			await Task.Delay(25);
		}

		return !_shuttingDown && generation == _speakListGeneration;
	}

	/// <summary>
	/// Says a result and then where the user now is: the window's caption, and whatever focus came back to.
	///
	/// The orientation is the half a plain swallow throws away. The caption after a foreign window closes is not
	/// wrong, only badly timed, so it is put back behind the result rather than destroyed. Tolk queues, so the
	/// three simply follow one another.
	/// </summary>
	private void SpeakWithBearings(string text)
	{
		if (_shuttingDown || string.IsNullOrWhiteSpace(text)) return;

		Speak(text, interrupt: true);
		Speak(Text);
		AnnounceFocusRestored();
	}

	/// <summary>Which announcement is the current one. See <see cref="SpeakListPosition"/>.</summary>
	private int _speakListGeneration;

	/// <summary>Stops whatever the screen reader is currently saying, if one is loaded.</summary>
	private static void SilenceSpeech()
	{
		if (!Tolk.IsLoaded()) return;
		try { Tolk.Silence(); } catch { }
	}

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
		if (_isLoading || _shuttingDown) return;
		await Task.Delay(120);
		if (list.IsDisposed || !list.Focused || list.Items.Count != 0 || _isLoading) return;

		long now = Environment.TickCount64;
		if (ReferenceEquals(_lastEmptyList, list) && now - _lastEmptyTicks < 1000) return;
		_lastEmptyList = list;
		_lastEmptyTicks = now;
		Speak(Loc.T("common.listEmpty"));
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

	/// <summary>Updates the bottom status-bar label with <paramref name="text"/>.</summary>
	/// <summary>The status the title bar should rest at between operations: the Nexus connection when logged in,
	/// otherwise a neutral "Ready". Only this should linger in the title — transient operation statuses
	/// (installing, rebuilding, downloading, …) reset to it when they finish so the title never shows a stale
	/// message long after the work is done.</summary>
	private string RestingStatus() =>
		!string.IsNullOrEmpty(_nexusService.NexusUser) && _nexusService.NexusUser != "Unknown User"
			? Loc.T("status.connectedAs", _nexusService.NexusUser)
			: Loc.T("status.ready");

	/// <summary>Returns the title bar to the resting status after a transient operation, without speaking it.</summary>
	private void ResetStatus() => SetStatus(RestingStatus(), speak: false);

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
			string text2 = Loc.T("status.titleFormat", GameDisplayName(), text);
			Text = text2;
			if (speak)
			{
				Speak(text);
			}
		}
	}

	private async void List_SelectedIndexChanged(object? sender, EventArgs e)
	{
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

		if (!(sender is ListBox { SelectedItem: not null } list) || _isLoading || !list.Focused)
		{
			return;
		}
		// No wait for a move the program made — see SpeakListPosition for why the reader has to be caught as it
		// starts rather than waited for.
		await Task.Delay(announceRowName ? 0 : 100);
		if (!list.Focused) return;
		// The Discovery list's inline "Load more" row is an action, not a numbered result: the screen
		// reader already reads its row text on focus, so add no position announcement.
		if (list.SelectedItem is DiscoveryLoadMoreRow) return;
		// Exclude that row from the Discovery count so positions read "20 of 20", not "20 of 21".
		int itemCount = list.Name == "listDiscovery" ? DiscoveryResultCount() : list.Items.Count;
		string text = Loc.T("common.position", list.SelectedIndex + 1, itemCount);

		// The row's own text is normally the screen reader's job: it reads the row the user arrowed onto, and this
		// adds the position a moment later. But a reader only announces a row the USER moved to — when the program
		// moves the selection, it says nothing at all, and the position was being read out with no idea which mod
		// it belonged to. So a programmatic move says the row itself first, and only a programmatic one, or an
		// ordinary arrow-key press would hear the name twice.
		if (announceRowName)
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
		SpeakListPosition(list, text, replaceReader: announceRowName);
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
			text = Loc.T("help.updates", GetShortcutString("UpdateAll"), GetShortcutString("ReadDescription"), GetShortcutString("LaunchGame"));
			break;
		case AppTab.Backups:
			text = Loc.T("help.backups", GetShortcutString("DeleteOldBackups"), GetShortcutString("OpenBackups"));
			break;
		case AppTab.Discovery:
			text = Loc.T("help.discovery", GetShortcutString("ReadDescription"));
			break;
		case AppTab.Wiki:
			text = Loc.T("help.wiki");
			break;
		case AppTab.Walkthroughs:
			string activeGameWalkthroughTitle = GameProfiles.BaseId(_settings.ActiveGame) switch
			{
				"SkyrimSE" => "Skyrim",
				_ => GameProfiles.DisplayNameFor(_settings.ActiveGame)
			};
			text = Loc.T("help.walkthroughs", activeGameWalkthroughTitle);
			break;
		case AppTab.Profiles:
			text = Loc.T("help.profiles");
			break;
		case AppTab.ModPriority:
			text = Loc.T("help.modPriority");
			break;
		case AppTab.PluginOrder:
			text = Loc.T("help.pluginOrder", GetShortcutString("AutoSort"), GetShortcutString("PluginSlots"));
			break;
		case AppTab.Creations:
			text = Loc.T("help.creations");
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

	/// <summary>
	/// Sentinel placeholder for the inline "Load more" row pinned to the bottom of the Discovery
	/// results list. It is never a real search result: it is excluded from the spoken "X of Y"
	/// position count, and pressing Enter on it loads the next page rather than opening a mod page.
	/// Its <see cref="ToString"/> is what the screen reader reads when the row is focused.
	/// </summary>
	private sealed class DiscoveryLoadMoreRow
	{
		public override string ToString() => Loc.T("discovery.loadMoreRow");
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
	/// Queries the Nexus Mods GraphQL API for the current search text and populates
	/// <c>listDiscovery</c>. Pass <paramref name="loadMore"/> as <c>true</c> to append the
	/// next page of results instead of starting fresh.
	/// </summary>
	private async Task RunDiscovery(bool loadMore = false)
	{
		if (string.IsNullOrEmpty(_settings.ApiKey))
		{
			Speak(Loc.T("discovery.loginFirst"));
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
			cmbDiscoveryCategory?.Focus();
			return;
		}

		// Record real text searches (not "load more" pages or the browse modes) to the active game's history.
		if (!loadMore && searchType == "Search" && searchTerm.Length > 0 && _settings.SaveSearchHistory)
			SearchHistoryStore.Add(_settings.ActiveGame, searchTerm);
		Speak(loadMore ? Loc.T("discovery.loadingMore", searchType) : Loc.T("discovery.startingSearch", searchType));
		SetStatus(loadMore ? Loc.T("discovery.loadingMore", searchType) : Loc.T("discovery.statusRunning", searchType));
		try
		{
			int pageSize = _currentDiscoveryPageSize;
			var (results, total) = await _nexusService.SearchModsAsync(
				searchType, searchTerm, _currentDiscoveryPage, pageSize, language, category);
			int offset = (_currentDiscoveryPage - 1) * pageSize;

			// Drop the old inline "Load more" row (always last) before appending; firstNewIndex is then
			// the slot of the first freshly loaded result, which we select so focus lands on it.
			RemoveDiscoveryLoadMoreRow();
			int firstNewIndex = listDiscovery.Items.Count;
			MarkAlreadyInstalled(results);
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
			if (!loadMore && !string.IsNullOrEmpty(language))
			{
				int unfiltered = await _nexusService.GetUnfilteredModCountAsync();
				if (unfiltered > total)
					hiddenByLanguage = " " + Loc.T("discovery.languageHiding", total, unfiltered, language);
			}

			if (results.Count > 0)
			{
				Speak((loadMore ? Loc.T("discovery.added", results.Count) : Loc.T("discovery.found", results.Count))
					+ hiddenByLanguage);
				// Fresh search lands on the first result; Load more lands on the first new result.
				listDiscovery.SelectedIndex = loadMore ? firstNewIndex : 0;
			}
			else
			{
				Speak((loadMore ? Loc.T("discovery.noMore") : Loc.T("discovery.none")) + hiddenByLanguage);
			}
		}
		catch (Exception ex)
		{
			SpeakBox(Loc.T("discovery.errorBox", FriendlyError(ex)));
		}
		finally
		{
			// "Running search…" / "Loading more…" are transient; return the title to the resting status
			// (Nexus connection or "Ready") so it never sits on a stale "Loading more…" forever.
			ResetStatus();
		}
	}

	/// <summary>
	/// Fills the Discovery "Language" dropdown with the languages that actually have mods for the active game
	/// (most common first, with counts), and selects the user's saved language preference. Falls back to the
	/// starter list if the facet query returns nothing.
	/// </summary>
	private async Task PopulateDiscoveryLanguagesAsync()
	{
		if (cmbDiscoveryLanguage == null) return;
		var langs = await _nexusService.GetModLanguagesAsync();
		if (langs.Count == 0) return; // network failure etc. — keep whatever is already there

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
	private void MarkAlreadyInstalled(List<StardewMod> results)
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

		foreach (StardewMod result in results)
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

		List<StardewMod> results = listDiscovery.Items.OfType<StardewMod>().ToList();
		if (results.Count == 0) return;

		bool[] before = results.Select(r => r.IsInstalled).ToArray();
		MarkAlreadyInstalled(results);

		// Nothing became installed since these results were fetched, so leave the list completely alone. A
		// rebuild that changes nothing is still a rebuild, and this runs after every rescan.
		if (!results.Where((r, i) => r.IsInstalled != before[i]).Any()) return;

		int at = listDiscovery.SelectedIndex;
		bool selectedRowChanged =
			at >= 0 && at < results.Count && results[at].IsInstalled != before[at];
		bool hadLoadMore = DiscoveryHasLoadMoreRow();

		_movingListSilently = true;
		try
		{
			listDiscovery.BeginUpdate();
			listDiscovery.Items.Clear();
			foreach (StardewMod result in results) listDiscovery.Items.Add(result);
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
		if (selectedRowChanged && listDiscovery.Focused && listDiscovery.SelectedItem is StardewMod current)
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

		// Fetched once per game rather than on every data refresh. A game's categories do not change while the
		// manager is open, and RefreshAllData runs often enough that asking each time would be a request spent
		// on an answer we already have.
		string game = _settings.ActiveGame;
		if (string.Equals(_discoveryCategoriesGame, game, StringComparison.Ordinal)) return;

		var categories = await _nexusService.GetModCategoriesAsync();
		if (cmbDiscoveryCategory == null || categories.Count == 0) return;
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

	/// <summary>Maps a Nexus Mods numeric category ID to a human-readable category name.</summary>
	private string MapNexusCategory(int id)
	{
		return id switch
		{
			1 => "Expansion", 
			2 => "NPC", 
			3 => "Portrait", 
			4 => "Map", 
			5 => "Crafting", 
			6 => "Gameplay", 
			7 => "Visual", 
			8 => "Audio", 
			_ => "General", 
		};
	}

	/// <summary>
	/// Sends <paramref name="text"/> to the active screen reader via Tolk.
	/// Auto-reloads Tolk if it has been externally unloaded since the last call.
	/// </summary>
	private void Speak(string text) => Speak(text, interrupt: false);

	/// <summary>
	/// Sends <paramref name="text"/> to the active screen reader via Tolk. When <paramref name="interrupt"/>
	/// is true, it cuts off any in-progress/queued speech first — used to take full control of the wording
	/// and ordering of an announcement (e.g. so a list's title is spoken before its first item).
	/// </summary>
	private void Speak(string text, bool interrupt)
	{
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
		if (!Tolk.IsLoaded())
		{
			try { Tolk.Load(); Tolk.TrySAPI(trySAPI: true); } catch { }
		}
		if (Tolk.IsLoaded())
		{
			Tolk.Output(text, interrupt);
		}
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
	/// Tolk only reports <see cref="Tolk.IsSpeaking"/> reliably for its own SAPI voice — most screen readers
	/// return false even while talking — so a fixed minimum sized to the sentence is waited out regardless, then
	/// it stops early once speech is known to be done, or at the hard cap.
	/// </summary>
	private static async Task WaitForSpeechAsync(int minMs = 2500, int maxMs = 12000)
	{
		const int step = 100;
		int elapsed = 0;
		while (elapsed < maxMs)
		{
			await Task.Delay(step);
			elapsed += step;
			bool speaking;
			try { speaking = Tolk.IsSpeaking(); } catch { speaking = false; }
			if (!speaking && elapsed >= minMs) break;
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
	/// Plays a periodic loading sound while update checks are in flight (<c>_isLoading</c> is true).
	/// Exits and plays the completion sound once all pending checks finish.
	/// </summary>
	private async Task RunLoadingLoop()
	{
		while (_isLoading)
		{
			_soundEngine.Play("loading_indicator");
			await Task.Delay(1500);
		}
	}
}
