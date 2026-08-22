using System.Collections.Generic;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>Covers the offline detection used by the file-conflict and requirements reports.</summary>
public class ModReportTests
{
    private static GameMod Mod(string name, string uid, bool enabled = true) =>
        new GameMod { Name = name, UniqueId = uid, IsEnabled = enabled };

    [Fact]
    public void DuplicateUniqueIds_FlagsSharedId_WithAllSharingMods()
    {
        var mods = new List<GameMod>
        {
            Mod("Alpha", "shared.id"),
            Mod("Beta",  "shared.id"),
            Mod("Gamma", "unique.id"),
        };

        var dups = ModHealth.FindDuplicateUniqueIds(mods);

        var one = Assert.Single(dups);
        Assert.Equal("shared.id", one.UniqueId);
        Assert.Equal(new[] { "Alpha", "Beta" }, one.ModNames.OrderBy(n => n).ToArray());
    }

    [Fact]
    public void DuplicateUniqueIds_IsCaseInsensitive()
    {
        var mods = new List<GameMod> { Mod("Alpha", "Shared.ID"), Mod("Beta", "shared.id") };
        Assert.Single(ModHealth.FindDuplicateUniqueIds(mods));
    }

    [Fact]
    public void DuplicateUniqueIds_EmptyWhenAllUnique()
    {
        var mods = new List<GameMod> { Mod("Alpha", "a"), Mod("Beta", "b"), Mod("Gamma", "") };
        Assert.Empty(ModHealth.FindDuplicateUniqueIds(mods));
    }

    [Fact]
    public void DuplicateUniqueIds_IgnoresGroupHeaders()
    {
        var mods = new List<GameMod>
        {
            new GameMod { Name = "Group", IsGroup = true, UniqueId = "x" },
            Mod("Real", "x"),
        };
        Assert.Empty(ModHealth.FindDuplicateUniqueIds(mods));
    }
}

/// <summary>
/// Covers the dependency resolution behind the requirements report — the part that decides whether a mod a
/// manifest asks for counts as installed.
/// </summary>
public class DependencyResolutionTests
{
    /// <summary>Version comparison stand-in matching the app's own numeric-segment rule.</summary>
    private static bool IsNewer(string? current, string? target)
    {
        if (string.IsNullOrEmpty(target)) return false;
        if (string.IsNullOrEmpty(current)) return true;
        string[] a = current.Split('.'), b = target.Split('.');
        for (int i = 0; i < System.Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length && int.TryParse(a[i], out int p) ? p : 0;
            int y = i < b.Length && int.TryParse(b[i], out int q) ? q : 0;
            if (y > x) return true;
            if (x > y) return false;
        }
        return false;
    }

    private static GameMod Mod(string name, string uid, string version = "1.0.0", bool enabled = true) =>
        new GameMod { Name = name, UniqueId = uid, Version = version, IsEnabled = enabled };

    private static GameMod Needs(string name, string depId, string? minVersion = null)
    {
        var mod = new GameMod { Name = name, UniqueId = name + ".id", Version = "1.0.0", IsEnabled = true };
        mod.Dependencies.Add(new ModDependency { UniqueId = depId, MinimumVersion = minVersion, IsRequired = true });
        return mod;
    }

    [Fact]
    public void Dependency_IsSatisfied_WhenTheIdsDifferOnlyInCase()
    {
        // The real case: PFM calls itself "Digus.ProducerFrameworkMod"; the PPJA packs ask for
        // "DIGUS.ProducerFrameworkMod". SMAPI loads them; the requirements report used to say "not installed".
        var pfm = Mod("Producer Framework Mod", "Digus.ProducerFrameworkMod", "1.9.8");
        var pack = Needs("[PFM] Artisan Valley", "DIGUS.ProducerFrameworkMod");
        var mods = new List<GameMod> { pfm, pack };

        ModHealth.ResolveDependencies(mods, IsNewer);

        ModDependency dep = pack.Dependencies[0];
        Assert.True(dep.IsPresent);
        Assert.True(dep.IsEnabled);
        Assert.True(dep.IsNewEnough);
    }

    [Fact]
    public void Dependency_IsSatisfied_WhenAnIdCarriesStrayWhitespace()
    {
        var host = Mod("Json Assets", "spacechase0.JsonAssets");
        var pack = Needs("Pack", " spacechase0.JsonAssets ");
        ModHealth.ResolveDependencies(new List<GameMod> { host, pack }, IsNewer);

        Assert.True(pack.Dependencies[0].IsPresent);
    }

    [Fact]
    public void Dependency_IsMissing_WhenNoModDeclaresThatId()
    {
        var pack = Needs("Pack", "someone.NotInstalled");
        ModHealth.ResolveDependencies(new List<GameMod> { Mod("Other", "someone.Else"), pack }, IsNewer);

        Assert.False(pack.Dependencies[0].IsPresent);
    }

    [Fact]
    public void Dependency_ReportsDisabled_WhenTheOnlyCopyIsSwitchedOff()
    {
        var host = Mod("Host", "some.Host", enabled: false);
        var pack = Needs("Pack", "SOME.host");
        ModHealth.ResolveDependencies(new List<GameMod> { host, pack }, IsNewer);

        Assert.True(pack.Dependencies[0].IsPresent);
        Assert.False(pack.Dependencies[0].IsEnabled);
    }

    [Fact]
    public void Dependency_PrefersTheEnabledCopy_WhenTheModIsInstalledTwice()
    {
        // Whichever copy the scan happened to find first, the enabled one is what the game will load.
        var off = Mod("Host (old)", "some.Host", "1.0.0", enabled: false);
        var on  = Mod("Host", "some.Host", "2.0.0");
        var pack = Needs("Pack", "some.Host", minVersion: "2.0.0");
        ModHealth.ResolveDependencies(new List<GameMod> { off, on, pack }, IsNewer);

        Assert.True(pack.Dependencies[0].IsEnabled);
        Assert.True(pack.Dependencies[0].IsNewEnough);
    }

    [Fact]
    public void Dependency_IsOutdated_WhenTheInstalledVersionIsBelowTheMinimum()
    {
        var host = Mod("Host", "some.Host", "1.2.0");
        var pack = Needs("Pack", "some.Host", minVersion: "1.3.0");
        ModHealth.ResolveDependencies(new List<GameMod> { host, pack }, IsNewer);

        Assert.True(pack.Dependencies[0].IsPresent);
        Assert.False(pack.Dependencies[0].IsNewEnough);
    }

    [Fact]
    public void Dependency_IsNewEnough_WhenTheVersionExactlyMeetsTheMinimum()
    {
        var host = Mod("Host", "some.Host", "1.3.0");
        var pack = Needs("Pack", "some.Host", minVersion: "1.3.0");
        ModHealth.ResolveDependencies(new List<GameMod> { host, pack }, IsNewer);

        Assert.True(pack.Dependencies[0].IsNewEnough);
    }

    [Fact]
    public void Dependency_IgnoresGroupHeaders_WhichAreListRowsNotMods()
    {
        var group = new GameMod { Name = "General", IsGroup = true, UniqueId = "some.Host", IsEnabled = true };
        var pack = Needs("Pack", "some.Host");
        ModHealth.ResolveDependencies(new List<GameMod> { group, pack }, IsNewer);

        Assert.False(pack.Dependencies[0].IsPresent);
    }
}
