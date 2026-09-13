using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// A shareable file of suggested mods: the entries, the categories they are filed under, and enough about where
/// it came from that somebody opening it later knows whose opinion they are reading.
///
/// <para>
/// The categories travel with the entries because they are no longer a fixed set. A list filed under a category
/// its recipient has never heard of would arrive pointing at nothing, so the file carries the definitions and
/// the import adds any that are missing. Without that, half a shared list would quietly fail to appear.
/// </para>
/// </summary>
public class SuggestionList
{
	/// <summary>What the author called this list, e.g. "Sean's Moonlight Peaks picks".</summary>
	public string Name { get; set; } = "";

	/// <summary>Who put it together, as they chose to be known. Free text; may be empty.</summary>
	public string Author { get; set; } = "";

	/// <summary>When it was exported.</summary>
	public DateTime CreatedUtc { get; set; }

	/// <summary>The manager version that wrote it, for working out later what an odd file came from.</summary>
	public string AppVersion { get; set; } = "";

	/// <summary>The categories the entries are filed under.</summary>
	public List<SuggestionCategory> Categories { get; set; } = new();

	/// <summary>The suggestions themselves.</summary>
	public List<SuggestedMod> Entries { get; set; } = new();
}

/// <summary>What to do with a mod that is in both the incoming list and the one already here.</summary>
public enum ImportConflictChoice
{
	/// <summary>Leave the existing entry exactly as it is.</summary>
	KeepMine,

	/// <summary>Replace the existing entry with the incoming one.</summary>
	TakeTheirs
}

/// <summary>
/// One mod that both lists describe, differently — the two versions side by side so a choice can be made with
/// the actual wording in front of the user rather than in the abstract.
/// </summary>
/// <param name="Mine">The entry already here. Its category and reason are what get changed, if anything does.</param>
/// <param name="Theirs">The entry from the incoming file.</param>
public sealed record ImportConflict(SuggestedMod Mine, SuggestedMod Theirs)
{
	/// <summary>Whether the two file the mod under different categories.</summary>
	public bool CategoryDiffers => !SuggestionSharing.SameCategory(Mine, Theirs);

	/// <summary>Whether the two give different reasons.</summary>
	public bool ReasonDiffers => !SuggestionSharing.SameReason(Mine, Theirs);
}

/// <summary>What an import would do, worked out before anything is written.</summary>
/// <param name="Added">Mods not already present, which arrive whatever is chosen.</param>
/// <param name="Conflicting">Mods present already but described differently — the only ones a choice affects.</param>
/// <param name="Identical">Mods already present and already saying the same thing, which are simply skipped.</param>
/// <param name="NewCategories">Categories the incoming file uses that this manager does not have yet.</param>
public sealed record ImportPreview(int Added, int Conflicting, int Identical, int NewCategories)
{
	/// <summary>Whether this import would change anything at all.</summary>
	public bool ChangesSomething => Added > 0 || Conflicting > 0 || NewCategories > 0;
}

/// <summary>
/// Reading, writing and merging shareable suggestion lists.
///
/// Pure, like the stores it sits beside: no file paths of its own and no dependency on the app's settings, so
/// every rule here can be tested directly rather than through a dialog.
/// </summary>
public static class SuggestionSharing
{
	/// <summary>
	/// Reads a shared list, accepting both the wrapped form and the bare array the curator tool wrote before
	/// there was anything to wrap.
	///
	/// <para>
	/// The old form still turns up: it is what every file written before sharing existed looks like, including
	/// the one the curator's own notebook is kept in. Refusing it would mean the manager could not read a file it
	/// wrote itself, so a bare array is read as a nameless list of entries and everything else follows normally.
	/// </para>
	/// </summary>
	public static SuggestionList Parse(string json)
	{
		JToken root = JToken.Parse(json);

		SuggestionList list = root.Type == JTokenType.Array
			? new SuggestionList { Entries = root.ToObject<List<SuggestedMod>>() ?? new List<SuggestedMod>() }
			: root.ToObject<SuggestionList>() ?? new SuggestionList();

		list.Entries ??= new List<SuggestedMod>();
		list.Categories = SuggestionCategoryStore.Normalize(list.Categories);

		foreach (SuggestedMod entry in list.Entries)
			entry.Category = SuggestedModStore.NormalizeCategoryId(entry.Category);

		return list;
	}

	/// <summary>Writes a shared list in the wrapped form.</summary>
	public static string Write(SuggestionList list) =>
		JsonConvert.SerializeObject(list, Formatting.Indented);

	/// <summary>
	/// The categories worth carrying with <paramref name="entries"/>: the ones they are actually filed under.
	///
	/// Sending the whole set would push the author's private headings — and any renaming they have done to the
	/// built-ins — onto everyone who opens the file, for categories the file says nothing about.
	/// </summary>
	public static List<SuggestionCategory> CategoriesUsedBy(
		IEnumerable<SuggestedMod> entries, IEnumerable<SuggestionCategory> categories)
	{
		var used = new HashSet<string>(entries.Select(e => e.Category), StringComparer.OrdinalIgnoreCase);
		return categories.Where(c => used.Contains(c.Id)).ToList();
	}

	/// <summary>
	/// Works out what importing <paramref name="incoming"/> would do, without doing any of it.
	///
	/// This exists so the user is told what they are agreeing to before a file they did not write touches their
	/// own list — and so the "keep mine or take theirs" question is only asked when there is actually a conflict.
	/// </summary>
	public static ImportPreview Preview(
		List<SuggestedMod> mine,
		List<SuggestedMod> incoming,
		List<SuggestionCategory> myCategories,
		List<SuggestionCategory> theirCategories)
	{
		int added = 0, conflicting = 0, identical = 0;

		foreach (SuggestedMod entry in incoming)
		{
			SuggestedMod? existing = mine.FirstOrDefault(m => SuggestedModStore.IsSameMod(m, entry));
			if (existing == null) added++;
			else if (SaysTheSameThing(existing, entry)) identical++;
			else conflicting++;
		}

		var known = new HashSet<string>(myCategories.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
		int newCategories = theirCategories.Count(c => !known.Contains(c.Id));

		return new ImportPreview(added, conflicting, identical, newCategories);
	}

	/// <summary>
	/// The categories after an import: the ones already here, plus any the incoming file brings that are new.
	///
	/// An incoming category never overwrites one of the same id. The recipient may well have renamed it, and
	/// their name for their own list is not something a file they opened gets to change.
	/// </summary>
	public static List<SuggestionCategory> MergeCategories(
		List<SuggestionCategory> mine, List<SuggestionCategory> theirs)
	{
		var result = new List<SuggestionCategory>(mine);
		var known = new HashSet<string>(mine.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);

		foreach (SuggestionCategory category in theirs)
			if (known.Add(category.Id))
				result.Add(category);

		return SuggestionCategoryStore.Normalize(result);
	}

	/// <summary>
	/// The entries after an import. New mods are always added; a mod already here is replaced only when
	/// <paramref name="choice"/> says so.
	///
	/// <para>
	/// Anything arriving under a category that exists nowhere — not here, not in the file — is re-filed under the
	/// first built-in rather than kept as written. An entry pointing at a category that does not exist has no
	/// heading to appear under, so it would be imported and then be invisible, which is worse than being
	/// imported into the wrong group and re-filed in a keypress.
	/// </para>
	/// </summary>
	public static List<SuggestedMod> Apply(
		List<SuggestedMod> mine,
		List<SuggestedMod> incoming,
		List<SuggestionCategory> categoriesAfterImport,
		ImportConflictChoice choice)
	{
		var result = new List<SuggestedMod>(mine);

		foreach (SuggestedMod entry in incoming)
		{
			int at = result.FindIndex(m => SuggestedModStore.IsSameMod(m, entry));
			if (at < 0) result.Add(entry);
			else if (choice == ImportConflictChoice.TakeTheirs) result[at] = entry;
		}

		var known = new HashSet<string>(categoriesAfterImport.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
		foreach (SuggestedMod entry in result)
			if (!known.Contains(entry.Category))
				entry.Category = SuggestionCategoryStore.BuiltInIds[0];

		return result
			.OrderBy(e => e.Game, StringComparer.OrdinalIgnoreCase)
			.ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>
	/// Every mod the two lists disagree about, paired up so each can be shown side by side.
	///
	/// <para>
	/// Returned in the order the incoming list holds them, and only where something actually differs — a mod
	/// present in both saying the same thing is not a disagreement and must not be put in front of the user.
	/// </para>
	/// </summary>
	public static List<ImportConflict> Conflicts(List<SuggestedMod> mine, List<SuggestedMod> incoming)
	{
		var conflicts = new List<ImportConflict>();

		foreach (SuggestedMod theirs in incoming)
		{
			SuggestedMod? existing = mine.FirstOrDefault(m => SuggestedModStore.IsSameMod(m, theirs));
			if (existing == null || SaysTheSameThing(existing, theirs)) continue;
			conflicts.Add(new ImportConflict(existing, theirs));
		}

		return conflicts;
	}

	/// <summary>
	/// Whether two entries for the same mod actually differ in what they say about it. Only the category and the
	/// reason count: a different name or author for the same Nexus id is the mod having been renamed, not a
	/// disagreement worth asking the user about.
	/// </summary>
	private static bool SaysTheSameThing(SuggestedMod a, SuggestedMod b) =>
		SameCategory(a, b) && SameReason(a, b);

	/// <summary>Whether two entries file the mod under the same category.</summary>
	internal static bool SameCategory(SuggestedMod a, SuggestedMod b) =>
		string.Equals(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);

	/// <summary>Whether two entries give the same reason, ignoring case and surrounding space.</summary>
	internal static bool SameReason(SuggestedMod a, SuggestedMod b) =>
		string.Equals(a.Reason.Trim(), b.Reason.Trim(), StringComparison.CurrentCultureIgnoreCase);
}
