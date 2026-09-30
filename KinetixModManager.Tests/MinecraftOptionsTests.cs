using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="MinecraftOptions"/>: making sure Minecraft's Narrator is on for United Minecraft, which speaks
/// only through it. A brand-new install starts with it off, and the game was silent on a friend's computer until
/// Ctrl+B was pressed in it.
/// </summary>
public class MinecraftOptionsTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kmm-mcoptions-" + Guid.NewGuid().ToString("N"));
	private string Options => Path.Combine(_dir, "options.txt");

	public MinecraftOptionsTests() => Directory.CreateDirectory(_dir);

	public void Dispose()
	{
		try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
	}

	[Fact]
	public void AGameThatHasNeverRunGetsASettingsFileWithTheNarratorOn()
	{
		NarratorFix fix = MinecraftOptions.EnsureNarratorOn(Options, dataVersion: 5023);

		Assert.Equal(NarratorFix.Created, fix);
		// The version line first, as Minecraft writes it, so the game does not treat the file as an old one to upgrade.
		Assert.Equal("version:5023\nnarrator:3\n", File.ReadAllText(Options));
	}

	[Fact]
	public void WithNoKnownDataVersionTheFileStillTurnsTheNarratorOn()
	{
		MinecraftOptions.EnsureNarratorOn(Options, dataVersion: null);

		Assert.Equal("narrator:3\n", File.ReadAllText(Options));
	}

	[Fact]
	public void ANarratorSwitchedOffIsSwitchedOnAndNothingElseChanges()
	{
		// Exactly the friend's file after the first run: every other setting must come through byte for byte.
		const string before = "version:5023\nfov:0.0\nnarrator:0\nkey_key.attack:key.mouse.left\n";
		File.WriteAllText(Options, before);

		NarratorFix fix = MinecraftOptions.EnsureNarratorOn(Options, dataVersion: 5023);

		Assert.Equal(NarratorFix.TurnedOn, fix);
		Assert.Equal(before.Replace("narrator:0", "narrator:3"), File.ReadAllText(Options));
	}

	[Theory]
	[InlineData(1)]   // All
	[InlineData(2)]   // Chat
	[InlineData(3)]   // System
	public void AnyModeThePlayerChoseIsLeftAlone(int mode)
	{
		string before = $"version:5023\nnarrator:{mode}\n";
		File.WriteAllText(Options, before);
		DateTime written = File.GetLastWriteTimeUtc(Options);

		Assert.Equal(NarratorFix.AlreadyOn, MinecraftOptions.EnsureNarratorOn(Options, 5023));
		Assert.Equal(before, File.ReadAllText(Options));
		Assert.Equal(written, File.GetLastWriteTimeUtc(Options));
	}

	[Fact]
	public void AFileWithNoNarratorLineHasOneAdded()
	{
		// No line means Minecraft's default, which is off.
		File.WriteAllText(Options, "version:5023\nfov:0.0");

		Assert.Equal(NarratorFix.TurnedOn, MinecraftOptions.EnsureNarratorOn(Options, 5023));
		Assert.Equal("version:5023\nfov:0.0\nnarrator:3\n", File.ReadAllText(Options));
	}

	[Fact]
	public void WindowsLineEndingsAreKept()
	{
		File.WriteAllText(Options, "version:5023\r\nnarrator:0\r\nfov:0.0\r\n");

		MinecraftOptions.EnsureNarratorOn(Options, 5023);

		Assert.Equal("version:5023\r\nnarrator:3\r\nfov:0.0\r\n", File.ReadAllText(Options));
	}

	[Fact]
	public void AnUnreadableNarratorValueIsTreatedAsOff()
	{
		File.WriteAllText(Options, "narrator:banana\n");

		Assert.Equal(NarratorFix.TurnedOn, MinecraftOptions.EnsureNarratorOn(Options, null));
		Assert.Equal("narrator:3\n", File.ReadAllText(Options));
	}

	[Fact]
	public void NoByteOrderMarkIsWritten()
	{
		// Minecraft writes plain UTF-8. A BOM would glue itself to "version" and the game would lose that line.
		MinecraftOptions.EnsureNarratorOn(Options, 5023);

		Assert.NotEqual(0xEF, File.ReadAllBytes(Options)[0]);
	}

	// -------------------------------------------------------------------------
	// The data version
	// -------------------------------------------------------------------------

	[Fact]
	public void TheDataVersionIsReadFromTheGameJarInsideTheFabricProfile()
	{
		// Where the launcher really leaves it: the vanilla folder holds only JSON, the jar sits under Fabric's name.
		WriteGameJar("fabric-loader-0.19.5-26.3", worldVersion: 5023);

		Assert.Equal(5023, MinecraftOptions.DataVersionOf(_dir, "fabric-loader-0.19.5-26.3", "26.3"));
	}

	[Fact]
	public void TheVanillaJarIsUsedWhenTheProfileHasNone()
	{
		WriteGameJar("1.21.10", worldVersion: 4556);

		Assert.Equal(4556, MinecraftOptions.DataVersionOf(_dir, "fabric-loader-0.19.5-1.21.10", "1.21.10"));
	}

	[Fact]
	public void NoJarMeansNoDataVersion()
	{
		Assert.Null(MinecraftOptions.DataVersionOf(_dir, "26.3"));
	}

	private void WriteGameJar(string id, int worldVersion)
	{
		string folder = Path.Combine(_dir, "versions", id);
		Directory.CreateDirectory(folder);
		using ZipArchive zip = ZipFile.Open(Path.Combine(folder, id + ".jar"), ZipArchiveMode.Create);
		using Stream s = zip.CreateEntry("version.json").Open();
		s.Write(Encoding.UTF8.GetBytes($"{{ \"id\": \"{id}\", \"world_version\": {worldVersion} }}"));
	}
}
