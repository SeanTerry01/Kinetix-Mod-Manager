using System;
using System.Drawing;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// The numbering a screen reader walks a tab strip by.
///
/// <para>
/// <see cref="TabStripGuardTests"/> proves every strip in the app is an <see cref="AccessibleTabControl"/> rather
/// than a stock one. It cannot see whether that control counts correctly — and the counting is the entire reason
/// the class exists, because a stock TabControl's child numbering is one behind the numbering the native window
/// sends out, which is what made every tab change read as "tab control" instead of the tab's name.
/// </para>
///
/// <para>
/// A mutation campaign widened each bound here by one and made hit-testing always answer with the first tab. All
/// four passed the whole suite. These are the cases that fail instead.
/// </para>
/// </summary>
public class TabStripAccessibilityTests
{
	// ------------------------------------------------------------------------ child count ----

	[Fact]
	public void TheStripHasOneChildPerTabPlusTheSelectedPagesContent()
	{
		Assert.Equal(5, TabStripAccessibility.ChildCount(tabCount: 4, hasSelectedTab: true));
	}

	[Fact]
	public void WithNoPageSelectedTheStripIsJustItsTabs()
	{
		Assert.Equal(4, TabStripAccessibility.ChildCount(tabCount: 4, hasSelectedTab: false));
	}

	[Fact]
	public void AStripWithNoTabsAndNoPageHasNoChildren()
	{
		Assert.Equal(0, TabStripAccessibility.ChildCount(tabCount: 0, hasSelectedTab: false));
	}

	// ------------------------------------------------------------------ what is at an index ----

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(3)]
	public void TabsComeFirst_NumberedFromZero(int index)
	{
		// The whole fix: child zero is the first TAB, not the selected page's content.
		Assert.Equal(TabStripChild.Tab,
			TabStripAccessibility.ChildAt(index, tabCount: 4, hasSelectedTab: true));
	}

	[Fact]
	public void ThePagesContentBringsUpTheRear()
	{
		Assert.Equal(TabStripChild.PageContent,
			TabStripAccessibility.ChildAt(4, tabCount: 4, hasSelectedTab: true));
	}

	[Fact]
	public void OnePastTheLastTabIsNothingWhenNoPageIsSelected()
	{
		Assert.Equal(TabStripChild.None,
			TabStripAccessibility.ChildAt(4, tabCount: 4, hasSelectedTab: false));
	}

	[Theory]
	[InlineData(5)]
	[InlineData(99)]
	[InlineData(-1)]
	public void AnIndexBeyondTheStripIsNothingAtAll(int index)
	{
		// Widening this bound by one hands a reader an object for a tab that does not exist.
		Assert.Equal(TabStripChild.None,
			TabStripAccessibility.ChildAt(index, tabCount: 4, hasSelectedTab: true));
	}

	// ---------------------------------------------------------------------- is it a real tab ----

	[Theory]
	[InlineData(0, 4, true)]
	[InlineData(3, 4, true)]
	[InlineData(4, 4, false)]   // one past the end — the off-by-one that was uncaught
	[InlineData(-1, 4, false)]
	[InlineData(0, 0, false)]   // tabs can vanish as the game changes
	public void ATabIndexIsOnlyRealWhileTheTabIsThere(int index, int tabCount, bool expected)
	{
		Assert.Equal(expected, TabStripAccessibility.IsTab(index, tabCount));
	}

	// -------------------------------------------------------------------------- what is selected ----

	[Fact]
	public void TheFirstTabCanBeTheSelectedOne()
	{
		// A strip starts on its first tab. Reporting nothing for index zero leaves a reader silent exactly where
		// it lands.
		Assert.Equal(0, TabStripAccessibility.SelectedChildIndex(0));
	}

	[Fact]
	public void ALaterTabIsReportedAsItself()
	{
		Assert.Equal(3, TabStripAccessibility.SelectedChildIndex(3));
	}

	[Fact]
	public void NothingSelectedIsReportedAsNoTab()
	{
		// The literal, not the constant. Asserting against TabStripAccessibility.NoTab compares the sentinel with
		// itself: change the constant and the expectation follows it, so the test cannot tell -1 from 0. A
		// mutation campaign set NoTab to 0 and this file did not notice.
		Assert.Equal(-1, TabStripAccessibility.SelectedChildIndex(-1));
	}

	[Fact]
	public void NoTabIsNotAnIndexAnyTabCouldHave()
	{
		// The property that makes the sentinel a sentinel. "No tab" and "tab zero" have to be different answers,
		// because callers act on one by returning null and on the other by handing back a real tab.
		Assert.Equal(-1, TabStripAccessibility.NoTab);

		foreach (int tabCount in new[] { 0, 1, 4, 99 })
			Assert.False(TabStripAccessibility.IsTab(TabStripAccessibility.NoTab, tabCount));

		Assert.Equal(TabStripChild.None,
			TabStripAccessibility.ChildAt(TabStripAccessibility.NoTab, tabCount: 4, hasSelectedTab: true));
	}

	// ------------------------------------------------------------------------------ hit testing ----

	/// <summary>Four 40x20 headers in a row, the way a strip lays them out.</summary>
	private static Rectangle Header(int index) => new Rectangle(index * 40, 0, 40, 20);

	[Theory]
	[InlineData(5, 0)]
	[InlineData(45, 1)]
	[InlineData(85, 2)]
	[InlineData(125, 3)]
	public void APointOnAHeaderFindsThatTab_NotTheFirstOne(int x, int expected)
	{
		// Answering with tab zero for every point tells a mouse or touch user they are on the wrong tab.
		Assert.Equal(expected, TabStripAccessibility.TabAt(new Point(x, 10), 4, Header));
	}

	[Theory]
	[InlineData(200, 10)]   // past the last header
	[InlineData(5, 50)]     // below the strip, in the page body
	[InlineData(-5, 10)]    // left of the first header
	public void APointOffTheHeadersFindsNoTab(int x, int y)
	{
		// Literal, for the reason given on NoTabIsNotAnIndexAnyTabCouldHave: a miss must not be reportable as a
		// hit on the first tab.
		Assert.Equal(-1, TabStripAccessibility.TabAt(new Point(x, y), 4, Header));
	}

	[Fact]
	public void AStripWithNoTabsNeverInspectsARectangle()
	{
		Assert.Equal(-1,
			TabStripAccessibility.TabAt(new Point(5, 10), 0,
				_ => throw new InvalidOperationException("there are no headers to measure")));
	}
}
