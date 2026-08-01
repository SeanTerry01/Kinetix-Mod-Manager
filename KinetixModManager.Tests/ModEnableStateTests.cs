using System.IO;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ModEnableState"/> — where a mod folder has to sit for its loader to load it, or skip it.
///
/// The two layouts are not interchangeable, and getting them the wrong way round fails silently. SMAPI and the
/// manager's Bethesda deployment both skip a dot-prefixed folder, so those mods are disabled in place. BepInEx
/// walks <c>plugins</c> for DLLs and never looks at folder names, so a dot-renamed BepInEx mod keeps loading —
/// the user switches a mod off, the manager says it is off, and the mod is still running in game.
/// </summary>
public class ModEnableStateTests
{
    private static string P(params string[] parts) => Path.Combine(parts);

    private const string Plugins = @"D:\SteamLibrary\steamapps\common\Moonlight Peaks\BepInEx\plugins";
    private const string Disabled = @"D:\SteamLibrary\steamapps\common\Moonlight Peaks\BepInEx\plugins-disabled";

    // -------------------------------------------------------------------------
    // BepInEx: disabling moves the mod out of the folder the chainloader scans
    // -------------------------------------------------------------------------

    [Fact]
    public void BepInEx_DisablingMovesTheModOutOfPlugins()
    {
        string target = ModEnableState.TargetPath(P(Plugins, "SaveAnywhere"), enable: false, GameProfiles.MoonlightPeaks);

        Assert.Equal(P(Disabled, "SaveAnywhere"), target);
    }

    [Fact]
    public void BepInEx_EnablingMovesTheModBackIntoPlugins()
    {
        string target = ModEnableState.TargetPath(P(Disabled, "SaveAnywhere"), enable: true, GameProfiles.MoonlightPeaks);

        Assert.Equal(P(Plugins, "SaveAnywhere"), target);
    }

    [Fact]
    public void BepInEx_NeverUsesTheLeadingDotConvention()
    {
        // The whole point: a dot-renamed folder would still be scanned by BepInEx and its mod would still load.
        string disabled = ModEnableState.TargetPath(P(Plugins, "MoonlightAccess"), enable: false, GameProfiles.MoonlightPeaks);

        Assert.DoesNotContain(Path.DirectorySeparatorChar + ".", disabled);
        Assert.Equal("MoonlightAccess", Path.GetFileName(disabled));
    }

    [Fact]
    public void BepInEx_ReEnablingAnAlreadyEnabledModChangesNothing()
    {
        string path = P(Plugins, "TimeControl");

        Assert.Equal(path, ModEnableState.TargetPath(path, enable: true, GameProfiles.MoonlightPeaks));
    }

    [Fact]
    public void BepInEx_ReDisablingAnAlreadyDisabledModChangesNothing()
    {
        string path = P(Disabled, "TimeControl");

        Assert.Equal(path, ModEnableState.TargetPath(path, enable: false, GameProfiles.MoonlightPeaks));
    }

    [Fact]
    public void BepInEx_ModNameIsPreservedAcrossTheRoundTrip()
    {
        string original = P(Plugins, "MoonlightPeaks.ModMenu");

        string off = ModEnableState.TargetPath(original, enable: false, GameProfiles.MoonlightPeaks);
        string backOn = ModEnableState.TargetPath(off, enable: true, GameProfiles.MoonlightPeaks);

        Assert.Equal(original, backOn);
    }

    [Fact]
    public void BepInEx_IsEnabledIsJudgedByTheParentFolder()
    {
        Assert.True(ModEnableState.IsEnabled(P(Plugins, "SaveAnywhere"), GameProfiles.MoonlightPeaks));
        Assert.False(ModEnableState.IsEnabled(P(Disabled, "SaveAnywhere"), GameProfiles.MoonlightPeaks));
        // A dot-prefixed folder still inside plugins IS loaded by BepInEx, so it must report as enabled.
        Assert.True(ModEnableState.IsEnabled(P(Plugins, ".SaveAnywhere"), GameProfiles.MoonlightPeaks));
    }

    // -------------------------------------------------------------------------
    // Stardew / Bethesda: the long-standing leading-dot convention, unchanged
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(GameProfiles.StardewValley)]
    [InlineData(GameProfiles.SkyrimSE)]
    [InlineData(GameProfiles.Fallout4)]
    public void DotGames_DisablingPrefixesADot(string game)
    {
        string target = ModEnableState.TargetPath(P(@"C:\Mods", "SomeMod"), enable: false, game);

        Assert.Equal(P(@"C:\Mods", ".SomeMod"), target);
    }

    [Theory]
    [InlineData(GameProfiles.StardewValley)]
    [InlineData(GameProfiles.SkyrimSE)]
    [InlineData(GameProfiles.Fallout4)]
    public void DotGames_EnablingStripsTheDot(string game)
    {
        string target = ModEnableState.TargetPath(P(@"C:\Mods", ".SomeMod"), enable: true, game);

        Assert.Equal(P(@"C:\Mods", "SomeMod"), target);
    }

    [Fact]
    public void DotGames_DisablingAnAlreadyDisabledModDoesNotStackDots()
    {
        string target = ModEnableState.TargetPath(P(@"C:\Mods", ".SomeMod"), enable: false, GameProfiles.StardewValley);

        Assert.Equal(P(@"C:\Mods", ".SomeMod"), target);
    }

    [Fact]
    public void DotGames_IsEnabledIsJudgedByTheLeadingDot()
    {
        Assert.True(ModEnableState.IsEnabled(P(@"C:\Mods", "SomeMod"), GameProfiles.StardewValley));
        Assert.False(ModEnableState.IsEnabled(P(@"C:\Mods", ".SomeMod"), GameProfiles.StardewValley));
    }

    [Fact]
    public void TrailingSeparatorsDoNotProduceAnEmptyFolderName()
    {
        string target = ModEnableState.TargetPath(P(Plugins, "SaveAnywhere") + Path.DirectorySeparatorChar,
            enable: false, GameProfiles.MoonlightPeaks);

        Assert.Equal(P(Disabled, "SaveAnywhere"), target);
    }

    [Fact]
    public void AnEmptyPathIsReturnedUnchangedRatherThanTurnedIntoSomethingElse()
    {
        Assert.Equal("", ModEnableState.TargetPath("", enable: false, GameProfiles.MoonlightPeaks));
    }
}
