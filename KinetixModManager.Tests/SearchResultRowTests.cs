using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers what a Nexus search result reads as, which is the only thing a screen-reader user has to judge a
/// result by before opening or downloading it.
///
/// <para>
/// The ordering is the part worth pinning down. Whether you already have the mod, and how many people use and
/// endorse it, both come before the summary on purpose: they are the facts that let you move on to the next
/// result without sitting through a description you have already ruled out. A change that quietly moved any of
/// them behind the summary would undo that without failing anything else.
/// </para>
/// </summary>
public class SearchResultRowTests
{
    private static GameMod Result(bool installed = false, string summary = "Dark magic, dramatic rituals.") => new()
    {
        IsSearchResult = true,
        Name = "Serena's Grimoire",
        NexusID = "23",
        Downloads = 3428,
        Endorsements = 50,
        Description = summary,
        IsInstalled = installed
    };

    [Fact]
    public void AResultYouAlreadyHaveSaysSoBeforeAnythingElse()
    {
        string row = Result(installed: true).ToString();

        Assert.Equal("Serena's Grimoire (ID: 23). Installed. 3,428 downloads, 50 endorsements. Dark magic, dramatic rituals.", row);
        Assert.True(row.IndexOf("Installed.") < row.IndexOf("downloads"),
            "Whether the mod is already installed must be read before the popularity numbers.");
    }

    [Fact]
    public void AResultYouDoNotHaveSaysNothingAboutIt()
    {
        // Most results are not installed. Saying "not installed" on every one of a hundred rows would bury the
        // few that are, which are the only ones the marker exists for.
        string row = Result(installed: false).ToString();

        Assert.DoesNotContain("Installed", row);
        Assert.Equal("Serena's Grimoire (ID: 23). 3,428 downloads, 50 endorsements. Dark magic, dramatic rituals.", row);
    }

    [Fact]
    public void TheMarkerStillLeadsWhenNexusReportsNoCounts()
    {
        var mod = Result(installed: true);
        mod.Downloads = -1;      // -1 is "the API did not say", distinct from a genuine zero
        mod.Endorsements = -1;

        Assert.Equal("Serena's Grimoire (ID: 23). Installed. Dark magic, dramatic rituals.", mod.ToString());
    }

    [Fact]
    public void InstalledIsOnlyEverSaidOnASearchResult()
    {
        // The flag is meaningless on an installed-list row, which says Enabled or Disabled instead, and on an
        // update row, which is about versions. Neither should start announcing it if the flag is left set.
        var installedRow = new GameMod { Name = "Serena's Grimoire", Author = "Serena", Version = "1.0", IsInstalled = true };
        var updateRow = new GameMod { Name = "Serena's Grimoire", Author = "Serena", Version = "1.0", LatestVersion = "1.1", IsUpdateResult = true, IsInstalled = true };

        Assert.DoesNotContain("Installed.", installedRow.ToString());
        Assert.DoesNotContain("Installed.", updateRow.ToString());
    }
}
