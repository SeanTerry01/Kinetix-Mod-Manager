using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// The safety net: a zip of a mod folder taken before anything is changed, and the rules for keeping and
/// discarding them.
///
/// <para>
/// It is taken before every update and every deletion, which is the only reason an update that goes wrong is
/// recoverable. For this program's users that matters more than usual — a mod whose failure mode is the game
/// going quiet cannot be diagnosed by looking at it, so being able to put back exactly what was there before
/// is often the only way out.
/// </para>
///
/// <para>
/// Lifted out of ModFileSystem by Phase 4. Backing something up is neither a Windows concern nor a UI one.
/// </para>
/// </summary>
public static class BackupStore
{
	/// <summary>The timestamp appended to a backup's file name, and the length that suffix always occupies.</summary>
	private const string StampFormat = "yyyyMMdd_HHmmss";
	private const int StampLength = 16;   // "_" + 15 characters of stamp

	/// <summary>
	/// The mod name a backup file belongs to, with its timestamp removed.
	///
	/// Done by length rather than by splitting on the underscore, because mod names contain underscores far
	/// more often than they contain anything else — "SMAPI_Console_Commands" would otherwise be listed as
	/// "SMAPI".
	/// </summary>
	public static string ModNameFromFileName(string path)
	{
		string name = Path.GetFileNameWithoutExtension(path);
		return name.Length > StampLength && name[name.Length - StampLength] == '_'
			? name.Substring(0, name.Length - StampLength)
			: name;
	}

	/// <summary>
	/// Every backup in the folder, newest first.
	///
	/// Newest first because the one a user wants is nearly always the one taken moments ago, by the update
	/// they are now regretting.
	/// </summary>
	public static List<BackupItem> List(string backupsPath)
	{
		var items = new List<BackupItem>();
		if (string.IsNullOrEmpty(backupsPath) || !Directory.Exists(backupsPath)) return items;

		foreach (string path in Directory.GetFiles(backupsPath, "*.zip")
			.OrderByDescending(p => { try { return File.GetCreationTimeUtc(p); } catch { return DateTime.MinValue; } }))
		{
			items.Add(new BackupItem { Name = ModNameFromFileName(path), FullPath = path });
		}

		return items;
	}

	/// <summary>
	/// Creates a timestamped <c>.zip</c> backup of a mod folder.
	/// </summary>
	/// <summary>
	/// Zips a mod folder into the backups folder. Supply <paramref name="progress"/> (0–100) to be told how far
	/// along it is — a large mod can take long enough that silence looks like the manager has hung.
	/// </summary>
	public static void CreateBackup(string folderPath, string modName, string backupsPath,
		IProgress<double>? progress = null)
	{
		// A mod is not always a folder. Minecraft's are single .jar files, and this used to return here
		// without a word for anything that was not a directory — so deleting a Minecraft mod kept no backup
		// at all while the manager said it had made one. Silent, and only discovered when somebody wants the
		// mod back.
		bool oneFile = !Directory.Exists(folderPath) && File.Exists(folderPath);
		if (!Directory.Exists(folderPath) && !oneFile) return;

		Directory.CreateDirectory(backupsPath);
		string dest = Path.Combine(backupsPath, $"{modName}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

		if (oneFile)
		{
			// Zipped rather than copied, so a backup is one kind of thing whatever the mod is shaped like and
			// List() below finds it the same way.
			using (var zip = ZipFile.Open(dest, ZipArchiveMode.Create))
				zip.CreateEntryFromFile(folderPath, Path.GetFileName(folderPath));

			progress?.Report(100.0);
			return;
		}

		if (progress == null)
		{
			ZipFile.CreateFromDirectory(folderPath, dest);
			return;
		}

		// Written entry by entry so progress can be reported, and measured in BYTES rather than files: mods are
		// routinely one large archive beside a handful of small files, and counting files would race to 90% and
		// then sit there for the entire wait — the opposite of reassuring.
		string[] files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
		long total = 0;
		foreach (string file in files)
		{
			try { total += new FileInfo(file).Length; }
			catch (Exception ex) { DiagnosticLog.WriteException("Backup", $"measuring {file}", ex); }
		}
		if (total <= 0) total = 1;

		string root = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		long done = 0;

		using (var zip = ZipFile.Open(dest, ZipArchiveMode.Create))
		{
			foreach (string file in files)
			{
				string relative = Path.GetFullPath(file).Substring(root.Length);
				zip.CreateEntryFromFile(file, relative);
				try { done += new FileInfo(file).Length; }
				catch (Exception ex) { DiagnosticLog.WriteException("Backup", $"measuring {file}", ex); }
				progress.Report(Math.Min(100.0, done * 100.0 / total));
			}

			// ZipFile.CreateFromDirectory records empty folders; keep doing so, or restoring a backup would
			// quietly drop a folder a mod expects to exist.
			foreach (string dir in Directory.GetDirectories(folderPath, "*", SearchOption.AllDirectories))
			{
				if (Directory.EnumerateFileSystemEntries(dir).Any()) continue;
				string relative = Path.GetFullPath(dir).Substring(root.Length).Replace(Path.DirectorySeparatorChar, '/');
				zip.CreateEntry(relative + "/");
			}
		}

		progress.Report(100.0);
	}

	/// <summary>
	/// Deletes oldest backups.
	/// </summary>
	public static void PruneBackups(string modName, string backupsPath, int maxCount)
	{
		if (!Directory.Exists(backupsPath)) return;
		var files = Directory.GetFiles(backupsPath, modName + "_*.zip")
			.Select(p => new FileInfo(p))
			.OrderByDescending(f => f.CreationTime)
			.ToList();

		for (int i = maxCount; i < files.Count; i++)
		{
			try { files[i].Delete(); }
			catch (Exception ex) { DiagnosticLog.WriteException("Backup", $"deleting the old backup {files[i].FullName}", ex); }
		}
	}
}
