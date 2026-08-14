using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Guards every list box in the app against being built without the announcements a screen reader user navigates
/// it by.
///
/// <para>
/// A screen reader tells you three things when you land on a list: what the list is called, which row you are on,
/// and where that row sits in the whole — "Installed Mods List, Stardew Access, 129 of 149". It reads the row on
/// its own, but the position is the manager's to add, and it has to be added twice over: once when focus arrives
/// (<c>GotFocus</c>) and once for every arrow key (<c>SelectedIndexChanged</c>). Wire only the first and the list
/// says "1 of 40" on the way in and then goes silent while the user arrows through it, with no way to tell a long
/// list from a short one or to know when the end has been reached. Wire only the second and tabbing in says
/// nothing at all.
/// </para>
///
/// <para>
/// That half-wiring has been shipped and caught by ear more than once — the INI editor, the save manager, the log
/// link picker, the search-history list — because nothing about it looks wrong on screen and nothing fails to
/// compile. This test is the thing that looks. <see cref="Form1.WireAccessibleDialogList"/> does all of it in one
/// call and is what a new list should use; a list that announces itself its own way (the documentation drill-downs
/// build a richer position that says whether a row opens into sub-topics) satisfies this by subscribing to both
/// events itself.
/// </para>
/// </summary>
public class AccessibleListGuardTests
{
    /// <summary>
    /// Names a list box as it is declared. Covers the three shapes used in the app: an explicit type
    /// (<c>ListBox list = new ListBox</c>), an inferred one (<c>var lb = new ListBox</c>), and assignment to a
    /// field declared elsewhere (<c>_lstGames = new ListBox</c>).
    /// </summary>
    private static readonly Regex Declaration =
        new Regex(@"(?:ListBox\s+|var\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s+ListBox\b", RegexOptions.Compiled);

    [Fact]
    public void EveryListBoxAnnouncesItselfOnFocusAndOnEveryArrowKey()
    {
        var unwired = new List<string>();
        int found = 0;

        foreach (string file in AppSourceFiles())
        {
            // Comments are stripped first: a line comment that happens to name the wiring — including one that has
            // had the call commented out of it — must not read as the wiring being there.
            string source = WithoutComments(File.ReadAllText(file));
            // Split from the stripped text, so line numbers still line up with the file on disk while a
            // commented-out declaration no longer counts as a list that exists.
            string[] lines = source.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                Match declared = Declaration.Match(lines[i]);
                if (!declared.Success) continue;

                string name = declared.Groups["name"].Value;
                string where = $"{Path.GetFileName(file)}:{i + 1}  {name}";
                found++;

                if (Subscribes(source, name, "WireAccessibleDialogList")) continue;

                bool onFocus = Subscribes(source, name, "GotFocus");
                bool onMove = Subscribes(source, name, "SelectedIndexChanged");

                if (!onFocus && !onMove) unwired.Add($"{where} — announces nothing, on focus or on arrow");
                else if (!onFocus) unwired.Add($"{where} — silent when focus lands on it (no GotFocus)");
                else if (!onMove) unwired.Add($"{where} — silent while arrowing (no SelectedIndexChanged)");
            }
        }

        // A guard that has stopped finding anything to guard passes for the wrong reason. If the way lists are
        // built ever changes shape enough that the declaration pattern misses them, this is what says so.
        Assert.True(found >= 30, $"Only found {found} list boxes to check — the declaration pattern has stopped matching how lists are built.");

        Assert.True(unwired.Count == 0,
            "These list boxes leave a screen reader user without the position announcement they navigate by. Call " +
            "WireAccessibleDialogList(list) — it wires GotFocus, SelectedIndexChanged and Left/Right suppression " +
            "together — or subscribe to both events with the list's own announcement:\n  " +
            string.Join("\n  ", unwired));
    }

    /// <summary>
    /// True when the file wires <paramref name="member"/> for the list called <paramref name="name"/>. Matched
    /// across the whole file rather than the lines after the declaration, because a list is often declared at the
    /// top of a method and wired further down, after it has been filled.
    /// </summary>
    private static bool Subscribes(string source, string name, string member)
    {
        string pattern = member == "WireAccessibleDialogList"
            ? $@"WireAccessibleDialogList\(\s*{Regex.Escape(name)}\s*\)"
            : $@"\b{Regex.Escape(name)}\.{member}\s*\+=";
        return Regex.IsMatch(source, pattern);
    }

    /// <summary>
    /// Blanks out line comments, keeping the line count and every line's length so positions still match the file
    /// on disk. Enough for this test's purpose: it only needs code to stop looking like code once it is commented
    /// out, and does not have to understand strings or block comments to do that.
    /// </summary>
    private static string WithoutComments(string source)
    {
        return Regex.Replace(source, @"//[^\n]*", match => new string(' ', match.Length));
    }

    /// <summary>Every C# file the app itself is built from, skipping build output.</summary>
    private static IEnumerable<string> AppSourceFiles()
    {
        string root = AppRoot();
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }

    /// <summary>Walks up from the test binary to the app's source folder.</summary>
    private static string AppRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "KinetixModManager");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "Form1.Helpers.cs")))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the KinetixModManager source folder from " + Directory.GetCurrentDirectory());
    }
}
