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

	/// <summary>
	/// The install key of the copy of the game it was saved in, or <c>null</c> for one saved before profiles were kept per
	/// game, when every game's profiles sat in one folder and a Skyrim profile could be applied to Fallout 4.
	/// </summary>
	public string? Game { get; set; }

	/// <summary>The file it was read from, so it is deleted from where it is. Not saved.</summary>
	[Newtonsoft.Json.JsonIgnore]
	public string SourcePath { get; set; } = "";

	/// <inheritdoc/>
	public override string ToString() => Name;
}
