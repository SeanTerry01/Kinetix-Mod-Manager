using System.Collections.Generic;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="BethesdaLayout.Plan"/> — the pure decision behind root-folder-mod support: which archive files
/// belong in the game's Data folder and which belong under the reserved <c>Root/</c> subfolder (the game root).
/// </summary>
public class BethesdaLayoutTests
{
    private static Dictionary<string, string> PlanMap(params string[] paths) =>
        BethesdaLayout.Plan(paths).ToDictionary(e => e.Source, e => (e.IsRoot ? "ROOT:" : "DATA:") + e.Dest);

    [Fact]
    public void PlainDataMod_IsUnchanged_AndHasNoRootContent()
    {
        var plan = BethesdaLayout.Plan(new[] { "textures/a.dds", "meshes/b.nif", "MyMod.esp" });

        Assert.False(BethesdaLayout.HasRootContent(plan));
        // Every file stays exactly where it was (Data content, top level of the mod folder).
        Assert.All(plan, e => Assert.False(e.IsRoot));
        Assert.All(plan, e => Assert.Equal(e.Source, e.Dest));
    }

    [Fact]
    public void EnbLooseFiles_GoToRoot()
    {
        var map = PlanMap("d3d11.dll", "enblocal.ini", "enbseries.ini", "enbseries/weather.ini");

        Assert.Equal("ROOT:Root/d3d11.dll", map["d3d11.dll"]);
        Assert.Equal("ROOT:Root/enblocal.ini", map["enblocal.ini"]);
        Assert.Equal("ROOT:Root/enbseries.ini", map["enbseries.ini"]);
        Assert.Equal("ROOT:Root/enbseries/weather.ini", map["enbseries/weather.ini"]);
    }

    [Fact]
    public void ExplicitDataFolder_IsUnwrapped_AndSiblingsGoToRoot()
    {
        var map = PlanMap("Data/textures/a.dds", "Data/MyPlugin.esp", "d3d11.dll", "tool.exe");

        Assert.Equal("DATA:textures/a.dds", map["Data/textures/a.dds"]);   // Data\ prefix stripped
        Assert.Equal("DATA:MyPlugin.esp", map["Data/MyPlugin.esp"]);
        Assert.Equal("ROOT:Root/d3d11.dll", map["d3d11.dll"]);              // beside Data\ -> game root
        Assert.Equal("ROOT:Root/tool.exe", map["tool.exe"]);
    }

    [Fact]
    public void ExplicitRootFolder_IsKeptAsRoot()
    {
        var map = PlanMap("Root/skse64_loader.exe", "textures/a.dds");

        Assert.Equal("ROOT:Root/skse64_loader.exe", map["Root/skse64_loader.exe"]);
        Assert.Equal("DATA:textures/a.dds", map["textures/a.dds"]);
    }

    [Fact]
    public void DataAssetFolder_BesideExplicitData_StaysData_NotRoot()
    {
        // A malformed archive with both Data\ and a loose textures\ folder: the real asset folder must not be
        // misfiled to the game root just because it sits beside a Data\ folder.
        var map = PlanMap("Data/x.esp", "textures/a.dds", "readme_notes.txt");

        Assert.Equal("DATA:textures/a.dds", map["textures/a.dds"]);
        Assert.Equal("ROOT:Root/readme_notes.txt", map["readme_notes.txt"]); // unknown sibling of Data\ -> root
    }

    [Fact]
    public void TopLevelExe_WithoutDataStructure_GoesToRoot()
    {
        var map = PlanMap("FNIS.exe", "meshes/actors/rig.hkx");

        Assert.Equal("ROOT:Root/FNIS.exe", map["FNIS.exe"]);
        Assert.Equal("DATA:meshes/actors/rig.hkx", map["meshes/actors/rig.hkx"]);
    }

    [Fact]
    public void DeepDllInsideDataTree_IsNotTreatedAsRootInjector()
    {
        // An SKSE plugin DLL living under its proper Data path must stay Data — only top-level loose DLLs are
        // treated as game-root injectors.
        var plan = BethesdaLayout.Plan(new[] { "skse/plugins/somemod.dll" });
        var e = Assert.Single(plan);
        Assert.False(e.IsRoot);
        Assert.Equal("skse/plugins/somemod.dll", e.Dest);
    }

    [Fact]
    public void BackslashPaths_AreHandled()
    {
        // Plan normalises separators to '/', so the reported Source (map key) is the normalised form.
        var map = PlanMap(@"Data\textures\a.dds", @"d3d11.dll");
        Assert.Equal("DATA:textures/a.dds", map["Data/textures/a.dds"]);
        Assert.Equal("ROOT:Root/d3d11.dll", map["d3d11.dll"]);
    }
}
