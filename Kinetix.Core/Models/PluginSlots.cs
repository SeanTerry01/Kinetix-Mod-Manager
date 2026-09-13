using System;

namespace KinetixModManager;

/// <summary>
/// The ceilings Skyrim SE and Fallout 4 put on how many plugins can load at once.
///
/// The engine addresses a regular plugin with a one-byte index, so it can load 255 of them (0x00–0xFE;
/// 0xFF is reserved) — and the implicit base-game and DLC masters count toward that, not just the player's
/// mods. Light (ESL-flagged) plugins load into a separate container at index 0xFE with room for 4,096, so
/// they are their own pool entirely. Going past the regular ceiling is a classic hard failure: the game
/// silently drops plugins or refuses to start.
///
/// These are facts about the games rather than about this program, which is why they sit in the core beside
/// <see cref="PluginSlotUsage"/> rather than as constants inside the window that happens to display them.
/// </summary>
public static class PluginSlots
{
	/// <summary>Usable regular indices 0x00–0xFE; 0xFF is reserved.</summary>
	public const int RegularPluginCap = 255;

	/// <summary>Where to start warning — within the last handful of regular slots.</summary>
	public const int RegularPluginNear = 250;

	/// <summary>The 0xFE light container: indices 0x000–0xFFF.</summary>
	public const int LightPluginCap = 4096;

	/// <summary>Where to start warning on the light pool.</summary>
	public const int LightPluginNear = 4000;
}
