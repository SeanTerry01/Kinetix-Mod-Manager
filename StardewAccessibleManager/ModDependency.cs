namespace StardewAccessibleManager;

public class ModDependency
{
	public string UniqueId { get; set; }

	public string? MinimumVersion { get; set; }

	public bool IsRequired { get; set; } = true;

	public bool IsPresent { get; set; }

	public bool IsNewEnough { get; set; } = true;

	public bool IsEnabled { get; set; } = true;
}
