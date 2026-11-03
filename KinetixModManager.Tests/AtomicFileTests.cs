using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

public sealed class AtomicFileTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kmm-atomic-" + Guid.NewGuid().ToString("N"));
	private readonly string _file;

	public AtomicFileTests()
	{
		Directory.CreateDirectory(_dir);
		_file = Path.Combine(_dir, "settings.json");
	}

	public void Dispose()
	{
		try { ModOnDisk.DeleteDirectory(_dir); } catch (IOException) { }
	}

	[Fact]
	public void ANewFileIsCreated()
	{
		AtomicFile.WriteAllText(_file, "hello");

		Assert.Equal("hello", File.ReadAllText(_file));
	}

	[Fact]
	public void AnExistingFileIsReplacedWhole()
	{
		File.WriteAllText(_file, "a much longer old version of the contents");

		AtomicFile.WriteAllText(_file, "new");

		Assert.Equal("new", File.ReadAllText(_file));
	}

	[Fact]
	public void NothingIsLeftBesideIt()
	{
		File.WriteAllText(_file, "old");

		AtomicFile.WriteAllText(_file, "new");

		Assert.Equal(new[] { "settings.json" }, Directory.GetFiles(_dir).Select(Path.GetFileName));
	}

	[Fact]
	public void ThePreviousVersionIsKeptWhenAskedFor()
	{
		File.WriteAllText(_file, "old");

		AtomicFile.WriteAllText(_file, "new", keepPrevious: true);

		Assert.Equal("old", File.ReadAllText(_file + AtomicFile.BackupSuffix));
	}

	[Fact]
	public void AFailedWriteLeavesTheOldContentsAndNoLitter()
	{
		// A folder sitting where the temporary file has to go makes the very first step fail.
		File.WriteAllText(_file, "old");
		Directory.CreateDirectory(_file + AtomicFile.TemporarySuffix);

		Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(_file, "new"));

		Assert.Equal("old", File.ReadAllText(_file));
	}

	[Fact]
	public void AFileHeldOpenDuringTheSwapKeepsItsOldContents()
	{
		if (!OperatingSystem.IsWindows()) return;

		File.WriteAllText(_file, "old");

		using (new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			Assert.ThrowsAny<IOException>(() => AtomicFile.WriteAllText(_file, "new"));
		}

		Assert.Equal("old", File.ReadAllText(_file));
		Assert.False(File.Exists(_file + AtomicFile.TemporarySuffix));
	}

	[Fact]
	public void TheEncodingMatchesWhatItReplaces()
	{
		// No byte order mark, the same as File.WriteAllText, because plugins.txt is read by the game itself.
		AtomicFile.WriteAllText(_file, "é");

		Assert.Equal(new byte[] { 0xC3, 0xA9 }, File.ReadAllBytes(_file));
	}

	[Fact]
	public void LinesAreWrittenOneEach()
	{
		AtomicFile.WriteAllLines(_file, new[] { "*A.esp", "*B.esp" });

		Assert.Equal(new[] { "*A.esp", "*B.esp" }, File.ReadAllLines(_file));
	}

	[Fact]
	public void ADamagedJsonFileReadsAsEmptyAndIsKeptAside()
	{
		File.WriteAllText(_file, "{ \"a\": ");

		JObject read = AtomicFile.ReadJsonObjectOrEmpty(_file);

		Assert.Empty(read);
		Assert.False(File.Exists(_file));
		Assert.Equal("{ \"a\": ", File.ReadAllText(_file + ".damaged"));
	}

	[Fact]
	public void AMissingJsonFileReadsAsEmpty()
	{
		Assert.Empty(AtomicFile.ReadJsonObjectOrEmpty(_file));
	}

	[Fact]
	public void AGoodJsonFileReadsNormally()
	{
		File.WriteAllText(_file, "{ \"a\": 1 }");

		Assert.Equal(1, (int)AtomicFile.ReadJsonObjectOrEmpty(_file)["a"]!);
	}
}

/// <summary>A damaged settings file must not silently become a fresh install, or destroy what could be rescued.</summary>
public sealed class SettingsRecoveryTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kmm-settings-" + Guid.NewGuid().ToString("N"));
	private readonly string _file;

	public SettingsRecoveryTests()
	{
		Directory.CreateDirectory(_dir);
		_file = Path.Combine(_dir, "settings.json");
	}

	public void Dispose()
	{
		try { ModOnDisk.DeleteDirectory(_dir); } catch (IOException) { }
	}

	private static string SettingsFor(string game) => new JObject { ["ActiveGame"] = game }.ToString();

	[Fact]
	public void AGoodFileLoadsWithNothingToReport()
	{
		File.WriteAllText(_file, SettingsFor("Minecraft"));

		AppSettings loaded = AppSettings.LoadFrom(_file);

		Assert.Equal("Minecraft", loaded.ActiveGame);
		Assert.Equal(SettingsLoadProblem.None, loaded.LoadProblem);
	}

	[Theory]
	[InlineData("{ \"ActiveGame\": \"Mine")]   // a write cut off part way
	[InlineData("")]                          // emptied, then never filled: the usual result of a crash
	[InlineData("   \r\n")]
	[InlineData("null")]
	public void ADamagedFileFallsBackToTheLastGoodCopy(string damaged)
	{
		File.WriteAllText(_file, damaged);
		File.WriteAllText(_file + AtomicFile.BackupSuffix, SettingsFor("Minecraft"));

		AppSettings loaded = AppSettings.LoadFrom(_file);

		Assert.Equal("Minecraft", loaded.ActiveGame);
		Assert.Equal(SettingsLoadProblem.RestoredFromBackup, loaded.LoadProblem);
	}

	[Theory]
	[InlineData("{ cut off")]
	[InlineData("")]
	public void ADamagedFileWithNoGoodCopySaysSo(string damaged)
	{
		File.WriteAllText(_file, damaged);

		AppSettings loaded = AppSettings.LoadFrom(_file);

		Assert.Equal(SettingsLoadProblem.ResetToDefaults, loaded.LoadProblem);
	}

	[Fact]
	public void ADamagedBackupAsWellSaysSo()
	{
		File.WriteAllText(_file, "{ cut off");
		File.WriteAllText(_file + AtomicFile.BackupSuffix, "also { cut off");

		Assert.Equal(SettingsLoadProblem.ResetToDefaults, AppSettings.LoadFrom(_file).LoadProblem);
	}

	[Fact]
	public void TheGoodCopyIsBackInPlaceForTheNextLoad()
	{
		// The splash screen loads first and the main window second. With the damaged file moved aside and nothing put
		// back, the second load found no file at all and started from nothing, undoing the rescue.
		File.WriteAllText(_file, "{ cut off");
		File.WriteAllText(_file + AtomicFile.BackupSuffix, SettingsFor("Minecraft"));

		AppSettings.LoadFrom(_file);
		AppSettings second = AppSettings.LoadFrom(_file);

		Assert.Equal("Minecraft", second.ActiveGame);
	}

	[Fact]
	public void TheDamagedFileIsKeptSoItCanBeRescued()
	{
		File.WriteAllText(_file, "{ my whole setup, slightly cut off");

		AppSettings.LoadFrom(_file);

		Assert.Contains("my whole setup", File.ReadAllText(_file + ".damaged"));
	}

	[Fact]
	public void TheProblemIsNotWrittenIntoTheFile()
	{
		// It describes this run, and saved it would be announced on every start for ever.
		var settings = new AppSettings { LoadProblem = SettingsLoadProblem.ResetToDefaults };

		Assert.DoesNotContain("LoadProblem", Newtonsoft.Json.JsonConvert.SerializeObject(settings));
	}

	[Fact]
	public void NoFileAtAllIsAnOrdinaryFirstRun()
	{
		Assert.Equal(SettingsLoadProblem.None, AppSettings.LoadFrom(_file).LoadProblem);
	}
}

/// <summary>A mod's damaged record must not make the mod disappear, because deployment then removes its files.</summary>
public sealed class DamagedManifestTests : IDisposable
{
	private readonly string _mods = Path.Combine(Path.GetTempPath(), "kmm-manifest-" + Guid.NewGuid().ToString("N"));

	public DamagedManifestTests() => Directory.CreateDirectory(_mods);

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

	[Theory]
	[InlineData("{ cut off")]
	[InlineData("")]
	public void TheModStaysInTheListAndItsRecordIsRepaired(string damaged)
	{
		string folder = Path.Combine(_mods, "SkyUI-12604-5-2");
		Directory.CreateDirectory(folder);
		string manifest = Path.Combine(folder, ".manager_manifest.json");
		File.WriteAllText(manifest, damaged);

		List<GameMod> found = ModScanner.ScanMods(_mods, new JObject(), new Context(), GameProfiles.SkyrimSE, (_, _) => { });

		GameMod mod = Assert.Single(found);
		Assert.Equal(folder, mod.FolderPath);
		Assert.Equal("12604", mod.NexusID);
		JObject.Parse(File.ReadAllText(manifest));
	}
}

/// <summary>What a crash leaves behind is cleared up, and nothing else is.</summary>
public sealed class LeftoverSweepTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kmm-sweep-" + Guid.NewGuid().ToString("N"));

	public LeftoverSweepTests() => Directory.CreateDirectory(_dir);

	public void Dispose()
	{
		try { ModOnDisk.DeleteDirectory(_dir); } catch (IOException) { }
	}

	private string Folder(string name, int daysOld)
	{
		string path = Path.Combine(_dir, name);
		Directory.CreateDirectory(path);
		Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-daysOld));
		return path;
	}

	private string FileIn(string name, int daysOld)
	{
		string path = Path.Combine(_dir, name);
		File.WriteAllText(path, "x");
		File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-daysOld));
		return path;
	}

	[Theory]
	[InlineData("Extract_abc")]
	[InlineData("New_abc")]
	[InlineData("Old_abc")]
	public void AnOldWorkFolderIsRemoved(string name)
	{
		string folder = Folder(name, daysOld: 3);

		Assert.Equal(1, ModArchive.SweepOldWorkFolders(_dir, TimeSpan.FromDays(1)));
		Assert.False(Directory.Exists(folder));
	}

	[Fact]
	public void ARecentWorkFolderIsLeftAlone()
	{
		// It may belong to an install running in another copy of the manager, or one that only just crashed.
		string folder = Folder("Extract_abc", daysOld: 0);

		Assert.Equal(0, ModArchive.SweepOldWorkFolders(_dir, TimeSpan.FromDays(1)));
		Assert.True(Directory.Exists(folder));
	}

	[Fact]
	public void AFolderTheManagerDidNotNameIsLeftAlone()
	{
		string folder = Folder("Someone Elses Things", daysOld: 300);

		ModArchive.SweepOldWorkFolders(_dir, TimeSpan.FromDays(1));

		Assert.True(Directory.Exists(folder));
	}

	[Fact]
	public void AnOldPartialDownloadIsRemovedAndAFinishedOneIsNot()
	{
		string partial = FileIn("mod.zip" + ModArchive.PartialDownloadSuffix, daysOld: 3);
		string finished = FileIn("mod.zip", daysOld: 300);

		Assert.Equal(1, ModArchive.SweepOldPartialDownloads(_dir, TimeSpan.FromDays(1)));
		Assert.False(File.Exists(partial));
		Assert.True(File.Exists(finished));
	}

	[Fact]
	public void ARecentPartialDownloadIsLeftAlone()
	{
		string partial = FileIn("mod.zip" + ModArchive.PartialDownloadSuffix, daysOld: 0);

		ModArchive.SweepOldPartialDownloads(_dir, TimeSpan.FromDays(1));

		Assert.True(File.Exists(partial));
	}

	[Fact]
	public void MissingFoldersAreNotAnError()
	{
		string missing = Path.Combine(_dir, "nope");

		Assert.Equal(0, ModArchive.SweepOldWorkFolders(missing, TimeSpan.FromDays(1)));
		Assert.Equal(0, ModArchive.SweepOldPartialDownloads(missing, TimeSpan.FromDays(1)));
	}
}

/// <summary>Whether something is changing mods right now, which is what stops a game switch under it.</summary>
public sealed class OperationGateTests
{
	[Fact]
	public void FreeUntilSomethingStarts()
	{
		Assert.False(new OperationGate().IsBusy);
	}

	[Fact]
	public void BusyUntilItFinishes()
	{
		var gate = new OperationGate();

		using (gate.Begin()) Assert.True(gate.IsBusy);

		Assert.False(gate.IsBusy);
	}

	[Fact]
	public void BusyUntilTheLastOfSeveralFinishes()
	{
		var gate = new OperationGate();
		IDisposable first = gate.Begin();
		IDisposable second = gate.Begin();

		first.Dispose();
		Assert.True(gate.IsBusy);

		second.Dispose();
		Assert.False(gate.IsBusy);
	}

	[Fact]
	public void FinishingTwiceDoesNotFreeItEarly()
	{
		var gate = new OperationGate();
		IDisposable first = gate.Begin();
		using IDisposable second = gate.Begin();

		first.Dispose();
		first.Dispose();

		Assert.True(gate.IsBusy);
	}
}

/// <summary>The manager's own documents read as words. Line for line, so a search result still lands in the right place.</summary>
public sealed class DocumentCleaningTests
{
	private static string Clean(string content)
	{
		var node = new DocNode("Topic") { Content = content };
		DocOutline.CleanForReading(new[] { node });
		return node.Content!;
	}

	[Theory]
	[InlineData("*   **F5**: Launch the game.", "F5: Launch the game.")]
	[InlineData("- A plain item", "A plain item")]
	[InlineData("Use the `mods` folder.", "Use the mods folder.")]
	[InlineData("### A heading in the text", "A heading in the text")]
	[InlineData("> A quotation", "A quotation")]
	[InlineData("See [Searching a Document](#searching-a-document-ctrl--f) for more.", "See Searching a Document for more.")]
	public void PunctuationThatIsOnlyLayoutGoes(string line, string expected)
	{
		Assert.Equal(expected, Clean(line));
	}

	[Fact]
	public void ADividingLineBecomesBlank()
	{
		Assert.Equal("", Clean("---"));
	}

	[Fact]
	public void ALinkToAWebsiteKeepsItsAddress()
	{
		// The pane offers it: Enter on the line opens it.
		Assert.Contains("https://example.com", Clean("See [the site](https://example.com) for more."));
	}

	[Fact]
	public void ANumberedStepKeepsItsNumber()
	{
		Assert.Equal("1. Do this first.", Clean("1. Do this first."));
	}

	[Fact]
	public void TheNumberOfLinesDoesNotChange()
	{
		// A search result is a line number, so this is what keeps it pointing at the right place.
		string cleaned = Clean("# Title\n\n*   item\n---\ntext");

		Assert.Equal(5, cleaned.Split('\n').Length);
	}

	[Fact]
	public void SubTopicsAreCleanedToo()
	{
		var child = new DocNode("Child") { Content = "**bold**" };
		var parent = new DocNode("Parent");
		parent.Children.Add(child);

		DocOutline.CleanForReading(new[] { parent });

		Assert.Equal("bold", child.Content);
	}
}
