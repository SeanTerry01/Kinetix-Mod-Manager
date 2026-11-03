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
