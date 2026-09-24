using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// What the game printed to its error output, kept so that a crash can be explained rather than merely noticed.
///
/// <para>
/// ⚠️ The first modpack launch died in three seconds with "duplicate ASM classes found on classpath", and the
/// player heard only "game running" and then "game closed". Java had said exactly what was wrong — to an error
/// stream nobody was reading. A crash that early happens before Minecraft opens its own log, so this is the only
/// place the reason ever exists.
/// </para>
///
/// <para>
/// Only the error stream is kept, and only its last lines. The game's ordinary output is its log, which is
/// thousands of lines long and already written to disk; the error stream is short, and on a crash it is the
/// stack trace.
/// </para>
/// </summary>
public sealed class MinecraftErrorOutput
{
	/// <summary>How many lines are kept. A stack trace is a few dozen; this is room for one and its causes.</summary>
	public const int Keep = 200;

	/// <summary>The longest a summary is allowed to be when read aloud.</summary>
	public const int SummaryLength = 400;

	private readonly Queue<string> _lines = new();
	private readonly object _gate = new();

	/// <summary>Adds one line. Safe from the reading thread; a <c>null</c> (end of stream) is ignored.</summary>
	public void Add(string? line)
	{
		if (line is null) return;

		lock (_gate)
		{
			_lines.Enqueue(line);
			while (_lines.Count > Keep) _lines.Dequeue();
		}
	}

	/// <summary>The lines kept so far, oldest first.</summary>
	public IReadOnlyList<string> Lines
	{
		get { lock (_gate) return _lines.ToList(); }
	}

	/// <summary>
	/// Whether what Java printed — or wrote into its <c>hs_err_pid</c> crash file — says it ran out of memory.
	///
	/// <para>
	/// The accessibility modpack's second crash: Java asked Windows for another gigabyte while loading, and Windows had
	/// none to give, because another program was holding 58 GB. Java's only word on the error stream was
	/// "os::commit_memory(...) failed; error='The paging file is too small for this operation to complete'" — no
	/// "Exception", no "Error", so the summary said nothing. Out of memory is the one cause a player can act on
	/// directly, by closing things, so it is recognised by name.
	/// </para>
	/// </summary>
	public static bool IsOutOfMemory(IEnumerable<string> lines) =>
		lines.Any(l =>
			l.Contains("paging file is too small", StringComparison.OrdinalIgnoreCase) ||
			l.Contains("insufficient memory for the Java Runtime", StringComparison.OrdinalIgnoreCase) ||
			l.Contains("java.lang.OutOfMemoryError", StringComparison.Ordinal) ||
			l.Contains("os::commit_memory", StringComparison.Ordinal));

	private static readonly System.Text.RegularExpressions.Regex AccessToken =
		new(@"(--accessToken\s+)(?!\[)\S+", System.Text.RegularExpressions.RegexOptions.Compiled);

	/// <summary>
	/// <paramref name="text"/> with the Minecraft session token taken out.
	///
	/// <para>
	/// ⚠️ Java's crash file writes the game's whole command line into itself, and an online session's command line
	/// carries the player's sign-in token (<c>--accessToken</c>). Found in the first real crash file: a live token,
	/// sitting in a file a tester is asked to send with a bug report. It lasts about a day, and it is still a
	/// password to their account for that day.
	/// </para>
	/// </summary>
	public static string RedactSecrets(string text) =>
		string.IsNullOrEmpty(text) ? text ?? "" : AccessToken.Replace(text, "$1[removed by Kinetix]");

	/// <summary>
	/// Whether a line of the game's log is Minecraft beginning to shut down because it was asked to — Quit Game, or
	/// the window closed. It logs <c>[Render thread/INFO]: Stopping!</c> at that moment and at no other.
	///
	/// <para>
	/// ⚠️ What separates a crash from a messy exit. The accessibility modpack quits cleanly and then, on the way out,
	/// something native inside it fails fast (exit code -1073740791, a Windows fail-fast), after "Stopping!" and with
	/// nothing on the error stream. The player asked it to quit, the world was already saved, and nothing was lost —
	/// so telling them "Minecraft stopped with an error" is a false alarm, and a false alarm teaches people to
	/// ignore the real one.
	/// </para>
	/// </summary>
	public static bool IsShutdownLine(string? line) =>
		line != null && line.TrimEnd().EndsWith("]: Stopping!", StringComparison.Ordinal);

	/// <summary>
	/// The one line that says why, from a Java error stream — or <c>""</c> when it said nothing useful.
	///
	/// <para>
	/// The LAST "Caused by" is the root cause: a Java stack trace wraps each failure in the one that noticed it,
	/// and the outermost ("ExceptionInInitializerError") says only that something failed. Without a cause, the
	/// first line naming an exception or error. The Java framing — "Caused by:", "Exception in thread "main"" — is
	/// dropped, because read aloud it is noise in front of the part that means something.
	/// </para>
	/// </summary>
	public static string Summarise(IReadOnlyList<string> lines)
	{
		string? line =
			lines.LastOrDefault(l => l.TrimStart().StartsWith("Caused by:", StringComparison.Ordinal)) ??
			lines.FirstOrDefault(l => !l.TrimStart().StartsWith("at ", StringComparison.Ordinal) &&
									  (l.Contains("Exception") || l.Contains("Error")));
		if (line is null) return "";

		string text = line.Trim();
		foreach (string framing in new[] { "Caused by:", "Exception in thread \"main\"" })
			if (text.StartsWith(framing, StringComparison.Ordinal)) text = text.Substring(framing.Length).Trim();

		return text.Length > SummaryLength ? text.Substring(0, SummaryLength).TrimEnd() + "…" : text;
	}
}
