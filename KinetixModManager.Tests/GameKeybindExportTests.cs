using System;
using System.IO;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="GameKeybindExport"/>, which reads a game's own keyboard bindings from the JSON a
/// keybind-export plugin writes.
///
/// The point of these is the fallback: a player who has just bought the game has no plugin and has never
/// launched it, so the manager must still be able to show the stock controls from its own bundled snapshot —
/// and must be able to tell the two apart, because showing defaults to someone who has remapped a key looks
/// like the manager being wrong about their game rather than a snapshot being out of date.
/// </summary>
public class GameKeybindExportTests
{
	private const string LiveJson = """
	{
	  "schemaVersion": 1,
	  "game": "Moonlight Peaks",
	  "source": "com.moonlightpeaks.keybindexport",
	  "generatedUtc": "2026-08-02T22:41:41Z",
	  "device": "Keyboard",
	  "bindings": [
	    { "key": "E", "modifiers": "", "combo": "E", "actions": ["Tool Wheel", "Rotate Right"] },
	    { "key": "U", "modifiers": "Control", "combo": "Control+U", "actions": ["Unstick"] }
	  ],
	  "freeKeys": ["K", "L"]
	}
	""";

	private const string BundledJson = """
	{
	  "schemaVersion": 1,
	  "source": "bundled-defaults",
	  "bindings": [ { "key": "M", "modifiers": "", "combo": "M", "actions": ["Map"] } ]
	}
	""";

	// -------------------------------------------------------------------------
	// Parsing
	// -------------------------------------------------------------------------

	[Fact]
	public void Parse_ReadsEveryBindingWithAllOfItsActions()
	{
		GameKeybindExport? export = GameKeybindExport.Parse(LiveJson);

		Assert.NotNull(export);
		Assert.Equal(2, export!.Bindings.Count);

		// A key routinely drives several actions on different screens; none may be dropped.
		Assert.Equal(new[] { "Tool Wheel", "Rotate Right" }, export.Bindings[0].Actions);
		Assert.Equal("Control+U", export.Bindings[1].Combo);
		Assert.Equal("Control", export.Bindings[1].Modifiers);
		Assert.Equal(new[] { "K", "L" }, export.FreeKeys);
	}

	[Fact]
	public void Parse_KnowsALiveExportFromTheBundledSnapshot()
	{
		Assert.True(GameKeybindExport.Parse(LiveJson)!.IsLive);
		Assert.False(GameKeybindExport.Parse(BundledJson)!.IsLive);
	}

	[Fact]
	public void Parse_ReadsGeneratedTimeAsUtc()
	{
		DateTime? generated = GameKeybindExport.Parse(LiveJson)!.GeneratedUtc;

		Assert.NotNull(generated);
		Assert.Equal(new DateTime(2026, 8, 2, 22, 41, 41, DateTimeKind.Utc), generated!.Value);
	}

	[Fact]
	public void Parse_FillsInAMissingComboFromTheKeyAndModifiers()
	{
		GameKeybindExport? export = GameKeybindExport.Parse("""
		{ "schemaVersion": 1, "bindings": [ { "key": "U", "modifiers": "Control", "actions": ["Unstick"] } ] }
		""");

		Assert.Equal("Control+U", export!.Bindings[0].Combo);
	}

	[Theory]
	// A schema this reader doesn't know must be refused rather than half-understood.
	[InlineData("""{ "schemaVersion": 2, "bindings": [ { "key": "M", "actions": ["Map"] } ] }""")]
	// Nothing usable in it: fall through to the next source instead of showing an empty control list.
	[InlineData("""{ "schemaVersion": 1, "bindings": [] }""")]
	[InlineData("""{ "schemaVersion": 1 }""")]
	// A binding with no key is not a binding.
	[InlineData("""{ "schemaVersion": 1, "bindings": [ { "modifiers": "Control", "actions": ["Unstick"] } ] }""")]
	[InlineData("not json at all")]
	[InlineData("")]
	public void Parse_RefusesAnythingItCannotHonestlyShow(string json)
	{
		Assert.Null(GameKeybindExport.Parse(json));
	}

	// -------------------------------------------------------------------------
	// Which file wins
	// -------------------------------------------------------------------------

	[Fact]
	public void Load_PrefersTheLiveExportOverTheBundledSnapshot()
	{
		(string game, string app) = WriteBoth(LiveJson, BundledJson);

		GameKeybindExport? export = GameKeybindExport.Load(Profile(), game, app);

		Assert.True(export!.IsLive);
		Assert.Equal("E", export.Bindings[0].Key);
	}

	[Fact]
	public void Load_FallsBackToTheBundledSnapshotWhenTheGameHasNeverBeenLaunched()
	{
		(string game, string app) = WriteBoth(liveJson: null, BundledJson);

		GameKeybindExport? export = GameKeybindExport.Load(Profile(), game, app);

		Assert.False(export!.IsLive);
		Assert.Equal("M", export.Bindings[0].Key);
	}

	[Fact]
	public void Load_FallsBackToTheSnapshotWhenTheLiveExportIsUnusable()
	{
		(string game, string app) = WriteBoth("{ corrupt", BundledJson);

		GameKeybindExport? export = GameKeybindExport.Load(Profile(), game, app);

		Assert.False(export!.IsLive);
	}

	[Fact]
	public void Load_ReturnsNothingForAGameWithNoExportOfEitherKind()
	{
		(string game, string app) = WriteBoth(liveJson: null, bundledJson: null);

		Assert.Null(GameKeybindExport.Load(GameProfiles.Find(GameProfiles.StardewValley), game, app));
	}

	[Fact]
	public void LiveExportPath_FollowsTheLocationThePluginsConfigAsksFor()
	{
		string game = NewFolder();
		Directory.CreateDirectory(Path.Combine(game, "BepInEx", "config"));
		File.WriteAllText(Path.Combine(game, "BepInEx", "config", "com.moonlightpeaks.keybindexport.cfg"),
			"""
			[Output]

			## Where to write the file.
			# Setting type: String
			# Default value: moonlight-keybinds.json
			Path = elsewhere.json
			""");

		Assert.Equal(Path.Combine(game, "BepInEx", "elsewhere.json"),
			GameKeybindExport.LiveExportPath(Profile()!, game));
	}

	[Fact]
	public void LiveExportPath_UsesTheDefaultLocationWhenThereIsNoConfig()
	{
		string game = NewFolder();

		Assert.Equal(Path.Combine(game, @"BepInEx\moonlight-keybinds.json"),
			GameKeybindExport.LiveExportPath(Profile()!, game));
	}

	// -------------------------------------------------------------------------

	private static GameProfile? Profile() => GameProfiles.Find(GameProfiles.MoonlightPeaks);

	private static string NewFolder()
	{
		string path = Path.Combine(Path.GetTempPath(), "kmm-keybinds-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	/// <summary>Lays out a game folder and an app folder, writing whichever of the two files is wanted.</summary>
	private static (string GameFolder, string AppFolder) WriteBoth(string? liveJson, string? bundledJson)
	{
		string game = NewFolder();
		string app = NewFolder();

		if (liveJson != null)
		{
			Directory.CreateDirectory(Path.Combine(game, "BepInEx"));
			File.WriteAllText(Path.Combine(game, "BepInEx", "moonlight-keybinds.json"), liveJson);
		}

		if (bundledJson != null)
		{
			Directory.CreateDirectory(Path.Combine(app, "data"));
			File.WriteAllText(Path.Combine(app, "data", "moonlight-peaks.defaults.json"), bundledJson);
		}

		return (game, app);
	}
}
