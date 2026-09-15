using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ModSources"/> — every place the manager knows mods can come from.
///
/// <para>
/// The list is the feature. Sean asked for a chooser so a user can decide where their mods come from, and the
/// honest version of that says which games actually have a choice: Minecraft and Stardew Valley do, and the
/// other four have Nexus and nothing else with an API. These tests hold the manager to that rather than
/// letting it offer a dropdown with one item.
/// </para>
///
/// <para>
/// The defect underneath them is smaller and was live: a Stardew mod hosted on ModDrop had its page and its
/// newest version handed over by smapi.io, and the manager recognised only Nexus and GitHub URLs — so it
/// dropped the page and then reported the mod to the user as one it could not track.
/// </para>
/// </summary>
public class ModSourcesTests : IDisposable
{
	private readonly string _appData = Path.Combine(Path.GetTempPath(), "kinetix-src-" + Guid.NewGuid().ToString("N"));

	public ModSourcesTests() => Directory.CreateDirectory(_appData);

	public void Dispose()
	{
		try { Directory.Delete(_appData, true); } catch { }
	}

	// ---------------------------------------------------------------------
	// Recognising a page
	// ---------------------------------------------------------------------

	[Theory]
	[InlineData("https://www.nexusmods.com/stardewvalley/mods/1915", ModSources.Nexus, "1915")]
	[InlineData("https://www.nexusmods.com/skyrimspecialedition/mods/12604?tab=files", ModSources.Nexus, "12604")]
	[InlineData("https://modrinth.com/mod/sodium", ModSources.Modrinth, "sodium")]
	[InlineData("https://github.com/Pathoschild/SMAPI", ModSources.GitHub, "Pathoschild/SMAPI")]
	[InlineData("https://github.com/Pathoschild/SMAPI/releases/latest", ModSources.GitHub, "Pathoschild/SMAPI")]
	[InlineData("https://www.moddrop.com/stardew-valley/mods/567890-content-patcher", ModSources.ModDrop, "567890")]
	public void APageIsRecognisedByItsAddress(string url, string expectedSource, string expectedId)
	{
		ModPageLink? link = ModSources.ParsePage(url);

		Assert.NotNull(link);
		Assert.Equal(expectedSource, link!.SourceId);
		Assert.Equal(expectedId, link.Id);
	}

	[Fact]
	public void ANexusPageForAnyGameIsRecognised()
	{
		// It used to be matched with the word "stardewvalley" written into the pattern, which is fine right
		// up until the same code is asked about any other game.
		foreach (string domain in new[] { "stardewvalley", "skyrimspecialedition", "fallout4", "witcher3" })
		{
			ModPageLink? link = ModSources.ParsePage($"https://www.nexusmods.com/{domain}/mods/42");
			Assert.Equal(ModSources.Nexus, link?.SourceId);
			Assert.Equal("42", link?.Id);
		}
	}

	[Fact]
	public void SomewhereTheManagerHasNeverHeardOfIsNullRatherThanAGuess()
	{
		Assert.Null(ModSources.ParsePage("https://example.com/mods/1"));
		Assert.Null(ModSources.ParsePage(""));
		Assert.Null(ModSources.ParsePage(null));
	}

	[Fact]
	public void ACurseForgePageIsRecognisedWhoeverHostsTheGame()
	{
		Assert.Equal(ModSources.CurseForge,
			ModSources.ParsePage("https://www.curseforge.com/minecraft/mc-mods/jei")?.SourceId);
		Assert.Equal(ModSources.CurseForge,
			ModSources.ParsePage("https://www.curseforge.com/stardewvalley/mods/lookup-anything")?.SourceId);
	}

	// ---------------------------------------------------------------------
	// Which sites a game has
	// ---------------------------------------------------------------------

	[Fact]
	public void MinecraftAndStardewAreTheGamesWithARealChoice()
	{
		Assert.True(ModSources.SearchableFor(GameProfiles.Minecraft).Count > 1);
		Assert.True(ModSources.SearchableFor(GameProfiles.StardewValley).Count > 1);
	}

	[Theory]
	[InlineData(GameProfiles.SkyrimSE)]
	[InlineData(GameProfiles.Fallout4)]
	[InlineData(GameProfiles.Witcher3)]
	[InlineData(GameProfiles.MoonlightPeaks)]
	public void TheOtherFourHaveNexusAndNothingElseWithAnApi(string game)
	{
		// Bethesda.net, ModDB and LoversLab have no API of any kind, and Thunderstore — the obvious home for
		// a BepInEx game — has no Moonlight Peaks community. A chooser for these would be a dropdown with one
		// item in it, and offering one would be worse than not.
		IReadOnlyList<ModSourceInfo> searchable = ModSources.SearchableFor(game);

		Assert.Single(searchable);
		Assert.Equal(ModSources.Nexus, searchable[0].Id);
	}

	[Fact]
	public void MinecraftIsNotOfferedNexus()
	{
		// Nexus has no Fabric mods for it, and asking would name a game domain that is empty.
		Assert.DoesNotContain(ModSources.SearchableFor(GameProfiles.Minecraft), s => s.Id == ModSources.Nexus);
	}

	[Fact]
	public void GitHubCarriesEveryGameAndIsNeverOfferedForSearch()
	{
		ModSourceInfo github = ModSources.Find(ModSources.GitHub)!;

		Assert.True(github.CarriesGame(GameProfiles.Minecraft));
		Assert.True(github.CarriesGame(GameProfiles.SkyrimSE));
		Assert.True(github.Can(ModSourceAbilities.Download));
		Assert.False(github.Can(ModSourceAbilities.Search));   // no per-game catalogue to search
	}

	[Fact]
	public void ModDropCanSayWhatVersionAModIsAndNothingMore()
	{
		ModSourceInfo moddrop = ModSources.Find(ModSources.ModDrop)!;

		Assert.True(moddrop.Can(ModSourceAbilities.Updates));
		Assert.True(moddrop.Can(ModSourceAbilities.Page));
		Assert.False(moddrop.Can(ModSourceAbilities.Download));
	}

	[Fact]
	public void EverySiteCanAtLeastOpenAPage()
	{
		// The floor of the whole idea: if the manager knows a site at all, it can send the user there.
		Assert.All(ModSources.Known, s => Assert.True(s.Can(ModSourceAbilities.Page)));
	}

	[Fact]
	public void EverySiteHasAnIdAndAName()
	{
		Assert.All(ModSources.Known, s =>
		{
			Assert.False(string.IsNullOrWhiteSpace(s.Id));
			Assert.False(string.IsNullOrWhiteSpace(s.DisplayName));
			Assert.StartsWith("https://", s.HomeUrl);
		});
		Assert.Equal(ModSources.Known.Count, ModSources.Known.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
	}

	[Fact]
	public void TheDefaultForEachGameIsWhatWasBakedInBefore()
	{
		// Nobody's install changes until they ask it to.
		Assert.Equal(ModSources.Modrinth, ModSources.DefaultFor(GameProfiles.Minecraft));
		Assert.Equal(ModSources.Nexus, ModSources.DefaultFor(GameProfiles.StardewValley));
		Assert.Equal(ModSources.Nexus, ModSources.DefaultFor(GameProfiles.SkyrimSE));
		Assert.Equal(ModSources.Nexus, ModSources.DefaultFor(GameProfiles.NoGame));
	}

	[Fact]
	public void EveryGameCanBeSearchedSomewhere()
	{
		foreach (GameProfile game in GameProfiles.All)
			Assert.NotEmpty(ModSources.SearchableFor(game.Id));
	}

	// ---------------------------------------------------------------------
	// The page to open for an installed mod
	// ---------------------------------------------------------------------

	[Fact]
	public void APageTheManagerWasToldAboutWins()
	{
		var mod = new GameMod
		{
			NexusID = "1915",
			PageUrl = "https://www.moddrop.com/stardew-valley/mods/567890-content-patcher",
		};

		Assert.Equal(mod.PageUrl, ModSources.PageUrlFor(mod, "stardewvalley"));
	}

	[Fact]
	public void ANexusIdNeedsTheGameToMeanAnything()
	{
		// The same number is a different mod under every game, so without the domain there is no page to open
		// rather than a wrong one.
		var mod = new GameMod { NexusID = "1915" };

		Assert.Equal("https://www.nexusmods.com/stardewvalley/mods/1915", ModSources.PageUrlFor(mod, "stardewvalley"));
		Assert.Null(ModSources.PageUrlFor(mod, ""));
	}

	[Fact]
	public void AModWithNothingKnownAboutItHasNoPage()
	{
		Assert.Null(ModSources.PageUrlFor(new GameMod { Name = "Some Mod" }, "stardewvalley"));
	}

	[Fact]
	public void ModrinthAndGitHubModsGetTheirOwnPages()
	{
		Assert.Equal("https://modrinth.com/mod/sodium",
			ModSources.PageUrlFor(new GameMod { ModrinthId = "sodium" }, ""));
		Assert.Equal("https://github.com/Pathoschild/SMAPI",
			ModSources.PageUrlFor(new GameMod { GitHubRepo = "Pathoschild/SMAPI" }, ""));
	}

	// ---------------------------------------------------------------------
	// A mod on a site the manager cannot otherwise use is still trackable
	// ---------------------------------------------------------------------

	[Fact]
	public void AModDropModCountsAsLinked()
	{
		// The defect this whole piece exists for. Before, this mod was reported to the user as one the manager
		// could not track — a gap in their setup they had no way to close — immediately after smapi.io had
		// told the manager its exact version and its exact page.
		var mod = new GameMod { PageUrl = "https://www.moddrop.com/stardew-valley/mods/567890-content-patcher" };

		Assert.True(UpdateCoverage.HasUpdateLink(mod));
	}

	[Fact]
	public void AModWithNoLinkAnywhereStillCountsAsUnlinked()
	{
		Assert.False(UpdateCoverage.HasUpdateLink(new GameMod { Name = "Hand-made mod" }));
	}

	// ---------------------------------------------------------------------
	// Remembering the page between runs
	// ---------------------------------------------------------------------

	[Fact]
	public void APageIsRememberedAndGivenBackToTheNextScan()
	{
		ModPageLinks.Save(_appData, new Dictionary<string, string>
		{
			["Pathoschild.ContentPatcher"] = "https://www.moddrop.com/stardew-valley/mods/567890-content-patcher",
		});

		var scanned = new[] { new GameMod { UniqueId = "Pathoschild.ContentPatcher" } };
		int filled = ModPageLinks.Apply(scanned, ModPageLinks.Load(_appData));

		Assert.Equal(1, filled);
		Assert.Equal(ModSources.ModDrop, scanned[0].SourceId);
		Assert.Contains("moddrop.com", scanned[0].PageUrl);
	}

	[Fact]
	public void RememberingOneModDoesNotForgetTheRest()
	{
		// A check only covers the mods installed at the time. Rewriting the file from one of those would
		// forget every mod the user has since switched off.
		ModPageLinks.Save(_appData, new Dictionary<string, string> { ["A"] = "https://modrinth.com/mod/a" });
		ModPageLinks.Save(_appData, new Dictionary<string, string> { ["B"] = "https://modrinth.com/mod/b" });

		Dictionary<string, string> map = ModPageLinks.Load(_appData);

		Assert.Equal(2, map.Count);
		Assert.Equal("https://modrinth.com/mod/a", map["A"]);
	}

	[Fact]
	public void AModThatAlreadyKnowsItsPageIsLeftAlone()
	{
		ModPageLinks.Save(_appData, new Dictionary<string, string> { ["A"] = "https://modrinth.com/mod/stale" });

		var scanned = new[] { new GameMod { UniqueId = "A", PageUrl = "https://modrinth.com/mod/fresh" } };
		ModPageLinks.Apply(scanned, ModPageLinks.Load(_appData));

		Assert.Equal("https://modrinth.com/mod/fresh", scanned[0].PageUrl);
	}

	[Fact]
	public void NoRememberedPagesIsNotAFailure()
	{
		Assert.Empty(ModPageLinks.Load(Path.Combine(_appData, "never-written")));
	}
}
