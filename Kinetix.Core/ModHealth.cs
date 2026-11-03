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
	/// Whether these two entries are the same mod — the installed copy and what an update left in its place.
	///
	/// <para>
	/// Matched on any of the three identities a mod can have: its Nexus page, its GitHub repository, or its own id.
	/// Leaving the third one out is what let an updated Minecraft mod come back switched ON after the user had
	/// deliberately switched it off: a Minecraft mod has neither of the first two, so nothing ever matched and the
	/// code that puts the mod back as it was quietly did nothing.
	/// </para>
	///
	/// <para>
	/// An empty identity never matches, or every mod without a Nexus id would be "the same mod" as every other.
	/// </para>
	/// </summary>
	public static bool IsSameMod(GameMod a, GameMod b)
	{
		static bool Same(string? x, string? y) =>
			!string.IsNullOrWhiteSpace(x) && !string.IsNullOrWhiteSpace(y) &&
			x.Trim().Equals(y.Trim(), StringComparison.OrdinalIgnoreCase);

		return Same(a.NexusID, b.NexusID) || Same(a.GitHubRepo, b.GitHubRepo) || Same(a.UniqueId, b.UniqueId);
	}

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
