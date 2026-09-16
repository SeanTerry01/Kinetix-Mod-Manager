using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// The Minecraft accessibility suite, whose whole job is to turn one question the user can answer - "which
/// accessibility mod?" - into an install plan they never have to think about.
/// </summary>
public class MinecraftSuiteTests
{
	[Fact]
	public void BothAccessModsAreOfferedRatherThanOnlyTheNewerOne()
	{
		// Minecraft Access is the older of the two, and blind players have used it for years. Dropping it
		// because United Minecraft is newer would strand them; familiarity is an accessibility concern.
		Assert.Equal(2, MinecraftSuite.AccessMods.Count);
		Assert.Contains(MinecraftSuite.AccessMods, m => m.Id == MinecraftSuite.UnitedMinecraftId);
		Assert.Contains(MinecraftSuite.AccessMods, m => m.Id == MinecraftSuite.MinecraftAccessId);
	}

	[Fact]
	public void UnitedMinecraftNeedsFabricApiAndMinecraftAccessDoesNot()
	{
		// The single fact the whole suite turns on, and the one the user must never be asked about. Read from
		// the mods themselves: Minecraft Access lists fabric-api as "embedded" and ships it jar-in-jar;
		// United Minecraft declares it as an ordinary dependency and bundles nothing.
		Assert.True(MinecraftSuite.AccessModFor(MinecraftSuite.UnitedMinecraftId).NeedsFabricApi);
		Assert.False(MinecraftSuite.AccessModFor(MinecraftSuite.MinecraftAccessId).NeedsFabricApi);
	}

	[Fact]
	public void ChoosingUnitedMinecraftInstallsFabricApiFirst()
	{
		var plan = MinecraftSuite.InstallPlanFor(MinecraftSuite.AccessModFor(MinecraftSuite.UnitedMinecraftId));

		Assert.Equal(2, plan.Count);
		// Order matters: a half-finished install must never leave the access mod sitting there without the API
		// it refuses to load without.
		Assert.Equal("fabric-api", plan[0].FabricModId);
		Assert.Equal("united_minecraft", plan[1].FabricModId);
	}

	[Fact]
	public void ChoosingMinecraftAccessInstallsOneFileAndNothingElse()
	{
		var plan = MinecraftSuite.InstallPlanFor(MinecraftSuite.AccessModFor(MinecraftSuite.MinecraftAccessId));

		Assert.Single(plan);
		Assert.Equal("minecraft_access", plan[0].FabricModId);
	}

	[Fact]
	public void AnUnsetOrUnknownChoiceFallsBackToTheDefaultRatherThanNothing()
	{
		// A fresh install has no setting yet, and a settings file written by a future version might name a mod
		// this build has never heard of. Neither should leave the user with an empty suite.
		Assert.Equal(MinecraftSuite.Default.Id, MinecraftSuite.AccessModFor(null).Id);
		Assert.Equal(MinecraftSuite.Default.Id, MinecraftSuite.AccessModFor("").Id);
		Assert.Equal(MinecraftSuite.Default.Id, MinecraftSuite.AccessModFor("SomeModFromTheFuture").Id);
	}

	[Fact]
	public void TheChoiceIsMadeByTheNameTheUserActuallyHears()
	{
		// The chooser lists display names, and hands the chosen string straight back.
		foreach (string name in MinecraftSuite.AccessModNames)
			Assert.NotNull(MinecraftSuite.AccessModByDisplayName(name));

		Assert.Null(MinecraftSuite.AccessModByDisplayName("Not A Mod We Ship"));
	}

	[Fact]
	public void EachAccessModIsTheOthersRival()
	{
		// Both hook narration on the same screens, so running the pair is expected to double-speak everything.
		var united = MinecraftSuite.AccessModFor(MinecraftSuite.UnitedMinecraftId);
		var access = MinecraftSuite.AccessModFor(MinecraftSuite.MinecraftAccessId);

		Assert.Equal(access.Id, MinecraftSuite.RivalsOf(united).Single().Id);
		Assert.Equal(united.Id, MinecraftSuite.RivalsOf(access).Single().Id);
	}

	[Fact]
	public void EveryModCarriesWhatIsNeededToFetchAndRecogniseIt()
	{
		foreach (MinecraftSuiteMod mod in MinecraftSuite.AccessMods.Append(MinecraftSuite.FabricApi))
		{
			Assert.False(string.IsNullOrWhiteSpace(mod.Id));
			Assert.False(string.IsNullOrWhiteSpace(mod.DisplayName));
			// Without a fabric.mod.json id there is no way to tell an installed copy from an absent one.
			Assert.False(string.IsNullOrWhiteSpace(mod.FabricModId));
			Assert.False(string.IsNullOrWhiteSpace(mod.Source));
		}
	}

	[Fact]
	public void UnitedMinecraftComesFromGitHubAndMinecraftAccessFromModrinth()
	{
		// Not interchangeable: United Minecraft is not published on Modrinth at all, so a single fetch path
		// would silently fail to find it.
		Assert.Equal(MinecraftModOrigin.GitHubRelease,
			MinecraftSuite.AccessModFor(MinecraftSuite.UnitedMinecraftId).Origin);
		Assert.Equal(MinecraftModOrigin.Modrinth,
			MinecraftSuite.AccessModFor(MinecraftSuite.MinecraftAccessId).Origin);
		Assert.Equal(MinecraftModOrigin.Modrinth, MinecraftSuite.FabricApi.Origin);
	}

	// ---------------------------------------------------------------------
	// Whether a GitHub release is for a given Minecraft version
	// ---------------------------------------------------------------------

	private static GitHubRelease Release(string tag, params string[] assets) =>
		new(tag, assets.Select(a => new GitHubAsset(a, "https://example.invalid/" + a, 1)).ToList());

	[Fact]
	public void AReleaseTaggedForTheVersionCounts() =>
		Assert.True(MinecraftSuite.ReleaseIsForGameVersion(Release("v1.2.0-mc26.3"), "26.3"));

	[Fact]
	public void AReleaseWhoseJarNamesTheVersionCounts() =>
		Assert.True(MinecraftSuite.ReleaseIsForGameVersion(Release("v1.2.0", "united-minecraft-1.2.0-mc26.3.jar"), "26.3"));

	[Fact]
	public void AReleaseForAnotherVersionDoesNot() =>
		Assert.False(MinecraftSuite.ReleaseIsForGameVersion(Release("v1.1.0-mc26.2", "um-26.2.jar"), "26.3"));

	[Fact]
	public void ALongerVersionIsNotMistakenForTheOneAskedAbout()
	{
		// The reason this is matched as a version and not as text. Reading 26.20 as support for 26.2 would move a
		// player onto a Minecraft version their accessibility mod has no build for — a game that never speaks.
		Assert.False(MinecraftSuite.ReleaseIsForGameVersion(Release("v2.0.0-mc26.20"), "26.2"));
		Assert.False(MinecraftSuite.ReleaseIsForGameVersion(Release("v2.0.0-mc1.26.2"), "26.2"));
	}

	[Fact]
	public void NothingToReadMeansNo()
	{
		Assert.False(MinecraftSuite.ReleaseIsForGameVersion(null, "26.3"));
		Assert.False(MinecraftSuite.ReleaseIsForGameVersion(Release("v1.2.0", "united-minecraft.jar"), "26.3"));
	}
}
