using System;
using System.Collections.Generic;
using System.IO;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ScriptExtenderInfo"/>, whose leading case is recorded from the machine that prompted it
/// rather than invented.
///
/// On 2026-08-19 Fallout 4 updated from 1.11.221 to 1.11.240. The manager's update guardian caught it, the user
/// downloaded F4SE 0.7.9 and installed it through the manager, and the game folder ended up holding exactly what
/// it should: <c>f4se_loader.exe</c>, <c>f4se_1_11_240.dll</c> — and, still sitting there from the previous
/// install, <c>f4se_1_11_221.dll</c>. Nothing removes the old DLL and nothing needs to: F4SE loads only the one
/// named for the running game build.
///
/// The launch check nevertheless refused to believe the update had happened, because it took the <em>first</em>
/// versioned DLL it enumerated — the old one — and compared that. So a correctly updated F4SE was reported as
/// built for a game the user was no longer running, every single launch. These tests pin the folder as it
/// actually was, so a leftover build can never be mistaken for the installed one again.
/// </summary>
public class ScriptExtenderInfoTests
{
	/// <summary>The Fallout 4 folder exactly as it stood after the 1.11.240 update and F4SE reinstall.</summary>
	private static readonly string[] AfterTheUpdate = { "1.11.240", "1.11.221" };

	[Fact]
	public void ALeftoverDllFromThePreviousBuildIsNotAMismatch()
	{
		(string target, bool match) = ScriptExtenderInfo.ChooseBuild("1.11.240", AfterTheUpdate);

		Assert.True(match);
		Assert.Equal("1.11.240", target);
	}

	/// <summary>Enumeration order is the filesystem's business, and the answer must not depend on it.</summary>
	[Fact]
	public void TheMatchingBuildWinsWhicheverOrderTheDllsAreFoundIn()
	{
		Assert.True(ScriptExtenderInfo.ChooseBuild("1.11.240", new[] { "1.11.221", "1.11.240" }).Match);
		Assert.True(ScriptExtenderInfo.ChooseBuild("1.11.240", new[] { "1.11.240", "1.11.221" }).Match);
	}

	/// <summary>A real mismatch — the game moved on and no DLL was installed for it — still has to be caught.</summary>
	[Fact]
	public void AGameBuildWithNoDllInstalledForItIsAMismatch()
	{
		(string target, bool match) = ScriptExtenderInfo.ChooseBuild("1.11.240", new[] { "1.11.221" });

		Assert.False(match);
		Assert.Equal("1.11.221", target);
	}

	/// <summary>With nothing matching, the newest build present is the one to name — it is what was installed last.</summary>
	[Fact]
	public void AMismatchNamesTheNewestBuildPresentRatherThanTheFirstFound()
	{
		Assert.Equal("1.11.221", ScriptExtenderInfo.ChooseBuild("1.11.240", new[] { "1.10.163", "1.11.221" }).Target);
		Assert.Equal("1.11.221", ScriptExtenderInfo.ChooseBuild("1.11.240", new[] { "1.11.221", "1.10.163" }).Target);
	}

	/// <summary>Build numbers are numbers: 1.6.1170 is newer than 1.6.640, though it sorts earlier as text.</summary>
	[Fact]
	public void BuildsAreOrderedNumericallyNotAlphabetically()
	{
		Assert.Equal(
			new List<string> { "1.6.1170", "1.6.640", "1.5.97" },
			ScriptExtenderInfo.SortNewestFirst(new[] { "1.5.97", "1.6.640", "1.6.1170" }));
	}

	[Fact]
	public void NoVersionedDllInstalledReportsNothingRatherThanGuessing()
	{
		(string target, bool match) = ScriptExtenderInfo.ChooseBuild("1.11.240", new string[0]);

		Assert.False(match);
		Assert.Equal("", target);
	}

	/// <summary>Under Wine the game exe carries no version, and an unknown game build must not read as a mismatch
	/// against a perfectly good extender — <see cref="ScriptExtenderStatus.CanCompare"/> is what the callers gate on.</summary>
	[Fact]
	public void AnUnreadableGameVersionLeavesTheComparisonUnmade()
	{
		(string target, bool match) = ScriptExtenderInfo.ChooseBuild("", new[] { "1.11.240" });

		Assert.False(match);
		Assert.Equal("1.11.240", target);

		var status = new ScriptExtenderStatus { GameVersion = "", TargetVersion = target };
		Assert.False(status.CanCompare);
	}

	[Theory]
	[InlineData("f4se_1_11_240.dll", "f4se_", "1.11.240")]
	[InlineData("skse64_1_6_1170.dll", "skse64_", "1.6.1170")]
	[InlineData("F4SE_1_11_240.DLL", "f4se_", "1.11.240")]      // the filesystem does not care about case
	public void AVersionedDllNamesTheGameBuildItTargets(string file, string prefix, string expected) =>
		Assert.Equal(expected, ScriptExtenderInfo.RuntimeBuildFromDllName(file, prefix));

	/// <summary>The extender ships other DLLs under the same prefix; none of them names a build.</summary>
	[Theory]
	[InlineData("f4se_steam_loader.dll", "f4se_")]
	[InlineData("skse64_steam_loader.dll", "skse64_")]
	[InlineData("f4se_1_11.dll", "f4se_")]
	[InlineData("SomeOtherMod.dll", "f4se_")]
	public void DllsThatDoNotNameABuildAreIgnored(string file, string prefix) =>
		Assert.Null(ScriptExtenderInfo.RuntimeBuildFromDllName(file, prefix));

	/// <summary>Both extenders hide their real version behind a zero major part: F4SE 0.7.9 ships as 0.0.7.9 and
	/// SKSE 2.2.6 as 0.2.2.6. These two are read off the binaries in the game folders that prompted this.</summary>
	[Theory]
	[InlineData(0, 0, 7, 9, "0.7.9")]      // f4se_loader.exe, F4SE 0.7.9
	[InlineData(0, 2, 2, 6, "2.2.6")]      // skse64_loader.exe, SKSE 2.2.6
	[InlineData(1, 2, 3, 4, "1.2.3")]      // an ordinary version block, read the ordinary way
	[InlineData(0, 0, 0, 0, "")]           // no version block at all — say nothing rather than "0.0.0"
	public void TheExtendersOwnVersionIsReadTheWayItsDownloadPageWritesIt(
		int major, int minor, int build, int revision, string expected) =>
		Assert.Equal(expected, ScriptExtenderInfo.FormatProductVersion(major, minor, build, revision));

	[Fact]
	public void GamesWithoutAScriptExtenderHaveNothingToReport()
	{
		Assert.Equal("", ScriptExtenderInfo.LoaderName("StardewValley"));
		Assert.Equal("", ScriptExtenderInfo.DisplayName("Witcher3"));
		Assert.Null(ScriptExtenderInfo.Read("MoonlightPeaks", Path.GetTempPath()));
	}

	/// <summary>Each extender's names have to line up with each other, or the reader looks for the wrong files.</summary>
	[Theory]
	[InlineData("SkyrimSE", "skse64_loader.exe", "skse64_", "SKSE")]
	[InlineData("Fallout4", "f4se_loader.exe", "f4se_", "F4SE")]
	public void EachExtenderIsNamedConsistently(string game, string loader, string prefix, string display)
	{
		Assert.Equal(loader, ScriptExtenderInfo.LoaderName(game));
		Assert.Equal(prefix, ScriptExtenderInfo.DllPrefix(game));
		Assert.Equal(display, ScriptExtenderInfo.DisplayName(game));
		Assert.StartsWith(prefix, loader);
	}

	/// <summary>An empty folder is "no extender installed", not an error.</summary>
	[Fact]
	public void AnEmptyFolderReportsNothingInstalled()
	{
		WithFolder(dir => Assert.Null(ScriptExtenderInfo.Read("Fallout4", dir)));
	}

	/// <summary>
	/// Reported by an internal tester on 2026-08-20: the manager said SKSE was not installed when it plainly was,
	/// on a GOG copy of Skyrim.
	///
	/// Detection looked for <c>skse64_loader.exe</c> and nothing else. But the loader exe is only one way to
	/// start SKSE — many players, and most GOG ones, load it through the SSE Engine Fixes preloader instead and
	/// have no loader exe at all. The runtime DLL is the script extender; the loader is a convenience. So a
	/// perfectly working install was reported as missing, and the suite installer offered to install it again.
	/// </summary>
	[Fact]
	public void TheScriptExtenderIsInstalledWhenItsRuntimeDllIsThereWithNoLoaderExe()
	{
		WithFolder(dir =>
		{
			File.WriteAllText(Path.Combine(dir, "skse64_1_6_1179.dll"), "");

			ScriptExtenderStatus? se = ScriptExtenderInfo.Read("SkyrimSE", dir);

			Assert.NotNull(se);
			Assert.False(se!.LoaderPresent);
			Assert.Equal(new List<string> { "1.6.1179" }, se.InstalledBuilds);
		});
	}

	/// <summary>The ordinary install has both, and says so — the launcher is used where it exists.</summary>
	[Fact]
	public void AnInstallWithItsLoaderSaysTheLoaderIsThere()
	{
		WithFolder(dir =>
		{
			File.WriteAllText(Path.Combine(dir, "skse64_loader.exe"), "");
			File.WriteAllText(Path.Combine(dir, "skse64_1_6_1170.dll"), "");

			Assert.True(ScriptExtenderInfo.Read("SkyrimSE", dir)!.LoaderPresent);
		});
	}

	/// <summary>The loader alone, with no runtime DLL yet, is still an install worth reporting.</summary>
	[Fact]
	public void TheLoaderOnItsOwnStillCounts()
	{
		WithFolder(dir =>
		{
			File.WriteAllText(Path.Combine(dir, "f4se_loader.exe"), "");

			ScriptExtenderStatus? se = ScriptExtenderInfo.Read("Fallout4", dir);

			Assert.NotNull(se);
			Assert.True(se!.LoaderPresent);
			Assert.Empty(se.InstalledBuilds);
		});
	}

	/// <summary>Files that merely share the prefix must not be mistaken for an install.</summary>
	[Fact]
	public void UnrelatedFilesInTheGameFolderAreNotAnInstall()
	{
		WithFolder(dir =>
		{
			File.WriteAllText(Path.Combine(dir, "skse64_steam_loader.dll"), "");
			File.WriteAllText(Path.Combine(dir, "skse64_readme.txt"), "");

			Assert.Null(ScriptExtenderInfo.Read("SkyrimSE", dir));
		});
	}

	private static void WithFolder(Action<string> body)
	{
		string dir = Path.Combine(Path.GetTempPath(), "SeInfo_" + Path.GetRandomFileName());
		Directory.CreateDirectory(dir);
		try { body(dir); }
		finally { try { Directory.Delete(dir, true); } catch { } }
	}

	/// <summary>The builds that are not the one being loaded are what the suite list offers to point out.</summary>
	[Fact]
	public void TheBuildsNotBeingLoadedAreReportedSeparately()
	{
		var status = new ScriptExtenderStatus
		{
			TargetVersion = "1.11.240",
			InstalledBuilds = ScriptExtenderInfo.SortNewestFirst(AfterTheUpdate)
		};

		Assert.Equal(new List<string> { "1.11.221" }, status.OtherBuilds);
	}
}
