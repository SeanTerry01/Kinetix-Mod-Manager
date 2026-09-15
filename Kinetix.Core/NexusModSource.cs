using System;
using System.Threading;
using System.Threading.Tasks;

namespace KinetixModManager;

/// <summary>
/// Nexus as a catalogue the user can choose.
///
/// <para>
/// This was written expecting to stay in the WinForms project, because <see cref="NexusService"/> was there
/// and looked immovable: 1,387 lines, an instance, stateful, carrying the user's key and their rate-limit
/// counters. None of that turned out to be a portability problem — it referenced nothing Windows-only at all
/// — and both moved to the core without a line changing. The estimate was wrong because it was made from how
/// the file felt rather than from what it referenced.
/// </para>
///
/// <para>
/// What the cover does show is how little of that surface a <em>catalogue</em> needs. Endorsements, NXM
/// links, tracked mods, changelogs and rate limits are Nexus's own business and are reached through
/// <see cref="NexusService"/> directly, as they always were. Searching is the part every site has, and it is
/// the only part in here.
/// </para>
/// </summary>
public sealed class NexusModSource : IModSource
{
	private readonly NexusService _nexus;
	private readonly AppSettings _settings;

	public NexusModSource(NexusService nexus, AppSettings settings)
	{
		_nexus = nexus;
		_settings = settings;
	}

	public ModSourceInfo Info { get; } = ModSources.Find(ModSources.Nexus)!;

	/// <summary>
	/// Needs a key, and says so in the words the rest of the app already uses for it. Reported rather than
	/// discovered at the download, so a user choosing Nexus in Settings is told there before they search.
	/// </summary>
	public ModSourceStatus Status =>
		string.IsNullOrWhiteSpace(_settings.ApiKey)
			? new ModSourceStatus(false, Loc.T("status.authRequired"))
			: ModSourceStatus.Available;

	public bool CanSearch(string? gameId) => Info.CarriesGame(gameId);

	public async Task<ModSearchResults> SearchAsync(ModSearchQuery query, CancellationToken cancel = default)
	{
		var (results, total) = await _nexus.SearchModsAsync(
			query.SearchType, query.Term, query.Page, query.PageSize, query.Language, query.Category);

		return new ModSearchResults(results, total);
	}
}
