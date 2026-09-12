using System.Collections.Generic;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ModFolderTidy"/>: deciding what an untidy mod folder should be called.
///
/// <para>
/// The folder names here are real ones, out of a Skyrim mods folder where 23 of 30 folders were still named after
/// the download they arrived in. Renaming a folder is the one operation in this manager that a user cannot easily
/// undo by hand across twenty mods, so the rules are pinned here rather than trusted: what gets left alone matters
/// as much as what gets renamed.
/// </para>
/// </summary>
public class ModFolderTidyTests
{
    private static ModFolderTidy.ModFolder Folder(string folder, string display = "") => new(folder, display);

    private static (List<FolderTidyRename> Renames, List<FolderTidySkip> Skips) Plan(
        IEnumerable<ModFolderTidy.ModFolder> folders, string prefix = ".", bool witcher = false) =>
        ModFolderTidy.Plan(folders, prefix, witcher);

    [Fact]
    public void AFolderIsNamedAfterTheModInIt()
    {
        // The name the manager already shows for the mod, so the folder in Explorer reads the same as the row in
        // the list. This is Mod Organizer 2's convention, and the reason the manager writes a manifest beside the
        // mod at all: the machinery lives there, and the folder is free to be readable.
        var (renames, _) = Plan(new[]
        {
            Folder("Hunterborn-7900-1-6-2", "Hunterborn SE"),
            Folder("Media Keys Fix 92948 1.0.2 2026-08-21T18-58Z Yj6wQRo26", "Media Keys Fix SKSE"),
        });

        Assert.Equal(new[] { "Hunterborn SE", "Media Keys Fix SKSE" }, renames.Select(r => r.To));
    }

    [Fact]
    public void AFolderNobodyKnowsTheNameOfFallsBackToItsOwnCleanedName()
    {
        var (renames, _) = Plan(new[] { Folder("Carry Weight Modifiers-2176-1-1-3") });

        Assert.Equal("Carry Weight Modifiers", Assert.Single(renames).To);
    }

    [Fact]
    public void AFolderWithNothingWrongWithItIsLeftCompletelyAlone()
    {
        // Most folders, and every Stardew one: those arrive named by the mod's own author. A tool that rewrites
        // 148 tidy folder names to fix 23 untidy ones is a worse tool.
        var (renames, skips) = Plan(new[]
        {
            Folder("ContentPatcher", "Content Patcher"),
            Folder("SMAPI 4-0-2", "SMAPI"),
            Folder("Half-Life 2", "Half-Life 2"),
        });

        Assert.Empty(renames);
        Assert.Empty(skips);
    }

    [Fact]
    public void ADisabledModStaysDisabled()
    {
        // The leading dot is what makes a mod switched off. Renaming a folder and dropping it would silently turn
        // the mod back on — which for a mod the user disabled on purpose is worse than any untidy name.
        var (renames, _) = Plan(new[]
        {
            Folder(".Alternate Start - Live Another Life-272-4-2-6a-1772996477", "Alternate Start - Live Another Life"),
        });

        Assert.Equal(".Alternate Start - Live Another Life", Assert.Single(renames).To);
    }

    [Fact]
    public void TwoModsCannotBeGivenTheSameFolder()
    {
        // Renaming the second onto the first would merge two mods into one folder, which is data loss, not an
        // untidy name. Numbering it is what a person would do.
        var (renames, _) = Plan(new[]
        {
            Folder("SkyUI-12604-5-2-1533670703", "SkyUI"),
            Folder("SkyUI SE-12604-5-2SE-1533670799", "SkyUI"),
        });

        Assert.Equal(new[] { "SkyUI", "SkyUI (2)" }, renames.Select(r => r.To));
    }

    [Fact]
    public void ATidyNameIsNotClaimedFromAFolderThatAlreadyHasIt()
    {
        // The untidy folder wants to be called "SkyUI", and a different mod already is. The existing folder is
        // never touched, so the newcomer takes the number.
        var (renames, _) = Plan(new[]
        {
            Folder("SkyUI", "SkyUI"),
            Folder("SkyUI-12604-5-2-1533670703", "SkyUI"),
        });

        FolderTidyRename only = Assert.Single(renames);
        Assert.Equal("SkyUI-12604-5-2-1533670703", only.From);
        Assert.Equal("SkyUI (2)", only.To);
    }

    [Fact]
    public void ANameWindowsWouldRefuseIsMadeIntoOneItAccepts()
    {
        var (renames, _) = Plan(new[] { Folder("Mod-1234-1-0", "Skyrim: Reloaded? <best>") });

        Assert.Equal("Skyrim Reloaded best", Assert.Single(renames).To);
    }

    [Fact]
    public void AVeryLongModNameIsCutSoThePathsInsideItStillFit()
    {
        string huge = new string('x', 200);
        var (renames, _) = Plan(new[] { Folder("Mod-1234-1-0", huge) });

        Assert.True(Assert.Single(renames).To.Length <= 90,
            "A mod folder is only the start of a path that carries on through meshes, textures and scripts.");
    }

    /// <summary>
    /// The Witcher 3 is the one game where a folder name is an instruction to the engine rather than a label: it
    /// loads <c>mod*</c> and silently ignores everything else. A prettier name that stops the mod loading is not
    /// a tidier one.
    /// </summary>
    public class Witcher3
    {
        [Fact]
        public void TheModPrefixTheEngineNeedsSurvives()
        {
            var (renames, _) = ModFolderTidy.Plan(
                new[] { new ModFolderTidy.ModFolder("modSprintForever-1576-1-0", "Sprint Forever") },
                "~", isWitcher: true);

            Assert.Equal("modSprintForever", Assert.Single(renames).To);
        }

        [Fact]
        public void TheDisplayNameIsNeverUsedBecauseItIsProse()
        {
            // "Sprint Forever" as a folder is two words and no prefix — the engine would not load it. The folder's
            // own cleaned name is the one that keeps working.
            var (renames, _) = ModFolderTidy.Plan(
                new[] { new ModFolderTidy.ModFolder("BrothersInArms-11260-3-1-2-1772472350", "Brothers In Arms") },
                "~", isWitcher: true);

            string to = Assert.Single(renames).To;
            Assert.Equal("modBrothersInArms", to);
            Assert.DoesNotContain(" ", to);
        }

        [Fact]
        public void ADisabledWitcherModKeepsItsTilde()
        {
            var (renames, _) = ModFolderTidy.Plan(
                new[] { new ModFolderTidy.ModFolder("~modSprintForever-1576-1-0", "Sprint Forever") },
                "~", isWitcher: true);

            Assert.Equal("~modSprintForever", Assert.Single(renames).To);
        }

        [Fact]
        public void ANumberedDuplicateStaysEngineSafe()
        {
            var (renames, _) = ModFolderTidy.Plan(new[]
            {
                new ModFolderTidy.ModFolder("modSprint-1576-1-0", "Sprint"),
                new ModFolderTidy.ModFolder("modSprint-1577-1-0", "Sprint"),
            }, "~", isWitcher: true);

            Assert.Equal(new[] { "modSprint", "modSprint2" }, renames.Select(r => r.To));
        }
    }
}
