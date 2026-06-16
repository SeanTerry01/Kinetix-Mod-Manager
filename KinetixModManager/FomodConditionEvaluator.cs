namespace KinetixModManager;

/// <summary>
/// Evaluates FOMOD composite conditions (<see cref="FomodDependency"/>) against the set of flags
/// accumulated so far and the state of already-installed plugin files. Used to decide step
/// visibility, conditional plugin types, and conditional file installs.
/// </summary>
public static class FomodConditionEvaluator
{
	/// <summary>
	/// Returns whether <paramref name="dependency"/> is satisfied. A <c>null</c> dependency (no
	/// condition) and an empty condition set both count as satisfied. Unknown condition kinds the
	/// parser dropped (e.g. game/manager version) simply do not constrain the result.
	/// </summary>
	public static bool Evaluate(
		FomodDependency? dependency,
		IReadOnlyDictionary<string, string> flags,
		Func<string, FomodFileState> fileState)
	{
		if (dependency == null) return true;

		bool requireAll = dependency.Operator == FomodDependencyOperator.And;
		bool any = false;

		foreach (FomodFlag flag in dependency.FlagDependencies)
		{
			bool ok = flags.TryGetValue(flag.Name, out string? current)
				? string.Equals(current, flag.Value, StringComparison.OrdinalIgnoreCase)
				: string.IsNullOrEmpty(flag.Value); // an unset flag satisfies a check for the empty value
			if (requireAll && !ok) return false;
			any |= ok;
		}

		foreach (FomodFileDependency file in dependency.FileDependencies)
		{
			bool ok = fileState(file.File) == file.State;
			if (requireAll && !ok) return false;
			any |= ok;
		}

		foreach (FomodDependency nested in dependency.NestedDependencies)
		{
			bool ok = Evaluate(nested, flags, fileState);
			if (requireAll && !ok) return false;
			any |= ok;
		}

		// And: nothing failed above. Or: at least one passed (vacuously true when there are no children).
		bool hasChildren = dependency.FlagDependencies.Count > 0
			|| dependency.FileDependencies.Count > 0
			|| dependency.NestedDependencies.Count > 0;
		return requireAll || !hasChildren || any;
	}

	/// <summary>
	/// Resolves a plugin's effective <see cref="FomodPluginType"/>: the first <c>dependencyType</c>
	/// pattern whose condition matches wins, otherwise the descriptor's default type.
	/// </summary>
	public static FomodPluginType ResolveType(
		FomodPlugin plugin,
		IReadOnlyDictionary<string, string> flags,
		Func<string, FomodFileState> fileState)
	{
		foreach (FomodTypePattern pattern in plugin.TypeDescriptor.Patterns)
			if (Evaluate(pattern.Dependencies, flags, fileState))
				return pattern.Type;

		return plugin.TypeDescriptor.DefaultType;
	}
}
