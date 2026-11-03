using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>Moving Minecraft versions has broken a real game twice, so what happens to each mod is pinned down here.</summary>
public sealed class MinecraftMovePlanTests
{
	private static GameMod Mod(string id) => new() { Name = id, UniqueId = id };

	private readonly GameMod _united = Mod("unitedminecraft");
	private readonly GameMod _sodium = Mod("sodium");
	private readonly GameMod _chill = Mod("chill");
	private readonly GameMod _stuck = Mod("stuck");

	private MinecraftMovePlan Plan() => MinecraftMovePlan.Decide(
		new[] { _united, _sodium, _chill, _stuck },
		hasBuild: m => m == _united || m == _sodium,
		survives: m => m == _chill);

	[Fact]
	public void AModPublishedOnGitHubIsUpdatedNotSwitchedOff()
	{
		// The access mod lives on GitHub alone. Sorted by Modrinth, it was switched off by a move offered because it had
		// a release for the new version, and the game went silent.
		MinecraftMovePlan plan = Plan();

		Assert.Contains(_united, plan.ToUpdate);
		Assert.DoesNotContain(_united, plan.ToSwitchOff);
	}

	[Fact]
	public void EveryModLandsInExactlyOnePile()
	{
		MinecraftMovePlan plan = Plan();

		List<GameMod> all = plan.ToUpdate.Concat(plan.FineAsTheyAre).Concat(plan.ToSwitchOff).ToList();
		Assert.Equal(4, all.Count);
		Assert.Equal(4, all.Distinct().Count());
	}

	[Fact]
	public void AModWithABuildIsUpdatedEvenIfItWouldAlsoSurvive()
	{
		MinecraftMovePlan plan = MinecraftMovePlan.Decide(new[] { _sodium }, _ => true, _ => true);

		Assert.Contains(_sodium, plan.ToUpdate);
		Assert.Empty(plan.FineAsTheyAre);
	}

	[Fact]
	public void AModThatCannotRunAndHasNoBuildIsSwitchedOff()
	{
		Assert.Equal(new[] { _stuck }, Plan().ToSwitchOff);
	}

	[Fact]
	public void AFailedUpdateThatCannotRunMustGoOff()
	{
		// Left on, it stops the game starting. This is the case that used to end in "Moved", then a Java refusal.
		IReadOnlyList<GameMod> off = MinecraftMovePlan.FailedThatMustGoOff(new[] { _sodium, _chill }, m => m == _chill);

		Assert.Equal(new[] { _sodium }, off);
	}

	[Fact]
	public void SwitchingOffTheAccessModIsNoticed()
	{
		MinecraftMovePlan plan = MinecraftMovePlan.Decide(new[] { _united }, _ => false, _ => false);

		Assert.True(plan.SwitchesOffAccessMod("UnitedMinecraft"));
		Assert.False(plan.SwitchesOffAccessMod("sodium"));
		Assert.False(plan.SwitchesOffAccessMod(""));
	}
}

/// <summary>A jar install that could not remove the old copy has not succeeded: Fabric refuses two copies of a mod.</summary>
public sealed class JarInstallTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kmm-jar-" + Guid.NewGuid().ToString("N"));
	private readonly string _mods;

	public JarInstallTests()
	{
		_mods = Path.Combine(_dir, "mods");
		Directory.CreateDirectory(_mods);
	}

	public void Dispose()
	{
		try { ModOnDisk.DeleteDirectory(_dir); } catch (IOException) { }
	}

	private string Jar(string folder, string file, string id)
	{
		Directory.CreateDirectory(folder);
		string path = Path.Combine(folder, file);
		using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
		using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
			writer.Write($"{{\"schemaVersion\":1,\"id\":\"{id}\",\"version\":\"1\",\"name\":\"{id}\"}}");
		return path;
	}

	[Fact]
	public void TheOldCopyIsReplaced()
	{
		string old = Jar(_mods, "sodium-1.jar", "sodium");
		string fresh = Jar(Path.Combine(_dir, "dl"), "sodium-2.jar", "sodium");

		ModInstaller.InstallResult result = ModInstaller.InstallFile(fresh, _mods);

		Assert.False(File.Exists(old));
		Assert.Empty(result.CouldNotRemove);
		Assert.True(File.Exists(Path.Combine(_mods, "sodium-2.jar")));
	}

	[Fact]
	public void AnOldCopyThatCannotBeRemovedIsReported()
	{
		if (!OperatingSystem.IsWindows()) return;

		string old = Jar(_mods, "sodium-1.jar", "sodium");
		string fresh = Jar(Path.Combine(_dir, "dl"), "sodium-2.jar", "sodium");

		// ExistingCopies reads it, so the lock allows reading and refuses only the delete.
		using (new FileStream(old, FileMode.Open, FileAccess.Read, FileShare.Read))
		{
			ModInstaller.InstallResult result = ModInstaller.InstallFile(fresh, _mods);

			Assert.Equal(new[] { "sodium-1.jar" }, result.CouldNotRemove);
		}
	}
}
