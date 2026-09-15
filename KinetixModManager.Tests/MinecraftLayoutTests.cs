using System;
using System.IO;
using System.Runtime.InteropServices;
using System.IO.Compression;
using System.Linq;
using System.Text;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Minecraft's layout, which is the first one where a mod is a FILE rather than a folder.
///
/// The manifests used here are the real ones, copied out of the jars that were on disk when this was written -
/// United Minecraft 1.1.0+mc26.2 and Fabric API 0.160.0+26.2. Made-up manifests would have missed the two
/// things that actually matter: that Fabric's <c>depends</c> map mixes real mods in with pseudo-ids the loader
/// satisfies itself, and that a mod can carry its dependencies nested inside its own jar.
/// </summary>
public class MinecraftLayoutTests
{
	// The real United Minecraft manifest. Note "fabric-api" in depends and no "jars" list: it needs Fabric API
	// installed alongside it, which is what makes it a two-jar install.
	private const string UnitedMinecraftManifest = """
	{
		"schemaVersion": 1,
		"id": "united_minecraft",
		"version": "1.1.0",
		"name": "United Minecraft",
		"description": "An accessibility mod for Minecraft.",
		"authors": [ "NibbleNerds" ],
		"contact": {
			"homepage": "https://github.com/blindgoofball/united-Minecraft",
			"sources": "https://github.com/blindgoofball/united-Minecraft",
			"issues": "https://github.com/blindgoofball/united-Minecraft/issues"
		},
		"license": "GPL-3.0-or-later",
		"environment": "*",
		"depends": {
			"fabricloader": ">=0.19.3",
			"minecraft": "~26.2",
			"java": ">=25",
			"fabric-api": "*"
		}
	}
	""";

	// -------------------------------------------------------------------------
	// Reading a manifest
	// -------------------------------------------------------------------------

	[Fact]
	public void ReadsTheFieldsTheModListShows()
	{
		FabricModInfo info = MinecraftLayout.ParseModInfo(UnitedMinecraftManifest, "fallback.jar");

		Assert.False(info.IsUnreadable);
		Assert.Equal("united_minecraft", info.Id);
		Assert.Equal("United Minecraft", info.Name);
		Assert.Equal("1.1.0", info.Version);
		Assert.Equal("An accessibility mod for Minecraft.", info.Description);
		Assert.Equal(new[] { "NibbleNerds" }, info.Authors);
		Assert.Equal("https://github.com/blindgoofball/united-Minecraft", info.HomepageUrl);
	}

	[Fact]
	public void KeepsTheLoaderPseudoDependenciesRatherThanDiscardingThem()
	{
		// MinecraftLayout reports everything the manifest declared; it is ModFileSystem that filters the
		// pseudo-ids out before they reach the requirements check. Keeping them here is what lets a future
		// "this mod needs a newer Fabric loader" message be written without re-reading the jar.
		FabricModInfo info = MinecraftLayout.ParseModInfo(UnitedMinecraftManifest, "fallback.jar");

		Assert.Equal(">=0.19.3", info.Depends["fabricloader"]);
		Assert.Equal("~26.2", info.Depends["minecraft"]);
		Assert.Equal(">=25", info.Depends["java"]);
		Assert.Equal("*", info.Depends["fabric-api"]);
	}

	[Fact]
	public void AuthorsMayBeObjectsRatherThanStrings()
	{
		// Both spellings are legal in a fabric.mod.json and both are used in the wild. Getting this wrong
		// renders an author as "{}" in the mod list rather than their name.
		const string manifest = """
		{
			"id": "x", "version": "1",
			"authors": [ "Plain Name", { "name": "Object Name", "contact": { "homepage": "https://e.g" } } ]
		}
		""";

		FabricModInfo info = MinecraftLayout.ParseModInfo(manifest, "x.jar");

		Assert.Equal(new[] { "Plain Name", "Object Name" }, info.Authors);
	}

	[Fact]
	public void ADependencyRangeMayBeAListOfAlternatives()
	{
		const string manifest = """
		{ "id": "x", "version": "1", "depends": { "some-mod": [">=1.0", "<2.0"] } }
		""";

		FabricModInfo info = MinecraftLayout.ParseModInfo(manifest, "x.jar");

		Assert.Equal(">=1.0 or <2.0", info.Depends["some-mod"]);
	}

	[Fact]
	public void UnparseableJsonIsReportedRatherThanThrown()
	{
		// A truncated download sitting in the mods folder must show up as a broken mod the user can delete,
		// not take the whole scan down with it.
		FabricModInfo info = MinecraftLayout.ParseModInfo("{ not json", "half-downloaded.jar");

		Assert.True(info.IsUnreadable);
		Assert.Equal("half-downloaded.jar", info.Name);
	}

	[Fact]
	public void AManifestWithoutANameFallsBackToItsIdAndThenItsFileName()
	{
		Assert.Equal("some_id", MinecraftLayout.ParseModInfo("""{ "id": "some_id" }""", "file.jar").Name);
		Assert.Equal("file.jar", MinecraftLayout.ParseModInfo("{ }", "file.jar").Name);
	}

	// -------------------------------------------------------------------------
	// Nested jars - why the two access mods differ
	// -------------------------------------------------------------------------

	[Fact]
	public void NestedJarIdsAreRecognisedSoBundledDependenciesAreNotReportedMissing()
	{
		// Trimmed from the real Fabric API manifest. This is the mechanism that lets Minecraft Access install
		// as a single jar while United Minecraft needs two.
		const string manifest = """
		{
			"id": "fabric-api", "version": "0.160.0+26.2",
			"jars": [
				{ "file": "META-INF/jars/fabric-api-base-2.0.4+ece063239e.jar" },
				{ "file": "META-INF/jars/fabric-biome-api-v1-18.0.6+c7bd5b8e9e.jar" },
				{ "file": "META-INF/jars/fabric-block-api-v1-3.1.0+53515aab9e.jar" }
			]
		}
		""";

		FabricModInfo info = MinecraftLayout.ParseModInfo(manifest, "fabric-api.jar");

		Assert.Contains("fabric-api-base", info.NestedJars);
		Assert.Contains("fabric-biome-api-v1", info.NestedJars);
		Assert.Contains("fabric-block-api-v1", info.NestedJars);
	}

	[Theory]
	// The id itself contains hyphens, so the split is at the last hyphen followed by a digit.
	[InlineData("META-INF/jars/fabric-api-base-2.0.4+ece063239e.jar", "fabric-api-base")]
	[InlineData("META-INF/jars/cloth-config-19.0.147.jar", "cloth-config")]
	[InlineData("META-INF/jars/balm-fabric-21.0.30.jar", "balm-fabric")]
	[InlineData("nested.jar", "nested")]
	[InlineData("", "")]
	public void ANestedJarsIdIsItsFileNameWithoutTheVersion(string file, string expected)
	{
		Assert.Equal(expected, MinecraftLayout.NestedJarIdFromFileName(file));
	}

	// -------------------------------------------------------------------------
	// Reading a real jar off disk
	// -------------------------------------------------------------------------

	[Fact]
	public void ReadsTheManifestOutOfAnActualJar()
	{
		string dir = NewTempDir();
		try
		{
			string jar = Path.Combine(dir, "united-minecraft-1.1.0+mc26.2.jar");
			WriteJar(jar, MinecraftLayout.ManifestEntryName, UnitedMinecraftManifest);

			FabricModInfo info = MinecraftLayout.ReadModInfo(jar);

			Assert.False(info.IsUnreadable);
			Assert.Equal("united_minecraft", info.Id);
			Assert.Equal("United Minecraft", info.Name);
		}
		finally { Directory.Delete(dir, recursive: true); }
	}

	[Fact]
	public void AJarWithNoFabricManifestIsReportedUnreadable()
	{
		string dir = NewTempDir();
		try
		{
			// A perfectly valid zip that simply is not a Fabric mod - a resource pack, say, dropped in by mistake.
			string jar = Path.Combine(dir, "not-a-mod.jar");
			WriteJar(jar, "pack.mcmeta", "{}");

			FabricModInfo info = MinecraftLayout.ReadModInfo(jar);

			Assert.True(info.IsUnreadable);
			Assert.Equal("not-a-mod.jar", info.Name);
		}
		finally { Directory.Delete(dir, recursive: true); }
	}

	[Fact]
	public void AFileThatIsNotAZipAtAllIsReportedUnreadable()
	{
		string dir = NewTempDir();
		try
		{
			string jar = Path.Combine(dir, "truncated.jar");
			File.WriteAllText(jar, "this is not a zip");

			Assert.True(MinecraftLayout.ReadModInfo(jar).IsUnreadable);
		}
		finally { Directory.Delete(dir, recursive: true); }
	}

	// -------------------------------------------------------------------------
	// Enabling and disabling - a rename, not a move
	// -------------------------------------------------------------------------

	[Theory]
	[InlineData("mod.jar", true)]
	[InlineData("mod.JAR", true)]
	[InlineData("mod.jar.disabled", true)]
	[InlineData("readme.txt", false)]
	[InlineData("mod.zip", false)]
	// A mutation campaign found the gap these two close. Every case above passes just as happily if the
	// disabled test is loosened to "ends with .disabled", because none of them is a NON-jar that has been
	// disabled — so the mod list would offer a stray text file as something the user can switch on.
	[InlineData("readme.txt.disabled", false)]
	[InlineData("notes.disabled", false)]
	public void OnlyJarsAndDisabledJarsCountAsMods(string fileName, bool expected)
	{
		Assert.Equal(expected, MinecraftLayout.IsModFile(fileName, ".disabled"));
	}

	[Fact]
	public void OnlyAPlainJarIsOneFabricWillLoad()
	{
		Assert.True(MinecraftLayout.IsEnabledModFile(@"C:\mc\mods\united.jar"));
		Assert.False(MinecraftLayout.IsEnabledModFile(@"C:\mc\mods\united.jar.disabled"));
	}

	[Fact]
	public void DisablingAppendsTheSuffixAndEnablingTakesItBackOff()
	{
		const string enabled = @"C:\mc\mods\united.jar";
		const string disabled = @"C:\mc\mods\united.jar.disabled";

		Assert.Equal(disabled, MinecraftLayout.PathWithEnabled(enabled, enable: false, ".disabled"));
		Assert.Equal(enabled, MinecraftLayout.PathWithEnabled(disabled, enable: true, ".disabled"));

		// Asking for the state it is already in must not double the suffix or strip the extension.
		Assert.Equal(enabled, MinecraftLayout.PathWithEnabled(enabled, enable: true, ".disabled"));
		Assert.Equal(disabled, MinecraftLayout.PathWithEnabled(disabled, enable: false, ".disabled"));
	}

	[Fact]
	public void ModEnableStateAgreesWithTheLayoutForMinecraft()
	{
		// The rest of the manager goes through ModEnableState rather than MinecraftLayout, so the two have to
		// give the same answer or a mod switches off in the list and stays on in the game.
		const string enabled = @"C:\mc\mods\united.jar";
		const string disabled = @"C:\mc\mods\united.jar.disabled";

		Assert.Equal(disabled, ModEnableState.TargetPath(enabled, enable: false, GameProfiles.Minecraft));
		Assert.Equal(enabled, ModEnableState.TargetPath(disabled, enable: true, GameProfiles.Minecraft));

		Assert.True(ModEnableState.IsEnabled(enabled, GameProfiles.Minecraft));
		Assert.False(ModEnableState.IsEnabled(disabled, GameProfiles.Minecraft));
	}

	[Fact]
	public void TheDotPrefixThatDisablesOtherGamesModsDoesNothingHere()
	{
		// Worth pinning down: a Minecraft mod named ".united.jar" is still loaded by Fabric, because Fabric
		// looks at the extension and nothing else. Borrowing Stardew's convention here would silently leave a
		// switched-off mod running.
		Assert.True(MinecraftLayout.IsEnabledModFile(@"C:\mc\mods\.united.jar"));
		Assert.True(ModEnableState.IsEnabled(@"C:\mc\mods\.united.jar", GameProfiles.Minecraft));
	}

	// -------------------------------------------------------------------------
	// Versions and update sources
	// -------------------------------------------------------------------------

	[Theory]
	[InlineData("1.1.0+mc26.2", "1.1.0")]
	[InlineData("0.160.0+26.2", "0.160.0")]
	[InlineData("1.1.0", "1.1.0")]
	[InlineData("", "")]
	public void BuildMetadataIsNotPartOfTheVersion(string version, string expected)
	{
		Assert.Equal(expected, MinecraftLayout.VersionWithoutBuildMetadata(version));
	}

	[Fact]
	public void TheSameReleaseWrittenTwoWaysIsNotAnUpdate()
	{
		// The phantom update this guards. United Minecraft's manifest says "1.1.0" and its GitHub tag says
		// "v1.1.0+mc26.2"; the general comparison splits on dots, sees a fourth segment in the tag that the
		// manifest lacks, and reports the mod out of date for ever - with "updating" downloading the very same
		// release again and changing nothing.
		Assert.True(MinecraftLayout.SameRelease("1.1.0", "1.1.0+mc26.2"));
		Assert.True(MinecraftLayout.SameRelease("0.160.0+26.2", "0.160.0+26.2"));

		// A real update still reads as one.
		Assert.False(MinecraftLayout.SameRelease("1.1.0", "1.2.0+mc26.2"));
		Assert.False(MinecraftLayout.SameRelease("0.160.0+26.2", "0.161.0+26.2"));
	}

	[Theory]
	// A mod that is not on Modrinth can still be update-checked, because its manifest names its repository.
	[InlineData("https://github.com/blindgoofball/united-Minecraft", "blindgoofball/united-Minecraft")]
	[InlineData("https://github.com/blindgoofball/united-Minecraft/issues", "blindgoofball/united-Minecraft")]
	[InlineData("https://www.github.com/owner/repo.git", "owner/repo")]
	// Anything that is not a GitHub repository gives nothing, rather than a repo name that does not exist.
	[InlineData("https://modrinth.com/mod/fabric-api", "")]
	[InlineData("https://github.com/onlyanowner", "")]
	[InlineData("not a url", "")]
	[InlineData("", "")]
	public void AGitHubHomepageBecomesARepositoryToCheck(string url, string expected)
	{
		Assert.Equal(expected, MinecraftLayout.GitHubRepoFromUrl(url));
	}

	// -------------------------------------------------------------------------
	// Paths and detection
	// -------------------------------------------------------------------------

	[Fact]
	public void TheRootFolderIsWhereEachPlatformActuallyPutsIt()
	{
		// Two answers, because Minecraft uses two. On Windows it is under %APPDATA%; on Linux and macOS it
		// is ~/.minecraft directly. This test asserted the Windows one on every platform, which meant the
		// Linux answer — ~/.config/.minecraft, a folder no Minecraft install has ever used — was green. The
		// game read as not installed on a machine where it plainly was.
		string expected = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft")
			: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".minecraft");

		Assert.Equal(expected, MinecraftLayout.DefaultRootFolder);
		Assert.Equal(Path.Combine(expected, "mods"), MinecraftLayout.ModsFolderFor(expected));
	}

	[Fact]
	public void TheRootFolderIsNeverBuriedUnderConfig()
	{
		// The specific wrong answer, named so it cannot come back: ApplicationData off Windows is ~/.config.
		Assert.DoesNotContain(Path.Combine(".config", ".minecraft"), MinecraftLayout.DefaultRootFolder);
	}

	[Fact]
	public void AnEmptyOrAbsentFolderIsNotMistakenForAnInstall()
	{
		string dir = NewTempDir();
		try
		{
			// An empty .minecraft left behind by an uninstall must not read as a working copy.
			Assert.False(MinecraftLayout.LooksLikeMinecraftRoot(dir));
			Assert.False(MinecraftLayout.LooksLikeMinecraftRoot(Path.Combine(dir, "nope")));
			Assert.False(MinecraftLayout.LooksLikeMinecraftRoot(""));

			Directory.CreateDirectory(Path.Combine(dir, "versions"));
			Assert.True(MinecraftLayout.LooksLikeMinecraftRoot(dir));
		}
		finally { Directory.Delete(dir, recursive: true); }
	}

	// -------------------------------------------------------------------------
	// The profile itself
	// -------------------------------------------------------------------------

	[Fact]
	public void MinecraftCarriesTheValuesTheManagerDependsOn()
	{
		GameProfile mc = GameProfiles.Require(GameProfiles.Minecraft);

		Assert.Equal("Minecraft", mc.DisplayName);
		Assert.Equal(ModLayout.FabricMods, mc.Layout);
		Assert.True(mc.IsMinecraft);
		Assert.False(mc.IsBepInEx);
		Assert.False(mc.IsBethesda);
		Assert.False(mc.IsWitcher3);

		// Sold by Mojang directly, so no store to detect and no store page to offer.
		Assert.False(mc.IsSoldThroughAStore);
		Assert.Equal("", mc.SteamAppId);
		Assert.Null(mc.GogProductId);

		// Fabric mods are not on Nexus.
		Assert.Equal(ModSource.Modrinth, mc.ModSource);
		Assert.Equal("", mc.NexusDomain);

		// A file suffix, not a folder prefix - the distinction the whole layout turns on.
		Assert.Equal(".disabled", mc.DisabledModSuffix);
		Assert.Null(mc.DisabledModPrefix);
	}

	[Fact]
	public void MinecraftIsTheOnlyGameWhoseModsAreFiles()
	{
		// If a second such game is ever added, every "a mod is a folder" assumption needs revisiting rather
		// than the new game quietly inheriting Minecraft's answers.
		Assert.Single(GameProfiles.All, g => g.Layout == ModLayout.FabricMods);
	}

	// -------------------------------------------------------------------------

	private static string NewTempDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "kmm-mc-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	/// <summary>Writes a one-entry zip, which is all a jar is as far as reading its manifest goes.</summary>
	private static void WriteJar(string path, string entryName, string content)
	{
		using FileStream fs = File.Create(path);
		using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

		ZipArchiveEntry entry = zip.CreateEntry(entryName);
		using Stream s = entry.Open();
		byte[] bytes = Encoding.UTF8.GetBytes(content);
		s.Write(bytes, 0, bytes.Length);
	}
}
