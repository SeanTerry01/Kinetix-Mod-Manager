using System;
using System.Collections.Generic;
using System.IO;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="BepInExConfigSchema"/>, which reads what a BepInEx config file says about its own
/// settings — the type, the default, and exactly what each setting will accept — so a mod's settings can be
/// offered as choices rather than typed blind.
///
/// The fixture is the real format, taken from installed Moonlight Peaks mods: <c>##</c> for the author's words,
/// a single <c>#</c> for BepInEx's generated metadata, and a bare <c>Key = Value</c> underneath.
/// </summary>
public class BepInExConfigSchemaTests
{
	private const string RealisticConfig = """
	## Settings file was created by plugin Moonlight Quick Spells v1.0.0
	## Plugin GUID: com.moonlightpeaks.quickspells

	[Accessibility]

	## Speak the menu and its messages through NVDA. Silent if NVDA isn't running.
	# Setting type: Boolean
	# Default value: true
	Speak = true

	[Display]

	## How long each step of the drawn pattern is held, in seconds.
	# Setting type: Single
	# Default value: 0.14
	# Acceptable value range: From 0.02 to 1
	PatternStepSeconds = 0.14

	## Text size of the on-screen menu.
	# Setting type: Int32
	# Default value: 17
	# Acceptable value range: From 10 to 40
	FontSize = 22

	[Keys]

	## Open the spell menu. Works in normal play too.
	# Setting type: KeyCode
	# Default value: K
	# Acceptable values: None, Backspace, Tab, A, B, K
	MenuKey = K

	[Theme]

	## Which colour scheme to use.
	# Setting type: String
	# Default value: Dark
	# Acceptable values: Dark, Light, HighContrast
	Scheme = Light
	""";

	// -------------------------------------------------------------------------
	// Reading
	// -------------------------------------------------------------------------

	[Fact]
	public void Read_ReadsEverySettingWithItsSectionAndValue()
	{
		List<BepInExSetting> settings = BepInExConfigSchema.Read(WriteTemp(RealisticConfig));

		Assert.Equal(5, settings.Count);
		Assert.Equal("Accessibility", settings[0].Section);
		Assert.Equal("Speak", settings[0].Key);
		Assert.Equal("true", settings[0].Value);
		Assert.Equal("22", settings[2].Value);   // the current value, not the default of 17
	}

	[Fact]
	public void Read_IgnoresTheFilesOwnHeaderRatherThanTreatingItAsADescription()
	{
		// "Settings file was created by plugin ..." is not an explanation of the first setting.
		BepInExSetting speak = BepInExConfigSchema.Read(WriteTemp(RealisticConfig))[0];

		Assert.Equal("Speak the menu and its messages through NVDA. Silent if NVDA isn't running.", speak.Description);
	}

	[Fact]
	public void Read_ReadsAnEnumerationAsTheChoicesItOffers()
	{
		BepInExSetting scheme = Find(RealisticConfig, "Theme", "Scheme");

		Assert.Equal(new[] { "Dark", "Light", "HighContrast" }, scheme.AcceptableValues);
		Assert.True(scheme.HasChoices);
		Assert.Equal("Dark", scheme.Default);
	}

	[Fact]
	public void Read_OffersTrueAndFalseForAYesNoSettingEvenThoughTheFileListsNoValues()
	{
		BepInExSetting speak = Find(RealisticConfig, "Accessibility", "Speak");

		Assert.True(speak.IsBoolean);
		Assert.True(speak.HasChoices);
		Assert.Equal(new[] { "true", "false" }, speak.Choices);
	}

	[Fact]
	public void Read_ReadsANumericRange()
	{
		BepInExSetting step = Find(RealisticConfig, "Display", "PatternStepSeconds");

		Assert.True(step.IsNumeric);
		Assert.True(step.HasRange);
		Assert.Equal("0.02", step.RangeFrom);
		Assert.Equal("1", step.RangeTo);
		Assert.False(step.HasChoices);   // typed, not picked from a list
	}

	[Fact]
	public void Read_MarksKeySettingsSoTheControlsListCanFindThem()
	{
		Assert.True(Find(RealisticConfig, "Keys", "MenuKey").IsKey);
		Assert.False(Find(RealisticConfig, "Theme", "Scheme").IsKey);
	}

	[Fact]
	public void Read_DoesNotCarryOneSettingsMetadataOverToTheNext()
	{
		// A setting written with no metadata above it must not inherit the previous setting's type or range.
		List<BepInExSetting> settings = BepInExConfigSchema.Read(WriteTemp("""
		[Section]
		# Setting type: Int32
		# Acceptable value range: From 1 to 10
		First = 5
		Second = anything
		"""));

		Assert.Equal("Int32", settings[0].Type);
		Assert.Equal("", settings[1].Type);
		Assert.False(settings[1].HasRange);
	}

	[Fact]
	public void Read_ReturnsNothingForAMissingOrUnreadableFile()
	{
		Assert.Empty(BepInExConfigSchema.Read(Path.Combine(Path.GetTempPath(), "no-such-file.cfg")));
		Assert.Empty(BepInExConfigSchema.Read(WriteTemp("")));
	}

	[Fact]
	public void Find_MatchesWithoutRegardToCase()
	{
		List<BepInExSetting> settings = BepInExConfigSchema.Read(WriteTemp(RealisticConfig));

		Assert.NotNull(BepInExConfigSchema.Find(settings, "theme", "scheme"));
		Assert.Null(BepInExConfigSchema.Find(settings, "Theme", "NotASetting"));
	}

	// -------------------------------------------------------------------------
	// Checking a value before it is saved
	// -------------------------------------------------------------------------

	[Fact]
	public void IsValid_AcceptsAValueFromTheAuthorsList_WhateverCase()
	{
		BepInExSetting scheme = Find(RealisticConfig, "Theme", "Scheme");

		Assert.True(BepInExConfigSchema.IsValid(scheme, "highcontrast", out _));
	}

	[Fact]
	public void IsValid_RefusesAValueTheAuthorDidNotList()
	{
		BepInExSetting scheme = Find(RealisticConfig, "Theme", "Scheme");

		Assert.False(BepInExConfigSchema.IsValid(scheme, "Purple", out BepInExConfigSchema.Problem problem));
		Assert.Equal(BepInExConfigSchema.Problem.NotAnAllowedValue, problem);
	}

	[Theory]
	[InlineData("0.5", true, BepInExConfigSchema.Problem.None)]
	[InlineData("0.02", true, BepInExConfigSchema.Problem.None)]   // the boundary is allowed
	[InlineData("1", true, BepInExConfigSchema.Problem.None)]
	[InlineData("2", false, BepInExConfigSchema.Problem.OutOfRange)]
	[InlineData("0", false, BepInExConfigSchema.Problem.OutOfRange)]
	[InlineData("soon", false, BepInExConfigSchema.Problem.NotANumber)]
	public void IsValid_ChecksANumberAgainstItsRange(string value, bool expected, BepInExConfigSchema.Problem problem)
	{
		// Worth checking rather than trusting: BepInEx silently reverts a value it can't load, so an unchecked
		// entry would look accepted here and quietly go back to the default next time the game ran.
		BepInExSetting step = Find(RealisticConfig, "Display", "PatternStepSeconds");

		Assert.Equal(expected, BepInExConfigSchema.IsValid(step, value, out BepInExConfigSchema.Problem actual));
		Assert.Equal(problem, actual);
	}

	[Fact]
	public void IsValid_RefusesSomethingThatIsNotTrueOrFalse()
	{
		BepInExSetting speak = Find(RealisticConfig, "Accessibility", "Speak");

		Assert.True(BepInExConfigSchema.IsValid(speak, "false", out _));
		Assert.False(BepInExConfigSchema.IsValid(speak, "yes", out BepInExConfigSchema.Problem problem));
		Assert.Equal(BepInExConfigSchema.Problem.NotABoolean, problem);
	}

	// -------------------------------------------------------------------------

	private static BepInExSetting Find(string config, string section, string key) =>
		BepInExConfigSchema.Find(BepInExConfigSchema.Read(WriteTemp(config)), section, key)!;

	private static string WriteTemp(string contents)
	{
		string folder = Path.Combine(Path.GetTempPath(), "kmm-bepcfg-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		string path = Path.Combine(folder, "plugin.cfg");
		File.WriteAllText(path, contents);
		return path;
	}
}
