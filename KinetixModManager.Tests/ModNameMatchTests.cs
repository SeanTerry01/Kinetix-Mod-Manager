using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ModNameMatch"/> — the last-resort "is this Nexus search result the same mod?" rule used
/// by Auto-match when nothing records where a mod came from. Both directions matter: too strict and mods stay
/// unlinked, too loose and a mod gets pointed at someone else's page, reporting their version and downloading
/// their files. The names here are taken from real installed mods.
/// </summary>
public class ModNameMatchTests
{
    private static GameMod Mod(string name, string author = "") => new() { Name = name, Author = author };

    [Theory]
    [InlineData("[CP] Stoned Valley", "stonedvalley")]
    [InlineData("(CP)Antique crystal lighting fixtures", "antiquecrystallightingfixtures")]
    [InlineData("[JA] Stoned Valley Clothes", "stonedvalleyclothes")]
    [InlineData("[BL] Nature In The Valley", "natureinthevalley")]
    public void Normalize_DropsTheStardewContentPackTypeTag(string name, string expected)
    {
        // "[CP]", "(JA)" and friends say which framework the pack targets; they are never part of the mod's
        // name on Nexus, and leaving them in stopped otherwise-exact matches from matching.
        Assert.Equal(expected, ModNameMatch.Normalize(name));
    }

    [Theory]
    [InlineData("Paul the Optometrist!", "paultheoptometrist")]
    [InlineData("paul-the-optometrist", "paultheoptometrist")]
    [InlineData("  MEGA Furnace  ", "megafurnace")]
    public void Normalize_IgnoresPunctuationCaseAndSpacing(string name, string expected)
    {
        Assert.Equal(expected, ModNameMatch.Normalize(name));
    }

    [Fact]
    public void Normalize_KeepsATagThatIsActuallyPartOfTheName()
    {
        // Only a short leading tag is dropped; a long bracketed phrase is left alone rather than guessed at.
        Assert.Equal("averylongbracketedthingmod", ModNameMatch.Normalize("[A very long bracketed thing] Mod"));
    }

    [Fact]
    public void ExactNameMatch_IsAccepted()
    {
        Assert.True(ModNameMatch.IsConfident(Mod("MEGA Furnace"), Mod("MEGA Furnace")));
    }

    [Fact]
    public void TypeTaggedPackMatchesItsNexusPage()
    {
        Assert.True(ModNameMatch.IsConfident(Mod("[CP]Craftable Battery Pack"), Mod("Craftable Battery Pack")));
    }

    [Fact]
    public void PartialNameMatch_IsAcceptedOnlyWithTheSameAuthor()
    {
        var installed = Mod("Granny's Recipe Box", "DuchessIvy");

        Assert.True(ModNameMatch.IsConfident(installed, Mod("Grannys Recipe Box Expanded", "DuchessIvy")));
        Assert.False(ModNameMatch.IsConfident(installed, Mod("Grannys Recipe Box Expanded", "SomeoneElse")));
    }

    [Fact]
    public void DifferentModsAreNeverMatched()
    {
        Assert.False(ModNameMatch.IsConfident(Mod("MEGA Furnace", "Vechio"), Mod("Mega Storage", "Vechio")));
    }

    [Fact]
    public void UnrelatedResultSharingNoName_IsRejected()
    {
        // The failure that started this: a loose "contains" rule linked mods to whatever came back first.
        Assert.False(ModNameMatch.IsConfident(Mod("Pelican Town Food Truck", "8BitAlien"), Mod("Food", "Someone")));
    }

    [Theory]
    [InlineData("[CP] Stoned Valley", "Stoned Valley")]
    [InlineData("(CP)Antique crystal lighting fixtures", "Antique crystal lighting fixtures")]
    [InlineData("MEGA Furnace", "MEGA Furnace")]
    public void StripTypeTag_GivesTheNameToSearchNexusWith(string name, string expected)
    {
        // Keeps spacing and capitals: this string goes to the Nexus search box, not to a comparison.
        Assert.Equal(expected, ModNameMatch.StripTypeTag(name));
    }

    [Fact]
    public void EmptyNames_AreNeverConfident()
    {
        Assert.False(ModNameMatch.IsConfident(Mod(""), Mod("")));
        Assert.False(ModNameMatch.IsConfident(Mod("[CP]"), Mod("Anything")));
    }

	// -------------------------------------------------------------------------
	// Searching for something the manager only knows an identifier for
	// -------------------------------------------------------------------------

	[Theory]
	// The reported bug, in its own words: "in some cases it is searching for authorname.modname".
	[InlineData("Digus.ProducerFrameworkMod", "Producer Framework Mod")]
	[InlineData("Pathoschild.Automate", "Automate")]
	[InlineData("Sandman53.AbilitiesExperienceBars", "Abilities Experience Bars")]
	[InlineData("Omegasis.AdvancedSaveBackup", "Advanced Save Backup")]
	[InlineData("AaronTaggart.AutoAnimalDoors", "Auto Animal Doors")]
	// No author segment at all: the identifier IS the name, and still needs splitting into words.
	[InlineData("AutoGate", "Auto Gate")]
	public void AnIdentifierIsSearchedForByTheModsNameNotTheWholeIdentifier(string uniqueId, string expected)
	{
		// Nexus has never heard of "Pathoschild.Automate". It has heard of "Automate".
		Assert.Equal(expected, ModNameMatch.SearchTermForIdentifier(uniqueId));
	}

	[Fact]
	public void AnIdentifierWhoseNameSpansSeveralSegmentsIsSearchedForWhole()
	{
		// Taken from a real mods folder. The last segment alone is "fixtures", which is far too general to find
		// the page this belongs to — the name is spread across the segments after the author.
		List<string> aliases = ModNameMatch.SearchAliasesForIdentifier("CocumiT.TQP.crystal.lighting.fixtures");

		Assert.Contains("crystal lighting fixtures", aliases);
	}

	[Fact]
	public void TheWholeIdentifierIsStillOfferedLastRatherThanNotAtAll()
	{
		// Some pages really are titled with the identifier. It just must never be the FIRST thing tried.
		List<string> aliases = ModNameMatch.SearchAliasesForIdentifier("Digus.ProducerFrameworkMod");

		// The identifier is still in there, though as the spaced spelling of itself — two spellings of one name
		// are kept once, and the spaced one is what a name search can actually match.
		Assert.Contains(aliases, a => ModNameMatch.Normalize(a) == ModNameMatch.Normalize("Digus.ProducerFrameworkMod"));
		Assert.NotEqual("Digus.ProducerFrameworkMod", aliases[0]);
	}

	[Fact]
	public void AnIdentifierWithNothingUsableInItFallsBackToItself()
	{
		// Better a poor search than no search: the user can still see what was looked for and correct it.
		Assert.Equal("ab", ModNameMatch.SearchTermForIdentifier("ab"));
		Assert.Equal("", ModNameMatch.SearchTermForIdentifier(null));
	}

	// -------------------------------------------------------------------------
	// Searching for a mod whose folder is named after the download it came in
	// -------------------------------------------------------------------------

	[Fact]
	public void TheDownloadsTailIsNeverSearchedFor()
	{
		// Real folder names from an installed Skyrim setup. Nexus changed its download naming to a spaced form
		// with a datestamp, which the old cleaner could not see at all — so every mod installed since then kept
		// the mod id, the version and the timestamp in its folder name, and that is what got searched for.
		var mod = new GameMod
		{
			Name = "DbMiscFunctions 65410 10.4 2026-08-27T05-37Z L5WQbqhzr",
			FolderPath = @"D:\Mods\DbMiscFunctions 65410 10.4 2026-08-27T05-37Z L5WQbqhzr"
		};

		List<string> aliases = ModNameMatch.SearchAliases(mod);

		Assert.NotEmpty(aliases);
		// Spaced, because two spellings of one name are kept once and Nexus matches names by word.
		Assert.Equal("Db Misc Functions", aliases[0]);
		// The point of the test: not one alias carries any part of the download's tail.
		Assert.DoesNotContain(aliases, a => a.Contains("65410") || a.Contains("2026") || a.Contains("L5WQbqhzr"));
	}

	[Fact]
	public void TheModsOwnNexusIdSplitsATailThatCarriesNoTimestamp()
	{
		// "Carry Weight Modifiers-2176-1-1-3" ends in a version, not a timestamp, so the shape of the tail alone
		// cannot say where the name stops. Knowing the mod id makes the split exact.
		var mod = new GameMod
		{
			Name = "Carry Weight Modifiers-2176-1-1-3",
			NexusID = "2176",
			FolderPath = @"D:\Mods\Carry Weight Modifiers-2176-1-1-3"
		};

		Assert.Equal("Carry Weight Modifiers", ModNameMatch.SearchAliases(mod)[0]);
	}

	[Fact]
	public void ARecordedNameStillLeadsTheSearchAheadOfTheFolder()
	{
		// The best answer, where there is one: what the mod calls itself. The folder is the fallback for a mod
		// with nothing to read, not a replacement for a name that is already right.
		var mod = new GameMod
		{
			Name = "Address Library for SKSE Plugins",
			FolderPath = @"D:\Mods\Address Library All in One (1.7.104.0) v13 32444 13 2026-08-27T15-29Z Ae46W7Fw2"
		};

		List<string> aliases = ModNameMatch.SearchAliases(mod);

		Assert.Equal("Address Library for SKSE Plugins", aliases[0]);
		Assert.DoesNotContain(aliases, a => a.Contains("Ae46W7Fw2") || a.Contains("2026-08-27"));
	}
}

/// <summary>
/// Guards the rule the search paths kept breaking: an identifier is not a name, and must never be handed to a
/// search as though it were.
///
/// A SMAPI <c>UniqueID</c> looks enough like a name to be used as one — <c>Digus.ProducerFrameworkMod</c> — and
/// four separate places had done exactly that. Nexus has never heard of it, so the search came back empty and the
/// user was told the mod could not be found, while its page sat there under the name "Producer Framework Mod".
/// Each of those places is a different feature, which is why this is a rule about the codebase rather than a fix
/// in any one of them.
///
/// Naming the identifier is still right — it is what the mod's manifest asks for, and what the user sees written
/// in a log. It is only searching for it that is wrong.
/// </summary>
public class SearchTermGuardTests
{
	[Fact]
	public void NoSearchIsRunAgainstARawIdentifier()
	{
		var assignment = new Regex(@"(SearchTerm|txtSearch\.Text)\s*=", RegexOptions.Compiled);
		var offenders = new List<string>();

		foreach (string file in Directory.EnumerateFiles(SourceFolder(), "*.cs"))
		{
			string[] lines = File.ReadAllLines(file);
			for (int i = 0; i < lines.Length; i++)
			{
				if (!assignment.IsMatch(lines[i])) continue;

				// The expression can run onto the next few lines — a ternary, an object initialiser — so this
				// reads a small window rather than the single line it started on.
				string window = string.Join(" ", lines.Skip(i).Take(4));
				if (!window.Contains("UniqueId")) continue;
				if (window.Contains("ModNameMatch.")) continue;

				offenders.Add($"{Path.GetFileName(file)}:{i + 1}  {lines[i].Trim()}");
			}
		}

		Assert.True(offenders.Count == 0,
			"A mod's identifier is not its name, and Nexus only knows the name. Search with "
			+ "ModNameMatch.SearchTermForIdentifier(id) instead — the row can still SAY the identifier:"
			+ Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}

	/// <summary>The app's source folder, found from the test binary rather than hard-coded.</summary>
	private static string SourceFolder()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "KinetixModManager")))
			dir = dir.Parent;

		Assert.NotNull(dir);
		return Path.Combine(dir!.FullName, "KinetixModManager");
	}
}
