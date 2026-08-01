using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// Represents a single installed mod, a mod group, a Nexus search result, or a pending update.
/// The <see cref="IsGroup"/>, <see cref="IsSubMod"/>, <see cref="IsSearchResult"/>, and
/// <see cref="IsUpdateResult"/> flags determine how the instance is displayed and handled.
/// </summary>
public class GameMod
{
	/// <summary>True when this entry represents a group header containing <see cref="SubMods"/>.</summary>
	public bool IsGroup { get; set; }

	/// <summary>True when a group's sub-mods are currently shown in the list.</summary>
	public bool IsExpanded { get; set; }

	/// <summary>Display name for a group header row.</summary>
	public string GroupName { get; set; } = "";

	/// <summary>Child mods contained within this group.</summary>
	public List<GameMod> SubMods { get; set; } = new List<GameMod>();

	/// <summary>True when this mod is a child entry inside a group.</summary>
	public bool IsSubMod { get; set; }

	/// <summary>Human-readable mod name from manifest.json.</summary>
	public string Name { get; set; } = "";

	/// <summary>Mod author from manifest.json.</summary>
	public string Author { get; set; } = "";

	/// <summary>Installed version string from manifest.json.</summary>
	public string Version { get; set; } = "";

	/// <summary>Short description from manifest.json.</summary>
	public string Description { get; set; } = "";

	/// <summary>UniqueID field from manifest.json (used for dependency matching).</summary>
	public string UniqueId { get; set; } = "";

	/// <summary>Nexus Mods numeric mod ID, or <c>null</c> if not mapped.</summary>
	public string? NexusID { get; set; }

	/// <summary>GitHub repository path in 'owner/repo' format, or <c>null</c> if not mapped.</summary>
	public string? GitHubRepo { get; set; }

	/// <summary>Absolute path to the mod's folder on disk.</summary>
	public string FolderPath { get; set; } = "";

	/// <summary>Dependencies declared in manifest.json, annotated with presence and version status.</summary>
	public List<ModDependency> Dependencies { get; set; } = new List<ModDependency>();

	/// <summary>User-assigned or auto-detected category (e.g. "Expansion", "Crafting").</summary>
	public string Category { get; set; } = "Uncategorized";

	/// <summary>Personal free-text note the user attached to this mod, spoken on selection; empty if none.</summary>
	public string Note { get; set; } = "";

	/// <summary>Latest version available on Nexus Mods, populated during update checks.</summary>
	public string? LatestVersion { get; set; }

	/// <summary>Whether the mod folder is in the active Mods directory (not the Disabled folder).</summary>
	public bool IsEnabled { get; set; } = true;

	/// <summary>True when this instance was populated from a Nexus search result.</summary>
	public bool IsSearchResult { get; set; }

	/// <summary>
	/// How many times the mod has been downloaded, and how many people endorsed it, as reported by Nexus.
	/// <c>-1</c> means not known — nothing on disk records either, so they are only filled in for search
	/// results. Together they are the quickest read on whether a mod is widely used and well thought of, which
	/// otherwise means leaving the manager and opening the mod's page.
	/// </summary>
	public long Downloads { get; set; } = -1;

	/// <inheritdoc cref="Downloads"/>
	public long Endorsements { get; set; } = -1;

	/// <summary>True when this instance represents a pending update in the Updates tab.</summary>
	public bool IsUpdateResult { get; set; }

	/// <summary>
	/// True when the mod's manifest declares update keys but they are all blank or malformed (e.g.
	/// <c>"UpdateKeys": [""]</c> or <c>["Nexus: "]</c> with no id). The mod can't be version-checked, but the
	/// cause is an author's typo rather than a missing field — the Update Coverage report says which.
	/// </summary>
	public bool HasBlankUpdateKey { get; set; }

	/// <inheritdoc/>
	public override string ToString()
	{
		if (IsGroup)
		{
			string value = (IsExpanded ? "Expanded" : "Collapsed");
			return $"Mod Group: {GroupName}. Contains {SubMods.Count} mods. {value}. Press Right or Plus to expand, Left or Minus to collapse.";
		}
		string value2 = (IsSubMod ? "Sub-mod: " : "");
		string value3 = "";
		if (!IsSearchResult && !IsUpdateResult)
		{
			value3 = (IsEnabled ? "Enabled" : "Disabled") + ". ";
		}
		string value4 = "";
		bool flag = false;
		foreach (ModDependency dependency in Dependencies)
		{
			if (dependency.IsRequired && (!dependency.IsPresent || !dependency.IsEnabled))
			{
				flag = true;
				break;
			}
		}
		if (flag)
		{
			value4 = " Warning: Missing required dependencies.";
		}
		if (IsUpdateResult)
		{
			return $"{Name} by {Author}. Current: {Version}. Latest: {LatestVersion}.{value4}";
		}
		if (IsSearchResult)
		{
			// Downloads and endorsements come BEFORE the summary on purpose. They are the two numbers that
			// decide whether a result is worth more of your time, and putting them first means you can move on
			// to the next result without sitting through a description you have already ruled out. It also keeps
			// them clear of the row's length limit, which the summary can push against on its own.
			string popularity = "";
			if (Downloads >= 0 || Endorsements >= 0)
			{
				string downloads = Downloads >= 0 ? $"{Downloads:N0} downloads" : "";
				string endorsements = Endorsements >= 0 ? $"{Endorsements:N0} endorsements" : "";
				string joined = string.Join(", ", new[] { downloads, endorsements }.Where(p => p.Length > 0));
				if (joined.Length > 0) popularity = joined + ". ";
			}

			// The summary here is however much of it Nexus returns, which for a long one is NOT all of it:
			// the API's summary field arrives already truncated at roughly 240 characters, often mid-word.
			// Nothing can recover the rest — the full text lives in the mod's description (Ctrl+Shift+I).
			return $"{Name} (ID: {NexusID}). {popularity}{Description}";
		}
		string noteSuffix = string.IsNullOrEmpty(Note) ? "" : $" Note: {Note}.";
		return $"{value2}{Name} by {Author}, version {Version}. Category: {Category}. {value3}{value4}{noteSuffix}";
	}
}
