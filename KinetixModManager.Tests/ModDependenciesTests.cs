using System;
using System.Collections.Generic;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ModDependencies.Dependents"/> — the question asked before deleting a mod.
///
/// Getting it wrong is expensive somewhere else, later. Removing a mod three others quietly require does
/// not fail at the time; it fails on the next launch, as a game that will not start or a feature that has
/// gone, with nothing connecting the two events. These pin the Stardew half, which is exact because the
/// manifest declares it. The Bethesda half infers dependencies from plugin headers on disk and is covered
/// by <see cref="BethesdaPlugins"/> instead.
/// </summary>
public class ModDependenciesTests
{
	private static GameMod Mod(string name, string uniqueId = "", params (string Id, bool Required)[] needs) =>
		new()
		{
			Name = name,
			UniqueId = uniqueId,
			Dependencies = needs
				.Select(n => new ModDependency { UniqueId = n.Id, IsRequired = n.Required })
				.ToList()
		};

	private static IReadOnlyList<ModDependencies.Dependent> Who(GameMod target, params GameMod[] installed) =>
		ModDependencies.Dependents(target, installed, GameProfiles.StardewValley);

	[Fact]
	public void AModNothingNeedsHasNoDependents()
	{
		GameMod target = Mod("Lonely Mod", "author.lonely");

		Assert.Empty(Who(target, target, Mod("Unrelated", "author.unrelated")));
	}

	[Fact]
	public void AModThatDeclaresItIsFound()
	{
		GameMod target = Mod("Content Patcher", "Pathoschild.ContentPatcher");
		GameMod needer = Mod("Ridgeside Village", "Rafseazz.RSVCP", ("Pathoschild.ContentPatcher", true));

		ModDependencies.Dependent only = Assert.Single(Who(target, target, needer));

		Assert.Equal("Ridgeside Village", only.Mod.Name);
		Assert.True(only.IsDeclared);
	}

	[Fact]
	public void AnOptionalDependencyIsNotADependent()
	{
		// Deleting something only suggested must not warn as though the game will break. A warning that
		// cries wolf is one the user learns to click past, and then the real one goes past too.
		GameMod target = Mod("Generic Config Menu", "spacechase0.GenericModConfigMenu");
		GameMod optional = Mod("Some Mod", "a.b", ("spacechase0.GenericModConfigMenu", false));

		Assert.Empty(Who(target, target, optional));
	}

	[Fact]
	public void MatchingIgnoresCaseBecauseAUniqueIdIsTypedTwiceByTwoPeople()
	{
		GameMod target = Mod("SpaceCore", "spacechase0.SpaceCore");
		GameMod needer = Mod("Needs It", "x.y", ("SPACECHASE0.SPACECORE", true));

		Assert.Single(Who(target, target, needer));
	}

	[Fact]
	public void AModWithNoUniqueIdCannotBeDependedOn()
	{
		// Nothing can name it, so nothing can require it. Answering otherwise would match every mod whose
		// dependency id also happened to be empty.
		GameMod target = Mod("Nameless");
		GameMod other = Mod("Other", "a.b", ("", true));

		Assert.Empty(Who(target, target, other));
	}

	[Fact]
	public void AModIsNotItsOwnDependent()
	{
		GameMod target = Mod("Self", "a.self", ("a.self", true));

		Assert.Empty(Who(target, target));
	}

	[Fact]
	public void GroupRowsAreSkippedRatherThanCountedAsMods()
	{
		// The installed list carries group headings as GameMod objects. One counted as a dependent would
		// tell the user a folder heading needs the mod they are deleting.
		GameMod target = Mod("Target", "a.target");
		var group = new GameMod { Name = "A Group", IsGroup = true, UniqueId = "a.group" };
		group.Dependencies.Add(new ModDependency { UniqueId = "a.target", IsRequired = true });

		Assert.Empty(Who(target, target, group));
	}

	[Fact]
	public void EveryDependentIsReportedNotJustTheFirst()
	{
		GameMod target = Mod("SMAPI Helper", "a.helper");

		var found = Who(target, target,
			Mod("One", "b.one", ("a.helper", true)),
			Mod("Two", "b.two", ("a.helper", true)),
			Mod("Three", "b.three", ("a.helper", true)));

		Assert.Equal(3, found.Count);
	}

	[Fact]
	public void NoInstalledModsIsAnEmptyAnswerRatherThanAFailure()
	{
		Assert.Empty(ModDependencies.Dependents(Mod("x", "a.x"), Array.Empty<GameMod>(), GameProfiles.StardewValley));
		Assert.Empty(ModDependencies.Dependents(Mod("x", "a.x"), null!, GameProfiles.StardewValley));
	}

	[Fact]
	public void AModWithNoFolderShipsNoPlugins()
	{
		Assert.Empty(ModDependencies.PluginFiles(new GameMod { Name = "Nowhere", FolderPath = "" }));
	}
}
