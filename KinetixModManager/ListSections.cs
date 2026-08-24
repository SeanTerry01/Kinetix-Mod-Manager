using System.Collections.Generic;

namespace KinetixModManager;

/// <summary>
/// A list row that is a heading rather than an item: it names the group of rows that follows, and cannot be
/// chosen or acted on. Implemented by the row types of any list that is divided into sections.
/// </summary>
public interface IListHeadingRow
{
	/// <summary>True when this row is a heading rather than one of the things being listed.</summary>
	bool IsHeading { get; }
}

/// <summary>
/// Where a row sits in a list that is divided by headings.
///
/// <para>
/// Counting every row alike makes a sectioned list harder to follow, not easier. A settings list of two groups
/// read "3 of 35" — a number spanning both groups and including the headings themselves, so it answered neither
/// "how far through this group am I" nor "how much is left". The headings were also numbered, which is a
/// position for something that is not an item.
/// </para>
///
/// <para>
/// So a row is numbered <b>within its own section</b>, and headings are not numbered at all: "Sound: Hit, on,
/// 1 of 28" says where you are in the Sounds group, and the heading before it already said which group that is.
/// A list with no headings in it is counted exactly as before — the whole list is one section.
/// </para>
///
/// <para>Pure and self-contained, so the counting is unit tested rather than inferred from listening to it.</para>
/// </summary>
public static class ListSections
{
	/// <summary>True when a list row is a heading.</summary>
	public static bool IsHeading(object? item) => item is IListHeadingRow { IsHeading: true };

	/// <summary>
	/// Where the row at <paramref name="index"/> sits within its section, and how many items that section holds
	/// — both counting only real items, never the headings.
	///
	/// Returns <c>(0, 0)</c> when the row is itself a heading, or when the index is outside the list: a heading
	/// gets no position, and the caller says nothing rather than inventing one.
	/// </summary>
	/// <param name="headings">One flag per row, in list order: true where that row is a heading.</param>
	/// <param name="index">The row to locate.</param>
	public static (int Position, int Total) PositionWithinSection(IReadOnlyList<bool> headings, int index)
	{
		if (index < 0 || index >= headings.Count || headings[index]) return (0, 0);

		// The section starts after the nearest heading above this row, or at the top of the list when there is
		// none — rows can precede the first heading, and they are a section of their own.
		int start = 0;
		for (int i = index; i >= 0; i--)
			if (headings[i]) { start = i + 1; break; }

		int position = 0;
		int total = 0;
		for (int i = start; i < headings.Count && !headings[i]; i++)
		{
			total++;
			if (i <= index) position++;
		}
		return (position, total);
	}
}
