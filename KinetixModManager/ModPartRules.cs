using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

	/// <summary>
	/// The file must be the one built for this copy's <em>store</em>, where the page tells them apart.
	///
	/// Stronger than <see cref="MatchGameBuild"/> and deliberately so: a build number can be read wrong — off a
	/// second copy of the game, or off an exe that will not answer — and when it is, the build match is worth a
	/// thousand points to entirely the wrong file. A GOG copy handed the Steam script extender is the exact
	/// failure that keeps being reported, and no amount of scoring prevents it while the store is only a
	/// tie-breaker. So the store disqualifies instead: the Steam build is never an answer for a GOG copy, whatever
	/// else it scores, and vice versa.
	///
	/// It applies only when the page actually distinguishes — some file on it names GOG. A page that offers one
	/// build for everybody (F4SE's does) is unaffected, so this can never turn a working download into "no file
	/// found" on a page that has nothing to choose between.
	/// </summary>
	public bool PlatformSpecific { get; init; }

	/// <summary>
	/// The game build from which this part stopped being needed, or <c>null</c> for a part that is always needed.
	///
	/// Mods outlive the reasons they were split up. SSE Engine Fixes needed its preloader for as long as SKSE had
	/// no way to load a plugin early; from Skyrim 1.7.99 SKSE does that itself, and the preloader is neither
	/// downloaded nor wanted. Stated as the build it changed at rather than as a flag, because both answers are
	/// live at once — the same page still serves 1.5.97 players, for whom the preloader is not optional at all:
	/// without it the plugin puts up an error box and closes the game.
	///
	/// An unreadable build counts as "still needed", which is the safe way to be wrong: the worst case is a
	/// finding about a file the player does not need, rather than a game that will not start.
	/// </summary>
	public string? SupersededFromGameBuild { get; init; }

	/// <summary>
	/// Every file this part puts in the game folder, relative to it. Only parts that land in the game folder have
	/// this, and it is what lets a part that has been superseded be cleared out again — the mod list has never
	/// heard of these files, so nothing else could name them.
	/// </summary>
	public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();

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
	/// Everything the preloader archive drops beside the game's exe: the proxy DLL itself and the two Intel TBB
	/// libraries it loads the allocator from. All three are the preloader, and all three are what is left behind
	/// once it is no longer used.
	/// </summary>
	private static readonly string[] EngineFixesPreloaderFiles = { EngineFixesPreloaderDll, "tbb.dll", "tbbmalloc.dll" };

	/// <summary>
	/// The Skyrim build from which SSE Engine Fixes stopped needing its own preloader.
	///
	/// This is the build the author's own installer switches on: below it the plugin still exports the old
	/// <c>Initialize()</c> entry point that the proxy DLL calls, at and above it the plugin is preloaded by SKSE
	/// directly. It is stated once, here, because both the "you are missing a part" check and the "this file is
	/// no longer used" one have to agree about where the line is.
	/// </summary>
	public const string EngineFixesPreloaderSupersededAt = "1.7.99";

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
					// And the store settles it outright. The build match alone was not enough: it is only as good
					// as the exe it was read from, and reading it from the wrong copy of a game somebody owns twice
					// is precisely how the Steam build kept arriving in GOG folders. See ModPart.PlatformSpecific.
					PlatformSpecific = true,
					Files          = new[] { "skse64_loader.exe" },
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
					// Harmless on a page that has never offered a GOG build — the rule only bites where some file
					// on the page names a store — and correct the day one appears.
					PlatformSpecific = true,
					Files          = new[] { "f4se_loader.exe" },
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
					// From Skyrim 1.7.99 the plugin is loaded early by SKSE itself (its 1.7 build exports SKSE's
					// own preload interface instead of the Initialize() entry point the proxy DLL called), so this
					// part is neither fetched nor wanted there. Below that build it is still mandatory: the plugin
					// puts up an error box and terminates the game when it finds it did not preload.
					SupersededFromGameBuild = EngineFixesPreloaderSupersededAt,
					Files       = EngineFixesPreloaderFiles,
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

	/// <summary>
	/// Whether <paramref name="part"/> is still needed on a copy running <paramref name="gameBuild"/>.
	///
	/// An unreadable build ("" or null) answers <c>true</c>. That is the conservative direction on purpose: a part
	/// wrongly called for costs a line in a report, while a part wrongly called obsolete costs a game that will
	/// not start.
	/// </summary>
	public static bool PartNeeded(ModPart part, string? gameBuild)
	{
		if (string.IsNullOrEmpty(part.SupersededFromGameBuild)) return true;
		if (string.IsNullOrEmpty(gameBuild)) return true;
		if (!Version.TryParse(gameBuild, out Version? game)) return true;
		if (!Version.TryParse(part.SupersededFromGameBuild, out Version? from)) return true;

		return game < from;
	}

	/// <summary>
	/// Whether <paramref name="part"/> has been left behind: it is a game-folder part that this copy's build no
	/// longer uses. The mirror of <see cref="PartNeeded"/>, and separate from it because "not needed" and "sitting
	/// in the game folder doing nothing" are different findings with different wording.
	/// </summary>
	public static bool PartSuperseded(ModPart part, string? gameBuild) =>
		!string.IsNullOrEmpty(part.SupersededFromGameBuild) && !PartNeeded(part, gameBuild);

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
		List<NexusFileInfo> usable = files.Where(Servable).ToList();

		// Does this page tell the stores apart at all? Only if some usable file names GOG, and only then can the
		// store be a requirement — otherwise a page with one build for everyone (F4SE's) would answer "no file
		// found" for a GOG owner, which is worse than the single build it has always correctly handed them.
		bool platformKnown = part.PlatformSpecific && platform != GamePlatform.Unknown;
		bool pageSplitsByPlatform = platformKnown && usable.Any(f => NamesGogBuild(f.FullHaystack));

		NexusFileInfo? best = null;
		int bestScore = int.MinValue;

		foreach (NexusFileInfo file in usable)
		{
			// Keywords read what the file calls itself; the build reads everything, because it lives in the prose.
			string named = file.NameHaystack;
			string full  = file.FullHaystack;

			if (part.Exclude.Any(x => named.Contains(x.ToLowerInvariant(), StringComparison.Ordinal))) continue;
			if (part.Include.Count > 0 &&
				!part.Include.Any(x => named.Contains(x.ToLowerInvariant(), StringComparison.Ordinal))) continue;

			// The wrong store's build is not a worse answer, it is not an answer: it is compiled against an exe
			// this copy does not have and will silently refuse to load. So it is dropped rather than scored, which
			// is what stops a build number read off the OTHER copy of a game from carrying it to the top.
			if (pageSplitsByPlatform && (platform == GamePlatform.Gog) != NamesGogBuild(full)) continue;

			int score = 0;

			if (part.MatchGameBuild && !string.IsNullOrEmpty(gameBuild))
			{
				// Both spellings: pages write the build as "1.6.1179" in prose and file names sometimes carry
				// "1_6_1179" instead.
				if (full.Contains(gameBuild, StringComparison.Ordinal) ||
					full.Contains(gameBuild.Replace('.', '_'), StringComparison.Ordinal))
					score += 1000;
			}

			// Where the page does not split by store the match is still worth something, as the tie-breaker it has
			// always been — it is what decides the week the game updates and no file names the new build yet.
			if (platformKnown && !pageSplitsByPlatform && (platform == GamePlatform.Gog) == NamesGogBuild(full))
				score += 100;

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
	/// Whether Nexus would actually serve this file. The API refuses to generate a download link for a retired
	/// one — the usual cause of "Nexus denied the download link" — so it can never be the answer however well it
	/// matches. Both spellings of retired: no category at all (SKSE's page has a stray "Placeholder" like that)
	/// and the explicit "ARCHIVED".
	/// </summary>
	private static bool Servable(NexusFileInfo file) =>
		!string.IsNullOrEmpty(file.CategoryName) &&
		!string.Equals(file.CategoryName, ArchivedCategory, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Whether a piece of a mod page's text says the file it describes is the GOG build.
	///
	/// A whole word, not a substring: "gog" inside another word means nothing, and this decides whether a file is
	/// disqualified outright. Public because the update path has to ask the same question of the same pages — a
	/// mod that ships one file per store must not be updated across stores either.
	/// </summary>
	public static bool NamesGogBuild(string text) =>
		!string.IsNullOrEmpty(text) && Regex.IsMatch(text, "\\bgog\\b", RegexOptions.IgnoreCase);

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
