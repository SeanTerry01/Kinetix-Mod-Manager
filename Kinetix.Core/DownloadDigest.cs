using System.Security.Cryptography;

namespace KinetixModManager;

/// <summary>The person asked for running downloads to stop.</summary>
public sealed class DownloadCancelledException : OperationCanceledException
{
	public DownloadCancelledException() : base("The download was cancelled.") { }
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
