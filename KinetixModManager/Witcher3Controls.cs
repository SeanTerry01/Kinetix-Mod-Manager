using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>One binding as the game's own file records it: a key, what it does, where that applies, and whose
/// action it is. <see cref="ModName"/> is <c>""</c> for the game's own controls.</summary>
public sealed class Witcher3Binding
{
	public required string Key { get; init; }
	public required string Action { get; init; }

	/// <summary>The raw input context the binding was declared in — <c>Exploration</c>, <c>BASE_Signs</c>.</summary>
	public required string Context { get; init; }

	/// <summary>The mod whose action this is, or <c>""</c> when it is the game's own.</summary>
	public required string ModName { get; init; }
}

/// <summary>One key and everything it does within whatever the caller grouped by.</summary>
public sealed class Witcher3KeyControls
{
	public required string Key { get; init; }
	public List<Witcher3Binding> Bindings { get; } = new();
}

/// <summary>A situation — "On horseback", "In combat" — and the keys that do something in it.</summary>
public sealed class Witcher3Situation
{
	public required string Name { get; init; }
	public List<Witcher3KeyControls> Keys { get; } = new();

	/// <summary>How many controls this situation holds, counting each thing a key does separately.</summary>
	public int Count => Keys.Sum(k => k.Bindings.Count);
}

/// <summary>
/// Turns The Witcher 3's bindings into something that can be read aloud without losing the reader.
///
/// <para>
/// The problem is real and it is the game's, not the reader's. The Witcher 3 declares its bindings once per
/// input context, and it has forty-two of them. Gather those by key alone — which is the obvious thing to do,
/// and what this list did — and the interact key answers with a single sentence seventy-five items long:
/// "E: Bury Body, Place Trophy, Hide In, Dispose Paint, Take Paint Purple…". Every word of that is true and
/// none of it is usable, because the thing that separates one item from the next is the situation you are in,
/// and that was the part thrown away.
/// </para>
///
/// <para>
/// So the same bindings are offered two ways, and the reader picks the question they actually have.
/// <see cref="BySituation"/> answers "what can I press right now?" — the contexts, made readable and merged,
/// with the keys under each. <see cref="ByKey"/> answers "what does this key do?" — every key once, with each
/// thing it does named alongside the situation it applies in. Neither is a summary: both hold every binding.
/// </para>
/// </summary>
public static class Witcher3Controls
{
	/// <summary>
	/// The game's input contexts, in the order a player meets them, mapped to what they would call the
	/// situation. Several contexts fold into one name on purpose: the game splits its interaction bindings
	/// across three contexts and its attacks across six, and those splits are engine bookkeeping rather than
	/// anything a player would recognise.
	/// </summary>
	private static readonly (string Context, string Situation)[] Situations =
	{
		("BASE_CharacterMovement",           "Moving around"),
		("BASE_CharacterMovementWithSprint", "Moving around"),
		("BASE_CameraMovement",              "Looking around"),
		("Exploration",                      "Exploring on foot"),
		("Combat",                           "In combat"),
		("BASE_ALL_ATTACKS",                 "Attacking"),
		("BASE_ATTACKS_NO_LIGHT",            "Attacking"),
		("BASE_ATTACK_HEAVY",                "Attacking"),
		("BASE_ATTACK_LIGHT",                "Attacking"),
		("BASE_SPECIAL_ATTACK_HEAVY",        "Attacking"),
		("BASE_SPECIAL_ATTACK_LIGHT",        "Attacking"),
		("BASE_DRAW_SWORDS",                 "Drawing swords"),
		("BASE_DRAW_SWORDS_KEYBOARD",        "Drawing swords"),
		("BASE_Signs",                       "Casting signs"),
		("BASE_FocusMode",                   "Witcher senses"),
		("BASE_DRINK_POTIONS",               "Drinking potions"),
		("BASE_Items",                       "Using items"),
		("ThrowHold",                        "Aiming a throw"),
		("BASE_Interactions",                "Interacting with things"),
		("BASE_INTERACTIONS_KEYBOARD",       "Interacting with things"),
		("BASE_INTERACTIONS_HORSE",          "Interacting with things"),
		("Horse",                            "On horseback"),
		("Boat",                             "Sailing a boat"),
		("BoatPassenger",                    "Riding in a boat"),
		("Swimming",                         "Swimming"),
		("Diving",                           "Diving"),
		("JumpClimb",                        "Jumping and climbing"),
		("Meditation",                       "Meditating"),
		("LootPopup",                        "Looting"),
		("BASE_PanelsShortcuts",             "Menus and panels"),
		("FastMenu",                         "Menus and panels"),
		("BASE_JOURNAL_AND_QUEST_MANAGE",    "Journal and quests"),
		("BASE_SHOW_RADIAL_MENU",            "Radial menu"),
		("RadialMenu",                       "Radial menu"),
		("Scene",                            "Cutscenes"),
		("ScriptedAction",                   "Cutscenes"),
		("TutorialPopup",                    "Tutorial messages"),
		("Photomode",                        "Photo mode"),
		("Death",                            "After dying"),
		("Combat_Replacer_Ciri",             "Playing as Ciri"),
		("Exploration_Replacer_Ciri",        "Playing as Ciri"),
		("Horse_Replacer_Ciri",              "Playing as Ciri"),
	};

	/// <summary>
	/// Contexts that are not a situation a player is ever in: the engine's own scaffolding and the developers'
	/// debug keys. Left in the list they would be rows that cannot be acted on, named after internals.
	///
	/// The gamepad interaction context goes too, not because it is scaffolding but because every binding in it
	/// is a controller button, and those are dropped before they reach here — it would be an empty heading.
	/// </summary>
	private static readonly HashSet<string> HiddenContexts = new(StringComparer.OrdinalIgnoreCase)
	{
		"EMPTY_CONTEXT", "FakeAxisInput", "InputSettings", "SCENE_IS_STARTING_HACK",
		"BASE_DEBUG", "BASE_INTERACTIONS_PAD",
	};

	/// <summary>
	/// What to call the situation a context describes, or <c>""</c> when it should not be shown at all.
	///
	/// A context this does not know about is not dropped — it is named after itself, tidied into words. A mod
	/// is free to add its own contexts, and a binding no one can find is worse than one under an odd heading.
	/// </summary>
	public static string SituationFor(string context)
	{
		if (string.IsNullOrWhiteSpace(context)) return "";
		if (HiddenContexts.Contains(context)) return "";

		foreach ((string ctx, string situation) in Situations)
			if (ctx.Equals(context, StringComparison.OrdinalIgnoreCase))
				return situation;

		return Witcher3InputSettings.Humanise(context);
	}

	/// <summary>Where a situation sits in the list. Anything not in the table follows the ones that are.</summary>
	private static int SituationRank(string situation)
	{
		for (int i = 0; i < Situations.Length; i++)
			if (Situations[i].Situation.Equals(situation, StringComparison.OrdinalIgnoreCase))
				return i;
		return Situations.Length;
	}

	/// <summary>
	/// A context holding bindings that apply generally rather than in one situation. The game marks them
	/// itself, with a <c>BASE_</c> prefix.
	/// </summary>
	private static bool IsSharedBlock(string context) =>
		context.StartsWith("BASE_", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Every "key does this" that the game declares in one of its shared blocks.
	///
	/// This is the whole reason the list is readable. The Witcher 3 does not reference its shared blocks from
	/// the contexts that use them — it copies them out in full, so all seventy-five interaction bindings are
	/// written again under Exploration, again under Combat, again under Swimming, Diving and both Ciri
	/// contexts. Listing them in each place would say the same seventy-five things six times over and bury
	/// what is actually particular to swimming.
	///
	/// So a binding the game itself calls general is listed once, under the general heading it gave it, and a
	/// situation is left holding only what is true there and nowhere else.
	/// </summary>
	private static HashSet<string> SharedBindings(IEnumerable<Witcher3Binding> bindings)
	{
		var shared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (Witcher3Binding b in bindings)
			if (IsSharedBlock(b.Context) && SituationFor(b.Context).Length > 0)
				shared.Add(b.Key + " " + b.Action);

		return shared;
	}

	/// <summary>
	/// The game's own bindings grouped by the situation they apply in, keys within a situation in the order
	/// they would be looked for.
	///
	/// A binding declared in several contexts that fold to one situation is listed once: the fold is the point
	/// at which "the same thing said twice" and "two different things" become distinguishable.
	/// </summary>
	public static List<Witcher3Situation> BySituation(IEnumerable<Witcher3Binding> bindings)
	{
		var bySituation = new Dictionary<string, Dictionary<string, Witcher3KeyControls>>(StringComparer.OrdinalIgnoreCase);
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		List<Witcher3Binding> all = bindings.ToList();
		HashSet<string> shared = SharedBindings(all);

		foreach (Witcher3Binding b in all)
		{
			string situation = SituationFor(b.Context);
			if (situation.Length == 0) continue;

			// Copied out of a shared block into this context: it is already listed under the general heading the
			// game itself gave it, and what is left here is what makes this situation different from the rest.
			if (!IsSharedBlock(b.Context) && shared.Contains(b.Key + " " + b.Action)) continue;

			if (!seen.Add(situation + " " + b.Key + " " + b.Action)) continue;

			if (!bySituation.TryGetValue(situation, out var keys))
				bySituation[situation] = keys = new Dictionary<string, Witcher3KeyControls>(StringComparer.OrdinalIgnoreCase);
			if (!keys.TryGetValue(b.Key, out Witcher3KeyControls? key))
				keys[b.Key] = key = new Witcher3KeyControls { Key = b.Key };

			key.Bindings.Add(b);
		}

		return bySituation
			.OrderBy(s => SituationRank(s.Key))
			.ThenBy(s => s.Key, StringComparer.CurrentCultureIgnoreCase)
			.Select(s =>
			{
				var situation = new Witcher3Situation { Name = s.Key };
				situation.Keys.AddRange(s.Value.Values
					.OrderBy(k => k.Key, KeyOrder)
					.Select(SortActions));
				return situation;
			})
			.ToList();
	}

	/// <summary>
	/// Every key once, with everything it does under it, each named alongside the situation it applies in.
	///
	/// <para>
	/// One thing a key does is one line, however many contexts the game declares it in. Counting declarations
	/// instead is what turned the interact key into "454 things it does": seventy-five interactions, written
	/// out again in each of the six contexts that inherit them. The key does seventy-five things, and the file
	/// saying so six times does not make it more.
	/// </para>
	/// </summary>
	public static List<Witcher3KeyControls> ByKey(IEnumerable<Witcher3Binding> bindings)
	{
		var keys = new Dictionary<string, Witcher3KeyControls>(StringComparer.OrdinalIgnoreCase);
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// Ordered so a shared block is met before the contexts that copy it: where the same thing is declared
		// in both, the general heading is the one worth naming.
		List<Witcher3Binding> all = bindings
			.Where(b => SituationFor(b.Context).Length > 0)
			.OrderByDescending(b => IsSharedBlock(b.Context))
			.ToList();
		HashSet<string> shared = SharedBindings(all);

		foreach (Witcher3Binding b in all)
		{
			// Something the game calls general is one line whatever copies it; anything else is one line per
			// situation, because there the situation is the difference between two otherwise identical lines.
			bool isShared = shared.Contains(b.Key + " " + b.Action);
			string once = isShared
				? b.Key + " " + b.Action
				: b.Key + " " + SituationFor(b.Context) + " " + b.Action;

			if (!seen.Add(once)) continue;

			if (!keys.TryGetValue(b.Key, out Witcher3KeyControls? key))
				keys[b.Key] = key = new Witcher3KeyControls { Key = b.Key };

			key.Bindings.Add(b);
		}

		return keys.Values.OrderBy(k => k.Key, KeyOrder).Select(SortActions).ToList();
	}

	/// <summary>
	/// One mod's own bindings, each listed once however many contexts it declared them in.
	///
	/// No situations here. A mod's actions are named after what they do and where they do it — a screen-reader
	/// mod's "Keys next" and "History next" are its own panels, not the game's contexts — so the context a
	/// binding was declared in says nothing a player wants, and the mod's own name for the action says it all.
	/// </summary>
	public static List<Witcher3Binding> ForMod(IEnumerable<Witcher3Binding> bindings, string modName) =>
		bindings
			.Where(b => b.ModName.Equals(modName, StringComparison.OrdinalIgnoreCase))
			.GroupBy(b => b.Key + " " + b.Action, StringComparer.OrdinalIgnoreCase)
			.Select(g => g.First())
			.OrderBy(b => b.Key, KeyOrder)
			.ThenBy(b => b.Action, StringComparer.CurrentCultureIgnoreCase)
			.ToList();

	/// <summary>The mods that own at least one binding, in the order they are first met.</summary>
	public static List<string> ModsWithBindings(IEnumerable<Witcher3Binding> bindings)
	{
		var names = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (Witcher3Binding b in bindings)
			if (b.ModName.Length > 0 && seen.Add(b.ModName)) names.Add(b.ModName);

		return names;
	}

	/// <summary>Puts a key's actions in situation order, so the ones that apply together are read together.</summary>
	private static Witcher3KeyControls SortActions(Witcher3KeyControls key)
	{
		List<Witcher3Binding> sorted = key.Bindings
			.OrderBy(b => SituationRank(SituationFor(b.Context)))
			.ThenBy(b => b.Action, StringComparer.CurrentCultureIgnoreCase)
			.ToList();

		var result = new Witcher3KeyControls { Key = key.Key };
		result.Bindings.AddRange(sorted);
		return result;
	}

	/// <summary>
	/// Key order for a list someone finds their way around by typing the first letter: alphabetical by the name
	/// that is read out, with runs of digits compared as numbers so F2 comes before F10 rather than after it.
	/// </summary>
	private static readonly IComparer<string> KeyOrder = new NaturalKeyComparer();

	private sealed class NaturalKeyComparer : IComparer<string>
	{
		public int Compare(string? x, string? y)
		{
			string a = x ?? "", b = y ?? "";
			int i = 0, j = 0;

			while (i < a.Length && j < b.Length)
			{
				if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
				{
					int si = i, sj = j;
					while (i < a.Length && char.IsDigit(a[i])) i++;
					while (j < b.Length && char.IsDigit(b[j])) j++;

					// Compared as numbers, so "F2" sorts before "F10". Parsed rather than length-compared so
					// that leading zeros don't change the answer.
					long na = long.Parse(a.Substring(si, i - si));
					long nb = long.Parse(b.Substring(sj, j - sj));
					if (na != nb) return na < nb ? -1 : 1;
					continue;
				}

				int cmp = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
				if (cmp != 0) return cmp;
				i++;
				j++;
			}

			return (a.Length - i).CompareTo(b.Length - j);
		}
	}
}
