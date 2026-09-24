using System.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Turning a crashed game's error output into the one line worth reading aloud — <see cref="MinecraftErrorOutput"/>.
/// </summary>
public class MinecraftErrorOutputTests
{
	/// <summary>Verbatim from the accessibility modpack's first launch, 2026-09-23 (paths shortened).</summary>
	private static readonly string[] DuplicateAsm =
	{
		"Exception in thread \"main\" java.lang.ExceptionInInitializerError",
		"\tat net.fabricmc.loader.impl.launch.knot.KnotClient.main(KnotClient.java:23)",
		"Caused by: java.lang.IllegalStateException: duplicate ASM classes found on classpath: jar:file:/C:/.minecraft/libraries/org/ow2/asm/asm/9.9/asm-9.9.jar!/org/objectweb/asm/ClassReader.class, jar:file:/C:/.minecraft/libraries/org/ow2/asm/asm/9.6/asm-9.6.jar!/org/objectweb/asm/ClassReader.class",
		"\tat net.fabricmc.loader.impl.util.LoaderUtil.verifyClasspath(LoaderUtil.java:83)",
		"\tat net.fabricmc.loader.impl.launch.knot.Knot.<clinit>(Knot.java:330)",
		"\t... 1 more"
	};

	[Fact]
	public void TheRootCauseIsReadRatherThanTheWrapperAroundIt()
	{
		string summary = MinecraftErrorOutput.Summarise(DuplicateAsm);

		Assert.StartsWith("java.lang.IllegalStateException: duplicate ASM classes found on classpath", summary);
		Assert.DoesNotContain("ExceptionInInitializerError", summary);
		Assert.DoesNotContain("Caused by", summary);
	}

	[Fact]
	public void TheDeepestCauseWinsWhenThereAreSeveral()
	{
		string summary = MinecraftErrorOutput.Summarise(new[]
		{
			"Exception in thread \"main\" java.lang.RuntimeException: outer",
			"Caused by: java.lang.IllegalStateException: middle",
			"Caused by: java.io.FileNotFoundException: the real reason"
		});

		Assert.Equal("java.io.FileNotFoundException: the real reason", summary);
	}

	[Fact]
	public void WithoutACauseTheFirstExceptionIsUsedWithoutItsFraming() =>
		Assert.Equal("java.lang.OutOfMemoryError: Java heap space",
			MinecraftErrorOutput.Summarise(new[] { "Exception in thread \"main\" java.lang.OutOfMemoryError: Java heap space" }));

	[Fact]
	public void StackFramesAreNeverTheSummary() =>
		Assert.Equal("", MinecraftErrorOutput.Summarise(new[] { "\tat some.ErrorHandler.run(ErrorHandler.java:1)" }));

	[Fact]
	public void NothingUsefulSaysNothing() =>
		Assert.Equal("", MinecraftErrorOutput.Summarise(new[] { "", "hello" }));

	[Fact]
	public void AVeryLongLineIsCutToSomethingThatCanBeListenedTo()
	{
		string summary = MinecraftErrorOutput.Summarise(new[] { "Caused by: java.lang.Error: " + new string('x', 2000) });

		Assert.True(summary.Length <= MinecraftErrorOutput.SummaryLength + 1);
		Assert.EndsWith("…", summary);
	}

	[Theory]
	[InlineData("[05:11:57] [Render thread/INFO]: Stopping!", true)]     // verbatim, the accessibility pack's Quit Game
	[InlineData("[05:11:57] [Render thread/INFO]: Stopping!\r", true)]   // the log's own line ending left on
	[InlineData("[05:11:57] [CullThread/INFO]: [STDOUT]: Shutting down culling task!", false)]
	[InlineData("[05:11:12] [Render thread/INFO]: Narrating(interrupt:true)= Quit Game button", false)]
	[InlineData("[12:00:00] [Server thread/INFO]: Stopping server", false)]
	[InlineData(null, false)]
	public void TheGameBeginningToStopIsRecognisedAndNothingElseIs(string? line, bool stopping) =>
		Assert.Equal(stopping, MinecraftErrorOutput.IsShutdownLine(line));

	[Fact]
	public void RunningOutOfMemoryIsRecognisedFromWhatJavaActuallySaid()
	{
		// Verbatim, the accessibility pack's second crash — the one line on the error stream, with no "Error" in it.
		Assert.True(MinecraftErrorOutput.IsOutOfMemory(new[]
		{
			"OpenJDK 64-Bit Server VM warning: INFO: os::commit_memory(0x000000053f800000, 1040187392, 0) failed; error='The paging file is too small for this operation to complete' (DOS error/errno=1455)"
		}));
		// And the opening of the crash file Java wrote beside it.
		Assert.True(MinecraftErrorOutput.IsOutOfMemory(new[] { "# There is insufficient memory for the Java Runtime Environment to continue." }));
		Assert.True(MinecraftErrorOutput.IsOutOfMemory(new[] { "Exception in thread \"Render thread\" java.lang.OutOfMemoryError: Java heap space" }));
		Assert.False(MinecraftErrorOutput.IsOutOfMemory(DuplicateAsm));
	}

	[Fact]
	public void TheSignInTokenIsTakenOutOfACrashFileAndNothingElseIs()
	{
		const string line = "Command Line: -Xss1M --username SeanTerry01 --accessToken eyJraWQiOiIwNDkx.abc-DEF_123 --versionType release";

		string clean = MinecraftErrorOutput.RedactSecrets(line);

		Assert.DoesNotContain("eyJraWQiOiIwNDkx", clean);
		Assert.Contains("--accessToken [removed by Kinetix] --versionType release", clean);
		Assert.Contains("--username SeanTerry01", clean);
		// Running it twice changes nothing more — the file is cleaned after every run.
		Assert.Equal(clean, MinecraftErrorOutput.RedactSecrets(clean));
	}

	[Fact]
	public void OnlyTheLastLinesAreKept()
	{
		var output = new MinecraftErrorOutput();
		for (int i = 0; i < MinecraftErrorOutput.Keep + 50; i++) output.Add("line " + i);
		output.Add(null);   // end of stream

		Assert.Equal(MinecraftErrorOutput.Keep, output.Lines.Count);
		Assert.Equal("line 50", output.Lines.First());
		Assert.Equal("line " + (MinecraftErrorOutput.Keep + 49), output.Lines.Last());
	}
}
