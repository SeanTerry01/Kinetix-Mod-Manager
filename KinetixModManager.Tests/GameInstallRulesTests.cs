using System.Collections.Generic;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Which copy of a game the manager is talking about.
///
/// <para>
/// Someone can own Skyrim twice — the Steam copy and the GOG one — and the install key is what separates them.
/// It selects the mods folder, the deployment manifest, the backups and the save backups, so a rule that answers
/// with the wrong copy points all of those at the wrong game folder.
/// </para>
///
/// <para>
/// A mutation campaign sorted the primary copy last, reported a single copy as several, and made a lookup by key
/// return a copy that was not asked for. All three passed the whole suite.
/// </para>
/// </summary>
public class GameInstallRulesTests
{
	private static GameInstall Install(string key, string gameId, GamePlatform platform = GamePlatform.Unknown) =>
		new GameInstall { Key = key, GameId = gameId, Platform = platform };

	/// <summary>A first copy keyed bare, a GOG second copy, and an unrelated game — the realistic shape.</summary>
	private static List<GameInstall> TwoCopiesOfSkyrimAndOneFallout() => new()
	{
		Install(GameProfiles.SkyrimSE + "@Gog", GameProfiles.SkyrimSE, GamePlatform.Gog),
		Install(GameProfiles.SkyrimSE, GameProfiles.SkyrimSE, GamePlatform.Steam),
		Install(GameProfiles.Fallout4, GameProfiles.Fallout4, GamePlatform.Steam),
	};

	// ------------------------------------------------------------------ finding and ordering ----

	[Fact]
	public void OnlyTheAskedForGamesCopiesComeBack()
	{
		List<GameInstall> found = GameInstallRules.Of(TwoCopiesOfSkyrimAndOneFallout(), GameProfiles.SkyrimSE);

		Assert.Equal(2, found.Count);
		Assert.All(found, i => Assert.Equal(GameProfiles.SkyrimSE, i.GameId));
	}

	[Fact]
	public void ThePrimaryCopyIsOfferedFirst()
	{
		// The bare-keyed copy is the one the manager had before any second copy existed. Sorting it after the
		// suffixed copies changes which install the games menu offers by default.
		List<GameInstall> found = GameInstallRules.Of(TwoCopiesOfSkyrimAndOneFallout(), GameProfiles.SkyrimSE);

		Assert.Equal(GameProfiles.SkyrimSE, found[0].Key);
		Assert.Equal(GameProfiles.SkyrimSE + "@Gog", found[1].Key);
	}

	[Fact]
	public void CopiesAfterThePrimaryAreOrderedByKey_NotByWhenTheyWereFound()
	{
		var installs = new List<GameInstall>
		{
			Install(GameProfiles.SkyrimSE + "@Steam", GameProfiles.SkyrimSE, GamePlatform.Steam),
			Install(GameProfiles.SkyrimSE + "@Gog", GameProfiles.SkyrimSE, GamePlatform.Gog),
			Install(GameProfiles.SkyrimSE, GameProfiles.SkyrimSE),
		};

		Assert.Equal(
			new[] { GameProfiles.SkyrimSE, GameProfiles.SkyrimSE + "@Gog", GameProfiles.SkyrimSE + "@Steam" },
			GameInstallRules.Of(installs, GameProfiles.SkyrimSE).Select(i => i.Key));
	}

	[Fact]
	public void AnInstallKeyAsksTheSameQuestionAsABareGameId()
	{
		// Callers hold an install key far more often than a bare id, and both must find the game's copies.
		Assert.Equal(2,
			GameInstallRules.Of(TwoCopiesOfSkyrimAndOneFallout(), GameProfiles.SkyrimSE + "@Gog").Count);
	}

	[Fact]
	public void AGameWithNoCopiesRecordedComesBackEmptyRatherThanNull()
	{
		Assert.Empty(GameInstallRules.Of(TwoCopiesOfSkyrimAndOneFallout(), GameProfiles.Witcher3));
	}

	// ------------------------------------------------------------------------ lookup by key ----

	[Fact]
	public void ACopyIsFoundByItsOwnKey()
	{
		GameInstall? found = GameInstallRules.WithKey(
			TwoCopiesOfSkyrimAndOneFallout(), GameProfiles.SkyrimSE + "@Gog");

		Assert.NotNull(found);
		Assert.Equal(GamePlatform.Gog, found!.Platform);
	}

	[Fact]
	public void ThePrimaryCopyIsFoundByItsBareKey()
	{
		GameInstall? found = GameInstallRules.WithKey(TwoCopiesOfSkyrimAndOneFallout(), GameProfiles.SkyrimSE);

		Assert.NotNull(found);
		Assert.Equal(GamePlatform.Steam, found!.Platform);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void NoKeyMeansNoCopy_NotTheFirstOneInTheList(string? key)
	{
		// Falling back to the first recorded copy would silently manage the wrong install.
		Assert.Null(GameInstallRules.WithKey(TwoCopiesOfSkyrimAndOneFallout(), key));
	}

	[Fact]
	public void AKeyThatIsNotRecordedFindsNothing()
	{
		Assert.Null(GameInstallRules.WithKey(TwoCopiesOfSkyrimAndOneFallout(), GameProfiles.SkyrimSE + "@Epic"));
	}

	// -------------------------------------------------------------------- more than one copy ----

	[Fact]
	public void TwoCopiesCountAsMultiple()
	{
		Assert.True(GameInstallRules.HasMultipleCopies(TwoCopiesOfSkyrimAndOneFallout(), GameProfiles.SkyrimSE));
	}

	[Fact]
	public void OneCopyIsNotMultiple()
	{
		// This is the condition that decides whether the platform is said out loud at all. Getting it wrong adds
		// "(Steam)" to every menu entry and every report for people who own one copy of each game.
		Assert.False(GameInstallRules.HasMultipleCopies(TwoCopiesOfSkyrimAndOneFallout(), GameProfiles.Fallout4));
	}

	[Fact]
	public void NoCopiesAtAllIsNotMultiple()
	{
		Assert.False(GameInstallRules.HasMultipleCopies(TwoCopiesOfSkyrimAndOneFallout(), GameProfiles.Witcher3));
	}

	// ------------------------------------------------------------------------- display name ----

	[Fact]
	public void ACopyIsNamedWithoutItsPlatformWhenTheCallerDoesNotWantOne()
	{
		Assert.Equal("Skyrim Special Edition",
			Install(GameProfiles.SkyrimSE, GameProfiles.SkyrimSE, GamePlatform.Gog).DisplayName(withPlatform: false));
	}

	[Fact]
	public void ACopyIsNamedWithItsPlatformWhenAsked()
	{
		Assert.Equal("Skyrim Special Edition (GOG)",
			Install(GameProfiles.SkyrimSE, GameProfiles.SkyrimSE, GamePlatform.Gog).DisplayName(withPlatform: true));
	}

	[Fact]
	public void ACopyWhoseStoreIsUnknownIsNamedPlainly_NotWithEmptyBrackets()
	{
		// An unknown platform has no display name, and "Skyrim Special Edition ()" is what asking for one anyway
		// produces.
		Assert.Equal("Skyrim Special Edition",
			Install(GameProfiles.SkyrimSE, GameProfiles.SkyrimSE).DisplayName(withPlatform: true));
	}
}
