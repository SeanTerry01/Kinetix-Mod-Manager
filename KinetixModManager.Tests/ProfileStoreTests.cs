using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ProfileStore"/> — saved snapshots of which mods were switched on.
///
/// Profiles matter more here than convenience suggests. Rebuilding a mod set by hand means arrowing a list
/// of a hundred folders and toggling the right forty by ear; getting one back should be one action, and it
/// should be exactly the set that was saved.
/// </summary>
public class ProfileStoreTests : IDisposable
{
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "kinetix-prof-" + Guid.NewGuid().ToString("N"));

	public ProfileStoreTests() => Directory.CreateDirectory(_folder);
	public void Dispose() { try { Directory.Delete(_folder, true); } catch { } }

	private static GameMod Mod(string id, bool enabled) =>
		new() { Name = id, UniqueId = id, IsEnabled = enabled };

	// ---------------------------------------------------------------------
	// Naming — the half that had a real bug in it
	// ---------------------------------------------------------------------

	[Fact]
	public void ANameWithASlashDoesNotBecomeAPathOfItsOwn()
	{
		// "Mage/Thief" used to be pasted straight into Path.Combine, which aimed the write at a subfolder
		// that did not exist and failed. A profile name is a name, not a path.
		string path = ProfileStore.PathFor(_folder, "Mage/Thief");

		Assert.Equal(_folder, Path.GetDirectoryName(path));
		Assert.Equal("MageThief.json", Path.GetFileName(path));
	}

	[Fact]
	public void ANameCannotClimbOutOfTheProfilesFolder()
	{
		string path = ProfileStore.PathFor(_folder, "../../elsewhere");

		Assert.Equal(_folder, Path.GetDirectoryName(path));
	}

	[Fact]
	public void ATrailingDotOrSpaceIsTrimmedBecauseWindowsWouldRefuseTheFile()
	{
		Assert.Equal("Playthrough.json", ProfileStore.FileNameFor("Playthrough."));
		Assert.Equal("Playthrough.json", ProfileStore.FileNameFor("Playthrough "));
	}

	[Fact]
	public void ANameWithNothingUsableInItProducesNoFileRatherThanOneCalledJson()
	{
		Assert.Equal("", ProfileStore.FileNameFor("///"));
		Assert.Equal("", ProfileStore.PathFor(_folder, "///"));
	}

	[Fact]
	public void SavingAndDeletingAgreeAboutTheFileName()
	{
		// They did not. Save built its own name and delete built another, so a profile with an awkward name
		// could be listed and then refuse to go away.
		var profile = ProfileStore.Capture("Mage: Thief", Array.Empty<GameMod>());

		string written = ProfileStore.Save(_folder, profile);

		Assert.True(File.Exists(written));
		Assert.True(ProfileStore.Delete(_folder, profile.Name));
		Assert.False(File.Exists(written));
	}

	// ---------------------------------------------------------------------
	// Capturing and reading back
	// ---------------------------------------------------------------------

	[Fact]
	public void ACapturedProfileRemembersEachModsState()
	{
		var profile = ProfileStore.Capture("Test", new[] { Mod("a", true), Mod("b", false) });

		Assert.True(profile.ModStates["a"]);
		Assert.False(profile.ModStates["b"]);
	}

	[Fact]
	public void AModWithNoUniqueIdIsNotCaptured()
	{
		// It could never be matched back on apply, and storing it under an empty key would collide with
		// every other mod that has no id.
		var profile = ProfileStore.Capture("Test", new[] { new GameMod { Name = "Nameless" } });

		Assert.Empty(profile.ModStates);
	}

	[Fact]
	public void ALoadOrderIsOnlyRecordedWhenOneWasGiven()
	{
		// Null rather than empty is load-bearing: applying a profile that captured no order must leave the
		// current one alone, and an empty list would clear it instead.
		var none = ProfileStore.Capture("Stardew", Array.Empty<GameMod>());
		Assert.Null(none.ModPriority);
		Assert.Null(none.PluginOrder);

		var bethesda = ProfileStore.Capture("Skyrim", Array.Empty<GameMod>(), null,
			new[] { "ModA", "ModB" }, new[] { "a.esp" });
		Assert.Equal(new[] { "ModA", "ModB" }, bethesda.ModPriority);
		Assert.Equal(new[] { "a.esp" }, bethesda.PluginOrder);
	}

	[Fact]
	public void ProfilesComeBackFromDisk()
	{
		ProfileStore.Save(_folder, ProfileStore.Capture("Alpha", new[] { Mod("a", true) }));
		ProfileStore.Save(_folder, ProfileStore.Capture("Beta", new[] { Mod("b", false) }));

		List<ModProfile> loaded = ProfileStore.LoadAll(_folder);

		Assert.Equal(2, loaded.Count);
		Assert.Contains(loaded, p => p.Name == "Alpha" && p.ModStates["a"]);
	}

	[Fact]
	public void OneUnreadableProfileDoesNotCostTheUserTheRest()
	{
		ProfileStore.Save(_folder, ProfileStore.Capture("Good", new[] { Mod("a", true) }));
		File.WriteAllText(Path.Combine(_folder, "broken.json"), "{ not json");

		var failures = new List<string>();
        List<ModProfile> loaded = ProfileStore.LoadAll(_folder, (file, _) => failures.Add(file));

		Assert.Single(loaded);
		Assert.Equal("Good", loaded[0].Name);
		Assert.Equal("broken.json", Assert.Single(failures));
	}

	[Fact]
	public void AMissingFolderIsSimplyNoProfiles()
	{
		Assert.Empty(ProfileStore.LoadAll(Path.Combine(_folder, "not-there")));
	}

	// ---------------------------------------------------------------------
	// Applying
	// ---------------------------------------------------------------------

	[Fact]
	public void OnlyTheModsThatDifferAreChanged()
	{
		// Keeps applying a profile quick rather than renaming every folder on disk to the name it has.
		var profile = ProfileStore.Capture("Saved", new[] { Mod("a", true), Mod("b", false) });
		var now = new[] { Mod("a", true), Mod("b", true) };

		var change = Assert.Single(ProfileStore.Changes(profile, now));

		Assert.Equal("b", change.Mod.UniqueId);
		Assert.False(change.Enable);
	}

	[Fact]
	public void AModTheProfileNeverMentionedIsLeftAlone()
	{
		// A profile saved before a mod existed must not uninstall it by implication.
		var profile = ProfileStore.Capture("Old", new[] { Mod("a", true) });
		var now = new[] { Mod("a", true), Mod("newly-installed", true) };

		Assert.Empty(ProfileStore.Changes(profile, now));
	}

	[Fact]
	public void ApplyingAProfileThatMatchesChangesNothing()
	{
		var mods = new[] { Mod("a", true), Mod("b", false) };

		Assert.Empty(ProfileStore.Changes(ProfileStore.Capture("Same", mods), mods));
	}
}
