using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Reading whether the last run of Minecraft actually had its mods.
///
/// This is the check that exists because of a real evening lost to it: Fabric, Fabric API and the
/// accessibility mod were all installed correctly, and the game still said nothing, because the launcher had
/// started the vanilla profile. Nothing anywhere reported that. Both fixtures below are the opening lines of
/// real logs from that machine - the modded run, and the vanilla one that caused the problem.
/// </summary>
public class MinecraftLaunchLogTests
{
	private static readonly string[] ModdedLog =
	{
		"[00:24:49] [main/INFO]: Loading Minecraft 26.2 with Fabric Loader 0.19.5",
		"[00:24:49] [main/INFO]: Mappings not present!",
		"[00:24:49] [main/INFO]: Loading 51 mods:",
		"\t- fabric-api 0.160.0+26.2",
		"\t   |-- fabric-api-base 2.0.4+ece063239e",
		"\t   |-- fabric-biome-api-v1 18.0.6+c7bd5b8e9e",
		// The LAST child under a parent uses a different corner. Copied from a real log, and the reason this
		// fixture is worth having verbatim: written from memory it would have used "|--" throughout and hidden
		// the bug below entirely.
		"\t   \\-- fabric-transitive-access-wideners-v1 8.1.4+67c847259e",
		"\t- fabricloader 0.19.5",
		"\t- minecraft 26.2",
		"\t- united_minecraft 1.1.0",
		"[00:24:52] [Render thread/INFO]: Setting user: SeanTerry01",
	};

	// The run that started all of this. Note what it does NOT contain.
	private static readonly string[] VanillaLog =
	{
		"[15:40:40] [Datafixer Bootstrap #0/INFO]: 295 Datafixer optimizations took 602 milliseconds",
		"[15:40:49] [Render thread/INFO]: Setting user: SeanTerry01",
		"[15:40:49] [Render thread/INFO]: Backend library: LWJGL version 3.4.1+2",
	};

	[Fact]
	public void AModdedRunIsRecognisedWithItsVersionsAndCount()
	{
		MinecraftLaunchOutcome outcome = MinecraftLaunchLog.Parse(ModdedLog);

		Assert.True(outcome.FabricLoaded);
		Assert.Equal("26.2", outcome.GameVersion);
		Assert.Equal("0.19.5", outcome.LoaderVersion);
		Assert.Equal(51, outcome.ModCount);
	}

	[Fact]
	public void AVanillaRunIsRecognisedAsHavingNoMods()
	{
		// The whole point. From outside these two runs are indistinguishable - the game starts, plays
		// perfectly, and is silent - and only the log tells them apart.
		MinecraftLaunchOutcome outcome = MinecraftLaunchLog.Parse(VanillaLog);

		Assert.False(outcome.FabricLoaded);
		Assert.Equal("", outcome.GameVersion);
		Assert.Equal(-1, outcome.ModCount);
	}

	[Fact]
	public void TheModListIsReadIncludingNestedLibraries()
	{
		MinecraftLaunchOutcome outcome = MinecraftLaunchLog.Parse(ModdedLog);

		Assert.Contains("fabric-api", outcome.ModIds);
		Assert.Contains("united_minecraft", outcome.ModIds);
		// Nested entries are listed differently and still count as loaded.
		Assert.Contains("fabric-api-base", outcome.ModIds);
	}

	[Fact]
	public void TheLastNestedModUnderAParentIsNotLost()
	{
		// ⚠️ A real bug, and one that had been written off as an inherent limitation. The loader draws the mod
		// list as a tree, and the last child under a parent uses "\--" where the others use "|--". Reading only
		// "|--" silently dropped three of the fifty-one mods a real log declared - each of them the last child
		// of its parent - and the shortfall was documented as "best effort" rather than investigated.
		MinecraftLaunchOutcome outcome = MinecraftLaunchLog.Parse(ModdedLog);

		Assert.Contains("fabric-transitive-access-wideners-v1", outcome.ModIds);
	}

	[Fact]
	public void EveryModTheLoaderListedIsRead()
	{
		// The exact set rather than a count: this catches reading too FEW (the "\--" bug) and too many (a
		// terminator that fails and swallows ordinary log lines) in one assertion, and a bare number would
		// have to be edited every time the fixture grows without ever saying what changed.
		MinecraftLaunchOutcome outcome = MinecraftLaunchLog.Parse(ModdedLog);

		Assert.Equal(new[]
		{
			"fabric-api",
			"fabric-api-base",
			"fabric-biome-api-v1",
			"fabric-transitive-access-wideners-v1",
			"fabricloader",
			"minecraft",
			"united_minecraft",
		}, outcome.ModIds);
	}

	[Fact]
	public void TheModListStopsAtTheNextLogLine()
	{
		// Without this the parser would keep swallowing ordinary log lines as if they were mods.
		MinecraftLaunchOutcome outcome = MinecraftLaunchLog.Parse(ModdedLog);

		Assert.DoesNotContain(outcome.ModIds, id => id.Contains("Setting"));
		Assert.DoesNotContain(outcome.ModIds, id => id.Contains("Render"));
	}

	[Fact]
	public void FabricWithNoModsInstalledIsStillAModdedRun()
	{
		// A different situation from a vanilla launch, and the fix for each is different: this one needs mods
		// installing, the other needs the game starting differently. Judged on the loading line, not the count.
		MinecraftLaunchOutcome outcome = MinecraftLaunchLog.Parse(new[]
		{
			"[00:24:49] [main/INFO]: Loading Minecraft 26.2 with Fabric Loader 0.19.5",
			"[00:24:49] [main/INFO]: Loading 0 mods:",
		});

		Assert.True(outcome.FabricLoaded);
		Assert.Equal(0, outcome.ModCount);
	}

	[Fact]
	public void AnEmptyLogSaysNothingRatherThanClaimingVanilla()
	{
		// A truncated or still-opening log must not be read as "your mods did not load" - that would send the
		// user chasing a problem that is not there.
		MinecraftLaunchOutcome outcome = MinecraftLaunchLog.Parse(System.Array.Empty<string>());

		Assert.False(outcome.FabricLoaded);
		Assert.Empty(outcome.ModIds);
	}
}
