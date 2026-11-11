using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// Works out where a backup belongs when it is put back.
///
/// <para>
/// Restoring used to treat a backup as a new download called something like <c>Foo_20260101_120000</c>. On Skyrim that
/// installed a second mod under that name beside the broken one, which stayed enabled; on Minecraft the jar landed in a
/// subfolder nothing reads. Either way it reported success while the game went on running what the player was trying
/// to get away from.
/// </para>
/// </summary>
public static class BackupRestore
{
	/// <summary>What a backup taken just before deleting a mod has after the mod's name.</summary>
	public const string DeleteSuffix = "_Delete";

	/// <summary>The name of the mod a backup file holds, without its timestamp or delete marker.</summary>
	public static string ModNameOf(string backupFile)
	{
		string name = BackupStore.ModNameFromFileName(backupFile);
		return name.EndsWith(DeleteSuffix, StringComparison.Ordinal) ? name[..^DeleteSuffix.Length] : name;
	}

	/// <summary>
	/// Where a backup's contents go, and the installed mod they take the place of, if there is one.
	///
	/// The mod's own record inside the backup is believed first, because a backup's file name can be a shown name that
	/// is no use as a folder: on The Witcher 3 a folder not called <c>mod…</c> does not load at all. A mod still
	/// installed keeps the folder it is in, so one that is switched off stays switched off.
	/// </summary>
	/// <param name="extracted">The folder the backup was unpacked into.</param>
	/// <param name="backupFile">The backup's file path.</param>
	/// <param name="installed">The mods on disk now.</param>
	/// <param name="modsPath">The game's mods folder.</param>
	/// <param name="useRecordedFolderName">
	/// False where the record's id is not a folder name, as on BepInEx, whose id is the plugin's GUID.
	/// </param>
	public static (string Destination, GameMod? Replaces) Plan(string extracted, string backupFile,
		IEnumerable<GameMod> installed, string modsPath, bool useRecordedFolderName = true)
	{
		string modName = ModNameOf(backupFile);
		List<GameMod> mods = installed.Where(m => !m.IsGroup).ToList();

		string stardewId = ReadId(Path.Combine(extracted, "manifest.json"));
		string recordedId = ReadId(Path.Combine(extracted, ".manager_manifest.json"));

		GameMod? current = mods.FirstOrDefault(m => stardewId.Length > 0 && Same(m.UniqueId, stardewId))
			?? mods.FirstOrDefault(m => recordedId.Length > 0 && Same(m.UniqueId, recordedId))
			?? mods.FirstOrDefault(m => Same(m.Name, modName) || Same(m.UniqueId, modName) ||
				Same(Path.GetFileName((m.FolderPath ?? "").TrimEnd(Path.DirectorySeparatorChar)), modName));

		if (current != null && !string.IsNullOrEmpty(current.FolderPath))
			return (current.FolderPath, current);

		string folder = useRecordedFolderName && recordedId.Length > 0 ? recordedId : modName;
		string safe = ModScanner.SanitiseFolderName(folder);
		return (Path.Combine(modsPath, safe.Length > 0 ? safe : "Restored Mod"), null);
	}

	/// <summary>
	/// Puts a backup back in place of the mod it came from, as the only thing that touches the mods folder. Throws if it
	/// cannot be done, with what was there left as it was. The one exception is a jar that wrote over a file of the same
	/// name while another copy would not go: see <see cref="ModInstaller.InstallFile"/>.
	///
	/// <para>
	/// ⚠️ Never unpacked straight into the mods folder. A backup of a folder has no top folder of its own, so its files
	/// landed loose among all the other mods with the broken one still in place, and on Stardew a <c>manifest.json</c>
	/// sitting there is listed as a mod whose folder is the whole of Mods. On Minecraft the jar landed beside the other
	/// version of itself, which Fabric refuses to start with. It reported success in both cases.
	/// </para>
	///
	/// <para>
	/// The copy being replaced is backed up first, so a restore can itself be undone. Backups are not pruned here, because
	/// the limit could remove the very one being restored.
	/// </para>
	/// </summary>
	/// <param name="backupFile">The backup's file.</param>
	/// <param name="modsFolder">The game's mods folder.</param>
	/// <param name="backupsFolder">Where the replaced copy is kept.</param>
	/// <param name="installed">The mods on disk now.</param>
	/// <param name="game">The game it is for.</param>
	/// <param name="logError">Told about leftovers that could not be cleared up. The restore still worked.</param>
	/// <returns>Whether a copy was replaced, so the caller says only what happened.</returns>
	public static bool Restore(string backupFile, string modsFolder, string backupsFolder,
		IEnumerable<GameMod> installed, GameProfile? game, Action<string, string> logError) =>
		RestoreWithResult(backupFile, modsFolder, backupsFolder, installed, game, logError).ReplacedACopy;

	/// <summary>What a restore did: whether a copy was replaced, which installed mod that was, and where the mod is now.</summary>
	public sealed record RestoreResult(bool ReplacedACopy, GameMod? Replaced, string Destination);

	/// <summary>
	/// <see cref="Restore"/>, saying also which installed mod was replaced and where the restored one went, for a caller
	/// with more to put right afterwards: the Windows head forgets the replaced copy's download record and re-links a
	/// Bethesda game's deployed files. Both heads restore through here, so the disk work is written once.
	/// </summary>
	public static RestoreResult RestoreWithResult(string backupFile, string modsFolder, string backupsFolder,
		IEnumerable<GameMod> installed, GameProfile? game, Action<string, string> logError)
	{
		string name = ModNameOf(backupFile);
		List<GameMod> mods = installed.Where(m => !m.IsGroup).ToList();
		string tempRoot = ModArchive.TempRootFor(modsFolder);
		string extracted = Path.Combine(tempRoot, "Extract_" + Path.GetRandomFileName());

		try
		{
			ModArchive.Extract(backupFile, extracted);
			// Only what was unpacked. Walking the whole mods folder failed a good restore over any link already in there.
			ModArchive.GuardAgainstEscapedEntries(extracted);

			if (!Directory.EnumerateFileSystemEntries(extracted).Any())
				throw new InvalidDataException(Loc.T("gtk.backupEmpty", name));

			if (game?.IsMinecraft == true)
			{
				string[] jars = Directory.GetFiles(extracted);
				if (jars.Length != 1) throw new InvalidDataException(Loc.T("gtk.backupNotOneJar", name));

				// The installed mod with the same id inside its jar, whatever its file is called.
				string id = MinecraftLayout.ReadModInfo(jars[0]).Id ?? "";
				GameMod? replacedMod = id.Length == 0 ? null : mods.FirstOrDefault(m => Same(m.UniqueId, id));

				int replaced = ModInstaller.BackUpCopiesBeingReplaced(jars[0], modsFolder, backupsFolder, maxBackupsPerMod: 0);

				ModInstaller.InstallResult result = ModInstaller.InstallFile(jars[0], modsFolder);
				if (result.CouldNotRemove.Count > 0)
					throw new IOException(Loc.T("mc.install.oldCopyStuck", string.Join(", ", result.CouldNotRemove)));

				return new RestoreResult(replaced > 0, replacedMod, result.Path);
			}

			// A mod listed with the mods folder as its own folder is not somewhere to put anything: replacing it replaces
			// every mod. See ModOnDisk.IsTheModsFolderItself.
			(string destination, GameMod? replacing) = Plan(extracted, backupFile,
				mods.Where(m => !ModOnDisk.IsTheModsFolderItself(m.FolderPath, modsFolder)), modsFolder,
				useRecordedFolderName: game?.IsBepInEx != true);

			bool keptTheOldCopy = Directory.Exists(destination);
			if (keptTheOldCopy) BackupStore.CreateBackup(destination, name, backupsFolder);

			ModFolderSwap.Replace(extracted, destination, new[] { destination }, tempRoot, logError);
			return new RestoreResult(keptTheOldCopy, replacing, destination);
		}
		finally
		{
			try { ModOnDisk.DeleteDirectory(extracted); }
			catch (Exception ex) { DiagnosticLog.WriteException("Backup", $"clearing the temporary folder {extracted}", ex); }
		}
	}

	private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

	private static string ReadId(string manifestPath)
	{
		try
		{
			if (!File.Exists(manifestPath)) return "";
			return (string?)JObject.Parse(File.ReadAllText(manifestPath))["UniqueID"] ?? "";
		}
		catch (Exception ex)
		{
			// A manifest that cannot be read is no reason to refuse a restore. The backup's name still says what it is.
			DiagnosticLog.WriteException("Backup", $"reading {manifestPath}", ex);
			return "";
		}
	}
}
