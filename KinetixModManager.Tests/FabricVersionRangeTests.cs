using System;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="FabricVersionRange"/> — whether a mod will let the game start on a Minecraft version.
///
/// <para>
/// Every range below was taken from the jars in Sean's mods folder on 2026-09-15, after the manager moved him to
/// Minecraft 26.3 and the game refused to launch. Four mods stopped it dead and two were perfectly happy without
/// any new build; nothing the manager looked at could tell those apart, because it never read what they declared.
/// </para>
/// </summary>
public class FabricVersionRangeTests
{
	// ---------------------------------------------------------------------
	// The real ranges, against the version that broke the game
	// ---------------------------------------------------------------------

	[Theory]
	[InlineData(">=26.2")]            // disrobe_sounds, lowhealthwarning — fine on 26.3, no new build needed
	[InlineData("~26.3")]             // united_minecraft
	[InlineData("~26.3-")]            // fabric-api
	[InlineData(">=26.3-rc.3")]       // presencefootsteps: a release outranks the pre-release it asks for
	[InlineData("*")]
	[InlineData("")]
	public void TheseLetMinecraft263Start(string range) =>
		Assert.True(FabricVersionRange.Accepts(range, "26.3"));

	[Theory]
	[InlineData("26.2")]              // reallifetime, sheltered-rain-sound: an exact version
	[InlineData("~26.2")]             // sound_physics_remastered: any 26.2.x
	[InlineData(">=26.2 <26.3-")]     // playerdeathsound: everything up to, but not including, 26.3
	[InlineData("26.2.x")]
	public void TheseStopMinecraft263Starting(string range) =>
		Assert.False(FabricVersionRange.Accepts(range, "26.3"));

	// ---------------------------------------------------------------------
	// The comparators
	// ---------------------------------------------------------------------

	[Theory]
	[InlineData(">=26.2", "26.2", true)]
	[InlineData(">26.2", "26.2", false)]
	[InlineData("<=26.3", "26.3", true)]
	[InlineData("<26.3", "26.3", false)]
	[InlineData("=26.2", "26.2", true)]
	[InlineData("^26.2", "26.9", true)]     // same major
	[InlineData("^26.2", "27.0", false)]
	[InlineData("~26.2", "26.2.4", true)]   // same minor
	[InlineData("~26.2", "26.3", false)]
	public void EachComparatorMeansWhatItSays(string range, string version, bool expected) =>
		Assert.Equal(expected, FabricVersionRange.Accepts(range, version));

	[Fact]
	public void AlternativesAreOredAndTermsAreAnded()
	{
		Assert.True(FabricVersionRange.Accepts("26.2 || 26.3", "26.3"));
		Assert.False(FabricVersionRange.Accepts("26.1 || 26.2", "26.3"));
		Assert.True(FabricVersionRange.Accepts(">=26.2 <27", "26.3"));
		Assert.False(FabricVersionRange.Accepts(">=26.2 <26.3", "26.3"));
	}

	// ---------------------------------------------------------------------
	// Ordering, including the pre-release rule
	// ---------------------------------------------------------------------

	[Fact]
	public void AReleaseOutranksItsOwnPreRelease()
	{
		Assert.True(FabricVersionRange.Compare("26.3", "26.3-rc.3") > 0);
		Assert.True(FabricVersionRange.Compare("26.3-rc.1", "26.3-rc.3") < 0);
		Assert.Equal(0, FabricVersionRange.Compare("26.3", "26.3"));
	}

	[Fact]
	public void ANumberIsComparedAsANumberNotAsText() =>
		Assert.True(FabricVersionRange.Compare("26.10", "26.9") > 0);

	[Fact]
	public void ABuildTagIsNotPartOfTheVersion() =>
		// Minecraft mods commonly carry the game version as build metadata: "1.5.1+26.2" is mod version 1.5.1.
		Assert.Equal(0, FabricVersionRange.Compare("1.5.1+26.2", "1.5.1"));

	// ---------------------------------------------------------------------
	// Surviving a move: the catalogue outranks what the mod says about itself
	// ---------------------------------------------------------------------

	[Fact]
	public void AModThatDeclaresNothingIsStillCaughtByItsCatalogueEntry()
	{
		// Toolbar Sounds, 2026-09-15. It declares no Minecraft version, so reading the jar alone said "fine on
		// anything" — it loaded on 26.3, its data pack would not parse, and the world refused to open with
		// nothing naming the mod. Modrinth listed its supported versions ending at 26.2 the whole time.
		var catalogue = new[] { "1.21.8", "26.1", "26.2" };

		Assert.False(FabricVersionRange.SurvivesMove(catalogue, declaredRange: null, "26.3"));
		Assert.True(FabricVersionRange.SurvivesMove(catalogue, declaredRange: null, "26.2"));
	}

	[Fact]
	public void TheCatalogueWinsOverAGenerousDeclaration()
	{
		// ">=26.2" would say yes to 26.3 on its own. The published build says otherwise, and it is the one that
		// was actually compiled against a game version.
		Assert.False(FabricVersionRange.SurvivesMove(new[] { "26.2" }, ">=26.2", "26.3"));
	}

	[Fact]
	public void WithNoCatalogueAnswerTheModsOwnWordIsUsed()
	{
		Assert.True(FabricVersionRange.SurvivesMove(null, ">=26.2", "26.3"));
		Assert.False(FabricVersionRange.SurvivesMove(Array.Empty<string>(), "26.2", "26.3"));
		// Nothing known from either: not evidence against the mod.
		Assert.True(FabricVersionRange.SurvivesMove(null, null, "26.3"));
	}

	// ---------------------------------------------------------------------
	// What happens when the range cannot be read
	// ---------------------------------------------------------------------

	[Fact]
	public void SomethingUnreadableIsNotTreatedAsABrokenMod()
	{
		// This decides whether to switch somebody's mod off. A range nobody can parse is not evidence against it.
		Assert.True(FabricVersionRange.Accepts("whatever the author typed", "26.3"));
		Assert.True(FabricVersionRange.Accepts(null, "26.3"));
		Assert.True(FabricVersionRange.Accepts(">=26.2", null));
	}
}
