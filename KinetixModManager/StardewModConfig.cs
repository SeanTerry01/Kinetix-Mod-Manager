using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>What a setting holds, worked out from the value already in the file.</summary>
public enum StardewValueKind
{
	/// <summary>True or false.</summary>
	Boolean,

	/// <summary>A number.</summary>
	Number,

	/// <summary>Anything else — a key name, a word, a phrase.</summary>
	Text
}

/// <summary>One setting in an ordinary Stardew mod's config file.</summary>
public sealed class StardewSetting
{
	/// <summary>Where the setting lives in the file, dotted for one inside a group (<c>Controls.ToggleOverlay</c>).</summary>
	public string Path { get; init; } = "";

	/// <summary>The setting's own name, without its group.</summary>
	public string Name { get; init; } = "";

	/// <summary>The author's label where the mod ships one, otherwise the name spaced out to read properly.</summary>
	public string Label { get; init; } = "";

	/// <summary>The author's explanation where the mod ships one, otherwise <c>""</c>.</summary>
	public string Description { get; init; } = "";

	/// <summary>What the setting holds, inferred from its current value.</summary>
	public StardewValueKind Kind { get; init; }

	/// <summary>The current value as text.</summary>
	public string Value { get; init; } = "";

	/// <summary>True for a yes/no setting, which is offered as two choices rather than typed.</summary>
	public bool IsBoolean => Kind == StardewValueKind.Boolean;
}

/// <summary>
/// Reads an ordinary Stardew mod's <c>config.json</c> — one that isn't a Content Patcher pack — and works out
/// as much about its settings as the files allow.
///
/// This is the weakest of the manager's settings readers, and deliberately honest about that. A normal SMAPI
/// mod declares its options in code, to Generic Mod Config Menu, at the moment the game loads them; no file on
/// disk lists what a setting accepts. So two things are recovered instead:
///
/// <list type="bullet">
/// <item><description><b>The type</b>, from the value already in the file. A <c>true</c> is a yes/no setting and
/// can be offered as two choices; a number can be checked for being a number. That alone removes the guesswork
/// of "was it true, True, or yes?" and stops a typo turning a number into text the mod cannot read.</description></item>
/// <item><description><b>The author's own label and explanation</b>, from the translation file many mods ship
/// for their config menu — <c>i18n/default.json</c>, holding <c>config.some-setting.name</c> and
/// <c>config.some-setting.desc</c>. Where a mod ships those, a setting reads as "Automation interval" with a
/// sentence explaining it, rather than as "AutomationInterval".</description></item>
/// </list>
///
/// What is not recovered is the list of values a setting accepts, or a slider's range, because they exist only
/// in the mod's code. That needs a helper mod inside the game to ask Generic Mod Config Menu what was
/// registered — the same arrangement the Moonlight Peaks keybind reader uses.
/// </summary>
public static class StardewModConfig
{
	/// <summary>
	/// The settings in a mod's config file, with labels from its translation file where it has one. Empty for a
	/// mod with no config file, an unreadable one, or one holding nothing that can be edited as a single value.
	/// </summary>
	public static List<StardewSetting> Read(string modFolder)
	{
		var settings = new List<StardewSetting>();
		try
		{
			string configPath = System.IO.Path.Combine(modFolder, "config.json");
			if (!File.Exists(configPath)) return settings;

			JObject? config = ParseLenient(File.ReadAllText(configPath));
			if (config == null) return settings;

			Dictionary<string, (string Label, string Description)> labels = ReadLabels(modFolder);
			Collect(config, "", settings, labels);
		}
		catch { }

		return settings;
	}

	/// <summary>
	/// Writes one setting back, keeping the JSON type the file already used so a mod still reads what it
	/// expects. The file is edited rather than rewritten, and moved into place, so an interrupted write cannot
	/// leave a mod with a config it refuses to load.
	/// </summary>
	public static bool Write(string modFolder, StardewSetting setting, string newValue)
	{
		try
		{
			string configPath = System.IO.Path.Combine(modFolder, "config.json");
			JObject? config = ParseLenient(File.ReadAllText(configPath));
			if (config == null) return false;

			string[] steps = setting.Path.Split('.');
			JObject parent = config;
			for (int i = 0; i < steps.Length - 1; i++)
			{
				if (parent[steps[i]] is not JObject next) return false;
				parent = next;
			}

			string leaf = steps[^1];
			parent[leaf] = Retype(parent[leaf], newValue);

			string temporary = configPath + ".tmp";
			File.WriteAllText(temporary, config.ToString(Formatting.Indented));
			File.Move(temporary, configPath, overwrite: true);
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// The keys a Stardew mod will accept for a binding, in the names SMAPI uses for them.
	///
	/// Offered for a setting that holds a key, which is the one kind of free-text setting where a list can be
	/// built without the mod telling us anything: SMAPI's key names are fixed and shared by every mod, so a
	/// setting called something like <c>ToggleOverlayKey</c> can be picked from rather than spelled from memory.
	/// The list is the keys mods actually bind, not every name SMAPI knows — several hundred entries would be
	/// worse to arrow through than typing.
	/// </summary>
	public static IReadOnlyList<string> KeyNames { get; } = BuildKeyNames();

	private static List<string> BuildKeyNames()
	{
		var keys = new List<string>();
		for (char letter = 'A'; letter <= 'Z'; letter++) keys.Add(letter.ToString());
		for (int digit = 0; digit <= 9; digit++) keys.Add("D" + digit);            // SMAPI's name for the top row
		for (int f = 1; f <= 12; f++) keys.Add("F" + f);
		for (int pad = 0; pad <= 9; pad++) keys.Add("NumPad" + pad);

		keys.AddRange(new[]
		{
			"Up", "Down", "Left", "Right",
			"Space", "Enter", "Tab", "Escape", "Back", "Delete", "Insert",
			"Home", "End", "PageUp", "PageDown",
			"LeftShift", "RightShift", "LeftControl", "RightControl", "LeftAlt", "RightAlt",
			"OemTilde", "OemMinus", "OemPlus", "OemOpenBrackets", "OemCloseBrackets",
			"OemSemicolon", "OemQuotes", "OemComma", "OemPeriod", "OemQuestion", "OemPipe",
			"MouseLeft", "MouseRight", "MouseMiddle", "MouseX1", "MouseX2",
			"None"
		});

		return keys;
	}

	/// <summary>
	/// Whether a setting looks like it holds a key binding, judged by its name — the only signal available,
	/// since nothing on disk says what an ordinary mod's setting means.
	///
	/// Matched on whole words rather than on the letters anywhere in the name: "Monkey" ends in "key" without
	/// being one, and offering someone a list of keyboard keys for it would be worse than leaving it as text.
	/// </summary>
	public static bool LooksLikeAKeyBinding(StardewSetting setting)
	{
		if (setting.Kind != StardewValueKind.Text) return false;

		string[] words = SplitWords(setting.Name);
		return words.Any(word =>
			word is "key" or "keys" or "button" or "buttons" or "hotkey" or "hotkeys" or "keybind" or "binding");
	}

	/// <summary>
	/// The words in a setting's name, lower case — splitting on separators and on the humps of camel case, so
	/// "ToggleOverlayKey", "toggle_overlay_key" and "toggle-overlay-key" all come apart the same way.
	/// </summary>
	private static string[] SplitWords(string name) =>
		Spaced(name)
			.Replace('_', ' ')
			.Replace('-', ' ')
			.ToLowerInvariant()
			.Split(' ', StringSplitOptions.RemoveEmptyEntries);

	/// <summary>Whether <paramref name="value"/> can be stored in <paramref name="setting"/> without changing its type.</summary>
	public static bool IsValid(StardewSetting setting, string value) => setting.Kind switch
	{
		StardewValueKind.Boolean => bool.TryParse(value, out _),
		StardewValueKind.Number => double.TryParse(value, System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out _),
		_ => true
	};

	/// <summary>
	/// Walks the config, gathering every value that can be edited on its own. Groups are followed one level at a
	/// time; a list is skipped, since replacing one as a single typed value is a good way to break a mod.
	/// </summary>
	private static void Collect(JObject node, string prefix, List<StardewSetting> settings,
		Dictionary<string, (string Label, string Description)> labels)
	{
		foreach (JProperty property in node.Properties())
		{
			string path = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;

			if (property.Value is JObject group)
			{
				Collect(group, path, settings, labels);
				continue;
			}

			JToken? value = property.Value;
			if (value == null) continue;

			StardewValueKind? kind = value.Type switch
			{
				JTokenType.Boolean => StardewValueKind.Boolean,
				JTokenType.Integer or JTokenType.Float => StardewValueKind.Number,
				JTokenType.String => StardewValueKind.Text,
				_ => null
			};
			if (kind == null) continue;

			labels.TryGetValue(Normalize(property.Name), out (string Label, string Description) label);

			settings.Add(new StardewSetting
			{
				Path = path,
				Name = property.Name,
				Label = label.Label is { Length: > 0 } ? label.Label : Spaced(property.Name),
				Description = label.Description ?? "",
				Kind = kind.Value,
				Value = value.Type == JTokenType.String
					? (string?)value ?? ""
					: value.ToString(Formatting.None)
			});
		}
	}

	/// <summary>
	/// The labels a mod ships for its config menu, keyed by the setting's normalised name.
	///
	/// The two files disagree about spelling by convention: the config holds <c>AutomationInterval</c> while the
	/// translation holds <c>config.automation-interval.name</c>. Normalising both to letters and digits alone is
	/// what lets them meet.
	/// </summary>
	private static Dictionary<string, (string Label, string Description)> ReadLabels(string modFolder)
	{
		var labels = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
		try
		{
			string path = System.IO.Path.Combine(modFolder, "i18n", "default.json");
			if (!File.Exists(path)) return labels;

			JObject? translations = ParseLenient(File.ReadAllText(path));
			if (translations == null) return labels;

			foreach (JProperty property in translations.Properties())
			{
				// config.<setting>.name / .desc — anything else in the file belongs to the mod's own text.
				string[] parts = property.Name.Split('.');
				if (parts.Length != 3 || !parts[0].Equals("config", StringComparison.OrdinalIgnoreCase)) continue;

				string key = Normalize(parts[1]);
				labels.TryGetValue(key, out (string Label, string Description) existing);

				string text = (string?)property.Value ?? "";
				labels[key] = parts[2].ToLowerInvariant() switch
				{
					"name" => (text, existing.Description),
					"desc" or "tooltip" or "description" => (existing.Label, text),
					_ => existing
				};
			}
		}
		catch { }

		return labels;
	}

	/// <summary>Letters and digits only, lower case — so "AutomationInterval" and "automation-interval" meet.</summary>
	private static string Normalize(string name) =>
		new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

	/// <summary>"AutomationInterval" read as "Automation Interval", for a mod that ships no labels.</summary>
	private static string Spaced(string name)
	{
		var text = new StringBuilder();
		for (int i = 0; i < name.Length; i++)
		{
			if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) text.Append(' ');
			text.Append(name[i]);
		}
		return text.ToString();
	}

	/// <summary>Keeps a value in the JSON type its setting already used, so the mod still reads what it expects.</summary>
	private static JToken Retype(JToken? existing, string newValue)
	{
		if (existing?.Type == JTokenType.Boolean && bool.TryParse(newValue, out bool asBool))
			return new JValue(asBool);

		if (existing?.Type == JTokenType.Integer && long.TryParse(newValue, out long asLong))
			return new JValue(asLong);

		if (existing?.Type == JTokenType.Float &&
			double.TryParse(newValue, System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out double asDouble))
			return new JValue(asDouble);

		return new JValue(newValue);
	}

	/// <summary>Parses hand-written JSON, allowing the comments and trailing commas Stardew's loader allows.</summary>
	private static JObject? ParseLenient(string json)
	{
		try
		{
			using var reader = new JsonTextReader(new StringReader(json));
			return JObject.Load(reader, new JsonLoadSettings
			{
				CommentHandling = CommentHandling.Ignore,
				LineInfoHandling = LineInfoHandling.Ignore
			});
		}
		catch
		{
			return null;
		}
	}
}
