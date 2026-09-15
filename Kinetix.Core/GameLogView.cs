using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>How much of a log is worth reading out.</summary>
public enum LogFilter
{
	/// <summary>Errors and warnings. What the list opens on, because it is what a log is opened for.</summary>
	Problems,

	/// <summary>Errors only, for a log where the warnings are themselves the noise.</summary>
	ErrorsOnly,

	/// <summary>The lot, in order.</summary>
	Everything,
}

/// <summary>One line of a game's log, and the sentence that describes it.</summary>
public sealed record GameLogRow(string Level, string Source, string Text, string SuggestedFix)
{
	public bool IsError => string.Equals(Level, "ERROR", StringComparison.OrdinalIgnoreCase);

	public bool IsWarning => string.Equals(Level, "WARN", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// What the reader says for this row.
	///
	/// <para>
	/// The level first, then who said it, then what it said — and the suggested fix last, but on the same
	/// row rather than behind a keypress. A log line a user cannot act on is noise however accurately it is
	/// read, and putting the fix one interaction away means it is never heard by the person who most needs
	/// it: somebody working through forty lines to find out why their game will not start.
	/// </para>
	///
	/// <para>
	/// The timestamp is deliberately dropped. It is at the front of every line, it is the same on most of
	/// them, and hearing "fifteen forty-one twelve" before every message is the fastest way to make a log
	/// unlistenable.
	/// </para>
	/// </summary>
	public string Spoken
	{
		get
		{
			string said = Source.Length > 0
				? Loc.T("gamelog.rowFrom", Level, Source, Text)
				: Loc.T("gamelog.row", Level, Text);

			return SuggestedFix.Length > 0 ? said + " " + Loc.T("gamelog.fix", SuggestedFix) : said;
		}
	}

	public override string ToString() => Spoken;
}

/// <summary>
/// A game's loader log, read for the parts worth hearing.
///
/// <para>
/// Every rule here is already in <see cref="LogAnalyzer"/> — what level a line is, who wrote it, what it
/// means and what to do about it. This decides what to show and in what order, which is the part a window
/// would otherwise invent for itself once per front end.
/// </para>
///
/// <para>
/// It opens on problems rather than on everything. A SMAPI log is thousands of lines of a game starting
/// normally, and a list that begins at line one asks a blind user to arrow through all of it to reach the
/// four lines that explain the crash.
/// </para>
/// </summary>
public sealed class GameLogView
{
	private GameLogView(IReadOnlyList<GameLogRow> rows, int totalLines, LogFilter filter, bool hadLog)
	{
		Rows = rows;
		TotalLines = totalLines;
		Filter = filter;
		HadLog = hadLog;
	}

	public IReadOnlyList<GameLogRow> Rows { get; }

	/// <summary>Every line the log held, so the summary can say what was left out.</summary>
	public int TotalLines { get; }

	public LogFilter Filter { get; }

	/// <summary>False when there was no log to read at all, which is different from a log with nothing in it.</summary>
	public bool HadLog { get; }

	public int ErrorCount => Rows.Count(r => r.IsError);

	public int WarningCount => Rows.Count(r => r.IsWarning);

	/// <summary>There was no log file.</summary>
	public static GameLogView NoLog() =>
		new(Array.Empty<GameLogRow>(), 0, LogFilter.Problems, hadLog: false);

	/// <summary>Reads the lines, keeping whatever <paramref name="filter"/> asks for.</summary>
	public static GameLogView Of(IReadOnlyList<string> lines, LogFilter filter)
	{
		var rows = new List<GameLogRow>();

		foreach (string line in lines)
		{
			if (string.IsNullOrWhiteSpace(line)) continue;

			string level = LogAnalyzer.GetLevel(line);
			bool isError = string.Equals(level, "ERROR", StringComparison.OrdinalIgnoreCase);
			bool isWarning = string.Equals(level, "WARN", StringComparison.OrdinalIgnoreCase);

			bool keep = filter switch
			{
				LogFilter.ErrorsOnly => isError,
				LogFilter.Problems => isError || isWarning,
				_ => true,
			};
			if (!keep) continue;

			rows.Add(new GameLogRow(
				level,
				LogAnalyzer.GetSource(line),
				LogAnalyzer.StripHeader(line),
				LogAnalyzer.GetSuggestedFix(line)));
		}

		return new GameLogView(rows, lines.Count, filter, hadLog: true);
	}

	/// <summary>
	/// The sentence to say when the list appears.
	///
	/// "No log" and "a log with nothing wrong in it" are told apart, because they mean opposite things: one
	/// is a game that has not been run since the loader was installed, the other is a game that ran fine.
	/// </summary>
	public string Announcement
	{
		get
		{
			if (!HadLog) return Loc.T("gamelog.noLog");
			if (Rows.Count == 0)
				return Filter == LogFilter.Everything
					? Loc.T("gamelog.empty")
					: Loc.T("gamelog.nothingWrong", TotalLines);

			if (Filter == LogFilter.Everything) return Loc.T("gamelog.allLines", Rows.Count);

			return ErrorCount == 0
				? Loc.T("gamelog.warningsOnly", WarningCount, TotalLines)
				: Loc.T("gamelog.problems", ErrorCount, WarningCount, TotalLines);
		}
	}
}
