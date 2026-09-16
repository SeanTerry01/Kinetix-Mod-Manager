using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Installing Minecraft itself, so that moving to a new version does not send the player back to the official
/// launcher.
///
/// <para>
/// The JSON in here is the real shape, trimmed: the manifest entries, the downloads block and the asset index
/// are copied from what Mojang actually serves for 26.3, and the runtime manifest from the windows-x64
/// java-runtime-delta one. The parts worth pinning are the ones that are silently wrong rather than loudly
/// wrong — an asset bucketed under the wrong two characters downloads fine, verifies fine, and leaves the game
/// missing a sound.
/// </para>
/// </summary>
public class MinecraftGameFilesTests
{
	private const string Root = @"C:\mc";

	private static bool Nothing(string path) => false;
	private static bool Everything(string path) => true;

	// -------------------------------------------------------------------------
	// The version manifest
	// -------------------------------------------------------------------------

	private static JObject Manifest() => JObject.Parse("""
	{
	  "latest": { "release": "26.3", "snapshot": "26w37a" },
	  "versions": [
	    { "id": "26w37a", "type": "snapshot", "url": "https://piston-meta.mojang.com/v1/packages/aaa/26w37a.json", "sha1": "aaa" },
	    { "id": "26.3",   "type": "release",  "url": "https://piston-meta.mojang.com/v1/packages/bbb/26.3.json",   "sha1": "bbb" },
	    { "id": "1.21.8", "type": "release",  "url": "https://piston-meta.mojang.com/v1/packages/ccc/1.21.8.json", "sha1": "ccc" }
	  ]
	}
	""");

	[Fact]
	public void TheNewestReleaseIsReadFromTheManifest() =>
		Assert.Equal("26.3", MinecraftGameFiles.LatestRelease(Manifest()));

	[Fact]
	public void AVersionIsFoundByIdWithItsUrlAndChecksum()
	{
		MinecraftVersionSource? found = MinecraftGameFiles.VersionSource(Manifest(), "26.3");
		Assert.NotNull(found);
		MinecraftVersionSource source = found.Value;

		Assert.Equal("26.3", source.Id);
		Assert.Equal("https://piston-meta.mojang.com/v1/packages/bbb/26.3.json", source.Url);
		Assert.Equal("bbb", source.Sha1);
		Assert.Equal("release", source.Type);
	}

	[Fact]
	public void AVersionTheManifestHasNeverHeardOfIsNotAnError()
	{
		// A Fabric profile lives in the same versions folder and is not something Mojang publishes. The caller
		// has to be able to tell "not ours to fetch" from "failed to fetch".
		Assert.Null(MinecraftGameFiles.VersionSource(Manifest(), "fabric-loader-0.17.2-26.3"));
		Assert.Null(MinecraftGameFiles.VersionSource(Manifest(), ""));
	}

	// -------------------------------------------------------------------------
	// The game jar
	// -------------------------------------------------------------------------

	private static JObject VersionJson() => JObject.Parse("""
	{
	  "id": "26.3",
	  "javaVersion": { "component": "java-runtime-epsilon", "majorVersion": 25 },
	  "downloads": {
	    "client": {
	      "sha1": "9a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b",
	      "size": 43210987,
	      "url": "https://piston-data.mojang.com/v1/objects/9a2b/client.jar"
	    },
	    "server": {
	      "sha1": "0000000000000000000000000000000000000000",
	      "size": 55000000,
	      "url": "https://piston-data.mojang.com/v1/objects/0000/server.jar"
	    }
	  },
	  "assetIndex": {
	    "id": "26",
	    "sha1": "1234567890abcdef1234567890abcdef12345678",
	    "size": 462144,
	    "url": "https://piston-meta.mojang.com/v1/packages/1234/26.json"
	  }
	}
	""");

	[Fact]
	public void TheClientJarIsFetchedAndTheServerJarIsNot()
	{
		MinecraftDownload? found = MinecraftGameFiles.MissingClientJar(VersionJson(), "26.3", Root, Nothing);
		Assert.NotNull(found);
		MinecraftDownload jar = found.Value;

		Assert.Equal(Path.Combine("versions", "26.3", "26.3.jar"), jar.RelativePath);
		Assert.Equal("https://piston-data.mojang.com/v1/objects/9a2b/client.jar", jar.Url);
		Assert.Equal("9a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b", jar.Sha1);
		Assert.Equal(43210987L, jar.Bytes);
		Assert.Equal(MinecraftFileKind.ClientJar, jar.Kind);
	}

	[Fact]
	public void AJarAlreadyOnDiskIsNotFetchedAgain() =>
		Assert.Null(MinecraftGameFiles.MissingClientJar(VersionJson(), "26.3", Root, Everything));

	[Fact]
	public void AJarTheLauncherCopiedIntoTheFabricFolderCountsAsPresent()
	{
		// Found by running this against the real .minecraft rather than by reading the code: 26.3 was reported
		// as missing its 41 MB jar on a machine that plays 26.3 every day. The official launcher copies the
		// game jar into the Fabric profile's folder on first run and leaves versions\26.3\ holding only the
		// JSON, and MinecraftLauncher.GameJarPath launches from either. Checking the plain name alone would
		// have re-downloaded 41 MB of a file already on the disk.
		string fabricJar = Path.Combine(Root, "versions", "fabric-loader-0.19.5-26.3", "fabric-loader-0.19.5-26.3.jar");

		Assert.Null(MinecraftGameFiles.MissingClientJar(VersionJson(), "26.3", Root,
			p => p.Equals(fabricJar, StringComparison.OrdinalIgnoreCase), "fabric-loader-0.19.5-26.3"));

		// Without being told about the profile it is genuinely missing, which is the answer a fresh install
		// needs -- so the fallback must not become a blanket excuse for never fetching the jar.
		Assert.NotNull(MinecraftGameFiles.MissingClientJar(VersionJson(), "26.3", Root,
			p => p.Equals(fabricJar, StringComparison.OrdinalIgnoreCase)));
	}

	[Fact]
	public void TheJarIsLookedForWhereThatVersionKeepsIt()
	{
		// The presence check has to ask about the version's own folder, not merely be told "something exists".
		var asked = new List<string>();
		MinecraftGameFiles.MissingClientJar(VersionJson(), "26.3", Root, p => { asked.Add(p); return false; });

		Assert.Equal(new[] { Path.Combine(Root, "versions", "26.3", "26.3.jar") }, asked);
	}

	// -------------------------------------------------------------------------
	// Assets
	// -------------------------------------------------------------------------

	[Fact]
	public void TheAssetIndexIsFetchedIntoTheFolderTheGameReadsItFrom()
	{
		MinecraftDownload? found = MinecraftGameFiles.MissingAssetIndex(VersionJson(), Root, Nothing);
		Assert.NotNull(found);
		MinecraftDownload index = found.Value;

		Assert.Equal(Path.Combine("assets", "indexes", "26.json"), index.RelativePath);
		Assert.Equal("https://piston-meta.mojang.com/v1/packages/1234/26.json", index.Url);
		Assert.Equal(MinecraftFileKind.AssetIndex, index.Kind);
	}

	[Fact]
	public void TheAssetIndexIdIsTheOneTheLaunchArgumentsUse() =>
		// BuildPlan puts this in ${assets_index_name}. If the two ever disagreed the game would start with an
		// index it had not been given, which is a silent loss of every sound and every translated string.
		Assert.Equal("26", MinecraftGameFiles.AssetIndexId(VersionJson()));

	private static JObject AssetIndexJson() => JObject.Parse("""
	{
	  "objects": {
	    "minecraft/sounds/mob/cow/say1.ogg":   { "hash": "ab12cd34ef56ab78cd90ef12ab34cd56ef7890ab", "size": 15432 },
	    "minecraft/sounds/mob/cow/say2.ogg":   { "hash": "0f9e8d7c6b5a40312f1e0d9c8b7a60514f3e2d1c", "size": 16001 },
	    "minecraft/lang/en_gb.json":           { "hash": "ffeeddccbbaa99887766554433221100ffeeddcc", "size": 812345 },
	    "minecraft/sounds/ui/button/click.ogg": { "hash": "ab12cd34ef56ab78cd90ef12ab34cd56ef7890ab", "size": 15432 }
	  }
	}
	""");

	[Fact]
	public void AnAssetGoesInTheBucketNamedByTheFirstTwoCharactersOfItsHash()
	{
		// Getting this wrong is invisible: the file downloads, verifies and is never found by the game.
		Assert.Equal(Path.Combine("assets", "objects", "ab", "ab12cd34ef56ab78cd90ef12ab34cd56ef7890ab"),
			MinecraftGameFiles.AssetObjectRelativePath("ab12cd34ef56ab78cd90ef12ab34cd56ef7890ab"));

		Assert.Equal("https://resources.download.minecraft.net/ab/ab12cd34ef56ab78cd90ef12ab34cd56ef7890ab",
			MinecraftGameFiles.AssetObjectUrl("ab12cd34ef56ab78cd90ef12ab34cd56ef7890ab"));
	}

	[Fact]
	public void OneAssetServingTwoNamesIsFetchedOnce()
	{
		// The click sound and the cow share a hash in this fixture, as sounds genuinely do share one across
		// events. Deduping by NAME would fetch the same bytes twice; by hash it is one file.
		IReadOnlyList<MinecraftDownload> missing = MinecraftGameFiles.MissingAssets(AssetIndexJson(), Root, Nothing);

		Assert.Equal(3, missing.Count);
		Assert.Single(missing, d => d.Sha1 == "ab12cd34ef56ab78cd90ef12ab34cd56ef7890ab");
	}

	[Fact]
	public void AnAssetsChecksumIsItsOwnHash()
	{
		// Asset objects carry no separate sha1 field, because the name IS the sha1. A verify step that looked
		// for one would find nothing and quietly stop checking.
		foreach (MinecraftDownload asset in MinecraftGameFiles.MissingAssets(AssetIndexJson(), Root, Nothing))
			Assert.EndsWith(asset.Sha1!, asset.RelativePath, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void OnlyTheAssetsThatAreActuallyMissingAreFetched()
	{
		// The point of the whole design: a machine that has played a recent version already holds most of the
		// index, so an update is a handful of files rather than 480 MB.
		string present = Path.Combine(Root, MinecraftGameFiles.AssetObjectRelativePath("ffeeddccbbaa99887766554433221100ffeeddcc"));

		IReadOnlyList<MinecraftDownload> missing =
			MinecraftGameFiles.MissingAssets(AssetIndexJson(), Root, p => p.Equals(present, StringComparison.OrdinalIgnoreCase));

		Assert.Equal(2, missing.Count);
		Assert.DoesNotContain(missing, d => d.Sha1 == "ffeeddccbbaa99887766554433221100ffeeddcc");
	}

	[Fact]
	public void AnIndexWithNothingInItAsksForNothing()
	{
		Assert.Empty(MinecraftGameFiles.MissingAssets(new JObject(), Root, Nothing));
		Assert.Empty(MinecraftGameFiles.MissingAssets(JObject.Parse("""{ "objects": {} }"""), Root, Nothing));
	}

	// -------------------------------------------------------------------------
	// The Java runtime
	// -------------------------------------------------------------------------

	[Fact]
	public void TheJavaComponentComesFromTheVersionAndFallsBackToTheLauncherDefault()
	{
		Assert.Equal("java-runtime-epsilon", MinecraftGameFiles.JavaComponent(VersionJson()));
		Assert.Equal(MinecraftGameFiles.DefaultJavaComponent, MinecraftGameFiles.JavaComponent(new JObject()));
	}

	[Theory]
	[InlineData("windows", "x86_64", "windows-x64")]
	[InlineData("windows", "x86", "windows-x86")]
	[InlineData("windows", "arm64", "windows-arm64")]
	[InlineData("linux", "x86_64", "linux")]
	[InlineData("linux", "x86", "linux-i386")]
	[InlineData("osx", "arm64", "mac-os-arm64")]
	[InlineData("osx", "x86_64", "mac-os")]
	public void TheRuntimeManifestIsKeyedByMojangsOwnPlatformNames(string os, string arch, string expected) =>
		// Not the spelling the version JSONs use, which is the trap: "windows" + "x86_64" there, "windows-x64"
		// here. A wrong key finds no runtime and looks exactly like "Mojang publishes none for your machine".
		Assert.Equal(expected, MinecraftGameFiles.RuntimePlatform(os, arch));

	private static JObject AllRuntimes() => JObject.Parse("""
	{
	  "windows-x64": {
	    "java-runtime-epsilon": [
	      {
	        "availability": { "group": 1, "progress": 100 },
	        "manifest": { "sha1": "dead", "size": 133, "url": "https://piston-meta.mojang.com/v1/packages/dead/windows-x64.json" },
	        "version": { "name": "25.0.1", "released": "2026-08-01T00:00:00+00:00" }
	      }
	    ],
	    "java-runtime-gamma": []
	  },
	  "windows-arm64": { "java-runtime-epsilon": [] }
	}
	""");

	[Fact]
	public void TheRuntimeManifestUrlIsFoundForThisPlatformAndComponent()
	{
		Assert.Equal("https://piston-meta.mojang.com/v1/packages/dead/windows-x64.json",
			MinecraftGameFiles.RuntimeManifestUrl(AllRuntimes(), "windows-x64", "java-runtime-epsilon"));

		Assert.Equal("25.0.1",
			MinecraftGameFiles.RuntimeVersionName(AllRuntimes(), "windows-x64", "java-runtime-epsilon"));
	}

	[Fact]
	public void AComponentMojangPublishesNoBuildOfAnswersEmptyRatherThanThrowing()
	{
		// An empty list is how Mojang says "not for this machine" -- an arm64 Windows with no arm64 build. The
		// caller has to be able to say that plainly instead of retrying a download that will never exist.
		Assert.Equal("", MinecraftGameFiles.RuntimeManifestUrl(AllRuntimes(), "windows-arm64", "java-runtime-epsilon"));
		Assert.Equal("", MinecraftGameFiles.RuntimeManifestUrl(AllRuntimes(), "windows-x64", "java-runtime-gamma"));
		Assert.Equal("", MinecraftGameFiles.RuntimeManifestUrl(AllRuntimes(), "mac-os", "java-runtime-epsilon"));
	}

	private static JObject RuntimeManifest() => JObject.Parse("""
	{
	  "files": {
	    "bin": { "type": "directory" },
	    "bin/javaw.exe": {
	      "type": "file",
	      "executable": true,
	      "downloads": {
	        "lzma": { "sha1": "c0ffee", "size": 40000, "url": "https://piston-data.mojang.com/v1/objects/c0ffee/javaw.exe.lzma" },
	        "raw":  { "sha1": "beef01", "size": 62000, "url": "https://piston-data.mojang.com/v1/objects/beef01/javaw.exe" }
	      }
	    },
	    "lib/modules": {
	      "type": "file",
	      "downloads": {
	        "raw": { "sha1": "beef02", "size": 140000000, "url": "https://piston-data.mojang.com/v1/objects/beef02/modules" }
	      }
	    },
	    "legal/LICENSE": { "type": "link", "target": "../COPYRIGHT" }
	  }
	}
	""");

	[Fact]
	public void TheRawDownloadIsTakenAndNotTheCompressedOne()
	{
		// Taking the lzma one would mean carrying an LZMA decoder to arrive at a file that has to be checked
		// against the RAW sha1 anyway. The two hashes differ, so picking the wrong one fails verification on
		// every single file.
		MinecraftRuntimePlan plan = MinecraftGameFiles.MissingRuntimeFiles(RuntimeManifest(), @"C:\rt", Nothing);

		MinecraftRuntimeFile javaw = Assert.Single(plan.Files, f => f.RelativePath == Path.Combine("bin", "javaw.exe"));
		Assert.Equal("https://piston-data.mojang.com/v1/objects/beef01/javaw.exe", javaw.Url);
		Assert.Equal("beef01", javaw.Sha1);
		Assert.True(javaw.Executable);
	}

	[Fact]
	public void DirectoriesAndLinksAreReportedSeparatelyFromFiles()
	{
		MinecraftRuntimePlan plan = MinecraftGameFiles.MissingRuntimeFiles(RuntimeManifest(), @"C:\rt", Nothing);

		Assert.Equal(new[] { "bin" }, plan.Directories);
		Assert.Equal(2, plan.Files.Count);

		MinecraftRuntimeLink link = Assert.Single(plan.Links);
		Assert.Equal(Path.Combine("legal", "LICENSE"), link.RelativePath);
		Assert.Equal("../COPYRIGHT", link.Target);
	}

	[Fact]
	public void TheDownloadSizeIsKnownBeforeAnyOfItStarts() =>
		// Said out loud before the user commits to it: a runtime is around 180 MB and on a slow line that is a
		// long silence to walk into unwarned.
		Assert.Equal(140062000L, MinecraftGameFiles.MissingRuntimeFiles(RuntimeManifest(), @"C:\rt", Nothing).TotalBytes);

	[Fact]
	public void RuntimeFilesAlreadyOnDiskAreNotFetchedAgain()
	{
		MinecraftRuntimePlan plan = MinecraftGameFiles.MissingRuntimeFiles(RuntimeManifest(), @"C:\rt", Everything);

		Assert.Empty(plan.Files);
		// The folders and links are still reported: they cost nothing to remake and an interrupted install can
		// leave a folder half-built.
		Assert.Single(plan.Directories);
		Assert.Single(plan.Links);
	}

	[Fact]
	public void AManagerInstalledRuntimeIsLaidOutTheWayTheLauncherLaysOutItsOwn() =>
		// MinecraftLauncher.FindBundledJava walks runtime\<component>\<platform>\<component>\bin. Ours has to
		// match that shape or the runtime we just spent 180 MB fetching is invisible to the thing that needs it.
		Assert.Equal(Path.Combine(Root, "runtime", "java-runtime-epsilon", "windows-x64", "java-runtime-epsilon"),
			MinecraftGameFiles.RuntimeRootFor(Root, "java-runtime-epsilon", "windows-x64"));
}
