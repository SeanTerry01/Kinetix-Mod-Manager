using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="LinuxGameLocator"/> — finding a game on Linux.
///
/// <para>
/// The first tests either Linux project has ever had. Until now neither was even referenced by the test
/// project, which mattered more here than anywhere else: this is code whose entire job is to cope with
/// layouts the author does not have. Four places Steam installs itself, a Flatpak home, a library on a
/// second disk, a Proton prefix that only appears after the game has been run once — whichever of those a
/// developer happens to have is the only one that would ever be exercised by running the program.
/// </para>
///
/// <para>
/// So the fixture builds a Steam library in a temporary folder and points the locator at it. That is what
/// the <c>home</c> parameter on the constructor exists for; the app passes nothing and behaves exactly as
/// it did.
/// </para>
/// </summary>
public class LinuxGameLocatorTests : IDisposable
{
	private readonly string _home = Path.Combine(Path.GetTempPath(), "kinetix-home-" + Guid.NewGuid().ToString("N"));

	public LinuxGameLocatorTests() => Directory.CreateDirectory(_home);

	public void Dispose()
	{
		try { Directory.Delete(_home, true); } catch { }
	}

	private LinuxGameLocator Locator => new(_home);

	/// <summary>Builds a Steam root with a library folder, and returns the steamapps path.</summary>
	private string Steam(params string[] rootParts)
	{
		string root = Path.Combine(new[] { _home }.Concat(rootParts).ToArray());
		string steamapps = Path.Combine(root, "steamapps");
		Directory.CreateDirectory(steamapps);

		// The file Steam actually keeps, in the shape it actually keeps it. A library that lists only
		// itself is the ordinary single-disk case.
		File.WriteAllText(Path.Combine(steamapps, "libraryfolders.vdf"), $$"""
		"libraryfolders"
		{
			"0"
			{
				"path"		"{{root.Replace("\\", "\\\\")}}"
			}
		}
		""");
		return steamapps;
	}

	/// <summary>Installs a game into a library: the .acf Steam writes, and the folder it names.</summary>
	private static string Install(string steamapps, string appId, string folderName)
	{
		File.WriteAllText(Path.Combine(steamapps, $"appmanifest_{appId}.acf"), $$"""
		"AppState"
		{
			"appid"		"{{appId}}"
			"installdir"		"{{folderName}}"
		}
		""");

		string folder = Path.Combine(steamapps, "common", folderName);
		Directory.CreateDirectory(folder);
		return folder;
	}

	private static GameProfile Stardew => GameProfiles.Require(GameProfiles.StardewValley);

	// ---------------------------------------------------------------------
	// Finding Steam itself
	// ---------------------------------------------------------------------

	[Fact]
	public void NoSteamAtAllIsAnEmptyAnswerRatherThanAThrow()
	{
		// The state of a machine that has never had Steam, and of this one. Every answer must be null.
		LinuxGameLocator locator = Locator;

		Assert.Empty(locator.LibraryRoots());
		Assert.Null(locator.InstallFolder(Stardew));
		Assert.Null(locator.UserDocumentsFolder(Stardew));
		Assert.Null(locator.ProtonDriveC(Stardew));
	}

	[Theory]
	[InlineData(".steam", "steam")]
	[InlineData(".steam", "root")]
	[InlineData(".local", "share", "Steam")]
	[InlineData(".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam")]
	public void SteamIsFoundWhereverItPutItself(params string[] parts)
	{
		// All four are real. The last is Flatpak, which keeps its own home — and a user who installed Steam
		// that way has no other.
		Steam(parts);

		Assert.Single(Locator.LibraryRoots());
	}

	[Fact]
	public void TwoSteamRootsAreBothListed()
	{
		Steam(".steam", "steam");
		Steam(".local", "share", "Steam");

		Assert.Equal(2, Locator.LibraryRoots().Count);
	}

	// ---------------------------------------------------------------------
	// Finding a game
	// ---------------------------------------------------------------------

	[Fact]
	public void AnInstalledGameIsFoundThroughItsSteamRecords()
	{
		string steamapps = Steam(".steam", "steam");
		string expected = Install(steamapps, Stardew.SteamAppId, "Stardew Valley");

		Assert.Equal(expected, Locator.InstallFolder(Stardew));
	}

	[Fact]
	public void AGameSteamKnowsAboutButHasNotDownloadedIsNotFound()
	{
		// The .acf can outlive the folder — an interrupted install, or a game removed by hand. Answering
		// with a path that is not there would have every later step fail for a reason nothing explains.
		string steamapps = Steam(".steam", "steam");
		File.WriteAllText(Path.Combine(steamapps, $"appmanifest_{Stardew.SteamAppId}.acf"),
			$"\"AppState\"\n{{\n\t\"appid\"\t\"{Stardew.SteamAppId}\"\n\t\"installdir\"\t\"Stardew Valley\"\n}}");

		Assert.Null(Locator.InstallFolder(Stardew));
	}

	[Fact]
	public void AGameInTheSecondSteamRootIsStillFound()
	{
		Steam(".steam", "steam");
		string other = Steam(".local", "share", "Steam");
		string expected = Install(other, Stardew.SteamAppId, "Stardew Valley");

		Assert.Equal(expected, Locator.InstallFolder(Stardew));
	}

	[Fact]
	public void AGameWithNoSteamIdIsNeverLookedUp()
	{
		// Minecraft is sold by Mojang and has no Steam id at all.
		Steam(".steam", "steam");

		Assert.Null(Locator.InstallFolder(GameProfiles.Require(GameProfiles.Minecraft)));
	}

	[Fact]
	public void MinecraftIsFoundInTheHomeDirectoryRatherThanUnderConfig()
	{
		// On Linux .minecraft sits directly in the home directory. Asking .NET for ApplicationData answers
		// ~/.config, which would look for ~/.config/.minecraft — a folder no Minecraft install has used, so
		// the game reads as missing on a machine where it plainly is not.
		Directory.CreateDirectory(Path.Combine(_home, ".minecraft"));

		Assert.Equal(Path.Combine(_home, ".minecraft"),
			Locator.InstallFolder(GameProfiles.Require(GameProfiles.Minecraft)));
	}

	[Fact]
	public void ANullGameIsNotACrash()
	{
		Assert.Null(Locator.InstallFolder(null!));
		Assert.Null(Locator.UserDocumentsFolder(null!));
		Assert.Null(Locator.ProtonDriveC(null!));
	}

	// ---------------------------------------------------------------------
	// The Proton prefix, where the player's own files live
	// ---------------------------------------------------------------------

	[Fact]
	public void TheProtonPrefixIsFoundOnceTheGameHasBeenRun()
	{
		// Mods live under the game folder and are found the same way on every platform. It is saves and
		// INIs that live inside the prefix, which is why the locator asks two questions rather than one.
		string steamapps = Steam(".steam", "steam");
		string documents = Path.Combine(steamapps, "compatdata", Stardew.SteamAppId,
			"pfx", "drive_c", "users", "steamuser", "Documents");
		Directory.CreateDirectory(documents);

		Assert.Equal(documents, Locator.UserDocumentsFolder(Stardew));
		Assert.Equal(Path.Combine(steamapps, "compatdata", Stardew.SteamAppId, "pfx", "drive_c"),
			Locator.ProtonDriveC(Stardew));
	}

	[Fact]
	public void AGameThatHasNeverBeenRunHasNoPrefixYetAndThatIsNotAnError()
	{
		// The prefix appears the first time the game starts, so this is worth asking again later rather
		// than reporting as missing.
		string steamapps = Steam(".steam", "steam");
		Install(steamapps, Stardew.SteamAppId, "Stardew Valley");

		Assert.NotNull(Locator.InstallFolder(Stardew));
		Assert.Null(Locator.UserDocumentsFolder(Stardew));
	}
}
