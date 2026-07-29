using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ModNameMatch"/> — the last-resort "is this Nexus search result the same mod?" rule used
/// by Auto-match when nothing records where a mod came from. Both directions matter: too strict and mods stay
/// unlinked, too loose and a mod gets pointed at someone else's page, reporting their version and downloading
/// their files. The names here are taken from real installed mods.
/// </summary>
public class ModNameMatchTests
{
    private static GameMod Mod(string name, string author = "") => new() { Name = name, Author = author };

    [Theory]
    [InlineData("[CP] Stoned Valley", "stonedvalley")]
    [InlineData("(CP)Antique crystal lighting fixtures", "antiquecrystallightingfixtures")]
    [InlineData("[JA] Stoned Valley Clothes", "stonedvalleyclothes")]
    [InlineData("[BL] Nature In The Valley", "natureinthevalley")]
    public void Normalize_DropsTheStardewContentPackTypeTag(string name, string expected)
    {
        // "[CP]", "(JA)" and friends say which framework the pack targets; they are never part of the mod's
        // name on Nexus, and leaving them in stopped otherwise-exact matches from matching.
        Assert.Equal(expected, ModNameMatch.Normalize(name));
    }

    [Theory]
    [InlineData("Paul the Optometrist!", "paultheoptometrist")]
    [InlineData("paul-the-optometrist", "paultheoptometrist")]
    [InlineData("  MEGA Furnace  ", "megafurnace")]
    public void Normalize_IgnoresPunctuationCaseAndSpacing(string name, string expected)
    {
        Assert.Equal(expected, ModNameMatch.Normalize(name));
    }

    [Fact]
    public void Normalize_KeepsATagThatIsActuallyPartOfTheName()
    {
        // Only a short leading tag is dropped; a long bracketed phrase is left alone rather than guessed at.
        Assert.Equal("averylongbracketedthingmod", ModNameMatch.Normalize("[A very long bracketed thing] Mod"));
    }

    [Fact]
    public void ExactNameMatch_IsAccepted()
    {
        Assert.True(ModNameMatch.IsConfident(Mod("MEGA Furnace"), Mod("MEGA Furnace")));
    }

    [Fact]
    public void TypeTaggedPackMatchesItsNexusPage()
    {
        Assert.True(ModNameMatch.IsConfident(Mod("[CP]Craftable Battery Pack"), Mod("Craftable Battery Pack")));
    }

    [Fact]
    public void PartialNameMatch_IsAcceptedOnlyWithTheSameAuthor()
    {
        var installed = Mod("Granny's Recipe Box", "DuchessIvy");

        Assert.True(ModNameMatch.IsConfident(installed, Mod("Grannys Recipe Box Expanded", "DuchessIvy")));
        Assert.False(ModNameMatch.IsConfident(installed, Mod("Grannys Recipe Box Expanded", "SomeoneElse")));
    }

    [Fact]
    public void DifferentModsAreNeverMatched()
    {
        Assert.False(ModNameMatch.IsConfident(Mod("MEGA Furnace", "Vechio"), Mod("Mega Storage", "Vechio")));
    }

    [Fact]
    public void UnrelatedResultSharingNoName_IsRejected()
    {
        // The failure that started this: a loose "contains" rule linked mods to whatever came back first.
        Assert.False(ModNameMatch.IsConfident(Mod("Pelican Town Food Truck", "8BitAlien"), Mod("Food", "Someone")));
    }

    [Theory]
    [InlineData("[CP] Stoned Valley", "Stoned Valley")]
    [InlineData("(CP)Antique crystal lighting fixtures", "Antique crystal lighting fixtures")]
    [InlineData("MEGA Furnace", "MEGA Furnace")]
    public void StripTypeTag_GivesTheNameToSearchNexusWith(string name, string expected)
    {
        // Keeps spacing and capitals: this string goes to the Nexus search box, not to a comparison.
        Assert.Equal(expected, ModNameMatch.StripTypeTag(name));
    }

    [Fact]
    public void EmptyNames_AreNeverConfident()
    {
        Assert.False(ModNameMatch.IsConfident(Mod(""), Mod("")));
        Assert.False(ModNameMatch.IsConfident(Mod("[CP]"), Mod("Anything")));
    }
}
