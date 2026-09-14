using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace KinetixModManager;

/// <summary>The archive container of a downloaded mod, identified by its file signature.</summary>
public enum ArchiveFormat
{
	Zip,
	SevenZip,
	Rar,
	/// <summary>Nothing recognisable in the first bytes — the caller falls back to the file extension.</summary>
	Unknown,
}

/// <summary>
/// Thrown when an archive extracted fine but doesn't hold a mod for the active game — most often a Stardew
/// download with no <c>manifest.json</c> anywhere inside. Distinct from an I/O or extraction failure because
/// the fix is different: the file is the wrong download, usually because the mod is linked to the wrong Nexus
/// page. The message carries what the archive actually contained, for the error log.
/// </summary>
public sealed class ModArchiveContentException : Exception
{
	public ModArchiveContentException(string message) : base(message) { }
}

/// <summary>
/// Getting a downloaded mod out of whatever it arrived in.
///
/// <para>
/// This is the layer underneath every install: identify the container, unpack it somewhere safe, and say how
/// far along it is while doing so. What the unpacked files then <em>mean</em> — a Stardew manifest folder, a
/// Bethesda staging tree, a BepInEx plugin — is the layout code's business and is deliberately not here.
/// </para>
///
/// <para>
/// It is managed code on every path, which is the change that lets a second front end exist at all. The
/// Windows app used to shell out to <c>7za.exe</c>, downloading it from 7-zip.org on first use — a 2010
/// binary, unsigned, fetched at install time, and absent entirely on Linux. SharpCompress reads the same
/// archives in-process, including the LZMA2 and BCJ2 filters that 7za920 predates, so dropping the download
/// removes a platform dependency and a class of "the install just stopped" failures at once.
/// </para>
/// </summary>
public static class ModArchive
{
	/// <summary>
	/// Detects a mod archive's real format from its leading bytes (magic number) rather than its file extension.
	/// Nexus mods are commonly .7z or .rar, but the downloaded file can end up named ".zip" — the CDN filename
	/// isn't always present, in which case the NXM resolver falls back to a ".zip" name. Feeding a 7z/rar to
	/// .NET's ZipFile then throws "End of Central Directory record could not be found". Sniffing the signature
	/// routes each archive to the right extractor regardless of how it was named.
	/// </summary>
	public static ArchiveFormat DetectFormat(string path)
	{
		try
		{
			using FileStream fs = File.OpenRead(path);
			byte[] head = new byte[8];
			int n = fs.Read(head, 0, head.Length);
			// 7z: 37 7A BC AF 27 1C
			if (n >= 6 && head[0] == 0x37 && head[1] == 0x7A && head[2] == 0xBC && head[3] == 0xAF && head[4] == 0x27 && head[5] == 0x1C)
				return ArchiveFormat.SevenZip;
			// RAR (v1.5–4.x and v5.0 both start): 52 61 72 21 1A 07
			if (n >= 6 && head[0] == 0x52 && head[1] == 0x61 && head[2] == 0x72 && head[3] == 0x21 && head[4] == 0x1A && head[5] == 0x07)
				return ArchiveFormat.Rar;
			// ZIP (incl. empty/spanned variants): 50 4B {03 04 | 05 06 | 07 08}
			if (n >= 4 && head[0] == 0x50 && head[1] == 0x4B &&
				((head[2] == 0x03 && head[3] == 0x04) || (head[2] == 0x05 && head[3] == 0x06) || (head[2] == 0x07 && head[3] == 0x08)))
				return ArchiveFormat.Zip;
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Install", $"reading the first bytes of {path} to identify it", ex); }
		return ArchiveFormat.Unknown;
	}

	/// <summary>
	/// The format <see cref="Extract"/> will actually use: the signature where the bytes say something, and
	/// only otherwise the extension. An unrecognised, extension-less file is treated as a zip, because that is
	/// what the error message reads best for — "not a zip file" is truer than a guess at 7z.
	/// </summary>
	public static ArchiveFormat FormatOf(string path)
	{
		ArchiveFormat fmt = DetectFormat(path);
		if (fmt != ArchiveFormat.Unknown) return fmt;

		string ext = Path.GetExtension(path).ToLowerInvariant();
		return ext == ".7z" ? ArchiveFormat.SevenZip
			 : ext == ".rar" ? ArchiveFormat.Rar
			 : ArchiveFormat.Zip;
	}

	/// <summary>
	/// Unpacks <paramref name="archivePath"/> into <paramref name="outputDir"/>, reporting 0–100 as a
	/// percentage of uncompressed bytes written.
	///
	/// Each entry's destination is resolved and checked before that entry is written, so an archive carrying
	/// <c>../../autoexec</c> fails without it ever reaching the disk. Callers should still call
	/// <see cref="GuardAgainstEscapedEntries"/> on the result — a link can point outside a folder without
	/// anything in its name saying so.
	/// </summary>
	public static void Extract(string archivePath, string outputDir, IProgress<double>? progress = null)
	{
		Directory.CreateDirectory(outputDir);
		switch (FormatOf(archivePath))
		{
			case ArchiveFormat.SevenZip:
			case ArchiveFormat.Rar:
				ExtractWithSharpCompress(archivePath, outputDir, progress);
				break;
			default:
				ExtractZip(archivePath, outputDir, progress);
				break;
		}
	}

	/// <summary>
	/// Extracts a .zip entry-by-entry so install progress can be reported as a true percentage of bytes written.
	/// Mirrors <see cref="ZipFile.ExtractToDirectory(string,string)"/> including its path-traversal guard.
	/// </summary>
	private static void ExtractZip(string archivePath, string outputDir, IProgress<double>? progress)
	{
		using ZipArchive archive = ZipFile.OpenRead(archivePath);
		string root = CanonicalRoot(outputDir);
		long total = 0;
		foreach (ZipArchiveEntry e in archive.Entries) total += e.Length;
		if (total <= 0) total = 1;

		long done = 0;
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			string destPath = ResolveInside(root, entry.FullName);

			// Directory entries have an empty Name; create the folder and move on.
			bool isDir = entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\") || string.IsNullOrEmpty(entry.Name);
			if (isDir)
			{
				Directory.CreateDirectory(destPath);
				continue;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
			entry.ExtractToFile(destPath, overwrite: true);
			done += entry.Length;
			progress?.Report((double)done / total * 100.0);
		}
		progress?.Report(100.0);
	}

	/// <summary>
	/// Extracts a 7z or RAR archive, preserving folder structure.
	///
	/// Read forwards through a single reader rather than entry-by-entry off the archive, because both formats
	/// are commonly <em>solid</em>: the files share one compressed stream, so asking for entry 200 on its own
	/// means decompressing the 199 before it again. A hundred-file mod extracted that way takes minutes
	/// instead of seconds, and looks to the user like a hang rather than a slow install.
	/// </summary>
	private static void ExtractWithSharpCompress(string archivePath, string outputDir, IProgress<double>? progress)
	{
		string root = CanonicalRoot(outputDir);
		var options = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };

		long total = 0;
		using (IArchive sizing = ArchiveFactory.OpenArchive(archivePath))
		{
			foreach (IArchiveEntry e in sizing.Entries)
			{
				if (e.IsDirectory) continue;
				// Reject before a single byte is written, for the same reason the zip path does. An entry with
				// no name at all is left to SharpCompress to refuse; there is nothing here to judge.
				if (!string.IsNullOrEmpty(e.Key)) ResolveInside(root, e.Key);
				if (e.Size > 0) total += e.Size;
			}
		}
		if (total <= 0) total = 1;

		long done = 0;
		using IArchive archive = ArchiveFactory.OpenArchive(archivePath);
		using IReader reader = archive.ExtractAllEntries();
		while (reader.MoveToNextEntry())
		{
			if (reader.Entry.IsDirectory) continue;
			reader.WriteEntryToDirectory(outputDir, options);
			if (reader.Entry.Size > 0) done += reader.Entry.Size;
			progress?.Report(Math.Min((double)done / total * 100.0, 100.0));
		}
		progress?.Report(100.0);
	}

	/// <summary>
	/// Confirms that nothing under <paramref name="extractedRoot"/> leads anywhere else — the check that
	/// catches what entry names alone cannot, namely a symlink or junction in the archive pointing at the
	/// user's own files. Throws on the first escape found.
	///
	/// <para>
	/// It matters because what happens next is a deploy: the extracted tree is copied, hard-linked or moved
	/// into the game folder. A link called <c>textures</c> whose target is the user's home directory turns
	/// that into writing over their files, and nothing in the entry's name would have suggested it.
	/// </para>
	///
	/// <para>
	/// Links are checked but never followed, which both keeps the walk finite — two links pointing at each
	/// other would otherwise loop for ever — and is enough on its own: an escape needs some link in the chain
	/// whose own target is outside, and that one is refused when the walk reaches it.
	/// </para>
	/// </summary>
	public static void GuardAgainstEscapedEntries(string extractedRoot)
	{
		string root = CanonicalRoot(extractedRoot);
		var pending = new Queue<string>();
		pending.Enqueue(Path.GetFullPath(extractedRoot));

		while (pending.Count > 0)
		{
			foreach (string entry in Directory.GetFileSystemEntries(pending.Dequeue()))
			{
				string? target = LinkTargetOf(entry);
				string leadsTo = target == null
					? Path.GetFullPath(entry)
					: Path.GetFullPath(Path.Combine(Path.GetDirectoryName(entry) ?? root, target));

				if (!leadsTo.StartsWith(root, StringComparison.OrdinalIgnoreCase))
					throw new InvalidOperationException($"Unsafe archive: entry escapes the extraction directory ({entry}).");

				if (target == null && Directory.Exists(entry)) pending.Enqueue(entry);
			}
		}
	}

	/// <summary>
	/// Where <paramref name="path"/> points if it is a link, or null if it is an ordinary file or folder.
	/// Deliberately the immediate target rather than the final one, because asking for the final target of a
	/// broken link throws — and a mod shipping a link to a file it does not include is careless, not hostile.
	/// </summary>
	private static string? LinkTargetOf(string path)
	{
		try
		{
			// The raw target as stored, which is what has to be judged: a relative one is relative to the
			// link, and FileSystemInfo.FullName would resolve it against the working directory instead.
			return new FileInfo(path).LinkTarget ?? new DirectoryInfo(path).LinkTarget;
		}
		catch (Exception ex)
		{
			// Something that cannot even be asked about is not something to wave through.
			DiagnosticLog.WriteException("Install", $"resolving {path} while checking the extracted files", ex);
			throw new InvalidOperationException($"Unsafe archive: {path} could not be checked.", ex);
		}
	}

	/// <summary>
	/// Unpacks any archives found inside an already-extracted download, one level deep, into a sibling folder
	/// each. Used only as a fallback when the expected mod files weren't found at the top level — a "pick the
	/// variant you want" pack, or a zip that simply contains the real zip. Best-effort: an archive that can't
	/// be read is skipped rather than failing the install.
	/// </summary>
	public static void ExtractNested(string extractedRoot, int limit = 12)
	{
		string[] inner;
		try
		{
			inner = Directory.GetFiles(extractedRoot, "*.*", SearchOption.AllDirectories)
				.Where(f =>
				{
					string ext = Path.GetExtension(f).ToLowerInvariant();
					return ext == ".zip" || ext == ".7z" || ext == ".rar";
				})
				.Take(limit)   // a sane bound: a mod pack with more variants than this isn't auto-installable anyway
				.ToArray();
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Install", $"listing the archives inside {extractedRoot}", ex);
			return;
		}

		foreach (string archive in inner)
		{
			try
			{
				string unpacked = archive + "__unpacked";
				Extract(archive, unpacked);
				// The outer folder was checked before this one existed, so the inner archive gets its own
				// check. Best-effort like the rest of this: a nested archive that fails it is skipped, not
				// allowed through.
				GuardAgainstEscapedEntries(unpacked);
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Install", $"unpacking the nested archive {archive}", ex); }
		}
	}

	/// <summary>
	/// A writable base directory for extraction on the same filesystem as <paramref name="modsPath"/>, so
	/// staging doesn't fill the system drive and the final move stays a rename rather than a copy.
	///
	/// On Windows that is the destination drive's root, which is where the manager has always put it. Elsewhere
	/// a path root is <c>/</c> and nobody may write there, so the same guarantee is had by staging beside the
	/// mods folder instead — same filesystem by construction, and dot-prefixed so it stays out of the way.
	/// Falls back to the system temp folder when neither can be created.
	/// </summary>
	public static string TempRootFor(string modsPath)
	{
		try
		{
			if (!string.IsNullOrEmpty(modsPath))
			{
				string full = Path.GetFullPath(modsPath);
				string candidate;

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					string? root = Path.GetPathRoot(full);
					if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return Path.GetTempPath();
					candidate = Path.Combine(root, "KinetixModManager.tmp");
				}
				else
				{
					string? parent = Path.GetDirectoryName(full.TrimEnd(Path.DirectorySeparatorChar));
					if (string.IsNullOrEmpty(parent)) return Path.GetTempPath();
					candidate = Path.Combine(parent, ".kinetix-tmp");
				}

				Directory.CreateDirectory(candidate);
				return candidate;
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Install", "choosing a temporary folder on the mods drive", ex); }
		return Path.GetTempPath();
	}

	/// <summary>
	/// Reads the mod UniqueIDs declared inside a downloaded .zip, by parsing every manifest.json in it without
	/// extracting anything. Pairing these with <see cref="ModManifest.ParseNexusIdFromFileName"/> recovers exactly
	/// which installed mods came from which Nexus page — including every mod of a multi-mod download, where usually
	/// only one (or none) carries an update key. Returns an empty list for anything unreadable or not a zip.
	/// </summary>
	public static List<string> ReadModIds(string archivePath)
	{
		var ids = new List<string>();
		try
		{
			if (DetectFormat(archivePath) != ArchiveFormat.Zip) return ids;
			using ZipArchive archive = ZipFile.OpenRead(archivePath);
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				if (!entry.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) continue;
				if (entry.Length > 512 * 1024) continue;   // a "manifest" that large isn't one
				try
				{
					using var reader = new StreamReader(entry.Open());
					JObject manifest = JObject.Parse(reader.ReadToEnd());
					string? id = ModScanner.ManifestString(manifest, "UniqueID");
					if (!string.IsNullOrWhiteSpace(id)) ids.Add(id!.Trim());
				}
				catch (Exception ex) { DiagnosticLog.WriteException("Install", $"reading a manifest inside {archivePath}", ex); }
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Install", $"reading {archivePath} as a zip", ex); }
		return ids;
	}

	/// <summary>
	/// Builds the diagnostic message for a Stardew download with no manifest.json: names the archive and lists
	/// what was actually extracted, so the error log shows whether the file was empty, held only documentation,
	/// or is simply a different mod than expected.
	/// </summary>
	public static string DescribeMissingManifest(string archivePath, string extractedRoot)
	{
		string contents;
		try
		{
			var names = Directory.EnumerateFileSystemEntries(extractedRoot, "*", SearchOption.TopDirectoryOnly)
				.Select(Path.GetFileName).Take(8).ToList();
			contents = names.Count == 0 ? "the archive extracted to nothing" : "it contains: " + string.Join(", ", names);
		}
		catch { contents = "its contents could not be listed"; }

		return $"No manifest.json found in {Path.GetFileName(archivePath)} — {contents}. " +
			   "This download is not a SMAPI mod, so it is probably the wrong file for this mod.";
	}

	/// <summary><paramref name="dir"/> as an absolute path ending in a separator, ready to prefix-match against.</summary>
	private static string CanonicalRoot(string dir) =>
		Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

	/// <summary>
	/// Where an archive entry lands, having proved it lands inside <paramref name="canonicalRoot"/>.
	///
	/// Archives are written on Windows as often as not, so a backslash inside an entry name is a folder
	/// separator and has to be read as one here — on Linux it is an ordinary filename character, which would
	/// turn <c>foo\bar.dll</c> into one oddly-named file at the top level and leave the mod's folder structure
	/// flattened.
	/// </summary>
	private static string ResolveInside(string canonicalRoot, string entryName)
	{
		string relative = entryName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
		string destPath = Path.GetFullPath(Path.Combine(canonicalRoot, relative));
		if (!destPath.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException($"Unsafe archive: entry escapes the extraction directory ({entryName}).");
		return destPath;
	}
}
