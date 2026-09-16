using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Building the java command that starts Minecraft without its launcher.
///
/// The whole algorithm was checked against ground truth while it was written: run against the real
/// .minecraft on the development machine it produced a 96-entry classpath identical, in content and in
/// order, to the one the official launcher had built for the same version. These tests pin the pieces of
/// that which are easy to break later and impossible to notice - a dropped library, a reversed order, an
/// argument left holding a literal ${placeholder}.
/// </summary>
public class MinecraftLauncherTests
{
	private const string Windows = "windows";
	private const string X64 = "x86_64";

	// -------------------------------------------------------------------------
	// Rules
	// -------------------------------------------------------------------------

	[Fact]
	public void ALibraryWithNoRulesIsAlwaysIncluded()
	{
		Assert.True(MinecraftLauncher.RulesAllow(null, Windows, X64));
		Assert.True(MinecraftLauncher.RulesAllow(new JArray(), Windows, X64));
	}

	[Fact]
	public void AnOsOnlyLibraryIsIncludedOnlyOnThatOs()
	{
		// The real shape: Minecraft ships ca.weblite:java-objc-bridge gated on osx.
		var rules = JArray.Parse("""[ { "action": "allow", "os": { "name": "osx" } } ]""");

		Assert.False(MinecraftLauncher.RulesAllow(rules, Windows, X64));
		Assert.True(MinecraftLauncher.RulesAllow(rules, "osx", X64));
	}

	[Fact]
	public void TheLastMatchingRuleWins()
	{
		// Allow everywhere, then disallow on one OS - the shape Mojang uses for "everywhere except".
		var rules = JArray.Parse("""
		[ { "action": "allow" }, { "action": "disallow", "os": { "name": "osx" } } ]
		""");

		Assert.True(MinecraftLauncher.RulesAllow(rules, Windows, X64));
		Assert.False(MinecraftLauncher.RulesAllow(rules, "osx", X64));
	}

	[Fact]
	public void AnX86ArchRuleMeansTheFamilyAndSoAppliesOn64Bit()
	{
		// Minecraft gates -Xss1M on "arch": "x86", and the real launcher passes that flag on a 64-bit machine -
		// confirmed by reading the command line it built. Reading x86 as 32-bit-only would silently drop it.
		var rules = JArray.Parse("""[ { "action": "allow", "os": { "arch": "x86" } } ]""");

		Assert.True(MinecraftLauncher.RulesAllow(rules, Windows, X64));
		Assert.True(MinecraftLauncher.RulesAllow(rules, Windows, "x86"));
		Assert.False(MinecraftLauncher.RulesAllow(rules, Windows, "arm64"));
	}

	[Fact]
	public void FeatureRulesAreTreatedAsAbsentBecauseNoFeatureIsEverTurnedOn()
	{
		// --demo, custom resolution and quick play are all gated this way. The manager sets none of them, so a
		// feature rule could only ever have excluded something that should be there.
		var rules = JArray.Parse("""[ { "action": "allow", "features": { "is_demo_user": true } } ]""");

		Assert.False(MinecraftLauncher.RulesAllow(rules, Windows, X64));
	}

	// -------------------------------------------------------------------------
	// Maven coordinates
	// -------------------------------------------------------------------------

	// The expectation is written with forward slashes and composed with Path.Combine below, because the
	// separator here is deliberately the host's. Minecraft is the one supported game that genuinely runs on
	// Linux, and this path ends up on the classpath handed to java - so it has to be spelled the way the
	// machine running the game spells a path, not the way Windows does. An [InlineData] cannot call
	// Path.Combine itself, since an attribute argument has to be a compile-time constant.
	[Theory]
	[InlineData("net.fabricmc:fabric-loader:0.19.5", "net/fabricmc/fabric-loader/0.19.5/fabric-loader-0.19.5.jar")]
	[InlineData("org.ow2.asm:asm:9.10.1", "org/ow2/asm/asm/9.10.1/asm-9.10.1.jar")]
	[InlineData("org.lwjgl:lwjgl:3.4.1:natives-windows", "org/lwjgl/lwjgl/3.4.1/lwjgl-3.4.1-natives-windows.jar")]
	[InlineData("not-a-coordinate", "")]
	public void AMavenCoordinateBecomesItsPathUnderLibraries(string coordinate, string expectedSegments)
	{
		string expected = expectedSegments.Length == 0 ? "" : Path.Combine(expectedSegments.Split('/'));

		// Fabric's own libraries name themselves this way and give no download path; the game's carry one.
		Assert.Equal(expected, MinecraftLauncher.MavenToRelativePath(coordinate));
	}

	// -------------------------------------------------------------------------
	// The game's own files, which the manager has to fetch because it starts the game itself
	// -------------------------------------------------------------------------

	/// <summary>Minecraft 26.3's jtracy entries, as the real version file spells them: one jar plus its natives.</summary>
	private static JObject WithJtracy() => JObject.Parse("""
	{
		"id": "26.3",
		"libraries": [
			{ "name": "com.mojang:jtracy:1.14.38",
			  "downloads": { "artifact": { "path": "com/mojang/jtracy/1.14.38/jtracy-1.14.38.jar",
										   "url": "https://libraries.minecraft.net/com/mojang/jtracy/1.14.38/jtracy-1.14.38.jar",
										   "sha1": "cc2ad81342001b4281c305a298d7f50332354058", "size": 14292 } } },
			{ "name": "com.mojang:jtracy:1.14.38:natives-windows", "rules": [ { "action": "allow", "os": { "name": "windows" } } ],
			  "downloads": { "artifact": { "path": "com/mojang/jtracy/1.14.38/jtracy-1.14.38-natives-windows.jar",
										   "url": "https://libraries.minecraft.net/natives.jar", "sha1": "abc", "size": 49885 } } },
			{ "name": "com.mojang:jtracy:1.14.38:natives-linux", "rules": [ { "action": "allow", "os": { "name": "linux" } } ],
			  "downloads": { "artifact": { "path": "com/mojang/jtracy/1.14.38/jtracy-1.14.38-natives-linux.jar",
										   "url": "https://libraries.minecraft.net/linux.jar" } } }
		]
	}
	""");

	[Fact]
	public void TheLibraryThatStoppedTheGameIsReportedMissing()
	{
		// Exactly what happened on 2026-09-15: the natives jar was on disk and the jar carrying the classes was
		// not, so the game died on NoClassDefFoundError with nothing naming a file. The manager starts the game
		// itself, so nothing else was ever going to fetch it.
		bool Exists(string path) => path.Contains("natives-windows", StringComparison.OrdinalIgnoreCase);

		IReadOnlyList<MinecraftLauncher.MissingLibrary> missing =
			MinecraftLauncher.MissingLibraries(WithJtracy(), @"C:\mc\libraries", Windows, X64, Exists);

		MinecraftLauncher.MissingLibrary only = Assert.Single(missing);
		Assert.Equal(Path.Combine("com", "mojang", "jtracy", "1.14.38", "jtracy-1.14.38.jar"), only.RelativePath);
		Assert.Equal("cc2ad81342001b4281c305a298d7f50332354058", only.Sha1);
		Assert.Equal(14292, only.Bytes);
	}

	[Fact]
	public void AnotherPlatformsFilesAreNotFetched()
	{
		// The Linux natives are absent on every Windows machine and are not missing in any sense that matters.
		IReadOnlyList<MinecraftLauncher.MissingLibrary> missing =
			MinecraftLauncher.MissingLibraries(WithJtracy(), @"C:\mc\libraries", Windows, X64, _ => false);

		Assert.Equal(2, missing.Count);
		Assert.DoesNotContain(missing, m => m.RelativePath.Contains("linux", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void NothingIsFetchedWhenEverythingIsThere() =>
		Assert.Empty(MinecraftLauncher.MissingLibraries(WithJtracy(), @"C:\mc\libraries", Windows, X64, _ => true));

	[Fact]
	public void AFabricLibraryIsFetchedFromItsOwnRepository()
	{
		// Fabric's libraries carry a maven coordinate and the repository to take it from, with no download path.
		JObject fabric = JObject.Parse("""
		{ "libraries": [ { "name": "org.ow2.asm:asm:9.10.1", "url": "https://maven.fabricmc.net/",
						   "sha1": "ada2141c0cc52ee8f5c48cd5fa4ce0e794f22236", "size": 126151 } ] }
		""");

		MinecraftLauncher.MissingLibrary only = Assert.Single(
			MinecraftLauncher.MissingLibraries(fabric, @"C:\mc\libraries", Windows, X64, _ => false));

		Assert.Equal("https://maven.fabricmc.net/org/ow2/asm/asm/9.10.1/asm-9.10.1.jar", only.Url);
		Assert.Equal("ada2141c0cc52ee8f5c48cd5fa4ce0e794f22236", only.Sha1);
	}

	[Fact]
	public void ALibraryWithNowhereToFetchItFromIsNotGuessedAt()
	{
		JObject noSource = JObject.Parse("""{ "libraries": [ { "name": "mystery:thing:1.0" } ] }""");

		Assert.Empty(MinecraftLauncher.MissingLibraries(noSource, @"C:\mc\libraries", Windows, X64, _ => false));
	}

	// -------------------------------------------------------------------------
	// Merging a Fabric profile onto vanilla
	// -------------------------------------------------------------------------

	private static JObject Vanilla() => JObject.Parse("""
	{
		"id": "26.2",
		"mainClass": "net.minecraft.client.main.Main",
		"type": "release",
		"assetIndex": { "id": "32" },
		"javaVersion": { "component": "java-runtime-epsilon" },
		"libraries": [ { "name": "com.mojang:authlib:9.0.75",
						 "downloads": { "artifact": { "path": "com/mojang/authlib/9.0.75/authlib-9.0.75.jar" } } } ],
		"arguments": { "jvm": [ "-cp", "${classpath}" ], "game": [ "--username", "${auth_player_name}" ] }
	}
	""");

	private static JObject FabricProfile() => JObject.Parse("""
	{
		"id": "fabric-loader-0.19.5-26.2",
		"inheritsFrom": "26.2",
		"mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
		"libraries": [ { "name": "net.fabricmc:fabric-loader:0.19.5" } ],
		"arguments": { "jvm": [ "-DFabricMcEmu= net.minecraft.client.main.Main " ], "game": [] }
	}
	""");

	[Fact]
	public void FabricsLoaderComesBeforeTheGameOnTheClasspath()
	{
		// Not cosmetic: the loader and mixin have to precede the game or the game class loads before anything
		// can transform it, and no mod takes effect. The real launcher orders them this way too.
		JObject merged = MinecraftLauncher.Merge(FabricProfile(), Vanilla());

		IReadOnlyList<string> paths = MinecraftLauncher.LibraryPaths(merged, Windows, X64);

		Assert.Equal(2, paths.Count);
		Assert.Contains("fabric-loader", paths[0]);
		Assert.Contains("authlib", paths[1]);
	}

	[Fact]
	public void TheFabricProfilesMainClassReplacesTheGames()
	{
		JObject merged = MinecraftLauncher.Merge(FabricProfile(), Vanilla());

		Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient", (string?)merged["mainClass"]);
		// Everything the profile does not mention is inherited.
		Assert.Equal("32", (string?)merged["assetIndex"]!["id"]);
		Assert.Equal("java-runtime-epsilon", (string?)merged["javaVersion"]!["component"]);
	}

	[Fact]
	public void ArgumentsAppendRatherThanReplace()
	{
		// Fabric adds -DFabricMcEmu and expects the game's own arguments to still be there. Replacing would
		// produce a command line with no classpath and no username.
		JObject merged = MinecraftLauncher.Merge(FabricProfile(), Vanilla());

		var jvm = MinecraftLauncher.BuildArguments(
			merged["arguments"]!["jvm"], new Dictionary<string, string> { ["classpath"] = "CP" }, Windows, X64);

		Assert.Contains("-cp", jvm);
		Assert.Contains("CP", jvm);
		Assert.Contains(jvm, a => a.Contains("FabricMcEmu"));
	}

	// -------------------------------------------------------------------------
	// Argument expansion
	// -------------------------------------------------------------------------

	[Fact]
	public void PlaceholdersAreFilledIn()
	{
		var values = new Dictionary<string, string> { ["auth_player_name"] = "SeanTerry01", ["classpath"] = "A;B" };

		Assert.Equal("SeanTerry01", MinecraftLauncher.Substitute("${auth_player_name}", values));
		Assert.Equal("-cp A;B", MinecraftLauncher.Substitute("-cp ${classpath}", values));
	}

	[Fact]
	public void AnUnknownPlaceholderIsLeftAloneRatherThanBlanked()
	{
		// Leaving it visible makes a missing value obvious in a crash report. Blanking it produces a command
		// line that is subtly wrong and looks fine.
		Assert.Equal("${resolution_width}",
			MinecraftLauncher.Substitute("${resolution_width}", new Dictionary<string, string>()));
	}

	[Fact]
	public void RuleGatedArgumentsAreDroppedOnTheWrongMachine()
	{
		var section = JArray.Parse("""
		[
			"--always",
			{ "rules": [ { "action": "allow", "os": { "name": "osx" } } ], "value": "-XstartOnFirstThread" },
			{ "rules": [ { "action": "allow", "os": { "name": "windows" } } ], "value": [ "-Dwin", "-Dtwo" ] }
		]
		""");

		var args = MinecraftLauncher.BuildArguments(section, new Dictionary<string, string>(), Windows, X64);

		Assert.Equal(new[] { "--always", "-Dwin", "-Dtwo" }, args);
	}

	// -------------------------------------------------------------------------
	// Empty-valued flags
	// -------------------------------------------------------------------------

	[Fact]
	public void OptionalFlagsWithNoValueAreDroppedRatherThanPassedEmpty()
	{
		// An empty argument cannot be passed reliably through a Windows process launcher; an offline session
		// has no clientId or xuid to give. Dropping the flag is right, passing a bare flag is not.
		var args = new[] { "--username", "Sean", "--clientId", "", "--xuid", "", "--versionType", "release" };

		var kept = MinecraftLauncher.DropEmptyValuedFlags(args, "--clientId", "--xuid");

		Assert.Equal(new[] { "--username", "Sean", "--versionType", "release" }, kept);
	}

	[Fact]
	public void AFlagThatDoesHaveAValueIsKept()
	{
		var args = new[] { "--clientId", "abc123", "--xuid", "" };

		Assert.Equal(new[] { "--clientId", "abc123" },
			MinecraftLauncher.DropEmptyValuedFlags(args, "--clientId", "--xuid"));
	}

	// -------------------------------------------------------------------------
	// Identity
	// -------------------------------------------------------------------------

	[Fact]
	public void ABareUuidGetsTheDashesMinecraftExpects()
	{
		Assert.Equal("7b05bd63-2994-4bcb-97b1-79772ffcc404",
			MinecraftIdentity.FormatUuid("7b05bd6329944bcb97b179772ffcc404"));

		// Already dashed, or not a uuid at all: handed back untouched rather than mangled.
		Assert.Equal("7b05bd63-2994-4bcb-97b1-79772ffcc404",
			MinecraftIdentity.FormatUuid("7b05bd63-2994-4bcb-97b1-79772ffcc404"));
		Assert.Equal("nonsense", MinecraftIdentity.FormatUuid("nonsense"));
	}

	[Fact]
	public void AnOfflineIdentityIsStillARealPlayer()
	{
		// The point of the whole exercise. A derived OfflinePlayer: uuid would be a DIFFERENT player, and
		// loading an existing world with it starts a new character while orphaning the old one on disk.
		var identity = new MinecraftIdentity
		{
			Username = "SeanTerry01",
			Uuid = "7b05bd63-2994-4bcb-97b1-79772ffcc404",
			AccessToken = "0",
			UserType = "legacy"
		};

		Assert.True(identity.IsOffline);
		Assert.NotEqual("3ddff6e6-d2fc-3f1a-8923-e04acba3e4e3", identity.Uuid);
	}

	[Fact]
	public void TheAccountIsReadFromTheLauncherWithoutTouchingACredential()
	{
		// Shaped like the real launcher_accounts_microsoft_store.json, whose token fields are empty strings -
		// the actual credential lives encrypted elsewhere. Only public profile data is read.
		string dir = NewTempDir();
		try
		{
			File.WriteAllText(Path.Combine(dir, "launcher_accounts_microsoft_store.json"), """
			{
				"accounts": {
					"1186517c": {
						"accessToken": "", "azureToken": "",
						"minecraftProfile": { "id": "7b05bd6329944bcb97b179772ffcc404", "name": "SeanTerry01" },
						"username": "SeanTerry01"
					}
				},
				"activeAccountLocalId": "1186517c",
				"mojangClientToken": ""
			}
			""");

			MinecraftIdentity? identity = MinecraftIdentity.OfflineFromLauncher(dir);

			Assert.NotNull(identity);
			Assert.Equal("SeanTerry01", identity!.Username);
			Assert.Equal("7b05bd63-2994-4bcb-97b1-79772ffcc404", identity.Uuid);
			Assert.True(identity.IsOffline);
		}
		finally { Directory.Delete(dir, recursive: true); }
	}

	[Fact]
	public void NoAccountFileMeansNoIdentityRatherThanAMadeUpOne()
	{
		string dir = NewTempDir();
		try
		{
			Assert.Null(MinecraftIdentity.OfflineFromLauncher(dir));
		}
		finally { Directory.Delete(dir, recursive: true); }
	}

	// -------------------------------------------------------------------------
	// The game jar
	// -------------------------------------------------------------------------

	[Fact]
	public void AFabricVersionWithNoJarOfItsOwnFallsBackToTheOneItInheritsFrom()
	{
		// A Fabric version has no jar until something puts one there; the launcher copies the parent's across
		// on first run, which is why the two are byte-identical on a machine that has launched it.
		string dir = NewTempDir();
		try
		{
			Directory.CreateDirectory(Path.Combine(dir, "versions", "26.2"));
			File.WriteAllText(Path.Combine(dir, "versions", "26.2", "26.2.jar"), "game");

			string path = MinecraftLauncher.GameJarPath(dir, "fabric-loader-0.19.5-26.2", "26.2");
			Assert.Equal(Path.Combine(dir, "versions", "26.2", "26.2.jar"), path);

			// Once it has its own, that one wins.
			Directory.CreateDirectory(Path.Combine(dir, "versions", "fabric-loader-0.19.5-26.2"));
			File.WriteAllText(Path.Combine(dir, "versions", "fabric-loader-0.19.5-26.2",
				"fabric-loader-0.19.5-26.2.jar"), "copy");

			Assert.Equal(
				Path.Combine(dir, "versions", "fabric-loader-0.19.5-26.2", "fabric-loader-0.19.5-26.2.jar"),
				MinecraftLauncher.GameJarPath(dir, "fabric-loader-0.19.5-26.2", "26.2"));
		}
		finally { Directory.Delete(dir, recursive: true); }
	}

	// -------------------------------------------------------------------------
	// Natives
	// -------------------------------------------------------------------------

	[Fact]
	public void TheNativesFolderIsOursAndNotTheLaunchers()
	{
		// The launcher extracts natives into bin\<sha1>\ and DELETES that folder when the game exits, so
		// anything reusing it works where the launcher happened to leave one and fails everywhere else.
		string natives = MinecraftLauncher.NativesDirectoryFor(@"C:\mc");

		Assert.Equal(Path.Combine(@"C:\mc", "bin", "kinetix"), natives);
	}

	// -------------------------------------------------------------------------

	private static string NewTempDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "kmm-launch-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}
}
