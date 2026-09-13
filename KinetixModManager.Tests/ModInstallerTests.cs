using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using KinetixModManager;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ModInstaller"/> putting a downloaded mod where the game will load it.
///
/// The rule under test is removing what the new file replaces. Fabric loads every jar in the folder, so
/// leaving an old copy beside a new one does not give the player the newer mod — it gives them a game that
/// refuses to start on a duplicate id. For a mod their game's speech depends on, that is the difference
/// between an install that did not work and a game that no longer talks.
/// </summary>
public class ModInstallerTests : IDisposable
{
	private readonly string _mods = Path.Combine(Path.GetTempPath(), "kinetix-inst-" + Guid.NewGuid().ToString("N"));
	private readonly string _incoming = Path.Combine(Path.GetTempPath(), "kinetix-dl-" + Guid.NewGuid().ToString("N"));

	public ModInstallerTests()
	{
		Directory.CreateDirectory(_mods);
		Directory.CreateDirectory(_incoming);
	}

	public void Dispose()
	{
		foreach (string d in new[] { _mods, _incoming })
			try { Directory.Delete(d, true); } catch { }
	}

	private string Jar(string folder, string fileName, string id, string version)
	{
		string path = Path.Combine(folder, fileName);
		using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
		using var writer = new StreamWriter(zip.CreateEntry(MinecraftLayout.ManifestEntryName).Open(), Encoding.UTF8);
		writer.Write(new JObject
		{
			["schemaVersion"] = 1, ["id"] = id, ["name"] = id, ["version"] = version
		}.ToString());
		return path;
	}

	[Fact]
	public void AnInstallLandsInTheModsFolder()
	{
		string downloaded = Jar(_incoming, "sodium-0.6.0.jar", "sodium", "0.6.0");

		var result = ModInstaller.InstallFile(downloaded, _mods);

		Assert.True(File.Exists(result.Path));
		Assert.Equal(_mods, Path.GetDirectoryName(result.Path));
		Assert.Equal("sodium", result.ModId);
		Assert.False(result.WasUpgrade);
	}

	[Fact]
	public void TheOlderCopyIsRemovedEvenThoughItsFileNameIsQuiteDifferent()
	{
		// Matched by the id inside the jar, because the file name is whatever the author called the download
		// and changes between releases: these two share no prefix worth matching on.
		Jar(_mods, "sodium-fabric-0.5.8.jar", "sodium", "0.5.8");
		string downloaded = Jar(_incoming, "sodium-fabric-mc1.21.1-0.6.0.jar", "sodium", "0.6.0");

		var result = ModInstaller.InstallFile(downloaded, _mods);

		Assert.True(result.WasUpgrade);
		Assert.Equal("sodium-fabric-0.5.8.jar", Assert.Single(result.Replaced));
		Assert.Single(Directory.GetFiles(_mods));
	}

	[Fact]
	public void ADisabledOlderCopyIsRemovedToo()
	{
		// Invisible to Fabric but not to the manager: left behind, the list shows the mod twice and
		// switching the old one on breaks the game in a way that looks unrelated to this install.
		Jar(_mods, "sodium-0.5.8.jar.disabled", "sodium", "0.5.8");
		string downloaded = Jar(_incoming, "sodium-0.6.0.jar", "sodium", "0.6.0");

		var result = ModInstaller.InstallFile(downloaded, _mods);

		Assert.True(result.WasUpgrade);
		Assert.Single(Directory.GetFiles(_mods));
	}

	[Fact]
	public void ADifferentModIsLeftAlone()
	{
		Jar(_mods, "lithium.jar", "lithium", "0.13.0");
		string downloaded = Jar(_incoming, "sodium.jar", "sodium", "0.6.0");

		var result = ModInstaller.InstallFile(downloaded, _mods);

		Assert.False(result.WasUpgrade);
		Assert.Equal(2, Directory.GetFiles(_mods).Length);
	}

	[Fact]
	public void AJarThatCannotBeReadIsNeverDeletedOnAGuess()
	{
		// Removing it would be deleting a mod the user installed for reasons we failed to understand.
		File.WriteAllText(Path.Combine(_mods, "mystery.jar"), "not a zip at all");
		string downloaded = Jar(_incoming, "sodium.jar", "sodium", "0.6.0");

		var result = ModInstaller.InstallFile(downloaded, _mods);

		Assert.False(result.WasUpgrade);
		Assert.True(File.Exists(Path.Combine(_mods, "mystery.jar")));
	}

	[Fact]
	public void InstallingOverTheSameVersionReplacesItWithoutCountingItself()
	{
		Jar(_mods, "sodium.jar", "sodium", "0.6.0");
		string downloaded = Jar(_incoming, "sodium.jar", "sodium", "0.6.0");

		var result = ModInstaller.InstallFile(downloaded, _mods);

		// The file it just wrote must not be found as "an older copy" and deleted.
		Assert.True(File.Exists(result.Path));
		Assert.Empty(result.Replaced);
	}

	[Fact]
	public void NothingDownloadedIsAnErrorRatherThanASilentNoOp()
	{
		Assert.Throws<FileNotFoundException>(() =>
			ModInstaller.InstallFile(Path.Combine(_incoming, "never-downloaded.jar"), _mods));
	}

	[Fact]
	public void AModsFolderThatDoesNotExistYetIsCreated()
	{
		string fresh = Path.Combine(_mods, "nested", "mods");
		string downloaded = Jar(_incoming, "sodium.jar", "sodium", "0.6.0");

		var result = ModInstaller.InstallFile(downloaded, fresh);

		Assert.True(File.Exists(result.Path));
	}

	[Fact]
	public void LookingForCopiesOfNothingFindsNothing()
	{
		Jar(_mods, "sodium.jar", "sodium", "0.6.0");

		Assert.Empty(ModInstaller.ExistingCopies(_mods, "", ""));
		Assert.Empty(ModInstaller.ExistingCopies(Path.Combine(_mods, "absent"), "", "sodium"));
	}
}
