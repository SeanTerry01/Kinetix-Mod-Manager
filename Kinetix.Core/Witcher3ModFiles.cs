using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// The parts of a Witcher 3 mod that live outside its own folder, and the game's own record of it.
///
/// <para>
/// Renaming or deleting the folder is not enough for such a mod. Its dlc and bin parts stay in the game, still loaded,
/// and the game's <c>mods.settings</c> goes on listing it. The manager records those parts in the mod's
/// <c>.manager_manifest.json</c> when it installs them.
/// </para>
///
/// <para>
/// The rules are the ones in the WinForms project's ModFileSystem, written out here because that project cannot be
/// reached from the GTK head. The folder names and the manifest key have to stay the same on both, because a mod
/// installed by one head can be removed by the other.
/// </para>
/// </summary>
public static class Witcher3ModFiles
{
	private const string ExtraPathsKey = "ExtraPaths";

	/// <summary>Where a switched-off mod's outside files wait, inside the game folder.</summary>
	public const string DisabledExtrasFolderName = "_KinetixDisabledMods";

	/// <summary>Where the game's own files that a mod replaced are kept, inside the game folder.</summary>
	public const string OriginalsFolderName = "_KinetixOriginals";

	/// <summary>
	/// The game folder the outside files of the mods in <paramref name="modsFolder"/> belong to, or empty when nobody knows.
	///
	/// A mods folder's parent is only the game when the game is actually there. The user can type any folder in Settings,
	/// and taking the parent on trust looked for outside files under the wrong root, found nothing, and reported success.
	/// Its parent comes first when it does hold the game, because then it may be a second copy the locator found another of.
	/// </summary>
	/// <param name="modsFolder">Where this game's mods are.</param>
	/// <param name="locatedGameFolder">Where the locator found the game, or null if it did not.</param>
	public static string GameFolderFor(string modsFolder, string? locatedGameFolder)
	{
		if (!string.IsNullOrEmpty(modsFolder))
		{
			string parent = Path.GetDirectoryName(modsFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "";
			IEnumerable<string> exes = GameProfiles.Find(GameProfiles.Witcher3)?.AllGameExeNames ?? Array.Empty<string>();
			if (parent.Length > 0 && exes.Any(exe => File.Exists(Path.Combine(parent, exe)))) return parent;
		}

		return locatedGameFolder ?? "";
	}

	/// <summary>
	/// Deletes what a mod put outside its folder, and says which files could not go. To be called before the folder goes,
	/// because the record of those files is inside it. Does nothing for another game.
	///
	/// <para>
	/// ⚠️ When this answers with anything, the folder has to stay: its manifest is then the only record of files still in
	/// the game. The manifest is cut down to those files. Left listing everything, a second try would find the parked files
	/// gone, take each listed path to be in the game, and delete the game's own file sitting there.
	/// </para>
	/// </summary>
	/// <param name="modFolderPath">The mod's folder.</param>
	/// <param name="gameId">The game it is for.</param>
	/// <param name="gameFolder">The game's folder. See <see cref="GameFolderFor"/>.</param>
	/// <param name="logError">Told why each file could not go.</param>
	/// <returns>The files that could not be removed, relative to the game folder.</returns>
	public static IReadOnlyList<string> RemoveExtras(string modFolderPath, string gameId, string gameFolder, Action<string, string> logError)
	{
		var stuck = new List<string>();
		if (GameProfiles.Find(gameId)?.IsWitcher3 != true || string.IsNullOrEmpty(modFolderPath)) return stuck;

		List<string> extras = ExtraPaths(modFolderPath);

		// Not knowing where the game is does not make these files gone. They are all still there, and the record stays whole.
		if (string.IsNullOrEmpty(gameFolder))
		{
			foreach (string relative in extras)
			{
				logError(modFolderPath, $"Could not remove {relative}, because the game's folder is not known.");
				stuck.Add(relative);
			}
			return stuck;
		}

		// ⚠️ A file this mod has parked is not in the game: what sits at that path is the game's own, put back when the mod was
		// switched off, or another mod's. Deleting the mod must not delete that.
		var parkedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var stillParked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// A switched-off mod has these parked rather than in the game, where the user cannot see them.
		string parked = Path.Combine(gameFolder, DisabledExtrasFolderName, Witcher3ModSettings.BareName(FolderName(modFolderPath)));

		try
		{
			if (Directory.Exists(parked))
				foreach (string file in Directory.GetFiles(parked, "*", SearchOption.AllDirectories))
					parkedFiles.Add(Path.GetRelativePath(parked, file));
			ModOnDisk.DeleteDirectory(parked);
		}
		catch (Exception ex)
		{
			logError(modFolderPath, "Could not remove the mod's parked files: " + ex.Message);

			// A failed delete can stop half way, so only what is really left counts as left.
			stillParked.UnionWith(parkedFiles.Where(relative => File.Exists(Path.Combine(parked, relative))));
			stuck.AddRange(stillParked);
		}

		var originals = new OriginalFiles(Path.Combine(gameFolder, OriginalsFolderName));
		var stillRecorded = new List<string>(stillParked);

		foreach (string relative in extras)
		{
			try
			{
				if (parkedFiles.Contains(relative.Replace('/', Path.DirectorySeparatorChar))) continue;

				string full = Path.GetFullPath(Path.Combine(gameFolder, relative));

				// Never step outside the game folder, whatever the manifest says.
				if (!IsInside(gameFolder, full)) continue;

				// Not skipped when the file has already gone. A first try can delete the mod's copy and then fail to put the
				// game's own file back, and only a second try will do that.
				if (File.Exists(full))
				{
					File.SetAttributes(full, FileAttributes.Normal);
					File.Delete(full);
				}
				originals.Restore(full, relative);
			}
			catch (Exception ex)
			{
				logError(modFolderPath, $"Could not remove {relative}, which the mod put outside its folder: " + ex.Message);
				stuck.Add(relative);
				stillRecorded.Add(relative);
				continue;
			}

			// Separate, because the file has gone by now. An empty folder left behind is not a file still loading.
			try
			{
				// The folder goes too once the mod's last file has left it, but never one of the game's own.
				string? dir = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(gameFolder, relative)));
				if (dir != null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any() &&
					!IsGameOwnedFolder(dir, gameFolder))
					Directory.Delete(dir);
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Witcher3", $"removing the folder {relative} left empty", ex); }
		}

		if (stuck.Count > 0) KeepOnlyTheseOnRecord(modFolderPath, stillRecorded, logError);
		return stuck;
	}

	/// <summary>Cuts the mod's list of outside files down to <paramref name="relativePaths"/>. See <see cref="RemoveExtras"/>.</summary>
	private static void KeepOnlyTheseOnRecord(string modFolderPath, List<string> relativePaths, Action<string, string> logError)
	{
		try
		{
			string manifestPath = Path.Combine(modFolderPath, ".manager_manifest.json");
			if (!File.Exists(manifestPath)) return;

			JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
			if (manifest[ExtraPathsKey] is not JArray recorded) return;

			var keep = new HashSet<string>(relativePaths.Select(FromRecord), StringComparer.OrdinalIgnoreCase);

			// Each entry as it was written, because the other head reads this file too.
			manifest[ExtraPathsKey] = new JArray(recorded.Where(entry => keep.Contains(FromRecord(entry?.ToString() ?? ""))));
			File.WriteAllText(manifestPath, manifest.ToString());
		}
		catch (Exception ex)
		{
			logError(modFolderPath, "Could not update the record of the mod's outside files: " + ex.Message);
		}
	}

	/// <summary>
	/// Takes a mod out of the game's own list once its folder has gone, so it does not linger in the in-game menu. Logged
	/// rather than thrown, because by now the mod is gone and saying the delete failed would be untrue.
	/// </summary>
	public static void Forget(string modFolderPath, string gameId, string gameFolder, Action<string, string> logError)
	{
		GameProfile? profile = GameProfiles.Find(gameId);
		if (profile?.IsWitcher3 != true || string.IsNullOrEmpty(modFolderPath) || string.IsNullOrEmpty(gameFolder)) return;

		try
		{
			Witcher3ModSettings.Remove(Witcher3ModSettings.PathFor(profile.UserDataDirectoryFor(gameFolder)), FolderName(modFolderPath));
		}
		catch (Exception ex)
		{
			logError(modFolderPath, "Could not take the mod out of the game's own list: " + ex.Message);
		}
	}

	/// <summary>Brings the game's own record of a mod into line with its new state. A no-op for another game.</summary>
	public static void SyncSettings(string modFolderPath, bool enable, string gameId, string gameFolder, Action<string, string> logError)
	{
		GameProfile? profile = GameProfiles.Find(gameId);
		if (profile?.IsWitcher3 != true || string.IsNullOrEmpty(modFolderPath) || string.IsNullOrEmpty(gameFolder)) return;

		try
		{
			Witcher3ModSettings.SetEnabled(Witcher3ModSettings.PathFor(profile.UserDataDirectoryFor(gameFolder)), FolderName(modFolderPath), enable);
		}
		catch (Exception ex)
		{
			logError(modFolderPath, "Could not update the game's own list of mods: " + ex.Message);
		}
	}

	/// <summary>
	/// Moves a mod's outside files out of the game when switching it off, and back when switching it on, and says how
	/// many moved. A "disabled" mod whose <c>.asi</c> is still beside the exe goes on running, so renaming its folder is
	/// not enough. To be called while the record of those files is still at <paramref name="modFolderPath"/>.
	/// </summary>
	/// <param name="modFolderPath">The mod's folder.</param>
	/// <param name="enable">Which way to move them.</param>
	/// <param name="gameId">The game it is for.</param>
	/// <param name="gameFolder">The game's folder. See <see cref="GameFolderFor"/>.</param>
	/// <param name="logError">Told why each file could not move.</param>
	/// <param name="stuck">Given each file that could not move, relative to the game folder, so the caller can say so.</param>
	public static int SetExtrasEnabled(string modFolderPath, bool enable, string gameId, string gameFolder,
		Action<string, string> logError, ICollection<string>? stuck = null)
	{
		if (GameProfiles.Find(gameId)?.IsWitcher3 != true || string.IsNullOrEmpty(modFolderPath)) return 0;

		List<string> extras = ExtraPaths(modFolderPath);
		if (extras.Count == 0) return 0;

		// Not knowing where the game is does not make these files moved. Skipping them quietly said the mod was off with
		// its .asi still running.
		if (string.IsNullOrEmpty(gameFolder))
		{
			foreach (string relative in extras)
			{
				logError(relative, "Could not move a file belonging to the mod, because the game's folder is not known.");
				stuck?.Add(relative);
			}
			return 0;
		}

		string parked = Path.Combine(gameFolder, DisabledExtrasFolderName, Witcher3ModSettings.BareName(FolderName(modFolderPath)));
		var originals = new OriginalFiles(Path.Combine(gameFolder, OriginalsFolderName));
		int moved = 0;

		foreach (string relative in extras)
		{
			try
			{
				string inGame = Path.GetFullPath(Path.Combine(gameFolder, relative));
				string outOfGame = Path.GetFullPath(Path.Combine(parked, relative));

				// Never step outside the game folder, whatever the manifest says.
				if (!IsInside(gameFolder, inGame) || !IsInside(parked, outOfGame)) continue;

				string from = enable ? outOfGame : inGame;
				string to = enable ? inGame : outOfGame;
				if (!File.Exists(from)) continue;

				// Coming back: what is there now is the game's own, put back when the mod was switched off.
				if (enable) originals.Keep(to, relative);
				if (File.Exists(to)) File.Delete(to);

				Directory.CreateDirectory(Path.GetDirectoryName(to)!);
				File.Move(from, to);
				if (!enable) originals.Restore(inGame, relative);
				moved++;
			}
			catch (Exception ex)
			{
				logError(relative, $"Could not {(enable ? "restore" : "park")} a file belonging to the mod: " + ex.Message);
				stuck?.Add(relative);
			}
		}

		if (enable)
		{
			try
			{
				if (Directory.Exists(parked) && !Directory.EnumerateFiles(parked, "*", SearchOption.AllDirectories).Any())
					Directory.Delete(parked, recursive: true);
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Witcher3", $"removing the emptied folder {parked}", ex); }
		}

		return moved;
	}

	private static string FolderName(string modFolderPath) =>
		Path.GetFileName(modFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

	/// <summary>
	/// With the separator on the end, so <c>game2/x</c> does not count as inside <c>game</c>. Linux has no drive letter to
	/// limit how far a manifest's <c>..</c> can climb, so this check is all there is between it and the rest of the disk.
	/// </summary>
	private static bool IsInside(string folder, string fullPath) =>
		fullPath.StartsWith(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

	/// <summary>The out-of-folder files recorded for a mod, or an empty list when it has none.</summary>
	private static List<string> ExtraPaths(string modFolderPath)
	{
		var paths = new List<string>();
		try
		{
			string manifestPath = Path.Combine(modFolderPath, ".manager_manifest.json");
			if (!File.Exists(manifestPath)) return paths;

			if (JObject.Parse(File.ReadAllText(manifestPath))[ExtraPathsKey] is not JArray extras) return paths;

			foreach (JToken entry in extras)
			{
				string? relative = entry?.ToString();
				if (!string.IsNullOrEmpty(relative))
					paths.Add(FromRecord(relative));
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Witcher3", "reading the extra paths the mod recorded", ex); }
		return paths;
	}

	/// <summary>
	/// A recorded path with this system's separators. Written on Windows as often as not, where a backslash is a separator.
	/// Here it is a file name character.
	/// </summary>
	private static string FromRecord(string relative) =>
		relative.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

	/// <summary>The game's own folders, which must survive a mod's removal however empty they look.</summary>
	private static bool IsGameOwnedFolder(string folder, string gameFolder)
	{
		string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
		foreach (string own in new[]
				 {
					 "dlc", "bin", "mods",
					 Path.Combine("bin", "x64"), Path.Combine("bin", "x64_dx12"), Path.Combine("bin", "config")
				 })
			if (full.Equals(Path.GetFullPath(Path.Combine(gameFolder, own)).TrimEnd(Path.DirectorySeparatorChar),
					StringComparison.OrdinalIgnoreCase))
				return true;
		return false;
	}
}
