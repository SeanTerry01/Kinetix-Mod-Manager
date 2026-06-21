using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// Self-contained mod-health checks that depend only on <see cref="GameMod"/> metadata (no filesystem or
/// game-specific I/O), so they can be unit-tested without pulling in the rest of the app. Heavier checks that
/// read plugin headers or scan folders live in <see cref="ModFileSystem"/>.
/// </summary>
public static class ModHealth
{
	/// <summary>
	/// Stardew's accessible "file conflict" equivalent: SMAPI mods live in isolated folders so they never
	/// overwrite each other's files, but two mods declaring the same <c>UniqueID</c> in their manifest will
	/// break SMAPI loading. Returns each duplicated id with the names of the mods that share it.
	/// </summary>
	public static List<(string UniqueId, List<string> ModNames)> FindDuplicateUniqueIds(IEnumerable<GameMod> mods)
	{
		return mods
			.Where(m => !m.IsGroup && !string.IsNullOrWhiteSpace(m.UniqueId))
			.GroupBy(m => m.UniqueId, StringComparer.OrdinalIgnoreCase)
			.Where(g => g.Count() > 1)
			.Select(g => (g.Key, g.Select(m => m.Name).ToList()))
			.ToList();
	}
}
