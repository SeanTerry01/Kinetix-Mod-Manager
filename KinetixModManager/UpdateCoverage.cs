using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>How a mod's updates are (or aren't) covered by the update check.</summary>
public enum UpdateCoverageKind
{
	/// <summary>Has its own Nexus ID or GitHub repo, so it is checked directly.</summary>
	Linked,
	/// <summary>No link of its own, but SMAPI's mod database knows it by UniqueID and version-checks it.</summary>
	SmapiDatabase,
	/// <summary>No link of its own; arrives inside another, linked mod's download, so that mod's update covers it.</summary>
	Bundled,
	/// <summary>Ships with SMAPI itself (Console Commands, Save Backup, Error Handler) and updates with it.</summary>
	PartOfSmapi,
	/// <summary>
	/// Identified by the file itself, so no link is needed — Minecraft, where Modrinth recognises a mod by the
	/// SHA-1 of its jar.
	///
	/// A separate kind rather than folding it into <see cref="Linked"/>, because it is a genuinely better
	/// position to be in and worth saying so: a link can be wrong, out of date, or point at the wrong mod
	/// page, and a hash cannot. Reported as unchecked it was exactly backwards — Fabric API was the one mod in
	/// the folder that could be identified with certainty.
	/// </summary>
	ByFileHash,
	/// <summary>Nothing knows where this mod came from — the only kind the user needs to act on.</summary>
	Unchecked
}

/// <summary>Why a mod couldn't be linked to an update source — what the user has to act on.</summary>
public enum UncheckedReason
{
	None,
	/// <summary>The manifest declares no update keys at all.</summary>
	NoUpdateKey,
	/// <summary>The manifest declares update keys, but they're blank or malformed (an author's typo).</summary>
	BlankUpdateKey
}

/// <summary>One mod's classification, with the mod that covers it when it doesn't carry its own link.</summary>
public sealed class UpdateCoverageEntry
{
	public GameMod Mod { get; init; } = null!;
	public UpdateCoverageKind Kind { get; init; }
	/// <summary>For <see cref="UpdateCoverageKind.Bundled"/>, the linked mod whose download carries this one.</summary>
	public GameMod? Parent { get; init; }
	/// <summary>For <see cref="UpdateCoverageKind.Unchecked"/>, why the mod has no update source.</summary>
	public UncheckedReason Reason { get; init; }
}

/// <summary>
/// Works out which installed mods the update check can actually cover.
///
/// "N mods have no Nexus or GitHub link" was misleading in both directions. Most of those mods are not stray
/// at all: a single Nexus download often unpacks into several mods — a main mod plus the content packs
/// bundled with it — and only one of them carries the update key. Updating the one that does reinstalls the
/// whole download, so the others are covered even though nothing links them individually. Counting them as
/// unchecked made a healthy setup sound broken, and buried the genuinely unchecked mods in the same number.
///
/// Self-contained (BCL plus <see cref="GameMod"/>) so the rules can be unit tested.
/// </summary>
public static class UpdateCoverage
{
	public static bool HasUpdateLink(GameMod mod) =>
		!string.IsNullOrEmpty(mod.NexusID) || !string.IsNullOrEmpty(mod.GitHubRepo);

	/// <summary>
	/// The mods SMAPI installs alongside itself. They live in the Mods folder like any other mod but are not
	/// downloaded from anywhere — updating SMAPI updates them — so reporting them as "no Nexus link" is noise.
	/// </summary>
	private static readonly HashSet<string> SmapiBundledModIds = new(StringComparer.OrdinalIgnoreCase)
	{
		"SMAPI.ConsoleCommands", "SMAPI.SaveBackup", "SMAPI.ErrorHandler"
	};

	public static bool IsSmapiBundledMod(GameMod mod) =>
		!string.IsNullOrEmpty(mod.UniqueId) && SmapiBundledModIds.Contains(mod.UniqueId);

	/// <summary>
	/// Classifies each mod in <paramref name="mods"/> (group rows are ignored). <paramref name="smapiKnownIds"/>
	/// is the set of UniqueIDs SMAPI's mod database recognises, which version-checks those mods whether or not
	/// their manifest carries an update key.
	/// </summary>
	/// <param name="bundledWith">
	/// Child UniqueID to parent UniqueID for mods the user has told the manager arrive with another mod. Used
	/// for the case nothing on disk reveals: a mod from the same mod page as another (an optional file, say)
	/// that unpacked into an unrelated folder of its own.
	/// </param>
	/// <param name="checkedByFileHash">
	/// True for a game whose catalogue identifies a mod by the hash of its file rather than by a stored link —
	/// Minecraft, on Modrinth. Every mod is then checkable without carrying an id, and reporting them as
	/// unlinked tells the user to go and fix something that is not broken.
	/// </param>
	public static List<UpdateCoverageEntry> Classify(
		IEnumerable<GameMod> mods, string modsPath, ISet<string>? smapiKnownIds = null,
		IReadOnlyDictionary<string, string>? bundledWith = null, bool checkedByFileHash = false)
	{
		var all = mods.Where(m => !m.IsGroup).ToList();
		var linked = all.Where(HasUpdateLink).ToList();
		var result = new List<UpdateCoverageEntry>();

		foreach (GameMod mod in all)
		{
			if (IsSmapiBundledMod(mod))
			{
				result.Add(new UpdateCoverageEntry { Mod = mod, Kind = UpdateCoverageKind.PartOfSmapi });
				continue;
			}
			if (HasUpdateLink(mod))
			{
				result.Add(new UpdateCoverageEntry { Mod = mod, Kind = UpdateCoverageKind.Linked });
				continue;
			}
			// Checked before the bundle guessing below: where the catalogue knows a mod by its file, there is
			// nothing left to infer from the folder layout.
			if (checkedByFileHash)
			{
				result.Add(new UpdateCoverageEntry { Mod = mod, Kind = UpdateCoverageKind.ByFileHash });
				continue;
			}
			if (!string.IsNullOrEmpty(mod.UniqueId) && smapiKnownIds != null && smapiKnownIds.Contains(mod.UniqueId))
			{
				result.Add(new UpdateCoverageEntry { Mod = mod, Kind = UpdateCoverageKind.SmapiDatabase });
				continue;
			}
			// An association the user set by hand wins over anything inferred from the folder layout: they know
			// which mod it came with, and by definition the layout doesn't show it.
			GameMod? parent = FindDeclaredParent(mod, linked, bundledWith)
				?? FindBundleParent(mod, linked, modsPath);
			result.Add(new UpdateCoverageEntry
			{
				Mod = mod,
				Kind = parent != null ? UpdateCoverageKind.Bundled : UpdateCoverageKind.Unchecked,
				Parent = parent,
				Reason = parent != null ? UncheckedReason.None
					: mod.HasBlankUpdateKey ? UncheckedReason.BlankUpdateKey : UncheckedReason.NoUpdateKey
			});
		}
		return result;
	}

	/// <summary>
	/// Picks the one mod that best stands for a whole download — the one to name in the Updates list and in the
	/// "which mod does this come with?" picker. Naming it badly makes the download unrecognisable: the Cape
	/// Stardew download installs five mods, and picking alphabetically labelled it "AnnettesRetreat [Farm Type
	/// Manager component]", which no user would connect to Cape Stardew.
	///
	/// So: prefer a mod at the top of the mods folder over a content pack nested inside one, then the mod that
	/// gives its folder its name (exactly, then by containment either way — "Cape Stardew" for the folder "Cape
	/// Stardew 1.6"), and only then fall back to alphabetical order.
	/// </summary>
	public static GameMod PickRepresentative(IEnumerable<GameMod> group, string modsPath)
	{
		var mods = group.ToList();
		int minDepth = mods.Min(m => FolderDepth(m, modsPath));
		var shallowest = mods.Where(m => FolderDepth(m, modsPath) == minDepth).ToList();

		return shallowest
			.OrderByDescending(m => FolderNameAffinity(m, modsPath))
			.ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
			.First();
	}

	private static int FolderDepth(GameMod mod, string modsPath)
	{
		string relative = RelativeFolder(mod, modsPath);
		return relative.Length == 0 ? int.MaxValue : relative.Count(c => c == Path.DirectorySeparatorChar);
	}

	/// <summary>How well a mod's name matches the folder it sits in: 2 exact, 1 one contains the other, 0 neither.</summary>
	private static int FolderNameAffinity(GameMod mod, string modsPath)
	{
		string folder = ModNameMatch.Normalize(TopLevelFolder(mod, modsPath));
		string name = ModNameMatch.Normalize(mod.Name);
		if (folder.Length == 0 || name.Length == 0) return 0;
		if (folder == name) return 2;
		return folder.Contains(name) || name.Contains(folder) ? 1 : 0;
	}

	/// <summary>
	/// The linked mod the user has said <paramref name="mod"/> arrives with, or null when no association is
	/// recorded, the named parent isn't installed any more, or it has no update link of its own (an association
	/// to a mod that can't itself be checked would cover nothing).
	/// </summary>
	public static GameMod? FindDeclaredParent(
		GameMod mod, List<GameMod> linked, IReadOnlyDictionary<string, string>? bundledWith)
	{
		if (bundledWith == null || string.IsNullOrEmpty(mod.UniqueId)) return null;
		if (!bundledWith.TryGetValue(mod.UniqueId, out string? parentId) || string.IsNullOrEmpty(parentId)) return null;
		return linked.FirstOrDefault(m => m.UniqueId.Equals(parentId, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Finds the linked mod whose download also delivered <paramref name="mod"/>, or null if there isn't one.
	/// Two shapes count: a mod sitting <em>inside</em> a linked mod's folder (a content pack shipped within the
	/// mod it extends), and a mod sharing its top-level folder under Mods with a linked mod (a download that
	/// unpacks several mods side by side into one wrapper folder). In both cases installing the linked mod's
	/// archive replaces this mod too, so it is covered.
	/// </summary>
	public static GameMod? FindBundleParent(GameMod mod, List<GameMod> linked, string modsPath)
	{
		string modPath = SafeFullPath(mod.FolderPath);
		if (modPath.Length == 0) return null;
		char sep = Path.DirectorySeparatorChar;

		foreach (GameMod candidate in linked)
		{
			string candidatePath = SafeFullPath(candidate.FolderPath);
			if (candidatePath.Length == 0) continue;
			if (modPath.StartsWith(candidatePath.TrimEnd(sep) + sep, StringComparison.OrdinalIgnoreCase))
				return candidate;
		}

		string group = TopLevelFolder(mod, modsPath);
		if (group.Length == 0) return null;
		return linked.FirstOrDefault(c => TopLevelFolder(c, modsPath).Equals(group, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>The mod's own folder directly under the Mods directory — the folder one download unpacks into.</summary>
	public static string TopLevelFolder(GameMod mod, string modsPath)
	{
		string relative = RelativeFolder(mod, modsPath);
		if (relative.Length == 0) return "";
		int sep = relative.IndexOf(Path.DirectorySeparatorChar);
		return sep >= 0 ? relative.Substring(0, sep) : relative;
	}

	/// <summary>
	/// The mod's folder relative to the Mods directory — how the user identifies it on disk. Returns "" when
	/// the mod lives outside that directory entirely, so callers can fall back to the absolute path.
	/// </summary>
	public static string RelativeFolder(GameMod mod, string modsPath)
	{
		try
		{
			if (string.IsNullOrEmpty(modsPath) || string.IsNullOrEmpty(mod.FolderPath)) return "";
			string relative = Path.GetRelativePath(modsPath, mod.FolderPath);
			return relative.StartsWith("..") || Path.IsPathRooted(relative) ? "" : relative;
		}
		catch { return ""; }
	}

	private static string SafeFullPath(string path)
	{
		try { return string.IsNullOrEmpty(path) ? "" : Path.GetFullPath(path); }
		catch { return ""; }
	}
}
