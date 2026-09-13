using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using K4os.Compression.LZ4;

namespace KinetixModManager;

/// <summary>
/// Parsed metadata for one Bethesda save file (Skyrim SE <c>.ess</c> or Fallout 4 <c>.fos</c>): the character,
/// level, location, in-game playtime, the save's timestamp, and the list of plugins the save was made with. The
/// plugin list is what lets the manager warn that a save now depends on a mod that has since been removed.
/// </summary>
public sealed class SaveGame
{
	public required string FilePath { get; init; }
	public string FileName => Path.GetFileName(FilePath);
	public string CharacterName { get; init; } = "";
	public int Level { get; init; }
	public string Location { get; init; } = "";
	/// <summary>The in-game playtime string as the engine stored it (e.g. "115.4.24"); shown verbatim.</summary>
	public string Playtime { get; init; } = "";
	public DateTime SaveTime { get; init; }
	public int SaveNumber { get; init; }
	/// <summary>Plugin file names the save was created with (regular masters followed by light/ESL masters).</summary>
	public IReadOnlyList<string> Masters { get; init; } = Array.Empty<string>();
	/// <summary>True when the plugin list was read successfully; false means metadata is valid but plugins are unknown
	/// (e.g. an unrecognized compression or a body we couldn't parse), so no missing-mod warning can be trusted.</summary>
	public bool PluginsRead { get; init; }
}

/// <summary>
/// Byte-level reader for Skyrim SE / Fallout 4 save headers. Both share an uncompressed header (so character,
/// level, location and playtime always parse); Skyrim SE additionally LZ4- or zlib-compresses the body that holds
/// the plugin list, which is decompressed here. Best-effort and defensive: any malformed field yields a null parse
/// (not a save) or a save with <see cref="SaveGame.PluginsRead"/> false, never an exception to the caller.
/// </summary>
public static class SaveGameParser
{
	// Sanity ceilings so a misparse (e.g. a wrong screenshot size) degrades gracefully instead of allocating wildly
	// or emitting garbage plugin names.
	private const int MaxDecompressedBody = 256 * 1024 * 1024;
	private const int MaxMasters = 4096;

	/// <summary>Parses a save file, or returns null if it isn't a recognized Skyrim SE / Fallout 4 save.</summary>
	public static SaveGame? Parse(string path, Action<string>? logError = null)
	{
		try
		{
			using FileStream fs = File.OpenRead(path);
			Span<byte> magic = stackalloc byte[13];
			if (fs.Read(magic) < 13) return null;

			bool skyrim = magic.SequenceEqual("TESV_SAVEGAME"u8);
			bool fallout = magic.Slice(0, 12).SequenceEqual("FO4_SAVEGAME"u8);
			if (!skyrim && !fallout) return null;

			fs.Position = skyrim ? 13 : 12; // FO4 magic is 12 bytes; Skyrim's is 13
			using var br = new BinaryReader(fs, Encoding.Latin1, leaveOpen: true);

			br.ReadUInt32();                       // headerSize (parsed sequentially, so unused)
			uint version = br.ReadUInt32();
			int saveNumber = (int)br.ReadUInt32();
			string name = ReadStr16(br);
			int level = (int)br.ReadUInt32();
			string location = ReadStr16(br);
			string playtime = ReadStr16(br);
			ReadStr16(br);                         // player race editor id (unused)
			br.ReadUInt16();                       // sex
			br.ReadSingle();                       // current exp
			br.ReadSingle();                       // exp to next level
			br.ReadInt64();                        // FILETIME — the file's own mtime is used instead (see below)
			uint shotWidth = br.ReadUInt32();
			uint shotHeight = br.ReadUInt32();

			var masters = new List<string>();
			bool pluginsRead;
			if (skyrim)
				pluginsRead = ReadSkyrimPlugins(br, fs, version, shotWidth, shotHeight, masters, logError);
			else
				pluginsRead = ReadFalloutPlugins(br, fs, version, shotWidth, shotHeight, masters, logError);

			return new SaveGame
			{
				FilePath = path,
				CharacterName = name,
				Level = level,
				Location = location,
				Playtime = playtime,
				SaveNumber = saveNumber,
				// The embedded FILETIME's exact semantics vary; the file's own last-write time is what the player
				// sees in Windows and is always reliable, so use it for the displayed/ sortable save date.
				SaveTime = SafeWriteTime(path),
				Masters = masters,
				PluginsRead = pluginsRead,
			};
		}
		catch (Exception ex)
		{
			logError?.Invoke($"Save parse failed for {Path.GetFileName(path)}: {ex.Message}");
			return null;
		}
	}

	/// <summary>Skyrim SE: read the uint16 compression type, skip the screenshot, decompress the body if needed,
	/// then read the master + ESL lists from the (decompressed) body start.</summary>
	private static bool ReadSkyrimPlugins(BinaryReader br, FileStream fs, uint version,
		uint shotWidth, uint shotHeight, List<string> masters, Action<string>? logError)
	{
		ushort compression = version >= 12 ? br.ReadUInt16() : (ushort)0;
		fs.Seek(4L * shotWidth * shotHeight, SeekOrigin.Current); // screenshot is RGBA, uncompressed

		byte[] body;
		if (compression == 0)
		{
			body = ReadChunk(fs, 1 << 20); // plenty for formVersion + plugin info + master list
		}
		else
		{
			uint decompSize = br.ReadUInt32();
			uint compSize = br.ReadUInt32();
			byte[] comp = br.ReadBytes((int)compSize);
			byte[]? dec = Decompress(compression, comp, (int)decompSize, logError);
			if (dec == null) return false;
			body = dec;
		}

		int pos = 0;
		if (!Advance(body, ref pos, 1)) return false;        // formVersion
		if (!TryReadU32(body, ref pos, out _)) return false; // plugin info size
		// Skyrim SE (save version >= 12) always carries the light/ESL master list after the regular one.
		return ReadMasterList(body, ref pos, masters, readEsl: true);
	}

	/// <summary>Fallout 4: uncompressed. Skip the screenshot, then read formVersion, the game-version string, the
	/// plugin info size, and the master list (with the ESL list only on the newer save version that has one).</summary>
	private static bool ReadFalloutPlugins(BinaryReader br, FileStream fs, uint version,
		uint shotWidth, uint shotHeight, List<string> masters, Action<string>? logError)
	{
		fs.Seek(4L * shotWidth * shotHeight, SeekOrigin.Current); // screenshot is RGBA, uncompressed
		byte[] body = ReadChunk(fs, 1 << 20);

		int pos = 0;
		if (pos >= body.Length) return false;
		int formVersion = body[pos++];
		if (!TryReadStr16(body, ref pos, out _)) return false; // game version string
		if (!TryReadU32(body, ref pos, out _)) return false;   // plugin info size
		// The ESL list was added to Fallout 4 saves at formVersion 68 on save version 15; older saves have none, so
		// reading a uint16 there would misinterpret unrelated bytes as a light-plugin count.
		bool readEsl = formVersion >= 68 && version == 15;
		return ReadMasterList(body, ref pos, masters, readEsl);
	}

	/// <summary>Reads a uint8 regular-master count and its names, then (when <paramref name="readEsl"/>) a uint16
	/// light-master count and its names, appending all to <paramref name="masters"/>. Validates the first name looks
	/// like a plugin file so a misparse (e.g. a wrong screenshot size) is rejected rather than surfaced as garbage.</summary>
	private static bool ReadMasterList(byte[] body, ref int pos, List<string> masters, bool readEsl)
	{
		if (pos >= body.Length) return false;
		int count = body[pos++];
		for (int i = 0; i < count; i++)
		{
			if (!TryReadStr16(body, ref pos, out string m)) return false;
			masters.Add(m);
		}
		if (readEsl && TryReadU16(body, ref pos, out int eslCount) && eslCount >= 0 && eslCount <= MaxMasters)
		{
			for (int i = 0; i < eslCount; i++)
			{
				if (!TryReadStr16(body, ref pos, out string m)) break;
				masters.Add(m);
			}
		}

		if (masters.Count == 0 || masters.Count > MaxMasters) return false;
		return LooksLikePlugin(masters[0]);
	}

	private static bool LooksLikePlugin(string name) =>
		name.EndsWith(".esp", StringComparison.OrdinalIgnoreCase) ||
		name.EndsWith(".esm", StringComparison.OrdinalIgnoreCase) ||
		name.EndsWith(".esl", StringComparison.OrdinalIgnoreCase);

	private static byte[]? Decompress(ushort type, byte[] comp, int decompSize, Action<string>? logError)
	{
		if (decompSize <= 0 || decompSize > MaxDecompressedBody) return null;
		try
		{
			var dst = new byte[decompSize];
			if (type == 2) // LZ4 block (Skyrim SE default, uiCompression=2)
			{
				int n = LZ4Codec.Decode(comp, 0, comp.Length, dst, 0, dst.Length);
				return n < 0 ? null : dst;
			}
			if (type == 1) // zlib (uiCompression=1)
			{
				using var ms = new MemoryStream(comp);
				using var zs = new ZLibStream(ms, CompressionMode.Decompress);
				int read = 0, r;
				while (read < dst.Length && (r = zs.Read(dst, read, dst.Length - read)) > 0) read += r;
				return read > 0 ? dst : null;
			}
			return null; // unknown compression id
		}
		catch (Exception ex)
		{
			logError?.Invoke($"Save body decompression failed: {ex.Message}");
			return null;
		}
	}

	private static byte[] ReadChunk(FileStream fs, int max)
	{
		int want = (int)Math.Min(max, Math.Max(0, fs.Length - fs.Position));
		var buf = new byte[want];
		int read = 0, r;
		while (read < want && (r = fs.Read(buf, read, want - read)) > 0) read += r;
		return read == want ? buf : buf[..read];
	}

	private static DateTime SafeWriteTime(string path)
	{
		try { return File.GetLastWriteTime(path); } catch { return DateTime.MinValue; }
	}

	// --- stream + buffer readers -------------------------------------------------

	private static string ReadStr16(BinaryReader br)
	{
		ushort len = br.ReadUInt16();
		byte[] bytes = br.ReadBytes(len);
		return Encoding.Latin1.GetString(bytes);
	}

	private static bool Advance(byte[] b, ref int pos, int n)
	{
		if (pos + n > b.Length) return false;
		pos += n;
		return true;
	}

	private static bool TryReadU16(byte[] b, ref int pos, out int value)
	{
		value = 0;
		if (pos + 2 > b.Length) return false;
		value = b[pos] | (b[pos + 1] << 8);
		pos += 2;
		return true;
	}

	private static bool TryReadU32(byte[] b, ref int pos, out uint value)
	{
		value = 0;
		if (pos + 4 > b.Length) return false;
		value = (uint)(b[pos] | (b[pos + 1] << 8) | (b[pos + 2] << 16) | (b[pos + 3] << 24));
		pos += 4;
		return true;
	}

	private static bool TryReadStr16(byte[] b, ref int pos, out string value)
	{
		value = "";
		if (!TryReadU16(b, ref pos, out int len)) return false;
		if (pos + len > b.Length) return false;
		value = Encoding.Latin1.GetString(b, pos, len);
		pos += len;
		return true;
	}
}
