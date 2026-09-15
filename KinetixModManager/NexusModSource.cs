using System;
using System.Threading;
using System.Threading.Tasks;

namespace KinetixModManager;

/// <summary>
/// Nexus as a catalogue the user can choose.
///
/// <para>
/// This lives in the app rather than the core for one honest reason: <see cref="NexusService"/> does. It is
/// 1,387 lines, an instance, stateful, carrying the user's key and their rate-limit counters, and reaching
/// into <see cref="AppSettings"/> — none of which belongs in a platform-independent layer, and none of which
/// is going to move in the same change that adds a source chooser.
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
