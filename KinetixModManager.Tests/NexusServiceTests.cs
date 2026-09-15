using System;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers the network-free half of <see cref="NexusService"/> — which game it is talking about, and whether
/// it should be talking at all.
///
/// <para>
/// The first tests this class has ever had. It was 1,387 lines in the WinForms project, so the test project
/// could not reference it however portable it was — and it turned out to be entirely portable, moving to the
/// core without a single line changing. These cover the part that decides, which is also the part that has
/// caused real defects: <see cref="NexusService.UsesNexus"/> answering wrongly is how Minecraft players kept
/// being sent to log in to a service their game has no relationship with.
/// </para>
/// </summary>
public class NexusServiceTests
{
	private static NexusService For(string game) =>
		new(new AppSettings { ActiveGame = game });

	// ---------------------------------------------------------------------
	// Which game it is talking about
	// ---------------------------------------------------------------------

	[Fact]
	public void EachNexusGameKnowsItsOwnDomain()
	{
		// The domain is in the URL of every call. Getting it wrong asks Nexus about somebody else's mod and
		// gets a perfectly successful answer about the wrong thing.
		Assert.Equal("stardewvalley", For(GameProfiles.StardewValley).CurrentGameDomain);
		Assert.Equal("skyrimspecialedition", For(GameProfiles.SkyrimSE).CurrentGameDomain);
		Assert.Equal("fallout4", For(GameProfiles.Fallout4).CurrentGameDomain);
	}

	[Fact]
	public void NoGameLoadedStillAnswersSomething()
	{
		// Parts of the interface read this before a session exists, and it has always resolved to Stardew.
		Assert.False(string.IsNullOrEmpty(For(GameProfiles.NoGame).CurrentGameDomain));
		Assert.False(string.IsNullOrEmpty(For("a game that does not exist").CurrentGameDomain));
	}

	[Fact]
	public void EveryNexusGameHasBothADomainAndAnId()
	{
		// A game that carries one and not the other builds a malformed URL for half its calls.
		foreach (GameProfile game in GameProfiles.All.Where(g => g.ModSource == ModSource.Nexus))
		{
			var service = For(game.Id);
			Assert.False(string.IsNullOrWhiteSpace(service.CurrentGameDomain), game.DisplayName + " has no Nexus domain");
			Assert.False(string.IsNullOrWhiteSpace(service.CurrentGameId), game.DisplayName + " has no Nexus game id");
		}
	}

	// ---------------------------------------------------------------------
	// Whether it should be talking at all
	// ---------------------------------------------------------------------

	[Fact]
	public void MinecraftDoesNotUseNexus()
	{
		// The check every game-scoped call has to make. Without it a Minecraft request names no game at all —
		// api.nexusmods.com/v1/games//mods/123.json — and, worse, the interface asks the user for a key their
		// game will never need. That was a live defect twice over: the mod list and the Discovery tab.
		Assert.False(For(GameProfiles.Minecraft).UsesNexus);
	}

	[Theory]
	[InlineData(GameProfiles.StardewValley)]
	[InlineData(GameProfiles.SkyrimSE)]
	[InlineData(GameProfiles.Fallout4)]
	[InlineData(GameProfiles.Witcher3)]
	[InlineData(GameProfiles.MoonlightPeaks)]
	public void TheOtherFiveDo(string game)
	{
		Assert.True(For(game).UsesNexus);
	}

	[Fact]
	public void AnUnknownGameCountsAsANexusGame()
	{
		// Deliberate: the fallback has always been Stardew, and an unknown game is far more likely to be one
		// the settings file remembers from a newer build than a game with some other catalogue.
		Assert.True(For("something unrecognised").UsesNexus);
	}

	// ---------------------------------------------------------------------
	// Connection state
	// ---------------------------------------------------------------------

	[Fact]
	public void AFreshServiceIsNotValidated()
	{
		Assert.False(For(GameProfiles.StardewValley).IsValidated);
	}

	[Fact]
	public void DisconnectingClearsTheValidatedKey()
	{
		var settings = new AppSettings { ActiveGame = GameProfiles.StardewValley, ApiKey = "abc123" };
		var service = new NexusService(settings);

		service.Disconnect();

		Assert.False(service.IsValidated);
	}

	[Fact]
	public void RateLimitsAreUnknownUntilNexusHasSaidSomething()
	{
		// -1 rather than 0, because "we have not asked yet" and "you have none left" are very different
		// things to report to somebody about to download twenty mods.
		var service = For(GameProfiles.StardewValley);

		Assert.False(service.HasRateLimitInfo);
		Assert.Equal(-1, service.HourlyRemaining);
		Assert.Equal(-1, service.DailyRemaining);
	}

	// ---------------------------------------------------------------------
	// Versions
	// ---------------------------------------------------------------------

	[Fact]
	public void AVersionIsTidiedIntoSomethingTheApiWillAccept()
	{
		// A four-part file version is what .NET reports for SMAPI's own DLL, and sending it unchanged fails
		// the whole request rather than that one mod.
		Assert.Equal("4.1.10", NexusService.SanitizeModVersion("4.1.10.0"));
		Assert.Equal("1.0.0", NexusService.SanitizeModVersion("v1.0.0"));
	}

	[Fact]
	public void TheAppVersionIsReadableAndNotEmpty()
	{
		// It goes into the User-Agent of every request, and Modrinth rejects a blank one outright — which is
		// how the manager once stopped being able to search at all.
		Assert.False(string.IsNullOrWhiteSpace(NexusService.AppVersion));
	}
}
