using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>One program and how much memory it is holding.</summary>
public sealed record MemoryUser(string Name, long Bytes);

/// <summary>What the check found: whether there is room, and who is using the most when there is not.</summary>
public sealed record MemoryVerdict(bool Enough, long AvailableBytes, long NeededBytes, IReadOnlyList<MemoryUser> Biggest);

/// <summary>
/// Whether the computer has enough memory left to start a game, checked before it is started.
///
/// <para>
/// ⚠️ The limit that matters is not free RAM but how much memory Windows can still promise — RAM and the page file
/// together, its "commit limit". Low RAM only makes a game slow; running out of that makes it die. The Minecraft
/// accessibility pack died loading twice, at the controller calibration screen, with Minecraft itself using only
/// 2 GB: another program was holding 58 GB and Windows had 4 MB left to give anyone. The player heard only that
/// the game had crashed, and cannot glance at Task Manager to see why. So the manager looks first, and names the
/// programs holding the most.
/// </para>
///
/// <para>
/// Silent when there is room, which on almost every computer is every time.
/// </para>
/// </summary>
public static class LaunchMemoryCheck
{
	private const long Mb = 1024L * 1024;

	/// <summary>Programs holding less than this are not worth naming.</summary>
	public const long WorthNamingBytes = 1024 * Mb;

	/// <summary>What Java needs beyond Minecraft's own memory limit: itself, the game's graphics and sound, and the mods' native parts.</summary>
	public const long MinecraftOverheadBytes = 1536 * Mb;

	/// <summary>
	/// How much a game needs Windows to still have available. Minecraft's follows the limit the manager gives it; a
	/// limit of 0 (left to Java) is counted as 4 GB, which is less than Java would really take.
	/// </summary>
	public static long NeededBytesFor(GameProfile profile, int minecraftMaxMemoryMb) =>
		profile.IsMinecraft
			? (minecraftMaxMemoryMb > 0 ? minecraftMaxMemoryMb : 4096) * Mb + MinecraftOverheadBytes
			: profile.MemoryNeededMb * Mb;

	/// <summary>
	/// Decides, and when there is not enough, names the programs holding the most — each program once, however many
	/// processes it runs as (a browser is dozens), largest first, at most <paramref name="limit"/> of them.
	/// </summary>
	public static MemoryVerdict Evaluate(long availableBytes, long neededBytes, IEnumerable<MemoryUser> processes, int limit = 3)
	{
		if (availableBytes >= neededBytes)
			return new MemoryVerdict(true, availableBytes, neededBytes, Array.Empty<MemoryUser>());

		List<MemoryUser> biggest = processes
			.Where(p => !string.IsNullOrWhiteSpace(p.Name))
			.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
			.Select(g => new MemoryUser(g.First().Name, g.Sum(p => p.Bytes)))
			.Where(p => p.Bytes >= WorthNamingBytes)
			.OrderByDescending(p => p.Bytes)
			.Take(limit)
			.ToList();

		return new MemoryVerdict(false, availableBytes, neededBytes, biggest);
	}
}
