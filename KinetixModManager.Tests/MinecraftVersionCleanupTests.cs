using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Which Minecraft versions nothing uses — <see cref="MinecraftVersionCleanup"/>. The folders are the real shape of
/// the development machine's <c>.minecraft</c>: a 26.3 that is only a JSON with its jar in the Fabric folder above
/// it, an old 26.2 an official-launcher installation still points at, and two versions nothing uses at all.
/// </summary>
public class MinecraftVersionCleanupTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "kinetix-versions-" + Guid.NewGuid().ToString("N"));

	public MinecraftVersionCleanupTests()
	{
		Version("1.21");
		Version("1.21.4");
		Version("1.21.10");
		Version("26.2");
		Version("26.3", jar: false);
		Version("fabric-loader-0.19.5-26.2", parent: "26.2");
		Version("fabric-loader-0.19.5-26.3", parent: "26.3");
		Version("fabric-loader-0.17.3-1.21.10", parent: "1.21.10", jar: false);

		File.WriteAllText(Path.Combine(_root, "launcher_profiles.json"), """
			{
			  "profiles": {
			    "a": { "name": "", "type": "latest-release", "lastVersionId": "latest-release", "created": "2026-09-11T20:54:24.830Z" },
			    "b": { "name": "fabric-loader-26.2", "type": "custom", "lastVersionId": "fabric-loader-0.19.5-26.2", "created": "2026-09-12T10:00:00.000Z" },
			    "c": { "name": "fabric-loader-26.3", "type": "custom", "lastVersionId": "fabric-loader-0.19.5-26.3", "created": "2026-09-15T10:00:00.000Z" }
			  },
			  "version": 3
			}
			""");
	}

	public void Dispose()
	{
		try { Directory.Delete(_root, true); } catch { }
	}

	private void Version(string id, string? parent = null, bool jar = true)
	{
		string folder = Path.Combine(_root, "versions", id);
		Directory.CreateDirectory(folder);
		var json = new JObject { ["id"] = id };
		if (parent != null) json["inheritsFrom"] = parent;
		File.WriteAllText(Path.Combine(folder, id + ".json"), json.ToString());
		if (jar) File.WriteAllBytes(Path.Combine(folder, id + ".jar"), new byte[1000]);
	}

	/// <summary>The player plays 26.3 with Fabric; one pack runs 1.21.10.</summary>
	private static readonly string[] InUse = { "fabric-loader-0.19.5-26.3", "fabric-loader-0.17.3-1.21.10" };

	[Fact]
	public void WhatNothingRunsIsOfferedAndWhatSomethingRunsIsNot()
	{
		List<string> unused = MinecraftVersionCleanup.FindUnused(_root, InUse).Select(v => v.Id).ToList();

		Assert.Equal(new[] { "1.21", "1.21.4", "26.2", "fabric-loader-0.19.5-26.2" }, unused);
	}

	[Fact]
	public void TheVersionAFabricSetupIsBuiltOnIsNeverOffered()
	{
		// 26.3 is only a JSON here: its jar lives in the Fabric folder. Removing it would still leave a Fabric
		// version inheriting from nothing, and a game that cannot start.
		List<string> unused = MinecraftVersionCleanup.FindUnused(_root, InUse).Select(v => v.Id).ToList();

		Assert.DoesNotContain("26.3", unused);
		Assert.DoesNotContain("1.21.10", unused);
	}

	[Fact]
	public void TheLaunchersInstallationIsNamedOnTheFabricFolderAndTheVersionUnderIt()
	{
		IReadOnlyList<UnusedMinecraftVersion> unused = MinecraftVersionCleanup.FindUnused(_root, InUse);

		Assert.Equal(new[] { "fabric-loader-26.2" }, unused.Single(v => v.Id == "fabric-loader-0.19.5-26.2").LauncherInstallations);
		Assert.Equal(new[] { "fabric-loader-26.2" }, unused.Single(v => v.Id == "26.2").LauncherInstallations);
		Assert.Empty(unused.Single(v => v.Id == "1.21.4").LauncherInstallations);
	}

	[Fact]
	public void AVersionsSizeIsWhatItsFolderHolds() =>
		Assert.True(MinecraftVersionCleanup.FindUnused(_root, InUse).Single(v => v.Id == "1.21").Bytes >= 1000);

	[Fact]
	public void NothingInUseMeansEverythingIsOffered() =>
		Assert.Equal(8, MinecraftVersionCleanup.FindUnused(_root, Array.Empty<string>()).Count);

	[Fact]
	public void RemovingInstallationsLeavesTheRestOfTheLaunchersFileAlone()
	{
		int removed = MinecraftVersionCleanup.RemoveLauncherInstallations(_root, new[] { "fabric-loader-0.19.5-26.2", "26.2" });

		JObject after = FabricInstaller.ParsePreservingDates(File.ReadAllText(Path.Combine(_root, "launcher_profiles.json")));
		string raw = File.ReadAllText(Path.Combine(_root, "launcher_profiles.json"));

		Assert.Equal(1, removed);
		Assert.Null(after["profiles"]!["b"]);
		Assert.NotNull(after["profiles"]!["a"]);
		Assert.NotNull(after["profiles"]!["c"]);
		// The launcher's own timestamps are written back exactly as it wrote them. See FabricInstaller.ParsePreservingDates.
		Assert.Contains("2026-09-15T10:00:00.000Z", raw);
	}

	[Fact]
	public void NothingToRemoveLeavesTheFileUntouched()
	{
		string path = Path.Combine(_root, "launcher_profiles.json");
		DateTime before = File.GetLastWriteTimeUtc(path);

		Assert.Equal(0, MinecraftVersionCleanup.RemoveLauncherInstallations(_root, new[] { "1.21" }));
		Assert.Equal(before, File.GetLastWriteTimeUtc(path));
	}
}
