// The smaller list rows that have no larger home: saves, tracked mods, suite items, documents, search history, languages.
//
// Lifted out of Form1 by Phase 2 of the core split. These are plain data - what a row holds and how
// it reads aloud - so they belong beside the rules rather than inside the window that happens to
// show them today. While they were private to Form1 no second front end could name them at all.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>One row in the save manager: the parsed save plus the plugins it needs that are no longer active.</summary>
public sealed class SaveRow
{
	public required SaveGame Save;
	public required List<string> MissingPlugins;
	public string Summary = "";
	public override string ToString() => Summary;
}

/// <summary>One row in the tracked-mods list.</summary>
public sealed class TrackedRow
{
	public required int ModId;
	public required bool Installed;
	public string Summary = "";
	public override string ToString() => Summary;
}

public class SuiteItem
{
	public string Name { get; }
	public bool IsInstalled { get; }
	public string Type { get; }
	public string Source { get; }
	/// <summary>Extra words spoken after "Installed" for entries that can say more about themselves
	/// (the script extender reports its version), or "" for the rest.</summary>
	public string Detail { get; }

	public SuiteItem(string name, bool isInstalled, string type, string source, string detail = "")
	{
		Name = name;
		IsInstalled = isInstalled;
		Type = type;
		Source = source;
		Detail = detail;
	}
}

/// <summary>One documentation source for the active game.</summary>
/// <param name="Title">Display name shown in the chooser (e.g. "Stardew Access").</param>
/// <param name="BundledFile">App-relative path to the shipped offline copy (e.g. "docs/stardew-access.md").</param>
/// <param name="GitHubRawUrls">Raw Markdown files combined into one doc on refresh; empty for Nexus-only mods.</param>
/// <param name="NexusModId">Nexus mod id whose live description is pulled on refresh; null for GitHub-sourced mods.</param>
public sealed record ModDocSource(
	string Title,
	string BundledFile,
	IReadOnlyList<string> GitHubRawUrls,
	string? NexusModId);

/// <summary>
/// Sentinel placeholder for the inline "Load more" row pinned to the bottom of the Discovery
/// results list. It is never a real search result: it is excluded from the spoken "X of Y"
/// position count, and pressing Enter on it loads the next page rather than opening a mod page.
/// Its <see cref="ToString"/> is what the screen reader reads when the row is focused.
/// </summary>
public sealed class DiscoveryLoadMoreRow
{
	public override string ToString() => Loc.T("discovery.loadMoreRow");
}

/// <summary>A scope shown in the dialog's "Show" combo: either every search, or one specific day.</summary>
public class HistoryScope
{
	public string Label { get; }
	public DateTime? Day { get; }   // null = all searches
	public HistoryScope(string label, DateTime? day) { Label = label; Day = day; }
	public override string ToString() => Label;
}

public class LanguageOption
{
	/// <summary>Nexus language name (e.g. "English"). Empty string means "Any language" (no filter).</summary>
	public string Name { get; set; } = "";
	/// <summary>Number of mods in this language for the active game; 0 hides the count.</summary>
	public int Count { get; set; }
	public override string ToString() =>
		string.IsNullOrEmpty(Name) ? Loc.T("ui.anyLanguage") : (Count > 0 ? $"{Name} ({Count})" : Name);
}

public enum GameNotInstalledChoice { Cancel, Locate, Purchase }
