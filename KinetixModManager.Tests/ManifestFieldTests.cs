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
}
