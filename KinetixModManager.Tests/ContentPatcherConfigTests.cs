using System;
using System.Collections.Generic;
using System.IO;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ContentPatcherConfig"/>, which reads what a Content Patcher pack lets the player change.
///
/// The fixtures here are deliberately ugly, because real packs are: every quirk below was copied from a pack
/// actually installed on a working Stardew setup — <c>//</c> comments, trailing commas, a trailing comma inside
/// an <c>AllowValues</c> string, wild indentation, settings with no allowed values at all. A reader that only
/// handles tidy JSON would fail exactly the files this feature exists to read.
/// </summary>
public class ContentPatcherConfigTests
{
	// Modelled on East Scarp Core, Cape Stardew, PearTree and Remapping.
	private const string RealisticContentJson = """
	{
		"Format": "2.0.0",
		"ConfigSchema":
		{
			// Priorities
			"GoatSkin": {
				"AllowValues": "original, revised",
				"Default": "revised"
			},

	"NpcWarpTownBusStopRoute": {
	 	"AllowValues": "True, False,",
		"Default": "True",
		"Description": "Adds an NpcWarp at the bus stop. Disable if it causes issues.",
	},

			"ObeliskOptions": {
				"AllowValues": "vanilla, glass, garden, Yri, Juffuffles",
				"Default": "vanilla",
			},
			// A free-text setting: no AllowValues at all.
			"TravelingCart_MinimumYear": {
				"Default": "2",
				"Section": "TravelingCart"
			}
		},
		"Changes": []
	}
	""";

	// -------------------------------------------------------------------------
	// Reading the schema
	// -------------------------------------------------------------------------

	[Fact]
	public void ReadSchema_ReadsHandWrittenJsonWithCommentsAndTrailingCommas()
	{
		List<CpConfigOption> options = ContentPatcherConfig.ReadSchema(WriteTemp(RealisticContentJson));

		Assert.Equal(4, options.Count);
		Assert.Equal(new[] { "GoatSkin", "NpcWarpTownBusStopRoute", "ObeliskOptions", "TravelingCart_MinimumYear" },
			options.ConvertAll(o => o.Name));
	}

	[Fact]
	public void ReadSchema_ReadsTheValuesTheAuthorAllows()
	{
		List<CpConfigOption> options = ContentPatcherConfig.ReadSchema(WriteTemp(RealisticContentJson));

		CpConfigOption obelisk = options.Find(o => o.Name == "ObeliskOptions")!;
		Assert.Equal(new[] { "vanilla", "glass", "garden", "Yri", "Juffuffles" }, obelisk.AllowedValues);
		Assert.Equal("vanilla", obelisk.Default);
		Assert.True(obelisk.HasChoices);
	}

	[Fact]
	public void ReadSchema_TreatsATrailingCommaInAllowValuesAsNoExtraChoice()
	{
		// "True, False," is two choices. An empty third would show up as a blank row to arrow onto.
		CpConfigOption option = ContentPatcherConfig
			.ReadSchema(WriteTemp(RealisticContentJson))
			.Find(o => o.Name == "NpcWarpTownBusStopRoute")!;

		Assert.Equal(new[] { "True", "False" }, option.AllowedValues);
		Assert.Equal("Adds an NpcWarp at the bus stop. Disable if it causes issues.", option.Description);
	}

	[Fact]
	public void ReadSchema_MarksASettingWithNoAllowedValuesAsFreeText()
	{
		CpConfigOption option = ContentPatcherConfig
			.ReadSchema(WriteTemp(RealisticContentJson))
			.Find(o => o.Name == "TravelingCart_MinimumYear")!;

		Assert.False(option.HasChoices);
		Assert.Empty(option.AllowedValues);
		Assert.Equal("2", option.Default);
		Assert.Equal("TravelingCart", option.Section);
	}

	[Fact]
	public void ReadSchema_ReturnsNothingForAPackThatDeclaresNoSettings()
	{
		// The common case: most packs have nothing to configure. That is not a failure.
		Assert.Empty(ContentPatcherConfig.ReadSchema(WriteTemp("""{ "Format": "2.0.0", "Changes": [] }""")));
		Assert.Empty(ContentPatcherConfig.ReadSchema(WriteTemp("not json at all")));
		Assert.Empty(ContentPatcherConfig.ReadSchema(Path.Combine(Path.GetTempPath(), "does-not-exist.json")));
	}

	// -------------------------------------------------------------------------
	// Reading and writing the player's answers
	// -------------------------------------------------------------------------

	[Fact]
	public void ReadValues_ReadsBooleansAndNumbersAsTheTextTheUserWouldSee()
	{
		string path = WriteTemp("""
		{ "GoatSkin": "revised", "EnableMeadowFarm": true, "MinimumYear": 2, "Chance": 0.1 }
		""");

		Dictionary<string, string> values = ContentPatcherConfig.ReadValues(path);

		Assert.Equal("revised", values["GoatSkin"]);
		// A JSON boolean reads as JSON spells it. Packs disagree about casing anyway — Cape Stardew allows
		// "True, False" and PearTree allows "true, false" for the same idea — so a value is matched to the
		// author's allowed values case-insensitively rather than by exact text.
		Assert.Equal("true", values["EnableMeadowFarm"]);
		Assert.Equal("2", values["MinimumYear"]);
		Assert.Equal("0.1", values["Chance"]);
	}

	[Fact]
	public void ReadValues_FindsASettingWhateverCaseTheFileSpelledItIn()
	{
		Dictionary<string, string> values = ContentPatcherConfig.ReadValues(
			WriteTemp("""{ "goatskin": "original" }"""));

		Assert.Equal("original", values["GoatSkin"]);
	}

	[Fact]
	public void WriteValues_KeepsSettingsItWasNotAskedToChange()
	{
		string path = WriteTemp("""{ "GoatSkin": "revised", "SomethingElse": "leave me" }""");

		Assert.True(ContentPatcherConfig.WriteValues(path, new Dictionary<string, string> { ["GoatSkin"] = "original" }));

		Dictionary<string, string> after = ContentPatcherConfig.ReadValues(path);
		Assert.Equal("original", after["GoatSkin"]);
		Assert.Equal("leave me", after["SomethingElse"]);
	}

	[Fact]
	public void WriteValues_KeepsTheJsonTypeTheFileAlreadyUsed()
	{
		string path = WriteTemp("""{ "AsBool": true, "AsInt": 2, "AsText": "true" }""");

		ContentPatcherConfig.WriteValues(path, new Dictionary<string, string>
		{
			["AsBool"] = "false",
			["AsInt"] = "5",
			["AsText"] = "false"
		});

		string written = File.ReadAllText(path);
		Assert.Contains("\"AsBool\": false", written);      // still a boolean, not "false"
		Assert.Contains("\"AsInt\": 5", written);           // still a number
		Assert.Contains("\"AsText\": \"false\"", written);  // still a string
	}

	[Fact]
	public void WriteValues_DeletesASettingSoContentPatcherRegeneratesItFromTheSchema()
	{
		// This is "put it back to the author's default". Writing today's default in its place would look the
		// same now and be wrong later, because a deleted setting follows the author if they change it.
		string path = WriteTemp("""{ "GoatSkin": "original", "Keep": "me" }""");

		Assert.True(ContentPatcherConfig.WriteValues(path, new Dictionary<string, string>(), remove: new[] { "GoatSkin" }));

		Dictionary<string, string> after = ContentPatcherConfig.ReadValues(path);
		Assert.False(after.ContainsKey("GoatSkin"));
		Assert.Equal("me", after["Keep"]);
	}

	[Fact]
	public void WriteValues_DeletesASettingWhateverCaseTheFileSpelledItIn()
	{
		// A removal that missed on casing would silently leave the old answer in place.
		string path = WriteTemp("""{ "goatskin": "original" }""");

		ContentPatcherConfig.WriteValues(path, new Dictionary<string, string>(), remove: new[] { "GoatSkin" });

		Assert.Empty(ContentPatcherConfig.ReadValues(path));
	}

	[Fact]
	public void WriteValues_CreatesTheFileWhenThePackHasNeverBeenRun()
	{
		// Content Patcher generates config.json on first load; before that there is nothing to edit.
		string path = Path.Combine(NewFolder(), "config.json");

		Assert.True(ContentPatcherConfig.WriteValues(path, new Dictionary<string, string> { ["GoatSkin"] = "original" }));
		Assert.Equal("original", ContentPatcherConfig.ReadValues(path)["GoatSkin"]);
	}

	[Fact]
	public void FindContentJson_OnlyAnswersForAContentPatcherPack()
	{
		string pack = NewFolder();
		File.WriteAllText(Path.Combine(pack, "content.json"), "{}");
		Assert.NotNull(ContentPatcherConfig.FindContentJson(pack));

		Assert.Null(ContentPatcherConfig.FindContentJson(NewFolder()));
		Assert.Null(ContentPatcherConfig.FindContentJson(""));
	}

	// -------------------------------------------------------------------------

	private static string NewFolder()
	{
		string path = Path.Combine(Path.GetTempPath(), "kmm-cp-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	private static string WriteTemp(string json)
	{
		string path = Path.Combine(NewFolder(), "content.json");
		File.WriteAllText(path, json);
		return path;
	}
}
