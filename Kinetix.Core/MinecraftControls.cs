using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>One thing the player can do, and the key that does it. Both already readable aloud.</summary>
public sealed class MinecraftBinding
{
	/// <summary>
	/// The raw identifier the file used — <c>key.pickItem</c>, <c>build_cursor_left</c>. Kept because it is
	/// what the grouping reads: the friendly name has already thrown away the prefix that says which part of
	/// the game a binding belongs to.
	/// </summary>
	public required string Id { get; init; }

	/// <summary>What it does, e.g. "Attack", "Narrate coordinates".</summary>
	public required string Action { get; init; }

	/// <summary>The key or combination, e.g. "W", "Left Shift", "Shift plus C", or "Not bound".</summary>
	public required string Key { get; init; }

	/// <summary>True when nothing is bound to this action, so a viewer can group or skip them.</summary>
	public bool IsUnbound { get; init; }

	/// <summary>
	/// True when this is bound to a mouse button rather than a key.
	///
	/// Worth separating out. A player who cannot see the screen is unlikely to be using the mouse, so "these
	/// three actions are on mouse buttons" is exactly the list they need in order to know what to rebind. It
	/// follows the actual binding rather than the action, so rebinding Attack to a key moves it out of the
	/// mouse section, which is the truthful answer.
	/// </summary>
	public bool IsMouse { get; init; }
}

/// <summary>
/// Minecraft's keyboard controls, read straight off disk.
///
/// <para>
/// Unusually easy compared with the other games. Moonlight Peaks resolves its bindings at runtime through
/// Rewired and keeps nothing readable, so the manager ships a plugin to export them; Minecraft writes both
/// halves out in plain text as a matter of course. The vanilla bindings are <c>key_</c> lines in
/// <c>options.txt</c>, and United Minecraft writes its own to
/// <c>config\united_minecraft_keybinds.json</c>. No plugin, and the player's own remaps are included for free
/// because these ARE the files the game reads.
/// </para>
/// </summary>
public static class MinecraftControls
{
	/// <summary>
	/// United Minecraft's keybind file, relative to the <c>.minecraft</c> root.
	///
	/// <c>static readonly</c> rather than <c>const</c> because Path.Combine is what spells the separator,
	/// and Minecraft is the one supported game that genuinely runs on Linux — where the separator is not a
	/// backslash and a literal one would be read as part of the file name.
	/// </summary>
	public static readonly string UnitedMinecraftKeybindsFile =
		Path.Combine("config", "united_minecraft_keybinds.json");

	// -------------------------------------------------------------------------
	// Vanilla: options.txt
	// -------------------------------------------------------------------------

	/// <summary>
	/// The game's own bindings from <c>options.txt</c>, whose lines read
	/// <c>key_key.attack:key.mouse.left</c> — the setting name is <c>key_</c> plus the action's translation
	/// key, and the value is the bound input's translation key.
	/// </summary>
	public static List<MinecraftBinding> ReadVanillaBindings(string optionsTxtPath)
	{
		var bindings = new List<MinecraftBinding>();
		if (!File.Exists(optionsTxtPath)) return bindings;

		string[] lines;
		try { lines = File.ReadAllLines(optionsTxtPath); }
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", "reading the game's options.txt for its controls", ex);
			return bindings;
		}

		foreach (string line in lines)
		{
			if (!line.StartsWith("key_", StringComparison.Ordinal)) continue;

			int colon = line.IndexOf(':');
			if (colon < 0) continue;

			string action = line.Substring("key_".Length, colon - "key_".Length);
			string input = line.Substring(colon + 1).Trim();

			bindings.Add(new MinecraftBinding
			{
				Id        = action,
				Action    = FriendlyActionName(action),
				Key       = FriendlyInputName(input),
				IsUnbound = IsUnbound(input),
				IsMouse   = input.StartsWith("key.mouse.", StringComparison.Ordinal)
			});
		}

		return bindings;
	}

	/// <summary>True for the value Minecraft writes when an action has no key.</summary>
	public static bool IsUnbound(string input) =>
		input.Equals("key.keyboard.unknown", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Turns an action's translation key into words — <c>key.pickItem</c> gives "Pick item",
	/// <c>key.hotbar.1</c> gives "Hotbar 1".
	///
	/// Derived rather than translated. Minecraft's real display names live in the game's own language files
	/// inside the jar, and reading those to gain "Pick Block" over "Pick item" would be a great deal of
	/// machinery for a small difference in wording. What matters is that it is a phrase and not an identifier.
	/// </summary>
	public static string FriendlyActionName(string translationKey)
	{
		string name = translationKey;
		foreach (string prefix in new[] { "key.", "keybind." })
			if (name.StartsWith(prefix, StringComparison.Ordinal)) name = name.Substring(prefix.Length);

		name = name.Replace('.', ' ').Replace('_', ' ');
		return Humanise(name);
	}

	/// <summary>
	/// Turns an input's translation key into something sayable — <c>key.keyboard.left.shift</c> gives
	/// "Left Shift", <c>key.mouse.left</c> gives "Left mouse button".
	/// </summary>
	public static string FriendlyInputName(string input)
	{
		if (IsUnbound(input)) return "Not bound";

		if (input.StartsWith("key.mouse.", StringComparison.Ordinal))
		{
			string button = input.Substring("key.mouse.".Length);
			return button switch
			{
				"left"   => "Left mouse button",
				"right"  => "Right mouse button",
				"middle" => "Middle mouse button",
				_        => "Mouse button " + button
			};
		}

		if (input.StartsWith("key.keyboard.", StringComparison.Ordinal))
		{
			string key = input.Substring("key.keyboard.".Length).Replace('.', ' ');
			return Humanise(key);
		}

		return Humanise(input.Replace('.', ' '));
	}

	// -------------------------------------------------------------------------
	// United Minecraft: config\united_minecraft_keybinds.json
	// -------------------------------------------------------------------------

	/// <summary>
	/// The accessibility mod's own bindings, e.g. <c>{ "narrate_health": { "key": 72, "modifiers": 0 } }</c>.
	///
	/// <c>key</c> is a GLFW key code — letters and digits are their ASCII values — and <c>modifiers</c> is
	/// GLFW's bitmask. A key of <c>-1</c> means the action has been left unbound, which several are by default.
	/// </summary>
	public static List<MinecraftBinding> ReadUnitedMinecraftBindings(string keybindsJsonPath)
	{
		var bindings = new List<MinecraftBinding>();
		if (!File.Exists(keybindsJsonPath)) return bindings;

		JObject doc;
		try { doc = JObject.Parse(File.ReadAllText(keybindsJsonPath)); }
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", "reading United Minecraft's keybinds file", ex);
			return bindings;
		}

		foreach (JProperty entry in doc.Properties())
		{
			int key = (int?)entry.Value["key"] ?? -1;
			int modifiers = (int?)entry.Value["modifiers"] ?? 0;

			bindings.Add(new MinecraftBinding
			{
				Id        = entry.Name,
				Action    = FriendlyActionName(entry.Name),
				Key       = DescribeGlfwCombo(key, modifiers),
				IsUnbound = key < 0
			});
		}

		return bindings;
	}

	/// <summary>
	/// A GLFW key plus its modifiers, said the way the controls viewer says every other game's — modifiers
	/// first, joined with "plus" so a screen reader does not read a "+" as punctuation mid-phrase.
	/// </summary>
	public static string DescribeGlfwCombo(int key, int modifiers)
	{
		if (key < 0) return "Not bound";

		var parts = new List<string>();
		// GLFW's own bit values: SHIFT 1, CONTROL 2, ALT 4, SUPER 8.
		if ((modifiers & 1) != 0) parts.Add("Shift");
		if ((modifiers & 2) != 0) parts.Add("Control");
		if ((modifiers & 4) != 0) parts.Add("Alt");
		if ((modifiers & 8) != 0) parts.Add("Windows");

		parts.Add(DescribeGlfwKey(key));
		return string.Join(" plus ", parts);
	}

	/// <summary>The name of one GLFW key code.</summary>
	public static string DescribeGlfwKey(int key)
	{
		if (key < 0) return "Not bound";

		// Letters and digits are their ASCII codes, which is most of what a mod ever binds.
		if (key >= 'A' && key <= 'Z') return ((char)key).ToString();
		if (key >= '0' && key <= '9') return ((char)key).ToString();

		return key switch
		{
			32  => "Space",
			256 => "Escape",
			257 => "Enter",
			258 => "Tab",
			259 => "Backspace",
			260 => "Insert",
			261 => "Delete",
			262 => "Right arrow",
			263 => "Left arrow",
			264 => "Down arrow",
			265 => "Up arrow",
			266 => "Page up",
			267 => "Page down",
			268 => "Home",
			269 => "End",
			39  => "Apostrophe",
			44  => "Comma",
			45  => "Minus",
			46  => "Period",
			47  => "Slash",
			59  => "Semicolon",
			61  => "Equals",
			91  => "Left bracket",
			92  => "Backslash",
			93  => "Right bracket",
			96  => "Grave accent",
			280 => "Caps lock",
			281 => "Scroll lock",
			282 => "Num lock",
			283 => "Print screen",
			284 => "Pause",
			// The modifier keys, which a mod may bind as ordinary keys rather than as modifiers. United
			// Minecraft binds Right control to place a block and Right shift to break one, and without these
			// the list read "Key 345: Build place" — a number nobody can act on.
			330 => "Numpad decimal",
			331 => "Numpad divide",
			332 => "Numpad multiply",
			333 => "Numpad subtract",
			334 => "Numpad add",
			335 => "Numpad enter",
			336 => "Numpad equals",
			340 => "Left shift",
			341 => "Left control",
			342 => "Left alt",
			343 => "Left Windows",
			344 => "Right shift",
			345 => "Right control",
			346 => "Right alt",
			347 => "Right Windows",
			348 => "Menu",
			>= 290 and <= 301 => "F" + (key - 289).ToString(CultureInfo.InvariantCulture),
			>= 320 and <= 329 => "Numpad " + (key - 320).ToString(CultureInfo.InvariantCulture),
			_ => "Key " + key.ToString(CultureInfo.InvariantCulture)
		};
	}

	// -------------------------------------------------------------------------
	// Grouping
	// -------------------------------------------------------------------------

	/// <summary>
	/// Which part of the game a group of bindings belongs to, and the bindings themselves.
	///
	/// Sections exist because the same key legitimately does several different things. United Minecraft binds
	/// Left arrow to <c>look_left</c>, <c>container_nav_left</c> and <c>build_cursor_left</c>, because each
	/// applies on a different screen or in a different mode. Listed flat, that reads as four contradictory
	/// entries for one key and looks like the manager is confused; listed under "Looking around",
	/// "Containers" and "Build mode" it is obvious.
	/// </summary>
	public sealed class MinecraftControlSection
	{
		public required string Name { get; init; }
		public List<MinecraftBinding> Bindings { get; } = new();
	}

	/// <summary>
	/// Where each family of United Minecraft actions belongs, longest prefix first so <c>scanner_</c> is
	/// matched before <c>scan_</c>.
	///
	/// Read off the mod's own naming, which is consistent: the part before the first underscore says which
	/// feature an action belongs to. An action matching nothing here still appears, under "Other" — a mod
	/// update adding a new feature must not make its keys vanish from the list.
	/// </summary>
	/// <summary>
	/// A rule that matches by the INPUT rather than by the action's name: anything bound to a mouse button.
	///
	/// Written as a reserved token in the rules list so the mouse section keeps a declared position among the
	/// others instead of being bolted on at one end. No action id can collide with it.
	/// </summary>
	private const string MouseRule = "<mouse>";

	private static readonly (string Prefix, string Section)[] UnitedSections =
	{
		// The accessibility mod binds only keys today, but if it ever binds a mouse button the same rule
		// applies — a player who is not using the mouse needs to know which actions are on it.
		(MouseRule, "Mouse buttons"),
		("narrate_",     "Narration"),
		("scanner_",     "Scanner"),
		("scan_",        "Scanner"),
		("place_marker", "Scanner"),
		("build_",       "Build mode"),
		("container_",   "Containers and inventory"),
		("recipe_book_", "Recipe book"),
		("creative_",    "Creative inventory"),
		("look_",        "Looking around"),
		("snap_",        "Looking around"),
		("reset_rotation", "Looking around"),
		("trail",        "Trails"),
		("water_",       "Water"),
		("toggle_",      "Modes and toggles"),
		("page_",        "Paging"),
	};

	/// <summary>
	/// Which part of the game each vanilla action belongs to, following the categories Minecraft's own
	/// Controls screen uses.
	///
	/// Matched on the action's translation key, because nothing in <c>options.txt</c> states a category — the
	/// game keeps that in its own code. An unrecognised key lands under "Other" rather than being dropped,
	/// which is what happens to anything Mojang adds after this was written.
	/// </summary>
	private static readonly (string Match, string Section)[] VanillaSections =
	{
		("key.forward",   "Movement"), ("key.back", "Movement"), ("key.left", "Movement"),
		("key.right",     "Movement"), ("key.jump", "Movement"), ("key.sneak", "Movement"),
		("key.sprint",    "Movement"),
		("key.attack",    "Gameplay"), ("key.use", "Gameplay"), ("key.pickItem", "Gameplay"),
		("key.drop",      "Inventory"), ("key.inventory", "Inventory"), ("key.swapOffhand", "Inventory"),
		("key.hotbar.",   "Inventory"), ("key.saveToolbarActivator", "Inventory"),
		("key.loadToolbarActivator", "Inventory"),
		("key.quickActions", "Gameplay"),
		// Matched on what the binding IS rather than what it does — see MouseRule. Positioned here so it reads
		// after the things done with the keyboard and before the debug keys.
		(MouseRule,       "Mouse buttons"),
		("key.chat",      "Multiplayer"), ("key.command", "Multiplayer"), ("key.playerlist", "Multiplayer"),
		("key.socialInteractions", "Multiplayer"), ("key.advancements", "Multiplayer"),
		("key.friends",   "Multiplayer"),
		// Declared last, so it comes after every section somebody actually plays with. Minecraft ships
		// twenty-two of these — a third of all its keys — and they are chunk borders, hitboxes and profiling
		// charts. Grouping them keeps them out of the way without hiding them from anyone who wants them.
		("key.debug.",    "Debug"),
	};

	/// <summary>Groups United Minecraft's bindings by the feature they belong to.</summary>
	public static List<MinecraftControlSection> GroupUnitedMinecraft(IEnumerable<MinecraftBinding> bindings) =>
		Group(bindings, UnitedSections, "Other");

	/// <summary>Groups the game's own bindings the way Minecraft's Controls screen groups them.</summary>
	public static List<MinecraftControlSection> GroupVanilla(IEnumerable<MinecraftBinding> bindings) =>
		Group(bindings, VanillaSections, "Miscellaneous");

	/// <summary>
	/// Buckets bindings by the first rule whose prefix their id starts with.
	///
	/// Sections come out in the order the rules declare them, not alphabetically — "Movement" before
	/// "Inventory" is how a player thinks about them, and how the game's own screen presents them. Within a
	/// section, bound actions precede unbound ones so a list never opens on a run of "Not bound".
	/// </summary>
	private static List<MinecraftControlSection> Group(
		IEnumerable<MinecraftBinding> bindings, (string Prefix, string Section)[] rules, string fallback)
	{
		var sections = new List<MinecraftControlSection>();
		var byName = new Dictionary<string, MinecraftControlSection>(StringComparer.Ordinal);

		MinecraftControlSection SectionFor(string name)
		{
			if (byName.TryGetValue(name, out MinecraftControlSection? existing)) return existing;

			var created = new MinecraftControlSection { Name = name };
			byName[name] = created;
			sections.Add(created);
			return created;
		}

		// Declared order first, so a section's position does not depend on which binding happened to arrive
		// first. Only sections that end up with something in them survive.
		foreach ((_, string name) in rules) SectionFor(name);
		SectionFor(fallback);

		// A rule's position in the array is its reading order, not its matching precedence, and for the mouse
		// rule the two differ deliberately. It has to be tried FIRST — what a binding is beats what it does,
		// or "key.attack" matches Gameplay before anything notices it is on a mouse button — while sitting
		// mid-list so the section reads after the keyboard ones.
		string? mouseSection = rules.FirstOrDefault(r => r.Prefix == MouseRule).Section;

		foreach (MinecraftBinding binding in bindings)
		{
			string name =
				binding.IsMouse && mouseSection is not null
					? mouseSection
					: rules.FirstOrDefault(r => r.Prefix != MouseRule &&
						binding.Id.StartsWith(r.Prefix, StringComparison.OrdinalIgnoreCase)).Section ?? fallback;

			SectionFor(name).Bindings.Add(binding);
		}

		foreach (MinecraftControlSection section in sections)
		{
			var ordered = section.Bindings.Where(b => !b.IsUnbound)
				.Concat(section.Bindings.Where(b => b.IsUnbound)).ToList();
			section.Bindings.Clear();
			section.Bindings.AddRange(ordered);
		}

		return sections.Where(s => s.Bindings.Count > 0).ToList();
	}

	// -------------------------------------------------------------------------

	/// <summary>
	/// Sentence-cases a run of words and splits camelCase, so <c>pickItem</c> reads "Pick item" and
	/// <c>toggle auto crosshair narration</c> reads "Toggle auto crosshair narration".
	/// </summary>
	public static string Humanise(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return "";

		string trimmed = text.Trim();
		var spaced = new StringBuilder();

		for (int i = 0; i < trimmed.Length; i++)
		{
			char c = trimmed[i];

			// A capital following a lower-case letter starts a new word: "pickItem" -> "pick item".
			bool startsWord = char.IsUpper(c) && spaced.Length > 0 && char.IsLower(spaced[spaced.Length - 1]);
			if (startsWord) spaced.Append(' ');

			// The new word is lower-cased so it reads as prose ("Pick item", not "Pick Item") — unless what
			// follows is also upper case, which marks an acronym worth leaving alone: "toggleHUD" is
			// "Toggle HUD", not "Toggle hUD".
			bool isAcronym = startsWord && i + 1 < trimmed.Length && char.IsUpper(trimmed[i + 1]);
			spaced.Append(startsWord && !isAcronym ? char.ToLowerInvariant(c) : c);
		}

		string[] words = spaced.ToString()
			.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (words.Length == 0) return "";

		// First word capitalised, the rest left as they are so "F3" and "GUI" survive.
		string first = words[0];
		words[0] = char.ToUpperInvariant(first[0]) + (first.Length > 1 ? first.Substring(1) : "");

		return string.Join(" ", words).Trim();
	}
}
