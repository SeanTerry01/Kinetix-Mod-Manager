using System.Xml.Linq;

namespace KinetixModManager;

/// <summary>
/// Parses a FOMOD <c>ModuleConfig.xml</c> (and optional <c>info.xml</c>) into a <see cref="FomodConfig"/>.
/// Deliberately lenient: real-world FOMODs vary in element casing, indentation, and which optional
/// attributes they include, so missing pieces fall back to FOMOD defaults rather than throwing.
/// Encoding is honoured by the underlying XML reader, so UTF-16 files (common for ModuleConfig.xml)
/// load correctly without special handling.
/// </summary>
public static class FomodParser
{
	/// <summary>Loads and parses a <c>ModuleConfig.xml</c> from disk.</summary>
	public static FomodConfig ParseFile(string moduleConfigPath)
	{
		// LoadOptions.None drops insignificant whitespace; the reader respects the file's encoding decl.
		XDocument doc = XDocument.Load(moduleConfigPath, LoadOptions.None);
		return Parse(doc);
	}

	/// <summary>Parses an already-loaded <c>ModuleConfig.xml</c> document.</summary>
	public static FomodConfig Parse(XDocument doc)
	{
		var config = new FomodConfig();
		XElement? root = doc.Root;
		if (root == null) return config;

		config.ModuleName = ElValue(root, "moduleName") ?? "";

		XElement? required = El(root, "requiredInstallFiles");
		if (required != null)
			config.RequiredInstallFiles.AddRange(ParseFileList(required));

		XElement? steps = El(root, "installSteps");
		if (steps != null)
			foreach (XElement step in Els(steps, "installStep"))
				config.InstallSteps.Add(ParseStep(step));

		XElement? conditional = El(root, "conditionalFileInstalls");
		if (conditional != null)
		{
			// conditionalFileInstalls -> patterns -> pattern -> (dependencies, files)
			XElement patterns = El(conditional, "patterns") ?? conditional;
			foreach (XElement pattern in Els(patterns, "pattern"))
			{
				var ci = new FomodConditionalInstall
				{
					Dependencies = ParseDependencyChild(pattern)
				};
				XElement? files = El(pattern, "files");
				if (files != null) ci.Files.AddRange(ParseFileList(files));
				config.ConditionalInstalls.Add(ci);
			}
		}

		return config;
	}

	/// <summary>Reads Name/Author/Version from a <c>fomod/info.xml</c>; returns empty info on any failure.</summary>
	public static FomodInfo ParseInfo(string infoXmlPath)
	{
		var info = new FomodInfo();
		try
		{
			XElement? root = XDocument.Load(infoXmlPath).Root;
			if (root != null)
			{
				info.Name = ElValue(root, "Name");
				info.Author = ElValue(root, "Author");
				info.Version = ElValue(root, "Version");
			}
		}
		catch { /* info.xml is optional metadata; absence is not fatal */ }
		return info;
	}

	// -------------------------------------------------------------------------
	// Element parsers
	// -------------------------------------------------------------------------

	private static FomodInstallStep ParseStep(XElement stepEl)
	{
		var step = new FomodInstallStep { Name = Attr(stepEl, "name") ?? "" };

		XElement? visible = El(stepEl, "visible");
		if (visible != null) step.Visible = ParseComposite(visible);

		XElement? groups = El(stepEl, "optionalFileGroups");
		if (groups != null)
			foreach (XElement group in Els(groups, "group"))
				step.Groups.Add(ParseGroup(group));

		return step;
	}

	private static FomodGroup ParseGroup(XElement groupEl)
	{
		var group = new FomodGroup
		{
			Name = Attr(groupEl, "name") ?? "",
			Type = ParseEnum(Attr(groupEl, "type"), FomodGroupType.SelectAny)
		};

		XElement? plugins = El(groupEl, "plugins");
		if (plugins != null)
			foreach (XElement plugin in Els(plugins, "plugin"))
				group.Plugins.Add(ParsePlugin(plugin));

		return group;
	}

	private static FomodPlugin ParsePlugin(XElement pluginEl)
	{
		var plugin = new FomodPlugin
		{
			Name = Attr(pluginEl, "name") ?? "",
			Description = (ElValue(pluginEl, "description") ?? "").Trim(),
			ImagePath = Attr(El(pluginEl, "image"), "path")
		};

		XElement? files = El(pluginEl, "files");
		if (files != null) plugin.Files.AddRange(ParseFileList(files));

		XElement? flags = El(pluginEl, "conditionFlags");
		if (flags != null)
			foreach (XElement flag in Els(flags, "flag"))
				plugin.ConditionFlags.Add(new FomodFlag
				{
					Name = Attr(flag, "name") ?? "",
					Value = (flag.Value ?? "").Trim()
				});

		XElement? typeDesc = El(pluginEl, "typeDescriptor");
		if (typeDesc != null) plugin.TypeDescriptor = ParseTypeDescriptor(typeDesc);

		return plugin;
	}

	private static FomodTypeDescriptor ParseTypeDescriptor(XElement typeDescEl)
	{
		var descriptor = new FomodTypeDescriptor();

		// Simple form: <typeDescriptor><type name="Optional"/></typeDescriptor>
		XElement? simple = El(typeDescEl, "type");
		if (simple != null)
		{
			descriptor.DefaultType = ParseEnum(Attr(simple, "name"), FomodPluginType.Optional);
			return descriptor;
		}

		// Conditional form: <dependencyType><defaultType/><patterns><pattern>...
		XElement? depType = El(typeDescEl, "dependencyType");
		if (depType != null)
		{
			descriptor.DefaultType = ParseEnum(Attr(El(depType, "defaultType"), "name"), FomodPluginType.Optional);
			XElement? patterns = El(depType, "patterns");
			if (patterns != null)
				foreach (XElement pattern in Els(patterns, "pattern"))
					descriptor.Patterns.Add(new FomodTypePattern
					{
						Dependencies = ParseDependencyChild(pattern),
						Type = ParseEnum(Attr(El(pattern, "type"), "name"), FomodPluginType.Optional)
					});
		}

		return descriptor;
	}

	private static List<FomodFileItem> ParseFileList(XElement filesEl)
	{
		var items = new List<FomodFileItem>();
		foreach (XElement child in filesEl.Elements())
		{
			string local = child.Name.LocalName.ToLowerInvariant();
			if (local != "file" && local != "folder") continue;

			items.Add(new FomodFileItem
			{
				Source = Attr(child, "source") ?? "",
				// Distinguish an omitted destination (null) from an explicit Data-root one ("").
				Destination = Attr(child, "destination"),
				Priority = ParseInt(Attr(child, "priority"), 0),
				IsFolder = local == "folder"
			});
		}
		return items;
	}

	// -------------------------------------------------------------------------
	// Dependency parsing
	// -------------------------------------------------------------------------

	/// <summary>Parses the <c>dependencies</c> child of a pattern/visible wrapper, if present.</summary>
	private static FomodDependency? ParseDependencyChild(XElement parent)
	{
		XElement? deps = El(parent, "dependencies");
		return deps != null ? ParseComposite(deps) : null;
	}

	/// <summary>
	/// Parses a composite condition element (<c>dependencies</c> or <c>visible</c>). Reads the
	/// <c>operator</c> attribute and walks flag/file/nested children. Unknown condition kinds
	/// (e.g. game/manager version) are skipped and so treated as satisfied.
	/// </summary>
	private static FomodDependency ParseComposite(XElement el)
	{
		var dep = new FomodDependency
		{
			Operator = ParseEnum(Attr(el, "operator"), FomodDependencyOperator.And)
		};

		foreach (XElement child in el.Elements())
		{
			switch (child.Name.LocalName.ToLowerInvariant())
			{
				case "flagdependency":
					dep.FlagDependencies.Add(new FomodFlag
					{
						Name = Attr(child, "flag") ?? "",
						Value = Attr(child, "value") ?? ""
					});
					break;
				case "filedependency":
					dep.FileDependencies.Add(new FomodFileDependency
					{
						File = Attr(child, "file") ?? "",
						State = ParseEnum(Attr(child, "state"), FomodFileState.Active)
					});
					break;
				case "dependencies":
					dep.NestedDependencies.Add(ParseComposite(child));
					break;
			}
		}

		return dep;
	}

	// -------------------------------------------------------------------------
	// Low-level helpers (case-insensitive, namespace-agnostic, fault-tolerant)
	// -------------------------------------------------------------------------

	/// <summary>First child element whose local name matches <paramref name="localName"/> (case-insensitive).</summary>
	private static XElement? El(XElement? parent, string localName) =>
		parent?.Elements().FirstOrDefault(e =>
			string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

	/// <summary>All child elements whose local name matches <paramref name="localName"/> (case-insensitive), in document order.</summary>
	private static IEnumerable<XElement> Els(XElement? parent, string localName) =>
		parent?.Elements().Where(e =>
			string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
		?? Enumerable.Empty<XElement>();

	/// <summary>Trimmed text of the first matching child element, or <c>null</c> if absent.</summary>
	private static string? ElValue(XElement? parent, string localName) => El(parent, localName)?.Value?.Trim();

	/// <summary>Attribute value matched case-insensitively by local name, or <c>null</c>.</summary>
	private static string? Attr(XElement? el, string name) =>
		el?.Attributes().FirstOrDefault(a =>
			string.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value;

	/// <summary>Parses an enum value case-insensitively, returning <paramref name="fallback"/> on null/unknown.</summary>
	private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct =>
		!string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, ignoreCase: true, out TEnum result)
			? result
			: fallback;

	private static int ParseInt(string? value, int fallback) =>
		int.TryParse(value, out int result) ? result : fallback;
}
