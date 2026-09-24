using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using KinetixModManager;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Reading, refusing and recording Modrinth modpacks — <see cref="MinecraftModpacks"/>.
///
/// <para>
/// A pack is a list of files someone else chose to write onto this computer, so most of what is pinned here is
/// the refusing: a path that climbs out of the pack's folder, a download from anywhere but the format's four
/// hosts, a file with no checksum. Each refuses the whole pack. The file entries are the real ones from the
/// Visually Impaired Access Mods+Fabric pack that the Minecraft Access guide sends blind players to.
/// </para>
/// </summary>
public class MinecraftModpacksTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kinetix-packs-" + Guid.NewGuid().ToString("N"));

	public MinecraftModpacksTests() => Directory.CreateDirectory(_dir);

	public void Dispose()
	{
		try { Directory.Delete(_dir, true); } catch { }
	}

	// Real entries from vi-access 2.5.0-alpha.1. The sha512s are cut short: nothing reads them.
	private const string FabricApi = """
		{ "path": "mods/fabric-api-0.141.1+1.21.11.jar",
		  "hashes": { "sha1": "7f51d854b55c8b29fb4dc0d666e550015c742b69", "sha512": "185b45dcbe229230" },
		  "env": { "client": "required", "server": "required" },
		  "downloads": [ "https://cdn.modrinth.com/data/P7dR8mSH/versions/DdVHbeR1/fabric-api-0.141.1%2B1.21.11.jar" ],
		  "fileSize": 2412038 }
		""";

	private const string HighContrast = """
		{ "path": "resourcepacks/High_Contrast_Extended.zip",
		  "hashes": { "sha1": "1913a726f2b306448f0ce8c1ca13e289c02ac102", "sha512": "542ef5be6e13dc1d" },
		  "env": { "client": "required", "server": "unsupported" },
		  "downloads": [ "https://cdn.modrinth.com/data/PEEMA1Hv/versions/SCun1ysK/High_Contrast_Extended.zip" ],
		  "fileSize": 961564 }
		""";

	private static string Index(string files, string dependencies = """{ "fabric-loader": "0.18.4", "minecraft": "1.21.11" }""",
		int formatVersion = 1, string game = "minecraft") => $$"""
		{ "formatVersion": {{formatVersion}}, "game": "{{game}}", "versionId": "2.5.0-alpha.1",
		  "name": "Visually Impaired Access Mods+Fabric", "summary": "Accessibility mods",
		  "files": [ {{files}} ], "dependencies": {{dependencies}} }
		""";

	private static string FileAt(string path, string url = "https://cdn.modrinth.com/data/x/y.jar",
		string sha1 = "7f51d854b55c8b29fb4dc0d666e550015c742b69", string client = "required") => $$"""
		{ "path": {{Newtonsoft.Json.JsonConvert.ToString(path)}}, "hashes": { "sha1": "{{sha1}}", "sha512": "x" },
		  "env": { "client": "{{client}}", "server": "required" }, "downloads": [ "{{url}}" ], "fileSize": 10 }
		""";

	// -------------------------------------------------------------------------
	// Reading the index
	// -------------------------------------------------------------------------

	[Fact]
	public void TheRealAccessibilityPackReadsAsFabricOnItsMinecraftVersion()
	{
		MrpackIndex index = MinecraftModpacks.ParseIndex(Index(FabricApi + "," + HighContrast));

		Assert.Equal(ModpackProblem.None, index.Problem);
		Assert.Equal("Visually Impaired Access Mods+Fabric", index.Name);
		Assert.Equal("2.5.0-alpha.1", index.VersionId);
		Assert.Equal("1.21.11", index.MinecraftVersion);
		Assert.Equal("0.18.4", index.LoaderVersion);
		Assert.Equal(2, index.Files.Count);
		Assert.Equal(2412038 + 961564, index.DownloadBytes);

		MrpackFile api = index.Files[0];
		Assert.Equal(Path.Combine("mods", "fabric-api-0.141.1+1.21.11.jar"), api.RelativePath);
		Assert.Equal("7f51d854b55c8b29fb4dc0d666e550015c742b69", api.Sha1);
		Assert.StartsWith("https://cdn.modrinth.com/", api.Url);
	}

	[Fact]
	public void AServerOnlyFileIsLeftOutAndCountedNotRefused()
	{
		MrpackIndex index = MinecraftModpacks.ParseIndex(Index(FabricApi + "," + FileAt("mods/admin-tool.jar", client: "unsupported")));

		Assert.Equal(ModpackProblem.None, index.Problem);
		Assert.Single(index.Files);
		Assert.Equal(1, index.ServerOnlyFiles);
	}

	[Fact]
	public void AnOptionalClientFileIsInstalled()
	{
		MrpackIndex index = MinecraftModpacks.ParseIndex(Index(FileAt("mods/extra.jar", client: "optional")));

		Assert.Single(index.Files);
	}

	[Fact]
	public void TheFirstTrustedAddressIsChosenWhenAFileOffersSeveral()
	{
		string file = """
			{ "path": "mods/a.jar", "hashes": { "sha1": "7f51d854b55c8b29fb4dc0d666e550015c742b69" },
			  "downloads": [ "https://example.com/a.jar", "https://github.com/owner/repo/releases/download/v1/a.jar" ],
			  "fileSize": 1 }
			""";

		MrpackIndex index = MinecraftModpacks.ParseIndex(Index(file));

		Assert.Equal("https://github.com/owner/repo/releases/download/v1/a.jar", index.Files.Single().Url);
	}

	[Fact]
	public void TextThatIsNotJsonIsNotAPack() =>
		Assert.Equal(ModpackProblem.NotAPack, MinecraftModpacks.ParseIndex("this is not json").Problem);

	[Fact]
	public void ANewerFormatIsRefused() =>
		Assert.Equal(ModpackProblem.UnsupportedFormat, MinecraftModpacks.ParseIndex(Index(FabricApi, formatVersion: 2)).Problem);

	[Fact]
	public void APackForAnotherGameIsRefused() =>
		Assert.Equal(ModpackProblem.NotMinecraft, MinecraftModpacks.ParseIndex(Index(FabricApi, game: "hytale")).Problem);

	[Fact]
	public void APackThatNamesNoMinecraftVersionIsRefused() =>
		Assert.Equal(ModpackProblem.NoMinecraftVersion,
			MinecraftModpacks.ParseIndex(Index(FabricApi, """{ "fabric-loader": "0.18.4" }""")).Problem);

	[Theory]
	[InlineData("forge", "Forge")]
	[InlineData("neoforge", "NeoForge")]
	[InlineData("quilt-loader", "Quilt")]
	public void APackForAnotherLoaderIsRefusedAndNamesIt(string key, string spoken)
	{
		MrpackIndex index = MinecraftModpacks.ParseIndex(Index(FabricApi, $$"""{ "{{key}}": "1.0", "minecraft": "1.21.1" }"""));

		Assert.Equal(ModpackProblem.UnsupportedLoader, index.Problem);
		Assert.Equal(spoken, index.ProblemDetail);
		Assert.Empty(index.Files);
	}

	[Fact]
	public void APackWithNoLoaderIsRefused() =>
		Assert.Equal(ModpackProblem.NoLoader,
			MinecraftModpacks.ParseIndex(Index(FabricApi, """{ "minecraft": "1.21.11" }""")).Problem);

	[Fact]
	public void OneUnsafePathRefusesTheWholePackAndNamesTheFile()
	{
		MrpackIndex index = MinecraftModpacks.ParseIndex(Index(FabricApi + "," + FileAt("../../Startup/evil.jar")));

		Assert.Equal(ModpackProblem.UnsafePath, index.Problem);
		Assert.Equal("../../Startup/evil.jar", index.ProblemDetail);
		Assert.Empty(index.Files);
	}

	[Theory]
	[InlineData("http://cdn.modrinth.com/data/x/y.jar")]            // not https
	[InlineData("https://example.com/y.jar")]                       // not a trusted host
	[InlineData("https://cdn.modrinth.com.example.com/y.jar")]      // a trusted name as a prefix of another host
	public void AFileOnlyOfferedFromAnUntrustedAddressRefusesThePack(string url)
	{
		MrpackIndex index = MinecraftModpacks.ParseIndex(Index(FileAt("mods/a.jar", url: url)));

		Assert.Equal(ModpackProblem.UntrustedDownload, index.Problem);
		Assert.Equal("mods/a.jar", index.ProblemDetail);
	}

	[Theory]
	[InlineData("")]
	[InlineData("abc")]
	public void AFileWithNoUsableChecksumRefusesThePack(string sha1) =>
		Assert.Equal(ModpackProblem.MissingChecksum,
			MinecraftModpacks.ParseIndex(Index(FileAt("mods/a.jar", sha1: sha1))).Problem);

	// -------------------------------------------------------------------------
	// Paths and addresses
	// -------------------------------------------------------------------------

	[Theory]
	[InlineData("mods/Some Mod+Fabric-1.0.jar")]
	[InlineData("config/yosbr/options.txt")]
	[InlineData("options.txt")]
	public void AnOrdinaryPathIsKeptWithThisMachinesSeparators(string path) =>
		Assert.Equal(Path.Combine(path.Split('/')), MinecraftModpacks.SafeRelativePath(path));

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("../evil.jar")]                 // climbs out
	[InlineData("mods/../../evil.jar")]         // climbs out from inside
	[InlineData(@"mods\..\..\evil.jar")]        // the same with Windows separators
	[InlineData("/etc/evil")]                   // absolute
	[InlineData(@"\Windows\evil")]              // absolute, Windows-style
	[InlineData("C:/Windows/evil.dll")]         // a drive letter
	[InlineData("mods/a.jar:hidden")]           // an alternate data stream
	[InlineData("mods//a.jar")]                 // an empty segment
	[InlineData("mods/./a.jar")]                // a dot segment
	[InlineData("mods/a.jar.")]                 // Windows trims the dot, so this is really mods/a.jar
	[InlineData("mods/a.jar ")]                 // and the space
	[InlineData("mods/CON.jar")]                // a device, not a file
	[InlineData("mods/nul")]
	[InlineData("mods/a*.jar")]                 // a character Windows forbids
	public void AnUnsafePathIsRefused(string? path) =>
		Assert.Null(MinecraftModpacks.SafeRelativePath(path));

	[Theory]
	[InlineData("https://cdn.modrinth.com/data/x/y.jar", true)]
	[InlineData("https://github.com/o/r/releases/download/v1/y.jar", true)]
	[InlineData("https://raw.githubusercontent.com/o/r/main/y.jar", true)]
	[InlineData("https://gitlab.com/o/r/-/raw/main/y.jar", true)]
	[InlineData("https://CDN.Modrinth.com/data/x/y.jar", true)]
	[InlineData("http://github.com/o/r/y.jar", false)]
	[InlineData("https://evil.github.com.example/y.jar", false)]
	[InlineData("ftp://cdn.modrinth.com/y.jar", false)]
	[InlineData("not a url", false)]
	[InlineData(null, false)]
	public void OnlyHttpsOnTheFourTrustedHostsIsTrusted(string? url, bool trusted) =>
		Assert.Equal(trusted, MinecraftModpacks.IsTrustedDownload(url));

	// -------------------------------------------------------------------------
	// Bundled files
	// -------------------------------------------------------------------------

	[Fact]
	public void AClientOverrideReplacesTheSameFileFromOverrides()
	{
		OverridePlan plan = MinecraftModpacks.PlanOverrides(new[]
		{
			"client-overrides/options.txt",
			"overrides/options.txt",
			"overrides/config/debugify.json"
		});

		var byPath = plan.Files.ToDictionary(f => f.RelativePath, f => f.Entry);
		Assert.Equal(2, plan.Files.Count);
		Assert.Equal("client-overrides/options.txt", byPath["options.txt"]);
		Assert.Equal("overrides/config/debugify.json", byPath[Path.Combine("config", "debugify.json")]);
	}

	[Fact]
	public void LayeringIgnoresCaseBecauseWindowsDoes()
	{
		OverridePlan plan = MinecraftModpacks.PlanOverrides(new[] { "overrides/Options.txt", "client-overrides/options.txt" });

		Assert.Equal("client-overrides/options.txt", plan.Files.Single().Entry);
	}

	[Fact]
	public void FolderEntriesServerOverridesAndTheIndexAreNotFiles()
	{
		OverridePlan plan = MinecraftModpacks.PlanOverrides(new[]
		{
			"modrinth.index.json", "overrides/", "overrides/config/", "server-overrides/server.properties",
			"overrides/mods/minecraft-access-1.12.0-alpha.2.SNAPSHOT+fabric.jar"
		});

		Assert.Equal(Path.Combine("mods", "minecraft-access-1.12.0-alpha.2.SNAPSHOT+fabric.jar"), plan.Files.Single().RelativePath);
		Assert.Empty(plan.Unsafe);
	}

	[Fact]
	public void AnUnsafeBundledFileIsReported()
	{
		OverridePlan plan = MinecraftModpacks.PlanOverrides(new[] { "overrides/../evil.dll", "overrides/ok.txt" });

		Assert.Equal("overrides/../evil.dll", plan.Unsafe.Single());
		Assert.Equal("ok.txt", plan.Files.Single().RelativePath);
	}

	[Fact]
	public void BundledFilesAreWrittenIntoTheFolderWithTheClientLayerOnTop()
	{
		string mrpack = Path.Combine(_dir, "pack.mrpack");
		using (ZipArchive zip = ZipFile.Open(mrpack, ZipArchiveMode.Create))
		{
			Add(zip, "modrinth.index.json", "{}");
			Add(zip, "overrides/options.txt", "shared");
			Add(zip, "client-overrides/options.txt", "client");
			Add(zip, "overrides/Tolk.dll", "speech");
		}

		string target = Path.Combine(_dir, "pack");
		OverridePlan plan;
		using (ZipArchive zip = ZipFile.OpenRead(mrpack))
			plan = MinecraftModpacks.PlanOverrides(zip.Entries.Select(e => e.FullName).ToList());

		IReadOnlyList<string> written = MinecraftModpacks.ExtractOverrides(mrpack, target, plan);

		Assert.Equal("client", File.ReadAllText(Path.Combine(target, "options.txt")));
		Assert.Equal("speech", File.ReadAllText(Path.Combine(target, "Tolk.dll")));
		Assert.Equal(2, written.Count);
		Assert.False(File.Exists(Path.Combine(_dir, "modrinth.index.json")));
	}

	[Fact]
	public void ExtractingRefusesAPlanThatWouldLeaveTheFolder()
	{
		// The second lock: a plan built by hand, bypassing PlanOverrides, still cannot write outside.
		string mrpack = Path.Combine(_dir, "pack.mrpack");
		using (ZipArchive zip = ZipFile.Open(mrpack, ZipArchiveMode.Create))
			Add(zip, "overrides/x.txt", "x");

		var plan = new OverridePlan { Files = new[] { ("overrides/x.txt", Path.Combine("..", "escaped.txt")) } };

		Assert.Throws<InvalidDataException>(() => MinecraftModpacks.ExtractOverrides(mrpack, Path.Combine(_dir, "pack"), plan));
		Assert.False(File.Exists(Path.Combine(_dir, "escaped.txt")));
	}

	[Fact]
	public void AZipWithNoIndexIsNotAPack()
	{
		string mrpack = Path.Combine(_dir, "notapack.mrpack");
		using (ZipArchive zip = ZipFile.Open(mrpack, ZipArchiveMode.Create))
			Add(zip, "readme.txt", "hello");

		Assert.Equal(ModpackProblem.NotAPack, MinecraftModpacks.ReadIndex(mrpack).Problem);
	}

	[Fact]
	public void AFileThatIsNotAZipIsNotAPack()
	{
		string mrpack = Path.Combine(_dir, "text.mrpack");
		File.WriteAllText(mrpack, "not a zip");

		Assert.Equal(ModpackProblem.NotAPack, MinecraftModpacks.ReadIndex(mrpack).Problem);
	}

	// -------------------------------------------------------------------------
	// Folders and records
	// -------------------------------------------------------------------------

	[Fact]
	public void APacksFolderIsItsNameMadeSafeForWindows() =>
		Assert.Equal("Visually Impaired Access Mods+Fabric",
			MinecraftModpacks.UniqueFolderName("Visually Impaired Access Mods+Fabric", _ => false));

	[Fact]
	public void ForbiddenCharactersAndTrailingDotsAreRemoved() =>
		Assert.Equal("What Pack", MinecraftModpacks.UniqueFolderName("What? Pack...", _ => false));

	[Fact]
	public void ASecondCopyIsNumbered()
	{
		var taken = new HashSet<string> { "Pack", "Pack (2)" };
		Assert.Equal("Pack (3)", MinecraftModpacks.UniqueFolderName("Pack", taken.Contains));
	}

	[Theory]
	[InlineData("")]
	[InlineData("???")]
	[InlineData(".hidden")]      // would read as a half-built pack and be skipped
	public void ANameThatLeavesNothingUsableBecomesModpack(string name) =>
		Assert.Equal("Modpack", MinecraftModpacks.UniqueFolderName(name, _ => false));

	[Fact]
	public void APacksRecordSurvivesTheRoundTripAndKnowsItsFolder()
	{
		string folder = Path.Combine(_dir, "Pack");
		Directory.CreateDirectory(folder);
		var pack = new MinecraftPack
		{
			Folder = folder, Name = "Pack", PackVersion = "2.4.2", MinecraftVersion = "1.21.10",
			LoaderVersion = "0.17.3", ModrinthProjectId = "TAT3EDpw", ModrinthVersionId = "Eg0u2Imb",
			InstalledUtc = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc),
			ProvidedFiles = new List<string> { "mods/fabric-api.jar", "config/debugify.json" }
		};

		MinecraftModpacks.Save(pack);
		MinecraftPack? loaded = MinecraftModpacks.Load(folder);

		Assert.NotNull(loaded);
		Assert.Equal(folder, loaded!.Folder);
		Assert.Equal("TAT3EDpw", loaded.ModrinthProjectId);
		Assert.Equal(pack.ProvidedFiles, loaded.ProvidedFiles);
		Assert.Equal("fabric-loader-0.17.3-1.21.10", loaded.FabricVersionId);
		Assert.Equal(Path.Combine(folder, "logs", "latest.log"), loaded.LatestLogPath);
	}

	[Fact]
	public void ARecordMissingItsVersionsIsNotAPack()
	{
		string folder = Path.Combine(_dir, "Broken");
		Directory.CreateDirectory(folder);
		File.WriteAllText(Path.Combine(folder, MinecraftModpacks.ManifestFileName), """{ "Name": "Broken" }""");

		Assert.Null(MinecraftModpacks.Load(folder));
	}

	[Fact]
	public void OnlyFinishedPacksWithRecordsAreFound()
	{
		foreach (string name in new[] { "Zeta", "alpha", ".installing-Beta" })
		{
			string folder = Path.Combine(_dir, name);
			Directory.CreateDirectory(folder);
			MinecraftModpacks.Save(new MinecraftPack
				{ Folder = folder, Name = name, MinecraftVersion = "1.21.11", LoaderVersion = "0.18.4" });
		}
		Directory.CreateDirectory(Path.Combine(_dir, "NoRecord"));

		IReadOnlyList<MinecraftPack> found = MinecraftModpacks.FindInstalled(_dir);

		Assert.Equal(new[] { "alpha", "Zeta" }, found.Select(p => p.Name));
	}

	[Fact]
	public void NoPacksFolderMeansNoPacks() =>
		Assert.Empty(MinecraftModpacks.FindInstalled(Path.Combine(_dir, "nothing-here")));

	[Fact]
	public void TheSamePackIsFoundByItsModrinthProjectEvenUnderAnotherName()
	{
		var installed = new[]
		{
			new MinecraftPack { Name = "Renamed", ModrinthProjectId = "TAT3EDpw" },
			new MinecraftPack { Name = "Other" }
		};

		Assert.Equal("Renamed", MinecraftModpacks.SamePack(installed, "Visually Impaired Access Mods+Fabric", "TAT3EDpw")?.Name);
		Assert.Equal("Other", MinecraftModpacks.SamePack(installed, "other", "")?.Name);
		Assert.Null(MinecraftModpacks.SamePack(installed, "New", ""));
	}

	[Fact]
	public void AnEmptyProjectIdNeverMatchesAnotherEmptyOne()
	{
		var installed = new[] { new MinecraftPack { Name = "Local pack", ModrinthProjectId = "" } };

		Assert.Null(MinecraftModpacks.SamePack(installed, "Different pack", ""));
	}

	[Fact]
	public void TheAccessibilityModsAreFoundAmongAPacksMods()
	{
		IReadOnlyList<MinecraftSuiteMod> found =
			MinecraftModpacks.AccessModsAmong(new[] { "sodium", "fabric-api", "minecraft_access", "" });

		Assert.Equal(MinecraftSuite.MinecraftAccessId, found.Single().Id);
		Assert.Empty(MinecraftModpacks.AccessModsAmong(new[] { "sodium", "fabric-api" }));
	}

	// -------------------------------------------------------------------------
	// A pack as a session
	// -------------------------------------------------------------------------

	[Fact]
	public void APacksInstallKeyIsAMinecraftKeyNamedForItsFolder()
	{
		var pack = new MinecraftPack { Folder = Path.Combine(_dir, "Visually Impaired Access Mods+Fabric") };

		string key = MinecraftModpacks.InstallKeyFor(pack);

		Assert.Equal("Minecraft@Pack-Visually Impaired Access Mods+Fabric", key);
		Assert.True(MinecraftModpacks.IsPackKey(key));
		// It is still Minecraft wherever the question is "which game" — the layout, the theme, Modrinth.
		Assert.True(GameProfiles.IsGame(key, GameProfiles.Minecraft));
		Assert.Equal(GameProfiles.Minecraft, GameProfiles.Find(key)?.Id);
	}

	[Theory]
	[InlineData("Minecraft")]
	[InlineData("SkyrimSE@Gog")]
	[InlineData("Minecraft@Pack-")]
	[InlineData("")]
	[InlineData(null)]
	public void OnlyAPacksKeyIsAPackKey(string? key) => Assert.False(MinecraftModpacks.IsPackKey(key));

	[Fact]
	public void APackIsFoundAgainFromItsKey()
	{
		string folder = Path.Combine(_dir, "My Pack (2)");
		Directory.CreateDirectory(folder);
		var pack = new MinecraftPack { Folder = folder, Name = "My Pack", MinecraftVersion = "1.21.10", LoaderVersion = "0.17.3" };
		MinecraftModpacks.Save(pack);

		MinecraftPack? found = MinecraftModpacks.ForKey(MinecraftModpacks.InstallKeyFor(pack), _dir);

		Assert.Equal(folder, found?.Folder);
	}

	[Theory]
	[InlineData("Minecraft@Pack-../escape")]
	[InlineData(@"Minecraft@Pack-..\escape")]
	[InlineData("Minecraft@Pack-sub/folder")]
	public void AKeyCannotReachOutsideThePacksFolder(string key) => Assert.Null(MinecraftModpacks.ForKey(key, _dir));

	// -------------------------------------------------------------------------
	// Identifying a pack file on Modrinth
	// -------------------------------------------------------------------------

	[Fact]
	public void AVersionFileAnswerGivesTheProjectAndVersion()
	{
		var version = JObject.Parse("""{ "id": "Eg0u2Imb", "project_id": "TAT3EDpw", "version_number": "2.4.2" }""");

		Assert.Equal(("TAT3EDpw", "Eg0u2Imb"), ModrinthService.ParseIdentifiedFile(version));
		Assert.Null(ModrinthService.ParseIdentifiedFile(new JObject()));
	}

	// -------------------------------------------------------------------------
	// Starting a pack from its own folder
	// -------------------------------------------------------------------------

	[Fact]
	public void APackStartsInItsOwnFolderWhileTheGameFilesStayShared()
	{
		string root = FakeMinecraftRoot();
		string packFolder = Path.Combine(_dir, "Pack");

		MinecraftLaunchPlan plan = MinecraftLauncher.BuildPlan(root, "1.21.11", Player(), gameDirectory: packFolder);

		Assert.Equal(packFolder, plan.WorkingDirectory);
		Assert.Equal(packFolder, ValueAfter(plan.Arguments, "--gameDir"));
		Assert.Equal(Path.Combine(root, "assets"), ValueAfter(plan.Arguments, "--assetsDir"));
	}

	[Fact]
	public void TheGameIsGivenAMemoryCeilingAheadOfEverythingElse()
	{
		// Without one Java takes up to a quarter of the computer's memory and keeps asking; the pack died loading
		// when Windows had nothing left to give it.
		MinecraftLaunchPlan plan = MinecraftLauncher.BuildPlan(FakeMinecraftRoot(), "1.21.11", Player(), maxMemoryMb: 4096);

		Assert.Equal("-Xmx4096M", plan.Arguments[0]);
		Assert.Single(plan.Arguments, a => a.StartsWith("-Xmx"));
	}

	[Fact]
	public void NoCeilingIsAddedWhenNoneIsAskedFor() =>
		Assert.DoesNotContain(MinecraftLauncher.BuildPlan(FakeMinecraftRoot(), "1.21.11", Player()).Arguments,
			a => a.StartsWith("-Xmx"));

	[Fact]
	public void WithoutAPackTheGameStartsInDotMinecraftAsBefore()
	{
		string root = FakeMinecraftRoot();

		MinecraftLaunchPlan plan = MinecraftLauncher.BuildPlan(root, "1.21.11", Player());

		Assert.Equal(root, plan.WorkingDirectory);
		Assert.Equal(root, ValueAfter(plan.Arguments, "--gameDir"));
	}

	private static void Add(ZipArchive zip, string name, string text)
	{
		using var writer = new StreamWriter(zip.CreateEntry(name).Open());
		writer.Write(text);
	}

	private static MinecraftIdentity Player() => new()
		{ Username = "Sean", Uuid = "7b05bd63-2994-4bcb-97b1-79772ffcc404", AccessToken = "0", UserType = "legacy" };

	private static string ValueAfter(IReadOnlyList<string> args, string flag) => args[args.ToList().IndexOf(flag) + 1];

	/// <summary>A .minecraft with one version file and a stand-in Java under its own runtime folder.</summary>
	private string FakeMinecraftRoot()
	{
		string root = Path.Combine(_dir, ".minecraft");
		string versionFolder = Path.Combine(root, "versions", "1.21.11");
		Directory.CreateDirectory(versionFolder);
		File.WriteAllText(Path.Combine(versionFolder, "1.21.11.json"), """
			{ "id": "1.21.11", "mainClass": "net.minecraft.client.main.Main", "type": "release",
			  "assetIndex": { "id": "29" },
			  "javaVersion": { "component": "kinetix-test-runtime" },
			  "libraries": [],
			  "arguments": { "jvm": [], "game": [ "--gameDir", "${game_directory}", "--assetsDir", "${assets_root}" ] } }
			""");

		string javaBin = Path.Combine(root, "runtime", "kinetix-test-runtime", "windows-x64", "bin");
		Directory.CreateDirectory(javaBin);
		File.WriteAllText(Path.Combine(javaBin, "javaw.exe"), "");

		return root;
	}
}
