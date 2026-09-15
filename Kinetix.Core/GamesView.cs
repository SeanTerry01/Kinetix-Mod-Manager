using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>One game in the games list, and the sentence that describes it.</summary>
public sealed record GameRow(GameProfile Game, string? InstallFolder, bool WillNotSpeak)
{
	/// <summary>True when the game was found on this machine.</summary>
	public bool IsInstalled => !string.IsNullOrEmpty(InstallFolder);

	/// <summary>
	/// What the reader says for this row.
	///
	/// <para>
	/// The silence warning goes <em>here</em>, on the row, rather than only on a settings screen. Settings is
	/// somewhere a user visits once; this list is where they choose what to spend an evening on. Being told
	/// afterwards that the game was never going to talk is being told too late.
	/// </para>
	/// </summary>
	public string Spoken
	{
		get
		{
			string body = IsInstalled
				? Loc.T("games.installedAt", Game.DisplayName, InstallFolder!)
				: Loc.T("games.notInstalled", Game.DisplayName);

			return WillNotSpeak ? body + " " + Loc.T("games.willNotSpeak") : body;
		}
	}

	public override string ToString() => Spoken;
}

/// <summary>
/// The games list as a front end shows it.
///
/// <para>
/// Every supported game is listed whether or not it is installed, and an uninstalled one says so. A user who
/// cannot see an empty list has no way to tell "this game is not supported" from "I have not installed it
/// yet" — and the first is a reason to give up on the program while the second is not.
/// </para>
/// </summary>
public static class GamesView
{
	/// <summary>
	/// Builds the rows.
	/// </summary>
	/// <param name="locate">Where each game is installed, or null. Passed in so this stays testable.</param>
	/// <param name="warnWhereItWillNotSpeak">
	/// True on a platform where a game's accessibility mod may not work — which today means anything that is
	/// not Windows. On Windows every supported game speaks, so the note would be noise on all six rows.
	/// </param>
	public static IReadOnlyList<GameRow> Of(
		IEnumerable<GameProfile> games, Func<GameProfile, string?> locate, bool warnWhereItWillNotSpeak) =>
		games
			.Select(g => new GameRow(
				g,
				Safely(locate, g),
				warnWhereItWillNotSpeak && !g.AccessModSpeaksOnLinux))
			.ToList();

	/// <summary>
	/// The summary for the top of the list: how many games are here, and how many were found.
	/// </summary>
	public static string Summarise(IReadOnlyList<GameRow> rows)
	{
		int installed = rows.Count(r => r.IsInstalled);

		return installed == 0
			? Loc.T("games.noneFound", rows.Count)
			: Loc.T("games.someFound", rows.Count, installed);
	}

	/// <summary>
	/// Looking for a game touches the disk, and a library on a disconnected drive throws rather than
	/// answering. One game that cannot be looked for must not cost the whole list.
	/// </summary>
	private static string? Safely(Func<GameProfile, string?> locate, GameProfile game)
	{
		try { return locate(game); }
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Detect", $"looking for {game.DisplayName}", ex);
			return null;
		}
	}
}
