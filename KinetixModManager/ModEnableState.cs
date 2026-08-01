using System;
using System.IO;

namespace KinetixModManager;

/// <summary>
/// Works out where a mod folder has to move to in order to be enabled or disabled. Kept apart from the file
/// I/O in <see cref="ModFileSystem"/> so the rules can be unit-tested directly — a mistake here silently
/// leaves a mod running when the user has switched it off, which is the kind of bug that is only noticed in
/// game, long after the fact.
/// </summary>
public static class ModEnableState
{
	/// <summary>The folder BepInEx mods are parked in when disabled, beside <c>plugins</c>.</summary>
	public const string BepInExDisabledFolderName = "plugins-disabled";

	/// <summary>The folder BepInEx loads plugins from.</summary>
	public const string BepInExPluginsFolderName = "plugins";

	/// <summary>
	/// Where <paramref name="modFolderPath"/> must end up for the mod to be <paramref name="enable"/>d, or the
	/// path unchanged when it is already in the right place.
	///
	/// The two layouts differ because the loaders differ. SMAPI and the manager's own Bethesda deployment both
	/// skip a folder whose name starts with a dot, so there a mod is switched off by renaming it in place.
	/// BepInEx's chainloader takes no notice of folder names at all — it walks <c>plugins</c> looking for DLLs —
	/// so a dot-renamed BepInEx mod would carry on loading. Those mods move out of the scanned folder entirely.
	/// </summary>
	public static string TargetPath(string modFolderPath, bool enable, string activeGame)
	{
		if (string.IsNullOrEmpty(modFolderPath)) return modFolderPath;

		string trimmed = modFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string folderName = Path.GetFileName(trimmed);
		string parent = Path.GetDirectoryName(trimmed) ?? "";

		if (folderName.Length == 0) return modFolderPath;

		if (GameProfiles.Find(activeGame)?.IsBepInEx == true)
		{
			// plugins and plugins-disabled are siblings under BepInEx, so the destination is simply the other one.
			string bepInExRoot = Path.GetDirectoryName(parent) ?? "";
			if (bepInExRoot.Length == 0) return modFolderPath;

			string targetParent = Path.Combine(
				bepInExRoot,
				enable ? BepInExPluginsFolderName : BepInExDisabledFolderName);
			return Path.Combine(targetParent, folderName);
		}

		string bare = folderName.StartsWith(".") ? folderName.Substring(1) : folderName;
		return Path.Combine(parent, enable ? bare : "." + bare);
	}

	/// <summary>
	/// Whether the mod at <paramref name="modFolderPath"/> is currently enabled, judged the same way the loader
	/// judges it: by the parent folder for a BepInEx game, and by the leading dot everywhere else.
	/// </summary>
	public static bool IsEnabled(string modFolderPath, string activeGame)
	{
		if (string.IsNullOrEmpty(modFolderPath)) return false;

		string trimmed = modFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		if (GameProfiles.Find(activeGame)?.IsBepInEx == true)
		{
			string parentName = Path.GetFileName(Path.GetDirectoryName(trimmed) ?? "");
			return !string.Equals(parentName, BepInExDisabledFolderName, StringComparison.OrdinalIgnoreCase);
		}

		return !Path.GetFileName(trimmed).StartsWith(".");
	}
}
