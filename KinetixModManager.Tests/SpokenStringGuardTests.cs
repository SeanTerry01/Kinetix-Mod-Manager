using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Guards the phrases the manager speaks against a key that has no text behind it.
///
/// <para>
/// <c>Loc.T</c> falls back to returning the key itself when it is not in the language file. Nothing errors and
/// nothing looks wrong on screen for a sighted developer, but the screen reader dutifully reads the key aloud —
/// "n x m dot bad link" — at the exact moment the user needed a sentence. A typo in a key is therefore not a
/// cosmetic mistake in this app; it is a spoken message replaced by gibberish, and the compiler cannot see it
/// because the key is just a string.
/// </para>
/// </summary>
public class SpokenStringGuardTests
{
    /// <summary>
    /// Keys assembled at runtime from a prefix plus a name — a contrast scheme, a text size, a sound event. The
    /// literal in the source is only the first half, so it can never be found in the language file as it stands.
    /// Each entry here is a prefix whose full keys are verified by the two tests below instead.
    /// </summary>
    private static readonly string[] BuiltAtRuntimePrefixes =
    {
        "settings.contrast.",
        "settings.textSize.",
        "sound."
    };

    [Fact]
    public void EveryPhraseTheAppAsksForExistsInEnglish()
    {
        HashSet<string> known = EnglishKeys();
        var missing = new List<string>();

        foreach (string file in AppSourceFiles())
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in Regex.Matches(lines[i], @"Loc\.T\(""([^""]+)"""))
                {
                    string key = match.Groups[1].Value;
                    if (known.Contains(key)) continue;
                    if (BuiltAtRuntimePrefixes.Contains(key)) continue;

                    missing.Add($"{Path.GetFileName(file)}:{i + 1}  {key}");
                }
            }
        }

        Assert.True(missing.Count == 0,
            "These keys are asked for in the code but are not in lang/en.json, so the screen reader will read the " +
            "key itself aloud instead of a sentence. Add each one to lang/en.json:\n  " +
            string.Join("\n  ", missing));
    }

    [Fact]
    public void EveryRuntimeBuiltPrefixStillHasKeysBehindIt()
    {
        // The prefixes above are excused from the check, so this makes the excuse conditional: if a rename ever
        // empties one of them, the exemption stops being harmless and this fails instead of hiding it.
        HashSet<string> known = EnglishKeys();

        foreach (string prefix in BuiltAtRuntimePrefixes)
            Assert.True(known.Any(k => k.StartsWith(prefix, StringComparison.Ordinal)),
                $"No key in lang/en.json begins with \"{prefix}\", so the code building keys from it can no longer " +
                "find any text. Either the keys were renamed or the prefix is obsolete.");
    }

    [Fact]
    public void NoPhraseIsDefinedTwice()
    {
        // A duplicated key is legal JSON and the last one silently wins, so two people can disagree about the
        // wording of one message and both believe they changed it.
        string path = EnglishFilePath();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new List<string>();

        foreach (Match match in Regex.Matches(File.ReadAllText(path), @"^\s*""([^""]+)""\s*:", RegexOptions.Multiline))
            if (!seen.Add(match.Groups[1].Value))
                duplicates.Add(match.Groups[1].Value);

        Assert.True(duplicates.Count == 0,
            "lang/en.json defines these keys more than once; the last definition wins silently:\n  " +
            string.Join("\n  ", duplicates));
    }

    private static HashSet<string> EnglishKeys()
    {
        JObject strings = JObject.Parse(File.ReadAllText(EnglishFilePath()));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (JProperty property in strings.Properties()) keys.Add(property.Name);

        Assert.True(keys.Count > 1000, "lang/en.json looks truncated: only " + keys.Count + " keys were read.");
        return keys;
    }

    private static string EnglishFilePath() =>
        Path.Combine(RepositoryRoot(), "KinetixModManager", "lang", "en.json");

    /// <summary>
    /// The app project's <c>.cs</c> files. Walks up from the test binary to the repository root rather than
    /// assuming a build layout — same approach as <see cref="InstallKeyGuardTests"/>.
    /// </summary>
    private static IEnumerable<string> AppSourceFiles()
    {
        string appDir = Path.Combine(RepositoryRoot(), "KinetixModManager");
        Assert.True(Directory.Exists(appDir), "Could not find the app project at " + appDir);

        // Top level only: bin/ and obj/ hold generated copies that would report the same line twice.
        string[] files = Directory.GetFiles(appDir, "*.cs", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(files);
        return files;
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
