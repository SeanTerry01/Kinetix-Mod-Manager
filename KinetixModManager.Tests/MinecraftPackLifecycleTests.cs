using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using KinetixModManager;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Everything after a modpack is installed: choosing which version to install or update to, what an update does
/// to the pack's files, the worlds that can be copied into one, and bringing packs across from the Modrinth App.
/// </summary>
public class MinecraftPackLifecycleTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kinetix-packlife-" + Guid.NewGuid().ToString("N"));

	public MinecraftPackLifecycleTests() => Directory.CreateDirectory(_dir);

	public void Dispose()
	{
		SqliteConnection.ClearAllPools();
		try { Directory.Delete(_dir, true); } catch { }
	}

	// -------------------------------------------------------------------------
	// Versions: which to install, which to offer as an update
	// -------------------------------------------------------------------------

	/// <summary>The shape of the real vi-access listing: an alpha newer than the newest release.</summary>
	private static IReadOnlyList<ModpackVersion> ViAccessVersions() => ModrinthService.ParseModpackVersions(JArray.Parse("""
		[
		  { "id": "gduBLqKs", "project_id": "TAT3EDpw", "version_number": "2.5.0-alpha.1", "version_type": "alpha",
		    "game_versions": ["1.21.11"], "loaders": ["fabric"], "date_published": "2026-01-20T04:43:23Z",
		    "files": [ { "filename": "VI-2.5.0-alpha.1.mrpack", "url": "https://cdn.modrinth.com/data/TAT3EDpw/versions/gduBLqKs/a.mrpack", "primary": true, "size": 4615105, "hashes": { "sha1": "aa" } } ] },
		  { "id": "Eg0u2Imb", "project_id": "TAT3EDpw", "version_number": "2.4.2", "version_type": "release",
		    "game_versions": ["1.21.10"], "loaders": ["fabric"], "date_published": "2025-11-15T00:00:00Z",
		    "files": [ { "filename": "VI-2.4.2.mrpack", "url": "https://cdn.modrinth.com/data/TAT3EDpw/versions/Eg0u2Imb/b.mrpack", "primary": true, "size": 4423642, "hashes": { "sha1": "bb" } } ] },
		  { "id": "bxUmksPh", "project_id": "TAT3EDpw", "version_number": "2.4.1", "version_type": "release",
		    "game_versions": ["1.21.10"], "loaders": ["fabric"], "date_published": "2025-10-30T00:00:00Z",
		    "files": [ { "filename": "VI-2.4.1.mrpack", "url": "https://cdn.modrinth.com/data/TAT3EDpw/versions/bxUmksPh/c.mrpack", "primary": true, "size": 4293261, "hashes": { "sha1": "cc" } } ] },
		  { "id": "quiltOnly", "project_id": "TAT3EDpw", "version_number": "9.9.9", "version_type": "release",
		    "game_versions": ["1.21.11"], "loaders": ["quilt"], "date_published": "2026-02-01T00:00:00Z",
		    "files": [ { "filename": "q.mrpack", "url": "https://cdn.modrinth.com/data/TAT3EDpw/versions/quiltOnly/q.mrpack", "primary": true, "size": 1, "hashes": { "sha1": "dd" } } ] },
		  { "id": "noPack", "project_id": "TAT3EDpw", "version_number": "0.1", "version_type": "release",
		    "game_versions": ["1.21.1"], "loaders": ["fabric"], "date_published": "2024-01-01T00:00:00Z",
		    "files": [ { "filename": "readme.txt", "url": "https://cdn.modrinth.com/x", "primary": true, "size": 1 } ] }
		]
		"""));

	[Fact]
	public void AVersionWithNoPackFileIsLeftOut()
	{
		IReadOnlyList<ModpackVersion> versions = ViAccessVersions();

		Assert.DoesNotContain(versions, v => v.Id == "noPack");
		Assert.Equal("1.21.10", versions.Single(v => v.Id == "Eg0u2Imb").MinecraftVersion);
	}

	[Fact]
	public void InstallingPicksTheNewestFabricReleaseOverANewerAlpha() =>
		Assert.Equal("2.4.2", ModrinthService.ChooseVersionToInstall(ViAccessVersions())?.VersionNumber);

	[Fact]
	public void APackWithOnlyTestBuildsInstallsTheNewestOne()
	{
		IReadOnlyList<ModpackVersion> alphasOnly = ViAccessVersions().Where(v => !v.IsRelease).ToList();

		Assert.Equal("2.5.0-alpha.1", ModrinthService.ChooseVersionToInstall(alphasOnly)?.VersionNumber);
	}

	[Fact]
	public void AReleaseIsOfferedOnlyANewerFabricRelease() =>
		Assert.Equal("2.4.2", ModrinthService.ChooseUpdate(ViAccessVersions(), "bxUmksPh")?.VersionNumber);

	[Fact]
	public void TheNewestReleaseIsUpToDateEvenWithANewerAlphaAbout() =>
		Assert.Null(ModrinthService.ChooseUpdate(ViAccessVersions(), "Eg0u2Imb"));

	[Fact]
	public void AVersionItsMakerRemovedIsNotOfferedAnything() =>
		Assert.Null(ModrinthService.ChooseUpdate(ViAccessVersions(), "gone"));

	[Fact]
	public void ANewerVersionForAnotherLoaderIsNotOffered() =>
		// The quilt-only 9.9.9 is the only thing newer than the alpha, and it is not for Fabric.
		Assert.Null(ModrinthService.ChooseUpdate(ViAccessVersions(), "gduBLqKs"));

	[Fact]
	public void SomeoneOnATestBuildIsOfferedTheReleaseThatFollowsIt()
	{
		IReadOnlyList<ModpackVersion> versions = ModrinthService.ParseModpackVersions(JArray.Parse("""
			[
			  { "id": "rel", "version_number": "2.4.2", "version_type": "release", "game_versions": ["1.21.10"],
			    "loaders": ["fabric"], "date_published": "2025-11-15T00:00:00Z",
			    "files": [ { "filename": "r.mrpack", "url": "https://cdn.modrinth.com/data/p/versions/rel/r.mrpack", "primary": true } ] },
			  { "id": "beta", "version_number": "2.4.2-beta.1", "version_type": "beta", "game_versions": ["1.21.10"],
			    "loaders": ["fabric"], "date_published": "2025-11-01T00:00:00Z",
			    "files": [ { "filename": "b.mrpack", "url": "https://cdn.modrinth.com/data/p/versions/beta/b.mrpack", "primary": true } ] }
			]
			"""));

		Assert.Equal("2.4.2", ModrinthService.ChooseUpdate(versions, "beta")?.VersionNumber);
	}

	[Fact]
	public void AModpackSearchResultSaysSoAndNamesItsMinecraft()
	{
		(List<GameMod> results, int total) = ModrinthService.ParseSearch(JObject.Parse("""
			{ "total_hits": 1, "hits": [ { "slug": "vi-access", "project_id": "TAT3EDpw", "project_type": "modpack",
			  "title": "Visually Impaired Access Mods+Fabric", "description": "Accessibility mods", "downloads": 5060,
			  "follows": 40, "versions": ["1.21.1", "1.21.10", "1.21.11"] } ] }
			"""));

		GameMod pack = results.Single();
		Assert.True(pack.IsModpack);
		Assert.Contains("Modpack for Minecraft 1.21.11.", pack.ToString());
		Assert.Equal(1, total);
	}

	[Fact]
	public void AModSearchResultIsNotAModpack()
	{
		(List<GameMod> results, _) = ModrinthService.ParseSearch(JObject.Parse("""
			{ "hits": [ { "slug": "sodium", "project_type": "mod", "title": "Sodium" } ] }
			"""));

		Assert.False(results.Single().IsModpack);
		Assert.DoesNotContain("Modpack", results.Single().ToString());
	}

	// -------------------------------------------------------------------------
	// What an update does to the pack's files
	// -------------------------------------------------------------------------

	[Theory]
	[InlineData("https://cdn.modrinth.com/data/P7dR8mSH/versions/DdVHbeR1/fabric-api.jar", "P7dR8mSH")]
	[InlineData("https://github.com/o/r/releases/download/v1/a.jar", "")]
	[InlineData("https://cdn.modrinth.com/other/P7dR8mSH/x.jar", "")]
	[InlineData("not a url", "")]
	public void AFilesProjectIsReadFromItsModrinthAddress(string url, string project) =>
		Assert.Equal(project, MinecraftModpacks.ProjectFromUrl(url));

	private static MrpackFile File(string path, string sha1, string project) => new()
	{
		RelativePath = path.Replace('/', Path.DirectorySeparatorChar),
		Sha1 = sha1,
		Url = $"https://cdn.modrinth.com/data/{project}/versions/v/{Path.GetFileName(path)}",
		Size = 100
	};

	private static MinecraftPack Installed() => new()
	{
		Name = "Pack",
		ProvidedFiles = new List<string>
		{
			"mods/sodium-0.6.13.jar", "mods/fabric-api.jar", "mods/dropped.jar", "config/sodium.json", "options.txt"
		},
		FileProjects = new Dictionary<string, string>
		{
			["mods/sodium-0.6.13.jar"] = "AANobbMI", ["mods/fabric-api.jar"] = "P7dR8mSH", ["mods/dropped.jar"] = "gone"
		}
	};

	private static ModpackUpdatePlan Plan(Func<string, string?> sha1, Func<string, bool> disabled)
	{
		var index = new MrpackIndex
		{
			Files = new[]
			{
				File("mods/sodium-0.6.14.jar", "new-sodium", "AANobbMI"),   // a new version under a new name
				File("mods/fabric-api.jar", "same-api", "P7dR8mSH"),        // unchanged
				File("mods/brand-new.jar", "brand-new", "NEWPROJ")          // added
			}
		};
		var overrides = new OverridePlan
		{
			Files = new[] { ("overrides/config/sodium.json", Path.Combine("config", "sodium.json")), ("overrides/options.txt", "options.txt") }
		};

		return MinecraftModpacks.PlanUpdate(Installed(), index, overrides, sha1, disabled);
	}

	[Fact]
	public void AFileAlreadyOnDiskWithTheRightChecksumIsNotDownloadedAgain()
	{
		ModpackUpdatePlan plan = Plan(p => p == "mods/fabric-api.jar" ? "same-api" : null, _ => false);

		Assert.Equal(1, plan.Unchanged);
		Assert.DoesNotContain(plan.Download, f => f.Sha1 == "same-api");
		Assert.Equal(2, plan.Download.Count);
	}

	[Fact]
	public void WhatTheOldVersionHadAndTheNewOneDoesNotIsRemoved()
	{
		ModpackUpdatePlan plan = Plan(_ => null, _ => false);

		Assert.Contains("mods/sodium-0.6.13.jar", plan.Remove);
		Assert.Contains("mods/dropped.jar", plan.Remove);
		Assert.DoesNotContain("mods/fabric-api.jar", plan.Remove);
	}

	[Fact]
	public void ThePlayersOptionsAreNeverRemovedOrOverwritten()
	{
		ModpackUpdatePlan plan = Plan(_ => null, _ => false);

		Assert.DoesNotContain("options.txt", plan.Remove);
		Assert.DoesNotContain(plan.Bundled.Files, o => o.RelativePath == "options.txt");
		Assert.Contains(plan.Bundled.Files, o => o.RelativePath == Path.Combine("config", "sodium.json"));
	}

	[Fact]
	public void ANewVersionThatStopsShippingOptionsStillLeavesThePlayersOptions()
	{
		// The old version provided options.txt; the new one does not mention it at all. By the ordinary rule it
		// would be "removed by the update" — and with it every setting and key the player has changed since.
		var index = new MrpackIndex { Files = new[] { File("mods/fabric-api.jar", "same-api", "P7dR8mSH") } };

		ModpackUpdatePlan plan = MinecraftModpacks.PlanUpdate(Installed(), index, new OverridePlan(), _ => null, _ => false);

		Assert.DoesNotContain("options.txt", plan.Remove);
		Assert.Contains("mods/dropped.jar", plan.Remove);
	}

	[Fact]
	public void AModSwitchedOffStaysOffWhenItsNewVersionHasANewName()
	{
		// sodium-0.6.13.jar was switched off. Its replacement is sodium-0.6.14.jar — a different path, the same
		// Modrinth project. This is exactly the bug where an updated mod came back switched on.
		ModpackUpdatePlan plan = Plan(_ => null, p => p == "mods/sodium-0.6.13.jar");

		Assert.Contains("mods/sodium-0.6.14.jar", plan.KeepDisabled);
		Assert.DoesNotContain("mods/brand-new.jar", plan.KeepDisabled);
	}

	[Fact]
	public void AModSwitchedOffUnderTheSameNameStaysOff()
	{
		ModpackUpdatePlan plan = Plan(p => p == "mods/fabric-api.jar" ? "same-api" : null, p => p == "mods/fabric-api.jar");

		Assert.Contains("mods/fabric-api.jar", plan.KeepDisabled);
	}

	[Fact]
	public void APackModThePlayerUpdatedThemselvesIsReplacedNotKeptBesideThePacksCopy()
	{
		// The player updated Sodium inside the pack: the pack's sodium-0.6.13.jar became sodium-0.6.14.jar, a name
		// the pack never listed. Kept as "something the player added", it would sit beside the pack's own Sodium
		// after the next pack update, and Fabric refuses to start with two copies of one mod.
		var packJars = new Dictionary<string, string>
		{
			["mods/sodium-0.6.13.jar"] = "sodium", ["mods/fabric-api.jar"] = "fabric-api"
		};
		var others = new Dictionary<string, string>
		{
			["mods/sodium-0.6.14.jar"] = "sodium",                 // the player's own update of a pack mod
			["mods/fabric-api-newer.jar.disabled"] = "fabric-api", // the same, switched off
			["mods/my-own-mod.jar"] = "my_own_mod",                // genuinely the player's: stays
			["mods/broken.jar"] = ""                               // unreadable: never matched to anything
		};

		IReadOnlyList<(string Path, bool WasDisabled)> superseded = MinecraftModpacks.FindSuperseded(packJars, others, ".disabled");

		Assert.Equal(new[] { ("mods/fabric-api-newer.jar.disabled", true), ("mods/sodium-0.6.14.jar", false) }, superseded);
	}

	[Fact]
	public void ModIdsMatchWhateverTheirCase() =>
		Assert.Single(MinecraftModpacks.FindSuperseded(
			new Dictionary<string, string> { ["mods/a.jar"] = "Sodium" },
			new Dictionary<string, string> { ["mods/b.jar"] = "sodium" }, ".disabled"));

	[Fact]
	public void TheRecordAfterwardsListsTheNewVersionsFilesAndProjects()
	{
		ModpackUpdatePlan plan = Plan(_ => null, _ => false);

		Assert.Contains("mods/sodium-0.6.14.jar", plan.ProvidedFiles);
		Assert.DoesNotContain("mods/sodium-0.6.13.jar", plan.ProvidedFiles);
		Assert.Equal("NEWPROJ", plan.FileProjects["mods/brand-new.jar"]);
	}

	// -------------------------------------------------------------------------
	// Worlds
	// -------------------------------------------------------------------------

	/// <summary>Writes a gzipped level.dat holding just what the reader looks for.</summary>
	private static void WriteLevelDat(string worldFolder, string levelName, string versionName, int dataVersion)
	{
		Directory.CreateDirectory(worldFolder);
		using var buffer = new MemoryStream();
		using (var w = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
		{
			void Name(string n) { byte[] b = Encoding.UTF8.GetBytes(n); w.Write((byte)(b.Length >> 8)); w.Write((byte)b.Length); w.Write(b); }
			void Int(int v) { w.Write((byte)(v >> 24)); w.Write((byte)(v >> 16)); w.Write((byte)(v >> 8)); w.Write((byte)v); }

			w.Write((byte)10); Name("");                    // root compound
			w.Write((byte)10); Name("Data");                //   Data
			w.Write((byte)8); Name("LevelName"); Name(levelName);
			w.Write((byte)3); Name("DataVersion"); Int(dataVersion);
			w.Write((byte)9); Name("ServerBrands"); w.Write((byte)8); Int(1); Name("fabric");   // a list, to be read past
			w.Write((byte)10); Name("Version");             //     Version
			w.Write((byte)8); Name("Name"); Name(versionName);
			w.Write((byte)0);                               //     end Version
			w.Write((byte)0);                               //   end Data
			w.Write((byte)0);                               // end root
		}

		using FileStream file = System.IO.File.Create(Path.Combine(worldFolder, "level.dat"));
		using var gzip = new GZipStream(file, CompressionLevel.Optimal);
		gzip.Write(buffer.ToArray());
	}

	[Fact]
	public void AWorldsNameAndLastVersionAreReadFromItsLevelDat()
	{
		WriteLevelDat(Path.Combine(_dir, "saves", "Silky"), "Silky World", "26.3", 5023);

		MinecraftWorld world = MinecraftWorlds.FindIn(_dir).Single();

		Assert.Equal("Silky World", world.Name);
		Assert.Equal("26.3", world.LastPlayedVersion);
		Assert.Equal(5023, world.DataVersion);
	}

	[Fact]
	public void AFolderWithoutALevelDatIsNotAWorld()
	{
		Directory.CreateDirectory(Path.Combine(_dir, "saves", "Empty"));

		Assert.Empty(MinecraftWorlds.FindIn(_dir));
	}

	[Fact]
	public void AnUnreadableLevelDatStillGivesTheWorldItsFolderName()
	{
		string folder = Path.Combine(_dir, "saves", "Broken");
		Directory.CreateDirectory(folder);
		System.IO.File.WriteAllText(Path.Combine(folder, "level.dat"), "not gzip");

		MinecraftWorld world = MinecraftWorlds.FindIn(_dir).Single();

		Assert.Equal("Broken", world.Name);
		Assert.Equal("", world.LastPlayedVersion);
	}

	private static readonly JObject Manifest = JObject.Parse("""
		{ "versions": [
		  { "id": "26.3", "releaseTime": "2026-09-01T00:00:00+00:00" },
		  { "id": "1.21.10", "releaseTime": "2025-10-07T00:00:00+00:00" },
		  { "id": "1.21.5", "releaseTime": "2025-03-25T00:00:00+00:00" } ] }
		""");

	[Theory]
	[InlineData("26.3", "1.21.10", true)]       // the new numbering is newer, whatever the digits say
	[InlineData("1.21.5", "1.21.10", false)]    // and 1.21.5 is older than 1.21.10, whatever the digits say
	[InlineData("1.21.10", "1.21.10", false)]
	public void WhichMinecraftIsNewerComesFromReleaseDates(string version, string than, bool newer) =>
		Assert.Equal(newer, MinecraftWorlds.IsNewer(version, than, Manifest));

	[Fact]
	public void AVersionNotInTheManifestCannotBeCompared() =>
		Assert.Null(MinecraftWorlds.IsNewer("99.9", "1.21.10", Manifest));

	[Fact]
	public void CopyingAWorldLeavesTheOriginalAndSkipsTheLockFile()
	{
		string source = Path.Combine(_dir, "own", "saves", "Silky");
		WriteLevelDat(source, "Silky World", "26.3", 5023);
		System.IO.File.WriteAllText(Path.Combine(source, "session.lock"), "lock");
		Directory.CreateDirectory(Path.Combine(source, "region"));
		System.IO.File.WriteAllText(Path.Combine(source, "region", "r.0.0.mca"), "chunks");

		MinecraftWorld world = MinecraftWorlds.FindIn(Path.Combine(_dir, "own")).Single();
		string pack = Path.Combine(_dir, "pack");
		string first = MinecraftWorlds.CopyInto(world, pack);
		string second = MinecraftWorlds.CopyInto(world, pack);

		Assert.True(System.IO.File.Exists(Path.Combine(source, "session.lock")));
		Assert.Equal("chunks", System.IO.File.ReadAllText(Path.Combine(first, "region", "r.0.0.mca")));
		Assert.False(System.IO.File.Exists(Path.Combine(first, "session.lock")));
		Assert.Equal("Silky (2)", Path.GetFileName(second));
	}

	// -------------------------------------------------------------------------
	// A Modrinth key
	// -------------------------------------------------------------------------

	[Fact]
	public void TheKeysOwnerIsReadFromTheUserRecord()
	{
		Assert.Equal(new ModrinthUser("abc123", "Sean"),
			ModrinthAccount.ParseUser(JObject.Parse("""{ "id": "abc123", "username": "Sean", "email": null }""")));
		Assert.Null(ModrinthAccount.ParseUser(new JObject()));
	}

	[Fact]
	public void FollowedProjectsBecomeResultsAndOnlyModsAndPacksAreKept()
	{
		List<GameMod> followed = ModrinthAccount.ParseProjects(JArray.Parse("""
			[
			  { "id": "TAT3EDpw", "slug": "vi-access", "project_type": "modpack", "title": "Visually Impaired Access Mods+Fabric",
			    "description": "d", "downloads": 5060, "followers": 40, "game_versions": ["1.21.10", "1.21.11"] },
			  { "id": "AANobbMI", "slug": "sodium", "project_type": "mod", "title": "Sodium", "downloads": 1, "followers": 2 },
			  { "id": "shader1", "slug": "some-shader", "project_type": "shader", "title": "A Shader" }
			]
			"""));

		Assert.Equal(new[] { "Sodium", "Visually Impaired Access Mods+Fabric" }, followed.Select(f => f.Name));
		Assert.True(followed.Single(f => f.ModrinthId == "vi-access").IsModpack);
		Assert.False(followed.Single(f => f.ModrinthId == "sodium").IsModpack);
		Assert.All(followed, f => Assert.True(f.IsSearchResult));
	}

	[Fact]
	public void AModpacksPageIsItsModpackPageNotAModPage()
	{
		var pack = new GameMod { ModrinthId = "vi-access", IsModpack = true };
		var mod = new GameMod { ModrinthId = "sodium" };

		Assert.Equal("https://modrinth.com/modpack/vi-access", ModSources.PageUrlFor(pack, null));
		Assert.Equal("https://modrinth.com/mod/sodium", ModSources.PageUrlFor(mod, null));
	}

	// -------------------------------------------------------------------------
	// The Modrinth App
	// -------------------------------------------------------------------------

	[Theory]
	[InlineData("Visually Impaired Access Mods+Fabric-2.2.3", "Visually Impaired Access Mods+Fabric", "2.2.3")]
	[InlineData("My Pack", "My Pack", "")]
	[InlineData("Other-1.0", "My Pack", "")]
	public void APacksVersionIsGuessedFromItsFolderNameOnlyWhenItHasThatShape(string folder, string name, string version) =>
		Assert.Equal(version, ModrinthAppImport.VersionFromFolderName(folder, name));

	[Fact]
	public void AnInstanceWithNoDatabaseRowGetsItsVersionsFromFabricsCache()
	{
		string instance = Path.Combine(_dir, "profiles", "Old");
		Directory.CreateDirectory(Path.Combine(instance, ".fabric", "remappedJars", "minecraft-1.21-0.16.0"));

		Assert.Equal(("1.21", "0.16.0"), ModrinthAppImport.VersionsFromFiles(instance));
	}

	[Fact]
	public void TheAppsDatabaseSaysWhatEachInstanceIs()
	{
		string data = Path.Combine(_dir, "ModrinthApp");
		string instance = Path.Combine(data, "profiles", "Visually Impaired Access Mods+Fabric-2.2.3");
		Directory.CreateDirectory(Path.Combine(instance, "mods"));
		System.IO.File.WriteAllText(Path.Combine(instance, "mods", "a.jar"), "");
		System.IO.File.WriteAllText(Path.Combine(instance, "mods", "b.jar.disabled"), "");
		System.IO.File.WriteAllText(Path.Combine(instance, "mods", "notes.txt"), "");

		using (var db = new SqliteConnection($"Data Source={Path.Combine(data, "app.db")};Pooling=False"))
		{
			db.Open();
			using SqliteCommand create = db.CreateCommand();
			// The real table's columns that the import reads; the app's has many more.
			create.CommandText = """
				CREATE TABLE profiles (path TEXT, name TEXT, game_version TEXT, mod_loader TEXT,
				  mod_loader_version TEXT, linked_project_id TEXT, linked_version_id TEXT);
				INSERT INTO profiles VALUES ('Visually Impaired Access Mods+Fabric-2.2.3',
				  'Visually Impaired Access Mods+Fabric', '1.21.5', 'fabric', '0.16.14', NULL, NULL);
				""";
			create.ExecuteNonQuery();
		}

		ModrinthAppInstance found = ModrinthAppImport.FindIn(data, older: false, scratchFolder: _dir).Single();

		Assert.Equal("Visually Impaired Access Mods+Fabric", found.Name);
		Assert.Equal("1.21.5", found.MinecraftVersion);
		Assert.Equal("0.16.14", found.LoaderVersion);
		Assert.True(found.IsFabric);
		Assert.True(found.HasVersions);
		Assert.Equal(2, found.ModCount);
		Assert.Equal("", found.LinkedProjectId);
		// The database was read from a copy, and the copy was cleaned up.
		Assert.Empty(Directory.EnumerateDirectories(_dir, "modrinth-app-db-*"));
	}

	[Fact]
	public void ANeoForgeInstanceIsFoundButIsNotFabric()
	{
		string data = Path.Combine(_dir, "ModrinthApp");
		Directory.CreateDirectory(Path.Combine(data, "profiles", "Big Pack"));
		using (var db = new SqliteConnection($"Data Source={Path.Combine(data, "app.db")};Pooling=False"))
		{
			db.Open();
			using SqliteCommand create = db.CreateCommand();
			create.CommandText = """
				CREATE TABLE profiles (path TEXT, name TEXT, game_version TEXT, mod_loader TEXT,
				  mod_loader_version TEXT, linked_project_id TEXT, linked_version_id TEXT);
				INSERT INTO profiles VALUES ('Big Pack', 'Big Pack', '1.21.1', 'neoforge', '21.1.1', 'abc', 'def');
				""";
			create.ExecuteNonQuery();
		}

		ModrinthAppInstance found = ModrinthAppImport.FindIn(data, older: false, scratchFolder: _dir).Single();

		Assert.False(found.IsFabric);
		Assert.Equal("abc", found.LinkedProjectId);
	}

	// -------------------------------------------------------------------------
	// Clearing away the Modrinth App's files
	// -------------------------------------------------------------------------

	[Fact]
	public void OnlyTheAppsFourOwnFoldersAreEverConsideredAndOnlyThoseThatExist()
	{
		string roaming = Path.Combine(_dir, "Roaming"), local = Path.Combine(_dir, "Local");
		Directory.CreateDirectory(Path.Combine(roaming, "ModrinthApp"));
		Directory.CreateDirectory(Path.Combine(roaming, ".minecraft"));          // never the game's own folder
		Directory.CreateDirectory(Path.Combine(roaming, "ModrinthAppBackup"));   // nor anything merely named like it
		Directory.CreateDirectory(Path.Combine(local, "com.modrinth.theseus"));

		IReadOnlyList<string> folders = ModrinthAppCleanup.DataFolders(roaming, local);

		Assert.Equal(new[] { Path.Combine(roaming, "ModrinthApp"), Path.Combine(local, "com.modrinth.theseus") }, folders);
	}

	[Fact]
	public void APackAlreadyBroughtAcrossIsNotListedAsSomethingToLose()
	{
		string imported = Path.Combine(_dir, "profiles", "Imported");
		string notYet = Path.Combine(_dir, "profiles", "Not Yet");
		WriteLevelDat(Path.Combine(notYet, "saves", "Home"), "Home", "1.21.5", 4325);

		var instances = new[]
		{
			new ModrinthAppInstance { Folder = imported, Name = "Imported", ModCount = 79 },
			new ModrinthAppInstance { Folder = notYet, Name = "Not Yet", ModCount = 3 }
		};
		// Recorded with a trailing separator, to show the comparison is on the folder and not on the spelling.
		var managerPacks = new[] { new MinecraftPack { Name = "Imported", ImportedFrom = imported + Path.DirectorySeparatorChar } };

		UnimportedModrinthPack left = ModrinthAppCleanup.NotImported(instances, managerPacks).Single();

		Assert.Equal("Not Yet", left.Instance.Name);
		Assert.Equal(new[] { "Home" }, left.Worlds);
		Assert.True(left.HasContent);
	}

	[Fact]
	public void APackWithTheSameNameThatWasNotImportedFromItStillCounts()
	{
		// The player installed the same pack from a file; the Modrinth App's copy of it was never brought across, and
		// its worlds are its own.
		string appCopy = Path.Combine(_dir, "profiles", "VI-2.2.3");
		var instances = new[] { new ModrinthAppInstance { Folder = appCopy, Name = "Visually Impaired Access Mods+Fabric", ModCount = 79 } };
		var managerPacks = new[] { new MinecraftPack { Name = "Visually Impaired Access Mods+Fabric", ImportedFrom = "" } };

		Assert.Single(ModrinthAppCleanup.NotImported(instances, managerPacks));
	}

	[Fact]
	public void AnEmptyLeftoverLosesNothing()
	{
		var empty = new UnimportedModrinthPack(new ModrinthAppInstance { Folder = _dir, Name = "Old", ModCount = 0 }, Array.Empty<string>());

		Assert.False(empty.HasContent);
	}

	[Fact]
	public void CopyingAnInstanceBringsEverythingButItsRunHistory()
	{
		string instance = Path.Combine(_dir, "instance");
		foreach (string folder in new[] { "mods", "saves/World", "logs", "crash-reports", ".fabric/remappedJars", "config/logs" })
			Directory.CreateDirectory(Path.Combine(instance, folder));
		System.IO.File.WriteAllText(Path.Combine(instance, "Tolk.dll"), "speech");
		System.IO.File.WriteAllText(Path.Combine(instance, "mods", "a.jar"), "");
		System.IO.File.WriteAllText(Path.Combine(instance, "logs", "latest.log"), "");
		System.IO.File.WriteAllText(Path.Combine(instance, "config", "logs", "kept.json"), "");

		string target = Path.Combine(_dir, "copy");
		ModrinthAppImport.CopyInstance(instance, target);

		Assert.True(System.IO.File.Exists(Path.Combine(target, "Tolk.dll")));
		Assert.True(System.IO.File.Exists(Path.Combine(target, "mods", "a.jar")));
		Assert.True(Directory.Exists(Path.Combine(target, "saves", "World")));
		Assert.False(Directory.Exists(Path.Combine(target, "logs")));
		Assert.False(Directory.Exists(Path.Combine(target, "crash-reports")));
		Assert.False(Directory.Exists(Path.Combine(target, ".fabric")));
		// Only the top-level run history is left behind; a mod's own folder that happens to be called logs is kept.
		Assert.True(System.IO.File.Exists(Path.Combine(target, "config", "logs", "kept.json")));
	}
}
