using System;
using System.Collections.Generic;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="SuggestionSharing"/> — writing a suggestions list out for somebody else and taking one in.
///
/// <para>
/// The rules worth pinning down are the ones a user cannot see going wrong. An import that silently overwrites a
/// reason somebody wrote by hand, or that files a mod under a category nobody has, produces a list that looks
/// fine and is quietly wrong — and the entries most likely to be affected are the ones two people both cared
/// enough about to write up.
/// </para>
/// </summary>
public class SuggestionSharingTests
{
	private static SuggestedMod Mod(
		string name,
		string game = "MoonlightPeaks",
		string? nexusId = null,
		string category = SuggestionCategoryStore.CantByEar,
		string reason = "") => new()
	{
		Game = game,
		Name = name,
		NexusId = nexusId ?? name.GetHashCode().ToString(),
		Category = category,
		Reason = reason
	};

	private static SuggestionCategory Custom(string id, string label) =>
		new() { Id = id, Label = label, IsBuiltIn = false };

	// -------------------------------------------------------------------------
	// Reading what turns up
	// -------------------------------------------------------------------------

	[Fact]
	public void AWrittenListReadsBackAsItWasWritten()
	{
		var list = new SuggestionList
		{
			Name = "Sean's Moonlight Peaks picks",
			Author = "Sean",
			CreatedUtc = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc),
			AppVersion = "1.5.0",
			Categories = new List<SuggestionCategory> { Custom("CombatQoL", "Combat quality of life") },
			Entries = new List<SuggestedMod> { Mod("Quick Spells", nexusId: "114", reason: "radial menu") }
		};

		SuggestionList read = SuggestionSharing.Parse(SuggestionSharing.Write(list));

		Assert.Equal("Sean's Moonlight Peaks picks", read.Name);
		Assert.Equal("Sean", read.Author);
		Assert.Equal("1.5.0", read.AppVersion);
		Assert.Equal("radial menu", Assert.Single(read.Entries).Reason);
		Assert.Contains(read.Categories, c => c.Id == "CombatQoL");
	}

	[Fact]
	public void ABareArrayIsStillReadable()
	{
		// The shape every file written before sharing existed has, including the curator's own notebook. Refusing
		// it would mean the manager could not read a file it wrote itself.
		SuggestionList read = SuggestionSharing.Parse("""
			[
			  { "Game": "MoonlightPeaks", "Name": "Save Anywhere", "NexusId": "11", "Category": 3, "Reason": "keeps" }
			]
			""");

		SuggestedMod single = Assert.Single(read.Entries);
		Assert.Equal("Save Anywhere", single.Name);
		Assert.Equal(SuggestionCategoryStore.Easier, single.Category);
		Assert.Equal("", read.Name);
	}

	[Fact]
	public void AListWithNoCategoriesOfItsOwnStillGetsTheBuiltInsBack()
	{
		SuggestionList read = SuggestionSharing.Parse("""{ "Name": "Thin", "Entries": [] }""");

		Assert.Equal(4, read.Categories.Count);
		Assert.Empty(read.Entries);
	}

	// -------------------------------------------------------------------------
	// What travels with a list
	// -------------------------------------------------------------------------

	[Fact]
	public void OnlyTheCategoriesActuallyUsedTravelWithTheEntries()
	{
		// Sending the whole set would push the author's private headings, and any renaming they have done to the
		// built-ins, onto everyone who opens the file.
		var categories = SuggestionCategoryStore.Defaults();
		categories.Add(Custom("CombatQoL", "Combat quality of life"));
		categories.Add(Custom("Cosmetic", "Cosmetic only"));

		var entries = new List<SuggestedMod>
		{
			Mod("A", category: "CombatQoL"),
			Mod("B", category: SuggestionCategoryStore.BugFix)
		};

		var carried = SuggestionSharing.CategoriesUsedBy(entries, categories);

		Assert.Equal(2, carried.Count);
		Assert.Contains(carried, c => c.Id == "CombatQoL");
		Assert.Contains(carried, c => c.Id == SuggestionCategoryStore.BugFix);
		Assert.DoesNotContain(carried, c => c.Id == "Cosmetic");
	}

	// -------------------------------------------------------------------------
	// Working out what an import would do, before it does it
	// -------------------------------------------------------------------------

	[Fact]
	public void ThePreviewSeparatesNewFromClashingFromIdentical()
	{
		var mine = new List<SuggestedMod>
		{
			Mod("Quick Spells", nexusId: "114", reason: "mine"),
			Mod("Save Anywhere", nexusId: "11", reason: "same wording")
		};
		var theirs = new List<SuggestedMod>
		{
			Mod("Quick Spells", nexusId: "114", reason: "theirs"),
			Mod("Save Anywhere", nexusId: "11", reason: "same wording"),
			Mod("Time Control", nexusId: "85", reason: "new to me")
		};

		ImportPreview preview = SuggestionSharing.Preview(
			mine, theirs, SuggestionCategoryStore.Defaults(), SuggestionCategoryStore.Defaults());

		Assert.Equal(1, preview.Added);
		Assert.Equal(1, preview.Conflicting);
		Assert.Equal(1, preview.Identical);
		Assert.Equal(0, preview.NewCategories);
		Assert.True(preview.ChangesSomething);
	}

	[Fact]
	public void AListYouAlreadyHaveEntirelyChangesNothing()
	{
		var mine = new List<SuggestedMod> { Mod("Quick Spells", nexusId: "114", reason: "same") };

		ImportPreview preview = SuggestionSharing.Preview(
			mine, new List<SuggestedMod> { Mod("Quick Spells", nexusId: "114", reason: "same") },
			SuggestionCategoryStore.Defaults(), SuggestionCategoryStore.Defaults());

		Assert.False(preview.ChangesSomething);
		Assert.Equal(1, preview.Identical);
	}

	[Fact]
	public void ADifferentCategoryForTheSameModCounts()
	{
		var mine = new List<SuggestedMod> { Mod("Quick Spells", nexusId: "114", category: SuggestionCategoryStore.Easier, reason: "same") };
		var theirs = new List<SuggestedMod> { Mod("Quick Spells", nexusId: "114", category: SuggestionCategoryStore.CantByEar, reason: "same") };

		Assert.Equal(1, SuggestionSharing.Preview(mine, theirs,
			SuggestionCategoryStore.Defaults(), SuggestionCategoryStore.Defaults()).Conflicting);
	}

	[Fact]
	public void ARenamedModWithTheSameReasonIsNotAConflict()
	{
		// A mod renamed on Nexus is not two people disagreeing, and asking about it would spend the user's one
		// question on something with no answer worth giving.
		var mine = new List<SuggestedMod> { Mod("Quick Spells", nexusId: "114", reason: "radial menu") };
		var theirs = new List<SuggestedMod> { Mod("Moonlight Quick Spells", nexusId: "114", reason: "radial menu") };

		ImportPreview preview = SuggestionSharing.Preview(mine, theirs,
			SuggestionCategoryStore.Defaults(), SuggestionCategoryStore.Defaults());

		Assert.Equal(0, preview.Conflicting);
		Assert.Equal(1, preview.Identical);
	}

	[Fact]
	public void CategoriesTheRecipientLacksAreCounted()
	{
		var theirCategories = SuggestionCategoryStore.Defaults();
		theirCategories.Add(Custom("CombatQoL", "Combat quality of life"));

		ImportPreview preview = SuggestionSharing.Preview(
			new List<SuggestedMod>(), new List<SuggestedMod>(),
			SuggestionCategoryStore.Defaults(), theirCategories);

		Assert.Equal(1, preview.NewCategories);
		Assert.True(preview.ChangesSomething);
	}

	// -------------------------------------------------------------------------
	// Walking the disagreements one at a time
	// -------------------------------------------------------------------------

	[Fact]
	public void OnlyTheModsThatActuallyDifferComeBackAsConflicts()
	{
		var mine = new List<SuggestedMod>
		{
			Mod("Quick Spells", nexusId: "114", reason: "mine"),
			Mod("Save Anywhere", nexusId: "11", reason: "identical")
		};
		var theirs = new List<SuggestedMod>
		{
			Mod("Quick Spells", nexusId: "114", reason: "theirs"),
			Mod("Save Anywhere", nexusId: "11", reason: "identical"),
			Mod("Time Control", nexusId: "85", reason: "not mine at all")
		};

		ImportConflict conflict = Assert.Single(SuggestionSharing.Conflicts(mine, theirs));

		Assert.Equal("114", conflict.Mine.NexusId);
		Assert.Equal("mine", conflict.Mine.Reason);
		Assert.Equal("theirs", conflict.Theirs.Reason);
	}

	[Fact]
	public void AConflictSaysWhichHalvesDisagree()
	{
		// What the two questions are asked from. Asking about a category two people already share, once per mod,
		// is the thing this prevents.
		ImportConflict reasonOnly = Assert.Single(SuggestionSharing.Conflicts(
			new List<SuggestedMod> { Mod("A", nexusId: "1", category: SuggestionCategoryStore.BugFix, reason: "mine") },
			new List<SuggestedMod> { Mod("A", nexusId: "1", category: SuggestionCategoryStore.BugFix, reason: "theirs") }));

		Assert.False(reasonOnly.CategoryDiffers);
		Assert.True(reasonOnly.ReasonDiffers);

		ImportConflict categoryOnly = Assert.Single(SuggestionSharing.Conflicts(
			new List<SuggestedMod> { Mod("A", nexusId: "1", category: SuggestionCategoryStore.BugFix, reason: "same") },
			new List<SuggestedMod> { Mod("A", nexusId: "1", category: SuggestionCategoryStore.Easier, reason: "same") }));

		Assert.True(categoryOnly.CategoryDiffers);
		Assert.False(categoryOnly.ReasonDiffers);

		ImportConflict both = Assert.Single(SuggestionSharing.Conflicts(
			new List<SuggestedMod> { Mod("A", nexusId: "1", category: SuggestionCategoryStore.BugFix, reason: "mine") },
			new List<SuggestedMod> { Mod("A", nexusId: "1", category: SuggestionCategoryStore.Easier, reason: "theirs") }));

		Assert.True(both.CategoryDiffers);
		Assert.True(both.ReasonDiffers);
	}

	[Fact]
	public void ConflictsComeBackInTheIncomingListsOrder()
	{
		var mine = new List<SuggestedMod>
		{
			Mod("A", nexusId: "1", reason: "mine"),
			Mod("B", nexusId: "2", reason: "mine")
		};
		var theirs = new List<SuggestedMod>
		{
			Mod("B", nexusId: "2", reason: "theirs"),
			Mod("A", nexusId: "1", reason: "theirs")
		};

		Assert.Equal(new[] { "2", "1" }, SuggestionSharing.Conflicts(mine, theirs).Select(c => c.Mine.NexusId));
	}

	[Fact]
	public void ChoosingOneHalfOfAConflictLeavesTheOtherAlone()
	{
		// What deciding one by one does: the answers are written onto the entry already here, so the merge after
		// it is an ordinary "keep mine" — mine having become what was chosen.
		var mine = new List<SuggestedMod> { Mod("A", nexusId: "1", category: SuggestionCategoryStore.BugFix, reason: "my wording") };
		var theirs = new List<SuggestedMod> { Mod("A", nexusId: "1", category: SuggestionCategoryStore.Easier, reason: "their wording") };

		ImportConflict conflict = Assert.Single(SuggestionSharing.Conflicts(mine, theirs));

		// Their category, my wording — the combination a single "mine or theirs" cannot express.
		conflict.Mine.Category = conflict.Theirs.Category;

		var result = SuggestionSharing.Apply(mine, theirs, SuggestionCategoryStore.Defaults(),
			ImportConflictChoice.KeepMine);

		SuggestedMod single = Assert.Single(result);
		Assert.Equal(SuggestionCategoryStore.Easier, single.Category);
		Assert.Equal("my wording", single.Reason);
	}

	// -------------------------------------------------------------------------
	// Applying it
	// -------------------------------------------------------------------------

	[Fact]
	public void KeepingMineLeavesMyWordingAloneButStillAddsWhatIsNew()
	{
		var mine = new List<SuggestedMod> { Mod("Quick Spells", nexusId: "114", reason: "mine") };
		var theirs = new List<SuggestedMod>
		{
			Mod("Quick Spells", nexusId: "114", reason: "theirs"),
			Mod("Time Control", nexusId: "85", reason: "new")
		};

		var result = SuggestionSharing.Apply(mine, theirs, SuggestionCategoryStore.Defaults(),
			ImportConflictChoice.KeepMine);

		Assert.Equal(2, result.Count);
		Assert.Equal("mine", result.Single(e => e.NexusId == "114").Reason);
		Assert.Equal("new", result.Single(e => e.NexusId == "85").Reason);
	}

	[Fact]
	public void TakingTheirsReplacesOnlyTheClashingOnes()
	{
		var mine = new List<SuggestedMod>
		{
			Mod("Quick Spells", nexusId: "114", reason: "mine"),
			Mod("Untouched", nexusId: "999", reason: "left alone")
		};
		var theirs = new List<SuggestedMod> { Mod("Quick Spells", nexusId: "114", reason: "theirs") };

		var result = SuggestionSharing.Apply(mine, theirs, SuggestionCategoryStore.Defaults(),
			ImportConflictChoice.TakeTheirs);

		Assert.Equal(2, result.Count);
		Assert.Equal("theirs", result.Single(e => e.NexusId == "114").Reason);
		Assert.Equal("left alone", result.Single(e => e.NexusId == "999").Reason);
	}

	[Fact]
	public void AnEntryFiledUnderACategoryNobodyHasIsRefiledRatherThanLost()
	{
		// The viewer groups by the categories it knows, so an entry pointing at one that exists nowhere would be
		// imported and then be invisible. Wrongly grouped is recoverable in a keypress; absent is not.
		var theirs = new List<SuggestedMod> { Mod("Orphan", nexusId: "1", category: "NoSuchCategory") };

		var result = SuggestionSharing.Apply(new List<SuggestedMod>(), theirs,
			SuggestionCategoryStore.Defaults(), ImportConflictChoice.KeepMine);

		Assert.Equal(SuggestionCategoryStore.BuiltInIds[0], Assert.Single(result).Category);
	}

	[Fact]
	public void AnEntryUsingACategoryThatArrivedWithItKeepsIt()
	{
		var theirs = new List<SuggestedMod> { Mod("Theirs", nexusId: "1", category: "CombatQoL") };
		var after = SuggestionSharing.MergeCategories(
			SuggestionCategoryStore.Defaults(),
			new List<SuggestionCategory> { Custom("CombatQoL", "Combat quality of life") });

		var result = SuggestionSharing.Apply(new List<SuggestedMod>(), theirs, after,
			ImportConflictChoice.KeepMine);

		Assert.Equal("CombatQoL", Assert.Single(result).Category);
	}

	// -------------------------------------------------------------------------
	// Categories arriving with a list
	// -------------------------------------------------------------------------

	[Fact]
	public void ANewCategoryIsAddedAndMyOwnNamesAreLeftAlone()
	{
		var mine = SuggestionCategoryStore.Defaults();
		mine[0].Label = "My own name for it";

		var theirs = SuggestionCategoryStore.Defaults();
		theirs[0].Label = "Their name for it";
		theirs.Add(Custom("CombatQoL", "Combat quality of life"));

		var merged = SuggestionSharing.MergeCategories(mine, theirs);

		// Their name for a category we both have is not something a file we opened gets to impose.
		Assert.Equal("My own name for it", SuggestionCategoryStore.Find(merged, SuggestionCategoryStore.CantByEar)!.Label);
		Assert.Contains(merged, c => c.Id == "CombatQoL");
	}

	[Fact]
	public void MergingCategoriesNeverLosesABuiltIn()
	{
		var merged = SuggestionSharing.MergeCategories(
			new List<SuggestionCategory> { Custom("OnlyMine", "Only mine") },
			new List<SuggestionCategory> { Custom("OnlyTheirs", "Only theirs") });

		foreach (string id in SuggestionCategoryStore.BuiltInIds)
			Assert.Contains(merged, c => c.Id == id);

		Assert.Contains(merged, c => c.Id == "OnlyMine");
		Assert.Contains(merged, c => c.Id == "OnlyTheirs");
	}
}
