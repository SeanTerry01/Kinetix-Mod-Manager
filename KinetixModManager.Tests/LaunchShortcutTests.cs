using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// The desktop shortcut that starts a game without opening the manager (GitHub issue #2): what it carries, what a second
/// copy of the manager passes on, and what the file is called.
/// </summary>
public sealed class LaunchShortcutTests
{
	[Theory]
	[InlineData("SkyrimSE")]
	[InlineData("SkyrimSE@Gog")]
	// A modpack's key, which can hold spaces.
	[InlineData("Minecraft#Visually Impaired Access Mods")]
	public void WhatTheShortcutCarriesIsReadBackAsTheSameGame(string key)
	{
		string[] args = SplitLikeWindows(LaunchShortcut.ArgumentsFor(key));
		Assert.Equal(key, LaunchShortcut.GameToLaunch(args));
	}

	[Fact]
	public void AnOrdinaryStartAsksForNoGame()
	{
		Assert.Null(LaunchShortcut.GameToLaunch(Array.Empty<string>()));
		Assert.Null(LaunchShortcut.GameToLaunch(new[] { "nxm://skyrimspecialedition/mods/1/files/2" }));
		// The switch with nothing after it is not a request for a game called "".
		Assert.Null(LaunchShortcut.GameToLaunch(new[] { "--launch" }));
		Assert.Null(LaunchShortcut.GameToLaunch(new[] { "--launch", "  " }));
	}

	[Fact]
	public void TheSwitchIsFoundWhateverItsCase()
	{
		Assert.Equal("Witcher3", LaunchShortcut.GameToLaunch(new[] { "--LAUNCH", "Witcher3" }));
	}

	[Theory]
	[InlineData("SkyrimSE")]
	[InlineData("Minecraft#Visually Impaired Access Mods")]
	public void ALaunchPassedToTheRunningManagerNamesTheSameGame(string key)
	{
		string message = LaunchShortcut.LaunchRequest(key);
		Assert.True(LaunchShortcut.IsManagerMessage(message));
		Assert.False(LaunchShortcut.IsShowRequest(message));
		Assert.Equal(key, LaunchShortcut.LaunchRequested(message));
	}

	[Fact]
	public void OpeningTheManagerAgainAsksTheRunningOneToComeForward()
	{
		string message = LaunchShortcut.ShowRequest;
		Assert.True(LaunchShortcut.IsManagerMessage(message));
		Assert.True(LaunchShortcut.IsShowRequest(message));
		Assert.Null(LaunchShortcut.LaunchRequested(message));
	}

	[Fact]
	public void ADownloadLinkIsStillADownloadLink()
	{
		// The pipe carries both, and a link taken for a message would never be downloaded.
		const string link = "nxm://skyrimspecialedition/mods/12604/files/1000?key=abc&expires=1";
		Assert.False(LaunchShortcut.IsManagerMessage(link));
		Assert.Null(LaunchShortcut.LaunchRequested(link));
		Assert.False(LaunchShortcut.IsShowRequest(link));
	}

	[Theory]
	[InlineData("Skyrim Special Edition (Kinetix)", "Skyrim Special Edition (Kinetix).lnk")]
	// Colons aren't allowed in a Windows file name.
	[InlineData("The Witcher 3: Wild Hunt (Kinetix)", "The Witcher 3 Wild Hunt (Kinetix).lnk")]
	// A modpack can be called anything.
	[InlineData("Access / Mods? <Beta> (Kinetix)", "Access Mods Beta (Kinetix).lnk")]
	// Windows drops dots and spaces from the end of a name.
	[InlineData("Pack...", "Pack.lnk")]
	[InlineData(":::", "Kinetix.lnk")]
	public void TheFileNameIsOneWindowsAccepts(string name, string expected)
	{
		Assert.Equal(expected, LaunchShortcut.FileNameFor(name));
	}

	/// <summary>How Windows hands a shortcut's arguments to the program: split at spaces, except inside quotes.</summary>
	private static string[] SplitLikeWindows(string commandLine)
	{
		var args = new List<string>();
		var current = new System.Text.StringBuilder();
		bool quoted = false, any = false;
		foreach (char c in commandLine)
		{
			if (c == '"') { quoted = !quoted; any = true; continue; }
			if (c == ' ' && !quoted)
			{
				if (any || current.Length > 0) args.Add(current.ToString());
				current.Clear();
				any = false;
				continue;
			}
			current.Append(c);
		}
		if (any || current.Length > 0) args.Add(current.ToString());
		return args.ToArray();
	}
}
