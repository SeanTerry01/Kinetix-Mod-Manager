using System;
using System.Collections.Generic;

namespace StardewAccessibleManager
{
    public class ModDependency
    {
        public string UniqueId { get; set; } = null!;
        public string? MinimumVersion { get; set; }
        public bool IsRequired { get; set; } = true;
        public bool IsPresent { get; set; } = false;
        public bool IsNewEnough { get; set; } = true;
        public bool IsEnabled { get; set; } = true;
    }

    public class StardewMod
    {
        // Hierarchical Grouping
        public bool IsGroup { get; set; } = false;
        public bool IsExpanded { get; set; } = false;
        public string GroupName { get; set; } = "";
        public List<StardewMod> SubMods { get; set; } = new List<StardewMod>();
        public bool IsSubMod { get; set; } = false;

        // Basic info from the manifest.json
        public string Name { get; set; } = null!;
        public string Author { get; set; } = null!;
        public string Version { get; set; } = null!;
        public string Description { get; set; } = null!;
        public string UniqueId { get; set; } = null!;
        public string? NexusID { get; set; }
        public string FolderPath { get; set; } = null!;
        public List<ModDependency> Dependencies { get; set; } = new List<ModDependency>();

        // New Category property
        public string Category { get; set; } = "Uncategorized";

        // Info we will get from the Nexus API later
        public string? LatestVersion { get; set; }
        public bool IsEnabled { get; set; } = true;

        // This tells the Screen Reader what to say when highlighting the mod in a list
        public override string ToString()
        {
            if (IsGroup)
            {
                string state = IsExpanded ? "Expanded" : "Collapsed";
                return $"Mod Group: {GroupName}. Contains {SubMods.Count} mods. {state}. Press Right or Plus to expand, Left or Minus to collapse.";
            }

            string prefix = IsSubMod ? "Sub-mod: " : "";
            string status = IsEnabled ? "Enabled" : "Disabled";
            string depStatus = "";
            
            bool hasMissingRequired = false;
            foreach(var dep in Dependencies)
            {
                if (dep.IsRequired && (!dep.IsPresent || !dep.IsEnabled))
                {
                    hasMissingRequired = true;
                    break;
                }
            }

            if (hasMissingRequired)
            {
                depStatus = " Warning: Missing required dependencies.";
            }

            return $"{prefix}{Name} by {Author}, version {Version}. Category: {Category}. {status}.{depStatus}";
        }
    }
}