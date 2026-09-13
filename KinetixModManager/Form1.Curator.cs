using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using StardewMod = KinetixModManager.GameMod;

namespace KinetixModManager;

/// <summary>
/// The curator tools: marking a mod as one worth suggesting to other players, and reviewing what has been
/// marked so far. Switched on by <see cref="AppSettings.CuratorMode"/>, which has no UI.
///
/// <para>
/// This is how the shipped Suggested Mods list gets written. It is not a feature of the manager — the finished
/// list will be, but the list has to exist before a screen can be designed around it, and guessing what belongs
/// in it from memory is exactly what this avoids. Marking happens while browsing, in the moment a mod proves
/// itself, and it records the one thing that is never remembered afterwards: why.
/// </para>
///
/// <para>
/// The keys are deliberately absent from <see cref="AppSettings.Shortcuts"/>. Everything in that dictionary is
/// listed by the shortcut manager and printed into the manual's key mappings, and a tester who found these
/// would be filing suggestions into a file on their own machine that nobody will ever read.
/// </para>
/// </summary>
public partial class Form1
{
	/// <summary>
	/// Where the marked list is kept: the manager's own AppData folder.
	///
	/// Deliberately NOT the <c>data</c> folder beside the executable, which is where the finished list will
	/// eventually ship from. An installed copy has that folder under Program Files and cannot write to it, and
	/// the whole point of this tool is to be used while ear-testing a real build. Moving the collected file into
	/// the repo's <c>data</c> folder is a deliberate step taken once, when the list is ready to ship.
	/// </summary>
	private static string CuratorFilePath =>
		System.IO.Path.Combine(AppSettings.AppDataFolder, "suggested-mods.json");

	/// <summary>Where the curator's own categories are kept, beside the suggestions they file.</summary>
	private static string CategoryFilePath =>
		System.IO.Path.Combine(AppSettings.AppDataFolder, "suggestion-categories.json");

	/// <summary>
	/// How a category reads out loud: the name it was given, or its translated one if it still has the name it
	/// shipped with, or its id if the language file has lost the key.
	///
	/// A user's own name wins over a translation because it is not a translation of anything — they typed it, in
	/// whatever language they think in, and no catalogue has an opinion about it.
	/// </summary>
	private static string CategoryLabel(SuggestionCategory? category)
	{
		if (category == null) return Loc.T("curator.categoryUnknown");
		if (!string.IsNullOrWhiteSpace(category.Label)) return category.Label;
		if (!string.IsNullOrWhiteSpace(category.LocKey)) return Loc.T(category.LocKey);
		return category.Id;
	}

	/// <summary>How the category an entry is filed under reads out loud, given the categories in force.</summary>
	private static string CategoryLabel(List<SuggestionCategory> categories, string categoryId) =>
		CategoryLabel(SuggestionCategoryStore.Find(categories, categoryId));

	/// <summary>
	/// The mod the curator keys act on: whichever list has focus is holding it.
	///
	/// Both the installed list and the Discovery results hold <see cref="GameMod"/> objects, so one path covers
	/// them. Discovery matters as much as the installed list — without it a mod could only be suggested after
	/// being installed, which would mean installing something on all five games just to recommend it.
	/// </summary>
	private StardewMod? CuratorTargetMod()
	{
		if (listInstalled != null && listInstalled.Focused && listInstalled.SelectedItem is StardewMod installed)
			return installed.IsGroup ? null : installed;

		// A Discovery row may be the inline "Load more" sentinel rather than a mod; the type test rules it out.
		if (listDiscovery != null && listDiscovery.Focused && listDiscovery.SelectedItem is StardewMod found)
			return found;

		return null;
	}

	/// <summary>
	/// Brings the Mods menu into line with whether curation is on: the switch shows a tick, and the three
	/// commands it governs are shown or hidden.
	///
	/// <para>
	/// Hidden, not greyed. A disabled menu item is still an item: the screen reader counts it, so a submenu of
	/// five where three could not be used announced "1 of 5" and then refused to go to three of them. Removing
	/// them makes the count the truth — "1 of 2" — which is the only number a listener can act on. The switch
	/// says what turning it on would bring, so nothing is lost by their not being there.
	/// </para>
	///
	/// <para>
	/// Called both when the switch is flipped and when the menu is opened. The second is what covers the keyboard
	/// route — the state can change without the menu having been near it.
	/// </para>
	/// </summary>
	private void UpdateCuratorMenuState()
	{
		if (MainMenuStrip?.Items["menuMods"] is not ToolStripMenuItem modsMenu) return;

		if (FindMenuItem(modsMenu, "menuCurationMode") is ToolStripMenuItem toggle)
		{
			toggle.Checked = _settings.CuratorMode;
			toggle.Text = _settings.CuratorMode
				? Loc.T("menu.curationMode", GetShortcutString("CurationMode"))
				: Loc.T("menu.curationModeOff", GetShortcutString("CurationMode"));
		}

		foreach (string name in new[] { "menuMarkSuggestion", "menuSuggestedList", "menuManageCategories" })
			if (FindMenuItem(modsMenu, name) is ToolStripItem item)
				item.Visible = _settings.CuratorMode;
	}

	/// <summary>
	/// Switches curation on or off and says which it now is.
	///
	/// This is the one curation command that works while curation is off, for the obvious reason. It is also why
	/// the feature needs no hidden setting: it is off until somebody asks for it, and asking takes one key.
	/// </summary>
	private void ToggleCurationMode()
	{
		_settings.CuratorMode = !_settings.CuratorMode;
		_settings.Save();
		UpdateCuratorMenuState();

		if (_settings.CuratorMode)
			Speak(Loc.T("curator.modeOn", GetShortcutString("MarkSuggestion"), GetShortcutString("SuggestedList")));
		else
			Speak(Loc.T("curator.modeOff"));
	}

	/// <summary>
	/// Marks the mod under the cursor as one worth suggesting, or takes it off the list if it is already on it.
	///
	/// One key for membership, both ways — the natural reading of pressing it twice. Changing a mod's category or
	/// reason is not here but in the marked-mods list, so this key means exactly one thing; if it meant "mark, or
	/// edit if already marked" there would be no way to remove anything from the mod list at all.
	/// </summary>
	private void ToggleModSuggestion()
	{
		if (!_settings.CuratorMode) return;

		StardewMod? mod = CuratorTargetMod();
		if (mod == null)
		{
			Speak(Loc.T("curator.nothingToMark"));
			return;
		}

		string game = GameProfiles.BaseId(_settings.ActiveGame);
		string gameName = GameProfiles.DisplayNameFor(game);

		var entry = new SuggestedMod
		{
			Game        = game,
			Name        = mod.Name,
			Author      = mod.Author,
			NexusId     = mod.NexusID,
			GitHubRepo  = mod.GitHubRepo,
			// A search result's UniqueId is the Nexus mod id, or a fresh GUID when the API gave none — neither is
			// the manifest id an installed mod carries, and a GUID would make the same mod a new entry on every
			// search. Only an installed mod contributes one.
			UniqueId    = mod.IsSearchResult ? "" : mod.UniqueId
		};

		SuggestedMod? existing = SuggestedModStore.Find(CuratorFilePath, game, entry);

		// Already on the list: this press takes it off, but not silently. The entry holds a sentence typed by
		// hand, there is no undo anywhere else in the manager, and F7 sits next to the F6 that cycles focus — so
		// a stray press must not be able to destroy the one part of an entry that cannot be recovered.
		if (existing != null)
		{
			if (SpeakBox(Loc.T("curator.unmarkConfirm", existing.Name),
				Loc.T("curator.unmarkTitle"), MessageBoxButtons.YesNo) != DialogResult.Yes)
			{
				Speak(Loc.T("curator.cancelled"));
				return;
			}

			try { SuggestedModStore.Remove(CuratorFilePath, existing); }
			catch (Exception ex) { Speak(Loc.T("curator.saveFailed", ex.Message)); return; }

			Speak(Loc.T("curator.unmarked", existing.Name, gameName));
			return;
		}

		if (!EditSuggestion(entry, null)) return;

		try
		{
			SuggestedModStore.Upsert(CuratorFilePath, entry);
		}
		catch (Exception ex)
		{
			Speak(Loc.T("curator.saveFailed", ex.Message));
			return;
		}

		Speak(Loc.T("curator.marked", entry.Name, gameName));
		if (entry.Reason.Length == 0) Speak(Loc.T("curator.noReasonRecorded"));
	}

	/// <summary>
	/// Asks for the category and the reason, filling <paramref name="entry"/> in. Returns <c>false</c> when the
	/// user backed out of either step, in which case nothing has been changed and the caller says so.
	///
	/// <paramref name="existing"/> is what was recorded for this mod before, or <c>null</c> for a new one; it is
	/// what both steps open on, so revisiting a mod is a correction rather than a re-typing.
	/// </summary>
	private bool EditSuggestion(SuggestedMod entry, SuggestedMod? existing)
	{
		string? categoryId = ChooseCategory(
			Loc.T("curator.categoryTitle", entry.Name),
			existing?.Category ?? SuggestionCategoryStore.BuiltInIds[0],
			Loc.T("curator.categoryHint"),
			allowNew: true);

		if (categoryId == null)
		{
			Speak(Loc.T("curator.cancelled"));
			return false;
		}

		string? reason = ShowTextPrompt(
			Loc.T("curator.reasonTitle", entry.Name),
			Loc.T("curator.reasonPrompt"),
			existing?.Reason ?? "");

		if (reason == null)
		{
			Speak(Loc.T("curator.cancelled"));
			return false;
		}

		entry.Category = categoryId;
		entry.Reason = reason.Trim();
		return true;
	}

	/// <summary>
	/// Offers the categories and returns the chosen id, or <c>null</c> if the user backed out.
	///
	/// <paramref name="allowNew"/> adds a "New category" row at the end. That row exists because the moment you
	/// discover a category is missing is the moment you are filing a mod that does not fit one — sending the user
	/// away to a management screen and making them start the mod again would mean the category never gets made.
	/// It is left off when the question is "which of the existing ones", as when re-filing a category's mods
	/// before deleting it.
	/// </summary>
	private string? ChooseCategory(string title, string currentId, string hint, bool allowNew,
		IEnumerable<string>? excludeIds = null)
	{
		// Loops so that backing out of "New category", or giving it a name already in use, returns to the list
		// rather than abandoning whatever the category was being chosen for. Losing a half-marked mod because a
		// name was taken would be a strange thing to do to someone.
		while (true)
		{
			List<SuggestionCategory> categories = SuggestionCategoryStore.Load(CategoryFilePath);

			if (excludeIds != null)
			{
				var skip = new HashSet<string>(excludeIds, StringComparer.OrdinalIgnoreCase);
				categories = categories.Where(c => !skip.Contains(c.Id)).ToList();
			}
			if (categories.Count == 0) return null;

			List<string> labels = categories.Select(c => CategoryLabel(c)).ToList();
			if (allowNew) labels.Add(Loc.T("curator.categoryNewRow"));

			string? chosen = ShowChoiceList(
				title,
				Loc.T("curator.categoryListName"),
				labels,
				CategoryLabel(SuggestionCategoryStore.Find(categories, currentId)),
				hint);

			if (chosen == null) return null;

			// Matched by position, not by text: two categories could be given the same name, and a label is a
			// sentence a translation is free to reword.
			int at = labels.FindIndex(l => string.Equals(l, chosen, StringComparison.Ordinal));
			if (at < 0) return null;
			if (!allowNew || at < categories.Count) return categories[at].Id;

			string? created = CreateCategory();
			if (created != null) return created;

			// CreateCategory has already said why nothing was added; round again so the list is still there.
			currentId = categories[0].Id;
		}
	}

	/// <summary>
	/// Asks for a new category's name, adds it at the end of the list and returns its id — or <c>null</c> if the
	/// user backed out or gave a name that is already taken.
	/// </summary>
	private string? CreateCategory()
	{
		string? label = ShowTextPrompt(
			Loc.T("curator.categoryNewTitle"),
			Loc.T("curator.categoryNewPrompt"),
			"");

		if (label == null) return null;

		label = label.Trim();
		if (label.Length == 0)
		{
			Speak(Loc.T("curator.categoryNameEmpty"));
			return null;
		}

		List<SuggestionCategory> categories = SuggestionCategoryStore.Load(CategoryFilePath);
		if (categories.Any(c => string.Equals(CategoryLabel(c), label, StringComparison.CurrentCultureIgnoreCase)))
		{
			Speak(Loc.T("curator.categoryNameTaken", label));
			return null;
		}

		var created = new SuggestionCategory
		{
			Id = SuggestionCategoryStore.NewId(categories, label),
			Label = label,
			IsBuiltIn = false
		};
		categories.Add(created);

		try { SuggestionCategoryStore.Save(CategoryFilePath, categories); }
		catch (Exception ex) { Speak(Loc.T("curator.saveFailed", ex.Message)); return null; }

		Speak(Loc.T("curator.categoryCreated", label));
		return created.Id;
	}

	/// <summary>
	/// The categories, in the order they are offered, so they can be renamed, reordered, added to and removed.
	///
	/// Enter renames, Delete removes, and the left and right arrows move a category up and down the order — that
	/// order is what the picker and the finished list both follow, so it is worth being able to put the ones you
	/// reach for at the top.
	/// </summary>
	private void ShowCategoryManager()
	{
		if (!_settings.CuratorMode) return;

		List<SuggestionCategory> categories = SuggestionCategoryStore.Load(CategoryFilePath);

		ShowInlineView(Loc.T("curator.categoriesTitle"), (container, closeView) =>
		{
			var list = new ListBox
			{
				Dock = DockStyle.Fill,
				Font = new Font("Segoe UI", 11f),
				IntegralHeight = false,
				HorizontalScrollbar = true,
				AccessibleName = Loc.T("curator.categoriesListName"),
				AccessibleDescription = Loc.T("curator.categoriesListDesc")
			};

			List<SuggestedMod> entries = SuggestedModStore.Load(CuratorFilePath);

			int UsageOf(string id) =>
				entries.Count(e => string.Equals(e.Category, id, StringComparison.OrdinalIgnoreCase));

			void Fill(int select)
			{
				_movingListSilently = true;
				try
				{
					list.BeginUpdate();
					list.Items.Clear();
					foreach (SuggestionCategory category in categories)
						list.Items.Add(new CategoryRow
						{
							Category = category,
							Label = CategoryLabel(category),
							Used = UsageOf(category.Id)
						});
					list.EndUpdate();

					if (list.Items.Count > 0)
						list.SelectedIndex = Math.Clamp(select, 0, list.Items.Count - 1);
				}
				finally { _movingListSilently = false; }
			}

			bool Persist()
			{
				try { SuggestionCategoryStore.Save(CategoryFilePath, categories); return true; }
				catch (Exception ex) { Speak(Loc.T("curator.saveFailed", ex.Message)); return false; }
			}

			Fill(0);
			list.GotFocus += List_Enter;
			list.SelectedIndexChanged += List_SelectedIndexChanged;

			list.KeyDown += (s, e) =>
			{
				// Adding comes before the row test: it is the one thing here that does not act on a category, and
				// it has to work on an empty list — which is also why it cannot live only on the marking picker.
				//
				// Ctrl+N, not Insert. Insert is the screen reader's own modifier key, so a bare Insert never
				// reaches the app at all — the same lesson F12 taught.
				if (e.KeyCode == Keys.N && e.Control)
				{
					e.Handled = true;
					e.SuppressKeyPress = true;

					string? created = CreateCategory();
					if (created == null) return;

					categories = SuggestionCategoryStore.Load(CategoryFilePath);
					int added = categories.FindIndex(c =>
						string.Equals(c.Id, created, StringComparison.OrdinalIgnoreCase));

					Fill(added < 0 ? categories.Count - 1 : added);
					return;
				}

				if (list.SelectedItem is not CategoryRow row) return;
				int at = list.SelectedIndex;

				// Ctrl with the same arrows that move through the list: moving an item and moving to an item are
				// the same gesture with a modifier, which is how reordering reads everywhere else. Left and right
				// were tried first and were worse — nothing about sideways says "up the order".
				if ((e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) && e.Control)
				{
					e.Handled = true;
					e.SuppressKeyPress = true;

					int to = e.KeyCode == Keys.Up ? at - 1 : at + 1;
					if (to < 0 || to >= categories.Count)
					{
						Speak(Loc.T("curator.categoryAtEnd", row.Label));
						return;
					}

					(categories[at], categories[to]) = (categories[to], categories[at]);
					if (!Persist()) return;

					Fill(to);
					Speak(Loc.T("curator.categoryMoved", row.Label, to + 1, categories.Count));
					return;
				}

				if (e.KeyCode == Keys.Enter)
				{
					e.Handled = true;
					e.SuppressKeyPress = true;

					// A built-in opens on its translated name, so renaming starts from what is on screen rather
					// than from an empty box; clearing the box entirely puts the shipped name back.
					string? renamed = ShowTextPrompt(
						Loc.T("curator.categoryRenameTitle", row.Label),
						row.Category.IsBuiltIn
							? Loc.T("curator.categoryRenameBuiltInPrompt")
							: Loc.T("curator.categoryRenamePrompt"),
						row.Label);

					if (renamed == null) { Speak(Loc.T("curator.cancelled")); return; }

					renamed = renamed.Trim();
					if (renamed.Length == 0 && !row.Category.IsBuiltIn)
					{
						Speak(Loc.T("curator.categoryNameEmpty"));
						return;
					}

					// Emptied on a built-in means "give me the shipped name back", which is only reachable
					// because the translated name was never overwritten — Label sits in front of LocKey.
					row.Category.Label = row.Category.IsBuiltIn && renamed.Length == 0 ? "" : renamed;
					if (!Persist()) return;

					Fill(at);
					Speak(Loc.T("curator.categoryRenamed", CategoryLabel(row.Category)));
					return;
				}

				if (e.KeyCode == Keys.Delete)
				{
					e.Handled = true;
					e.SuppressKeyPress = true;

					if (row.Category.IsBuiltIn)
					{
						Speak(Loc.T("curator.categoryBuiltInKept", row.Label));
						return;
					}
					if (categories.Count <= 1)
					{
						Speak(Loc.T("curator.categoryLastOne"));
						return;
					}

					// Mods filed under it are re-filed rather than orphaned, and the user says where they go —
					// choosing for them would quietly change what three entries mean.
					string? moveTo = null;
					if (row.Used > 0)
					{
						moveTo = ChooseCategory(
							Loc.T("curator.categoryReassignTitle", row.Label),
							SuggestionCategoryStore.BuiltInIds[0],
							Loc.T("curator.categoryReassignHint", row.Used, row.Label),
							allowNew: false,
							excludeIds: new[] { row.Category.Id });

						if (moveTo == null) { Speak(Loc.T("curator.cancelled")); return; }
					}
					else if (SpeakBox(Loc.T("curator.categoryDeleteConfirm", row.Label),
						Loc.T("curator.categoriesTitle"), MessageBoxButtons.YesNo) != DialogResult.Yes)
					{
						Speak(Loc.T("curator.cancelled"));
						return;
					}

					int moved = 0;
					try
					{
						if (moveTo != null) moved = SuggestedModStore.Reassign(CuratorFilePath, row.Category.Id, moveTo);
					}
					catch (Exception ex) { Speak(Loc.T("curator.saveFailed", ex.Message)); return; }

					categories.RemoveAt(at);
					if (!Persist()) return;

					entries = SuggestedModStore.Load(CuratorFilePath);
					Fill(at);

					string landedOn = list.SelectedItem?.ToString() ?? "";
					if (moved > 0)
						Speak(Loc.T("curator.categoryDeletedMoved", row.Label, moved,
							CategoryLabel(SuggestionCategoryStore.Find(categories, moveTo!)), landedOn));
					else
						Speak(Loc.T("curator.categoryDeleted", row.Label, landedOn));
				}
			};
			// Escape is handled by the view itself (see Form1.InlineView).

			container.Controls.Add(list);
			return list;
		},
		hint: Loc.T("curator.categoriesHint"));
	}

	/// <summary>
	/// Shows everything marked so far, across every game, so it can be read back and corrected.
	///
	/// Without this the only way to check the file is to open the JSON in a text editor, which is awkward to do
	/// by ear and easy to break. Reasons in particular want revising — the first wording of one is written in
	/// the moment, and the good version usually arrives later.
	/// </summary>
	private void ShowSuggestedModsReview()
	{
		if (!_settings.CuratorMode) return;

		List<SuggestedMod> entries = SuggestedModStore.Load(CuratorFilePath);
		if (entries.Count == 0)
		{
			Speak(Loc.T("curator.reviewEmpty"));
			return;
		}

		// Said once the view has actually gone, not when it was asked to go. Speaking from inside the view lands
		// while the overlay is still handing focus back, and the reader's announcement of wherever focus arrives
		// talks straight over it — the same call-order trap that made the mod list announce the wrong mod.
		string? sayAfterClosing = null;

		ShowInlineView(Loc.T("curator.reviewTitle"), (container, closeView) =>
		{
			var list = new ListBox
			{
				Dock = DockStyle.Fill,
				Font = new Font("Segoe UI", 11f),
				IntegralHeight = false,
				HorizontalScrollbar = true,
				AccessibleName = Loc.T("curator.reviewListName"),
				AccessibleDescription = Loc.T("curator.reviewListDesc")
			};

			// Rebuilding moves the selection, and a move the program makes is not news in itself — the reader says
			// nothing about it, and List_SelectedIndexChanged would add a bare "3 of 7" with no idea what row that
			// is. Held silent here so each edit and each removal is one sentence, said deliberately below.
			void Fill(int select)
			{
				// Re-read on each rebuild: a category can be created from inside the edit that triggered it, and
				// the row about to be rewritten is the one that would name it.
				List<SuggestionCategory> categories = SuggestionCategoryStore.Load(CategoryFilePath);

				_movingListSilently = true;
				try
				{
					list.BeginUpdate();
					list.Items.Clear();
					foreach (SuggestedMod entry in entries)
						list.Items.Add(new CuratorRow
						{
							Entry = entry,
							Category = CategoryLabel(categories, entry.Category)
						});
					list.EndUpdate();

					if (list.Items.Count > 0)
						list.SelectedIndex = Math.Clamp(select, 0, list.Items.Count - 1);
				}
				finally { _movingListSilently = false; }
			}

			Fill(0);

			// Announce the position on arrival and on each arrow move, as every other list in the manager does.
			list.GotFocus += List_Enter;
			list.SelectedIndexChanged += List_SelectedIndexChanged;

			list.KeyDown += (s, e) =>
			{
				if (list.SelectedItem is not CuratorRow row) return;

				if (e.KeyCode == Keys.Enter)
				{
					e.Handled = true;
					e.SuppressKeyPress = true;

					var edited = row.Entry;
					if (!EditSuggestion(edited, edited)) return;

					try { SuggestedModStore.Upsert(CuratorFilePath, edited); }
					catch (Exception ex) { Speak(Loc.T("curator.saveFailed", ex.Message)); return; }

					int keep = list.SelectedIndex;
					Fill(keep);
					Speak(Loc.T("curator.updated", edited.Name, GameProfiles.DisplayNameFor(edited.Game)));
				}
				else if (e.KeyCode == Keys.Delete)
				{
					e.Handled = true;
					e.SuppressKeyPress = true;

					if (SpeakBox(Loc.T("curator.removeConfirm", row.Entry.Name),
						Loc.T("curator.reviewTitle"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;

					try { SuggestedModStore.Remove(CuratorFilePath, row.Entry); }
					catch (Exception ex) { Speak(Loc.T("curator.saveFailed", ex.Message)); return; }

					int at = list.SelectedIndex;
					entries.Remove(row.Entry);

					if (entries.Count == 0)
					{
						// Nothing left to look at, so the view goes rather than leaving an empty list to arrow
						// around in.
						sayAfterClosing = Loc.T("curator.reviewNowEmpty", row.Entry.Name);
						closeView();
						return;
					}

					// Land on the neighbour and say where that is, in one utterance — the same shape as ignoring
					// an update from the updates list, which is the other place a row is removed under the cursor.
					Fill(at);
					string landedOn = list.SelectedItem?.ToString() ?? "";
					Speak(Loc.T("curator.removedPos", row.Entry.Name, landedOn, list.SelectedIndex + 1, list.Items.Count));
				}
			};
			// Escape is handled by the view itself (see Form1.InlineView).

			container.Controls.Add(list);
			return list;
		},
		onClosed: () => { if (sayAfterClosing != null) Speak(sayAfterClosing); },
		hint: Loc.T("curator.reviewHint", entries.Count));
	}
}
