namespace KinetixModManager;

/// <summary>
/// Object model for a FOMOD scripted installer (<c>fomod/ModuleConfig.xml</c>, schema
/// ModConfig 5.0). Populated by <see cref="FomodParser"/> and consumed by the install wizard
/// and file-application step. The model deliberately mirrors the XML so the parser stays a
/// dumb mapping and all interpretation (defaults, conditions, overwrite order) lives in the
/// evaluator/installer.
/// </summary>
public class FomodConfig
{
	/// <summary>Display name from <c>moduleName</c>. Falls back to the archive name if absent.</summary>
	public string ModuleName { get; set; } = "";

	/// <summary>Files copied unconditionally before any option is considered (<c>requiredInstallFiles</c>).</summary>
	public List<FomodFileItem> RequiredInstallFiles { get; } = new();

	/// <summary>The ordered wizard pages (<c>installSteps</c>).</summary>
	public List<FomodInstallStep> InstallSteps { get; } = new();

	/// <summary>Extra files installed at the end when their conditions match (<c>conditionalFileInstalls</c>).</summary>
	public List<FomodConditionalInstall> ConditionalInstalls { get; } = new();
}

/// <summary>A single wizard page (<c>installStep</c>) holding one or more option groups.</summary>
public class FomodInstallStep
{
	public string Name { get; set; } = "";

	/// <summary>Condition that decides whether this step is shown (<c>visible</c>), or <c>null</c> for always.</summary>
	public FomodDependency? Visible { get; set; }

	public List<FomodGroup> Groups { get; } = new();
}

/// <summary>How many options a <see cref="FomodGroup"/> allows the user to pick.</summary>
public enum FomodGroupType
{
	/// <summary>Radio buttons; exactly one must be selected.</summary>
	SelectExactlyOne,
	/// <summary>Radio buttons plus a "None"; zero or one may be selected.</summary>
	SelectAtMostOne,
	/// <summary>Checkboxes; at least one must be selected.</summary>
	SelectAtLeastOne,
	/// <summary>Checkboxes; any number (including none) may be selected.</summary>
	SelectAny,
	/// <summary>All options forced selected (checked and disabled).</summary>
	SelectAll
}

/// <summary>A set of mutually-related options (<c>group</c>) with a selection rule.</summary>
public class FomodGroup
{
	public string Name { get; set; } = "";
	public FomodGroupType Type { get; set; } = FomodGroupType.SelectAny;
	public List<FomodPlugin> Plugins { get; } = new();
}

/// <summary>A single selectable option (<c>plugin</c>) — one checkbox or radio button.</summary>
public class FomodPlugin
{
	public string Name { get; set; } = "";
	public string Description { get; set; } = "";

	/// <summary>Preview image path inside the archive; ignored by the accessible UI but parsed for completeness.</summary>
	public string? ImagePath { get; set; }

	/// <summary>Files/folders copied when this option is selected. May be empty (a valid "install nothing" choice).</summary>
	public List<FomodFileItem> Files { get; } = new();

	/// <summary>Flags this option sets when selected, consumed by later <see cref="FomodDependency"/> checks.</summary>
	public List<FomodFlag> ConditionFlags { get; } = new();

	/// <summary>Determines the option's default/forced state (<c>typeDescriptor</c>).</summary>
	public FomodTypeDescriptor TypeDescriptor { get; set; } = new();
}

/// <summary>The state a <see cref="FomodPlugin"/> presents in the UI.</summary>
public enum FomodPluginType
{
	/// <summary>Always installed; shown checked and disabled.</summary>
	Required,
	/// <summary>Default off; user may enable.</summary>
	Optional,
	/// <summary>Default on; user may disable.</summary>
	Recommended,
	/// <summary>Cannot be selected; shown disabled.</summary>
	NotUsable,
	/// <summary>Selectable but flagged as a poor choice for the current setup.</summary>
	CouldBeUsable
}

/// <summary>
/// Resolves a plugin's <see cref="FomodPluginType"/>. Either a fixed <see cref="DefaultType"/>
/// (simple <c>&lt;type&gt;</c>) or a <c>dependencyType</c> whose <see cref="Patterns"/> override the
/// default when their conditions match.
/// </summary>
public class FomodTypeDescriptor
{
	public FomodPluginType DefaultType { get; set; } = FomodPluginType.Optional;

	/// <summary>Conditional overrides, evaluated in order; the first matching pattern wins.</summary>
	public List<FomodTypePattern> Patterns { get; } = new();
}

/// <summary>One conditional type override inside a <c>dependencyType</c>.</summary>
public class FomodTypePattern
{
	public FomodDependency? Dependencies { get; set; }
	public FomodPluginType Type { get; set; } = FomodPluginType.Optional;
}

/// <summary>A file or folder copy instruction (<c>file</c> / <c>folder</c>).</summary>
public class FomodFileItem
{
	/// <summary>Path inside the archive, as written in the XML (back-slashed, arbitrary case).</summary>
	public string Source { get; set; } = "";

	/// <summary>
	/// Destination relative to the game Data folder. <c>null</c> means the attribute was omitted
	/// (FOMOD spec: mirror the source path); <c>""</c> means an explicit Data-root install.
	/// </summary>
	public string? Destination { get; set; }

	/// <summary>Higher priority overwrites lower when destinations collide. Defaults to 0.</summary>
	public int Priority { get; set; }

	/// <summary><c>true</c> for a <c>&lt;folder&gt;</c> (recursive copy), <c>false</c> for a single <c>&lt;file&gt;</c>.</summary>
	public bool IsFolder { get; set; }
}

/// <summary>A flag name/value pair set by a selected option.</summary>
public class FomodFlag
{
	public string Name { get; set; } = "";
	public string Value { get; set; } = "";
}

/// <summary>One <c>conditionalFileInstalls</c> pattern: files installed when <see cref="Dependencies"/> match.</summary>
public class FomodConditionalInstall
{
	public FomodDependency? Dependencies { get; set; }
	public List<FomodFileItem> Files { get; } = new();
}

/// <summary>Combines child conditions with And/Or.</summary>
public enum FomodDependencyOperator
{
	And,
	Or
}

/// <summary>The required state of a plugin file referenced by a <c>fileDependency</c>.</summary>
public enum FomodFileState
{
	Active,
	Inactive,
	Missing
}

/// <summary>
/// A composite condition (<c>dependencies</c> / <c>visible</c> / pattern <c>dependencies</c>).
/// Holds flag, file, and nested conditions combined by <see cref="Operator"/>. Game/manager
/// version dependencies are intentionally not modelled; they are treated as satisfied.
/// </summary>
public class FomodDependency
{
	public FomodDependencyOperator Operator { get; set; } = FomodDependencyOperator.And;

	/// <summary>Required flag values (<c>flagDependency</c>): flag name → expected value.</summary>
	public List<FomodFlag> FlagDependencies { get; } = new();

	/// <summary>Required plugin states (<c>fileDependency</c>): plugin file name → required state.</summary>
	public List<FomodFileDependency> FileDependencies { get; } = new();

	/// <summary>Nested composite conditions (<c>dependencies</c> inside <c>dependencies</c>).</summary>
	public List<FomodDependency> NestedDependencies { get; } = new();
}

/// <summary>A single <c>fileDependency</c>: a plugin file that must be in a given state.</summary>
public class FomodFileDependency
{
	public string File { get; set; } = "";
	public FomodFileState State { get; set; } = FomodFileState.Active;
}

/// <summary>
/// The outcome of running a FOMOD installer: which options the user (or the auto-default logic)
/// chose, and the flags those choices set. Produced by <see cref="FomodInstaller.ComputeDefaultSelection"/>
/// or the install wizard, and consumed by <see cref="FomodInstaller.BuildStaging"/>.
/// </summary>
public class FomodSelection
{
	/// <summary>The plugins whose files should be installed.</summary>
	public HashSet<FomodPlugin> SelectedPlugins { get; } = new();

	/// <summary>Final flag values set by the selected plugins, for conditional file installs.</summary>
	public Dictionary<string, string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Metadata read from <c>fomod/info.xml</c> (Name/Author/Version), used to fill the mod manifest.</summary>
public class FomodInfo
{
	public string? Name { get; set; }
	public string? Author { get; set; }
	public string? Version { get; set; }
}
