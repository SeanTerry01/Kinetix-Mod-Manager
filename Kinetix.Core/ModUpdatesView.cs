using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>Why an updates list looks the way it does.</summary>
public enum ModUpdatesStatus
{
	/// <summary>The catalogue answered. There may still be nothing to update.</summary>
	Checked,

	/// <summary>Nothing here can be checked — see <see cref="ModUpdatesView.Reason"/>.</summary>
	CannotCheck,
}

/// <summary>One mod with a newer version waiting, and the sentence that describes it.</summary>
public sealed record ModUpdateRow(string Path, string Name, string Installed, string Latest)
{
	/// <summary>
	/// What the reader says for this row.
	///
	/// The two versions are the whole of the decision — whether this update is worth taking now — so they
	/// come immediately after the name rather than behind anything else.
	/// </summary>
	public string Spoken => Loc.T("updatesview.row", Name, Installed, Latest);

	public override string ToString() => Spoken;
}

/// <summary>
/// The updates list as a front end shows it.
///
/// <para>
/// Here rather than in a window for the same reason <see cref="InstalledModsView"/> is: what a row says and
/// what is announced when the list appears are decisions, and two front ends deciding them separately is how
/// they drift. It also keeps the awkward case honest — "nothing to update" and "nothing here could be
/// checked" are completely different answers that look identical as an empty list, and only one of them is
/// good news.
/// </para>
/// </summary>
public sealed class ModUpdatesView
{
	private ModUpdatesView(
		ModUpdatesStatus status, GameProfile? game, IReadOnlyList<ModUpdateRow> rows, int checkedCount, string reason)
	{
		Status = status;
		Game = game;
		Rows = rows;
		CheckedCount = checkedCount;
		Reason = reason;
	}

	public ModUpdatesStatus Status { get; }

	public GameProfile? Game { get; }

	public IReadOnlyList<ModUpdateRow> Rows { get; }

	/// <summary>How many mods were actually asked about, which is not always how many are installed.</summary>
	public int CheckedCount { get; }

	/// <summary>Why nothing could be checked, when that is the answer. Empty otherwise.</summary>
	public string Reason { get; }

	/// <summary>Nothing here can be checked, and this is the sentence saying why.</summary>
	public static ModUpdatesView CannotCheck(GameProfile? game, string reason) =>
		new(ModUpdatesStatus.CannotCheck, game, Array.Empty<ModUpdateRow>(), 0, reason);

	/// <summary>
	/// What a check found: the installed mods whose catalogue reports a newer version.
	///
	/// A mod the catalogue said nothing about is not an update and not a failure — it is simply a mod that
	/// catalogue does not carry, which is ordinary for a hand-built mod or one from another site.
	/// </summary>
	public static ModUpdatesView Of(
		GameProfile? game, IEnumerable<GameMod> installed, IReadOnlyDictionary<string, string> latestByPath)
	{
		var rows = new List<ModUpdateRow>();
		int asked = 0;

		foreach (GameMod mod in installed)
		{
			asked++;
			if (!latestByPath.TryGetValue(mod.FolderPath, out string? latest)) continue;
			if (string.IsNullOrWhiteSpace(latest)) continue;

			// The same comparison the Windows head makes, so the two builds never disagree about whether
			// something is an update — a version that merely differs is not necessarily newer.
			if (!ModVersions.IsNewer(mod.Version, latest)) continue;

			rows.Add(new ModUpdateRow(mod.FolderPath, mod.Name, mod.Version, latest));
		}

		return new ModUpdatesView(ModUpdatesStatus.Checked, game, rows, asked, "");
	}

	/// <summary>
	/// The sentence to say when the list appears.
	///
	/// "Everything is up to date" and "nothing here could be checked" are told apart deliberately: both are
	/// an empty list, and only one of them means the user has nothing to do.
	/// </summary>
	public string Announcement
	{
		get
		{
			string name = Game?.DisplayName ?? "";

			if (Status == ModUpdatesStatus.CannotCheck) return Reason;

			return Rows.Count == 0
				? Loc.T("updatesview.upToDate", name, CheckedCount)
				: Loc.T("updatesview.available", name, Rows.Count);
		}
	}
}
