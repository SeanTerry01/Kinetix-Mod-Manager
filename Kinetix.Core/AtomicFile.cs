using System.Text;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// Writing a file so that a crash leaves either the old contents or the new, and never half of one.
///
/// <para>
/// <see cref="File.WriteAllText(string, string?)"/> empties the file and then fills it. A crash, power cut or kill in
/// between leaves it short or empty, and on the next start that read as a file with nothing in it: the API key, every
/// game path and the whole load order were gone, with nothing said. So the new contents go to a temporary file beside
/// the real one, are forced onto the disk, and only then take its place in a single step.
/// </para>
/// </summary>
public static class AtomicFile
{
	private const int Attempts = 4;
	private const int RetryDelayMs = 150;

	/// <summary>The suffix of the temporary file written beside the real one.</summary>
	public const string TemporarySuffix = ".kinetix-tmp";

	/// <summary>The suffix of the previous version, for a caller that keeps one.</summary>
	public const string BackupSuffix = ".bak";

	/// <summary>Replaces a file's contents. On failure it throws, and the file is as it was.</summary>
	/// <param name="path">The file.</param>
	/// <param name="contents">What it should now hold.</param>
	/// <param name="keepPrevious">Whether to leave the version being replaced beside it, with <see cref="BackupSuffix"/>.</param>
	public static void WriteAllText(string path, string contents, bool keepPrevious = false)
	{
		Write(path, writer => writer.Write(contents), keepPrevious);
	}

	/// <summary>Replaces a file's contents with these lines. On failure it throws, and the file is as it was.</summary>
	public static void WriteAllLines(string path, IEnumerable<string> lines)
	{
		Write(path, writer =>
		{
			foreach (string line in lines) writer.WriteLine(line);
		}, keepPrevious: false);
	}

	private static void Write(string path, Action<StreamWriter> write, bool keepPrevious)
	{
		string temporary = path + TemporarySuffix;
		try
		{
			using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
			using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
			{
				write(writer);
				writer.Flush();
				// Past the operating system's cache and onto the disk. A power cut after the swap but before this
				// reached the disk could otherwise still leave an empty file.
				stream.Flush(flushToDisk: true);
			}

			PutInPlace(temporary, path, keepPrevious);
		}
		catch
		{
			try { File.Delete(temporary); }
			catch (Exception ex) { DiagnosticLog.WriteException("Files", $"removing {temporary}", ex); }
			throw;
		}
	}

	/// <summary>Retries because an antivirus or a cloud sync can hold a just-written file for a moment.</summary>
	private static void PutInPlace(string temporary, string path, bool keepPrevious)
	{
		for (int attempt = 1; ; attempt++)
		{
			try
			{
				if (File.Exists(path)) File.Replace(temporary, path, keepPrevious ? path + BackupSuffix : null);
				else File.Move(temporary, path);
				return;
			}
			catch (IOException) when (attempt < Attempts) { Thread.Sleep(RetryDelayMs); }
		}
	}

	/// <summary>
	/// Reads a JSON file the manager keeps for itself, treating a damaged one as empty rather than as a reason to stop.
	/// The damaged file is moved aside, so the next write does not destroy whatever could still be rescued from it.
	/// </summary>
	public static JObject ReadJsonObjectOrEmpty(string path)
	{
		if (!File.Exists(path)) return new JObject();
		try
		{
			return JObject.Parse(File.ReadAllText(path));
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Files", $"reading {path}, which is damaged", ex);
			SetAside(path);
			return new JObject();
		}
	}

	/// <summary>Moves a damaged file out of the way under a name that says so, replacing any earlier damaged copy.</summary>
	public static void SetAside(string path)
	{
		try { File.Move(path, path + ".damaged", overwrite: true); }
		catch (Exception ex) { DiagnosticLog.WriteException("Files", $"setting aside the damaged file {path}", ex); }
	}
}
