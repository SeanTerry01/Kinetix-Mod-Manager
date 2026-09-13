using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>One folder that can be renamed, and what it would be called.</summary>
public sealed record FolderTidyRename(string From, string To);

/// <summary>One folder that carries a tail but is being left alone, and why.</summary>
public sealed record FolderTidySkip(string Folder, string Reason);

/// <summary>
/// Works out what an untidy mod folder should be called.
///
/// <para>
/// A mod installed from Nexus lands in a folder named after the download it came out of, machinery and all —
/// <c>Achievements Mods Enabler SE-AE-245-1-41-1715217907</c>. The manager reads the mod's real name from the
/// <c>.manager_manifest.json</c> it writes beside it, so the list has always sounded right; it is only the folder
/// on disk that looks like a serial number, and anybody who opens that folder in Explorer sees it.
/// </para>
///
/// <para>
/// Which is the same choice the two big managers made differently. <b>Vortex</b> names its staging folders exactly
/// after the archive, suffix included, and hides the suffix in its own interface — so the folders look like this
/// one's did. <b>Mod Organizer 2</b> gives each mod a readable folder and keeps the mod id, version and source
/// archive in a <c>meta.ini</c> beside it. This manager already writes that sidecar; naming the folder after the
/// mod is the half that was missing, and it is what makes the folder on disk match the name you hear.
/// </para>
///
/// <para>
/// Kept separate from the renaming itself so the whole decision — including the awkward parts, collisions and the
/// prefixes that carry meaning — can be tested without a mods folder to rename.
/// </para>
/// </summary>
public static class ModFolderTidy
{
	/// <summary>
	/// Long enough for any real mod name, short enough to leave room for the paths inside the mod. Windows still
	/// has a 260-character limit in most places, and a mod folder is only the beginning of a path that goes on
	/// through meshes, textures and scripts.
	/// </summary>
	private const int MaxFolderNameLength = 90;

	/// <summary>What a mod folder is called now, and the name the manager shows for it.</summary>
	/// <param name="Folder">The folder's name on disk, prefixes and all.</param>
	/// <param name="DisplayName">The mod's real name, from its manifest. Empty when nothing knows it.</param>
	public readonly record struct ModFolder(string Folder, string DisplayName);

	/// <summary>
	/// Decides what to rename, given every mod folder in one game's mods directory.
	/// </summary>
	/// <param name="folders">Every mod folder, so collisions can be seen before any rename happens.</param>
	/// <param name="disabledPrefix">
	/// The character that marks a folder disabled for this game — "." for Stardew and the Bethesda games, "~" for
	/// The Witcher 3. It is carried across a rename untouched: losing it would silently switch a mod back on.
	/// </param>
	/// <param name="isWitcher">
	/// The Witcher 3 loads a folder only when its name begins with <c>mod</c>, and says nothing when it doesn't.
	/// A tidy name that drops that prefix is a mod that stops working, so the prefix is put back and the mod's
	/// display name — which has spaces and reads as prose — is never used as a folder name there.
	/// </param>
	public static (List<FolderTidyRename> Renames, List<FolderTidySkip> Skips) Plan(
		IEnumerable<ModFolder> folders, string disabledPrefix, bool isWitcher)
	{
		var all = folders.ToList();
		var renames = new List<FolderTidyRename>();
		var skips = new List<FolderTidySkip>();

		// Every name that will exist when this is done: the ones not being renamed, plus the ones already
		// claimed by a rename. A tidy name is worthless if it lands on top of another mod.
		var taken = new HashSet<string>(all.Select(f => f.Folder), StringComparer.OrdinalIgnoreCase);

		foreach (ModFolder mod in all)
		{
			string prefix = mod.Folder.StartsWith(disabledPrefix, StringComparison.Ordinal) ? disabledPrefix : "";
			string bare = mod.Folder.Substring(prefix.Length);

			// Nothing to do for a folder that never carried a tail — which is most of them, and all of a
			// Stardew mod's, because those come out of the author's own zip already named properly.
			string tidyBare = ModDisplayName.Clean(bare);
			if (tidyBare.Length == 0 || tidyBare == bare) continue;

			string target = ChooseName(mod, bare, tidyBare, isWitcher);
			if (target.Length == 0)
			{
				skips.Add(new FolderTidySkip(mod.Folder, "there is no readable name to give it"));
				continue;
			}

			string proposed = prefix + target;
			if (string.Equals(proposed, mod.Folder, StringComparison.Ordinal)) continue;

			// A collision is not a reason to give up on the mod: the second "SkyUI" becomes "SkyUI (2)", which is
			// still an improvement on a timestamp, and is what a person would do by hand.
			taken.Remove(mod.Folder);
			proposed = Unique(proposed, taken, isWitcher);
			taken.Add(proposed);

			renames.Add(new FolderTidyRename(mod.Folder, proposed));
		}

		return (renames, skips);
	}

	/// <summary>
	/// The name a tidied folder should carry: the mod's own name where the manager knows it, so the folder in
	/// Explorer reads the same as the row in the list, and the cleaned folder name where it does not.
	/// </summary>
	private static string ChooseName(ModFolder mod, string bare, string tidyBare, bool isWitcher)
	{
		// The Witcher's folder names are an interface to the engine, not prose: "modRandomEncounters" is the name,
		// and the display name is something this manager invented to read it aloud. Cleaning the existing name
		// keeps the shape the engine needs.
		if (isWitcher) return WitcherName(Sanitise(tidyBare));

		string preferred = Sanitise(mod.DisplayName);
		return Truncate(preferred.Length > 0 ? preferred : Sanitise(tidyBare));
	}

	/// <summary>
	/// A folder name Windows will accept: invalid characters dropped, no trailing dots or spaces.
	///
	/// The character set comes from <see cref="WindowsFileName"/> rather than from
	/// <see cref="Path.GetInvalidFileNameChars"/>, which answers for the host rather than for the game.
	/// Off Windows the host's answer is two characters instead of 41, so this would quietly stop
	/// sanitising and hand a Proton-hosted game a folder name it cannot open.
	/// </summary>
	private static string Sanitise(string name) =>
		string.IsNullOrWhiteSpace(name) ? "" : WindowsFileName.ToFolderName(name);

	/// <summary>Keeps a very long mod name from pushing the files inside it past the path limit.</summary>
	private static string Truncate(string name) =>
		name.Length <= MaxFolderNameLength ? name : name.Substring(0, MaxFolderNameLength).TrimEnd('.', ' ');

	/// <summary>The <c>mod</c> prefix the Witcher's engine insists on, put back if cleaning removed it.</summary>
	private static string WitcherName(string name)
	{
		if (name.Length == 0) return "";
		string packed = name.Replace(" ", "");
		return Witcher3Layout.IsModFolderName(packed) ? packed : "mod" + packed;
	}

	/// <summary>
	/// <paramref name="proposed"/>, or the next free variation of it. Numbered "(2)", "(3)" the way a person or
	/// Explorer would — except on The Witcher 3, where a space or bracket in a folder name is asking for trouble
	/// with an engine that only ever wanted <c>mod</c> and letters.
	/// </summary>
	private static string Unique(string proposed, HashSet<string> taken, bool isWitcher)
	{
		if (!taken.Contains(proposed)) return proposed;

		for (int n = 2; n < 1000; n++)
		{
			string candidate = isWitcher ? $"{proposed}{n}" : $"{proposed} ({n})";
			if (!taken.Contains(candidate)) return candidate;
		}
		return proposed;   // a thousand mods of the same name is not a case worth code
	}
}
