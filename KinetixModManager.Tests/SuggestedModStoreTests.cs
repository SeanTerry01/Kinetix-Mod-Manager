using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="SuggestedModStore"/> — the curator's notebook of mods worth suggesting to other players.
///
/// Two of these tests are about not losing a capture. The whole tool exists to be used in passing, while
/// actually playing, so anything that refuses a mark or silently records it twice defeats the point: a
/// duplicate has to be found and merged by hand later, and a refused mark is simply forgotten. The identity
/// rules are therefore the part worth pinning down — an installed mod may have no Nexus id at all, a search
/// result has no UniqueId, and the same game may be open as a Steam copy one day and a GOG copy the next.
/// </summary>
public class SuggestedModStoreTests
{
	private static string TempFile() =>
		Path.Combine(Path.GetTempPath(), "kmm-suggested-" + Guid.NewGuid().ToString("N"), "suggested-mods.json");

	private static SuggestedMod Mod(
		string name,
		string game = "StardewValley",
		string? nexusId = null,
		string? gitHub = null,
		string uniqueId = "",
		string category = SuggestionCategoryStore.CantByEar,
		string reason = "") => new()
	{
		Game = game,
		Name = name,
		NexusId = nexusId,
		GitHubRepo = gitHub,
		UniqueId = uniqueId,
		Category = category,
		Reason = reason
	};

	// -------------------------------------------------------------------------
	// Round trip
	// -------------------------------------------------------------------------

	[Fact]
	public void AMarkedModComesBackWithEverythingItWasMarkedWith()
	{
		string path = TempFile();

		SuggestedModStore.Upsert(path, Mod(
			"Auto-Fishing",
			nexusId: "12345",
			category: SuggestionCategoryStore.CantByEar,
			reason: "the fishing minigame is a moving bar with no audio cue"));

		SuggestedMod stored = Assert.Single(SuggestedModStore.Load(path));
		Assert.Equal("Auto-Fishing", stored.Name);
		Assert.Equal("StardewValley", stored.Game);
		Assert.Equal("12345", stored.NexusId);
		Assert.Equal(SuggestionCategoryStore.CantByEar, stored.Category);
		Assert.Equal("the fishing minigame is a moving bar with no audio cue", stored.Reason);
		Assert.NotEqual(default, stored.MarkedUtc);
	}

	[Fact]
	public void TheListReadsBackOrderedByGameThenName()
	{
		string path = TempFile();

		SuggestedModStore.Upsert(path, Mod("Time Speed", nexusId: "3"));
		SuggestedModStore.Upsert(path, Mod("Audio Lockpicking", game: "SkyrimSE", nexusId: "1"));
		SuggestedModStore.Upsert(path, Mod("Auto-Fishing", nexusId: "2"));

		Assert.Equal(
			new[] { "Audio Lockpicking", "Auto-Fishing", "Time Speed" },
			SuggestedModStore.Load(path).Select(e => e.Name));
	}

	// -------------------------------------------------------------------------
	// Marking the same mod twice is an edit, not a duplicate
	// -------------------------------------------------------------------------

	[Fact]
	public void MarkingTheSameModAgainReplacesItAndSaysSo()
	{
		string path = TempFile();

		Assert.False(SuggestedModStore.Upsert(path, Mod("Auto-Fishing", nexusId: "12345", reason: "first thoughts")));
		Assert.True(SuggestedModStore.Upsert(path, Mod("Auto-Fishing", nexusId: "12345", reason: "better reason")));

		SuggestedMod stored = Assert.Single(SuggestedModStore.Load(path));
		Assert.Equal("better reason", stored.Reason);
	}

	[Fact]
	public void TheSameGameFromAnotherStoreIsStillTheSameGame()
	{
		string path = TempFile();

		// The mod does not care which shop the game came from, and neither should the list. If the install key
		// went in raw, marking a mod while the GOG copy was open would record a second entry that reads as a
		// separate suggestion for a game called "SkyrimSE@Gog".
		SuggestedModStore.Upsert(path, Mod("Audio Lockpicking", game: "SkyrimSE", nexusId: "999"));
		SuggestedModStore.Upsert(path, Mod("Audio Lockpicking", game: "SkyrimSE@Gog", nexusId: "999"));

		SuggestedMod stored = Assert.Single(SuggestedModStore.Load(path));
		Assert.Equal("SkyrimSE", stored.Game);
	}

	[Fact]
	public void TheSameModNameForTwoDifferentGamesIsTwoSuggestions()
	{
		string path = TempFile();

		SuggestedModStore.Upsert(path, Mod("Quick Loot", game: "SkyrimSE", nexusId: "7"));
		SuggestedModStore.Upsert(path, Mod("Quick Loot", game: "Fallout4", nexusId: "7"));

		Assert.Equal(2, SuggestedModStore.Load(path).Count);
	}

	// -------------------------------------------------------------------------
	// Identity when the mod has no Nexus id
	// -------------------------------------------------------------------------

	[Fact]
	public void AModWithNoNexusIdIsStillRecorded()
	{
		string path = TempFile();

		// Plenty of installed mods are not matched to Nexus yet — that is what Auto Match is for — and one of
		// them proving itself worth suggesting is not a reason to drop the capture.
		SuggestedModStore.Upsert(path, Mod("Some Local Mod", uniqueId: "author.somelocalmod"));

		SuggestedMod stored = Assert.Single(SuggestedModStore.Load(path));
		Assert.Null(stored.NexusId);
		Assert.Equal("author.somelocalmod", stored.UniqueId);
	}

	[Fact]
	public void AModWithNothingButANameIsStillRecordedAndStillDeduplicates()
	{
		string path = TempFile();

		SuggestedModStore.Upsert(path, Mod("Nameless Thing", reason: "first"));
		Assert.True(SuggestedModStore.Upsert(path, Mod("nameless thing", reason: "second")));

		SuggestedMod stored = Assert.Single(SuggestedModStore.Load(path));
		Assert.Equal("second", stored.Reason);
	}

	[Fact]
	public void AGitHubModIsMatchedByItsRepositoryWhateverTheCase()
	{
		string path = TempFile();

		SuggestedModStore.Upsert(path, Mod("Stardew Access", gitHub: "stardew-access/stardew-access", reason: "first"));
		Assert.True(SuggestedModStore.Upsert(path, Mod("Stardew Access", gitHub: "Stardew-Access/Stardew-Access", reason: "second")));

		Assert.Single(SuggestedModStore.Load(path));
	}

	// -------------------------------------------------------------------------
	// Removing, and finding what is already there
	// -------------------------------------------------------------------------

	[Fact]
	public void RemovingTakesTheEntryAndReportsWhetherThereWasOne()
	{
		string path = TempFile();
		SuggestedMod entry = Mod("Auto-Fishing", nexusId: "12345");

		SuggestedModStore.Upsert(path, entry);

		Assert.True(SuggestedModStore.Remove(path, entry));
		Assert.Empty(SuggestedModStore.Load(path));
		Assert.False(SuggestedModStore.Remove(path, entry));
	}

	[Fact]
	public void FindTellsACaptureWhetherItIsAnEdit()
	{
		string path = TempFile();

		Assert.Null(SuggestedModStore.Find(path, "StardewValley", Mod("Auto-Fishing", nexusId: "12345")));

		SuggestedModStore.Upsert(path, Mod("Auto-Fishing", nexusId: "12345", reason: "recorded earlier"));

		SuggestedMod? found = SuggestedModStore.Find(path, "StardewValley@Gog", Mod("Auto-Fishing", nexusId: "12345"));
		Assert.NotNull(found);
		Assert.Equal("recorded earlier", found!.Reason);
	}

	// -------------------------------------------------------------------------
	// A file that isn't there, or isn't readable
	// -------------------------------------------------------------------------

	[Fact]
	public void NoFileYetIsAnEmptyListRatherThanAnError()
	{
		Assert.Empty(SuggestedModStore.Load(TempFile()));
	}

	[Fact]
	public void AnUnreadableFileIsAnEmptyListRatherThanAnError()
	{
		// Refusing to open the review list because the file got truncated would hide whatever survived, at the
		// one moment the list is actually wanted.
		string path = TempFile();
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, "{ this is not the list");

		Assert.Empty(SuggestedModStore.Load(path));
	}

	[Fact]
	public void SavingCreatesTheFolderWhenItIsNotThereYet()
	{
		string path = TempFile();

		SuggestedModStore.Save(path, new List<SuggestedMod> { Mod("Auto-Fishing", nexusId: "1") });

		Assert.True(File.Exists(path));
	}

	// -------------------------------------------------------------------------
	// Entries written before categories became data
	// -------------------------------------------------------------------------

	[Theory]
	[InlineData("0", SuggestionCategoryStore.CantByEar)]
	[InlineData("1", SuggestionCategoryStore.SlowerByEar)]
	[InlineData("2", SuggestionCategoryStore.BugFix)]
	[InlineData("3", SuggestionCategoryStore.Easier)]
	public void AStoredEnumNumberBecomesTheCategoryItUsedToMean(string stored, string expected)
	{
		// Categories were a fixed enum once, so a file written then holds its NUMBER — "Category": 3. Read into a
		// string that arrives as "3", and without this it would name a category that does not exist.
		Assert.Equal(expected, SuggestedModStore.NormalizeCategoryId(stored));
	}

	[Fact]
	public void AFileFullOfEnumNumbersUpgradesItselfOnBeingRead()
	{
		string path = TempFile();
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, """
			[
			  { "Game": "MoonlightPeaks", "Name": "Save Anywhere", "NexusId": "11", "Category": 3, "Reason": "keeps" },
			  { "Game": "MoonlightPeaks", "Name": "Quick Spells",  "NexusId": "114", "Category": 0, "Reason": "keeps" }
			]
			""");

		var entries = SuggestedModStore.Load(path);

		Assert.Equal(2, entries.Count);
		Assert.Equal(SuggestionCategoryStore.CantByEar, entries.Single(e => e.Name == "Quick Spells").Category);
		Assert.Equal(SuggestionCategoryStore.Easier, entries.Single(e => e.Name == "Save Anywhere").Category);
		Assert.All(entries, e => Assert.Equal("keeps", e.Reason));
	}

	[Fact]
	public void AnUnrecognisedCategoryFallsBackRatherThanLosingTheEntry()
	{
		// Filed in the wrong place is recoverable in one keypress from the marked-mods list; filed under nothing
		// means the entry never appears in the finished list at all.
		Assert.Equal(SuggestionCategoryStore.CantByEar, SuggestedModStore.NormalizeCategoryId("99"));
		Assert.Equal(SuggestionCategoryStore.CantByEar, SuggestedModStore.NormalizeCategoryId(""));
		Assert.Equal(SuggestionCategoryStore.CantByEar, SuggestedModStore.NormalizeCategoryId(null));
	}

	[Fact]
	public void ACategoryOfItsOwnIsKeptAsItIs()
	{
		Assert.Equal("CombatQoL", SuggestedModStore.NormalizeCategoryId("CombatQoL"));
		Assert.Equal("CombatQoL", SuggestedModStore.NormalizeCategoryId("  CombatQoL  "));
	}

	// -------------------------------------------------------------------------
	// The shipped list and the user's own, laid over each other
	// -------------------------------------------------------------------------

	[Fact]
	public void MergingShowsBothListsWhenTheyDoNotOverlap()
	{
		var merged = SuggestedModStore.Merge(
			new[] { Mod("Shipped Mod", nexusId: "1") },
			new[] { Mod("My Mod", nexusId: "2") });

		Assert.Equal(new[] { "My Mod", "Shipped Mod" }, merged.Select(e => e.Name));
	}

	[Fact]
	public void MyOwnEntryWinsOverTheShippedOneForTheSameMod()
	{
		// The shipped list is replaced wholesale by the next release, so a user who re-marked a mod has to keep
		// their wording — otherwise an update silently overwrites an opinion they went out of their way to record.
		var merged = SuggestedModStore.Merge(
			new[] { Mod("Auto-Fishing", nexusId: "12345", reason: "what shipped") },
			new[] { Mod("Auto-Fishing", nexusId: "12345", reason: "what I think") });

		SuggestedMod single = Assert.Single(merged);
		Assert.Equal("what I think", single.Reason);
	}

	[Fact]
	public void TheSameModForAnotherGameIsNotAnOverride()
	{
		var merged = SuggestedModStore.Merge(
			new[] { Mod("Quick Loot", game: "SkyrimSE", nexusId: "7", reason: "shipped") },
			new[] { Mod("Quick Loot", game: "Fallout4", nexusId: "7", reason: "mine") });

		Assert.Equal(2, merged.Count);
	}

	[Fact]
	public void MergingWithNothingOfMyOwnIsJustTheShippedList()
	{
		var merged = SuggestedModStore.Merge(
			new[] { Mod("Shipped Mod", nexusId: "1") },
			Array.Empty<SuggestedMod>());

		Assert.Equal("Shipped Mod", Assert.Single(merged).Name);
	}

	[Fact]
	public void MergingWhenNothingShippedIsJustMine()
	{
		// What every game looks like today: no baseline file exists yet, and Load returns an empty list for one
		// that is not there, so this is the ordinary path rather than an edge case.
		var merged = SuggestedModStore.Merge(
			SuggestedModStore.Load(TempFile()),
			new[] { Mod("My Mod", nexusId: "2") });

		Assert.Equal("My Mod", Assert.Single(merged).Name);
	}

	// -------------------------------------------------------------------------
	// Re-filing, which is what lets a category in use be deleted
	// -------------------------------------------------------------------------

	[Fact]
	public void ReassigningMovesOnlyTheMatchingEntriesAndSaysHowMany()
	{
		string path = TempFile();
		SuggestedModStore.Upsert(path, Mod("A", nexusId: "1", category: "CombatQoL"));
		SuggestedModStore.Upsert(path, Mod("B", nexusId: "2", category: "CombatQoL"));
		SuggestedModStore.Upsert(path, Mod("C", nexusId: "3", category: SuggestionCategoryStore.BugFix));

		Assert.Equal(2, SuggestedModStore.Reassign(path, "CombatQoL", SuggestionCategoryStore.Easier));

		var entries = SuggestedModStore.Load(path);
		Assert.Equal(SuggestionCategoryStore.Easier, entries.Single(e => e.Name == "A").Category);
		Assert.Equal(SuggestionCategoryStore.Easier, entries.Single(e => e.Name == "B").Category);
		Assert.Equal(SuggestionCategoryStore.BugFix, entries.Single(e => e.Name == "C").Category);
	}

	[Fact]
	public void ReassigningACategoryNothingUsesChangesNothing()
	{
		string path = TempFile();
		SuggestedModStore.Upsert(path, Mod("A", nexusId: "1", category: SuggestionCategoryStore.BugFix));

		Assert.Equal(0, SuggestedModStore.Reassign(path, "CombatQoL", SuggestionCategoryStore.Easier));
		Assert.Equal(SuggestionCategoryStore.BugFix, Assert.Single(SuggestedModStore.Load(path)).Category);
	}
}
