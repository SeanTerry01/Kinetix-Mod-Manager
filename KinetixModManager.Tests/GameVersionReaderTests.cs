using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Reading the installed version of the games whose executable does not state it — <see cref="GameVersionReader"/>.
/// The byte layouts are the ones found in the real installs on the development machine.
/// </summary>
public class GameVersionReaderTests
{
	// -------------------------------------------------------------------------
	// The Witcher 3
	// -------------------------------------------------------------------------

	private static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);

	[Fact]
	public void TheWitchersOwnVersionIsTheOneItLabelsItselfWithMostOften()
	{
		// The real executable holds "v 4.04c" four times — beside crashVersion among them — and one older "v 4.04".
		byte[] exe = Latin1(
			"\0H.v 4.04c\0H.M" +
			"\0Force...v 4.04\0ZeroCameraRotation" +
			"\0crashVisitId\0postMortem\0crashVersion\0v 4.04c\0world_position" +
			"\0v 4.04c\0H.." +
			"\0v 4.04c\0e.n.g.i.n.e");

		Assert.Equal("4.04c", GameVersionReader.VersionFromWitcherExecutable(exe));
	}

	[Theory]
	[InlineData("\0nothing that looks like a version here\0")]
	[InlineData("\0v 4.0\0")]            // not the game's shape: two digits after the point
	[InlineData("\0v 4.04.1\0")]         // part of something longer
	public void NoVersionShapedStringMeansNoAnswer(string content) =>
		Assert.Equal("", GameVersionReader.VersionFromWitcherExecutable(Latin1(content)));

	// -------------------------------------------------------------------------
	// Unity games (Moonlight Peaks)
	// -------------------------------------------------------------------------

	/// <summary>Writes strings the way Unity does: a little-endian length, the bytes, padded to four.</summary>
	private static byte[] UnityData(params string[] strings)
	{
		var bytes = new List<byte> { 0x16, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF };   // some leading binary
		foreach (string s in strings)
		{
			bytes.AddRange(BitConverter.GetBytes(s.Length));
			bytes.AddRange(Encoding.ASCII.GetBytes(s));
			while (bytes.Count % 4 != 0) bytes.Add(0);
			bytes.AddRange(new byte[] { 1, 0, 0, 0 });   // a small integer setting between strings
		}
		return bytes.ToArray();
	}

	[Fact]
	public void AUnityGamesVersionIsTheLastVersionAfterItsStoreCategory()
	{
		// The real order in Moonlight Peaks_Data\globalgamemanagers: company, product, category, then 1.0, 1.0, 1.2.7
		// — and 1.2.7 is its published patch.
		byte[] data = UnityData("Little Chicken Game Company", "Moonlight Peaks", "public.app-category.games",
			"1.0", "1.0", "1.2.7", "f", "70b94c33-a86c-4800-9d0d-d3ffaf5ffc4a", "Default");

		Assert.Equal("1.2.7", GameVersionReader.BundleVersionFromUnityData(data));
	}

	[Fact]
	public void AVersionShapedStringBeforeTheCategoryIsNotTheGamesVersion()
	{
		// The engine's own version, 6000.3.6, sits near the very start of the file.
		byte[] data = UnityData("6000.3.6", "Some Company", "Some Game", "public.app-category.games", "0.9.1", "Default");

		Assert.Equal("0.9.1", GameVersionReader.BundleVersionFromUnityData(data));
	}

	[Fact]
	public void WithoutAStoreCategoryThereIsNoAnswerRatherThanAGuess() =>
		Assert.Equal("", GameVersionReader.BundleVersionFromUnityData(UnityData("Company", "Game", "1.2.7")));

	// -------------------------------------------------------------------------
	// Reading from disk
	// -------------------------------------------------------------------------

	[Fact]
	public void AGameFolderThatIsNotThereGivesNoVersion()
	{
		foreach (GameProfile game in GameProfiles.All)
			Assert.Equal("", GameVersionReader.ForGame(game, Path.Combine(Path.GetTempPath(), "no-such-game-" + Guid.NewGuid())));
	}

	[Fact]
	public void AUnityGamesVersionIsReadFromItsDataFolder()
	{
		string folder = Path.Combine(Path.GetTempPath(), "kinetix-unity-" + Guid.NewGuid().ToString("N"));
		try
		{
			GameProfile moonlight = GameProfiles.Require(GameProfiles.MoonlightPeaks);
			string data = Path.Combine(folder, Path.GetFileNameWithoutExtension(moonlight.GameExeName) + "_Data");
			Directory.CreateDirectory(data);
			File.WriteAllBytes(Path.Combine(data, "globalgamemanagers"),
				UnityData("Little Chicken Game Company", "Moonlight Peaks", "public.app-category.games", "1.0", "1.0", "1.2.7"));

			Assert.Equal("1.2.7", GameVersionReader.ForGame(moonlight, folder));
		}
		finally
		{
			try { Directory.Delete(folder, true); } catch { }
		}
	}
}
