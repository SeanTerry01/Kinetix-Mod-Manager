using System;
using System.Collections.Generic;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ModUpdatesView"/> — what an updates list says.
///
/// <para>
/// The distinction worth holding is the one an empty list cannot make by itself: "everything is up to date"
/// and "nothing here could be checked" look identical on screen, and only one of them means the user has
/// nothing left to do. A blind user hearing the wrong one either goes hunting for an update that is not
/// there, or sits on an out-of-date accessibility mod believing it was checked.
/// </para>
/// </summary>
public class ModUpdatesViewTests
{
	static ModUpdatesViewTests() => Loc.Init("en");

	private static GameProfile Minecraft => GameProfiles.Require(GameProfiles.Minecraft);

	private static GameMod Mod(string path, string name, string version) =>
		new() { FolderPath = path, Name = name, Version = version };

	[Fact]
	public void AModWithANewerVersionIsAnUpdate()
	{
		var view = ModUpdatesView.Of(Minecraft,
			new[] { Mod("/mods/sodium.jar", "Sodium", "0.5.8") },
			new Dictionary<string, string> { ["/mods/sodium.jar"] = "0.6.0" });

		Assert.Single(view.Rows);
		Assert.Equal("Sodium", view.Rows[0].Name);
		Assert.Equal("0.5.8", view.Rows[0].Installed);
		Assert.Equal("0.6.0", view.Rows[0].Latest);
	}

	[Fact]
	public void AModAlreadyAtTheLatestVersionIsNotAnUpdate()
	{
		var view = ModUpdatesView.Of(Minecraft,
			new[] { Mod("/mods/sodium.jar", "Sodium", "0.6.0") },
			new Dictionary<string, string> { ["/mods/sodium.jar"] = "0.6.0" });

		Assert.Empty(view.Rows);
	}

	[Fact]
	public void AnOlderVersionOnTheCatalogueIsNotAnUpdateEither()
	{
		// A version that merely differs is not newer. Offering a downgrade as an update is how somebody ends
		// up installing a build their game will not start with — the bug the Windows head already fixed once.
		var view = ModUpdatesView.Of(Minecraft,
			new[] { Mod("/mods/sodium.jar", "Sodium", "0.6.0") },
			new Dictionary<string, string> { ["/mods/sodium.jar"] = "0.5.8" });

		Assert.Empty(view.Rows);
	}

	[Fact]
	public void AModTheCatalogueHasNeverSeenIsNotAFailure()
	{
		// Ordinary for a hand-built mod, or one from another site. It is simply not mentioned.
		var view = ModUpdatesView.Of(Minecraft,
			new[] { Mod("/mods/homemade.jar", "Something Homemade", "1.0.0") },
			new Dictionary<string, string>());

		Assert.Empty(view.Rows);
		Assert.Equal(ModUpdatesStatus.Checked, view.Status);
		Assert.Equal(1, view.CheckedCount);
	}

	[Fact]
	public void UpToDateAndCouldNotCheckDoNotSoundAlike()
	{
		string upToDate = ModUpdatesView
			.Of(Minecraft, new[] { Mod("/mods/a.jar", "A", "1.0") }, new Dictionary<string, string>())
			.Announcement;

		string cannot = ModUpdatesView
			.CannotCheck(Minecraft, "Updates cannot be checked from here yet.")
			.Announcement;

		Assert.NotEqual(upToDate, cannot);
		Assert.Contains("up to date", upToDate);
		Assert.Equal("Updates cannot be checked from here yet.", cannot);
	}

	[Fact]
	public void TheAnnouncementCountsWhatIsWaiting()
	{
		var view = ModUpdatesView.Of(Minecraft,
			new[]
			{
				Mod("/mods/a.jar", "A", "1.0"),
				Mod("/mods/b.jar", "B", "1.0"),
				Mod("/mods/c.jar", "C", "1.0"),
			},
			new Dictionary<string, string> { ["/mods/a.jar"] = "2.0", ["/mods/b.jar"] = "2.0" });

		Assert.Equal(2, view.Rows.Count);
		Assert.Contains("2", view.Announcement);
	}

	[Fact]
	public void UpToDateSaysHowManyWereActuallyChecked()
	{
		// "Everything is up to date" over a folder where nothing could be read would be a lie of omission.
		var view = ModUpdatesView.Of(Minecraft,
			new[] { Mod("/mods/a.jar", "A", "1.0"), Mod("/mods/b.jar", "B", "1.0") },
			new Dictionary<string, string>());

		Assert.Contains("2", view.Announcement);
	}

	[Fact]
	public void ARowLeadsWithTheNameAndThenBothVersions()
	{
		// The two versions are the whole decision — whether this update is worth taking now.
		var view = ModUpdatesView.Of(Minecraft,
			new[] { Mod("/mods/sodium.jar", "Sodium", "0.5.8") },
			new Dictionary<string, string> { ["/mods/sodium.jar"] = "0.6.0" });

		string spoken = view.Rows[0].Spoken;

		Assert.StartsWith("Sodium", spoken);
		Assert.Contains("0.5.8", spoken);
		Assert.Contains("0.6.0", spoken);
	}

	[Fact]
	public void NothingInstalledIsUpToDateRatherThanUncheckable()
	{
		var view = ModUpdatesView.Of(Minecraft, Array.Empty<GameMod>(), new Dictionary<string, string>());

		Assert.Equal(ModUpdatesStatus.Checked, view.Status);
		Assert.Empty(view.Rows);
	}
}
