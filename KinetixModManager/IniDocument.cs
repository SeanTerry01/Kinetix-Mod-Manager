using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// A minimal, comment-preserving model of an INI file (the format Skyrim/Fallout 4 use for their configuration:
/// <c>[Section]</c> headers followed by <c>key=value</c> lines). It exposes the settings as a flat, navigable list
/// for the accessible editor and lets a single value be changed, added, or removed while leaving every other line —
/// comments, blank lines, ordering, unrelated keys — exactly as it was. Kept free of any app/runtime dependency so
/// the parsing and editing rules can be unit-tested directly.
/// </summary>
public sealed class IniDocument
{
    /// <summary>One <c>key = value</c> setting, tagged with the <c>[Section]</c> it lives under (empty for none).</summary>
    public sealed class Entry
    {
        public string Section { get; }
        public string Key { get; }
        public string Value { get; }
        public Entry(string section, string key, string value) { Section = section; Key = key; Value = value; }
    }

    private readonly List<string> _lines;

    public IniDocument(IEnumerable<string> lines) => _lines = lines.ToList();

    /// <summary>Loads the INI at <paramref name="path"/>, or an empty document if the file doesn't exist.</summary>
    public static IniDocument Load(string path) =>
        new IniDocument(File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>());

    /// <summary>The raw lines, including comments and blanks, in file order.</summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <summary>Every setting in file order, each carrying the section it belongs to. Comments/blanks are skipped.</summary>
    public List<Entry> Entries()
    {
        var result = new List<Entry>();
        string section = "";
        foreach (string raw in _lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || IsComment(line)) continue;
            if (IsSectionHeader(line, out string name)) { section = name; continue; }
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            result.Add(new Entry(section, line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
        }
        return result;
    }

    /// <summary>Reads a key's trimmed value within a section, or <c>null</c> if the section or key is absent.</summary>
    public string? GetValue(string section, string key)
    {
        bool inSection = false;
        foreach (string raw in _lines)
        {
            string line = raw.Trim();
            if (IsSectionHeader(line, out string name)) { inSection = name.Equals(section, StringComparison.OrdinalIgnoreCase); continue; }
            if (!inSection || IsComment(line)) continue;
            int eq = line.IndexOf('=');
            if (eq > 0 && line.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return line.Substring(eq + 1).Trim();
        }
        return null;
    }

    /// <summary>Sets a key within a section, replacing it in place if present, else adding it (creating the section
    /// at the end of the file if it doesn't exist yet). Every other line is preserved.</summary>
    public void SetValue(string section, string key, string value) => Apply(section, key, value);

    /// <summary>Removes a key from a section if present; otherwise a no-op. Other lines are preserved.</summary>
    public void RemoveKey(string section, string key) => Apply(section, key, null);

    /// <summary>Writes the current lines back to <paramref name="path"/>.</summary>
    public void Save(string path) => File.WriteAllLines(path, _lines);

    // Sets (value non-null) or removes (value null) a key within a section, preserving all other lines. Mirrors the
    // behaviour long used for the archive-invalidation block, generalised here for the editor.
    private void Apply(string section, string key, string? value)
    {
        int sectionStart = -1, sectionEnd = _lines.Count;
        for (int i = 0; i < _lines.Count; i++)
        {
            if (!IsSectionHeader(_lines[i].Trim(), out string name)) continue;
            if (sectionStart < 0 && name.Equals(section, StringComparison.OrdinalIgnoreCase)) sectionStart = i;
            else if (sectionStart >= 0) { sectionEnd = i; break; }
        }

        int keyLine = -1;
        if (sectionStart >= 0)
            for (int i = sectionStart + 1; i < sectionEnd; i++)
            {
                string line = _lines[i].Trim();
                int eq = line.IndexOf('=');
                if (eq > 0 && line.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) { keyLine = i; break; }
            }

        if (value == null)
        {
            if (keyLine >= 0) _lines.RemoveAt(keyLine);
            return;
        }

        string entry = key + "=" + value;
        if (keyLine >= 0) _lines[keyLine] = entry;
        else if (sectionStart >= 0) _lines.Insert(sectionEnd, entry);
        else
        {
            if (_lines.Count > 0 && !string.IsNullOrWhiteSpace(_lines[^1])) _lines.Add("");
            _lines.Add("[" + section + "]");
            _lines.Add(entry);
        }
    }

    private static bool IsComment(string trimmedLine) =>
        trimmedLine.StartsWith(";") || trimmedLine.StartsWith("#") || trimmedLine.StartsWith("//");

    private static bool IsSectionHeader(string trimmedLine, out string name)
    {
        if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]") && trimmedLine.Length >= 2)
        {
            name = trimmedLine.Substring(1, trimmedLine.Length - 2).Trim();
            return true;
        }
        name = "";
        return false;
    }
}
