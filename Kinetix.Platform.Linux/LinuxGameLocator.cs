using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// Finds games on Linux: Steam's own library records for the install, and the Proton prefix for the
/// player's files.
///
/// <para>
/// The install side needs nothing new. <see cref="SteamLibraryLocator"/> already parses
/// <c>libraryfolders.vdf</c> and the per-game <c>.acf</c>, it is already in the core, and its tests already
/// pass off Windows — because a Steam library is laid out identically on both. A game installed through
/// Proton is in <c>steamapps/common</c> like any other; Proton changes how it runs, not where it lives.
/// </para>
///
/// <para>
/// The player's files are the part that genuinely differs, and only for the games that keep any: Skyrim and
/// Fallout 4 put their INIs, saves and load order under <c>Documents\My Games</c>, which for a Proton game
/// is inside that game's own prefix rather than anywhere in the Linux home directory.
/// </para>
/// </summary>
public sealed class LinuxGameLocator : IGameLocator
{
	private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

	/// <summary>
	/// Everywhere Steam puts itself. The order is the order they are tried; a machine may have several,
	/// with only one of them real — <c>~/.steam/steam</c> is very often a symlink to one of the others.
	/// </summary>
	private static IEnumerable<string> SteamRoots()
	{
		yield return Path.Combine(Home, ".steam", "steam");
		yield return Path.Combine(Home, ".steam", "root");
		yield return Path.Combine(Home, ".local", "share", "Steam");
		// Flatpak keeps its own home, and a user who installed Steam that way has no other.
		yield return Path.Combine(Home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
	}

	public IReadOnlyList<string> LibraryRoots() =>
		SteamRoots().Where(Directory.Exists).Distinct().ToList();

	public string? InstallFolder(GameProfile game)
	{
		if (game == null) return null;

		// Minecraft is sold by Mojang and has no Steam id at all; it installs beside its launcher.
		if (game.IsMinecraft)
		{
			string root = MinecraftLayout.DefaultRootFolder;
			return Directory.Exists(root) ? root : null;
		}

		if (string.IsNullOrEmpty(game.SteamAppId)) return null;

		foreach (string steam in SteamRoots())
		{
			if (!Directory.Exists(steam)) continue;

			try
			{
				string? found = SteamLibraryLocator.FindGameFolder(steam, game.SteamAppId);
				if (!string.IsNullOrEmpty(found) && Directory.Exists(found)) return found;
			}
			catch (Exception ex)
			{
				DiagnosticLog.WriteException("Detect", $"searching {steam} for {game.DisplayName}", ex);
			}
		}

		return null;
	}

	public string? UserDocumentsFolder(GameProfile game)
	{
		if (game == null || string.IsNullOrEmpty(game.SteamAppId)) return null;

		foreach (string steam in SteamRoots())
		{
			string documents = Path.Combine(steam, "steamapps", "compatdata", game.SteamAppId,
				"pfx", "drive_c", "users", "steamuser", "Documents");

			// A prefix that does not exist yet is not a failure. It means the game has never been started,
			// and the folder appears the first time it is - so this is worth asking again later rather than
			// reporting as missing.
			if (Directory.Exists(documents)) return documents;
		}

		return null;
	}

	/// <summary>
	/// The Proton prefix's <c>drive_c</c> for a game, for callers that need somewhere else inside it.
	/// <c>null</c> until the game has been launched once.
	/// </summary>
	public string? ProtonDriveC(GameProfile game)
	{
		if (game == null || string.IsNullOrEmpty(game.SteamAppId)) return null;

		return SteamRoots()
			.Select(s => Path.Combine(s, "steamapps", "compatdata", game.SteamAppId, "pfx", "drive_c"))
			.FirstOrDefault(Directory.Exists);
	}
}
