using System;
using System.IO;
using KinetixModManager;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>Where a restored backup goes. Getting it wrong leaves the broken mod running and reports success.</summary>
public sealed class BackupRestoreTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "kmm-restore-" + Guid.NewGuid().ToString("N"));
	private readonly string _extracted;
	private readonly string _mods;

	public BackupRestoreTests()
	{
		_extracted = Path.Combine(_root, "extracted");
		_mods = Path.Combine(_root, "Mods");
		Directory.CreateDirectory(_extracted);
		Directory.CreateDirectory(_mods);
	}

	public void Dispose()
	{
		try { ModOnDisk.DeleteDirectory(_root); } catch (IOException) { }
	}

	private GameMod Mod(string folder, string id, string? name = null) => new()
	{
		Name = name ?? id, UniqueId = id, FolderPath = Path.Combine(_mods, folder)
	};

	private void Manifest(string file, string id) =>
		File.WriteAllText(Path.Combine(_extracted, file), new JObject { ["UniqueID"] = id }.ToString());

	[Fact]
	public void AStardewBackupGoesBackOverTheModWithItsId()
	{
		Manifest("manifest.json", "a.cool");
		GameMod cool = Mod("CoolMod", "a.cool", "Cool Mod");

		(string destination, GameMod? replaces) = BackupRestore.Plan(_extracted, "Cool Mod_20260101_120000.zip", new[] { cool }, _mods);

		Assert.Same(cool, replaces);
		Assert.Equal(cool.FolderPath, destination);
	}

	[Fact]
	public void ASwitchedOffModStaysSwitchedOff()
	{
		Manifest("manifest.json", "a.cool");
		GameMod cool = Mod(".CoolMod", "a.cool");

		(string destination, _) = BackupRestore.Plan(_extracted, "Cool_20260101_120000.zip", new[] { cool }, _mods);

		Assert.EndsWith(".CoolMod", destination);
	}

	[Fact]
	public void ABethesdaBackupDoesNotBecomeASecondModNamedAfterItsTimestamp()
	{
		// The original bug: a second folder called Foo_20260101_120000, with the broken copy still enabled.
		Manifest(".manager_manifest.json", "SkyUI");
		GameMod sky = Mod("SkyUI", "SkyUI", "SkyUI, a Friendly Page Title");

		(string destination, GameMod? replaces) = BackupRestore.Plan(_extracted, "SkyUI_20260101_120000.zip", new[] { sky }, _mods);

		Assert.Same(sky, replaces);
		Assert.Equal(sky.FolderPath, destination);
		Assert.DoesNotContain("2026", destination);
	}

	[Fact]
	public void ADeletedModComesBackInItsOwnFolderNotOneNamedAfterThePage()
	{
		// Deleting names the backup after the shown name. On The Witcher 3 a folder not called mod… does not load.
		Manifest(".manager_manifest.json", "modCool");

		(string destination, GameMod? replaces) = BackupRestore.Plan(
			_extracted, "Cool Display Name_Delete_20260101_120000.zip", Array.Empty<GameMod>(), _mods);

		Assert.Null(replaces);
		Assert.Equal(Path.Combine(_mods, "modCool"), destination);
	}

	[Fact]
	public void ADeletedStardewModComesBackUnderItsName()
	{
		Manifest("manifest.json", "a.cool");

		(string destination, _) = BackupRestore.Plan(_extracted, "CoolMod_Delete_20260101_120000.zip", Array.Empty<GameMod>(), _mods);

		Assert.Equal(Path.Combine(_mods, "CoolMod"), destination);
	}

	[Fact]
	public void ABepInExGuidIsNotUsedAsAFolderName()
	{
		Manifest(".manager_manifest.json", "com.author.plugin");

		(string destination, _) = BackupRestore.Plan(_extracted, "CoolPlugin_20260101_120000.zip", Array.Empty<GameMod>(), _mods,
			useRecordedFolderName: false);

		Assert.Equal(Path.Combine(_mods, "CoolPlugin"), destination);
	}

	[Fact]
	public void TheFolderNameMatchesWhenThereIsNoManifest()
	{
		GameMod plugin = Mod("CoolPlugin", "com.author.plugin", "Cool Plugin");

		Assert.Same(plugin, BackupRestore.Plan(_extracted, "CoolPlugin_20260101_120000.zip", new[] { plugin }, _mods).Replaces);
	}

	[Fact]
	public void ADamagedManifestDoesNotStopTheRestore()
	{
		File.WriteAllText(Path.Combine(_extracted, "manifest.json"), "{ not json");

		(string destination, _) = BackupRestore.Plan(_extracted, "CoolMod_20260101_120000.zip", Array.Empty<GameMod>(), _mods);

		Assert.Equal(Path.Combine(_mods, "CoolMod"), destination);
	}

	[Fact]
	public void AGroupRowIsNotTheModBeingRestored()
	{
		var group = new GameMod { Name = "CoolMod", UniqueId = "GROUP:CoolMod", IsGroup = true, FolderPath = _mods };

		(string destination, GameMod? replaces) = BackupRestore.Plan(_extracted, "CoolMod_20260101_120000.zip", new[] { group }, _mods);

		Assert.Null(replaces);
		Assert.NotEqual(_mods, destination);
	}

	[Theory]
	[InlineData("SMAPI_Console_Commands_20260101_120000.zip", "SMAPI_Console_Commands")]
	[InlineData("Foo_Delete_20260101_120000.zip", "Foo")]
	[InlineData("Delete_Me_20260101_120000.zip", "Delete_Me")]
	public void TheModsNameIsReadFromTheBackupsFileName(string file, string expected)
	{
		Assert.Equal(expected, BackupRestore.ModNameOf(file));
	}
}
