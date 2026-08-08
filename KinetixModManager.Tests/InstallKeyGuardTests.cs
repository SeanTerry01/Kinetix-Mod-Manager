using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Guards the one mistake that would quietly undo install keys: comparing a game string against a bare game id.
///
/// <c>AppSettings.ActiveGame</c> is an install key, not a game id. For nearly every user the two are the same
/// string — they own one copy of each game — so <c>game == "SkyrimSE"</c> keeps working and keeps testing clean
/// right up until someone installs a second copy. Then the key is "SkyrimSE@Gog", the comparison answers no, and
/// the caller takes whatever branch was meant for a different game entirely. Nothing throws. That is exactly the
/// class of silent wrong answer <see cref="GameProfiles"/> was created to stop, and a unit test cannot catch it
/// because the failure lives in code paths no test drives.
///
/// So this reads the sources instead. It is deliberately a lint rather than a behaviour test: the fix is always
/// the same one line — <c>GameProfiles.IsGame(game, GameProfiles.SkyrimSE)</c> — and the point is that a future
/// edit which reintroduces the pattern fails here rather than on a user's machine months later.
/// </summary>
public class InstallKeyGuardTests
{
    /// <summary>
    /// Comparisons against a game-id literal. Deliberately narrow: it matches <c>==</c> and <c>!=</c> against one
    /// of the known ids and nothing else, so it has no false positives to teach people to ignore. Dictionary keys,
    /// switch arms and ids passed as arguments are all legitimate and are left alone.
    /// </summary>
    private static readonly Regex BareComparison = new(
        @"(==|!=)\s*""(StardewValley|SkyrimSE|Fallout4|MoonlightPeaks|Witcher3)""",
        RegexOptions.Compiled);

    /// <summary>
    /// <c>GameProfiles.cs</c> defines the ids and is where the one legitimate comparison lives; the test project
    /// asserts on literal keys on purpose.
    /// </summary>
    private static readonly HashSet<string> Exempt = new(StringComparer.OrdinalIgnoreCase)
    {
        "GameProfiles.cs"
    };

    [Fact]
    public void NoSourceFileComparesAGameStringToABareGameId()
    {
        var offences = new List<string>();

        foreach (string file in AppSourceFiles())
        {
            if (Exempt.Contains(Path.GetFileName(file))) continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!BareComparison.IsMatch(lines[i])) continue;
                offences.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.True(offences.Count == 0,
            "A game string is being compared to a bare game id. An install key for a second copy of the game " +
            "(\"SkyrimSE@Gog\") will not match, and the branch will be skipped silently. Use " +
            "GameProfiles.IsGame(game, GameProfiles.SkyrimSE) — or GameProfiles.BaseId(game) as a switch " +
            "subject — instead:\n  " + string.Join("\n  ", offences));
    }

    [Fact]
    public void EveryGameIdIsFreeOfTheInstallKeySeparator()
    {
        // An id containing '@' would make BaseId split it in the wrong place, so every key built from it would
        // name a game that does not exist. Cheap to assert, and it keeps the separator safe for a sixth game.
        foreach (GameProfile profile in GameProfiles.All)
            Assert.DoesNotContain(GameProfiles.InstallKeySeparator, profile.Id);
    }

    /// <summary>
    /// The app project's <c>.cs</c> files. Walks up from the test binary to the repository root (the folder
    /// holding the solution) rather than assuming a build layout, so this keeps working under <c>dotnet test</c>,
    /// the IDE, and a published test run alike.
    /// </summary>
    private static IEnumerable<string> AppSourceFiles()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx")))
            dir = dir.Parent;

        Assert.True(dir != null, "Could not find the repository root from " + AppContext.BaseDirectory);

        string appDir = Path.Combine(dir!.FullName, "KinetixModManager");
        Assert.True(Directory.Exists(appDir), "Could not find the app project at " + appDir);

        // Top level only: bin/ and obj/ hold generated copies that would report the same line twice.
        string[] files = Directory.GetFiles(appDir, "*.cs", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(files);
        return files;
    }
}
