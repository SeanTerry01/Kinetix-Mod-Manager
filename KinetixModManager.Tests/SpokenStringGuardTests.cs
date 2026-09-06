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
        "sound.",
        // Built as "curator.category" + the category's id; see EveryBuiltInSuggestionCategoryHasAName.
        "curator.category"
    };

    [Fact]
    public void EveryPhraseTheAppAsksForExistsInEnglish()
    {
        HashSet<string> known = EnglishKeys();
        var missing = new List<string>();
        int found = 0;

        foreach (string file in AppSourceFiles())
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in Regex.Matches(lines[i], @"Loc\.T\(""([^""]+)"""))
                {
                    found++;
                    string key = match.Groups[1].Value;
                    if (known.Contains(key)) continue;
                    if (BuiltAtRuntimePrefixes.Contains(key)) continue;

                    missing.Add($"{Path.GetFileName(file)}:{i + 1}  {key}");
                }
            }
        }

        // A sweep that has stopped finding anything to sweep passes for the wrong reason. This test reads the app's
        // source looking for Loc.T("key") and checks each key against lang/en.json — so if AppSourceFiles() ever
        // comes back empty (a moved repository root, a different build layout) or the call sites stop matching the
        // pattern (a switch to interpolation, a wrapper helper), it would check nothing at all and still pass,
        // silently, forever. 1872 call sites were found when this floor was written; it is set below that so
        // ordinary churn does not trip it, but a collapse in discovery does.
        Assert.True(found >= 1800,
            $"Only found {found} Loc.T call sites to check — expected at least 1800. The scan has stopped seeing " +
            "the app's phrases, so this guard is no longer guarding anything. Fix the discovery before trusting " +
            "a green run here.");

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
    public void EveryBuiltInSuggestionCategoryHasAName()
    {
        // These keys are assembled as "curator.category" + the category's id, so the regex above can never see
        // them and nothing else would notice them going missing. Renaming a built-in id — an ordinary-looking
        // refactor — would leave the category picker reading "curator dot category dot cant by ear" out loud.
        HashSet<string> known = EnglishKeys();
        var missing = new List<string>();

        foreach (SuggestionCategory category in SuggestionCategoryStore.Defaults())
            if (!known.Contains(category.LocKey))
                missing.Add($"{category.Id}  ->  {category.LocKey}");

        Assert.True(missing.Count == 0,
            "These built-in suggestion categories have no name in lang/en.json, so the category picker would read " +
            "the key itself aloud. Add each one, or put the id back:\n  " + string.Join("\n  ", missing));
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


    [Fact]
    public void NoPhraseIsLeftBehindWithNothingAskingForIt()
    {
        // The other direction, and the one nothing used to check: a key that no longer has any code asking for
        // it. Harmless at runtime, but it is work handed to whoever translates this file into another language --
        // sentences nobody will ever hear, indistinguishable from the ones that matter. Twenty-nine had built up
        // by the time anybody looked.
        //
        // A key counts as asked for if it appears anywhere in the sources as a literal, which covers the ternary
        // and variable forms the EveryPhrase... regex above cannot see, or if it begins with one of the prefixes
        // the code assembles keys from.
        HashSet<string> known = EnglishKeys();
        var literals = new HashSet<string>(StringComparer.Ordinal);

        foreach (string file in AppSourceFiles())
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"""([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z0-9_]+)+)"""))
                literals.Add(match.Groups[1].Value);

        var unused = known
            .Where(k => !k.StartsWith("_", StringComparison.Ordinal))       // "_name" and friends are metadata
            .Where(k => !literals.Contains(k))
            .Where(k => !BuiltAtRuntimePrefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal)))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(unused.Count == 0,
            "lang/en.json defines these phrases but nothing in the app asks for them any more. Remove them, or " +
            "if one is built at runtime add its prefix to BuiltAtRuntimePrefixes:\n  " +
            string.Join("\n  ", unused));
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
