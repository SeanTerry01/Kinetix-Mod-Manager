using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers backing up a mod that is a single file rather than a folder.
///
/// <para>
/// Minecraft's mods are <c>.jar</c> files, and <see cref="BackupStore.CreateBackup"/> returned without a word
/// for anything that was not a directory. So deleting a Minecraft mod kept **no backup at all** while the
/// manager reported that it had made one — on Windows as well as Linux. Silent, and only discovered by
/// somebody wanting the mod back.
/// </para>
/// </summary>
public class BackupOfFileModsTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kinetix-bak-" + Guid.NewGuid().ToString("N"));
	private readonly string _backups;

	public BackupOfFileModsTests()
	{
		Directory.CreateDirectory(_dir);
		_backups = Path.Combine(_dir, "backups");
	}

	public void Dispose()
	{
		try { Directory.Delete(_dir, true); } catch { }
	}

	private string Jar(string name, string contents = "pretend jar")
	{
		string path = Path.Combine(_dir, name);
		File.WriteAllText(path, contents);
		return path;
	}

	[Fact]
	public void AFileModIsBackedUp()
	{
		BackupStore.CreateBackup(Jar("sodium.jar"), "Sodium", _backups);

		Assert.Single(Directory.GetFiles(_backups, "Sodium_*.zip"));
	}

	[Fact]
	public void TheBackupHoldsTheModItself()
	{
		// Zipped rather than copied, so a backup is one kind of thing whatever shape the mod is — which is
		// what lets List() find it the same way for every game.
		BackupStore.CreateBackup(Jar("sodium.jar", "the real bytes"), "Sodium", _backups);

		string backup = Directory.GetFiles(_backups, "Sodium_*.zip").Single();
		using ZipArchive zip = ZipFile.OpenRead(backup);

		ZipArchiveEntry entry = Assert.Single(zip.Entries);
		Assert.Equal("sodium.jar", entry.Name);

		using var reader = new StreamReader(entry.Open());
		Assert.Equal("the real bytes", reader.ReadToEnd());
	}

	[Fact]
	public void AFolderModStillWorksExactlyAsBefore()
	{
		string mod = Path.Combine(_dir, "ContentPatcher");
		Directory.CreateDirectory(mod);
		File.WriteAllText(Path.Combine(mod, "manifest.json"), "{}");

		BackupStore.CreateBackup(mod, "ContentPatcher", _backups);

		string backup = Directory.GetFiles(_backups, "ContentPatcher_*.zip").Single();
		using ZipArchive zip = ZipFile.OpenRead(backup);
		Assert.Contains(zip.Entries, e => e.Name == "manifest.json");
	}

	[Fact]
	public void ProgressStillReachesAHundred()
	{
		// The install screen speaks this, so a file-shaped mod must not leave it hanging at nothing.
		var reported = new System.Collections.Generic.List<double>();
		BackupStore.CreateBackup(Jar("sodium.jar"), "Sodium", _backups, new Progress(reported.Add));

		Assert.NotEmpty(reported);
		Assert.Equal(100.0, reported[^1]);
	}

	[Fact]
	public void SomethingThatIsNotThereIsStillNotABackup()
	{
		BackupStore.CreateBackup(Path.Combine(_dir, "never-existed.jar"), "Ghost", _backups);

		Assert.False(Directory.Exists(_backups) && Directory.GetFiles(_backups).Length > 0);
	}

	[Fact]
	public void AFileBackupIsFoundByTheBackupsList()
	{
		// The whole reason it is a zip: whatever a backup was made from, the restore list has to see it.
		BackupStore.CreateBackup(Jar("sodium.jar"), "Sodium", _backups);

		Assert.Contains(BackupStore.List(_backups), b => b.Name.StartsWith("Sodium", StringComparison.Ordinal));
	}

	/// <summary>Synchronous <see cref="IProgress{T}"/>; the built-in one posts to a context tests do not have.</summary>
	private sealed class Progress : IProgress<double>
	{
		private readonly Action<double> _report;
		public Progress(Action<double> report) => _report = report;
		public void Report(double value) => _report(value);
	}
}
