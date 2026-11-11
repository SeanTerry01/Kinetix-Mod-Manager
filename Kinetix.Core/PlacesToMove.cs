using System.Globalization;
using System.Text.RegularExpressions;

namespace KinetixModManager;

/// <summary>
/// Where a typed move puts a row in a reorderable list: "23 down", "down 23", "36 up", "top", "bottom", or a position
/// number on its own.
///
/// <para>
/// Asked for by a player with 60 mods on 2026-11-11. Getting a mod from near the bottom of Mod Priority to near the top
/// meant holding Control and Up, and an older laptop keyboard can't hold a key that long. Typing the move is one command
/// whatever the distance.
/// </para>
///
/// <para>
/// Positions here start at 0, as the list's own do; what the user types and hears starts at 1. Up is towards the top
/// of the list, which on Mod Priority is higher priority and on Plugin Order is loading earlier.
/// </para>
/// </summary>
public static class PlacesToMove
{
	/// <summary>The words a language uses for each direction. Each is a comma-separated list, such as "down,d".</summary>
	public sealed record Words(string Up, string Down, string Top, string Bottom);

	/// <param name="Target">Where the row goes.</param>
	/// <param name="Overshot">More places were asked for than there are, and the row stops at the top or the bottom.</param>
	public readonly record struct Move(int Target, bool Overshot);

	public enum Problem
	{
		None,
		/// <summary>Nothing that reads as a move.</summary>
		NotUnderstood,
		/// <summary>A position number outside the list.</summary>
		NoSuchPosition,
	}

	// A number and a word in either order, or just one of them. Letters of any script, so a translation's words work.
	private static readonly Regex Shape = new(
		@"^\s*(?:(?<n>\d+)\s*(?<w>\p{L}+)?|(?<w>\p{L}+)\s*(?<n>\d+)?)\s*[.!]?\s*$",
		RegexOptions.CultureInvariant);

	/// <summary>
	/// Where <paramref name="typed"/> moves the row at <paramref name="from"/> in a list of <paramref name="count"/>, or
	/// null with the reason in <paramref name="problem"/>. A move that ends where it began is still a move, with its
	/// Target equal to <paramref name="from"/>, for the caller to say so.
	/// </summary>
	public static Move? Read(string typed, int from, int count, Words words, out Problem problem)
	{
		problem = Problem.NotUnderstood;
		if (count <= 0 || from < 0 || from >= count) return null;

		Match m = Shape.Match(typed ?? "");
		if (!m.Success) return null;

		Group numberGroup = m.Groups["n"], wordGroup = m.Groups["w"];
		// Too long for an int is still a number of places: far more than any list has.
		int? number = null;
		if (numberGroup.Success)
			number = int.TryParse(numberGroup.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : int.MaxValue;
		string word = wordGroup.Success ? wordGroup.Value : "";

		int last = count - 1;
		if (word.Length == 0)
		{
			if (number is not int position) return null;
			if (position < 1 || position > count)
			{
				problem = Problem.NoSuchPosition;
				return null;
			}
			problem = Problem.None;
			return new Move(position - 1, false);
		}

		if (number == null)
		{
			if (IsOneOf(word, words.Top)) { problem = Problem.None; return new Move(0, false); }
			if (IsOneOf(word, words.Bottom)) { problem = Problem.None; return new Move(last, false); }
			return null;
		}

		bool up = IsOneOf(word, words.Up);
		if (!up && !IsOneOf(word, words.Down)) return null;

		int places = number.Value;
		long target = up ? (long)from - places : (long)from + places;
		problem = Problem.None;
		if (target < 0) return new Move(0, from != 0);
		if (target > last) return new Move(last, from != last);
		return new Move((int)target, false);
	}

	private static bool IsOneOf(string word, string list) =>
		list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Any(w => string.Equals(w, word, StringComparison.CurrentCultureIgnoreCase));
}
