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

	/// <summary>
	/// Skyrim SE / Fallout 4 mod priority order captured when the profile was saved (mod folder names,
	/// highest priority first). <c>null</c> for Stardew profiles or profiles saved before load-order
	/// support; applying such a profile leaves the current priority order untouched.
	/// </summary>
	public List<string>? ModPriority { get; set; }

	/// <summary>
	/// Skyrim SE / Fallout 4 plugin load order captured when the profile was saved (plugin file names in
	/// load order). <c>null</c> for Stardew profiles or profiles saved before plugin-order support; applying
	/// such a profile leaves the current plugin order untouched.
	/// </summary>
	public List<string>? PluginOrder { get; set; }

	/// <inheritdoc/>
	public override string ToString() => Name;
}
