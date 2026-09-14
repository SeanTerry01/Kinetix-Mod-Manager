using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KinetixModManager;

/// <summary>
/// Follows Minecraft's <c>logs/latest.log</c> while the game runs, handing each new line to a reader.
///
/// <para>
/// Polled rather than watched. A <c>FileSystemWatcher</c> reports that a file changed, not what was added,
/// so the read below has to happen anyway — and the game appends to this file constantly, which turns change
/// notifications into a stream of wake-ups that do no work. Half a second is far inside the reaction time of
/// a sound cue and costs a seek on an already-open handle.
/// </para>
///
/// <para>
/// Opened with <see cref="FileShare.ReadWrite"/> and <see cref="FileShare.Delete"/> together, which is not
/// optional: the game holds this file open for writing and rolls it over, and any narrower share mode either
/// fails to open it or stops the game writing to its own log.
/// </para>
/// </summary>
public sealed class MinecraftLogTail
{
	/// <summary>How much to take in one poll. The rest waits for the next one rather than being allocated.</summary>
	private const int MaxChunkBytes = 1024 * 1024;

	private readonly string _path;
	private readonly TimeSpan _interval;
	private long _position;

	/// <param name="logPath">Full path to <c>latest.log</c>. It need not exist yet.</param>
	/// <param name="pollInterval">How often to look for new lines. Defaults to half a second.</param>
	/// <remarks>
	/// Starts at the END of whatever is already there, and that is the important part. The previous run's
	/// log is still on disk at the moment the game is started, and it very likely ends with the player
	/// joining a server — so reading from the beginning would announce a connection to a server they left
	/// yesterday. The game truncates this file as it opens it, which this notices (the length drops below
	/// where we are) and reads the new run from its start.
	/// </remarks>
	public MinecraftLogTail(string logPath, TimeSpan? pollInterval = null)
	{
		_path = logPath;
		_interval = pollInterval ?? TimeSpan.FromMilliseconds(500);

		try { if (File.Exists(_path)) _position = new FileInfo(_path).Length; }
		catch (Exception ex) { DiagnosticLog.WriteException("Minecraft", $"measuring {_path} before following it", ex); }
	}

	/// <summary>
	/// Reads until <paramref name="token"/> is cancelled, calling <paramref name="onLine"/> for each new line.
	///
	/// Returns rather than throwing when the log cannot be read — a mod manager that fell over because a log
	/// was briefly locked would be worse than one that plays no cue.
	/// </summary>
	public async Task RunAsync(Action<string> onLine, CancellationToken token)
	{
		try
		{
			while (!token.IsCancellationRequested)
			{
				foreach (string line in ReadNewLines()) onLine(line);
				await Task.Delay(_interval, token).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) { /* the game stopped, which is how this always ends */ }
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", $"following {_path}", ex);
		}
	}

	/// <summary>
	/// Whatever has been appended since the last call. Empty when there is nothing new, no file yet, or the
	/// file cannot be opened this time round — all three are ordinary during a launch.
	/// </summary>
	public IReadOnlyList<string> ReadNewLines()
	{
		try
		{
			if (!File.Exists(_path)) return Array.Empty<string>();

			using var stream = new FileStream(
				_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

			// A shorter file than last time is a new one: the game rolls latest.log over on launch, and
			// seeking to a stale offset would skip the whole of the new run.
			if (stream.Length < _position) _position = 0;
			if (stream.Length == _position) return Array.Empty<string>();

			stream.Seek(_position, SeekOrigin.Begin);

			int wanted = (int)Math.Min(stream.Length - _position, MaxChunkBytes);
			byte[] buffer = new byte[wanted];
			int read = stream.Read(buffer, 0, wanted);
			if (read <= 0) return Array.Empty<string>();

			// Only complete lines. The game is writing to this file as we read it, so the tail of the chunk
			// is very often half a line — emitting that would hand the parser a truncated message and then
			// never show it the rest. Cutting at a newline is safe byte-wise too: 0x0A cannot appear inside
			// a UTF-8 multi-byte sequence.
			int lastNewline = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
			if (lastNewline < 0) return Array.Empty<string>();

			_position += lastNewline + 1;
			return Encoding.UTF8.GetString(buffer, 0, lastNewline + 1)
				.Split('\n', StringSplitOptions.RemoveEmptyEntries)
				.Select(l => l.TrimEnd('\r'))
				.ToList();
		}
		catch (IOException)
		{
			// The game had it locked for a moment. There will be another poll along shortly.
			return Array.Empty<string>();
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", $"reading new lines from {_path}", ex);
			return Array.Empty<string>();
		}
	}
}
