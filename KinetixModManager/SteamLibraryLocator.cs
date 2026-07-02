using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace KinetixModManager;

/// <summary>
/// Locates a Steam game's install folder by reading Steam's own library records (libraryfolders.vdf
/// and the per-game appmanifest .acf), so games on any drive or in a custom-named library folder are
/// found. Self-contained (BCL only) so it can be unit-tested without the WinForms app. The registry
/// lookup that finds Steam itself lives in the caller, since it is Windows-specific.
/// </summary>
public static class SteamLibraryLocator
{
	/// <summary>
	/// Returns the install folder for the Steam app with <paramref name="appId"/>, or <c>null</c> when
	/// Steam's library records don't list it (e.g. it isn't installed, or under Proton where Steam runs
	/// natively on Linux and leaves no records in the Wine prefix).
	/// </summary>
	public static string? FindGameFolder(string steamPath, string appId)
	{
		if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath)) return null;

		// libraryfolders.vdf lives under steamapps on current clients; older ones kept it in config.
		string[] vdfCandidates =
		{
			Path.Combine(steamPath, "steamapps", "libraryfolders.vdf"),
			Path.Combine(steamPath, "config", "libraryfolders.vdf")
		};

		foreach (string vdf in vdfCandidates)
		{
			if (!File.Exists(vdf)) continue;
			foreach (string library in ParseLibraryFolders(File.ReadAllText(vdf)))
			{
				string manifest = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
				if (!File.Exists(manifest)) continue;
				string? installDir = ParseInstallDir(File.ReadAllText(manifest));
				if (string.IsNullOrEmpty(installDir)) continue;
				string gameFolder = Path.Combine(library, "steamapps", "common", installDir);
				if (Directory.Exists(gameFolder)) return gameFolder;
			}
			break; // Only the first existing vdf is authoritative.
		}

		return null;
	}

	/// <summary>Extracts every library folder path listed in libraryfolders.vdf content.</summary>
	public static List<string> ParseLibraryFolders(string vdfContent)
	{
		var result = new List<string>();
		if (string.IsNullOrEmpty(vdfContent)) return result;

		// Current format: each library is an object with a "path" "<value>" entry.
		foreach (Match m in Regex.Matches(vdfContent, "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
		{
			string p = m.Groups[1].Value.Replace("\\\\", "\\");
			if (!string.IsNullOrEmpty(p) && !result.Contains(p)) result.Add(p);
		}

		// Legacy format (pre-2021): numeric keys map directly to a path string. Only fall back to this
		// when the modern "path" form found nothing, so we don't mistake the "apps" size table
		// (appid -> bytes) for library paths.
		if (result.Count == 0)
		{
			foreach (Match m in Regex.Matches(vdfContent, "^\\s*\"\\d+\"\\s*\"([^\"]+)\"", RegexOptions.Multiline))
			{
				string p = m.Groups[1].Value.Replace("\\\\", "\\");
				if (!string.IsNullOrEmpty(p) && !result.Contains(p)) result.Add(p);
			}
		}

		return result;
	}

	/// <summary>Reads the "installdir" value from appmanifest (.acf) content, or <c>null</c> if absent.</summary>
	public static string? ParseInstallDir(string acfContent)
	{
		if (string.IsNullOrEmpty(acfContent)) return null;
		Match m = Regex.Match(acfContent, "\"installdir\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
		return m.Success ? m.Groups[1].Value : null;
	}
}
