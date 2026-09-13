using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Reading Minecraft's controls off disk.
///
/// The fixtures here are copied verbatim from a real install - the options.txt of a played game and the
/// keybinds file United Minecraft wrote for itself. Invented ones would not have shown that the vanilla
/// file names an action by translation key while the mod names it by GLFW code, which is the whole reason
/// there are two readers.
/// </summary>
public class MinecraftControlsTests
{
	// -------------------------------------------------------------------------
	// options.txt
	// -------------------------------------------------------------------------

	private const string RealOptionsTxt = """
	autoJump:false
	key_key.attack:key.mouse.left
	key_key.use:key.mouse.right
	key_key.forward:key.keyboard.w
	key_key.sneak:key.keyboard.left.shift
	key_key.jump:key.keyboard.space
	key_key.pickItem:key.mouse.middle
	key_key.command:key.keyboard.slash
	key_key.socialInteractions:key.keyboard.p
	key_key.toggleGui:key.keyboard.f1
	key_key.hotbar.1:key.keyboard.1
	key_key.saveToolbarActivator:key.keyboard.unknown
	soundCategory_master:1.0
	""";

	[Fact]
	public void ReadsOnlyTheKeyLinesAndNotTheRestOfTheOptions()
	{
		// options.txt is mostly settings - autoJump, sound volumes, render distance. Only key_ lines are keys.
		List<MinecraftBinding> bindings = ReadOptions(RealOptionsTxt);

		Assert.Equal(11, bindings.Count);
		Assert.DoesNotContain(bindings, b => b.Action.Contains("auto", StringComparison.OrdinalIgnoreCase)
											 && b.Action.Contains("jump", StringComparison.OrdinalIgnoreCase));
		Assert.DoesNotContain(bindings, b => b.Action.Contains("sound", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void ActionsAndKeysAreBothReadableAloud()
	{
		List<MinecraftBinding> bindings = ReadOptions(RealOptionsTxt);

		Assert.Equal("Left mouse button", Find(bindings, "Attack").Key);
		Assert.Equal("Right mouse button", Find(bindings, "Use").Key);
		Assert.Equal("W", Find(bindings, "Forward").Key);
		// "Left shift" rather than "Left Shift": a screen reader says the two identically, and sentence case
		// keeps key names consistent with the action names beside them.
		Assert.Equal("Left shift", Find(bindings, "Sneak").Key);
		Assert.Equal("Space", Find(bindings, "Jump").Key);
		Assert.Equal("Middle mouse button", Find(bindings, "Pick item").Key);
		Assert.Equal("F1", Find(bindings, "Toggle gui").Key);
		Assert.Equal("1", Find(bindings, "Hotbar 1").Key);
	}

	[Fact]
	public void AnUnboundActionSaysSoRatherThanReadingOutAnIdentifier()
	{
		// "key.keyboard.unknown" spoken aloud is meaningless; several actions ship unbound.
		MinecraftBinding binding = Find(ReadOptions(RealOptionsTxt), "Save toolbar activator");

		Assert.True(binding.IsUnbound);
		Assert.Equal("Not bound", binding.Key);
	}

	[Theory]
	[InlineData("key.attack", "Attack")]
	[InlineData("key.pickItem", "Pick item")]
	[InlineData("key.socialInteractions", "Social interactions")]
	[InlineData("key.hotbar.1", "Hotbar 1")]
	[InlineData("narrate_light_level", "Narrate light level")]
	[InlineData("toggle_auto_crosshair_narration", "Toggle auto crosshair narration")]
	public void AnActionKeyBecomesAPhraseRatherThanAnIdentifier(string key, string expected)
	{
		Assert.Equal(expected, MinecraftControls.FriendlyActionName(key));
	}

	[Fact]
	public void AMissingOptionsFileIsEmptyRatherThanAnError()
	{
		Assert.Empty(MinecraftControls.ReadVanillaBindings(Path.Combine(Path.GetTempPath(), "no-such-options.txt")));
	}

	// -------------------------------------------------------------------------
	// United Minecraft's keybinds
	// -------------------------------------------------------------------------

	// Verbatim from a real config\united_minecraft_keybinds.json.
	private const string RealUnitedKeybinds = """
	{
		"narrate_coordinates":   { "key": 67, "modifiers": 0 },
		"narrate_light_level":   { "key": 67, "modifiers": 1 },
		"narrate_health":        { "key": 72, "modifiers": 0 },
		"narrate_armor_and_effects": { "key": 72, "modifiers": 4 },
		"narrate_coordinate_x":  { "key": -1, "modifiers": 0 },
		"scan_surroundings":     { "key": 82, "modifiers": 0 }
	}
	""";

	[Fact]
	public void GlfwKeyCodesAndModifiersBecomeSpokenCombinations()
	{
		List<MinecraftBinding> bindings = ReadUnited(RealUnitedKeybinds);

		// GLFW gives letters their ASCII codes, and its modifier bits are SHIFT 1, CONTROL 2, ALT 4.
		Assert.Equal("C", Find(bindings, "Narrate coordinates").Key);
		Assert.Equal("Shift plus C", Find(bindings, "Narrate light level").Key);
		Assert.Equal("H", Find(bindings, "Narrate health").Key);
		Assert.Equal("Alt plus H", Find(bindings, "Narrate armor and effects").Key);
		Assert.Equal("R", Find(bindings, "Scan surroundings").Key);
	}

	[Fact]
	public void AKeyOfMinusOneIsUnbound()
	{
		// Several of the mod's actions ship with no key. Worth listing - "this exists but you would have to
		// bind it" is useful - but it must not read as a key code.
		MinecraftBinding binding = Find(ReadUnited(RealUnitedKeybinds), "Narrate coordinate x");

		Assert.True(binding.IsUnbound);
		Assert.Equal("Not bound", binding.Key);
	}

	[Theory]
	[InlineData(67, 0, "C")]
	[InlineData(67, 1, "Shift plus C")]
	[InlineData(67, 2, "Control plus C")]
	[InlineData(67, 4, "Alt plus C")]
	// Several modifiers at once, in a stable order.
	[InlineData(67, 3, "Shift plus Control plus C")]
	[InlineData(32, 0, "Space")]
	[InlineData(290, 0, "F1")]
	[InlineData(301, 0, "F12")]
	[InlineData(-1, 0, "Not bound")]
	public void CombinationsReadInAStableOrder(int key, int modifiers, string expected)
	{
		// "plus" rather than "+", so a screen reader says the word instead of reading punctuation mid-phrase.
		Assert.Equal(expected, MinecraftControls.DescribeGlfwCombo(key, modifiers));
	}

	[Fact]
	public void AnUnreadableKeybindsFileIsEmptyRatherThanAnError()
	{
		string dir = NewTempDir();
		try
		{
			string path = Path.Combine(dir, "keybinds.json");
			File.WriteAllText(path, "{ not json");

			Assert.Empty(MinecraftControls.ReadUnitedMinecraftBindings(path));
		}
		finally { Directory.Delete(dir, recursive: true); }
	}

	// -------------------------------------------------------------------------
	// Grouping - why the same key appears several times
	// -------------------------------------------------------------------------

	[Fact]
	public void TheSameKeyInDifferentModesLandsInDifferentSections()
	{
		// The reason sections exist. United Minecraft binds Left arrow to three different things because each
		// applies on a different screen; flat, that reads as three contradictory entries for one key and looks
		// like the manager is confused.
		var bindings = ReadUnited("""
		{
			"look_left":           { "key": 263, "modifiers": 0 },
			"container_nav_left":  { "key": 263, "modifiers": 0 },
			"build_cursor_left":   { "key": 263, "modifiers": 0 },
			"snap_turn_left":      { "key": 263, "modifiers": 4 }
		}
		""");

		var sections = MinecraftControls.GroupUnitedMinecraft(bindings);

		Assert.Equal("Looking around", SectionOf(sections, "Look left"));
		Assert.Equal("Containers and inventory", SectionOf(sections, "Container nav left"));
		Assert.Equal("Build mode", SectionOf(sections, "Build cursor left"));
		// Snap turning is a way of looking around, so it belongs with it rather than on its own.
		Assert.Equal("Looking around", SectionOf(sections, "Snap turn left"));
	}

	[Fact]
	public void ALongerPrefixWinsOverAShorterOneThatAlsoMatches()
	{
		// "scanner_" and "scan_" both match "scanner_target"; the rules are ordered so the specific one wins.
		var bindings = ReadUnited("""
		{ "scanner_target": { "key": 257, "modifiers": 0 }, "scan_surroundings": { "key": 82, "modifiers": 0 } }
		""");

		var sections = MinecraftControls.GroupUnitedMinecraft(bindings);

		Assert.Equal("Scanner", SectionOf(sections, "Scanner target"));
		Assert.Equal("Scanner", SectionOf(sections, "Scan surroundings"));
		Assert.Single(sections);
	}

	[Fact]
	public void AnActionTheGroupingHasNeverHeardOfStillAppears()
	{
		// A mod update adding a new feature must not make its keys vanish from the list.
		var bindings = ReadUnited("""{ "teleport_to_the_moon": { "key": 77, "modifiers": 0 } }""");

		Assert.Equal("Other", SectionOf(MinecraftControls.GroupUnitedMinecraft(bindings), "Teleport to the moon"));
	}

	[Fact]
	public void TheGamesOwnKeysFollowMinecraftsOwnCategories()
	{
		var sections = MinecraftControls.GroupVanilla(ReadOptions(RealOptionsTxt));

		Assert.Equal("Movement", SectionOf(sections, "Forward"));
		Assert.Equal("Gameplay", SectionOf(sections, "Attack"));
		Assert.Equal("Inventory", SectionOf(sections, "Hotbar 1"));
		Assert.Equal("Multiplayer", SectionOf(sections, "Social interactions"));
		// Screenshot, fullscreen and perspective are Miscellaneous in Minecraft's own Controls screen too.
		Assert.Equal("Miscellaneous", SectionOf(sections, "Toggle gui"));
	}

	[Fact]
	public void TheTwentyTwoDebugKeysAreTheirOwnSectionAndComeLast()
	{
		// A third of Minecraft's keys are chunk borders, hitboxes and profiling charts. Left ungrouped they
		// drown the keys somebody actually plays with.
		var bindings = ReadOptions("""
		key_key.forward:key.keyboard.w
		key_key.debug.showHitboxes:key.keyboard.b
		key_key.debug.chunkBorders:key.keyboard.g
		""");

		var sections = MinecraftControls.GroupVanilla(bindings);

		Assert.Equal("Debug", SectionOf(sections, "Debug show hitboxes"));
		Assert.Equal("Movement", sections[0].Name);
		Assert.Equal("Debug", sections[^1].Name);
	}

	[Fact]
	public void EmptySectionsAreNotOffered()
	{
		// The rules declare a dozen sections; a player with only movement keys should get one, not twelve
		// headings they can arrow through to find nothing inside.
		var sections = MinecraftControls.GroupVanilla(ReadOptions("key_key.forward:key.keyboard.w"));

		Assert.Single(sections);
		Assert.Equal("Movement", sections[0].Name);
	}

	[Fact]
	public void WithinASectionBoundKeysComeBeforeUnboundOnes()
	{
		var bindings = ReadUnited("""
		{
			"narrate_coordinate_x": { "key": -1, "modifiers": 0 },
			"narrate_health":       { "key": 72, "modifiers": 0 }
		}
		""");

		var narration = MinecraftControls.GroupUnitedMinecraft(bindings).Single();

		Assert.Equal("Narrate health", narration.Bindings[0].Action);
		Assert.True(narration.Bindings[^1].IsUnbound);
	}

	// -------------------------------------------------------------------------
	// The modifier keys, which a mod may bind as ordinary keys
	// -------------------------------------------------------------------------

	[Theory]
	// United Minecraft binds these to place and break a block. Without them the list read "Key 345: Build
	// place" - a number nobody can act on.
	[InlineData(344, "Right shift")]
	[InlineData(345, "Right control")]
	[InlineData(340, "Left shift")]
	[InlineData(341, "Left control")]
	[InlineData(346, "Right alt")]
	[InlineData(348, "Menu")]
	[InlineData(280, "Caps lock")]
	[InlineData(335, "Numpad enter")]
	public void ModifierAndSpecialKeysAreNamedRatherThanNumbered(int key, string expected)
	{
		Assert.Equal(expected, MinecraftControls.DescribeGlfwKey(key));
	}

	// -------------------------------------------------------------------------

	private static string SectionOf(
		IEnumerable<MinecraftControls.MinecraftControlSection> sections, string action) =>
		sections.FirstOrDefault(s => s.Bindings.Any(b => b.Action == action))?.Name
		?? throw new Xunit.Sdk.XunitException(
			$"'{action}' is in no section. Sections: "
			+ string.Join(", ", sections.Select(s => $"{s.Name} [{string.Join(" | ", s.Bindings.Select(b => b.Action))}]")));

	private static MinecraftBinding Find(IEnumerable<MinecraftBinding> bindings, string action) =>
		bindings.FirstOrDefault(b => b.Action == action)
		?? throw new Xunit.Sdk.XunitException(
			$"No binding called '{action}'. Present: {string.Join(", ", bindings.Select(b => b.Action))}");

	private static List<MinecraftBinding> ReadOptions(string contents) =>
		WithTempFile(contents, MinecraftControls.ReadVanillaBindings);

	private static List<MinecraftBinding> ReadUnited(string contents) =>
		WithTempFile(contents, MinecraftControls.ReadUnitedMinecraftBindings);

	private static List<MinecraftBinding> WithTempFile(string contents, Func<string, List<MinecraftBinding>> read)
	{
		string dir = NewTempDir();
		try
		{
			string path = Path.Combine(dir, "fixture.txt");
			File.WriteAllText(path, contents);
			return read(path);
		}
		finally { Directory.Delete(dir, recursive: true); }
	}

	private static string NewTempDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "kmm-mcctl-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}
}
