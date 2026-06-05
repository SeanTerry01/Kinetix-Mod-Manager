using System.Collections.Generic;

namespace KinetixModManager;

/// <summary>A saved mod profile: a named snapshot of which mods are enabled or disabled.</summary>
public class ModProfile
{
	/// <summary>Display name of the profile.</summary>
	public string Name { get; set; } = "";

	/// <summary>
	/// Maps mod folder names to their enabled state at the time the profile was saved.
	/// </summary>
	public Dictionary<string, bool> ModStates { get; set; } = new Dictionary<string, bool>();

	/// <summary>
	/// Audio theme to activate when this profile is applied, or <c>null</c> to keep the current theme.
	/// </summary>
	public string? ThemeOverride { get; set; }

	/// <inheritdoc/>
	public override string ToString() => Name;
}
