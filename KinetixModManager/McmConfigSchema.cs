using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>What kind of thing an MCM control is, reduced to how the manager has to present it.</summary>
public enum McmControlKind
{
	/// <summary>Not a setting: a heading, a spacer, a paragraph of text, a link to another page.</summary>
	NotASetting,

	/// <summary>On or off.</summary>
	Toggle,

	/// <summary>A number, usually with a range and a step.</summary>
	Number,

	/// <summary>One of a fixed list the author supplied.</summary>
	Choice,

	/// <summary>Free text.</summary>
	Text,

	/// <summary>A key binding. Shown, but not rebound from here — that needs the game's own capture screen.</summary>
	Key
}

/// <summary>One control from a mod's Mod Configuration Menu.</summary>
public sealed class McmControl
{
	/// <summary>The author's label, as the menu shows it — "Wrap text", not "bWrapText".</summary>
	public string Label { get; init; } = "";

	/// <summary>The author's longer explanation, or <c>""</c>.</summary>
	public string Help { get; init; } = "";

	/// <summary>How this control has to be presented.</summary>
	public McmControlKind Kind { get; init; }

	/// <summary>The setting's name in the INI — the part of <c>id</c> before the colon.</summary>
	public string SettingName { get; init; } = "";

	/// <summary>The INI section the setting lives under — the part of <c>id</c> after the colon.</summary>
	public string IniSection { get; init; } = "";

	/// <summary>The heading this control appeared under, for grouping. <c>""</c> when it had none.</summary>
	public string Group { get; init; } = "";

	/// <summary>The values a <see cref="McmControlKind.Choice"/> offers, in the author's order.</summary>
	public List<string> Options { get; init; } = new();

	/// <summary>The low end of a number's range, or <c>null</c>.</summary>
	public double? Min { get; init; }

	/// <summary>The high end of a number's range, or <c>null</c>.</summary>
	public double? Max { get; init; }

	/// <summary>
	/// The author's default, for a menu that declares its defaults in the menu itself rather than in a separate
	/// file. Used only when neither settings file mentions the setting. <c>""</c> when there is none.
	///
	/// The Witcher 3 needs this: the game writes a setting into user.settings only once it has been touched, so
	/// a mod's settings are simply absent until the player has been through the menu in-game — which is exactly
	/// when reading them from outside is most useful. MCM Helper mods ship a defaults INI instead and leave
	/// this empty, so they are unaffected.
	/// </summary>
	public string Default { get; init; } = "";

	/// <summary>True when this control corresponds to a value in the INI that can be read and written.</summary>
	public bool IsSetting => Kind != McmControlKind.NotASetting && SettingName.Length > 0;

	/// <summary>The key this setting is stored under, matching <see cref="McmSettings"/>.</summary>
	public string ValueKey => IniSection + "|" + SettingName;
}

/// <summary>A mod's Mod Configuration Menu: what it offers, and where its answers are kept.</summary>
public sealed class McmMenu
{
	/// <summary>The mod's name as MCM knows it, which is also its settings file's name.</summary>
	public string ModName { get; init; } = "";

	/// <summary>Every control the menu declares, headings and spacers included, in order.</summary>
	public List<McmControl> Controls { get; init; } = new();

	/// <summary>The mod's shipped defaults INI.</summary>
	public string DefaultsIniPath { get; init; } = "";

	/// <summary>The player's own settings INI — where changes are written. May not exist yet.</summary>
	public string UserIniPath { get; init; } = "";

	/// <summary>Just the controls that hold a value.</summary>
	public IEnumerable<McmControl> Settings => Controls.Where(c => c.IsSetting);
}

/// <summary>
/// Reads a mod's Mod Configuration Menu — the menu Skyrim and Fallout 4 mods use for their settings.
///
/// MCM Helper mods declare their whole menu in a JSON file: every control with the author's label, their
/// explanation, the kind of control it is, and which INI setting it reads and writes. That is the same thing
/// Content Patcher's schema gave us for Stardew, and it means these settings can be offered as choices rather
/// than typed into an INI by hand:
///
/// <code>
/// { "id": "bWrapText:DialogueMenu", "text": "Wrap text", "type": "switcher",
///   "help": "Controls whether text will wrap to the width of the dialogue menu...",
///   "valueOptions": { "sourceType": "ModSettingBool" } }
/// </code>
///
/// Values live in two INIs: the defaults the mod ships, and the player's own file under
/// <c>Data\MCM\Settings\</c> which overrides them. Reading takes both; writing only ever touches the player's,
/// so a mod's own files are never modified and reinstalling it cannot lose the player's choices.
/// </summary>
public static class McmConfigSchema
{
	/// <summary>
	/// Every MCM menu a mod folder declares. Usually one; a mod can ship several under <c>MCM\Config\</c>.
	/// Empty for a mod that doesn't use MCM at all, which is most of them.
	/// </summary>
	public static List<McmMenu> ReadMenus(string modFolder, string gameFolder)
	{
		var menus = new List<McmMenu>();
		try
		{
			string root = Path.Combine(modFolder, "MCM", "Config");
			if (!Directory.Exists(root)) return menus;

			foreach (string configDir in Directory.GetDirectories(root))
			{
				string configPath = Path.Combine(configDir, "config.json");
				if (!File.Exists(configPath)) continue;

				McmMenu? menu = ReadMenu(configPath, configDir, gameFolder);
				if (menu != null && menu.Controls.Count > 0) menus.Add(menu);
			}
		}
		catch { }

		return menus;
	}

	/// <summary>Reads one <c>config.json</c>, or <c>null</c> if it can't be understood.</summary>
	public static McmMenu? ReadMenu(string configPath, string configDir, string gameFolder)
	{
		try
		{
			JObject root = JObject.Parse(File.ReadAllText(configPath));
			string modName = root.Value<string>("modName") ?? Path.GetFileName(configDir);

			// Controls sit under pages[].content[] (Fallout 4 Access) or a top-level content[] (XDI).
			var contentArrays = new List<JArray>();
			if (root["pages"] is JArray pages)
				foreach (JToken page in pages)
					if (page["content"] is JArray pageContent) contentArrays.Add(pageContent);
			if (root["content"] is JArray topContent) contentArrays.Add(topContent);

			var controls = new List<McmControl>();
			string group = "";

			foreach (JArray content in contentArrays)
			{
				foreach (JToken item in content)
				{
					string type = item.Value<string>("type")?.Trim() ?? "";
					string label = item.Value<string>("text")?.Trim() ?? "";

					// A heading names everything after it until the next one.
					if (type.Equals("section", StringComparison.OrdinalIgnoreCase))
					{
						group = label;
						controls.Add(new McmControl { Label = label, Kind = McmControlKind.NotASetting, Group = label });
						continue;
					}

					McmControlKind kind = KindOf(type);
					if (kind == McmControlKind.NotASetting)
					{
						controls.Add(new McmControl { Label = label, Kind = kind, Group = group });
						continue;
					}

					string id = item.Value<string>("id") ?? "";
					int colon = id.IndexOf(':');
					JObject? valueOptions = item["valueOptions"] as JObject;

					controls.Add(new McmControl
					{
						Label = label,
						Help = item.Value<string>("help")?.Trim() ?? "",
						Kind = kind,
						SettingName = colon >= 0 ? id.Substring(0, colon) : id,
						IniSection = colon >= 0 ? id.Substring(colon + 1) : "",
						Group = group,
						Options = ReadOptions(valueOptions),
						Min = ReadNumber(valueOptions, "min"),
						Max = ReadNumber(valueOptions, "max")
					});
				}
			}

			return new McmMenu
			{
				ModName = modName,
				Controls = controls,
				DefaultsIniPath = Path.Combine(configDir, "settings.ini"),
				UserIniPath = string.IsNullOrEmpty(gameFolder)
					? ""
					: Path.Combine(gameFolder, "Data", "MCM", "Settings", modName + ".ini")
			};
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// How a control type has to be presented. Unknown types are treated as not-a-setting rather than guessed
	/// at: showing a control the manager doesn't understand invites writing a value the mod can't read.
	/// </summary>
	private static McmControlKind KindOf(string type) => type.ToLowerInvariant() switch
	{
		"toggle" or "switcher" => McmControlKind.Toggle,
		"slider" or "stepper" => McmControlKind.Number,
		"enum" or "dropdown" => McmControlKind.Choice,
		"textinput" or "textinputint" or "textinputfloat" => McmControlKind.Text,
		"keyinput" or "keymap" => McmControlKind.Key,
		_ => McmControlKind.NotASetting
	};

	private static List<string> ReadOptions(JObject? valueOptions) =>
		(valueOptions?["options"] as JArray)?
			.Select(o => (string?)o ?? "")
			.Where(o => o.Length > 0)
			.ToList() ?? new List<string>();

	private static double? ReadNumber(JObject? valueOptions, string name)
	{
		JToken? token = valueOptions?[name];
		if (token == null) return null;
		return double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
			? value
			: null;
	}
}

/// <summary>
/// The values behind an MCM menu: the mod's shipped defaults with the player's own settings laid over them.
///
/// Kept apart from the schema because they come from different files with different rules — the defaults are
/// the mod's and must never be written to, the player's file is ours to change and may not exist yet.
/// </summary>
public sealed class McmSettings
{
	private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Reads a menu's defaults, then the player's overrides on top.</summary>
	public static McmSettings Load(McmMenu menu)
	{
		var settings = new McmSettings();
		settings.LoadFile(menu.DefaultsIniPath);
		settings.LoadFile(menu.UserIniPath);
		return settings;
	}

	/// <summary>
	/// The current value of a setting: what the player's file says, falling back to the author's default when
	/// neither file mentions it, and <c>""</c> when there is no default either.
	/// </summary>
	public string Get(McmControl control) =>
		_values.TryGetValue(control.ValueKey, out string? value) ? value : control.Default;

	/// <summary>Sets a value in memory. Use <see cref="Save"/> to write it to the player's file.</summary>
	public void Set(McmControl control, string value) => _values[control.ValueKey] = value;

	/// <summary>
	/// Writes one setting to the player's INI, leaving every other line — and the mod's own defaults file —
	/// untouched. The file and its folder are created if this is the first setting ever changed.
	/// </summary>
	public static bool Save(McmMenu menu, McmControl control, string value)
	{
		try
		{
			if (string.IsNullOrEmpty(menu.UserIniPath)) return false;

			string? folder = Path.GetDirectoryName(menu.UserIniPath);
			if (folder == null) return false;
			Directory.CreateDirectory(folder);

			// Load copes with the file not existing yet, which is the normal case the first time a setting is
			// changed: the player's settings file is created by MCM only once the in-game menu has been used.
			IniDocument document = IniDocument.Load(menu.UserIniPath);
			document.SetValue(control.IniSection, control.SettingName, value);
			document.Save(menu.UserIniPath);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private void LoadFile(string path)
	{
		try
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

			string section = "";
			foreach (string raw in File.ReadAllLines(path))
			{
				string line = raw.Trim();
				if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;

				if (line.StartsWith("[") && line.EndsWith("]"))
				{
					section = line.Substring(1, line.Length - 2).Trim();
					continue;
				}

				int equals = line.IndexOf('=');
				if (equals <= 0) continue;
				_values[section + "|" + line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
			}
		}
		catch { }
	}
}
