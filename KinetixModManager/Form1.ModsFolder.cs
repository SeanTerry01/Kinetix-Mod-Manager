using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// Moving a copy's staged mods between the manager's own folder under <c>%AppData%</c> and a folder inside the
/// game itself.
///
/// Only Skyrim SE and Fallout 4 stage mods outside the game at all — the other three games' loaders read a fixed
/// folder inside the install, so there is nothing to choose. Staging inside the game is what the other games
/// already do, and it has two real advantages: a copy becomes self-contained, which matters the moment someone
/// owns the game twice, and the staging folder ends up on the same volume as the game, so deployment can always
/// hard-link instead of falling back to copying every file a second time.
///
/// It also has a real cost, which is why this is a choice and not a default for anyone who already has mods:
/// uninstalling the game through Steam or GOG deletes the game folder, and the mods inside it go too.
/// </summary>
public partial class Form1
{
	/// <summary>
	/// Moves <paramref name="installKey"/>'s staged mods to the game folder (<paramref name="intoGameFolder"/>
	/// true) or back to <c>%AppData%</c>, after confirming with the user. Returns <c>true</c> only when the move
	/// happened and the recorded path now points at the new location — so a caller can put its checkbox back on
	/// a decline or a failure.
	///
	/// Nothing is deleted at any point. The files are copied, verified by count, and only then is the old folder
	/// removed; if anything goes wrong the original is still there and the setting is left untouched.
	/// </summary>
	private bool TryMoveModsFolder(string installKey, bool intoGameFolder, string gameFolder)
	{
		GameInstall? install = _settings.InstallFor(installKey);
		GameProfile? profile = GameProfiles.Find(installKey);
		if (install == null || profile == null || string.IsNullOrEmpty(profile.StagingFolderName))
		{
			Speak(Loc.T("modsfolder.notApplicable"));
			return false;
		}

		if (intoGameFolder && (string.IsNullOrEmpty(gameFolder) || !Directory.Exists(gameFolder)))
		{
			Speak(Loc.T("modsfolder.needGameFolderSpeak"));
			SpeakBox(Loc.T("modsfolder.needGameFolderBox"), Loc.T("modsfolder.title"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return false;
		}

		// Work out both ends from the same resolver the rest of the app uses, so this can never move mods
		// somewhere the session then fails to look.
		string source = _settings.GameModsPaths.TryGetValue(installKey, out string? current) ? current : "";
		var proposed = new GameInstall
		{
			Key = install.Key, GameId = install.GameId, Platform = install.Platform,
			Folder = string.IsNullOrEmpty(gameFolder) ? install.Folder : gameFolder,
			ModsInGameFolder = intoGameFolder
		};
		string destination = ResolveModsFolder(proposed);

		if (string.IsNullOrEmpty(destination))
		{
			Speak(Loc.T("modsfolder.notApplicable"));
			return false;
		}

		if (!string.IsNullOrEmpty(source) && PathsAreSame(source, destination))
		{
			// Already where it is being asked to go — record the flag and say nothing alarming.
			install.ModsInGameFolder = intoGameFolder;
			install.Folder = proposed.Folder;
			_settings.Save();
			return true;
		}

		int modCount = CountModFolders(source);

		// The warning is the point of the confirmation, so it leads. Spoken as well as shown, because the box's
		// text is read on focus but the consequence is the part that must not be skimmed past.
		string confirmBody = intoGameFolder
			? Loc.T("modsfolder.confirmIntoGame", modCount, destination)
			: Loc.T("modsfolder.confirmOutOfGame", modCount, destination);

		Speak(intoGameFolder ? Loc.T("modsfolder.confirmIntoGameSpeak") : Loc.T("modsfolder.confirmOutOfGameSpeak"));
		if (SpeakBox(confirmBody, Loc.T("modsfolder.title"), MessageBoxButtons.YesNo,
				intoGameFolder ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes)
		{
			Speak(Loc.T("modsfolder.cancelled"));
			return false;
		}

		try
		{
			SetStatus(Loc.T("modsfolder.moving", modCount), speak: true);
			MoveFolderContents(source, destination);
		}
		catch (Exception ex)
		{
			LogFailure("Mods folder", $"Could not move '{source}' to '{destination}'", ex);
			ResetStatus();
			Speak(Loc.T("modsfolder.failedSpeak"));
			SpeakBox(Loc.T("modsfolder.failedBox", source, FriendlyError(ex)), Loc.T("modsfolder.title"),
				MessageBoxButtons.OK, MessageBoxIcon.Error);
			return false;
		}

		install.ModsInGameFolder = intoGameFolder;
		install.Folder = proposed.Folder;
		_settings.GameModsPaths[installKey] = destination;
		if (GameProfiles.IsGame(installKey, GameProfiles.StardewValley) && installKey == GameProfiles.StardewValley)
			_settings.ModsPath = destination;
		_settings.Save();

		ResetStatus();
		string done = Loc.T("modsfolder.done", modCount, destination);
		Speak(done);
		SpeakBox(done, Loc.T("modsfolder.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);

		// The mod list is read from the path that just changed.
		if (_settings.ActiveGame == installKey) RefreshAllData(checkUpdates: false);
		return true;
	}

	/// <summary>How many mods are in <paramref name="folder"/> — folders, since that is what a staged mod is.</summary>
	private static int CountModFolders(string folder)
	{
		try
		{
			return string.IsNullOrEmpty(folder) || !Directory.Exists(folder)
				? 0
				: Directory.GetDirectories(folder).Length;
		}
		catch { return 0; }
	}

	/// <summary>True when two paths name the same place, whatever their spelling.</summary>
	private static bool PathsAreSame(string a, string b)
	{
		try
		{
			return string.Equals(
				Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
				Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
				StringComparison.OrdinalIgnoreCase);
		}
		catch { return false; }
	}

	/// <summary>
	/// Moves everything in <paramref name="source"/> into <paramref name="destination"/>, then removes the empty
	/// source.
	///
	/// Copy-then-delete rather than <c>Directory.Move</c>: the two ends are usually on different volumes (that is
	/// rather the point of the move), where <c>Move</c> fails outright. The source is only deleted once every
	/// entry has arrived, so an interrupted move leaves the mods where they were rather than half in each place.
	/// </summary>
	private static void MoveFolderContents(string source, string destination)
	{
		Directory.CreateDirectory(destination);
		if (string.IsNullOrEmpty(source) || !Directory.Exists(source)) return;

		foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
			Directory.CreateDirectory(dir.Replace(source, destination));

		foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
			File.Copy(file, file.Replace(source, destination), overwrite: true);

		int copied = Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length;
		int expected = Directory.GetFiles(source, "*", SearchOption.AllDirectories).Length;
		if (copied < expected)
			throw new IOException($"Only {copied} of {expected} files arrived; the original has been left alone.");

		Directory.Delete(source, recursive: true);
	}
}
