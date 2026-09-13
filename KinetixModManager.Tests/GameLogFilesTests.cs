using System;
using System.IO;
using System.Text;
using System.Threading;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="GameLogFiles"/>, which answers "did my mods actually load?".
///
/// That question is the reason this code exists. The characteristic failure of a modded game for a blind
/// player is not a crash but silence — the game starts, plays perfectly, and never speaks, because the
/// accessibility mod did not load. A vanilla launch and a broken modded one are indistinguishable from
/// outside, and the log is the only evidence either way.
/// </summary>
public class GameLogFilesTests
{
	private static GameProfile Game(string id) => GameProfiles.Require(id);

	[Fact]
	public void EveryGameWithALoaderLogNamesTheFileItWrites()
	{
		foreach (string id in GameProfiles.AllIds)
		{
			GameProfile game = Game(id);
			if (!GameLogFiles.HasLoaderLog(game)) continue;

			Assert.False(string.IsNullOrWhiteSpace(game.LoaderLogFileName),
				$"{game.DisplayName} is shown in the Log tab but names no log file.");
		}
	}

	[Fact]
	public void StardewIsNotALoaderLogGame()
	{
		// Deliberate: SMAPI's log is richer and has its own screen, so it must not also appear as a plain
		// Log tab. Two places showing the same thing differently is worse than one.
		Assert.False(GameLogFiles.HasLoaderLog(Game(GameProfiles.StardewValley)));
	}

	[Fact]
	public void TheBethesdaGamesLogUnderTheirOwnScriptExtenderFolder()
	{
		string skyrim = GameLogFiles.LoaderLogFolder(Game(GameProfiles.SkyrimSE), TestPaths.Under('D', "Skyrim"));
		string fallout = GameLogFiles.LoaderLogFolder(Game(GameProfiles.Fallout4), TestPaths.Under('D', "Fallout 4"));

		Assert.EndsWith("SKSE", skyrim);
		Assert.EndsWith("F4SE", fallout);
	}

	[Fact]
	public void MoonlightPeaksLogsInsideBepInEx()
	{
		string game = TestPaths.Under('D', "Moonlight Peaks");

		Assert.Equal(Path.Combine(game, "BepInEx"),
			GameLogFiles.LoaderLogFolder(Game(GameProfiles.MoonlightPeaks), game));
	}

	[Fact]
	public void TheWitcherLogsBesideItsExecutableBecauseItKeepsNoLogOfItsOwn()
	{
		string game = TestPaths.Under('D', "The Witcher 3");

		Assert.Equal(Path.Combine(game, "bin", "x64"),
			GameLogFiles.LoaderLogFolder(Game(GameProfiles.Witcher3), game));
	}

	[Fact]
	public void MinecraftLogsBesideItsLauncherRatherThanUnderAGameFolder()
	{
		// The one game whose logs do not follow from the game folder, which is why the root is passed in.
		string root = TestPaths.Under('C', "mc");

		Assert.Equal(Path.Combine(root, "logs"),
			GameLogFiles.LoaderLogFolder(Game(GameProfiles.Minecraft), gameFolder: "", minecraftRoot: root));
	}

	[Fact]
	public void NoGameFolderMeansNoLogFolderRatherThanARootedGuess()
	{
		// A half-built path here would send the user to a folder that is not theirs, or to "/logs".
		Assert.Equal("", GameLogFiles.LoaderLogFolder(Game(GameProfiles.MoonlightPeaks), ""));
		Assert.Equal("", GameLogFiles.LoaderLogFolder(Game(GameProfiles.Witcher3), ""));
		Assert.Equal("", GameLogFiles.LoaderLogPath(Game(GameProfiles.MoonlightPeaks), ""));
	}

	[Fact]
	public void ALogStillBeingWrittenToCanBeRead()
	{
		// The whole point. SMAPI holds its log open for the entire session, and File.ReadAllLines opens with
		// FileShare.Read and throws — so the log looked empty at exactly the moment it was wanted.
		string path = Path.Combine(Path.GetTempPath(), "kinetix-log-" + Guid.NewGuid().ToString("N") + ".txt");

		using (var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
		using (var text = new StreamWriter(writer, Encoding.UTF8) { AutoFlush = true })
		{
			text.WriteLine("[12:00:00 INFO  SMAPI] Mods loaded");
			text.WriteLine("[12:00:01 ERROR SMAPI] Something went wrong");

			// Read it while the handle above is still open, which is the case that used to throw.
			string[] lines = GameLogFiles.ReadAllLinesShared(path);
			Assert.Equal(2, lines.Length);
			Assert.Contains("Mods loaded", lines[0]);

			Assert.Contains("went wrong", GameLogFiles.ReadAllTextShared(path));
		}

		try { File.Delete(path); } catch { }
	}

	[Fact]
	public void TheLoaderLogPathIsTheFolderPlusTheGamesOwnFileName()
	{
		GameProfile skyrim = Game(GameProfiles.SkyrimSE);
		string game = TestPaths.Under('D', "Skyrim");

		Assert.Equal(
			Path.Combine(GameLogFiles.LoaderLogFolder(skyrim, game), skyrim.LoaderLogFileName),
			GameLogFiles.LoaderLogPath(skyrim, game));
	}
}
