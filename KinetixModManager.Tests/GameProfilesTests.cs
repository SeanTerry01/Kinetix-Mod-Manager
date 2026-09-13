using System;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="GameProfiles"/>, the single place the per-game constants live.
///
/// These tests exist because of the bug the registry was created to prevent. The per-game data used to be
/// spread across a dozen <c>game switch { ... _ =&gt; ... }</c> expressions whose default arm silently meant
/// "Stardew Valley". That was harmless while Stardew was the only fallback, but adding a fourth game made every
/// one of those defaults a trap: an unhandled game would have launched Stardew's executable, scanned Stardew's
/// mods folder and searched Stardew's Nexus domain, all without erroring.
/// </summary>
public class GameProfilesTests
{
    [Fact]
    public void EveryGameHasADistinctIdDisplayNameSteamAppIdAndNexusDomain()
    {
        // A duplicate in any of these silently makes two games the same game somewhere in the app.
        Assert.Equal(GameProfiles.All.Count, GameProfiles.All.Select(g => g.Id).Distinct().Count());
        Assert.Equal(GameProfiles.All.Count, GameProfiles.All.Select(g => g.DisplayName).Distinct().Count());
        Assert.Equal(GameProfiles.All.Count, GameProfiles.All.Select(g => g.SteamAppId).Distinct().Count());
        Assert.Equal(GameProfiles.All.Count, GameProfiles.All.Select(g => g.NexusDomain).Distinct().Count());
        Assert.Equal(GameProfiles.All.Count, GameProfiles.All.Select(g => g.NexusGameId).Distinct().Count());
    }

    [Fact]
    public void GamesAreListedAlphabeticallyByDisplayName()
    {
        // The Games menu, the game-selection list and the Settings picker all render this order directly.
        string[] shown = GameProfiles.AllDisplayNames.ToArray();
        string[] sorted = shown.OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(sorted, shown);
    }

    [Fact]
    public void AnUnknownGameResolvesToNothingRatherThanQuietlyToStardew()
    {
        Assert.Null(GameProfiles.Find("SomeGameWeDoNotSupport"));
        Assert.Null(GameProfiles.Find("None"));
        Assert.Null(GameProfiles.Find(""));
        Assert.Null(GameProfiles.Find(null));
        Assert.Equal("", GameProfiles.DisplayNameFor("SomeGameWeDoNotSupport"));
    }

    [Fact]
    public void RequireThrowsForAnUnknownGame()
    {
        // Callers that already know a game is loaded should fail loudly rather than get another game's data.
        Assert.Throws<ArgumentException>(() => GameProfiles.Require("SomeGameWeDoNotSupport"));
    }

    [Fact]
    public void DisplayNamesRoundTripToTheirIds()
    {
        // The game lists show display names and hand them straight back to be resolved to an id.
        foreach (GameProfile profile in GameProfiles.All)
            Assert.Equal(profile.Id, GameProfiles.IdForDisplayName(profile.DisplayName));

        Assert.Null(GameProfiles.IdForDisplayName("Not A Game We Support"));
    }

    [Fact]
    public void MoonlightPeaksCarriesTheValuesTheManagerDependsOn()
    {
        GameProfile mp = GameProfiles.Require(GameProfiles.MoonlightPeaks);

        Assert.Equal("Moonlight Peaks", mp.DisplayName);
        Assert.Equal("2209900", mp.SteamAppId);
        Assert.Equal("Moonlight Peaks.exe", mp.GameExeName);
        Assert.Equal("moonlightpeaks", mp.NexusDomain);
        Assert.Equal("9480", mp.NexusGameId);
        Assert.Equal(ModLayout.BepInExPlugins, mp.Layout);
        Assert.True(mp.IsBepInEx);
        Assert.False(mp.IsBethesda);
        // BepInEx hooks the game through a winhttp.dll shim, so there is no loader executable to start: the
        // game's own exe is the modded launch. An accidental loader name here would send the launcher looking
        // for a file that never exists.
        Assert.Equal("", mp.LoaderExeName);
    }

    [Fact]
    public void MoonlightPeaksModsFolderIsResolvedInsideTheGameFolder()
    {
        GameProfile mp = GameProfiles.Require(GameProfiles.MoonlightPeaks);

        string game = TestPaths.Under('D', "SteamLibrary", "steamapps", "common", "Moonlight Peaks");

        string mods = mp.ModsFolderFor(game);

        Assert.Equal(Path.Combine(game, "BepInEx", "plugins"), mods);
        Assert.Null(mp.StagingFolderName);
    }

    [Fact]
    public void BethesdaGamesStageTheirModsOutsideTheGameFolder()
    {
        foreach (string id in new[] { GameProfiles.SkyrimSE, GameProfiles.Fallout4 })
        {
            GameProfile profile = GameProfiles.Require(id);
            Assert.True(profile.IsBethesda);
            Assert.False(string.IsNullOrEmpty(profile.StagingFolderName));
            // Staged games have no mods folder derivable from the install.
            Assert.Equal("", profile.ModsFolderFor(@"C:\Games\Whatever"));
        }
    }

    [Fact]
    public void EveryGameOfferedForPurchaseHasAStorePageForEachStoreItClaims()
    {
        foreach (GameProfile profile in GameProfiles.All)
        {
            // A store id and that store's page have to travel together, in both directions: an id without a
            // page means the purchase dialog offers a store the game can't be bought from, and a page without
            // an id means a store the manager will never detect an install for.
            //
            // Tied to the id rather than asserted for everyone, because Minecraft claims neither. Mojang sells
            // Java Edition directly, so there is no Steam app id, no GOG product and no store page — and the
            // empty string has to stay empty rather than drifting into a plausible-looking placeholder.
            if (!string.IsNullOrEmpty(profile.SteamAppId))
                Assert.StartsWith("https://store.steampowered.com/", profile.SteamStoreUrl);
            else
                Assert.Equal("", profile.SteamStoreUrl);

            Assert.Equal(string.IsNullOrEmpty(profile.GogProductId), string.IsNullOrEmpty(profile.GogStoreUrl));
        }
    }

    // -------------------------------------------------------------------------
    // Install keys — telling two copies of one game apart
    // -------------------------------------------------------------------------

    [Fact]
    public void ABareGameIdIsItsOwnInstallKey()
    {
        // The whole reason a game's first copy keeps the bare id: every settings file and every folder already
        // on disk stays valid, so a one-copy owner is untouched by any of this.
        Assert.Equal(GameProfiles.SkyrimSE, GameProfiles.BaseId(GameProfiles.SkyrimSE));
        Assert.Equal(GamePlatform.Unknown, GameProfiles.PlatformOf(GameProfiles.SkyrimSE));
        Assert.Equal(GameProfiles.SkyrimSE, GameProfiles.InstallKeyFor(GameProfiles.SkyrimSE, GamePlatform.Gog, isPrimary: true));
    }

    [Fact]
    public void ASecondCopyCarriesItsPlatformAndStillResolvesToItsGame()
    {
        string gog = GameProfiles.InstallKeyFor(GameProfiles.SkyrimSE, GamePlatform.Gog, isPrimary: false);

        Assert.Equal("SkyrimSE@Gog", gog);
        Assert.Equal(GameProfiles.SkyrimSE, GameProfiles.BaseId(gog));
        Assert.Equal(GamePlatform.Gog, GameProfiles.PlatformOf(gog));

        // The profile lookups are what the ~227 call sites go through, so a second copy must find the same
        // executable, Nexus domain and mod layout as the first.
        Assert.Same(GameProfiles.Require(GameProfiles.SkyrimSE), GameProfiles.Require(gog));
        Assert.Equal("Skyrim Special Edition", GameProfiles.DisplayNameFor(gog));
        Assert.Equal("Skyrim", AppSettingsThemeFor(gog));
    }

    [Fact]
    public void IsGameAnswersForEitherCopyAndNeverForAnotherGame()
    {
        foreach (string key in new[] { GameProfiles.SkyrimSE, "SkyrimSE@Gog", "SkyrimSE@Steam" })
        {
            Assert.True(GameProfiles.IsGame(key, GameProfiles.SkyrimSE));
            Assert.False(GameProfiles.IsGame(key, GameProfiles.Fallout4));
            Assert.True(GameProfiles.IsAnyGame(key, GameProfiles.SkyrimSE, GameProfiles.Fallout4));
            Assert.False(GameProfiles.IsAnyGame(key, GameProfiles.StardewValley, GameProfiles.Witcher3));
        }
    }

    [Fact]
    public void NoGameAndUnknownIdsStayUnresolvable()
    {
        // "None" must not accidentally become a game, and an id that means nothing must still throw rather than
        // fall through to some other game's data — the failure this whole registry exists to force into the open.
        Assert.Null(GameProfiles.Find(GameProfiles.NoGame));
        Assert.Null(GameProfiles.Find("None@Gog"));
        Assert.Null(GameProfiles.Find(""));
        Assert.Null(GameProfiles.Find(null));
        Assert.Throws<ArgumentException>(() => GameProfiles.Require("Skyrim"));
        Assert.Throws<ArgumentException>(() => GameProfiles.Require("SkyrimSE2@Gog"));

        // A suffix that isn't a platform is not a licence to guess at one.
        Assert.Equal(GamePlatform.Unknown, GameProfiles.PlatformOf("SkyrimSE@Epic"));
        Assert.Equal(GamePlatform.Unknown, GameProfiles.PlatformOf("SkyrimSE@"));
    }

    /// <summary>The sound theme lookup, which normalises through <c>BaseId</c> like the profile lookups do.</summary>
    private static string AppSettingsThemeFor(string installKey) =>
        GameProfiles.Find(installKey)?.SoundTheme ?? "Default";
}
