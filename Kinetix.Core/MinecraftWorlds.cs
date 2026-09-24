using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>One Minecraft world on disk.</summary>
public sealed class MinecraftWorld
{
	/// <summary>The world's folder, under some <c>saves</c>.</summary>
	public required string Folder { get; init; }

	/// <summary>The name the game shows, from <c>level.dat</c> — falling back to the folder's name.</summary>
	public required string Name { get; init; }

	/// <summary>The Minecraft version it was last played in, e.g. <c>26.3</c>, or <c>""</c> when not recorded.</summary>
	public string LastPlayedVersion { get; init; } = "";

	/// <summary>That version's data version, or 0 when not recorded. Rises with every Minecraft release.</summary>
	public int DataVersion { get; init; }
}

/// <summary>
/// Finding, describing and copying Minecraft worlds — so that a new modpack can start from a world the player
/// already has.
///
/// <para>
/// ⚠️ A world is copied, never moved, and a world from a NEWER Minecraft than the pack runs is warned about before
/// it is copied. Minecraft upgrades a world when an older one is opened in a newer game, but it cannot go back:
/// opened in an older version, a newer world may refuse to load or lose what the older game does not understand.
/// Copying keeps the player's original safe whatever happens to the copy.
/// </para>
/// </summary>
public static class MinecraftWorlds
{
	/// <summary>Every world in a Minecraft folder's <c>saves</c>, by name.</summary>
	public static IReadOnlyList<MinecraftWorld> FindIn(string gameFolder)
	{
		string saves = Path.Combine(gameFolder, "saves");
		if (!Directory.Exists(saves)) return Array.Empty<MinecraftWorld>();

		return Directory.EnumerateDirectories(saves)
			.Where(d => File.Exists(Path.Combine(d, "level.dat")))
			.Select(Describe)
			.OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
	}

	/// <summary>Reads a world's name and last-played version from its <c>level.dat</c>. Never throws.</summary>
	public static MinecraftWorld Describe(string worldFolder)
	{
		string folderName = Path.GetFileName(worldFolder.TrimEnd('\\', '/'));

		try
		{
			using FileStream file = File.OpenRead(Path.Combine(worldFolder, "level.dat"));
			if (ReadNbt(file) is not Dictionary<string, object?> root ||
				root.GetValueOrDefault("Data") is not Dictionary<string, object?> data)
				return new MinecraftWorld { Folder = worldFolder, Name = folderName };

			string name = data.GetValueOrDefault("LevelName") as string ?? "";
			string version = (data.GetValueOrDefault("Version") as Dictionary<string, object?>)?
				.GetValueOrDefault("Name") as string ?? "";
			int dataVersion = data.GetValueOrDefault("DataVersion") as int? ?? 0;

			return new MinecraftWorld
			{
				Folder = worldFolder,
				Name = name.Trim().Length > 0 ? name.Trim() : folderName,
				LastPlayedVersion = version,
				DataVersion = dataVersion
			};
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", $"reading the world {folderName}", ex);
			return new MinecraftWorld { Folder = worldFolder, Name = folderName };
		}
	}

	/// <summary>
	/// Whether Minecraft <paramref name="version"/> came out after <paramref name="than"/>, by the release dates in
	/// Mojang's version manifest — or <c>null</c> when either is not in it and the question cannot be answered.
	///
	/// Dates rather than the version numbers themselves, because Minecraft's numbering changed scheme from 1.21 to
	/// 26.1, and a snapshot like <c>26.3-rc-2</c> has no numeric order at all.
	/// </summary>
	public static bool? IsNewer(string version, string than, JObject versionManifest)
	{
		DateTimeOffset? Released(string id) =>
			(versionManifest["versions"] as JArray ?? new JArray())
				.FirstOrDefault(v => string.Equals((string?)v["id"], id, StringComparison.Ordinal)) is { } entry &&
			DateTimeOffset.TryParse((string?)entry["releaseTime"], System.Globalization.CultureInfo.InvariantCulture,
				System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset when)
				? when
				: null;

		if (version.Length == 0 || than.Length == 0) return null;
		if (version == than) return false;

		DateTimeOffset? a = Released(version), b = Released(than);
		return a.HasValue && b.HasValue ? a > b : null;
	}

	/// <summary>
	/// Copies a world into <paramref name="targetGameFolder"/>'s <c>saves</c>, under its own folder name — numbered
	/// when that is taken — and returns the new folder. The lock file a running game holds is not copied.
	/// </summary>
	public static string CopyInto(MinecraftWorld world, string targetGameFolder)
	{
		string saves = Path.Combine(targetGameFolder, "saves");
		Directory.CreateDirectory(saves);

		string stem = Path.GetFileName(world.Folder.TrimEnd('\\', '/'));
		string target = Path.Combine(saves, stem);
		for (int n = 2; Directory.Exists(target); n++) target = Path.Combine(saves, $"{stem} ({n})");

		CopyFolder(world.Folder, target);
		return target;
	}

	private static void CopyFolder(string source, string target)
	{
		Directory.CreateDirectory(target);

		foreach (string file in Directory.EnumerateFiles(source))
		{
			if (string.Equals(Path.GetFileName(file), "session.lock", StringComparison.OrdinalIgnoreCase)) continue;
			File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
		}

		foreach (string folder in Directory.EnumerateDirectories(source))
			CopyFolder(folder, Path.Combine(target, Path.GetFileName(folder)));
	}

	// -------------------------------------------------------------------------
	// NBT — just enough of it to read level.dat
	// -------------------------------------------------------------------------

	/// <summary>
	/// Reads a gzipped NBT document into plain values: a compound becomes a dictionary, a list a list, and the
	/// numbers and strings themselves. <c>level.dat</c> is a few kilobytes, so reading all of it is simpler and
	/// safer than skipping to the three values wanted.
	/// </summary>
	public static object? ReadNbt(Stream gzipped)
	{
		using var gzip = new GZipStream(gzipped, CompressionMode.Decompress);
		using var reader = new BinaryReader(gzip, Encoding.UTF8);

		byte type = reader.ReadByte();
		if (type != 10) return null;   // the root is always a compound
		ReadString(reader);             // its name, usually empty
		return ReadPayload(reader, type, 0);
	}

	private static object? ReadPayload(BinaryReader r, byte type, int depth)
	{
		// Real files nest a handful deep; this only guards against a corrupt one recursing forever.
		if (depth > 64) throw new InvalidDataException("NBT nested too deeply.");

		switch (type)
		{
			case 1: return r.ReadSByte();
			case 2: return ReadInt16(r);
			case 3: return ReadInt32(r);
			case 4: return ReadInt64(r);
			case 5: return BitConverter.Int32BitsToSingle(ReadInt32(r));
			case 6: return BitConverter.Int64BitsToDouble(ReadInt64(r));
			case 7: return r.ReadBytes(Length(r));
			case 8: return ReadString(r);
			case 9:
			{
				byte itemType = r.ReadByte();
				int count = ReadInt32(r);
				var items = new List<object?>(Math.Max(0, Math.Min(count, 4096)));
				for (int i = 0; i < count; i++) items.Add(ReadPayload(r, itemType, depth + 1));
				return items;
			}
			case 10:
			{
				var compound = new Dictionary<string, object?>(StringComparer.Ordinal);
				for (byte child = r.ReadByte(); child != 0; child = r.ReadByte())
				{
					string name = ReadString(r);
					compound[name] = ReadPayload(r, child, depth + 1);
				}
				return compound;
			}
			case 11:
			{
				int count = Length(r);
				var ints = new int[count];
				for (int i = 0; i < count; i++) ints[i] = ReadInt32(r);
				return ints;
			}
			case 12:
			{
				int count = Length(r);
				var longs = new long[count];
				for (int i = 0; i < count; i++) longs[i] = ReadInt64(r);
				return longs;
			}
			default: throw new InvalidDataException($"Unknown NBT tag {type}.");
		}
	}

	private static int Length(BinaryReader r)
	{
		int length = ReadInt32(r);
		if (length < 0) throw new InvalidDataException("Negative NBT length.");
		return length;
	}

	// NBT is big-endian; BinaryReader is little-endian.
	private static short ReadInt16(BinaryReader r) => System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(r.ReadBytes(2));
	private static int ReadInt32(BinaryReader r) => System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(r.ReadBytes(4));
	private static long ReadInt64(BinaryReader r) => System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(r.ReadBytes(8));

	/// <summary>
	/// An NBT string: a big-endian length, then "modified UTF-8" — the same as UTF-8 for everything but a zero
	/// character and characters outside the basic plane, neither of which a world's name will realistically hold.
	/// </summary>
	private static string ReadString(BinaryReader r)
	{
		ushort length = (ushort)ReadInt16(r);
		return Encoding.UTF8.GetString(r.ReadBytes(length));
	}
}
