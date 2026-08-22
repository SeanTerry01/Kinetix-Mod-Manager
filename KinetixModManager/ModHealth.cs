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

	/// <summary>
	/// Resolves each mod's declared dependencies against the other installed mods, marking whether the required
	/// mod is present, enabled, and new enough.
	///
	/// Matching ignores case and surrounding whitespace, because a UniqueID is hand-typed in two places by two
	/// different authors and they do not always agree: Producer Framework Mod calls itself
	/// "Digus.ProducerFrameworkMod", while the PPJA packs that need it ask for "DIGUS.ProducerFrameworkMod".
	/// SMAPI matches ids case-insensitively and loads them happily, so a case-sensitive comparison here reported
	/// a requirement as "not installed" while the mod sat right there in the list, enabled.
	///
	/// When two copies of the same mod are installed, an enabled one satisfies the dependency in preference to a
	/// disabled one — that is what the game will load, so reporting the disabled copy would be equally untrue.
	/// </summary>
	public static void ResolveDependencies(
		List<GameMod> mods,
		Func<string?, string?, bool> isNewerVersion)
	{
		static string Key(string? id) => (id ?? "").Trim();

		var byId = new Dictionary<string, GameMod>(StringComparer.OrdinalIgnoreCase);
		foreach (GameMod m in mods)
		{
			if (m.IsGroup) continue;
			string key = Key(m.UniqueId);
			if (key.Length == 0) continue;
			if (!byId.TryGetValue(key, out GameMod? existing) || (!existing.IsEnabled && m.IsEnabled))
				byId[key] = m;
		}

		foreach (var mod in mods)
		{
			foreach (var dep in mod.Dependencies)
			{
				string key = Key(dep.UniqueId);
				if (key.Length == 0 || !byId.TryGetValue(key, out GameMod? found)) continue;

				dep.IsPresent  = true;
				dep.IsEnabled  = found.IsEnabled;
				dep.IsNewEnough = isNewerVersion(dep.MinimumVersion, found.Version)
				               || string.Equals(Key(dep.MinimumVersion), Key(found.Version), StringComparison.OrdinalIgnoreCase);
			}
		}
	}
}
