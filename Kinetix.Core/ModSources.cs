using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KinetixModManager;

/// <summary>
/// What a site can actually do for the manager.
///
/// <para>
/// Worth four separate flags rather than one "is supported", because the sites differ in exactly this way and
/// pretending otherwise is what makes a source chooser overpromise. GitHub can hand over a file and say what
/// the newest release is, and has no catalogue to search. ModDrop can answer "what version is this mod now"
/// — smapi.io asks it on the manager's behalf already — and has no public file endpoint. A site with nothing
/// but <see cref="Page"/> is still worth listing: opening a mod's page is most of what a user wants when the
/// manager cannot do the rest.
/// </para>
/// </summary>
[Flags]
public enum ModSourceAbilities
{
	None = 0,

	/// <summary>Has a catalogue that can be searched by name.</summary>
	Search = 1,

	/// <summary>Will hand the manager a file to install without the user going to a browser.</summary>
	Download = 2,

	/// <summary>Can say what the newest version of a known mod is.</summary>
	Updates = 4,

	/// <summary>Has a mod page that can be opened. Every site has this; it is the floor.</summary>
	Page = 8,
}

/// <summary>
/// What a site wants before it will answer, and how the user gets it.
///
/// <para>
/// The stored thing is always a key. What differs is how you come by one, and that is the whole of the
/// distinction here: CurseForge issues one to an approved application and you paste it in, while Nexus will
/// also let you sign in on its own site and hand the manager a key at the end — which is better, because the
/// manager then never sees the password and the user can revoke it without changing one. A site offering both
/// is <see cref="ApiKeyOrSignIn"/> so the screen can offer both and the user can take whichever they can
/// actually complete.
/// </para>
/// </summary>
public enum ModSourceCredential
{
	/// <summary>Nothing to set up. Search it and download from it.</summary>
	None,

	/// <summary>A key the user obtains themselves and types in.</summary>
	ApiKey,

	/// <summary>A key, or a sign-in on the site that ends with the manager being given one.</summary>
	ApiKeyOrSignIn,
}

/// <summary>Whether a source can be used right now, and if not, a sentence saying why.</summary>
public sealed record ModSourceStatus(bool Ready, string Reason)
{
	public static readonly ModSourceStatus Available = new(true, "");
}

/// <summary>
/// One place mods come from.
///
/// <para>
/// A data entry rather than a class per site, deliberately. The manager should end up knowing about many
/// sites — most of them only well enough to open a page — and each one that needs no code should cost no
/// code. A site that grows a real API later gains an <see cref="IModSource"/> implementation without this
/// entry changing.
/// </para>
/// </summary>
public sealed class ModSourceInfo
{
	/// <summary>Stable id, used in settings and in <see cref="GameMod.SourceId"/>. Never shown to the user.</summary>
	public required string Id { get; init; }

	/// <summary>What the user hears: "Nexus Mods", "CurseForge".</summary>
	public required string DisplayName { get; init; }

	/// <summary>The site's front page, for a "go and have a look" link.</summary>
	public required string HomeUrl { get; init; }

	/// <summary>What this site can do. See <see cref="ModSourceAbilities"/>.</summary>
	public ModSourceAbilities Abilities { get; init; } = ModSourceAbilities.Page;

	/// <summary>What it wants before it will answer. See <see cref="ModSourceCredential"/>.</summary>
	public ModSourceCredential Credential { get; init; } = ModSourceCredential.None;

	/// <summary>
	/// Where a user goes to get a key, or <c>""</c> for a site that needs none. Shown beside the source in
	/// the credentials screen, because "you need an API key" is not useful without saying where from.
	/// </summary>
	public string ApiKeyUrl { get; init; } = "";

	/// <summary>True when the user has to set something up before this site will answer.</summary>
	public bool NeedsCredential => Credential != ModSourceCredential.None;

	/// <summary>
	/// The games whose mods this site carries, by <see cref="GameProfiles"/> id. Empty means "any game" —
	/// true of GitHub, which is not a mod site at all but is where a great many mods are actually released.
	/// </summary>
	public IReadOnlyList<string> Games { get; init; } = Array.Empty<string>();

	/// <summary>
	/// Recognises one of this site's mod pages and, where the URL carries one, captures the mod's id in a
	/// group named <c>id</c>. Null for a site whose pages cannot be told apart from any other URL.
	/// </summary>
	public Regex? PageUrl { get; init; }

	/// <summary>True when this site carries <paramref name="gameId"/>'s mods.</summary>
	public bool CarriesGame(string? gameId) =>
		Games.Count == 0 || (gameId != null && Games.Contains(gameId, StringComparer.OrdinalIgnoreCase));

	public bool Can(ModSourceAbilities ability) => (Abilities & ability) == ability;
}

/// <summary>A mod's page, and which site it is on.</summary>
public sealed record ModPageLink(string SourceId, string Url, string Id)
{
	/// <summary>The site's name, or the id itself if the site is not one the manager knows.</summary>
	public string DisplayName => ModSources.Find(SourceId)?.DisplayName ?? SourceId;
}

/// <summary>
/// Every place the manager knows mods can come from.
///
/// <para>
/// The list is the feature. Adding a site is one entry here plus, only if it has an API worth talking to, an
/// <see cref="IModSource"/> for searching it — which is what makes "support as many sites as we can, over
/// time" a tractable promise rather than a rewrite each time.
/// </para>
///
/// <para>
/// The four games that are not Minecraft or Stardew Valley genuinely have one searchable catalogue between
/// them: Nexus. Bethesda.net, ModDB and LoversLab have no API of any kind, and Thunderstore — the obvious
/// home for a BepInEx game — has no Moonlight Peaks community. Offering those four a chooser with one entry
/// in it would be worse than not offering one, which is why <see cref="SearchableFor"/> is what the UI asks.
/// </para>
/// </summary>
public static class ModSources
{
	public const string Nexus = "nexus";
	public const string Modrinth = "modrinth";
	public const string GitHub = "github";
	public const string CurseForge = "curseforge";
	public const string ModDrop = "moddrop";

	private static readonly ModSourceInfo[] _known =
	{
		new()
		{
			Id = Nexus,
			DisplayName = "Nexus Mods",
			HomeUrl = "https://www.nexusmods.com/",
			Abilities = ModSourceAbilities.Search | ModSourceAbilities.Download | ModSourceAbilities.Updates | ModSourceAbilities.Page,
			Games = new[]
			{
				GameProfiles.StardewValley, GameProfiles.SkyrimSE, GameProfiles.Fallout4,
				GameProfiles.MoonlightPeaks, GameProfiles.Witcher3,
			},
			// Both, because the sign-in flow is built and waiting on Nexus approving the manager as an
			// application. Until it is, the typed key is the one that works. See NexusSso.
			Credential = ModSourceCredential.ApiKeyOrSignIn,
			ApiKeyUrl = "https://www.nexusmods.com/users/myaccount?tab=api",
			// Any game domain, not only stardewvalley — the update reader used to hard-code that one.
			PageUrl = new Regex(@"nexusmods\.com/(?:[^/]+/)?mods/(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
		},
		new()
		{
			Id = Modrinth,
			DisplayName = "Modrinth",
			HomeUrl = "https://modrinth.com/",
			Abilities = ModSourceAbilities.Search | ModSourceAbilities.Download | ModSourceAbilities.Updates | ModSourceAbilities.Page,
			Games = new[] { GameProfiles.Minecraft },
			PageUrl = new Regex(@"modrinth\.com/(?:mod|plugin|datapack|resourcepack)/(?<id>[A-Za-z0-9!@$()`.+,_""\-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
		},
		new()
		{
			// Not a mod site, and on the list because a great many mods are released here and nowhere else.
			// The manager has downloaded from GitHub since before this list existed — the Accessibility Suite,
			// SMAPI and BepInEx all arrive this way. What it cannot do is search it, because there is no
			// per-game catalogue to search: you install from a repository you can name.
			Id = GitHub,
			DisplayName = "GitHub",
			HomeUrl = "https://github.com/",
			Abilities = ModSourceAbilities.Download | ModSourceAbilities.Updates | ModSourceAbilities.Page,
			PageUrl = new Regex(@"github\.com/(?<id>[^/\s]+/[^/\s?#]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
		},
		new()
		{
			Id = CurseForge,
			DisplayName = "CurseForge",
			HomeUrl = "https://www.curseforge.com/",
			Abilities = ModSourceAbilities.Search | ModSourceAbilities.Download | ModSourceAbilities.Updates | ModSourceAbilities.Page,
			Games = new[] { GameProfiles.Minecraft, GameProfiles.StardewValley },
			Credential = ModSourceCredential.ApiKey,
			ApiKeyUrl = "https://console.curseforge.com/",
			PageUrl = new Regex(@"curseforge\.com/(?<game>[^/]+)/(?:[^/]+/)?(?<id>[^/\s?#]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
		},
		new()
		{
			// Versions only. smapi.io already asks ModDrop on the manager's behalf for any Stardew mod
			// carrying a ModDrop update key, so the newest version is known — there is simply no public file
			// endpoint, so installing one means opening its page.
			Id = ModDrop,
			DisplayName = "ModDrop",
			HomeUrl = "https://www.moddrop.com/",
			Abilities = ModSourceAbilities.Updates | ModSourceAbilities.Page,
			Games = new[] { GameProfiles.StardewValley },
			PageUrl = new Regex(@"moddrop\.com/(?:[^/]+/)?mods/(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
		},
	};

	/// <summary>Every site the manager knows about, in the order a list should show them.</summary>
	public static IReadOnlyList<ModSourceInfo> Known => _known;

	/// <summary>
	/// The sites that want setting up before they will answer — what the credentials screen lists.
	///
	/// Every site, not only the ones carrying the loaded game: a key is a thing about the user's account
	/// rather than about the game they happen to have open, and a screen that hid Nexus because you were
	/// playing Minecraft would be a screen you could not find your way back to.
	/// </summary>
	public static IReadOnlyList<ModSourceInfo> NeedingCredentials() =>
		_known.Where(s => s.NeedsCredential).ToList();

	/// <summary>One site by id, or null.</summary>
	public static ModSourceInfo? Find(string? id) =>
		id == null ? null : _known.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

	/// <summary>The sites carrying a game's mods, whatever they can do for it.</summary>
	public static IReadOnlyList<ModSourceInfo> ForGame(string? gameId) =>
		_known.Where(s => s.CarriesGame(gameId)).ToList();

	/// <summary>
	/// The sites whose catalogue can be searched for a game — which is what a source chooser should offer,
	/// rather than every site the manager can open a page on.
	/// </summary>
	public static IReadOnlyList<ModSourceInfo> SearchableFor(string? gameId) =>
		_known.Where(s => s.CarriesGame(gameId) && s.Can(ModSourceAbilities.Search)).ToList();

	/// <summary>
	/// The site a game's mods come from unless the user says otherwise — what was baked in before any of this
	/// was configurable, so an install that is upgraded behaves exactly as it did.
	/// </summary>
	public static string DefaultFor(string? gameId) =>
		GameProfiles.Find(gameId)?.ModSource == ModSource.Modrinth ? Modrinth : Nexus;

	/// <summary>
	/// The page to open for an installed mod, or null when nothing knows where it is.
	///
	/// A page the manager was explicitly told about wins over one built from an id, because it came from the
	/// mod's own author or from a database that tracks them — and because it is the only thing a mod on a
	/// site the manager cannot otherwise use will ever have.
	/// </summary>
	/// <param name="nexusGameDomain">
	/// The loaded game's Nexus domain, needed to build a Nexus URL from a bare mod id. A Nexus id means
	/// nothing without it: the same number is a different mod under every game.
	/// </param>
	public static string? PageUrlFor(GameMod mod, string? nexusGameDomain)
	{
		if (!string.IsNullOrWhiteSpace(mod.PageUrl)) return mod.PageUrl;

		if (!string.IsNullOrEmpty(mod.NexusID) && !string.IsNullOrWhiteSpace(nexusGameDomain))
			return $"https://www.nexusmods.com/{nexusGameDomain}/mods/{mod.NexusID}";

		if (!string.IsNullOrEmpty(mod.ModrinthId)) return $"https://modrinth.com/mod/{mod.ModrinthId}";
		if (!string.IsNullOrEmpty(mod.GitHubRepo)) return $"https://github.com/{mod.GitHubRepo}";

		return null;
	}

	/// <summary>
	/// Whether the manager needs a Nexus Premium account to update this mod by itself.
	///
	/// <para>
	/// Only a Nexus download does. Premium buys the right to fetch a file without a browser session, and it buys
	/// nothing anywhere else: Modrinth hands files to anybody, and a GitHub release is a public URL. This was once a
	/// single check on the whole Update All command, which told a Minecraft player — whose mods come from Modrinth
	/// and from their authors' GitHub releases — to buy an account from a site that hosts none of them.
	/// </para>
	/// </summary>
	public static bool NeedsNexusPremium(string? gameId, GameMod mod)
	{
		// Minecraft's catalogue is Modrinth, and a mod of any game that names a repository is fetched from there.
		if (GameProfiles.Find(gameId)?.IsMinecraft == true) return false;
		if (!string.IsNullOrEmpty(mod.GitHubRepo)) return false;
		if (!string.IsNullOrEmpty(mod.ModrinthId)) return false;

		return true;
	}

	/// <summary>
	/// Which known site a mod page belongs to, or null for a URL the manager does not recognise.
	///
	/// This is the floor of the whole feature. A Stardew mod living on ModDrop used to have its page handed
	/// to the manager by smapi.io and thrown away, because the only URLs recognised were Nexus's and
	/// GitHub's — so the mod was then reported to the user as one the manager could not track, moments after
	/// being told exactly where it was.
	/// </summary>
	public static ModPageLink? ParsePage(string? url)
	{
		if (string.IsNullOrWhiteSpace(url)) return null;

		foreach (ModSourceInfo source in _known)
		{
			if (source.PageUrl == null) continue;

			Match match = source.PageUrl.Match(url);
			if (match.Success) return new ModPageLink(source.Id, url, match.Groups["id"].Value);
		}

		return null;
	}
}
