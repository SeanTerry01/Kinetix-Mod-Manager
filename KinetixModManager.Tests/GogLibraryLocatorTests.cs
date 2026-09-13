using System;
using System.Collections.Generic;
using System.IO;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="GogLibraryLocator"/>, which finds a GOG game by looking for its executable when GOG's own
/// registry entry can't answer — the counterpart to reading Steam's library records.
///
/// Searching by content rather than by folder name is the point of it: GOG's naming doesn't match Steam's, it
/// differs between a game and its Game of the Year edition, and it drops the punctuation a path can't hold. A
/// guessed name would fail silently, which for a blind user looks exactly like the game not being installed.
/// </summary>
public class GogLibraryLocatorTests
{
	[Fact]
	public void FindGameFolder_FindsAGameByItsExecutableWhateverTheFolderIsCalled()
	{
		string root = NewFolder();
		string game = CreateGame(root, "The Elder Scrolls V Skyrim Special Edition", "SkyrimSE.exe");

		Assert.Equal(game, GogLibraryLocator.FindGameFolder(new[] { root }, "SkyrimSE.exe"));
	}

	[Fact]
	public void FindGameFolder_LooksInTheRootItselfAsWellAsTheFoldersInside()
	{
		// Someone who installed a single game straight into their library root.
		string root = NewFolder();
		File.WriteAllText(Path.Combine(root, "Stardew Valley.exe"), "");

		Assert.Equal(root, GogLibraryLocator.FindGameFolder(new[] { root }, "Stardew Valley.exe"));
	}

	[Fact]
	public void FindGameFolder_SearchesEveryRootItIsGiven()
	{
		string empty = NewFolder();
		string real = NewFolder();
		string game = CreateGame(real, "Fallout 4 GOTY", "Fallout4.exe");

		Assert.Equal(game, GogLibraryLocator.FindGameFolder(new[] { empty, real }, "Fallout4.exe"));
	}

	[Fact]
	public void FindGameFolder_KeepsLookingPastARootThatDoesNotExist()
	{
		// A drive that has no GOG Games folder is the normal case, not a reason to give up.
		string real = NewFolder();
		string game = CreateGame(real, "Stardew Valley", "Stardew Valley.exe");

		string?[] roots = { @"Z:\nothing here", real };

		Assert.Equal(game, GogLibraryLocator.FindGameFolder(roots!, "Stardew Valley.exe"));
	}

	[Fact]
	public void FindGameFolder_DoesNotDescendIntoAGamesOwnSubfolders()
	{
		// Only one level down: a game's data folders would turn a quick check into a disk crawl, and a stray
		// copy of an executable in a subfolder is not the install.
		string root = NewFolder();
		string deep = Path.Combine(root, "Some Game", "Data", "Tools");
		Directory.CreateDirectory(deep);
		File.WriteAllText(Path.Combine(deep, "SkyrimSE.exe"), "");

		Assert.Null(GogLibraryLocator.FindGameFolder(new[] { root }, "SkyrimSE.exe"));
	}

	[Fact]
	public void FindGameFolder_ReturnsNothingWhenTheGameIsNotThere()
	{
		string root = NewFolder();
		CreateGame(root, "A Different Game", "Something.exe");

		Assert.Null(GogLibraryLocator.FindGameFolder(new[] { root }, "Fallout4.exe"));
		Assert.Null(GogLibraryLocator.FindGameFolder(new[] { root }, ""));
		Assert.Null(GogLibraryLocator.FindGameFolder(Array.Empty<string>(), "Fallout4.exe"));
	}

	// -------------------------------------------------------------------------
	// Where it looks by default
	// -------------------------------------------------------------------------

	[Fact]
	public void DefaultRoots_LooksForAGogGamesFolderOnEveryDrive()
	{
		// A GOG library that outgrew the system drive is ordinary, so every fixed drive is worth a look.
		List<string> roots = GogLibraryLocator.DefaultRoots(
			null, new[] { TestPaths.DriveRoot('C'), TestPaths.DriveRoot('D') });

		Assert.Contains(TestPaths.Under('C', "GOG Games"), roots);
		Assert.Contains(TestPaths.Under('D', "GOG Games"), roots);
	}

	[Fact]
	public void DefaultRoots_IncludesGalaxysOwnGamesFolderWhenGalaxyIsInstalled()
	{
		string galaxy = TestPaths.Under('C', "Program Files (x86)", "GOG Galaxy");

		List<string> roots = GogLibraryLocator.DefaultRoots(galaxy, new[] { TestPaths.DriveRoot('C') });

		Assert.Contains(Path.Combine(galaxy, "Games"), roots);
	}

	[Fact]
	public void DefaultRoots_CopesWithGalaxyNotBeingInstalledAtAll()
	{
		// Owning GOG games without ever installing Galaxy is a perfectly normal way to own them.
		List<string> roots = GogLibraryLocator.DefaultRoots(null, new[] { TestPaths.DriveRoot('C') });

		Assert.Single(roots);
		Assert.Equal(TestPaths.Under('C', "GOG Games"), roots[0]);
	}

	// -------------------------------------------------------------------------
	// Telling a GOG copy from a Steam one, and what that changes
	// -------------------------------------------------------------------------

	[Fact]
	public void IsGogInstall_RecognisesAGogCopyByTheFileItsInstallerLeaves()
	{
		string folder = NewFolder();
		File.WriteAllText(Path.Combine(folder, "goggame-1711230643.info"), "{}");

		Assert.True(GogLibraryLocator.IsGogInstall(folder, "1711230643"));
	}

	[Fact]
	public void IsGogInstall_IsNotFooledByAFolderNamedLikeAGogOne()
	{
		// Judged by contents, not by where it sits: a Steam copy moved into "GOG Games" is still a Steam copy.
		string folder = NewFolder();
		File.WriteAllText(Path.Combine(folder, "SkyrimSE.exe"), "");

		Assert.False(GogLibraryLocator.IsGogInstall(folder, "1711230643"));
		Assert.False(GogLibraryLocator.IsGogInstall(folder, null));
		Assert.False(GogLibraryLocator.IsGogInstall("", "1711230643"));
	}

	[Fact]
	public void UserDataFolder_DiffersBetweenTheGogAndSteamCopiesOfTheSameGame()
	{
		// The whole point of the distinction. A GOG copy of Skyrim keeps its INIs, saves and load order in
		// "Skyrim Special Edition GOG" — read out of the GOG executable itself. Using the Steam name would
		// manage the Steam install instead, which on a machine with both is worse than doing nothing.
		GameProfile skyrim = GameProfiles.Require(GameProfiles.SkyrimSE);

		string steamCopy = NewFolder();
		string gogCopy = NewFolder();
		File.WriteAllText(Path.Combine(gogCopy, $"goggame-{skyrim.GogProductId}.info"), "{}");

		Assert.Equal("Skyrim Special Edition", skyrim.UserDataFolderFor(steamCopy));
		Assert.Equal("Skyrim Special Edition GOG", skyrim.UserDataFolderFor(gogCopy));
	}

	[Fact]
	public void UserDataFolder_IsEmptyForAGameThatKeepsNoSuchFolder()
	{
		// Stardew Valley and Moonlight Peaks keep everything inside the game folder.
		Assert.Equal("", GameProfiles.Require(GameProfiles.StardewValley).UserDataFolderFor(NewFolder()));
		Assert.Equal("", GameProfiles.Require(GameProfiles.MoonlightPeaks).UserDataFolderFor(NewFolder()));
	}

	[Fact]
	public void UserDataFolder_FallsBackToTheStandardNameWhenTheFolderCannotBeChecked()
	{
		// An unset or missing game folder must not silently produce the GOG answer.
		GameProfile skyrim = GameProfiles.Require(GameProfiles.SkyrimSE);

		Assert.Equal("Skyrim Special Edition", skyrim.UserDataFolderFor(""));
		Assert.Equal("Skyrim Special Edition", skyrim.UserDataFolderFor(@"Z:\not a real folder"));
	}

	// -------------------------------------------------------------------------

	private static string NewFolder()
	{
		string path = Path.Combine(Path.GetTempPath(), "kmm-gog-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	private static string CreateGame(string root, string folderName, string exeName)
	{
		string folder = Path.Combine(root, folderName);
		Directory.CreateDirectory(folder);
		File.WriteAllText(Path.Combine(folder, exeName), "");
		return folder;
	}
}
