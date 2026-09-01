using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ModPartRules"/> against the real Files tab of the mod pages it knows about, recorded from
/// the Nexus API.
///
/// Recorded rather than invented, because every rule in the table exists to survive something no hand-written
/// fixture would have contained. SKSE's Steam and GOG builds have near-identical names and differ only in a
/// sentence of prose inside each file's description. The one the author flags as primary is the Steam build, so
/// "take the primary file" — which is what the manager did — is precisely how a GOG owner ended up with an SKSE
/// that silently refuses to load. Engine Fixes' preloader stopped calling itself "Part 2" at some point and is
/// now "Engine Fixes - SKSE64 Preloader". And "archived" is spelled as the literal category "ARCHIVED", not as
/// an empty one, so a check for empty lets all twelve of SKSE's retired files through.
/// </summary>
public class ModPartRulesTests
{
    private const string SkyrimSteamBuild = "1.6.1170";
    private const string SkyrimGogBuild   = "1.6.1179";

    private static List<NexusFileInfo> Fixture(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", name);
        Assert.True(File.Exists(path), "Missing recorded file list: " + path);
        var files = ModPartRules.ParseFilesJson(File.ReadAllText(path));
        Assert.NotEmpty(files);
        return files;
    }

    private static ModPart PartOf(string gameId, string modId, int index)
    {
        KnownMod? mod = ModPartRules.Find(gameId, modId);
        Assert.NotNull(mod);
        return mod!.Parts[index];
    }

    // -------------------------------------------------------------------------
    // SKSE — the Steam / GOG split that started all this
    // -------------------------------------------------------------------------

    [Fact]
    public void SkseGogCopyGetsTheGogBuildEvenThoughSteamsIsTheAuthorsPrimaryFile()
    {
        var files = Fixture("nexus-skse64-30379-files.json");
        ModPart part = PartOf(GameProfiles.SkyrimSE, "30379", 0);

        NexusFileInfo? picked = ModPartRules.PickFile(files, part, SkyrimGogBuild, GamePlatform.Gog);

        Assert.NotNull(picked);
        Assert.Contains("GOG", picked!.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(SkyrimGogBuild, picked.Description);
        // The proof that the build beat the primary flag, which is what the old selection went by.
        Assert.False(picked.IsPrimary);
    }

    [Fact]
    public void SkseSteamCopyGetsTheSteamBuild()
    {
        var files = Fixture("nexus-skse64-30379-files.json");
        ModPart part = PartOf(GameProfiles.SkyrimSE, "30379", 0);

        NexusFileInfo? picked = ModPartRules.PickFile(files, part, SkyrimSteamBuild, GamePlatform.Steam);

        Assert.NotNull(picked);
        Assert.Contains(SkyrimSteamBuild, picked!.Description);
        Assert.DoesNotContain("GOG.com", picked.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SkseFallsBackToThePlatformWhenNoFileNamesTheGamesBuild()
    {
        // The week after Bethesda patches, nothing on the page mentions the new build yet. The store is then the
        // best remaining evidence, and a GOG owner must still not be handed the Steam build.
        var files = Fixture("nexus-skse64-30379-files.json");
        ModPart part = PartOf(GameProfiles.SkyrimSE, "30379", 0);

        NexusFileInfo? picked = ModPartRules.PickFile(files, part, "1.6.9999", GamePlatform.Gog);

        Assert.NotNull(picked);
        Assert.Contains("GOG", picked!.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SkseNeverReturnsAFileNexusWouldRefuseToServe()
    {
        var files = Fixture("nexus-skse64-30379-files.json");
        ModPart part = PartOf(GameProfiles.SkyrimSE, "30379", 0);

        // The page really does carry both kinds of unusable file, which is why both are asserted on.
        Assert.Contains(files, f => string.Equals(f.CategoryName, "ARCHIVED", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, f => string.IsNullOrEmpty(f.CategoryName));

        foreach ((string? build, GamePlatform platform) in new (string?, GamePlatform)[]
                 {
                     (SkyrimSteamBuild, GamePlatform.Steam),
                     (SkyrimGogBuild,   GamePlatform.Gog),
                     (null,             GamePlatform.Unknown)
                 })
        {
            NexusFileInfo? picked = ModPartRules.PickFile(files, part, build, platform);
            Assert.NotNull(picked);
            Assert.NotEqual("ARCHIVED", picked!.CategoryName, StringComparer.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrEmpty(picked.CategoryName));
        }
    }

    [Fact]
    public void SkseNeverHandsAGogCopyTheSteamBuildEvenWhenTheBuildNumberSaysSo()
    {
        // The reported bug, reproduced. A build number is read off an exe, and on a machine with both copies of
        // Skyrim it can be read off the wrong one — which is what happens to a GOG session whose install key is
        // the bare game id. The Steam build then matches the build exactly and outscores everything.
        //
        // The store is why that no longer decides it: the Steam file is compiled against an exe this copy does
        // not have, so it is not a worse answer, it is not an answer.
        var files = Fixture("nexus-skse64-30379-files.json");
        ModPart part = PartOf(GameProfiles.SkyrimSE, "30379", 0);

        NexusFileInfo? picked = ModPartRules.PickFile(files, part, SkyrimSteamBuild, GamePlatform.Gog);

        Assert.NotNull(picked);
        Assert.Contains("GOG", picked!.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SkseNeverHandsASteamCopyTheGogBuildEitherWayRound()
    {
        var files = Fixture("nexus-skse64-30379-files.json");
        ModPart part = PartOf(GameProfiles.SkyrimSE, "30379", 0);

        // Same mistake mirrored: the GOG build number against a Steam copy.
        NexusFileInfo? picked = ModPartRules.PickFile(files, part, SkyrimGogBuild, GamePlatform.Steam);

        Assert.NotNull(picked);
        Assert.DoesNotContain("GOG", picked!.Name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GOG.com", picked.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SkseWithNoStoreRecordedStillPicksSomethingUsable()
    {
        // A copy carried over from a settings file written before stores were recorded. Nothing to filter on, so
        // the build match does the work it always did — it must not come back empty-handed.
        var files = Fixture("nexus-skse64-30379-files.json");
        ModPart part = PartOf(GameProfiles.SkyrimSE, "30379", 0);

        NexusFileInfo? picked = ModPartRules.PickFile(files, part, SkyrimSteamBuild, GamePlatform.Unknown);

        Assert.NotNull(picked);
        Assert.Contains(SkyrimSteamBuild, picked!.Description);
    }

    // -------------------------------------------------------------------------
    // SSE Engine Fixes — the two-part install newcomers fall through
    // -------------------------------------------------------------------------

    [Fact]
    public void EngineFixesPartOneIsThePluginAndPartTwoIsThePreloader()
    {
        var files = Fixture("nexus-enginefixes-17230-files.json");
        ModPart partOne = PartOf(GameProfiles.SkyrimSE, "17230", 0);
        ModPart partTwo = PartOf(GameProfiles.SkyrimSE, "17230", 1);

        NexusFileInfo? one = ModPartRules.PickFile(files, partOne, SkyrimSteamBuild, GamePlatform.Steam);
        NexusFileInfo? two = ModPartRules.PickFile(files, partTwo, SkyrimSteamBuild, GamePlatform.Steam);

        Assert.NotNull(one);
        Assert.NotNull(two);

        // Two different downloads is the whole point — resolving both to one file is the bug being prevented.
        Assert.NotEqual(one!.FileId, two!.FileId);

        Assert.DoesNotContain("preloader", one.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preloader", two.Name, StringComparison.OrdinalIgnoreCase);

        // Both halves come off the current MAIN pair, not a stale one left up for older game versions.
        Assert.Equal("MAIN", one.CategoryName);
        Assert.Equal("MAIN", two.CategoryName);
    }

    [Fact]
    public void EngineFixesPartTwoIsFoundEvenThoughItNoLongerSaysPartTwo()
    {
        // It was "(Part 2) Engine Fixes - skse64 Preloader and TBB" for years and is now just
        // "Engine Fixes - SKSE64 Preloader". Matching on the old name alone would quietly return an
        // OLD_VERSION file built for a game nobody is running any more.
        var files = Fixture("nexus-enginefixes-17230-files.json");
        ModPart partTwo = PartOf(GameProfiles.SkyrimSE, "17230", 1);

        NexusFileInfo? picked = ModPartRules.PickFile(files, partTwo, SkyrimSteamBuild, GamePlatform.Steam);

        Assert.NotNull(picked);
        Assert.DoesNotContain("part 2", picked!.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("MAIN", picked.CategoryName);
    }

    [Fact]
    public void EngineFixesIgnoresTheAllInOneBundle()
    {
        // The bundle is a fine way to install by hand, but it is the file the author flags as primary — so
        // without excluding it, "the main plugin" becomes the bundle and the preloader gets fetched twice.
        var files = Fixture("nexus-enginefixes-17230-files.json");
        Assert.Contains(files, f => f.IsPrimary && f.Name.Contains("All-In-One", StringComparison.OrdinalIgnoreCase));

        foreach (int index in new[] { 0, 1 })
        {
            NexusFileInfo? picked = ModPartRules.PickFile(
                files, PartOf(GameProfiles.SkyrimSE, "17230", index), SkyrimSteamBuild, GamePlatform.Steam);

            Assert.NotNull(picked);
            Assert.DoesNotContain("All-In-One", picked!.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    // -------------------------------------------------------------------------
    // F4SE — a page with no store split at all
    // -------------------------------------------------------------------------

    [Fact]
    public void F4seFollowsTheGameBuild()
    {
        var files = Fixture("nexus-f4se-42147-files.json");
        ModPart part = PartOf(GameProfiles.Fallout4, "42147", 0);

        // An older copy that has not taken the latest patch needs the F4SE built for the version it is on, not
        // the newest one — installing the newest is exactly how the extender stops loading after an update.
        NexusFileInfo? old = ModPartRules.PickFile(files, part, "1.10.163", GamePlatform.Steam);
        Assert.NotNull(old);
        Assert.Contains("1.10.163", old!.Description);

        NexusFileInfo? current = ModPartRules.PickFile(files, part, "1.11.221", GamePlatform.Steam);
        Assert.NotNull(current);
        Assert.Contains("1.11.221", current!.Description);
        Assert.NotEqual(old.FileId, current.FileId);
    }

    [Fact]
    public void AGogCopyOfAGameWhosePageOffersOneBuildStillGetsThatBuild()
    {
        // Fallout 4 is sold on GOG, and F4SE's page has never offered a GOG-specific file. Requiring one would
        // answer "no file found" and send the user to the Files tab — worse than the single build that has always
        // been correct for them. The store only disqualifies where the page actually splits by it.
        var files = Fixture("nexus-f4se-42147-files.json");
        Assert.DoesNotContain(files, f =>
            (f.Name + " " + f.FileName).Contains("gog", StringComparison.OrdinalIgnoreCase));

        ModPart part = PartOf(GameProfiles.Fallout4, "42147", 0);
        NexusFileInfo? picked = ModPartRules.PickFile(files, part, "1.11.221", GamePlatform.Gog);

        Assert.NotNull(picked);
        Assert.Contains("1.11.221", picked!.Description);
    }

    [Fact]
    public void AnUnreadableGameVersionStillYieldsACurrentFile()
    {
        // The exe can't always be read (a copy on a drive that isn't mounted, a locked file). Falling back to
        // the author's current main download is right; falling back to nothing, or to an archived file, is not.
        var files = Fixture("nexus-f4se-42147-files.json");
        ModPart part = PartOf(GameProfiles.Fallout4, "42147", 0);

        NexusFileInfo? picked = ModPartRules.PickFile(files, part, gameBuild: null);

        Assert.NotNull(picked);
        Assert.Equal("MAIN", picked!.CategoryName);
    }

    // -------------------------------------------------------------------------
    // Parts the game has outgrown
    // -------------------------------------------------------------------------

    [Fact]
    public void TheEngineFixesPreloaderIsNeededBelowTheSkseHandoverBuildAndNotAboveIt()
    {
        ModPart plugin    = PartOf(GameProfiles.SkyrimSE, "17230", 0);
        ModPart preloader = PartOf(GameProfiles.SkyrimSE, "17230", 1);

        // 1.5.97 and the whole 1.6 line still call the plugin through the proxy DLL's Initialize() entry point,
        // and the plugin closes the game outright when it finds it did not preload.
        Assert.True(ModPartRules.PartNeeded(preloader, "1.5.97"));
        Assert.True(ModPartRules.PartNeeded(preloader, SkyrimSteamBuild));
        Assert.True(ModPartRules.PartNeeded(preloader, SkyrimGogBuild));

        // From 1.7.99 SKSE preloads the plugin itself.
        Assert.False(ModPartRules.PartNeeded(preloader, "1.7.99"));
        Assert.False(ModPartRules.PartNeeded(preloader, "1.7.104"));

        // The plugin itself is needed on every build there has ever been.
        foreach (string build in new[] { "1.5.97", SkyrimSteamBuild, SkyrimGogBuild, "1.7.104" })
            Assert.True(ModPartRules.PartNeeded(plugin, build));
    }

    [Fact]
    public void AnUnreadableGameBuildLeavesEveryPartNeeded()
    {
        // Being wrong here has two very different prices. A part wrongly called for is a line in a report; a part
        // wrongly called obsolete is a player told to delete the file without which their game will not start.
        ModPart preloader = PartOf(GameProfiles.SkyrimSE, "17230", 1);

        foreach (string? build in new[] { null, "", "not a version" })
        {
            Assert.True(ModPartRules.PartNeeded(preloader, build));
            Assert.False(ModPartRules.PartSuperseded(preloader, build));
        }
    }

    [Fact]
    public void OnlyAPartThatHasBeenOutgrownCountsAsLeftOver()
    {
        ModPart plugin    = PartOf(GameProfiles.SkyrimSE, "17230", 0);
        ModPart preloader = PartOf(GameProfiles.SkyrimSE, "17230", 1);

        Assert.True(ModPartRules.PartSuperseded(preloader, "1.7.104"));
        Assert.False(ModPartRules.PartSuperseded(preloader, SkyrimSteamBuild));

        // A part with no handover build is never a leftover, whatever the game is running.
        Assert.False(ModPartRules.PartSuperseded(plugin, "1.7.104"));
    }

    [Fact]
    public void EveryPartThatCanBeOutgrownNamesTheFilesItLeavesBehind()
    {
        // Without them the finding could say a file is no longer used and then be unable to name, or remove, it.
        foreach (KnownMod mod in ModPartRules.All)
            foreach (ModPart part in mod.Parts.Where(p => !string.IsNullOrEmpty(p.SupersededFromGameBuild)))
            {
                Assert.Equal(PartDestination.GameRoot, part.Destination);
                Assert.NotEmpty(part.Files);
                Assert.Contains(part.Files, f => string.Equals(f, part.DetectFile, StringComparison.OrdinalIgnoreCase));
            }
    }

    // -------------------------------------------------------------------------
    // The table itself
    // -------------------------------------------------------------------------

    [Fact]
    public void EveryKnownModNamesARealGameAndHasDetectableParts()
    {
        Assert.NotEmpty(ModPartRules.All);

        foreach (KnownMod mod in ModPartRules.All)
        {
            Assert.NotNull(GameProfiles.Find(mod.GameId));
            Assert.NotEmpty(mod.Parts);

            foreach (ModPart part in mod.Parts)
            {
                // Without a way to tell whether a part is already there, the health check cannot report it
                // missing and the installer would fetch it again on every run.
                Assert.True(
                    !string.IsNullOrEmpty(part.DetectFile) || !string.IsNullOrEmpty(part.DetectModName),
                    $"{mod.DisplayName} / {part.Name} has no way to detect whether it is installed.");
            }
        }
    }

    [Fact]
    public void FindMatchesEitherCopyOfAGame()
    {
        // The lookup is given the session's install key, which for a second copy is "SkyrimSE@Gog".
        Assert.NotNull(ModPartRules.Find("SkyrimSE@Gog", "30379"));
        Assert.NotNull(ModPartRules.Find(GameProfiles.SkyrimSE, "30379"));

        // A page belonging to another game must never match, whatever the key.
        Assert.Null(ModPartRules.Find(GameProfiles.Fallout4, "30379"));
        Assert.Null(ModPartRules.Find("SkyrimSE@Gog", "999999"));
    }

    [Fact]
    public void TheFilePageUrlPointsAtOneFileRatherThanTheWholeFilesTab()
    {
        // What a free account is sent to. The whole value is that there is nothing left to choose.
        Assert.Equal(
            "https://www.nexusmods.com/skyrimspecialedition/mods/30379?tab=files&file_id=470991",
            ModPartRules.FilePageUrl("skyrimspecialedition", "30379", "470991"));
    }

    [Fact]
    public void AnEmptyOrBrokenFileListIsAnsweredWithNothingRatherThanAThrow()
    {
        ModPart part = PartOf(GameProfiles.SkyrimSE, "30379", 0);

        Assert.Empty(ModPartRules.ParseFilesJson(""));
        Assert.Empty(ModPartRules.ParseFilesJson("not json at all"));
        Assert.Empty(ModPartRules.ParseFilesJson("{\"files\":[]}"));
        Assert.Null(ModPartRules.PickFile(new List<NexusFileInfo>(), part, SkyrimSteamBuild, GamePlatform.Steam));
    }
}
