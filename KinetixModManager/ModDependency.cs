namespace KinetixModManager;

/// <summary>Describes a single dependency entry from a mod's manifest.json.</summary>
public class ModDependency
{
	/// <summary>The UniqueID of the required mod.</summary>
	public string UniqueId { get; set; } = "";

	/// <summary>Minimum acceptable version, or <c>null</c> if any version is acceptable.</summary>
	public string? MinimumVersion { get; set; }

	/// <summary>Whether this dependency is required (<c>true</c>) or optional (<c>false</c>).</summary>
	public bool IsRequired { get; set; } = true;

	/// <summary>Whether the dependency mod is present in the mods folder.</summary>
	public bool IsPresent { get; set; }

	/// <summary>Whether the installed version satisfies <see cref="MinimumVersion"/>.</summary>
	public bool IsNewEnough { get; set; } = true;

	/// <summary>Whether the dependency mod is currently enabled.</summary>
	public bool IsEnabled { get; set; } = true;
}
