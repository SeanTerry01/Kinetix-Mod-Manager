using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KinetixModManager;

/// <summary>
/// The installed version of the games that do not simply state it in their executable, read the way each game keeps
/// it — for the title bar, which says which build of a game is loaded for every game, as it long has for Skyrim and
/// Fallout 4.
///
/// <para>
/// Each game keeps its version somewhere different, and the obvious place is wrong for two of them:
/// </para>
/// <list type="bullet">
/// <item><b>Stardew Valley</b> — its own files' version, <c>1.6.15.24356</c>, of which the first three parts are the
/// release players know.</item>
/// <item><b>The Witcher 3</b> — NOT its file version, which is an internal build (<c>4.0.0.103190</c>). The version the
/// game itself goes by, <c>v 4.04c</c>, is a string inside the executable, beside <c>crashVersion</c>, the label its
/// crash reports carry. The most frequent such string is taken; the file version's first two parts are the fallback.</item>
/// <item><b>Moonlight Peaks</b> (Unity) — NOT its file version either, which is Unity's own (<c>6000.3.6</c>). The
/// game's version is the Player Settings "bundle version" in <c>&lt;Game&gt;_Data\globalgamemanagers</c>: there, after
/// the store category, come three version-shaped settings — <c>1.0</c>, <c>1.0</c>, <c>1.2.7</c> — and the last is the
/// game's, matching its published patch 1.2.7 of 25 August 2026, the day after that file's date.</item>
/// </list>
///
/// <para>
/// Every answer is cached against the file's size and date: the Witcher's executable is 40 MB, and the title bar
/// asks often. A version that cannot be read is <c>""</c>, and the title bar then shows the plain name — never a guess.
/// </para>
/// </summary>
public static class GameVersionReader
{
	private static readonly ConcurrentDictionary<string, (long Size, DateTime Written, string Version)> Cache = new();

	/// <summary>The installed version of <paramref name="game"/> in <paramref name="gameFolder"/>, or <c>""</c>.</summary>
	public static string ForGame(GameProfile game, string gameFolder)
	{
		if (string.IsNullOrEmpty(gameFolder) || !Directory.Exists(gameFolder)) return "";

		try
		{
			return game.Id switch
			{
				GameProfiles.StardewValley => Cached(Path.Combine(gameFolder, "Stardew Valley.dll"), FileVersion(3)),
				GameProfiles.Witcher3 => Cached(Witcher3Exe(gameFolder), Witcher3Version),
				GameProfiles.MoonlightPeaks => Cached(UnityDataFile(gameFolder, game.GameExeName), UnityBundleVersion),
				_ => ""
			};
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Versions", $"reading {game.DisplayName}'s version", ex);
			return "";
		}
	}

	private static string Cached(string file, Func<string, string> read)
	{
		if (file.Length == 0 || !File.Exists(file)) return "";

		var info = new FileInfo(file);
		if (Cache.TryGetValue(file, out var hit) && hit.Size == info.Length && hit.Written == info.LastWriteTimeUtc)
			return hit.Version;

		string version = read(file);
		Cache[file] = (info.Length, info.LastWriteTimeUtc, version);
		return version;
	}

	/// <summary>A reader returning the first <paramref name="parts"/> parts of a file's own version.</summary>
	private static Func<string, string> FileVersion(int parts) => file =>
	{
		FileVersionInfo info = FileVersionInfo.GetVersionInfo(file);
		int[] numbers = { info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart };
		return numbers.All(n => n == 0) ? "" : string.Join(".", numbers.Take(parts));
	};

	// -------------------------------------------------------------------------
	// The Witcher 3
	// -------------------------------------------------------------------------

	private static string Witcher3Exe(string gameFolder) =>
		new[] { Path.Combine(gameFolder, "bin", "x64", "witcher3.exe"), Path.Combine(gameFolder, "bin", "x64_dx12", "witcher3.exe") }
			.FirstOrDefault(File.Exists) ?? "";

	private static readonly Regex Witcher3VersionString = new(@"v (\d+\.\d{2}[a-z]?)(?=[^0-9A-Za-z.])", RegexOptions.Compiled);

	private static string Witcher3Version(string exe)
	{
		string found = VersionFromWitcherExecutable(File.ReadAllBytes(exe));
		return found.Length > 0 ? found : FileVersion(2)(exe);
	}

	/// <summary>
	/// The game's own version string from the executable's bytes — the most frequent <c>v 4.04c</c>-shaped string, or
	/// <c>""</c>. Most frequent, because an older one can linger in a single place (this build also holds one
	/// <c>v 4.04</c>) while the current one is used wherever the game labels itself.
	/// </summary>
	public static string VersionFromWitcherExecutable(byte[] executable)
	{
		string text = Encoding.Latin1.GetString(executable);
		return Witcher3VersionString.Matches(text)
			.Select(m => m.Groups[1].Value)
			.GroupBy(v => v)
			.OrderByDescending(g => g.Count())
			.Select(g => g.Key)
			.FirstOrDefault() ?? "";
	}

	// -------------------------------------------------------------------------
	// Unity games
	// -------------------------------------------------------------------------

	private static string UnityDataFile(string gameFolder, string exeName)
	{
		string stem = Path.GetFileNameWithoutExtension(exeName);
		if (stem.Length == 0) return "";
		string data = Path.Combine(gameFolder, stem + "_Data");
		return new[] { Path.Combine(data, "globalgamemanagers"), Path.Combine(data, "mainData") }.FirstOrDefault(File.Exists) ?? "";
	}

	private static string UnityBundleVersion(string file)
	{
		// The player settings are near the front; reading all of a large file for them would be waste.
		using FileStream stream = File.OpenRead(file);
		var head = new byte[Math.Min(stream.Length, 256 * 1024)];
		int read = stream.Read(head, 0, head.Length);
		return BundleVersionFromUnityData(head.AsSpan(0, read).ToArray());
	}

	private static readonly Regex VersionShaped = new(@"^\d+(\.\d+)+[A-Za-z0-9._-]*$", RegexOptions.Compiled);

	/// <summary>
	/// The game's version from the start of a Unity <c>globalgamemanagers</c>: the last of the run of version-shaped
	/// strings that follows the store category (<c>public.app-category.…</c>), or <c>""</c>. Unity writes a string as
	/// a little-endian length and the bytes, padded to four.
	/// </summary>
	public static string BundleVersionFromUnityData(byte[] data)
	{
		List<string> strings = UnityStrings(data);
		int category = strings.FindIndex(s => s.StartsWith("public.app-category.", StringComparison.Ordinal));
		if (category < 0) return "";

		string version = "";
		foreach (string s in strings.Skip(category + 1))
		{
			if (!VersionShaped.IsMatch(s)) break;
			version = s;
		}
		return version;
	}

	/// <summary>Every length-prefixed printable string Unity has written, in order.</summary>
	private static List<string> UnityStrings(byte[] data)
	{
		var found = new List<string>();
		for (int i = 0; i + 4 <= data.Length; )
		{
			int length = BitConverter.ToInt32(data, i);
			if (length is >= 1 and <= 200 && i + 4 + length <= data.Length &&
				data.AsSpan(i + 4, length).ToArray().All(b => b is >= 32 and < 127))
			{
				found.Add(Encoding.ASCII.GetString(data, i + 4, length));
				i = (i + 4 + length + 3) & ~3;
				continue;
			}
			i += 4;
		}
		return found;
	}
}
