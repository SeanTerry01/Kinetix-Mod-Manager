using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="ModArchive"/> — getting a downloaded mod out of whatever it arrived in.
///
/// <para>
/// Two things here are worth more than the rest. The first is that a mod archive's name lies: Nexus hands
/// back a 7z or a rar called ".zip" often enough that routing on the extension is the single commonest way
/// an install fails, so the format is read from the bytes and the tests say so. The second is that 7z is now
/// read in-process instead of by a downloaded <c>7za.exe</c>, which is what lets the Linux head install
/// anything at all — so one of these tests extracts a real, solid, LZMA2 archive rather than a mock.
/// </para>
/// </summary>
public class ModArchiveTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "kinetix-arc-" + Guid.NewGuid().ToString("N"));

	public ModArchiveTests() => Directory.CreateDirectory(_dir);

	public void Dispose()
	{
		try { Directory.Delete(_dir, true); } catch { }
	}

	private string Path_(params string[] parts) => Path.Combine(new[] { _dir }.Concat(parts).ToArray());

	/// <summary>Writes a zip from a map of entry name to contents. Entry names are used verbatim.</summary>
	private string Zip(string name, params (string Entry, string Text)[] entries)
	{
		string path = Path_(name);
		using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
		foreach (var (entry, text) in entries)
		{
			using var writer = new StreamWriter(zip.CreateEntry(entry).Open(), Encoding.UTF8);
			writer.Write(text);
		}
		return path;
	}

	/// <summary>The real 7z shipped beside the tests — see the fixture's note in the test csproj.</summary>
	private static string SevenZipFixture =>
		Path.Combine(AppContext.BaseDirectory, "fixtures", "example-mod.7z");

	// ---------------------------------------------------------------------
	// Identifying what arrived
	// ---------------------------------------------------------------------

	[Fact]
	public void AZipIsRecognisedByItsSignature()
	{
		string zip = Zip("mod.zip", ("readme.txt", "hello"));
		Assert.Equal(ArchiveFormat.Zip, ModArchive.DetectFormat(zip));
	}

	[Fact]
	public void A7zIsRecognisedByItsSignature()
	{
		Assert.Equal(ArchiveFormat.SevenZip, ModArchive.DetectFormat(SevenZipFixture));
	}

	[Fact]
	public void A7zNamedZipIsStillA7z()
	{
		// The whole reason the signature is read at all: Nexus's CDN does not always give a file name, and the
		// download then gets called ".zip" whatever it holds. Trusting that name feeds a 7z to ZipFile, which
		// fails with "End of Central Directory record could not be found" — a message that tells the user
		// nothing about the mod they were trying to install.
		string mislabelled = Path_("skyrim-access.zip");
		File.Copy(SevenZipFixture, mislabelled);

		Assert.Equal(ArchiveFormat.SevenZip, ModArchive.DetectFormat(mislabelled));
		Assert.Equal(ArchiveFormat.SevenZip, ModArchive.FormatOf(mislabelled));
	}

	[Fact]
	public void ARarIsRecognisedByItsSignature()
	{
		// RAR cannot be created without WinRAR, so the header is written by hand. Detection is all this
		// proves — extracting one is SharpCompress's business, and unchanged by this work.
		string rar = Path_("mod.rar");
		File.WriteAllBytes(rar, new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 });
		Assert.Equal(ArchiveFormat.Rar, ModArchive.DetectFormat(rar));
	}

	[Fact]
	public void AnUnreadableFileFallsBackToItsExtension()
	{
		string odd = Path_("mod.7z");
		File.WriteAllText(odd, "this is not an archive at all");

		Assert.Equal(ArchiveFormat.Unknown, ModArchive.DetectFormat(odd));
		Assert.Equal(ArchiveFormat.SevenZip, ModArchive.FormatOf(odd));
	}

	[Fact]
	public void SomethingWithNoSignatureAndNoUsefulNameIsTreatedAsAZip()
	{
		string odd = Path_("download.bin");
		File.WriteAllText(odd, "nothing recognisable");
		Assert.Equal(ArchiveFormat.Zip, ModArchive.FormatOf(odd));
	}

	// ---------------------------------------------------------------------
	// Unpacking
	// ---------------------------------------------------------------------

	[Fact]
	public void AZipExtractsWithItsFoldersIntact()
	{
		string zip = Zip("mod.zip",
			("ExampleMod/manifest.json", "{\"UniqueID\":\"Kinetix.ExampleMod\"}"),
			("ExampleMod/ExampleMod.dll", "not really a dll"),
			("readme.txt", "hello"));
		string outDir = Path_("out");

		ModArchive.Extract(zip, outDir);

		Assert.True(File.Exists(Path.Combine(outDir, "ExampleMod", "manifest.json")));
		Assert.True(File.Exists(Path.Combine(outDir, "ExampleMod", "ExampleMod.dll")));
		Assert.Equal("hello", File.ReadAllText(Path.Combine(outDir, "readme.txt")));
	}

	[Fact]
	public void AZipWrittenOnWindowsKeepsItsFolders()
	{
		// A backslash inside an entry name is a folder separator to every tool that made the archive. On Linux
		// it is an ordinary filename character, so reading it literally would produce one file called
		// "ExampleMod\manifest.json" at the top level and a mod whose structure had been flattened — which
		// installs without error and then does nothing.
		string zip = Zip("windows-made.zip", ("ExampleMod\\manifest.json", "{}"));
		string outDir = Path_("out");

		ModArchive.Extract(zip, outDir);

		Assert.True(File.Exists(Path.Combine(outDir, "ExampleMod", "manifest.json")));
	}

	[Fact]
	public void ASolid7zExtractsInProcess()
	{
		// The change this test exists for: 7z used to mean downloading 7za.exe from 7-zip.org and shelling out
		// to it, which is both a Windows-only path and a 2010 binary fetched over the network mid-install.
		string outDir = Path_("out");

		ModArchive.Extract(SevenZipFixture, outDir);

		Assert.True(File.Exists(Path.Combine(outDir, "ExampleMod", "manifest.json")));
		Assert.True(File.Exists(Path.Combine(outDir, "ExampleMod", "ExampleMod.dll")));
		Assert.True(File.Exists(Path.Combine(outDir, "ExampleMod", "assets", "data.txt")));
		Assert.Contains("Kinetix.ExampleMod", File.ReadAllText(Path.Combine(outDir, "ExampleMod", "manifest.json")));
	}

	[Fact]
	public void A7zNamedZipStillExtracts()
	{
		string mislabelled = Path_("skyrim-access.zip");
		File.Copy(SevenZipFixture, mislabelled);
		string outDir = Path_("out");

		ModArchive.Extract(mislabelled, outDir);

		Assert.True(File.Exists(Path.Combine(outDir, "ExampleMod", "manifest.json")));
	}

	[Fact]
	public void ProgressRunsFromSomethingToAHundred()
	{
		// The install screen speaks this number, so it has to end at 100 rather than at "nearly".
		var reported = new List<double>();
		ModArchive.Extract(SevenZipFixture, Path_("out"), new Progress_(reported.Add));

		Assert.NotEmpty(reported);
		Assert.All(reported, p => Assert.InRange(p, 0.0, 100.0));
		Assert.Equal(100.0, reported[^1]);
	}

	/// <summary>Synchronous <see cref="IProgress{T}"/>; the built-in one posts to a context the tests don't have.</summary>
	private sealed class Progress_ : IProgress<double>
	{
		private readonly Action<double> _report;
		public Progress_(Action<double> report) => _report = report;
		public void Report(double value) => _report(value);
	}

	// ---------------------------------------------------------------------
	// Refusing to write outside the folder
	// ---------------------------------------------------------------------

	[Fact]
	public void AnEntryClimbingOutOfTheFolderIsRefusedBeforeItIsWritten()
	{
		string zip = Zip("evil.zip",
			("good.txt", "fine"),
			("../escaped.txt", "not fine"));
		string outDir = Path_("out");

		Assert.Throws<InvalidOperationException>(() => ModArchive.Extract(zip, outDir));
		Assert.False(File.Exists(Path_("escaped.txt")));
	}

	[Fact]
	public void AnAbsoluteEntryNameIsRefusedToo()
	{
		string zip = Zip("evil.zip", ("/etc/kinetix-should-not-exist", "no"));
		Assert.Throws<InvalidOperationException>(() => ModArchive.Extract(zip, Path_("out")));
	}

	[Fact]
	public void AnOrdinaryExtractionPassesTheAfterTheFactCheck()
	{
		string outDir = Path_("out");
		ModArchive.Extract(SevenZipFixture, outDir);
		ModArchive.GuardAgainstEscapedEntries(outDir);   // must not throw
	}

	[Fact]
	public void ALinkPointingOutOfTheFolderIsCaught()
	{
		// What entry names alone cannot catch: nothing about this link's name leaves the folder, but what it
		// leads to does — and what happens next is a deploy into the game folder, so a link named "textures"
		// pointing at the user's home directory is a mod install writing over their own files.
		string outDir = Path_("out");
		Directory.CreateDirectory(outDir);
		string outside = Path_("outside");
		Directory.CreateDirectory(outside);

		if (!TryLink(Path.Combine(outDir, "textures"), outside)) return;

		Assert.Throws<InvalidOperationException>(() => ModArchive.GuardAgainstEscapedEntries(outDir));
	}

	[Fact]
	public void ARelativeLinkClimbingOutIsCaughtToo()
	{
		// The same escape written the way an archive actually stores it. Judged against the link's own folder,
		// which is what the filesystem will do when it is followed.
		string outDir = Path_("out");
		Directory.CreateDirectory(Path.Combine(outDir, "ExampleMod"));
		Directory.CreateDirectory(Path_("outside"));

		if (!TryLink(Path.Combine(outDir, "ExampleMod", "textures"), Path.Combine("..", "..", "outside"))) return;

		Assert.Throws<InvalidOperationException>(() => ModArchive.GuardAgainstEscapedEntries(outDir));
	}

	[Fact]
	public void ALinkThatStaysInsideIsAllowed()
	{
		// Mods do ship these — one shared textures folder referenced from two places. Refusing them would turn
		// the guard into a reason perfectly good mods stop installing.
		string outDir = Path_("out");
		Directory.CreateDirectory(Path.Combine(outDir, "shared"));
		File.WriteAllText(Path.Combine(outDir, "shared", "texture.dds"), "");

		if (!TryLink(Path.Combine(outDir, "alias"), Path.Combine(outDir, "shared"))) return;

		ModArchive.GuardAgainstEscapedEntries(outDir);   // must not throw
	}

	/// <summary>Makes a symlink, or says the filesystem wouldn't — which is a skip, not a failure.</summary>
	private static bool TryLink(string path, string target)
	{
		try { Directory.CreateSymbolicLink(path, target); return true; }
		catch (Exception) { return false; }
	}

	// ---------------------------------------------------------------------
	// Archives inside archives
	// ---------------------------------------------------------------------

	[Fact]
	public void AnArchiveInsideAnArchiveIsUnpackedOneLevel()
	{
		// "Pick the variant you want" downloads, and zips that simply hold the real zip. Without this the
		// manager reports a perfectly good mod as not containing one.
		string inner = Zip("inner.zip", ("ExampleMod/manifest.json", "{\"UniqueID\":\"Kinetix.ExampleMod\"}"));
		string outer = Path_("out");
		Directory.CreateDirectory(outer);
		File.Move(inner, Path.Combine(outer, "inner.zip"));

		ModArchive.ExtractNested(outer);

		Assert.Single(Directory.GetFiles(outer, "manifest.json", SearchOption.AllDirectories));
	}

	[Fact]
	public void ANestedArchiveClimbingOutOfTheFolderIsSkipped()
	{
		// The outer folder is checked as soon as it is extracted, which is before this one exists — so the
		// nested unpack has to do its own checking rather than inherit the earlier answer.
		string inner = Zip("inner.zip", ("../../escaped.txt", "no"));
		string outer = Path_("out");
		Directory.CreateDirectory(outer);
		File.Move(inner, Path.Combine(outer, "inner.zip"));

		ModArchive.ExtractNested(outer);   // best-effort: must not throw

		Assert.False(File.Exists(Path_("escaped.txt")));
		Assert.False(File.Exists(Path.Combine(outer, "escaped.txt")));
	}

	[Fact]
	public void ANestedArchiveThatCannotBeReadDoesNotFailTheInstall()
	{
		string outer = Path_("out");
		Directory.CreateDirectory(outer);
		File.WriteAllText(Path.Combine(outer, "broken.zip"), "not an archive");
		File.WriteAllText(Path.Combine(outer, "manifest.json"), "{}");

		ModArchive.ExtractNested(outer);   // best-effort: must not throw

		Assert.True(File.Exists(Path.Combine(outer, "manifest.json")));
	}

	// ---------------------------------------------------------------------
	// Reading ids without extracting
	// ---------------------------------------------------------------------

	[Fact]
	public void EveryManifestInAMultiModDownloadIsRead()
	{
		string zip = Zip("pack.zip",
			("A/manifest.json", "{\"UniqueID\":\"Kinetix.A\"}"),
			("B/manifest.json", "{\"UniqueID\":\"Kinetix.B\"}"),
			("readme.txt", "hello"));

		Assert.Equal(new[] { "Kinetix.A", "Kinetix.B" }, ModArchive.ReadModIds(zip).OrderBy(s => s));
	}

	[Fact]
	public void ReadingIdsOutOfSomethingThatIsNotAZipIsEmptyRatherThanAnError()
	{
		Assert.Empty(ModArchive.ReadModIds(SevenZipFixture));
	}

	// ---------------------------------------------------------------------
	// Where staging goes
	// ---------------------------------------------------------------------

	[Fact]
	public void StagingLandsOnTheSameFilesystemAsTheModsFolder()
	{
		string mods = Path_("Stardew Valley", "Mods");
		Directory.CreateDirectory(mods);

		string root = ModArchive.TempRootFor(mods);

		Assert.True(Directory.Exists(root));
		// Not the system temp folder: the point of the whole exercise is that the final move is a rename.
		Assert.NotEqual(
			Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),
			Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar));
		try { Directory.Delete(root, true); } catch { }
	}

	[Fact]
	public void NoModsFolderMeansTheSystemTempFolder()
	{
		Assert.Equal(Path.GetTempPath(), ModArchive.TempRootFor(""));
	}

	// ---------------------------------------------------------------------
	// What the user is told when the download is the wrong one
	// ---------------------------------------------------------------------

	[Fact]
	public void AWrongDownloadIsDescribedByWhatItActuallyHeld()
	{
		string outDir = Path_("out");
		Directory.CreateDirectory(outDir);
		File.WriteAllText(Path.Combine(outDir, "Screenshots.png"), "");
		File.WriteAllText(Path.Combine(outDir, "ReadMe.txt"), "");

		string message = ModArchive.DescribeMissingManifest(Path_("Some Mod-1234-2-1.zip"), outDir);

		Assert.Contains("Some Mod-1234-2-1.zip", message);
		Assert.Contains("Screenshots.png", message);
		Assert.Contains("ReadMe.txt", message);
	}

	[Fact]
	public void AnArchiveThatHeldNothingSaysSo()
	{
		string outDir = Path_("out");
		Directory.CreateDirectory(outDir);

		Assert.Contains("extracted to nothing", ModArchive.DescribeMissingManifest(Path_("Empty.zip"), outDir));
	}
}
