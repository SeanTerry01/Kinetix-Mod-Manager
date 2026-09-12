using System;
using System.IO;
using KinetixModManager;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Installing Fabric without its installer exe.
///
/// The pieces tested here are the ones a mistake in is silent: a version id that doesn't match what the meta
/// API put inside the profile JSON, a launcher entry that loses the user's other installations, or a
/// <c>lastUsed</c> that doesn't claim the Play button - which leaves the player one dropdown away from
/// launching vanilla, where the game starts, plays normally and never speaks.
/// </summary>
public class FabricInstallerTests
{
	// -------------------------------------------------------------------------
	// Naming
	// -------------------------------------------------------------------------

	[Fact]
	public void TheVersionIdMatchesWhatFabricItselfWrites()
	{
		// Taken from a real install: the folder the fabric-installer produced was
		// versions\fabric-loader-0.19.5-26.2\, and the profile JSON's own "id" field said the same. A
		// mismatch here means the launcher shows an installation that points at nothing.
		Assert.Equal("fabric-loader-0.19.5-26.2", FabricInstaller.VersionIdFor("0.19.5", "26.2"));
	}

	[Fact]
	public void TheProfileKeyCarriesTheGameVersionButNotTheLoaderVersion()
	{
		// Deliberate: installing a newer loader for the same Minecraft version should update the one entry
		// rather than leave the user with a list of near-identical installations to choose between.
		Assert.Equal("fabric-loader-26.2", FabricInstaller.ProfileKeyFor("26.2"));
		Assert.Equal(
			FabricInstaller.ProfileKeyFor("26.2"),
			FabricInstaller.ProfileKeyFor("26.2"));
	}

	[Fact]
	public void TheVersionJsonGoesWhereTheLauncherLooksForIt()
	{
		string path = FabricInstaller.VersionJsonPathFor(@"C:\mc", "fabric-loader-0.19.5-26.2");

		Assert.Equal(
			Path.Combine(@"C:\mc", "versions", "fabric-loader-0.19.5-26.2", "fabric-loader-0.19.5-26.2.json"),
			path);
	}

	[Theory]
	// The game version is everything after the loader version, not the last hyphenated part - a snapshot or
	// release candidate carries hyphens of its own, and taking the final segment would answer "2".
	[InlineData("fabric-loader-0.19.5-26.2", "0.19.5", "26.2")]
	[InlineData("fabric-loader-0.19.5-26.3-rc-2", "0.19.5", "26.3-rc-2")]
	[InlineData("fabric-loader-0.16.10-1.21.4", "0.16.10", "1.21.4")]
	// Not Fabric's, so neither answer is guessed at.
	[InlineData("26.2", "", "")]
	[InlineData("", "", "")]
	public void AVersionIdSplitsBackIntoItsLoaderAndItsGame(string versionId, string loader, string game)
	{
		Assert.Equal(loader, FabricInstaller.LoaderVersionOf(versionId));
		Assert.Equal(game, FabricInstaller.GameVersionOf(versionId));
	}

	[Fact]
	public void AHandInstalledFabricIsAdoptedRatherThanReportedMissing()
	{
		// The bug this guards: the suite asked "is Fabric installed for the PINNED version?", and nothing was
		// pinned until the manager had installed it once. Anyone who set Fabric up before installing the
		// manager - which is most people already playing modded - was told their working install was missing.
		string dir = NewTempDir();
		try
		{
			Assert.Equal("", FabricInstaller.DetectInstalledGameVersion(dir));

			Directory.CreateDirectory(Path.Combine(dir, "versions", "fabric-loader-0.19.5-26.2"));
			Directory.CreateDirectory(Path.Combine(dir, "versions", "26.2"));

			Assert.Equal("26.2", FabricInstaller.DetectInstalledGameVersion(dir));
		}
		finally { Directory.Delete(dir, recursive: true); }
	}

	// -------------------------------------------------------------------------
	// Picking a version
	// -------------------------------------------------------------------------

	[Fact]
	public void SnapshotsAndReleaseCandidatesAreSkippedInFavourOfTheNewestStableRelease()
	{
		// Shaped like the real /v2/versions/game response, which is ordered newest first and routinely opens
		// with release candidates. Taking entry zero would install Fabric for a version no mod is built for
		// yet: the profile would work and the mods folder would do nothing.
		var games = JArray.Parse("""
		[
			{ "version": "26.3-rc-2",  "stable": false },
			{ "version": "26.3-rc-1",  "stable": false },
			{ "version": "26.3-pre-1", "stable": false },
			{ "version": "26.2",       "stable": true  },
			{ "version": "26.1",       "stable": true  }
		]
		""");

		Assert.Equal("26.2", FabricInstaller.FirstStableVersion(games));
	}

	[Fact]
	public void AListWithNothingStableIsAnErrorRatherThanAGuess()
	{
		var games = JArray.Parse("""[ { "version": "26.3-rc-2", "stable": false } ]""");

		Assert.Throws<InvalidOperationException>(() => FabricInstaller.FirstStableVersion(games));
	}

	// -------------------------------------------------------------------------
	// The launcher's installation list
	// -------------------------------------------------------------------------

	[Fact]
	public void AddingAnInstallationLeavesEverythingElseInTheFileAlone()
	{
		// The launcher's own file, trimmed. Losing the user's other installations or their settings would be
		// a far worse failure than not installing at all.
		var root = FabricInstaller.ParsePreservingDates("""
		{
			"profiles": {
				"aae77e44": { "lastVersionId": "latest-release", "type": "latest-release", "name": "" }
			},
			"settings": { "profileSorting": "ByLastPlayed", "keepLauncherOpen": false },
			"version": 6
		}
		""");

		string result = FabricInstaller.UpsertLauncherProfileJson(
			root, "26.2", "fabric-loader-0.19.5-26.2", new DateTime(2026, 9, 12, 0, 11, 43, 867, DateTimeKind.Utc));

		JObject after = FabricInstaller.ParsePreservingDates(result);

		Assert.NotNull(after["profiles"]!["aae77e44"]);
		Assert.Equal("latest-release", (string?)after["profiles"]!["aae77e44"]!["lastVersionId"]);
		Assert.Equal("ByLastPlayed", (string?)after["settings"]!["profileSorting"]);
		Assert.Equal(6, (int?)after["version"]);

		JToken fabric = after["profiles"]!["fabric-loader-26.2"]!;
		Assert.Equal("fabric-loader-0.19.5-26.2", (string?)fabric["lastVersionId"]);
		Assert.Equal("custom", (string?)fabric["type"]);
	}

	[Fact]
	public void TheNewInstallationClaimsTheMostRecentlyUsedSpot()
	{
		// This is the whole point rather than a detail. launcher_profiles.json version 6 has no
		// selectedProfile key - the Play button follows whichever installation was used most recently. A
		// profile written without claiming that spot is one the user has to go and find.
		var root = FabricInstaller.ParsePreservingDates("""
		{
			"profiles": {
				"vanilla": { "lastVersionId": "latest-release", "lastUsed": "2026-09-11T20:54:24.830Z" }
			},
			"version": 6
		}
		""");

		var now = new DateTime(2026, 9, 12, 0, 11, 43, 867, DateTimeKind.Utc);
		JObject after = FabricInstaller.ParsePreservingDates(
			FabricInstaller.UpsertLauncherProfileJson(root, "26.2", "fabric-loader-0.19.5-26.2", now));

		string fabricLastUsed = (string?)after["profiles"]!["fabric-loader-26.2"]!["lastUsed"] ?? "";
		string vanillaLastUsed = (string?)after["profiles"]!["vanilla"]!["lastUsed"] ?? "";

		Assert.Equal("2026-09-12T00:11:43.867Z", fabricLastUsed);
		// String comparison is the right one here: the launcher's timestamps are ISO-8601 UTC, so they sort
		// lexically in the same order they sort chronologically.
		Assert.True(string.CompareOrdinal(fabricLastUsed, vanillaLastUsed) > 0);
	}

	[Fact]
	public void EveryOtherInstallationsTimestampSurvivesUntouched()
	{
		// The bug this guards against was found by the test above and is worth its own: Json.NET recognises
		// ISO-8601 strings, converts them to DateTime, and re-serialises them in its own format. Reading the
		// launcher's file and writing it back therefore rewrote EVERY timestamp in it - not just the one being
		// changed - into "09/11/2026 20:54:24", a shape the launcher never writes.
		var root = FabricInstaller.ParsePreservingDates("""
		{
			"profiles": {
				"vanilla":  { "lastUsed": "2026-09-11T20:54:24.830Z", "created": "1970-01-02T00:00:00.000Z" },
				"snapshot": { "lastUsed": "1970-01-01T00:00:00.000Z" }
			},
			"version": 6
		}
		""");

		string result = FabricInstaller.UpsertLauncherProfileJson(
			root, "26.2", "fabric-loader-0.19.5-26.2", DateTime.UtcNow);

		Assert.Contains("2026-09-11T20:54:24.830Z", result);
		Assert.Contains("1970-01-02T00:00:00.000Z", result);
		Assert.Contains("1970-01-01T00:00:00.000Z", result);
	}

	[Fact]
	public void ReinstallingUpdatesTheExistingEntryRatherThanAddingASecond()
	{
		var root = FabricInstaller.ParsePreservingDates("""
		{
			"profiles": {
				"fabric-loader-26.2": {
					"lastVersionId": "fabric-loader-0.19.4-26.2",
					"lastUsed": "2026-09-01T00:00:00.000Z",
					"name": "My Renamed Fabric",
					"type": "custom"
				}
			},
			"version": 6
		}
		""");

		JObject after = FabricInstaller.ParsePreservingDates(FabricInstaller.UpsertLauncherProfileJson(
			root, "26.2", "fabric-loader-0.19.5-26.2", DateTime.UtcNow));

		Assert.Single(after["profiles"]!.Children());
		Assert.Equal("fabric-loader-0.19.5-26.2", (string?)after["profiles"]!["fabric-loader-26.2"]!["lastVersionId"]);
		// A name the user chose is theirs; upgrading the loader must not rename their installation.
		Assert.Equal("My Renamed Fabric", (string?)after["profiles"]!["fabric-loader-26.2"]!["name"]);
	}

	[Fact]
	public void AnAbsentOrEmptyFileStillProducesAUsableInstallationList()
	{
		JObject after = FabricInstaller.ParsePreservingDates(FabricInstaller.UpsertLauncherProfileJson(
			new JObject(), "26.2", "fabric-loader-0.19.5-26.2", DateTime.UtcNow));

		Assert.Equal("fabric-loader-0.19.5-26.2", (string?)after["profiles"]!["fabric-loader-26.2"]!["lastVersionId"]);
		Assert.Equal(6, (int?)after["version"]);
	}

	[Fact]
	public void TimestampsAreWrittenTheWayTheLauncherWritesThem()
	{
		Assert.Equal(
			"2026-09-12T00:11:43.867Z",
			FabricInstaller.FormatLauncherTime(new DateTime(2026, 9, 12, 0, 11, 43, 867, DateTimeKind.Utc)));
	}

	// -------------------------------------------------------------------------
	// Detecting an existing install
	// -------------------------------------------------------------------------

	[Fact]
	public void AnInstalledFabricIsRecognisedByItsVersionFolder()
	{
		string dir = NewTempDir();
		try
		{
			Assert.False(FabricInstaller.IsInstalledFor(dir, "26.2"));

			Directory.CreateDirectory(Path.Combine(dir, "versions", "fabric-loader-0.19.5-26.2"));

			Assert.True(FabricInstaller.IsInstalledFor(dir, "26.2"));
			// A Fabric install for a DIFFERENT Minecraft version is not this one - it would load nothing.
			Assert.False(FabricInstaller.IsInstalledFor(dir, "26.3"));
			Assert.Contains("fabric-loader-0.19.5-26.2", FabricInstaller.InstalledVersionIds(dir));
		}
		finally { Directory.Delete(dir, recursive: true); }
	}

	[Fact]
	public void PlainVanillaVersionFoldersAreNotMistakenForFabric()
	{
		string dir = NewTempDir();
		try
		{
			Directory.CreateDirectory(Path.Combine(dir, "versions", "26.2"));
			Directory.CreateDirectory(Path.Combine(dir, "versions", "26.3-rc-2"));

			Assert.False(FabricInstaller.IsInstalledFor(dir, "26.2"));
			Assert.Empty(FabricInstaller.InstalledVersionIds(dir));
		}
		finally { Directory.Delete(dir, recursive: true); }
	}

	// -------------------------------------------------------------------------
	// Quick Play
	// -------------------------------------------------------------------------

	[Fact]
	public void QuickPlayTilesArePointedAtTheFabricInstallation()
	{
		// The real shape, and the real bug: a tile stores its own configId, baked in when the world was first
		// played, and launching from it ignores the selected installation entirely. Observed live - the right
		// installation was selected and a tile launched vanilla eleven seconds later.
		string dir = NewTempDir();
		try
		{
			File.WriteAllText(Path.Combine(dir, "launcher_quick_play.json"), """
			{
				"quickPlayData": {
					"2535407176901573": [
						{ "id": "Silky World", "name": "Silky World",
						  "javaInstance": { "configId": "aae77e4460c78e9ff3518e69315822ab",
											"game": { "gamemode": "survival", "type": "singleplayer" } } },
						{ "id": "aae77e4460c78e9ff3518e69315822ab",
						  "javaInstance": { "configId": "aae77e4460c78e9ff3518e69315822ab" } }
					]
				},
				"version": 2
			}
			""");

			int changed = FabricInstaller.RepointQuickPlay(dir, "fabric-loader-26.2");

			Assert.Equal(2, changed);

			JObject after = FabricInstaller.ParsePreservingDates(File.ReadAllText(Path.Combine(dir, "launcher_quick_play.json")));
			foreach (JToken entry in after["quickPlayData"]!["2535407176901573"]!)
				Assert.Equal("fabric-loader-26.2", (string?)entry["javaInstance"]!["configId"]);

			// The world it points at has to survive, or the tile stops being the thing the user reaches for.
			Assert.Equal("Silky World", (string?)after["quickPlayData"]!["2535407176901573"]![0]!["name"]);

			// Running it again changes nothing.
			Assert.Equal(0, FabricInstaller.RepointQuickPlay(dir, "fabric-loader-26.2"));
		}
		finally { Directory.Delete(dir, recursive: true); }
	}

	[Fact]
	public void AMissingOrUnreadableQuickPlayFileIsNotAnError()
	{
		string dir = NewTempDir();
		try
		{
			Assert.Equal(0, FabricInstaller.RepointQuickPlay(dir, "fabric-loader-26.2"));

			File.WriteAllText(Path.Combine(dir, "launcher_quick_play.json"), "{ not json");
			Assert.Equal(0, FabricInstaller.RepointQuickPlay(dir, "fabric-loader-26.2"));
		}
		finally { Directory.Delete(dir, recursive: true); }
	}

	// -------------------------------------------------------------------------

	private static string NewTempDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "kmm-fabric-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}
}
