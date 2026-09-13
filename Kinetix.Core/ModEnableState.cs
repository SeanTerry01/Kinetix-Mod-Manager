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

	/// <summary>The prefix used by the games that mark a disabled mod with one, when the game isn't known.</summary>
	private const string DefaultDisabledPrefix = ".";

	/// <summary>The prefix that marks a disabled mod folder for <paramref name="activeGame"/>.</summary>
	private static string DisabledPrefix(string activeGame) =>
		GameProfiles.Find(activeGame)?.DisabledModPrefix ?? DefaultDisabledPrefix;

	/// <summary>
	/// Where <paramref name="modFolderPath"/> must end up for the mod to be <paramref name="enable"/>d, or the
	/// path unchanged when it is already in the right place.
	///
	/// The layouts differ because the loaders differ. SMAPI and the manager's own Bethesda deployment both skip a
	/// folder whose name starts with a dot, and The Witcher 3 loads only folders called <c>mod*</c> — so a
	/// leading <c>~</c> takes one out of the running there. In all of those a mod is switched off by renaming it
	/// in place; only the prefix changes. BepInEx's chainloader takes no notice of folder names at all — it walks
	/// <c>plugins</c> looking for DLLs — so a renamed BepInEx mod would carry on loading. Those mods move out of
	/// the scanned folder entirely.
	/// </summary>
	public static string TargetPath(string modFolderPath, bool enable, string activeGame)
	{
		if (string.IsNullOrEmpty(modFolderPath)) return modFolderPath;

		string trimmed = modFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string folderName = Path.GetFileName(trimmed);
		string parent = Path.GetDirectoryName(trimmed) ?? "";

		if (folderName.Length == 0) return modFolderPath;

		GameProfile? enableProfile = GameProfiles.Find(activeGame);

		// Minecraft's mods are files, not folders, so there is nowhere to put a prefix that wouldn't also change
		// the name Fabric reports. Fabric accepts a candidate only when it ends in ".jar", so appending to the
		// end is what takes a mod out of the running — the same trick as The Witcher 3's "~", at the other end
		// of the name.
		if (enableProfile?.IsMinecraft == true)
			return MinecraftLayout.PathWithEnabled(trimmed, enable, enableProfile.DisabledModSuffix);

		if (enableProfile?.IsBepInEx == true)
		{
			// plugins and plugins-disabled are siblings under BepInEx, so the destination is simply the other one.
			string bepInExRoot = Path.GetDirectoryName(parent) ?? "";
			if (bepInExRoot.Length == 0) return modFolderPath;

			string targetParent = Path.Combine(
				bepInExRoot,
				enable ? BepInExPluginsFolderName : BepInExDisabledFolderName);
			return Path.Combine(targetParent, folderName);
		}

		string prefix = DisabledPrefix(activeGame);
		string bare = folderName.StartsWith(prefix, StringComparison.Ordinal)
			? folderName.Substring(prefix.Length)
			: folderName;
		return Path.Combine(parent, enable ? bare : prefix + bare);
	}

	/// <summary>
	/// Whether the mod at <paramref name="modFolderPath"/> is currently enabled, judged the same way the loader
	/// judges it: by the parent folder for a BepInEx game, and by the game's disabled-mod prefix everywhere else.
	/// </summary>
	public static bool IsEnabled(string modFolderPath, string activeGame)
	{
		if (string.IsNullOrEmpty(modFolderPath)) return false;

		string trimmed = modFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		GameProfile? profile = GameProfiles.Find(activeGame);

		// Judged exactly as Fabric judges it: does the file name end in ".jar"?
		if (profile?.IsMinecraft == true) return MinecraftLayout.IsEnabledModFile(trimmed);

		if (profile?.IsBepInEx == true)
		{
			string parentName = Path.GetFileName(Path.GetDirectoryName(trimmed) ?? "");
			return !string.Equals(parentName, BepInExDisabledFolderName, StringComparison.OrdinalIgnoreCase);
		}

		return !Path.GetFileName(trimmed).StartsWith(DisabledPrefix(activeGame), StringComparison.Ordinal);
	}

	/// <summary>
	/// The top-level folder a mod sits in, within whichever root holds it — the key the installed list groups by,
	/// so two mods answer with the same string only when they really do share a folder.
	///
	/// <para>
	/// The disabled folder is what makes this more than one line. For every other game a disabled mod is renamed
	/// where it stands, so it stays under the mods folder; a disabled BepInEx mod moves out of it entirely, into
	/// <c>plugins-disabled</c> — a <em>sibling</em> of <c>plugins</c>, not a child. Measured from the mods folder
	/// its path therefore reads <c>..\plugins-disabled\SomeMod</c>, and the first segment of that is <c>..</c> for
	/// every disabled mod alike. Keyed on it they collapsed into a single group the moment a second mod was
	/// switched off — one that could not even be named, because <c>..</c> is not a name.
	/// </para>
	///
	/// <para>
	/// A mod somewhere else again is keyed by its own folder. A shared key is a claim that two mods came out of
	/// one folder, and the honest answer for a mod whose location is not understood is that it is on its own.
	/// </para>
	/// </summary>
	public static string InstalledGroupFolder(string modsRoot, string modFolderPath)
	{
		if (string.IsNullOrEmpty(modFolderPath)) return "";

		string trimmed = modFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string ownFolder = Path.GetFileName(trimmed);
		if (string.IsNullOrEmpty(modsRoot)) return ownFolder;

		if (FirstSegmentUnder(modsRoot, trimmed) is { } inMods) return inMods;

		string bepInExRoot = Path.GetDirectoryName(modsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "";
		if (bepInExRoot.Length > 0 &&
			FirstSegmentUnder(Path.Combine(bepInExRoot, BepInExDisabledFolderName), trimmed) is { } inDisabled)
			return inDisabled;

		return ownFolder;
	}

	/// <summary>
	/// The first path segment of <paramref name="path"/> beneath <paramref name="root"/>, or <c>null</c> when it
	/// is not beneath it at all — which <see cref="Path.GetRelativePath"/> reports by walking back out with
	/// <c>..</c>, or by handing back an absolute path when the two are on different drives.
	/// </summary>
	private static string? FirstSegmentUnder(string root, string path)
	{
		string relative;
		try { relative = Path.GetRelativePath(root, path); }
		catch { return null; }

		if (Path.IsPathRooted(relative)) return null;
		if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return null;

		int sep = relative.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
		return sep == -1 ? relative : relative.Substring(0, sep);
	}
}
