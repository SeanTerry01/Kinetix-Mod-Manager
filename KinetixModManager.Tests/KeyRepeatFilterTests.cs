using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers the rule that stops a held key running a one-shot command over and over — the reason Refresh
/// Everything ran three or four times when its shortcut was held down for a moment.
/// </summary>
public class KeyRepeatFilterTests
{
	// Stand-ins for KeyData values; the filter only ever compares them.
	private const int ShiftF5 = 1000;
	private const int F1 = 1001;

	[Fact]
	public void TheFirstPressIsAPress()
	{
		var filter = new KeyRepeatFilter();
		Assert.True(filter.IsFirstPress(ShiftF5));
	}

	[Fact]
	public void HoldingTheKeyRepeatsAreNotPresses()
	{
		var filter = new KeyRepeatFilter();

		Assert.True(filter.IsFirstPress(ShiftF5));
		// Windows keeps sending KeyDown for as long as it is held; the command must run once, not four times.
		Assert.False(filter.IsFirstPress(ShiftF5));
		Assert.False(filter.IsFirstPress(ShiftF5));
		Assert.False(filter.IsFirstPress(ShiftF5));
	}

	[Fact]
	public void PressingTheSameKeyAgainAfterReleasingItIsAPress()
	{
		var filter = new KeyRepeatFilter();

		Assert.True(filter.IsFirstPress(ShiftF5));
		filter.Released();

		// Deliberately pressing a shortcut twice must still run it twice, however fast it is done — the
		// release is what separates that from a repeat, not any amount of elapsed time.
		Assert.True(filter.IsFirstPress(ShiftF5));
	}

	[Fact]
	public void ADifferentKeyIsAlwaysAPress()
	{
		var filter = new KeyRepeatFilter();

		Assert.True(filter.IsFirstPress(ShiftF5));
		Assert.True(filter.IsFirstPress(F1));   // rolling from one shortcut onto another, no release between
	}

	[Fact]
	public void ReturningToAKeyAfterAnotherOneIsAPress()
	{
		var filter = new KeyRepeatFilter();

		filter.IsFirstPress(ShiftF5);
		filter.IsFirstPress(F1);
		Assert.True(filter.IsFirstPress(ShiftF5));
	}

	[Fact]
	public void LosingFocusMidPressDoesNotLeaveTheKeyLookingHeld()
	{
		var filter = new KeyRepeatFilter();

		Assert.True(filter.IsFirstPress(ShiftF5));
		filter.FocusLost();   // alt-tabbed away still holding it, so the release lands in another window

		Assert.True(filter.IsFirstPress(ShiftF5));
	}

	[Fact]
	public void AReleaseWithNothingHeldIsHarmless()
	{
		var filter = new KeyRepeatFilter();

		filter.Released();    // key up for a key pressed before the window had focus
		Assert.True(filter.IsFirstPress(ShiftF5));
		Assert.False(filter.IsFirstPress(ShiftF5));
	}
}
