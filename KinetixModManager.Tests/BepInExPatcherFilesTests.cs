using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Pins <see cref="BepInExPatcherFiles"/>: a BepInEx mod's preloader patchers, outside its folder, follow the mod. They used to
/// be copied into <c>BepInEx\patchers</c> and forgotten, so switching the mod off or deleting it left them patching the game.
/// </summary>
public sealed class BepInExPatcherFilesTests : IDisposable
{
	private const string Game = GameProfiles.MoonlightPeaks;
	private readonly string _root = Path.Combine(Path.GetTempPath(), "kinetix-patchers-" + Guid.NewGuid().ToString("N"));
	private readonly List<string> _log = new();

	private string BepInEx => Path.Combine(_root, "BepInEx");
	private string Patchers => Path.Combine(BepInEx, "patchers");
	private string PatchersOff => Path.Combine(BepInEx, "patchers-disabled");
	private string Plugins => Path.Combine(BepInEx, "plugins");

	private void Log(string where, string what) => _log.Add(where + ": " + what);

	public void Dispose()
	{
		try { Directory.Delete(_root, recursive: true); } catch { }
	}

	/// <summary>A mod under plugins that recorded <paramref name="patchers"/>, each of which is in place under patchers.</summary>
	private string InstalledMod(string name, params string[] patchers)
	{
		string mod = Path.Combine(Plugins, name);
		Directory.CreateDirectory(mod);
		File.WriteAllText(Path.Combine(mod, name + ".dll"), "plugin");
		var manifest = new JObject { ["Name"] = name };
		BepInExPatcherFiles.WriteRecord(manifest, patchers);
		File.WriteAllText(Path.Combine(mod, ".manager_manifest.json"), manifest.ToString());
		foreach (string p in patchers)
		{
			string path = Path.Combine(Patchers, p);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, "patcher " + name);
		}
		return mod;
	}

	[Fact]
	public void SwitchingTheModOffTakesItsPatchersOutOfWhatBepInExLoads()
	{
		string mod = InstalledMod("Alpha", "Alpha.Patcher.dll", Path.Combine("Sub", "Alpha.Extra.dll"));

		string off = ModOnDisk.SetEnabled(mod, false, Game, "", Log);

		Assert.Equal(Path.Combine(BepInEx, "plugins-disabled", "Alpha"), off);
		Assert.False(File.Exists(Path.Combine(Patchers, "Alpha.Patcher.dll")));
		Assert.False(File.Exists(Path.Combine(Patchers, "Sub", "Alpha.Extra.dll")));
		Assert.True(File.Exists(Path.Combine(PatchersOff, "Alpha.Patcher.dll")));
		Assert.True(File.Exists(Path.Combine(PatchersOff, "Sub", "Alpha.Extra.dll")));

		string on = ModOnDisk.SetEnabled(off, true, Game, "", Log);

		Assert.Equal(mod, on);
		Assert.True(File.Exists(Path.Combine(Patchers, "Alpha.Patcher.dll")));
		Assert.True(File.Exists(Path.Combine(Patchers, "Sub", "Alpha.Extra.dll")));
		Assert.False(File.Exists(Path.Combine(PatchersOff, "Alpha.Patcher.dll")));
		Assert.Empty(_log);
	}

	[Fact]
	public void APatcherAnotherModThatIsOnNeedsStaysRunning()
	{
		string alpha = InstalledMod("Alpha", "Shared.Patcher.dll");
		InstalledMod("Beta", "Shared.Patcher.dll");

		ModOnDisk.SetEnabled(alpha, false, Game, "", Log);

		Assert.True(File.Exists(Path.Combine(Patchers, "Shared.Patcher.dll")));
	}

	[Fact]
	public void DeletingTheModRemovesItsPatchersButNotAnotherModsOrAnyoneElses()
	{
		string alpha = InstalledMod("Alpha", "Alpha.Patcher.dll", "Shared.Patcher.dll");
		InstalledMod("Beta", "Shared.Patcher.dll");
		File.WriteAllText(Path.Combine(Patchers, "Nobody.Patcher.dll"), "put there by hand");

		Assert.Empty(BepInExPatcherFiles.Remove(alpha, Game, Log));

		Assert.False(File.Exists(Path.Combine(Patchers, "Alpha.Patcher.dll")));
		Assert.True(File.Exists(Path.Combine(Patchers, "Shared.Patcher.dll")));
		Assert.True(File.Exists(Path.Combine(Patchers, "Nobody.Patcher.dll")));
	}

	[Fact]
	public void ASwitchedOffModsPatchersAreRemovedFromTheDisabledFolder()
	{
		string off = ModOnDisk.SetEnabled(InstalledMod("Alpha", "Alpha.Patcher.dll"), false, Game, "", Log);

		BepInExPatcherFiles.Remove(off, Game, Log);

		Assert.False(File.Exists(Path.Combine(PatchersOff, "Alpha.Patcher.dll")));
	}

	[Fact]
	public void InstallCopiesThePatchersAndSaysWhichForTheRecord()
	{
		string archive = Path.Combine(_root, "archive", "BepInEx", "patchers");
		Directory.CreateDirectory(Path.Combine(archive, "Sub"));
		File.WriteAllText(Path.Combine(archive, "A.dll"), "a");
		File.WriteAllText(Path.Combine(archive, "Sub", "B.dll"), "b");

		List<string> copied = BepInExPatcherFiles.Install(archive, BepInEx, Log);

		Assert.Equal(new[] { "A.dll", Path.Combine("Sub", "B.dll") }, copied.OrderBy(c => c));
		Assert.True(File.Exists(Path.Combine(Patchers, "Sub", "B.dll")));
	}

	[Fact]
	public void AnUpdateRemovesAPatcherTheNewVersionNoLongerShips()
	{
		string old = InstalledMod("Alpha", "Old.Patcher.dll", "Kept.Patcher.dll");

		BepInExPatcherFiles.RemoveLeftovers(old, new[] { "Kept.Patcher.dll" }, Log);

		Assert.False(File.Exists(Path.Combine(Patchers, "Old.Patcher.dll")));
		Assert.True(File.Exists(Path.Combine(Patchers, "Kept.Patcher.dll")));
	}

	[Fact]
	public void ABackupCarriesThePatchersAndARestorePutsThemBack()
	{
		string mod = InstalledMod("Alpha", "Alpha.Patcher.dll");

		Assert.True(BepInExPatcherFiles.StashInto(mod, Game, Log));
		string carried = Path.Combine(mod, BepInExPatcherFiles.StashFolderName, "Alpha.Patcher.dll");
		Assert.True(File.Exists(carried));

		// Deleted, then restored with the copy still inside it, as a backup holds it.
		BepInExPatcherFiles.Remove(mod, Game, Log);
		Assert.False(File.Exists(Path.Combine(Patchers, "Alpha.Patcher.dll")));

		BepInExPatcherFiles.RestoreStash(mod, Game, Log);

		Assert.True(File.Exists(Path.Combine(Patchers, "Alpha.Patcher.dll")));
		Assert.False(Directory.Exists(Path.Combine(mod, BepInExPatcherFiles.StashFolderName)));
	}

	[Fact]
	public void TheCarriedCopyNeverStaysInsideTheMod()
	{
		// BepInEx scans plugins for DLLs, and a patcher must not be found there.
		string mod = InstalledMod("Alpha", "Alpha.Patcher.dll");
		BepInExPatcherFiles.StashInto(mod, Game, Log);

		BepInExPatcherFiles.ClearStash(mod);

		Assert.False(Directory.Exists(Path.Combine(mod, BepInExPatcherFiles.StashFolderName)));
	}

	[Fact]
	public void ARecordThatPointsOutsidePatchersIsIgnored()
	{
		string mod = InstalledMod("Alpha");
		string outside = Path.Combine(_root, "keep.txt");
		File.WriteAllText(outside, "not a patcher");
		var manifest = JObject.Parse(File.ReadAllText(Path.Combine(mod, ".manager_manifest.json")));
		manifest[BepInExPatcherFiles.RecordKey] = new JArray(Path.Combine("..", "..", "keep.txt"));
		File.WriteAllText(Path.Combine(mod, ".manager_manifest.json"), manifest.ToString());

		BepInExPatcherFiles.Remove(mod, Game, Log);

		Assert.True(File.Exists(outside));
	}

	private sealed class ScanContext(string game) : IModScanContext
	{
		public string CurrentGamePath { get; } = game;
		public IReadOnlyDictionary<string, string> ModCategories { get; } = new Dictionary<string, string>();
		public IReadOnlyDictionary<string, string> ModNotes { get; } = new Dictionary<string, string>();
	}

	[Fact]
	public void AScanKeepsTheRecord()
	{
		// It rebuilt the manifest from scratch, and the first scan after an install threw the record away: found by installing a
		// mod with a patcher in the test copy, 2026-11-11.
		string mod = InstalledMod("Alpha", "Alpha.Patcher.dll");

		ModScanner.ScanMods(Plugins, new JObject(), new ScanContext(_root), Game, (_, _) => { });

		Assert.Equal(new[] { "Alpha.Patcher.dll" }, BepInExPatcherFiles.Recorded(mod));
	}

	[Fact]
	public void OtherGamesAreLeftAlone()
	{
		string mod = InstalledMod("Alpha", "Alpha.Patcher.dll");

		Assert.Equal(0, BepInExPatcherFiles.SetEnabled(mod, false, GameProfiles.StardewValley, Log));
		Assert.Empty(BepInExPatcherFiles.Remove(mod, GameProfiles.StardewValley, Log));
		Assert.True(File.Exists(Path.Combine(Patchers, "Alpha.Patcher.dll")));
	}
}
