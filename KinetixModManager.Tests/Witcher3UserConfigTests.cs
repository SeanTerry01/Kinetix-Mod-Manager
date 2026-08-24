using System;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers reading a Witcher 3 mod's Options → Mods menu, against WitcherAccess's real menu file rather than an
/// invented one — its separators, its hidden bookkeeping var and its three groups are exactly what the reader
/// has to survive.
/// </summary>
public class Witcher3UserConfigTests
{
    private static McmMenu RealMenu()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "witcher3-modWitcherAccess-userconfig.xml");
        return Witcher3UserConfig.Parse(File.ReadAllText(path), "modWitcherAccess", @"C:\Docs\user.settings");
    }

    [Fact]
    public void TheGroupsHoldingValuesAreRead()
    {
        McmMenu menu = RealMenu();

        // Sections in user.settings are named after the group ids. The mod declares three groups, but the third
        // — WAGlossary — is thirteen "play this sound" buttons that store nothing, so it holds no settings.
        Assert.Equal(
            new[] { "WAGeneral", "WASounds" },
            menu.Settings.Select(s => s.IniSection).Distinct().OrderBy(s => s).ToArray());
    }

    [Fact]
    public void SoundPreviewButtonsAreNotOfferedAsSettings()
    {
        // Only the game can play a sound; offering these would write meaningless keys into user.settings.
        Assert.DoesNotContain(RealMenu().Settings,
            s => s.SettingName.StartsWith("waPlay", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EverySettingTheMenuOffersHoldsAValue()
    {
        // 18 toggles and 15 sliders are declared; one slider is the hidden "has this mod run once" marker.
        Assert.Equal(32, RealMenu().Settings.Count());
    }

    [Fact]
    public void SettingsAreReadWithTheirKindAndRange()
    {
        McmMenu menu = RealMenu();

        McmControl toggle = menu.Settings.Single(s => s.SettingName == "waSndEnemyPing");
        Assert.Equal(McmControlKind.Toggle, toggle.Kind);
        Assert.Equal("WASounds", toggle.IniSection);

        McmControl volume = menu.Settings.Single(s => s.SettingName == "waVolEnemyPing");
        Assert.Equal(McmControlKind.Number, volume.Kind);
        Assert.Equal(0, volume.Min);
        Assert.Equal(100, volume.Max);
    }

    [Fact]
    public void TheAuthorsDefaultsAreRead()
    {
        // The game writes a setting only once it has been touched, so on a fresh install user.settings holds
        // nothing for this mod. Without the defaults the whole menu would read as blank.
        McmMenu menu = RealMenu();

        Assert.Equal("true", menu.Settings.Single(s => s.SettingName == "waAimEnabled").Default);
        Assert.Equal("100", menu.Settings.Single(s => s.SettingName == "waVolEnemyPing").Default);
        Assert.Equal("false", menu.Settings.Single(s => s.SettingName == "waMapRevealAll").Default);
    }

    [Fact]
    public void ADefaultIsUsedOnlyWhenNeitherFileMentionsTheSetting()
    {
        var control = new McmControl
        {
            SettingName = "waVolHit", IniSection = "WASounds", Kind = McmControlKind.Number, Default = "100",
        };

        // Nothing loaded at all: the author's default stands in.
        var fresh = McmSettings.Load(new McmMenu { DefaultsIniPath = "", UserIniPath = "" });
        Assert.Equal("100", fresh.Get(control));

        // Once the player has a value of their own, that wins.
        fresh.Set(control, "40");
        Assert.Equal("40", fresh.Get(control));
    }

    [Fact]
    public void SeparatorsAndHiddenBookkeepingAreNotOfferedAsSettings()
    {
        McmMenu menu = RealMenu();

        // "waInit" records that the mod has run once and is hidden from the game's own menu; offering it would
        // invite breaking the mod from outside.
        Assert.DoesNotContain(menu.Settings, s => s.SettingName.Equals("waInit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(menu.Settings, s => s.SettingName.StartsWith("sep", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EverySettingHasSomewhereToBeReadAndWritten()
    {
        McmMenu menu = RealMenu();

        Assert.NotEmpty(menu.Settings);
        Assert.All(menu.Settings, s =>
        {
            Assert.NotEqual("", s.SettingName);
            Assert.NotEqual("", s.IniSection);
            Assert.NotEqual("", s.Label);
        });
        Assert.Equal(@"C:\Docs\user.settings", menu.UserIniPath);
    }

    [Fact]
    public void LabelsAreReadableRatherThanIdentifiers()
    {
        McmMenu menu = RealMenu();

        // The labels are localisation keys into a compiled .w3strings we cannot read, so they are made readable
        // instead of being spoken as identifiers.
        Assert.Equal("Sound: Enemy ping", menu.Settings.Single(s => s.SettingName == "waSndEnemyPing").Label);
        Assert.Equal("Volume: Enemy ping", menu.Settings.Single(s => s.SettingName == "waVolEnemyPing").Label);
        Assert.Equal("Aim enabled", menu.Settings.Single(s => s.SettingName == "waAimEnabled").Label);
    }

    [Fact]
    public void EachGroupIsIntroducedByAHeading()
    {
        McmMenu menu = RealMenu();

        var headings = menu.Controls.Where(c => c.Kind == McmControlKind.NotASetting).Select(c => c.Label).ToList();

        // "WAGeneral" is the mod's own name for the section; the heading is what the game's menu calls it.
        Assert.Equal(new[] { "General", "Sounds" }, headings);
    }

    [Fact]
    public void AnUnknownControlTypeIsStillOffered()
    {
        // A menu using a control this reader does not know must not come out empty.
        McmMenu menu = Witcher3UserConfig.Parse(
            """
            <UserConfig>
              <Group id="XGroup" displayName="x_group">
                <VisibleVars>
                  <Var id="xThing" displayName="x_thing" displayType="OPTIONS_LIST"/>
                </VisibleVars>
              </Group>
            </UserConfig>
            """, "modX", "");

        McmControl only = Assert.Single(menu.Settings);
        Assert.Equal("xThing", only.SettingName);
        Assert.Equal(McmControlKind.Text, only.Kind);
    }

    [Fact]
    public void AGroupWithNothingEditableIsSkipped()
    {
        McmMenu menu = Witcher3UserConfig.Parse(
            """
            <UserConfig>
              <Group id="XGroup" displayName="x_group">
                <VisibleVars>
                  <Var id="sep1" displayName="" displayType="SUBTLE_SEPARATOR"/>
                </VisibleVars>
              </Group>
            </UserConfig>
            """, "modX", "");

        Assert.Empty(menu.Controls);
    }
}
