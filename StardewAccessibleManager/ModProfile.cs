using System.Collections.Generic;

namespace StardewAccessibleManager;

public class ModProfile
{
	public string Name { get; set; }

	public Dictionary<string, bool> ModStates { get; set; } = new Dictionary<string, bool>();

	public string? ThemeOverride { get; set; }

	public override string ToString()
	{
		return Name;
	}
}
