using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>One key (or key combination) and everything the game does with it.</summary>
public sealed class GameKeyBinding
{
	/// <summary>The Unity <c>KeyCode</c> name — <c>E</c>, <c>Alpha1</c>, <c>LeftShift</c>, <c>PageDown</c>.
	/// A stable identifier, not a label to show: see <c>FriendlyKeyName</c> in the controls viewer.</summary>
	public string Key { get; init; } = "";

	/// <summary><c>""</c>, or <c>Control</c>/<c>Alt</c>/<c>Shift</c>/<c>Command</c> joined with <c>+</c>.</summary>
	public string Modifiers { get; init; } = "";

	/// <summary>Modifiers and key pre-joined, so bindings can be compared or grouped on one string.</summary>
	public string Combo { get; init; } = "";

	/// <summary>
	/// Every action bound to this combination. There is routinely more than one — <c>X</c> is Sort Inventory
	/// <em>and</em> Character Creator Rotate Right <em>and</em> Decorate, because each applies on a different
	/// screen — so this must never be treated as a single action per key.
	/// </summary>
	public List<string> Actions { get; init; } = new();
}

/// <summary>
/// A game's own keyboard bindings, read from the JSON a keybind-export plugin writes.
///
/// Two files share one schema, and which one is in hand matters to the player:
///
/// <list type="bullet">
/// <item><description><b>The live export</b>, written by the plugin inside the running game, is the player's
/// actual bindings including anything they have remapped.</description></item>
/// <item><description><b>The bundled snapshot</b>, shipped in the manager's <c>data\</c> folder, is the game's
/// stock bindings. It is what someone sees who has just bought the game, installed nothing and never launched
/// it — the case the whole feature exists for.</description></item>
/// </list>
///
/// The live file wins when it is there and parses; otherwise the snapshot stands in. Callers are expected to
/// say which they are showing (<see cref="IsLive"/>), because a player who has remapped a key and is shown the
/// stock one should be able to tell that they are looking at defaults rather than at a manager that is wrong
/// about their game.
/// </summary>
public sealed class GameKeybindExport
{
	/// <summary>The only schema this reader understands. A newer file is refused rather than mis-read.</summary>
	public const int SupportedSchemaVersion = 1;

	/// <summary>The <c>source</c> value that marks the shipped snapshot rather than a live export.</summary>
	public const string BundledSource = "bundled-defaults";

	/// <summary>True when these are the player's own bindings, read from their game.</summary>
	public bool IsLive { get; private init; }

	/// <summary>When the file was written, or <c>null</c> if it didn't say.</summary>
	public DateTime? GeneratedUtc { get; private init; }

	/// <summary>Every bound key, in the order the file lists them.</summary>
	public List<GameKeyBinding> Bindings { get; private init; } = new();

	/// <summary>
	/// Common keys the game binds nothing to. Only a <em>bare</em> binding takes a key off this list: a
	/// modifier never takes the key back, so <c>Control+U</c> being in use leaves plain <c>U</c> free.
	/// </summary>
	public List<string> FreeKeys { get; private init; } = new();

	/// <summary>
	/// The game's bindings for an install at <paramref name="gameFolder"/>: the live export if the plugin has
	/// written one, otherwise the snapshot bundled with the manager. <c>null</c> when the game has neither.
	/// </summary>
	public static GameKeybindExport? Load(GameProfile? profile, string gameFolder, string appFolder)
	{
		if (profile == null) return null;

		GameKeybindExport? live = ReadFile(LiveExportPath(profile, gameFolder));
		if (live != null) return live;

		if (string.IsNullOrEmpty(profile.BundledKeybindsFileName)) return null;
		return ReadFile(Path.Combine(appFolder, "data", profile.BundledKeybindsFileName));
	}

	/// <summary>
	/// Where the export plugin writes its file for an install at <paramref name="gameFolder"/>.
	///
	/// The plugin lets the player move it, so its own config is consulted first: a bare file name lands in the
	/// BepInEx folder and an absolute path is used as given. Only if that says nothing does the profile's
	/// default location apply.
	/// </summary>
	public static string LiveExportPath(GameProfile profile, string gameFolder)
	{
		if (string.IsNullOrEmpty(profile.KeybindExportFileRelativeToGame) || string.IsNullOrEmpty(gameFolder))
			return "";

		string configured = ConfiguredExportPath(gameFolder);
		if (configured.Length > 0) return configured;

		return Path.Combine(gameFolder, profile.KeybindExportFileRelativeToGame);
	}

	/// <summary>The <c>Path</c> the export plugin's own config asks for, or <c>""</c> when it doesn't say.</summary>
	private static string ConfiguredExportPath(string gameFolder)
	{
		try
		{
			string cfg = Path.Combine(gameFolder, "BepInEx", "config", "com.moonlightpeaks.keybindexport.cfg");
			if (!File.Exists(cfg)) return "";

			foreach (string raw in File.ReadAllLines(cfg))
			{
				// Only real settings, never the "# Default value: ..." comment lines above them.
				string line = raw.Trim();
				if (line.StartsWith("#") || line.StartsWith("[")) continue;

				Match m = Regex.Match(line, @"^Path\s*=\s*(.+)$", RegexOptions.IgnoreCase);
				if (!m.Success) continue;

				string value = m.Groups[1].Value.Trim();
				if (value.Length == 0) return "";
				return Path.IsPathRooted(value) ? value : Path.Combine(gameFolder, "BepInEx", value);
			}
		}
		catch { }
		return "";
	}

	/// <summary>Reads and parses one export file, or <c>null</c> if it is absent, unreadable or unusable.</summary>
	private static GameKeybindExport? ReadFile(string path)
	{
		try
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
			return Parse(File.ReadAllText(path));
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Parses export JSON. Returns <c>null</c> for anything this reader cannot honestly display: a schema it
	/// doesn't know, malformed JSON, or a file with no bindings in it — all of which should fall back to the
	/// next source rather than be shown as an empty or half-understood control list.
	/// </summary>
	public static GameKeybindExport? Parse(string json)
	{
		JObject root;
		try { root = JObject.Parse(json); }
		catch { return null; }

		if ((int?)root["schemaVersion"] != SupportedSchemaVersion) return null;

		var bindings = new List<GameKeyBinding>();
		if (root["bindings"] is JArray rawBindings)
		{
			foreach (JToken entry in rawBindings)
			{
				if (entry is not JObject b) continue;

				string key = (string?)b["key"] ?? "";
				if (key.Length == 0) continue;

				string modifiers = (string?)b["modifiers"] ?? "";
				string combo = (string?)b["combo"] ?? "";
				if (combo.Length == 0) combo = modifiers.Length > 0 ? modifiers + "+" + key : key;

				var actions = (b["actions"] as JArray)?
					.Select(a => (string?)a ?? "")
					.Where(a => a.Length > 0)
					.ToList() ?? new List<string>();

				bindings.Add(new GameKeyBinding
				{
					Key = key,
					Modifiers = modifiers,
					Combo = combo,
					Actions = actions
				});
			}
		}

		if (bindings.Count == 0) return null;

		DateTime? generated = null;
		if (DateTime.TryParse((string?)root["generatedUtc"], CultureInfo.InvariantCulture,
				DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
			generated = parsed;

		return new GameKeybindExport
		{
			// The file says which it is, so a snapshot someone has replaced with their own export is believed.
			IsLive = !string.Equals((string?)root["source"], BundledSource, StringComparison.OrdinalIgnoreCase),
			GeneratedUtc = generated,
			Bindings = bindings,
			FreeKeys = (root["freeKeys"] as JArray)?
				.Select(k => (string?)k ?? "")
				.Where(k => k.Length > 0)
				.ToList() ?? new List<string>()
		};
	}
}
