using System;
using System.Drawing;

namespace KinetixModManager;

/// <summary>What the child at a given position in an accessible tab strip actually is.</summary>
internal enum TabStripChild
{
	/// <summary>No child at that position.</summary>
	None,

	/// <summary>A tab header, numbered from zero exactly as the native tab window numbers them.</summary>
	Tab,

	/// <summary>The selected page's content, which brings up the rear behind the tabs.</summary>
	PageContent,
}

/// <summary>
/// The numbering rules behind <see cref="AccessibleTabControl"/>, separated from the WinForms control they are
/// applied to.
///
/// <para>
/// The ordering is the entire fix that class exists for: a stock <c>TabControl</c> puts the selected page's
/// content first and the tab headers after it, so every focus event arrives pointing one tab behind and a screen
/// reader reads "tab control" instead of the tab's name. Putting the tabs first and the content last lines the
/// managed tree up with the numbering the native window already sends out.
/// </para>
///
/// <para>
/// That makes these off-by-one rules load-bearing for a screen reader user, and until now nothing checked them: a
/// mutation campaign widened every bound here by one and shifted hit-testing to always answer with the first tab,
/// and all four changes passed the whole suite. The arithmetic lives here, in a file that depends on nothing but
/// the BCL, so the test project can compile it directly.
/// </para>
/// </summary>
internal static class TabStripAccessibility
{
	/// <summary>Returned by <see cref="TabAt"/> when the point is not on a tab header.</summary>
	internal const int NoTab = -1;

	/// <summary>
	/// How many children the strip reports: one per tab, plus one for the selected page's content when a page is
	/// selected at all.
	/// </summary>
	internal static int ChildCount(int tabCount, bool hasSelectedTab) => tabCount + (hasSelectedTab ? 1 : 0);

	/// <summary>
	/// What sits at <paramref name="index"/>. Tabs occupy 0 to <paramref name="tabCount"/> - 1; the position one
	/// past the last tab is the selected page's content, and is nothing at all when no page is selected.
	/// </summary>
	internal static TabStripChild ChildAt(int index, int tabCount, bool hasSelectedTab)
	{
		if (IsTab(index, tabCount)) return TabStripChild.Tab;
		if (index == tabCount && hasSelectedTab) return TabStripChild.PageContent;
		return TabStripChild.None;
	}

	/// <summary>
	/// Whether <paramref name="index"/> names a tab that exists. Tabs come and go as the game changes, so an index
	/// held from a moment ago can point past the end.
	/// </summary>
	internal static bool IsTab(int index, int tabCount) => index >= 0 && index < tabCount;

	/// <summary>
	/// The child index the strip should report as selected, or <see cref="NoTab"/> when nothing is selected.
	/// The first tab is index zero and is as selectable as any other — reporting nothing for it would leave a
	/// reader silent whenever the strip is on its first tab, which is where it starts.
	/// </summary>
	internal static int SelectedChildIndex(int selectedIndex) => selectedIndex >= 0 ? selectedIndex : NoTab;

	/// <summary>
	/// The tab whose header contains <paramref name="point"/>, or <see cref="NoTab"/> when none does. The first
	/// containing rectangle wins, so overlapping headers resolve to the earlier tab.
	/// </summary>
	/// <param name="rectOf">The header rectangle for a tab index, in the same space as <paramref name="point"/>.</param>
	internal static int TabAt(Point point, int tabCount, Func<int, Rectangle> rectOf)
	{
		for (int i = 0; i < tabCount; i++)
			if (rectOf(i).Contains(point)) return i;

		return NoTab;
	}
}
