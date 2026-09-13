// Rows behind the curator: a suggested mod, a category, and a suggestion as the list reads it.
//
// Lifted out of Form1 by Phase 2 of the core split. These are plain data - what a row holds and how
// it reads aloud - so they belong beside the rules rather than inside the window that happens to
// show them today. While they were private to Form1 no second front end could name them at all.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// One row of the marked-mods list: the entry itself, read out as a sentence.
///
/// The category's name is handed in rather than looked up here, because a row has no way to reach the stored
/// categories and re-reading the file once per row would be silly for a list that is rebuilt on every edit.
/// </summary>
public sealed class CuratorRow
{
	public required SuggestedMod Entry { get; init; }

	/// <summary>The category's name at the moment the list was built.</summary>
	public required string Category { get; init; }

	public override string ToString()
	{
		string game = GameProfiles.DisplayNameFor(Entry.Game);
		if (string.IsNullOrEmpty(game)) game = Entry.Game;

		return Entry.Reason.Length > 0
			? Loc.T("curator.reviewRow", game, Entry.Name, Category, Entry.Reason)
			: Loc.T("curator.reviewRowNoReason", game, Entry.Name, Category);
	}
}

/// <summary>One row of the categories list: its name, whether it ships, and how many mods are filed under it.</summary>
public sealed class CategoryRow
{
	public required SuggestionCategory Category { get; init; }
	public required string Label { get; init; }
	public required int Used { get; init; }

	public override string ToString() => Category.IsBuiltIn
		? Loc.T("curator.categoryRowBuiltIn", Label, Used)
		: Loc.T("curator.categoryRow", Label, Used);
}

/// <summary>One row of the suggested list: either a category heading, or a mod under it.</summary>
public sealed class SuggestionRow
{
	/// <summary>The mod this row is about, or <c>null</c> when the row is a heading.</summary>
	public SuggestedMod? Entry { get; init; }

	/// <summary>What the screen reader reads for this row.</summary>
	public required string Text { get; init; }

	public bool IsHeading => Entry == null;

	public override string ToString() => Text;
}
