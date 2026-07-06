using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// Decides, for a Skyrim/Fallout 4 mod archive, which files belong in the game's <c>Data</c> folder and which
/// belong in the game's <b>root</b> folder (next to the .exe) — think ENB (<c>d3d11.dll</c>, <c>enbseries\</c>),
/// ReShade (<c>dxgi.dll</c>), script-extender-style loose DLLs, or an explicit MO2-style <c>Root\</c> folder.
///
/// This is the pure, file-system-free core of "root-folder mod" support: given the archive's file paths it returns
/// where each one should live inside the stored mod folder. The manager keeps game-root content under a reserved
/// <c>Root\</c> subfolder, which the deployment engine already maps back out to the game root (everything else in a
/// mod folder deploys under <c>Data\</c>). Keeping the decision here — with no IO — makes it unit-testable.
/// </summary>
public static class BethesdaLayout
{
    /// <summary>The reserved mod-folder subfolder whose contents deploy to the game root instead of <c>Data\</c>.</summary>
    public const string RootFolderName = "Root";

    /// <summary>One planned file placement: where it is in the archive, and where it should go in the mod folder.</summary>
    public sealed class Entry
    {
        /// <summary>Path of the file relative to the archive's content root (using <c>/</c> separators).</summary>
        public string Source { get; }
        /// <summary>Path it should occupy inside the stored mod folder (using <c>/</c> separators).</summary>
        public string Dest { get; }
        /// <summary>True when this file deploys to the game root (lives under <c>Root\</c>), false for <c>Data\</c>.</summary>
        public bool IsRoot { get; }

        public Entry(string source, string dest, bool isRoot) { Source = source; Dest = dest; IsRoot = isRoot; }
    }

    // Loose files that, sitting at the top level of an archive, belong in the game root (graphics injectors,
    // DLL proxies, their config files). Matched by exact name, case-insensitively.
    private static readonly HashSet<string> RootFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "d3d11.dll", "d3d9.dll", "d3d10.dll", "dxgi.dll", "ddraw.dll", "dinput8.dll",
        "d3dcompiler_46.dll", "d3dcompiler_46e.dll", "d3dcompiler_47.dll",
        "binkw64.dll", "binkw64_.dll", "xinput1_3.dll", "xinput1_4.dll",
        "enblocal.ini", "enbseries.ini", "enbadaptation.fx", "enbraindrops.tga",
        "reshade.ini", "reshade-preset.ini", "dxvk.conf", "d3dx.ini",
    };

    // Extensions that, at the top level of an archive, are game-root tools/injectors rather than Data content.
    private static readonly HashSet<string> RootFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll",
    };

    // Top-level folders whose contents belong in the game root (ENB/ReShade support trees).
    private static readonly HashSet<string> RootFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "enbseries", "reshade-shaders", "reshade-presets", "injfx_shaders", "enbcache", "shaderscache",
    };

    // Recognised game-Data folders. Used only to protect a genuine Data folder that happens to sit beside an
    // explicit "Data" folder in a malformed archive (see Plan), so it is not misfiled to the root.
    private static readonly HashSet<string> DataFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "textures", "meshes", "scripts", "source", "interface", "sound", "music", "strings", "seq", "grass",
        "materials", "shadersfx", "lodsettings", "dialogueviews", "video", "facegen", "skse", "f4se", "misc",
        "actors", "effects", "planetdata", "terrain", "trees", "geometries", "vis", "distantlod", "scaleform",
    };

    /// <summary>
    /// Plans where each archive file should live inside the stored mod folder. <paramref name="relativePaths"/> are
    /// paths relative to the archive's content root (separators may be <c>/</c> or <c>\</c>). Rules, in order:
    /// an explicit <c>Data\</c> folder is unwrapped (its contents become Data-relative) and anything beside it goes
    /// to the root; an explicit <c>Root\</c> folder is kept as game-root content; otherwise only recognised
    /// injector files/folders go to the root and everything else stays Data content — matching the manager's prior
    /// behaviour, so a normal Data-only mod is planned exactly as before.
    /// </summary>
    public static List<Entry> Plan(IEnumerable<string> relativePaths)
    {
        var paths = relativePaths
            .Select(p => p.Replace('\\', '/').TrimStart('/'))
            .Where(p => p.Length > 0)
            .ToList();

        bool hasDataFolder = paths.Any(p => TopSegment(p).Equals("Data", StringComparison.OrdinalIgnoreCase));

        var entries = new List<Entry>(paths.Count);
        foreach (string p in paths)
        {
            string top = TopSegment(p);

            // Explicit game-folder layout: Data\... is Data content (unwrap the prefix), Root\... is game-root.
            if (top.Equals("Data", StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new Entry(p, StripTopSegment(p), isRoot: false));
                continue;
            }
            if (top.Equals(RootFolderName, StringComparison.OrdinalIgnoreCase))
            {
                entries.Add(new Entry(p, p, isRoot: true)); // already under Root/
                continue;
            }

            bool isRoot;
            if (DataFolderNames.Contains(top))
                isRoot = false;                                  // a real Data asset folder always stays Data
            else if (IsRootLooseFile(p) || RootFolderNames.Contains(top))
                isRoot = true;                                   // recognised injector file/folder
            else
                isRoot = hasDataFolder;                          // sibling of an explicit Data\ folder -> root; else Data

            entries.Add(new Entry(p, isRoot ? RootFolderName + "/" + p : p, isRoot));
        }
        return entries;
    }

    /// <summary>True when the plan places at least one file in the game root — i.e. this is a root-folder mod.</summary>
    public static bool HasRootContent(IEnumerable<Entry> plan) => plan.Any(e => e.IsRoot);

    /// <summary>
    /// True when <paramref name="folderName"/> is a folder whose name carries layout meaning (an explicit
    /// <c>Data</c>/<c>Root</c> folder, a known Data asset folder, or a known game-root folder). Used when peeling
    /// off an archive's wrapper folders so a meaningful folder is never mistaken for a throwaway wrapper.
    /// </summary>
    public static bool IsLayoutFolder(string folderName) =>
        folderName.Equals("Data", StringComparison.OrdinalIgnoreCase) ||
        folderName.Equals(RootFolderName, StringComparison.OrdinalIgnoreCase) ||
        DataFolderNames.Contains(folderName) ||
        RootFolderNames.Contains(folderName);

    /// <summary>A top-level loose file (no subfolder) that belongs in the game root by name or by tool extension.</summary>
    private static bool IsRootLooseFile(string relPath)
    {
        if (relPath.Contains('/')) return false;                 // only files sitting at the archive's top level
        return RootFileNames.Contains(relPath) || RootFileExtensions.Contains(Path.GetExtension(relPath));
    }

    private static string TopSegment(string relPath)
    {
        int slash = relPath.IndexOf('/');
        return slash < 0 ? relPath : relPath.Substring(0, slash);
    }

    private static string StripTopSegment(string relPath)
    {
        int slash = relPath.IndexOf('/');
        return slash < 0 ? relPath : relPath.Substring(slash + 1);
    }
}
