using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KinetixModManager;

/// <summary>
/// Modrinth as a catalogue the user can choose.
///
/// <para>
/// A thin cover over <see cref="ModrinthService"/>, which is static and stateless and stays that way. The
/// only judgement in here is about the Minecraft version: Modrinth will happily return a mod built for
/// another one, and a mod built for another version installs perfectly and then loads nothing. Without a
/// version to filter to there is no search worth running, so it says so rather than returning results that
/// cannot be used.
/// </para>
/// </summary>
public sealed class ModrinthModSource : IModSource
{
	public ModSourceInfo Info { get; } = ModSources.Find(ModSources.Modrinth)!;

	/// <summary>Always. Modrinth's search needs no account, which is most of why Minecraft support was easy.</summary>
	public ModSourceStatus Status => ModSourceStatus.Available;

	public bool CanSearch(string? gameId) => Info.CarriesGame(gameId);

	public async Task<ModSearchResults> SearchAsync(ModSearchQuery query, CancellationToken cancel = default)
	{
		if (string.IsNullOrWhiteSpace(query.GameVersion))
		{
			return ModSearchResults.Empty with
			{
				Notes = new[] { Loc.T("search.modrinthNeedsVersion") },
			};
		}

		// Language and category are Nexus's filters and have no equivalent here. Modrinth's own categories are
		// a different vocabulary entirely, so honouring them would promise filtering that is not happening.
		var (results, total) = await ModrinthService.SearchAsync(
			query.Term, query.GameVersion!, (query.Page - 1) * query.PageSize, query.PageSize);

		return new ModSearchResults(results, total);
	}
}

/// <summary>
/// CurseForge, listed and not yet usable.
///
/// <para>
/// It is a real second catalogue for both Minecraft and Stardew Valley and a large one, and it is here so
/// the user can see that the manager knows about it and what is standing in the way — the same reasoning as
/// the Nexus sign-in button, which says plainly that the manager is not registered yet rather than failing
/// in a way nobody could act on.
/// </para>
///
/// <para>
/// Two things are needed, and only one of them is code. CurseForge issues API keys to approved applications,
/// which is a conversation with them. And a mod's author may opt out of third-party downloads, in which case
/// the API deliberately returns no file URL at all — so even with a key, some results can only ever open in
/// a browser, and a source that pretended otherwise would fail at the download with no explanation.
/// </para>
/// </summary>
public sealed class CurseForgeModSource : IModSource
{
	private readonly Func<string?> _apiKey;

	/// <param name="apiKey">Reads the stored key, or null while there is none.</param>
	public CurseForgeModSource(Func<string?> apiKey) => _apiKey = apiKey;

	public ModSourceInfo Info { get; } = ModSources.Find(ModSources.CurseForge)!;

	public ModSourceStatus Status =>
		string.IsNullOrWhiteSpace(_apiKey())
			? new ModSourceStatus(false, Loc.T("search.curseforgeNeedsKey"))
			: new ModSourceStatus(false, Loc.T("search.curseforgeNotBuilt"));

	public bool CanSearch(string? gameId) => Info.CarriesGame(gameId);

	/// <summary>
	/// Never reached while <see cref="Status"/> is not ready — the planner checks first, so an unavailable
	/// source turns into a sentence the user hears rather than an exception.
	/// </summary>
	public Task<ModSearchResults> SearchAsync(ModSearchQuery query, CancellationToken cancel = default) =>
		Task.FromResult(ModSearchResults.Empty with { Notes = new[] { Status.Reason } });
}
