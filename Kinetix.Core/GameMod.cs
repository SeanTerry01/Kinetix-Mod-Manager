using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// Represents a single installed mod, a mod group, a Nexus search result, or a pending update.
/// The <see cref="IsGroup"/>, <see cref="IsSubMod"/>, <see cref="IsSearchResult"/>, and
/// <see cref="IsUpdateResult"/> flags determine how the instance is displayed and handled.
/// </summary>
public class GameMod
{
	/// <summary>True when this entry represents a group header containing <see cref="SubMods"/>.</summary>
	public bool IsGroup { get; set; }

	/// <summary>True when a group's sub-mods are currently shown in the list.</summary>
	public bool IsExpanded { get; set; }

	/// <summary>Display name for a group header row.</summary>
	public string GroupName { get; set; } = "";

	/// <summary>Child mods contained within this group.</summary>
	public List<GameMod> SubMods { get; set; } = new List<GameMod>();

	/// <summary>True when this mod is a child entry inside a group.</summary>
	public bool IsSubMod { get; set; }

	/// <summary>Human-readable mod name from manifest.json.</summary>
	public string Name { get; set; } = "";

	/// <summary>Mod author from manifest.json.</summary>
	public string Author { get; set; } = "";

	/// <summary>Installed version string from manifest.json.</summary>
	public string Version { get; set; } = "";

	/// <summary>Short description from manifest.json.</summary>
	public string Description { get; set; } = "";

	/// <summary>UniqueID field from manifest.json (used for dependency matching).</summary>
	public string UniqueId { get; set; } = "";

	/// <summary>Nexus Mods numeric mod ID, or <c>null</c> if not mapped.</summary>
	public string? NexusID { get; set; }

	/// <summary>
	/// Modrinth project slug (or id), or <c>null</c> for a mod that does not come from Modrinth.
	///
	/// Kept apart from <see cref="NexusID"/> rather than sharing it. The two are not interchangeable: a Nexus
	/// id is a number that means something entirely different on the other service, and a value in the wrong
	/// field would send a download or an update check to the wrong catalogue asking about somebody else's mod.
	/// Which catalogue a result came from is <see cref="SourceId"/>, which the user's own choice now decides
	/// — see <see cref="ModSearchPlan"/>.
	/// </summary>
	public string? ModrinthId { get; set; }

	/// <summary>
	/// The mod's id as the user hears it in a search result, whichever catalogue it came from, or <c>""</c>
	/// when it has none.
	/// </summary>
	public string DisplayId =>
		!string.IsNullOrEmpty(NexusID) ? NexusID! :
		!string.IsNullOrEmpty(ModrinthId) ? ModrinthId! : "";

	/// <summary>GitHub repository path in 'owner/repo' format, or <c>null</c> if not mapped.</summary>
	public string? GitHubRepo { get; set; }

	/// <summary>
	/// The mod's page, wherever it lives — Nexus, Modrinth, ModDrop, CurseForge, a GitHub repository — or
	/// <c>null</c> when nothing has told the manager where that is.
	///
	/// <para>
	/// Kept because the manager is often told where a mod lives on a site it cannot otherwise do anything
	/// with, and used to throw that away. A Stardew mod hosted on ModDrop has its page and its newest version
	/// handed over by smapi.io; recognising only Nexus and GitHub URLs meant the mod was then reported as one
	/// the manager could not track. Opening a page is the one thing that works for every site there will ever
	/// be, so it is worth keeping even when nothing else about the site is known.
	/// </para>
	/// </summary>
	public string? PageUrl { get; set; }

	/// <summary>
	/// Which site this mod came from, as a <see cref="ModSources"/> id, or <c>""</c> when it is not known.
	///
	/// Set on a search result so the row can say where it came from — two catalogues will return the same mod,
	/// and two rows a listener cannot tell apart are worse than one.
	/// </summary>
	public string SourceId { get; set; } = "";

	/// <summary>
	/// Whether this row should say which site it came from.
	///
	/// Set only when a search actually asked more than one, because otherwise it is a phrase repeated on every
	/// one of a hundred rows to no purpose. When two catalogues did answer it is the opposite: it is the only
	/// thing separating two results that are otherwise read out identically.
	/// </summary>
	public bool ShowSource { get; set; }

	/// <summary>Absolute path to the mod's folder on disk.</summary>
	public string FolderPath { get; set; } = "";

	/// <summary>Dependencies declared in manifest.json, annotated with presence and version status.</summary>
	public List<ModDependency> Dependencies { get; set; } = new List<ModDependency>();

	/// <summary>User-assigned or auto-detected category (e.g. "Expansion", "Crafting").</summary>
	public string Category { get; set; } = "Uncategorized";

	/// <summary>Personal free-text note the user attached to this mod, spoken on selection; empty if none.</summary>
	public string Note { get; set; } = "";

	/// <summary>Latest version available on Nexus Mods, populated during update checks.</summary>
	public string? LatestVersion { get; set; }

	/// <summary>Whether the mod folder is in the active Mods directory (not the Disabled folder).</summary>
	public bool IsEnabled { get; set; } = true;

	/// <summary>True when this instance was populated from a Nexus search result.</summary>
	public bool IsSearchResult { get; set; }

	/// <summary>
	/// True when a Nexus search result turns out to be a mod already sitting in the user's mods folder.
	///
	/// Only ever set on a search result, and only by the Discovery tab, which is the one place that can see both
	/// the catalogue and what is installed. It is the most decisive thing a result can say — a mod you already
	/// have is one you can skip without reading any further — so <see cref="ToString"/> puts it before the
	/// download and endorsement counts.
	/// </summary>
	public bool IsInstalled { get; set; }

	/// <summary>
	/// How many times the mod has been downloaded, and how many people endorsed it, as reported by Nexus.
	/// <c>-1</c> means not known — nothing on disk records either, so they are only filled in for search
	/// results. Together they are the quickest read on whether a mod is widely used and well thought of, which
	/// otherwise means leaving the manager and opening the mod's page.
	/// </summary>
	public long Downloads { get; set; } = -1;

	/// <inheritdoc cref="Downloads"/>
	public long Endorsements { get; set; } = -1;

	/// <summary>
	/// When Nexus last saw a change to the mod, or <c>null</c> when it is not known — nothing on disk records
	/// it, so it is only filled in for search results.
	///
	/// <para>
	/// It answers the one question the download and endorsement counts cannot: a mod with a hundred thousand
	/// downloads and four years of silence behind it is a different proposition from the same mod updated last
	/// week, and until now the only way to tell them apart was to leave the manager and open the mod's page.
	/// </para>
	/// </summary>
	public DateTimeOffset? LastUpdated { get; set; }

	/// <summary>
	/// When the user last downloaded this mod's archive, or <c>null</c> when they have not — read from the
	/// archives sitting in the game's downloads folder, so it means "this file is on your computer right now"
	/// rather than "you fetched this once". Only ever set on a search result.
	///
	/// <para>
	/// Nexus says this on a mod page and it is one of the better reasons to leave the manager: knowing you have
	/// already pulled a mod down saves you fetching it twice, and points you at the copy you have. The API does
	/// not report it — it is built from the site's own download logs — so the manager answers from what it can
	/// see, which is its own downloads folder.
	/// </para>
	/// </summary>
	public DateTimeOffset? LastDownloaded { get; set; }

	/// <summary>True when this instance represents a pending update in the Updates tab.</summary>
	public bool IsUpdateResult { get; set; }

	/// <summary>
	/// True when the mod's manifest declares update keys but they are all blank or malformed (e.g.
	/// <c>"UpdateKeys": [""]</c> or <c>["Nexus: "]</c> with no id). The mod can't be version-checked, but the
	/// cause is an author's typo rather than a missing field — the Update Coverage report says which.
	/// </summary>
	public bool HasBlankUpdateKey { get; set; }

	/// <inheritdoc/>
	public override string ToString()
	{
		if (IsGroup)
		{
			string value = (IsExpanded ? "Expanded" : "Collapsed");
			return $"Mod Group: {GroupName}. Contains {SubMods.Count} mods. {value}. Press Right or Plus to expand, Left or Minus to collapse.";
		}
		string value2 = (IsSubMod ? "Sub-mod: " : "");
		string value3 = "";
		if (!IsSearchResult && !IsUpdateResult)
		{
			value3 = (IsEnabled ? "Enabled" : "Disabled") + ". ";
		}
		string value4 = "";
		bool flag = false;
		foreach (ModDependency dependency in Dependencies)
		{
			if (dependency.IsRequired && (!dependency.IsPresent || !dependency.IsEnabled))
			{
				flag = true;
				break;
			}
		}
		if (flag)
		{
			value4 = " Warning: Missing required dependencies.";
		}
		if (IsUpdateResult)
		{
			return $"{Name} by {Author}. Current: {Version}. Latest: {LatestVersion}.{value4}";
		}
		if (IsSearchResult)
		{
			// Whether you already have it comes first of all, because it is the one fact that can end your
			// interest in a row outright: a mod already in your mods folder needs no further thought. Only the
			// installed case is announced -- most results are not installed, and saying so on every one of a
			// hundred rows would bury the few that matter.
			//
			// Plain English rather than a language-file lookup: GameMod is compiled into the test project on its
			// own, without Loc, which is why nothing else in this method is localised either.
			string installed = IsInstalled ? "Installed. " : "";

			// Said in the second person, and before the counts, because it is about the user's own copy rather
			// than the mod: "you downloaded this 2 days ago" cannot be confused with "3,428 downloads" a moment
			// later, where a bare "Downloaded 2 days ago" very much could be. It joins "Installed" as the part of
			// the row that is about you, which is also the part that can end your interest in a result outright.
			string downloaded = LastDownloaded.HasValue
				? $"You downloaded this {DescribeAge(LastDownloaded.Value, DateTimeOffset.UtcNow)}. "
				: "";

			// Downloads and endorsements come BEFORE the summary on purpose. They are the two numbers that
			// decide whether a result is worth more of your time, and putting them first means you can move on
			// to the next result without sitting through a description you have already ruled out. It also keeps
			// them clear of the row's length limit, which the summary can push against on its own.
			string popularity = "";
			if (Downloads >= 0 || Endorsements >= 0)
			{
				string downloads = Downloads >= 0 ? $"{Downloads:N0} downloads" : "";
				string endorsements = Endorsements >= 0 ? $"{Endorsements:N0} endorsements" : "";
				string joined = string.Join(", ", new[] { downloads, endorsements }.Where(p => p.Length > 0));
				if (joined.Length > 0) popularity = joined + ". ";
			}

			// How long ago the mod was last touched, in the same relative wording Nexus uses on the mod page
			// itself ("3 weeks ago", "4 years ago"), and in the same part of the row as the counts: all three
			// are facts you can act on before hearing a word of the description. A mod with a large download
			// count and years of silence behind it is a different proposition from the same mod updated last
			// week, and this is the only thing in the row that separates them.
			string updated = LastUpdated.HasValue
				? $"Updated {DescribeAge(LastUpdated.Value, DateTimeOffset.UtcNow)}. "
				: "";

			// The summary here is however much of it Nexus returns, which for a long one is NOT all of it:
			// the API's summary field arrives already truncated at roughly 240 characters, often mid-word.
			// Nothing can recover the rest — the full text lives in the mod's description (Ctrl+Shift+I).
			// The id is read out because it is what a user quotes when asking for help or looking a mod up, and
			// it differs by catalogue: a Nexus number, a Modrinth slug. A result with neither says neither,
			// rather than reading "(ID: )" aloud at the start of every row.
			string id = DisplayId.Length > 0 ? $" (ID: {DisplayId})" : "";

			// Which catalogue answered, and only when more than one did -- see ShowSource. It sits with the
			// counts rather than at the front because it is context for a result rather than a reason to skip
			// one, but it comes before the description so that two similarly-named mods from different sites
			// are told apart without listening to the end of both.
			string from = "";
			if (ShowSource && SourceId.Length > 0)
				from = $"From {ModSources.Find(SourceId)?.DisplayName ?? SourceId}. ";

			return $"{Name}{id}. {installed}{downloaded}{from}{popularity}{updated}{Description}";
		}
		string noteSuffix = string.IsNullOrEmpty(Note) ? "" : $" Note: {Note}.";
		// Some mods genuinely carry no author or version — a Witcher 3 mod folder holds neither, because the
		// engine never needed them. Saying so is better than the placeholders that used to stand in: "1.0.0" is
		// a number the user could act on, and it is one nobody wrote down.
		string by  = string.IsNullOrWhiteSpace(Author)  ? "" : $" by {Author}";
		string ver = string.IsNullOrWhiteSpace(Version) ? "version unknown" : $"version {Version}";
		return $"{value2}{Name}{by}, {ver}. Category: {Category}. {value3}{value4}{noteSuffix}";
	}

	/// <summary>
	/// How long ago <paramref name="updated"/> was, in the same shape Nexus itself uses on a mod page: the unit
	/// grows with the gap, so a mod touched this morning reads "5 hours ago" and one left alone since 2021 reads
	/// "4 years ago", rather than both arriving as a date the listener has to do arithmetic on.
	/// </summary>
	/// <param name="updated">When the mod was last changed.</param>
	/// <param name="now">The moment to measure from — a parameter so the wording can be tested without waiting.</param>
	public static string DescribeAge(DateTimeOffset updated, DateTimeOffset now)
	{
		// A timestamp in the future means the two clocks disagree, not that the mod was updated tomorrow. Say
		// the least wrong thing rather than "in -1 minutes".
		double seconds = (now - updated).TotalSeconds;
		if (seconds < 60) return "just now";

		// Each unit runs until the next one can say "1 <unit>" honestly, so nothing ever reads "60 minutes ago"
		// or "24 hours ago". Months and years are the usual approximations — 30 and 365 days — which is what a
		// relative age is for; anyone who needs the exact day wants the mod page, not a list row.
		double minutes = seconds / 60, hours = minutes / 60, days = hours / 24;
		if (minutes < 60) return Plural((int)minutes, "minute");
		if (hours   < 24) return Plural((int)hours,   "hour");
		if (days    <  7) return Plural((int)days,    "day");
		if (days    < 30) return Plural((int)(days / 7),   "week");
		if (days   < 365) return Plural((int)(days / 30),  "month");
		return Plural((int)(days / 365), "year");
	}

	/// <summary>"1 week ago" / "3 weeks ago" — never "1 weeks ago", which is the kind of thing that sounds like
	/// a machine talking when every result row in a long list says it.</summary>
	private static string Plural(int count, string unit) =>
		count == 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";
}
