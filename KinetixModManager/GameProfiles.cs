using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// How a game keeps its mods on disk. This decides how the manager scans, installs, enables and disables
/// them, and it is the one thing that genuinely differs from game to game.
/// </summary>
public enum ModLayout
{
	/// <summary>Stardew Valley: every mod is a folder under the game's <c>Mods</c> folder carrying a
	/// <c>manifest.json</c> that SMAPI reads. Mods live where they are installed; a leading dot disables one.</summary>
	StardewManifest,

	/// <summary>Skyrim SE / Fallout 4: mods are staged in a manager-owned folder outside the game and their
	/// files are deployed into the game's <c>Data</c> folder in priority order.</summary>
	BethesdaStaged,

	/// <summary>Moonlight Peaks: each mod is a folder of DLLs under <c>BepInEx\plugins</c>, loaded by the
	/// BepInEx chainloader. Mods live where they are installed, like Stardew, but a leading dot does NOT
	/// disable one — the chainloader scans for DLLs recursively and ignores folder names entirely.</summary>
	BepInExPlugins
}

/// <summary>
/// Everything the manager needs to know about one supported game that isn't behaviour: where it installs,
/// what it is called, what runs it, where its mods live, and which Nexus game it is.
///
/// This exists because the per-game data used to live in a dozen separate <c>game switch { ... }</c>
/// expressions whose <c>_ =&gt;</c> default silently meant "Stardew Valley". That was fine while Stardew was
/// the only fallback, but a fourth game would have quietly inherited Stardew's Steam id, executable and Nexus
/// domain in every one of them. Looking the data up in one place makes an unknown game an obvious failure
/// rather than a wrong answer.
/// </summary>
public sealed class GameProfile
{
	/// <summary>The stable internal id used as the key in settings and in <c>AppSettings.ActiveGame</c>.</summary>
	public required string Id { get; init; }

	/// <summary>The game's name as the user sees it, e.g. "Moonlight Peaks".</summary>
	public required string DisplayName { get; init; }

	/// <summary>The game's Steam application id, used for registry and libraryfolders.vdf detection.</summary>
	public required string SteamAppId { get; init; }

	/// <summary>The game's GOG product id, or <c>null</c> when it isn't sold on GOG.</summary>
	public string? GogProductId { get; init; }

	/// <summary>The Steam store page, shown when the user doesn't own the game yet.</summary>
	public required string SteamStoreUrl { get; init; }

	/// <summary>The GOG store page, or <c>null</c> when it isn't sold on GOG.</summary>
	public string? GogStoreUrl { get; init; }

	/// <summary>Where the game usually installs, tried last when nothing else finds it.</summary>
	public required string DefaultInstallFolder { get; init; }

	/// <summary>The game's own executable, e.g. "Moonlight Peaks.exe".</summary>
	public required string GameExeName { get; init; }

	/// <summary>
	/// The mod loader's launcher executable, or <c>""</c> when the loader has none. Stardew and the Bethesda
	/// games are started through their loader (SMAPI, SKSE, F4SE); BepInEx instead hooks the game through a
	/// <c>winhttp.dll</c> shim, so the game's own exe loads mods and there is nothing separate to run.
	/// </summary>
	public required string LoaderExeName { get; init; }

	/// <summary>The loader's name as the user knows it ("SMAPI", "SKSE", "BepInEx"), or <c>""</c> if none.</summary>
	public required string LoaderDisplayName { get; init; }

	/// <summary>How this game's mods are laid out on disk.</summary>
	public required ModLayout Layout { get; init; }

	/// <summary>
	/// Where mods live relative to the game folder (e.g. <c>Mods</c>, <c>BepInEx\plugins</c>), or <c>null</c>
	/// for games whose mods are staged outside the game entirely (see <see cref="StagingFolderName"/>).
	/// </summary>
	public string? ModsFolderRelativeToGame { get; init; }

	/// <summary>
	/// The manager-owned staging folder's name for games that deploy rather than install in place, or
	/// <c>null</c> for games whose mods live inside the game folder.
	/// </summary>
	public string? StagingFolderName { get; init; }

	/// <summary>The game's Nexus Mods domain, used for every v1 API call and mod page URL.</summary>
	public required string NexusDomain { get; init; }

	/// <summary>The game's numeric Nexus id, used by the v2 GraphQL search.</summary>
	public required string NexusGameId { get; init; }

	/// <summary>The sound theme folder under <c>sounds/</c> that follows this game.</summary>
	public required string SoundTheme { get; init; }

	/// <summary>True when this game's mods are BepInEx plugins.</summary>
	public bool IsBepInEx => Layout == ModLayout.BepInExPlugins;

	/// <summary>True when this game deploys staged mods into a Data folder (Skyrim SE / Fallout 4).</summary>
	public bool IsBethesda => Layout == ModLayout.BethesdaStaged;

	/// <summary>
	/// The mods folder for an install at <paramref name="gameFolder"/>, or <c>""</c> when this game stages its
	/// mods outside the game and the path therefore isn't derivable from the game folder.
	/// </summary>
	public string ModsFolderFor(string gameFolder)
	{
		if (string.IsNullOrEmpty(ModsFolderRelativeToGame) || string.IsNullOrEmpty(gameFolder)) return "";
		return Path.Combine(gameFolder, ModsFolderRelativeToGame);
	}
}

/// <summary>The games the manager supports, and lookups over them.</summary>
public static class GameProfiles
{
	/// <summary>The id used when no game session is loaded.</summary>
	public const string NoGame = "None";

	public const string StardewValley = "StardewValley";
	public const string SkyrimSE      = "SkyrimSE";
	public const string Fallout4      = "Fallout4";
	public const string MoonlightPeaks = "MoonlightPeaks";

	/// <summary>Every supported game, in the alphabetical order the game lists and menus show them.</summary>
	public static readonly IReadOnlyList<GameProfile> All = new[]
	{
		new GameProfile
		{
			Id                   = Fallout4,
			DisplayName          = "Fallout 4",
			SteamAppId           = "377160",
			GogProductId         = "1998527297",
			SteamStoreUrl        = "https://store.steampowered.com/app/377160/Fallout_4/",
			GogStoreUrl          = "https://www.gog.com/game/fallout_4_game_of_the_year_edition",
			DefaultInstallFolder = @"C:\Program Files (x86)\Steam\steamapps\common\Fallout 4",
			GameExeName          = "Fallout4.exe",
			LoaderExeName        = "f4se_loader.exe",
			LoaderDisplayName    = "F4SE",
			Layout               = ModLayout.BethesdaStaged,
			StagingFolderName    = "Fallout4Mods",
			NexusDomain          = "fallout4",
			NexusGameId          = "1151",
			SoundTheme           = "Fallout 4"
		},
		new GameProfile
		{
			Id                   = MoonlightPeaks,
			DisplayName          = "Moonlight Peaks",
			SteamAppId           = "2209900",
			GogProductId         = null, // Steam only at the time of writing.
			SteamStoreUrl        = "https://store.steampowered.com/app/2209900/Moonlight_Peaks/",
			GogStoreUrl          = null,
			DefaultInstallFolder = @"C:\Program Files (x86)\Steam\steamapps\common\Moonlight Peaks",
			GameExeName          = "Moonlight Peaks.exe",
			// BepInEx loads through a winhttp.dll shim next to the game exe, so there is no loader to launch:
			// starting the game normally is what loads the mods.
			LoaderExeName        = "",
			LoaderDisplayName    = "BepInEx",
			Layout               = ModLayout.BepInExPlugins,
			ModsFolderRelativeToGame = @"BepInEx\plugins",
			NexusDomain          = "moonlightpeaks",
			NexusGameId          = "9480",
			SoundTheme           = "Moonlight Peaks"
		},
		new GameProfile
		{
			Id                   = SkyrimSE,
			DisplayName          = "Skyrim Special Edition",
			SteamAppId           = "489830",
			GogProductId         = "1711230643",
			SteamStoreUrl        = "https://store.steampowered.com/app/489830/The_Elder_Scrolls_V_Skyrim_Special_Edition/",
			GogStoreUrl          = "https://www.gog.com/game/the_elder_scrolls_v_skyrim_special_edition",
			DefaultInstallFolder = @"C:\Program Files (x86)\Steam\steamapps\common\Skyrim Special Edition",
			GameExeName          = "SkyrimSE.exe",
			LoaderExeName        = "skse64_loader.exe",
			LoaderDisplayName    = "SKSE",
			Layout               = ModLayout.BethesdaStaged,
			StagingFolderName    = "SkyrimSEMods",
			NexusDomain          = "skyrimspecialedition",
			NexusGameId          = "1704",
			SoundTheme           = "Skyrim"
		},
		new GameProfile
		{
			Id                   = StardewValley,
			DisplayName          = "Stardew Valley",
			SteamAppId           = "413150",
			GogProductId         = "1453375253",
			SteamStoreUrl        = "https://store.steampowered.com/app/413150/Stardew_Valley/",
			GogStoreUrl          = "https://www.gog.com/game/stardew_valley",
			DefaultInstallFolder = @"C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley",
			GameExeName          = "Stardew Valley.exe",
			LoaderExeName        = "StardewModdingAPI.exe",
			LoaderDisplayName    = "SMAPI",
			Layout               = ModLayout.StardewManifest,
			ModsFolderRelativeToGame = "Mods",
			NexusDomain          = "stardewvalley",
			NexusGameId          = "1303",
			SoundTheme           = "Stardew Valley"
		}
	};

	/// <summary>Every supported game's internal id, in display order.</summary>
	public static IReadOnlyList<string> AllIds { get; } = All.Select(g => g.Id).ToList();

	/// <summary>Every supported game's display name, in display order — the game lists and menus.</summary>
	public static IReadOnlyList<string> AllDisplayNames { get; } = All.Select(g => g.DisplayName).ToList();

	/// <summary>
	/// The profile for <paramref name="gameId"/>, or <c>null</c> for an unknown id and for "None" (no session).
	/// Callers that have already established a game is loaded should use <see cref="Require"/> instead.
	/// </summary>
	public static GameProfile? Find(string? gameId) =>
		string.IsNullOrEmpty(gameId) ? null : All.FirstOrDefault(g => g.Id == gameId);

	/// <summary>
	/// The profile for <paramref name="gameId"/>. Throws for an unknown id, deliberately: silently falling back
	/// to some other game's executable or Nexus domain is exactly the bug this registry exists to prevent.
	/// </summary>
	public static GameProfile Require(string gameId) =>
		Find(gameId) ?? throw new ArgumentException($"Unknown game id '{gameId}'.", nameof(gameId));

	/// <summary>The display name for a game id, or <c>""</c> when no game is loaded.</summary>
	public static string DisplayNameFor(string? gameId) => Find(gameId)?.DisplayName ?? "";

	/// <summary>The internal id for a display name as shown in the game lists, or <c>null</c> if unrecognised.</summary>
	public static string? IdForDisplayName(string? displayName) =>
		string.IsNullOrEmpty(displayName) ? null : All.FirstOrDefault(g => g.DisplayName == displayName)?.Id;
}
