using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="FomodInstaller"/>: per-group default selection, fomod discovery, and the file-staging
/// rules (destination variants, priority overwrite, folder copy, conditional installs, missing sources).
/// </summary>
public class FomodInstallerTests : IDisposable
{
    private readonly string _root;
    private readonly string _staging;
    private static readonly Func<string, FomodFileState> AllMissing = _ => FomodFileState.Missing;

    public FomodInstallerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "FomodTest_" + Path.GetRandomFileName());
        _staging = Path.Combine(Path.GetTempPath(), "FomodStage_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        try { if (Directory.Exists(_staging)) Directory.Delete(_staging, true); } catch { }
    }

    // ----- helpers --------------------------------------------------------

    private void WriteSource(string relative, string content)
    {
        string full = Path.Combine(_root, relative.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private string ReadStaged(string relative) =>
        File.ReadAllText(Path.Combine(_staging, relative.Replace('\\', Path.DirectorySeparatorChar)));

    private bool StagedExists(string relative) =>
        File.Exists(Path.Combine(_staging, relative.Replace('\\', Path.DirectorySeparatorChar)));

    private static FomodPlugin Plugin(string name, FomodPluginType type, params FomodFileItem[] files)
    {
        var p = new FomodPlugin { Name = name };
        p.TypeDescriptor.DefaultType = type;
        p.Files.AddRange(files);
        return p;
    }

    private static FomodFileItem File_(string source, string? destination, int priority = 0, bool folder = false) =>
        new() { Source = source, Destination = destination, Priority = priority, IsFolder = folder };

    private static FomodGroup Group(string name, FomodGroupType type, params FomodPlugin[] plugins)
    {
        var g = new FomodGroup { Name = name, Type = type };
        g.Plugins.AddRange(plugins);
        return g;
    }

    private static FomodConfig Config(params FomodGroup[] groups)
    {
        var c = new FomodConfig();
        var step = new FomodInstallStep { Name = "s" };
        step.Groups.AddRange(groups);
        c.InstallSteps.Add(step);
        return c;
    }

    private static FomodSelection Select(FomodConfig config, params FomodPlugin[] chosen)
    {
        var s = new FomodSelection();
        foreach (var p in chosen)
        {
            s.SelectedPlugins.Add(p);
            foreach (var f in p.ConditionFlags) s.Flags[f.Name] = f.Value;
        }
        return s;
    }

    // ----- ComputeDefaultSelection ---------------------------------------

    [Fact]
    public void Default_SelectExactlyOne_PrefersRecommended_ElseFirst()
    {
        var first = Plugin("first", FomodPluginType.Optional);
        var rec = Plugin("rec", FomodPluginType.Recommended);
        var config = Config(Group("g", FomodGroupType.SelectExactlyOne, first, rec));

        FomodSelection sel = FomodInstaller.ComputeDefaultSelection(config, AllMissing);
        Assert.Equal(new[] { rec }, sel.SelectedPlugins);

        // With nothing recommended, it falls back to the first usable option.
        var a = Plugin("a", FomodPluginType.Optional);
        var b = Plugin("b", FomodPluginType.Optional);
        FomodSelection sel2 = FomodInstaller.ComputeDefaultSelection(
            Config(Group("g", FomodGroupType.SelectExactlyOne, a, b)), AllMissing);
        Assert.Equal(new[] { a }, sel2.SelectedPlugins);
    }

    [Fact]
    public void Default_SelectAtMostOne_PicksNothing_UnlessRecommendedOrRequired()
    {
        var a = Plugin("a", FomodPluginType.Optional);
        var b = Plugin("b", FomodPluginType.Optional);
        FomodSelection none = FomodInstaller.ComputeDefaultSelection(
            Config(Group("g", FomodGroupType.SelectAtMostOne, a, b)), AllMissing);
        Assert.Empty(none.SelectedPlugins);

        var rec = Plugin("rec", FomodPluginType.Recommended);
        FomodSelection one = FomodInstaller.ComputeDefaultSelection(
            Config(Group("g", FomodGroupType.SelectAtMostOne, a, rec)), AllMissing);
        Assert.Equal(new[] { rec }, one.SelectedPlugins);
    }

    [Fact]
    public void Default_SelectAtLeastOne_PicksFirst_WhenNoneRecommended()
    {
        var a = Plugin("a", FomodPluginType.Optional);
        var b = Plugin("b", FomodPluginType.Optional);
        FomodSelection sel = FomodInstaller.ComputeDefaultSelection(
            Config(Group("g", FomodGroupType.SelectAtLeastOne, a, b)), AllMissing);
        Assert.Equal(new[] { a }, sel.SelectedPlugins);
    }

    [Fact]
    public void Default_SelectAll_TakesEveryUsableOption_ExcludesNotUsable()
    {
        var a = Plugin("a", FomodPluginType.Optional);
        var bad = Plugin("bad", FomodPluginType.NotUsable);
        FomodSelection sel = FomodInstaller.ComputeDefaultSelection(
            Config(Group("g", FomodGroupType.SelectAll, a, bad)), AllMissing);
        Assert.Equal(new[] { a }, sel.SelectedPlugins);
    }

    [Fact]
    public void Default_SelectAny_TakesOnlyRequiredAndRecommended()
    {
        var opt = Plugin("opt", FomodPluginType.Optional);
        var rec = Plugin("rec", FomodPluginType.Recommended);
        var req = Plugin("req", FomodPluginType.Required);
        FomodSelection sel = FomodInstaller.ComputeDefaultSelection(
            Config(Group("g", FomodGroupType.SelectAny, opt, rec, req)), AllMissing);
        Assert.Equal(new HashSet<FomodPlugin> { rec, req }, sel.SelectedPlugins);
    }

    [Fact]
    public void Default_SkipsHiddenSteps()
    {
        var visiblePlugin = Plugin("vis", FomodPluginType.Recommended);
        var hiddenPlugin = Plugin("hid", FomodPluginType.Recommended);

        var config = new FomodConfig();
        var s1 = new FomodInstallStep { Name = "shown" };
        s1.Groups.Add(Group("g1", FomodGroupType.SelectAny, visiblePlugin));
        var s2 = new FomodInstallStep { Name = "hidden" };
        var vis = new FomodDependency { Operator = FomodDependencyOperator.And };
        vis.FlagDependencies.Add(new FomodFlag { Name = "never", Value = "1" });
        s2.Visible = vis;
        s2.Groups.Add(Group("g2", FomodGroupType.SelectAny, hiddenPlugin));
        config.InstallSteps.Add(s1);
        config.InstallSteps.Add(s2);

        FomodSelection sel = FomodInstaller.ComputeDefaultSelection(config, AllMissing);
        Assert.Contains(visiblePlugin, sel.SelectedPlugins);
        Assert.DoesNotContain(hiddenPlugin, sel.SelectedPlugins);
    }

    // ----- BuildStaging ---------------------------------------------------

    [Fact]
    public void Staging_HandlesAllDestinationVariants()
    {
        WriteSource("rootfile.txt", "ROOT");
        WriteSource("mirror\\deep\\m.txt", "MIRROR");
        WriteSource("src.txt", "EXPLICIT");

        var config = new FomodConfig();
        config.RequiredInstallFiles.Add(File_("rootfile.txt", destination: ""));          // -> Data root, filename only
        config.RequiredInstallFiles.Add(File_("mirror\\deep\\m.txt", destination: null));  // -> mirror full source path
        config.RequiredInstallFiles.Add(File_("src.txt", destination: "sub\\renamed.txt")); // -> explicit path

        FomodInstaller.BuildStaging(config, new FomodSelection(), _root, _staging, AllMissing);

        Assert.Equal("ROOT", ReadStaged("rootfile.txt"));
        Assert.Equal("MIRROR", ReadStaged("mirror\\deep\\m.txt"));
        Assert.Equal("EXPLICIT", ReadStaged("sub\\renamed.txt"));
    }

    [Fact]
    public void Staging_HigherPriority_OverwritesLower_RegardlessOfOrder()
    {
        WriteSource("low.txt", "LOW");
        WriteSource("high.txt", "HIGH");

        var config = new FomodConfig();
        // Add the high-priority item FIRST to prove ordering is by priority, not document order.
        config.RequiredInstallFiles.Add(File_("high.txt", destination: "result.txt", priority: 100));
        config.RequiredInstallFiles.Add(File_("low.txt", destination: "result.txt", priority: 0));

        FomodInstaller.BuildStaging(config, new FomodSelection(), _root, _staging, AllMissing);

        Assert.Equal("HIGH", ReadStaged("result.txt"));
    }

    [Fact]
    public void Staging_CopiesFolderRecursively()
    {
        WriteSource("tex\\a.dds", "A");
        WriteSource("tex\\nested\\b.dds", "B");

        var config = new FomodConfig();
        config.RequiredInstallFiles.Add(File_("tex", destination: "textures", folder: true));

        int copied = FomodInstaller.BuildStaging(config, new FomodSelection(), _root, _staging, AllMissing);

        Assert.Equal(2, copied);
        Assert.Equal("A", ReadStaged("textures\\a.dds"));
        Assert.Equal("B", ReadStaged("textures\\nested\\b.dds"));
    }

    [Fact]
    public void Staging_ConditionalInstall_FiresOnlyWhenDependenciesMatch()
    {
        WriteSource("cond.txt", "COND");

        FomodConfig MakeConfig()
        {
            var c = new FomodConfig();
            var ci = new FomodConditionalInstall();
            var dep = new FomodDependency { Operator = FomodDependencyOperator.And };
            dep.FlagDependencies.Add(new FomodFlag { Name = "go", Value = "on" });
            ci.Dependencies = dep;
            ci.Files.Add(File_("cond.txt", destination: "cond.txt"));
            c.ConditionalInstalls.Add(ci);
            return c;
        }

        // Flag set -> installed.
        var on = new FomodSelection();
        on.Flags["go"] = "on";
        FomodInstaller.BuildStaging(MakeConfig(), on, _root, _staging, AllMissing);
        Assert.True(StagedExists("cond.txt"));

        // Fresh staging, flag absent -> not installed.
        Directory.Delete(_staging, true);
        FomodInstaller.BuildStaging(MakeConfig(), new FomodSelection(), _root, _staging, AllMissing);
        Assert.False(StagedExists("cond.txt"));
    }

    [Fact]
    public void Staging_SelectedPluginFiles_AreInstalled_UnselectedAreNot()
    {
        WriteSource("yes.txt", "YES");
        WriteSource("no.txt", "NO");

        var chosen = Plugin("chosen", FomodPluginType.Optional, File_("yes.txt", destination: "yes.txt"));
        var skipped = Plugin("skipped", FomodPluginType.Optional, File_("no.txt", destination: "no.txt"));
        var config = Config(Group("g", FomodGroupType.SelectAny, chosen, skipped));

        FomodInstaller.BuildStaging(config, Select(config, chosen), _root, _staging, AllMissing);

        Assert.True(StagedExists("yes.txt"));
        Assert.False(StagedExists("no.txt"));
    }

    [Fact]
    public void Staging_MissingSource_IsSkipped_NotThrown()
    {
        var config = new FomodConfig();
        config.RequiredInstallFiles.Add(File_("does_not_exist.txt", destination: "x.txt"));

        int copied = FomodInstaller.BuildStaging(config, new FomodSelection(), _root, _staging, AllMissing);

        Assert.Equal(0, copied);
        Assert.False(StagedExists("x.txt"));
    }

    // ----- TryFindFomod ---------------------------------------------------

    [Fact]
    public void TryFindFomod_LocatesNestedFomodFolder_AndResolvesRoot()
    {
        string wrapped = Path.Combine(_root, "Wrapper");
        Directory.CreateDirectory(Path.Combine(wrapped, "fomod"));
        File.WriteAllText(Path.Combine(wrapped, "fomod", "ModuleConfig.xml"), "<config/>");
        File.WriteAllText(Path.Combine(wrapped, "fomod", "info.xml"), "<fomod/>");

        bool found = FomodInstaller.TryFindFomod(_root, out string moduleConfig, out string? info, out string fomodRoot);

        Assert.True(found);
        Assert.EndsWith("ModuleConfig.xml", moduleConfig);
        Assert.NotNull(info);
        Assert.Equal(wrapped, fomodRoot); // the folder containing the fomod directory
    }

    [Fact]
    public void TryFindFomod_ReturnsFalse_WhenNoFomodPresent()
    {
        Assert.False(FomodInstaller.TryFindFomod(_root, out _, out _, out _));
    }
}
