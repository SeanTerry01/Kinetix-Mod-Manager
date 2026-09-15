using System;
using System.Collections.Generic;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="GamesView"/> — the games list, and the sentence each row says.
///
/// <para>
/// The warning about a game that will not speak used to live only on the Settings screen. Settings is
/// somewhere a user goes once; the games list is where they choose what to spend an evening on, so that is
/// where it has to be heard. Being told afterwards that the game was never going to talk is being told too
/// late.
/// </para>
/// </summary>
public class GamesViewTests
{
	static GamesViewTests() => Loc.Init("en");

	private static IReadOnlyList<GameRow> Rows(bool onLinux, params string[] installed)
	{
		var found = new HashSet<string>(installed, StringComparer.OrdinalIgnoreCase);
		return GamesView.Of(
			GameProfiles.All,
			g => found.Contains(g.Id) ? "/games/" + g.Id : null,
			warnWhereItWillNotSpeak: onLinux);
	}

	[Fact]
	public void EverySupportedGameIsListedWhetherOrNotItIsInstalled()
	{
		// A user who cannot see an empty list has no way to tell "not supported" from "not installed yet",
		// and the first is a reason to give up on the program.
		Assert.Equal(GameProfiles.All.Count, Rows(onLinux: true).Count);
	}

	[Fact]
	public void AnUninstalledGameSaysSo()
	{
		GameRow row = Rows(onLinux: true).Single(r => r.Game.Id == GameProfiles.SkyrimSE);

		Assert.False(row.IsInstalled);
		Assert.Contains("not installed", row.Spoken);
	}

	[Fact]
	public void AnInstalledGameNamesWhereItIs()
	{
		GameRow row = Rows(onLinux: true, GameProfiles.StardewValley)
			.Single(r => r.Game.Id == GameProfiles.StardewValley);

		Assert.True(row.IsInstalled);
		Assert.Contains("/games/" + GameProfiles.StardewValley, row.Spoken);
	}

	// ---------------------------------------------------------------------
	// The warning
	// ---------------------------------------------------------------------

	[Fact]
	public void AGameThatWillNotSpeakSaysSoOnItsOwnRow()
	{
		GameRow row = Rows(onLinux: true, GameProfiles.SkyrimSE).Single(r => r.Game.Id == GameProfiles.SkyrimSE);

		Assert.True(row.WillNotSpeak);
		Assert.Contains("will not speak", row.Spoken);
	}

	[Fact]
	public void TheWarningSaysTheModsAreStillManaged()
	{
		// Otherwise it reads as "this game is not supported", which is not what it means and would send
		// somebody away from a game the manager handles perfectly well.
		GameRow row = Rows(onLinux: true, GameProfiles.Fallout4).Single(r => r.Game.Id == GameProfiles.Fallout4);

		Assert.Contains("still managed", row.Spoken);
	}

	[Theory]
	[InlineData(GameProfiles.Minecraft)]
	[InlineData(GameProfiles.StardewValley)]
	public void TheTwoThatDoSpeakGetNoWarning(string game)
	{
		GameRow row = Rows(onLinux: true).Single(r => r.Game.Id == game);

		Assert.False(row.WillNotSpeak);
		Assert.DoesNotContain("will not speak", row.Spoken);
	}

	[Fact]
	public void NobodyIsWarnedOnWindows()
	{
		// Every supported game speaks there, so the note on all six rows would be noise — and worse, wrong.
		Assert.All(Rows(onLinux: false), r => Assert.False(r.WillNotSpeak));
	}

	// ---------------------------------------------------------------------
	// The summary, and not falling over
	// ---------------------------------------------------------------------

	[Fact]
	public void TheSummarySaysHowManyWereFound()
	{
		string none = GamesView.Summarise(Rows(onLinux: true));
		string some = GamesView.Summarise(Rows(onLinux: true, GameProfiles.Minecraft, GameProfiles.StardewValley));

		Assert.NotEqual(none, some);
		Assert.Contains("None of them", none);
		Assert.Contains("2", some);
	}

	[Fact]
	public void OneGameThatCannotBeLookedForDoesNotCostTheWholeList()
	{
		// A library on a disconnected drive throws rather than answering, and the list still has to be built.
		IReadOnlyList<GameRow> rows = GamesView.Of(
			GameProfiles.All,
			g => g.Id == GameProfiles.SkyrimSE ? throw new UnauthorizedAccessException("drive gone") : null,
			warnWhereItWillNotSpeak: true);

		Assert.Equal(GameProfiles.All.Count, rows.Count);
		Assert.False(rows.Single(r => r.Game.Id == GameProfiles.SkyrimSE).IsInstalled);
	}
}
