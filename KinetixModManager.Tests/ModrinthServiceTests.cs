using System.Linq;
using KinetixModManager;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Reading Modrinth, which is where Minecraft's Fabric mods live.
///
/// The fixtures are recorded from the live API rather than written by hand - a real search for "access"
/// filtered to Fabric on Minecraft 26.2, and a real version listing. That is what confirms the field names
/// actually used, which is the whole risk in a mapping like this: a typo in one produces a search result
/// whose title, downloads or date are silently empty, and nothing fails.
/// </summary>
public class ModrinthServiceTests
{
	// Trimmed from the live /v2/search response.
	private const string RealSearchResponse = """
	{
		"hits": [
			{
				"slug": "uwrad",
				"project_id": "MsGuTgPJ",
				"title": "Unsafe World Random Access Detector",
				"author": "ishland",
				"description": "This is a mod that detects unsafe off-thread world random access.",
				"downloads": 1606615,
				"follows": 50,
				"date_modified": "2026-03-24T13:52:24.599164+00:00"
			},
			{
				"slug": "accessible-step",
				"project_id": "z6d6n7ve",
				"title": "Accessible Step",
				"author": "secret_online",
				"description": "Step up full blocks without reaching for the jump button.",
				"downloads": 179831,
				"follows": 147,
				"date_modified": "2026-06-17T00:10:11.074307+00:00"
			}
		],
		"offset": 0,
		"limit": 2,
		"total_hits": 72
	}
	""";

	[Fact]
	public void ASearchResultCarriesEverythingTheRowReadsOut()
	{
		var (results, total) = ModrinthService.ParseSearch(JObject.Parse(RealSearchResponse));

		Assert.Equal(72, total);
		Assert.Equal(2, results.Count);

		GameMod first = results[0];
		Assert.True(first.IsSearchResult);
		Assert.Equal("Unsafe World Random Access Detector", first.Name);
		Assert.Equal("ishland", first.Author);
		Assert.Equal(1606615, first.Downloads);
		// Modrinth has no endorsements; follows is the nearest thing it keeps.
		Assert.Equal(50, first.Endorsements);
		Assert.Equal(2026, first.LastUpdated!.Value.Year);
	}

	[Fact]
	public void TheSlugIsTheIdBecauseItIsWhatTheUrlAndTheApiBothTake()
	{
		var (results, _) = ModrinthService.ParseSearch(JObject.Parse(RealSearchResponse));

		Assert.Equal("uwrad", results[0].ModrinthId);
		// And it must NOT land in the Nexus field: a value there would send a download or an update check to
		// the wrong catalogue, asking about somebody else's mod.
		Assert.Null(results[0].NexusID);
	}

	[Fact]
	public void AHitWithNoSlugFallsBackToItsProjectId()
	{
		var (results, _) = ModrinthService.ParseSearch(JObject.Parse("""
		{ "hits": [ { "project_id": "AABBCCDD", "title": "No Slug Mod" } ], "total_hits": 1 }
		"""));

		Assert.Equal("AABBCCDD", Assert.Single(results).ModrinthId);
	}

	[Fact]
	public void AHitWithNoIdAtAllIsSkippedRatherThanListedUnusable()
	{
		// A row with no id cannot be opened, downloaded or update-checked. Better absent than present and inert.
		var (results, _) = ModrinthService.ParseSearch(JObject.Parse("""
		{ "hits": [ { "title": "Nameless" } ], "total_hits": 1 }
		"""));

		Assert.Empty(results);
	}

	[Fact]
	public void AModrinthResultReadsOutItsSlugAsTheId()
	{
		var (results, _) = ModrinthService.ParseSearch(JObject.Parse(RealSearchResponse));

		string row = results[1].ToString();

		Assert.StartsWith("Accessible Step (ID: accessible-step).", row);
		Assert.Contains("179,831 downloads", row);
		Assert.Contains("147 endorsements", row);
	}

	[Fact]
	public void AResultWithNoIdAtAllDoesNotReadAnEmptyOneAloud()
	{
		// Guards the row format itself: "(ID: )" at the start of every row would be noise on a list the user
		// is arrowing through.
		var mod = new GameMod { IsSearchResult = true, Name = "Something", Description = "A mod." };

		Assert.Equal("Something. A mod.", mod.ToString());
	}

	// -------------------------------------------------------------------------
	// Picking a file for a version
	// -------------------------------------------------------------------------

	[Fact]
	public void TheNewestVersionWithARealFileWins()
	{
		// Modrinth returns versions newest first. A version whose files are all non-primary is a source jar or
		// a supplementary download, not the mod, so it is skipped rather than guessed at.
		var versions = JArray.Parse("""
		[
			{ "version_number": "2.0.0", "game_versions": ["26.2"],
			  "files": [ { "primary": false, "filename": "sources.jar", "url": "https://x/sources.jar" } ] },
			{ "version_number": "1.9.0", "game_versions": ["26.2"],
			  "files": [ { "primary": true, "filename": "mod-1.9.0.jar", "url": "https://x/mod-1.9.0.jar", "size": 4242 } ] }
		]
		""");

		ModrinthFile? file = ModrinthService.ParseNewestFile(versions);

		Assert.NotNull(file);
		Assert.Equal("1.9.0", file!.VersionNumber);
		Assert.Equal("mod-1.9.0.jar", file.FileName);
		Assert.Equal(4242, file.Size);
	}

	[Fact]
	public void OnlyRequiredDependenciesAreReportedAndNotEmbeddedOnes()
	{
		// The distinction that makes the two accessibility mods behave differently. Minecraft Access lists
		// fabric-api as "embedded" because it ships it inside its own jar; reporting that would have the
		// manager installing Fabric API beside a mod that already contains it.
		var versions = JArray.Parse("""
		[
			{ "version_number": "1.12.0", "game_versions": ["26.2"],
			  "files": [ { "primary": true, "filename": "a.jar", "url": "https://x/a.jar" } ],
			  "dependencies": [
				{ "project_id": "P7dR8mSH", "dependency_type": "embedded" },
				{ "project_id": "9s6osm5g", "dependency_type": "embedded" },
				{ "project_id": "nvQzSEkH", "dependency_type": "optional" },
				{ "project_id": "REQUIRED1", "dependency_type": "required" }
			  ] }
		]
		""");

		ModrinthFile file = ModrinthService.ParseNewestFile(versions)!;

		Assert.Equal(new[] { "REQUIRED1" }, file.RequiredDependencyProjectIds);
	}

	// -------------------------------------------------------------------------
	// Updates by file hash
	// -------------------------------------------------------------------------

	[Fact]
	public void AnUpdateIsFoundByTheHashOfTheJarThatIsInstalled()
	{
		// Shaped like the real /v2/version_files/update reply: an object keyed by the hash asked about. This
		// is exact identification - the file IS that release of that project - rather than the name matching
		// used elsewhere, which once installed a six-week-old build over a working one.
		var response = JObject.Parse("""
		{
			"58e82722dee4a78235c4b5c06100ae433973f62e": {
				"project_id": "P7dR8mSH",
				"version_number": "0.161.0+26.2",
				"game_versions": ["26.2"],
				"files": [ { "primary": true, "filename": "fabric-api-0.161.0+26.2.jar",
							 "url": "https://cdn.modrinth.com/x.jar", "size": 2542898 } ]
			}
		}
		""");

		var updates = ModrinthService.ParseHashUpdates(response);

		Assert.Single(updates);
		Assert.Equal("0.161.0+26.2", updates["58e82722dee4a78235c4b5c06100ae433973f62e"].VersionNumber);
		// Case-insensitive, because a hash written in upper case is the same hash.
		Assert.True(updates.ContainsKey("58E82722DEE4A78235C4B5C06100AE433973F62E"));
	}

	[Fact]
	public void AModModrinthDoesNotHostIsAbsentRatherThanReportedUpToDate()
	{
		// Verified against the live API: United Minecraft's jar returns nothing at all, because it is
		// published on GitHub only. Absent must not be read as "nothing newer" - that would quietly stop
		// checking the one mod a blind player most needs kept current.
		var updates = ModrinthService.ParseHashUpdates(JObject.Parse("{}"));

		Assert.Empty(updates);
		Assert.False(updates.ContainsKey("d003f8f5c9b66c052b1e4778f2ba5a1f0794e580"));
	}

	[Fact]
	public void AHashWhoseEntryCarriesNoUsableFileIsSkipped()
	{
		var updates = ModrinthService.ParseHashUpdates(JObject.Parse("""
		{ "abc123": { "version_number": "9.9.9", "files": [ { "primary": false, "filename": "sources.jar" } ] } }
		"""));

		Assert.Empty(updates);
	}

	[Fact]
	public void NoBuildForThisVersionIsNullRatherThanSomeOtherVersionsBuild()
	{
		// A mod built for a different Minecraft version installs cleanly and then loads nothing, which is the
		// silent failure this whole game is prone to. Saying "there is none" is the useful answer.
		Assert.Null(ModrinthService.ParseNewestFile(new JArray()));
	}
}
