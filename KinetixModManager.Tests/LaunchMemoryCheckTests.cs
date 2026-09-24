using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// The memory check before a game starts — <see cref="LaunchMemoryCheck"/>. The figures are the real ones from the
/// development machine the day the accessibility pack died loading twice: 4 MB left for anyone, one program holding
/// 58 GB, and a browser that is a dozen processes.
/// </summary>
public class LaunchMemoryCheckTests
{
	private const long Mb = 1024L * 1024, Gb = 1024 * Mb;

	private static readonly MemoryUser[] ThatMorning =
	{
		new("com.docker.backend", 58178 * Mb),
		new("PocketTTSHost", 2564 * Mb),
		new("firefox", 1222 * Mb), new("firefox", 793 * Mb), new("firefox", 758 * Mb),
		new("devenv", 1130 * Mb),
		new("nvda", 1097 * Mb),
		new("Discord", 800 * Mb),
	};

	[Fact]
	public void WithRoomToSpareNothingIsNamedAndTheGameStarts()
	{
		MemoryVerdict verdict = LaunchMemoryCheck.Evaluate(40 * Gb, 6 * Gb, ThatMorning);

		Assert.True(verdict.Enough);
		Assert.Empty(verdict.Biggest);
	}

	[Fact]
	public void WhenMemoryIsShortTheBiggestProgramsAreNamedLargestFirst()
	{
		MemoryVerdict verdict = LaunchMemoryCheck.Evaluate(4 * Mb, 5632 * Mb, ThatMorning);

		Assert.False(verdict.Enough);
		Assert.Equal(new[] { "com.docker.backend", "firefox", "PocketTTSHost" }, verdict.Biggest.Select(b => b.Name));
	}

	[Fact]
	public void AProgramRunningAsManyProcessesIsCountedOnceWithTheirTotal()
	{
		MemoryVerdict verdict = LaunchMemoryCheck.Evaluate(0, Gb, ThatMorning);

		Assert.Equal((1222 + 793 + 758) * Mb, verdict.Biggest.Single(b => b.Name == "firefox").Bytes);
	}

	[Fact]
	public void ProgramsHoldingLessThanAGigabyteAreNotWorthNaming()
	{
		MemoryVerdict verdict = LaunchMemoryCheck.Evaluate(0, Gb, new[] { new MemoryUser("Discord", 800 * Mb), new MemoryUser("", 9 * Gb) });

		Assert.Empty(verdict.Biggest);
	}

	[Theory]
	[InlineData(4096, 4096 + 1536)]
	[InlineData(8192, 8192 + 1536)]
	[InlineData(0, 4096 + 1536)]   // left to Java: still asked for at least this much
	public void MinecraftNeedsItsOwnLimitAndRoomForJava(int limitMb, long neededMb) =>
		Assert.Equal(neededMb * Mb, LaunchMemoryCheck.NeededBytesFor(GameProfiles.Require(GameProfiles.Minecraft), limitMb));

	[Fact]
	public void EveryGameAsksForSomethingSensible()
	{
		foreach (GameProfile game in GameProfiles.All.Where(g => !g.IsMinecraft))
			Assert.InRange(game.MemoryNeededMb, 1024, 16384);

		// The ones people mod hardest ask for the most.
		Assert.True(GameProfiles.Require(GameProfiles.SkyrimSE).MemoryNeededMb >
					GameProfiles.Require(GameProfiles.StardewValley).MemoryNeededMb);
	}
}
