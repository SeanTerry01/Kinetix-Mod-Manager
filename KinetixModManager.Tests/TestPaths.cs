using System;
using System.IO;
using System.Linq;

namespace KinetixModManager.Tests;

/// <summary>
/// Rooted paths for the tests that have to name one, spelled the way the host spells a path.
///
/// Tests used to write these as Windows literals — <c>@"D:\SteamLibrary\steamapps\common\..."</c> — which
/// was fine while the suite only ever ran on Windows. It does not any more: the core builds and runs on
/// Linux, and there a backslash is an ordinary character rather than a separator, so
/// <c>Path.GetDirectoryName</c> hands back the whole string and <c>Path.Combine</c> produces
/// <c>C:\/GOG Games</c>. Sixteen tests failed on exactly that, and none of them was finding a real bug —
/// they were asserting the separator rather than the behaviour.
///
/// So these build the path from segments and let <see cref="Path.Combine"/> spell the separator. What the
/// tests are actually about — which folder a mod lands in, how a root and a relative path compose — is the
/// same on both, and is now what they check.
///
/// A test that is genuinely about Windows spelling should say so with its own literal rather than use this.
/// </summary>
internal static class TestPaths
{
	/// <summary>
	/// The root of a notional drive: <c>D:\</c> on Windows, <c>/d</c> elsewhere.
	///
	/// The letter survives off Windows because several of these tests are about telling one root from
	/// another — a GOG library that outgrew the system drive, a mod installed somewhere else entirely — and
	/// they need two roots that differ. It is a stand-in for "a second place a game might live", not a
	/// claim that Linux has drive letters.
	/// </summary>
	public static string DriveRoot(char letter) =>
		OperatingSystem.IsWindows()
			? $@"{char.ToUpperInvariant(letter)}:\"
			: $"/{char.ToLowerInvariant(letter)}";

	/// <summary>An absolute path under <see cref="DriveRoot"/>, with the host's separator between segments.</summary>
	public static string Under(char driveLetter, params string[] segments) =>
		Path.Combine(new[] { DriveRoot(driveLetter) }.Concat(segments).ToArray());
}
