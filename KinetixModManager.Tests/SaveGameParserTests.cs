using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using K4os.Compression.LZ4;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers the Skyrim SE / Fallout 4 save-header parser, including the LZ4- and zlib-compressed Skyrim bodies that
/// hold the plugin list. Saves are synthesized in memory so the tests are self-contained (no game install needed).
/// </summary>
public class SaveGameParserTests
{
	// --- little-endian writers matching the save format -------------------------

	private static void U16(List<byte> b, int v) { b.Add((byte)(v & 0xFF)); b.Add((byte)((v >> 8) & 0xFF)); }
	private static void U32(List<byte> b, uint v) { for (int i = 0; i < 4; i++) b.Add((byte)((v >> (8 * i)) & 0xFF)); }
	private static void Str16(List<byte> b, string s) { byte[] raw = Encoding.Latin1.GetBytes(s); U16(b, raw.Length); b.AddRange(raw); }

	private static void CommonHeader(List<byte> b, uint version, int saveNumber, string name, int level,
		string location, string playtime, int shotW, int shotH)
	{
		U32(b, 123);            // headerSize (ignored by the parser)
		U32(b, version);
		U32(b, (uint)saveNumber);
		Str16(b, name);
		U32(b, (uint)level);
		Str16(b, location);
		Str16(b, playtime);
		Str16(b, "NordRace");   // race editor id
		U16(b, 0);              // sex
		b.AddRange(BitConverter.GetBytes(1.0f)); // exp
		b.AddRange(BitConverter.GetBytes(2.0f)); // exp to next
		b.AddRange(BitConverter.GetBytes(0L));   // filetime
		U32(b, (uint)shotW);
		U32(b, (uint)shotH);
	}

	private static byte[] SkyrimBody(IEnumerable<string> regular, IEnumerable<string> light)
	{
		var body = new List<byte>();
		body.Add(74);           // formVersion
		U32(body, 0);           // plugin info size (unused by parser)
		var reg = new List<string>(regular);
		body.Add((byte)reg.Count);
		foreach (string m in reg) Str16(body, m);
		var esl = new List<string>(light);
		U16(body, esl.Count);
		foreach (string m in esl) Str16(body, m);
		return body.ToArray();
	}

	private static byte[] BuildSkyrim(ushort compression, byte[] body, int shotW = 2, int shotH = 2)
	{
		var b = new List<byte>();
		b.AddRange(Encoding.ASCII.GetBytes("TESV_SAVEGAME"));
		CommonHeader(b, 12, 7, "Hero", 42, "Whiterun", "115.4.24", shotW, shotH);
		U16(b, compression);
		b.AddRange(new byte[4 * shotW * shotH]); // screenshot (RGBA)

		if (compression == 0)
		{
			b.AddRange(body);
		}
		else if (compression == 2) // LZ4
		{
			byte[] comp = new byte[LZ4Codec.MaximumOutputSize(body.Length)];
			int n = LZ4Codec.Encode(body, 0, body.Length, comp, 0, comp.Length);
			U32(b, (uint)body.Length);
			U32(b, (uint)n);
			b.AddRange(comp[..n]);
		}
		else if (compression == 1) // zlib
		{
			using var ms = new MemoryStream();
			using (var zs = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) zs.Write(body, 0, body.Length);
			byte[] comp = ms.ToArray();
			U32(b, (uint)body.Length);
			U32(b, (uint)comp.Length);
			b.AddRange(comp);
		}
		return b.ToArray();
	}

	private static byte[] BuildFallout(IEnumerable<string> regular, IEnumerable<string> light,
		uint version = 15, int formVersion = 74, int shotW = 2, int shotH = 2)
	{
		var b = new List<byte>();
		b.AddRange(Encoding.ASCII.GetBytes("FO4_SAVEGAME"));
		CommonHeader(b, version, 3, "Sole", 15, "Sanctuary", "3.12.5", shotW, shotH);
		b.AddRange(new byte[4 * shotW * shotH]); // screenshot
		b.Add((byte)formVersion);
		Str16(b, "1.10.163");   // game version
		U32(b, 0);              // plugin info size
		var reg = new List<string>(regular);
		b.Add((byte)reg.Count);
		foreach (string m in reg) Str16(b, m);
		if (formVersion >= 68 && version == 15)
		{
			var esl = new List<string>(light);
			U16(b, esl.Count);
			foreach (string m in esl) Str16(b, m);
		}
		return b.ToArray();
	}

	private static SaveGame ParseBytes(byte[] data, string ext)
	{
		string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ext);
		File.WriteAllBytes(path, data);
		try
		{
			SaveGame? save = SaveGameParser.Parse(path);
			Assert.NotNull(save);
			return save!;
		}
		finally { File.Delete(path); }
	}

	[Theory]
	[InlineData((ushort)0)] // uncompressed
	[InlineData((ushort)1)] // zlib
	[InlineData((ushort)2)] // lz4
	public void Skyrim_ParsesMetadataAndPlugins_AcrossCompression(ushort compression)
	{
		byte[] body = SkyrimBody(new[] { "Skyrim.esm", "MyMod.esp" }, new[] { "Light.esl" });
		SaveGame save = ParseBytes(BuildSkyrim(compression, body), ".ess");

		Assert.Equal("Hero", save.CharacterName);
		Assert.Equal(42, save.Level);
		Assert.Equal("Whiterun", save.Location);
		Assert.Equal("115.4.24", save.Playtime);
		Assert.True(save.PluginsRead);
		Assert.Equal(new[] { "Skyrim.esm", "MyMod.esp", "Light.esl" }, save.Masters);
	}

	[Fact]
	public void Fallout4_ParsesMetadataAndPlugins()
	{
		SaveGame save = ParseBytes(BuildFallout(new[] { "Fallout4.esm", "MyFO4Mod.esp" }, new[] { "Patch.esl" }), ".fos");

		Assert.Equal("Sole", save.CharacterName);
		Assert.Equal(15, save.Level);
		Assert.Equal("Sanctuary", save.Location);
		Assert.True(save.PluginsRead);
		Assert.Equal(new[] { "Fallout4.esm", "MyFO4Mod.esp", "Patch.esl" }, save.Masters);
	}

	[Fact]
	public void Fallout4_OldFormVersion_SkipsEslList()
	{
		// formVersion below 68 has no ESL list; the parser must not read one (which would corrupt the master list).
		SaveGame save = ParseBytes(BuildFallout(new[] { "Fallout4.esm" }, Array.Empty<string>(), version: 11, formVersion: 60), ".fos");
		Assert.True(save.PluginsRead);
		Assert.Equal(new[] { "Fallout4.esm" }, save.Masters);
	}

	[Fact]
	public void NonSaveFile_ReturnsNull()
	{
		string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ess");
		File.WriteAllBytes(path, Encoding.ASCII.GetBytes("not a real save file at all"));
		try { Assert.Null(SaveGameParser.Parse(path)); }
		finally { File.Delete(path); }
	}
}
