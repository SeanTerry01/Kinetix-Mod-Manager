using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>One Visual C++ runtime DLL as found in the system folder: its file name, and the version stamped
/// into it, or null when the file is not installed at all.</summary>
public sealed class VcRuntimeFile
{
	public string Name { get; init; } = "";

	/// <summary>The version from the file's own resources, or null when the file is absent.</summary>
	public Version? Version { get; init; }
}

/// <summary>What <see cref="VcRuntimeCheck"/> concluded about the installed runtime.</summary>
public sealed class VcRuntimeVerdict
{
	/// <summary>True when every runtime file present came from the same redistributable — the healthy case.</summary>
	public bool IsConsistent => Stale.Count == 0 && !CoreMissing;

	/// <summary>The newest build found, which is what the stale files are expected to match. Null if nothing was found.</summary>
	public Version? Expected { get; init; }

	/// <summary>Files left behind at an older build than <see cref="Expected"/>, newest-mismatch first.</summary>
	public List<VcRuntimeFile> Stale { get; init; } = new();

	/// <summary>True when the core runtime is not installed at all, so no native mod can load.</summary>
	public bool CoreMissing { get; init; }
}

/// <summary>
/// Checks that the Visual C++ 2015-2022 x64 runtime in the Windows system folder is internally consistent.
///
/// Every accessibility mod this manager installs is a native DLL — Skyrim Access and Sound Record Distributor
/// as SKSE plugins, WitcherAccess for The Witcher 3, the BepInEx plugins for Moonlight Peaks — and they all
/// link the same handful of C runtime DLLs. That makes the runtime a single point of failure shared by every
/// supported game.
///
/// The failure it catches is a *mixed* install rather than a missing one: an installer or a game drops an old
/// <c>msvcp140.dll</c> into System32 over a newer set, and afterwards the core files and their satellites come
/// from different builds. The satellites depend on exports from a core of their own build, so loading them
/// together fails. Windows' own uninstall list still reports the newer version as installed, which is what makes
/// this so hard to spot by hand.
///
/// It matters more here than in most programs because of how it presents. A screen-reader mod that cannot load
/// does not raise an error — the game simply starts and says nothing. To a blind user that is indistinguishable
/// from a mod that is merely misconfigured, and there is nothing to read. This check turns that silence into a
/// sentence.
/// </summary>
public static class VcRuntimeCheck
{
	/// <summary>
	/// The files the VC++ 2015-2022 redistributable installs, all of which ship from one bundle and therefore
	/// share a build number in a healthy install.
	///
	/// Deliberately excluded: the <c>*_clr0400.dll</c> variants, which .NET services separately and which
	/// legitimately sit at a different version, and the <c>*d.dll</c> debug builds, which only exist on machines
	/// with Visual Studio and are not what a game loads. Including either would make every developer's machine
	/// report a false problem.
	/// </summary>
	public static readonly string[] RuntimeFileNames =
	{
		"msvcp140.dll",
		"msvcp140_1.dll",
		"msvcp140_2.dll",
		"msvcp140_atomic_wait.dll",
		"msvcp140_codecvt_ids.dll",
		"vcruntime140.dll",
		"vcruntime140_1.dll",
		"vcruntime140_threads.dll",
		"concrt140.dll"
	};

	/// <summary>The file without which nothing native loads at all; its absence is reported on its own.</summary>
	public const string CoreFileName = "msvcp140.dll";

	/// <summary>Microsoft's own page of current downloads. Linked rather than a direct installer URL because the
	/// direct link is version-pinned, and handing someone an <em>older</em> build than they already have produces a
	/// refused downgrade rather than a repair — exactly the dead end this check exists to prevent.</summary>
	public const string DownloadUrl = "https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist";

	/// <summary>
	/// Decides whether a set of runtime files hangs together. Pure, so the rule can be tested against recorded
	/// version sets without touching a real Windows folder.
	///
	/// Files are compared on major.minor.build only. The fourth component is always zero for these, and comparing
	/// it would add nothing but a way to be wrong. Absent files are skipped rather than reported: the set has grown
	/// over the years (<c>msvcp140_atomic_wait.dll</c> only appeared in 14.28), so an older but perfectly coherent
	/// install is simply missing the newer members.
	/// </summary>
	public static VcRuntimeVerdict Evaluate(IEnumerable<VcRuntimeFile> files)
	{
		List<VcRuntimeFile> present = files.Where(f => f.Version != null).ToList();

		bool coreMissing = !present.Any(f =>
			string.Equals(f.Name, CoreFileName, StringComparison.OrdinalIgnoreCase));

		if (present.Count == 0)
			return new VcRuntimeVerdict { CoreMissing = true };

		Version newest = present.Max(f => Truncate(f.Version!))!;

		// Anything behind the newest build is a leftover from an older redistributable that a later install failed
		// to replace. Ordered oldest-first so the worst offender is spoken first.
		List<VcRuntimeFile> stale = present
			.Where(f => Truncate(f.Version!) < newest)
			.OrderBy(f => Truncate(f.Version!))
			.ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		return new VcRuntimeVerdict { Expected = newest, Stale = stale, CoreMissing = coreMissing };
	}

	/// <summary>Drops the revision so files from one redistributable compare equal.</summary>
	private static Version Truncate(Version v) => new Version(v.Major, v.Minor, v.Build);

	/// <summary>
	/// Reads the runtime files out of the Windows system folder and evaluates them. Returns null when the check
	/// does not apply and should be skipped silently rather than guessed at — under Wine, where these files are
	/// the compatibility layer's own builtins and version numbers carry none of the meaning assumed here.
	/// </summary>
	public static VcRuntimeVerdict? Inspect()
	{
		if (PlatformInfo.IsWine) return null;

		try
		{
			string dir = SystemDirectory();
			var files = new List<VcRuntimeFile>();

			foreach (string name in RuntimeFileNames)
			{
				string path = Path.Combine(dir, name);
				if (!File.Exists(path)) { files.Add(new VcRuntimeFile { Name = name }); continue; }

				FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);

				// Read the numeric parts, never the FileVersion string: Microsoft stamps build annotations into it
				// ("14.28.29910.0 built by: vcwrkspc"), which no version parser accepts.
				files.Add(new VcRuntimeFile
				{
					Name = name,
					Version = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart)
				});
			}

			return Evaluate(files);
		}
		catch (Exception)
		{
			// A health check must never be the thing that breaks. An unreadable system folder means we cannot say,
			// not that something is wrong.
			return null;
		}
	}

	/// <summary>
	/// The folder holding the 64-bit runtime that the games actually load. A 32-bit process is redirected away from
	/// System32, so it has to ask for <c>Sysnative</c> to see past the redirector and inspect the same files the
	/// x64 games will.
	/// </summary>
	private static string SystemDirectory()
	{
		if (Environment.Is64BitProcess)
			return Environment.GetFolderPath(Environment.SpecialFolder.System);

		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative");
	}

	/// <summary>Formats a build for display as Microsoft writes it, e.g. "14.51.36247".</summary>
	public static string Format(Version v) => $"{v.Major}.{v.Minor}.{v.Build}";
}
