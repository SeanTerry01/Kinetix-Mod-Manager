using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>Where a downloaded part has to end up for the game to load it.</summary>
public enum PartDestination
{
	/// <summary>An ordinary mod: into the mods folder, managed like any other.</summary>
	ModsFolder,

	/// <summary>Loose into the game folder beside the executable — a script extender, or a preloader DLL.</summary>
	GameRoot
}

/// <summary>
/// One file from a mod page's Files tab, reduced to the fields that decide which one is wanted. Kept apart from
/// the JSON so the picking rules can be tested against recorded file lists without any HTTP.
/// </summary>
public sealed class NexusFileInfo
{
	public string FileId { get; init; } = "";
	/// <summary>The name the author gave the file on the page, e.g. "SKSE64 GOG build 2.2.6".</summary>
	public string Name { get; init; } = "";
	/// <summary>The archive's own name, e.g. <c>skse64_2_02_06.7z</c>.</summary>
	public string FileName { get; init; } = "";
	public string Description { get; init; } = "";
	public string Version { get; init; } = "";
	/// <summary>MAIN, OPTIONAL, MISCELLANEOUS, OLD_VERSION — or empty for an <b>archived</b> file.</summary>
	public string CategoryName { get; init; } = "";
	public long UploadedTimestamp { get; init; }
	public bool IsPrimary { get; init; }

	/// <summary>
	/// What a file calls ITSELF — its display name and its archive name, lowercased. This is what the
	/// include/exclude keywords search, and deliberately not the description.
	///
	/// A description talks about <em>other</em> files as much as its own. SSE Engine Fixes' main plugin says
	/// "Requires the Preloader found below to be installed to load properly", so a rule looking for "preloader"
	/// anywhere would match the main plugin and hand back the same file for both halves of a two-part install —
	/// which is the precise failure this table exists to prevent, arrived at from the other direction.
	/// </summary>
	internal string NameHaystack => $"{Name} {FileName}".ToLowerInvariant();

	/// <summary>
	/// Everything, including the description, lowercased. Only the game-build match reads this — and it has to,
	/// because the build is stated <em>only</em> in the description ("Compatible with Skyrim Special Edition
	/// 1.6.1179 from GOG.com"); the Steam and GOG files are otherwise named almost identically.
	/// </summary>
	internal string FullHaystack => $"{Name} {FileName} {Description} {Version}".ToLowerInvariant();
}

/// <summary>
/// One downloadable part of a mod that ships as more than one file, or whose single file has to be chosen from
/// several builds.
/// </summary>
public sealed class ModPart
{
	/// <summary>How this part is named to the user, e.g. "Part 2 — the preloader".</summary>
	public required string Name { get; init; }

	public required PartDestination Destination { get; init; }

	/// <summary>
	/// A file, relative to the game folder, whose presence means this part is installed. This is how a part that
	/// was never installed gets noticed — the preloader is a loose DLL in the game folder, so nothing in the mod
	/// list would ever mention it.
	/// </summary>
	public string? DetectFile { get; init; }

	/// <summary>Or: text in an installed mod's name that means this part is present.</summary>
	public string? DetectModName { get; init; }

	/// <summary>
	/// Text that disqualifies a mod from satisfying <see cref="DetectModName"/>.
	///
	/// Needed because the parts of one mod are named after that mod, so a loose name match finds its siblings.
	/// "Engine Fixes" matches the mod called "Engine Fixes - SKSE64 Preloader" — which is Part 2 — so Part 1
	/// counted as installed on a machine that had only the preloader, and the check reported nothing while the
	/// plugin the preloader exists to load was absent entirely.
	/// </summary>
	public string? DetectModNameExcluding { get; init; }

	/// <summary>
	/// Prefer the file built for the exact game build this copy is running.
	///
	/// This is what tells the Steam and GOG builds of a script extender apart, and it is a better question than
	/// "which store?" because it is the question that actually matters: SKSE refuses to load against a build it
	/// was not compiled for, whoever sold the game. Reading the number off the user's own exe also means the
	/// answer keeps being right after Bethesda patches, with nothing to update here.
	/// </summary>
	public bool MatchGameBuild { get; init; }

	/// <summary>Text that must appear somewhere in a file's name, filename or description for it to qualify.</summary>
	public IReadOnlyList<string> Include { get; init; } = Array.Empty<string>();

	/// <summary>Text that disqualifies a file outright — how Part 1 is stopped from ever matching Part 2.</summary>
	public IReadOnlyList<string> Exclude { get; init; } = Array.Empty<string>();

	/// <summary>The Nexus file category this part is normally published under, preferred but not required.</summary>
	public string? Category { get; init; }
}

/// <summary>A mod the manager knows the shape of: which page it is on, and what has to come off it.</summary>
public sealed class KnownMod
{
	public required string GameId { get; init; }
	public required string NexusModId { get; init; }
	public required string DisplayName { get; init; }
	public required IReadOnlyList<ModPart> Parts { get; init; }
}

/// <summary>
/// What has to be downloaded from a mod page, and how to recognise it among the files on that page.
///
/// Two problems live here, and they are the same problem. A mod page can offer one file per game build — SKSE
/// does, and installing the wrong one means it silently never loads. And a mod can need two separate downloads
/// from one page — SSE Engine Fixes does, and its second part is a loose DLL that goes in the game folder rather
/// than the mods folder, which newcomers routinely never learn about because nothing tells them.
///
/// Both were handled before by guessing from the mod's <em>name</em> at the call site
/// (<c>mod.Name.Contains("Part 2")</c>), which only worked because the caller set that name up first. Stating it
/// as data instead means the rules can be tested against real recorded file lists, the answer is the same
/// wherever it is asked from, and a mod whose page needs the same treatment is a table entry rather than another
/// branch.
/// </summary>
public static class ModPartRules
{
	/// <summary>SSE Engine Fixes' preloader, which proxies d3dx9_42 to get itself loaded before the game starts.</summary>
	private const string EngineFixesPreloaderDll = "d3dx9_42.dll";

	/// <summary>
	/// The <c>category_name</c> Nexus gives a retired file. The API refuses to generate a download link for one,
	/// so it can never be the answer however well it matches.
	///
	/// Worth stating plainly because it is easy to get wrong: an archived file's category is the literal string
	/// "ARCHIVED", <em>not</em> an empty one. Checking only for empty — which is what the older selection code
	/// did — lets every archived file straight through, and SKSE's page alone carries a dozen.
	/// </summary>
	private const string ArchivedCategory = "ARCHIVED";

	public static readonly IReadOnlyList<KnownMod> All = new[]
	{
		new KnownMod
		{
			GameId      = GameProfiles.SkyrimSE,
			NexusModId  = "30379",
			DisplayName = "Skyrim Script Extender (SKSE64)",
			Parts = new[]
			{
				new ModPart
				{
					Name           = "SKSE64",
					Destination    = PartDestination.GameRoot,
					DetectFile     = "skse64_loader.exe",
					// The page carries one file per game build, and the build is stated only in each file's
					// DESCRIPTION — "Compatible with Skyrim Special Edition 1.6.1170 from Steam" against
					// "… 1.6.1179 from GOG.com". Both are MAIN files with near-identical names, so the build is
					// the only thing that actually separates them.
					//
					// This is the tester's bug, exactly: the Steam file is the one flagged is_primary, so picking
					// "the author's primary download" — which is what the manager used to do — hands every GOG
					// owner an SKSE built for a game they are not running. It installs without complaint and
					// then simply never loads.
					MatchGameBuild = true,
					// Skyrim VR is a different game whose files live on this page's family of pages; excluded so
					// a keyword tie can never land on one.
					Exclude        = new[] { "vr" }
				}
			}
		},
		new KnownMod
		{
			GameId      = GameProfiles.Fallout4,
			NexusModId  = "42147",
			DisplayName = "Fallout 4 Script Extender (F4SE)",
			Parts = new[]
			{
				new ModPart
				{
					Name        = "F4SE",
					Destination = PartDestination.GameRoot,
					DetectFile  = "f4se_loader.exe",
					// Same shape, different words: "Game version 1.11.221 required." F4SE has no GOG build, so
					// here the build match is doing the whole job of keeping an updated game off an old F4SE.
					MatchGameBuild = true,
					Exclude        = new[] { "vr" }
				}
			}
		},
		new KnownMod
		{
			GameId      = GameProfiles.SkyrimSE,
			NexusModId  = "17230",
			DisplayName = "SSE Engine Fixes",
			Parts = new[]
			{
				new ModPart
				{
					Name          = "Part 1 — the SKSE plugin",
					Destination   = PartDestination.ModsFolder,
					// The plugin the whole mod exists to run. Checked by the file it deploys as well as by name,
					// because the name is the weaker signal: every part of this mod is called "Engine Fixes"
					// something.
					DetectFile             = @"Data\SKSE\Plugins\EngineFixes.dll",
					DetectModName          = "Engine Fixes",
					DetectModNameExcluding = "Preloader",
					Category      = "MAIN",
					// The page also offers an All-In-One that bundles both halves. It is a perfectly good way to
					// install the mod by hand, but it is an OPTIONAL file, it lags the two MAIN files by a
					// revision, and it is the one the author flags is_primary — so without excluding it here, the
					// "main plugin" download would quietly become the bundle and the preloader would be fetched
					// twice. Someone who installed the All-In-One themselves is still fine: the health check
					// looks for the preloader DLL, which the bundle also places.
					Exclude       = new[] { "part 2", "part2", "preloader", "all-in-one", "all in one", "aio" }
				},
				new ModPart
				{
					Name        = "Part 2 — the preloader",
					// Not a mod: it has to sit loose beside the game's exe, which is exactly why people miss it.
					// The author's own description says so — "extract it to your main Skyrim folder manually" —
					// and a newcomer who never scrolls that far ends up with a plugin that cannot load.
					Destination = PartDestination.GameRoot,
					DetectFile  = EngineFixesPreloaderDll,
					Category    = "MAIN",
					// "Part 2" is how it was named for years and how people still refer to it; the current file
					// is called "Engine Fixes - SKSE64 Preloader" with no "Part 2" anywhere in it. Matching on
					// either means neither renaming breaks this.
					Include     = new[] { "preloader", "part 2", "part2", "tbb" },
					Exclude     = new[] { "all-in-one", "all in one", "aio" }
				}
			}
		}
	};

	/// <summary>The known mod at <paramref name="nexusModId"/> for <paramref name="gameId"/>, or <c>null</c>.</summary>
	public static KnownMod? Find(string? gameId, string? nexusModId)
	{
		if (string.IsNullOrEmpty(nexusModId)) return null;
		string id = GameProfiles.BaseId(gameId);
		return All.FirstOrDefault(m => m.GameId == id && m.NexusModId == nexusModId);
	}

	/// <summary>Every known mod for a game, in table order.</summary>
	public static IReadOnlyList<KnownMod> For(string? gameId)
	{
		string id = GameProfiles.BaseId(gameId);
		return All.Where(m => m.GameId == id).ToList();
	}

	/// <summary>
	/// Which file on the page satisfies <paramref name="part"/>, or <c>null</c> when none does.
	///
	/// Scored rather than filtered in sequence, because the signals disagree in practice: a file can name the
	/// right build but sit in the wrong category, and the build is the one that decides whether the thing loads.
	/// Scoring says which signal wins instead of leaving it to the order the filters happen to run in.
	/// </summary>
	/// <param name="gameBuild">The build read off the game's own exe, e.g. "1.6.1179". Null when unreadable.</param>
	public static NexusFileInfo? PickFile(
		IEnumerable<NexusFileInfo> files,
		ModPart part,
		string? gameBuild = null,
		GamePlatform platform = GamePlatform.Unknown)
	{
		NexusFileInfo? best = null;
		int bestScore = int.MinValue;

		foreach (NexusFileInfo file in files)
		{
			// The API refuses to generate a download link for a retired file — the usual cause of "Nexus denied
			// the download link". Both spellings of retired: no category at all (SKSE's page has a stray
			// "Placeholder" like that) and the explicit "ARCHIVED".
			if (string.IsNullOrEmpty(file.CategoryName)) continue;
			if (string.Equals(file.CategoryName, ArchivedCategory, StringComparison.OrdinalIgnoreCase)) continue;

			// Keywords read what the file calls itself; the build reads everything, because it lives in the prose.
			string named = file.NameHaystack;
			string full  = file.FullHaystack;

			if (part.Exclude.Any(x => named.Contains(x.ToLowerInvariant(), StringComparison.Ordinal))) continue;
			if (part.Include.Count > 0 &&
				!part.Include.Any(x => named.Contains(x.ToLowerInvariant(), StringComparison.Ordinal))) continue;

			int score = 0;

			if (part.MatchGameBuild && !string.IsNullOrEmpty(gameBuild))
			{
				// Both spellings: pages write the build as "1.6.1179" in prose and file names sometimes carry
				// "1_6_1179" instead.
				if (full.Contains(gameBuild, StringComparison.Ordinal) ||
					full.Contains(gameBuild.Replace('.', '_'), StringComparison.Ordinal))
					score += 1000;
			}

			if (part.MatchGameBuild && platform != GamePlatform.Unknown)
			{
				// Only a tie-breaker, and only when the build didn't settle it — which is the case that matters,
				// because it is what happens the week the game updates and no file names the new build yet.
				// "from GOG.com" is in the description, so this reads the full text too.
				bool mentionsGog = full.Contains("gog", StringComparison.Ordinal);
				if (platform == GamePlatform.Gog == mentionsGog) score += 100;
			}

			if (!string.IsNullOrEmpty(part.Category) &&
				string.Equals(file.CategoryName, part.Category, StringComparison.OrdinalIgnoreCase))
				score += 10;

			// The author's own "this is the download" flag, worth something but not much next to a build match.
			if (file.IsPrimary) score += 5;

			// An old build the author has kept up is a legitimate answer when it is the one that matches the
			// user's game, so OLD_VERSION is not excluded — only pushed below an equally good current file.
			if (string.Equals(file.CategoryName, "OLD_VERSION", StringComparison.OrdinalIgnoreCase)) score -= 3;

			if (score > bestScore ||
				(score == bestScore && best != null && file.UploadedTimestamp > best.UploadedTimestamp))
			{
				best = file;
				bestScore = score;
			}
		}

		return best;
	}

	/// <summary>
	/// Reads a Nexus <c>files.json</c> body into <see cref="NexusFileInfo"/>s.
	///
	/// Worth knowing: this endpoint is open to <b>every</b> account. Only <c>download_link.json</c> is premium —
	/// so even a free user's manager can work out precisely which file they need and send them straight to it,
	/// rather than leaving them to guess on the Files tab.
	/// </summary>
	public static List<NexusFileInfo> ParseFilesJson(string json)
	{
		var result = new List<NexusFileInfo>();
		if (string.IsNullOrWhiteSpace(json)) return result;

		JArray? files;
		try
		{
			JToken parsed = JToken.Parse(json);
			files = parsed as JArray ?? parsed["files"] as JArray;
		}
		catch { return result; }

		if (files == null) return result;

		foreach (JToken f in files)
		{
			result.Add(new NexusFileInfo
			{
				FileId            = f["file_id"]?.ToString() ?? "",
				Name              = f["name"]?.ToString() ?? "",
				FileName          = f["file_name"]?.ToString() ?? "",
				Description       = f["description"]?.ToString() ?? "",
				Version           = f["version"]?.ToString() ?? "",
				CategoryName      = f["category_name"]?.ToString() ?? "",
				UploadedTimestamp = (long?)f["uploaded_timestamp"] ?? 0L,
				IsPrimary         = (bool?)f["is_primary"] ?? false
			});
		}

		return result;
	}

	/// <summary>
	/// The page on Nexus showing exactly one file, for a user whose account cannot be handed a download link
	/// directly. Far better than the bare Files tab: the right file is already picked out, so there is no list to
	/// read through and no wrong build to take by mistake.
	/// </summary>
	public static string FilePageUrl(string nexusDomain, string modId, string fileId) =>
		$"https://www.nexusmods.com/{nexusDomain}/mods/{modId}?tab=files&file_id={fileId}";
}
