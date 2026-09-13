using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="UpdateCoverage"/> — the rules deciding which installed mods the update check can cover.
/// The behaviour that matters: a Stardew download frequently unpacks into several mods where only one carries
/// the update key, and reporting the rest as "not checked" told users their setup was broken when it wasn't.
/// </summary>
public class UpdateCoverageTests
{
	private static readonly string ModsPath = Path.Combine("C:", "Games", "Stardew Valley", "Mods");

	private static GameMod Mod(string name, string folder, string? nexusId = null, string? gitHub = null) => new()
	{
		Name = name,
		UniqueId = name.Replace(" ", "."),
		Version = "1.0.0",
		NexusID = nexusId,
		GitHubRepo = gitHub,
		FolderPath = Path.Combine(ModsPath, folder)
	};

	private static UpdateCoverageEntry Entry(List<UpdateCoverageEntry> all, string name) =>
		all.Single(e => e.Mod.Name == name);

	[Fact]
	public void ModWithItsOwnNexusId_IsCheckedDirectly()
	{
		var mods = new List<GameMod> { Mod("Content Patcher", "ContentPatcher", nexusId: "1915") };

		var result = UpdateCoverage.Classify(mods, ModsPath);

		Assert.Equal(UpdateCoverageKind.Linked, Entry(result, "Content Patcher").Kind);
	}

	[Fact]
	public void ModInsideALinkedModsFolder_IsCoveredByThatMod()
	{
		// The shape that caused the false "no Nexus link" reports: one download, a main mod plus the content
		// packs it ships, with only the main mod carrying the update key.
		var main = Mod("Big Expansion", "BigExpansion", nexusId: "4242");
		var pack = Mod("Big Expansion Portraits", Path.Combine("BigExpansion", "[CP] Portraits"));

		var result = UpdateCoverage.Classify(new List<GameMod> { main, pack }, ModsPath);

		var entry = Entry(result, "Big Expansion Portraits");
		Assert.Equal(UpdateCoverageKind.Bundled, entry.Kind);
		Assert.Same(main, entry.Parent);
	}

	[Fact]
	public void ModSharingATopLevelFolderWithALinkedMod_IsCoveredByIt()
	{
		// A download that unpacks several mods side by side into one wrapper folder, none of them at its root.
		var linked = Mod("Suite Core", Path.Combine("ModSuite", "Core"), nexusId: "777");
		var sibling = Mod("Suite Extras", Path.Combine("ModSuite", "Extras"));

		var result = UpdateCoverage.Classify(new List<GameMod> { linked, sibling }, ModsPath);

		var entry = Entry(result, "Suite Extras");
		Assert.Equal(UpdateCoverageKind.Bundled, entry.Kind);
		Assert.Same(linked, entry.Parent);
	}

	[Fact]
	public void UnlinkedModInItsOwnFolder_IsReportedAsUnchecked()
	{
		var mods = new List<GameMod>
		{
			Mod("Content Patcher", "ContentPatcher", nexusId: "1915"),
			Mod("Hand Copied Mod", "HandCopiedMod")
		};

		var result = UpdateCoverage.Classify(mods, ModsPath);

		var entry = Entry(result, "Hand Copied Mod");
		Assert.Equal(UpdateCoverageKind.Unchecked, entry.Kind);
		Assert.Null(entry.Parent);
	}

	[Fact]
	public void UnlinkedModKnownToTheSmapiDatabase_IsNotReportedAsUnchecked()
	{
		var mod = Mod("Dynamic Map Tiles Extended", "DynamicMapTilesExtended");
		var known = new HashSet<string> { mod.UniqueId };

		var result = UpdateCoverage.Classify(new List<GameMod> { mod }, ModsPath, known);

		Assert.Equal(UpdateCoverageKind.SmapiDatabase, Entry(result, "Dynamic Map Tiles Extended").Kind);
	}

	[Fact]
	public void SiblingsThatAreAllUnlinked_AreAllReportedAsUnchecked()
	{
		// Nothing in the group carries a link, so nobody covers anybody: these really are unchecked.
		var a = Mod("Orphan A", Path.Combine("MysteryPack", "A"));
		var b = Mod("Orphan B", Path.Combine("MysteryPack", "B"));

		var result = UpdateCoverage.Classify(new List<GameMod> { a, b }, ModsPath);

		Assert.All(result, e => Assert.Equal(UpdateCoverageKind.Unchecked, e.Kind));
	}

	[Fact]
	public void LinkedSubModCoversItsUnlinkedParentFolder()
	{
		// The reverse nesting: the wrapper folder's own mod has no key but a mod inside it does. The same
		// download delivered both, so reinstalling it still covers the parent.
		var parent = Mod("Wrapper Mod", "WrapperMod");
		var child = Mod("Wrapper Core", Path.Combine("WrapperMod", "Core"), nexusId: "31337");

		var result = UpdateCoverage.Classify(new List<GameMod> { parent, child }, ModsPath);

		var entry = Entry(result, "Wrapper Mod");
		Assert.Equal(UpdateCoverageKind.Bundled, entry.Kind);
		Assert.Same(child, entry.Parent);
	}

	[Fact]
	public void ModTheUserSaysComesWithAnother_IsCoveredByIt()
	{
		// CapeOreNodes: an optional file from the Cape Stardew page that unpacks into a folder of its own, with
		// no update key. Nothing on disk connects the two, so the user says so once.
		var main = Mod("Cape Stardew", Path.Combine("Cape Stardew 1.6", "Cape Stardew"), nexusId: "14635");
		var extra = Mod("CapeOreNodes", "Kimberlite ItemExtensions");
		var declared = new Dictionary<string, string> { [extra.UniqueId] = main.UniqueId };

		var result = UpdateCoverage.Classify(new List<GameMod> { main, extra }, ModsPath, null, declared);

		var entry = Entry(result, "CapeOreNodes");
		Assert.Equal(UpdateCoverageKind.Bundled, entry.Kind);
		Assert.Same(main, entry.Parent);
	}

	[Fact]
	public void DeclaredParentThatIsNoLongerInstalled_LeavesTheModUnchecked()
	{
		var extra = Mod("CapeOreNodes", "Kimberlite ItemExtensions");
		var declared = new Dictionary<string, string> { [extra.UniqueId] = "some.uninstalled.mod" };

		var result = UpdateCoverage.Classify(new List<GameMod> { extra }, ModsPath, null, declared);

		Assert.Equal(UpdateCoverageKind.Unchecked, result[0].Kind);
	}

	[Fact]
	public void DeclaredParentWithNoUpdateLink_CoversNothing()
	{
		// Attaching a mod to one that can't itself be checked would only hide the problem.
		var parent = Mod("Unlinked Parent", "UnlinkedParent");
		var child = Mod("Stray Pack", "StrayPack");
		var declared = new Dictionary<string, string> { [child.UniqueId] = parent.UniqueId };

		var result = UpdateCoverage.Classify(new List<GameMod> { parent, child }, ModsPath, null, declared);

		Assert.Equal(UpdateCoverageKind.Unchecked, Entry(result, "Stray Pack").Kind);
	}

	[Fact]
	public void DeclaredParentWins_OverTheFolderBasedGuess()
	{
		var folderNeighbour = Mod("Folder Neighbour", Path.Combine("Shared", "Neighbour"), nexusId: "111");
		var realSource = Mod("Real Source", "RealSource", nexusId: "222");
		var child = Mod("Stray Pack", Path.Combine("Shared", "Stray"));
		var declared = new Dictionary<string, string> { [child.UniqueId] = realSource.UniqueId };

		var result = UpdateCoverage.Classify(
			new List<GameMod> { folderNeighbour, realSource, child }, ModsPath, null, declared);

		Assert.Same(realSource, Entry(result, "Stray Pack").Parent);
	}

	[Fact]
	public void SmapiOwnBundledMods_AreNotReportedAsUnchecked()
	{
		// Console Commands and friends live in the Mods folder but come with SMAPI, not from Nexus.
		var mod = Mod("Console Commands", "ConsoleCommands");
		mod.UniqueId = "SMAPI.ConsoleCommands";

		var result = UpdateCoverage.Classify(new List<GameMod> { mod }, ModsPath);

		Assert.Equal(UpdateCoverageKind.PartOfSmapi, result[0].Kind);
	}

	[Fact]
	public void UncheckedMod_ReportsWhetherItsUpdateKeyIsMissingOrBlank()
	{
		var noKey = Mod("No Key At All", "NoKeyAtAll");
		var blankKey = Mod("Blank Key", "BlankKey");
		blankKey.HasBlankUpdateKey = true;   // e.g. "UpdateKeys": [""] or ["Nexus: "]

		var result = UpdateCoverage.Classify(new List<GameMod> { noKey, blankKey }, ModsPath);

		Assert.Equal(UncheckedReason.NoUpdateKey, Entry(result, "No Key At All").Reason);
		Assert.Equal(UncheckedReason.BlankUpdateKey, Entry(result, "Blank Key").Reason);
	}

	[Fact]
	public void CoveredMods_CarryNoUncheckedReason()
	{
		var main = Mod("Main Mod", "MainMod", nexusId: "10");
		var pack = Mod("Bundled Pack", Path.Combine("MainMod", "Pack"));
		pack.HasBlankUpdateKey = true;

		var result = UpdateCoverage.Classify(new List<GameMod> { main, pack }, ModsPath);

		Assert.Equal(UpdateCoverageKind.Bundled, Entry(result, "Bundled Pack").Kind);
		Assert.Equal(UncheckedReason.None, Entry(result, "Bundled Pack").Reason);
	}

	[Fact]
	public void Representative_IsTheModThatGivesTheFolderItsName()
	{
		// The real Cape Stardew download: five mods in one folder. Picking alphabetically named the download
		// "AnnettesRetreat [Farm Type Manager component]", which nobody would recognise as Cape Stardew.
		var group = new List<GameMod>
		{
			Mod("AnnettesRetreat [Farm Type Manager component]", Path.Combine("Cape Stardew 1.6", "(FTM) Cape Stardew"), nexusId: "14635"),
			Mod("Annetta NPC", Path.Combine("Cape Stardew 1.6", "[CP]Annetta"), nexusId: "14635"),
			Mod("Cape Stardew", Path.Combine("Cape Stardew 1.6", "Cape Stardew"), nexusId: "14635"),
			Mod("CapeStardewCode", Path.Combine("Cape Stardew 1.6", "CapeStardewCode"), nexusId: "14635"),
			Mod("OrbOfTheTides", Path.Combine("Cape Stardew 1.6", "(CP)OrbOfTheTides"), nexusId: "14635")
		};

		Assert.Equal("Cape Stardew", UpdateCoverage.PickRepresentative(group, ModsPath).Name);
	}

	[Fact]
	public void Representative_PrefersAnExactFolderNameMatchOverAPartialOne()
	{
		// Both mods' names sit inside the folder name; the one that matches it exactly is the main mod.
		var group = new List<GameMod>
		{
			Mod("MFM Grannys Recipe Box", Path.Combine("[CP] Granny's Recipe Box", "[MFM] Granny's Recipe Box"), nexusId: "23737"),
			Mod("Granny's Recipe Box", Path.Combine("[CP] Granny's Recipe Box", "[CP] Granny's Recipe Box"), nexusId: "23737")
		};

		Assert.Equal("Granny's Recipe Box", UpdateCoverage.PickRepresentative(group, ModsPath).Name);
	}

	[Fact]
	public void Representative_PrefersATopLevelModOverOneNestedInsideIt()
	{
		var group = new List<GameMod>
		{
			Mod("Zebra Pack", Path.Combine("MainMod", "ZebraPack"), nexusId: "5"),
			Mod("Something Else Entirely", "MainMod", nexusId: "5")
		};

		Assert.Equal("Something Else Entirely", UpdateCoverage.PickRepresentative(group, ModsPath).Name);
	}

	[Fact]
	public void Representative_FallsBackToAlphabeticalWhenNoNameMatchesTheFolder()
	{
		var group = new List<GameMod>
		{
			Mod("Zulu", Path.Combine("Bundle", "Z"), nexusId: "7"),
			Mod("Alpha", Path.Combine("Bundle", "A"), nexusId: "7")
		};

		Assert.Equal("Alpha", UpdateCoverage.PickRepresentative(group, ModsPath).Name);
	}

	[Fact]
	public void GroupRows_AreIgnored()
	{
		var mods = new List<GameMod>
		{
			new() { Name = "A Group", IsGroup = true, FolderPath = Path.Combine(ModsPath, "AGroup") },
			Mod("Real Mod", "RealMod", nexusId: "1")
		};

		var result = UpdateCoverage.Classify(mods, ModsPath);

		Assert.Single(result);
		Assert.Equal("Real Mod", result[0].Mod.Name);
	}

	[Fact]
	public void RelativeFolder_IsTheFolderUnderMods_AndEmptyWhenOutsideIt()
	{
		var inside = Mod("Inside", Path.Combine("Group", "Sub"));
		var outside = new GameMod { Name = "Outside", FolderPath = Path.Combine("D:", "Elsewhere", "Mod") };

		Assert.Equal(Path.Combine("Group", "Sub"), UpdateCoverage.RelativeFolder(inside, ModsPath));
		Assert.Equal("Group", UpdateCoverage.TopLevelFolder(inside, ModsPath));
		Assert.Equal("", UpdateCoverage.RelativeFolder(outside, ModsPath));
	}

	// -------------------------------------------------------------------------
	// A catalogue that identifies a mod by its file
	// -------------------------------------------------------------------------

	[Fact]
	public void AModCheckedByItsFileHashIsCoveredRatherThanUnchecked()
	{
		// Reported exactly backwards before this. Fabric API carries no Nexus id and no GitHub repo, and it
		// was the one mod in the folder that could be identified with total certainty: Modrinth recognises it
		// by the SHA-1 of its jar. The report told the user to go and link something that was not broken.
		var fabricApi = Mod("Fabric API", "fabric-api-0.160.0+26.2.jar");

		var coverage = UpdateCoverage.Classify(
			new[] { fabricApi }, ModsPath, checkedByFileHash: true);

		Assert.Equal(UpdateCoverageKind.ByFileHash, Entry(coverage, "Fabric API").Kind);
		Assert.DoesNotContain(coverage, c => c.Kind == UpdateCoverageKind.Unchecked);
	}

	[Fact]
	public void AFileHashGameStillPrefersARealLinkWhenTheModHasOne()
	{
		// United Minecraft names its GitHub repository in its own manifest, and that is a stronger statement
		// than "some catalogue might recognise the bytes" - it says where the mod actually comes from.
		var united = Mod("United Minecraft", "united-minecraft-1.1.0+mc26.2.jar",
			gitHub: "blindgoofball/united-Minecraft");

		var coverage = UpdateCoverage.Classify(new[] { united }, ModsPath, checkedByFileHash: true);

		Assert.Equal(UpdateCoverageKind.Linked, Entry(coverage, "United Minecraft").Kind);
	}

	[Fact]
	public void EveryOtherGameIsUnaffected()
	{
		// The flag is off by default, so a Stardew or Skyrim mod with no link is still unchecked and still
		// worth telling the user about.
		var stray = Mod("Some Mod", "SomeMod");

		var coverage = UpdateCoverage.Classify(new[] { stray }, ModsPath);

		Assert.Equal(UpdateCoverageKind.Unchecked, Entry(coverage, "Some Mod").Kind);
	}
}
