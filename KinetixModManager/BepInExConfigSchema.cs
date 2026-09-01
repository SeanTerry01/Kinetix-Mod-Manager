using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KinetixModManager;

/// <summary>One setting in a BepInEx config file: its value, and everything the author said about it.</summary>
public sealed class BepInExSetting
{
	/// <summary>The <c>[Section]</c> heading the setting sits under, or <c>""</c> before the first one.</summary>
	public string Section { get; init; } = "";

	/// <summary>The setting's name — the left-hand side of <c>Key = Value</c>.</summary>
	public string Key { get; init; } = "";

	/// <summary>The setting's current value.</summary>
	public string Value { get; init; } = "";

	/// <summary>BepInEx's declared type: <c>Boolean</c>, <c>Int32</c>, <c>Single</c>, <c>KeyCode</c>, and so on.</summary>
	public string Type { get; init; } = "";

	/// <summary>The author's explanation, with a multi-line one joined into a sentence, or <c>""</c>.</summary>
	public string Description { get; init; } = "";

	/// <summary>The value BepInEx would use if this setting were absent.</summary>
	public string Default { get; init; } = "";

	/// <summary>Every value the setting accepts, when it is an enumeration. Empty otherwise.</summary>
	public List<string> AcceptableValues { get; init; } = new();

	/// <summary>The low end of a numeric range, or <c>""</c> when the author declared none.</summary>
	public string RangeFrom { get; init; } = "";

	/// <summary>The high end of a numeric range, or <c>""</c>.</summary>
	public string RangeTo { get; init; } = "";

	/// <summary>True for a yes/no setting, which is offered as two choices rather than typed.</summary>
	public bool IsBoolean => Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase);

	/// <summary>True for a setting holding a key, which the controls list reads.</summary>
	public bool IsKey =>
		Type.Contains("KeyCode", StringComparison.OrdinalIgnoreCase) ||
		Type.Contains("KeyboardShortcut", StringComparison.OrdinalIgnoreCase) ||
		Type.Equals("Key", StringComparison.OrdinalIgnoreCase);

	/// <summary>True when the setting has a number for a value, so entry can be checked before it is saved.</summary>
	public bool IsNumeric =>
		Type.Equals("Int32", StringComparison.OrdinalIgnoreCase) ||
		Type.Equals("Int64", StringComparison.OrdinalIgnoreCase) ||
		Type.Equals("Single", StringComparison.OrdinalIgnoreCase) ||
		Type.Equals("Double", StringComparison.OrdinalIgnoreCase) ||
		Type.Equals("Byte", StringComparison.OrdinalIgnoreCase);

	/// <summary>True when the setting has a fixed set of answers that can be offered as a list.</summary>
	public bool HasChoices => AcceptableValues.Count > 0 || IsBoolean;

	/// <summary>The answers to offer: the author's list, or true/false for a yes/no setting.</summary>
	public List<string> Choices =>
		AcceptableValues.Count > 0 ? AcceptableValues
		: IsBoolean ? new List<string> { "true", "false" }
		: new List<string>();

	/// <summary>True when the author declared both ends of a numeric range.</summary>
	public bool HasRange => RangeFrom.Length > 0 && RangeTo.Length > 0;
}

/// <summary>
/// Reads what a BepInEx config file says about its own settings.
///
/// BepInEx writes far more than the values: above each setting it records the author's description, the
/// setting's type, its default, and — for an enumeration or a number — exactly what it will accept. All of it
/// sits in the same file as the value, in a fixed format:
///
/// <code>
/// ## Text size of the on-screen menu.
/// # Setting type: Int32
/// # Default value: 17
/// # Acceptable value range: From 10 to 40
/// FontSize = 17
/// </code>
///
/// Which means a mod's settings never have to be typed blind: the manager can offer a yes/no setting as two
/// choices, an enumeration as the author's own list, and check a number against its range before saving. That
/// is the whole point of reading this rather than treating the file as anonymous INI text.
///
/// One reader serves both users of this information — the settings editor and the controls list, which wants
/// the same parse filtered down to <see cref="BepInExSetting.IsKey"/>.
/// </summary>
public static class BepInExConfigSchema
{
	private static readonly Regex SettingType = new(@"^#\s*Setting type:\s*(.+)$", RegexOptions.IgnoreCase);
	private static readonly Regex DefaultValue = new(@"^#\s*Default value:\s*(.*)$", RegexOptions.IgnoreCase);
	private static readonly Regex AcceptableValues = new(@"^#\s*Acceptable values:\s*(.+)$", RegexOptions.IgnoreCase);
	private static readonly Regex AcceptableRange = new(@"^#\s*Acceptable value range:\s*From\s+(\S+)\s+to\s+(\S+)\s*$", RegexOptions.IgnoreCase);

	/// <summary>
	/// Every setting in the file, in the order it appears. An unreadable file yields nothing rather than
	/// throwing — a mod with an odd config should not stop the rest being listed.
	/// </summary>
	public static List<BepInExSetting> Read(string configPath)
	{
		var settings = new List<BepInExSetting>();
		try
		{
			if (!File.Exists(configPath)) return settings;

			string section = "";
			var description = new List<string>();
			string type = "", defaultValue = "", rangeFrom = "", rangeTo = "";
			var allowed = new List<string>();

			void Forget()
			{
				description.Clear();
				type = defaultValue = rangeFrom = rangeTo = "";
				allowed = new List<string>();
			}

			foreach (string raw in File.ReadAllLines(configPath))
			{
				string line = raw.Trim();
				if (line.Length == 0) continue;

				if (line.StartsWith("[") && line.EndsWith("]"))
				{
					section = line.Substring(1, line.Length - 2).Trim();
					Forget();
					continue;
				}

				// Two hashes is the author talking; one is BepInEx's generated metadata.
				if (line.StartsWith("##"))
				{
					string text = line.Substring(2).Trim();
					// The file's own header names the plugin and its id, which is not a description of anything.
					if (text.StartsWith("Settings file was created by plugin", StringComparison.OrdinalIgnoreCase) ||
						text.StartsWith("Plugin GUID:", StringComparison.OrdinalIgnoreCase))
						continue;
					if (text.Length > 0) description.Add(text);
					continue;
				}

				if (line.StartsWith("#"))
				{
					Match match;
					if ((match = SettingType.Match(line)).Success) type = match.Groups[1].Value.Trim();
					else if ((match = DefaultValue.Match(line)).Success) defaultValue = match.Groups[1].Value.Trim();
					else if ((match = AcceptableRange.Match(line)).Success)
					{
						rangeFrom = match.Groups[1].Value.Trim();
						rangeTo = match.Groups[2].Value.Trim();
					}
					else if ((match = AcceptableValues.Match(line)).Success)
					{
						allowed = match.Groups[1].Value
							.Split(',')
							.Select(value => value.Trim())
							.Where(value => value.Length > 0)
							.ToList();
					}
					continue;
				}

				int equals = line.IndexOf('=');
				if (equals > 0)
				{
					settings.Add(new BepInExSetting
					{
						Section = section,
						Key = line.Substring(0, equals).Trim(),
						Value = line.Substring(equals + 1).Trim(),
						Type = type,
						Description = string.Join(" ", description),
						Default = defaultValue,
						AcceptableValues = allowed,
						RangeFrom = rangeFrom,
						RangeTo = rangeTo
					});
				}

				// Whatever the line was, the metadata above it belonged to it alone.
				Forget();
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("BepInEx", $"reading the settings in {configPath}", ex); }

		return settings;
	}

	/// <summary>
	/// Looks up the schema for one setting, matched on section and key without regard to case. <c>null</c> when
	/// the file says nothing about it — which is normal for a config a user has hand-edited.
	/// </summary>
	public static BepInExSetting? Find(IEnumerable<BepInExSetting> settings, string section, string key) =>
		settings.FirstOrDefault(s =>
			string.Equals(s.Section, section, StringComparison.OrdinalIgnoreCase) &&
			string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

	/// <summary>Why a value was rejected. The wording lives with the UI; the rule lives here.</summary>
	public enum Problem
	{
		None,

		/// <summary>Not one of the values the author listed.</summary>
		NotAnAllowedValue,

		/// <summary>Not true or false.</summary>
		NotABoolean,

		/// <summary>Not a number.</summary>
		NotANumber,

		/// <summary>A number, but outside the range the author declared.</summary>
		OutOfRange
	}

	/// <summary>
	/// Whether <paramref name="value"/> is an acceptable answer for <paramref name="setting"/>, and if not, why.
	///
	/// Worth checking rather than trusting: BepInEx silently falls back to the default when it loads a value it
	/// cannot use, so a bad entry looks accepted in the manager and then quietly reverts the next time the game
	/// runs — the most confusing possible outcome for someone who cannot see the file.
	/// </summary>
	public static bool IsValid(BepInExSetting setting, string value, out Problem problem)
	{
		problem = Problem.None;

		if (setting.AcceptableValues.Count > 0 &&
			!setting.AcceptableValues.Any(v => v.Equals(value, StringComparison.OrdinalIgnoreCase)))
		{
			problem = Problem.NotAnAllowedValue;
			return false;
		}

		if (setting.IsBoolean && !bool.TryParse(value, out _))
		{
			problem = Problem.NotABoolean;
			return false;
		}

		if (setting.IsNumeric)
		{
			if (!TryParseNumber(value, out double number))
			{
				problem = Problem.NotANumber;
				return false;
			}

			if (setting.HasRange &&
				TryParseNumber(setting.RangeFrom, out double low) &&
				TryParseNumber(setting.RangeTo, out double high) &&
				(number < low || number > high))
			{
				problem = Problem.OutOfRange;
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Parses a number the way the config file writes one — always with a dot for the decimal point, whatever
	/// the user's regional settings say, since BepInEx writes and reads these files culture-invariantly.
	/// </summary>
	private static bool TryParseNumber(string text, out double number) =>
		double.TryParse(text, System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out number);
}
