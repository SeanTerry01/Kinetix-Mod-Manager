using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using KinetixModManager;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ModScanner"/> reading a Minecraft mods folder.
///
/// These are the first tests this code has ever had. Scanning lived in ModFileSystem inside the WinForms
/// app, so the test project — which deliberately does not reference that app — could not reach it, and the
/// single most-run piece of logic in the program went unexercised: every refresh, every game switch and
/// every install ends in a scan.
///
/// Minecraft is the layout worth pinning first. It is the only one where a mod is a file rather than a
/// folder, its metadata sits inside a zip, and a mod is disabled by a suffix rather than a prefix — three
/// assumptions every other layout makes the other way.
/// </summary>
public class ModScannerTests : IDisposable
{
	private readonly string _folder = Path.Combine(Path.GetTempPath(), "kinetix-scan-" + Guid.NewGuid().ToString("N"));

	public ModScannerTests() => Directory.CreateDirectory(_folder);

	public void Dispose()
	{
		try { Directory.Delete(_folder, true); } catch { /* a temp folder that will not delete is not a test failure */ }
	}

	/// <summary>Writes a real jar: a zip with a fabric.mod.json at its root, which is what Fabric reads.</summary>
	private string WriteJar(string fileName, string id, string name, string version, string? description = null)
	{
		string path = Path.Combine(_folder, fileName);
		using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
		var manifest = new JObject
		{
			["schemaVersion"] = 1,
			["id"] = id,
			["name"] = name,
			["version"] = version,
			["description"] = description ?? ""
		};
		using var writer = new StreamWriter(zip.CreateEntry(MinecraftLayout.ManifestEntryName).Open(), Encoding.UTF8);
		writer.Write(manifest.ToString());
		return path;
	}

	private List<GameMod> Scan() => ModScanner.ScanMods(
		_folder, new JObject(), new Context(_folder), GameProfiles.Minecraft, (_, _) => { });

	private sealed class Context : IModScanContext
	{
		public Context(string path) => CurrentGamePath = path;
		public string CurrentGamePath { get; }
		public IReadOnlyDictionary<string, string> ModCategories { get; } = new Dictionary<string, string>();
		public IReadOnlyDictionary<string, string> ModNotes { get; } = new Dictionary<string, string>();
	}

	[Fact]
	public void AJarIsReadByWhatIsInsideItRatherThanByItsFileName()
	{
		// The file is named nothing like the mod. Fabric goes by the manifest, so the manager has to as well,
		// or the list reads out download file names instead of mod names.
		WriteJar("sodium-fabric-mc1.21.1-0.6.0-build.3.jar", "sodium", "Sodium", "0.6.0");

		GameMod mod = Assert.Single(Scan());

		Assert.Equal("Sodium", mod.Name);
		Assert.Equal("0.6.0", mod.Version);
		Assert.Equal("sodium", mod.UniqueId);
	}

	[Fact]
	public void AModRenamedToDotDisabledIsStillListed()
	{
		// It has to be: a list that hides disabled mods gives the user no way to switch one back on.
		WriteJar("sodium.jar.disabled", "sodium", "Sodium", "0.6.0");

		GameMod mod = Assert.Single(Scan());

		Assert.Equal("Sodium", mod.Name);
		Assert.False(MinecraftLayout.IsEnabledModFile(mod.FolderPath));
	}

	[Fact]
	public void EnabledAndDisabledModsAreToldApart()
	{
		WriteJar("on.jar", "on", "Always On", "1.0");
		WriteJar("off.jar.disabled", "off", "Switched Off", "1.0");

		var byName = Scan().ToDictionary(m => m.Name);

		Assert.True(MinecraftLayout.IsEnabledModFile(byName["Always On"].FolderPath));
		Assert.False(MinecraftLayout.IsEnabledModFile(byName["Switched Off"].FolderPath));
	}

	[Fact]
	public void SomethingThatIsNotAJarIsIgnored()
	{
		// Mods folders collect these: a README the author included, a config, a half-finished download.
		File.WriteAllText(Path.Combine(_folder, "README.txt"), "not a mod");
		File.WriteAllText(Path.Combine(_folder, "options.txt"), "not a mod either");
		WriteJar("real.jar", "real", "A Real Mod", "1.0");

		Assert.Equal("A Real Mod", Assert.Single(Scan()).Name);
	}

	[Fact]
	public void AJarWithNoManifestStillAppearsRatherThanVanishing()
	{
		// A mod the manager cannot read is still a mod that Fabric will try to load. Dropping it from the
		// list would leave the user with a game that behaves differently from what the manager shows.
		string path = Path.Combine(_folder, "mystery.jar");
		using (var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create))
			zip.CreateEntry("META-INF/MANIFEST.MF");

		GameMod mod = Assert.Single(Scan());

		Assert.False(string.IsNullOrWhiteSpace(mod.Name));
	}

	[Fact]
	public void AnEmptyFolderScansToNothingRatherThanFailing()
	{
		Assert.Empty(Scan());
	}

	[Fact]
	public void ScanningNeverDeletesWhenNoRemovalIsSupplied()
	{
		// The scan used to undeploy, back up and delete the older of two duplicate installs on its own.
		// That behaviour now has to be asked for; a caller that does not ask gets a scan that only reads.
		WriteJar("dupe-old.jar", "dupe", "Duplicate", "1.0");
		WriteJar("dupe-new.jar", "dupe", "Duplicate", "2.0");

		Scan();

		Assert.True(File.Exists(Path.Combine(_folder, "dupe-old.jar")));
		Assert.True(File.Exists(Path.Combine(_folder, "dupe-new.jar")));
	}
}
