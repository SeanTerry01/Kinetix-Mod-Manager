using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace KinetixModManager;

/// <summary>What the last run of Minecraft actually was.</summary>
public sealed class MinecraftLaunchOutcome
{
	/// <summary>True when Fabric loaded — the run had mods.</summary>
	public bool FabricLoaded { get; init; }

	/// <summary>The Minecraft version that ran, e.g. <c>26.2</c>, or <c>""</c> when the log did not say.</summary>
	public string GameVersion { get; init; } = "";

	/// <summary>The Fabric loader build that ran, or <c>""</c>.</summary>
	public string LoaderVersion { get; init; } = "";

	/// <summary>How many mods the loader accepted, or <c>-1</c> when the log did not say.</summary>
	public int ModCount { get; init; } = -1;

	/// <summary>
	/// The mod ids read out of the list that follows the count.
	///
	/// Complete on a normal log — verified against a real one, all 51 of the 51 the loader declared. Can still
	/// fall short of <see cref="ModCount"/> on a setup with hundreds of mods, where the list runs past the
	/// point this stops reading, so that count remains the number to quote.
	/// </summary>
	public IReadOnlyList<string> ModIds { get; init; } = Array.Empty<string>();

	/// <summary>True when there was no log to read at all.</summary>
	public bool NoLog { get; init; }
}

/// <summary>
/// Reads <c>.minecraft\logs\latest.log</c> to answer the one question the game itself never answers: did the
/// run that just happened have the mods, or not?
///
/// <para>
/// This exists because of a real evening lost to it. Fabric, Fabric API and the accessibility mod were all
/// installed correctly and the game still said nothing, because the launcher had started the vanilla profile
/// instead of the Fabric one. Nothing anywhere reported that. The game starts, plays perfectly, and is simply
/// silent — and from outside, a vanilla launch and a modded one are indistinguishable.
/// </para>
///
/// <para>
/// The log distinguishes them absolutely. A Fabric run opens with
/// <c>Loading Minecraft 26.2 with Fabric Loader 0.19.5</c> followed by <c>Loading 49 mods:</c> and the list.
/// A vanilla run opens with <c>Datafixer Bootstrap</c> and never mentions either.
/// </para>
/// </summary>
public static class MinecraftLaunchLog
{
	// [15:54:04] [main/INFO]: Loading Minecraft 26.2 with Fabric Loader 0.19.5
	private static readonly Regex LoadingLine = new(
		@"Loading Minecraft (?<game>\S+) with Fabric Loader (?<loader>\S+)",
		RegexOptions.Compiled | RegexOptions.IgnoreCase);

	// [15:54:04] [main/INFO]: Loading 49 mods:
	private static readonly Regex ModCountLine = new(
		@"Loading (?<count>\d+) mods:", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	// A mod line in the block that follows, either top level or a nested library. The loader draws the nesting
	// as a tree, and the LAST child under a parent uses a different corner from the rest:
	//     - fabric-api 0.160.0+26.2
	//        |-- fabric-api-base 2.0.4+ece063239e
	//        \-- fabric-transitive-access-wideners-v1 8.1.4+67c847259e
	//
	// ⚠️ Missing the "\--" form is why this once read 48 of the 51 mods a real log declared — three mods, each
	// of them the last child of its parent, silently absent from the list. It was written off as an inherent
	// limitation of reading the log before anyone checked what the missing lines actually looked like.
	private static readonly Regex ModLine = new(
		@"^\s*(?:-|\|--|\\--)\s+(?<id>[A-Za-z0-9_\-.]+)\s", RegexOptions.Compiled);

	/// <summary>How much of the log to read. The answer is always in the opening lines.</summary>
	private const int LinesToRead = 400;

	/// <summary>Reads the log for the copy of Minecraft at <paramref name="root"/>.</summary>
	public static MinecraftLaunchOutcome ReadLatest(string root)
	{
		string path = MinecraftLayout.LatestLogPathFor(root);
		if (!File.Exists(path)) return new MinecraftLaunchOutcome { NoLog = true };

		try
		{
			var lines = new List<string>(LinesToRead);

			// Shared read, because the game may well be running and writing to it right now.
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
				FileShare.ReadWrite | FileShare.Delete);
			using var reader = new StreamReader(stream);

			while (lines.Count < LinesToRead && reader.ReadLine() is { } line) lines.Add(line);

			return Parse(lines);
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", "reading the game's latest.log", ex);
			return new MinecraftLaunchOutcome { NoLog = true };
		}
	}

	/// <summary>
	/// <see cref="ReadLatest"/>'s parsing half, so the rules can be tested against real log text without a
	/// Minecraft install.
	/// </summary>
	public static MinecraftLaunchOutcome Parse(IReadOnlyList<string> lines)
	{
		string game = "", loader = "";
		int count = -1;
		var ids = new List<string>();
		bool inModList = false;

		foreach (string line in lines)
		{
			Match loading = LoadingLine.Match(line);
			if (loading.Success)
			{
				game = loading.Groups["game"].Value;
				loader = loading.Groups["loader"].Value;
				continue;
			}

			Match counted = ModCountLine.Match(line);
			if (counted.Success)
			{
				count = int.Parse(counted.Groups["count"].Value);
				inModList = true;
				continue;
			}

			if (!inModList) continue;

			Match mod = ModLine.Match(line);
			if (mod.Success) ids.Add(mod.Groups["id"].Value);
			// The block ends at the first line that is not a mod entry — the next timestamped log line.
			else if (line.TrimStart().StartsWith("[", StringComparison.Ordinal)) inModList = false;
		}

		return new MinecraftLaunchOutcome
		{
			// Judged on the loading line rather than on the mod count: a Fabric run with no mods installed
			// still loaded Fabric, and that is a different situation from a vanilla launch.
			FabricLoaded  = game.Length > 0,
			GameVersion   = game,
			LoaderVersion = loader,
			ModCount      = count,
			ModIds        = ids
		};
	}
}
