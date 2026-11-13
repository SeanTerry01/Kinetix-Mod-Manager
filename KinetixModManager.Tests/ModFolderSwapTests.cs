using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// The one promise here is that an update that fails leaves the old mod working. So the tests that matter are the ones
/// where it fails, and they check the old copy's actual contents afterwards.
/// </summary>
public sealed class ModFolderSwapTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kmm-swap-" + Guid.NewGuid().ToString("N"));
	private readonly string _mods;
	private readonly string _temp;
	private readonly List<string> _logged = new();

	public ModFolderSwapTests()
	{
		_mods = Path.Combine(_dir, "Mods");
		_temp = Path.Combine(_dir, "tmp");
		Directory.CreateDirectory(_mods);
		Directory.CreateDirectory(_temp);
	}

	public void Dispose()
	{
		try { ModOnDisk.DeleteDirectory(_dir); } catch (IOException) { }
	}

	private void Log(string where, string what) => _logged.Add(where + ": " + what);

	private string MakeMod(string parent, string name, string marker)
	{
		string folder = Path.Combine(parent, name);
		Directory.CreateDirectory(Path.Combine(folder, "assets"));
		File.WriteAllText(Path.Combine(folder, "marker.txt"), marker);
		File.WriteAllText(Path.Combine(folder, "assets", "a.png"), marker);
		return folder;
	}

	private static string MarkerIn(string folder) => File.ReadAllText(Path.Combine(folder, "marker.txt"));

	[Fact]
	public void TheNewCopyTakesThePlaceOfTheOld()
	{
		string old = MakeMod(_mods, "Foo", "old");
		string incoming = MakeMod(_temp, "built", "new");

		ModFolderSwap.Replace(incoming, old, new[] { old }, _temp, Log);

		Assert.Equal("new", MarkerIn(old));
		Assert.False(Directory.Exists(incoming));
		Assert.Empty(_logged);
	}

	[Fact]
	public void AnOldCopyUnderADifferentNameGoesToo()
	{
		// A switched-off copy, or one whose folder was named after an older download.
		string old = MakeMod(_mods, ".Foo", "old");
		string incoming = MakeMod(_temp, "built", "new");
		string destination = Path.Combine(_mods, "Foo");

		ModFolderSwap.Replace(incoming, destination, new[] { old }, _temp, Log);

		Assert.Equal("new", MarkerIn(destination));
		Assert.False(Directory.Exists(old));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void AModWithModsInsideItsOwnFolderIsReplacedWhole(bool parentNamedFirst)
	{
		// Bush Bloom Mod keeps three example content packs in its own folder, each with a manifest, so each is a mod as well
		// and an old copy the update replaces. The folder holding them moved first, and they went with it: moving them next
		// found nothing there, and every update of the mod failed (2026-11-12).
		string old = MakeMod(_mods, "BushBloomMod", "old");
		string example = MakeMod(Path.Combine(old, "examples"), "[CP] Basic Bush Bloom Example", "old example");
		string incoming = MakeMod(_temp, "built", "new");
		MakeMod(Path.Combine(incoming, "examples"), "[CP] Basic Bush Bloom Example", "new example");

		string[] oldFolders = parentNamedFirst ? new[] { old, example } : new[] { example, old };
		ModFolderSwap.Replace(incoming, old, oldFolders, _temp, Log);

		Assert.Equal("new", MarkerIn(old));
		Assert.Equal("new example", MarkerIn(example));
		Assert.Empty(_logged);
	}

	[Fact]
	public void SomethingAtTheDestinationIsReplacedEvenIfNotNamed()
	{
		string atDestination = MakeMod(_mods, "Foo", "old");
		string incoming = MakeMod(_temp, "built", "new");

		ModFolderSwap.Replace(incoming, atDestination, Array.Empty<string>(), _temp, Log);

		Assert.Equal("new", MarkerIn(atDestination));
	}

	[Fact]
	public void NoLeftoversAreLeftBehind()
	{
		string old = MakeMod(_mods, "Foo", "old");
		string incoming = MakeMod(_temp, "built", "new");

		ModFolderSwap.Replace(incoming, old, new[] { old }, _temp, Log);

		Assert.Empty(Directory.GetFileSystemEntries(_temp));
		Assert.Equal(new[] { "Foo" }, Directory.GetDirectories(_mods).Select(Path.GetFileName));
	}

	[Fact]
	public void WhenTheNewCopyCannotBePutInPlaceTheOldOneIsPutBack()
	{
		// The new folder does not exist, so the very last step fails, after the old copy has been moved away. This is
		// the worst moment, and it is what the old code had no answer to.
		string old = MakeMod(_mods, "Foo", "old");
		string missing = Path.Combine(_temp, "never-built");

		Assert.ThrowsAny<IOException>(() => ModFolderSwap.Replace(missing, old, new[] { old }, _temp, Log));

		Assert.Equal("old", MarkerIn(old));
		Assert.Equal("old", File.ReadAllText(Path.Combine(old, "assets", "a.png")));
	}

	[Fact]
	public void EveryOldCopyIsPutBackNotJustTheLast()
	{
		string enabled = MakeMod(_mods, "Foo", "enabled");
		string disabled = MakeMod(_mods, ".Foo", "disabled");
		string missing = Path.Combine(_temp, "never-built");

		Assert.ThrowsAny<IOException>(() => ModFolderSwap.Replace(missing, enabled, new[] { enabled, disabled }, _temp, Log));

		Assert.Equal("enabled", MarkerIn(enabled));
		Assert.Equal("disabled", MarkerIn(disabled));
	}

	[Fact]
	public void AFileHeldOpenStopsTheSwapWithEverythingIntact()
	{
		// The game running, or an antivirus scan. Windows refuses to move a folder with a file open in it.
		if (!OperatingSystem.IsWindows()) return;

		string old = MakeMod(_mods, "Foo", "old");
		string incoming = MakeMod(_temp, "built", "new");

		using (new FileStream(Path.Combine(old, "marker.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
		{
			Assert.ThrowsAny<Exception>(() => ModFolderSwap.Replace(incoming, old, new[] { old }, _temp, Log));
		}

		Assert.Equal("old", MarkerIn(old));
		Assert.Equal("new", MarkerIn(incoming));
	}

	[Fact]
	public void TheOldCopyCanStillBeReadJustBeforeItGoes()
	{
		// How a Bethesda mod's plugin entries get removed: the list of them is in the folder being replaced.
		string old = MakeMod(_mods, "Foo", "old");
		string incoming = MakeMod(_temp, "built", "new");
		string? seen = null;

		ModFolderSwap.Replace(incoming, old, new[] { old }, _temp, Log, parkedCopy => seen = MarkerIn(parkedCopy));

		Assert.Equal("old", seen);
	}

	[Fact]
	public void AFailureWhileReadingTheOldCopyDoesNotUndoAFinishedInstall()
	{
		string old = MakeMod(_mods, "Foo", "old");
		string incoming = MakeMod(_temp, "built", "new");

		ModFolderSwap.Replace(incoming, old, new[] { old }, _temp, Log, _ => throw new InvalidOperationException("nope"));

		Assert.Equal("new", MarkerIn(old));
		Assert.Contains(_logged, l => l.Contains("nope"));
	}

	[Fact]
	public void ReplacingWithACopyLeavesTheSourceInPlace()
	{
		string old = MakeMod(_mods, "Foo", "old");
		string source = MakeMod(_temp, "extracted", "new");

		ModFolderSwap.ReplaceWithCopyOf(source, old, new[] { old }, _temp, Log);

		Assert.Equal("new", MarkerIn(old));
		Assert.Equal("new", MarkerIn(source));
		Assert.Equal(new[] { "extracted" }, Directory.GetFileSystemEntries(_temp).Select(Path.GetFileName));
	}

	[Fact]
	public void CopyingATreeKeepsEmptyFolders()
	{
		string source = MakeMod(_temp, "extracted", "new");
		Directory.CreateDirectory(Path.Combine(source, "empty"));
		string destination = Path.Combine(_temp, "copy");

		ModFolderSwap.CopyTree(source, destination);

		Assert.True(Directory.Exists(Path.Combine(destination, "empty")));
		Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "assets", "a.png")));
	}

	[Fact]
	public void ASettingsFileIsCarriedAcrossAnUpdate()
	{
		string old = MakeMod(_mods, "Foo", "old");
		File.WriteAllText(Path.Combine(old, "config.json"), "my keybinds");
		string incoming = MakeMod(_temp, "built", "new");

		ModFolderSwap.KeepUserFile(old, incoming, "config.json");
		ModFolderSwap.Replace(incoming, old, new[] { old }, _temp, Log);

		Assert.Equal("my keybinds", File.ReadAllText(Path.Combine(old, "config.json")));
		Assert.Equal("new", MarkerIn(old));
	}

	[Fact]
	public void ASettingsFileBeatsTheDefaultOneInTheDownload()
	{
		string old = MakeMod(_mods, "Foo", "old");
		File.WriteAllText(Path.Combine(old, "config.json"), "my keybinds");
		string incoming = MakeMod(_temp, "built", "new");
		File.WriteAllText(Path.Combine(incoming, "config.json"), "defaults");

		ModFolderSwap.KeepUserFile(old, incoming, "config.json");

		Assert.Equal("my keybinds", File.ReadAllText(Path.Combine(incoming, "config.json")));
	}

	[Fact]
	public void AModWithNoSettingsFileIsNotGivenOne()
	{
		string old = MakeMod(_mods, "Foo", "old");
		string incoming = MakeMod(_temp, "built", "new");

		ModFolderSwap.KeepUserFile(old, incoming, "config.json");

		Assert.False(File.Exists(Path.Combine(incoming, "config.json")));
	}

	[Fact]
	public void BuildingBesideTheExtractionFolderMeansACopyCannotIncludeItself()
	{
		string extraction = Path.Combine(_temp, "Extract_abc");

		string build = ModFolderSwap.BuildFolderFor(extraction);

		Assert.False(build.StartsWith(extraction + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
		Assert.Equal(Path.GetDirectoryName(extraction), Path.GetDirectoryName(build));
	}
}

/// <summary>Which installed mod a download replaces. Getting this wrong deletes the wrong mod.</summary>
public sealed class InstallTargetTests
{
	private static GameMod Mod(string name, string? nexusId = null, string? folder = null) => new()
	{
		Name = name,
		UniqueId = folder ?? name,
		NexusID = nexusId,
		FolderPath = Path.Combine(Path.GetTempPath(), folder ?? name)
	};

	[Fact]
	public void ThePatchDoesNotReplaceTheMainModFromThePage()
	{
		// The case that lost mods: same page, different files, and only the page id compared.
		GameMod main = Mod("SkyUI", "12604");
		GameMod patch = Mod("SkyUI Patch", "12604");

		(GameMod? found, InstallMatch match) = InstallTarget.Find(new[] { main, patch }, "12604", "SkyUI Patch");

		Assert.Same(patch, found);
		Assert.Equal(InstallMatch.SameName, match);
	}

	[Fact]
	public void TheSameNameMatchesEvenWhenListedSecond()
	{
		GameMod other = Mod("Other", "12604");
		GameMod wanted = Mod("Wanted", "999");

		Assert.Same(wanted, InstallTarget.Find(new[] { other, wanted }, "12604", "Wanted").Mod);
	}

	[Fact]
	public void TheFolderNameCountsAsTheName()
	{
		// On Bethesda games the shown name comes from the Nexus page and the folder from the download.
		GameMod mod = Mod("A Friendly Page Title", "1", folder: "SkyUI");

		Assert.Same(mod, InstallTarget.Find(new[] { mod }, null, "SkyUI").Mod);
	}

	[Fact]
	public void TheOnlyModFromThePageIsReportedAsOnlyAPageMatch()
	{
		// May be a new version, may be a patch. Only the player knows, so the caller asks.
		GameMod main = Mod("SkyUI", "12604");

		(GameMod? found, InstallMatch match) = InstallTarget.Find(new[] { main }, "12604", "SkyUI Patch");

		Assert.Same(main, found);
		Assert.Equal(InstallMatch.SamePage, match);
	}

	[Fact]
	public void SeveralModsFromThePageAndNoNameMatchPicksNone()
	{
		GameMod one = Mod("One", "12604");
		GameMod two = Mod("Two", "12604");

		(GameMod? found, InstallMatch match) = InstallTarget.Find(new[] { one, two }, "12604", "Three");

		Assert.Null(found);
		Assert.Equal(InstallMatch.None, match);
	}

	[Fact]
	public void APartOfAMultiPartModIgnoresThePage()
	{
		// SSE Engine Fixes: its plugin and its preloader are both mod 17230, and both are needed.
		GameMod preloader = Mod("Preloader", "17230");

		Assert.Null(InstallTarget.Find(new[] { preloader }, "17230", "Plugin", matchByPage: false).Mod);
	}

	[Fact]
	public void APartOfAMultiPartModStillMatchesItsOwnName()
	{
		GameMod plugin = Mod("Plugin", "17230");

		Assert.Same(plugin, InstallTarget.Find(new[] { plugin }, "17230", "Plugin", matchByPage: false).Mod);
	}

	[Fact]
	public void AGroupRowIsNeverTheModBeingReplaced()
	{
		var group = new GameMod { Name = "Foo", UniqueId = "GROUP:Foo", IsGroup = true, FolderPath = "x" };

		Assert.Null(InstallTarget.Find(new[] { group }, null, "Foo").Mod);
	}

	[Fact]
	public void ModsWithNoNexusIdDoNotAllMatchEachOther()
	{
		GameMod local = Mod("Local Mod");

		Assert.Null(InstallTarget.Find(new[] { local }, null, "Something Else").Mod);
	}
}

/// <summary>A refresh reads the disk. It used to delete mods as well, without saying so.</summary>
public sealed class ScanOnlyReadsTests : IDisposable
{
	private readonly string _mods = Path.Combine(Path.GetTempPath(), "kmm-scan-" + Guid.NewGuid().ToString("N"));

	public ScanOnlyReadsTests() => Directory.CreateDirectory(_mods);

	public void Dispose()
	{
		try { ModOnDisk.DeleteDirectory(_mods); } catch (IOException) { }
	}

	private sealed class Context : IModScanContext
	{
		public string CurrentGamePath => "";
		public IReadOnlyDictionary<string, string> ModCategories { get; } = new Dictionary<string, string>();
		public IReadOnlyDictionary<string, string> ModNotes { get; } = new Dictionary<string, string>();
	}

	private void Mod(string folder, string name, string version, string nexusId)
	{
		string path = Path.Combine(_mods, folder);
		Directory.CreateDirectory(path);
		File.WriteAllText(Path.Combine(path, ".manager_manifest.json"),
			new Newtonsoft.Json.Linq.JObject
			{
				["Name"] = name, ["Version"] = version, ["UniqueID"] = folder, ["NexusID"] = nexusId
			}.ToString());
	}

	private List<GameMod> Scan(string game) =>
		ModScanner.ScanMods(_mods, new Newtonsoft.Json.Linq.JObject(), new Context(), game, (_, _) => { });

	[Theory]
	[InlineData(GameProfiles.SkyrimSE)]
	[InlineData(GameProfiles.Fallout4)]
	public void AMainFileAndItsPatchBothSurviveARefresh(string game)
	{
		Mod("Main", "Main", "2.0", "12604");
		Mod("Patch", "Patch", "1.0", "12604");

		Assert.Equal(2, Scan(game).Count);
		Assert.Equal(2, Directory.GetDirectories(_mods).Length);
	}

	[Fact]
	public void TwoModsOfTheSameNameBothSurviveARefresh()
	{
		Mod("A", "Same Name", "1.0", "");
		Mod("B", "Same Name", "2.0", "");

		Assert.Equal(2, Scan(GameProfiles.SkyrimSE).Count);
		Assert.Equal(2, Directory.GetDirectories(_mods).Length);
	}
}

/// <summary>What the install code must keep doing. It lives in the Windows app, so these read its source.</summary>
public sealed class InstallOrderGuardTests
{
	private static string App(string file)
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		return File.ReadAllText(Path.Combine(dir!.FullName, "KinetixModManager", file));
	}

	[Fact]
	public void NoInstallDeletesTheOldCopyBeforeTheNewOneIsInPlace()
	{
		string source = App("ModFileSystem.cs");
		Assert.DoesNotContain("ForceDeleteDirectory(existing.FolderPath)", source);
		Assert.DoesNotContain("ForceDeleteDirectory(destModFolder)", source);
		Assert.DoesNotContain("Directory.Delete(destModFolderStardew", source);
	}

	[Fact]
	public void AFailedBackupIsNeverSwallowedDuringAStardewInstall()
	{
		Assert.DoesNotContain("Pre-install backup error", App("ModFileSystem.cs"));
	}

	[Fact]
	public void AnUpdateChecksTheInstallWorkedBeforeRecordingIt()
	{
		string updates = App("Form1.Updates.cs");
		int install = updates.IndexOf("InstallFromZip(tempPath, mod.NexusID", StringComparison.Ordinal);
		Assert.True(install > 0);
		Assert.Contains("if (!await", updates.Substring(install - 12, 12));
	}

	[Theory]
	[InlineData("private void EnsureModPriorityList()")]
	[InlineData("private List<FileConflict> SyncBethesdaDeploymentHoldingLock(")]
	[InlineData("private void SyncBethesdaPluginsHoldingLock()")]
	public void NothingActsOnAModListThatIsNotATruePicture(string signature)
	{
		// An empty list is not the same as no mods. Acting on one removed every deployed file and the saved order.
		string source = App("Form1.LoadOrder.cs");
		int at = source.IndexOf(signature, StringComparison.Ordinal);
		Assert.True(at >= 0, signature);
		Assert.Contains("!ModListIsTrustworthy", source.Substring(at, 700));
	}

	[Theory]
	[InlineData("Form1.Install.cs", "private async Task<bool> InstallFromZip(")]
	[InlineData("Form1.Install.cs", "private async Task RestoreBackupAsync(")]
	[InlineData("Form1.Updates.cs", "private async Task DownloadAndInstallUpdate(")]
	[InlineData("Form1.Minecraft.cs", "private async Task<bool> MoveToMinecraftVersionAsync(")]
	[InlineData("Form1.Minecraft.cs", "private async Task InstallMinecraftJarAsync(")]
	[InlineData("Form1.Collections.cs", "private async Task InstallCollectionAsync(")]
	[InlineData("Form1.Dependencies.cs", "private async Task ResolveDependenciesAsync(")]
	public void AnOperationChangingModsHoldsOffAGameSwitch(string file, string signature)
	{
		string source = App(file);
		int at = source.IndexOf(signature, StringComparison.Ordinal);
		Assert.True(at >= 0, signature);
		// Near the top of the body, counted from its opening brace: a longer parameter list must not push it out of reach.
		int body = source.IndexOf('{', at);
		Assert.True(body >= 0, signature);
		Assert.Contains("BeginModOperation()", source.Substring(body, Math.Min(900, source.Length - body)));
	}

	[Fact]
	public void SwitchingGameIsRefusedWhileModsAreBeingChanged()
	{
		string source = App("Form1.GameSession.cs");
		int at = source.IndexOf("private void SwitchActiveGame(", StringComparison.Ordinal);
		Assert.Contains("ModOperationRunning", source.Substring(at, 500));
	}

	[Fact]
	public void AFailedConnectionNeverStopsTheModsBeingListed()
	{
		// The mods are on disk. An outage used to return from here with the list empty and the key blamed.
		string source = App("Form1.ModList.cs");
		int failed = source.IndexOf("connection == NexusConnection.KeyRejected", StringComparison.Ordinal);
		Assert.True(failed > 0);
		int scan = source.IndexOf("ModScanner.ScanMods(", failed, StringComparison.Ordinal);

		// From reporting the failure to the next branch of the refresh: nothing here may leave the method.
		string afterFailure = source.Substring(failed, 500);
		Assert.DoesNotContain("return;", afterFailure);
		Assert.True(scan > failed);

		// Nor may having no key at all stop the list being built.
		int noKey = source.IndexOf("if (needsNexusKey)", StringComparison.Ordinal);
		Assert.DoesNotContain("return;", source.Substring(noKey, source.IndexOf("Acquire the single-batch", noKey, StringComparison.Ordinal) - noKey));
	}

	[Fact]
	public void AnUnansweredCheckIsNotReportedAsUpToDate()
	{
		string source = App("Form1.Updates.cs");
		Assert.Contains("someUnanswered ? \"updates.checkFoundNoneSoFar\" : \"updates.checkCompleteNone\"", source);
		// A lookup that got nothing is counted. One Nexus answered with "removed" or "hidden" is not: asking again changes
		// nothing, and counting it said a mod site had failed to answer when it had (found by ear, 2026-11-11).
		Assert.Contains("if (asked.Answer is NexusVersionAnswer.NoAnswer or NexusVersionAnswer.KeyRefused)\n\t\t\t\t\t\tInterlocked.Increment(ref _updateChecksUnanswered);",
			source.ReplaceLineEndings("\n"));
		Assert.DoesNotContain("if (latestVersion == null) Interlocked.Increment(ref _updateChecksUnanswered);", source);
	}

	[Fact]
	public void AnUpdateCheckCannotLandOnTheWrongGame()
	{
		string source = App("Form1.Updates.cs");
		Assert.Contains("AskLatestVersionAsync(group[0].NexusID ?? \"\", domain)", source);
		Assert.Contains("if (latestVersion == null || _settings.ActiveGame != game) return;", source);
		Assert.Contains("if (_updateCheckGame != _settings.ActiveGame)", source);
	}

	[Fact]
	public void TheMultiPartAnswerReachesAnInstallForEveryLayout()
	{
		// A flag the Witcher 3 and BepInEx installs were never told about, so a second file replaced the first.
		string source = App("ModFileSystem.cs");
		Assert.Contains("nexusService, gitHubRepo, matchExistingByNexusId, updating);", source);
		Assert.Contains("gitHubRepo, currentGamePath, matchExistingByNexusId);", source);
	}
}
