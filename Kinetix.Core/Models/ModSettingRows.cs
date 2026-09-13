// Rows for the screens that edit a mod's own settings - INI files, Content Patcher, MCM and Stardew config.
//
// Lifted out of Form1 by Phase 2 of the core split. These are plain data - what a row holds and how
// it reads aloud - so they belong beside the rules rather than inside the window that happens to
// show them today. While they were private to Form1 no second front end could name them at all.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>A settings row in the editor list. Carries its parsed entry and reads as "[Section] key = value".</summary>
public sealed class IniRow
{
    public IniDocument.Entry Entry { get; }
    public IniRow(IniDocument.Entry entry) => Entry = entry;
    public override string ToString() => Loc.T("ini.row", Entry.Section, Entry.Key, Entry.Value);
}

/// <summary>A game INI file offered in the chooser. Reads as the file name, noting when it doesn't exist yet.</summary>
public sealed class IniFileChoice
{
    public string Label { get; }
    public string Path { get; }
    public bool Exists { get; }
    public IniFileChoice(string label, string path, bool exists) { Label = label; Path = path; Exists = exists; }
    public override string ToString() => Exists ? Label : Loc.T("ini.chooseMissing", Label);
}

/// <summary>One row: a setting, its current answer, and whether that answer is just the author's default.</summary>
public sealed class CpRow
{
	public required CpConfigOption Option { get; init; }
	public required string Value { get; init; }

	/// <summary>True when the pack's config has no answer of its own and the author's default is standing in.</summary>
	public required bool IsDefault { get; init; }

	public override string ToString() =>
		IsDefault
			? Loc.T("cpconfig.rowDefault", Option.Name, DisplayValue)
			: Loc.T("cpconfig.row", Option.Name, DisplayValue);

	/// <summary>The answer as it should be read out — an empty answer is said, not left as silence.</summary>
	private string DisplayValue => Value.Length > 0 ? Value : Loc.T("cpconfig.valueEmpty");
}

/// <summary>One row: an MCM control and its current value, or a heading with no value of its own.</summary>
public sealed class McmRow : IListHeadingRow
{
	public required McmControl Control { get; init; }
	public required string Value { get; init; }

	/// <summary>A heading names the group of settings under it; the rows are numbered within that group.</summary>
	public bool IsHeading => Control.Kind == McmControlKind.NotASetting;

	public override string ToString() => Control.Kind switch
	{
		McmControlKind.NotASetting => Loc.T("mcm.rowHeading", Control.Label),
		McmControlKind.Key => Loc.T("mcm.rowKey", Control.Label, Display),
		_ => Loc.T("mcm.row", Control.Label, Display)
	};

	/// <summary>The value as it should be read out: on/off for a toggle, a key name for a binding.</summary>
	private string Display
	{
		get
		{
			if (Value.Length == 0) return Loc.T("mcm.valueUnset");

			return Control.Kind switch
			{
				McmControlKind.Toggle => McmValue.McmToggleIsOn(Value) ? Loc.T("mcm.on") : Loc.T("mcm.off"),
				McmControlKind.Key => McmValue.McmKeyText(Value),
				_ => Value
			};
		}
	}
}

/// <summary>One row: a setting and its current value, read as "Label: value".</summary>
public sealed class StardewRow
{
	public required StardewSetting Setting { get; init; }
	public override string ToString() => Loc.T("sdvconfig.row", Setting.Label, Display);

	private string Display => Setting.Value.Length > 0 ? Setting.Value : Loc.T("sdvconfig.valueEmpty");
}

/// <summary>
/// How an MCM value is said out loud, separated from the row that says it so the wording is the same
/// wherever a setting is shown.
/// </summary>
public static class McmValue
{
	/// <summary>
	/// An MCM key binding as a person would say it: "Page Up" rather than "33,0".
	///
	/// MCM stores a binding as a Windows virtual-key code and a modifier bitfield, which is exactly right for
	/// the game and useless to read aloud. The controls list already decodes these; this is the same decoding,
	/// so a key reads identically wherever it appears.
	/// </summary>
	public static string McmKeyText(string raw)
	{
		string[] parts = raw.Split(',');
		if (!int.TryParse(parts[0].Trim(), out int virtualKey) || virtualKey <= 0)
			return Loc.T("mcm.keyUnassigned");

		int modifiers = parts.Length > 1 && int.TryParse(parts[1].Trim(), out int m) ? m : 0;
		return VirtualKeys.DecodeVirtualKey(virtualKey, modifiers);
	}

	/// <summary>
	/// MCM writes a toggle as 0 or 1, but a hand-edited file may hold true or false. Both are understood so a
	/// setting someone edited by hand still reads correctly.
	/// </summary>
	public static bool McmToggleIsOn(string value) =>
		value.Trim() is "1" || value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
}
