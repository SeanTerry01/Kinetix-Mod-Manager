// What the wiki and walkthrough browsers hold: a result, a guide, and where the reader has been.
//
// Lifted out of Form1 by Phase 2 of the core split. These are plain data - what a row holds and how
// it reads aloud - so they belong beside the rules rather than inside the window that happens to
// show them today. While they were private to Form1 no second front end could name them at all.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

public class WikiNavigationState
{
	public string Title { get; set; } = "";
	public List<object> Results { get; set; } = new List<object>();
	public int SelectedIndex { get; set; }
}

public class WikiResult
{
	public string Title { get; set; } = "";
	public bool IsCategory { get; set; }
	public override string ToString()
	{
		string displayTitle = Title.StartsWith("Category:") ? Title.Substring(9) : Title;
		return IsCategory ? "[Category] " + displayTitle : displayTitle;
	}
}

public class WalkthroughGuide
{
	public string Title { get; set; } = "";
	public string Url { get; set; } = "";
	public override string ToString() => Title;
}

public class ModWikiLink
{
	/// <summary>Display name shown in the Mod Wikis dropdown.</summary>
	public string Title { get; set; } = "";
	/// <summary>Landing page navigated to when this wiki is selected.</summary>
	public string Url { get; set; } = "";
	/// <summary>
	/// MediaWiki <c>api.php</c> endpoint used for in-app Search and Categories. Empty means this wiki is
	/// "browse-only" — its host exposes no usable MediaWiki API, so it only opens in the embedded browser.
	/// </summary>
	public string ApiUrl { get; set; } = "";
	/// <summary>Article URL prefix (e.g. <c>https://x.wiki.gg/wiki/</c>) used to open a search/category result.</summary>
	public string ArticleBase { get; set; } = "";
	/// <summary>True for the base game wiki.</summary>
	public bool IsGameWiki { get; set; }
	/// <summary>
	/// MediaWiki <c>acprefix</c> used to scope the live category list to one game on multi-game wikis
	/// (e.g. "Skyrim" on UESP, "Fallout 4" on the Fallout wiki). Empty means list all categories.
	/// </summary>
	public string CategoryPrefix { get; set; } = "";
	public override string ToString() => Title;
}
