using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace KinetixModManager;

/// <summary>
/// One kind of reason a mod is worth suggesting — a heading the finished list groups under.
///
/// <para>
/// This started as a fixed enum of four and became data so that a curator can add their own. What is stored
/// against a mod is always <see cref="Id"/>, never the words the user hears: the four that ship are translated,
/// any the user adds are typed in their own language, and either can be renamed later. An entry recorded months
/// ago has to keep meaning what it meant when it was recorded, whatever the label says today.
/// </para>
/// </summary>
public class SuggestionCategory
{
	/// <summary>The stable identifier stored against a mod. Never shown to anyone.</summary>
	public string Id { get; set; } = "";

	/// <summary>
	/// The language-file key holding this category's name, for the four that ship with the manager. Empty for
	/// one the user made, whose name is their own words and has nothing to translate.
	/// </summary>
	public string LocKey { get; set; } = "";

	/// <summary>
	/// The name as typed: what a user-made category is called, and what a built-in was renamed to. Empty on a
	/// built-in still using its shipped name, which is what makes renaming reversible — clear this and the
	/// translated name comes back.
	/// </summary>
	public string Label { get; set; } = "";

	/// <summary>
	/// Whether this is one of the four the manager ships. Built-ins can be renamed and reordered but never
	/// deleted: the suggested list that ships is filed under them, so removing one would leave those entries
	/// pointing at a category that no longer exists.
	/// </summary>
	public bool IsBuiltIn { get; set; }
}

/// <summary>
/// The set of categories a curator can file a mod under: the four that ship, plus any they add, in the order
/// they are heard.
///
/// Kept beside the suggestions themselves and, like <see cref="SuggestedModStore"/>, addressed by path rather
/// than reaching for a fixed one, so it carries no dependency on the app's settings and can be tested on its own.
/// </summary>
public static class SuggestionCategoryStore
{
	/// <summary>The mechanic cannot be done by ear at all — a timing minigame, a visual puzzle.</summary>
	public const string CantByEar = "CantByEar";

	/// <summary>It can be done by ear, but costs several times more time or keystrokes.</summary>
	public const string SlowerByEar = "SlowerByEar";

	/// <summary>A bug fix, an unofficial patch, a stability or performance mod. Everyone benefits.</summary>
	public const string BugFix = "BugFix";

	/// <summary>
	/// It makes the game easier as well as more reachable — worth saying out loud, so a player who would rather
	/// keep the challenge can pass it by.
	/// </summary>
	public const string Easier = "Easier";

	/// <summary>
	/// The built-in ids in their shipped order, which is also the order the retired enum used.
	///
	/// That second fact is load-bearing: entries written before categories became data stored the enum's NUMBER,
	/// so position here is what turns a stored <c>3</c> back into <see cref="Easier"/>. Never reorder this array
	/// — the displayed order is the order of the stored list, which the user can change freely.
	/// </summary>
	public static readonly string[] BuiltInIds = { CantByEar, SlowerByEar, BugFix, Easier };

	/// <summary>The four categories the manager ships, in their shipped order.</summary>
	public static List<SuggestionCategory> Defaults() =>
		BuiltInIds.Select(id => new SuggestionCategory
		{
			Id = id,
			LocKey = "curator.category" + id,
			IsBuiltIn = true
		}).ToList();

	/// <summary>
	/// Loads the categories in their kept order. A missing, empty or unreadable file gives the shipped four,
	/// which is also what a first run sees.
	/// </summary>
	public static List<SuggestionCategory> Load(string path)
	{
		try
		{
			if (!File.Exists(path)) return Defaults();
			var stored = JsonConvert.DeserializeObject<List<SuggestionCategory>>(File.ReadAllText(path));
			return Normalize(stored);
		}
		catch { return Defaults(); }
	}

	/// <summary>Writes the categories, creating the folder if it isn't there yet.</summary>
	public static void Save(string path, IEnumerable<SuggestionCategory> categories)
	{
		string? folder = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
		File.WriteAllText(path, JsonConvert.SerializeObject(Normalize(categories.ToList()), Formatting.Indented));
	}

	/// <summary>
	/// Makes a stored list safe to use: drops entries with no id, removes duplicates, and puts back any built-in
	/// that has gone missing.
	///
	/// The last part is the point. Built-ins cannot be deleted through the UI, but a file can be hand-edited or
	/// half-written, and a suggestion filed under a category that no longer exists has nowhere to appear in the
	/// finished list. Restoring it is always better than losing the entries behind it.
	/// </summary>
	public static List<SuggestionCategory> Normalize(List<SuggestionCategory>? categories)
	{
		var result = new List<SuggestionCategory>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (SuggestionCategory category in categories ?? new List<SuggestionCategory>())
		{
			if (category == null || string.IsNullOrWhiteSpace(category.Id)) continue;
			category.Id = category.Id.Trim();
			if (!seen.Add(category.Id)) continue;

			// A built-in is a built-in whatever the file says, so a hand-edited flag cannot make one deletable
			// or turn a user's category into one.
			category.IsBuiltIn = BuiltInIds.Contains(category.Id, StringComparer.OrdinalIgnoreCase);
			if (category.IsBuiltIn) category.LocKey = "curator.category" + category.Id;

			result.Add(category);
		}

		foreach (SuggestionCategory missing in Defaults())
			if (!seen.Contains(missing.Id))
				result.Add(missing);

		return result;
	}

	/// <summary>The category with this id, or <c>null</c>.</summary>
	public static SuggestionCategory? Find(IEnumerable<SuggestionCategory> categories, string? id) =>
		string.IsNullOrWhiteSpace(id)
			? null
			: categories.FirstOrDefault(c => string.Equals(c.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Turns a name typed by the user into an id that will not collide with an existing one.
	///
	/// Derived from the name rather than random so a hand-read file still makes sense, but the name itself is
	/// free to change afterwards without the id following it.
	/// </summary>
	public static string NewId(IEnumerable<SuggestionCategory> existing, string label)
	{
		string root = new string((label ?? "").Where(char.IsLetterOrDigit).ToArray());
		if (root.Length == 0) root = "Category";

		var taken = new HashSet<string>(existing.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
		if (!taken.Contains(root)) return root;

		for (int n = 2; ; n++)
			if (!taken.Contains(root + n)) return root + n;
	}
}

/// <summary>
/// One mod marked as worth suggesting to other players, recorded while browsing rather than written by hand.
///
/// The manager already ships a curated per-game list — the accessibility suite in <c>Form1.SuiteDialog.cs</c> —
/// but that one answers "what does this game need before it can be played at all". This answers a different
/// question: which mods remove a wall that is still there once the game is playable. The two are deliberately
/// separate lists with separate promises.
/// </summary>
public class SuggestedMod
{
	/// <summary>
	/// The game, as a bare game id — never an install key.
	///
	/// A suggestion is about the game, not about which copy of it happened to be open: someone running Skyrim
	/// from GOG wants the same mods as someone running it from Steam. <see cref="SuggestedModStore.Upsert"/>
	/// normalises this through <see cref="GameProfiles.BaseId"/> so the two copies cannot record the mod twice.
	/// </summary>
	public string Game { get; set; } = "";

	/// <summary>The mod's name as it was shown when it was marked.</summary>
	public string Name { get; set; } = "";

	/// <summary>The mod's author, kept only to tell two similarly-named mods apart later.</summary>
	public string Author { get; set; } = "";

	/// <summary>The Nexus numeric mod id, or <c>null</c> when the mod was marked without one.</summary>
	public string? NexusId { get; set; }

	/// <summary>The GitHub <c>owner/repo</c>, or <c>null</c>. A few access-adjacent mods live only there.</summary>
	public string? GitHubRepo { get; set; }

	/// <summary>
	/// The manifest UniqueID when the mod was marked from the installed list, otherwise <c>""</c>.
	///
	/// Not every installed mod carries a Nexus id — matching one up is exactly what Auto Match exists to do, and
	/// plenty of mods are still unmatched when they prove themselves worth suggesting. Recording whatever
	/// identity the mod does have keeps the capture rather than refusing it.
	/// </summary>
	public string UniqueId { get; set; } = "";

	/// <summary>
	/// Which category this is filed under: a <see cref="SuggestionCategory.Id"/>, not a name anyone sees.
	///
	/// Entries written before categories became data hold the retired enum's number here instead
	/// (<c>"Category": 3</c>). <see cref="SuggestedModStore.NormalizeCategoryId"/> converts those as they are
	/// read, so an existing file upgrades itself the first time it is opened and nothing needs re-marking.
	/// </summary>
	public string Category { get; set; } = SuggestionCategoryStore.CantByEar;

	/// <summary>One line saying what the wall actually is. May be empty if it was skipped at capture time.</summary>
	public string Reason { get; set; } = "";

	/// <summary>When this was marked, in UTC.</summary>
	public DateTime MarkedUtc { get; set; }

	/// <summary>
	/// What makes this the same suggestion as another: the strongest identity the mod had when it was marked.
	///
	/// A Nexus id is preferred because it survives the mod being renamed, and it is the thing the eventual
	/// installer will act on. The fallbacks matter because a mod marked from the installed list may have no
	/// Nexus id at all, and one marked from a search result has no UniqueId — so neither field alone can be the
	/// key without letting the same mod be recorded twice.
	///
	/// Not written to the file: it is worked out from the fields beside it, so storing it would put a second,
	/// staler copy of the same fact into a file meant to be shared and read by hand — one that somebody could
	/// edit and reasonably expect to matter.
	/// </summary>
	[JsonIgnore]
	public string Identity =>
		!string.IsNullOrWhiteSpace(NexusId)    ? "nexus:"  + NexusId.Trim() :
		!string.IsNullOrWhiteSpace(GitHubRepo) ? "github:" + GitHubRepo.Trim().ToLowerInvariant() :
		!string.IsNullOrWhiteSpace(UniqueId)   ? "unique:" + UniqueId.Trim().ToLowerInvariant() :
		"name:" + Name.Trim().ToLowerInvariant();
}

/// <summary>
/// Reads and writes the curator's list of suggested mods.
///
/// Every method takes the file path rather than reaching for a fixed one. That keeps this file free of any
/// dependency on the app's settings — which is what lets the test project compile it on its own, without
/// dragging WinForms and the rest of the runtime in behind it — and it lets the behaviour be tested against a
/// temporary file. The app's own path lives with the curator UI, in <c>Form1.Curator.cs</c>.
/// </summary>
public static class SuggestedModStore
{
	/// <summary>
	/// Loads the marked mods, ordered for reading: by game, then by name.
	///
	/// A missing file is an empty list, and so is an unreadable one. This is a developer's notebook, not the
	/// user's data — refusing to open the review list because the file got truncated would leave no way to see
	/// what survived, which is the one moment the list is actually wanted.
	/// </summary>
	public static List<SuggestedMod> Load(string path)
	{
		try
		{
			if (!File.Exists(path)) return new List<SuggestedMod>();
			var entries = JsonConvert.DeserializeObject<List<SuggestedMod>>(File.ReadAllText(path));
			foreach (SuggestedMod entry in entries ?? new List<SuggestedMod>())
				entry.Category = NormalizeCategoryId(entry.Category);

			return (entries ?? new List<SuggestedMod>())
				.OrderBy(e => e.Game, StringComparer.OrdinalIgnoreCase)
				.ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
		catch { return new List<SuggestedMod>(); }
	}

	/// <summary>
	/// The category id for a stored value, converting the ones written before categories became data.
	///
	/// Those held the retired enum's number, which arrives here as <c>"0"</c> to <c>"3"</c> because a JSON number
	/// read into a string property comes through as its digits. Position in
	/// <see cref="SuggestionCategoryStore.BuiltInIds"/> is what maps it back, which is why that array's order is
	/// fixed. Anything unrecognised falls back to the first built-in rather than being dropped: a suggestion
	/// filed under nothing would vanish from the finished list, and losing the entry is worse than filing it in
	/// the wrong place, which the marked-mods list can correct in a keypress.
	/// </summary>
	public static string NormalizeCategoryId(string? stored)
	{
		if (string.IsNullOrWhiteSpace(stored)) return SuggestionCategoryStore.BuiltInIds[0];

		string trimmed = stored.Trim();
		if (!int.TryParse(trimmed, out int legacyIndex)) return trimmed;

		return legacyIndex >= 0 && legacyIndex < SuggestionCategoryStore.BuiltInIds.Length
			? SuggestionCategoryStore.BuiltInIds[legacyIndex]
			: SuggestionCategoryStore.BuiltInIds[0];
	}

	/// <summary>
	/// The suggestions as a reader should see them: the list that ships with the manager, with the user's own
	/// entries laid over the top.
	///
	/// <para>
	/// Two files rather than one, because they have different lifetimes. The shipped list is replaced wholesale
	/// by the next release; anything the user marked or imported lives in their own file and must survive that.
	/// Merging at the point of reading is what lets both be true — nothing is ever copied from one into the
	/// other, so an update cannot eat a curator's work and a curator cannot pin an outdated copy of the shipped
	/// list in place.
	/// </para>
	///
	/// <para>
	/// Where both describe the same mod, the user's entry wins outright: they either marked it themselves or
	/// chose to import it, and either way it is a more recent opinion than the one that came in the box.
	/// </para>
	/// </summary>
	public static List<SuggestedMod> Merge(IEnumerable<SuggestedMod> shipped, IEnumerable<SuggestedMod> mine)
	{
		var result = new List<SuggestedMod>();
		var ours = mine.ToList();

		foreach (SuggestedMod entry in shipped)
			if (!ours.Any(m => Matches(m, entry)))
				result.Add(entry);

		result.AddRange(ours);

		return result
			.OrderBy(e => e.Game, StringComparer.OrdinalIgnoreCase)
			.ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>
	/// Re-files every suggestion in <paramref name="fromCategoryId"/> under <paramref name="toCategoryId"/>,
	/// returning how many moved. This is what lets a category in use be deleted without orphaning its mods.
	/// </summary>
	public static int Reassign(string path, string fromCategoryId, string toCategoryId)
	{
		var entries = Load(path);
		var moved = entries
			.Where(e => string.Equals(e.Category, fromCategoryId, StringComparison.OrdinalIgnoreCase))
			.ToList();

		if (moved.Count == 0) return 0;

		foreach (SuggestedMod entry in moved) entry.Category = toCategoryId;
		Save(path, entries);
		return moved.Count;
	}

	/// <summary>Writes the list, creating the folder if it isn't there yet.</summary>
	public static void Save(string path, IEnumerable<SuggestedMod> entries)
	{
		string? folder = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
		File.WriteAllText(path, JsonConvert.SerializeObject(entries, Formatting.Indented));
	}

	/// <summary>
	/// Records <paramref name="entry"/>, replacing any earlier entry for the same mod, and returns <c>true</c>
	/// when it replaced one rather than adding it.
	///
	/// Marking a mod that is already marked is an edit, not a second copy — it is how a reason gets improved on
	/// the second playthrough. The caller uses the return value to say "Updated" instead of "Marked", so the
	/// difference is audible.
	/// </summary>
	public static bool Upsert(string path, SuggestedMod entry)
	{
		entry.Game = GameProfiles.BaseId(entry.Game);
		entry.MarkedUtc = DateTime.UtcNow;

		var entries = Load(path);
		int at = entries.FindIndex(e => Matches(e, entry));
		bool replaced = at >= 0;

		if (replaced) entries[at] = entry;
		else entries.Add(entry);

		Save(path, entries);
		return replaced;
	}

	/// <summary>Removes the entry matching <paramref name="entry"/>, returning <c>true</c> if one went.</summary>
	public static bool Remove(string path, SuggestedMod entry)
	{
		var entries = Load(path);
		int removed = entries.RemoveAll(e => Matches(e, entry));
		if (removed == 0) return false;

		Save(path, entries);
		return true;
	}

	/// <summary>Finds the stored entry for a mod, or <c>null</c> — what tells a capture whether it is an edit.</summary>
	public static SuggestedMod? Find(string path, string game, SuggestedMod probe)
	{
		probe.Game = GameProfiles.BaseId(game);
		return Load(path).FirstOrDefault(e => Matches(e, probe));
	}

	/// <summary>
	/// Two entries are the same suggestion when they are the same mod for the same game. Public because importing
	/// a shared list asks exactly this question of every entry in it — see <see cref="SuggestionSharing"/>.
	/// </summary>
	public static bool IsSameMod(SuggestedMod a, SuggestedMod b) => Matches(a, b);

	/// <summary>Two entries are the same suggestion when they are the same mod for the same game.</summary>
	private static bool Matches(SuggestedMod a, SuggestedMod b) =>
		string.Equals(GameProfiles.BaseId(a.Game), GameProfiles.BaseId(b.Game), StringComparison.Ordinal) &&
		string.Equals(a.Identity, b.Identity, StringComparison.Ordinal);
}
