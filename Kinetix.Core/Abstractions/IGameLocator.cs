using System;
using System.Collections.Generic;

namespace KinetixModManager;

/// <summary>
/// Finding where a game is installed, and where it keeps the player's own files.
///
/// <para>
/// Two questions rather than one, because on Linux they have different answers. Under Proton the game's
/// files still sit in <c>steamapps/common/&lt;Game&gt;</c> exactly as a native install would — Proton does
/// not move them. What lives inside the compatibility prefix is the <em>Windows user profile</em>: the
/// <c>Documents\My Games</c> folder holding Skyrim's INIs and saves, and <c>%LOCALAPPDATA%</c>. So mods,
/// which live under the game folder, are found the same way on every platform, while saves and INIs are
/// not.
/// </para>
///
/// <para>
/// Getting that round the wrong way is easy and expensive: it suggests Proton support means re-teaching
/// the manager where mods are, when in fact the mod paths are the part that already works.
/// </para>
/// </summary>
public interface IGameLocator
{
	/// <summary>
	/// Where the game itself is installed, or <c>null</c> if it cannot be found. Mods are located relative
	/// to this for every layout except Minecraft, which keeps them beside the launcher instead.
	/// </summary>
	string? InstallFolder(GameProfile game);

	/// <summary>
	/// The folder standing in for the player's Windows <c>Documents</c> — the parent of <c>My Games</c>.
	///
	/// On Windows that is the real Documents folder. Off it, for a game running under a compatibility
	/// layer, it is inside that game's prefix, which is why this is asked per game rather than once.
	/// <c>null</c> when the game keeps nothing there, or when no prefix has been created yet because the
	/// game has never been launched.
	/// </summary>
	string? UserDocumentsFolder(GameProfile game);

	/// <summary>Every library root worth searching, for callers that want to look for themselves.</summary>
	IReadOnlyList<string> LibraryRoots();
}
