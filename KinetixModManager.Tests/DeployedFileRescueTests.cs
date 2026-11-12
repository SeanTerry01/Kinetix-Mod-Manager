using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// A deployment deleted the last copy of a mod's files: the mod was still installed, but its own copies had gone from its folder,
/// so to the deployment nothing provided them any more (the Unofficial Patch's .esp and .bsa files, a real Skyrim, 2026-11-11).
/// </summary>
public sealed class DeployedFileRescueTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kinetix-rescue-" + Guid.NewGuid().ToString("N"));
	private readonly string _game, _mod;

	public DeployedFileRescueTests()
	{
		_game = Path.Combine(_dir, "game");
		_mod = Path.Combine(_dir, "mods", "USSEP");
		Directory.CreateDirectory(Path.Combine(_game, "Data"));
		Directory.CreateDirectory(_mod);
	}

	public void Dispose()
	{
		try { Directory.Delete(_dir, true); } catch (IOException) { }
	}

	private static readonly Func<string, string, bool> NoLinks = (_, _) => false;

	private string InGame(string rel, string contents)
	{
		string path = Path.Combine(_game, rel);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, contents);
		return path;
	}

	[Theory]
	[InlineData(@"Data\unofficial skyrim special edition patch.esp", @"unofficial skyrim special edition patch.esp")]
	[InlineData(@"Data\SKSE\Plugins\po3_Tweaks.pdb", @"SKSE\Plugins\po3_Tweaks.pdb")]
	[InlineData(@"data\Sound\x.wav", @"Sound\x.wav")]
	[InlineData(@"skse64_loader.exe", @"Root\skse64_loader.exe")]
	[InlineData(@"Tools\thing.dll", @"Root\Tools\thing.dll")]
	public void AFileGoesBackWhereDeploymentWouldTakeItFrom(string deployed, string inMod)
	{
		// The reverse of SyncDeployment's mapping: Data\X comes from X, and everything beside the .exe from Root\.
		Assert.Equal(inMod.Replace('\\', Path.DirectorySeparatorChar), DeployedFileRescue.PathInMod(deployed.Replace('\\', Path.DirectorySeparatorChar)));
	}

	[Fact]
	public void TheGamesCopyGoesBackIntoTheMod()
	{
		string game = InGame(@"Data\unofficial skyrim special edition patch.esp", "the only copy");

		Assert.Equal(DeployedFileRescue.Rescue.PutBack, DeployedFileRescue.PutBackIntoMod(game, _mod, @"Data\unofficial skyrim special edition patch.esp", NoLinks));

		Assert.Equal("the only copy", File.ReadAllText(Path.Combine(_mod, "unofficial skyrim special edition patch.esp")));
		Assert.True(File.Exists(game), "Putting it back must not take it out of the game.");
	}

	[Fact]
	public void ItIsLinkedWhenItCanBe()
	{
		string game = InGame(@"Data\a.bsa", "x");
		var asked = new List<(string newName, string existing)>();

		DeployedFileRescue.PutBackIntoMod(game, _mod, @"Data\a.bsa", (n, e) => { asked.Add((n, e)); File.Copy(e, n); return true; });

		Assert.Equal((Path.Combine(_mod, "a.bsa"), game), Assert.Single(asked));
	}

	[Fact]
	public void FoldersInsideTheModAreMade()
	{
		string game = InGame(@"Data\SKSE\Plugins\po3_Tweaks.pdb", "pdb");

		DeployedFileRescue.PutBackIntoMod(game, _mod, @"Data\SKSE\Plugins\po3_Tweaks.pdb", NoLinks);

		Assert.True(File.Exists(Path.Combine(_mod, "SKSE", "Plugins", "po3_Tweaks.pdb")));
	}

	[Fact]
	public void TheModsOwnCopyIsNeverOverwritten()
	{
		// Switching a mod off asks about every one of its files, so this is the usual answer, and it must not be logged as a rescue.
		string game = InGame(@"Data\a.esp", "game's");
		File.WriteAllText(Path.Combine(_mod, "a.esp"), "mod's");

		Assert.Equal(DeployedFileRescue.Rescue.AlreadyInMod, DeployedFileRescue.PutBackIntoMod(game, _mod, @"Data\a.esp", NoLinks));
		Assert.Equal("mod's", File.ReadAllText(Path.Combine(_mod, "a.esp")));
	}

	[Fact]
	public void NothingToKeepWhenTheGameHasNoCopy()
	{
		Assert.Equal(DeployedFileRescue.Rescue.NothingToKeep, DeployedFileRescue.PutBackIntoMod(Path.Combine(_game, "Data", "gone.esp"), _mod, @"Data\gone.esp", NoLinks));
		Assert.Empty(Directory.GetFileSystemEntries(_mod));
	}

	[Fact]
	public void NothingToKeepWhenTheModsFolderIsGone()
	{
		// A deleted mod: its files are meant to leave the game, and there's nowhere to put them anyway.
		string game = InGame(@"Data\a.esp", "x");
		Assert.Equal(DeployedFileRescue.Rescue.NothingToKeep, DeployedFileRescue.PutBackIntoMod(game, Path.Combine(_dir, "mods", "deleted"), @"Data\a.esp", NoLinks));
		Assert.False(Directory.Exists(Path.Combine(_dir, "mods", "deleted")));
	}

	[Fact]
	public void AFailureIsThrownSoTheCallerKeepsTheGamesCopy()
	{
		string game = InGame(@"Data\a.esp", "x");
		File.WriteAllText(Path.Combine(_mod, "Sub"), "a file where a folder must go");

		Assert.ThrowsAny<IOException>(() => DeployedFileRescue.PutBackIntoMod(game, _mod, @"Data\Sub\a.esp", NoLinks));
		Assert.True(File.Exists(game));
	}

	// The deployment itself is in the app, which can't be loaded here, so its order is read from the source.

	private static string App(string file)
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		return File.ReadAllText(Path.Combine(dir!.FullName, "KinetixModManager", file));
	}

	[Fact]
	public void TheDeploymentPutsBackBeforeItDeletes()
	{
		string source = App("ModFileSystem.cs");
		int step1 = source.IndexOf("// 1. Remove files we previously deployed", StringComparison.Ordinal);
		int step2 = source.IndexOf("// 2. (Re)link desired files", step1, StringComparison.Ordinal);
		string removal = source[step1..step2];

		int rescue = removal.IndexOf("DeployedFileRescue.PutBackIntoMod(", StringComparison.Ordinal);
		int delete = removal.IndexOf("RobustDeleteFile(destAbs)", StringComparison.Ordinal);
		Assert.True(rescue >= 0 && rescue < delete, "The last copy has to go back to its mod before the game's is deleted.");
		Assert.Contains("enabledNames.Contains(kv.Value)", removal);
		Assert.Contains("rescue == DeployedFileRescue.Rescue.PutBack", removal);
	}

	[Fact]
	public void TheAppHandsOverSwitchedOffModsToo()
	{
		// Without them, switching off a mod that had lost its copy would still delete the last one.
		Assert.Contains("SyncDeployment(gameRoot, ordered, manifest, LogError, forceRelink, overrides, InstalledModFolders())", App("Form1.LoadOrder.cs"));
	}
}
