using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="HeroicLibraryLocator"/> — finding games installed through Heroic.
///
/// <para>
/// Steam is not the whole of Linux gaming, and the manager was behaving as though it were. A Skyrim bought
/// from GOG and installed with Heroic sits in a perfectly ordinary folder that nothing looked in, so the game
/// read as not installed on a machine where it plainly was.
/// </para>
///
/// <para>
/// The library shapes below are built from Heroic's documented formats rather than copied off a real install,
/// because there is no Heroic on the machine these were written on. That is a real limit and worth stating:
/// what these hold is that the reader copes with each shape, not that those are the only shapes Heroic
/// writes. The reader is deliberately forgiving for exactly that reason — it looks for the install path
/// wherever it appears rather than walking a fixed structure, so a version that rearranges its JSON does not
/// silently stop being found.
/// </para>
/// </summary>
public class HeroicLibraryLocatorTests : IDisposable
{
	private readonly string _home = Path.Combine(Path.GetTempPath(), "kinetix-heroic-" + Guid.NewGuid().ToString("N"));

	public HeroicLibraryLocatorTests() => Directory.CreateDirectory(_home);

	public void Dispose()
	{
		try { Directory.Delete(_home, true); } catch { }
	}

	/// <summary>Writes one of Heroic's library files, creating the folders it lives in.</summary>
	private void Library(string relative, string json, bool flatpak = false)
	{
		string config = flatpak
			? Path.Combine(_home, ".var", "app", "com.heroicgameslauncher.hgl", "config", "heroic")
			: Path.Combine(_home, ".config", "heroic");

		string path = Path.Combine(config, relative);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, json);
	}

	/// <summary>Creates a game folder with the given executable in it, and returns the folder.</summary>
	private string Game(string folderName, string exe)
	{
		string folder = Path.Combine(_home, "Games", folderName);
		Directory.CreateDirectory(folder);
		File.WriteAllText(Path.Combine(folder, exe), "");
		return folder;
	}

	// ---------------------------------------------------------------------
	// Reading the library
	// ---------------------------------------------------------------------

	[Fact]
	public void TheGogShapeIsRead()
	{
		var paths = HeroicLibraryLocator.ReadInstallPaths("""
		{ "installed": [ { "appName": "1234", "install_path": "/games/Skyrim", "platform": "windows" } ] }
		""");

		Assert.Equal(new[] { "/games/Skyrim" }, paths);
	}

	[Fact]
	public void ABareArrayIsReadToo()
	{
		var paths = HeroicLibraryLocator.ReadInstallPaths("""
		[ { "install_path": "/games/One" }, { "install_path": "/games/Two" } ]
		""");

		Assert.Equal(new[] { "/games/One", "/games/Two" }, paths);
	}

	[Fact]
	public void ANestedInstallObjectIsRead()
	{
		// Newer Heroic puts it a level down, which is exactly the sort of change that would have broken a
		// reader that walked a fixed structure.
		var paths = HeroicLibraryLocator.ReadInstallPaths("""
		{ "library": [ { "title": "Skyrim", "install": { "install_path": "/games/Skyrim" } } ] }
		""");

		Assert.Equal(new[] { "/games/Skyrim" }, paths);
	}

	[Fact]
	public void TheCamelCaseSpellingIsReadAsWell()
	{
		var paths = HeroicLibraryLocator.ReadInstallPaths("""{ "installPath": "/games/Skyrim" }""");

		Assert.Equal(new[] { "/games/Skyrim" }, paths);
	}

	[Fact]
	public void TheSameFolderTwiceIsListedOnce()
	{
		var paths = HeroicLibraryLocator.ReadInstallPaths("""
		[ { "install_path": "/games/One" }, { "install_path": "/games/One" } ]
		""");

		Assert.Single(paths);
	}

	[Fact]
	public void SomethingThatIsNotALibraryIsEmptyRatherThanAThrow()
	{
		// A half-written file during an install is an ordinary thing to walk into.
		Assert.Empty(HeroicLibraryLocator.ReadInstallPaths("{ not json"));
		Assert.Empty(HeroicLibraryLocator.ReadInstallPaths(""));
		Assert.Empty(HeroicLibraryLocator.ReadInstallPaths("""{ "installed": [] }"""));
	}

	// ---------------------------------------------------------------------
	// On disk
	// ---------------------------------------------------------------------

	[Fact]
	public void NoHeroicAtAllIsAnEmptyAnswer()
	{
		Assert.Empty(HeroicLibraryLocator.InstalledFolders(_home));
		Assert.Null(HeroicLibraryLocator.FindGameFolder(_home, "SkyrimSE.exe"));
	}

	[Fact]
	public void AFolderHeroicRecordsButThatIsGoneIsNotOffered()
	{
		// Heroic's record outlives an uninstall done by hand, and answering with a path that is not there
		// would have every later step fail for a reason nothing explains.
		Library(Path.Combine("gog_store", "installed.json"),
			"""{ "installed": [ { "install_path": "/games/deleted-by-hand" } ] }""");

		Assert.Empty(HeroicLibraryLocator.InstalledFolders(_home));
	}

	[Fact]
	public void AGameIsFoundByItsExecutable()
	{
		string folder = Game("Skyrim Special Edition GOG", "SkyrimSE.exe");
		Library(Path.Combine("gog_store", "installed.json"),
			$$"""{ "installed": [ { "install_path": {{System.Text.Json.JsonSerializer.Serialize(folder)}} } ] }""");

		Assert.Equal(folder, HeroicLibraryLocator.FindGameFolder(_home, "SkyrimSE.exe"));
	}

	[Fact]
	public void AGameOneFolderDeeperIsStillFound()
	{
		// Heroic's record sometimes names the folder a game was installed INTO rather than the one it ended
		// up in.
		string outer = Path.Combine(_home, "Games", "Heroic");
		string inner = Path.Combine(outer, "Skyrim Special Edition");
		Directory.CreateDirectory(inner);
		File.WriteAllText(Path.Combine(inner, "SkyrimSE.exe"), "");

		Library(Path.Combine("gog_store", "installed.json"),
			$$"""{ "installed": [ { "install_path": {{System.Text.Json.JsonSerializer.Serialize(outer)}} } ] }""");

		Assert.Equal(inner, HeroicLibraryLocator.FindGameFolder(_home, "SkyrimSE.exe"));
	}

	[Fact]
	public void TheFlatpakHomeIsSearchedToo()
	{
		// Somebody who installed Heroic that way has no other config folder at all.
		string folder = Game("Stardew Valley", "Stardew Valley.exe");
		Library(Path.Combine("gog_store", "installed.json"),
			$$"""{ "installed": [ { "install_path": {{System.Text.Json.JsonSerializer.Serialize(folder)}} } ] }""",
			flatpak: true);

		Assert.Equal(folder, HeroicLibraryLocator.FindGameFolder(_home, "Stardew Valley.exe"));
	}

	[Fact]
	public void AGameThatIsNotThereIsNotInvented()
	{
		Game("Something Else", "other.exe");
		Library(Path.Combine("gog_store", "installed.json"),
			$$"""{ "installed": [ { "install_path": {{System.Text.Json.JsonSerializer.Serialize(Path.Combine(_home, "Games", "Something Else"))}} } ] }""");

		Assert.Null(HeroicLibraryLocator.FindGameFolder(_home, "SkyrimSE.exe"));
	}

	[Fact]
	public void AGameWithNoExecutableNameIsNeverLookedFor()
	{
		// Minecraft has none, and guessing would be worse than answering nothing.
		Assert.Null(HeroicLibraryLocator.FindGameFolder(_home, ""));
		Assert.Null(HeroicLibraryLocator.FindGameFolder(_home, null));
	}
}
