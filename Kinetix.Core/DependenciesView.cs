using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>How badly a dependency is missing, worst first.</summary>
public enum DependencyTrouble
{
	/// <summary>Present, switched on and new enough. Nothing to do.</summary>
	None,

	/// <summary>Not installed at all, and the mod says it needs it.</summary>
	MissingRequired,

	/// <summary>Installed but switched off, and the mod says it needs it.</summary>
	DisabledRequired,

	/// <summary>Installed and switched on, but older than the mod asks for.</summary>
	TooOld,

	/// <summary>Not installed, and the mod says it is optional.</summary>
	MissingOptional,
}

/// <summary>One dependency of one mod, and the sentence that describes it.</summary>
public sealed record DependencyRow(string ModName, string DependencyId, DependencyTrouble Trouble, string? WantedVersion)
{
	/// <summary>
	/// What the reader says for this row.
	///
	/// The trouble comes first, before either name. A user opens this list to find what is broken, and
	/// hearing "Content Patcher" before hearing whether it is a problem means listening to every row to the
	/// end to find out which ones matter.
	/// </summary>
	public string Spoken => Trouble switch
	{
		DependencyTrouble.MissingRequired => Loc.T("deps.rowMissing", ModName, DependencyId),
		DependencyTrouble.DisabledRequired => Loc.T("deps.rowDisabled", ModName, DependencyId),
		DependencyTrouble.TooOld => Loc.T("deps.rowTooOld", ModName, DependencyId, WantedVersion ?? ""),
		DependencyTrouble.MissingOptional => Loc.T("deps.rowOptional", ModName, DependencyId),
		_ => Loc.T("deps.rowFine", ModName, DependencyId),
	};

	public override string ToString() => Spoken;
}

/// <summary>
/// Which installed mods are missing something they need.
///
/// <para>
/// Built from what <see cref="ModHealth.ResolveDependencies"/> has already worked out, so this decides
/// nothing about whether a dependency is satisfied — it decides what is worth showing and in what order,
/// which is the part a window would otherwise invent for itself twice.
/// </para>
///
/// <para>
/// Only trouble is listed. A mod whose dependencies are all present, switched on and new enough produces no
/// rows at all, because a list where the nine hundred things that are fine bury the three that are not is a
/// list nobody can use — least of all by ear.
/// </para>
/// </summary>
public sealed class DependenciesView
{
	private DependenciesView(IReadOnlyList<DependencyRow> rows, int modsChecked)
	{
		Rows = rows;
		ModsChecked = modsChecked;
	}

	public IReadOnlyList<DependencyRow> Rows { get; }

	/// <summary>How many installed mods were looked at, which is what makes "nothing wrong" mean something.</summary>
	public int ModsChecked { get; }

	/// <summary>Rows that would stop a mod working, as opposed to an optional one merely absent.</summary>
	public int SeriousCount =>
		Rows.Count(r => r.Trouble is DependencyTrouble.MissingRequired
			or DependencyTrouble.DisabledRequired
			or DependencyTrouble.TooOld);

	/// <summary>
	/// The troubles among <paramref name="mods"/>, worst first.
	///
	/// Ordered by how much it matters rather than by mod name: a missing required dependency stops a mod
	/// loading, a disabled one is the same problem with an easier fix, an old one usually still runs, and a
	/// missing optional one is information rather than a fault.
	/// </summary>
	public static DependenciesView Of(IEnumerable<GameMod> mods)
	{
		var all = mods.ToList();
		var rows = new List<DependencyRow>();

		foreach (GameMod mod in all)
		{
			foreach (ModDependency dependency in mod.Dependencies ?? new List<ModDependency>())
			{
				DependencyTrouble trouble = Judge(dependency);
				if (trouble == DependencyTrouble.None) continue;

				rows.Add(new DependencyRow(mod.Name, dependency.UniqueId, trouble, dependency.MinimumVersion));
			}
		}

		var ordered = rows
			.OrderBy(r => (int)Severity(r.Trouble))
			.ThenBy(r => r.ModName, StringComparer.CurrentCultureIgnoreCase)
			.ThenBy(r => r.DependencyId, StringComparer.CurrentCultureIgnoreCase)
			.ToList();

		return new DependenciesView(ordered, all.Count);
	}

	/// <summary>What is wrong with one dependency, or nothing.</summary>
	private static DependencyTrouble Judge(ModDependency dependency)
	{
		if (!dependency.IsPresent)
			return dependency.IsRequired ? DependencyTrouble.MissingRequired : DependencyTrouble.MissingOptional;

		// An optional dependency that IS installed is nobody's problem, whatever state it is in — the mod
		// said it could do without it.
		if (!dependency.IsRequired) return DependencyTrouble.None;

		// Switched off before too old, because it is the same outcome with a far easier fix and the user
		// should hear the easy one first.
		if (!dependency.IsEnabled) return DependencyTrouble.DisabledRequired;

		return dependency.IsNewEnough ? DependencyTrouble.None : DependencyTrouble.TooOld;
	}

	private static int Severity(DependencyTrouble trouble) => trouble switch
	{
		DependencyTrouble.MissingRequired => 0,
		DependencyTrouble.DisabledRequired => 1,
		DependencyTrouble.TooOld => 2,
		DependencyTrouble.MissingOptional => 3,
		_ => 4,
	};

	/// <summary>
	/// The sentence to say when the list appears.
	///
	/// "Nothing is missing" says how many mods that covers, because the same words over a folder that was
	/// never read would be a lie of omission — the same reason the updates list says how many it checked.
	/// </summary>
	public string Announcement
	{
		get
		{
			if (Rows.Count == 0) return Loc.T("deps.allFine", ModsChecked);

			int serious = SeriousCount;

			return serious == 0
				? Loc.T("deps.optionalOnly", Rows.Count)
				: Loc.T("deps.trouble", serious);
		}
	}
}
