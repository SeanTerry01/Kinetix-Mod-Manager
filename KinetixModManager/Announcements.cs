using System;

namespace KinetixModManager;

/// <summary>
/// How the sentences a screen reader reads out are put together — the ordering, the trimming and the counting,
/// separated from the controls they are read from and from the language file they are worded in.
///
/// <para>
/// This exists because the wiring was guarded and the wording was not. <see cref="Form1.WireAccessibleDialogList"/>
/// and the tests around it prove that every list announces itself; nothing proved that what it announced was
/// right. A mutation campaign against the accessibility layer confirmed it: the position could be made to count
/// from zero, the row and its position could be swapped, the position could be dropped entirely, and the double
/// full stop the code deliberately removes could be put back — and all four passed the whole suite.
/// </para>
///
/// <para>
/// Each method takes its formatting as a delegate rather than calling <c>Loc</c> itself. That keeps this file
/// dependent on nothing but the BCL, so the test project can compile it directly the way it compiles the other
/// self-contained rules — and it lets a test supply its own join, so the test is not asserting that the code
/// agrees with itself.
/// </para>
/// </summary>
internal static class Announcements
{
	/// <summary>
	/// "&lt;the row&gt;. &lt;position&gt;" for an announcement the program is making itself.
	///
	/// Row text already ends in a full stop — one is added deliberately so the reader pauses before whatever
	/// follows — so joining with another produced "Enabled. . 129 of 149", heard as a stumble. The row's own stop
	/// is dropped and <paramref name="join"/> puts one back, leaving exactly one pause where the pause was wanted.
	/// A row with no position (a heading) is all there is to say, and joining it to nothing would leave a
	/// dangling separator hanging off the end of the sentence.
	/// </summary>
	internal static string RowThenPosition(object? row, string position, Func<string, string, string> join)
	{
		string text = DropTrailingStop(row?.ToString() ?? "");
		if (position.Length == 0) return text;
		return text.Length == 0 ? position : join(text, position);
	}

	/// <summary>
	/// "&lt;the list's name&gt;. &lt;the rest&gt;" — what a screen reader says when focus lands on a list, in the
	/// order it says it. A list with no name of its own adds nothing rather than an empty pause.
	///
	/// The same trailing-stop rule as <see cref="RowThenPosition"/>, and deliberately the same helper: this is the
	/// second caller of that rule, and the second caller is the one that historically does not get a test.
	/// </summary>
	internal static string ListNameThenRest(string? accessibleName, string rest, Func<string, string, string> join)
	{
		string name = DropTrailingStop((accessibleName ?? "").Trim());
		if (name.Length == 0) return rest;
		return rest.Length == 0 ? name : join(name, rest);
	}

	/// <summary>
	/// The "x of y" for a row of the documentation outline, or <c>""</c> when the index is not a row at all.
	///
	/// Positions are one-based because that is how they are spoken: the first row of forty is "1 of 40", never
	/// "0 of 40". A heading that opens into sub-topics is announced with its own phrase so the user knows there
	/// is something behind it.
	/// </summary>
	/// <param name="format">Given a phrase key, the one-based position and the total, produces the sentence.</param>
	internal static string OutlinePosition(
		int index, int count, Func<int, bool> hasChildren, Func<string, int, int, string> format)
	{
		if (index < 0 || index >= count) return "";
		return format(hasChildren(index) ? "doc.posGroup" : "common.position", index + 1, count);
	}

	/// <summary>
	/// Text without the full stop it ends in, and without whatever whitespace that stop was hiding. Blank in,
	/// blank out.
	/// </summary>
	private static string DropTrailingStop(string text)
	{
		text = text.TrimEnd();
		if (text.EndsWith(".", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1).TrimEnd();
		return text;
	}
}
