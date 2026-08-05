using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="StardewModConfig"/>, which recovers what it can about an ordinary Stardew mod's settings:
/// the type of each value from the file itself, and the author's labels from the translation file many mods
/// ship for their in-game settings menu.
///
/// The naming mismatch in <see cref="Read_UsesTheAuthorsLabelsWhereTheModShipsThem"/> is the real one — Automate
/// stores <c>AutomationInterval</c> and labels it <c>config.automation-interval.name</c>.
/// </summary>
public class StardewModConfigTests
{
	private const string Config = """
	{
	  // a comment, which Stardew's own loader allows
	  "Enabled": true,
	  "AutomationInterval": 60,
	  "MinMinutesForFairyDust": 0.5,
	  "ConnectorNames": "workbench",
	  "Controls": {
	    "ToggleOverlayKey": "U"
	  },
	  "ChestOverrides": [ "one", "two" ]
	}
	""";

	private const string Labels = """
	{
	  "config.title.main-options": "Main options",
	  "config.enabled.name": "Enabled",
	  "config.enabled.desc": "Whether to enable Automate features.",
	  "config.automation-interval.name": "Automation interval",
	  "config.automation-interval.desc": "How often machines are automated, in game ticks.",
	  "some.other.text": "not a setting"
	}
	""";

	// -------------------------------------------------------------------------
	// Reading
	// -------------------------------------------------------------------------

	[Fact]
	public void Read_InfersEachSettingsTypeFromTheValueAlreadyInTheFile()
	{
		List<StardewSetting> settings = StardewModConfig.Read(WriteMod(Config, Labels));

		Assert.Equal(StardewValueKind.Boolean, Find(settings, "Enabled").Kind);
		Assert.Equal(StardewValueKind.Number, Find(settings, "AutomationInterval").Kind);
		Assert.Equal(StardewValueKind.Number, Find(settings, "MinMinutesForFairyDust").Kind);
		Assert.Equal(StardewValueKind.Text, Find(settings, "ConnectorNames").Kind);
	}

	[Fact]
	public void Read_UsesTheAuthorsLabelsWhereTheModShipsThem()
	{
		// The config spells it AutomationInterval; the translation spells it automation-interval.
		StardewSetting interval = Find(StardewModConfig.Read(WriteMod(Config, Labels)), "AutomationInterval");

		Assert.Equal("Automation interval", interval.Label);
		Assert.Equal("How often machines are automated, in game ticks.", interval.Description);
	}

	[Fact]
	public void Read_FallsBackToTheSettingsOwnNameSpacedOut()
	{
		// No translation file at all: "ConnectorNames" still has to read as something.
		StardewSetting setting = Find(StardewModConfig.Read(WriteMod(Config, labels: null)), "ConnectorNames");

		Assert.Equal("Connector Names", setting.Label);
		Assert.Equal("", setting.Description);
	}

	[Fact]
	public void Read_FollowsSettingsGroupedInsideAnObject()
	{
		StardewSetting key = Find(StardewModConfig.Read(WriteMod(Config, Labels)), "ToggleOverlayKey");

		Assert.Equal("Controls.ToggleOverlayKey", key.Path);
		Assert.Equal("U", key.Value);
	}

	[Fact]
	public void Read_SkipsAListRatherThanOfferingItAsOneTypedValue()
	{
		// Replacing a whole list through a single text box is a good way to break a mod.
		Assert.DoesNotContain(StardewModConfig.Read(WriteMod(Config, Labels)), s => s.Name == "ChestOverrides");
	}

	[Fact]
	public void Read_ReturnsNothingForAModWithNoConfigOrAnUnreadableOne()
	{
		Assert.Empty(StardewModConfig.Read(NewFolder()));
		Assert.Empty(StardewModConfig.Read(WriteMod("{ not json", labels: null)));
	}

	// -------------------------------------------------------------------------
	// Writing
	// -------------------------------------------------------------------------

	[Fact]
	public void Write_KeepsTheJsonTypeTheSettingAlreadyUsed()
	{
		string folder = WriteMod(Config, Labels);
		List<StardewSetting> settings = StardewModConfig.Read(folder);

		StardewModConfig.Write(folder, Find(settings, "Enabled"), "false");
		StardewModConfig.Write(folder, Find(settings, "AutomationInterval"), "120");

		string written = File.ReadAllText(Path.Combine(folder, "config.json"));
		Assert.Contains("\"Enabled\": false", written);            // still a boolean
		Assert.Contains("\"AutomationInterval\": 120", written);   // still a number
	}

	[Fact]
	public void Write_ReachesASettingInsideAGroup()
	{
		string folder = WriteMod(Config, Labels);
		StardewSetting key = Find(StardewModConfig.Read(folder), "ToggleOverlayKey");

		Assert.True(StardewModConfig.Write(folder, key, "K"));
		Assert.Equal("K", Find(StardewModConfig.Read(folder), "ToggleOverlayKey").Value);
	}

	[Fact]
	public void Write_LeavesEverySettingItWasNotAskedToChange()
	{
		string folder = WriteMod(Config, Labels);
		StardewModConfig.Write(folder, Find(StardewModConfig.Read(folder), "Enabled"), "false");

		List<StardewSetting> after = StardewModConfig.Read(folder);
		Assert.Equal("60", Find(after, "AutomationInterval").Value);
		Assert.Equal("workbench", Find(after, "ConnectorNames").Value);
		Assert.Equal("U", Find(after, "ToggleOverlayKey").Value);
	}

	[Theory]
	[InlineData("AutomationInterval", "120", true)]
	[InlineData("AutomationInterval", "sixty", false)]   // would turn a number into text the mod can't read
	[InlineData("Enabled", "false", true)]
	[InlineData("Enabled", "yes", false)]
	[InlineData("ConnectorNames", "anything at all", true)]
	public void IsValid_RefusesAValueThatWouldChangeASettingsType(string name, string value, bool expected)
	{
		List<StardewSetting> settings = StardewModConfig.Read(WriteMod(Config, Labels));

		Assert.Equal(expected, StardewModConfig.IsValid(Find(settings, name), value));
	}

	// -------------------------------------------------------------------------
	// Key bindings — the one free-text setting a list can be offered for
	// -------------------------------------------------------------------------

	[Theory]
	[InlineData("ToggleOverlayKey", true)]
	[InlineData("toggle_hotkey", true)]
	[InlineData("MenuButton", true)]
	[InlineData("KeyBinding", true)]
	[InlineData("ConnectorNames", false)]
	[InlineData("Monkey", false)]        // ends in "key" only by accident of spelling
	public void LooksLikeAKeyBinding_JudgesBySettingName(string name, bool expected)
	{
		// The name is the only signal there is: nothing on disk says what an ordinary mod's setting means.
		var setting = new StardewSetting { Name = name, Kind = StardewValueKind.Text };

		Assert.Equal(expected, StardewModConfig.LooksLikeAKeyBinding(setting));
	}

	[Fact]
	public void LooksLikeAKeyBinding_IsOnlyEverTrueForText()
	{
		// A number called "MonkeyCount" is still a number.
		var number = new StardewSetting { Name = "HotkeyDelay", Kind = StardewValueKind.Number };

		Assert.False(StardewModConfig.LooksLikeAKeyBinding(number));
	}

	[Fact]
	public void KeyNames_CoverTheKeysModsActuallyBind()
	{
		IReadOnlyList<string> keys = StardewModConfig.KeyNames;

		Assert.Contains("A", keys);
		Assert.Contains("D1", keys);          // SMAPI's name for the top-row digits
		Assert.Contains("F5", keys);
		Assert.Contains("PageDown", keys);
		Assert.Contains("LeftControl", keys);
		Assert.Contains("MouseLeft", keys);
		Assert.Contains("None", keys);
		Assert.Equal(keys.Count, keys.Distinct().Count());
	}

	// -------------------------------------------------------------------------

	private static StardewSetting Find(IEnumerable<StardewSetting> settings, string name) =>
		settings.First(s => s.Name == name);

	private static string NewFolder()
	{
		string path = Path.Combine(Path.GetTempPath(), "kmm-sdv-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	private static string WriteMod(string config, string? labels)
	{
		string folder = NewFolder();
		File.WriteAllText(Path.Combine(folder, "config.json"), config);
		if (labels != null)
		{
			Directory.CreateDirectory(Path.Combine(folder, "i18n"));
			File.WriteAllText(Path.Combine(folder, "i18n", "default.json"), labels);
		}
		return folder;
	}
}
