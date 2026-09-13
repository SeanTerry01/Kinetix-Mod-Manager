using System;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ModVersions"/> — whether a mod has an update waiting.
///
/// Nagging is not a cosmetic failure here. An Updates list that keeps reporting work already done is one
/// the user stops trusting, and that list is how they find out a mod they depend on has been fixed.
/// </summary>
public class ModVersionsTests
{
	private static GameMod Mod(string version, string? nexusId = null, string? gitHub = null) =>
		new() { Name = "Test", Version = version, NexusID = nexusId, GitHubRepo = gitHub };

	// ---------------------------------------------------------------------
	// Comparing
	// ---------------------------------------------------------------------

	[Theory]
	[InlineData("1.0.0", "1.0.1", true)]
	[InlineData("1.0.0", "1.1.0", true)]
	[InlineData("1.9.0", "1.10.0", true)]     // not a string comparison: "10" beats "9"
	[InlineData("2.0.0", "1.9.9", false)]
	[InlineData("1.0.0", "1.0.0", false)]
	public void TheLaterVersionIsRecognisedAsLater(string current, string target, bool expected) =>
		Assert.Equal(expected, ModVersions.IsNewer(current, target));

	[Fact]
	public void NothingToCompareAgainstMeansAnythingIsNewer()
	{
		Assert.True(ModVersions.IsNewer("", "1.0.0"));
		Assert.True(ModVersions.IsNewer(null, "1.0.0"));
	}

	[Fact]
	public void NoCandidateIsNeverAnUpdate()
	{
		Assert.False(ModVersions.IsNewer("1.0.0", ""));
		Assert.False(ModVersions.IsNewer("1.0.0", null));
	}

	// ---------------------------------------------------------------------
	// Taking the best of two answers
	// ---------------------------------------------------------------------

	[Fact]
	public void TheLaterOfTwoAnswersWinsWhicheverArrivedFirst()
	{
		// Two sources are asked, smapi.io and Nexus, and they do not answer in a fixed order. Keeping the
		// maximum is what stops a slower, older answer overwriting a newer one and leaving a row reading
		// "Current: 2.2.0. Latest: 2.2.0".
		Assert.Equal("2.3.0", ModVersions.Best("2.2.0", "2.3.0"));
		Assert.Equal("2.3.0", ModVersions.Best("2.3.0", "2.2.0"));
	}

	[Fact]
	public void AnEmptyAnswerLeavesWhatWasAlreadyKnown()
	{
		Assert.Equal("2.3.0", ModVersions.Best("2.3.0", ""));
		Assert.Equal("2.3.0", ModVersions.Best("2.3.0", null));
	}

	[Fact]
	public void TheFirstAnswerIsTakenWhenNothingWasKnown()
	{
		Assert.Equal("1.0.0", ModVersions.Best(null, "1.0.0"));
	}

	// ---------------------------------------------------------------------
	// The decision
	// ---------------------------------------------------------------------

	[Fact]
	public void AModOnAnOlderVersionHasAnUpdate()
	{
		Assert.True(ModVersions.HasPendingUpdate(Mod("1.0.0", nexusId: "123"), "1.1.0", recordedVersion: null));
	}

	[Fact]
	public void AModAlreadyOnTheLatestDoesNot()
	{
		Assert.False(ModVersions.HasPendingUpdate(Mod("1.1.0", nexusId: "123"), "1.1.0", recordedVersion: null));
	}

	[Fact]
	public void TheRecordedReleaseBeatsTheManifestVersion()
	{
		// The case this class exists for. SMAPI demands a semantic version, so an author publishing "2.0.3.5"
		// must still write "2.0.3" in the manifest. Comparing the manifest to the page offers the same update
		// forever; comparing what was actually installed answers correctly.
		GameMod mod = Mod("2.0.3", nexusId: "456");

		Assert.False(ModVersions.HasPendingUpdate(mod, "2.0.3.5", recordedVersion: "2.0.3.5"));
		Assert.True(ModVersions.HasPendingUpdate(mod, "2.0.3.5", recordedVersion: null));
	}

	[Fact]
	public void ARecordedReleaseIsIgnoredForAModWithNoUpdateLink()
	{
		// With no Nexus id and no GitHub repo there is no download the record could be about, so the
		// manifest is the only honest thing to compare.
		GameMod unlinked = Mod("1.0.0");

		Assert.True(ModVersions.HasPendingUpdate(unlinked, "1.1.0", recordedVersion: "9.9.9"));
	}

	[Fact]
	public void NoLatestVersionMeansNoUpdateRatherThanAnUnknownOne()
	{
		// A check that failed must not be reported as "up to date" nor as an update. Both are claims the
		// manager cannot support.
		Assert.False(ModVersions.HasPendingUpdate(Mod("1.0.0", nexusId: "1"), null, null));
		Assert.False(ModVersions.HasPendingUpdate(Mod("1.0.0", nexusId: "1"), "   ", null));
	}

	// ---------------------------------------------------------------------
	// Identity and details
	// ---------------------------------------------------------------------

	[Fact]
	public void ANexusModIsKeyedByItsPageAndAGitHubOneByItsRepo()
	{
		Assert.Equal("Nexus:2400", ModVersions.DownloadKey(Mod("1.0", nexusId: "2400")));
		Assert.Equal("GitHub:owner/repo", ModVersions.DownloadKey(Mod("1.0", gitHub: "owner/repo")));
	}

	[Theory]
	[InlineData("Unknown", "A real description", true)]
	[InlineData("User", "A real description", true)]
	[InlineData("", "A real description", true)]
	[InlineData("A Real Author", "Installed local mod.", true)]
	[InlineData("A Real Author", "Installed BepInEx plugin.", true)]
	[InlineData("A Real Author", "", true)]
	[InlineData("A Real Author", "A real description", false)]
	public void AModIsWorthAskingAboutWhileItStillCarriesPlaceholders(string author, string description, bool expected)
	{
		var mod = new GameMod { Name = "Test", Author = author, Description = description };

		Assert.Equal(expected, ModVersions.NeedsDetails(mod));
	}
}
