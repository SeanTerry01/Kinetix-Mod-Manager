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
	/// <summary>What it does, e.g. "Attack", "Narrate coordinates".</summary>
	public required string Action { get; init; }

	/// <summary>The key or combination, e.g. "W", "Left Shift", "Shift plus C", or "Not bound".</summary>
	public required string Key { get; init; }

	/// <summary>True when nothing is bound to this action, so a viewer can group or skip them.</summary>
	public bool IsUnbound { get; init; }
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
	/// <summary>United Minecraft's keybind file, relative to the <c>.minecraft</c> root.</summary>
	public const string UnitedMinecraftKeybindsFile = @"config\united_minecraft_keybinds.json";

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
				Action    = FriendlyActionName(action),
				Key       = FriendlyInputName(input),
				IsUnbound = IsUnbound(input)
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
			>= 290 and <= 301 => "F" + (key - 289).ToString(CultureInfo.InvariantCulture),
			>= 320 and <= 329 => "Numpad " + (key - 320).ToString(CultureInfo.InvariantCulture),
			_ => "Key " + key.ToString(CultureInfo.InvariantCulture)
		};
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
