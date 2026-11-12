namespace KinetixModManager;

/// <summary>
/// A file in the game folder that a deployment put there for a mod which is still installed, but whose own copy has gone from the
/// mod's folder. A deployment removes a file no mod provides any more, so it deleted the only copy left: on 2026-11-11, installing
/// one mod into a real Skyrim took the Unofficial Skyrim Special Edition Patch's .esp and both its .bsa files out of the game,
/// because at some point earlier they had gone from that mod's folder, and nothing was kept anywhere. The game's copy goes back into
/// the mod's folder instead, so a mod still switched on keeps working, and a mod switched off can be switched on again.
/// </summary>
public static class DeployedFileRescue
{
	/// <summary>
	/// Where a file deployed at <paramref name="deployedRel"/> (relative to the game folder) belongs inside its mod's folder. Under
	/// Data it is the same path without "Data"; anything else sits beside the game's .exe, and a mod keeps those in its Root folder,
	/// which deployment puts in the same place.
	/// </summary>
	public static string PathInMod(string deployedRel)
	{
		string rel = deployedRel.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
		string data = "Data" + Path.DirectorySeparatorChar;
		return rel.StartsWith(data, StringComparison.OrdinalIgnoreCase) ? rel.Substring(data.Length) : Path.Combine("Root", rel);
	}

	/// <summary>
	/// Copies the game's file back into its mod's folder when the mod has lost its own copy. <paramref name="link"/> makes a hard link
	/// (new name, existing file) and says whether it could, so the two are one file again, as a deployed file is to its mod's; a copy
	/// is the fallback. Throws when putting it back fails, so the caller leaves the game's copy alone.
	/// </summary>
	public static Rescue PutBackIntoMod(string gameFile, string modFolder, string deployedRel, Func<string, string, bool> link)
	{
		if (!File.Exists(gameFile) || string.IsNullOrEmpty(modFolder) || !Directory.Exists(modFolder)) return Rescue.NothingToKeep;

		string target = Path.Combine(modFolder, PathInMod(deployedRel));
		// The usual case when a mod is switched off: its files are all still in its folder, and nothing needs doing.
		if (File.Exists(target)) return Rescue.AlreadyInMod;

		Directory.CreateDirectory(Path.GetDirectoryName(target)!);
		if (!link(target, gameFile)) File.Copy(gameFile, target);
		return Rescue.PutBack;
	}

	public enum Rescue
	{
		/// <summary>The game has no such file, or the mod's folder isn't there (a deleted mod), so there's nothing to keep.</summary>
		NothingToKeep,
		/// <summary>The mod still has its own copy.</summary>
		AlreadyInMod,
		/// <summary>The mod had lost its copy, and has the game's now.</summary>
		PutBack
	}
}
