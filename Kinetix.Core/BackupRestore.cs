using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
