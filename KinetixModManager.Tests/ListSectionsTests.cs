using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers how a row in a list divided by headings reports where it is: counted within its own section, with the
/// headings themselves left out of the count and given no position of their own.
/// </summary>
public class ListSectionsTests
{
    /// <summary>A layout written the way it reads: '#' is a heading, '.' is an item.</summary>
    private static bool[] Rows(string layout) => layout.Select(c => c == '#').ToArray();

    [Fact]
    public void AListWithNoHeadingsIsCountedWhole()
    {
        // The behaviour every other list in the manager already has, and must keep.
        bool[] rows = Rows("....");

        Assert.Equal((1, 4), ListSections.PositionWithinSection(rows, 0));
        Assert.Equal((4, 4), ListSections.PositionWithinSection(rows, 3));
    }

    [Fact]
    public void ItemsAreCountedWithinTheirOwnSection()
    {
        // Two settings groups: a heading, two settings, a heading, three settings.
        bool[] rows = Rows("#..#...");

        Assert.Equal((1, 2), ListSections.PositionWithinSection(rows, 1));
        Assert.Equal((2, 2), ListSections.PositionWithinSection(rows, 2));
        Assert.Equal((1, 3), ListSections.PositionWithinSection(rows, 4));
        Assert.Equal((3, 3), ListSections.PositionWithinSection(rows, 6));
    }

    [Fact]
    public void AHeadingHasNoPositionOfItsOwn()
    {
        bool[] rows = Rows("#..#...");

        Assert.Equal((0, 0), ListSections.PositionWithinSection(rows, 0));
        Assert.Equal((0, 0), ListSections.PositionWithinSection(rows, 3));
    }

    [Fact]
    public void HeadingsAreNotCountedTowardsATotal()
    {
        // The whole point: the second group holds three settings, so it must say "of 3" and not "of 4" or "of 7".
        bool[] rows = Rows("#..#...");
        Assert.Equal(3, ListSections.PositionWithinSection(rows, 4).Total);
    }

    [Fact]
    public void RowsBeforeTheFirstHeadingAreASectionOfTheirOwn()
    {
        bool[] rows = Rows("..#..");

        Assert.Equal((1, 2), ListSections.PositionWithinSection(rows, 0));
        Assert.Equal((2, 2), ListSections.PositionWithinSection(rows, 1));
        Assert.Equal((1, 2), ListSections.PositionWithinSection(rows, 3));
    }

    [Fact]
    public void AnEmptySectionAffectsNothingAroundIt()
    {
        // Two headings in a row: the first group has nothing in it.
        bool[] rows = Rows("##..");
        Assert.Equal((1, 2), ListSections.PositionWithinSection(rows, 2));
    }

    [Fact]
    public void AnIndexOutsideTheListHasNoPosition()
    {
        bool[] rows = Rows("#..");

        Assert.Equal((0, 0), ListSections.PositionWithinSection(rows, -1));
        Assert.Equal((0, 0), ListSections.PositionWithinSection(rows, 3));
        Assert.Equal((0, 0), ListSections.PositionWithinSection(System.Array.Empty<bool>(), 0));
    }

    [Fact]
    public void OnlyARowThatSaysItIsAHeadingCountsAsOne()
    {
        Assert.False(ListSections.IsHeading(null));
        Assert.False(ListSections.IsHeading("just a string"));
        Assert.False(ListSections.IsHeading(new Row(false)));
        Assert.True(ListSections.IsHeading(new Row(true)));
    }

    private sealed class Row : IListHeadingRow
    {
        public Row(bool isHeading) => IsHeading = isHeading;
        public bool IsHeading { get; }
    }

    [Fact]
    public void TheWitcherAccessSettingsListReadsAsIntended()
    {
        // The shape the settings list actually produces: a General heading with 4 settings, then a Sounds
        // heading with 28. What should be heard is "1 of 4" … then "1 of 28", not "2 of 34".
        bool[] rows = Rows("#...." + "#" + new string('.', 28));

        Assert.Equal((0, 0), ListSections.PositionWithinSection(rows, 0));   // General, heading
        Assert.Equal((1, 4), ListSections.PositionWithinSection(rows, 1));   // Aim enabled
        Assert.Equal((4, 4), ListSections.PositionWithinSection(rows, 4));
        Assert.Equal((0, 0), ListSections.PositionWithinSection(rows, 5));   // Sounds, heading
        Assert.Equal((1, 28), ListSections.PositionWithinSection(rows, 6));  // Sound: Hit
        Assert.Equal((28, 28), ListSections.PositionWithinSection(rows, 33));
    }
}
