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
