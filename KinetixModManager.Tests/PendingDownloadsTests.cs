using System;
using System.Collections.Generic;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="PendingDownloads"/> — which downloads were never installed.
///
/// The list exists for the download somebody said "not now" to. Showing one that is already installed sends them to
/// install it a second time; hiding one that is not means the mod they meant to come back to has quietly vanished.
/// </summary>
public class PendingDownloadsTests
{
	private static readonly DateTime RecordBegan = new(2026, 9, 15, 12, 0, 0);
	private static readonly DateTime Before = RecordBegan.AddDays(-10);
	private static readonly DateTime After = RecordBegan.AddDays(1);

	private static DownloadedFile File(string name, DateTime when, string? id = null, string? version = null) =>
		new(@"C:\Downloads\" + name, when, 1024, id, version, name);

	private static GameMod Nexus(string name, string id, string version) => new() { Name = name, NexusID = id, Version = version };

	private static List<string> Pending(IEnumerable<DownloadedFile> downloads, IEnumerable<GameMod> installed,
		IEnumerable<string>? recorded = null, DateTime? since = null) =>
		PendingDownloads.NotYetInstalled(downloads, recorded ?? Array.Empty<string>(), since ?? RecordBegan,
				installed.ToList(), m => m.NexusID, m => m.Version)
			.Select(d => d.FileName).ToList();

	// ---------------------------------------------------------------------
	// Downloads made once the record was being kept
	// ---------------------------------------------------------------------

	[Fact]
	public void ADownloadRecordedAsInstalledIsLeftOut()
	{
		var download = File("SkyUI-12604-5-2.7z", After, "12604", "5.2");

		Assert.Empty(Pending(new[] { download }, Array.Empty<GameMod>(), recorded: new[] { "SkyUI-12604-5-2.7z" }));
	}

	[Fact]
	public void ADownloadThatWasDeclinedIsListed()
	{
		Assert.Equal(new[] { "SkyUI-12604-5-2.7z" },
			Pending(new[] { File("SkyUI-12604-5-2.7z", After, "12604", "5.2") }, Array.Empty<GameMod>()));
	}

	[Fact]
	public void ADeclinedOptionalFileIsListedEvenThoughItsModIsInstalled()
	{
		// The case guessing gets wrong: an optional file from the page of a mod already installed has the same id
		// and release. Only the record knows it never went in.
		var optional = File("SkyUI Patch-12604-5-2.7z", After, "12604", "5.2");

		Assert.Equal(new[] { optional.FileName },
			Pending(new[] { optional }, new[] { Nexus("SkyUI", "12604", "5.2") }));
	}

	[Fact]
	public void ARecordedNameMatchesWhateverCaseItWasWrittenIn()
	{
		Assert.Empty(Pending(new[] { File("SkyUI-12604-5-2.7z", After) }, Array.Empty<GameMod>(), recorded: new[] { "skyui-12604-5-2.7Z" }));
	}

	// ---------------------------------------------------------------------
	// Downloads from before the record existed
	// ---------------------------------------------------------------------

	[Fact]
	public void AnOldDownloadOfAnInstalledModIsLeftOut()
	{
		Assert.Empty(Pending(new[] { File("SkyUI-12604-5-2.7z", Before, "12604", "5.2") }, new[] { Nexus("SkyUI", "12604", "5.2") }));
	}

	[Fact]
	public void AnOldDownloadOfAnOlderReleaseThanTheInstalledOneIsLeftOut()
	{
		// It was installed once and updated since.
		Assert.Empty(Pending(new[] { File("SkyUI-12604-5-1.7z", Before, "12604", "5.1") }, new[] { Nexus("SkyUI", "12604", "5.2") }));
	}

	[Fact]
	public void AnOldDownloadNewerThanTheInstalledReleaseIsAnUpdateThatWasDeclined()
	{
		Assert.Equal(new[] { "SkyUI-12604-5-3.7z" },
			Pending(new[] { File("SkyUI-12604-5-3.7z", Before, "12604", "5.3") }, new[] { Nexus("SkyUI", "12604", "5.2") }));
	}

	[Fact]
	public void AnOldDownloadOfAModNotInstalledIsListed()
	{
		Assert.Equal(new[] { "Hunterborn-7900-1-6-2.7z" },
			Pending(new[] { File("Hunterborn-7900-1-6-2.7z", Before, "7900", "1.6.2") }, new[] { Nexus("SkyUI", "12604", "5.2") }));
	}

	[Fact]
	public void AnInstalledModWhoseReleaseIsUnknownStillCountsAsInstalled()
	{
		Assert.Empty(Pending(new[] { File("SkyUI-12604-5-2.7z", Before, "12604", "5.2") }, new[] { Nexus("SkyUI", "12604", "") }));
	}

	[Fact]
	public void WithNoIdAnOldDownloadIsMatchedByName()
	{
		var named = File("Stoned Valley", Before);
		var other = File("Something Else Entirely", Before);

		Assert.Equal(new[] { other.FileName },
			Pending(new[] { named, other }, new[] { new GameMod { Name = "[CP] Stoned Valley" } }));
	}

	[Fact]
	public void TheInstalledReleaseComesFromWhatTheCallerRecorded()
	{
		// The manifest says 1.0.0 because the author never bumped it; the download that went in was 1.0.2.
		var download = File("Media Keys Fix-92948-1-0-2.7z", Before, "92948", "1.0.2");
		var mod = Nexus("Media Keys Fix", "92948", "1.0.0");

		List<DownloadedFile> pending = PendingDownloads.NotYetInstalled(new[] { download }, Array.Empty<string>(), RecordBegan,
			new[] { mod }, m => m.NexusID, m => "1.0.2");

		Assert.Empty(pending);
	}

	// ---------------------------------------------------------------------
	// Order, and a settings file with no record at all
	// ---------------------------------------------------------------------

	[Fact]
	public void NewestDownloadsComeFirst()
	{
		var older = File("A-1-1-0.7z", After, "1", "1.0");
		var newer = File("B-2-1-0.7z", After.AddHours(3), "2", "1.0");

		Assert.Equal(new[] { newer.FileName, older.FileName }, Pending(new[] { older, newer }, Array.Empty<GameMod>()));
	}

	[Fact]
	public void WithNoRecordStartedEveryDownloadIsWorkedOutFromWhatIsInstalled()
	{
		var installed = File("SkyUI-12604-5-2.7z", After, "12604", "5.2");
		var notInstalled = File("Hunterborn-7900-1-6-2.7z", After, "7900", "1.6.2");

		List<string> pending = PendingDownloads.NotYetInstalled(new[] { installed, notInstalled }, Array.Empty<string>(), null,
				new[] { Nexus("SkyUI", "12604", "5.2") }, m => m.NexusID, m => m.Version)
			.Select(d => d.FileName).ToList();

		Assert.Equal(new[] { notInstalled.FileName }, pending);
	}
}
