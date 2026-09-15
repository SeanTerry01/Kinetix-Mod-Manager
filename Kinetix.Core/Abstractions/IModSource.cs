using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KinetixModManager;

/// <summary>What to look for, and everything a catalogue might need in order to answer.</summary>
public sealed record ModSearchQuery(string GameId, string Term, int Page, int PageSize)
{
	/// <summary>Nexus's notion of what the term matches — by name, by author, and so on.</summary>
	public string SearchType { get; init; } = "name";

	/// <summary>Nexus's language filter, which no other catalogue has an equivalent for.</summary>
	public string? Language { get; init; }

	/// <summary>Nexus's category filter. Modrinth's categories are a different vocabulary entirely.</summary>
	public string? Category { get; init; }

	/// <summary>
	/// The Minecraft version to filter to. There is no sensible default: a mod built for another version
	/// installs perfectly and then loads nothing.
	/// </summary>
	public string? GameVersion { get; init; }
}

/// <summary>What one search came back with.</summary>
public sealed record ModSearchResults(IReadOnlyList<GameMod> Results, int Total)
{
	public static readonly ModSearchResults Empty = new(Array.Empty<GameMod>(), 0);

	/// <summary>
	/// Plain sentences about sources that could not answer — a missing key, a site that was unreachable.
	///
	/// Carried with the results rather than thrown, because one catalogue being unavailable is not a failed
	/// search: the user still wants the mods the others found, and still needs telling that a source they
	/// asked for was not among them. Silence would read as "there is nothing there".
	/// </summary>
	public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// A catalogue the manager can search.
///
/// <para>
/// Deliberately narrow. Nexus alone has thirty-odd public methods — endorsements, rate limits, NXM links,
/// tracked mods, changelogs — and not one of them belongs in an interface that exists so a user can choose
/// where to search. Those stay on <c>NexusService</c> and are reached when the source in hand is Nexus. What
/// every catalogue must be able to do is answer "what mods match this", and that is what this asks.
/// </para>
/// </summary>
public interface IModSource
{
	/// <summary>Which site this is: its name, what it can do, and which games it carries.</summary>
	ModSourceInfo Info { get; }

	/// <summary>
	/// Whether it can be used right now, and a sentence for the user if not — a missing API key, most often.
	/// Asked before a search so the reason can be shown beside the source's name rather than after the user
	/// has chosen it and got nothing.
	/// </summary>
	ModSourceStatus Status { get; }

	/// <summary>True when this catalogue carries the game and can be searched for it.</summary>
	bool CanSearch(string? gameId);

	/// <summary>
	/// Whatever matches. Returns <see cref="ModSearchResults.Empty"/> with a note rather than throwing when
	/// the catalogue cannot answer — see <see cref="ModSearchResults.Notes"/>.
	/// </summary>
	Task<ModSearchResults> SearchAsync(ModSearchQuery query, CancellationToken cancel = default);
}
