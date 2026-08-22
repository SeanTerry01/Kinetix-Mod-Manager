using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Guards the Suggested Mods list that ships with the manager — <c>data\suggested-mods.json</c>.
///
/// <para>
/// That file is not written by hand. It is produced by <c>tools\update-suggested-defaults.ps1</c> from the
/// curator's own marks, which makes it the one shipped file whose contents come out of somebody's AppData
/// rather than out of the repository. Everything that can go wrong with it goes wrong quietly: the viewer
/// catches a parse failure and shows an empty list, an entry filed under a category the file forgot to carry
/// appears under nothing, and an entry with no Nexus id or GitHub repo cannot be installed from the list at all.
/// None of that throws, and none of it is visible without opening the viewer for the right game.
/// </para>
///
/// <para>
/// So the checks here are the ones the generator warns about, made binding at build time — the point being that
/// regenerating the list and committing it is safe to do without reading the JSON.
/// </para>
/// </summary>
public class ShippedSuggestionsTests
{
	private static string ShippedPath =>
		Path.Combine(RepoRoot(), "KinetixModManager", "data", "suggested-mods.json");

	private static SuggestionList Shipped()
	{
		Assert.True(File.Exists(ShippedPath),
			$"The shipped suggestions list is missing from {ShippedPath}. Regenerate it with tools\\update-suggested-defaults.ps1.");
		return SuggestionSharing.Parse(File.ReadAllText(ShippedPath));
	}

	[Fact]
	public void TheShippedListParsesAndIsNotEmpty()
	{
		SuggestionList list = Shipped();

		Assert.NotEmpty(list.Entries);
		Assert.False(string.IsNullOrWhiteSpace(list.Name), "the shipped list should say what it is");
	}

	/// <summary>An entry for a game the manager does not know would never be shown to anybody.</summary>
	[Fact]
	public void EverySuggestionIsForAGameTheManagerSupports()
	{
		foreach (SuggestedMod entry in Shipped().Entries)
			Assert.True(GameProfiles.Find(entry.Game) != null,
				$"'{entry.Name}' is filed under unknown game '{entry.Game}'");
	}

	/// <summary>The reason line is the whole point of the list — it is what lets a player decline one on purpose.</summary>
	[Fact]
	public void EverySuggestionSaysWhyItIsThere()
	{
		foreach (SuggestedMod entry in Shipped().Entries)
		{
			Assert.False(string.IsNullOrWhiteSpace(entry.Name), $"an entry for {entry.Game} has no name");
			Assert.False(string.IsNullOrWhiteSpace(entry.Reason), $"'{entry.Name}' ({entry.Game}) has no reason written");
		}
	}

	/// <summary>Without an id there is nothing for the list to open or install, so the entry is only a name.</summary>
	[Fact]
	public void EverySuggestionCanActuallyBeReached()
	{
		foreach (SuggestedMod entry in Shipped().Entries)
			Assert.True(
				!string.IsNullOrWhiteSpace(entry.NexusId) || !string.IsNullOrWhiteSpace(entry.GitHubRepo),
				$"'{entry.Name}' ({entry.Game}) has neither a Nexus id nor a GitHub repo");
	}

	/// <summary>
	/// A shared list carries the categories its entries are filed under, because the recipient may never have
	/// heard of them. An entry pointing at a category the file did not bring arrives filed under nothing.
	/// </summary>
	[Fact]
	public void EveryCategoryUsedIsOneTheFileCarries()
	{
		SuggestionList list = Shipped();
		var known = new HashSet<string>(
			list.Categories.Select(c => c.Id).Concat(SuggestionCategoryStore.BuiltInIds),
			StringComparer.OrdinalIgnoreCase);

		foreach (SuggestedMod entry in list.Entries)
			Assert.True(known.Contains(entry.Category),
				$"'{entry.Name}' is filed under category '{entry.Category}', which the shipped file does not define");
	}

	/// <summary>
	/// The same mod listed twice for one game shows up twice in the viewer. It is an easy thing to end up with,
	/// because a mod can be marked from the installed list and again from a search.
	/// </summary>
	[Fact]
	public void NoModIsSuggestedTwiceForTheSameGame()
	{
		var duplicates = Shipped().Entries
			.Where(e => !string.IsNullOrWhiteSpace(e.NexusId))
			.GroupBy(e => $"{GameProfiles.BaseId(e.Game)}|{e.NexusId}", StringComparer.OrdinalIgnoreCase)
			.Where(g => g.Count() > 1)
			.Select(g => g.Key)
			.ToList();

		Assert.True(duplicates.Count == 0, "listed more than once: " + string.Join(", ", duplicates));
	}

	/// <summary>
	/// The generator writes UTF-8 with no byte-order mark. A BOM on a JSON file is the kind of thing that parses
	/// everywhere until it doesn't, and the manager reads this with File.ReadAllText.
	/// </summary>
	[Fact]
	public void TheShippedListHasNoByteOrderMark()
	{
		byte[] head = File.ReadAllBytes(ShippedPath).Take(3).ToArray();

		Assert.False(head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF,
			"the shipped suggestions file starts with a UTF-8 BOM");
	}

	private static string RepoRoot()
	{
		var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MANUAL.md"))) dir = dir.Parent;
		return dir?.FullName ?? Directory.GetCurrentDirectory();
	}
}
