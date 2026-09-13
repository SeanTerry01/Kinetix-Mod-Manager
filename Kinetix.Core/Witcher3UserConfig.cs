using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace KinetixModManager;

/// <summary>
/// Reads a Witcher 3 mod's settings menu — the one the game shows under Options → Mods.
///
/// <para>
/// A mod that wants settings ships a <c>UserConfig</c> XML into
/// <c>&lt;game&gt;\bin\config\r4game\user_config_matrix\pc\</c>, one file per mod. That file is the mod's whole
/// menu: the groups it is divided into, every setting, whether each is a toggle or a slider, a slider's range,
/// and — in its <c>PresetsArray</c> — the author's defaults. It is the same kind of declaration Content
/// Patcher's schema gives us on Stardew and MCM Helper gives us on Skyrim, so it earns the same treatment:
/// settings offered as choices rather than typed into a file by hand.
/// </para>
///
/// <para>
/// The values themselves are <b>not</b> kept with the mod. The Witcher 3 writes every mod's settings into the
/// player's own <c>Documents\The Witcher 3\user.settings</c>, under a section named after the group — so
/// <c>[WASounds]</c>, holding lines like <c>waVolEnemyPing=100</c>. That split is why pressing the settings key
/// on the mod itself used to find nothing: there is nothing to find in the mod folder.
/// </para>
///
/// <para>
/// The menu is expressed as an <see cref="McmMenu"/> so it reuses the settings list the Skyrim/Fallout 4 mod
/// configuration menus already use, rather than adding another list that does the same job.
/// </para>
///
/// <para>
/// Self-contained (BCL + System.Xml.Linq) so the parsing can be unit tested.
/// </para>
/// </summary>
public static class Witcher3UserConfig
{
	/// <summary>
	/// Where the game keeps every mod's settings menu definition, relative to the game folder.
	/// <c>static readonly</c> rather than <c>const</c> so Path.Combine spells the separator; see
	/// <see cref="WindowsFileName"/> for why the host's idea of one cannot be assumed.
	/// </summary>
	public static readonly string ConfigMatrixRelativePath =
		Path.Combine("bin", "config", "r4game", "user_config_matrix", "pc");

	/// <summary>The file, inside the player's Witcher 3 documents folder, that holds every mod's settings.</summary>
	public const string UserSettingsFileName = "user.settings";

	/// <summary>
	/// Finds and reads the settings menu for the mod in <paramref name="modFolderPath"/>, or <c>null</c> when
	/// that mod declares no menu (most Witcher 3 mods do not).
	/// </summary>
	/// <param name="modFolderPath">The mod's folder, e.g. <c>…\mods\modWitcherAccess</c>.</param>
	/// <param name="gameRootPath">The game folder, which holds the config matrix.</param>
	/// <param name="userSettingsPath">The player's <c>user.settings</c>, where the values live.</param>
	public static McmMenu? ReadMenu(string modFolderPath, string gameRootPath, string userSettingsPath)
	{
		string? xmlPath = FindConfigXml(modFolderPath, gameRootPath);
		if (xmlPath == null) return null;

		try
		{
			return Parse(File.ReadAllText(xmlPath), Path.GetFileName(modFolderPath.TrimEnd(
				Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), userSettingsPath);
		}
		catch { return null; }   // a malformed menu is not worth failing the whole settings key over
	}

	/// <summary>
	/// Locates the config XML a mod's settings are declared in.
	///
	/// The file is normally named after the mod's folder (<c>modWitcherAccess</c> → <c>modWitcherAccess.xml</c>),
	/// but that is a convention rather than a rule and mods do break it — <c>modFMCAudioRemaster</c> ships
	/// <c>modFMCAudio.xml</c>. So an exact match is tried first, then the longest file name that the folder name
	/// starts with, which picks that case up without matching some unrelated mod's menu. Failing both, a mod that
	/// ships its menu inside its own folder (leaving the copying to the player) is searched too.
	/// </summary>
	public static string? FindConfigXml(string modFolderPath, string gameRootPath)
	{
		string modName = Path.GetFileName(modFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (modName.Length == 0) return null;

		string matrix = string.IsNullOrEmpty(gameRootPath) ? "" : Path.Combine(gameRootPath, ConfigMatrixRelativePath);
		if (Directory.Exists(matrix))
		{
			string exact = Path.Combine(matrix, modName + ".xml");
			if (File.Exists(exact)) return exact;

			string? prefixMatch = Directory.EnumerateFiles(matrix, "*.xml")
				.Where(f => modName.StartsWith(Path.GetFileNameWithoutExtension(f), StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(f => Path.GetFileNameWithoutExtension(f).Length)
				.FirstOrDefault();
			if (prefixMatch != null) return prefixMatch;
		}

		// Some mods ship the menu inside the mod and leave installing it to the player.
		try
		{
			if (Directory.Exists(modFolderPath))
				return Directory.EnumerateFiles(modFolderPath, "*.xml", SearchOption.AllDirectories)
					.FirstOrDefault(IsUserConfigFile);
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Witcher3", $"looking for a menu file under {modFolderPath}", ex); }
		return null;
	}

	/// <summary>True when an XML file is a settings-menu declaration rather than any other XML a mod ships.</summary>
	private static bool IsUserConfigFile(string path)
	{
		try
		{
			// Read only as far as the root element; some mod XML runs to megabytes.
			using var reader = System.Xml.XmlReader.Create(path, new System.Xml.XmlReaderSettings { IgnoreWhitespace = true });
			return reader.MoveToContent() == System.Xml.XmlNodeType.Element &&
				reader.Name.Equals("UserConfig", StringComparison.OrdinalIgnoreCase);
		}
		catch { return false; }
	}

	/// <summary>
	/// Turns a settings-menu XML into a menu the settings list can show. Pure — no file access — so the rules
	/// below are unit tested against the real menus mods ship.
	/// </summary>
	public static McmMenu Parse(string xml, string modName, string userSettingsPath)
	{
		var controls = new List<McmControl>();
		XDocument document = XDocument.Parse(xml);

		foreach (XElement group in document.Root?.Elements().Where(e => e.Name.LocalName == "Group") ?? Enumerable.Empty<XElement>())
		{
			string groupId = (string?)group.Attribute("id") ?? "";
			if (groupId.Length == 0) continue;

			string groupLabel = HumaniseGroupId(groupId);

			// The author's defaults, which matter more here than they look: the game writes a setting into
			// user.settings only once it has been touched, so on a fresh install the file holds nothing for this
			// mod at all. Without these the whole menu would read as blank until the player had been through it
			// in-game, which is exactly the situation where seeing the settings from outside is most useful.
			var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			XElement? preset = group.Descendants().FirstOrDefault(e => e.Name.LocalName == "Preset");
			if (preset != null)
				foreach (XElement entry in preset.Elements().Where(e => e.Name.LocalName == "Entry"))
				{
					string varId = (string?)entry.Attribute("varId") ?? "";
					if (varId.Length > 0) defaults[varId] = (string?)entry.Attribute("value") ?? "";
				}

			var groupControls = new List<McmControl>();
			foreach (XElement var in group.Descendants().Where(e => e.Name.LocalName == "Var"))
			{
				string id = (string?)var.Attribute("id") ?? "";
				if (id.Length == 0) continue;

				string displayType = (string?)var.Attribute("displayType") ?? "";
				// A var the menu itself never shows is bookkeeping — WitcherAccess's "waInit" records that the
				// mod has run once. Offering it as a setting would invite breaking the mod from outside.
				string visibility = (string?)var.Attribute("visibilityCondition") ?? "";
				if (visibility.Contains("hideAlways", StringComparison.OrdinalIgnoreCase)) continue;

				(McmControlKind kind, double? min, double? max) = ReadDisplayType(displayType);
				if (kind == McmControlKind.NotASetting) continue;   // separators and decoration

				groupControls.Add(new McmControl
				{
					Label = Humanise((string?)var.Attribute("displayName") ?? id),
					Kind = kind,
					SettingName = id,
					IniSection = groupId,
					Group = groupLabel,
					Min = min,
					Max = max,
					Default = defaults.TryGetValue(id, out string? d) ? d : "",
				});
			}

			if (groupControls.Count == 0) continue;
			controls.Add(new McmControl { Label = groupLabel, Kind = McmControlKind.NotASetting });
			controls.AddRange(groupControls);
		}

		return new McmMenu
		{
			ModName = modName,
			Controls = controls,
			DefaultsIniPath = "",              // the defaults are in the XML, not a file
			UserIniPath = userSettingsPath,
		};
	}

	/// <summary>
	/// Reads a <c>displayType</c>. <c>TOGGLE</c> is on/off; <c>SLIDER;min;max;steps</c> is a number in a range;
	/// a separator is decoration. Anything unrecognised is treated as free text rather than dropped, so a menu
	/// using a control this does not know about still shows its settings instead of appearing empty.
	/// </summary>
	private static (McmControlKind Kind, double? Min, double? Max) ReadDisplayType(string displayType)
	{
		string[] parts = displayType.Split(';', StringSplitOptions.TrimEntries);
		string kind = parts.Length > 0 ? parts[0] : "";

		if (kind.Equals("TOGGLE", StringComparison.OrdinalIgnoreCase))
			return (McmControlKind.Toggle, null, null);

		if (kind.Equals("SLIDER", StringComparison.OrdinalIgnoreCase))
		{
			double? min = parts.Length > 1 && double.TryParse(parts[1], out double lo) ? lo : null;
			double? max = parts.Length > 2 && double.TryParse(parts[2], out double hi) ? hi : null;
			return (McmControlKind.Number, min, max);
		}

		// A BUTTON performs an action inside the game and stores nothing — WitcherAccess's Glossary group is
		// thirteen of them, each playing a sound so the player can hear what it is. Only the game can carry
		// those out, and treating them as settings would write meaningless keys into user.settings.
		if (kind.Length == 0 ||
			kind.Equals("BUTTON", StringComparison.OrdinalIgnoreCase) ||
			kind.Contains("SEPARATOR", StringComparison.OrdinalIgnoreCase))
			return (McmControlKind.NotASetting, null, null);

		return (McmControlKind.Text, null, null);
	}

	/// <summary>
	/// Turns a group's id into the name of the section — "WAGeneral" into "General".
	///
	/// The id is the mod's initials followed by the section's own name, and it is also the section heading in
	/// user.settings. Read as it stands a screen reader says "Wageneral", which is the mod's internal name for
	/// the group rather than what the game's own menu calls it. Splitting on the capitals and dropping a leading
	/// run of them recovers the name without needing to know anything about this particular mod: WASounds gives
	/// "Sounds", and a group id that is just a word is left as it is.
	/// </summary>
	private static string HumaniseGroupId(string groupId)
	{
		var words = new List<string>();
		var current = new System.Text.StringBuilder();

		for (int i = 0; i < groupId.Length; i++)
		{
			char c = groupId[i];
			// A capital starts a new word when it follows a lower-case letter ("SoundsMaster"), and also when it
			// is the last capital of a run and a lower-case letter follows it — which is what separates the "WA"
			// from the "General" in "WAGeneral". Without that second case the whole id stays one word.
			bool startsWord = char.IsUpper(c) && current.Length > 0 &&
				(!char.IsUpper(current[current.Length - 1]) ||
				 (i + 1 < groupId.Length && char.IsLower(groupId[i + 1])));

			if (startsWord)
			{
				words.Add(current.ToString());
				current.Clear();
			}
			current.Append(c);
		}
		if (current.Length > 0) words.Add(current.ToString());

		// "WAGeneral" splits as "WA" + "General": the initials are the mod's, not the section's.
		if (words.Count > 1 && words[0].All(char.IsUpper)) words.RemoveAt(0);
		if (words.Count == 0) return groupId;

		string joined = string.Join(" ", words);
		return char.ToUpperInvariant(joined[0]) + joined.Substring(1);
	}

	/// <summary>
	/// Turns a menu's label key into something worth reading aloud. These are localisation keys resolved from
	/// the mod's compiled <c>.w3strings</c>, which is a binary format we cannot read — but the keys themselves
	/// are written by hand and describe the setting, so "wa_snd_enemy_ping" becomes "Sound: enemy ping" rather
	/// than being read out as an identifier.
	/// </summary>
	private static string Humanise(string displayName)
	{
		if (displayName.Length == 0) return "";

		// "Mods.witcheraccess.wasounds" -> "wasounds": the leading path is the mod's own namespace.
		string text = displayName.Substring(displayName.LastIndexOf('.') + 1);

		var words = text.Split('_', StringSplitOptions.RemoveEmptyEntries).ToList();
		if (words.Count == 0) return displayName;
		if (words[0].Equals("wa", StringComparison.OrdinalIgnoreCase) && words.Count > 1) words.RemoveAt(0);

		// The two prefixes this mod uses for every sound: a switch for the cue, and its loudness.
		string prefix = "";
		if (words.Count > 1 && words[0].Equals("snd", StringComparison.OrdinalIgnoreCase)) { prefix = "Sound: "; words.RemoveAt(0); }
		else if (words.Count > 1 && words[0].Equals("vol", StringComparison.OrdinalIgnoreCase)) { prefix = "Volume: "; words.RemoveAt(0); }

		string joined = string.Join(" ", words);
		if (joined.Length == 0) return displayName;
		return prefix + char.ToUpperInvariant(joined[0]) + joined.Substring(1);
	}
}
