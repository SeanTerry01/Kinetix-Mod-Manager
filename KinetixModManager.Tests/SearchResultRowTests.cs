using System;
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

    [Fact]
    public void HowLongAgoTheModWasUpdatedIsReadBeforeTheSummary()
    {
        var mod = Result();
        mod.LastUpdated = DateTimeOffset.UtcNow.AddDays(-21);

        string row = mod.ToString();

        Assert.Equal("Serena's Grimoire (ID: 23). 3,428 downloads, 50 endorsements. Updated 3 weeks ago. Dark magic, dramatic rituals.", row);
        Assert.True(row.IndexOf("Updated") < row.IndexOf("Dark magic"),
            "Whether a mod is still being worked on decides whether the description is worth hearing, so it comes first.");
    }

    [Fact]
    public void AResultWithNoDateSaysNothingAboutOne()
    {
        // Only search results carry a date. A row must not sprout a stray "Updated." when Nexus omits it, and
        // the installed list and update rows must not start mentioning it at all.
        Assert.DoesNotContain("Updated", Result().ToString());
        Assert.DoesNotContain("Updated", new GameMod { Name = "Serena's Grimoire", Author = "Serena", Version = "1.0" }.ToString());
    }

    /// <summary>
    /// The wording of the age itself, which is the part a user hears on every single result. It follows Nexus's
    /// own relative style deliberately: the unit grows with the gap, so nothing has to be worked out from a date.
    /// </summary>
    public class Age
    {
        private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

        private static string Ago(TimeSpan span) => GameMod.DescribeAge(Now - span, Now);

        [Theory]
        [InlineData(0,     0,  0,   "just now")]        // finished uploading while you were searching
        [InlineData(0,     0,  59,  "just now")]
        [InlineData(0,     0,  60,  "1 minute ago")]
        [InlineData(0,     0,  300, "5 minutes ago")]
        [InlineData(0,     1,  0,   "1 hour ago")]
        [InlineData(0,     5,  0,   "5 hours ago")]
        [InlineData(0,     23, 59,  "23 hours ago")]    // never "24 hours ago"
        [InlineData(1,     0,  0,   "1 day ago")]
        [InlineData(6,     0,  0,   "6 days ago")]
        [InlineData(7,     0,  0,   "1 week ago")]      // never "7 days ago"
        [InlineData(21,    0,  0,   "3 weeks ago")]
        [InlineData(30,    0,  0,   "1 month ago")]
        [InlineData(200,   0,  0,   "6 months ago")]
        [InlineData(364,   0,  0,   "12 months ago")]
        [InlineData(365,   0,  0,   "1 year ago")]
        [InlineData(1500,  0,  0,   "4 years ago")]     // the abandoned mod this whole field exists to reveal
        public void EachGapIsSaidInTheUnitThatFitsIt(int days, int hours, int seconds, string expected)
        {
            Assert.Equal(expected, Ago(new TimeSpan(days, hours, 0, seconds)));
        }

        [Fact]
        public void AClockThatDisagreesDoesNotProduceANegativeAge()
        {
            // The user's clock and Nexus's need not agree, and a mod uploaded "in 3 minutes" is nonsense worth
            // not saying out loud.
            Assert.Equal("just now", GameMod.DescribeAge(Now.AddMinutes(5), Now));
        }

        [Fact]
        public void OneOfAnythingIsSingular()
        {
            Assert.Equal("1 week ago",  Ago(TimeSpan.FromDays(7)));
            Assert.Equal("2 weeks ago", Ago(TimeSpan.FromDays(14)));
        }
    }

    [Fact]
    public void AModYouHaveAlreadyDownloadedSaysSoInTheSecondPerson()
    {
        var mod = Result();
        mod.LastDownloaded = DateTimeOffset.UtcNow.AddDays(-2);

        string row = mod.ToString();

        Assert.Equal("Serena's Grimoire (ID: 23). You downloaded this 2 days ago. 3,428 downloads, 50 endorsements. Dark magic, dramatic rituals.", row);
        // "Downloaded 2 days ago" would sit three words from "3,428 downloads" and mean something entirely
        // different. The second person is what keeps the two apart by ear.
        Assert.Contains("You downloaded this", row);
    }

    [Fact]
    public void WhatYouHaveIsSaidBeforeWhatEveryoneElseThinks()
    {
        var mod = Result(installed: true);
        mod.LastDownloaded = DateTimeOffset.UtcNow.AddDays(-2);
        mod.LastUpdated = DateTimeOffset.UtcNow.AddDays(-21);

        string row = mod.ToString();

        Assert.Equal("Serena's Grimoire (ID: 23). Installed. You downloaded this 2 days ago. 3,428 downloads, 50 endorsements. Updated 3 weeks ago. Dark magic, dramatic rituals.", row);
        Assert.True(row.IndexOf("You downloaded") < row.IndexOf("downloads,"),
            "Your own copy comes before the crowd's opinion of the mod.");
    }

    [Fact]
    public void AModYouHaveNotDownloadedSaysNothingAboutIt()
    {
        // Nexus's own page says "you haven't downloaded this mod". A hundred rows each saying so would bury the
        // handful that have something to report, which is the same reason "not installed" is never said.
        Assert.DoesNotContain("downloaded this", Result().ToString());
    }

    [Fact]
    public void DownloadedIsOnlyEverSaidOnASearchResult()
    {
        var installedRow = new GameMod { Name = "Serena's Grimoire", Author = "Serena", Version = "1.0", LastDownloaded = DateTimeOffset.UtcNow };
        var updateRow = new GameMod { Name = "Serena's Grimoire", Author = "Serena", Version = "1.0", LatestVersion = "1.1", IsUpdateResult = true, LastDownloaded = DateTimeOffset.UtcNow };

        Assert.DoesNotContain("downloaded", installedRow.ToString());
        Assert.DoesNotContain("downloaded", updateRow.ToString());
    }
}
