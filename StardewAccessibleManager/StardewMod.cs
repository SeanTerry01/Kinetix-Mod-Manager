using System.Collections.Generic;

namespace StardewAccessibleManager;

public class StardewMod
{
	public bool IsGroup { get; set; }

	public bool IsExpanded { get; set; }

	public string GroupName { get; set; } = "";

	public List<StardewMod> SubMods { get; set; } = new List<StardewMod>();

	public bool IsSubMod { get; set; }

	public string Name { get; set; }

	public string Author { get; set; }

	public string Version { get; set; }

	public string Description { get; set; }

	public string UniqueId { get; set; }

	public string? NexusID { get; set; }

	public string FolderPath { get; set; }

	public List<ModDependency> Dependencies { get; set; } = new List<ModDependency>();

	public string Category { get; set; } = "Uncategorized";

	public string? LatestVersion { get; set; }

	public bool IsEnabled { get; set; } = true;

	public bool IsSearchResult { get; set; }

	public bool IsUpdateResult { get; set; }

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
			return $"{Name} (ID: {NexusID}). {Description}";
		}
		return $"{value2}{Name} by {Author}, version {Version}. Category: {Category}. {value3}{value4}";
	}
}
