using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ModSearchPlan"/> — which catalogues a search asks, and how their answers go together.
///
/// <para>
/// Sean asked for a chooser so a user can decide where their mods come from, and for the choosing itself to
/// be a setting: search everything, search one place, or search a preferred place with the rest on request.
/// This is that decision, kept away from the network so it can be tested at all.
/// </para>
///
/// <para>
/// Two rules here are accessibility rules rather than plumbing. Two catalogues will return the same mod, and
/// two rows a listener cannot tell apart are worse than one row — so duplicates collapse, and what survives
/// says where it came from, but only when more than one catalogue was actually asked. And a catalogue that
/// could not answer becomes a sentence the user hears, because a search that quietly leaves one out reads as
/// "there is nothing there".
/// </para>
/// </summary>
public class ModSearchPlanTests
{
	/// <summary>A catalogue that answers with whatever it was told to, without a network.</summary>
	private sealed class FakeSource : IModSource
	{
		private readonly List<GameMod> _results;
		private readonly Exception? _throws;

		public FakeSource(string id, IEnumerable<GameMod>? results = null, bool ready = true,
			string reason = "", Exception? throws = null)
		{
			Info = ModSources.Find(id) ?? throw new ArgumentException($"no such source: {id}", nameof(id));
			_results = results?.ToList() ?? new List<GameMod>();
			Status = ready ? ModSourceStatus.Available : new ModSourceStatus(false, reason);
			_throws = throws;
		}

		public ModSourceInfo Info { get; }
		public ModSourceStatus Status { get; }
		public bool Asked { get; private set; }

		public bool CanSearch(string? gameId) => Info.CarriesGame(gameId);

		public Task<ModSearchResults> SearchAsync(ModSearchQuery query, CancellationToken cancel = default)
		{
			Asked = true;
			if (_throws != null) throw _throws;
			return Task.FromResult(new ModSearchResults(_results, _results.Count));
		}
	}

	private static GameMod Mod(string name, string author = "Someone") =>
		new() { Name = name, Author = author, IsSearchResult = true };

	private static ModSearchQuery Query(string game) => new(game, "anything", 1, 25);

	// ---------------------------------------------------------------------
	// Which catalogues get asked
	// ---------------------------------------------------------------------

	[Fact]
	public void SearchingEverythingAsksEveryCatalogueThatCarriesTheGame()
	{
		var sources = new IModSource[] { new FakeSource(ModSources.Nexus), new FakeSource(ModSources.CurseForge) };

		IReadOnlyList<IModSource> chosen = ModSearchPlan.Choose(
			sources, GameProfiles.StardewValley, ModSearchMode.AllSources, ModSources.Nexus);

		Assert.Equal(2, chosen.Count);
	}

	[Fact]
	public void SearchingOneAsksOnlyThePreferredOne()
	{
		var sources = new IModSource[] { new FakeSource(ModSources.Nexus), new FakeSource(ModSources.CurseForge) };

		IReadOnlyList<IModSource> chosen = ModSearchPlan.Choose(
			sources, GameProfiles.StardewValley, ModSearchMode.OneSource, ModSources.CurseForge);

		Assert.Single(chosen);
		Assert.Equal(ModSources.CurseForge, chosen[0].Info.Id);
	}

	[Fact]
	public void PreferredFirstAsksOneUntilTheUserSaysOtherwise()
	{
		var sources = new IModSource[] { new FakeSource(ModSources.Nexus), new FakeSource(ModSources.CurseForge) };

		Assert.Single(ModSearchPlan.Choose(
			sources, GameProfiles.StardewValley, ModSearchMode.PreferredFirst, ModSources.Nexus));

		Assert.Equal(2, ModSearchPlan.Choose(
			sources, GameProfiles.StardewValley, ModSearchMode.PreferredFirst, ModSources.Nexus,
			alsoTheOthers: true).Count);
	}

	[Fact]
	public void ThePreferredCatalogueIsAlwaysAskedFirst()
	{
		// Beyond taste: it is the order duplicates resolve in, so the user's own choice is the copy shown.
		var sources = new IModSource[] { new FakeSource(ModSources.Nexus), new FakeSource(ModSources.CurseForge) };

		IReadOnlyList<IModSource> chosen = ModSearchPlan.Choose(
			sources, GameProfiles.StardewValley, ModSearchMode.AllSources, ModSources.CurseForge);

		Assert.Equal(ModSources.CurseForge, chosen[0].Info.Id);
	}

	[Fact]
	public void APreferenceForACatalogueThatDoesNotCarryTheGameFallsBackQuietly()
	{
		// The setting is per game and a user may simply have switched. Falling back to what the game came with
		// is right; complaining about it is not.
		var sources = new IModSource[] { new FakeSource(ModSources.Nexus), new FakeSource(ModSources.Modrinth) };

		IReadOnlyList<IModSource> chosen = ModSearchPlan.Choose(
			sources, GameProfiles.SkyrimSE, ModSearchMode.OneSource, ModSources.Modrinth);

		Assert.Single(chosen);
		Assert.Equal(ModSources.Nexus, chosen[0].Info.Id);
	}

	[Fact]
	public void NowhereToSearchIsAnEmptyListRatherThanAGuess()
	{
		var sources = new IModSource[] { new FakeSource(ModSources.Modrinth) };

		Assert.Empty(ModSearchPlan.Choose(sources, GameProfiles.SkyrimSE, ModSearchMode.AllSources, null));
	}

	[Fact]
	public void TheCataloguesNotAskedAreWhatTheOfferIsMadeFrom()
	{
		var sources = new IModSource[] { new FakeSource(ModSources.Nexus), new FakeSource(ModSources.CurseForge) };
		IReadOnlyList<IModSource> asked = ModSearchPlan.Choose(
			sources, GameProfiles.StardewValley, ModSearchMode.PreferredFirst, ModSources.Nexus);

		IReadOnlyList<IModSource> rest = ModSearchPlan.Remaining(sources, GameProfiles.StardewValley, asked);

		Assert.Single(rest);
		Assert.Equal(ModSources.CurseForge, rest[0].Info.Id);
	}

	[Fact]
	public void WhenEverythingWasAskedThereIsNothingLeftToOffer()
	{
		var sources = new IModSource[] { new FakeSource(ModSources.Nexus), new FakeSource(ModSources.CurseForge) };
		IReadOnlyList<IModSource> asked = ModSearchPlan.Choose(
			sources, GameProfiles.StardewValley, ModSearchMode.AllSources, ModSources.Nexus);

		Assert.Empty(ModSearchPlan.Remaining(sources, GameProfiles.StardewValley, asked));
	}

	// ---------------------------------------------------------------------
	// Putting the answers together
	// ---------------------------------------------------------------------

	[Fact]
	public async Task ResultsFromTwoCataloguesArriveInTheOrderTheyWereAsked()
	{
		// Not in the order they answered. A list whose contents depend on which server was quickest today is
		// not a list anyone can learn their way around.
		var sources = new IModSource[]
		{
			new FakeSource(ModSources.Nexus, new[] { Mod("Automate") }),
			new FakeSource(ModSources.CurseForge, new[] { Mod("Lookup Anything") }),
		};

		ModSearchResults found = await ModSearchPlan.SearchAsync(sources, Query(GameProfiles.StardewValley));

		Assert.Equal(new[] { "Automate", "Lookup Anything" }, found.Results.Select(m => m.Name));
		Assert.Equal(2, found.Total);
	}

	[Fact]
	public async Task TheSameModOnTwoCataloguesIsShownOnce()
	{
		var sources = new IModSource[]
		{
			new FakeSource(ModSources.Nexus, new[] { Mod("Automate", "Pathoschild") }),
			new FakeSource(ModSources.CurseForge, new[] { Mod("Automate", "Pathoschild") }),
		};

		ModSearchResults found = await ModSearchPlan.SearchAsync(sources, Query(GameProfiles.StardewValley));

		Assert.Single(found.Results);
		Assert.Equal(ModSources.Nexus, found.Results[0].SourceId);   // the one asked first wins
	}

	[Fact]
	public async Task TwoDifferentModsThatShareANameAreBothKept()
	{
		// The matching errs towards keeping both. Showing a mod twice is untidy; hiding a genuinely different
		// mod because it shares a name is a mod the user cannot find at all.
		var sources = new IModSource[]
		{
			new FakeSource(ModSources.Nexus, new[] { Mod("Automate", "Pathoschild") }),
			new FakeSource(ModSources.CurseForge, new[] { Mod("Automate", "Somebody Else") }),
		};

		ModSearchResults found = await ModSearchPlan.SearchAsync(sources, Query(GameProfiles.StardewValley));

		Assert.Equal(2, found.Results.Count);
	}

	[Fact]
	public async Task ARowSaysWhereItCameFromOnlyWhenMoreThanOneCatalogueAnswered()
	{
		// On a single-source search it would be the same phrase on every row of a hundred.
		var one = new IModSource[] { new FakeSource(ModSources.Nexus, new[] { Mod("Automate") }) };
		ModSearchResults single = await ModSearchPlan.SearchAsync(one, Query(GameProfiles.StardewValley));

		Assert.Equal(ModSources.Nexus, single.Results[0].SourceId);
		Assert.False(single.Results[0].ShowSource);
		Assert.DoesNotContain("From Nexus Mods", single.Results[0].ToString());

		var two = new IModSource[]
		{
			new FakeSource(ModSources.Nexus, new[] { Mod("Automate", "A") }),
			new FakeSource(ModSources.CurseForge, new[] { Mod("Lookup Anything", "B") }),
		};
		ModSearchResults merged = await ModSearchPlan.SearchAsync(two, Query(GameProfiles.StardewValley));

		Assert.All(merged.Results, m => Assert.True(m.ShowSource));
		Assert.Contains("From Nexus Mods.", merged.Results[0].ToString());
		Assert.Contains("From CurseForge.", merged.Results[1].ToString());
	}

	// ---------------------------------------------------------------------
	// When a catalogue cannot answer
	// ---------------------------------------------------------------------

	[Fact]
	public async Task ACatalogueThatIsNotReadyIsNotAskedAndIsMentioned()
	{
		var sources = new IModSource[]
		{
			new FakeSource(ModSources.Nexus, new[] { Mod("Automate") }),
			new FakeSource(ModSources.CurseForge, ready: false, reason: "CurseForge needs an API key."),
		};

		ModSearchResults found = await ModSearchPlan.SearchAsync(sources, Query(GameProfiles.StardewValley));

		Assert.Single(found.Results);
		Assert.Contains("CurseForge needs an API key.", found.Notes);
		Assert.False(((FakeSource)sources[1]).Asked);
	}

	[Fact]
	public async Task OneCatalogueBeingDownStillReturnsTheOthers()
	{
		// A failed search across three sites where one is down should return the other two and say so.
		var sources = new IModSource[]
		{
			new FakeSource(ModSources.Nexus, new[] { Mod("Automate") }),
			new FakeSource(ModSources.CurseForge, throws: new InvalidOperationException("no")),
		};

		ModSearchResults found = await ModSearchPlan.SearchAsync(sources, Query(GameProfiles.StardewValley));

		Assert.Single(found.Results);
		Assert.Contains(found.Notes, n => n.Contains("CurseForge"));
	}

	[Fact]
	public async Task AskingNobodyIsAnEmptyAnswerRatherThanAThrow()
	{
		Assert.Empty((await ModSearchPlan.SearchAsync(Array.Empty<IModSource>(), Query(GameProfiles.SkyrimSE))).Results);
	}

	[Fact]
	public async Task ASingleCatalogueThatIsNotReadySaysSoRatherThanReturningNothing()
	{
		var sources = new IModSource[]
		{
			new FakeSource(ModSources.Nexus, ready: false, reason: "An API key is required."),
		};

		ModSearchResults found = await ModSearchPlan.SearchAsync(sources, Query(GameProfiles.StardewValley));

		Assert.Empty(found.Results);
		Assert.Contains("An API key is required.", found.Notes);
	}
}
