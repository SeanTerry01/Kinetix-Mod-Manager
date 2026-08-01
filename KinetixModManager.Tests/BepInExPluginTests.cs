using System;
using System.Collections.Generic;
using System.IO;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="BepInExPlugin"/>, which works out what a Moonlight Peaks mod is called and which version
/// it is. The DLL's own file version can't be used for this — on a normal install several plugins report
/// 0.0.0.0 — so the manager reads the <c>[BepInPlugin]</c> attribute and falls back to the two places BepInEx
/// writes a plugin's name and version in plain text: its log and the plugin's config file header.
/// </summary>
public class BepInExPluginTests
{
    // -------------------------------------------------------------------------
    // LogOutput.log: the chainloader's "Loading [Name Version]" lines
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseLog_ReadsNameAndVersionOfEveryLoadedPlugin()
    {
        string log = WriteTemp("LogOutput.log", string.Join(Environment.NewLine, new[]
        {
            "[Message:   BepInEx] BepInEx 5.4.23.5 - Moonlight Peaks (7/12/2026 12:39:28 AM)",
            "[Info   :   BepInEx] 10 plugins to load",
            "[Info   :   BepInEx] Loading [Bigger Stacks for Items 1.0.0]",
            "[Info   :Bigger Stacks for Items] Plugin BiggerStacks is loaded!",
            "[Info   :   BepInEx] Loading [Moonlight Access 0.1.0]",
            "[Info   :   BepInEx] Loading [Stack To Chest 1.2.0]",
            "[Message:   BepInEx] Chainloader startup complete"
        }));

        Dictionary<string, string> plugins = BepInExPlugin.ParseLoadedPluginsFromLog(log);

        Assert.Equal(3, plugins.Count);
        Assert.Equal("1.0.0", plugins["Bigger Stacks for Items"]);
        Assert.Equal("0.1.0", plugins["Moonlight Access"]);
        Assert.Equal("1.2.0", plugins["Stack To Chest"]);
    }

    [Fact]
    public void ParseLog_IgnoresPluginChatterThatMerelyMentionsLoading()
    {
        // Plugins log their own "loaded" messages, which must not be mistaken for chainloader load records.
        string log = WriteTemp("chatter.log", string.Join(Environment.NewLine, new[]
        {
            "[Info   :Save Anywhere] Save Anywhere 1.1.2 loaded. Press F5 in-game to save.",
            "[Info   :Moonlight Access] Moonlight Access v0.1.0 is loading.",
            "[Info   :   BepInEx] Loading [Save Anywhere 1.1.2]"
        }));

        Dictionary<string, string> plugins = BepInExPlugin.ParseLoadedPluginsFromLog(log);

        Assert.Single(plugins);
        Assert.Equal("1.1.2", plugins["Save Anywhere"]);
    }

    [Fact]
    public void ParseLog_KeepsMultiWordNamesIntact()
    {
        string log = WriteTemp("multiword.log",
            "[Info   :   BepInEx] Loading [Moonlight Peaks House Storage Anywhere 1.0.0]");

        Dictionary<string, string> plugins = BepInExPlugin.ParseLoadedPluginsFromLog(log);

        Assert.Equal("1.0.0", plugins["Moonlight Peaks House Storage Anywhere"]);
    }

    [Fact]
    public void ParseLog_MissingFileIsEmptyRatherThanAnError()
    {
        Assert.Empty(BepInExPlugin.ParseLoadedPluginsFromLog(
            Path.Combine(Path.GetTempPath(), "no-such-bepinex-log-" + Guid.NewGuid() + ".log")));
    }

    // -------------------------------------------------------------------------
    // Config headers: "## Settings file was created by plugin X vY" + "## Plugin GUID: z"
    // -------------------------------------------------------------------------

    [Fact]
    public void ReadFromConfig_ReadsNameVersionAndGuidFromTheHeader()
    {
        string cfg = WriteTemp("com.moonlightaccess.core.cfg", string.Join(Environment.NewLine, new[]
        {
            "## Settings file was created by plugin Moonlight Access v0.1.0",
            "## Plugin GUID: com.moonlightaccess.core",
            "",
            "[Accessibility]",
            "Hints = true"
        }));

        BepInExPluginInfo? info = BepInExPlugin.ReadFromConfig(cfg);

        Assert.NotNull(info);
        Assert.Equal("Moonlight Access", info!.Name);
        Assert.Equal("0.1.0", info.Version);
        Assert.Equal("com.moonlightaccess.core", info.Guid);
    }

    [Fact]
    public void ReadFromConfig_ReturnsNullForAConfigWithNoPluginHeader()
    {
        string cfg = WriteTemp("plain.cfg", "[Section]" + Environment.NewLine + "Key = value");

        Assert.Null(BepInExPlugin.ReadFromConfig(cfg));
    }

    // -------------------------------------------------------------------------
    // Identify: what the mod list actually shows for a folder
    // -------------------------------------------------------------------------

    [Fact]
    public void Identify_FallsBackToTheLoggedPluginWhoseNameMatchesTheFolder()
    {
        // No readable [BepInPlugin] attribute (the folder holds no real assembly), so the log has to answer.
        string folder = NewTempFolder("SaveAnywhere");
        File.WriteAllText(Path.Combine(folder, "Save Anywhere.dll"), "not a real assembly");

        var logged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Save Anywhere"] = "1.1.2",
            ["Stack To Chest"] = "1.2.0"
        };

        BepInExPluginInfo info = BepInExPlugin.Identify(folder, logged);

        Assert.Equal("Save Anywhere", info.Name);
        Assert.Equal("1.1.2", info.Version);
    }

    [Fact]
    public void Identify_FallsBackToTheFolderNameWhenNothingElseIsKnown()
    {
        string folder = NewTempFolder("MysteryMod");
        File.WriteAllText(Path.Combine(folder, "Mystery.dll"), "not a real assembly");

        BepInExPluginInfo info = BepInExPlugin.Identify(folder, new Dictionary<string, string>());

        Assert.Equal("MysteryMod", info.Name);
        Assert.Equal("", info.Version);
    }

    [Fact]
    public void Identify_DoesNotClaimAVersionFromAnUnrelatedPlugin()
    {
        // A folder that matches nothing in the log must not inherit some other plugin's version.
        string folder = NewTempFolder("UnrelatedMod");
        File.WriteAllText(Path.Combine(folder, "Unrelated.dll"), "not a real assembly");

        var logged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Completely Different Plugin"] = "9.9.9"
        };

        BepInExPluginInfo info = BepInExPlugin.Identify(folder, logged);

        Assert.Equal("UnrelatedMod", info.Name);
        Assert.Equal("", info.Version);
    }

    [Fact]
    public void ReadFromAssembly_ReturnsNullForAFileThatIsNotAManagedAssembly()
    {
        string fake = WriteTemp("NotAnAssembly.dll", "MZ but not really");

        Assert.Null(BepInExPlugin.ReadFromAssembly(fake));
    }

    // -------------------------------------------------------------------------

    private static string WriteTemp(string name, string contents)
    {
        string dir = NewTempFolder("files");
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, contents);
        return path;
    }

    private static string NewTempFolder(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "KinetixBepInExTests", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
