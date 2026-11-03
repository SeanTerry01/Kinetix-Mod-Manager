using System.Security.Cryptography;

namespace KinetixModManager;

/// <summary>The person asked for running downloads to stop.</summary>
/// <remarks>
/// Not an <see cref="OperationCanceledException"/>. Install paths catch that as "the FOMOD wizard was cancelled", and carry on to
/// the next mod.
/// </remarks>
public sealed class DownloadCancelledException : Exception
{
	public DownloadCancelledException() : base("The download was cancelled.") { }
}

/// <summary>
/// Which downloads can be stopped. Shared, because they start from two places: the Nexus service and Modrinth's static calls.
/// </summary>
public static class RunningDownloads
{
	private static CancellationTokenSource _cancel = new();
	private static int _running;

	/// <summary>What a download about to start should watch. Read once, so a later cancel's replacement doesn't free it.</summary>
	public static CancellationToken Token => Volatile.Read(ref _cancel).Token;

	public static bool Any => Volatile.Read(ref _running) > 0;

	public static void Started() => Interlocked.Increment(ref _running);
	public static void Finished() => Interlocked.Decrement(ref _running);

	/// <summary>Stops every download running now. Later ones aren't affected.</summary>
	public static void CancelAll() => Interlocked.Exchange(ref _cancel, new CancellationTokenSource()).Cancel();
}

/// <summary>A download arrived, and it is not the file the site says it published.</summary>
public sealed class DownloadMismatchException : IOException
{
	public DownloadMismatchException(string fileName) : base($"{fileName} did not match the checksum its site published.") { }
}

/// <summary>Whether a downloaded file is the one its site published.</summary>
public enum DigestResult
{
	/// <summary>The site gave a checksum and the file matches it.</summary>
	Matches,

	/// <summary>The site gave none, or one in a form this doesn't read. Nothing is known either way.</summary>
	NotPublished,

	/// <summary>The site gave a checksum and the file does not match it.</summary>
	Differs
}

/// <summary>
/// Compares a file with the checksum GitHub publishes for a release asset, written <c>sha256:hex</c>.
///
/// This catches a file damaged or swapped on its way from the storage host. It does not prove who made the release: the
/// checksum comes from the same account as the file, so someone who took over that account could change both.
/// </summary>
public static class DownloadDigest
{
	public static DigestResult Check(string path, string? digest)
	{
		if (string.IsNullOrWhiteSpace(digest)) return DigestResult.NotPublished;

		const string prefix = "sha256:";
		if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return DigestResult.NotPublished;

		string expected = digest[prefix.Length..].Trim();
		if (expected.Length != SHA256.HashSizeInBytes * 2) return DigestResult.NotPublished;

		using FileStream file = File.OpenRead(path);
		string actual = Convert.ToHexString(SHA256.HashData(file));
		return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) ? DigestResult.Matches : DigestResult.Differs;
	}
}
