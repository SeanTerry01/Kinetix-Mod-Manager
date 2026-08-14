using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ModDisplayName"/>: the rules that turn what a download is called into what the mod is called.
///
/// Reported by ear — "it says 99824770-6ed9-4868-9f98-b54fb58ecad6 is for Moonlight Peaks" — after a content server
/// handed back an opaque id instead of a file name, and separately for the ordinary Nexus naming, where the mod id,
/// version and upload timestamp on the end of every download were being read out as part of the mod's name and used
/// to name the folder it installed into.
/// </summary>
public class ModDisplayNameTests
{
    [Theory]
    // The ordinary Nexus shape: name, mod id, version with dots as dashes, upload timestamp.
    [InlineData("Skyrim Access-181131-1-2-3-1723456789.7z", "Skyrim Access")]
    [InlineData("SkyUI_5_2_SE-12604-5-2SE-1533670703.7z", "SkyUI_5_2_SE")]
    [InlineData("Address Library-32444-1-0-0-1664993166.zip", "Address Library")]
    // No version between the id and the timestamp.
    [InlineData("Engine Fixes-17230-1699999999.7z", "Engine Fixes")]
    // Already clean names are left exactly as they are.
    [InlineData("MyLocalMod.zip", "MyLocalMod")]
    [InlineData("Some Mod v1.2", "Some Mod v1.2")]
    [InlineData("Unofficial Patch 4.2.9b", "Unofficial Patch 4.2.9b")]
    public void ReadsTheModsNameOutOfADownloadName(string archive, string expected)
    {
        Assert.Equal(expected, ModDisplayName.Clean(archive));
    }

    [Theory]
    // Folder names taken verbatim from a real Fallout 4 install — the ones that prompted the report. A mod whose
    // own name ends in a version ("Mod Configuration Menu 1.11.221") is the case worth keeping honest: only the
    // machinery Nexus appends comes off, and the author's own numbering stays.
    [InlineData("Address Library - All In One-47327-1-11-221-1780112703", "Address Library - All In One")]
    [InlineData("Buffout 4 - Anniversary Edition-99911-1-7-1-1770487667", "Buffout 4 - Anniversary Edition")]
    [InlineData("Extended Dialogue Interface 1.11.221-27216-1-11-221-1780909377", "Extended Dialogue Interface 1.11.221")]
    [InlineData("Mod Configuration Menu 1.11.221-21497-1-11-221-1780909090", "Mod Configuration Menu 1.11.221")]
    [InlineData("Easy Hacking", "Easy Hacking")]
    [InlineData("Easy Lockpicking", "Easy Lockpicking")]
    [InlineData("fallout 4 access", "fallout 4 access")]
    public void ReadsRealInstalledFolderNames(string folder, string expected)
    {
        Assert.Equal(expected, ModDisplayName.Clean(folder));
    }

    [Fact]
    public void KeepsNumbersThatArePartOfTheModsOwnName()
    {
        // The tail is anchored to the end, so a name that simply contains numbers is not truncated.
        Assert.Equal("Fallout 4 Script Extender", ModDisplayName.Clean("Fallout 4 Script Extender"));
        Assert.Equal("2K Textures", ModDisplayName.Clean("2K Textures"));
        Assert.Equal("Mod 2024 Edition", ModDisplayName.Clean("Mod 2024 Edition.zip"));
    }

    [Fact]
    public void SplitsExactlyWhenTheModIdIsKnown()
    {
        // With the id in hand there is no guessing: everything before "-<id>-" is the name, whatever follows.
        Assert.Equal("Cool Mod", ModDisplayName.Clean("Cool Mod-9480-1-0-unusual-tail", "9480"));
    }

    [Theory]
    [InlineData("99824770-6ed9-4868-9f98-b54fb58ecad6")]
    [InlineData("99824770-6ed9-4868-9f98-b54fb58ecad6.zip")]
    [InlineData("d41d8cd98f00b204e9800998ecf8427e")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ReportsNoNameWhenTheDownloadCarriesNone(string? archive)
    {
        // An empty answer is the signal to go and ask Nexus what the mod is called; guessing from an id would put
        // that id in front of the user, which is the bug this was reported as.
        Assert.Equal("", ModDisplayName.Clean(archive));
    }

    [Theory]
    [InlineData("99824770-6ed9-4868-9f98-b54fb58ecad6", true)]
    [InlineData("deadbeefdeadbeef", true)]
    [InlineData("Skyrim Access", false)]
    [InlineData("SKSE64", false)]
    [InlineData("abc123", false)]
    public void KnowsAnIdFromAName(string name, bool opaque)
    {
        Assert.Equal(opaque, ModDisplayName.IsOpaque(name));
    }

    [Fact]
    public void FallsBackToTheNameNexusGivesTheMod()
    {
        // What the cross-game download prompt does: the file name said nothing, so the mod page's own name is used.
        Assert.Equal("Moonlight Access",
            ModDisplayName.ForSpeech("99824770-6ed9-4868-9f98-b54fb58ecad6", null, "Moonlight Access"));
    }

    [Fact]
    public void PrefersTheDownloadsOwnNameWhenItHasOne()
    {
        // A mod page can hold several files; the file's own name is the more specific answer where there is one.
        Assert.Equal("Skyrim Access",
            ModDisplayName.ForSpeech("Skyrim Access-181131-1-2-3-1723456789.7z", "181131", "Skyrim Access Suite"));
    }

    [Fact]
    public void SaysSomethingEvenWithNothingToGoOn()
    {
        // Never silence, and never an empty announcement: the raw name is the last resort.
        Assert.Equal("99824770-6ed9-4868-9f98-b54fb58ecad6",
            ModDisplayName.ForSpeech("99824770-6ed9-4868-9f98-b54fb58ecad6", null, null));
    }
}
