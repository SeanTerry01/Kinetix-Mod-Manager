using System;
using System.Collections.Generic;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers the FOMOD condition engine: And/Or/nested composites, flag and file dependencies, and
/// conditional plugin-type resolution — the logic ISC never exercised.
/// </summary>
public class FomodConditionEvaluatorTests
{
    private static readonly IReadOnlyDictionary<string, string> NoFlags = new Dictionary<string, string>();

    /// <summary>A file-state lookup over a fixed name→state table; unknown names are Missing.</summary>
    private static Func<string, FomodFileState> Files(params (string name, FomodFileState state)[] entries)
    {
        var map = new Dictionary<string, FomodFileState>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, state) in entries) map[name] = state;
        return f => map.TryGetValue(f, out FomodFileState s) ? s : FomodFileState.Missing;
    }

    private static FomodDependency Flag(string name, string value, FomodDependencyOperator op = FomodDependencyOperator.And)
    {
        var dep = new FomodDependency { Operator = op };
        dep.FlagDependencies.Add(new FomodFlag { Name = name, Value = value });
        return dep;
    }

    [Fact]
    public void NullDependency_IsSatisfied()
    {
        Assert.True(FomodConditionEvaluator.Evaluate(null, NoFlags, Files()));
    }

    [Fact]
    public void EmptyComposite_IsSatisfied_ForBothOperators()
    {
        Assert.True(FomodConditionEvaluator.Evaluate(new FomodDependency { Operator = FomodDependencyOperator.And }, NoFlags, Files()));
        Assert.True(FomodConditionEvaluator.Evaluate(new FomodDependency { Operator = FomodDependencyOperator.Or }, NoFlags, Files()));
    }

    [Fact]
    public void And_RequiresEveryChild()
    {
        var dep = new FomodDependency { Operator = FomodDependencyOperator.And };
        dep.FlagDependencies.Add(new FomodFlag { Name = "a", Value = "1" });
        dep.FlagDependencies.Add(new FomodFlag { Name = "b", Value = "1" });

        var bothSet = new Dictionary<string, string> { ["a"] = "1", ["b"] = "1" };
        var oneSet = new Dictionary<string, string> { ["a"] = "1" };

        Assert.True(FomodConditionEvaluator.Evaluate(dep, bothSet, Files()));
        Assert.False(FomodConditionEvaluator.Evaluate(dep, oneSet, Files()));
    }

    [Fact]
    public void Or_RequiresAnyChild()
    {
        var dep = new FomodDependency { Operator = FomodDependencyOperator.Or };
        dep.FlagDependencies.Add(new FomodFlag { Name = "a", Value = "1" });
        dep.FlagDependencies.Add(new FomodFlag { Name = "b", Value = "1" });

        Assert.True(FomodConditionEvaluator.Evaluate(dep, new Dictionary<string, string> { ["b"] = "1" }, Files()));
        Assert.False(FomodConditionEvaluator.Evaluate(dep, new Dictionary<string, string> { ["a"] = "0" }, Files()));
    }

    [Fact]
    public void FlagDependency_UnsetFlag_MatchesEmptyExpectedValue()
    {
        // A flag that was never set satisfies a check for the empty value (FOMOD semantics).
        Assert.True(FomodConditionEvaluator.Evaluate(Flag("never", ""), NoFlags, Files()));
        Assert.False(FomodConditionEvaluator.Evaluate(Flag("never", "on"), NoFlags, Files()));
    }

    [Fact]
    public void FlagDependency_IsCaseInsensitiveOnValue()
    {
        var flags = new Dictionary<string, string> { ["state"] = "ON" };
        Assert.True(FomodConditionEvaluator.Evaluate(Flag("state", "on"), flags, Files()));
    }

    [Theory]
    [InlineData(FomodFileState.Active, FomodFileState.Active, true)]
    [InlineData(FomodFileState.Active, FomodFileState.Missing, false)]
    [InlineData(FomodFileState.Missing, FomodFileState.Missing, true)]
    [InlineData(FomodFileState.Inactive, FomodFileState.Active, false)]
    public void FileDependency_MatchesRequiredState(FomodFileState actual, FomodFileState required, bool expected)
    {
        var dep = new FomodDependency { Operator = FomodDependencyOperator.And };
        dep.FileDependencies.Add(new FomodFileDependency { File = "x.esp", State = required });
        Assert.Equal(expected, FomodConditionEvaluator.Evaluate(dep, NoFlags, Files(("x.esp", actual))));
    }

    [Fact]
    public void Nested_AndContainingOr_EvaluatesRecursively()
    {
        // (file Active) AND (flag a=1 OR flag b=1)
        var inner = new FomodDependency { Operator = FomodDependencyOperator.Or };
        inner.FlagDependencies.Add(new FomodFlag { Name = "a", Value = "1" });
        inner.FlagDependencies.Add(new FomodFlag { Name = "b", Value = "1" });

        var outer = new FomodDependency { Operator = FomodDependencyOperator.And };
        outer.FileDependencies.Add(new FomodFileDependency { File = "Skyrim.esm", State = FomodFileState.Active });
        outer.NestedDependencies.Add(inner);

        var files = Files(("Skyrim.esm", FomodFileState.Active));

        Assert.True(FomodConditionEvaluator.Evaluate(outer, new Dictionary<string, string> { ["b"] = "1" }, files));
        Assert.False(FomodConditionEvaluator.Evaluate(outer, new Dictionary<string, string> { ["c"] = "1" }, files)); // inner Or fails
        Assert.False(FomodConditionEvaluator.Evaluate(outer, new Dictionary<string, string> { ["a"] = "1" }, Files())); // file missing
    }

    [Fact]
    public void ResolveType_FirstMatchingPatternWins_ElseDefault()
    {
        var plugin = new FomodPlugin();
        plugin.TypeDescriptor.DefaultType = FomodPluginType.Optional;
        plugin.TypeDescriptor.Patterns.Add(new FomodTypePattern
        {
            Dependencies = Flag("hi", "1"),
            Type = FomodPluginType.Recommended
        });

        Assert.Equal(FomodPluginType.Recommended,
            FomodConditionEvaluator.ResolveType(plugin, new Dictionary<string, string> { ["hi"] = "1" }, Files()));
        Assert.Equal(FomodPluginType.Optional,
            FomodConditionEvaluator.ResolveType(plugin, NoFlags, Files()));
    }

    [Fact]
    public void ResolveType_NotUsable_WhenRequiredFileMissing()
    {
        var plugin = new FomodPlugin();
        plugin.TypeDescriptor.DefaultType = FomodPluginType.Optional;
        var pattern = new FomodTypePattern { Type = FomodPluginType.NotUsable };
        var dep = new FomodDependency { Operator = FomodDependencyOperator.And };
        dep.FileDependencies.Add(new FomodFileDependency { File = "missing.esp", State = FomodFileState.Missing });
        pattern.Dependencies = dep;
        plugin.TypeDescriptor.Patterns.Add(pattern);

        Assert.Equal(FomodPluginType.NotUsable, FomodConditionEvaluator.ResolveType(plugin, NoFlags, Files()));
    }
}
