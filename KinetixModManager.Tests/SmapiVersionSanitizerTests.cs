using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="SmapiVersion.Sanitize"/>, the guard on the Stardew Valley update check.
/// The SMAPI web API answers a batch containing even one unparseable version with an empty result — a 200
/// response listing no mods at all — so a single mod whose manifest says "1", "v1.2.3" or nothing used to
/// wipe out the whole update check and updates silently went missing from the Updates tab. These cases are
/// the exact strings the live API was observed to accept and reject.
/// </summary>
public class SmapiVersionSanitizerTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.0", "1.0")]
    [InlineData("1.12.1", "1.12.1")]
    [InlineData("2.4.0-beta.3", "2.4.0-beta.3")]
    [InlineData("1.0.0+build7", "1.0.0+build7")]
    public void AlreadyValidVersions_PassThroughUnchanged(string input, string expected)
    {
        Assert.Equal(expected, SmapiVersion.Sanitize(input));
    }

    [Theory]
    [InlineData("1", "1.0")]                 // the API rejects a bare major version
    [InlineData("v1.2.3", "1.2.3")]          // a leading v is not part of a semantic version
    [InlineData("V2.0", "2.0")]
    [InlineData("1.0.0.0", "1.0.0")]         // four-part file versions (e.g. StardewModdingAPI.dll) are rejected
    [InlineData("  1.2.3  ", "1.2.3")]
    [InlineData("1.2 for SMAPI 4", "1.2")]   // trailing prose is not a prerelease tag
    public void CoercibleVersions_AreNormalisedToAnAcceptedForm(string input, string expected)
    {
        Assert.Equal(expected, SmapiVersion.Sanitize(input));
    }

    [Theory]
    [InlineData("0")]        // all-zero versions are rejected outright by the API
    [InlineData("0.0.0")]    // ...including the obvious "unknown version" placeholder
    [InlineData("0.0")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not a version")]
    [InlineData("unknown")]
    public void UnusableVersions_BecomeEmptyRatherThanPoisoningTheBatch(string? input)
    {
        Assert.Equal("", SmapiVersion.Sanitize(input));
    }

    [Fact]
    public void ExtraVersionParts_AreDroppedBeyondMajorMinorPatch()
    {
        Assert.Equal("3.2.1", SmapiVersion.Sanitize("3.2.1.4.5"));
    }

    [Fact]
    public void NonNumericTail_IsTruncatedAtTheFirstUnparseablePart()
    {
        // "1.x" has no usable minor, so it falls back to the major version padded to major.minor.
        Assert.Equal("1.0", SmapiVersion.Sanitize("1.x"));
    }
}
