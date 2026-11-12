using System.IO.Compression;
using System.Reflection;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// The 7z and RAR path read every archive forwards through one reader, and SharpCompress refuses that for an archive that is
/// neither solid nor 7z: "ExtractAllEntries can only be used on solid archives or 7Zip archives". An ordinary RAR is exactly
/// that, and installing one failed (a real Carry Weight Modifiers .rar, 2026-11-11).
///
/// <para>
/// There is no tool here to make a RAR, so a zip stands in for it: read through the same path, a zip is the same case, not solid
/// and not 7z, and it failed with the same message before the fix. The solid 7z fixture covers the other branch.
/// </para>
/// </summary>
public sealed class ArchiveNotSolidTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kinetix-notsolid-" + Guid.NewGuid().ToString("N"));

	public ArchiveNotSolidTests() => Directory.CreateDirectory(_dir);

	public void Dispose()
	{
		try { Directory.Delete(_dir, true); } catch (IOException) { }
	}

	private static void ThroughTheSharpCompressPath(string archive, string output, IProgress<double>? progress = null)
	{
		MethodInfo extract = typeof(ModArchive).GetMethod("ExtractWithSharpCompress", BindingFlags.NonPublic | BindingFlags.Static)
			?? throw new InvalidOperationException("ExtractWithSharpCompress has been renamed; point this test at its replacement.");
		try { extract.Invoke(null, new object?[] { archive, output, progress }); }
		catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
	}

	private string NotSolidArchive()
	{
		string source = Path.Combine(_dir, "source");
		Directory.CreateDirectory(Path.Combine(source, "SKSE", "Plugins"));
		File.WriteAllText(Path.Combine(source, "Carry Weight x100.esp"), "plugin");
		File.WriteAllText(Path.Combine(source, "SKSE", "Plugins", "thing.ini"), "[x]");
		string archive = Path.Combine(_dir, "mod.zip");
		ZipFile.CreateFromDirectory(source, archive);
		return archive;
	}

	[Fact]
	public void AnArchiveThatIsNotSolidExtracts()
	{
		string output = Path.Combine(_dir, "out");
		Directory.CreateDirectory(output);

		ThroughTheSharpCompressPath(NotSolidArchive(), output);

		Assert.Equal("plugin", File.ReadAllText(Path.Combine(output, "Carry Weight x100.esp")));
		Assert.Equal("[x]", File.ReadAllText(Path.Combine(output, "SKSE", "Plugins", "thing.ini")));
	}

	[Fact]
	public void ItsProgressStillReachesAHundred()
	{
		string output = Path.Combine(_dir, "out");
		Directory.CreateDirectory(output);
		var reported = new List<double>();

		ThroughTheSharpCompressPath(NotSolidArchive(), output, new Progress(reported.Add));

		Assert.NotEmpty(reported);
		Assert.Equal(100.0, reported[^1]);
	}

	/// <summary>Synchronous; the built-in Progress posts to a context tests don't have.</summary>
	private sealed class Progress(Action<double> report) : IProgress<double>
	{
		public void Report(double value) => report(value);
	}
}
