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
	BepInExPlugins,

	/// <summary>
	/// The Witcher 3: each mod is a folder under the game's <c>mods</c> folder whose name must begin with
	/// <c>mod</c> — that prefix is precisely how the engine decides what to load. Mods live where they are
	/// installed, and one is disabled by prefixing its folder with <c>~</c>, which works for the plainest of
	/// reasons: <c>~modFoo</c> no longer starts with "mod", so the engine walks straight past it.
	/// </summary>
	Witcher3Mods,

	/// <summary>
	/// Minecraft (Java Edition): every mod is a single <c>.jar</c> file sitting directly in the
	/// <c>mods</c> folder, loaded by Fabric. This is the first layout where a mod is a FILE rather than a
	/// folder, which is the one assumption every other layout shares — so anything that walks directories
	/// looking for mods has to be asked about this case explicitly rather than inheriting a default.
	///
	/// The jar carries its own metadata in a <c>fabric.mod.json</c> at its root: id, name, version, authors and
	/// a dependency map with version ranges. That is richer than any other supported game manages, Stardew's
	/// <c>manifest.json</c> included — it just happens to be inside a zip.
	///
	/// A mod is disabled by renaming it to <c>.jar.disabled</c>; see <see cref="GameProfile.DisabledModSuffix"/>.
	/// </summary>
	FabricMods
}

/// <summary>
/// Where a game's mods come from unless the user says otherwise.
///
/// <para>
/// Nexus was the only answer for the first five games, to the point that the assumption is spread across the
/// download, search and update-check paths. Minecraft breaks it: Nexus has a Minecraft section, but it is
/// largely maps and legacy content, and Fabric mods live on Modrinth and CurseForge instead. Modrinth's API is
/// the better one anyway — no key, and it states the loader and game version each file targets, which is the
/// very thing the update checker has to guess at everywhere else.
/// </para>
///
/// <para>
/// ⚠️ Not to be confused with <see cref="ModSources"/>, which is the catalogue of every site the manager knows
/// about and is keyed by string id. This enum is narrower and answers one question: which site a game
/// <em>starts</em> on. <see cref="ModSources.DefaultFor"/> is the bridge between them, and the user's own
/// preference overrides it — see <see cref="ModSearchMode"/>.
/// </para>
/// </summary>
public enum ModSource
{
	Nexus,
	Modrinth
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

	/// <summary>
	/// The game's wiki: the MediaWiki <c>api.php</c> that search and categories query, and the article
	/// prefix a result is opened at.
	///
	/// Here rather than in the window that displays them, because they are per-game constants and this is
	/// where those live — the same reasoning that put the Steam ids and the executable names here. They
	/// were a pair of <c>game switch { ... }</c> expressions inside Form1.Wiki, which is precisely the
	/// shape this class exists to replace, and it meant a second front end could not open a wiki at all.
	/// </summary>
	public string WikiApiUrl { get; init; } = "";

	/// <summary>Article URL prefix; a page is opened as this plus the title.</summary>
	public string WikiArticleBase { get; init; } = "";

	/// <summary>
	/// The file the game's mod loader writes its log to — the one the Log tab opens first. Empty for a game
	/// whose loader keeps none.
	///
	/// Another <c>game switch</c> that was living in Form1, and per-game data like every other entry here.
	/// Stardew Valley is deliberately empty: SMAPI's log is richer and gets its own screen rather than the
	/// plain Log tab.
	/// </summary>
	public string LoaderLogFileName { get; init; } = "";

	/// <summary>The game's name as the user sees it, e.g. "Moonlight Peaks".</summary>
	public required string DisplayName { get; init; }

	/// <summary>
	/// The game's Steam application id, used for registry and libraryfolders.vdf detection, or <c>""</c> for a
	/// game Steam does not sell.
	///
	/// Optional rather than required, which weakens the "a new game must supply this" guarantee slightly, and
	/// deliberately: Minecraft (Java Edition) is sold by Mojang directly and installs as a Microsoft Store
	/// package or a standalone launcher, so it has no Steam id, no GOG id and no install folder under a Steam
	/// library. Forcing a placeholder would be worse than an empty string — a fake app id is a value detection
	/// code would go looking for. <see cref="IsSoldThroughAStore"/> is the question to ask.
	/// </summary>
	public string SteamAppId { get; init; } = "";

	/// <summary>
	/// Further Steam app ids the same install can be sold under, tried after <see cref="SteamAppId"/>.
	///
	/// A game re-released as a bundle keeps its original app id for people who bought it before — The Witcher 3
	/// is app 292030 as Wild Hunt and 499450 as the Complete Edition, and both install the same game into the
	/// same folder. Detection that knows only one of them finds nothing for half the owners.
	/// </summary>
	public IReadOnlyList<string> AlternateSteamAppIds { get; init; } = Array.Empty<string>();

	/// <summary>The game's GOG product id, or <c>null</c> when it isn't sold on GOG.</summary>
	public string? GogProductId { get; init; }

	/// <summary>Further GOG product ids for the same game — its editions, which GOG sells as separate products.</summary>
	public IReadOnlyList<string> AlternateGogProductIds { get; init; } = Array.Empty<string>();

	/// <summary>Every Steam app id this game may be installed under, the primary one first.</summary>
	public IEnumerable<string> AllSteamAppIds =>
		new[] { SteamAppId }.Concat(AlternateSteamAppIds).Where(id => !string.IsNullOrEmpty(id));

	/// <summary>Every GOG product id this game may be installed under, the primary one first.</summary>
	public IEnumerable<string> AllGogProductIds =>
		new[] { GogProductId }.Concat(AlternateGogProductIds)
			.Where(id => !string.IsNullOrEmpty(id))
			.Select(id => id!);

	/// <summary>The Steam store page, shown when the user doesn't own the game yet.</summary>
	public string SteamStoreUrl { get; init; } = "";

	/// <summary>The GOG store page, or <c>null</c> when it isn't sold on GOG.</summary>
	public string? GogStoreUrl { get; init; }

	/// <summary>Where the game usually installs, tried last when nothing else finds it.</summary>
	public string DefaultInstallFolder { get; init; } = "";

	/// <summary>The game's own executable, e.g. "Moonlight Peaks.exe".</summary>
	public string GameExeName { get; init; } = "";

	/// <summary>
	/// The mod loader's launcher executable, or <c>""</c> when the loader has none. Stardew and the Bethesda
	/// games are started through their loader (SMAPI, SKSE, F4SE); BepInEx instead hooks the game through a
	/// <c>winhttp.dll</c> shim, so the game's own exe loads mods and there is nothing separate to run.
	/// </summary>
	public required string LoaderExeName { get; init; }

	/// <summary>The loader's name as the user knows it ("SMAPI", "SKSE", "BepInEx"), or <c>""</c> if none.</summary>
	public required string LoaderDisplayName { get; init; }

	/// <summary>
	/// Whether the manager must start <see cref="GameExeName"/> itself rather than asking the store to launch
	/// the game.
	///
	/// Asking Steam is normally the better way — a Steam game started from its own executable notices and
	/// restarts itself, which loads the game twice. But some games ship a launcher in front of the executable,
	/// and Steam starts the launcher. The Witcher 3's is `REDprelauncher.exe`, which is a graphical window a
	/// screen reader cannot use, and it is also what chooses between the DirectX 11 and DirectX 12 builds — so
	/// going through it means a player may not be able to get past it, and may not land on the build their mods
	/// expect. Starting `bin\x64\witcher3.exe` (the DirectX 11 build) skips both problems.
	/// </summary>
	public bool LaunchGameExeDirectly { get; init; }

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
	public string NexusDomain { get; init; } = "";

	/// <summary>The game's numeric Nexus id, used by the v2 GraphQL search.</summary>
	public string NexusGameId { get; init; } = "";

	/// <summary>The sound theme folder under <c>sounds/</c> that follows this game.</summary>
	public required string SoundTheme { get; init; }

	/// <summary>
	/// The folder this game keeps its per-player files in — the INIs under <c>Documents\My Games\</c>, the saves
	/// beneath them, and the active-plugins list under <c>%LOCALAPPDATA%\</c>. <c>null</c> for a game that keeps
	/// none of that (Stardew Valley, Moonlight Peaks).
	/// </summary>
	public string? UserDataFolderName { get; init; }

	/// <summary>
	/// The same folder for a GOG copy of the game, when GOG's release names it differently.
	///
	/// Bethesda's GOG releases do exactly that, and it is not cosmetic: a GOG copy of Skyrim keeps its INIs and
	/// its load order in "Skyrim Special Edition GOG", so a manager that assumes the Steam name edits the wrong
	/// game's files. On a machine with both installed — which is how this was found — it would quietly rewrite
	/// the Steam copy's load order while the user believed they were managing the GOG one.
	/// </summary>
	public string? GogUserDataFolderName { get; init; }

	/// <summary>
	/// Whether the per-player folder sits under <c>Documents\My Games\</c>, as Bethesda's games put it, rather
	/// than directly under <c>Documents\</c>. The Witcher 3 uses <c>Documents\The Witcher 3</c>, with no
	/// intervening My Games folder, so this is not a detail that can be assumed.
	/// </summary>
	public bool UserDataUnderMyGames { get; init; } = true;

	/// <summary>The save folder's name inside the per-player folder (<c>Saves</c>, <c>gamesaves</c>).</summary>
	public string? SavesFolderName { get; init; }

	/// <summary>The extension the game's save files carry, e.g. <c>.ess</c>, <c>.fos</c>, <c>.sav</c>.</summary>
	public string? SaveFileExtension { get; init; }

	/// <summary>
	/// The game's own configuration files inside the per-player folder, in the order players reach for them.
	/// All are INI-shaped (<c>[Section]</c> plus <c>key=value</c>), which is what lets one editor serve them all
	/// — The Witcher 3's <c>user.settings</c> and <c>input.settings</c> included, despite the unusual extension.
	/// Empty for a game whose settings the manager doesn't edit.
	/// </summary>
	public IReadOnlyList<string> ConfigFileNames { get; init; } = Array.Empty<string>();

	/// <summary>
	/// The prefix that marks a mod folder disabled, or <c>null</c> when this game disables mods by moving them
	/// somewhere else instead. SMAPI and the manager's own Bethesda deployment skip a leading dot; The Witcher 3
	/// skips anything not starting with "mod", which a leading <c>~</c> arranges.
	/// </summary>
	public string? DisabledModPrefix { get; init; }

	/// <summary>
	/// The suffix appended to a mod's file name to switch it off, or <c>null</c> for a game that disables mods
	/// some other way.
	///
	/// This exists because Minecraft is the first game whose mods are files rather than folders, and a file
	/// cannot take a disabling prefix without changing the name the loader reports. Fabric's mod discovery
	/// accepts a candidate only when it ends in <c>.jar</c> — verified by reading the string constants out of
	/// <c>DirectoryModCandidateFinder</c> — so <c>foo.jar.disabled</c> is walked straight past, exactly the way
	/// The Witcher 3 walks past a folder not starting with "mod".
	/// </summary>
	public string? DisabledModSuffix { get; init; }

	/// <summary>
	/// The prefix a mod's folder name must carry for the game to load it at all, or <c>null</c> where the name
	/// doesn't matter. The Witcher 3 loads a folder only when it is called <c>mod*</c>, so a mod installed from
	/// an archive that unpacks to some other name has to be renamed or it simply never loads — silently.
	/// </summary>
	public string? RequiredModFolderPrefix { get; init; }

	/// <summary>
	/// The per-player data folder name for the copy installed at <paramref name="gameFolder"/>, choosing the GOG
	/// name when that folder holds a GOG install. <c>""</c> for a game that keeps no such folder.
	/// </summary>
	public string UserDataFolderFor(string gameFolder)
	{
		if (string.IsNullOrEmpty(UserDataFolderName)) return "";

		if (!string.IsNullOrEmpty(GogUserDataFolderName) &&
			GogLibraryLocator.IsGogInstallAnyOf(gameFolder, AllGogProductIds))
			return GogUserDataFolderName;

		return UserDataFolderName;
	}

	/// <summary>
	/// The full path of the per-player folder for the copy at <paramref name="gameFolder"/> — the one holding the
	/// game's settings, its saves and (on the Bethesda games) its load order. <c>""</c> where the game keeps none.
	/// </summary>
	public string UserDataDirectoryFor(string gameFolder)
	{
		string folder = UserDataFolderFor(gameFolder);
		if (folder.Length == 0) return "";

		string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
		return UserDataUnderMyGames
			? Path.Combine(docs, "My Games", folder)
			: Path.Combine(docs, folder);
	}

	/// <summary>
	/// The file, relative to the game folder, that a keybind-export plugin writes the game's own keyboard
	/// bindings to — or <c>null</c> for a game with no such plugin.
	///
	/// Some games resolve their bindings at runtime and keep nothing readable on disk: Moonlight Peaks uses
	/// Rewired, so there is no input asset to parse and a player's remaps live inside Rewired's own save data.
	/// The only way to know what a key does is to ask the game while it runs, which a small plugin does,
	/// leaving the answer in a file the manager can read.
	/// </summary>
	public string? KeybindExportFileRelativeToGame { get; init; }

	/// <summary>
	/// The snapshot of this game's stock bindings that ships inside the manager, under <c>data\</c>, or
	/// <c>null</c> when there is none.
	///
	/// This is what makes the controls list work for someone who has just bought the game: they have no export
	/// plugin and have never launched it, and "the controls are empty until you install something and play
	/// once" is a poor answer from the feature whose whole job is to say what the controls are. Same schema as
	/// the live export, so one reader handles both.
	/// </summary>
	public string? BundledKeybindsFileName { get; init; }

	/// <summary>
	/// The keybind-reader plugin the manager carries for this game, under <c>data\plugins\</c>, or <c>null</c>
	/// when there is none. Offered to the player once, never installed without them saying yes.
	/// </summary>
	public string? KeybindReaderFileName { get; init; }

	/// <summary>The folder the keybind reader installs into, under the game's mods folder.</summary>
	public string? KeybindReaderFolderName { get; init; }

	/// <summary>
	/// Where this game's mods are browsed, downloaded and update-checked from. Nexus for every game the manager
	/// supported first; Modrinth for Minecraft, whose Fabric mods simply are not on Nexus.
	/// </summary>
	public ModSource ModSource { get; init; } = ModSource.Nexus;

	/// <summary>
	/// True when a shop sells this game, i.e. there is a store page to send someone to and a store install to
	/// detect. False for Minecraft, which Mojang sells directly and which installs as a Microsoft Store package
	/// or a standalone launcher under the user's profile.
	/// </summary>
	public bool IsSoldThroughAStore => !string.IsNullOrEmpty(SteamAppId) || !string.IsNullOrEmpty(GogProductId);

	/// <summary>True when this game's mods are BepInEx plugins.</summary>
	public bool IsBepInEx => Layout == ModLayout.BepInExPlugins;

	/// <summary>True when this game's mods are loose Fabric jars (Minecraft).</summary>
	public bool IsMinecraft => Layout == ModLayout.FabricMods;

	/// <summary>True when this game deploys staged mods into a Data folder (Skyrim SE / Fallout 4).</summary>
	public bool IsBethesda => Layout == ModLayout.BethesdaStaged;

	/// <summary>True when this game's mods are folders under its own <c>mods</c> folder (The Witcher 3).</summary>
	public bool IsWitcher3 => Layout == ModLayout.Witcher3Mods;

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

/// <summary>
/// The store a particular copy of a game was bought from. Two copies of one game are told apart by this, and it
/// is also what decides which build of a script extender fits — SKSE ships a different file for the GOG release
/// than for the Steam one.
/// </summary>
public enum GamePlatform
{
	/// <summary>The copy's store could not be established — a hand-placed install, or one located by the user.</summary>
	Unknown,
	Steam,
	Gog
}

/// <summary>The games the manager supports, and lookups over them.</summary>
public static class GameProfiles
{
	/// <summary>The id used when no game session is loaded.</summary>
	public const string NoGame = "None";

	/// <summary>
	/// Separates a game id from its platform in an install key. Chosen because no game id contains it, so an
	/// install key can always be split back into its parts without ambiguity.
	/// </summary>
	public const char InstallKeySeparator = '@';

	public const string StardewValley = "StardewValley";
	public const string SkyrimSE      = "SkyrimSE";
	public const string Fallout4      = "Fallout4";
	public const string Minecraft     = "Minecraft";
	public const string MoonlightPeaks = "MoonlightPeaks";
	public const string Witcher3      = "Witcher3";

	/// <summary>Every supported game, in the alphabetical order the game lists and menus show them.</summary>
	public static readonly IReadOnlyList<GameProfile> All = new[]
	{
		new GameProfile
		{
			Id                   = Fallout4,
			LoaderLogFileName    = "f4se.log",
			WikiApiUrl           = "https://fallout.fandom.com/api.php",
			WikiArticleBase      = "https://fallout.fandom.com/wiki/",
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
			SoundTheme           = "Fallout 4",
			UserDataFolderName   = "Fallout4",
			SavesFolderName      = "Saves",
			SaveFileExtension    = ".fos",
			ConfigFileNames      = new[] { "Fallout4.ini", "Fallout4Prefs.ini", "Fallout4Custom.ini" },
			DisabledModPrefix    = ".",
			// Follows the same pattern as Skyrim's GOG release, which was read out of the GOG executable itself.
			// Not verified against a GOG copy of Fallout 4 — if one ever proves otherwise, this is the one line
			// to change, and a wrong value here is caught by the install simply not being detected as GOG.
			GogUserDataFolderName = "Fallout4 GOG"
		},
		new GameProfile
		{
			Id                   = Minecraft,
			LoaderLogFileName    = "latest.log",
			WikiApiUrl           = "https://minecraft.wiki/api.php",
			WikiArticleBase      = "https://minecraft.wiki/w/",
			DisplayName          = "Minecraft",
			// No store ids at all: Mojang sells Java Edition directly, and it arrives either as the Microsoft
			// Store package Microsoft.MinecraftJavaEdition_8wekyb3d8bbwe or as the standalone launcher. Neither
			// is a Steam or GOG install, so there is nothing here to detect a copy by and no store page to send
			// anyone to. Detection lives in MinecraftLayout instead.
			SteamAppId           = "",
			GogProductId         = null,
			SteamStoreUrl        = "",
			GogStoreUrl          = null,
			// The game "folder" for Minecraft is the .minecraft data folder, not an install directory — that is
			// where mods, config, saves, logs and the launcher profiles all live, and it is the only path the
			// manager ever needs. Resolved at startup rather than hard-coded so a relocated or per-profile
			// gameDir works the same way.
			DefaultInstallFolder = MinecraftLayout.DefaultRootFolder,
			// Nothing to launch directly: the game is started by building a java command line, which is what
			// MinecraftLayout does. There is no exe here to point Steam or Explorer at.
			GameExeName          = "",
			LoaderExeName        = "",
			LoaderDisplayName    = "Fabric",
			Layout               = ModLayout.FabricMods,
			ModsFolderRelativeToGame = "mods",
			// Fabric mods are not on Nexus in any meaningful way; Modrinth is where they live.
			ModSource            = ModSource.Modrinth,
			NexusDomain          = "",
			NexusGameId          = "",
			SoundTheme           = "Minecraft",
			// A mod is a file, so it is switched off by a suffix rather than a prefix: Fabric only accepts a
			// candidate ending in ".jar", so "foo.jar.disabled" is skipped.
			DisabledModSuffix    = ".disabled"
		},
		new GameProfile
		{
			Id                   = MoonlightPeaks,
			LoaderLogFileName    = "LogOutput.log",
			WikiApiUrl           = "https://moonlightpeaks.wiki.gg/api.php",
			WikiArticleBase      = "https://moonlightpeaks.wiki.gg/wiki/",
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
			ModsFolderRelativeToGame = Path.Combine("BepInEx", "plugins"),
			NexusDomain          = "moonlightpeaks",
			NexusGameId          = "9480",
			SoundTheme           = "Moonlight Peaks",
			// Written by the Moonlight Keybind Export plugin; the bundled snapshot stands in until it exists.
			KeybindExportFileRelativeToGame = Path.Combine("BepInEx", "moonlight-keybinds.json"),
			BundledKeybindsFileName         = "moonlight-peaks.defaults.json",
			KeybindReaderFileName           = "MoonlightKeybindExport.dll",
			KeybindReaderFolderName         = "MoonlightKeybindExport"
		},
		new GameProfile
		{
			Id                   = SkyrimSE,
			LoaderLogFileName    = "skse64.log",
			WikiApiUrl           = "https://en.uesp.net/w/api.php",
			WikiArticleBase      = "https://en.uesp.net/wiki/",
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
			SoundTheme           = "Skyrim",
			UserDataFolderName   = "Skyrim Special Edition",
			SavesFolderName      = "Saves",
			SaveFileExtension    = ".ess",
			ConfigFileNames      = new[] { "Skyrim.ini", "SkyrimPrefs.ini", "SkyrimCustom.ini" },
			DisabledModPrefix    = ".",
			// Read out of the GOG build's own executable, not guessed. GOG installs the game into a folder called
			// "Skyrim Anniversary Edition", which is a third name again — hence matching on neither.
			GogUserDataFolderName = "Skyrim Special Edition GOG"
		},
		new GameProfile
		{
			Id                   = StardewValley,
			WikiApiUrl           = "https://stardewvalleywiki.com/mediawiki/api.php",
			WikiArticleBase      = "https://stardewvalleywiki.com/",
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
			SoundTheme           = "Stardew Valley",
			DisabledModPrefix    = "."
		},
		new GameProfile
		{
			Id                   = Witcher3,
			LoaderLogFileName    = "WitcherAccess.log",
			WikiApiUrl           = "https://witcher.fandom.com/api.php",
			WikiArticleBase      = "https://witcher.fandom.com/wiki/",
			DisplayName          = "The Witcher 3: Wild Hunt",
			// Wild Hunt's original app id. The Complete Edition sells as 499450 and installs the same game into
			// the same folder, so both have to be looked for.
			SteamAppId           = "292030",
			AlternateSteamAppIds = new[] { "499450" },
			GogProductId         = "1207664643",
			// "The Witcher 3: Wild Hunt - Complete Edition" — a separate GOG product for the same game. Both ids
			// were read back from api.gog.com rather than guessed.
			AlternateGogProductIds = new[] { "1495134320" },
			SteamStoreUrl        = "https://store.steampowered.com/app/292030/The_Witcher_3_Wild_Hunt/",
			GogStoreUrl          = "https://www.gog.com/game/the_witcher_3_wild_hunt",
			DefaultInstallFolder = @"C:\Program Files (x86)\Steam\steamapps\common\The Witcher 3",
			// The game's exe lives two folders down, not beside the game root — every path built from this one
			// goes through Path.Combine, which takes the relative path in its stride. It is assembled with
			// Path.Combine rather than written as "bin\x64\witcher3.exe" for the same reason: a backslash is
			// only a separator on Windows, and Path.Combine would treat the literal as one long file name off it.
			GameExeName          = Path.Combine("bin", "x64", "witcher3.exe"),
			// Nothing to launch separately: The Witcher 3 loads the contents of its own mods folder, and the
			// native part of an accessibility mod arrives as an .asi beside the exe, loaded however it starts.
			LoaderExeName        = "",
			LoaderDisplayName    = "",
			// Skip REDprelauncher, which is what Steam would start: it is a graphical window a screen reader
			// cannot use, and it is also what chooses between the DirectX 11 and DirectX 12 builds. The x64
			// folder is the DirectX 11 one, which is the build the accessibility mod is developed against
			// (x64_dx12 holds the other).
			LaunchGameExeDirectly = true,
			Layout               = ModLayout.Witcher3Mods,
			ModsFolderRelativeToGame = "mods",
			NexusDomain          = "witcher3",
			NexusGameId          = "952",
			// Authored from the game's own audio, under sounds\The Witcher 3\. Any sound a theme does not
			// provide falls back to the Default one, so a theme need never be complete to be worth shipping.
			SoundTheme           = "The Witcher 3",
			// Documents\The Witcher 3, with no My Games in between.
			UserDataFolderName   = "The Witcher 3",
			UserDataUnderMyGames = false,
			SavesFolderName      = "gamesaves",
			SaveFileExtension    = ".sav",
			// INI-shaped despite the extension: [Section] headers and key=value lines.
			ConfigFileNames      = new[] { "user.settings", "input.settings" },
			DisabledModPrefix    = "~",
			RequiredModFolderPrefix = "mod"
		}
	};

	/// <summary>Every supported game's internal id, in display order.</summary>
	public static IReadOnlyList<string> AllIds { get; } = All.Select(g => g.Id).ToList();

	/// <summary>Every supported game's display name, in display order — the game lists and menus.</summary>
	public static IReadOnlyList<string> AllDisplayNames { get; } = All.Select(g => g.DisplayName).ToList();

	// -------------------------------------------------------------------------
	// Install keys
	// -------------------------------------------------------------------------
	// Someone can own the same game twice — the Steam copy and the GOG one, installed side by side. Everything
	// the manager remembers about a game is keyed by a string: the settings dictionaries, and the folders under
	// %AppData% holding the deployment manifest, downloads, backups, safety snapshots, save backups and search
	// history. So the way to make all of that per-COPY rather than per-GAME is to make that one string identify
	// the copy.
	//
	// An install key is the bare game id for a game's first copy ("SkyrimSE") and "<gameId>@<platform>" for any
	// further one ("SkyrimSE@Gog"). Keeping the first copy's key bare is what makes this cost nothing: every
	// existing settings file and every folder already on disk stays valid, and a user who owns one copy of each
	// game — which is nearly everyone — sees no change at all.

	/// <summary>
	/// The game id inside an install key — <c>"SkyrimSE@Gog"</c> gives <c>"SkyrimSE"</c>, and a key that is
	/// already a bare game id is returned unchanged. Use this wherever the question is "which game is this?"
	/// rather than "which copy is this?": the Nexus domain, the LOOT masterlist, the mod layout.
	/// </summary>
	public static string BaseId(string? installKey)
	{
		if (string.IsNullOrEmpty(installKey)) return "";
		int at = installKey.IndexOf(InstallKeySeparator);
		return at < 0 ? installKey : installKey.Substring(0, at);
	}

	/// <summary>
	/// True when <paramref name="installKey"/> identifies a copy of <paramref name="gameId"/>, whichever store it
	/// came from.
	///
	/// This exists to be used INSTEAD of <c>game == "SkyrimSE"</c>. A bare comparison against a game id stopped
	/// being right the moment a second copy could exist: "SkyrimSE@Gog" does not equal "SkyrimSE", so the
	/// comparison quietly answers no and the caller takes the branch meant for some other game entirely. A test
	/// in the test project scans the sources and fails on any such comparison that comes back.
	/// </summary>
	public static bool IsGame(string? installKey, string gameId) =>
		string.Equals(BaseId(installKey), gameId, StringComparison.Ordinal);

	/// <summary>True when <paramref name="installKey"/> is a copy of any of <paramref name="gameIds"/>.</summary>
	public static bool IsAnyGame(string? installKey, params string[] gameIds)
	{
		string id = BaseId(installKey);
		foreach (string candidate in gameIds)
			if (string.Equals(id, candidate, StringComparison.Ordinal)) return true;
		return false;
	}

	/// <summary>
	/// The install key for a copy of <paramref name="gameId"/> from <paramref name="platform"/>.
	///
	/// <paramref name="isPrimary"/> is what decides whether the key is suffixed at all: a game's first copy keeps
	/// the bare id no matter which store it came from, so a GOG-only owner's key is "SkyrimSE" and their existing
	/// settings keep working. Only a second copy needs telling apart.
	/// </summary>
	public static string InstallKeyFor(string gameId, GamePlatform platform, bool isPrimary) =>
		isPrimary || platform == GamePlatform.Unknown ? gameId : gameId + InstallKeySeparator + platform;

	/// <summary>
	/// The platform named in an install key, or <see cref="GamePlatform.Unknown"/> for a bare game id — which
	/// says nothing about where that copy came from, only that it is the game's first copy. The copy's recorded
	/// <c>GameInstall.Platform</c> is the answer to "which store"; this is only for reading the key back.
	/// </summary>
	public static GamePlatform PlatformOf(string? installKey)
	{
		if (string.IsNullOrEmpty(installKey)) return GamePlatform.Unknown;
		int at = installKey.IndexOf(InstallKeySeparator);
		if (at < 0 || at == installKey.Length - 1) return GamePlatform.Unknown;
		return Enum.TryParse(installKey.Substring(at + 1), ignoreCase: true, out GamePlatform p)
			? p
			: GamePlatform.Unknown;
	}

	/// <summary>How a platform is named to the user, e.g. in "Skyrim Special Edition (GOG)".</summary>
	public static string PlatformDisplayName(GamePlatform platform) => platform switch
	{
		GamePlatform.Steam => "Steam",
		GamePlatform.Gog   => "GOG",
		_                  => ""
	};

	/// <summary>
	/// The profile for <paramref name="gameId"/>, or <c>null</c> for an unknown id and for "None" (no session).
	/// Accepts an install key as well as a bare game id, so the great many callers that only want the game's own
	/// data — its executable, its Nexus domain, its mod layout — need not care which copy is loaded.
	/// Callers that have already established a game is loaded should use <see cref="Require"/> instead.
	/// </summary>
	public static GameProfile? Find(string? gameId)
	{
		string id = BaseId(gameId);
		return id.Length == 0 ? null : All.FirstOrDefault(g => g.Id == id);
	}

	/// <summary>
	/// The profile for <paramref name="gameId"/>. Throws for an unknown id, deliberately: silently falling back
	/// to some other game's executable or Nexus domain is exactly the bug this registry exists to prevent.
	/// </summary>
	public static GameProfile Require(string gameId) =>
		Find(gameId) ?? throw new ArgumentException($"Unknown game id '{gameId}'.", nameof(gameId));

	/// <summary>
	/// The profile whose <see cref="GameProfile.NexusDomain"/> is <paramref name="domain"/> — the game named at the
	/// start of every <c>nxm://</c> link — or <c>null</c> when the link is for a game this manager does not support.
	/// A null answer is a sentence to say to the user, not a reason to guess: the alternative is asking Nexus about
	/// one game's mod under another game's name, which is how the download path used to fail.
	/// </summary>
	public static GameProfile? FindByNexusDomain(string? domain) =>
		string.IsNullOrWhiteSpace(domain)
			? null
			: All.FirstOrDefault(g => string.Equals(g.NexusDomain, domain, StringComparison.OrdinalIgnoreCase));

	/// <summary>The display name for a game id, or <c>""</c> when no game is loaded.</summary>
	public static string DisplayNameFor(string? gameId) => Find(gameId)?.DisplayName ?? "";

	/// <summary>The internal id for a display name as shown in the game lists, or <c>null</c> if unrecognised.</summary>
	public static string? IdForDisplayName(string? displayName) =>
		string.IsNullOrEmpty(displayName) ? null : All.FirstOrDefault(g => g.DisplayName == displayName)?.Id;
}
