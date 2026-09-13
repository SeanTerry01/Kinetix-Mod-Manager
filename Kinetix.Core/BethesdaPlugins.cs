using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KinetixModManager;

/// <summary>
/// Reading a Bethesda plugin file: whether a name is one, whether the engine already owns it, and what it
/// says in its own header about being a master, being light, and what it depends on.
///
/// <para>
/// This is the format's business rather than the manager's, which is why it belongs beside the rules and
/// not inside a class about moving files around. A plugin declares its masters in its <c>TES4</c> header,
/// and everything the manager knows about load order rests on reading that correctly: a plugin loaded
/// before something it depends on does not warn, it crashes the game or silently drops content.
/// </para>
///
/// <para>
/// The light flag matters for the same reason and is easier to get wrong. Light plugins load into their own
/// container with room for four thousand, rather than counting against the 255 the engine can address, so
/// mistaking one for the other is the difference between a load order that fits and one that does not.
/// See <see cref="PluginSlots"/>.
/// </para>
/// </summary>
public static class BethesdaPlugins
{
	/// <summary>File extensions of Bethesda plugins that participate in load order.</summary>
	public static bool IsPluginFile(string fileName)
	{
		string ext = Path.GetExtension(fileName).ToLowerInvariant();
		return ext == ".esp" || ext == ".esm" || ext == ".esl";
	}

	/// <summary>
	/// Base-game and official DLC master files that the engine always loads first on its own. They are
	/// kept implicit: never shown in the Plugin Order list and never written to plugins.txt.
	/// </summary>
	private static readonly HashSet<string> SkyrimBaseMasters = new(StringComparer.OrdinalIgnoreCase)
	{
		"Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm"
	};

	private static readonly HashSet<string> Fallout4BaseMasters = new(StringComparer.OrdinalIgnoreCase)
	{
		"Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm", "DLCCoast.esm",
		"DLCworkshop02.esm", "DLCworkshop03.esm", "DLCNukaWorld.esm", "DLCUltraHighResolution.esm"
	};

	/// <summary>True when <paramref name="fileName"/> is an implicit base-game/DLC master for the game.</summary>
	public static bool IsBaseMaster(string activeGame, string fileName) => GameProfiles.BaseId(activeGame) switch
	{
		"SkyrimSE" => SkyrimBaseMasters.Contains(fileName),
		"Fallout4" => Fallout4BaseMasters.Contains(fileName),
		_ => false
	};

	/// <summary>
	/// The implicit base-game/DLC master file names for the game. These never appear in the Plugin Order list but
	/// still occupy regular plugin slots, so the plugin-limit check counts the ones actually present on disk.
	/// </summary>
	public static IReadOnlyCollection<string> BaseMasters(string activeGame) => GameProfiles.BaseId(activeGame) switch
	{
		"SkyrimSE" => SkyrimBaseMasters,
		"Fallout4" => Fallout4BaseMasters,
		_ => Array.Empty<string>()
	};

	/// <summary>
	/// Reads a plugin's master/light status from its TES4 record header flags, falling back to the file
	/// extension when the header cannot be read. The engine loads master-flagged and light (ESL) plugins
	/// before regular plugins, so this — not the extension alone — decides the masters-first grouping
	/// (an ESL-flagged <c>.esp</c> loads with the masters even though its extension says otherwise).
	/// </summary>
	public static (bool IsMaster, bool IsLight) ReadPluginFlags(string filePath)
	{
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		bool extMaster = ext == ".esm" || ext == ".esl";
		bool extLight = ext == ".esl";
		try
		{
			using FileStream fs = File.OpenRead(filePath);
			byte[] buf = new byte[12];
			if (fs.Read(buf, 0, 12) == 12 && buf[0] == (byte)'T' && buf[1] == (byte)'E' && buf[2] == (byte)'S' && buf[3] == (byte)'4')
			{
				uint flags = BitConverter.ToUInt32(buf, 8);
				bool master = (flags & 0x1u) != 0 || extMaster;     // 0x1 = ESM (master)
				bool light  = (flags & 0x200u) != 0 || extLight;    // 0x200 = light (ESL / ESPFE)
				return (master, light);
			}
		}
		// An unreadable header falls back to the extension classification below, but a plugin whose header
		// cannot be read is worth knowing about: it is how a corrupt download presents.
		catch (Exception ex) { DiagnosticLog.WriteException("Plugins", $"reading the header of {filePath}", ex); }
		return (extMaster, extLight);
	}

	/// <summary>
	/// Reads the master files a plugin depends on, from the MAST subrecords in its TES4 header. These are
	/// the plugins that must load before this one, and drive the dependency-aware auto-sort. Returns an
	/// empty list if the header cannot be parsed.
	/// </summary>
	public static List<string> ReadPluginMasters(string filePath)
	{
		var masters = new List<string>();
		try
		{
			using FileStream fs = File.OpenRead(filePath);
			using var br = new System.IO.BinaryReader(fs);

			byte[] sig = br.ReadBytes(4);
			if (sig.Length < 4 || sig[0] != (byte)'T' || sig[1] != (byte)'E' || sig[2] != (byte)'S' || sig[3] != (byte)'4')
				return masters;

			uint dataSize = br.ReadUInt32();
			br.ReadUInt32(); // flags
			br.ReadUInt32(); // form id
			br.ReadUInt32(); // version control info
			br.ReadUInt16(); // internal version
			br.ReadUInt16(); // unknown
			// The remaining record data is a series of fields: type[4] + size[2] + data[size].
			byte[] data = br.ReadBytes((int)Math.Min(dataSize, (uint)int.MaxValue));

			int pos = 0;
			while (pos + 6 <= data.Length)
			{
				string type = System.Text.Encoding.ASCII.GetString(data, pos, 4);
				ushort size = BitConverter.ToUInt16(data, pos + 4);
				pos += 6;
				if (pos + size > data.Length) break;
				if (type == "MAST")
				{
					int strLen = size;
					while (strLen > 0 && data[pos + strLen - 1] == 0) strLen--; // trim trailing null(s)
					if (strLen > 0)
					{
						string name = System.Text.Encoding.Latin1.GetString(data, pos, strLen);
						if (!string.IsNullOrWhiteSpace(name)) masters.Add(name);
					}
				}
				pos += size;
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Plugins", $"reading the masters of {filePath}", ex); }
		return masters;
	}
}
