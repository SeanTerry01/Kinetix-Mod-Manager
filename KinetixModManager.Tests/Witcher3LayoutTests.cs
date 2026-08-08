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
