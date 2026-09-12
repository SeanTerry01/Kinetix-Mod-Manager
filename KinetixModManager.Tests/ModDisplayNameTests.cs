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

    /// <summary>
    /// Reading the Nexus mod id back out of a download's file name, which is how a search result finds out whether
    /// the user already has the mod sitting in their downloads folder.
    ///
    /// <para>
    /// Every name below is a real one, taken from a downloads folder five games deep. That matters: the shapes
    /// Nexus has used over the years are not documented anywhere, and the awkward ones — an author's own dashed
    /// version in front of the id, a single-digit mod id, a version piece like "6a" — were all found by running
    /// this over a real folder rather than imagined.
    /// </para>
    ///
    /// <para>
    /// A wrong answer here is worse than no answer: it would tell somebody they already have a mod they have
    /// never downloaded, and send them looking for a file that is not on their disk. So every rule is positional,
    /// and anything that does not clearly say an id says nothing at all.
    /// </para>
    /// </summary>
    public class ModIdFromArchiveName
    {
        [Theory]
        // The long-standing shape: name, mod id, version with dots as dashes, upload timestamp.
        [InlineData("Address Library - All In One-47327-1-11-221-1780112703.zip", "47327")]
        [InlineData("Alternate Start - Live Another Life-272-4-2-6a-1772996477.7z", "272")]
        [InlineData("Achievements Mods Enabler SE-AE-245-1-41-1715217907.zip", "245")]
        [InlineData("Audio Overhaul for Skyrim (4.1.3)-12466-4-1-3-1683940246.7z", "12466")]
        // A version tail carrying words, not just numbers.
        [InlineData("StardewUI Continued-43861-0-6-3-unofficial-mushymato-1-1775132314.zip", "43861")]
        // Older downloads have no upload timestamp at all.
        [InlineData("Easy Hacking-266-1-0.zip", "266")]
        [InlineData("Better MessageBox Controls v1_2-1428-1-2.zip", "1428")]
        // Newer, space-separated and dated: name, mod id, version, upload date, per-download hash.
        [InlineData("Address Library All in One (1.7.104.0) v13 32444 13 2026-08-27T15-29Z Ae46W7Fw2.zip", "32444")]
        [InlineData("MoonlightAccess (Full) 137 1.2.0 2026-08-31T23-00Z YKu8YV3b5.zip", "137")]
        // Single and double digit ids are real — a young game's mods are numbered from 1.
        [InlineData("HouseStorageAnywhere 7 2 2026-08-22T02-14Z ifoLiAHsd.zip", "7")]
        [InlineData("Save Anywhere 11 2.0.1 2026-07-14T01-43Z qrMmqPElu.zip", "11")]
        [InlineData("Over 9000 - Weight limit mod v1.31-3-1-31.zip", "3")]
        [InlineData("CJB Cheats Menu 1.42.0-4-1-42-0-1776647245.zip", "4")]
        // One named file off a mod's Files tab.
        [InlineData("Fallout 4 Script Extender (F4SE)_42147_file_407709.zip", "42147")]
        [InlineData("Always Show Item Value (sell price)_27_file_380.zip", "27")]
        public void ReadsTheModIdOutOfADownloadName(string archive, string expected)
        {
            Assert.Equal(expected, KinetixModManager.ModDisplayName.ModIdFromArchiveName(archive));
        }

        [Fact]
        public void AnAuthorsOwnDashedVersionIsNotMistakenForTheModId()
        {
            // The one that broke the first attempt at this, found in a real folder: the file name already carried
            // "v1-2-0" before Nexus appended anything, so the first number after the name is a 2. The mod is
            // 17561, and saying 2 would have claimed a completely unrelated mod as downloaded.
            Assert.Equal("17561", KinetixModManager.ModDisplayName.ModIdFromArchiveName("UIExtensions v1-2-0-17561-1-2-0.7z"));
        }

        [Theory]
        // An opaque content-server id: no name in it, and no id either.
        [InlineData("99824770-6ed9-4868-9f98-b54fb58ecad6")]
        [InlineData("0317c328-81e4-4db2-8a13-333cac300d23")]
        // Nothing Nexus named: a GitHub release, a hand-placed archive, the manager's own installer.
        [InlineData("mushymato.LivestockBazaar_github_latest.zip")]
        [InlineData("Dao - Custom NPC for Sunberry Village.zip")]
        [InlineData("KinetixModManager_Setup.exe")]
        [InlineData("SkyrimAccessibility_Install.zip")]
        // Underscore-separated names that carry a version but never an id.
        [InlineData("Cape_Stardew_1.6_7.1.37_GpzvK78er.zip")]
        [InlineData("FullInventoryView_1.7.4_1.7.4_Zq5fWPdJW.zip")]
        // A name that is only a number says nothing: there is no name in front of it to end.
        [InlineData("2077-1-0.zip")]
        // A name that merely ends in a number is a name, not an id with a tail.
        [InlineData("Half-Life 2.zip")]
        [InlineData("Fallout-4.zip")]
        [InlineData("")]
        [InlineData(null)]
        public void SaysNothingWhenTheNameDoesNotCarryAnId(string? archive)
        {
            Assert.Null(KinetixModManager.ModDisplayName.ModIdFromArchiveName(archive));
        }
    }

    /// <summary>
    /// The shapes <see cref="ModDisplayName.Clean"/> used to leave whole, all of them found sitting in a real
    /// downloads folder or, worse, in an installed mod's folder name.
    ///
    /// <para>
    /// The cause was one assumption: that Nexus's tail could be recognised by its own shape, which meant a mod id
    /// of at least three digits and an upload timestamp on the end. Neither holds. A young game's mods are
    /// numbered from 1 — Moonlight Peaks' are 7, 11 and 33 — and older downloads carry no timestamp at all. Clean
    /// now cuts the name at the mod id instead, found by the same rules that read the id out for a search result,
    /// so the two cannot disagree about where a name ends.
    /// </para>
    /// </summary>
    public class NamesThatUsedToKeepTheirTail
    {
        [Theory]
        // A mod id under 100, which the old three-digit floor could not see. Every one of these was a folder name.
        [InlineData("HouseStorageAnywhere 7 2 2026-08-22T02-14Z ifoLiAHsd.zip", "HouseStorageAnywhere")]
        [InlineData("Save Anywhere 11 2.0.1 2026-07-14T01-43Z qrMmqPElu.zip", "Save Anywhere")]
        [InlineData("TimeControl 85 1.1 2026-08-04T12-26Z Aq51Aavah.zip", "TimeControl")]
        [InlineData("Over 9000 - Weight limit mod v1.31-3-1-31.zip", "Over 9000 - Weight limit mod v1.31")]
        // No upload timestamp on the end — an older download, which the old pattern required.
        [InlineData("Easy Hacking-266-1-0.zip", "Easy Hacking")]
        [InlineData("Carry Weight Modifiers-2176-1-1-3.rar", "Carry Weight Modifiers")]
        [InlineData("Hunterborn-7900-1-6-2.7z", "Hunterborn")]
        // One named file off the Files tab, a shape neither old pattern knew.
        [InlineData("Skyrim Access_181131_file_772480.zip", "Skyrim Access")]
        [InlineData("Fallout 4 Script Extender (F4SE)_42147_file_407709.zip", "Fallout 4 Script Extender (F4SE)")]
        [InlineData("Enchanted Gramophone - Your music in game_125_file_332.zip", "Enchanted Gramophone - Your music in game")]
        // The author's own dashed version in front of Nexus's: the mod is 17561, so the name ends before it.
        [InlineData("UIExtensions v1-2-0-17561-1-2-0.7z", "UIExtensions v1-2-0")]
        public void TheNameEndsWhereTheModIdBegins(string archive, string expected)
        {
            Assert.Equal(expected, ModDisplayName.Clean(archive));
        }

        [Theory]
        // A version piece is not a mod id. Nexus numbers mods from 1 and never pads, so a part that is "0" or
        // starts with one is the version — without this, a folder called "SMAPI 4-0-2" loses half its name.
        [InlineData("SMAPI 4-0-2")]
        [InlineData("Some Mod-0-1-2")]
        [InlineData("Some Mod-007-1-2")]
        // A name that merely ends in a number is a name: there is no tail behind it to cut.
        [InlineData("Half-Life 2")]
        [InlineData("Fallout-4")]
        // Nothing Nexus named at all.
        [InlineData("Dao - Custom NPC for Sunberry Village")]
        [InlineData("mushymato.LivestockBazaar_github_latest")]
        public void ANameWithNoModIdInItIsLeftExactlyAsItIs(string name)
        {
            Assert.Equal(name, ModDisplayName.Clean(name));
        }

        [Fact]
        public void AKnownModIdSettlesAShapeThatCannotBeReadOnItsOwn()
        {
            // Nexus sometimes drops the date from the newer shape, leaving "<name> <id> <version> <hash>" — which
            // cannot be told from a name ending in numbers by looking at it. When the download flow already knows
            // the id, that settles it.
            Assert.Equal("Cape Stardew 1.6",
                ModDisplayName.Clean("Cape Stardew 1.6 14635 7.2.1 Ukcm5dRM7.zip", "14635"));

            // And a mod whose own name contains its id keeps the name: the machinery is always on the end, so the
            // last appearance is the one that counts.
            Assert.Equal("Mod 2 Deluxe", ModDisplayName.Clean("Mod 2 Deluxe-2-1-0.zip", "2"));
        }
    }
}
