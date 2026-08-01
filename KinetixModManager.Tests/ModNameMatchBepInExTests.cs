using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Auto-matching installed BepInEx mods to their Nexus pages, using the real Moonlight Peaks data that exposed
/// the problem.
///
/// Auto-match used to search only for a mod's declared name and, failing an exact hit, fall back to "partial
/// name plus the same author". Neither works for a BepInEx plugin: authors routinely title the plugin one thing
/// and the Nexus page another ("Bigger Stacks for Items" versus a page called "BiggerStacks"), and the manager
/// has no author to compare because a plugin doesn't record one. Of eleven installed mods, two matched.
///
/// The identifiers that do work were already on disk: the folder the mod was installed into, and the plugin's
/// GUID, which conventionally carries both the author's handle and the mod's real name. Every case below is a
/// real installed mod and a real Nexus page (mod ids and authors as published), so a regression here is a
/// regression against mods someone actually has.
/// </summary>
public class ModNameMatchBepInExTests
{
    /// <summary>An installed BepInEx mod as ScanMods builds it: declared name, folder, and plugin GUID.</summary>
    private static GameMod Installed(string pluginName, string folder, string guid) => new GameMod
    {
        Name = pluginName,
        FolderPath = Path.Combine(@"D:\Games\Moonlight Peaks\BepInEx\plugins", folder),
        UniqueId = guid,
        // ScanMods has no author to record for a BepInEx plugin — this is the crux of the old failure.
        Author = "Unknown"
    };

    /// <summary>A Nexus search result as the GraphQL search returns it.</summary>
    private static GameMod Nexus(string modId, string name, string author) => new GameMod
    {
        Name = name,
        Author = author,
        NexusID = modId,
        UniqueId = modId,
        IsSearchResult = true
    };

    // The real Moonlight Peaks catalogue entries these mods correspond to.
    private static readonly GameMod BiggerStacks      = Nexus("33",  "BiggerStacks",                          "Kuriti0n");
    private static readonly GameMod ModMenu           = Nexus("102", "Mod Menu",                              "Elsiabeth");
    private static readonly GameMod TimeControl       = Nexus("85",  "TimeControl",                           "SeanTerry01");
    private static readonly GameMod ShowItemValue     = Nexus("27",  "Always Show Item Value (sell price)",   "Padme4000");
    private static readonly GameMod HouseStorage      = Nexus("7",   "House Storage Anywhere",                "lockyaw");
    private static readonly GameMod SaveAnywhere      = Nexus("11",  "Save Anywhere",                         "SerenaEnchanted");
    private static readonly GameMod InfiniteEnergy    = Nexus("2",   "Infinite Energy",                       "Ask for Mods");
    private static readonly GameMod StackToStorage    = Nexus("22",  "Stack to Storage and Chests",           "roguechikkin");
    private static readonly GameMod EnergyMultiplier  = Nexus("4",   "Energy Multiplier",                     "lockyaw");
    private static readonly GameMod EnergyPlus        = Nexus("35",  "EnergyPlus",                            "Kuriti0n");

    // -------------------------------------------------------------------------
    // The cases that used to fail
    // -------------------------------------------------------------------------

    [Fact]
    public void FolderNameMatchesThePageWhenTheDeclaredNameDoesNot()
    {
        // Plugin says "Bigger Stacks for Items"; the page is "BiggerStacks". The folder is the giveaway.
        GameMod mod = Installed("Bigger Stacks for Items", "BiggerStacks", "BiggerStacks");

        Assert.True(ModNameMatch.IsConfident(mod, BiggerStacks));
    }

    [Fact]
    public void FolderNameMatchesAPageTitledWithoutTheGamePrefix()
    {
        // Plugin says "Moonlight Time Control"; the page is "TimeControl".
        GameMod mod = Installed("Moonlight Time Control", "TimeControl", "com.moonlightpeaks.timecontrol");

        Assert.True(ModNameMatch.IsConfident(mod, TimeControl));
    }

    [Fact]
    public void PluginGuidTailMatchesAPageWithAShorterName()
    {
        // Plugin says "Moonlight Peaks Mod Menu"; the page is "Mod Menu". The GUID's tail is "modmenu".
        GameMod mod = Installed("Moonlight Peaks Mod Menu", "MoonlightPeaks.ModMenu", "elsia.modmenu");

        Assert.True(ModNameMatch.IsConfident(mod, ModMenu));
    }

    [Fact]
    public void PluginGuidTailMatchesAnUnderscoredPageName()
    {
        // Folder is "StorageAnywhere" and the plugin name carries a game prefix, but the GUID ends in the
        // page's exact name: "house_storage_anywhere" -> "House Storage Anywhere".
        GameMod mod = Installed(
            "Moonlight Peaks House Storage Anywhere", "StorageAnywhere",
            "com.lockyaw.moonlightpeaks.house_storage_anywhere");

        Assert.True(ModNameMatch.IsConfident(mod, HouseStorage));
    }

    [Fact]
    public void GuidAuthorBacksUpAPartialNameMatch()
    {
        // Plugin "Always Show Item Value" is contained in the page "Always Show Item Value (sell price)", which
        // on its own is not enough. The GUID's author segment matches the page's author, which makes it safe.
        GameMod mod = Installed(
            "Always Show Item Value", "ValueAlwaysVisible", "padme4000.moonlightpeaks.alwaysshowvalue");

        Assert.True(ModNameMatch.IsConfident(mod, ShowItemValue));
    }

    // -------------------------------------------------------------------------
    // The cases that already worked must keep working
    // -------------------------------------------------------------------------

    [Fact]
    public void ExactNameMatchesStillMatch()
    {
        GameMod save = Installed("Save Anywhere", "SaveAnywhere", "serena.moonlightpeaks.saveanywhere");
        GameMod energy = Installed("Infinite Energy", "InfiniteEnergy", "costmillion.moonlightpeaks.infiniteenergy");

        Assert.True(ModNameMatch.IsConfident(save, SaveAnywhere));
        Assert.True(ModNameMatch.IsConfident(energy, InfiniteEnergy));
    }

    // -------------------------------------------------------------------------
    // The bar stays high: wrong pages must still be refused
    // -------------------------------------------------------------------------

    [Fact]
    public void AModWhoseNameAndAuthorBothChangedIsNotGuessedAt()
    {
        // "Stack To Chest" by ohsal versus a page called "Stack to Storage and Chests" by roguechikkin. Nothing
        // corroborates that these are the same mod, so linking them would be a guess — and a wrong link makes
        // the manager report someone else's version and download an unrelated archive.
        GameMod mod = Installed("Stack To Chest", "StackToChest", "com.ohsal.moonlightpeaks.stacktochest");

        Assert.False(ModNameMatch.IsConfident(mod, StackToStorage));
    }

    [Fact]
    public void ADifferentModByTheSameAuthorIsNotMatched()
    {
        // lockyaw wrote both House Storage Anywhere and Energy Multiplier; a shared author must never be enough.
        GameMod mod = Installed(
            "Moonlight Peaks House Storage Anywhere", "StorageAnywhere",
            "com.lockyaw.moonlightpeaks.house_storage_anywhere");

        Assert.False(ModNameMatch.IsConfident(mod, EnergyMultiplier));
    }

    [Fact]
    public void ASimilarlyNamedModByAnotherAuthorIsNotMatched()
    {
        GameMod mod = Installed("Infinite Energy", "InfiniteEnergy", "costmillion.moonlightpeaks.infiniteenergy");

        Assert.False(ModNameMatch.IsConfident(mod, EnergyPlus));
        Assert.False(ModNameMatch.IsConfident(mod, EnergyMultiplier));
    }

    [Fact]
    public void TheGamesOwnNameInAGuidIsNeverTreatedAsTheModsName()
    {
        // Nearly every Moonlight Peaks GUID contains "moonlightpeaks". If that counted as a name, every mod
        // would match a page called "Moonlight Peaks" — and as an author, every mod would corroborate itself.
        GameMod mod = Installed("Some Mod", "SomeMod", "com.someone.moonlightpeaks.somemod");

        Assert.False(ModNameMatch.IsConfident(mod, Nexus("999", "Moonlight Peaks", "Someone Else")));
        Assert.DoesNotContain("moonlightpeaks",
            ModNameMatch.SearchAliases(mod).Select(ModNameMatch.Normalize));
    }

    [Fact]
    public void BepInExScaffoldingIsNeverTreatedAsTheModsName()
    {
        GameMod mod = Installed("Configuration Manager", "ConfigurationManager", "com.bepis.bepinex.configurationmanager");

        List<string> normalized = ModNameMatch.SearchAliases(mod).Select(ModNameMatch.Normalize).ToList();

        Assert.DoesNotContain("com", normalized);
        Assert.DoesNotContain("bepis", normalized);
        Assert.DoesNotContain("bepinex", normalized);
        // The real name is still there to search for.
        Assert.Contains("configurationmanager", normalized);
    }

    // -------------------------------------------------------------------------
    // Alias generation
    // -------------------------------------------------------------------------

    [Fact]
    public void AliasesCoverTheDeclaredNameTheFolderAndTheGuidTail()
    {
        GameMod mod = Installed("Bigger Stacks for Items", "BiggerStacks", "padme4000.moonlightpeaks.alwaysshowvalue");

        List<string> normalized = ModNameMatch.SearchAliases(mod).Select(ModNameMatch.Normalize).ToList();

        Assert.Contains("biggerstacksforitems", normalized);
        Assert.Contains("biggerstacks", normalized);
        Assert.Contains("alwaysshowvalue", normalized);
        Assert.Contains("padme4000", normalized);
    }

    [Fact]
    public void AliasesUseTheSpacedSpellingBecauseNexusSearchesByWord()
    {
        // Observed against the live API: a name search for "modmenu" returns nothing, while "Mod Menu" returns
        // the page. A plugin GUID only ever gives the run-together spelling, so the spaced form has to win or
        // the search never surfaces the candidate for the matcher to confirm.
        GameMod mod = Installed("Moonlight Peaks Mod Menu", "MoonlightPeaks.ModMenu", "elsia.modmenu");

        List<string> aliases = ModNameMatch.SearchAliases(mod);

        Assert.Contains("Mod Menu", aliases);
        Assert.DoesNotContain("modmenu", aliases);
    }

    [Fact]
    public void AliasesAreNotDuplicatedWhenTheNamesAgree()
    {
        // "Save Anywhere", folder "SaveAnywhere" and GUID tail "saveanywhere" are all one alias once normalized.
        GameMod mod = Installed("Save Anywhere", "SaveAnywhere", "serena.moonlightpeaks.saveanywhere");

        List<string> normalized = ModNameMatch.SearchAliases(mod).Select(ModNameMatch.Normalize).ToList();

        Assert.Equal(normalized.Count, normalized.Distinct().Count());
        Assert.Single(normalized, n => n == "saveanywhere");
    }

    [Fact]
    public void ADisabledModsLeadingDotIsNotPartOfItsName()
    {
        GameMod mod = new GameMod
        {
            Name = "Bigger Stacks for Items",
            FolderPath = @"D:\Games\Moonlight Peaks\BepInEx\plugins-disabled\.BiggerStacks",
            UniqueId = "BiggerStacks",
            Author = "Unknown"
        };

        Assert.True(ModNameMatch.IsConfident(mod, BiggerStacks));
    }

    [Theory]
    [InlineData("BiggerStacks", "Bigger Stacks")]
    [InlineData("house_storage_anywhere", "house storage anywhere")]
    [InlineData("MoonlightQuickSpells", "Moonlight Quick Spells")]
    [InlineData("Already Spaced", "Already Spaced")]
    [InlineData("Padme4000", "Padme4000")]
    public void HumanizeSplitsRunTogetherIdentifiers(string input, string expected)
    {
        Assert.Equal(expected, ModNameMatch.Humanize(input));
    }

    // -------------------------------------------------------------------------
    // Stardew's existing behaviour must be untouched
    // -------------------------------------------------------------------------

    [Fact]
    public void StardewContentPackTagsAreStillStripped()
    {
        GameMod mod = new GameMod
        {
            Name = "[CP] Stoned Valley",
            FolderPath = @"C:\Stardew\Mods\[CP] Stoned Valley",
            UniqueId = "someone.stonedvalley",
            Author = "Someone"
        };

        Assert.True(ModNameMatch.IsConfident(mod, Nexus("1", "Stoned Valley", "Someone")));
    }
}
