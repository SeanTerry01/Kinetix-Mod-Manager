using KinetixModManager;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers the manifest-reading helpers in <see cref="ModFileSystem"/>: reading fields case-insensitively
/// (mod manifests are hand-written and their casing varies — SMAPI's own Console Commands spells it
/// "UniqueId" — and SMAPI reads them case-insensitively), and telling an absent update key apart from one
/// that is present but blank.
/// </summary>
public class ManifestFieldTests
{
    [Theory]
    [InlineData("UniqueID")]
    [InlineData("UniqueId")]
    [InlineData("uniqueid")]
    [InlineData("UNIQUEID")]
    public void ManifestField_FindsAFieldWhateverItsCasing(string spelling)
    {
        var manifest = JObject.Parse($"{{ \"{spelling}\": \"SMAPI.ConsoleCommands\" }}");

        Assert.Equal("SMAPI.ConsoleCommands", (string?)ModManifest.Field(manifest, "UniqueID"));
    }

    [Fact]
    public void ManifestField_ReturnsNullWhenTheFieldIsAbsent()
    {
        Assert.Null(ModManifest.Field(JObject.Parse("{ \"Name\": \"A Mod\" }"), "UniqueID"));
    }

    [Theory]
    [InlineData("[\"\"]")]           // an empty entry
    [InlineData("[\"Nexus: \"]")]    // a source with no id after the colon
    [InlineData("[\"   \"]")]
    [InlineData("\"\"")]             // a bare string rather than an array
    public void HasUnusableUpdateKey_IsTrueForKeysThatNameNoModPage(string json)
    {
        Assert.True(ModManifest.HasUnusableUpdateKey(JToken.Parse(json)));
    }

    [Theory]
    [InlineData("[\"Nexus:1915\"]")]
    [InlineData("[\"GitHub:Pathoschild/StardewMods\"]")]
    [InlineData("[\"\", \"Nexus:1915\"]")]   // one good key among blanks is still usable
    public void HasUnusableUpdateKey_IsFalseWhenAtLeastOneKeyIsUsable(string json)
    {
        Assert.False(ModManifest.HasUnusableUpdateKey(JToken.Parse(json)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    public void HasUnusableUpdateKey_IsFalseWhenNoKeysAreDeclaredAtAll(string? json)
    {
        // An absent or empty UpdateKeys field is "no key", not a malformed one — the report words them
        // differently, so they must not be conflated.
        Assert.False(ModManifest.HasUnusableUpdateKey(json == null ? null : JToken.Parse(json)));
    }

    [Theory]
    [InlineData("Granny's Recipe Box-23737-1-0-2-1715181269.zip", "23737")]
    [InlineData("Cape Stardew 1.6-14635-7-1-12-1775991448.zip", "14635")]
    [InlineData("SomeMod-4242-1-0.7z", "4242")]
    public void ParseNexusIdFromFileName_ReadsTheModIdNexusPutsInTheName(string fileName, string expected)
    {
        Assert.Equal(expected, ModManifest.ParseNexusIdFromFileName(fileName));
    }

    [Theory]
    [InlineData("Cape_Stardew_1.6_7.1.37_GpzvK78er.zip")]        // a browser-renamed download
    [InlineData("Cape Stardew 1.6 14635 7.2.1 Ukcm5dRM7.zip")]   // spaces, not dashes: no id to trust
    [InlineData("manually-made.zip")]
    public void ParseNexusIdFromFileName_ReturnsNullWhenTheNameDoesNotFollowTheConvention(string fileName)
    {
        Assert.Null(ModManifest.ParseNexusIdFromFileName(fileName));
    }

    // -------------------------------------------------------------------------
    // The stricter read, for a file the user picked from anywhere on their disk
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("Granny's Recipe Box-23737-1-0-2-1715181269.zip", "23737")]
    [InlineData("Cape Stardew 1.6-14635-7-1-12-1775991448.zip", "14635")]
    [InlineData("SomeMod-4242-1-0.7z", "4242")]
    [InlineData("Skyrim Access-181131-1-2-3-1723456789.7z", "181131")]
    public void NexusIdFromDownloadName_ReadsTheIdOutOfARealNexusDownload(string fileName, string expected)
    {
        Assert.Equal(expected, ModManifest.NexusIdFromDownloadName(fileName));
    }

    [Theory]
    [InlineData("ModBackup-12345-old.zip")]          // a number in the name, but not a Nexus download
    [InlineData("Save Anywhere-90210-final.zip")]
    [InlineData("MyMod-4242-1-0 (1).zip")]           // renamed by the browser on a second download
    [InlineData("manually-made.zip")]
    [InlineData("")]
    public void NexusIdFromDownloadName_RefusesAnythingItIsNotSureOf(string fileName)
    {
        // The cost of refusing is one automatic link the user can make themselves; the cost of a wrong id is
        // another mod's updates being offered for this one, which reads as if the manager knows something.
        Assert.Null(ModManifest.NexusIdFromDownloadName(fileName));
    }

    [Fact]
    public void NexusIdFromDownloadName_RefusesANameWithTwoIdsItCannotChooseBetween()
    {
        // A title ending in a dashed number puts a second candidate in the name, and nothing about their shape
        // says which is the mod. Taking the first would be a guess wearing a fact's clothes.
        Assert.Null(ModManifest.NexusIdFromDownloadName("Cape Stardew-16000-14635-7-1-12-1775991448.zip"));
    }

    /// <summary>
    /// Which version gets recorded when a mod is installed.
    ///
    /// <para>
    /// A page's version and an installed copy's version are different facts, and conflating them hides the one
    /// failure a mod manager must never hide. Reported as a Skyrim that would not launch: an update had installed
    /// a build six weeks older than the one it replaced, and nothing could show it, because the new copy was
    /// labelled with the page's current version. The mod list read out the newest number, the update check
    /// compared that number and found nothing to do, and the reinstall prompt said "version 6.5.2 is already
    /// installed" of a folder holding 6.4.3.
    /// </para>
    /// </summary>
    public class VersionOfTheInstalledCopy
    {
        [Fact]
        public void ThePagesVersionNeverOverridesTheFilesOwn()
        {
            // The exact case: 6.4.3 was downloaded while the page had moved on to 6.5.2.
            Assert.Equal("6.4.3", ModManifest.VersionOfTheInstalledCopy(null, "6.4.3", "6.5.2"));
        }

        [Fact]
        public void TheArchivesAuthorIsBelievedOverNexusFileName()
        {
            // A FOMOD's info.xml is the author's own statement about the very files being installed.
            Assert.Equal("2.1.0", ModManifest.VersionOfTheInstalledCopy("2.1.0", "2.0.9", "3.0.0"));
        }

        [Fact]
        public void ThePageIsUsedOnlyWhenTheFileItselfSaysNothing()
        {
            // A hand-renamed archive carries no version. The page's number is then a better guess than none —
            // it is only wrong to prefer it, not wrong to fall back to it.
            Assert.Equal("6.5.2", ModManifest.VersionOfTheInstalledCopy(null, null, "6.5.2"));
            Assert.Equal("6.5.2", ModManifest.VersionOfTheInstalledCopy("", "   ", "6.5.2"));
        }

        [Fact]
        public void NothingKnownIsSaidRatherThanInvented()
        {
            Assert.Null(ModManifest.VersionOfTheInstalledCopy(null, null, null));
            Assert.Null(ModManifest.VersionOfTheInstalledCopy("", "", " "));
        }
    }
}
