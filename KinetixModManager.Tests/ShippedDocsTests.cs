using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Guards what goes out in the installer from the repository's <c>docs</c> folder.
///
/// That folder holds two unrelated kinds of file. Three of them are accessibility-mod documentation the Mod
/// Documentation viewer (F3) reads at runtime, so they have to ship. The rest are notes to ourselves —
/// development plans, objectives, a pre-release checklist — which have no business on a user's machine.
///
/// The csproj keeps them apart by naming the three explicitly rather than globbing the folder, and the
/// installer copies whatever the build produced. That is easy to undo by accident: adding a fourth access doc
/// and reaching for <c>..\docs\*.md</c> would quietly start shipping the planning notes with it. This test is
/// here so that edit fails in the build instead of in a release.
/// </summary>
public class ShippedDocsTests
{
    /// <summary>Files in <c>docs</c> that must never reach a user's machine.</summary>
    private static readonly string[] DeveloperOnly =
    {
        "FUTURE_DEVELOPMENT_PLANS.txt",
        "OBJECTIVES.md",
        "PRE_RELEASE_CHECKLIST.md"
    };

    [Fact]
    public void TheProjectNamesTheDocsItShipsInsteadOfGlobbingTheFolder()
    {
        string csproj = File.ReadAllText(AppProjectFile());

        foreach (Match include in Regex.Matches(csproj, @"Include=""([^""]*\.\.\\docs\\[^""]*)"""))
        {
            string value = include.Groups[1].Value;
            Assert.False(value.Contains('*'),
                "The csproj globs the docs folder (" + value + "). Everything matched is copied into the " +
                "publish output, and the installer ships that folder wholesale — so a wildcard here puts our " +
                "own planning notes on every user's machine. List the files to ship by name.");
        }
    }

    [Fact]
    public void NoDeveloperOnlyDocIsListedForShipping()
    {
        string csproj = File.ReadAllText(AppProjectFile());

        foreach (string name in DeveloperOnly)
            Assert.DoesNotContain(name, csproj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryDocTheProjectShipsStillExists()
    {
        // A renamed or deleted access doc would otherwise fail silently: the build copies nothing, and the F3
        // viewer simply has no documentation for that game.
        string root = RepositoryRoot();
        string csproj = File.ReadAllText(AppProjectFile());

        var listed = Regex.Matches(csproj, @"\.\.\\docs\\([^"";]+)")
            .Select(m => m.Groups[1].Value.Trim())
            .Where(n => n.Length > 0 && !n.Contains('*'))
            .Distinct()
            .ToList();

        Assert.NotEmpty(listed);
        foreach (string name in listed)
            Assert.True(File.Exists(Path.Combine(root, "docs", name)),
                $"The project ships docs\\{name}, which does not exist.");
    }

    private static string AppProjectFile()
    {
        string path = Path.Combine(RepositoryRoot(), "KinetixModManager", "KinetixModManager.csproj");
        Assert.True(File.Exists(path), "Could not find the app project at " + path);
        return path;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx")))
            dir = dir.Parent;

        Assert.True(dir != null, "Could not find the repository root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
