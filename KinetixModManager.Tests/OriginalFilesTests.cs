using System;
using System.IO;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

public sealed class OriginalFilesTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kmm-originals-" + Guid.NewGuid().ToString("N"));
	private readonly string _game;
	private readonly OriginalFiles _store;

	public OriginalFilesTests()
	{
		_game = Path.Combine(_dir, "game");
		Directory.CreateDirectory(_game);
		_store = new OriginalFiles(Path.Combine(_dir, "originals"));
	}

	public void Dispose()
	{
		try { ModOnDisk.DeleteDirectory(_dir); } catch (IOException) { }
	}

	private string GameFile(string name, string contents)
	{
		string path = Path.Combine(_game, name);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, contents);
		return path;
	}

	[Fact]
	public void TheGamesOwnFileComesBackWhenTheModsCopyGoes()
	{
		string dll = GameFile("binkw64.dll", "the game's");

		_store.Keep(dll, "binkw64.dll");
		Assert.False(File.Exists(dll));

		File.WriteAllText(dll, "the mod's");
		File.Delete(dll);

		Assert.True(_store.Restore(dll, "binkw64.dll"));
		Assert.Equal("the game's", File.ReadAllText(dll));
	}

	[Fact]
	public void ASecondModsCopyIsNotMistakenForTheOriginal()
	{
		string dll = GameFile("binkw64.dll", "the game's");
		_store.Keep(dll, "binkw64.dll");

		File.WriteAllText(dll, "mod A's");
		_store.Keep(dll, "binkw64.dll");
		File.Delete(dll);

		_store.Restore(dll, "binkw64.dll");

		Assert.Equal("the game's", File.ReadAllText(dll));
	}

	[Fact]
	public void NothingToRestoreIsNotAnError()
	{
		Assert.False(_store.Restore(Path.Combine(_game, "a.dll"), "a.dll"));
		Assert.False(File.Exists(Path.Combine(_game, "a.dll")));
	}

	[Fact]
	public void AFileThatIsNotThereIsNotKept()
	{
		_store.Keep(Path.Combine(_game, "never.dll"), "never.dll");

		Assert.False(_store.Restore(Path.Combine(_game, "never.dll"), "never.dll"));
	}

	[Fact]
	public void FilesInSubfoldersKeepTheirPlace()
	{
		string a = GameFile(Path.Combine("Data", "a", "x.dds"), "a");
		string b = GameFile(Path.Combine("Data", "b", "x.dds"), "b");

		_store.Keep(a, Path.Combine("Data", "a", "x.dds"));
		_store.Keep(b, Path.Combine("Data", "b", "x.dds"));
		_store.Restore(a, Path.Combine("Data", "a", "x.dds"));
		_store.Restore(b, Path.Combine("Data", "b", "x.dds"));

		Assert.Equal("a", File.ReadAllText(a));
		Assert.Equal("b", File.ReadAllText(b));
	}

	[Fact]
	public void APathTryingToLeaveTheFolderIsRefused()
	{
		string victim = GameFile("victim.txt", "safe");

		_store.Keep(victim, Path.Combine("..", "..", "elsewhere.txt"));

		Assert.Equal("safe", File.ReadAllText(victim));
	}

	[Fact]
	public void WithNoFolderNothingIsKept()
	{
		string dll = GameFile("a.dll", "x");

		new OriginalFiles("").Keep(dll, "a.dll");

		Assert.True(File.Exists(dll));
	}

	[Fact]
	public void TheManifestCarriesThePlaceButDoesNotSaveIt()
	{
		// Both are about this run. Saved, a stale failure count would be reported on every start.
		string json = Newtonsoft.Json.JsonConvert.SerializeObject(new DeploymentManifest { OriginalsFolder = "x", Failures = 3 });

		Assert.DoesNotContain("OriginalsFolder", json);
		Assert.DoesNotContain("Failures", json);
	}
}
