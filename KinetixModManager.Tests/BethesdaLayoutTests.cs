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

    // ---------------------------------------------------------------------
    // Screen-reader bridge DLLs, which only work sitting beside the game's .exe
    // ---------------------------------------------------------------------

    [Fact]
    public void NvdaClientInItsOwnFolder_IsHoistedBesideTheExe()
    {
        // Skyrim Access's real layout: the DLL is in an "NVDACC" folder, which is not top level, so every other
        // root rule missed it and it was filed as Data content the mod could never load.
        var map = PlanMap("NVDACC/nvdaControllerClient.dll", "SKSE/Plugins/SkyrimAccess.dll");

        Assert.Equal("ROOT:Root/nvdaControllerClient.dll", map["NVDACC/nvdaControllerClient.dll"]);
        // The SKSE plugin itself belongs in Data and must not be dragged along with it.
        Assert.Equal("DATA:SKSE/Plugins/SkyrimAccess.dll", map["SKSE/Plugins/SkyrimAccess.dll"]);
    }

    [Fact]
    public void NvdaClientUnderDataFolder_IsHoistedBesideTheExe()
    {
        // The layout an earlier build of the same mod shipped: inside Data, where unwrapping alone would have
        // left it at Data\Root\ — still not beside the exe.
        var map = PlanMap("Data/Root/nvdaControllerClient.dll", "Data/textures/a.dds");

        Assert.Equal("ROOT:Root/nvdaControllerClient.dll", map["Data/Root/nvdaControllerClient.dll"]);
        Assert.Equal("DATA:textures/a.dds", map["Data/textures/a.dds"]);
    }

    [Fact]
    public void NvdaClientBuriedDeep_IsStillHoisted()
    {
        var map = PlanMap("extras/tools/nvda/x64/nvdaControllerClient64.dll");
        Assert.Equal("ROOT:Root/nvdaControllerClient64.dll", map["extras/tools/nvda/x64/nvdaControllerClient64.dll"]);
    }

    [Fact]
    public void NvdaClientAlreadyAtTopLevel_IsUnchanged()
    {
        var map = PlanMap("nvdaControllerClient.dll");
        Assert.Equal("ROOT:Root/nvdaControllerClient.dll", map["nvdaControllerClient.dll"]);
    }

    [Fact]
    public void FileNameMatchIgnoresCase()
    {
        var map = PlanMap("nvdacc/NVDACONTROLLERCLIENT.DLL");
        Assert.Equal("ROOT:Root/NVDACONTROLLERCLIENT.DLL", map["nvdacc/NVDACONTROLLERCLIENT.DLL"]);
    }

    [Fact]
    public void HoistingAloneMakesItARootFolderMod()
    {
        // The staging path that realises game-root content only runs when the plan has root content in it, so a
        // mod whose only root file is the hoisted DLL has to be recognised as one.
        var plan = BethesdaLayout.Plan(new[] { "NVDACC/nvdaControllerClient.dll", "SKSE/Plugins/SkyrimAccess.dll" });
        Assert.True(BethesdaLayout.HasRootContent(plan));
    }

    [Fact]
    public void BothArchitecturesShipped_TheSixtyFourBitOneIsInstalled()
    {
        // Only one copy can sit beside the exe. Skyrim SE and Fallout 4 are 64-bit, and installing the 32-bit
        // build would fail exactly like the original bug: the game starts and never speaks.
        var map = PlanMap("x86/nvdaControllerClient.dll", "x64/nvdaControllerClient.dll");

        Assert.Equal("ROOT:Root/nvdaControllerClient.dll", map["x64/nvdaControllerClient.dll"]);
        Assert.NotEqual("ROOT:Root/nvdaControllerClient.dll", map["x86/nvdaControllerClient.dll"]);
    }

    [Fact]
    public void TheLosingCopyIsKept_NotDiscarded()
    {
        // An archive must never come out lighter than it went in; the copy that did not win stays where the
        // ordinary rules put it.
        var plan = BethesdaLayout.Plan(new[] { "x86/nvdaControllerClient.dll", "x64/nvdaControllerClient.dll" });

        Assert.Equal(2, plan.Count);
        Assert.Contains(plan, e => e.Source == "x86/nvdaControllerClient.dll");
    }

    [Fact]
    public void WithNoArchitectureHint_TheShallowestCopyWins()
    {
        var map = PlanMap("nvda/nvdaControllerClient.dll", "extras/spare/copies/nvdaControllerClient.dll");
        Assert.Equal("ROOT:Root/nvdaControllerClient.dll", map["nvda/nvdaControllerClient.dll"]);
    }

    [Fact]
    public void DifferentBridgeDllsEachGetTheirOwnDestination()
    {
        // Different names never collide, so a mod shipping both is fully served.
        var map = PlanMap("bin/nvdaControllerClient64.dll", "bin/Tolk.dll", "bin/SAAPI64.dll");

        Assert.Equal("ROOT:Root/nvdaControllerClient64.dll", map["bin/nvdaControllerClient64.dll"]);
        Assert.Equal("ROOT:Root/Tolk.dll", map["bin/Tolk.dll"]);
        Assert.Equal("ROOT:Root/SAAPI64.dll", map["bin/SAAPI64.dll"]);
    }

    [Fact]
    public void ChooseFilesForGameRoot_AcceptsWindowsSeparators()
    {
        // The deployment engine asks this about an installed mod folder, where paths come back with backslashes.
        var chosen = BethesdaLayout.ChooseFilesForGameRoot(new[]
        {
            @"NVDACC\nvdaControllerClient.dll",
            @"SKSE\Plugins\SkyrimAccess.dll",
        });

        Assert.Equal(new[] { "NVDACC/nvdaControllerClient.dll" }, chosen);
    }

    [Fact]
    public void ChooseFilesForGameRoot_StillPicksAFileAlreadyUnderRoot()
    {
        // A mod already staged correctly must give the same answer, so re-deploying one does not move anything.
        var chosen = BethesdaLayout.ChooseFilesForGameRoot(new[] { @"Root\nvdaControllerClient.dll" });
        Assert.Equal(new[] { "Root/nvdaControllerClient.dll" }, chosen);
    }

    [Fact]
    public void ChooseFilesForGameRoot_IgnoresAModWithNoBridgeDll()
    {
        Assert.Empty(BethesdaLayout.ChooseFilesForGameRoot(new[] { @"textures\a.dds", @"MyMod.esp" }));
    }

    [Fact]
    public void AnOrdinaryPluginDllDeepInDataIsStillNotHoisted()
    {
        // The hoist is by exact file name, not "any DLL anywhere" — that would drag every SKSE plugin out of
        // Data and break the mods this feature exists to fix.
        var plan = BethesdaLayout.Plan(new[] { "SKSE/Plugins/po3_Tweaks.dll" });
        Assert.False(Assert.Single(plan).IsRoot);
    }
}
