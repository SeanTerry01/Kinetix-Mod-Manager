using System;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Guards <see cref="GameProfile.AccessModSpeaksOnLinux"/> — which games actually talk when played on Linux.
///
/// <para>
/// This is the most consequential sentence the Linux build says, and it is a fact about the games rather
/// than about the manager. Minecraft Access speaks through speech-dispatcher and Stardew Access supports
/// Linux natively. Skyrim, Fallout 4, The Witcher 3 and Moonlight Peaks all drive NVDA or JAWS, and neither
/// exists inside a Proton prefix — so the game loads its accessibility mod, starts, plays perfectly and
/// never says a word.
/// </para>
///
/// <para>
/// Their mods are still managed, and managing them works. What does not is the game talking, and a blind
/// user has no way to tell that apart from a broken install — which is the exact failure this manager exists
/// to prevent. So the flag is asserted here per game rather than left as a comment somewhere: getting it
/// wrong in the optimistic direction sends somebody to install a mod for an evening of silence.
/// </para>
/// </summary>
public class LinuxSpeechGateTests
{
	[Theory]
	[InlineData(GameProfiles.Minecraft)]
	[InlineData(GameProfiles.StardewValley)]
	public void TheseTwoSpeakOnLinux(string game)
	{
		Assert.True(GameProfiles.Require(game).AccessModSpeaksOnLinux);
	}

	[Theory]
	[InlineData(GameProfiles.SkyrimSE)]
	[InlineData(GameProfiles.Fallout4)]
	[InlineData(GameProfiles.Witcher3)]
	[InlineData(GameProfiles.MoonlightPeaks)]
	public void TheseFourDoNot(string game)
	{
		// Changing one of these to true is a claim that its accessibility mod has stopped needing a Windows
		// screen reader. That would be excellent news and should come with a link, not a green test.
		Assert.False(GameProfiles.Require(game).AccessModSpeaksOnLinux);
	}

	[Fact]
	public void EveryGameHasAnAnswerAndTwoOfThemAreYes()
	{
		// A new game added without thinking about this defaults to "will not speak", which is the safe way
		// round: the warning appears, somebody notices it is wrong, and it gets corrected. The reverse would
		// be a silent game and no warning.
		Assert.Equal(2, GameProfiles.All.Count(g => g.AccessModSpeaksOnLinux));
	}

	[Fact]
	public void ManagingModsIsNotGatedByIt()
	{
		// The distinction the whole flag rests on: every game's mods are managed on Linux, whether or not the
		// game will talk. Each one knows where its mods go — under the game folder, or in a staging folder the
		// manager owns — and under Proton the game folder is an ordinary directory in steamapps/common, because
		// Proton changes how a game runs and not where it lives.
		foreach (GameProfile game in GameProfiles.All)
		{
			bool knowsWhereModsGo =
				!string.IsNullOrEmpty(game.ModsFolderFor("/games/whatever")) ||
				!string.IsNullOrEmpty(game.StagingFolderName) ||
				game.IsMinecraft;

			Assert.True(knowsWhereModsGo, game.DisplayName + " has nowhere to put mods.");
		}
	}
}
