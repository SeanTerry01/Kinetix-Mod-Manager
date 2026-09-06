using System;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// What the screen reader is actually told, as opposed to whether it is told anything.
///
/// <para>
/// <see cref="AccessibleListGuardTests"/> proves every list is wired to announce itself, and
/// <see cref="SpokenStringGuardTests"/> proves every phrase the app asks for exists in English. Neither can see
/// the wording: both read the source text, so a position that counts from zero, a row and its position in the
/// wrong order, or the return of the double full stop all leave them green. A mutation campaign against the
/// accessibility layer confirmed exactly that — those four changes passed all 742 tests. These are the cases
/// that fail instead.
/// </para>
///
/// <para>
/// The join is supplied by the test rather than taken from the language file, so these assert the composition
/// rules themselves and not that the code agrees with its own call to <c>Loc.T</c>. The expected strings are
/// written out in full for the same reason: a value recomputed the way the code computes it asserts nothing.
/// </para>
/// </summary>
public class AnnouncementTests
{
	/// <summary>The real shape of both join phrases in lang/en.json: "{0}. {1}".</summary>
	private static string Join(string a, string b) => a + ". " + b;

	// ---------------------------------------------------------------- row, then position ----

	[Fact]
	public void ARowIsReadBeforeItsPosition()
	{
		Assert.Equal("Stardew Access. 129 of 149",
			Announcements.RowThenPosition("Stardew Access", "129 of 149", Join));
	}

	[Fact]
	public void TheRowsOwnFullStopDoesNotBecomeTwo()
	{
		// "Enabled." already ends in the stop that makes the reader pause. Joining naively gave "Enabled. . 129
		// of 149", heard as a stumble.
		Assert.Equal("Enabled. 129 of 149",
			Announcements.RowThenPosition("Enabled.", "129 of 149", Join));
	}

	[Fact]
	public void WhitespaceHidingBehindTheFullStopGoesWithIt()
	{
		Assert.Equal("Enabled. 3 of 9",
			Announcements.RowThenPosition("Enabled .  ", "3 of 9", Join));
	}

	[Fact]
	public void ARowWithNoPositionIsReadOnItsOwn()
	{
		// A heading has no position. Joining it to nothing would leave a separator dangling off the end.
		Assert.Equal("Installed Mods", Announcements.RowThenPosition("Installed Mods.", "", Join));
	}

	[Fact]
	public void APositionWithNoRowIsStillRead()
	{
		Assert.Equal("7 of 12", Announcements.RowThenPosition("", "7 of 12", Join));
	}

	[Fact]
	public void ARowThatIsNullIsNotSpokenAsTheWordNull()
	{
		Assert.Equal("7 of 12", Announcements.RowThenPosition(null, "7 of 12", Join));
	}

	// ------------------------------------------------------------- list name, then the rest ----

	[Fact]
	public void TheListsNameIsReadBeforeTheRest()
	{
		Assert.Equal("Installed Mods List. Stardew Access. 129 of 149",
			Announcements.ListNameThenRest("Installed Mods List", "Stardew Access. 129 of 149", Join));
	}

	[Fact]
	public void TheListNameAlsoDoesNotProduceTwoFullStops()
	{
		// The same rule as the row, in the other caller. This is the caller that historically does not get a test.
		Assert.Equal("Installed Mods List. Stardew Access",
			Announcements.ListNameThenRest("Installed Mods List.", "Stardew Access", Join));
	}

	[Fact]
	public void AnUnnamedListAddsNothingRatherThanAnEmptyPause()
	{
		Assert.Equal("Stardew Access", Announcements.ListNameThenRest("", "Stardew Access", Join));
		Assert.Equal("Stardew Access", Announcements.ListNameThenRest(null, "Stardew Access", Join));
		Assert.Equal("Stardew Access", Announcements.ListNameThenRest("   ", "Stardew Access", Join));
	}

	[Fact]
	public void SpaceInFrontOfAListsNameIsNotReadAsAPause()
	{
		// A name with real content in it, deliberately: the all-whitespace case above cannot test the leading
		// trim, because the trailing trim inside the helper flattens "   " to empty on its own. A re-measurement
		// removed the leading trim and every case here stayed green.
		Assert.Equal("Installed Mods List. Stardew Access",
			Announcements.ListNameThenRest("  Installed Mods List  ", "Stardew Access", Join));
	}

	[Fact]
	public void ANamedListWithNothingElseToSayStillSaysItsName()
	{
		Assert.Equal("Installed Mods List", Announcements.ListNameThenRest("Installed Mods List.", "", Join));
	}

	// ------------------------------------------------------------------- outline position ----

	/// <summary>Renders the key and its two numbers so a test can see all three without a language file.</summary>
	private static string Format(string key, int position, int total) => $"{key}|{position}|{total}";

	[Fact]
	public void TheFirstRowOfTheOutlineIsOneOfN_NotZero()
	{
		Assert.Equal("common.position|1|40",
			Announcements.OutlinePosition(0, 40, _ => false, Format));
	}

	[Fact]
	public void TheLastRowOfTheOutlineCountsToTheTotal()
	{
		Assert.Equal("common.position|40|40",
			Announcements.OutlinePosition(39, 40, _ => false, Format));
	}

	[Fact]
	public void AHeadingThatOpensIntoSubTopicsIsAnnouncedAsAGroup()
	{
		// One child is enough to be a group: a heading with a single sub-topic still opens.
		Assert.Equal("doc.posGroup|3|40",
			Announcements.OutlinePosition(2, 40, _ => true, Format));
	}

	[Theory]
	[InlineData(-1, 40)]
	[InlineData(40, 40)]
	[InlineData(41, 40)]
	[InlineData(0, 0)]
	public void AnIndexThatIsNotARowSaysNothingRatherThanThrowing(int index, int count)
	{
		Assert.Equal("", Announcements.OutlinePosition(index, count, _ => throw new InvalidOperationException(
			"the row must not be inspected when the index is out of range"), Format));
	}
}
