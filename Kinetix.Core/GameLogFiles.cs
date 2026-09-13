using System;
using System.Collections.Generic;
using System.IO;

namespace KinetixModManager;

/// <summary>
/// Where each game's mod loader writes its log, and how to read one while the game still has it open.
///
/// <para>
/// This matters more in this program than the subject usually deserves. The characteristic failure of a
/// modded game, for the people this manager is for, is not a crash — it is silence: the game starts, plays
/// perfectly, and never speaks, because the accessibility mod did not load. The log is the only thing that
/// can tell a modded launch from an unmodded one, and a player who cannot see the screen has no other
/// evidence at all.
/// </para>
///
/// <para>
/// Lifted out of Form1.SmapiLog by Phase 4. Locating a file and reading it are not things a window should
/// know how to do, and a second front end needs both.
/// </para>
/// </summary>
public static class GameLogFiles
{
	/// <summary>Whether this game's loader keeps a log the manager can show.</summary>
	public static bool HasLoaderLog(GameProfile? game) =>
		game != null && (game.IsBethesda || game.IsBepInEx || game.IsWitcher3 || game.IsMinecraft);

	/// <summary>
	/// The folder the loader writes its logs into, or empty when there is none.
	///
	/// <paramref name="minecraftRoot"/> is asked for rather than worked out, because Minecraft's root is
	/// the one location that does not follow from the game folder — it lives beside the launcher.
	/// </summary>
	public static string LoaderLogFolder(GameProfile? game, string gameFolder, string minecraftRoot = "")
	{
		if (game == null) return "";

		if (game.IsBethesda)
		{
			// The user-data folder differs between a Steam copy and a GOG one — "Skyrim Special Edition"
			// against "Skyrim Special Edition GOG" — so a hard-coded folder name here would silently show
			// the wrong copy's log, most confusingly on the machine where both are installed.
			string userData = game.UserDataDirectoryFor(gameFolder);
			if (string.IsNullOrEmpty(userData)) return "";
			return Path.Combine(userData, game.Id == GameProfiles.Fallout4 ? "F4SE" : "SKSE");
		}

		// BepInEx writes LogOutput.log beside its own config and plugins.
		if (game.IsBepInEx)
			return string.IsNullOrEmpty(gameFolder) ? "" : Path.Combine(gameFolder, "BepInEx");

		// The Witcher 3 writes no log of its own, but the mods that hook it do, and they write beside the
		// game's executable — which is where a player asking "why did my mod not load" needs to be sent.
		if (game.IsWitcher3)
			return string.IsNullOrEmpty(gameFolder) ? "" : Path.Combine(gameFolder, "bin", "x64");

		if (game.IsMinecraft)
			return string.IsNullOrEmpty(minecraftRoot) ? "" : Path.Combine(minecraftRoot, "logs");

		return "";
	}

	/// <summary>The full path of the loader's own log, or empty when the game keeps none.</summary>
	public static string LoaderLogPath(GameProfile? game, string gameFolder, string minecraftRoot = "")
	{
		if (game == null || string.IsNullOrEmpty(game.LoaderLogFileName)) return "";

		string folder = LoaderLogFolder(game, gameFolder, minecraftRoot);
		return string.IsNullOrEmpty(folder) ? "" : Path.Combine(folder, game.LoaderLogFileName);
	}

	/// <summary>SMAPI's own error-log folder. Stardew Valley only, and the same on every platform SMAPI runs on.</summary>
	public static string SmapiLogFolder() => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs");

	/// <summary>SMAPI's current log file.</summary>
	public static string SmapiLogPath() => Path.Combine(SmapiLogFolder(), "SMAPI-latest.txt");

	/// <summary>
	/// Reads every line of a file even while another process holds it open for writing.
	///
	/// SMAPI keeps <c>SMAPI-latest.txt</c> open for the whole game session, and
	/// <see cref="File.ReadAllLines"/> opens with only <see cref="FileShare.Read"/> — so it throws a sharing
	/// violation while the game is running and the log appears empty. Which is exactly when the log is
	/// wanted. Opening with <see cref="FileShare.ReadWrite"/> reads the live file without disturbing the
	/// writer.
	/// </summary>
	public static string[] ReadAllLinesShared(string path)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new StreamReader(stream);

		var lines = new List<string>();
		string? line;
		while ((line = reader.ReadLine()) != null) lines.Add(line);
		return lines.ToArray();
	}

	/// <summary>Reads a whole file as text while another process still has it open. See <see cref="ReadAllLinesShared"/>.</summary>
	public static string ReadAllTextShared(string path)
	{
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}
}
