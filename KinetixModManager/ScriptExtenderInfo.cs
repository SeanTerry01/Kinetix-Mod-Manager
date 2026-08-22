using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KinetixModManager;

/// <summary>
/// What the manager can see about an installed script extender (SKSE on Skyrim SE, F4SE on Fallout 4).
///
/// Neither extender appears in the mod list — they are loose files in the game folder, installed by the
/// Accessibility Suite rather than as mods — so without this the user has no way to ask what is installed.
/// The extender's own version (F4SE 0.7.9) and the game build it was compiled against (1.11.240) are two
/// different numbers, and it is the second one that decides whether it loads at all.
/// </summary>
public sealed class ScriptExtenderStatus
{
	/// <summary>"SKSE" or "F4SE".</summary>
	public string Name { get; init; } = "";

	/// <summary>The extender's own version, e.g. "0.7.9" or "2.2.6"; "" when the loader can't be read.</summary>
	public string ProductVersion { get; init; } = "";

	/// <summary>The game's runtime build, e.g. "1.11.240"; "" when the game exe can't be read (e.g. under Wine).</summary>
	public string GameVersion { get; init; } = "";

	/// <summary>
	/// The game build the extender that will actually load is compiled for. When a build matching the game is
	/// installed this is that build; otherwise it is the newest one present. "" when no versioned DLL was found.
	/// </summary>
	public string TargetVersion { get; init; } = "";

	/// <summary>Every game build with an extender DLL present, newest first — usually one, more after an update.</summary>
	public IReadOnlyList<string> InstalledBuilds { get; init; } = Array.Empty<string>();

	/// <summary>True when a DLL for the game's own build is installed, so the extender will load.</summary>
	public bool Match { get; init; }

	/// <summary>True when both versions were readable, so <see cref="Match"/> means something.</summary>
	public bool CanCompare => GameVersion.Length > 0 && TargetVersion.Length > 0;

	/// <summary>The other builds sitting in the game folder besides the one that will load; usually empty.</summary>
	public IReadOnlyList<string> OtherBuilds =>
		InstalledBuilds.Where(b => !string.Equals(b, TargetVersion, StringComparison.Ordinal)).ToList();
}

/// <summary>
/// Reads the installed script extender's versions out of the game folder.
///
/// The rule that matters: SKSE/F4SE ship one DLL per game build, named for the build it targets
/// (<c>f4se_1_11_240.dll</c>), and the loader loads only the one matching the running exe. Installing a new
/// extender therefore leaves the previous build's DLL behind — nothing removes it, and nothing needs to: it is
/// simply never loaded again. That harmless leftover is why this looks at <em>every</em> versioned DLL present
/// rather than the first one it happens to enumerate, which is how a correctly updated F4SE was reported as a
/// mismatch against the very build it was installed for.
/// </summary>
public static class ScriptExtenderInfo
{
	/// <summary>The script extender's loader exe name for a game, or "" for games without one.</summary>
	public static string LoaderName(string? activeGame) => GameProfiles.BaseId(activeGame) switch
	{
		"SkyrimSE" => "skse64_loader.exe",
		"Fallout4" => "f4se_loader.exe",
		_          => ""
	};

	/// <summary>The prefix the versioned extender DLLs are named with, or "" for games without one.</summary>
	public static string DllPrefix(string? activeGame) => GameProfiles.BaseId(activeGame) switch
	{
		"SkyrimSE" => "skse64_",
		"Fallout4" => "f4se_",
		_          => ""
	};

	/// <summary>"SKSE" / "F4SE", or "" for a game with no script extender.</summary>
	public static string DisplayName(string? activeGame) => GameProfiles.BaseId(activeGame) switch
	{
		"SkyrimSE" => "SKSE",
		"Fallout4" => "F4SE",
		_          => ""
	};

	/// <summary>
	/// The game build a versioned extender DLL targets, from its file name — "f4se_1_11_240.dll" is built for
	/// 1.11.240. Returns null for the extender's other DLLs, whose names carry no version
	/// (<c>f4se_steam_loader.dll</c>), and for anything else sharing the prefix.
	/// </summary>
	public static string? RuntimeBuildFromDllName(string fileName, string prefix)
	{
		if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(fileName)) return null;
		string stem = Path.GetFileNameWithoutExtension(fileName);
		if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

		Match m = Regex.Match(stem.Substring(prefix.Length), @"^(\d+)_(\d+)_(\d+)$");
		return m.Success ? $"{m.Groups[1].Value}.{m.Groups[2].Value}.{m.Groups[3].Value}" : null;
	}

	/// <summary>
	/// Which of the installed builds the loader will actually use, given the game's own build. A build matching
	/// the game wins outright — that is the one that loads, whatever else is lying around. With no match the
	/// newest build present is reported, because that is the one the user most recently installed and the one
	/// the mismatch message should name. Returns ("", false) when nothing versioned is installed.
	/// </summary>
	public static (string Target, bool Match) ChooseBuild(string gameVersion, IEnumerable<string> installedBuilds)
	{
		List<string> builds = installedBuilds?.Where(b => !string.IsNullOrEmpty(b)).ToList() ?? new List<string>();
		if (builds.Count == 0) return ("", false);

		if (!string.IsNullOrEmpty(gameVersion) &&
			builds.Any(b => string.Equals(b, gameVersion, StringComparison.Ordinal)))
			return (gameVersion, true);

		return (SortNewestFirst(builds)[0], false);
	}

	/// <summary>Build strings ("1.11.240") ordered newest first; unparsable entries sort last, alphabetically.</summary>
	public static List<string> SortNewestFirst(IEnumerable<string> builds) =>
		builds.OrderByDescending(b => ParseBuild(b) ?? new Version(0, 0, 0))
			.ThenBy(b => b, StringComparer.OrdinalIgnoreCase)
			.ToList();

	private static Version? ParseBuild(string build) =>
		Version.TryParse(build, out Version? v) ? v : null;

	/// <summary>
	/// The extender's own version as it is written on its download page, from the loader exe's four version
	/// parts. Both extenders keep a leading zero major part and put their real version in the parts after it:
	/// F4SE 0.7.9 ships as 0.0.7.9 and SKSE 2.2.6 as 0.2.2.6. Anything with a real major part is read normally.
	/// Returns "" when the exe carries no version at all.
	/// </summary>
	public static string FormatProductVersion(int major, int minor, int build, int revision)
	{
		if (major == 0 && minor == 0 && build == 0 && revision == 0) return "";
		return major == 0 ? $"{minor}.{build}.{revision}" : $"{major}.{minor}.{build}";
	}

	/// <summary>
	/// Inspects <paramref name="gamePath"/> and reports what script extender is installed there. Returns null
	/// only when the question does not apply — a game with no script extender, no game folder, or no loader
	/// installed. Otherwise it returns whatever it could read: an unreadable game exe or a missing versioned DLL
	/// leaves those fields empty and <see cref="ScriptExtenderStatus.CanCompare"/> false, rather than guessing.
	/// </summary>
	public static ScriptExtenderStatus? Read(string? activeGame, string? gamePath)
	{
		string loader = LoaderName(activeGame);
		if (loader.Length == 0 || string.IsNullOrEmpty(gamePath)) return null;

		string loaderPath = Path.Combine(gamePath, loader);
		if (!File.Exists(loaderPath)) return null;

		string productVersion = "";
		try
		{
			FileVersionInfo vi = FileVersionInfo.GetVersionInfo(loaderPath);
			productVersion = FormatProductVersion(vi.FileMajorPart, vi.FileMinorPart, vi.FileBuildPart, vi.FilePrivatePart);
		}
		catch { /* the version block is a bonus; the install is still detected without it */ }

		string gameVersion = "";
		try
		{
			string gameExePath = Path.Combine(gamePath, GameProfiles.Require(GameProfiles.BaseId(activeGame)).GameExeName);
			if (File.Exists(gameExePath))
			{
				FileVersionInfo vi = FileVersionInfo.GetVersionInfo(gameExePath);
				if (vi.FileMajorPart != 0 || vi.FileMinorPart != 0 || vi.FileBuildPart != 0)
					gameVersion = $"{vi.FileMajorPart}.{vi.FileMinorPart}.{vi.FileBuildPart}";
			}
		}
		catch { /* unreadable exe (e.g. under Wine) — leave the comparison unmade */ }

		string prefix = DllPrefix(activeGame);
		var builds = new List<string>();
		try
		{
			foreach (string dll in Directory.EnumerateFiles(gamePath, prefix + "*.dll", SearchOption.TopDirectoryOnly))
			{
				string? build = RuntimeBuildFromDllName(Path.GetFileName(dll), prefix);
				if (build != null && !builds.Contains(build)) builds.Add(build);
			}
		}
		catch { /* unreadable folder — same as none found */ }

		(string target, bool match) = ChooseBuild(gameVersion, builds);

		return new ScriptExtenderStatus
		{
			Name            = DisplayName(activeGame),
			ProductVersion  = productVersion,
			GameVersion     = gameVersion,
			TargetVersion   = target,
			InstalledBuilds = SortNewestFirst(builds),
			Match           = match
		};
	}
}
