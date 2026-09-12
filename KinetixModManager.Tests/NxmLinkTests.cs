using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="NxmLink"/> and the domain lookup it feeds.
///
/// These exist because of a specific reported failure. The download path used to build its API request from the
/// <em>loaded game</em> while taking the mod and file ids from the link, so pressing "Mod Manager Download" for one
/// game while another was loaded — or none, where the game fell back to Stardew Valley — asked Nexus about a mod id
/// that does not exist in that game. Nexus answers with an error object rather than the expected array of download
/// mirrors, and the user was shown "Current JsonReader item is not an array: StartObject", which names nothing they
/// can act on. The link always carried the right game; these tests hold it to reading it.
/// </summary>
public class NxmLinkTests
{
    [Fact]
    public void ReadsTheGameModAndFileFromALink()
    {
        Assert.True(NxmLink.TryParse("nxm://skyrimspecialedition/mods/1234/files/5678", out NxmLink link));

        Assert.Equal("skyrimspecialedition", link.GameDomain);
        Assert.Equal("1234", link.ModId);
        Assert.Equal("5678", link.FileId);
        Assert.Equal("", link.Query);
    }

    [Fact]
    public void KeepsTheKeyAndExpiryVerbatim()
    {
        // The key and expiry authorise a free account's download and expire within minutes; anything less than
        // passing them through untouched turns into a refusal from Nexus.
        Assert.True(NxmLink.TryParse(
            "nxm://fallout4/mods/1/files/2?key=AbC-123_xyz&expires=1754870400&user_id=42", out NxmLink link));

        Assert.Equal("?key=AbC-123_xyz&expires=1754870400&user_id=42", link.Query);
    }

    [Fact]
    public void TreatsTheGameNameAsCaseInsensitive()
    {
        Assert.True(NxmLink.TryParse("nxm://SkyrimSpecialEdition/mods/7/files/8", out NxmLink link));

        Assert.Equal("skyrimspecialedition", link.GameDomain);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/1234")]
    [InlineData("nxm://skyrimspecialedition")]
    [InlineData("nxm://skyrimspecialedition/mods/1234")]
    [InlineData("nxm://skyrimspecialedition/mods/1234/files")]
    [InlineData("nxm://skyrimspecialedition/collections/abc/revisions/1")]
    [InlineData("nxm:///mods/1234/files/5678")]
    [InlineData("not a url at all")]
    public void RefusesAnythingItCannotRead(string? url)
    {
        // The input arrives from outside the program, so a bad one has to be a message to the user rather than an
        // exception on a background thread.
        Assert.False(NxmLink.TryParse(url, out _));
    }

    [Fact]
    public void EveryGameCanBeFoundByTheDomainItsLinksUse()
    {
        foreach (GameProfile game in GameProfiles.All)
        {
            if (game.NexusDomain.Length == 0)
            {
                // A game with no Nexus domain must be one that doesn't get its mods from Nexus — Minecraft,
                // whose Fabric mods live on Modrinth. Asserted rather than merely skipped: a Nexus-sourced game
                // that lost its domain would otherwise slip through this loop unnoticed, which is precisely the
                // silent-nothing failure this test exists to catch.
                Assert.NotEqual(ModSource.Nexus, game.ModSource);
                continue;
            }

            GameProfile? found = GameProfiles.FindByNexusDomain(game.NexusDomain);

            Assert.NotNull(found);
            Assert.Equal(game.Id, found!.Id);
        }
    }

    [Fact]
    public void EveryGamesLinkResolvesEndToEnd()
    {
        // The pairing that actually mattered: a link built the way Nexus builds it, for each supported game, has to
        // arrive at that game and no other. Moonlight Peaks and The Witcher 3 are the reason this is a loop — the
        // code this replaced hardcoded three of the five games and silently did nothing for the other two.
        foreach (GameProfile game in GameProfiles.All)
        {
            // Minecraft has no Nexus domain, so there is no nxm:// link that should ever arrive at it.
            if (game.NexusDomain.Length == 0)
            {
                Assert.NotEqual(ModSource.Nexus, game.ModSource);
                continue;
            }

            Assert.True(NxmLink.TryParse($"nxm://{game.NexusDomain}/mods/100/files/200?key=k&expires=1", out NxmLink link));

            Assert.Equal(game.Id, GameProfiles.FindByNexusDomain(link.GameDomain)?.Id);
        }
    }

    [Theory]
    [InlineData("cyberpunk2077")]
    [InlineData("newvegas")]
    [InlineData("")]
    [InlineData(null)]
    public void ReportsAnUnsupportedGameRatherThanGuessingOne(string? domain)
    {
        // A null answer is a sentence to say to the user. Guessing is what produced the original bug.
        Assert.Null(GameProfiles.FindByNexusDomain(domain));
    }
}
