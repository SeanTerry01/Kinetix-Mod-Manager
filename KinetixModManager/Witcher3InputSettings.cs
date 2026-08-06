using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KinetixModManager;

/// <summary>
/// The Witcher 3's keyboard bindings, read from the game's own <c>input.settings</c>.
///
/// This game needs no plugin to answer "what does this key do?" — unlike Moonlight Peaks, which resolves its
/// bindings at runtime and writes nothing down, The Witcher 3 keeps every binding in a plain file in the
/// player's own folder, and rewrites it whenever they remap something. So the controls list here is always the
/// player's real, current bindings, including their own changes.
///
/// The file's shape is INI: a section per input context, and one line per binding —
/// <c>IK_LShift=(Action=PCAlternate)</c>. Two things about it shape this reader:
///
/// <list type="bullet">
/// <item><description>A key appears many times over, once per context. <c>Left Mouse</c> attacks in combat,
/// selects in menus and casts in the radial menu, and all of that is true at once — so the actions are
/// gathered per key rather than one winning.</description></item>
/// <item><description>Movement is bound as a controller axis with a direction: <c>W</c> is
/// <c>GI_AxisLeftY</c> with <c>Value=1.0</c>. Read literally that becomes "Axis Left Y", which tells a player
/// nothing, so the four movement axes are named properly.</description></item>
/// </list>
/// </summary>
public static class Witcher3InputSettings
{
	/// <summary>The file, inside the game's per-player folder, that holds the bindings.</summary>
	public const string FileName = "input.settings";

	/// <summary>The prefix every binding key carries.</summary>
	private const string KeyPrefix = "IK_";

	/// <summary>The value used for "nothing is bound here".</summary>
	private const string Unbound = "IK_None";

	/// <summary>
	/// The four movement directions, which the file records as a controller axis and a sign. Without this they
	/// read as "Axis Left X", which is exactly the sort of answer that makes a controls list useless.
	/// </summary>
	private static readonly Dictionary<(string Axis, bool Positive), string> AxisActions = new()
	{
		[("GI_AxisLeftY", true)]  = "Move Forward",
		[("GI_AxisLeftY", false)] = "Move Backward",
		[("GI_AxisLeftX", true)]  = "Move Right",
		[("GI_AxisLeftX", false)] = "Move Left",
	};

	/// <summary>The full path of <c>input.settings</c> for a copy of the game at <paramref name="gameFolder"/>.</summary>
	public static string PathFor(GameProfile profile, string gameFolder)
	{
		string dir = profile.UserDataDirectoryFor(gameFolder);
		return dir.Length == 0 ? "" : Path.Combine(dir, FileName);
	}

	/// <summary>
	/// Every keyboard and mouse binding in <paramref name="path"/>, one entry per key, with everything that key
	/// does gathered under it. Gamepad bindings are left out: this list answers a keyboard question.
	/// </summary>
	public static List<GameKeyBinding> Read(string path)
	{
		var byKey = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		var order = new List<string>();

		if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new List<GameKeyBinding>();

		try
		{
			foreach (IniDocument.Entry entry in IniDocument.Load(path).Entries())
			{
				if (!entry.Key.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase)) continue;
				if (entry.Key.Equals(Unbound, StringComparison.OrdinalIgnoreCase)) continue;

				// Controller bindings, under all the names the file gives them (IK_Pad_A_CROSS, IK_PS4_OPTIONS,
				// IK_Joy…). A keyboard player reading a list of them learns nothing about the keys in front of
				// them. The two mouse-look axes go too: IK_MouseX is the mouse being moved, not a key to press.
				if (IsNotAKeyboardKey(entry.Key)) continue;

				string? action = FriendlyAction(entry.Value);
				if (action == null) continue;

				string key = FriendlyKeyName(entry.Key.Substring(KeyPrefix.Length));
				if (key.Length == 0) continue;

				if (!byKey.TryGetValue(key, out List<string>? actions))
				{
					actions = new List<string>();
					byKey[key] = actions;
					order.Add(key);
				}

				// The same action appears under a key once per context it applies in; saying it once is enough.
				if (!actions.Contains(action, StringComparer.OrdinalIgnoreCase)) actions.Add(action);
			}
		}
		catch
		{
			// An unreadable or half-written input.settings is not worth failing the controls list over; the
			// caller falls back to having no bindings, which it already handles.
			return new List<GameKeyBinding>();
		}

		return order
			.Select(key => new GameKeyBinding
			{
				Key = key,
				Modifiers = "",
				Combo = key,
				Actions = byKey[key]
			})
			.ToList();
	}

	/// <summary>
	/// True for a binding that isn't a key on the keyboard: a controller button, or the mouse's own movement.
	/// </summary>
	private static bool IsNotAKeyboardKey(string rawKey) =>
		Regex.IsMatch(rawKey, @"^IK_(Pad|PS4|Xbox|Joy)", RegexOptions.IgnoreCase) ||
		rawKey.Equals("IK_MouseX", StringComparison.OrdinalIgnoreCase) ||
		rawKey.Equals("IK_MouseY", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// The action a binding line describes, in words — or <c>null</c> when the line binds nothing readable.
	/// </summary>
	private static string? FriendlyAction(string value)
	{
		Match action = Regex.Match(value, @"Action\s*=\s*([A-Za-z0-9_]+)");
		if (!action.Success) return null;

		string name = action.Groups[1].Value;

		// An axis binding carries its direction in the same line, and the direction is the whole meaning.
		Match axisValue = Regex.Match(value, @"Value\s*=\s*(-?[\d.]+)");
		if (axisValue.Success &&
			double.TryParse(axisValue.Groups[1].Value, System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out double v) &&
			AxisActions.TryGetValue((name, v >= 0), out string? movement))
			return movement;

		return Humanise(name);
	}

	/// <summary>
	/// A key's name as a player would say it. The file's own names are close but not sayable — <c>LShift</c>,
	/// <c>LeftMouse</c>, <c>NumPad0</c> — and a controls list read aloud has to be words.
	/// </summary>
	public static string FriendlyKeyName(string raw)
	{
		if (raw.Length == 0) return "";

		switch (raw.ToLowerInvariant())
		{
			case "lshift":      return "Left Shift";
			case "rshift":      return "Right Shift";
			case "lcontrol":    return "Left Control";
			case "rcontrol":    return "Right Control";
			case "lalt":        return "Left Alt";
			case "ralt":        return "Right Alt";
			case "leftmouse":   return "Left Mouse Button";
			case "rightmouse":  return "Right Mouse Button";
			case "middlemouse": return "Middle Mouse Button";
			case "mousewheelup":   return "Mouse Wheel Up";
			case "mousewheeldown": return "Mouse Wheel Down";
			case "space":       return "Spacebar";
			case "escape":      return "Escape";
			case "enter":       return "Enter";
			case "backspace":   return "Backspace";
			case "tab":         return "Tab";
			case "capslock":    return "Caps Lock";
			case "pageup":      return "Page Up";
			case "pagedown":    return "Page Down";
			case "insert":      return "Insert";
			case "delete":      return "Delete";
			case "home":        return "Home";
			case "end":         return "End";
			case "up":          return "Up Arrow";
			case "down":        return "Down Arrow";
			case "left":        return "Left Arrow";
			case "right":       return "Right Arrow";
		}

		Match pad = Regex.Match(raw, @"^NumPad(.+)$", RegexOptions.IgnoreCase);
		if (pad.Success) return "Numpad " + pad.Groups[1].Value;

		return Humanise(raw);
	}

	/// <summary>Splits an engine name into words: <c>AttackHeavy</c> becomes "Attack Heavy".</summary>
	private static string Humanise(string name)
	{
		// Engine prefixes carry no meaning for a player reading the list.
		name = Regex.Replace(name, @"^(GI|IK)_", "");
		name = name.Replace('_', ' ');

		var sb = new StringBuilder(name.Length + 8);
		for (int i = 0; i < name.Length; i++)
		{
			char c = name[i];
			bool boundary = i > 0 &&
				char.IsUpper(c) &&
				(char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));

			if (boundary && sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
			sb.Append(c);
		}

		return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
	}
}
