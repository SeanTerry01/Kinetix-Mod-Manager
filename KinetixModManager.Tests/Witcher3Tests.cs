using System;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers what is particular about The Witcher 3: the folder-name rule the engine loads by, the tilde that
/// switches a mod off, and the game's own <c>mods.settings</c> record of which mods are on.
///
/// The folder-name rule is the reason these are worth testing. The Witcher 3 loads a folder in <c>mods</c> only
/// if it is called <c>mod*</c>, and it says nothing when it doesn't: a mod installed under the wrong name simply
/// never runs, the game starts perfectly, and the player is left to work out why nothing happened.
/// </summary>
public class Witcher3Tests
{
    private static string Game => GameProfiles.Witcher3;

    // ---------------------------------------------------------------------
    // The profile itself
    // ---------------------------------------------------------------------

    [Fact]
    public void TheWitcher3IsRegisteredWithItsOwnLayoutAndStoreIds()
    {
        GameProfile w3 = GameProfiles.Require(Game);

        Assert.Equal(ModLayout.Witcher3Mods, w3.Layout);
        Assert.True(w3.IsWitcher3);
        Assert.False(w3.IsBethesda);
        Assert.False(w3.IsBepInEx);

        Assert.Equal("witcher3", w3.NexusDomain);
        Assert.Equal("952", w3.NexusGameId);
        Assert.Equal("mods", w3.ModsFolderRelativeToGame);
        Assert.Equal("~", w3.DisabledModPrefix);
        Assert.Equal("mod", w3.RequiredModFolderPrefix);
    }

    [Fact]
    public void TheGameIsFoundUnderEitherOfItsSteamAppIdsAndEitherGogEdition()
    {
        // Wild Hunt and the Complete Edition are different products that install the same game; a manager that
        // knows only one of them fails to find the game for everyone who owns the other.
        GameProfile w3 = GameProfiles.Require(Game);

        Assert.Contains("292030", w3.AllSteamAppIds);
        Assert.Contains("499450", w3.AllSteamAppIds);
        Assert.Equal("292030", w3.AllSteamAppIds.First());

        Assert.Contains("1207664643", w3.AllGogProductIds);
        Assert.Contains("1495134320", w3.AllGogProductIds);
    }

    [Fact]
    public void ItsExecutableLivesBelowTheGameFolderRatherThanInIt()
    {
        // Detection and the running-process check both build on this, and witcher3.exe is two folders down.
        GameProfile w3 = GameProfiles.Require(Game);

        Assert.Equal(Path.Combine("bin", "x64", "witcher3.exe"), w3.GameExeName);
        Assert.Equal("witcher3", Path.GetFileNameWithoutExtension(w3.GameExeName));
        Assert.Equal(Path.Combine(@"D:\Games\The Witcher 3", "bin", "x64", "witcher3.exe"),
            Path.Combine(@"D:\Games\The Witcher 3", w3.GameExeName));
    }

    [Fact]
    public void ItIsStartedFromItsOwnExecutableRatherThanThroughSteam()
    {
        // Steam starts REDprelauncher, which is a graphical window a screen reader cannot use — and it is also
        // what picks between the DirectX 11 and DirectX 12 builds. bin\x64 is the DirectX 11 one, which is the
        // build the accessibility mod is developed against.
        GameProfile w3 = GameProfiles.Require(Game);

        Assert.True(w3.LaunchGameExeDirectly);
        Assert.Equal("", w3.LoaderExeName);
        Assert.DoesNotContain("x64_dx12", w3.GameExeName);

        // Every other game still goes the normal route.
        foreach (GameProfile other in GameProfiles.All.Where(g => g.Id != Game))
            Assert.False(other.LaunchGameExeDirectly);
    }

    [Fact]
    public void ItsPerPlayerFolderIsNotUnderMyGames()
    {
        // Documents\The Witcher 3, unlike the Bethesda games' Documents\My Games\<game>. Getting this wrong
        // means reading settings and saves from a folder that doesn't exist.
        GameProfile w3 = GameProfiles.Require(Game);
        Assert.False(w3.UserDataUnderMyGames);

        string expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "The Witcher 3");
        Assert.Equal(expected, w3.UserDataDirectoryFor(@"D:\Games\The Witcher 3"));
    }

    // ---------------------------------------------------------------------
    // Enabling and disabling
    // ---------------------------------------------------------------------

    [Fact]
    public void DisablingAModPrefixesItWithATildeSoTheEngineWalksPastIt()
    {
        string mods = Path.Combine("C:", "Game", "mods");

        string off = ModEnableState.TargetPath(Path.Combine(mods, "modWitcherAccess"), enable: false, Game);

        Assert.Equal(Path.Combine(mods, "~modWitcherAccess"), off);
    }

    [Fact]
    public void EnablingAModTakesTheTildeBackOff()
    {
        string mods = Path.Combine("C:", "Game", "mods");

        string on = ModEnableState.TargetPath(Path.Combine(mods, "~modWitcherAccess"), enable: true, Game);

        Assert.Equal(Path.Combine(mods, "modWitcherAccess"), on);
    }

    [Fact]
    public void DisablingAndReEnablingReturnsTheOriginalName()
    {
        string original = Path.Combine("C:", "Game", "mods", "modBrothersInArms");

        string off = ModEnableState.TargetPath(original, enable: false, Game);
        string backOn = ModEnableState.TargetPath(off, enable: true, Game);

        Assert.Equal(original, backOn);
    }

    [Fact]
    public void ADotPrefixedFolderIsNotTreatedAsDisabledHere()
    {
        // The leading dot is the other games' convention and means nothing to this engine — a folder called
        // .modFoo is simply a folder that doesn't start with "mod", i.e. one the game ignores anyway.
        Assert.True(ModEnableState.IsEnabled(Path.Combine("C:", "Game", "mods", "modFoo"), Game));
        Assert.False(ModEnableState.IsEnabled(Path.Combine("C:", "Game", "mods", "~modFoo"), Game));
    }

    // ---------------------------------------------------------------------
    // mods.settings — the game's own record
    // ---------------------------------------------------------------------

    [Fact]
    public void ReadsTheModsTheGameListsAndWhetherTheyAreOn()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, Witcher3ModSettings.FileName);
        File.WriteAllLines(path, new[]
        {
            "[modWitcherAccess]",
            "Enabled=1",
            "",
            "[modFriendlyHUD]",
            "Enabled=0",
            "Priority=3",
        });

        var entries = Witcher3ModSettings.Read(path);

        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].Enabled);
        Assert.Equal("modWitcherAccess", entries[0].Name);
        Assert.False(entries[1].Enabled);
        Assert.Equal(3, entries[1].Priority);
    }

    [Fact]
    public void AModTheFileSaysNothingAboutCountsAsOn()
    {
        // The game loads a mod folder it can see unless something has switched it off, so silence is not "off".
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, Witcher3ModSettings.FileName);
        File.WriteAllLines(path, new[] { "[modWitcherAccess]", "Enabled=1" });

        Assert.False(Witcher3ModSettings.IsDisabledByFile(path, "modSomethingElse"));
        Assert.False(Witcher3ModSettings.IsDisabledByFile(path, "modWitcherAccess"));
    }

    [Fact]
    public void SwitchingAModOffIsRecordedWithoutDisturbingTheOthers()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, Witcher3ModSettings.FileName);
        File.WriteAllLines(path, new[]
        {
            "[modWitcherAccess]",
            "Enabled=1",
            "[modFriendlyHUD]",
            "Enabled=1",
        });

        Witcher3ModSettings.SetEnabled(path, "modWitcherAccess", false);

        var entries = Witcher3ModSettings.Read(path);
        Assert.False(entries.Single(e => e.Name == "modWitcherAccess").Enabled);
        Assert.True(entries.Single(e => e.Name == "modFriendlyHUD").Enabled);
    }

    [Fact]
    public void TheRecordIsKeyedOnTheModsRealNameNotItsDisabledOne()
    {
        // The manager disables by renaming the folder to ~modFoo, but the game's own record only ever knows it
        // as modFoo. Writing a [~modFoo] section would leave the real entry untouched and switch nothing off.
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, Witcher3ModSettings.FileName);
        File.WriteAllLines(path, new[] { "[modWitcherAccess]", "Enabled=1" });

        Witcher3ModSettings.SetEnabled(path, "~modWitcherAccess", false);

        var entries = Witcher3ModSettings.Read(path);
        Assert.Single(entries);
        Assert.Equal("modWitcherAccess", entries[0].Name);
        Assert.False(entries[0].Enabled);
    }

    [Fact]
    public void EnablingAModDoesNotConjureAFileTheGameHasNeverWritten()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, Witcher3ModSettings.FileName);

        Witcher3ModSettings.SetEnabled(path, "modWitcherAccess", true);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void AnUninstalledModIsDroppedFromTheListEntirely()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, Witcher3ModSettings.FileName);
        File.WriteAllLines(path, new[]
        {
            "[modWitcherAccess]",
            "Enabled=1",
            "[modFriendlyHUD]",
            "Enabled=1",
        });

        Witcher3ModSettings.Remove(path, "modWitcherAccess");

        var entries = Witcher3ModSettings.Read(path);
        Assert.Single(entries);
        Assert.Equal("modFriendlyHUD", entries[0].Name);
    }

    [Fact]
    public void AMissingFileReadsAsNoModsRatherThanFailing()
    {
        using var dir = new TempDir();
        Assert.Empty(Witcher3ModSettings.Read(Path.Combine(dir.Path, "nothing-here.settings")));
        Assert.Empty(Witcher3ModSettings.Read(""));
    }

    // ---------------------------------------------------------------------
    // input.settings — the controls list
    // ---------------------------------------------------------------------

    private static string WriteInputSettings(TempDir dir, params string[] lines)
    {
        string path = Path.Combine(dir.Path, Witcher3InputSettings.FileName);
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void AModsOwnActionIsReadAsWordsAndAttributedToIt()
    {
        // WitcherAccess binds WA_Compass and WA_Announce. Read as they are stored they are identifiers, and in
        // a list where a mod's controls sit beside the game's there is nothing to say which is which.
        using var dir = new TempDir();
        string path = WriteInputSettings(dir,
            "[Exploration]",
            "IK_N=(Action=WA_Compass)",
            "IK_Home=(Action=WA_Announce)",
            "IK_E=(Action=SitAndWait)");

        string mods = Path.Combine(dir.Path, "mods");
        Directory.CreateDirectory(Path.Combine(mods, "modWitcherAccess"));

        var bindings = Witcher3InputSettings.Read(path, mods);

        Assert.Equal(new[] { "Compass (Witcher Access)" }, bindings.Single(b => b.Key == "N").Actions);
        Assert.Equal(new[] { "Announce (Witcher Access)" }, bindings.Single(b => b.Key == "Home").Actions);
        // The game's own actions are untouched.
        Assert.Equal(new[] { "Sit And Wait" }, bindings.Single(b => b.Key == "E").Actions);
    }

    [Fact]
    public void AnActionWhosePrefixMatchesNoInstalledModIsLeftAlone()
    {
        // Inventing a meaning for an unknown prefix would be worse than the identifier it came from.
        using var dir = new TempDir();
        string path = WriteInputSettings(dir, "[Exploration]", "IK_N=(Action=ZZ_Something)");
        string mods = Path.Combine(dir.Path, "mods");
        Directory.CreateDirectory(Path.Combine(mods, "modWitcherAccess"));

        Assert.Equal(new[] { "ZZ Something" }, Witcher3InputSettings.Read(path, mods).Single().Actions);
    }

    [Fact]
    public void TwoModsWithTheSameInitialsClaimNeither()
    {
        using var dir = new TempDir();
        string path = WriteInputSettings(dir, "[Exploration]", "IK_N=(Action=WA_Compass)");
        string mods = Path.Combine(dir.Path, "mods");
        Directory.CreateDirectory(Path.Combine(mods, "modWitcherAccess"));
        Directory.CreateDirectory(Path.Combine(mods, "modWildArmour"));

        Assert.Equal(new[] { "WA Compass" }, Witcher3InputSettings.Read(path, mods).Single().Actions);
    }

    [Fact]
    public void WithoutAModsFolderTheActionsAreStillListed()
    {
        using var dir = new TempDir();
        string path = WriteInputSettings(dir, "[Exploration]", "IK_N=(Action=WA_Compass)");

        Assert.Equal(new[] { "WA Compass" }, Witcher3InputSettings.Read(path).Single().Actions);
    }

    [Fact]
    public void EverythingAKeyDoesIsGatheredUnderThatKey()
    {
        // A key is listed once per context it applies in, and all of those are true at the same time — the
        // left mouse button really does attack, select and cast, depending on what is on screen.
        using var dir = new TempDir();
        string path = WriteInputSettings(dir,
            "[BASE_ALL_ATTACKS]",
            "IK_LeftMouse=(Action=AttackLight)",
            "[Radial]",
            "IK_LeftMouse=(Action=CastSign)",
            "[Menu]",
            "IK_LeftMouse=(Action=AttackLight)");

        var bindings = Witcher3InputSettings.Read(path);

        GameKeyBinding mouse = Assert.Single(bindings);
        Assert.Equal("Left Mouse Button", mouse.Key);
        // Listed twice in the file, said once here.
        Assert.Equal(new[] { "Attack Light", "Cast Sign" }, mouse.Actions);
    }

    [Fact]
    public void MovementReadsAsDirectionsRatherThanAsControllerAxes()
    {
        // W is stored as "the left stick's Y axis, positive". Reported literally that is "Axis Left Y", which
        // tells a player nothing about what W does.
        using var dir = new TempDir();
        string path = WriteInputSettings(dir,
            "[Exploration]",
            "IK_W=(Action=GI_AxisLeftY,State=Axis,Value=1.0)",
            "IK_S=(Action=GI_AxisLeftY,State=Axis,Value=-1.0)",
            "IK_A=(Action=GI_AxisLeftX,State=Axis,Value=-1.0)",
            "IK_D=(Action=GI_AxisLeftX,State=Axis,Value=1.0)");

        var bindings = Witcher3InputSettings.Read(path);

        Assert.Equal("Move Forward", bindings.Single(b => b.Key == "W").Actions.Single());
        Assert.Equal("Move Backward", bindings.Single(b => b.Key == "S").Actions.Single());
        Assert.Equal("Move Left", bindings.Single(b => b.Key == "A").Actions.Single());
        Assert.Equal("Move Right", bindings.Single(b => b.Key == "D").Actions.Single());
    }

    [Fact]
    public void UnboundAndControllerEntriesAreLeftOutOfAKeyboardList()
    {
        using var dir = new TempDir();
        string path = WriteInputSettings(dir,
            "[BASE_ALL_ATTACKS]",
            "IK_None=(Action=AttackWithAlternateHeavy)",
            "IK_Pad_A_CROSS=(Action=Jump)",
            "IK_PS4_OPTIONS=(Action=IngameMenu)",
            "IK_MouseX=(Action=MouseDampX)",
            "IK_E=(Action=Interact)");

        var bindings = Witcher3InputSettings.Read(path);

        GameKeyBinding only = Assert.Single(bindings);
        Assert.Equal("E", only.Key);
    }

    [Fact]
    public void KeyNamesAreReadableAloud()
    {
        Assert.Equal("Left Shift", Witcher3InputSettings.FriendlyKeyName("LShift"));
        Assert.Equal("Right Mouse Button", Witcher3InputSettings.FriendlyKeyName("RightMouse"));
        Assert.Equal("Numpad 4", Witcher3InputSettings.FriendlyKeyName("NumPad4"));
        Assert.Equal("Spacebar", Witcher3InputSettings.FriendlyKeyName("Space"));
        Assert.Equal("Page Down", Witcher3InputSettings.FriendlyKeyName("PageDown"));
        Assert.Equal("E", Witcher3InputSettings.FriendlyKeyName("E"));
    }

    [Fact]
    public void ActionNamesAreSplitIntoWords()
    {
        using var dir = new TempDir();
        string path = WriteInputSettings(dir,
            "[Exploration]",
            "IK_R=(Action=DrinkPotion1Hold)",
            "IK_LShift=(Action=PCAlternate)");

        var bindings = Witcher3InputSettings.Read(path);

        Assert.Equal("Drink Potion1 Hold", bindings.Single(b => b.Key == "R").Actions.Single());
        Assert.Equal("PC Alternate", bindings.Single(b => b.Key == "Left Shift").Actions.Single());
    }

    [Fact]
    public void AMissingInputSettingsIsNoBindingsRatherThanACrash()
    {
        using var dir = new TempDir();
        Assert.Empty(Witcher3InputSettings.Read(Path.Combine(dir.Path, "nope.settings")));
        Assert.Empty(Witcher3InputSettings.Read(""));
    }

    /// <summary>A temporary directory that deletes itself.</summary>
    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KMM_W3_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
