namespace KinetixModManager;

/// <summary>
/// Drives a FOMOD scripted install: locating the <c>fomod</c> folder, computing a default option
/// selection (for silent/automatic installs), and copying the chosen files into a staging folder
/// laid out the way the mod expects under the game's Data directory. The interactive wizard reuses
/// <see cref="BuildStaging"/> with a user-built <see cref="FomodSelection"/>.
/// </summary>
public static class FomodInstaller
{
	/// <summary>
	/// Looks for a <c>fomod/ModuleConfig.xml</c> anywhere under <paramref name="tempDir"/> (archives
	/// sometimes wrap the mod in a top-level folder). On success, <paramref name="fomodRoot"/> is the
	/// directory that contains the <c>fomod</c> folder — the base all file <c>source</c> paths resolve against.
	/// </summary>
	public static bool TryFindFomod(string tempDir, out string moduleConfigPath, out string? infoXmlPath, out string fomodRoot)
	{
		moduleConfigPath = "";
		infoXmlPath = null;
		fomodRoot = tempDir;

		string? configFile = Directory
			.EnumerateFiles(tempDir, "ModuleConfig.xml", SearchOption.AllDirectories)
			.FirstOrDefault(p => string.Equals(
				Path.GetFileName(Path.GetDirectoryName(p)), "fomod", StringComparison.OrdinalIgnoreCase));

		if (configFile == null) return false;

		string fomodFolder = Path.GetDirectoryName(configFile)!;
		moduleConfigPath = configFile;
		fomodRoot = Path.GetDirectoryName(fomodFolder) ?? tempDir;

		string info = Path.Combine(fomodFolder, "info.xml");
		if (File.Exists(info)) infoXmlPath = info;

		return true;
	}

	/// <summary>
	/// Picks a sensible default option for every group without user input: Required options are always
	/// taken, Recommended options are preferred, and each group's selection rule is honoured (e.g. a
	/// SelectExactlyOne group always yields exactly one). Flags from chosen options accumulate so later
	/// steps' visibility and conditional types resolve against them. Used for silent installs and as the
	/// wizard's initial state.
	/// </summary>
	public static FomodSelection ComputeDefaultSelection(FomodConfig config, Func<string, FomodFileState> fileState)
	{
		var selection = new FomodSelection();

		foreach (FomodInstallStep step in config.InstallSteps)
		{
			if (!FomodConditionEvaluator.Evaluate(step.Visible, selection.Flags, fileState))
				continue;

			foreach (FomodGroup group in step.Groups)
			{
				foreach (FomodPlugin chosen in PickGroupDefaults(group, selection.Flags, fileState))
				{
					selection.SelectedPlugins.Add(chosen);
					foreach (FomodFlag flag in chosen.ConditionFlags)
						selection.Flags[flag.Name] = flag.Value;
				}
			}
		}

		return selection;
	}

	/// <summary>Applies a group's selection rule to produce its default chosen options.</summary>
	private static List<FomodPlugin> PickGroupDefaults(
		FomodGroup group,
		IReadOnlyDictionary<string, string> flags,
		Func<string, FomodFileState> fileState)
	{
		// Pair every option with its resolved type once.
		var typed = group.Plugins
			.Select(p => (plugin: p, type: FomodConditionEvaluator.ResolveType(p, flags, fileState)))
			.ToList();

		List<FomodPlugin> Selectable() =>
			typed.Where(t => t.type != FomodPluginType.NotUsable).Select(t => t.plugin).ToList();
		List<FomodPlugin> Required() =>
			typed.Where(t => t.type == FomodPluginType.Required).Select(t => t.plugin).ToList();
		List<FomodPlugin> Recommended() =>
			typed.Where(t => t.type == FomodPluginType.Recommended).Select(t => t.plugin).ToList();

		switch (group.Type)
		{
			case FomodGroupType.SelectAll:
				return Selectable();

			case FomodGroupType.SelectExactlyOne:
			{
				// Exactly one: prefer a required, then a recommended, then the first usable option.
				FomodPlugin? pick = Required().FirstOrDefault()
					?? Recommended().FirstOrDefault()
					?? Selectable().FirstOrDefault();
				return pick != null ? new List<FomodPlugin> { pick } : new List<FomodPlugin>();
			}

			case FomodGroupType.SelectAtMostOne:
			{
				// Zero or one: only auto-select if something is required or recommended.
				FomodPlugin? pick = Required().FirstOrDefault() ?? Recommended().FirstOrDefault();
				return pick != null ? new List<FomodPlugin> { pick } : new List<FomodPlugin>();
			}

			case FomodGroupType.SelectAtLeastOne:
			{
				var picks = Required().Union(Recommended()).ToList();
				if (picks.Count == 0)
				{
					FomodPlugin? first = Selectable().FirstOrDefault();
					if (first != null) picks.Add(first);
				}
				return picks;
			}

			case FomodGroupType.SelectAny:
			default:
				return Required().Union(Recommended()).ToList();
		}
	}

	/// <summary>
	/// Copies the effective file set for <paramref name="selection"/> into <paramref name="stagingDir"/>:
	/// required files first, then each selected option's files (in config order), then any conditional
	/// installs whose flags now match. Items are applied in ascending <c>priority</c> so higher-priority
	/// files overwrite lower ones, matching FOMOD semantics. Returns the number of files written.
	/// </summary>
	public static int BuildStaging(
		FomodConfig config,
		FomodSelection selection,
		string fomodRoot,
		string stagingDir,
		Func<string, FomodFileState> fileState)
	{
		Directory.CreateDirectory(stagingDir);

		var items = new List<FomodFileItem>();
		items.AddRange(config.RequiredInstallFiles);

		// Walk the config in document order so selected options contribute their files predictably.
		foreach (FomodInstallStep step in config.InstallSteps)
			foreach (FomodGroup group in step.Groups)
				foreach (FomodPlugin plugin in group.Plugins)
					if (selection.SelectedPlugins.Contains(plugin))
						items.AddRange(plugin.Files);

		foreach (FomodConditionalInstall conditional in config.ConditionalInstalls)
			if (FomodConditionEvaluator.Evaluate(conditional.Dependencies, selection.Flags, fileState))
				items.AddRange(conditional.Files);

		// Stable order: ascending priority, ties broken by original order, so later wins on collision.
		var ordered = items
			.Select((item, index) => (item, index))
			.OrderBy(x => x.item.Priority)
			.ThenBy(x => x.index)
			.Select(x => x.item);

		int copied = 0;
		foreach (FomodFileItem item in ordered)
			copied += CopyItem(fomodRoot, item, stagingDir);

		return copied;
	}

	/// <summary>Copies a single file/folder item into the staging tree, returning how many files it wrote.</summary>
	private static int CopyItem(string fomodRoot, FomodFileItem item, string stagingDir)
	{
		string? source = ResolveSourcePath(fomodRoot, item.Source);
		if (source == null) return 0; // a source the archive doesn't actually contain — skip rather than fail the whole install

		if (item.IsFolder)
		{
			if (!Directory.Exists(source)) return 0;
			// Folder destination is the directory the source's *contents* land in. Omitted => mirror the source path.
			string destBase = item.Destination == null
				? Path.Combine(stagingDir, NormalizeRelative(item.Source))
				: Path.Combine(stagingDir, NormalizeRelative(item.Destination));

			int count = 0;
			foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
			{
				string relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
				string dest = Path.Combine(destBase, relative);
				CopyFile(file, dest);
				count++;
			}
			return count;
		}

		if (!File.Exists(source)) return 0;
		// File destination is the full target path. Omitted => mirror source; "" => Data root with the source's file name.
		string destPath = item.Destination switch
		{
			null => Path.Combine(stagingDir, NormalizeRelative(item.Source)),
			"" => Path.Combine(stagingDir, Path.GetFileName(source)),
			_ => Path.Combine(stagingDir, NormalizeRelative(item.Destination))
		};
		CopyFile(source, destPath);
		return 1;
	}

	private static void CopyFile(string source, string dest)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
		File.Copy(source, dest, overwrite: true);
	}

	/// <summary>Turns a FOMOD path (back-slashed, possibly with a leading slash) into a safe relative path.</summary>
	private static string NormalizeRelative(string path) =>
		path.Replace('\\', Path.DirectorySeparatorChar)
			.Replace('/', Path.DirectorySeparatorChar)
			.TrimStart(Path.DirectorySeparatorChar);

	/// <summary>
	/// Resolves a FOMOD <c>source</c> path against the archive case-insensitively (FOMOD paths use
	/// back-slashes and arbitrary casing, while the extracted files keep their real names). Returns the
	/// real on-disk path, or <c>null</c> if any path component is missing.
	/// </summary>
	private static string? ResolveSourcePath(string root, string source)
	{
		string[] parts = source.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
		string current = root;

		foreach (string part in parts)
		{
			if (part == ".") continue;

			// Fast path: an exact-case match exists.
			string direct = Path.Combine(current, part);
			if (File.Exists(direct) || Directory.Exists(direct))
			{
				current = direct;
				continue;
			}

			if (!Directory.Exists(current)) return null;
			string? match = Directory
				.EnumerateFileSystemEntries(current)
				.FirstOrDefault(e => string.Equals(Path.GetFileName(e), part, StringComparison.OrdinalIgnoreCase));
			if (match == null) return null;
			current = match;
		}

		return current;
	}
}
