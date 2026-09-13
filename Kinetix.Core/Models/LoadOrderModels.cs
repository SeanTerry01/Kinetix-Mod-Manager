// Rows and entries behind the load-order screens: mod priority, plugin order and Creations.
//
// Lifted out of Form1 by Phase 2 of the core split. These are plain data - what a row holds and how
// it reads aloud - so they belong beside the rules rather than inside the window that happens to
// show them today. While they were private to Form1 no second front end could name them at all.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>One row of the Mod Priority list: a mod and its current conflict standing.</summary>
public sealed class PriorityEntry
{
	public string Key = "";
	public string Display = "";
	public bool Enabled;
	public string Summary = "";
	public override string ToString() => Summary;
}

/// <summary>One row of the Plugin Order list: a plugin file and its master/light classification.</summary>
public sealed class PluginEntry
{
	public string Name = "";
	public bool Master;
	public bool Light;
	public string Summary = "";
	public override string ToString() => Summary;
}

/// <summary>One row of the Creations list: an installed Creation plugin and its current state.</summary>
public sealed class CreationEntry
{
	/// <summary>The Creation's plugin file name in the game Data folder (e.g. ccBGSSSE001-Fish.esm).</summary>
	public string File = "";
	public bool Master;
	public bool Light;
	public bool Active;
	public string Summary = "";
	public override string ToString() => Summary;
}

/// <summary>Serializable snapshot of a game's load order: the mod priority order and the plugin order.</summary>
public sealed class LoadOrderExport
{
	/// <summary>Bumped if the file shape ever changes incompatibly, so old files can be detected.</summary>
	public string FormatVersion { get; set; } = "1";
	/// <summary>Game id this order belongs to ("SkyrimSE" / "Fallout4"), so it is not applied to the wrong game.</summary>
	public string Game { get; set; } = "";
	/// <summary>App version that produced the file (informational only).</summary>
	public string AppVersion { get; set; } = "";
	public DateTime ExportedAtUtc { get; set; }
	/// <summary>Mod folder keys, highest conflict priority first. Mirrors <see cref="AppSettings.ModPriority"/>.</summary>
	public List<string> ModPriority { get; set; } = new List<string>();
	/// <summary>Active plugin file names in load order. Mirrors <see cref="AppSettings.PluginOrder"/>.</summary>
	public List<string> PluginOrder { get; set; } = new List<string>();
}

/// <summary>One row in the rules list.</summary>
public sealed class RuleItem
{
	public required LoadOrderRule Rule;
	public string Summary = "";
	public override string ToString() => Summary;
}

/// <summary>One row in the conflict resolver: a contested file path (its winner/summary is recomputed live).</summary>
public sealed class ConflictRow
{
	public required string Path;
	public string Summary = "";
	public override string ToString() => Summary;
}

/// <summary>A snapshot of how many regular and light plugin slots the active load order is using.</summary>
public readonly struct PluginSlotUsage
{
	public int RegularUsed { get; init; }
	public int LightUsed { get; init; }
	public int RegularRemaining => Math.Max(0, PluginSlots.RegularPluginCap - RegularUsed);
	public bool RegularOver => RegularUsed > PluginSlots.RegularPluginCap;
	public bool RegularNear => RegularUsed >= PluginSlots.RegularPluginNear; // true once over, too
	public bool LightOver => LightUsed > PluginSlots.LightPluginCap;
	public bool LightNear => LightUsed >= PluginSlots.LightPluginNear;
	/// <summary>True when anything is worth warning about (regular or light pool near/over its cap).</summary>
	public bool AnyConcern => RegularNear || LightNear;
}
