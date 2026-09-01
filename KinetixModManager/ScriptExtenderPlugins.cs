using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KinetixModManager;

/// <summary>Why the script extender refused to start a DLL plugin.</summary>
public enum PluginFailureKind
{
	/// <summary>The Address Library has no data for the game build being run, so plugins that use it cannot start.
	/// After a game update this is usually every one of them at once, and it is fixed by the Address Library's
	/// author publishing a build for the new version — not by anything the user can do.</summary>
	AddressLibrary,

	/// <summary>The plugin itself is compiled for a different game build. Fixed by its own author.</summary>
	GameVersion,

	/// <summary>Something else the script extender objected to; the reason is passed through as written.</summary>
	Other
}

/// <summary>One DLL plugin the script extender scanned and refused to load.</summary>
public sealed class PluginLoadFailure
{
	/// <summary>The file as it sits in the plugins folder, e.g. "fallout4access.dll".</summary>
	public required string PluginFile { get; init; }

	/// <summary>The script extender's own words for why, e.g. "address library needs to be updated".</summary>
	public required string Reason { get; init; }

	public required PluginFailureKind Kind { get; init; }

	/// <summary>The plugin's name without the extension — the closest thing the log gives to a mod name.</summary>
	public string Name => Path.GetFileNameWithoutExtension(PluginFile);
}

/// <summary>What the Address Library installed in the game folder can and cannot cover.</summary>
public sealed class AddressLibraryStatus
{
	/// <summary>True when at least one Address Library database is present at all.</summary>
	public bool Installed => CoveredBuilds.Count > 0;

	/// <summary>The game build being run, or "" when it could not be read.</summary>
	public required string GameVersion { get; init; }

	/// <summary>Every game build there is a database for, newest first.</summary>
	public required IReadOnlyList<string> CoveredBuilds { get; init; }

	/// <summary>True when a database for the running game build is present, so plugins that use it can start.</summary>
	public bool CoversGame { get; init; }

	/// <summary>The newest build covered — what to name when the running one is not among them.</summary>
	public string NewestBuild => CoveredBuilds.Count > 0 ? CoveredBuilds[0] : "";

	/// <summary>True when the question was answerable: something is installed and the game build is known.</summary>
	public bool CanJudge => Installed && GameVersion.Length > 0;
}

/// <summary>
/// Why a modded Bethesda game can launch perfectly and still do nothing.
///
/// The manager already checks that SKSE/F4SE matches the game. That is only the first link. The real chain is
/// <c>game → script extender → Address Library → DLL plugins → the mods that rely on them</c>, and a break
/// anywhere below the script extender is <em>silent</em>: the game starts, the script extender loads, and the
/// plugins are quietly skipped. For a screen-reader user the whole symptom is that the accessibility mod says
/// nothing, with no error anywhere to explain it.
///
/// This reads the two things that can explain it. The <b>Address Library</b> ships one database file per game
/// build, and after a game update there is simply no file for the new build until its author publishes one — at
/// which point every plugin that uses it stops loading, through no fault of the user's install. That can be
/// checked before the game is ever started. The <b>script extender's log</b> says exactly which plugins were
/// refused and why, though only for the last time the game ran.
///
/// Recorded from the machine that prompted this: Fallout 4 updated to 1.11.240 on 2026-08-18 and F4SE 0.7.9
/// followed the same day, so the manager's script-extender check was satisfied and said nothing — while
/// Buffout4AE, CrashLoggerAE and fallout4access were all disabled for want of an Address Library newer than the
/// 1.11.221 one that was the latest in existence, and MCM and XDI were disabled as builds for the older game.
/// </summary>
public static class ScriptExtenderPlugins
{
	/// <summary>The game folder's DLL-plugin directory — <c>Data\F4SE\Plugins</c> or <c>Data\SKSE\Plugins</c>.
	/// This is the deployed game folder, which is what the game actually reads, not the manager's mod store.</summary>
	public static string PluginsFolder(string? activeGame, string? gamePath)
	{
		string se = ScriptExtenderInfo.DisplayName(activeGame);
		return se.Length == 0 || string.IsNullOrEmpty(gamePath) ? "" : Path.Combine(gamePath, "Data", se, "Plugins");
	}

	/// <summary>
	/// The game build an Address Library database file is for, or null if the name is not one.
	///
	/// Both spellings are in use and both turn up in the same folder: Fallout 4 and older Skyrim SE use
	/// <c>version-1-11-221-0.bin</c>, while Skyrim's Anniversary Edition files are <c>versionlib-1-6-1170-0.bin</c>.
	/// A trailing counter (<c>versionlib-1-6-1170-0-1.bin</c>) appears where a build was republished, and the
	/// data also ships as <c>.csv</c> for some builds.
	/// </summary>
	public static string? BuildFromVersionFileName(string fileName)
	{
		if (string.IsNullOrEmpty(fileName)) return null;
		string stem = Path.GetFileNameWithoutExtension(fileName);
		string ext = Path.GetExtension(fileName);
		if (!ext.Equals(".bin", StringComparison.OrdinalIgnoreCase) &&
			!ext.Equals(".csv", StringComparison.OrdinalIgnoreCase)) return null;

		Match m = Regex.Match(stem, @"^version(?:lib)?-(\d+)-(\d+)-(\d+)-\d+(?:-\d+)?$", RegexOptions.IgnoreCase);
		return m.Success ? $"{m.Groups[1].Value}.{m.Groups[2].Value}.{m.Groups[3].Value}" : null;
	}

	/// <summary>Which game builds the Address Library in <paramref name="pluginsFolder"/> has data for, newest
	/// first. An empty list means it is not installed — which is a different problem, and one already reported.</summary>
	public static List<string> InstalledAddressLibraryBuilds(string pluginsFolder)
	{
		var builds = new List<string>();
		if (string.IsNullOrEmpty(pluginsFolder) || !Directory.Exists(pluginsFolder)) return builds;

		try
		{
			foreach (string file in Directory.EnumerateFiles(pluginsFolder, "version*", SearchOption.TopDirectoryOnly))
			{
				string? build = BuildFromVersionFileName(Path.GetFileName(file));
				if (build != null && !builds.Contains(build)) builds.Add(build);
			}
		}
		catch { /* unreadable folder reads the same as nothing installed */ }

		return ScriptExtenderInfo.SortNewestFirst(builds);
	}

	/// <summary>Reads the Address Library's coverage of the running game build out of the game folder.</summary>
	public static AddressLibraryStatus ReadAddressLibrary(string? activeGame, string? gamePath, string gameVersion)
	{
		List<string> builds = InstalledAddressLibraryBuilds(PluginsFolder(activeGame, gamePath));
		return new AddressLibraryStatus
		{
			GameVersion   = gameVersion ?? "",
			CoveredBuilds = builds,
			CoversGame    = gameVersion?.Length > 0 && builds.Contains(gameVersion, StringComparer.Ordinal)
		};
	}

	/// <summary>
	/// The plugins the script extender refused, read from its log. SKSE and F4SE write the same lines:
	/// <c>plugin X.dll (00000001 Some Name 01070010) disabled, &lt;reason&gt; 0 (handle 0)</c> for a refusal and
	/// <c>… loaded correctly (handle 3)</c> for a success.
	///
	/// Only an explicit "disabled" is treated as a failure. A plugin folder also holds support DLLs that are not
	/// plugins at all — <c>msdia140.dll</c>, shipped by the crash loggers — which the script extender notes as
	/// having "no version data". That is normal, and reporting it would be crying wolf about a working install.
	/// </summary>
	public static List<PluginLoadFailure> ParseLoadFailures(IEnumerable<string> logLines)
	{
		var failures = new List<PluginLoadFailure>();
		if (logLines == null) return failures;

		foreach (string raw in logLines)
		{
			string line = raw?.Trim() ?? "";
			if (!line.StartsWith("plugin ", StringComparison.OrdinalIgnoreCase)) continue;

			// The display name in the middle can itself contain brackets — "Achievements Mods Enabler Loader
			// (SKSE64, AE)" — so the file name is taken from the front and the verdict from the end, never by
			// trying to carve up what lies between them.
			string rest = line.Substring("plugin ".Length).TrimStart();
			int space = rest.IndexOf(' ');
			if (space <= 0) continue;
			string file = rest.Substring(0, space);
			if (!file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

			int disabled = rest.IndexOf("disabled,", StringComparison.OrdinalIgnoreCase);
			if (disabled < 0) continue;   // loaded correctly, or merely noted — not a failure

			string reason = rest.Substring(disabled + "disabled,".Length).Trim();
			// Drop the trailing bookkeeping the log puts after the reason: "… 0 (handle 0)".
			reason = Regex.Replace(reason, @"\s*\d+\s*\(handle\s+\d+\)\s*$", "", RegexOptions.IgnoreCase).Trim();

			failures.RemoveAll(f => string.Equals(f.PluginFile, file, StringComparison.OrdinalIgnoreCase));
			failures.Add(new PluginLoadFailure
			{
				PluginFile = file,
				Reason     = reason,
				Kind       = ClassifyReason(reason)
			});
		}

		return failures;
	}

	/// <summary>
	/// The line the script extender writes once every plugin has been through it. Matched whole, never as a
	/// substring, because the line before it is <c>preinit complete</c> and that one ends the same way.
	/// </summary>
	private const string InitComplete = "init complete";

	/// <summary>
	/// The plugin the script extender was still loading when its log stopped, or <c>null</c> when the log ran to
	/// the end like it should.
	///
	/// This is the failure no other check can see. Every other kind of broken plugin is <em>refused</em> — the
	/// script extender writes a line saying so and carries on, and <see cref="ParseLoadFailures"/> reads it. A
	/// plugin that instead hangs, crashes, or puts up a message box and kills the process writes no verdict at
	/// all: it stops the log mid-sentence and takes the game with it. From the outside the whole symptom is that
	/// the game does not start, with nothing anywhere naming what stopped it.
	///
	/// The rule is exactly that: the last <c>loading plugin "X"</c> with no verdict and no <c>init complete</c>
	/// after it. Recorded from the machine that prompted this — Skyrim updated to 1.7.104 while the SSE Engine
	/// Fixes installed was still February's 7.0.20, which puts up its own error box and terminates the game, and
	/// the log ends on the bare line <c>loading plugin "EngineFixes"</c>.
	///
	/// ⚠️ A log being written right now looks identical, so the caller must not ask this while the game is
	/// running.
	/// </summary>
	public static string? PluginLeftLoading(IEnumerable<string> logLines)
	{
		List<string> lines = logLines?.Select(l => l?.Trim() ?? "").ToList() ?? new List<string>();

		int last = -1;
		string? name = null;
		for (int i = 0; i < lines.Count; i++)
		{
			Match m = Regex.Match(lines[i], @"^loading plugin\s+""(.+)""\s*$", RegexOptions.IgnoreCase);
			if (!m.Success) continue;
			last = i;
			name = m.Groups[1].Value.Trim();
		}

		if (last < 0 || string.IsNullOrEmpty(name)) return null;

		// Anything after it that shows the extender was still going means it did not stop there. A verdict line
		// ("plugin X.dll (…) loaded correctly") is one; finishing the whole load is the other.
		for (int i = last + 1; i < lines.Count; i++)
		{
			if (string.Equals(lines[i], InitComplete, StringComparison.OrdinalIgnoreCase)) return null;
			if (lines[i].StartsWith("plugin ", StringComparison.OrdinalIgnoreCase) &&
				lines[i].IndexOf(".dll", StringComparison.OrdinalIgnoreCase) > 0) return null;
		}

		return name;
	}

	/// <summary>Sorts a refusal into the two that have a known cause and a cure, and everything else.</summary>
	public static PluginFailureKind ClassifyReason(string reason)
	{
		if (reason.IndexOf("address library", StringComparison.OrdinalIgnoreCase) >= 0)
			return PluginFailureKind.AddressLibrary;
		if (reason.IndexOf("incompatible with current version", StringComparison.OrdinalIgnoreCase) >= 0 ||
			reason.IndexOf("version of the game", StringComparison.OrdinalIgnoreCase) >= 0)
			return PluginFailureKind.GameVersion;
		return PluginFailureKind.Other;
	}
}
