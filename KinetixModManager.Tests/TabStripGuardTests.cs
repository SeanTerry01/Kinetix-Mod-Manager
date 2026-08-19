using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Guards every tab strip in the app against being built from a stock <c>TabControl</c>.
///
/// <para>
/// A WinForms tab strip is a native tab window with a managed accessible tree laid over it, and the two disagree
/// about what child number one is. The native window numbers only the tabs; the managed tree puts the selected
/// page's content first and the tabs after it. Every focus event a screen reader receives therefore names the tab
/// one place behind the one you moved to, and points at an object that reports itself as neither focused nor
/// selected — so the reader discards it, asks the window who has focus instead, and reads out the strip. That is
/// where the "tab control" heard before every tab name came from, on the main window and in Settings alike.
/// </para>
///
/// <para>
/// It cost several attempts to find, because nothing about it is visible on screen, nothing fails to compile, and
/// the obvious suspects — the strip's accessible name, a stray refocus, owner-drawing — were all innocent.
/// <see cref="AccessibleTabControl"/> is the fix and the only tab strip the app should build; a plain
/// <c>TabControl</c> anywhere would quietly bring the whole thing back.
/// </para>
/// </summary>
public class TabStripGuardTests
{
    private static readonly Regex StockTabControl = new Regex(@"new\s+TabControl\b", RegexOptions.Compiled);

    [Fact]
    public void EveryTabStripIsAnAccessibleTabControl()
    {
        var stock = new List<string>();
        int strips = 0;

        foreach (string file in AppSourceFiles())
        {
            // AccessibleTabControl is the one file allowed to mention the stock control: it derives from it.
            if (Path.GetFileName(file) == "AccessibleTabControl.cs") continue;

            string source = WithoutComments(File.ReadAllText(file));
            string[] lines = source.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("new AccessibleTabControl")) strips++;
                if (StockTabControl.IsMatch(lines[i]))
                    stock.Add($"{Path.GetFileName(file)}:{i + 1} — builds a stock TabControl; use AccessibleTabControl");
            }
        }

        // A guard that has stopped finding anything to guard passes for the wrong reason.
        Assert.True(strips > 0, "No tab strips found at all — has the way they are built changed?");
        Assert.True(stock.Count == 0, string.Join("\n", stock));
    }

    /// <summary>
    /// Blanks out line comments, keeping every line's length so positions still match the file on disk.
    /// </summary>
    private static string WithoutComments(string source) =>
        Regex.Replace(source, @"//[^\n]*", match => new string(' ', match.Length));

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
