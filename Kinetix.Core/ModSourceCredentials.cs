using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// One row of the mod-source keys screen: a site, and whether the user has set it up.
/// </summary>
public sealed record ModSourceKeyRow(ModSourceInfo Source, bool HasKey)
{
	/// <summary>
	/// What the row says when the user lands on it.
	///
	/// <para>
	/// It never contains the key. The list is arrowed through, so every row is spoken in passing — and a
	/// credential read aloud on the way past is read aloud in whatever room the user happens to be in. What
	/// the row carries is whether one is stored, which is the fact the user came to the screen for; the key
	/// itself is shown only when they ask for that one row.
	/// </para>
	/// </summary>
	public string Describe() =>
		Loc.T(HasKey ? "sourcekeys.rowSaved" : "sourcekeys.rowMissing", Source.DisplayName);

	public override string ToString() => Describe();
}

/// <summary>
/// The sites that want a key before they will answer, and what the user has given them.
///
/// <para>
/// A screen of its own because it is not about any one game. A key belongs to the user's account with a site;
/// the game they happen to have open at the time has nothing to do with it, and burying each key in the
/// settings of the games that use it would mean a user who plays Minecraft could not find the Nexus key they
/// set up last year.
/// </para>
///
/// <para>
/// The stored thing is always a key. How you come by one differs — CurseForge issues one to an approved
/// application and you paste it in; Nexus will also let you sign in on its own site and hand the manager a key
/// at the end, which is better because the manager never sees a password. That is what
/// <see cref="ModSourceCredential"/> distinguishes, and it is the answer to "what about sites with a login
/// rather than a key": they still end in a key, they just get you one a different way.
/// </para>
/// </summary>
public static class ModSourceCredentials
{
	/// <summary>
	/// Every site that wants setting up, whether or not it is set up and whether or not it carries the game
	/// that is loaded. See <see cref="ModSources.NeedingCredentials"/> for why the game does not come into it.
	/// </summary>
	public static IReadOnlyList<ModSourceKeyRow> Rows(Func<string, string?> keyFor) =>
		ModSources.NeedingCredentials()
			.Select(s => new ModSourceKeyRow(s, !string.IsNullOrWhiteSpace(keyFor(s.Id))))
			.ToList();

	/// <summary>How many of them are still waiting for one, for the line at the top of the screen.</summary>
	public static int Missing(IReadOnlyList<ModSourceKeyRow> rows) => rows.Count(r => !r.HasKey);

	/// <summary>
	/// A sentence for the top of the screen: how many sites want a key and how many still have none.
	/// </summary>
	public static string Summarise(IReadOnlyList<ModSourceKeyRow> rows)
	{
		int missing = Missing(rows);

		return missing == 0
			? Loc.T("sourcekeys.hintAllSet", rows.Count)
			: Loc.T("sourcekeys.hintSomeMissing", rows.Count, missing);
	}

	/// <summary>
	/// Whether <paramref name="key"/> is worth storing, and a reason when it is not.
	///
	/// Deliberately shallow: the only thing checked is that it is not obviously nothing. A key's real test is
	/// whether the site accepts it, and a pattern guessed at here would one day reject a perfectly good key
	/// that the site had started issuing in a new shape — with the user having no way to argue.
	/// </summary>
	public static string? WhyNotUsable(string? key)
	{
		string trimmed = (key ?? "").Trim();

		if (trimmed.Length == 0) return Loc.T("sourcekeys.rejectEmpty");
		if (trimmed.Any(char.IsWhiteSpace)) return Loc.T("sourcekeys.rejectSpaces");

		return null;
	}
}
