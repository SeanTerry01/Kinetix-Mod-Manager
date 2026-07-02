using System;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="SteamLibraryLocator"/>: parsing libraryfolders.vdf (modern and legacy formats),
/// reading installdir from appmanifest .acf, and end-to-end folder resolution across multiple
/// libraries — including games that live in a non-primary library (i.e. another drive).
/// </summary>
public class SteamLibraryLocatorTests
{
	[Fact]
	public void ParseLibraryFolders_ModernFormat_ReturnsEveryPath()
	{
		string vdf = @"
""libraryfolders""
{
	""0""
	{
		""path""		""C:\\Program Files (x86)\\Steam""
		""apps""
		{
			""413150""		""123456789""
		}
	}
	""1""
	{
		""path""		""D:\\SteamLibrary""
	}
	""2""
	{
		""path""		""F:\\Games\\Steam""
	}
}";

		var paths = SteamLibraryLocator.ParseLibraryFolders(vdf);

		Assert.Equal(
			new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary", @"F:\Games\Steam" },
			paths);
	}

	[Fact]
	public void ParseLibraryFolders_IgnoresAppsSizeTable()
	{
		// The "apps" block maps appid -> bytes. Those numeric entries must never be treated as paths.
		string vdf = @"
""libraryfolders""
{
	""0""
	{
		""path""		""C:\\Steam""
		""apps""
		{
			""489830""		""15000000000""
			""377160""		""28000000000""
		}
	}
}";

		var paths = SteamLibraryLocator.ParseLibraryFolders(vdf);

		Assert.Equal(new[] { @"C:\Steam" }, paths);
	}

	[Fact]
	public void ParseLibraryFolders_LegacyFormat_ReturnsPaths()
	{
		// Pre-2021 clients mapped numeric keys directly to a path string, with no "path" key.
		string vdf = @"
""LibraryFolders""
{
	""TimeNextStatsReport""		""123456789""
	""ContentStatsID""		""987654321""
	""1""		""D:\\SteamLibrary""
	""2""		""E:\\Games""
}";

		var paths = SteamLibraryLocator.ParseLibraryFolders(vdf);

		Assert.Contains(@"D:\SteamLibrary", paths);
		Assert.Contains(@"E:\Games", paths);
	}

	[Fact]
	public void ParseLibraryFolders_EmptyOrGarbage_ReturnsEmpty()
	{
		Assert.Empty(SteamLibraryLocator.ParseLibraryFolders(""));
		Assert.Empty(SteamLibraryLocator.ParseLibraryFolders("not a vdf at all"));
	}

	[Fact]
	public void ParseInstallDir_ReturnsValue()
	{
		string acf = @"
""AppState""
{
	""appid""		""413150""
	""installdir""		""Stardew Valley""
	""name""		""Stardew Valley""
}";

		Assert.Equal("Stardew Valley", SteamLibraryLocator.ParseInstallDir(acf));
	}

	[Fact]
	public void ParseInstallDir_Missing_ReturnsNull()
	{
		Assert.Null(SteamLibraryLocator.ParseInstallDir(@"""AppState"" { ""appid"" ""413150"" }"));
	}

	[Fact]
	public void FindGameFolder_LocatesGameInSecondaryLibrary()
	{
		using var temp = new TempSteam();
		// Primary library exists but does NOT hold the game; the game lives in a second library
		// (simulating an install on another drive).
		string primary = temp.MakeLibrary("primary");
		string secondary = temp.MakeLibrary("secondary");
		string gameFolder = temp.InstallGame(secondary, appId: "489830", installDir: "Skyrim Special Edition");
		temp.WriteLibraryFoldersVdf("steamapps", primary, secondary);

		string? found = SteamLibraryLocator.FindGameFolder(temp.SteamPath, "489830");

		Assert.Equal(gameFolder, found);
	}

	[Fact]
	public void FindGameFolder_NotInstalled_ReturnsNull()
	{
		using var temp = new TempSteam();
		string lib = temp.MakeLibrary("lib");
		temp.InstallGame(lib, appId: "489830", installDir: "Skyrim Special Edition");
		temp.WriteLibraryFoldersVdf("steamapps", lib);

		// A different appid that has no manifest anywhere.
		Assert.Null(SteamLibraryLocator.FindGameFolder(temp.SteamPath, "377160"));
	}

	[Fact]
	public void FindGameFolder_ManifestPresentButCommonFolderMissing_ReturnsNull()
	{
		using var temp = new TempSteam();
		string lib = temp.MakeLibrary("lib");
		// Write the manifest but skip creating the common\<installdir> folder.
		string steamapps = Path.Combine(lib, "steamapps");
		Directory.CreateDirectory(steamapps);
		File.WriteAllText(Path.Combine(steamapps, "appmanifest_413150.acf"),
			"\"AppState\" { \"installdir\" \"Stardew Valley\" }");
		temp.WriteLibraryFoldersVdf("steamapps", lib);

		Assert.Null(SteamLibraryLocator.FindGameFolder(temp.SteamPath, "413150"));
	}

	[Fact]
	public void FindGameFolder_FallsBackToConfigLocationVdf()
	{
		using var temp = new TempSteam();
		string lib = temp.MakeLibrary("lib");
		string gameFolder = temp.InstallGame(lib, appId: "377160", installDir: "Fallout 4");
		// Older clients keep libraryfolders.vdf under config\ rather than steamapps\.
		temp.WriteLibraryFoldersVdf("config", lib);

		Assert.Equal(gameFolder, SteamLibraryLocator.FindGameFolder(temp.SteamPath, "377160"));
	}

	/// <summary>Builds a throwaway on-disk Steam install for the file-system integration tests.</summary>
	private sealed class TempSteam : IDisposable
	{
		private readonly string _root;
		public string SteamPath { get; }

		public TempSteam()
		{
			_root = Path.Combine(Path.GetTempPath(), "kmm_steam_test_" + Guid.NewGuid().ToString("N"));
			SteamPath = Path.Combine(_root, "Steam");
			Directory.CreateDirectory(SteamPath);
		}

		public string MakeLibrary(string name)
		{
			string lib = Path.Combine(_root, name);
			Directory.CreateDirectory(Path.Combine(lib, "steamapps", "common"));
			return lib;
		}

		/// <summary>Writes a manifest and creates the common\&lt;installDir&gt; folder. Returns that folder.</summary>
		public string InstallGame(string library, string appId, string installDir)
		{
			string steamapps = Path.Combine(library, "steamapps");
			Directory.CreateDirectory(steamapps);
			File.WriteAllText(Path.Combine(steamapps, $"appmanifest_{appId}.acf"),
				$"\"AppState\"\n{{\n\t\"appid\" \"{appId}\"\n\t\"installdir\" \"{installDir}\"\n}}");
			string gameFolder = Path.Combine(steamapps, "common", installDir);
			Directory.CreateDirectory(gameFolder);
			return gameFolder;
		}

		/// <summary>Writes a modern libraryfolders.vdf under steamapps\ or config\ listing the given libraries.</summary>
		public void WriteLibraryFoldersVdf(string subfolder, params string[] libraries)
		{
			string dir = Path.Combine(SteamPath, subfolder);
			Directory.CreateDirectory(dir);
			var sb = new System.Text.StringBuilder();
			sb.AppendLine("\"libraryfolders\"\n{");
			for (int i = 0; i < libraries.Length; i++)
			{
				// Emulate Steam's escaping of backslashes in the vdf.
				string escaped = libraries[i].Replace("\\", "\\\\");
				sb.AppendLine($"\t\"{i}\"\n\t{{\n\t\t\"path\"\t\t\"{escaped}\"\n\t}}");
			}
			sb.AppendLine("}");
			File.WriteAllText(Path.Combine(dir, "libraryfolders.vdf"), sb.ToString());
		}

		public void Dispose()
		{
			try { Directory.Delete(_root, recursive: true); } catch { }
		}
	}
}
