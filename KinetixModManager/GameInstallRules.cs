using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// How the recorded copies of a game are looked up and ordered, separated from the settings file they are stored
/// in.
///
/// <para>
/// These three rules decide which copy the manager is talking about and how copies are offered to the user. A
/// mutation campaign changed all of them — the primary copy sorted last instead of first, a single copy reported
/// as several, a lookup by key returning some other copy — and every one passed the whole suite. The consequences
/// are not cosmetic: the install key selects the mods folder, the deployment manifest, the backups and the save
/// backups, so answering with the wrong copy points every one of those at the wrong game folder.
/// </para>
/// </summary>
internal static class GameInstallRules
{
	/// <summary>
	/// The recorded copies of <paramref name="gameId"/>, primary copy first.
	///
	/// A game's first copy keeps the bare game id as its key; later copies carry
	/// <see cref="GameProfiles.InstallKeySeparator"/> and a platform. Bare keys therefore sort ahead of suffixed
	/// ones, and copies within each group sort by key so the order never depends on the order they were found in.
	/// </summary>
	internal static List<GameInstall> Of(IEnumerable<GameInstall> installs, string gameId)
	{
		string id = GameProfiles.BaseId(gameId);
		return installs
			.Where(i => i.GameId == id)
			.OrderBy(i => i.Key.IndexOf(GameProfiles.InstallKeySeparator) >= 0 ? 1 : 0)
			.ThenBy(i => i.Key, StringComparer.Ordinal)
			.ToList();
	}

	/// <summary>
	/// The copy identified by <paramref name="installKey"/>, or <c>null</c> if none is recorded. An absent key is
	/// not a match against the first copy in the list — it is no copy at all.
	/// </summary>
	internal static GameInstall? WithKey(IEnumerable<GameInstall> installs, string? installKey) =>
		string.IsNullOrEmpty(installKey) ? null : installs.FirstOrDefault(i => i.Key == installKey);

	/// <summary>
	/// True when <paramref name="gameId"/> has more than one copy recorded — the one condition under which a
	/// platform is worth saying out loud. With a single copy the games menu, the title bar and every report read
	/// exactly as they did before any of this existed, which is the whole point: one copy, no noise.
	/// </summary>
	internal static bool HasMultipleCopies(IEnumerable<GameInstall> installs, string gameId) =>
		Of(installs, gameId).Count > 1;
}
