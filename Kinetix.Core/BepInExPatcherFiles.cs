using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// A BepInEx mod's preloader patchers, which live outside the mod's own folder, and the mod's record of them.
///
/// <para>
/// A mod built to be extracted over the game folder can carry <c>BepInEx\patchers</c> as well as <c>BepInEx\plugins</c>.
/// Patchers load before the game does, from <c>BepInEx\patchers</c> and nowhere else, so they cannot sit in the mod's
/// folder under <c>plugins</c>. They used to be copied there and forgotten: switching the mod off moved its plugin to
/// <c>plugins-disabled</c> and left its patchers running, and deleting it left them for good.
/// </para>
///
/// <para>
/// Now the files copied are listed in the mod's <c>.manager_manifest.json</c> under <see cref="RecordKey"/>, relative to
/// <c>patchers</c>. Switching the mod off moves them to <c>patchers-disabled</c>, which BepInEx never reads, the way its
/// plugin goes to <c>plugins-disabled</c>; switching it on moves them back; deleting it removes them. A file another
/// installed mod also lists is left for that mod.
/// </para>
/// </summary>
public static class BepInExPatcherFiles
{
	public const string RecordKey = "BepInExPatchers";
	public const string PatchersFolderName = "patchers";
	public const string DisabledFolderName = "patchers-disabled";

	private const string ManifestName = ".manager_manifest.json";

	/// <summary>The BepInEx folder a mod sits two levels under: <c>BepInEx\plugins\Mod</c> or <c>BepInEx\plugins-disabled\Mod</c>.</summary>
	public static string BepInExRootOf(string modPath)
	{
		string trimmed = modPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return Path.GetDirectoryName(Path.GetDirectoryName(trimmed) ?? "") ?? "";
	}

	/// <summary>The patcher files a mod recorded, relative to <c>patchers</c>. Empty for a mod that has none, or no record.</summary>
	public static IReadOnlyList<string> Recorded(string modPath)
	{
		var files = new List<string>();
		try
		{
			string manifestPath = Path.Combine(modPath, ManifestName);
			if (!File.Exists(manifestPath)) return files;
			if (JObject.Parse(File.ReadAllText(manifestPath))[RecordKey] is not JArray list) return files;
			foreach (JToken entry in list)
				if (entry?.ToString() is { Length: > 0 } relative && IsSafeRelative(relative)) files.Add(relative);
		}
		catch (Exception ex) { DiagnosticLog.WriteException("BepInEx", "reading the patchers a mod recorded", ex); }
		return files;
	}

	/// <summary>Puts <paramref name="files"/> in <paramref name="manifest"/> as the mod's patchers, or takes the entry out when there are none.</summary>
	public static void WriteRecord(JObject manifest, IReadOnlyCollection<string> files)
	{
		if (files.Count == 0) manifest.Remove(RecordKey);
		else manifest[RecordKey] = new JArray(files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Copies an archive's <c>BepInEx\patchers</c> into the game's, replacing what is there, and returns the files copied,
	/// relative to <c>patchers</c>, for the mod to record. Nothing when the archive has no patchers.
	/// </summary>
	/// <param name="folderName">Where under the BepInEx folder they go: <c>patchers</c>, or <c>patchers-disabled</c> for a mod that is off.</param>
	public static List<string> Install(string sourcePatchers, string bepInExRoot, Action<string, string> logError,
		string folderName = PatchersFolderName)
	{
		var copied = new List<string>();
		try
		{
			if (!Directory.Exists(sourcePatchers)) return copied;
			string target = Path.Combine(bepInExRoot, folderName);
			foreach (string file in Directory.GetFiles(sourcePatchers, "*.*", SearchOption.AllDirectories))
			{
				string relative = Path.GetRelativePath(sourcePatchers, file);
				string destination = Path.Combine(target, relative);
				Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
				File.Copy(file, destination, overwrite: true);
				copied.Add(relative);
			}
		}
		catch (Exception ex) { logError(sourcePatchers, "Could not install the mod's BepInEx patchers: " + ex.Message); }
		return copied;
	}

	/// <summary>
	/// Moves the mod's recorded patchers to where its new state wants them, and returns how many moved. Asked before the mod's
	/// own folder moves, while the record is still at <paramref name="modPath"/>.
	/// </summary>
	/// <param name="stuckFiles">Given the files that would not move.</param>
	public static int SetEnabled(string modPath, bool enable, string gameId, Action<string, string> logError,
		ICollection<string>? stuckFiles = null)
	{
		if (GameProfiles.Find(gameId)?.IsBepInEx != true) return 0;
		IReadOnlyList<string> files = Recorded(modPath);
		if (files.Count == 0) return 0;

		string root = BepInExRootOf(modPath);
		if (root.Length == 0) return 0;
		string from = Path.Combine(root, enable ? DisabledFolderName : PatchersFolderName);
		string to = Path.Combine(root, enable ? PatchersFolderName : DisabledFolderName);

		int moved = 0;
		foreach (string relative in files)
		{
			string source = Path.Combine(from, relative), destination = Path.Combine(to, relative);
			try
			{
				if (!File.Exists(source)) continue;
				// Switched off while another mod that is on needs the same file: that mod keeps it running.
				if (!enable && ClaimedByAnother(root, modPath, relative, ModEnableState.BepInExPluginsFolderName)) continue;
				if (File.Exists(destination))
				{
					// Already there, put back by another mod. This copy is the spare.
					ModOnDisk.DeleteFile(source);
					continue;
				}
				Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
				File.Move(source, destination);
				moved++;
			}
			catch (Exception ex)
			{
				logError(source, "Could not move a BepInEx patcher: " + ex.Message);
				stuckFiles?.Add(relative);
			}
		}
		return moved;
	}

	/// <summary>
	/// Removes the mod's recorded patchers, from wherever they are, and returns the ones that could not be removed. A file another
	/// installed mod lists is left where it is.
	/// </summary>
	public static IReadOnlyList<string> Remove(string modPath, string gameId, Action<string, string> logError) =>
		RemoveFiles(modPath, Recorded(modPath), gameId, logError);

	/// <summary>
	/// For an install over an older copy: removes the files the older copy recorded and the new one doesn't, and any of the
	/// older copy's that were switched off, since the new copy's go in switched on.
	/// </summary>
	/// <remarks>Called only from a BepInEx install, so the game is not asked again.</remarks>
	public static void RemoveLeftovers(string oldModPath, IReadOnlyCollection<string> nowInstalled, Action<string, string> logError)
	{
		string root = BepInExRootOf(oldModPath);
		if (root.Length == 0) return;
		var kept = new HashSet<string>(nowInstalled, StringComparer.OrdinalIgnoreCase);
		foreach (string relative in Recorded(oldModPath))
		{
			if (!kept.Contains(relative)) RemoveFiles(oldModPath, new[] { relative }, logError);
			else TryDelete(Path.Combine(root, DisabledFolderName, relative), logError);
		}
	}

	/// <summary>The folder inside a mod that carries its patchers in a backup. Never left there: see <see cref="StashInto"/>.</summary>
	public const string StashFolderName = ".kinetix_patchers";

	/// <summary>
	/// Copies the mod's recorded patchers into its own folder, for a backup about to be taken of it to carry them, since deleting
	/// the mod now removes them from <c>patchers</c>. Returns whether anything was copied; the caller takes the copy out again
	/// with <see cref="ClearStash"/> once the backup is made, because BepInEx scans <c>plugins</c> for DLLs and it must not
	/// find a patcher there.
	/// </summary>
	public static bool StashInto(string modPath, string gameId, Action<string, string> logError)
	{
		if (GameProfiles.Find(gameId)?.IsBepInEx != true) return false;
		string root = BepInExRootOf(modPath);
		if (root.Length == 0) return false;
		bool any = false;
		foreach (string relative in Recorded(modPath))
		{
			string? found = new[] { PatchersFolderName, DisabledFolderName }
				.Select(f => Path.Combine(root, f, relative))
				.FirstOrDefault(File.Exists);
			if (found == null) continue;
			try
			{
				string destination = Path.Combine(modPath, StashFolderName, relative);
				Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
				File.Copy(found, destination, overwrite: true);
				any = true;
			}
			catch (Exception ex) { logError(found, "Could not keep a BepInEx patcher with the mod's backup: " + ex.Message); }
		}
		return any;
	}

	/// <summary>Takes <see cref="StashFolderName"/> out of the mod's folder again.</summary>
	public static void ClearStash(string modPath)
	{
		try { ModOnDisk.DeleteDirectory(Path.Combine(modPath, StashFolderName)); }
		catch (Exception ex) { DiagnosticLog.WriteException("BepInEx", "taking the patchers kept for a backup out of the mod again", ex); }
	}

	/// <summary>
	/// For a mod just restored from a backup: puts the patchers its backup carried back in <c>patchers</c>, or in
	/// <c>patchers-disabled</c> for a mod restored switched off, and takes the carried copy out of its folder.
	/// </summary>
	public static void RestoreStash(string modPath, string gameId, Action<string, string> logError)
	{
		string stash = Path.Combine(modPath, StashFolderName);
		if (!Directory.Exists(stash)) return;
		if (GameProfiles.Find(gameId)?.IsBepInEx == true)
		{
			string root = BepInExRootOf(modPath);
			string into = Path.Combine(root, ModEnableState.IsEnabled(modPath, gameId) ? PatchersFolderName : DisabledFolderName);
			if (root.Length > 0) Install(stash, Path.GetDirectoryName(into)!, logError, Path.GetFileName(into));
		}
		ClearStash(modPath);
	}

	private static IReadOnlyList<string> RemoveFiles(string modPath, IEnumerable<string> files, string gameId, Action<string, string> logError) =>
		GameProfiles.Find(gameId)?.IsBepInEx == true ? RemoveFiles(modPath, files, logError) : Array.Empty<string>();

	private static IReadOnlyList<string> RemoveFiles(string modPath, IEnumerable<string> files, Action<string, string> logError)
	{
		var notRemoved = new List<string>();
		string root = BepInExRootOf(modPath);
		if (root.Length == 0) return notRemoved;

		foreach (string relative in files)
		{
			bool stuck = false;
			if (!ClaimedByAnother(root, modPath, relative, ModEnableState.BepInExPluginsFolderName))
				stuck |= !TryDelete(Path.Combine(root, PatchersFolderName, relative), logError);
			if (!ClaimedByAnother(root, modPath, relative, ModEnableState.BepInExDisabledFolderName))
				stuck |= !TryDelete(Path.Combine(root, DisabledFolderName, relative), logError);
			if (stuck) notRemoved.Add(relative);
		}
		return notRemoved;
	}

	private static bool TryDelete(string path, Action<string, string> logError)
	{
		try
		{
			ModOnDisk.DeleteFile(path);
			return true;
		}
		catch (Exception ex)
		{
			logError(path, "Could not remove a BepInEx patcher: " + ex.Message);
			return false;
		}
	}

	/// <summary>Whether a mod other than <paramref name="modPath"/>, in <paramref name="folderName"/> under the BepInEx root, records <paramref name="relative"/>.</summary>
	private static bool ClaimedByAnother(string root, string modPath, string relative, string folderName)
	{
		string folder = Path.Combine(root, folderName);
		if (!Directory.Exists(folder)) return false;
		string self = Path.GetFullPath(modPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		foreach (string other in Directory.GetDirectories(folder))
		{
			if (string.Equals(Path.GetFullPath(other).TrimEnd(Path.DirectorySeparatorChar), self, StringComparison.OrdinalIgnoreCase)) continue;
			if (Recorded(other).Contains(relative, StringComparer.OrdinalIgnoreCase)) return true;
		}
		return false;
	}

	/// <summary>A recorded path that stays inside <c>patchers</c>: a record edited to say <c>..\..\x</c> must not reach anything else.</summary>
	private static bool IsSafeRelative(string relative) =>
		!Path.IsPathRooted(relative) &&
		!relative.Split('\\', '/').Any(part => part == "..");
}
