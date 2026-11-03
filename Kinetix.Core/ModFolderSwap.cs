using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// Puts a newly built mod folder where an older one was, so that a failure at any point leaves the old one working.
///
/// <para>
/// Installs used to delete the old copy and then copy the new one in. A full disk, an antivirus lock or a crash in
/// between left a partial folder that was then listed, deployed and enabled, with the working copy gone. So the order
/// here is the other way round: the new folder is complete before the old one is touched, and the change itself is
/// only renames, which either happen or do not.
/// </para>
/// </summary>
public static class ModFolderSwap
{
	/// <summary>
	/// Where to build a new mod folder, given the install's extraction folder. Beside it rather than inside it,
	/// because an archive's own root can be the folder being copied from, and a copy would then include itself.
	/// </summary>
	public static string BuildFolderFor(string extractionFolder) => extractionFolder.TrimEnd(Path.DirectorySeparatorChar) + "_new";

	/// <summary>Copies a whole folder, empty folders included. The destination must not be inside the source.</summary>
	public static void CopyTree(string source, string destination)
	{
		Directory.CreateDirectory(destination);
		foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
			Directory.CreateDirectory(PathRebase.To(source, dir, destination));
		foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
			File.Copy(file, PathRebase.To(source, file, destination), overwrite: true);
	}

	/// <summary>
	/// Carries a file the user's own play has written across an update, such as the <c>config.json</c> a Stardew mod
	/// keeps its settings and keybinds in. Nothing happens when the old copy has none.
	///
	/// ⚠️ A failure here throws, on purpose. It happens before anything has been replaced, so the update stops with
	/// the old copy and its settings intact, rather than going ahead and quietly losing them.
	/// </summary>
	public static void KeepUserFile(string oldFolder, string newFolder, string fileName)
	{
		string kept = Path.Combine(oldFolder, fileName);
		if (!File.Exists(kept)) return;

		Directory.CreateDirectory(newFolder);
		File.Copy(kept, Path.Combine(newFolder, fileName), overwrite: true);
	}

	/// <summary>
	/// Copies <paramref name="source"/>, then puts the copy in place. For a source that is still needed afterwards,
	/// or that sits inside something still being read.
	/// </summary>
	public static void ReplaceWithCopyOf(string source, string destination, IEnumerable<string> oldFolders,
		string tempRoot, Action<string, string> logError)
	{
		string copy = Path.Combine(tempRoot, "New_" + Path.GetRandomFileName());
		try
		{
			CopyTree(source, copy);
			Replace(copy, destination, oldFolders, tempRoot, logError);
		}
		finally
		{
			Discard(copy, logError);
		}
	}

	/// <summary>
	/// Moves <paramref name="newFolder"/> to <paramref name="destination"/>, taking away <paramref name="oldFolders"/>
	/// and whatever is already at the destination. The caller backs up what it wants kept before calling this.
	///
	/// Throws if it cannot be done, with everything back where it was.
	/// </summary>
	/// <param name="newFolder">The complete new mod. Moved, so it is gone on success.</param>
	/// <param name="destination">Where the mod belongs.</param>
	/// <param name="oldFolders">Copies this one replaces. They need not be at the destination, nor exist at all.</param>
	/// <param name="tempRoot">Where the old copies wait until the new one is in place.</param>
	/// <param name="logError">Told about old copies that could not be cleared up afterwards. The install still worked.</param>
	/// <param name="beforeDiscardingOld">
	/// Runs for each old copy once the new one is in place, while its files can still be read. A failure here is logged
	/// rather than undoing a finished install.
	/// </param>
	public static void Replace(string newFolder, string destination, IEnumerable<string> oldFolders, string tempRoot,
		Action<string, string> logError, Action<string>? beforeDiscardingOld = null)
	{
		destination = Path.GetFullPath(destination);
		string? parent = Path.GetDirectoryName(destination);
		if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

		// Across drives a move is not a rename, so the copy is made here, while nothing has been touched. If the disk
		// is going to fill up, this is where it should happen.
		string incoming = Path.GetFullPath(newFolder);
		string? crossDriveCopy = null;
		if (!SameDrive(incoming, destination))
		{
			crossDriveCopy = destination + ".kinetix-new";
			Discard(crossDriveCopy, logError);
			CopyTree(incoming, crossDriveCopy);
			incoming = crossDriveCopy;
		}

		List<string> toMove = oldFolders
			.Append(destination)
			.Where(p => !string.IsNullOrEmpty(p))
			.Select(Path.GetFullPath)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Where(Directory.Exists)
			.ToList();

		var parked = new List<(string From, string To)>();
		try
		{
			// A file held open in an old copy, such as the game still running, fails here with nothing changed.
			foreach (string old in toMove)
			{
				string to = ParkingPlaceFor(old, tempRoot);
				string? toParent = Path.GetDirectoryName(to);
				if (!string.IsNullOrEmpty(toParent)) Directory.CreateDirectory(toParent);
				Directory.Move(old, to);
				parked.Add((old, to));
			}

			Directory.Move(incoming, destination);
		}
		catch
		{
			foreach ((string from, string to) in Enumerable.Reverse(parked))
			{
				try { Directory.Move(to, from); }
				catch (Exception ex) { logError(from, $"Could not put the old copy back from {to}: {ex.Message}"); }
			}
			if (crossDriveCopy != null) Discard(crossDriveCopy, logError);
			throw;
		}

		foreach ((string from, string to) in parked)
		{
			try { beforeDiscardingOld?.Invoke(to); }
			catch (Exception ex) { logError(from, ex.Message); }
			Discard(to, logError);
		}

		// The new copy's own files are the user's now, whatever flags the archive gave them.
		if (crossDriveCopy != null) Discard(crossDriveCopy, logError);
	}

	/// <summary>Where an old copy can wait. On the same drive, so that moving it there and back are both renames.</summary>
	private static string ParkingPlaceFor(string folder, string tempRoot)
	{
		string name = "Old_" + Path.GetRandomFileName();
		return SameDrive(folder, tempRoot)
			? Path.Combine(Path.GetFullPath(tempRoot), name)
			: Path.Combine(Path.GetDirectoryName(folder) ?? tempRoot, ".kinetix-" + name);
	}

	private static bool SameDrive(string a, string b) =>
		string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

	private static void Discard(string folder, Action<string, string> logError)
	{
		try { ModOnDisk.DeleteDirectory(folder); }
		catch (Exception ex) { logError(folder, $"Could not clear up a temporary folder: {ex.Message}"); }
	}
}

/// <summary>What the player wants done when a download comes from the same page as a mod they already have.</summary>
public enum SamePageChoice
{
	/// <summary>It is a new version of that mod.</summary>
	Replace,

	/// <summary>It is a separate file, such as a patch or an optional part.</summary>
	InstallAlongside,

	/// <summary>Install nothing.</summary>
	Cancel
}

/// <summary>How an installed mod came to be matched as the one a download would replace.</summary>
public enum InstallMatch
{
	/// <summary>Nothing installed matches.</summary>
	None,

	/// <summary>It has the same name or folder: the same mod, in any version.</summary>
	SameName,

	/// <summary>It only came from the same mod page, which may mean a different file altogether.</summary>
	SamePage
}

/// <summary>Works out which installed mod, if any, a download would replace.</summary>
public static class InstallTarget
{
	/// <summary>
	/// The installed mod an archive would replace.
	///
	/// A matching name always wins, because a Nexus id identifies a page and not a mod: a main file and its patch share
	/// one. Matching on the id first picked whichever came first in the list, so installing a patch could delete the
	/// main mod, and an update could replace the patch. With two or more mods from the page and no matching name there
	/// is no telling which is meant, so it matches nothing, and the download goes in alongside them.
	/// </summary>
	/// <param name="installed">The mods on disk.</param>
	/// <param name="nexusId">The mod page the download came from, if known.</param>
	/// <param name="folderName">The folder the download would install to.</param>
	/// <param name="matchByPage">False when several downloads of one mod are all wanted together, such as SSE Engine Fixes.</param>
	public static (GameMod? Mod, InstallMatch Match) Find(IEnumerable<GameMod> installed, string? nexusId, string folderName,
		bool matchByPage = true)
	{
		List<GameMod> mods = installed.Where(m => !m.IsGroup).ToList();

		GameMod? byName = mods.FirstOrDefault(m =>
			m.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase) ||
			m.UniqueId.Equals(folderName, StringComparison.OrdinalIgnoreCase) ||
			Path.GetFileName((m.FolderPath ?? "").TrimEnd(Path.DirectorySeparatorChar)).Equals(folderName, StringComparison.OrdinalIgnoreCase));
		if (byName != null) return (byName, InstallMatch.SameName);

		if (!matchByPage) return (null, InstallMatch.None);

		if (string.IsNullOrEmpty(nexusId)) nexusId = ModDisplayName.ModIdFromArchiveName(folderName);
		if (string.IsNullOrEmpty(nexusId)) return (null, InstallMatch.None);

		List<GameMod> fromPage = mods.Where(m => m.NexusID == nexusId).ToList();
		return fromPage.Count == 1 ? (fromPage[0], InstallMatch.SamePage) : (null, InstallMatch.None);
	}
}
