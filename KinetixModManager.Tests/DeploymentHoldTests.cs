using Xunit;

namespace KinetixModManager.Tests;

/// <summary>What a purge and a changed game folder mean for later deployment. The app can't be loaded here, so its side is read.</summary>
public sealed class DeploymentHoldTests
{
	private static string App(string file)
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		return File.ReadAllText(Path.Combine(dir!.FullName, "KinetixModManager", file));
	}

	private static string Between(string source, string from, string to)
	{
		int start = source.IndexOf(from, StringComparison.Ordinal);
		Assert.True(start >= 0, from);
		int end = source.IndexOf(to, start, StringComparison.Ordinal);
		Assert.True(end > start, to);
		return source[start..end];
	}

	[Fact]
	public void AHeldGameIsNotDeployedTo()
	{
		string pass = Between(App("Form1.LoadOrder.cs"), "private List<FileConflict> SyncBethesdaDeploymentHoldingLock", "ModFileSystem.SyncDeployment(");
		Assert.Contains("DeploymentHeldOff.Contains(game)", pass);
	}

	[Theory]
	[InlineData("Form1.LoadOrder.cs", "private void PurgeDeployment()")]
	[InlineData("Form1.GameUpdatePrep.cs", "Loc.T(\"prep.working\")")]
	public void TheHoldStartsBeforeAnyFileGoes(string file, string start)
	{
		string body = Between(App(file), start, "manifest.Save(game)");
		int held = body.IndexOf("DeploymentHeldOff.Add(game)", StringComparison.Ordinal);
		int purge = body.IndexOf("ModFileSystem.PurgeDeployment(", StringComparison.Ordinal);
		Assert.True(held >= 0 && held < purge, "A refresh waiting for the lock would put everything back.");
	}

	[Theory]
	[InlineData("Form1.LoadOrder.cs", "private void RebuildDeployment()", "SyncBethesdaDeployment(forceAll)")]
	[InlineData("Form1.GameUpdatePrep.cs", "private void RestoreAfterUpdate()", "SyncBethesdaDeployment(forceAll)")]
	public void AskingForModsBackEndsTheHoldFirst(string file, string start, string sync)
	{
		Assert.Contains("DeploymentHeldOff.Remove(", Between(App(file), start, sync));
	}

	[Fact]
	public void TheFolderAndItsRecordChangeTogether()
	{
		string save = App("Form1.Settings.cs");
		int asked = save.IndexOf("AskAboutDeploymentsLeftBehind(", StringComparison.Ordinal);
		int locked = save.IndexOf("lock (_deploymentLock)", asked, StringComparison.Ordinal);
		int assigned = save.IndexOf("_settings.GamePaths = tempGamePaths", asked, StringComparison.Ordinal);
		int settled = save.IndexOf("SettleDeploymentsLeftBehind(", asked, StringComparison.Ordinal);

		Assert.True(asked >= 0 && asked < locked, "A prompt under the lock could re-enter it.");
		Assert.True(locked < assigned && assigned < settled);
	}

	[Theory]
	[InlineData(@"C:\Games\Skyrim", @"D:\Games\Skyrim", true, true)]
	[InlineData(@"C:\Games\Skyrim", @"D:\Games\Skyrim", false, false)] // Moved, and its files with it.
	[InlineData(@"C:\Games\Skyrim", @"C:\Games\Skyrim", true, false)]
	[InlineData(@"C:\Games\Skyrim", @"C:\GAMES\skyrim\", true, false)] // The same folder, spelled differently.
	[InlineData("", @"D:\Games\Skyrim", true, false)]
	[InlineData(@"C:\Games\Skyrim", "", true, false)]                  // Cleared rather than moved.
	public void FilesAreLeftBehindOnlyByARealChange(string before, string after, bool oldExists, bool expected)
	{
		Assert.Equal(expected, GameFolderChange.LeavesFilesBehind(before, after, oldExists));
	}

	[Fact]
	public void MoTwoComparesTheGameNotTheCopy()
	{
		// A second copy's key is not the game's id, so comparing them refused to import into it.
		string import = App("Form1.ImportMO2.cs");
		Assert.Contains("GameProfiles.IsGame(game, iniGame)", import);
	}

	[Fact]
	public void TheLoadingPulseIsNotOneFlagAgain()
	{
		foreach (string file in Directory.GetFiles(Path.GetDirectoryName(ModsFolder())!, "Form1*.cs"))
			Assert.DoesNotContain("_isLoading", File.ReadAllText(file));
	}

	private static string ModsFolder()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx"))) dir = dir.Parent;
		return Path.Combine(dir!.FullName, "KinetixModManager", "Form1.cs");
	}
}
