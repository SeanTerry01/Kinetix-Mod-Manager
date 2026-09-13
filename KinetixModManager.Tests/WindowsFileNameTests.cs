using System;
using System.IO;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="WindowsFileName"/>, which states the characters Windows forbids in a name instead of
/// asking the host what it forbids.
///
/// The distinction is not academic. <see cref="Path.GetInvalidFileNameChars"/> returns 41 characters on
/// Windows and 2 on Linux, so a sanitiser built on it does not fail off Windows — it quietly stops
/// sanitising, and hands a Proton-hosted game a folder it cannot open. These tests pin the set so it cannot
/// drift back to the host's answer without something going red.
/// </summary>
public class WindowsFileNameTests
{
	[Fact]
	public void TheReservedPunctuationWindowsRefusesIsRefusedOnEveryHost()
	{
		// The nine printable characters Win32 path syntax reserves. Linux permits all but the slash, which is
		// exactly why asking the host is the wrong question.
		foreach (char c in new[] { '"', '<', '>', '|', ':', '*', '?', '\\', '/' })
			Assert.True(WindowsFileName.IsInvalid(c), $"{c} should be refused");
	}

	[Fact]
	public void TheControlCharactersAreRefusedToo()
	{
		Assert.All(Enumerable.Range(0, 32), i => Assert.True(WindowsFileName.IsInvalid((char)i)));
		// 127 is not on Windows' list, and guessing that it were would quietly rename files nobody asked about.
		Assert.False(WindowsFileName.IsInvalid((char)127));
	}

	[Fact]
	public void AnOrdinaryModNameIsLeftAlone()
	{
		Assert.Equal("SkyUI", WindowsFileName.StripInvalid("SkyUI"));
		Assert.Equal("Unofficial Skyrim Special Edition Patch",
			WindowsFileName.StripInvalid("Unofficial Skyrim Special Edition Patch"));
		// Dots and spaces survive: callers disagree about trailing ones, so trimming is left to them.
		Assert.Equal("SMAPI 4.1.10.", WindowsFileName.StripInvalid("SMAPI 4.1.10."));
	}

	[Fact]
	public void ANameWindowsWouldRefuseComesBackAsOneItAccepts()
	{
		Assert.Equal("Skyrim Reloaded best", WindowsFileName.StripInvalid("Skyrim: Reloaded? <best>"));
	}

	[Fact]
	public void AnEmptyNameIsNotTurnedIntoSomethingElse()
	{
		Assert.Equal("", WindowsFileName.StripInvalid(""));
		Assert.Equal("", WindowsFileName.StripInvalid(null!));
	}

	[Fact]
	public void OnWindowsTheSetIsExactlyWhatTheFrameworkReports()
	{
		// The whole point is to be Windows' answer everywhere, so where the host IS Windows the two must agree.
		// Off Windows there is nothing to compare against - the framework's answer is the one being avoided.
		if (!OperatingSystem.IsWindows()) return;

		Assert.Equal(
			Path.GetInvalidFileNameChars().OrderBy(c => c).ToArray(),
			WindowsFileName.InvalidChars.OrderBy(c => c).ToArray());
	}
}
