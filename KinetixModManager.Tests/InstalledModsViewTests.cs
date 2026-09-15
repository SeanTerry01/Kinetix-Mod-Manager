using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="InstalledModsView"/> — the installed list as a front end shows it.
///
/// <para>
/// Written because the GTK head got both halves wrong in ways only a blind user would have noticed. It read
/// every game's switched-off state with <em>Minecraft's</em> rule while writing it with the right one, and it
/// announced a game that was not installed at all as having zero mods. Neither throws, neither shows up in a
/// screenshot, and both are the kind of thing that makes a user conclude the program does not support their
/// game.
/// </para>
/// </summary>
public class InstalledModsViewTests
{
	static InstalledModsViewTests() => Loc.Init("en");

	private static GameProfile Game(string id) => GameProfiles.Require(id);

	private static GameMod Mod(string path, string name = "A Mod", string version = "1.0.0") =>
		new() { FolderPath = path, Name = name, Version = version };

	// ---------------------------------------------------------------------
	// Reading the switched-off state
	// ---------------------------------------------------------------------

	[Fact]
	public void AStardewModIsSwitchedOffByALeadingDot()
	{
		// The defect. Minecraft's rule is "does the name end in .jar", which a Stardew folder never does —
		// so every Stardew mod read as switched ON, including the ones that were not, and switching one on
		// moved it to the name it already had.
		var view = InstalledModsView.Of(Game(GameProfiles.StardewValley), "/mods", new[]
		{
			Mod("/mods/ContentPatcher"),
			Mod("/mods/.AutomateOff"),
		});

		Assert.True(view.Rows[0].Enabled);
		Assert.False(view.Rows[1].Enabled);
		Assert.Equal(1, view.DisabledCount);
	}

	[Fact]
	public void AMinecraftModIsSwitchedOffBySuffix()
	{
		var view = InstalledModsView.Of(Game(GameProfiles.Minecraft), "/mods", new[]
		{
			Mod("/mods/sodium.jar"),
			Mod("/mods/lithium.jar.disabled"),
		});

		Assert.True(view.Rows[0].Enabled);
		Assert.False(view.Rows[1].Enabled);
	}

	[Fact]
	public void AWitcherModIsSwitchedOffByATilde()
	{
		var view = InstalledModsView.Of(Game(GameProfiles.Witcher3), "/mods", new[]
		{
			Mod("/mods/modWitcherAccess"),
			Mod("/mods/~modOther"),
		});

		Assert.True(view.Rows[0].Enabled);
		Assert.False(view.Rows[1].Enabled);
	}

	[Fact]
	public void TheSamePathMeansOppositeThingsToTwoGames()
	{
		// The whole reason the rule is not reimplemented in a window. Minecraft asks "does this end in
		// .jar", Stardew asks "does this start with a dot" — and one file answers yes to both, so whichever
		// rule a shared list picked, it would be wrong for one of them.
		const string path = "/mods/.sodium.jar";

		Assert.True(InstalledModsView.Of(Game(GameProfiles.Minecraft), "/mods", new[] { Mod(path) }).Rows[0].Enabled);
		Assert.False(InstalledModsView.Of(Game(GameProfiles.StardewValley), "/mods", new[] { Mod(path) }).Rows[0].Enabled);
	}

	// ---------------------------------------------------------------------
	// What gets said
	// ---------------------------------------------------------------------

	[Fact]
	public void AGameThatIsNotInstalledIsNotAnnouncedAsHavingNoMods()
	{
		// The second defect. "Skyrim Special Edition. 0 mods installed" is indistinguishable by ear from a
		// game that is installed and empty — and the first is a reason to go and install something, while
		// the second is a reason to conclude the manager does not support your game.
		string said = InstalledModsView.NotInstalled(Game(GameProfiles.SkyrimSE)).Announcement;

		Assert.Contains("Skyrim Special Edition", said);
		Assert.DoesNotContain("0 mods", said);
		Assert.Contains("not installed", said);
	}

	[Fact]
	public void AMissingModsFolderSaysSomethingDifferentAgain()
	{
		string notInstalled = InstalledModsView.NotInstalled(Game(GameProfiles.StardewValley)).Announcement;
		string noFolder = InstalledModsView.NoModsFolder(Game(GameProfiles.StardewValley), "/games/sdv/Mods").Announcement;
		string empty = InstalledModsView.Of(Game(GameProfiles.StardewValley), "/games/sdv/Mods", Array.Empty<GameMod>()).Announcement;

		// Three states, three sentences. None of them may be mistaken for another.
		Assert.Equal(3, new[] { notInstalled, noFolder, empty }.Distinct().Count());
		Assert.All(new[] { notInstalled, noFolder, empty }, s => Assert.Contains("Stardew Valley", s));
	}

	[Fact]
	public void TheAnnouncementAlwaysNamesTheGame()
	{
		// A front end that switches games needs the user to hear which one they landed on.
		foreach (GameProfile game in GameProfiles.All)
		{
			var view = InstalledModsView.Of(game, "/mods", new[] { Mod("/mods/Thing") });
			Assert.Contains(game.DisplayName, view.Announcement);
		}
	}

	[Fact]
	public void HowManyAreSwitchedOffIsWorthSaying()
	{
		var view = InstalledModsView.Of(Game(GameProfiles.StardewValley), "/mods", new[]
		{
			Mod("/mods/One"), Mod("/mods/.Two"), Mod("/mods/.Three"),
		});

		Assert.Contains("3", view.Announcement);
		Assert.Contains("2", view.Announcement);
	}

	[Fact]
	public void NothingSwitchedOffDoesNotMentionIt()
	{
		var view = InstalledModsView.Of(Game(GameProfiles.StardewValley), "/mods", new[] { Mod("/mods/One") });

		Assert.DoesNotContain("switched off", view.Announcement);
	}

	// ---------------------------------------------------------------------
	// What a row says
	// ---------------------------------------------------------------------

	[Fact]
	public void ARowLeadsWithTheStateWhenItIsOff()
	{
		// The user is arrowing a list looking for what is switched off. Putting it last means hearing the
		// whole name before the one word being listened for.
		var view = InstalledModsView.Of(Game(GameProfiles.StardewValley), "/mods", new[]
		{
			Mod("/mods/.Automate", "Automate", "2.1.0"),
		});

		Assert.StartsWith("disabled,", view.Rows[0].Spoken);
		Assert.Contains("Automate 2.1.0", view.Rows[0].Spoken);
	}

	[Fact]
	public void ARowThatIsOnJustSaysTheMod()
	{
		var view = InstalledModsView.Of(Game(GameProfiles.StardewValley), "/mods", new[]
		{
			Mod("/mods/Automate", "Automate", "2.1.0"),
		});

		Assert.Equal("Automate 2.1.0", view.Rows[0].Spoken);
	}

	[Fact]
	public void AModWithNoNameFallsBackToItsFileName()
	{
		var view = InstalledModsView.Of(Game(GameProfiles.Minecraft), "/mods", new[]
		{
			Mod("/mods/mystery-1.2.jar", name: "", version: ""),
		});

		Assert.Equal("mystery-1.2.jar", view.Rows[0].Spoken);
	}

	[Fact]
	public void TheStatusLineSaysWhereTheModsCameFrom()
	{
		var view = InstalledModsView.Of(Game(GameProfiles.Minecraft), "/home/me/.minecraft/mods", new[]
		{
			Mod("/home/me/.minecraft/mods/sodium.jar"),
		});

		Assert.Contains("/home/me/.minecraft/mods", view.StatusLine);
	}

	[Fact]
	public void AnUnknownGameIsNotACrash()
	{
		var view = InstalledModsView.Of(null, "/mods", new[] { Mod("/mods/Thing") });

		Assert.Single(view.Rows);
		Assert.False(string.IsNullOrWhiteSpace(view.Announcement));
	}
}
