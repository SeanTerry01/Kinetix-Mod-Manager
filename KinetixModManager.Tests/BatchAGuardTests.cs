using System.Text.RegularExpressions;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>Removing a mod, in both shapes it comes in. See <see cref="ModOnDisk"/>.</summary>
public class ModOnDiskTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kmm-modondisk-" + Guid.NewGuid().ToString("N"));

	public ModOnDiskTests() => Directory.CreateDirectory(_dir);

	public void Dispose()
	{
		try { ModOnDisk.DeleteDirectory(_dir); } catch (IOException) { }
	}

	[Fact]
	public void AMinecraftJarIsReallyDeleted()
	{
		// A mod here is one file. This is the case that used to report success and leave the mod in place.
		string jar = Path.Combine(_dir, "sodium.jar");
		File.WriteAllText(jar, "x");

		ModOnDisk.Delete(jar);

		Assert.False(File.Exists(jar));
	}

	[Fact]
	public void ASwitchedOffJarIsReallyDeleted()
	{
		string jar = Path.Combine(_dir, "sodium.jar.disabled");
		File.WriteAllText(jar, "x");

		ModOnDisk.Delete(jar);

		Assert.False(File.Exists(jar));
	}

	[Fact]
	public void AModFolderIsDeletedWithEverythingInIt()
	{
		string folder = Path.Combine(_dir, "SomeMod");
		Directory.CreateDirectory(Path.Combine(folder, "assets"));
		File.WriteAllText(Path.Combine(folder, "assets", "a.png"), "x");

		ModOnDisk.Delete(folder);

		Assert.False(Directory.Exists(folder));
	}

	[Fact]
	public void AReadOnlyFileInsideAModDoesNotStopTheDelete()
	{
		string folder = Path.Combine(_dir, "SkyPatcher");
		Directory.CreateDirectory(folder);
		string dll = Path.Combine(folder, "SkyPatcher.dll");
		File.WriteAllText(dll, "x");
		File.SetAttributes(dll, FileAttributes.ReadOnly);

		ModOnDisk.Delete(folder);

		Assert.False(Directory.Exists(folder));
	}

	[Fact]
	public void AReadOnlyJarDoesNotStopTheDelete()
	{
		string jar = Path.Combine(_dir, "locked-flag.jar");
		File.WriteAllText(jar, "x");
		File.SetAttributes(jar, FileAttributes.ReadOnly);

		ModOnDisk.Delete(jar);

		Assert.False(File.Exists(jar));
	}

	[Fact]
	public void SomethingAlreadyGoneIsNotAnError()
	{
		ModOnDisk.Delete(Path.Combine(_dir, "never-existed"));
	}

	[Fact]
	public void AJarInUseThrowsRatherThanReportingSuccess()
	{
		// The game running holds its mods open. Windows refuses the delete, and the user has to be told.
		if (!OperatingSystem.IsWindows()) return;

		string jar = Path.Combine(_dir, "in-use.jar");
		File.WriteAllText(jar, "x");
		using var held = new FileStream(jar, FileMode.Open, FileAccess.Read, FileShare.None);

		Assert.ThrowsAny<IOException>(() => ModOnDisk.Delete(jar));
		Assert.True(File.Exists(jar));
	}
}

/// <summary>
/// The fixes below live in Form1, which the tests cannot load, so these read its source. Each one guards a specific
/// way the manager once left someone stuck or lost their mods.
/// </summary>
public class BatchAGuardTests
{
	private static string RepositoryRoot()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx")))
			dir = dir.Parent;
		return dir?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
	}

	private static string App(string file) => File.ReadAllText(Path.Combine(RepositoryRoot(), "KinetixModManager", file));

	/// <summary>The text of a method: from its signature to the brace that closes it.</summary>
	private static string MethodBody(string file, string signature)
	{
		string source = App(file);
		int at = source.IndexOf(signature, StringComparison.Ordinal);
		Assert.True(at >= 0, $"{signature} was not found in {file}.");

		int open = source.IndexOf('{', at);
		int depth = 0;
		for (int i = open; i < source.Length; i++)
		{
			if (source[i] == '{') depth++;
			else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
		}
		throw new InvalidOperationException($"{signature} never closes.");
	}

	private static string WithoutComments(string code) => string.Join("\n", code.Split('\n').Select(l => l.Split("//")[0]));

	[Theory]
	[InlineData("Form1.ModActions.cs", "private async void ToggleModStatus()")]
	[InlineData("Form1.ModActions.cs", "private void SetManualCategory()")]
	[InlineData("Form1.ModList.cs", "private async Task LinkModUpdateSource(")]
	public void AnActionOnASelectedModRefusesAGroupRow(string file, string signature)
	{
		// A group row is a heading dressed as a mod. Its folder is the whole mods folder, so switching one "off"
		// renamed that folder and every mod in the game vanished from the list, with no way back from inside the app.
		Assert.Contains("IsGroup", WithoutComments(MethodBody(file, signature)));
	}

	[Fact]
	public void DeletingAModUsesTheDeleteThatChecksItWorked()
	{
		Assert.Contains("DeleteModFolder(string path) => ModOnDisk.Delete(path);", App("ModFileSystem.cs"));
	}

	[Fact]
	public void AnOverlayOpeningDuringASpeechWaitGetsTheKeyboard()
	{
		// LetSpeechFinish swallows keys while it holds. A prompt can open inside its pump, and with the hold still on,
		// no key could answer the prompt and no key could end the wait.
		string body = WithoutComments(MethodBody("Form1.InlinePrompt.cs", "private void RunOverlay("));
		Assert.Contains("_holdingForSpeech = false", body);
		Assert.Contains("_holdingForSpeech = holdingBefore", body);
	}

	[Fact]
	public void EscapeOnAnOpenDropdownOnlyClosesTheDropdown()
	{
		string body = WithoutComments(MethodBody("Form1.InlinePrompt.cs", "private static void AttachEscape("));
		Assert.Contains("DroppedDown", body);
	}

	[Fact]
	public void EveryFomodStepAnswersEscape()
	{
		// Each step is built after the overlay wired Escape, so a step has to wire its own or the key is dead on it.
		string source = WithoutComments(App("Form1.FomodWizard.cs"));
		Assert.Matches(new Regex(@"AttachEscape\(content,"), source);
		Assert.Matches(new Regex(@"StylePromptPanel\(content\)"), source);
		Assert.Matches(new Regex(@"SilenceUnnamedContainers\(content\)"), source);
	}

	[Fact]
	public void TheInterfaceIsBuiltExactlyOnce()
	{
		// It adds a second tab control and menu beside the first rather than replacing them. Calling it to refresh
		// the shortcut labels left focus on the old lists while every field pointed at new, empty ones.
		string app = Path.Combine(RepositoryRoot(), "KinetixModManager");
		int calls = TestPaths.SourceFiles(app, "Form1*.cs")
			.Sum(f => Regex.Matches(WithoutComments(File.ReadAllText(f)), @"(?<!void )\bSetupAccessibleUI\(\)").Count);
		Assert.Equal(1, calls);
	}

	[Fact]
	public void EveryDefaultShortcutActionIsActuallyListenedFor()
	{
		// A shortcut in the table with nothing checking it is silence when pressed, and it can be remapped to no effect.
		string app = Path.Combine(RepositoryRoot(), "KinetixModManager");
		string source = string.Concat(TestPaths.SourceFiles(app, "Form1*.cs").Select(f => WithoutComments(File.ReadAllText(f))));

		var settings = new AppSettings();
		settings.InitializeDefaults();
		Assert.NotEmpty(settings.Shortcuts);

		List<string> unheard = settings.Shortcuts.Keys
			.Where(action => !Regex.IsMatch(source, @"IsShortcut\(\w+, """ + action + @"""\)"))
			.ToList();

		Assert.True(unheard.Count == 0, "Nothing listens for these shortcuts:\n  " + string.Join("\n  ", unheard));
	}

	[Fact]
	public void UploadingTheLogIsReachedFromTheFormsKeyHandler()
	{
		// Key preview runs the form's handler first and marks the key handled, so a list's own check for the same key
		// never runs. That is how Ctrl+L on the log asked for an API key instead of uploading.
		string login = MethodBody("Form1.Input.cs", "private void Form1_KeyDown(")
			.Split("IsShortcut(e, \"Login\")")[1].Split("IsShortcut(e, \"InstallZip\")")[0];
		Assert.Contains("UploadSmapiLog()", login);
	}

	[Fact]
	public void APromptsQuestionCanBeReachedAndReadAgain()
	{
		// A label cannot take focus, so a question cut off by anything could never be heard again.
		string body = WithoutComments(MethodBody("Form1.InlinePrompt.cs", "private Panel BuildPromptPanel("));
		Assert.Matches(new Regex(@"new TextBox\s*\{\s*Text = text"), body);
		Assert.Contains("ReadOnly = true", body);
	}

	[Fact]
	public void AHeldKeyIsNotSeveralPressesOnAList()
	{
		string body = WithoutComments(MethodBody("Form1.Input.cs", "private void List_KeyDown("));
		Assert.Contains("_lastKeyWasAutoRepeat", body);
	}

	[Theory]
	[InlineData("private void SetManualCategory()")]
	[InlineData("private void SetModNote()")]
	[InlineData("private void OpenSelectedModManifest()")]
	[InlineData("private void OpenSelectedModConfigFile()")]
	[InlineData("private void OpenSelectedModConfig()")]
	public void APerModCommandActsOnTheTabInFront(string signature)
	{
		string body = WithoutComments(MethodBody("Form1.ModActions.cs", signature));
		Assert.Contains("ModOnThisTab()", body);
		Assert.DoesNotContain("listInstalled.SelectedItem", body);
	}

	[Fact]
	public void AFocusedButtonDoesNotDisableItself()
	{
		// That throws focus to whatever comes next, so a second Enter lands somewhere else.
		Assert.DoesNotContain("btnAiRefresh.Enabled = false", App("Form1.Settings.cs"));
		Assert.DoesNotContain("btnAiTest.Enabled = false", App("Form1.Settings.cs"));
		Assert.DoesNotContain("send.Enabled = input.Enabled = false", App("Form1.AiDiagnosis.cs"));
	}

	[Fact]
	public void AManagerUpdateSaysItIsTheManager()
	{
		// A prompt's title is not spoken, so the words have to carry it. At startup a bare "Version 1.7.0 is available"
		// could be a mod or the game.
		string en = App(Path.Combine("lang", "en.json"));
		foreach (string key in new[] { "updates.versionAvailDownload", "updates.versionAvailNoInstaller" })
		{
			string line = en.Split('\n').Single(l => l.Contains($"\"{key}\""));
			Assert.Contains("Kinetix Mod Manager", line);
		}
	}
}
