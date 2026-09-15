using System;
using System.Collections.Generic;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ProfilesView"/> — the saved profiles, and what switching to one would do.
///
/// <para>
/// The second half is the point. Applying a profile moves mods in and out of a game the user is about to
/// play, and a list that only says "Mage" and "Thief" asks them to remember which is which. Saying how many
/// mods would move, before anything moves, is what makes that an informed choice.
/// </para>
/// </summary>
public class ProfilesViewTests
{
	static ProfilesViewTests() => Loc.Init("en");

	private static GameMod Mod(string id, bool enabled) =>
		new() { UniqueId = id, Name = id, FolderPath = "/mods/" + id, IsEnabled = enabled };

	private static ModProfile Profile(string name, params (string Id, bool On)[] states)
	{
		var profile = new ModProfile { Name = name };
		foreach (var (id, on) in states) profile.ModStates[id] = on;
		return profile;
	}

	[Fact]
	public void ARowSaysHowManyModsAProfileHoldsAndHowManyAreOn()
	{
		var view = ProfilesView.Of(
			new[] { Profile("Mage", ("a", true), ("b", true), ("c", false)) },
			Array.Empty<GameMod>());

		Assert.Single(view.Rows);
		Assert.Equal(3, view.Rows[0].ModCount);
		Assert.Equal(2, view.Rows[0].EnabledCount);
	}

	[Fact]
	public void TheProfileYouAreAlreadyOnSaysSoFirst()
	{
		// It is the one fact that decides whether the row is worth further thought, so it leads — the same
		// reasoning that puts "Installed" at the front of a search result.
		var installed = new[] { Mod("a", true), Mod("b", false) };
		var view = ProfilesView.Of(new[] { Profile("Current", ("a", true), ("b", false)) }, installed);

		Assert.True(view.Rows[0].IsCurrent);
		Assert.Contains("current setup", view.Rows[0].Spoken);
	}

	[Fact]
	public void AProfileTheModsHaveDepartedFromIsNoLongerCurrent()
	{
		// "Current" means the mods on disk match it, not that it was selected last. A profile applied and
		// then changed by hand is not the one you are on, and saying it is would be the manager's
		// bookkeeping contradicting the user's own folder.
		var installed = new[] { Mod("a", true), Mod("b", true) };
		var view = ProfilesView.Of(new[] { Profile("Mage", ("a", true), ("b", false)) }, installed);

		Assert.False(view.Rows[0].IsCurrent);
	}

	[Fact]
	public void ProfilesAreListedInAnOrderAPersonCanPredict()
	{
		var view = ProfilesView.Of(
			new[] { Profile("Winter"), Profile("archery"), Profile("Mage") },
			Array.Empty<GameMod>());

		Assert.Equal(new[] { "archery", "Mage", "Winter" }, view.Rows.Select(r => r.Name));
	}

	[Fact]
	public void NoProfilesSaysSoRatherThanSayingZero()
	{
		string said = ProfilesView.Of(Array.Empty<ModProfile>(), Array.Empty<GameMod>()).Announcement;

		Assert.Contains("not saved any", said);
		Assert.DoesNotContain("0 saved", said);
	}

	// ---------------------------------------------------------------------
	// What switching would do
	// ---------------------------------------------------------------------

	[Fact]
	public void SwitchingToWhatYouAlreadyHaveSaysNothingWouldChange()
	{
		var installed = new[] { Mod("a", true), Mod("b", false) };

		string said = ProfilesView.DescribeApplying(Profile("Same", ("a", true), ("b", false)), installed);

		Assert.Contains("Nothing would change", said);
	}

	[Fact]
	public void TurningModsOnAndOffAreCountedSeparately()
	{
		// Two different things are about to happen to the user's game, and one number covering both would
		// tell them less than either.
		var installed = new[] { Mod("a", false), Mod("b", false), Mod("c", true) };

		string said = ProfilesView.DescribeApplying(
			Profile("Mage", ("a", true), ("b", true), ("c", false)), installed);

		Assert.Contains("2", said);
		Assert.Contains("1", said);
	}

	[Fact]
	public void OnlyTurningOnReadsDifferentlyFromOnlyTurningOff()
	{
		var installed = new[] { Mod("a", false), Mod("b", true) };

		string onOnly = ProfilesView.DescribeApplying(Profile("On", ("a", true)), installed);
		string offOnly = ProfilesView.DescribeApplying(Profile("Off", ("b", false)), installed);

		Assert.NotEqual(onOnly, offOnly);
		Assert.Contains("on", onOnly);
		Assert.Contains("off", offOnly);
	}

	[Fact]
	public void AModTheProfileNeverMentionedIsLeftAlone()
	{
		// A profile saved before a mod was installed must not switch it off by implication.
		var installed = new[] { Mod("a", true), Mod("newer", true) };

		string said = ProfilesView.DescribeApplying(Profile("Old", ("a", true)), installed);

		Assert.Contains("Nothing would change", said);
	}
}
