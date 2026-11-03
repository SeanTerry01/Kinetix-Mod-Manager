using System.IO.Compression;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>What must hold so a Minecraft update or move can't leave a game that won't start, or starts and never speaks.</summary>
public sealed class MinecraftSafetyTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "KinetixMinecraftSafety_" + Path.GetRandomFileName());

	public MinecraftSafetyTests() => Directory.CreateDirectory(_root);
	public void Dispose() => Directory.Delete(_root, true);

	private static string App(string file)
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		return File.ReadAllText(Path.Combine(dir!.FullName, "KinetixModManager", file));
	}

	private static string AppPath(string file)
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		return Path.Combine(dir!.FullName, "KinetixModManager", file);
	}

	private static GitHubRelease Release(string tag, params string[] assets) =>
		new(tag, assets.Select(a => new GitHubAsset(a, "https://example.test/" + a, 1)).ToList());

	private string Versions(params string[] ids)
	{
		foreach (string id in ids) Directory.CreateDirectory(Path.Combine(_root, "versions", id));
		return _root;
	}

	private string Jar(string folder, string fileName, string modId)
	{
		Directory.CreateDirectory(folder);
		string path = Path.Combine(folder, fileName);
		using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
		using var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open());
		writer.Write($$"""{ "schemaVersion": 1, "id": "{{modId}}", "version": "1.0.0", "name": "{{modId}}" }""");
		return path;
	}

	[Fact]
	public void TheNewestLoaderIsChosenByNumberNotByText()
	{
		string root = Versions("fabric-loader-0.16.9-1.21", "fabric-loader-0.16.10-1.21");
		Assert.Equal("fabric-loader-0.16.10-1.21", FabricInstaller.InstalledVersionIds(root)[0]);
	}

	[Fact]
	public void LaunchUsesTheVersionInUseNotWhicheverSortsFirst()
	{
		// A modpack on 26.3 sits beside the player's own 26.2.
		string root = Versions("fabric-loader-0.19.5-26.3", "fabric-loader-0.19.5-26.2");
		var ids = FabricInstaller.InstalledVersionIds(root);

		Assert.Equal("fabric-loader-0.19.5-26.2", FabricInstaller.ChooseForGameVersion(ids, "26.2"));
		Assert.Equal("", FabricInstaller.ChooseForGameVersion(ids, "26.1"));
	}

	[Fact]
	public void TheAppLaunchesByVersionAndTheStatusLineAgrees()
	{
		string source = App("Form1.Minecraft.cs");
		Assert.DoesNotContain("InstalledVersionIds(root).FirstOrDefault()", source);
	}

	[Fact]
	public void DetectionCanLeaveOutAModpacksFabric()
	{
		string root = Versions("fabric-loader-0.19.5-26.3", "fabric-loader-0.19.5-26.2");
		Assert.Equal("26.2", FabricInstaller.DetectInstalledGameVersion(root, new[] { "fabric-loader-0.19.5-26.3" }));
	}

	[Fact]
	public void AnUpdateDoesNotTakeANewerMinecraftsRelease()
	{
		// United Minecraft's real tag shape. The newest is for 26.3, and this setup is on 26.2.
		var releases = new[] { Release("v1.2.0+mc26.3"), Release("v1.1.0+mc26.2"), Release("v1.0.0+mc26.1") };

		Assert.Equal("v1.1.0+mc26.2", MinecraftSuite.ReleaseForGameVersion(releases, "26.2", mustNameIt: false)!.TagName);
		Assert.Null(MinecraftSuite.ReleaseForGameVersion(releases, "25.9", mustNameIt: false));
	}

	[Fact]
	public void ANameThatSaysNothingIsLeftToTheJarOnAnUpdateButNotOnAMove()
	{
		var releases = new[] { Release("v2.0.0", "some-mod.jar") };

		Assert.Equal("v2.0.0", MinecraftSuite.ReleaseForGameVersion(releases, "26.2", mustNameIt: false)!.TagName);
		Assert.Null(MinecraftSuite.ReleaseForGameVersion(releases, "26.2", mustNameIt: true));
	}

	[Fact]
	public void AFreshInstallPinsWhatTheModAndFabricBothSupport()
	{
		var releases = new[] { Release("v1.3.0+mc26.4"), Release("v1.2.0+mc26.3") };

		Assert.Equal("26.3", MinecraftSuite.NewestSupportedVersion(releases, new[] { "26.3", "26.2" }));
		Assert.Equal("", MinecraftSuite.NewestSupportedVersion(releases, new[] { "1.19.2" }));
	}

	[Fact]
	public void PreReleasesAndDraftsAreNotOffered()
	{
		var list = GitHubReleases.ParseList("""
		[ { "tag_name": "v3", "draft": true, "assets": [] },
		  { "tag_name": "v2", "prerelease": true, "assets": [] },
		  { "tag_name": "v1", "assets": [] } ]
		""");

		Assert.Equal("v1", Assert.Single(list!).TagName);
		Assert.Null(GitHubReleases.ParseList("not json"));
	}

	[Fact]
	public void AnOldCopyThatWillNotGoTakesTheNewOneBackOut()
	{
		// Two copies stop Fabric starting. On Linux a held file can still be deleted, so this only means anything on Windows.
		if (!OperatingSystem.IsWindows()) return;

		string mods = Path.Combine(_root, "mods");
		string old = Jar(mods, "mod-1.0.jar", "somemod");
		string incoming = Jar(Path.Combine(_root, "downloads"), "mod-2.0.jar", "somemod");

		ModInstaller.InstallResult result;
		using (new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.Read))
			result = ModInstaller.InstallFile(incoming, mods);

		Assert.Equal("mod-1.0.jar", Assert.Single(result.CouldNotRemove));
		Assert.True(File.Exists(old));
		Assert.False(File.Exists(Path.Combine(mods, "mod-2.0.jar")));
	}

	[Fact]
	public void AFileOfTheSameNameIsNotDeletedWhenAnotherCopyWillNotGo()
	{
		if (!OperatingSystem.IsWindows()) return;

		string mods = Path.Combine(_root, "mods");
		string other = Jar(mods, "mod-old-name.jar", "somemod");
		string sameName = Jar(mods, "mod-2.0.jar", "somemod");
		string incoming = Jar(Path.Combine(_root, "downloads"), "mod-2.0.jar", "somemod");

		using (new FileStream(other, FileMode.Open, FileAccess.Read, FileShare.Read))
			ModInstaller.InstallFile(incoming, mods);

		Assert.True(File.Exists(sameName), "It was the only other copy, and deleting it could leave no working mod.");
	}

	[Fact]
	public void NoCommandClaimsAKeyAMenuItemAlreadyHas()
	{
		// A menu item's own key fires before the window's, so a command given the same one is never reached: Cancel Downloads
		// on Ctrl+Shift+C closed the game session instead.
		var settings = new AppSettings();
		settings.InitializeDefaults();
		var defaults = settings.Shortcuts.Values.ToHashSet();

		foreach (string file in Directory.GetFiles(Path.GetDirectoryName(AppPath("Form1.cs"))!, "Form1*.cs"))
			foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
				File.ReadAllText(file), @"ShortcutKeys\s*=\s*((?:Keys\.\w+(?:\s*\|\s*)?)+)"))
			{
				int combination = 0;
				foreach (string part in m.Groups[1].Value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
					combination |= part["Keys.".Length..] switch
					{
						"Control" => Shortcut.Control,
						"Shift" => Shortcut.Shift,
						"Alt" => Shortcut.Alt,
						var key when key.Length == 1 => Shortcut.Letter(key[0]),
						var key => (int)Enum.Parse<System.ConsoleKey>(key),
					};
				Assert.False(defaults.Contains(combination), $"{m.Value} in {Path.GetFileName(file)} is also a default shortcut.");
			}
	}

	[Fact]
	public void ATagWithOnlyAVersionAfterThePlusStillNamesIt()
	{
		// Fabric API's own shape. Newest is for another Minecraft, so it is not offered.
		var releases = new[] { Release("0.161.0+26.3") };

		Assert.Null(MinecraftSuite.ReleaseForGameVersion(releases, "26.2", mustNameIt: false));
		Assert.Equal("0.161.0+26.3", MinecraftSuite.ReleaseForGameVersion(releases, "26.3", mustNameIt: false)!.TagName);
	}

	[Fact]
	public void PruningOneModLeavesAnotherWhoseNameStartsTheSame()
	{
		string backups = Path.Combine(_root, "backups");
		Directory.CreateDirectory(backups);
		string jar = Jar(Path.Combine(_root, "mods"), "any.jar", "any");
		BackupStore.CreateBackup(jar, "modFoo_Patch", backups);
		BackupStore.CreateBackup(jar, "modFoo", backups);

		BackupStore.PruneBackups("modFoo", backups, 0);

		var left = Directory.GetFiles(backups, "*.zip").Select(BackupStore.ModNameFromFileName).ToList();
		Assert.Equal("modFoo_Patch", Assert.Single(left));
	}

	[Fact]
	public void AMoveCannotSwitchOffTheAccessModAfterThePinMoved()
	{
		string move = App("Form1.Minecraft.cs");
		int start = move.IndexOf("private async Task<bool> MoveToMinecraftVersionAsync", StringComparison.Ordinal);
		int pin = move.IndexOf("_settings.MinecraftGameVersion = newVersion", start, StringComparison.Ordinal);
		int stopped = move.IndexOf("mc.move.accessModNotInstalled", pin, StringComparison.Ordinal);
		int switching = move.IndexOf("MinecraftMovePlan.FailedThatMustGoOff", pin, StringComparison.Ordinal);

		Assert.True(stopped > pin && (switching < 0 || stopped < switching));
		Assert.Contains("OrderByDescending(f => IsAccessMod(f.Mod))", move);
	}

	[Fact]
	public void AnUnfinishedBackupIsNeverListed()
	{
		string backups = Path.Combine(_root, "backups");
		Directory.CreateDirectory(backups);
		string jar = Jar(Path.Combine(_root, "mods"), "any.jar", "any");

		BackupStore.CreateBackup(jar, "Any", backups);

		Assert.Single(Directory.GetFiles(backups, "*.zip"));
		Assert.Empty(Directory.GetFiles(backups, "*.kinetix-part"));
	}

	[Fact]
	public void AnAbandonedPartIsCleared()
	{
		string backups = Path.Combine(_root, "backups");
		Directory.CreateDirectory(backups);
		string old = Path.Combine(backups, "Gone_1.zip.kinetix-part");
		string recent = Path.Combine(backups, "Running_2.zip.kinetix-part");
		File.WriteAllText(old, "x");
		File.WriteAllText(recent, "x");
		File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-2));

		BackupStore.CreateBackup(Jar(Path.Combine(_root, "mods"), "any.jar", "any"), "Any", backups);

		Assert.False(File.Exists(old));
		Assert.True(File.Exists(recent), "Another backup may still be writing it.");
	}

	[Theory]
	[InlineData("Create: Fabric")]
	[InlineData("What? \"Mod\"")]
	public void AModsFreeTextNameMakesAFindableBackup(string name)
	{
		string jar = Jar(Path.Combine(_root, "mods"), "any.jar", "any");
		string backups = Path.Combine(_root, "backups");
		Directory.CreateDirectory(backups);

		BackupStore.CreateBackup(jar, name, backups);

		Assert.Single(Directory.GetFiles(backups, "*.zip"));
		BackupStore.PruneBackups(name, backups, 5);
	}

	[Fact]
	public void AMoveFetchesEverythingBeforeItChangesAnything()
	{
		string move = App("Form1.Minecraft.cs");
		int start = move.IndexOf("private async Task<bool> MoveToMinecraftVersionAsync", StringComparison.Ordinal);
		int fetched = move.IndexOf("fetched.Add((mod, downloaded))", start, StringComparison.Ordinal);
		int stop = move.IndexOf("mc.move.accessModFailed", start, StringComparison.Ordinal);
		int pin = move.IndexOf("_settings.MinecraftGameVersion = newVersion", start, StringComparison.Ordinal);
		int fabric = move.IndexOf("InstallFabricAsync(root, accessMod)", start, StringComparison.Ordinal);

		Assert.True(fetched > 0 && fetched < stop && stop < pin && pin < fabric);
	}

	[Fact]
	public void AMoveLeavesSwitchedOffModsAndAsksBeforeItActs()
	{
		string move = App("Form1.Minecraft.cs");
		int start = move.IndexOf("private async Task<bool> MoveToMinecraftVersionAsync", StringComparison.Ordinal);
		string body = move.Substring(start, 3500);

		Assert.Contains("installed.Where(m => m.IsEnabled)", body);
		Assert.Contains("ModListIsTrustworthy", body);
		Assert.Contains("ActiveMinecraftPack() != null", body);
		Assert.Contains("GetLatestForHashesOrNullAsync", body);
		Assert.Contains("GetInstalledVersionsForHashesOrNullAsync", body);
	}

	[Fact]
	public void ALoaderUpdatePinsWhatIsInUseFirst()
	{
		string move = App("Form1.Minecraft.cs");
		int start = move.IndexOf("private async Task<bool> UpdateFabricLoaderAsync", StringComparison.Ordinal);
		string body = move.Substring(start, 700);

		int pinned = body.IndexOf("_settings.MinecraftGameVersion = inUse", StringComparison.Ordinal);
		int install = body.IndexOf("InstallFabricAsync(", StringComparison.Ordinal);
		Assert.True(pinned > 0 && pinned < install, "With no pin, Fabric's install moves the game to the newest Minecraft.");
	}

	[Fact]
	public void OnlyTheInstallerPutsAJarInPlace()
	{
		// Written straight to the mods folder, a differently named copy of the mod stops the game starting.
		string source = App("Form1.Minecraft.cs");
		Assert.DoesNotContain("ModrinthService.DownloadAsync(file, modsFolder)", source);
		Assert.DoesNotContain("File.WriteAllBytes(path,", source);
		Assert.DoesNotContain("Task.Run(() => ModInstaller.InstallFile(downloaded, modsFolder))", source);
	}
}
