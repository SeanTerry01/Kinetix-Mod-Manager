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

        string mods = mp.ModsFolderFor(@"D:\SteamLibrary\steamapps\common\Moonlight Peaks");

        Assert.Equal(@"D:\SteamLibrary\steamapps\common\Moonlight Peaks\BepInEx\plugins", mods);
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
            Assert.StartsWith("https://store.steampowered.com/", profile.SteamStoreUrl);
            // A GOG product id and a GOG store page have to travel together: one without the other means the
            // purchase dialog offers a store the game can't be bought from, or misses one it can.
            Assert.Equal(string.IsNullOrEmpty(profile.GogProductId), string.IsNullOrEmpty(profile.GogStoreUrl));
        }
    }
}
