using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace KinetixModManager;

/// <summary>
/// Putting a downloaded mod where the game will load it, for the layouts where a mod is a single file.
///
/// <para>
/// Minecraft today; anything else that ships one file per mod tomorrow. The folder-shaped layouts —
/// Stardew's manifest folders, the Bethesda staging tree, BepInEx plugins — still go through the archive
/// pipeline in the WinForms app, and this deliberately does not pretend otherwise.
/// </para>
///
/// <para>
/// The rule that matters is removing what the new file replaces. Fabric loads every jar in the folder, so
/// leaving Sodium 0.5.8 beside Sodium 0.6.0 does not give the player the newer one — it gives them a game
/// that refuses to start, citing a duplicate mod id. Worse, the same is true of a disabled copy: a
/// <c>.jar.disabled</c> left behind is invisible to Fabric but not to the manager, so the list shows a mod
/// twice and switching one on breaks the game in a way that looks unrelated to the install that caused it.
/// </para>
/// </summary>
public static class ModInstaller
{
	/// <summary>What an install did, so the caller can say so without guessing.</summary>
	public readonly record struct InstallResult(string Path, string ModId, IReadOnlyList<string> Replaced)
	{
		/// <summary>True when this install took the place of a copy already there.</summary>
		public bool WasUpgrade => Replaced.Count > 0;
	}

	/// <summary>
	/// Every file in <paramref name="modsFolder"/> that declares the same mod id as
	/// <paramref name="jarPath"/> — the copies a new install has to remove.
	///
    /// Matched by the id inside the jar rather than by file name, because the file name is whatever the
	/// author called the download and changes between releases: <c>sodium-fabric-0.5.8.jar</c> and
	/// <c>sodium-fabric-mc1.21.1-0.6.0.jar</c> share no prefix worth matching on. Disabled copies count,
	/// for the reason given on this class.
	/// </summary>
	public static IReadOnlyList<string> ExistingCopies(string modsFolder, string jarPath, string modId)
	{
		var found = new List<string>();
		if (string.IsNullOrEmpty(modId) || !Directory.Exists(modsFolder)) return found;

		foreach (string candidate in Directory.EnumerateFiles(modsFolder))
		{
			if (string.Equals(candidate, jarPath, StringComparison.OrdinalIgnoreCase)) continue;

			string name = Path.GetFileName(candidate);
			if (!name.EndsWith(MinecraftLayout.ModExtension, StringComparison.OrdinalIgnoreCase) &&
				!name.EndsWith(MinecraftLayout.ModExtension + ".disabled", StringComparison.OrdinalIgnoreCase))
				continue;

			try
			{
				FabricModInfo info = MinecraftLayout.ReadModInfo(candidate);
				if (!info.IsUnreadable && string.Equals(info.Id, modId, StringComparison.OrdinalIgnoreCase))
					found.Add(candidate);
			}
			catch (Exception ex)
			{
				// A jar that cannot be read is not a match we can claim. Deleting it on a guess would be
				// removing a mod the user installed for reasons we failed to understand.
				DiagnosticLog.WriteException("Install", $"reading {candidate} while checking for older copies", ex);
			}
		}

		return found;
	}

	/// <summary>
	/// Moves an already-downloaded file into <paramref name="modsFolder"/>, removing any older copy of the
	/// same mod.
	///
	/// The new file is put in place <em>before</em> the old ones are removed, deliberately. If this fails
	/// half way the player is left with a working mod folder holding the copy they already had, rather than
	/// one with the mod missing entirely — which for a mod their game's speech depends on is the difference
	/// between an install that did not work and a game that no longer talks.
	/// </summary>
	public static InstallResult InstallFile(string downloadedPath, string modsFolder)
	{
		if (!File.Exists(downloadedPath)) throw new FileNotFoundException("Nothing was downloaded.", downloadedPath);

		Directory.CreateDirectory(modsFolder);

		string destination = Path.Combine(modsFolder, Path.GetFileName(downloadedPath));
		if (!string.Equals(downloadedPath, destination, StringComparison.OrdinalIgnoreCase))
			File.Copy(downloadedPath, destination, overwrite: true);

		string modId = "";
		try { modId = MinecraftLayout.ReadModInfo(destination).Id; }
		catch (Exception ex) { DiagnosticLog.WriteException("Install", $"reading the id of {destination}", ex); }

		var replaced = new List<string>();
		foreach (string old in ExistingCopies(modsFolder, destination, modId))
		{
			try { File.Delete(old); replaced.Add(Path.GetFileName(old)); }
			catch (Exception ex)
			{
				// Worth reporting rather than swallowing: the mod is installed, but the game will now refuse
				// to start on a duplicate id, and nothing about that failure would point back here.
				DiagnosticLog.WriteException("Install", $"removing the older copy at {old}", ex);
			}
		}

		return new InstallResult(destination, modId, replaced);
	}

	/// <summary>
	/// Downloads the newest build of a Modrinth project for a Minecraft version and installs it.
	///
	/// Returns null when the project has no file for that version at all — which is a normal answer rather
	/// than a failure, and one the user has to be told plainly: a mod built for another version installs
	/// perfectly and then loads nothing.
	/// </summary>
	public static async Task<InstallResult?> InstallFromModrinthAsync(
		string projectIdOrSlug, string gameVersion, string modsFolder, string downloadFolder)
	{
		ModrinthFile? file = await ModrinthService.GetLatestFileAsync(projectIdOrSlug, gameVersion);
		if (file == null) return null;

		Directory.CreateDirectory(downloadFolder);
		string downloaded = await ModrinthService.DownloadAsync(file, downloadFolder);

		return InstallFile(downloaded, modsFolder);
	}
}
