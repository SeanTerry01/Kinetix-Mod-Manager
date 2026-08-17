using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="SuggestionCategoryStore"/> — the headings a curator files suggestions under.
///
/// <para>
/// The rule these tests exist to hold is that a suggestion can never end up filed under a category that is not
/// there. The four built-ins cannot be deleted through the UI, but a file can be hand-edited or half-written,
/// and the list that ships is filed under them — so whatever arrives, what comes back out has all four in it.
/// </para>
/// </summary>
public class SuggestionCategoryStoreTests
{
	private static string TempFile() =>
		Path.Combine(Path.GetTempPath(), "kmm-categories-" + Guid.NewGuid().ToString("N"), "suggestion-categories.json");

	private static SuggestionCategory Custom(string id, string label) =>
		new() { Id = id, Label = label, IsBuiltIn = false };

	// -------------------------------------------------------------------------
	// What ships
	// -------------------------------------------------------------------------

	[Fact]
	public void AFirstRunGetsTheFourThatShip()
	{
		var categories = SuggestionCategoryStore.Load(TempFile());

		Assert.Equal(SuggestionCategoryStore.BuiltInIds, categories.Select(c => c.Id));
		Assert.All(categories, c => Assert.True(c.IsBuiltIn));
		Assert.All(categories, c => Assert.Equal("curator.category" + c.Id, c.LocKey));
	}

	[Fact]
	public void TheBuiltInOrderIsTheOrderTheRetiredEnumUsed()
	{
		// Position in this array is what turns a stored "3" back into Easier, so reordering it would silently
		// re-file every entry written before categories became data.
		Assert.Equal(
			new[] { "CantByEar", "SlowerByEar", "BugFix", "Easier" },
			SuggestionCategoryStore.BuiltInIds);
	}

	[Fact]
	public void AnUnreadableFileGivesTheFourThatShipRatherThanNothing()
	{
		string path = TempFile();
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, "{ not a list at all");

		Assert.Equal(4, SuggestionCategoryStore.Load(path).Count);
	}

	// -------------------------------------------------------------------------
	// Round trip, and the user's own categories
	// -------------------------------------------------------------------------

	[Fact]
	public void ACategoryOfYourOwnSurvivesAndKeepsItsPlace()
	{
		string path = TempFile();
		var categories = SuggestionCategoryStore.Defaults();
		categories.Insert(1, Custom("CombatQoL", "Combat quality of life"));

		SuggestionCategoryStore.Save(path, categories);
		var loaded = SuggestionCategoryStore.Load(path);

		Assert.Equal(5, loaded.Count);
		Assert.Equal("CombatQoL", loaded[1].Id);
		Assert.Equal("Combat quality of life", loaded[1].Label);
		Assert.False(loaded[1].IsBuiltIn);
	}

	[Fact]
	public void ARenamedBuiltInKeepsItsIdAndItsLanguageKey()
	{
		string path = TempFile();
		var categories = SuggestionCategoryStore.Defaults();
		categories[0].Label = "Impossible without sight";

		SuggestionCategoryStore.Save(path, categories);
		SuggestionCategory reloaded = SuggestionCategoryStore.Load(path)[0];

		// The shipped name is never overwritten, only sat in front of — which is what makes clearing the label
		// put the translation back.
		Assert.Equal(SuggestionCategoryStore.CantByEar, reloaded.Id);
		Assert.Equal("Impossible without sight", reloaded.Label);
		Assert.Equal("curator.categoryCantByEar", reloaded.LocKey);
		Assert.True(reloaded.IsBuiltIn);
	}

	// -------------------------------------------------------------------------
	// Normalising whatever the file happens to contain
	// -------------------------------------------------------------------------

	[Fact]
	public void AMissingBuiltInIsPutBack()
	{
		var normalized = SuggestionCategoryStore.Normalize(new List<SuggestionCategory>
		{
			Custom("CombatQoL", "Combat quality of life")
		});

		Assert.Contains(normalized, c => c.Id == "CombatQoL");
		foreach (string id in SuggestionCategoryStore.BuiltInIds)
			Assert.Contains(normalized, c => c.Id == id);
	}

	[Fact]
	public void AFileClaimingAUserCategoryIsBuiltInIsNotBelieved()
	{
		// Otherwise a hand-edited flag could make a user's own category undeletable, or a built-in deletable.
		var normalized = SuggestionCategoryStore.Normalize(new List<SuggestionCategory>
		{
			new() { Id = "CombatQoL", Label = "Combat", IsBuiltIn = true },
			new() { Id = SuggestionCategoryStore.BugFix, IsBuiltIn = false }
		});

		Assert.False(SuggestionCategoryStore.Find(normalized, "CombatQoL")!.IsBuiltIn);
		Assert.True(SuggestionCategoryStore.Find(normalized, SuggestionCategoryStore.BugFix)!.IsBuiltIn);
	}

	[Fact]
	public void DuplicatesAndBlanksAreDropped()
	{
		var normalized = SuggestionCategoryStore.Normalize(new List<SuggestionCategory>
		{
			Custom("CombatQoL", "First"),
			Custom("combatqol", "Second, same id in another case"),
			Custom("", "No id at all")
		});

		Assert.Single(normalized, c => string.Equals(c.Id, "CombatQoL", StringComparison.OrdinalIgnoreCase));
		Assert.Equal("First", SuggestionCategoryStore.Find(normalized, "CombatQoL")!.Label);
		Assert.DoesNotContain(normalized, c => c.Label == "No id at all");
	}

	[Fact]
	public void NormalizingNothingStillGivesTheFourThatShip()
	{
		Assert.Equal(4, SuggestionCategoryStore.Normalize(null).Count);
		Assert.Equal(4, SuggestionCategoryStore.Normalize(new List<SuggestionCategory>()).Count);
	}

	// -------------------------------------------------------------------------
	// Finding, and naming a new one
	// -------------------------------------------------------------------------

	[Fact]
	public void FindIgnoresCaseAndSurroundingSpace()
	{
		var categories = SuggestionCategoryStore.Defaults();

		Assert.NotNull(SuggestionCategoryStore.Find(categories, "bugfix"));
		Assert.NotNull(SuggestionCategoryStore.Find(categories, "  BugFix "));
		Assert.Null(SuggestionCategoryStore.Find(categories, "NoSuchCategory"));
		Assert.Null(SuggestionCategoryStore.Find(categories, null));
	}

	[Fact]
	public void ANewIdComesFromTheNameAndNeverCollides()
	{
		var categories = SuggestionCategoryStore.Defaults();

		Assert.Equal("CombatQoL", SuggestionCategoryStore.NewId(categories, "Combat QoL"));

		categories.Add(Custom("CombatQoL", "Combat QoL"));
		Assert.Equal("CombatQoL2", SuggestionCategoryStore.NewId(categories, "Combat QoL"));

		categories.Add(Custom("CombatQoL2", "Combat QoL again"));
		Assert.Equal("CombatQoL3", SuggestionCategoryStore.NewId(categories, "Combat QoL"));
	}

	[Fact]
	public void ANameWithNothingUsableStillGetsAnId()
	{
		// A name written entirely in punctuation, or in a script this strips, still has to file somewhere.
		string id = SuggestionCategoryStore.NewId(SuggestionCategoryStore.Defaults(), "!!! ???");

		Assert.False(string.IsNullOrWhiteSpace(id));
		Assert.Equal("Category", id);
	}

	[Fact]
	public void ANewIdCannotCollideWithABuiltIn()
	{
		Assert.Equal("BugFix2", SuggestionCategoryStore.NewId(SuggestionCategoryStore.Defaults(), "Bug Fix"));
	}
}
