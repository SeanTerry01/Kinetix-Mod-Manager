using System.Text.RegularExpressions;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Keeps the version number the same everywhere it appears.
///
/// 1.6.1 shipped calling itself 1.6.0. The version was set in each project file, the release bumped only the
/// app's, and the manager reports the version of Kinetix.Core (<see cref="NexusService.AppVersion"/> reads the
/// assembly it lives in). The About box said 1.6.0 and the update check kept offering 1.6.1 to a copy that
/// already had it. The number now lives in <c>Directory.Build.props</c> alone, and these tests hold the
/// installer script, the change log and the running code to it.
/// </summary>
public class VersionConsistencyTests
{
    [Fact]
    public void TheCodeReportsTheVersionBeingBuilt()
    {
        Assert.Equal(PropsVersion(), NexusService.AppVersion);
    }

    [Fact]
    public void NoProjectSetsItsOwnVersion()
    {
        string root = RepositoryRoot();
        var offenders = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                     && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .Where(p => Regex.IsMatch(File.ReadAllText(p), @"<(Version|AssemblyVersion|FileVersion|VersionPrefix)>"))
            .Select(p => Path.GetRelativePath(root, p))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These projects set their own version, which overrides Directory.Build.props and lets the numbers " +
            "drift apart: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheInstallerHasTheSameVersion()
    {
        string iss = File.ReadAllText(Path.Combine(RepositoryRoot(), "KinetixModManager", "setup.iss"));
        Match m = Regex.Match(iss, @"^AppVersion=(\S+)", RegexOptions.Multiline);
        Assert.True(m.Success, "setup.iss has no AppVersion line.");
        Assert.Equal(PropsVersion(), m.Groups[1].Value);
    }

    [Fact]
    public void TheNewestChangeLogEntryIsForThisVersion()
    {
        // What's New and F2 find the running version's entry by this heading.
        string changelog = File.ReadAllText(Path.Combine(RepositoryRoot(), "CHANGELOG.md"));
        Match m = Regex.Match(changelog, @"^# Version (\S+)", RegexOptions.Multiline);
        Assert.True(m.Success, "CHANGELOG.md has no \"# Version\" heading.");
        Assert.Equal(PropsVersion(), m.Groups[1].Value);
    }

    private static string PropsVersion()
    {
        string props = File.ReadAllText(Path.Combine(RepositoryRoot(), "Directory.Build.props"));
        Match m = Regex.Match(props, @"<Version>(\d+\.\d+\.\d+)</Version>");
        Assert.True(m.Success, "Directory.Build.props has no <Version>X.Y.Z</Version>.");
        return m.Groups[1].Value;
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
