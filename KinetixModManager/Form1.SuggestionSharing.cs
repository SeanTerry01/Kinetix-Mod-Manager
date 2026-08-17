using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// Sharing suggestion lists: writing one out for somebody else, and taking one in.
///
/// <para>
/// Only the user's OWN suggestions are ever exported, never the list that ships with the manager. That is not a
/// detail — the shipped list is replaced wholesale by each release, and the personal file is not, so a shared
/// file containing shipped entries would plant a frozen copy of somebody's baseline into the recipient's
/// personal layer, where it would win over every future update forever. Exporting only what the author actually
/// marked keeps the two layers doing their jobs on both machines.
/// </para>
/// </summary>
public partial class Form1
{
	/// <summary>The file type shared suggestion lists are saved as.</summary>
	private const string SuggestionFileExtension = ".json";

	/// <summary>
	/// Writes the user's own suggestions to a file they can pass on, together with the categories those entries
	/// are filed under.
	/// </summary>
	private async Task ExportSuggestions()
	{
		List<SuggestedMod> mine = SuggestedModStore.Load(CuratorFilePath);
		if (mine.Count == 0)
		{
			Speak(Loc.T("share.exportEmpty"));
			return;
		}

		// Asked only when the answer could differ. Someone who has curated one game has nothing to decide, and a
		// question with one sensible answer is just another screen between them and the file.
		List<SuggestedMod> chosen = mine;
		string scopeName = "";

		var games = mine.Select(e => GameProfiles.BaseId(e.Game)).Distinct(StringComparer.Ordinal).ToList();
		string activeGame = GameProfiles.BaseId(_settings.ActiveGame);

		if (games.Count > 1 && games.Contains(activeGame, StringComparer.Ordinal))
		{
			string thisGame = Loc.T("share.scopeThisGame", GameProfiles.DisplayNameFor(activeGame));
			string everything = Loc.T("share.scopeEverything", games.Count);

			string? pick = ShowChoiceList(
				Loc.T("share.scopeTitle"),
				Loc.T("share.scopeListName"),
				new[] { thisGame, everything },
				thisGame,
				Loc.T("share.scopeHint"));

			if (pick == null) { Speak(Loc.T("share.cancelled")); return; }

			if (string.Equals(pick, thisGame, StringComparison.Ordinal))
			{
				chosen = mine
					.Where(e => string.Equals(GameProfiles.BaseId(e.Game), activeGame, StringComparison.Ordinal))
					.ToList();
				scopeName = GameProfiles.DisplayNameFor(activeGame);
			}
		}
		else if (games.Count == 1)
		{
			scopeName = GameProfiles.DisplayNameFor(games[0]);
		}

		string? name = ShowTextPrompt(
			Loc.T("share.nameTitle"),
			Loc.T("share.namePrompt"),
			scopeName.Length > 0 ? Loc.T("share.nameDefault", scopeName) : "");

		if (name == null) { Speak(Loc.T("share.cancelled")); return; }
		name = name.Trim();
		if (name.Length == 0) { Speak(Loc.T("share.nameEmpty")); return; }

		string? author = ShowTextPrompt(Loc.T("share.authorTitle"), Loc.T("share.authorPrompt"), "");
		if (author == null) { Speak(Loc.T("share.cancelled")); return; }

		var list = new SuggestionList
		{
			Name = name,
			Author = author.Trim(),
			CreatedUtc = DateTime.UtcNow,
			AppVersion = NexusService.AppVersion,
			Categories = SuggestionSharing.CategoriesUsedBy(chosen, SuggestionCategoryStore.Load(CategoryFilePath)),
			Entries = chosen
		};

		using SaveFileDialog dialog = new SaveFileDialog
		{
			Filter = Loc.T("share.fileFilter"),
			FileName = MakeSafeFileName(name) + SuggestionFileExtension
		};
		DialogResult picked = dialog.ShowDialog();

		// Before anything is said or shown: the picker closing sets the reader off re-reading the main window,
		// and that lands on top of whatever comes next — a sentence or a prompt's question alike.
		if (!await SettleAfterForeignWindowAsync()) return;

		if (picked != DialogResult.OK) { SpeakWithBearings(Loc.T("share.cancelled")); return; }

		try
		{
			File.WriteAllText(dialog.FileName, SuggestionSharing.Write(list));
		}
		catch (Exception ex)
		{
			_soundEngine.Play("error");
			SpeakBox(Loc.T("share.exportFailed", FriendlyError(ex)), Loc.T("share.exportTitle"));
			return;
		}

		_soundEngine.Play("load_complete");
		SpeakWithBearings(Loc.T("share.exported", chosen.Count, Path.GetFileName(dialog.FileName)));
	}

	/// <summary>
	/// Takes in somebody else's suggestion list, saying what it would do before it does any of it.
	///
	/// <para>
	/// The one question worth asking is asked once, not per mod: a list of forty that overlaps yours in twelve
	/// places would otherwise be twelve prompts, which is a lot of keypresses to answer the same way twelve
	/// times. What happened is reported afterwards instead, and the marked-mods list is where anything that
	/// landed wrongly gets corrected.
	/// </para>
	/// </summary>
	private async Task ImportSuggestions()
	{
		using OpenFileDialog dialog = new OpenFileDialog
		{
			Filter = Loc.T("share.fileFilter"),
			CheckFileExists = true
		};
		DialogResult picked = dialog.ShowDialog();

		// Before anything is said or shown. Everything from here on either speaks or opens a prompt that speaks,
		// and the reader's re-read of the main window would cut the first of them off — which is exactly what
		// swallowed the "Import it?" question and left only the "Yes" button audible.
		if (!await SettleAfterForeignWindowAsync()) return;

		if (picked != DialogResult.OK) { SpeakWithBearings(Loc.T("share.cancelled")); return; }

		SuggestionList incoming;
		try
		{
			incoming = SuggestionSharing.Parse(File.ReadAllText(dialog.FileName));
		}
		catch (Exception ex)
		{
			_soundEngine.Play("error");
			SpeakBox(Loc.T("share.importUnreadable", Path.GetFileName(dialog.FileName), FriendlyError(ex)),
				Loc.T("share.importTitle"));
			return;
		}

		if (incoming.Entries.Count == 0)
		{
			SpeakBox(Loc.T("share.importEmpty", Path.GetFileName(dialog.FileName)), Loc.T("share.importTitle"));
			return;
		}

		List<SuggestedMod> mine = SuggestedModStore.Load(CuratorFilePath);
		List<SuggestionCategory> myCategories = SuggestionCategoryStore.Load(CategoryFilePath);

		ImportPreview preview = SuggestionSharing.Preview(mine, incoming.Entries, myCategories, incoming.Categories);

		// Whose list this is, said before what it would do — it is the thing that decides whether the rest is
		// worth agreeing to.
		string from = string.IsNullOrWhiteSpace(incoming.Author)
			? Loc.T("share.fromUnknown", DescribeList(incoming, dialog.FileName))
			: Loc.T("share.fromAuthor", DescribeList(incoming, dialog.FileName), incoming.Author);

		if (!preview.ChangesSomething)
		{
			SpeakBox(Loc.T("share.importNothingNew", from, preview.Identical), Loc.T("share.importTitle"));
			return;
		}

		if (SpeakBox(Loc.T("share.importConfirm", from, preview.Added, preview.Conflicting, preview.Identical),
			Loc.T("share.importTitle"), MessageBoxButtons.YesNo) != DialogResult.Yes)
		{
			Speak(Loc.T("share.cancelled"));
			return;
		}

		List<SuggestionCategory> merged = SuggestionSharing.MergeCategories(myCategories, incoming.Categories);

		// Only asked when something actually clashes; with no conflicts the answer could not change anything.
		ImportConflictChoice choice = ImportConflictChoice.KeepMine;
		bool decidedOneByOne = false;
		if (preview.Conflicting > 0)
		{
			string keepMine = Loc.T("share.conflictKeepMine");
			string takeTheirs = Loc.T("share.conflictTakeTheirs");
			string decideEach = Loc.T("share.conflictDecideEach", preview.Conflicting);

			string? pick = ShowChoiceList(
				Loc.T("share.conflictTitle"),
				Loc.T("share.conflictListName"),
				new[] { keepMine, takeTheirs, decideEach },
				keepMine,
				Loc.T("share.conflictHint", preview.Conflicting));

			if (pick == null) { Speak(Loc.T("share.cancelled")); return; }

			if (string.Equals(pick, takeTheirs, StringComparison.Ordinal))
			{
				choice = ImportConflictChoice.TakeTheirs;
			}
			else if (string.Equals(pick, decideEach, StringComparison.Ordinal))
			{
				// The decisions are written straight onto the entries already here, so the merge afterwards is
				// simply "keep mine" — mine having become what was chosen. Nothing else about an existing entry
				// moves: it keeps its own identity and the date it was first marked.
				if (!ResolveConflictsOneByOne(SuggestionSharing.Conflicts(mine, incoming.Entries), merged)) return;
				decidedOneByOne = true;
			}
		}

		List<SuggestedMod> result = SuggestionSharing.Apply(mine, incoming.Entries, merged, choice);

		try
		{
			// Categories first: an entry saved against a category that had not been written yet would have
			// nothing to appear under if the second write failed.
			SuggestionCategoryStore.Save(CategoryFilePath, merged);
			SuggestedModStore.Save(CuratorFilePath, result);
		}
		catch (Exception ex)
		{
			_soundEngine.Play("error");
			SpeakBox(Loc.T("share.importFailed", FriendlyError(ex)), Loc.T("share.importTitle"));
			return;
		}

		_soundEngine.Play("load_complete");

		// Reported for what actually happened. Deciding one by one is neither "replaced" nor "kept" — some of each,
		// field by field — so saying either number would be a wrong account of work the user just did themselves.
		if (decidedOneByOne)
		{
			Speak(Loc.T("share.importedDecided", preview.Added, preview.Conflicting, preview.Identical));
		}
		else
		{
			int replaced = choice == ImportConflictChoice.TakeTheirs ? preview.Conflicting : 0;
			int kept = choice == ImportConflictChoice.KeepMine ? preview.Conflicting : 0;
			Speak(Loc.T("share.imported", preview.Added, replaced, kept, preview.Identical));
		}

		if (preview.NewCategories > 0) Speak(Loc.T("share.importedCategories", preview.NewCategories));
	}

	/// <summary>
	/// Walks the mods the two lists disagree about, showing both versions and taking a decision on each, and
	/// writes the answers onto the entries already here. Returns <c>false</c> if the user backed out, in which
	/// case nothing has been written and the caller abandons the whole import.
	///
	/// <para>
	/// The category and the reason are asked separately because they are separate opinions: somebody may well
	/// prefer their own wording under the other person's heading, or the reverse, and a single "mine or theirs"
	/// cannot express that. Each is offered as a list of two rather than a pair of checkboxes — the choices are
	/// mutually exclusive, which is what a list of two already means, and it costs one arrow and one Enter rather
	/// than tabbing between sections and toggling boxes.
	/// </para>
	///
	/// <para>
	/// A field the two agree about is not asked at all. Two people who disagree only about the wording should not
	/// be made to confirm the category they already share, once per mod.
	/// </para>
	/// </summary>
	private bool ResolveConflictsOneByOne(List<ImportConflict> conflicts, List<SuggestionCategory> categories)
	{
		for (int i = 0; i < conflicts.Count; i++)
		{
			ImportConflict conflict = conflicts[i];
			string mod = conflict.Mine.Name;
			string position = Loc.T("share.pickPosition", i + 1, conflicts.Count);

			if (conflict.CategoryDiffers)
			{
				string mineLabel = Loc.T("share.pickMine", CategoryLabel(categories, conflict.Mine.Category));
				string theirsLabel = Loc.T("share.pickTheirs", CategoryLabel(categories, conflict.Theirs.Category));

				string? pick = ShowChoiceList(
					Loc.T("share.pickCategoryTitle", mod, position),
					Loc.T("share.pickCategoryListName"),
					new[] { mineLabel, theirsLabel },
					mineLabel,
					Loc.T("share.pickCategoryHint", mod));

				if (pick == null) { Speak(Loc.T("share.cancelled")); return false; }
				if (string.Equals(pick, theirsLabel, StringComparison.Ordinal))
					conflict.Mine.Category = conflict.Theirs.Category;
			}

			if (conflict.ReasonDiffers)
			{
				string mineLabel = Loc.T("share.pickMine", ReasonOrNone(conflict.Mine.Reason));
				string theirsLabel = Loc.T("share.pickTheirs", ReasonOrNone(conflict.Theirs.Reason));

				string? pick = ShowChoiceList(
					Loc.T("share.pickReasonTitle", mod, position),
					Loc.T("share.pickReasonListName"),
					new[] { mineLabel, theirsLabel },
					mineLabel,
					Loc.T("share.pickReasonHint", mod));

				if (pick == null) { Speak(Loc.T("share.cancelled")); return false; }
				if (string.Equals(pick, theirsLabel, StringComparison.Ordinal))
					conflict.Mine.Reason = conflict.Theirs.Reason;
			}
		}

		return true;
	}

	/// <summary>A reason as it should be read out, or a stand-in when there isn't one — an empty row would
	/// otherwise be a choice with nothing to hear.</summary>
	private static string ReasonOrNone(string reason) =>
		string.IsNullOrWhiteSpace(reason) ? Loc.T("share.pickNoReason") : reason.Trim();

	/// <summary>What to call an incoming list: the name its author gave it, or failing that its file name.</summary>
	private static string DescribeList(SuggestionList list, string path) =>
		string.IsNullOrWhiteSpace(list.Name) ? Path.GetFileName(path) : list.Name.Trim();
}
