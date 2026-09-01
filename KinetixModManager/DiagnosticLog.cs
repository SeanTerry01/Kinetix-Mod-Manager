using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KinetixModManager;

/// <summary>
/// The one file to ask a user for when something went wrong.
///
/// There were two before this, and that was the whole problem. Ordinary failures went to
/// <c>mod_manager_log.txt</c>, which the File menu and Control+Shift+L open. Crashes went to a separate
/// <c>crash_log.txt</c> that nothing in the app ever mentioned, opened, or referred to — so a user who crashed
/// was asked for a log that did not contain their crash, while the file that did sat unread beside it. Two real
/// crashes had been recorded there for months and never seen.
///
/// So there is now one file, everything goes in it, and it is the file the app already knows how to open. A user
/// who is asked for "the log" sends something that actually holds the answer.
///
/// It is written on the assumption that whoever eventually reads it was not there. That means: a header saying
/// which build and which machine produced it, full dates rather than bare times (a report arrives days later),
/// exceptions written out with their type and stack and every inner exception rather than reduced to
/// <see cref="Exception.Message"/>, and the thread the failure happened on — because a background failure and a
/// UI-thread failure are different bugs and read identically once flattened to a sentence.
/// </summary>
public static class DiagnosticLog
{
	/// <summary>
	/// The size at which the log is rolled over to <c>.1</c>. Big enough to hold a long session with a large mod
	/// list, small enough that a user can attach it to a message.
	/// </summary>
	public const long MaxBytes = 2 * 1024 * 1024;

	/// <summary>
	/// How many times one kind of failure is recorded in a session before it is summarised instead.
	///
	/// Some of what this logs sits inside a loop over every file of every mod. One unreadable folder there is
	/// worth knowing about; ten thousand of them is a log nobody can read and a report nobody can send, and the
	/// one unrelated failure that mattered would be buried in the middle of it.
	/// </summary>
	public const int MaxRepeatsPerKind = 20;

	/// <summary>Serialises writes: failures arrive from background threads as readily as from the UI one.</summary>
	private static readonly object Gate = new object();

	/// <summary>How many of each kind of failure have been recorded, so a repeating one can be cut off.</summary>
	private static readonly Dictionary<string, int> Seen = new Dictionary<string, int>(StringComparer.Ordinal);

	private static string _path = "";

	/// <summary>Where the log is. Empty until <see cref="Start"/> has been called.</summary>
	public static string Path => _path;

	/// <summary>The rolled-over previous log, kept so one rollover cannot destroy the evidence.</summary>
	public static string PreviousPath(string path) =>
		System.IO.Path.Combine(
			System.IO.Path.GetDirectoryName(path) ?? "",
			System.IO.Path.GetFileNameWithoutExtension(path) + ".1" + System.IO.Path.GetExtension(path));

	/// <summary>
	/// Points the log at <paramref name="path"/> and writes the session header.
	///
	/// Called before anything else can fail, which is why it takes the header lines rather than gathering them:
	/// the things worth recording (the app's version, the loaded game, the screen reader) are known to different
	/// parts of the app, and the earliest of them is known before any of those parts exist.
	/// </summary>
	public static void Start(string path, IEnumerable<string> headerLines)
	{
		lock (Gate)
		{
			_path = path ?? "";
			if (_path.Length == 0) return;

			try
			{
				Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
				RollIfTooBig();

				var sb = new StringBuilder();
				sb.Append(Environment.NewLine);
				sb.Append("======== session started ").Append(Stamp()).Append(" ========").Append(Environment.NewLine);
				foreach (string line in headerLines ?? Array.Empty<string>())
					sb.Append("  ").Append(line).Append(Environment.NewLine);

				File.AppendAllText(_path, sb.ToString());
			}
			catch { /* a log that cannot be written must never be the reason something fails */ }
		}
	}

	/// <summary>
	/// Adds a line to the header of the session already started — for the facts that are only known once
	/// something has loaded, such as which game the session opened on.
	/// </summary>
	public static void Note(string line) => Write("", line);

	/// <summary>
	/// Records that something went wrong, in whatever words the caller has. <paramref name="category"/> is the
	/// part of the app it happened in (a mod's name, "Updates", "Nexus"); an empty one writes a bare line.
	/// </summary>
	public static void Write(string category, string message)
	{
		lock (Gate)
		{
			if (_path.Length == 0) return;
			try
			{
				string? note = CountAndDecide(category, message);
				if (note == null) return;

				RollIfTooBig();
				string prefix = string.IsNullOrEmpty(category) ? "" : category + ": ";
				File.AppendAllText(_path, $"[{Stamp()}] {prefix}{message}{note}{Environment.NewLine}");
			}
			catch { }
		}
	}

	/// <summary>
	/// Counts this kind of entry and says how to write it: <c>""</c> normally, a one-off "no more of these"
	/// notice on the last one allowed, or <c>null</c> to write nothing at all.
	///
	/// Kind is the category plus the start of the message, so "could not read plugin A" and "could not read
	/// plugin B" count as the same recurring problem while an unrelated failure alongside them does not.
	/// </summary>
	private static string? CountAndDecide(string category, string message)
	{
		string kind = category + "|" + (message.Length <= 48 ? message : message.Substring(0, 48));
		Seen.TryGetValue(kind, out int count);
		Seen[kind] = count + 1;

		if (count < MaxRepeatsPerKind - 1) return "";
		if (count == MaxRepeatsPerKind - 1)
			return "   [further entries like this one are not repeated]";
		return null;
	}

	/// <summary>
	/// Records a failure with its exception written out in full.
	///
	/// <paramref name="what"/> is what the app was trying to do at the time, and it is the field that decides
	/// whether the entry is any use. A stack trace says where the code was; only this says what the user had
	/// asked for, which is what makes a report reproducible.
	/// </summary>
	public static void WriteException(string category, string what, Exception? ex)
	{
		lock (Gate)
		{
			if (_path.Length == 0) return;
			try
			{
				string? note = CountAndDecide(category, what);
				if (note == null) return;

				RollIfTooBig();
				File.AppendAllText(_path,
					FormatFailure(category, what + note, ex, Stamp(), Environment.CurrentManagedThreadId));
			}
			catch { }
		}
	}

	/// <summary>
	/// The text of one failure entry. Separated from the writing so it can be tested without a file, and so the
	/// crash handler — which runs when the app is already in trouble — shares exactly one formatter with
	/// everything else.
	///
	/// Every inner exception is written out. The outermost message is routinely the least informative one in the
	/// chain ("One or more errors occurred"), and a report reduced to it says nothing at all.
	/// </summary>
	public static string FormatFailure(string category, string what, Exception? ex, string stamp, int threadId)
	{
		var sb = new StringBuilder();
		string prefix = string.IsNullOrEmpty(category) ? "" : category + ": ";
		sb.Append($"[{stamp}] {prefix}FAILED: {what} (thread {threadId})").Append(Environment.NewLine);

		int depth = 0;
		for (Exception? e = ex; e != null; e = e.InnerException, depth++)
		{
			string lead = depth == 0 ? "  " : "  caused by: ";
			sb.Append(lead).Append(e.GetType().FullName).Append(": ").Append(e.Message).Append(Environment.NewLine);

			if (!string.IsNullOrEmpty(e.StackTrace))
				foreach (string frame in e.StackTrace.Split('\n'))
					if (frame.Trim().Length > 0)
						sb.Append("    ").Append(frame.TrimEnd('\r').Trim()).Append(Environment.NewLine);

			// An AggregateException's real causes are its InnerExceptions, and following InnerException alone
			// reaches only the first of them — which on a failed batch is one arbitrary mod out of ten.
			if (e is AggregateException agg && agg.InnerExceptions.Count > 1)
			{
				for (int i = 1; i < agg.InnerExceptions.Count; i++)
					sb.Append("  also: ").Append(agg.InnerExceptions[i].GetType().FullName)
						.Append(": ").Append(agg.InnerExceptions[i].Message).Append(Environment.NewLine);
			}
		}

		if (ex == null) sb.Append("  (no exception object was supplied)").Append(Environment.NewLine);

		return sb.ToString();
	}

	/// <summary>
	/// Moves the log aside once it is too big, keeping exactly one previous file.
	///
	/// Deleting outright would throw away the session before the one being reported, which is often where the
	/// cause is: something failed quietly last time and the visible break came after. Keeping one generation is
	/// the compromise between that and a file nobody can send.
	/// </summary>
	private static void RollIfTooBig()
	{
		try
		{
			if (_path.Length == 0 || !File.Exists(_path)) return;
			if (new FileInfo(_path).Length < MaxBytes) return;

			string previous = PreviousPath(_path);
			if (File.Exists(previous)) File.Delete(previous);
			File.Move(_path, previous);
		}
		catch { /* rolling is housekeeping; failing at it must not stop the entry being written */ }
	}

	/// <summary>
	/// A full date and time, in a fixed format regardless of the machine's locale.
	///
	/// Not the bare <c>HH:mm:ss</c> this used to write: a log reaches whoever is diagnosing it days later and
	/// often covers several sessions, and "14:07:02" cannot be placed against "it broke on Tuesday".
	/// </summary>
	private static string Stamp() =>
		DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
