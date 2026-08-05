using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="McmConfigSchema"/>, which reads a Skyrim / Fallout 4 mod's Mod Configuration Menu so its
/// settings can be changed from the manager instead of from inside the game.
///
/// The fixture follows the shape of a real menu — the switcher and text controls are copied from Extended
/// Dialogue Interface, the page/section nesting from Fallout 4 Access.
/// </summary>
public class McmConfigSchemaTests
{
	private const string RealisticMenu = """
	{
	  "modName": "XDI",
	  "pages": [
	    {
	      "pageDisplayName": "Settings",
	      "content": [
	        { "text": "Dialogue Menu Options", "type": "section" },
	        {
	          "id": "bWrapText:DialogueMenu",
	          "text": "Wrap text",
	          "type": "switcher",
	          "help": "Controls whether text will wrap to the width of the dialogue menu.",
	          "valueOptions": { "sourceType": "ModSettingBool" }
	        },
	        {
	          "id": "fMenuScale:DialogueMenu",
	          "text": "Menu scale",
	          "type": "slider",
	          "help": "How large the menu is drawn.",
	          "valueOptions": { "sourceType": "ModSettingFloat", "min": 0.5, "max": 2.0, "step": 0.1 }
	        },
	        {
	          "id": "iTheme:DialogueMenu",
	          "text": "Theme",
	          "type": "enum",
	          "valueOptions": { "sourceType": "ModSettingInt", "options": ["Vanilla", "Dark", "High Contrast"] }
	        },
	        {
	          "id": "sGreeting:DialogueMenu",
	          "text": "Greeting",
	          "type": "textinput",
	          "valueOptions": { "sourceType": "ModSettingString" }
	        },
	        {
	          "id": "iHotkey:DialogueMenu",
	          "text": "Open menu",
	          "type": "keyinput",
	          "valueOptions": { "sourceType": "ModSettingInt" }
	        },
	        { "type": "spacer" },
	        { "text": "Some explanatory prose.", "type": "text" }
	      ]
	    }
	  ]
	}
	""";

	// -------------------------------------------------------------------------
	// Reading the menu
	// -------------------------------------------------------------------------

	[Fact]
	public void ReadMenu_ReadsEachControlAsTheKindItHasToBePresentedAs()
	{
		McmMenu menu = Load(RealisticMenu);

		Assert.Equal(McmControlKind.Toggle, Control(menu, "Wrap text").Kind);
		Assert.Equal(McmControlKind.Number, Control(menu, "Menu scale").Kind);
		Assert.Equal(McmControlKind.Choice, Control(menu, "Theme").Kind);
		Assert.Equal(McmControlKind.Text, Control(menu, "Greeting").Kind);
		Assert.Equal(McmControlKind.Key, Control(menu, "Open menu").Kind);
	}

	[Fact]
	public void ReadMenu_SplitsTheIdIntoTheIniSettingItReadsAndWrites()
	{
		McmControl wrap = Control(Load(RealisticMenu), "Wrap text");

		Assert.Equal("bWrapText", wrap.SettingName);
		Assert.Equal("DialogueMenu", wrap.IniSection);
		Assert.Equal("DialogueMenu|bWrapText", wrap.ValueKey);
	}

	[Fact]
	public void ReadMenu_KeepsTheAuthorsLabelHelpAndGrouping()
	{
		McmControl wrap = Control(Load(RealisticMenu), "Wrap text");

		Assert.Equal("Controls whether text will wrap to the width of the dialogue menu.", wrap.Help);
		Assert.Equal("Dialogue Menu Options", wrap.Group);
	}

	[Fact]
	public void ReadMenu_ReadsARangeAndAListOfOptions()
	{
		McmMenu menu = Load(RealisticMenu);

		Assert.Equal(0.5, Control(menu, "Menu scale").Min);
		Assert.Equal(2.0, Control(menu, "Menu scale").Max);
		Assert.Equal(new[] { "Vanilla", "Dark", "High Contrast" }, Control(menu, "Theme").Options);
	}

	[Fact]
	public void ReadMenu_CountsOnlyTheControlsThatHoldAValueAsSettings()
	{
		// Headings, spacers and prose are shown or skipped, but never written to.
		McmMenu menu = Load(RealisticMenu);

		Assert.Equal(5, menu.Settings.Count());
		Assert.DoesNotContain(menu.Settings, c => c.Kind == McmControlKind.NotASetting);
	}

	[Fact]
	public void ReadMenu_TreatsAControlTypeItDoesNotKnowAsNotASetting()
	{
		// Writing a value for a control we don't understand is how a mod ends up with a setting it can't read.
		McmMenu menu = Load("""
		{ "modName": "M", "content": [ { "id": "x:S", "text": "Mystery", "type": "colorpicker" } ] }
		""");

		Assert.Empty(menu.Settings);
	}

	[Fact]
	public void ReadMenu_PointsAtTheModsDefaultsAndThePlayersOwnSettingsFile()
	{
		string configDir = NewFolder();
		File.WriteAllText(Path.Combine(configDir, "config.json"), RealisticMenu);

		McmMenu menu = McmConfigSchema.ReadMenu(Path.Combine(configDir, "config.json"), configDir, @"C:\Game")!;

		Assert.Equal(Path.Combine(configDir, "settings.ini"), menu.DefaultsIniPath);
		Assert.Equal(@"C:\Game\Data\MCM\Settings\XDI.ini", menu.UserIniPath);
	}

	// -------------------------------------------------------------------------
	// Values: defaults, overrides, and writing
	// -------------------------------------------------------------------------

	[Fact]
	public void Load_LaysThePlayersSettingsOverTheModsDefaults()
	{
		string configDir = NewFolder();
		string game = NewFolder();
		File.WriteAllText(Path.Combine(configDir, "config.json"), RealisticMenu);
		File.WriteAllText(Path.Combine(configDir, "settings.ini"), "[DialogueMenu]\nbWrapText=1\nfMenuScale=1.0\n");

		Directory.CreateDirectory(Path.Combine(game, "Data", "MCM", "Settings"));
		File.WriteAllText(Path.Combine(game, "Data", "MCM", "Settings", "XDI.ini"), "[DialogueMenu]\nbWrapText=0\n");

		McmMenu menu = McmConfigSchema.ReadMenu(Path.Combine(configDir, "config.json"), configDir, game)!;
		McmSettings values = McmSettings.Load(menu);

		Assert.Equal("0", values.Get(Control(menu, "Wrap text")));   // the player's override wins
		Assert.Equal("1.0", values.Get(Control(menu, "Menu scale"))); // untouched, so the mod's default stands
	}

	[Fact]
	public void Save_WritesToThePlayersFileAndNeverToTheModsDefaults()
	{
		string configDir = NewFolder();
		string game = NewFolder();
		File.WriteAllText(Path.Combine(configDir, "config.json"), RealisticMenu);
		string defaults = Path.Combine(configDir, "settings.ini");
		File.WriteAllText(defaults, "[DialogueMenu]\nbWrapText=1\n");

		McmMenu menu = McmConfigSchema.ReadMenu(Path.Combine(configDir, "config.json"), configDir, game)!;

		Assert.True(McmSettings.Save(menu, Control(menu, "Wrap text"), "0"));

		// The player's file now exists and holds the change; the mod's own file is exactly as it was.
		Assert.Contains("bWrapText=0", File.ReadAllText(menu.UserIniPath).Replace(" ", ""));
		Assert.Equal("[DialogueMenu]\nbWrapText=1\n", File.ReadAllText(defaults));
	}

	[Fact]
	public void Save_CreatesTheSettingsFileAndItsFolderTheFirstTime()
	{
		// Normal for a mod whose in-game menu has never been opened: nothing under Data\MCM\Settings yet.
		string configDir = NewFolder();
		string game = NewFolder();
		File.WriteAllText(Path.Combine(configDir, "config.json"), RealisticMenu);

		McmMenu menu = McmConfigSchema.ReadMenu(Path.Combine(configDir, "config.json"), configDir, game)!;
		Assert.False(File.Exists(menu.UserIniPath));

		Assert.True(McmSettings.Save(menu, Control(menu, "Greeting"), "hello"));
		Assert.True(File.Exists(menu.UserIniPath));
	}

	[Fact]
	public void Save_LeavesOtherSettingsInThePlayersFileAlone()
	{
		string configDir = NewFolder();
		string game = NewFolder();
		File.WriteAllText(Path.Combine(configDir, "config.json"), RealisticMenu);
		Directory.CreateDirectory(Path.Combine(game, "Data", "MCM", "Settings"));
		File.WriteAllText(Path.Combine(game, "Data", "MCM", "Settings", "XDI.ini"),
			"; a comment of mine\n[DialogueMenu]\nbWrapText=1\nsGreeting=keep me\n");

		McmMenu menu = McmConfigSchema.ReadMenu(Path.Combine(configDir, "config.json"), configDir, game)!;
		McmSettings.Save(menu, Control(menu, "Wrap text"), "0");

		string written = File.ReadAllText(menu.UserIniPath);
		Assert.Contains("; a comment of mine", written);
		Assert.Contains("sGreeting=keep me", written);
		Assert.Contains("bWrapText=0", written);
	}

	[Fact]
	public void ReadMenus_FindsNothingForAModThatDoesNotUseMcm()
	{
		Assert.Empty(McmConfigSchema.ReadMenus(NewFolder(), @"C:\Game"));
	}

	// -------------------------------------------------------------------------

	private static McmMenu Load(string json)
	{
		string configDir = NewFolder();
		string path = Path.Combine(configDir, "config.json");
		File.WriteAllText(path, json);
		return McmConfigSchema.ReadMenu(path, configDir, @"C:\Game")!;
	}

	private static McmControl Control(McmMenu menu, string label) =>
		menu.Controls.First(c => c.Label == label);

	private static string NewFolder()
	{
		string path = Path.Combine(Path.GetTempPath(), "kmm-mcm-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}
}
