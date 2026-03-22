using System;
using System.Collections.Generic;

namespace StardewAccessibleManager
{
    public class ModProfile
    {
        public string Name { get; set; } = null!;
        // Dictionary where Key is Mod UniqueID and Value is IsEnabled state
        public Dictionary<string, bool> ModStates { get; set; } = new Dictionary<string, bool>();
        
        // Optional theme to switch to when this profile is applied
        public string? ThemeOverride { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }
}