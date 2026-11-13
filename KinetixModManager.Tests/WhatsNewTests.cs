using Xunit;

namespace KinetixModManager.Tests;

/// <summary>When the change log opens by itself at startup: the first run after an update, and only then.</summary>
public sealed class WhatsNewTests
{
	[Theory]
	[InlineData(true, "", "1.6.0")]        // updated from 1.5.1, which recorded no version
	[InlineData(true, null, "1.6.0")]
	[InlineData(true, "1.5.1", "1.6.0")]
	[InlineData(true, "1.6.0", "1.6.1")]
	[InlineData(true, "1.6.9", "1.10.0")]  // compared as versions, not as text
	[InlineData(true, "garbled", "1.6.0")]
	public void TheFirstRunOfANewerVersionShowsIt(bool hasRunBefore, string? last, string current)
	{
		Assert.True(WhatsNew.ShouldShow(hasRunBefore, last, current));
	}

	[Theory]
	[InlineData(false, "", "1.6.0")]       // a first install gets the setup wizard instead
	[InlineData(true, "1.6.0", "1.6.0")]   // the same version again
	[InlineData(true, "1.7.0", "1.6.0")]   // going back to an older build
	[InlineData(true, "1.10.0", "1.6.9")]
	[InlineData(true, "1.5.1", "")]        // no version to speak of
	public void AnythingElseDoesNot(bool hasRunBefore, string? last, string current)
	{
		Assert.False(WhatsNew.ShouldShow(hasRunBefore, last, current));
	}
}
