using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// The rescan moved off the interface thread, so things that could not interleave now can. The app cannot be loaded
/// here, so these pin the order of what it does, and each one names what went wrong without it.
/// </summary>
public sealed class RefreshOffThreadTests
{
	private static string App(string file)
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		return File.ReadAllText(Path.Combine(dir!.FullName, "KinetixModManager", file));
	}

	private static string Body(string file, string signature, int length)
	{
		string source = App(file);
		int at = source.IndexOf(signature, StringComparison.Ordinal);
		Assert.True(at >= 0, signature);
		return source.Substring(at, Math.Min(length, source.Length - at));
	}

	private static void InOrder(string text, params string[] parts)
	{
		int from = 0;
		foreach (string part in parts)
		{
			int at = text.IndexOf(part, from, StringComparison.Ordinal);
			Assert.True(at >= 0, $"Expected \"{part}\" after what came before it.");
			from = at + part.Length;
		}
	}

	[Fact]
	public void AGameSwitchTakesTheOldGamesRowsAwayBeforeTheRescan()
	{
		// Left up, Space on one moved another game's mod using the new game's rules.
		InOrder(Body("Form1.GameSession.cs", "private void SwitchActiveGame(", 12000),
			"_settings.ActiveGame = game;", "ForgetPreviousGamesMods();", "RefreshAllData(");
	}

	[Fact]
	public void TrustGoesBeforeTheListDoes()
	{
		// A deployment pass reads the list and then asks. The other order lets an empty list look like a true one, and to a
		// deployment an empty list means every file should go.
		InOrder(Body("Form1.GameSession.cs", "private void ForgetPreviousGamesMods()", 400),
			"_scannedGame = null;", "_allInstalledMods = new", "listInstalled.Items.Clear();", "listUpdates.Items.Clear();");
	}

	[Theory]
	[InlineData("private void EnsureModPriorityList()")]
	[InlineData("private void SyncBethesdaPluginsHoldingLock()")]
	public void WhatActsOnTheListReadsItBeforeAskingWhetherItCanBeTrusted(string signature)
	{
		InOrder(Body("Form1.LoadOrder.cs", signature, 900), "= _allInstalledMods;", "!ModListIsTrustworthy");
	}

	[Fact]
	public void DeploymentAsksAgainAfterWorkingOutWhatIsEnabled()
	{
		InOrder(Body("Form1.LoadOrder.cs", "private List<FileConflict> SyncBethesdaDeploymentHoldingLock(", 900),
			"!ModListIsTrustworthy", "GetOrderedEnabledMods();", "!ModListIsTrustworthy", "ModFileSystem.SyncDeployment(");
	}

	[Fact]
	public void ARescanOfTheSameGameDoesNotWithdrawTrust()
	{
		// It did, for the length of the scan. An install's forced relink and Rebuild Mod Deployment were refused meanwhile,
		// and both then reported success.
		string refresh = Body("Form1.ModList.cs", "private async Task RefreshModList(", 8000);
		string beforeTheCheck = refresh.Substring(0, refresh.IndexOf("if (!Directory.Exists(modsPath))", StringComparison.Ordinal));
		Assert.DoesNotContain("_scannedGame = null", beforeTheCheck);
	}

	[Fact]
	public void AFailedScanWithdrawsTrustAndAnOvertakenOneDoesNot()
	{
		InOrder(Body("Form1.ModList.cs", "private async Task RefreshModList(", 12000),
			"catch (Exception ex)", "if (Superseded()) return;", "_scannedGame = null;", "_allInstalledMods = new List<GameMod>();");
	}

	[Fact]
	public void APassOvertakenBeforeItsScanGoesNoFurther()
	{
		// A slow Minecraft sign-in carried on after a newer pass had finished.
		InOrder(Body("Form1.ModList.cs", "private async Task RefreshModList(", 12000),
			"await ConnectMinecraftAccountAsync();", "if (Superseded()) return;", "string modsPath = _settings.CurrentModsPath;");
	}

	[Fact]
	public void TheGameDoesNotStartWhileItsFolderIsBeingChanged()
	{
		InOrder(Body("Form1.GameSelection.cs", "private void LaunchGame()", 1500),
			"_refreshInFlight", "LaunchOnceSettledAsync", "ConfirmEnoughMemoryToLaunch");
	}

	[Fact]
	public void AWaitingLaunchStartsOnce()
	{
		Assert.Contains("if (!_launchWaiting) Fire(LaunchOnceSettledAsync", App("Form1.GameSelection.cs"));
	}

	[Fact]
	public void AnInstallReadsAListThatIsCurrent()
	{
		// Reading it mid-rescan after a game switch saw nothing installed, so nothing was backed up.
		InOrder(Body("Form1.Install.cs", "private async Task<bool> InstallFromZip(", 3000),
			"_refreshInFlight", "List<GameMod> installed = _allInstalledMods.ToList();");
	}

	[Fact]
	public void SettingsStayingOpenIsNotARefreshStillRunning()
	{
		// Launching, installing and leaving all wait on this count, and Settings stays open as long as the player likes.
		InOrder(Body("Form1.ModList.cs", "private async Task RefreshModList(", 16000),
			"NoLongerRefreshing();", "ShowSettings();");
	}

	[Fact]
	public void AnUpdateCheckIsNotLostWhenAPassIsOvertaken()
	{
		string source = App("Form1.ModList.cs");
		InOrder(source.Substring(source.IndexOf("NoLongerRefreshing();\n\t\t\t// The question", StringComparison.Ordinal)),
			"_updateCheckRunning, 0", "bool overtaken", "_checkAgainWhenFree", "RefreshModList(checkUpdates: true)");
	}

	[Fact]
	public void ANexusFailureIsSaidOnceNotOnEveryRescan()
	{
		InOrder(Body("Form1.ModList.cs", "private async Task RefreshModList(", 12000),
			"_nexusFailedForKey == _settings.ApiKey", "speak: false", "CheckConnectionAsync()", "_nexusFailedForKey = _settings.ApiKey");
	}

	[Fact]
	public void TheBepInExQuestionWaitsForTheList()
	{
		InOrder(Body("Form1.BepInEx.cs", "private async Task CheckBepInExForSessionAsync()", 500),
			"_refreshInFlight", "_bepInExCheckedThisSession");
	}

	[Fact]
	public void LeavingWaitsForADeploymentPass()
	{
		// Ended partway, files are linked that the manifest has not recorded, and one is later taken for the game's own.
		InOrder(Body("Form1.cs", "private async Task RunShutdownSequenceAsync()", 2500), "Hide();", "_refreshInFlight", "disconnect");
	}

	[Fact]
	public void AnEditorStaysOpenUntilItsRescanHasFinished()
	{
		// Ending after the arrival, it moved the row and said a second position over the first.
		InOrder(Body("Form1.Accessibility.cs", "private void OpenConfigEditor(", 9000),
			"onSaveSuccess?.Invoke();", "LetRefreshFinish();", "closeView();");
	}

	[Theory]
	[InlineData("Form1.ModList.cs", "private void RefreshBackupsList()")]
	[InlineData("Form1.Profiles.cs", "listProfiles.BeginUpdate();")]
	public void AListWhoseRowHasGoneStaysNearWhereItWas(string file, string start)
	{
		// Starting from 0 made "not found" look found, and deleting backup 20 of 40 landed on row 1.
		InOrder(Body(file, start, 1200), "int newIndex = -1;", "newIndex >= 0 ? newIndex : Math.Min(");
	}

	[Fact]
	public void AKeyLetGoOnAPromptCounts()
	{
		// A prompt turns the window's key preview off, so its KeyUp never came, and the next press looked like a repeat.
		InOrder(Body("Form1.cs", "public bool PreFilterMessage(", 1500), "WM_KEYUP or WM_SYSKEYUP", "_keyRepeat.Released();", "_holdingForSpeech");
	}
}

public sealed class UpdateTargetTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kmm-update-" + Guid.NewGuid().ToString("N"));

	public UpdateTargetTests() => Directory.CreateDirectory(_dir);

	public void Dispose()
	{
		try { ModOnDisk.DeleteDirectory(_dir); } catch (IOException) { }
	}

	private GameMod Mod(string folder, string id, bool makeFolder = true)
	{
		string path = Path.Combine(_dir, folder);
		if (makeFolder) Directory.CreateDirectory(path);
		return new GameMod { Name = id, UniqueId = id, FolderPath = path };
	}

	[Fact]
	public void ARowStillRightIsUsed()
	{
		GameMod row = Mod("Foo", "foo");

		Assert.Same(row, InstallTarget.ForUpdate(row, Array.Empty<GameMod>()));
	}

	[Fact]
	public void ARowWhoseFolderHasMovedFindsTheModWhereItIsNow()
	{
		// Switched off after the check ran. Trusting the row left .Foo behind, so the mod was then installed twice.
		GameMod row = Mod("Foo", "foo", makeFolder: false);
		GameMod switchedOff = Mod(".Foo", "foo");

		Assert.Same(switchedOff, InstallTarget.ForUpdate(row, new[] { switchedOff }));
	}

	[Fact]
	public void WithNoRowThereIsNothingToSay()
	{
		Assert.Null(InstallTarget.ForUpdate(null, new[] { Mod("Foo", "foo") }));
	}

	[Fact]
	public void AModThatHasGoneIsNotInventedAndAGroupIsNotIt()
	{
		GameMod row = Mod("Foo", "foo", makeFolder: false);
		var group = new GameMod { Name = "foo", UniqueId = "foo", IsGroup = true, FolderPath = _dir };

		Assert.Null(InstallTarget.ForUpdate(row, new[] { group, Mod("Bar", "bar") }));
	}
}

public sealed class FolderMoveTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kmm-move-" + Guid.NewGuid().ToString("N"));

	public FolderMoveTests() => Directory.CreateDirectory(_dir);

	public void Dispose()
	{
		try { ModOnDisk.DeleteDirectory(_dir); } catch (IOException) { }
	}

	[Fact]
	public void ALockThatPassesDoesNotFailTheMove()
	{
		if (!OperatingSystem.IsWindows()) return;

		string from = Path.Combine(_dir, "from");
		string to = Path.Combine(_dir, "to");
		Directory.CreateDirectory(from);
		string file = Path.Combine(from, "a.txt");
		File.WriteAllText(file, "x");

		// As an antivirus does with a folder just written: holds it a moment, then lets go.
		var held = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
		int retries = 0;

		ModOnDisk.MoveDirectory(from, to, () =>
		{
			retries++;
			held.Dispose();
		});

		Assert.Equal(1, retries);
		Assert.True(File.Exists(Path.Combine(to, "a.txt")));
	}

	[Fact]
	public void ALockThatStaysIsStillAFailure()
	{
		if (!OperatingSystem.IsWindows()) return;

		string from = Path.Combine(_dir, "from");
		Directory.CreateDirectory(from);
		string file = Path.Combine(from, "a.txt");
		File.WriteAllText(file, "x");

		using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
		{
			Assert.ThrowsAny<Exception>(() => ModOnDisk.MoveDirectory(from, Path.Combine(_dir, "to")));
		}

		Assert.True(File.Exists(file));
	}

	[Fact]
	public void AFolderThatIsNotThereFailsWithoutWaiting()
	{
		long started = Environment.TickCount64;

		Assert.ThrowsAny<IOException>(() => ModOnDisk.MoveDirectory(Path.Combine(_dir, "never"), Path.Combine(_dir, "to")));

		Assert.True(Environment.TickCount64 - started < 150);
	}
}
