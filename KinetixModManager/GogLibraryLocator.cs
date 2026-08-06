using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// Finds a GOG game by looking for it, for the times GOG's registry entry doesn't answer.
///
/// A GOG install records its folder in the registry, and that is the right thing to read first. But the entry
/// goes missing or goes stale in ordinary use — a folder moved by hand, a game copied from another machine, an
/// offline installer run without administrator rights — and when it does, the manager was left falling back to
/// a <em>Steam</em> default path that a GOG user will never have. Steam has a second way of being found (its
/// own library records, which list every library on every drive); this is the equivalent for GOG.
///
/// It searches by content rather than by name: any folder holding the game's executable is the game, whatever
/// GOG or the user happened to call it. Guessing folder names is what this deliberately avoids — GOG's naming
/// doesn't match Steam's, varies between a game and its Game of the Year edition, and drops the punctuation a
/// path can't hold, so a guessed name is a silent failure waiting to happen.
/// </summary>
public static class GogLibraryLocator
{
	/// <summary>The folder names GOG installs into by default, relative to a drive or to the Galaxy client.</summary>
	private const string ClassicFolderName = "GOG Games";
	private const string GalaxyGamesFolderName = "Games";

	/// <summary>
	/// The places GOG games are normally found: <c>GOG Games</c> on every fixed drive (the classic installer's
	/// default, and where people put a library that outgrew the system drive), plus the Galaxy client's own
	/// Games folder where <paramref name="galaxyClientPath"/> says it is.
	/// </summary>
	public static List<string> DefaultRoots(string? galaxyClientPath, IEnumerable<string> driveRoots)
	{
		var roots = new List<string>();

		foreach (string drive in driveRoots)
		{
			try { roots.Add(Path.Combine(drive, ClassicFolderName)); }
			catch { }
		}

		if (!string.IsNullOrEmpty(galaxyClientPath))
		{
			try { roots.Add(Path.Combine(galaxyClientPath, GalaxyGamesFolderName)); }
			catch { }
		}

		return roots;
	}

	/// <summary>
	/// Whether the copy installed at <paramref name="gameFolder"/> is the GOG one.
	///
	/// Every GOG install carries a <c>goggame-&lt;product id&gt;.info</c> file in its folder, written by the
	/// installer and left there. Judging by the folder's contents rather than by where the folder is means a
	/// copy that has been moved, or installed somewhere unusual, is still recognised — and a Steam copy sitting
	/// in a folder called "GOG Games" is not mistaken for one.
	/// </summary>
	public static bool IsGogInstall(string gameFolder, string? gogProductId)
	{
		try
		{
			if (string.IsNullOrEmpty(gameFolder) || string.IsNullOrEmpty(gogProductId)) return false;
			return File.Exists(Path.Combine(gameFolder, $"goggame-{gogProductId}.info"));
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Whether the copy at <paramref name="gameFolder"/> is a GOG one under any of <paramref name="gogProductIds"/>.
	///
	/// A game GOG sells in several editions has a product id per edition, and the copy on disk carries exactly one
	/// of them. Asking about only the first would call the Complete Edition a non-GOG install.
	/// </summary>
	public static bool IsGogInstallAnyOf(string gameFolder, IEnumerable<string>? gogProductIds)
	{
		if (gogProductIds == null) return false;
		foreach (string id in gogProductIds)
			if (IsGogInstall(gameFolder, id)) return true;
		return false;
	}

	/// <summary>The root of every fixed drive on this machine, for the default search.</summary>
	public static List<string> FixedDriveRoots()
	{
		try
		{
			return DriveInfo.GetDrives()
				.Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
				.Select(d => d.RootDirectory.FullName)
				.ToList();
		}
		catch
		{
			return new List<string>();
		}
	}

	/// <summary>
	/// The folder under <paramref name="roots"/> holding <paramref name="exeName"/>, or <c>null</c> if none does.
	///
	/// A root itself is checked as well as the folders inside it, since a library root and a game folder are the
	/// same thing for someone who installed a single game straight into it. The search stops one level down: a
	/// game's own subfolders hold its data, and walking into them would turn a quick check into a disk crawl.
	/// </summary>
	public static string? FindGameFolder(IEnumerable<string> roots, string exeName)
	{
		if (string.IsNullOrEmpty(exeName)) return null;

		foreach (string root in roots)
		{
			try
			{
				if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

				if (File.Exists(Path.Combine(root, exeName))) return root;

				foreach (string folder in Directory.EnumerateDirectories(root))
					if (File.Exists(Path.Combine(folder, exeName)))
						return folder;
			}
			catch
			{
				// An unreadable drive or a folder we have no rights to is not a reason to stop looking in the rest.
			}
		}

		return null;
	}
}
