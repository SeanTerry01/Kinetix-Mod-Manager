using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// Which installed mods depend on which others — the question asked before deleting something.
///
/// <para>
/// Answering it wrongly is expensive in a way the user finds out about later and elsewhere. Removing a mod
/// that three others quietly require does not fail at the time: it fails on the next launch, as a game that
/// will not start or a feature that has gone, with nothing connecting the two events. That is bad for
/// anyone and worse for someone who cannot read a crash log.
/// </para>
///
/// <para>
/// The two games disagree about what a dependency even is, and both answers are read here. Stardew mods
/// declare theirs by <c>UniqueID</c> in a manifest, so the answer is exact. Skyrim and Fallout 4 declare
/// nothing of the kind — a plugin lists its <em>masters</em> in its own header, so a dependency is inferred
/// by asking every other installed plugin whether it names one of this mod's files. Offline and
/// best-effort, and deliberately so: it never touches the network, which is what makes it safe to run
/// inside a delete confirmation the user is waiting on.
/// </para>
/// </summary>
public static class ModDependencies
{
	/// <summary>
	/// The plugin files (<c>.esp</c>/<c>.esm</c>/<c>.esl</c>) a mod ships. I/O errors are swallowed: a mod
	/// folder that cannot be walked is a mod with no known plugins, not a reason to abandon the question.
	/// </summary>
	public static IReadOnlyList<string> PluginFiles(GameMod mod)
	{
		if (mod == null || string.IsNullOrEmpty(mod.FolderPath) || !Directory.Exists(mod.FolderPath))
			return Array.Empty<string>();

		try
		{
			return Directory.EnumerateFiles(mod.FolderPath, "*", SearchOption.AllDirectories)
				.Where(f => BethesdaPlugins.IsPluginFile(Path.GetFileName(f)))
				.ToList();
		}
		catch
		{
			return Array.Empty<string>();
		}
	}

	/// <summary>One mod that depends on the one being asked about, and why.</summary>
	public readonly record struct Dependent(GameMod Mod, string ViaPlugin, string Master)
	{
		/// <summary>True for a Stardew-style match, where the manifest names the dependency outright.</summary>
		public bool IsDeclared => string.IsNullOrEmpty(ViaPlugin);
	}

	/// <summary>
	/// Every installed mod that requires <paramref name="target"/>.
	///
	/// Returns the facts rather than sentences. The wording belongs to whatever is doing the speaking, and
	/// there are two front ends now — it was previously built as display strings inside the window, which
	/// meant the second one could not ask the question at all without also inheriting the first one's phrasing.
	/// </summary>
	public static IReadOnlyList<Dependent> Dependents(
		GameMod target, IEnumerable<GameMod> installed, string activeGame)
	{
		var found = new List<Dependent>();
		if (target == null || installed == null) return found;

		var others = installed
			.Where(m => m != null && !m.IsGroup && !ReferenceEquals(m, target))
			.ToList();

		if (GameProfiles.IsGame(activeGame, GameProfiles.StardewValley))
		{
			if (string.IsNullOrEmpty(target.UniqueId)) return found;

			foreach (GameMod m in others)
				if (m.Dependencies.Any(d => d.IsRequired &&
						string.Equals(d.UniqueId, target.UniqueId, StringComparison.OrdinalIgnoreCase)))
					found.Add(new Dependent(m, "", ""));

			return found;
		}

		if (GameProfiles.Find(activeGame)?.IsBethesda != true) return found;

		var targetPlugins = new HashSet<string>(
			PluginFiles(target).Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
		if (targetPlugins.Count == 0) return found;

		foreach (GameMod m in others)
		{
			foreach (string plugin in PluginFiles(m))
			{
				string? master = BethesdaPlugins.ReadPluginMasters(plugin)
					.FirstOrDefault(targetPlugins.Contains);

				if (master == null) continue;

				// One line per dependent mod is enough. A mod with eight plugins all pointing at the same
				// target would otherwise read out eight near-identical sentences.
				found.Add(new Dependent(m, Path.GetFileName(plugin), master));
				break;
			}
		}

		return found;
	}
}
