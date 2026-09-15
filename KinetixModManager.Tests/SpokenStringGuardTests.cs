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

    [Fact]
    public void EveryPhraseIsGivenAsManyValuesAsItAsksFor()
    {
        // The gap the other two guards left. A phrase that says "{0} of {1}" and is called with one value does
        // not error either: string.Format throws, Loc.T catches the FormatException, and the caller gets the
        // template back unformatted. The screen reader then reads the braces out - "Download open brace zero
        // close brace" - which is the same class of failure as a missing key, and was just as invisible.
        //
        // Two real ones were found this way: a duplicate-UniqueID report that never passed its count, and the
        // manual-download dialog whose title was "Download {0}".
        //
        // Only calls whose arguments sit on one line are checked, which is nearly all of them. A call split
        // across lines is skipped rather than guessed at - a guard that cries wolf gets muted.
        HashSet<string> known = EnglishKeys();
        JObject strings = JObject.Parse(File.ReadAllText(EnglishFilePath()));
        var wrong = new List<string>();

        foreach (string file in AppSourceFiles())
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match call in Regex.Matches(lines[i], @"Loc\.T\(""([^""]+)""([^;]*)"))
                {
                    string key = call.Groups[1].Value;
                    if (!known.Contains(key)) continue;          // EveryPhraseTheAppAsksForExists covers that

                    string? rest = TopLevelArguments(call.Groups[2].Value);
                    if (rest == null) continue;                  // arguments run onto the next line; skip

                    int needed = PlaceholdersIn((string?)strings[key] ?? "");
                    int given = rest.Length == 0 ? 0 : rest.Split(',').Length;
                    if (given >= needed) continue;

                    wrong.Add($"{Path.GetFileName(file)}:{i + 1}  {key} wants {needed} value(s), given {given}" +
                        $"\n      \"{(string?)strings[key]}\"");
                }
            }
        }

        Assert.True(wrong.Count == 0,
            "these phrases are called with fewer values than they contain placeholders, so the user is read the " +
            "template including its braces:\n  " + string.Join("\n  ", wrong));
    }

    /// <summary>
    /// The comma-separated argument list after the key, or <c>null</c> when the call does not finish on this
    /// line. Commas inside nested calls, strings or interpolations are collapsed first so that
    /// <c>Loc.T("k", Join(", ", names))</c> counts as one argument rather than two.
    /// </summary>
    private static string? TopLevelArguments(string afterKey)
    {
        var kept = new System.Text.StringBuilder();
        int depth = 0;
        bool inString = false;

        for (int i = 0; i < afterKey.Length; i++)
        {
            char c = afterKey[i];

            if (inString)
            {
                if (c == '\\') { i++; continue; }
                if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; continue;
                case '(': case '[': case '{': depth++; continue;
                case ')' when depth == 0: return kept.ToString().Trim().TrimStart(',').Trim();
                case ')': case ']': case '}': depth--; continue;
            }

            if (depth == 0) kept.Append(c);
        }

        return null;   // never closed on this line
    }

    /// <summary>How many values a phrase asks for: one more than its highest placeholder index, or zero.</summary>
    private static int PlaceholdersIn(string phrase)
    {
        int highest = -1;
        foreach (Match m in Regex.Matches(phrase, @"(?<!\{)\{(\d+)[,:}]"))
            highest = Math.Max(highest, int.Parse(m.Groups[1].Value));
        return highest + 1;
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
    /// Every <c>.cs</c> file that could speak: the app project and Kinetix.Core. Walks up from the test binary
    /// to the repository root rather than assuming a build layout — same approach as
    /// <see cref="InstallKeyGuardTests"/>.
    ///
    /// All three heads are swept, and the GTK one is the reason this list is not hard-coded to the app. It
    /// spoke fifty-odd English literals for months — untranslatable, and invisible to every check in this
    /// file, so a phrase could go missing from the catalogue and the guard would still be green while a
    /// Linux user heard nothing. Sweeping it is what makes "every sentence the program says" true rather
    /// than "every sentence the Windows program says".
    /// </summary>
    private static IEnumerable<string> AppSourceFiles()
    {
        var files = new List<string>();

        foreach (string project in new[] { "KinetixModManager", "Kinetix.Core", "Kinetix.Gtk" })
        {
            string dir = Path.Combine(RepositoryRoot(), project);
            Assert.True(Directory.Exists(dir), "Could not find " + project + " at " + dir);

            // Recursive, because the core keeps its models in a Models/ folder — and a top-level-only sweep
            // stopped seeing their phrases the moment they moved there, which this guard caught by going red
            // rather than by quietly passing. bin/ and obj/ are skipped by name: they hold generated copies of
            // these same files, and counting one twice would report every line in it twice over.
            files.AddRange(Directory
                .GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                         && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)));
        }

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
