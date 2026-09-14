using System;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="SoundThemes"/> — which sound file plays, for the game that is loaded.
///
/// <para>
/// The sounds are per game on purpose: a Skyrim session and a Stardew session are told apart by ear before a
/// word is read out, which is why the theme follows the loaded game rather than being picked in Settings.
/// Adding a game's sounds is meant to be nothing but dropping <c>.ogg</c> files into a folder, and these
/// tests are what make that true — including the half-finished case, which is the normal state of a pack
/// somebody is still authoring.
/// </para>
/// </summary>
public class SoundThemesTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "kinetix-sound-" + Guid.NewGuid().ToString("N"));

	public SoundThemesTests() => Directory.CreateDirectory(_root);

	public void Dispose()
	{
		try { Directory.Delete(_root, true); } catch { }
	}

	/// <summary>Makes <c>&lt;theme&gt;/&lt;event&gt;/</c>, with the named files in it. No files = an empty folder.</summary>
	private string Event(string theme, string name, params string[] files)
	{
		string dir = Path.Combine(_root, theme, name);
		Directory.CreateDirectory(dir);
		foreach (string f in files) File.WriteAllBytes(Path.Combine(dir, f), Array.Empty<byte>());
		return dir;
	}

	// ---------------------------------------------------------------------
	// Picking the file
	// ---------------------------------------------------------------------

	[Fact]
	public void ThemeSoundWins()
	{
		Event("Default", "connect", "nexus_connect.ogg");
		Event("Minecraft", "connect", "mc_join.ogg");

		Assert.Equal(Path.Combine(_root, "Minecraft", "connect", "mc_join.ogg"),
			SoundThemes.Resolve(_root, "Minecraft", "connect"));
	}

	[Fact]
	public void AnEventTheThemeHasNotAuthoredFallsBackToDefault()
	{
		Event("Default", "error", "oops.ogg");
		Event("Minecraft", "connect", "mc_join.ogg");

		Assert.Equal(Path.Combine(_root, "Default", "error", "oops.ogg"),
			SoundThemes.Resolve(_root, "Minecraft", "error"));
	}

	[Fact]
	public void AnAuthoredButEmptyFolderStillFallsBackToDefault()
	{
		// The case that matters, and the one this used to get wrong. A new theme starts as a set of empty
		// folders and gains sounds one at a time. Judged on the folder rather than the file, the manager went
		// SILENT for every event not yet recorded — no error, nothing on screen, and the one channel a blind
		// user cannot check for themselves.
		Event("Default", "connect", "nexus_connect.ogg");
		Event("Minecraft", "connect");   // folder made, nothing in it yet

		Assert.Equal(Path.Combine(_root, "Default", "connect", "nexus_connect.ogg"),
			SoundThemes.Resolve(_root, "Minecraft", "connect"));
	}

	[Fact]
	public void AFolderHoldingOnlyANoteFallsBackToDefault()
	{
		// The scaffolded theme ships a README in each folder saying what to drop there. That must not count
		// as a sound.
		Event("Default", "connect", "nexus_connect.ogg");
		Event("Minecraft", "connect", "README.txt");

		Assert.Equal(Path.Combine(_root, "Default", "connect", "nexus_connect.ogg"),
			SoundThemes.Resolve(_root, "Minecraft", "connect"));
	}

	[Fact]
	public void NothingAnywhereIsNullRatherThanAThrow()
	{
		Assert.Null(SoundThemes.Resolve(_root, "Minecraft", "connect"));
		Assert.Null(SoundThemes.Resolve(Path.Combine(_root, "nope"), "Minecraft", "connect"));
	}

	[Fact]
	public void TheSameThemePicksTheSameFileOnEveryMachine()
	{
		// Whichever file the filesystem happens to hand back first is not an answer: two machines would then
		// play different sounds from one theme.
		Event("Minecraft", "logo", "b_second.ogg", "a_first.ogg", "c_third.ogg");

		Assert.Equal(Path.Combine(_root, "Minecraft", "logo", "a_first.ogg"),
			SoundThemes.Resolve(_root, "Minecraft", "logo"));
	}

	// ---------------------------------------------------------------------
	// Which theme
	// ---------------------------------------------------------------------

	[Fact]
	public void EachGameAsksForItsOwnTheme()
	{
		Assert.Equal("Minecraft", SoundThemes.ForGame(GameProfiles.Minecraft));
		Assert.Equal("Stardew Valley", SoundThemes.ForGame(GameProfiles.StardewValley));
		Assert.Equal("Skyrim", SoundThemes.ForGame(GameProfiles.SkyrimSE));
	}

	[Fact]
	public void NoGameLoadedMeansTheDefaultTheme()
	{
		Assert.Equal("Default", SoundThemes.ForGame(GameProfiles.NoGame));
		Assert.Equal("Default", SoundThemes.ForGame(null));
		Assert.Equal("Default", SoundThemes.ForGame("SomeGameThatDoesNotExist"));
	}

	[Fact]
	public void DefaultIsListedFirstBecauseEverythingFallsBackToIt()
	{
		Event("Stardew Valley", "connect");
		Event("Default", "connect");
		Event("Minecraft", "connect");

		Assert.Equal(new[] { "Default", "Minecraft", "Stardew Valley" }, SoundThemes.Installed(_root));
	}

	// ---------------------------------------------------------------------
	// What actually ships
	// ---------------------------------------------------------------------

	[Fact]
	public void EveryGameHasTheSoundFolderItAsksFor()
	{
		// A game whose SoundTheme names a folder that does not exist plays Default for everything, silently.
		// It works, so nobody notices — and the per-game sounds are the thing that tells a player by ear
		// which game they are in.
		string sounds = ShippedSoundsFolder();

		foreach (GameProfile game in GameProfiles.All)
		{
			string theme = Path.Combine(sounds, game.SoundTheme);
			Assert.True(Directory.Exists(theme),
				$"{game.DisplayName} asks for the \"{game.SoundTheme}\" sound theme, and {theme} is not there.");
		}
	}

	[Fact]
	public void EveryThemeHasAFolderForEveryEvent()
	{
		string sounds = ShippedSoundsFolder();

		foreach (string theme in SoundThemes.Installed(sounds))
		{
			foreach (string name in SoundThemes.EventNames)
			{
				Assert.True(Directory.Exists(Path.Combine(sounds, theme, name)),
					$"The {theme} theme has no \"{name}\" folder. Make it, even empty — Default's sound plays until it has one.");
			}
		}
	}

	[Fact]
	public void TheDefaultThemeHasARealSoundForEveryEvent()
	{
		// Default is the one theme that cannot fall back to anything, so a gap in it is a cue that never plays
		// for any game.
		string sounds = ShippedSoundsFolder();

		foreach (string name in SoundThemes.EventNames)
		{
			Assert.NotNull(SoundThemes.Resolve(sounds, SoundThemes.DefaultTheme, name));
		}
	}

	private static string ShippedSoundsFolder()
	{
		var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MANUAL.md"))) dir = dir.Parent;
		string root = dir?.FullName ?? Directory.GetCurrentDirectory();

		string sounds = Path.Combine(root, "KinetixModManager", "sounds");
		Assert.True(Directory.Exists(sounds), $"The shipped sounds folder is missing from {sounds}.");
		return sounds;
	}
}
