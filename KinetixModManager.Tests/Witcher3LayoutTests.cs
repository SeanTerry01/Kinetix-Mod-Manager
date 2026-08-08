using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="Witcher3Layout"/>, which decides what in a downloaded archive is a mod.
///
/// The Witcher 3 loads a folder under <c>mods</c> only when its name starts with "mod" and is otherwise silent,
/// so every mistake here produces the same symptom: the mod installs, appears in the list, and does nothing.
/// The case that bit a real install is the <c>mods</c> wrapper — it starts with "mod", so a plain prefix test
/// takes the wrapper for the mod and the real one lands a level too deep at <c>mods\mods\modFoo</c>.
/// </summary>
public class Witcher3LayoutTests
{
    private static string P(params string[] parts) => string.Join(Path.DirectorySeparatorChar, parts);

    [Fact]
    public void AModsWrapperIsSteppedThroughRatherThanInstalled()
    {
        // How the FMC Audio Remaster archive is laid out, and how it got installed to mods\mods\modFMCAudio.
        var dirs = new List<string>
        {
            P("t", "mods"),
            P("t", "mods", "modFMCAudioRemaster"),
            P("t", "mods", "modFMCAudioRemaster", "content")
        };

        List<string> picked = Witcher3Layout.SelectModFolders(dirs);

        Assert.Equal(new[] { P("t", "mods", "modFMCAudioRemaster") }, picked);
    }

    [Fact]
    public void SeveralModsInOneWrapperAllComeThrough()
    {
        // Random Encounters Reworked ships its shared-utils companions alongside it; all are real mod folders.
        var dirs = new List<string>
        {
            P("t", "mods"),
            P("t", "mods", "modRandomEncountersReworked"),
            P("t", "mods", "modRandomEncountersReworked", "content"),
            P("t", "mods", "modSharedImports"),
            P("t", "mods", "mod_sharedutils_glossary")
        };

        List<string> picked = Witcher3Layout.SelectModFolders(dirs);

        Assert.Equal(3, picked.Count);
        Assert.DoesNotContain(P("t", "mods"), picked);
        Assert.Contains(P("t", "mods", "modRandomEncountersReworked"), picked);
        Assert.Contains(P("t", "mods", "mod_sharedutils_glossary"), picked);
    }

    [Fact]
    public void AModsFolderInsideAModIsNotASecondMod()
    {
        // A mod's own contents are its business; only the mod folder itself is installed.
        var dirs = new List<string>
        {
            P("t", "modFoo"),
            P("t", "modFoo", "content"),
            P("t", "modFoo", "content", "mods"),
            P("t", "modFoo", "content", "modNested")
        };

        Assert.Equal(new[] { P("t", "modFoo") }, Witcher3Layout.SelectModFolders(dirs));
    }

    [Fact]
    public void FoldersTheEngineWouldIgnoreAreNotMods()
    {
        var dirs = new List<string>
        {
            P("t", "readme"),
            P("t", "dlc"),
            P("t", "bin"),
            P("t", "Optional Files")
        };

        Assert.Empty(Witcher3Layout.SelectModFolders(dirs));
    }

    [Fact]
    public void ShallowestFoldersAreChosenFirstWhateverOrderTheyArrive()
    {
        // Directory enumeration order is not guaranteed; a child arriving before its parent must not become the
        // chosen mod, or the install nests a level too deep exactly as the wrapper bug did.
        var dirs = new List<string>
        {
            P("t", "modFoo", "content", "modInner"),
            P("t", "modFoo")
        };

        Assert.Equal(new[] { P("t", "modFoo") }, Witcher3Layout.SelectModFolders(dirs));
    }

    // -------------------------------------------------------------------------
    // Reading a folder name aloud
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("modRandomEncountersReworked", "Random Encounters Reworked")]
    [InlineData("modWitcherAccess",            "Witcher Access")]
    [InlineData("modFMCAudioRemaster",         "FMC Audio Remaster")]
    [InlineData("mod_sharedutils_glossary",    "Shared Utils: Glossary")]
    [InlineData("mod_sharedutils_npcInteraction", "Shared Utils: Npc Interaction")]
    [InlineData("modSharedImports",            "Shared Imports")]
    [InlineData("~modWitcherAccess",           "Witcher Access")]   // the disabled marker is not part of the name
    public void FolderNamesAreMadeReadable(string folder, string expected)
    {
        Assert.Equal(expected, Witcher3Layout.DisplayNameFromFolder(folder));
    }

    [Fact]
    public void AFolderNameWithNothingLeftAfterThePrefixStillGetsAName()
    {
        // Never return an empty name — a nameless row is worse than an ugly one — and never throw on the
        // degenerate folder names that the prefix rules can reduce to nothing.
        Assert.Equal("Mod",  Witcher3Layout.DisplayNameFromFolder("mod"));
        Assert.Equal("mod_", Witcher3Layout.DisplayNameFromFolder("mod_"));
        Assert.Equal("~",    Witcher3Layout.DisplayNameFromFolder("~"));
        Assert.Equal("",     Witcher3Layout.DisplayNameFromFolder(""));
    }

    // -------------------------------------------------------------------------
    // Grouping a framework's pieces
    // -------------------------------------------------------------------------

    [Fact]
    public void FrameworkPiecesShareAFamilyAndStandaloneModsDoNot()
    {
        // Sean's install: eight mod_sharedutils_* folders that are one framework, filling eight rows.
        Assert.Equal("Shared Utils", Witcher3Layout.FamilyKey("mod_sharedutils_glossary"));
        Assert.Equal("Shared Utils", Witcher3Layout.FamilyKey("mod_sharedutils_mappins"));
        Assert.Equal("Shared Utils", Witcher3Layout.FamilyKey("~mod_sharedutils_helpers"));

        // A camelCase name runs words together for readability, not to claim kinship — grouping on it would
        // put unrelated mods together purely because they start with the same word.
        Assert.Null(Witcher3Layout.FamilyKey("modRandomEncountersReworked"));
        Assert.Null(Witcher3Layout.FamilyKey("modSharedImports"));
        Assert.Null(Witcher3Layout.FamilyKey("mod_onlyonepart"));
        Assert.Null(Witcher3Layout.FamilyKey("mods"));
        Assert.Null(Witcher3Layout.FamilyKey("somethingelse"));
    }

    [Theory]
    [InlineData("mods", false)]
    [InlineData("Mods", false)]
    [InlineData("MODS", false)]
    [InlineData("modFoo", true)]
    [InlineData("mod_sharedutils_glossary", true)]
    [InlineData("modsomething", true)]   // starts with "mod" and is not exactly "mods"
    [InlineData("content", false)]
    [InlineData("", false)]
    public void ModFolderNamesAreRecognisedCaseInsensitively(string name, bool expected)
    {
        Assert.Equal(expected, Witcher3Layout.IsModFolderName(name));
    }
}
