using System.IO.Compression;
using System.Text;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// What the GTK head relies on the core for when it changes things on disk or says how a check went. That head can only
/// be compiled here, never run, so these are the only proof its decisions work: real files, real zips, real moves.
///
/// <para>
/// The Witcher 3 cases call the outside-files helpers directly, not <see cref="ModOnDisk.SetEnabled"/> or
/// <see cref="Witcher3ModFiles.Forget"/>. Those write to mods.settings in the real Documents folder, and this machine may
/// have the real game on it.
/// </para>
/// </summary>
public sealed class GtkHeadSharedLogicTests : IDisposable
{
	static GtkHeadSharedLogicTests() => Loc.Init("en");

	private readonly string _root = Path.Combine(Path.GetTempPath(), "kmm-gtk-" + Guid.NewGuid().ToString("N"));
	private readonly string _mods;
	private readonly string _backups;
	private readonly string _source;

	private readonly List<string> _logged = new();

	public GtkHeadSharedLogicTests()
	{
		_mods = Path.Combine(_root, "game", "Mods");
		_backups = Path.Combine(_root, "backups");
		_source = Path.Combine(_root, "source");
		Directory.CreateDirectory(_mods);
		Directory.CreateDirectory(_backups);
		Directory.CreateDirectory(_source);
	}

	public void Dispose()
	{
		try { ModOnDisk.DeleteDirectory(_root); } catch (IOException) { }
	}

	private void Log(string where, string what) => _logged.Add(where + ": " + what);

	private static GameProfile Game(string id) => GameProfiles.Require(id);

	private string ModFolder(string name, string file = "marker.txt", string content = "old")
	{
		string folder = Path.Combine(_mods, name);
		Directory.CreateDirectory(folder);
		File.WriteAllText(Path.Combine(folder, file), content);
		return folder;
	}

	/// <summary>A backup with no top folder, the shape BackupStore really writes.</summary>
	private string BackupOf(string fileName, params (string Entry, string Content)[] entries)
	{
		string path = Path.Combine(_root, fileName);
		using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
		foreach ((string entry, string content) in entries)
		{
			using var writer = new StreamWriter(zip.CreateEntry(entry).Open(), Encoding.UTF8);
			writer.Write(content);
		}
		return path;
	}

	private static string Jar(string folder, string fileName, string id, string version)
	{
		string path = Path.Combine(folder, fileName);
		using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
		using var writer = new StreamWriter(zip.CreateEntry(MinecraftLayout.ManifestEntryName).Open(), Encoding.UTF8);
		writer.Write(new JObject { ["schemaVersion"] = 1, ["id"] = id, ["name"] = id, ["version"] = version }.ToString());
		return path;
	}

	private string BackupOfJar(string zipName, string jarName, string id, string version)
	{
		string jar = Jar(_source, jarName, id, version);
		string zipPath = Path.Combine(_root, zipName);
		using var zip = new ZipArchive(File.Create(zipPath), ZipArchiveMode.Create);
		zip.CreateEntryFromFile(jar, jarName);
		return zipPath;
	}

	private string[] BackupsTaken() => Directory.GetFiles(_backups, "*.zip");

	private string StardewManifest(string id) => new JObject { ["UniqueID"] = id, ["Name"] = id, ["Version"] = "1.0.0" }.ToString();

	// ---------------------------------------------------------------------
	// Restoring a backup
	// ---------------------------------------------------------------------

	[Fact]
	public void ARestoreReplacesTheModRatherThanLeavingFilesLooseInTheModsFolder()
	{
		string folder = ModFolder("Cool", "manifest.json", StardewManifest("a.cool"));
		File.WriteAllText(Path.Combine(folder, "broken.txt"), "the broken version");
		string backup = BackupOf("Cool_20260101_120000.zip", ("manifest.json", StardewManifest("a.cool")), ("good.txt", "the good version"));
		var installed = new[] { new GameMod { Name = "Cool", UniqueId = "a.cool", FolderPath = folder } };

		bool replaced = BackupRestore.Restore(backup, _mods, _backups, installed, Game(GameProfiles.StardewValley), Log);

		Assert.True(replaced);
		Assert.Empty(Directory.GetFiles(_mods));
		Assert.True(File.Exists(Path.Combine(folder, "good.txt")));
		Assert.False(File.Exists(Path.Combine(folder, "broken.txt")));
	}

	[Fact]
	public void ARestoreBacksUpWhatItReplaces()
	{
		string folder = ModFolder("Cool");
		string backup = BackupOf("Cool_20260101_120000.zip", ("good.txt", "x"));
		var installed = new[] { new GameMod { Name = "Cool", UniqueId = "Cool", FolderPath = folder } };

		BackupRestore.Restore(backup, _mods, _backups, installed, Game(GameProfiles.SkyrimSE), Log);

		string kept = Assert.Single(BackupsTaken());
		Assert.Equal("Cool", BackupRestore.ModNameOf(kept));
		using ZipArchive zip = ZipFile.OpenRead(kept);
		Assert.Contains(zip.Entries, e => e.Name == "marker.txt");
	}

	[Fact]
	public void RestoringADeletedModSaysNothingWasReplaced()
	{
		string backup = BackupOf("Gone_Delete_20260101_120000.zip", ("good.txt", "x"));

		bool replaced = BackupRestore.Restore(backup, _mods, _backups, Array.Empty<GameMod>(), Game(GameProfiles.SkyrimSE), Log);

		Assert.False(replaced);
		Assert.Empty(BackupsTaken());
		Assert.True(File.Exists(Path.Combine(_mods, "Gone", "good.txt")));
	}

	[Fact]
	public void AModListedWithTheWholeModsFolderAsItsOwnIsNotWhatGetsReplaced()
	{
		// What the old restore left behind on Stardew. Replacing that "mod" would replace every other mod.
		string other = ModFolder("Other");
		File.WriteAllText(Path.Combine(_mods, "manifest.json"), StardewManifest("a.cool"));
		string backup = BackupOf("Cool_20260101_120000.zip", ("manifest.json", StardewManifest("a.cool")));
		var installed = new[] { new GameMod { Name = "Cool", UniqueId = "a.cool", FolderPath = _mods } };

		bool replaced = BackupRestore.Restore(backup, _mods, _backups, installed, Game(GameProfiles.StardewValley), Log);

		Assert.False(replaced);
		Assert.True(File.Exists(Path.Combine(other, "marker.txt")));
		Assert.True(File.Exists(Path.Combine(_mods, "Cool", "manifest.json")));
	}

	[Fact]
	public void ADamagedBackupLeavesTheModAsItWas()
	{
		string folder = ModFolder("Cool");
		string backup = Path.Combine(_root, "Cool_20260101_120000.zip");
		File.WriteAllText(backup, "this is not a zip");
		var installed = new[] { new GameMod { Name = "Cool", UniqueId = "Cool", FolderPath = folder } };

		Assert.ThrowsAny<Exception>(() =>
			BackupRestore.Restore(backup, _mods, _backups, installed, Game(GameProfiles.SkyrimSE), Log));

		Assert.Equal("old", File.ReadAllText(Path.Combine(folder, "marker.txt")));
	}

	[Fact]
	public void AnEmptyBackupDoesNotWipeTheMod()
	{
		string folder = ModFolder("Cool");
		string backup = BackupOf("Cool_20260101_120000.zip");
		var installed = new[] { new GameMod { Name = "Cool", UniqueId = "Cool", FolderPath = folder } };

		Assert.Throws<InvalidDataException>(() =>
			BackupRestore.Restore(backup, _mods, _backups, installed, Game(GameProfiles.SkyrimSE), Log));

		Assert.Equal("old", File.ReadAllText(Path.Combine(folder, "marker.txt")));
	}

	[Fact]
	public void ARestoredJarTakesThePlaceOfTheOtherVersionOfItself()
	{
		// Fabric refuses to start with two copies of one mod id, so the jar landing beside the other is a game that will
		// not start, reported as a success.
		string current = Jar(_mods, "sodium-0.6.0.jar", "sodium", "0.6.0");
		string backup = BackupOfJar("sodium_20260101_120000.zip", "sodium-0.5.8.jar", "sodium", "0.5.8");

		bool replaced = BackupRestore.Restore(backup, _mods, _backups, Array.Empty<GameMod>(), Game(GameProfiles.Minecraft), Log);

		Assert.True(replaced);
		Assert.Equal("sodium-0.5.8.jar", Path.GetFileName(Assert.Single(Directory.GetFiles(_mods))));
		Assert.False(File.Exists(current));
		Assert.Single(BackupsTaken());
	}

	[Fact]
	public void AMinecraftBackupHoldingMoreThanOneFileChangesNothing()
	{
		string current = Jar(_mods, "sodium-0.6.0.jar", "sodium", "0.6.0");
		string backup = BackupOf("sodium_20260101_120000.zip", ("a.jar", "x"), ("b.jar", "y"));

		Assert.Throws<InvalidDataException>(() =>
			BackupRestore.Restore(backup, _mods, _backups, Array.Empty<GameMod>(), Game(GameProfiles.Minecraft), Log));

		Assert.True(File.Exists(current));
		Assert.Single(Directory.GetFiles(_mods));
	}

	// ---------------------------------------------------------------------
	// Backing up a jar before it is replaced
	// ---------------------------------------------------------------------

	[Fact]
	public void EveryCopyAnUpdateWouldReplaceIsBackedUpFirst()
	{
		Jar(_mods, "sodium-0.5.8.jar", "sodium", "0.5.8");
		Jar(_mods, "sodium-0.5.0.jar.disabled", "sodium", "0.5.0");
		Jar(_mods, "lithium.jar", "lithium", "1.0.0");
		string downloaded = Jar(_source, "sodium-0.6.0.jar", "sodium", "0.6.0");

		int copies = ModInstaller.BackUpCopiesBeingReplaced(downloaded, _mods, _backups, 0);

		Assert.Equal(2, copies);
		Assert.Equal(2, BackupsTaken().Length);
		Assert.All(BackupsTaken(), zip => Assert.StartsWith("sodium", Path.GetFileName(zip)));
	}

	[Fact]
	public void AJarAboutToBeWrittenOverUnderTheSameNameIsBackedUpToo()
	{
		// ExistingCopies looks past the new file's own place, because normally nothing is there yet.
		Jar(_mods, "sodium.jar", "sodium", "0.5.8");
		string downloaded = Jar(_source, "sodium.jar", "sodium", "0.6.0");

		Assert.Equal(1, ModInstaller.BackUpCopiesBeingReplaced(downloaded, _mods, _backups, 0));
		Assert.Single(BackupsTaken());
	}

	[Fact]
	public void AFreshInstallHasNothingToBackUp()
	{
		string downloaded = Jar(_source, "sodium.jar", "sodium", "0.6.0");

		Assert.Equal(0, ModInstaller.BackUpCopiesBeingReplaced(downloaded, _mods, _backups, 0));
		Assert.Empty(BackupsTaken());
	}

	[Fact]
	public void ABackupThatCannotBeMadeStopsTheUpdate()
	{
		string old = Jar(_mods, "sodium-0.5.8.jar", "sodium", "0.5.8");
		string downloaded = Jar(_source, "sodium-0.6.0.jar", "sodium", "0.6.0");
		string notAFolder = Path.Combine(_root, "not-a-folder");
		File.WriteAllText(notAFolder, "a file where the backups folder should be");

		Assert.ThrowsAny<Exception>(() => ModInstaller.BackUpCopiesBeingReplaced(downloaded, _mods, notAFolder, 5));

		Assert.True(File.Exists(old));
	}

	[Fact]
	public void TheLimitIsKeptToWhenGivenOne()
	{
		string downloaded = Jar(_source, "sodium-0.6.0.jar", "sodium", "0.6.0");

		for (int i = 0; i < 3; i++)
		{
			string old = Jar(_mods, $"sodium-0.5.{i}.jar", "sodium", "0.5." + i);
			ModInstaller.BackUpCopiesBeingReplaced(downloaded, _mods, _backups, 2);
			File.Delete(old);
		}

		Assert.Equal(2, BackupsTaken().Length);
	}

	// ---------------------------------------------------------------------
	// Switching a mod on and off
	// ---------------------------------------------------------------------

	[Fact]
	public void AFolderModIsSwitchedOffWithoutTheFileMoveThatFailedOnEveryOne()
	{
		string folder = ModFolder("Cool");

		string target = ModOnDisk.SetEnabled(folder, false, GameProfiles.StardewValley, Log);

		Assert.Equal(Path.Combine(_mods, ".Cool"), target);
		Assert.True(File.Exists(Path.Combine(target, "marker.txt")));
		Assert.False(Directory.Exists(folder));
	}

	[Fact]
	public void AJarIsSwitchedOffAsAFile()
	{
		string jar = Jar(_mods, "sodium.jar", "sodium", "0.6.0");

		string target = ModOnDisk.SetEnabled(jar, false, GameProfiles.Minecraft, Log);

		Assert.False(File.Exists(jar));
		Assert.True(File.Exists(target));
		Assert.False(ModEnableState.IsEnabled(target, GameProfiles.Minecraft));
	}

	[Fact]
	public void TheBepInExDisabledFolderIsMadeWhenTheFirstModIsSwitchedOff()
	{
		string plugins = Path.Combine(_root, "BepInEx", ModEnableState.BepInExPluginsFolderName);
		string mod = Path.Combine(plugins, "Cool");
		Directory.CreateDirectory(mod);
		string bepInEx = GameProfiles.All.First(g => g.IsBepInEx).Id;

		string target = ModOnDisk.SetEnabled(mod, false, bepInEx, Log);

		Assert.Equal(Path.Combine(_root, "BepInEx", ModEnableState.BepInExDisabledFolderName, "Cool"), target);
		Assert.True(Directory.Exists(target));

		Assert.Equal(mod, ModOnDisk.SetEnabled(target, true, bepInEx, Log));
		Assert.True(Directory.Exists(mod));
	}

	[Fact]
	public void SwitchingOffOntoSomethingAlreadyThereFailsAndLeavesBothAlone()
	{
		string folder = ModFolder("Cool");
		string inTheWay = ModFolder(".Cool", content: "in the way");

		Assert.ThrowsAny<IOException>(() => ModOnDisk.SetEnabled(folder, false, GameProfiles.StardewValley, Log));

		Assert.Equal("old", File.ReadAllText(Path.Combine(folder, "marker.txt")));
		Assert.Equal("in the way", File.ReadAllText(Path.Combine(inTheWay, "marker.txt")));
	}

	[Fact]
	public void AModWhoseFolderIsTheModsFolderIsRecognisedWhateverItsSpelling()
	{
		Assert.True(ModOnDisk.IsTheModsFolderItself(_mods, _mods));
		Assert.True(ModOnDisk.IsTheModsFolderItself(_mods + Path.DirectorySeparatorChar, _mods));
		Assert.False(ModOnDisk.IsTheModsFolderItself(Path.Combine(_mods, "Cool"), _mods));
		Assert.False(ModOnDisk.IsTheModsFolderItself("", _mods));
	}

	// ---------------------------------------------------------------------
	// The Witcher 3's files outside its mod folders
	// ---------------------------------------------------------------------

	private string WitcherMod(params string[] extras)
	{
		string folder = ModFolder("modCool");
		File.WriteAllText(Path.Combine(folder, ".manager_manifest.json"), new JObject { ["ExtraPaths"] = new JArray(extras) }.ToString());
		return folder;
	}

	private string GameFile(string relative, string content = "from the mod")
	{
		string path = Path.Combine(_root, "game", relative);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);
		return path;
	}

	[Fact]
	public void DeletingAWitcherModTakesItsOutsideFilesWithIt()
	{
		string asi = GameFile(Path.Combine("bin", "x64", "cool.asi"));
		string keep = GameFile(Path.Combine("bin", "x64", "witcher3.exe"), "the game");
		string folder = WitcherMod(Path.Combine("bin", "x64", "cool.asi"));

		Witcher3ModFiles.RemoveExtras(folder, GameProfiles.Witcher3, Log);

		Assert.False(File.Exists(asi));
		Assert.True(File.Exists(keep));
	}

	[Fact]
	public void TheGamesOwnFolderSurvivesLosingTheLastFileInIt()
	{
		string asi = GameFile(Path.Combine("bin", "x64", "cool.asi"));
		string folder = WitcherMod(Path.Combine("bin", "x64", "cool.asi"));

		Witcher3ModFiles.RemoveExtras(folder, GameProfiles.Witcher3, Log);

		Assert.False(File.Exists(asi));
		Assert.True(Directory.Exists(Path.GetDirectoryName(asi)));
	}

	[Fact]
	public void AManifestCannotReachOutsideTheGameFolder()
	{
		// A sibling whose name starts with the game folder's own, as well as a plain climb out of it.
		string beside = Path.Combine(_root, "game2", "precious.txt");
		Directory.CreateDirectory(Path.GetDirectoryName(beside)!);
		File.WriteAllText(beside, "keep me");
		string above = Path.Combine(_root, "precious.txt");
		File.WriteAllText(above, "keep me");
		string folder = WitcherMod(Path.Combine("..", "game2", "precious.txt"), Path.Combine("..", "precious.txt"));

		Witcher3ModFiles.RemoveExtras(folder, GameProfiles.Witcher3, Log);

		Assert.True(File.Exists(beside));
		Assert.True(File.Exists(above));
	}

	[Fact]
	public void SwitchingOffAWitcherModParksItsOutsideFilesAndSwitchingOnBringsThemBack()
	{
		string relative = Path.Combine("bin", "x64", "cool.asi");
		string asi = GameFile(relative);
		string folder = WitcherMod(relative);

		Assert.Equal(1, Witcher3ModFiles.SetExtrasEnabled(folder, false, GameProfiles.Witcher3, Log));
		Assert.False(File.Exists(asi));

		Assert.Equal(1, Witcher3ModFiles.SetExtrasEnabled(folder, true, GameProfiles.Witcher3, Log));
		Assert.Equal("from the mod", File.ReadAllText(asi));
		Assert.False(Directory.Exists(Path.Combine(_root, "game", Witcher3ModFiles.DisabledExtrasFolderName, "modCool")));
	}

	[Fact]
	public void TheOutsideFileHelpersLeaveOtherGamesAlone()
	{
		string file = GameFile(Path.Combine("bin", "x64", "cool.asi"));
		string folder = WitcherMod(Path.Combine("bin", "x64", "cool.asi"));

		Witcher3ModFiles.RemoveExtras(folder, GameProfiles.SkyrimSE, Log);
		Assert.Equal(0, Witcher3ModFiles.SetExtrasEnabled(folder, false, GameProfiles.SkyrimSE, Log));

		Assert.True(File.Exists(file));
	}

	// ---------------------------------------------------------------------
	// What an update check says
	// ---------------------------------------------------------------------

	private static GameMod Mod(string path, string version = "1.0") => new() { FolderPath = path, Name = Path.GetFileName(path), Version = version };

	private static readonly Dictionary<string, string> NoAnswers = new();

	[Fact]
	public void NothingAnsweringIsNotUpToDate()
	{
		ModUpdatesView view = ModUpdatesView.Of(Game(GameProfiles.SkyrimSE), new[] { Mod("/a"), Mod("/b") }, NoAnswers, unanswered: 2);

		Assert.Equal(ModUpdatesStatus.CannotCheck, view.Status);
		Assert.DoesNotContain("up to date", view.Announcement);
		Assert.Contains("nothing could be checked", view.Announcement);
		Assert.False(view.IsComplete);
		Assert.Equal(2, view.Unanswered);
	}

	[Fact]
	public void BeingToldToSlowDownGivesADifferentThingToDo()
	{
		ModUpdatesView view = ModUpdatesView.Of(Game(GameProfiles.SkyrimSE), new[] { Mod("/a") }, NoAnswers, unanswered: 1, toldToSlowDown: true);

		Assert.Contains("slow down", view.Announcement);
	}

	[Fact]
	public void SomeUnansweredIsNotUpToDateEither()
	{
		ModUpdatesView view = ModUpdatesView.Of(Game(GameProfiles.SkyrimSE), new[] { Mod("/a"), Mod("/b"), Mod("/c") },
			new Dictionary<string, string> { ["/a"] = "1.0" }, unanswered: 2);

		Assert.Equal(ModUpdatesStatus.Checked, view.Status);
		Assert.DoesNotContain("up to date", view.Announcement);
		Assert.Contains("could not be checked", view.Announcement);
		Assert.False(view.IsComplete);
		Assert.Equal(1, view.CheckedCount);
	}

	[Fact]
	public void UpdatesFoundStillSayWhatWentUnanswered()
	{
		ModUpdatesView view = ModUpdatesView.Of(Game(GameProfiles.SkyrimSE), new[] { Mod("/a"), Mod("/b") },
			new Dictionary<string, string> { ["/a"] = "2.0" }, unanswered: 1);

		Assert.Single(view.Rows);
		Assert.Contains("could not be checked", view.Announcement);
		Assert.False(view.IsComplete);
	}

	[Fact]
	public void ModsWithNoLinkAreNotCountedAsCheckedOrAsEverything()
	{
		ModUpdatesView view = ModUpdatesView.Of(Game(GameProfiles.SkyrimSE), new[] { Mod("/a") }, NoAnswers, notLinked: 39);

		Assert.Equal(1, view.CheckedCount);
		Assert.DoesNotContain("Everything", view.Announcement);
		Assert.Contains("39 mods have no Nexus link", view.Announcement);
		Assert.True(view.IsComplete);
	}

	[Fact]
	public void AFullAnswerIsComplete()
	{
		ModUpdatesView view = ModUpdatesView.Of(Game(GameProfiles.SkyrimSE), new[] { Mod("/a") }, NoAnswers);

		Assert.True(view.IsComplete);
		Assert.Contains("up to date", view.Announcement);
	}

	[Fact]
	public void BeingUnableToCheckIsNotComplete()
	{
		Assert.False(ModUpdatesView.CannotCheck(Game(GameProfiles.SkyrimSE), "No key.").IsComplete);
	}

	// ---------------------------------------------------------------------
	// Lists that did not get read
	// ---------------------------------------------------------------------

	[Fact]
	public void AFolderThatCouldNotBeReadDoesNotSoundLikeAnEmptyOne()
	{
		InstalledModsView view = InstalledModsView.ScanFailed(Game(GameProfiles.SkyrimSE), "/mods", "Could not read it.");

		Assert.Equal(InstalledModsStatus.ScanFailed, view.Status);
		Assert.Equal("Could not read it.", view.Announcement);
		Assert.Equal("Could not read it.", view.StatusLine);
		Assert.Empty(view.Rows);
		Assert.NotEqual(InstalledModsView.Of(Game(GameProfiles.SkyrimSE), "/mods", Array.Empty<GameMod>()).Announcement, view.Announcement);
	}

	[Fact]
	public void WithNothingReadNoProfileIsTheCurrentSetup()
	{
		// Announced on every row at startup, because the list was built before the first scan.
		var profile = new ModProfile { Name = "Mage" };
		profile.ModStates["a"] = true;

		Assert.False(Assert.Single(ProfilesView.Of(new[] { profile }, Array.Empty<GameMod>()).Rows).IsCurrent);
	}

	[Fact]
	public void AProfileNamingNoneOfTheModsHereIsNotTheCurrentSetup()
	{
		var profile = new ModProfile { Name = "Mage" };
		profile.ModStates["somewhere.else"] = true;
		var installed = new[] { new GameMod { UniqueId = "a", IsEnabled = true } };

		Assert.False(Assert.Single(ProfilesView.Of(new[] { profile }, installed).Rows).IsCurrent);
	}

	[Fact]
	public void ABackupTakenBeforeADeleteIsNamedAfterTheMod()
	{
		BackupRow row = Assert.Single(BackupsView.Of(new[]
		{
			new BackupItem { Name = "Cool_Delete", FullPath = Path.Combine(_root, "Cool_Delete_20260101_120000.zip") }
		}).Rows);

		Assert.Equal("Cool", row.ModName);
	}

	// ---------------------------------------------------------------------
	// The GTK source itself, since nothing runs it here
	// ---------------------------------------------------------------------

	private static string RepositoryRoot()
	{
		DirectoryInfo? dir = new(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		Assert.True(dir != null, "Could not find the repository root.");
		return dir!.FullName;
	}

	[Theory]
	// True of one widget only, and arrowing a list puts the focus on a row, so a guard on it stops the list's keys working.
	[InlineData(".HasFocus")]
	// Hides Modrinth not answering, which reads as up to date.
	[InlineData("GetLatestForHashesAsync(")]
	// Nothing in the head unpacks a backup itself, because into the mods folder is where that went.
	[InlineData("ModArchive.Extract(")]
	// A mod can be either shape, so a move or a delete has to go through ModOnDisk.
	[InlineData("File.Move(")]
	[InlineData("Directory.Move(")]
	[InlineData("Directory.Delete(")]
	public void TheGtkHeadDoesNotGoBackTo(string call)
	{
		string[] offenders = Directory.GetFiles(Path.Combine(RepositoryRoot(), "Kinetix.Gtk"), "*.cs")
			.Where(f => File.ReadAllText(f).Contains(call))
			.Select(Path.GetFileName)
			.ToArray()!;

		Assert.Empty(offenders);
	}
}
