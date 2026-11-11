namespace KinetixModManager;

/// <summary>
/// Removing a mod from the disk. Read-only flags are cleared first, because a mod that ships a read-only file
/// otherwise fails with "access is denied", and a transient lock such as an antivirus scan gets a few retries.
/// </summary>
public static class ModOnDisk
{
	private const int Attempts = 4;
	private const int RetryDelayMs = 200;

	/// <summary>
	/// Removes a mod, whichever shape it has: a folder on most games, a single jar on Minecraft.
	///
	/// ⚠️ Both shapes have to go through here. A folder delete handed a jar returns at once, because no such folder
	/// exists, and that is how Minecraft mods came to be reported "Deleted" while they carried on loading. A delete
	/// that cannot be done throws, so the caller can say so.
	/// </summary>
	public static void Delete(string path)
	{
		if (File.Exists(path)) DeleteFile(path);
		else DeleteDirectory(path);
	}

	/// <summary>Deletes a directory tree. Nothing happens when it does not exist.</summary>
	public static void DeleteDirectory(string path)
	{
		if (!Directory.Exists(path)) return;
		ClearReadOnly(path);
		for (int attempt = 1; ; attempt++)
		{
			try { Directory.Delete(path, recursive: true); return; }
			catch (Exception) when (attempt < Attempts) { Thread.Sleep(RetryDelayMs); }
		}
	}

	/// <summary>Deletes a single file. Nothing happens when it does not exist.</summary>
	public static void DeleteFile(string path)
	{
		if (!File.Exists(path)) return;
		for (int attempt = 1; ; attempt++)
		{
			try
			{
				FileAttributes attributes = File.GetAttributes(path);
				if ((attributes & FileAttributes.ReadOnly) != 0)
					File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
				File.Delete(path);
				return;
			}
			catch (Exception) when (attempt < Attempts) { Thread.Sleep(RetryDelayMs); }
		}
	}

	/// <summary>
	/// Renames a folder, retrying a transient lock. An antivirus or the indexer often holds a folder just written.
	/// </summary>
	/// <param name="from">The folder to move.</param>
	/// <param name="to">Where it goes.</param>
	/// <param name="beforeRetry">Told each time a move is about to be tried again. For a test to end the lock it is holding.</param>
	public static void MoveDirectory(string from, string to, Action? beforeRetry = null)
	{
		for (int attempt = 1; ; attempt++)
		{
			try { Directory.Move(from, to); return; }
			catch (Exception) when (attempt < Attempts && Directory.Exists(from))
			{
				beforeRetry?.Invoke();
				Thread.Sleep(RetryDelayMs);
			}
		}
	}

	/// <summary>
	/// Switches a mod on or off on disk, whichever shape it has, and returns where it now is.
	///
	/// ⚠️ A mod is a folder on most games and one jar on Minecraft, and <see cref="File.Move(string, string)"/> throws on a
	/// folder, so moving by one shape only fails on every mod of the other. The destination's folder may not exist yet:
	/// BepInEx's <c>plugins-disabled</c> does not until the first mod is switched off.
	/// </summary>
	/// <param name="modPath">The mod, as a folder or a file.</param>
	/// <param name="enable">Which way to switch it.</param>
	/// <param name="gameId">The game it is for.</param>
	/// <param name="gameFolder">
	/// The game's folder, for The Witcher 3's files outside the mod. Not worked out from the mod's path, because the user
	/// can keep their mods anywhere. See <see cref="Witcher3ModFiles.GameFolderFor"/>. Ignored for every other game.
	/// </param>
	/// <param name="logError">Told why anything could not be moved.</param>
	/// <param name="stuckFiles">
	/// Given the outside files left in the wrong place. Filled even when this throws, so read it after either. Without it
	/// the mod is reported switched with its .asi still running, because the folder itself did move.
	/// </param>
	public static string SetEnabled(string modPath, bool enable, string gameId, string gameFolder,
		Action<string, string> logError, ICollection<string>? stuckFiles = null)
	{
		string target = ModEnableState.TargetPath(modPath, enable, gameId);
		bool moving = !string.Equals(target, modPath, StringComparison.Ordinal);

		// Before the folder moves, while the record of these files is still at this path.
		Witcher3ModFiles.SetExtrasEnabled(modPath, enable, gameId, gameFolder, logError, stuckFiles);
		// A BepInEx mod's preloader patchers, likewise outside its folder. See BepInExPatcherFiles.
		BepInExPatcherFiles.SetEnabled(modPath, enable, gameId, logError, stuckFiles);

		if (moving)
		{
			try
			{
				string? parent = Path.GetDirectoryName(target);
				if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

				if (Directory.Exists(modPath)) MoveDirectory(modPath, target);
				else File.Move(modPath, target);
			}
			catch
			{
				// The folder is staying as it was, so its outside files have to as well, or it reads as on with its parts away.
				// A file that would not move on the way out is already where it now belongs, so only one that will not come back counts.
				if (!string.IsNullOrEmpty(gameFolder))
				{
					stuckFiles?.Clear();
					Witcher3ModFiles.SetExtrasEnabled(modPath, !enable, gameId, gameFolder, logError, stuckFiles);
				}
				// Its patchers go back too, or a plugin left on would have lost them, or one left off still be patching.
				BepInExPatcherFiles.SetEnabled(modPath, !enable, gameId, logError, stuckFiles);
				throw;
			}
		}

		Witcher3ModFiles.SyncSettings(modPath, enable, gameId, gameFolder, logError);
		return target;
	}

	/// <summary>
	/// Whether a mod's path is the mods folder itself. Then its files are loose among all the others, as a restore that
	/// unpacked straight into the mods folder once left them: Stardew's scan lists <c>Mods/manifest.json</c> as a mod whose
	/// folder is the whole of Mods. Deleting, renaming or replacing that "mod" takes every other one with it.
	/// </summary>
	public static bool IsTheModsFolderItself(string modPath, string modsFolder)
	{
		if (string.IsNullOrEmpty(modPath) || string.IsNullOrEmpty(modsFolder)) return false;

		return string.Equals(
			Path.GetFullPath(modPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
			Path.GetFullPath(modsFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
			StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Clears the read-only flag on a folder and everything under it. A failure is logged: the delete that follows reports it.</summary>
	private static void ClearReadOnly(string path)
	{
		try
		{
			var root = new DirectoryInfo(path);
			if ((root.Attributes & FileAttributes.ReadOnly) != 0) root.Attributes &= ~FileAttributes.ReadOnly;
			foreach (FileInfo file in root.GetFiles("*", SearchOption.AllDirectories))
				if ((file.Attributes & FileAttributes.ReadOnly) != 0) file.Attributes &= ~FileAttributes.ReadOnly;
			foreach (DirectoryInfo dir in root.GetDirectories("*", SearchOption.AllDirectories))
				if ((dir.Attributes & FileAttributes.ReadOnly) != 0) dir.Attributes &= ~FileAttributes.ReadOnly;
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Files", $"clearing read-only flags under {path}", ex); }
	}
}
