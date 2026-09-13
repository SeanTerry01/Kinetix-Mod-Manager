using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="BackupStore"/> — the zip taken before every update and every deletion.
///
/// It is the only reason an update that goes wrong is recoverable, and that matters more here than usual: a
/// mod whose failure mode is the game going quiet cannot be diagnosed by looking at it, so putting back
/// exactly what was there before is often the only way out.
/// </summary>
public class BackupStoreTests : IDisposable
{
	private readonly string _source = Path.Combine(Path.GetTempPath(), "kinetix-src-" + Guid.NewGuid().ToString("N"));
	private readonly string _backups = Path.Combine(Path.GetTempPath(), "kinetix-bak-" + Guid.NewGuid().ToString("N"));

	public BackupStoreTests()
	{
		Directory.CreateDirectory(_source);
		Directory.CreateDirectory(_backups);
	}

	public void Dispose()
	{
		foreach (string d in new[] { _source, _backups })
			try { Directory.Delete(d, true); } catch { }
	}

	// ---------------------------------------------------------------------
	// Reading the name back
	// ---------------------------------------------------------------------

	[Fact]
	public void TheTimestampIsStrippedToGiveTheModName()
	{
		Assert.Equal("SkyUI", BackupStore.ModNameFromFileName("SkyUI_20260913_142530.zip"));
	}

	[Fact]
	public void AModNameContainingUnderscoresSurvivesIntact()
	{
		// Done by length rather than by splitting on the underscore. Mod names contain underscores far more
		// often than not, and splitting would list this one as "SMAPI".
		Assert.Equal("SMAPI_Console_Commands",
			BackupStore.ModNameFromFileName("SMAPI_Console_Commands_20260913_142530.zip"));
	}

	[Fact]
	public void AFileWithNoTimestampKeepsItsWholeName()
	{
		// A backup copied in by hand, or made by an older build. Trimming sixteen characters off it anyway
		// would show the user a truncated name for a file that is still perfectly restorable.
		Assert.Equal("HandMadeBackup", BackupStore.ModNameFromFileName("HandMadeBackup.zip"));
	}

	// ---------------------------------------------------------------------
	// Taking one
	// ---------------------------------------------------------------------

	[Fact]
	public void BackingUpAFolderProducesAZipThatContainsIt()
	{
		File.WriteAllText(Path.Combine(_source, "manifest.json"), "{}");
		Directory.CreateDirectory(Path.Combine(_source, "assets"));
		File.WriteAllText(Path.Combine(_source, "assets", "thing.png"), "x");

		BackupStore.CreateBackup(_source, "MyMod", _backups);

		string zip = Assert.Single(Directory.GetFiles(_backups, "*.zip"));
		using var archive = ZipFile.OpenRead(zip);
		Assert.Contains(archive.Entries, e => e.FullName.EndsWith("manifest.json"));
		Assert.Contains(archive.Entries, e => e.FullName.EndsWith("thing.png"));
	}

	[Fact]
	public void AnEmptyFolderInsideTheModIsKept()
	{
		// Restoring a backup that silently dropped a folder the mod expects to exist gives the user a mod
		// that is present and broken, which is harder to notice than one that is missing.
		File.WriteAllText(Path.Combine(_source, "manifest.json"), "{}");
		Directory.CreateDirectory(Path.Combine(_source, "empty-on-purpose"));

		BackupStore.CreateBackup(_source, "MyMod", _backups, new Progress<double>());

		using var archive = ZipFile.OpenRead(Directory.GetFiles(_backups, "*.zip").Single());
		Assert.Contains(archive.Entries, e => e.FullName.Contains("empty-on-purpose"));
	}

	[Fact]
	public void BackingUpAFolderThatIsNotThereDoesNothingRatherThanFailing()
	{
		BackupStore.CreateBackup(Path.Combine(_source, "absent"), "Gone", _backups);

		Assert.Empty(Directory.GetFiles(_backups, "*.zip"));
	}

	// ---------------------------------------------------------------------
	// Listing and pruning
	// ---------------------------------------------------------------------

	[Fact]
	public void BackupsAreListedWithTheirModNames()
	{
		File.WriteAllText(Path.Combine(_source, "a.txt"), "x");
		BackupStore.CreateBackup(_source, "Alpha", _backups);
		BackupStore.CreateBackup(_source, "Beta", _backups);

		var names = BackupStore.List(_backups).Select(b => b.Name).ToList();

		Assert.Contains("Alpha", names);
		Assert.Contains("Beta", names);
	}

	[Fact]
	public void AMissingBackupsFolderIsSimplyNoBackups()
	{
		Assert.Empty(BackupStore.List(Path.Combine(_backups, "not-there")));
		Assert.Empty(BackupStore.List(""));
	}

	[Fact]
	public void PruningKeepsTheNewestAndRemovesTheRest()
	{
		File.WriteAllText(Path.Combine(_source, "a.txt"), "x");
		for (int i = 0; i < 4; i++)
		{
			// Distinct names, since the timestamp only has one-second resolution and these are written at once.
			string path = Path.Combine(_backups, $"MyMod_2026091{i}_120000.zip");
			ZipFile.CreateFromDirectory(_source, path);
			File.SetCreationTimeUtc(path, new DateTime(2026, 9, 1 + i, 12, 0, 0, DateTimeKind.Utc));
		}

		BackupStore.PruneBackups("MyMod", _backups, maxCount: 2);

		Assert.Equal(2, Directory.GetFiles(_backups, "MyMod_*.zip").Length);
	}

	[Fact]
	public void PruningLeavesAnotherModsBackupsAlone()
	{
		File.WriteAllText(Path.Combine(_source, "a.txt"), "x");
		BackupStore.CreateBackup(_source, "Mine", _backups);
		BackupStore.CreateBackup(_source, "Theirs", _backups);

		BackupStore.PruneBackups("Mine", _backups, maxCount: 0);

		Assert.Single(Directory.GetFiles(_backups, "Theirs_*.zip"));
	}
}
