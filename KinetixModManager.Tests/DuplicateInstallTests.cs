using System.Collections.Generic;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// A game recorded twice for one folder — Sean's "Minecraft" and "Minecraft2", both at <c>.minecraft</c>, left
/// behind by testing the version move against an empty folder. The games list offered Minecraft twice.
///
/// ⚠️ The tests that matter are the ones where the duplicate SURVIVES: removing a record that mods, history or
/// the open session hang off would lose them, and a tidy-up has no business doing that.
/// </summary>
public sealed class DuplicateInstallTests
{
	private const string Folder = @"C:\Users\SeanT\AppData\Roaming\.minecraft";

	private static AppSettings TwoRecordsOf(string secondFolder)
	{
		var settings = new AppSettings { ActiveGame = "Minecraft" };
		settings.GameInstalls.Add(new GameInstall { Key = "Minecraft", GameId = "Minecraft", Folder = Folder });
		settings.GameInstalls.Add(new GameInstall { Key = "Minecraft2", GameId = "Minecraft", Folder = secondFolder });
		settings.GamePaths["Minecraft"] = Folder;
		settings.GamePaths["Minecraft2"] = secondFolder;
		settings.GameModsPaths["Minecraft"] = Folder + @"\mods";
		settings.GameModsPaths["Minecraft2"] = secondFolder + @"\mods";
		return settings;
	}

	private static bool NothingOnDisk(string key) => false;

	[Fact]
	public void AnEmptySecondRecordOfTheSameFolderIsRemoved()
	{
		AppSettings settings = TwoRecordsOf(Folder);

		Assert.Equal(new[] { "Minecraft2" }, settings.DropEmptyDuplicateInstalls(NothingOnDisk));

		GameInstall kept = Assert.Single(settings.GameInstalls);
		Assert.Equal("Minecraft", kept.Key);
		Assert.False(settings.GamePaths.ContainsKey("Minecraft2"));
		Assert.False(settings.GameModsPaths.ContainsKey("Minecraft2"));
		// The copy that stays keeps its paths — the three places a location lives must still agree.
		Assert.Equal(Folder, settings.GamePaths["Minecraft"]);
		Assert.Equal(Folder + @"\mods", settings.GameModsPaths["Minecraft"]);
	}

	[Fact]
	public void TheSameFolderWrittenDifferentlyIsStillTheSameFolder()
	{
		AppSettings settings = TwoRecordsOf(Folder.ToUpperInvariant() + @"\");

		Assert.Equal(new[] { "Minecraft2" }, settings.DropEmptyDuplicateInstalls(NothingOnDisk));
	}

	[Fact]
	public void TwoCopiesInDifferentFoldersAreBothKept()
	{
		AppSettings settings = TwoRecordsOf(@"D:\Games\Minecraft Test");

		Assert.Empty(settings.DropEmptyDuplicateInstalls(NothingOnDisk));
		Assert.Equal(2, settings.GameInstalls.Count);
	}

	[Fact]
	public void ADuplicateWithAnythingFiledUnderItIsKept()
	{
		AppSettings settings = TwoRecordsOf(Folder);
		settings.InstalledArchives["Minecraft2"] = new List<string> { "fabric-api-0.161.0+26.3.jar" };

		Assert.Empty(settings.DropEmptyDuplicateInstalls(NothingOnDisk));
		Assert.Equal(2, settings.GameInstalls.Count);
		Assert.True(settings.GamePaths.ContainsKey("Minecraft2"));
	}

	[Fact]
	public void AnEmptyEntryUnderADuplicateIsNotSomethingFiled()
	{
		AppSettings settings = TwoRecordsOf(Folder);
		settings.InstalledArchives["Minecraft2"] = new List<string>();

		Assert.Equal(new[] { "Minecraft2" }, settings.DropEmptyDuplicateInstalls(NothingOnDisk));
	}

	[Fact]
	public void ADuplicateWithDataOnDiskIsKept()
	{
		AppSettings settings = TwoRecordsOf(Folder);

		Assert.Empty(settings.DropEmptyDuplicateInstalls(key => key == "Minecraft2"));
		Assert.Equal(2, settings.GameInstalls.Count);
	}

	[Fact]
	public void TheOpenSessionIsNeverTheOneRemoved()
	{
		AppSettings settings = TwoRecordsOf(Folder);
		settings.ActiveGame = "Minecraft2";

		Assert.Empty(settings.DropEmptyDuplicateInstalls(NothingOnDisk));
		Assert.Equal(2, settings.GameInstalls.Count);
	}
}
