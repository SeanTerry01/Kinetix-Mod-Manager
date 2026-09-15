using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>One saved profile, and the sentence that describes it.</summary>
public sealed record ProfileRow(string Name, int ModCount, int EnabledCount, bool IsCurrent)
{
	/// <summary>
	/// What the reader says for this row.
	///
	/// Whether this is the setup you are already on comes first, because it is the one fact that decides
	/// whether the row is worth any further thought — the same reasoning that puts "Installed" at the front
	/// of a search result.
	/// </summary>
	public string Spoken => IsCurrent
		? Loc.T("profilesview.rowCurrent", Name, ModCount, EnabledCount)
		: Loc.T("profilesview.row", Name, ModCount, EnabledCount);

	public override string ToString() => Spoken;
}

/// <summary>
/// The saved profiles as a front end shows them, and what applying one would actually do.
///
/// <para>
/// The second half is the point. Switching profiles moves mods in and out of a game the user is about to
/// play, and a list that only says "Mage" and "Thief" asks them to remember which is which. Counting the
/// changes first — and saying so before anything moves — is what makes that an informed choice rather than a
/// guess, and it is the same reasoning as the confirmation the Windows head shows.
/// </para>
/// </summary>
public sealed class ProfilesView
{
	private ProfilesView(IReadOnlyList<ProfileRow> rows, IReadOnlyList<ModProfile> profiles)
	{
		Rows = rows;
		Profiles = profiles;
	}

	public IReadOnlyList<ProfileRow> Rows { get; }

	/// <summary>The profiles themselves, in the same order as <see cref="Rows"/>.</summary>
	public IReadOnlyList<ModProfile> Profiles { get; }

	/// <summary>
	/// Builds the list, marking whichever profile the mods on disk already match.
	///
	/// "Current" means the switched-on mods are exactly what this profile records — not that the user
	/// selected it last. A profile applied and then departed from by hand is no longer the one you are on,
	/// and saying it is would be the manager's bookkeeping contradicting the user's own folder.
	/// </summary>
	public static ProfilesView Of(IEnumerable<ModProfile> profiles, IReadOnlyList<GameMod> installed)
	{
		var ordered = profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

		var rows = ordered
			.Select(p => new ProfileRow(
				p.Name,
				p.ModStates.Count,
				p.ModStates.Count(state => state.Value),
				ProfileStore.Changes(p, installed).Count == 0))
			.ToList();

		return new ProfilesView(rows, ordered);
	}

	/// <summary>The sentence to say when the list appears.</summary>
	public string Announcement =>
		Rows.Count == 0
			? Loc.T("profilesview.none")
			: Loc.T("profilesview.count", Rows.Count);

	/// <summary>
	/// What applying <paramref name="profile"/> would change, as a sentence to confirm before anything moves.
	///
	/// Nothing to do is said differently from a change, because they mean different things to somebody about
	/// to play: one is "you are already set up", the other is "twelve mods are about to move".
	/// </summary>
	public static string DescribeApplying(ModProfile profile, IReadOnlyList<GameMod> installed)
	{
		IReadOnlyList<ProfileStore.StateChange> changes = ProfileStore.Changes(profile, installed);
		if (changes.Count == 0) return Loc.T("profilesview.applyNothing", profile.Name);

		int on = changes.Count(c => c.Enable);
		int off = changes.Count - on;

		if (off == 0) return Loc.T("profilesview.applyOnOnly", profile.Name, on);
		if (on == 0) return Loc.T("profilesview.applyOffOnly", profile.Name, off);

		return Loc.T("profilesview.applyBoth", profile.Name, on, off);
	}
}
