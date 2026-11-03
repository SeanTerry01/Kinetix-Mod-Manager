using System.Text.RegularExpressions;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// A test copy must never touch the real thing. The folder rules are tested directly, and the app's side is read from source,
/// because the app can't be loaded here.
/// </summary>
public class TestModeTests
{
	private const string Base = @"C:\Zip\Manager";

	private static TestModeSettings Settings(string root = @"D:\KinetixTest") =>
		TestModeSettings.Parse($$"""
		{ "Root": "{{root.Replace("\\", "\\\\")}}", "Games": { "SkyrimSE": "E:\\Copies\\Skyrim" } }
		""", Base);

	private static string Source(string file)
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		return File.ReadAllText(Path.Combine(dir!.FullName, file)).ReplaceLineEndings("\n");
	}

	private static string Repo()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		return dir!.FullName;
	}

	[Fact]
	public void AnOrdinaryCopyIsNotInTestMode()
	{
		// The test run has no file beside it, and neither does an installed copy.
		Assert.False(TestMode.IsOn);
		Assert.Equal("KinetixModManager-Nexus-Handler", TestMode.HandlerName);
		Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), UserFolders.ApplicationData);
	}

	[Fact]
	public void ADamagedFileStillMeansTestMode()
	{
		// The mistake to avoid is a test copy falling back to real data.
		TestModeSettings settings = TestModeSettings.Parse("not json", Base);
		Assert.Equal(Base, settings.Root);
	}

	[Fact]
	public void ARelativeRootIsBesideTheProgram()
	{
		TestModeSettings settings = TestModeSettings.Parse("""{ "Root": "." }""", Base);
		Assert.Equal(Base, settings.Root);
		Assert.Equal(Path.Combine(Base, "Data"), settings.DataFolder);
	}

	[Theory]
	[InlineData(@"D:\KinetixTest", true)]
	[InlineData(@"D:\KinetixTest\Games\Stardew Valley\Mods", true)]
	[InlineData(@"E:\Copies\Skyrim", true)]
	[InlineData(@"E:\Copies\Skyrim\Data", true)]
	[InlineData(@"D:\SteamLibrary\steamapps\common\Stardew Valley\Mods", false)]
	[InlineData(@"D:\KinetixTest2", false)]              // a folder merely starting the same
	[InlineData(@"E:\Copies\Skyrim Special Edition", false)]
	[InlineData(@"D:\KinetixTest\..\SteamLibrary", false)]
	[InlineData("", true)]
	public void OnlyTheTestFolderAndGivenCopiesAreAllowed(string path, bool allowed)
	{
		Assert.Equal(allowed, Settings().Allows(path));
	}

	[Fact]
	public void AGameIsFoundByItsIdWhateverCopyAsks()
	{
		Assert.Equal(@"E:\Copies\Skyrim", Settings().GameFolderFor(GameProfiles.SkyrimSE));
		Assert.Null(Settings().GameFolderFor(GameProfiles.Fallout4));
	}

	[Fact]
	public void SavedFoldersOutsideAreForgotten()
	{
		var app = new AppSettings
		{
			ModsPath = @"D:\SteamLibrary\steamapps\common\Stardew Valley\Mods",
			GamePaths = new Dictionary<string, string>
			{
				[GameProfiles.StardewValley] = @"D:\SteamLibrary\steamapps\common\Stardew Valley",
				[GameProfiles.SkyrimSE] = @"E:\Copies\Skyrim",
			},
			GameModsPaths = new Dictionary<string, string>
			{
				[GameProfiles.StardewValley] = @"D:\SteamLibrary\steamapps\common\Stardew Valley\Mods",
				[GameProfiles.SkyrimSE] = @"D:\KinetixTest\Data\SkyrimSEMods",
			},
		};

		Settings().ForgetFoldersOutside(app);

		Assert.Equal("", app.ModsPath);
		Assert.Equal("", app.GamePaths[GameProfiles.StardewValley]);
		Assert.Equal("", app.GameModsPaths[GameProfiles.StardewValley]);
		Assert.Equal(@"E:\Copies\Skyrim", app.GamePaths[GameProfiles.SkyrimSE]);
		Assert.Equal(@"D:\KinetixTest\Data\SkyrimSEMods", app.GameModsPaths[GameProfiles.SkyrimSE]);
	}

	[Fact]
	public void NothingAsksWindowsForAUserFolderDirectly()
	{
		// One missed is a test copy writing into the real profile. Only the Java lookup may, and it only reads.
		var pattern = new Regex(@"GetFolderPath\(\s*Environment\.SpecialFolder\.(ApplicationData|LocalApplicationData|MyDocuments|Personal|UserProfile)");
		var offenders = new List<string>();
		foreach (string project in new[] { "KinetixModManager", "Kinetix.Core" })
			foreach (string file in TestPaths.SourceFiles(Path.Combine(Repo(), project)))
			{
				string name = Path.GetFileName(file);
				if (name is "TestMode.cs" or "MinecraftLauncher.cs") continue;
				if (pattern.IsMatch(File.ReadAllText(file))) offenders.Add(name);
			}

		Assert.Empty(offenders);
	}

	[Fact]
	public void OnlyTheJavaLookupStillAsksForTheRealLocalFolder()
	{
		string launcher = Source(Path.Combine("Kinetix.Core", "MinecraftLauncher.cs"));
		Assert.Single(Regex.Matches(launcher, @"SpecialFolder\.LocalApplicationData").Cast<Match>());
	}

	[Theory]
	[InlineData("Form1.cs", "private void RegisterNxmProtocol()", "if (TestMode.IsOn) return;")]
	[InlineData("Form1.GameSelection.cs", "private bool TryLaunchViaSteam(", "if (TestMode.IsOn) return false;")]
	[InlineData("Form1.Updates.cs", "private async Task CheckForAppUpdates(bool manual)", "if (TestMode.IsOn)")]
	[InlineData("ModFileSystem.cs", "public static ModUninstaller? FindUninstallerInsideGame(", "TestMode.IsOn")]
	[InlineData("Form1.ModrinthAppCleanup.cs", "private static string? ModrinthAppUninstaller()", "if (TestMode.IsOn) return null;")]
	public void WhatReachesOutsideTheFolderChecksFirst(string file, string method, string guard)
	{
		string source = Source(Path.Combine("KinetixModManager", file));
		int start = source.IndexOf(method, StringComparison.Ordinal);
		Assert.True(start >= 0, method);
		Assert.Contains(guard, source.Substring(start, 300));
	}

	[Fact]
	public void DetectionUsesOnlyTheGivenCopy()
	{
		string source = Source(Path.Combine("KinetixModManager", "Form1.GameSelection.cs"));
		int test = source.IndexOf("TestMode.Current is { } test", StringComparison.Ordinal);
		int steam = source.IndexOf("foreach (string appId in profile.AllSteamAppIds)", StringComparison.Ordinal);
		Assert.True(test > 0 && test < steam, "It must return before any store is asked.");
	}

	[Fact]
	public void TheInstallerNeverShipsTheFile()
	{
		Assert.DoesNotContain(TestMode.FileName, File.ReadAllText(Path.Combine(Repo(), "KinetixModManager", "setup.iss")));
	}
}
