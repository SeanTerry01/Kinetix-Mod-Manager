// The tree the controls list is read from: one mod's keybindings, grouped into sections.
//
// Lifted out of Form1 by Phase 2 of the core split. These are plain data - what a row holds and how
// it reads aloud - so they belong beside the rules rather than inside the window that happens to
// show them today. While they were private to Form1 no second front end could name them at all.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>One control line: a parsed key plus its description, or (when <see cref="Key"/> is null) a
/// plain info/section-intro line shown verbatim.</summary>
public class KbEntry
{
	public string? Key { get; set; }
	public string Text { get; set; } = "";
}

/// <summary>A named group of control lines (e.g. a README sub-section like "Scanner" or "Combat").
/// An empty <see cref="Name"/> is the un-sectioned bucket shown directly under its parent.</summary>
public class KbSection
{
	public string Name { get; set; } = "";
	public bool Gamepad { get; set; }
	public List<KbEntry> Entries { get; } = new();
}

/// <summary>A source of controls in the list (a mod, or the base-game reference), holding its sections.</summary>
public class ModKeybinds
{
	public string Name { get; }
	public string ConfigPath { get; set; } = "";
	public List<KbSection> Sections { get; } = new();

	public ModKeybinds(string name, string configPath = "")
	{
		Name = name;
		ConfigPath = configPath;
	}

	public bool HasContent => Sections.Any(s => s.Entries.Count > 0);

	public override string ToString() => Name;
}

/// <summary>One entry in the drill-down list: either a leaf (a key/info line) or a group with children.
/// <see cref="Owner"/> is the mod the node belongs to, so the config editor works from anywhere inside it.</summary>
public class NavNode
{
	public string Label { get; }
	public ModKeybinds? Owner { get; }
	public List<NavNode> Children { get; } = new();

	/// <summary>An intro/info line: shown and read aloud, but not counted in the level's "x of y" position.</summary>
	public bool IsInfo { get; set; }

	public NavNode(string label, ModKeybinds? owner)
	{
		Label = label;
		Owner = owner;
	}

	public override string ToString() => Label;
}
