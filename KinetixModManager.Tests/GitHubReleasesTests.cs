using System;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="GitHubReleases"/> — installing a mod from a repository the user names.
///
/// <para>
/// GitHub is not a catalogue and is not in the source chooser: there is nothing to search, because you cannot
/// ask GitHub for "Stardew mods about fishing". What a user can do is name a repository, and a great many
/// mods are released there and nowhere else.
/// </para>
///
/// <para>
/// The whole difficulty is picking the right file out of a release, and the case that matters most is a
/// Fabric mod. Fabric publishes <c>sodium-0.6.0.jar</c> and <c>sodium-0.6.0-sources.jar</c> side by side, and
/// installing the second gives a mods folder that looks correct, a game that starts, and no mod — which is
/// exactly the failure the whole of Minecraft support exists to prevent.
/// </para>
/// </summary>
public class GitHubReleasesTests
{
	// ---------------------------------------------------------------------
	// Saying which repository
	// ---------------------------------------------------------------------

	[Theory]
	[InlineData("Pathoschild/SMAPI")]
	[InlineData("https://github.com/Pathoschild/SMAPI")]
	[InlineData("https://github.com/Pathoschild/SMAPI/releases")]
	[InlineData("https://github.com/Pathoschild/SMAPI/releases/tag/4.1.10")]
	[InlineData("https://api.github.com/repos/Pathoschild/SMAPI/releases/latest")]
	[InlineData("git@github.com:Pathoschild/SMAPI.git")]
	[InlineData("  Pathoschild/SMAPI  ")]
	public void ARepositoryIsReadFromWhateverTheUserPastes(string typed)
	{
		// The user is coming from a browser. Asking them to read an address aloud to themselves and retype two
		// words out of the middle of it is asking them to do by hand what a pattern does exactly.
		Assert.Equal("Pathoschild/SMAPI", GitHubReleases.ParseRepo(typed));
	}

	[Theory]
	[InlineData("")]
	[InlineData(null)]
	[InlineData("SMAPI")]
	[InlineData("https://www.nexusmods.com/stardewvalley/mods/2400")]
	[InlineData("just some words")]
	public void SomethingThatIsNotARepositoryIsRefusedRatherThanGuessedAt(string? typed)
	{
		Assert.Null(GitHubReleases.ParseRepo(typed));
	}

	// ---------------------------------------------------------------------
	// Reading a release
	// ---------------------------------------------------------------------

	private const string Release = """
	{
	  "tag_name": "4.1.10",
	  "assets": [
	    { "name": "SMAPI-4.1.10-installer.zip", "browser_download_url": "https://example.test/installer.zip", "size": 4000000 },
	    { "name": "SMAPI-4.1.10.md",            "browser_download_url": "https://example.test/notes.md",      "size": 900 }
	  ]
	}
	""";

	[Fact]
	public void AReleaseIsReadWithItsTagAndItsFiles()
	{
		GitHubRelease? release = GitHubReleases.Parse(Release);

		Assert.NotNull(release);
		Assert.Equal("4.1.10", release!.TagName);
		Assert.Equal(2, release.Assets.Count);
		Assert.Equal(4000000, release.Assets[0].Size);
	}

	[Fact]
	public void SomethingThatIsNotAReleaseIsNullRatherThanAThrow()
	{
		Assert.Null(GitHubReleases.Parse("not json at all"));
	}

	[Fact]
	public void AReleaseWithNoFilesAtAllIsStillARelease()
	{
		// Common: a tag published with release notes and nothing attached. It is not a failure to read, it is
		// a release with nothing to install — which the caller says differently.
		GitHubRelease? release = GitHubReleases.Parse("""{ "tag_name": "1.0.0" }""");

		Assert.NotNull(release);
		Assert.Empty(release!.Assets);
		Assert.Null(GitHubReleases.PickModAsset(release, wantJar: false));
	}

	// ---------------------------------------------------------------------
	// Picking the file
	// ---------------------------------------------------------------------

	[Fact]
	public void DocumentationIsNeverTheMod()
	{
		GitHubRelease release = GitHubReleases.Parse(Release)!;

		Assert.Equal("SMAPI-4.1.10-installer.zip", GitHubReleases.PickModAsset(release, wantJar: false)!.Name);
	}

	[Fact]
	public void AFabricSourcesJarIsNotTheMod()
	{
		// The one that matters. Installing this gives a mods folder that looks right, a game that starts, and
		// no mod — with nothing anywhere saying why.
		GitHubRelease release = GitHubReleases.Parse("""
		{
		  "tag_name": "0.6.0",
		  "assets": [
		    { "name": "sodium-fabric-0.6.0-sources.jar", "browser_download_url": "https://example.test/sources.jar" },
		    { "name": "sodium-fabric-0.6.0.jar",         "browser_download_url": "https://example.test/mod.jar" },
		    { "name": "sodium-fabric-0.6.0-dev.jar",     "browser_download_url": "https://example.test/dev.jar" }
		  ]
		}
		""")!;

		Assert.Equal("sodium-fabric-0.6.0.jar", GitHubReleases.PickModAsset(release, wantJar: true)!.Name);
	}

	[Fact]
	public void ASourcesJarIsTakenOnlyWhenItIsAllThereIs()
	{
		// Saying "nothing usable" about a file sitting right there is its own kind of unhelpful, so it is
		// offered — it is simply never preferred.
		GitHubRelease release = GitHubReleases.Parse("""
		{
		  "tag_name": "0.6.0",
		  "assets": [{ "name": "thing-sources.jar", "browser_download_url": "https://example.test/s.jar" }]
		}
		""")!;

		Assert.Equal("thing-sources.jar", GitHubReleases.PickModAsset(release, wantJar: true)!.Name);
	}

	[Fact]
	public void AJarIsNotAModForAGameThatDoesNotLoadJars()
	{
		GitHubRelease release = GitHubReleases.Parse("""
		{
		  "tag_name": "1.0.0",
		  "assets": [{ "name": "something.jar", "browser_download_url": "https://example.test/x.jar" }]
		}
		""")!;

		Assert.Null(GitHubReleases.PickModAsset(release, wantJar: false));
	}

	[Fact]
	public void AZipIsPreferredToA7zOrARar()
	{
		// A release publishing both is publishing the same mod twice, and the zip is the one every extractor
		// handles without a thought.
		GitHubRelease release = GitHubReleases.Parse("""
		{
		  "tag_name": "1.0.0",
		  "assets": [
		    { "name": "mod.rar", "browser_download_url": "https://example.test/a.rar" },
		    { "name": "mod.7z",  "browser_download_url": "https://example.test/a.7z" },
		    { "name": "mod.zip", "browser_download_url": "https://example.test/a.zip" }
		  ]
		}
		""")!;

		Assert.Equal("mod.zip", GitHubReleases.PickModAsset(release, wantJar: false)!.Name);
	}

	[Fact]
	public void A7zIsTakenWhenThereIsNoZip()
	{
		GitHubRelease release = GitHubReleases.Parse("""
		{
		  "tag_name": "1.0.0",
		  "assets": [
		    { "name": "readme.txt", "browser_download_url": "https://example.test/r.txt" },
		    { "name": "mod.7z",     "browser_download_url": "https://example.test/a.7z" }
		  ]
		}
		""")!;

		Assert.Equal("mod.7z", GitHubReleases.PickModAsset(release, wantJar: false)!.Name);
	}

	[Fact]
	public void AReleaseOfNothingButChecksumsHasNoModInIt()
	{
		GitHubRelease release = GitHubReleases.Parse("""
		{
		  "tag_name": "1.0.0",
		  "assets": [
		    { "name": "mod.zip.sha256", "browser_download_url": "https://example.test/a.sha256" },
		    { "name": "mod.zip.asc",    "browser_download_url": "https://example.test/a.asc" }
		  ]
		}
		""")!;

		Assert.Null(GitHubReleases.PickModAsset(release, wantJar: false));
	}

	[Fact]
	public void TheApiAddressIsBuiltFromTheRepository()
	{
		Assert.Equal("https://api.github.com/repos/Pathoschild/SMAPI/releases/latest",
			GitHubReleases.LatestReleaseApiUrl("Pathoschild/SMAPI"));
	}
}
