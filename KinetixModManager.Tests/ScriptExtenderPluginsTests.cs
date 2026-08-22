using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ScriptExtenderPlugins"/>, whose leading case is recorded from the machine that prompted it
/// rather than invented.
///
/// On 2026-08-18 Fallout 4 updated to 1.11.240 and F4SE 0.7.9 followed the same day. The manager's
/// script-extender check was therefore satisfied and said nothing — and the game launched with every one of its
/// DLL plugins silently skipped, because the Address Library's newest release covered 1.11.221 and no build for
/// 1.11.240 existed yet. The accessibility mod was among the plugins that did not load, so the entire symptom,
/// for the person relying on it, was a game that started normally and then said nothing.
///
/// That is what makes this worth a check of its own: a matching script extender proves only the first link of
/// <c>game → script extender → Address Library → DLL plugins</c>, and every break below it is invisible.
/// </summary>
public class ScriptExtenderPluginsTests
{
	/// <summary>f4se.log as it stood after the 1.11.240 update, verbatim.</summary>
	private static string[] TheBrokenFalloutLog() => new[]
	{
		"F4SE runtime: initialize (version = 0.7.9 010B0F00 01DD30B888264059, os = 6.2 (9200))",
		"plugin directory = D:\\SteamLibrary\\steamapps\\common\\Fallout 4\\Data\\F4SE\\Plugins\\",
		"checking plugin Buffout4AE.dll",
		"plugin Buffout4AE.dll (00000001 Buffout4AE 01070010) disabled, address library needs to be updated 0 (handle 0)",
		"checking plugin CrashLoggerAE.dll",
		"plugin CrashLoggerAE.dll (00000001 CrashLoggerAE 01070010) disabled, address library needs to be updated 0 (handle 0)",
		"checking plugin fallout4access.dll",
		"plugin fallout4access.dll (00000001 fallout4access 00010000) disabled, address library needs to be updated 0 (handle 0)",
		"checking plugin mcm.dll",
		"plugin mcm.dll (00000001 F4MCM 0000000B) disabled, incompatible with current version of the game 0 (handle 0)",
		"checking plugin msdia140.dll",
		"plugin msdia140.dll (00000000  00000000) no version data 0 (handle 0)",
		"checking plugin XDI.dll",
		"plugin XDI.dll (00000001 XDI 00000001) disabled, incompatible with current version of the game 0 (handle 0)",
		"preinit complete"
	};

	/// <summary>The same log from before the update, when everything loaded.</summary>
	private static string[] TheWorkingFalloutLog() => new[]
	{
		"F4SE runtime: initialize (version = 0.7.7 010B0BF0 01DC97169903371D, os = 6.2 (9200))",
		"checking plugin fallout4access.dll",
		"checking plugin XDI.dll",
		"preinit complete",
		"loading plugin \"fallout4access\"",
		"plugin fallout4access.dll (00000001 fallout4access 00000000) loaded correctly (handle 1)",
		"plugin XDI.dll (00000001 XDI 00000001) loaded correctly (handle 2)",
		"init complete"
	};

	// ---------------------------------------------------------------------
	// Reading the log
	// ---------------------------------------------------------------------

	[Fact]
	public void EveryRefusedPluginIsNamed()
	{
		List<PluginLoadFailure> failures = ScriptExtenderPlugins.ParseLoadFailures(TheBrokenFalloutLog());

		Assert.Equal(
			new[] { "Buffout4AE.dll", "CrashLoggerAE.dll", "fallout4access.dll", "mcm.dll", "XDI.dll" },
			failures.Select(f => f.PluginFile));
	}

	/// <summary>The two causes have different cures — one waits on the Address Library, the other on the mod's own
	/// author — so a report that lumped them together would send the user to the wrong page.</summary>
	[Fact]
	public void TheTwoKindsOfRefusalAreToldApart()
	{
		List<PluginLoadFailure> failures = ScriptExtenderPlugins.ParseLoadFailures(TheBrokenFalloutLog());

		Assert.Equal(
			new[] { "Buffout4AE.dll", "CrashLoggerAE.dll", "fallout4access.dll" },
			failures.Where(f => f.Kind == PluginFailureKind.AddressLibrary).Select(f => f.PluginFile));
		Assert.Equal(
			new[] { "mcm.dll", "XDI.dll" },
			failures.Where(f => f.Kind == PluginFailureKind.GameVersion).Select(f => f.PluginFile));
	}

	/// <summary>The script extender's own words are kept, without the bookkeeping it prints after them.</summary>
	[Fact]
	public void TheReasonIsKeptButTheHandleNoiseIsNot()
	{
		PluginLoadFailure buffout = ScriptExtenderPlugins.ParseLoadFailures(TheBrokenFalloutLog())[0];

		Assert.Equal("address library needs to be updated", buffout.Reason);
		Assert.Equal("Buffout4AE", buffout.Name);
	}

	/// <summary>
	/// A plugins folder also holds DLLs that are not plugins — msdia140.dll, shipped by the crash loggers — which
	/// the script extender notes as having "no version data". That is normal, and reporting it would be crying
	/// wolf about a healthy install.
	/// </summary>
	[Fact]
	public void ADllThatIsNotAPluginAtAllIsNotAFailure()
	{
		List<PluginLoadFailure> failures = ScriptExtenderPlugins.ParseLoadFailures(TheBrokenFalloutLog());

		Assert.DoesNotContain(failures, f => f.PluginFile == "msdia140.dll");
	}

	[Fact]
	public void AHealthyLogReportsNothing() =>
		Assert.Empty(ScriptExtenderPlugins.ParseLoadFailures(TheWorkingFalloutLog()));

	/// <summary>
	/// SKSE writes the same lines, and a Skyrim plugin's display name can itself contain brackets —
	/// "Achievements Mods Enabler Loader (SKSE64, AE)". Carving the line up by its brackets would go wrong on
	/// exactly that one, so the file name is taken from the front and the verdict from the end.
	/// </summary>
	[Fact]
	public void ASkyrimPluginWhoseNameContainsBracketsIsReadCorrectly()
	{
		var log = new[]
		{
			"plugin AchievementsModsEnablerLoader.dll (00000001 Achievements Mods Enabler Loader (SKSE64, AE) 00000001) loaded correctly (handle 1)",
			"plugin SkyrimAccess.dll (00000001 Skyrim Access (SE, AE) 00000001) disabled, address library needs to be updated 0 (handle 0)"
		};

		List<PluginLoadFailure> failures = ScriptExtenderPlugins.ParseLoadFailures(log);

		Assert.Equal("SkyrimAccess.dll", Assert.Single(failures).PluginFile);
	}

	/// <summary>A log the game rewrote mid-read, or one that simply is not there, must not throw.</summary>
	[Theory]
	[InlineData("")]
	[InlineData("plugin")]
	[InlineData("plugin ")]
	[InlineData("plugin NotADll (0) disabled, something 0 (handle 0)")]
	[InlineData("scanning plugin directory C:\\x\\")]
	public void ALineThatIsNotAPluginVerdictIsIgnored(string line) =>
		Assert.Empty(ScriptExtenderPlugins.ParseLoadFailures(new[] { line }));

	[Fact]
	public void NoLogAtAllIsNotAnError() =>
		Assert.Empty(ScriptExtenderPlugins.ParseLoadFailures(new string[0]));

	// ---------------------------------------------------------------------
	// The Address Library
	// ---------------------------------------------------------------------

	/// <summary>
	/// Both spellings turn up, and on Skyrim they turn up in the same folder: the pre-Anniversary files are
	/// "version-1-5-97-0.bin" and the Anniversary ones "versionlib-1-6-1170-0.bin". A republished build gains a
	/// trailing counter, and some builds ship as .csv.
	/// </summary>
	[Theory]
	[InlineData("version-1-11-221-0.bin", "1.11.221")]      // Fallout 4
	[InlineData("version-1-10-163-0.bin", "1.10.163")]
	[InlineData("version-1-5-97-0.bin", "1.5.97")]          // Skyrim SE, pre-Anniversary
	[InlineData("versionlib-1-6-1170-0.bin", "1.6.1170")]   // Skyrim Anniversary
	[InlineData("versionlib-1-6-1170-0-1.bin", "1.6.1170")] // republished
	[InlineData("version-1-10-163-0.csv", "1.10.163")]
	public void AVersionFileNamesTheGameBuildItCovers(string file, string expected) =>
		Assert.Equal(expected, ScriptExtenderPlugins.BuildFromVersionFileName(file));

	[Theory]
	[InlineData("Buffout4AE.dll")]
	[InlineData("versionlib.bin")]
	[InlineData("version-1-6.bin")]
	[InlineData("version-1-11-221-0.txt")]
	[InlineData("")]
	public void AFileThatIsNotAVersionDatabaseIsIgnored(string file) =>
		Assert.Null(ScriptExtenderPlugins.BuildFromVersionFileName(file));

	/// <summary>The exact folder that prompted this: every build up to 1.11.221, against a game on 1.11.240.</summary>
	[Fact]
	public void TheLibraryIsBehindWhenItHasNoDataForTheGameBeingRun()
	{
		string dir = MakePluginsFolder(
			"version-1-10-163-0.bin", "version-1-10-980-0.bin", "version-1-11-191-0.bin", "version-1-11-221-0.bin");
		try
		{
			var status = ScriptExtenderPlugins.ReadAddressLibrary("Fallout4", Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dir))), "1.11.240");

			Assert.True(status.Installed);
			Assert.True(status.CanJudge);
			Assert.False(status.CoversGame);
			Assert.Equal("1.11.221", status.NewestBuild);   // the newest it has, which is what the warning names
		}
		finally { CleanUp(dir); }
	}

	[Fact]
	public void TheLibraryIsFineWhenItHasDataForTheGameBeingRun()
	{
		string dir = MakePluginsFolder("versionlib-1-6-640-0.bin", "versionlib-1-6-1170-0.bin");
		try
		{
			var status = ScriptExtenderPlugins.ReadAddressLibrary("SkyrimSE", Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dir))), "1.6.1170");

			Assert.True(status.CoversGame);
		}
		finally { CleanUp(dir); }
	}

	/// <summary>Builds are ordered numerically, so "the newest it covers" is not 1.11.191 because 9 &gt; 2.</summary>
	[Fact]
	public void TheNewestCoveredBuildIsFoundNumerically()
	{
		string dir = MakePluginsFolder("version-1-11-191-0.bin", "version-1-11-221-0.bin", "version-1-10-980-0.bin");
		try
		{
			var status = ScriptExtenderPlugins.ReadAddressLibrary("Fallout4", Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dir))), "1.11.240");

			Assert.Equal("1.11.221", status.NewestBuild);
		}
		finally { CleanUp(dir); }
	}

	/// <summary>
	/// No Address Library at all is a missing requirement, which the suite check already reports. Judging it here
	/// too would put two rows in front of the user for one thing.
	/// </summary>
	[Fact]
	public void TheLibraryNotBeingInstalledIsNotJudgedHere()
	{
		string dir = MakePluginsFolder("Buffout4AE.dll");
		try
		{
			var status = ScriptExtenderPlugins.ReadAddressLibrary("Fallout4", Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dir))), "1.11.240");

			Assert.False(status.Installed);
			Assert.False(status.CanJudge);
		}
		finally { CleanUp(dir); }
	}

	/// <summary>Under Wine the game exe carries no version, so there is nothing to compare against and no claim
	/// to make — an unknown game build must not read as a library that is behind.</summary>
	[Fact]
	public void AnUnknownGameVersionIsNotJudged()
	{
		string dir = MakePluginsFolder("version-1-11-221-0.bin");
		try
		{
			var status = ScriptExtenderPlugins.ReadAddressLibrary("Fallout4", Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(dir))), "");

			Assert.True(status.Installed);
			Assert.False(status.CanJudge);
		}
		finally { CleanUp(dir); }
	}

	[Fact]
	public void AGameWithNoScriptExtenderHasNoPluginsFolder()
	{
		Assert.Equal("", ScriptExtenderPlugins.PluginsFolder("StardewValley", @"C:\Games\Stardew"));
		Assert.Equal("", ScriptExtenderPlugins.PluginsFolder("Fallout4", ""));
	}

	[Theory]
	[InlineData("Fallout4", "F4SE")]
	[InlineData("SkyrimSE", "SKSE")]
	public void ThePluginsFolderIsTheOneTheGameActuallyReads(string game, string seFolder) =>
		Assert.Equal(
			Path.Combine(@"C:\Games\X", "Data", seFolder, "Plugins"),
			ScriptExtenderPlugins.PluginsFolder(game, @"C:\Games\X"));

	// ---------------------------------------------------------------------

	/// <summary>Builds a throwaway game folder with the named files in its Data\F4SE\Plugins directory.</summary>
	private static string MakePluginsFolder(params string[] files)
	{
		string root = Path.Combine(Path.GetTempPath(), "SePlugins_" + Path.GetRandomFileName());
		// Both games are covered by making the folder for whichever the test asks about; the path shape is the
		// same, so the tests create both and let PluginsFolder pick.
		foreach (string se in new[] { "F4SE", "SKSE" })
		{
			string dir = Path.Combine(root, "Data", se, "Plugins");
			Directory.CreateDirectory(dir);
			foreach (string f in files) File.WriteAllText(Path.Combine(dir, f), "");
		}
		return Path.Combine(root, "Data", "F4SE", "Plugins");
	}

	private static void CleanUp(string pluginsDir)
	{
		try
		{
			string root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(pluginsDir)))!;
			Directory.Delete(root, true);
		}
		catch { }
	}
}
