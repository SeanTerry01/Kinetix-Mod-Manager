using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// The Witcher 3's own record of which mods it has and whether they are switched on: <c>mods.settings</c>, kept
/// beside the game's other per-player files in <c>Documents\The Witcher 3</c>.
///
/// It matters because it is what the game's own mod menu shows. The folder name is still what decides whether a
/// mod loads — the engine reads folders called <c>mod*</c> and walks past everything else, which is why the
/// manager enables and disables by renaming, exactly as it does for the other games. But a mod left listed as
/// enabled here after the manager has switched it off makes the in-game menu disagree with the manager, and a
/// player who checks both is right to believe neither. So the file is kept in step.
///
/// The format is plain INI — one <c>[modFolderName]</c> section per mod, with <c>Enabled</c> and (on the
/// next-gen releases) <c>Priority</c> — so <see cref="IniDocument"/> does the parsing, and every unrelated line
/// the game or another tool put there is preserved untouched.
/// </summary>
public static class Witcher3ModSettings
{
	/// <summary>The file's name inside the game's per-player folder.</summary>
	public const string FileName = "mods.settings";

	private const string EnabledKey = "Enabled";
	private const string PriorityKey = "Priority";

	/// <summary>What the file records about one mod.</summary>
	public sealed record ModEntry(string Name, bool Enabled, int? Priority);

	/// <summary>The full path of <c>mods.settings</c> for a game whose per-player folder is
	/// <paramref name="userDataDirectory"/>, or <c>""</c> when there is no such folder.</summary>
	public static string PathFor(string userDataDirectory) =>
		string.IsNullOrEmpty(userDataDirectory) ? "" : Path.Combine(userDataDirectory, FileName);

	/// <summary>
	/// Every mod the file lists, in file order. An empty list for a file that doesn't exist yet — which is the
	/// normal state of a copy of the game that has never been modded, not an error.
	/// </summary>
	public static List<ModEntry> Read(string modsSettingsPath)
	{
		var result = new List<ModEntry>();
		if (string.IsNullOrEmpty(modsSettingsPath) || !File.Exists(modsSettingsPath)) return result;

		try
		{
			IniDocument doc = IniDocument.Load(modsSettingsPath);
			foreach (var group in doc.Entries().GroupBy(e => e.Section, StringComparer.OrdinalIgnoreCase))
			{
				if (string.IsNullOrEmpty(group.Key)) continue;

				string? enabled = group.FirstOrDefault(e => e.Key.Equals(EnabledKey, StringComparison.OrdinalIgnoreCase))?.Value;
				string? priority = group.FirstOrDefault(e => e.Key.Equals(PriorityKey, StringComparison.OrdinalIgnoreCase))?.Value;

				result.Add(new ModEntry(
					group.Key,
					// Absent counts as enabled: that is how the game treats a mod folder it can see, and the
					// setting only ever appears once something has switched the mod off.
					enabled == null || IsTruthy(enabled),
					int.TryParse(priority, out int p) ? p : null));
			}
		}
		catch
		{
			// A malformed or unreadable mods.settings is the game's business, not a reason to fail a mod scan.
		}

		return result;
	}

	/// <summary>Whether the file switches <paramref name="modFolderName"/> off. A mod the file says nothing
	/// about is not switched off — the game loads a folder it can see unless told otherwise.</summary>
	public static bool IsDisabledByFile(string modsSettingsPath, string modFolderName) =>
		Read(modsSettingsPath).Any(e =>
			e.Name.Equals(modFolderName, StringComparison.OrdinalIgnoreCase) && !e.Enabled);

	/// <summary>
	/// Records <paramref name="modFolderName"/> as enabled or disabled, adding its section if the file has none
	/// for it. Does nothing when there is no file yet and the mod is being enabled: writing a file the game has
	/// never created, purely to say "on", would be the manager inventing state it wasn't asked for.
	/// </summary>
	public static void SetEnabled(string modsSettingsPath, string modFolderName, bool enabled)
	{
		if (string.IsNullOrEmpty(modsSettingsPath) || string.IsNullOrEmpty(modFolderName)) return;
		if (!File.Exists(modsSettingsPath) && enabled) return;

		try
		{
			string? dir = Path.GetDirectoryName(modsSettingsPath);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

			IniDocument doc = IniDocument.Load(modsSettingsPath);
			doc.SetValue(BareName(modFolderName), EnabledKey, enabled ? "1" : "0");
			doc.Save(modsSettingsPath);
		}
		catch
		{
			// The rename is what actually enables or disables the mod; this file only keeps the game's own menu
			// in step, so failing to write it must not fail the operation the user asked for.
		}
	}

	/// <summary>Sets a mod's load priority, the number the next-gen releases sort conflicting mods by.</summary>
	public static void SetPriority(string modsSettingsPath, string modFolderName, int priority)
	{
		if (string.IsNullOrEmpty(modsSettingsPath) || string.IsNullOrEmpty(modFolderName)) return;

		try
		{
			string? dir = Path.GetDirectoryName(modsSettingsPath);
			if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

			IniDocument doc = IniDocument.Load(modsSettingsPath);
			doc.SetValue(BareName(modFolderName), PriorityKey, priority.ToString());
			doc.Save(modsSettingsPath);
		}
		catch { }
	}

	/// <summary>
	/// Drops <paramref name="modFolderName"/>'s section entirely, for a mod that has been uninstalled. Leaving it
	/// behind would keep the mod in the game's own menu after its files are gone.
	/// </summary>
	public static void Remove(string modsSettingsPath, string modFolderName)
	{
		if (string.IsNullOrEmpty(modsSettingsPath) || !File.Exists(modsSettingsPath)) return;

		try
		{
			string section = BareName(modFolderName);
			var kept = new List<string>();
			bool inTarget = false;

			foreach (string raw in File.ReadAllLines(modsSettingsPath))
			{
				string line = raw.Trim();
				if (line.StartsWith("[") && line.EndsWith("]") && line.Length >= 2)
					inTarget = line.Substring(1, line.Length - 2).Trim()
						.Equals(section, StringComparison.OrdinalIgnoreCase);

				if (!inTarget) kept.Add(raw);
			}

			File.WriteAllLines(modsSettingsPath, kept);
		}
		catch { }
	}

	/// <summary>
	/// The name the file knows a mod by: its folder name with any disabled marker stripped. The manager disables
	/// by renaming <c>modFoo</c> to <c>~modFoo</c>, and the game's own record is always keyed on the real name.
	/// </summary>
	public static string BareName(string modFolderName) =>
		modFolderName.StartsWith("~", StringComparison.Ordinal) ? modFolderName.Substring(1) : modFolderName;

	private static bool IsTruthy(string value) =>
		value.Trim() is "1" or "true" or "True" or "TRUE" or "yes";
}
