using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using DavyKager;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewMod = KinetixModManager.GameMod;

namespace KinetixModManager;

/// <summary>Wiki, Walkthrough, and embedded WebView2 functionality for <see cref="Form1"/>.</summary>
public partial class Form1
{
	private void PopulateWalkthroughs()
	{
		if (listWalkthroughs == null) return;
		listWalkthroughs.Items.Clear();
		
		var guides = _settings.ActiveGame switch
		{
			"StardewValley" => new[]
			{
				new WalkthroughGuide { Title = "Stardew Valley Wiki Getting Started Guide", Url = "https://stardewvalleywiki.com/Getting_Started" },
				new WalkthroughGuide { Title = "Stardew Valley Wiki Quests Guide", Url = "https://stardewvalleywiki.com/Quests" },
				new WalkthroughGuide { Title = "Stardew Valley Wiki Community Center Guide", Url = "https://stardewvalleywiki.com/Community_Center" },
				new WalkthroughGuide { Title = "Stardew Valley Wiki Controls Guide", Url = "https://stardewvalleywiki.com/Controls" },
				new WalkthroughGuide { Title = "Stardew Valley Wiki Secrets Guide", Url = "https://stardewvalleywiki.com/Secrets" },
				new WalkthroughGuide { Title = "Stardew Access Mod & Keyboard Guide", Url = "https://github.com/khanshoaib3/stardew-access/blob/master/README.md" }
			},
			"SkyrimSE" => new[]
			{
				new WalkthroughGuide { Title = "UESP Skyrim First-Time Players Guide", Url = "https://en.uesp.net/wiki/Skyrim:First_Time_Players" },
				new WalkthroughGuide { Title = "UESP Skyrim Main Quest Walkthrough", Url = "https://en.uesp.net/wiki/Skyrim:Main_Quest" },
				new WalkthroughGuide { Title = "UESP Skyrim Quests Index & Guides", Url = "https://en.uesp.net/wiki/Skyrim:Quests" },
				new WalkthroughGuide { Title = "UESP Skyrim Factions & Guilds Guide", Url = "https://en.uesp.net/wiki/Skyrim:Factions" },
				new WalkthroughGuide { Title = "UESP Skyrim Player Houses Guide", Url = "https://en.uesp.net/wiki/Skyrim:Houses" },
				new WalkthroughGuide { Title = "UESP Skyrim Standing Stones Guide", Url = "https://en.uesp.net/wiki/Skyrim:Standing_Stones" }
			},
			"Fallout4" => new[]
			{
				new WalkthroughGuide { Title = "Fallout 4 Wiki Quests Guide", Url = "https://fallout.fandom.com/wiki/Fallout_4_quests" },
				new WalkthroughGuide { Title = "StrategyWiki Fallout 4 Complete Walkthrough", Url = "https://strategywiki.org/wiki/Fallout_4/Walkthrough" },
				new WalkthroughGuide { Title = "StrategyWiki Fallout 4 Getting Started Guide", Url = "https://strategywiki.org/wiki/Fallout_4" },
				new WalkthroughGuide { Title = "Fallout 4 Wiki Perks Guide", Url = "https://fallout.fandom.com/wiki/Fallout_4_perks" },
				new WalkthroughGuide { Title = "Fallout 4 Wiki Settlements Guide", Url = "https://fallout.fandom.com/wiki/Fallout_4_settlements" },
				new WalkthroughGuide { Title = "Fallout 4 Wiki Endings Guide", Url = "https://fallout.fandom.com/wiki/Fallout_4_endings" }
			},
			_ => Array.Empty<WalkthroughGuide>()
		};

		foreach (var guide in guides)
		{
			listWalkthroughs.Items.Add(guide);
		}

		if (listWalkthroughs.Items.Count > 0)
		{
			listWalkthroughs.SelectedIndex = 0;
		}
	}

	/// <summary>Builds a wiki.gg entry (standard MediaWiki layout: <c>/api.php</c> + <c>/wiki/</c> articles).</summary>
	private static ModWikiLink WikiGg(string title, string host) => new ModWikiLink
	{
		Title = title,
		Url = $"https://{host}/",
		ApiUrl = $"https://{host}/api.php",
		ArticleBase = $"https://{host}/wiki/"
	};

	/// <summary>Builds a Fandom entry (MediaWiki: <c>/api.php</c> + <c>/wiki/</c> articles). With no
	/// <paramref name="landingPath"/> it lands on the site root (which redirects to the wiki's main page).</summary>
	private static ModWikiLink Fandom(string title, string host, string landingPath = "") => new ModWikiLink
	{
		Title = title,
		Url = landingPath.Length > 0 ? $"https://{host}/wiki/{landingPath}" : $"https://{host}/",
		ApiUrl = $"https://{host}/api.php",
		ArticleBase = $"https://{host}/wiki/"
	};

	/// <summary>Builds a UESP entry (MediaWiki: <c>/w/api.php</c> + <c>/wiki/</c> articles).</summary>
	private static ModWikiLink Uesp(string title, string landingPath) => new ModWikiLink
	{
		Title = title,
		Url = $"https://en.uesp.net/wiki/{landingPath}",
		ApiUrl = "https://en.uesp.net/w/api.php",
		ArticleBase = "https://en.uesp.net/wiki/"
	};

	/// <summary>Builds a browse-only entry: opens in the embedded browser, but no in-app Search/Categories.</summary>
	private static ModWikiLink BrowseOnly(string title, string url) => new ModWikiLink { Title = title, Url = url };

	/// <summary>
	/// Fills the Mod Wikis dropdown for the active game. The first entry is always the base game wiki, so the
	/// dropdown acts as the master wiki selector: choosing an entry repoints Search, Categories, and the
	/// embedded view at that wiki. Entries built via <see cref="WikiGg"/>/<see cref="Fandom"/>/<see cref="Uesp"/>
	/// support full in-app search; <see cref="BrowseOnly"/> entries (hosts with no usable MediaWiki API) only
	/// open in the browser.
	/// </summary>
	private void PopulateModWikis()
	{
		if (cmbModWikis == null) return;

		ModWikiLink gameWiki = _settings.ActiveGame switch
		{
			"SkyrimSE" => new ModWikiLink { Title = "UESP Skyrim Wiki (main game wiki)", Url = "https://en.uesp.net/wiki/Skyrim:Skyrim", ApiUrl = "https://en.uesp.net/w/api.php", ArticleBase = "https://en.uesp.net/wiki/", IsGameWiki = true, CategoryPrefix = "Skyrim" },
			"Fallout4" => new ModWikiLink { Title = "Fallout Wiki (main game wiki)", Url = "https://fallout.fandom.com/wiki/Fallout_4", ApiUrl = "https://fallout.fandom.com/api.php", ArticleBase = "https://fallout.fandom.com/wiki/", IsGameWiki = true, CategoryPrefix = "Fallout 4" },
			_ => new ModWikiLink { Title = "Stardew Valley Wiki (main game wiki)", Url = "https://stardewvalleywiki.com/", ApiUrl = "https://stardewvalleywiki.com/mediawiki/api.php", ArticleBase = "https://stardewvalleywiki.com/", IsGameWiki = true }
		};

		ModWikiLink[] modWikis = _settings.ActiveGame switch
		{
			// Wikis for big Skyrim content mods (new lands, quests, areas). The Elder Scrolls Mods Wiki and
			// UESP host several of these, so the per-mod entries land on that mod's page while Search/Categories
			// run against the host wiki (which carries mod-specific categories like "Skyrim: Falskaar Quests").
			"SkyrimSE" => new[]
			{
				Fandom("Elder Scrolls Mods Wiki (new lands & quest mods hub)", "tes-mods.fandom.com"),
				Fandom("Legacy of the Dragonborn Wiki", "legacy-of-the-dragonborn.fandom.com"),
				Fandom("Enderal: Forgotten Stories Wiki", "enderal-forgotten-stories.fandom.com"),
				Uesp("Beyond Skyrim: Bruma", "Beyond_Skyrim:Bruma"),
				Fandom("Falskaar", "tes-mods.fandom.com", "Falskaar"),
				Uesp("Wyrmstooth", "Skyrim_Mod:Wyrmstooth"),
				Fandom("VIGILANT", "tes-mods.fandom.com", "VIGILANT"),
				Fandom("Moonpath to Elsweyr", "tes-mods.fandom.com", "Moonpath_to_Elsweyr")
			},
			// Wikis for big Fallout 4 content mods. The mod hosts (fallout.wiki, Sim Settlements 2) block or lack
			// an in-app API, so these are browse-only; the main Fallout wiki above stays fully searchable.
			"Fallout4" => new[]
			{
				BrowseOnly("Fallout 4 Mods Hub (The Fallout Wiki)", "https://fallout.wiki/wiki/Mod:Fallout_4_Mods"),
				BrowseOnly("Sim Settlements 2 Wiki", "https://wiki.simsettlements2.com/"),
				BrowseOnly("Fallout: London Wiki", "https://fallout.wiki/wiki/Mod:Fallout_London"),
				BrowseOnly("America Rising 2 — Legacy of the Enclave Wiki", "https://fallout.wiki/wiki/Mod:America_Rising_2_-_Legacy_of_the_Enclave")
			},
			// Dedicated wikis for Stardew Valley "world expansion" content mods (new towns, NPCs, quests). Most are
			// on wiki.gg (fully searchable in-app); Downtown Zuzu (Miraheze) and Stoffton are browse-only.
			_ => new[]
			{
				WikiGg("Expansion Mods Hub (Stardew Modding Wiki)", "stardewmodding.wiki.gg"),
				WikiGg("Stardew Valley Expanded (SVE) Wiki", "stardewvalleyexpanded.wiki.gg"),
				WikiGg("Ridgeside Village Wiki", "ridgesidevillage.wiki.gg"),
				WikiGg("East Scarp Wiki", "eastscarp.wiki.gg"),
				WikiGg("Sunberry Village Wiki", "sunberryvillage.wiki.gg"),
				WikiGg("Visit Mount Vapius Wiki", "visitmountvapius.wiki.gg"),
				BrowseOnly("Downtown Zuzu Wiki", "https://downtownzuzu.miraheze.org/wiki/Main_Page"),
				WikiGg("Return to Mineral Town Wiki", "returntomineraltown.wiki.gg"),
				// The dedicated stoffton.com wiki is frequently unreachable (connection times out), so point at the
				// reliable Nexus mod page instead; it carries the mod's full description and docs.
				BrowseOnly("Stoffton & Fostoria (Nexus page)", "https://www.nexusmods.com/stardewvalley/mods/11666")
			}
		};

		_suppressModWikiEvent = true;
		cmbModWikis.Items.Clear();
		cmbModWikis.Items.Add(gameWiki);
		foreach (var w in modWikis) cmbModWikis.Items.Add(w);
		cmbModWikis.SelectedIndex = 0;
		_activeWiki = gameWiki;
		txtWikiSearch.AccessibleName = Loc.T("wiki.searchAcc", gameWiki.Title);
		_suppressModWikiEvent = false;
		// NB: the caller loads the default wiki's categories via RefreshCategoriesForActiveWikiAsync once the wiki
		// tab (specifically splitWiki) exists. We must not trigger it here — during the initial UI build this runs
		// before splitWiki is created, and the category combo's handler would dereference a null splitWiki.
	}

	/// <summary>
	/// Switches the active wiki to <paramref name="link"/>: lands the embedded view on it and repoints Search and
	/// Categories. Browse-only wikis simply open in the view (Search/Categories become unavailable).
	/// </summary>
	private async Task OnModWikiSelected(ModWikiLink link)
	{
		_activeWiki = link;
		txtWikiSearch.AccessibleName = Loc.T("wiki.searchAcc", link.Title);

		// Clear any results from the previous wiki so they aren't mistaken for this one's.
		listWikiResults.Items.Clear();
		wikiNavStack.Clear();

		// Refresh categories first: it resets the category combo to index 0, which fires that combo's handler and
		// hides the split. We reveal the split afterwards so the wiki page (in Panel2's WebView) stays visible.
		await RefreshCategoriesForActiveWikiAsync();

		await EnsureWebViewsInitializedAsync();
		splitWiki.Visible = true;
		if (!string.IsNullOrEmpty(link.Url)) webViewWiki.CoreWebView2?.Navigate(link.Url);

		// Tell the user whether this wiki is fully searchable in-app or just opens in the view.
		bool browseOnly = string.IsNullOrEmpty(link.ApiUrl);
		Speak(browseOnly
			? Loc.T("wiki.openingBrowseOnly", link.Title)
			: Loc.T("wiki.opening", link.Title));
	}

	/// <summary>
	/// Ensures both WebView2 controls are initialised exactly once and returns a task that completes
	/// when they are ready. Safe to call (and await) from any navigation path — repeat calls return the
	/// same cached task. Initialisation does NOT navigate anywhere; callers navigate after awaiting this.
	/// </summary>
	private Task EnsureWebViewsInitializedAsync()
	{
		// On failure the core method nulls the cache so the next call retries.
		return _webViewInitTask ??= InitializeWebViewCoreAsync();
	}

	/// <summary>
	/// One-time WebView2 setup: creates the shared environment, ensures both cores, disables browser
	/// accelerator keys, and wires up in-page keyboard accessibility (F6 / Ctrl+Home).
	/// </summary>
    private async Task InitializeWebViewCoreAsync()
    {
        try
        {
            string webViewDataPath = Path.Combine(dataBasePath, "WebView2Data");
            if (!Directory.Exists(webViewDataPath)) Directory.CreateDirectory(webViewDataPath);

            var env = await CoreWebView2Environment.CreateAsync(null, webViewDataPath);

            await webViewWiki.EnsureCoreWebView2Async(env);
            if (webViewWalkthrough != null)
            {
                await webViewWalkthrough.EnsureCoreWebView2Async(env);
            }

            // Just disable browser-specific shortcuts (like Ctrl+P for print)
            if (webViewWiki.CoreWebView2 != null)
            {
                webViewWiki.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                await AttachWebViewAccessibility(webViewWiki, isWiki: true);
            }
            if (webViewWalkthrough != null && webViewWalkthrough.CoreWebView2 != null)
            {
                webViewWalkthrough.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                await AttachWebViewAccessibility(webViewWalkthrough, isWiki: false);
            }
        }
        catch (Exception ex)
        {
            _webViewInitTask = null; // allow a later navigation to retry initialisation
            LogError("Wiki", "WebView2 Init Error: " + ex.Message);
        }
    }

    /// <summary>
    /// Initialises the WebView controls and then loads their initial pages (wiki home page and the
    /// currently selected walkthrough, if any). Used at startup; navigation waits for init to finish.
    /// </summary>
    private async Task InitializeAndLoadInitialPagesAsync()
    {
        await EnsureWebViewsInitializedAsync();

        webViewWiki.CoreWebView2?.Navigate(CurrentWikiBaseUrl);

        if (webViewWalkthrough?.CoreWebView2 != null &&
            listWalkthroughs?.SelectedItem is WalkthroughGuide guide)
        {
            webViewWalkthrough.CoreWebView2.Navigate(guide.Url);
        }
    }

    /// <summary>
    /// Wires up in-page keyboard accessibility for a WebView2 control. WebView2 hosts its content
    /// in a separate browser process, so once focus is inside the page, key events never reach the
    /// host app's message pump (IMessageFilter / ProcessCmdKey) — that is why F6 could move focus
    /// <em>into</em> the view but not back out. We instead capture the keys in JavaScript and post a
    /// message back to the host:
    ///   * F6        -> notify the host to cycle focus out of the WebView (to the tab headers).
    ///   * Ctrl+Home -> scroll to the top AND move DOM focus to the top of the document, so that a
    ///                  subsequent Tab / Shift+Tab restarts from the top instead of the last element.
    /// Idempotent: the script is only injected and the event only subscribed once per control.
    /// </summary>
    private async Task AttachWebViewAccessibility(WebView2 view, bool isWiki)
    {
        if (view.CoreWebView2 == null) return;
        if (isWiki && _wikiAccessibilityAttached) return;
        if (!isWiki && _walkthroughAccessibilityAttached) return;

        // In-page keyboard model (WebView2 content runs out-of-process, so this can't live host-side):
        //   F6        -> tell the host to cycle focus out of the view (to the tab headers).
        //   Ctrl+Home -> scroll to top AND focus the page's main heading, so the screen reader announces
        //                something meaningful and Tab continues forward from the top.
        //   Ctrl+End  -> scroll to bottom AND focus the last focusable element, so Tab exits to the tabs.
        //   Tab/Shift+Tab at the actual first/last focusable element -> tell the host to move focus to the
        //                results list (backward) or tab headers (forward), instead of letting Chromium wrap
        //                focus around to the other end of the page. We compute the real focusable list so
        //                this works no matter how focus arrived at the boundary.
        const string script = @"
(function () {
    function isVisible(el) {
        return el.offsetWidth > 0 || el.offsetHeight > 0 || el.getClientRects().length > 0;
    }
    function focusables() {
        var nodes = document.querySelectorAll('a[href], button, input, select, textarea, [tabindex]');
        return Array.prototype.slice.call(nodes).filter(function (el) {
            return el.tabIndex >= 0 && !el.disabled && isVisible(el);
        });
    }
    function focusTop() {
        window.scrollTo(0, 0);
        var t = document.querySelector('#firstHeading, h1, main, [role=main]') || document.body;
        t.setAttribute('tabindex', '-1');
        t.focus();
    }
    function focusBottom() {
        window.scrollTo(0, document.body.scrollHeight);
        var list = focusables();
        if (list.length) {
            list[list.length - 1].focus();
        } else {
            document.body.setAttribute('tabindex', '-1');
            document.body.focus();
        }
    }
    document.addEventListener('keydown', function (e) {
        if (e.key === 'F6') {
            e.preventDefault();
            window.chrome.webview.postMessage('cyclefocus');
            return;
        }
        if (e.ctrlKey && (e.key === 'Home' || e.keyCode === 36)) {
            e.preventDefault();
            focusTop();
            return;
        }
        if (e.ctrlKey && (e.key === 'End' || e.keyCode === 35)) {
            e.preventDefault();
            focusBottom();
            return;
        }
        if (e.key === 'Tab') {
            var list = focusables();
            var idx = list.indexOf(document.activeElement);
            if (e.shiftKey) {
                // At (or before) the first focusable element -> leave backward to the results list.
                if (idx <= 0) {
                    e.preventDefault();
                    window.chrome.webview.postMessage('focusresults');
                }
            } else {
                // At the last focusable element -> leave forward to the tab headers.
                if (idx === list.length - 1) {
                    e.preventDefault();
                    window.chrome.webview.postMessage('cyclefocus');
                }
            }
        }
    }, true);
})();";

        await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);

        view.CoreWebView2.WebMessageReceived += (s, e) =>
        {
            string message;
            try { message = e.TryGetWebMessageAsString(); }
            catch { return; }

            if (message == "cyclefocus")
            {
                // Leaving the WebView always lands on the tab headers (list -> view -> headers cycle).
                BeginInvoke(new Action(() =>
                {
                    mainTabs.Focus();
                    Speak(Loc.T("common.tabSuffix", mainTabs.SelectedTab?.Text ?? ""));
                }));
            }
            else if (message == "focusresults")
            {
                // Shift+Tab from the top of the page goes back to this view's results/guides list.
                BeginInvoke(new Action(() =>
                {
                    if (isWiki)
                    {
                        listWikiResults.Focus();
                        Speak(Loc.T("common.wikiResultsList"));
                    }
                    else
                    {
                        listWalkthroughs.Focus();
                        Speak(Loc.T("common.walkthroughGuidesList"));
                    }
                }));
            }
        };

        if (isWiki) _wikiAccessibilityAttached = true;
        else _walkthroughAccessibilityAttached = true;
    }

	/// <summary>
	/// MediaWiki <c>api.php</c> endpoint that Search and Categories query. Reflects the wiki currently selected
	/// in the Mod Wikis dropdown; empty for a browse-only wiki. Falls back to the game default before the
	/// dropdown is populated.
	/// </summary>
	private string CurrentWikiApiUrl => _activeWiki != null
		? _activeWiki.ApiUrl
		: _settings.ActiveGame switch
		{
			"SkyrimSE" => "https://en.uesp.net/w/api.php",
			"Fallout4" => "https://fallout.fandom.com/api.php",
			_ => "https://stardewvalleywiki.com/mediawiki/api.php"
		};

	/// <summary>Article URL prefix for the active wiki; results are opened as <c>base + Title</c>.</summary>
	private string CurrentWikiBaseUrl => _activeWiki != null
		? _activeWiki.ArticleBase
		: _settings.ActiveGame switch
		{
			"SkyrimSE" => "https://en.uesp.net/wiki/",
			"Fallout4" => "https://fallout.fandom.com/wiki/",
			_ => "https://stardewvalleywiki.com/"
		};

	/// <summary>True when the active wiki is the base game wiki (curated categories + game-specific prefixes).</summary>
	private bool ActiveWikiIsGameWiki => _activeWiki == null || _activeWiki.IsGameWiki;

	/// <summary>Populates the Categories dropdown with the base game wiki's curated category list.</summary>
	private void RefreshWikiCategories()
	{
		cmbWikiCategories.Items.Clear();
		cmbWikiCategories.Items.Add("Select Category");

		string[] categories = _settings.ActiveGame switch
		{
			"SkyrimSE" => new string[] { "Quests", "Items", "Skills", "NPCs", "Magic", "Factions", "Locations" },
			"Fallout4" => new string[] { "Quests", "Weapons", "Perks", "Characters", "Factions", "Locations", "Items" },
			_ => new string[] { "Villagers", "Crops", "Fish", "Artisan Goods", "Cooking", "Mining", "Animals" }
		};
		cmbWikiCategories.Items.AddRange(categories);
		cmbWikiCategories.SelectedIndex = 0;
	}

	/// <summary>
	/// Rebuilds the Categories dropdown for the active wiki: live categories fetched from the MediaWiki API for
	/// every searchable wiki (each game wiki and mod wiki shows its own categories), or a clear "no categories"
	/// placeholder for browse-only wikis. Multi-game wikis (UESP, Fallout) are scoped via their
	/// <see cref="ModWikiLink.CategoryPrefix"/> so only the active game's categories appear.
	/// </summary>
	private async Task RefreshCategoriesForActiveWikiAsync()
	{
		if (cmbWikiCategories == null) return;
		cmbWikiCategories.Items.Clear();

		// Browse-only wiki: no API to query, so say so instead of showing an empty "Select Category".
		if (string.IsNullOrEmpty(CurrentWikiApiUrl))
		{
			cmbWikiCategories.Items.Add(Loc.T("wiki.noCategories"));
			cmbWikiCategories.SelectedIndex = 0;
			return;
		}

		cmbWikiCategories.Items.Add("Select Category");

		try
		{
			// allcategories is alphabetical, so pull a large batch with page counts and keep the most populated
			// content categories — that surfaces the wiki's real sections (Characters, Locations, ...) rather
			// than its alphabetical first entries. On multi-game wikis, acprefix scopes to the active game.
			string prefix = _activeWiki?.CategoryPrefix ?? "";
			string prefixParam = prefix.Length > 0 ? $"&acprefix={Uri.EscapeDataString(prefix)}" : "";
			string url = $"{CurrentWikiApiUrl}?action=query&list=allcategories&aclimit=500&acprop=size{prefixParam}&format=json&formatversion=2";
			string json = await NexusService.HttpClient.GetStringAsync(url);
			JArray cats = (JObject.Parse(json)["query"]?["allcategories"] as JArray) ?? new JArray();

			object[] top = cats
				.Select(c => new { Name = c["category"]?.ToString() ?? "", Pages = (int?)c["pages"] ?? 0 })
				.Where(c => c.Pages > 0 && c.Name.Length > 0 && !IsMaintenanceCategory(c.Name))
				.OrderByDescending(c => c.Pages)
				.Take(30)
				.Select(c => (object)c.Name)
				.ToArray();

			cmbWikiCategories.Items.AddRange(top);
		}
		catch (Exception ex)
		{
			LogError("Wiki", "Category list error: " + ex.Message);
		}
		cmbWikiCategories.SelectedIndex = 0;
	}

	// Substrings that mark a wiki's housekeeping categories (templates, image requests, stubs, etc.) rather
	// than navigable game content. Matched case-insensitively against fetched category names.
	private static readonly string[] _maintenanceCategoryMarkers =
	{
		"template", "navbox", "infobox", "stub", "documentation", "image needed", "screenshot",
		"candidates for deletion", "disambig", "transclusion", "pages with", "maintenance", "cleanup",
		"redirect", "gallery", "files", "browse", "policy", "needed", "wiki", "icons", "category",
		"under construction", "candidates", "deletion", "talk pages", "bugs", "discussion",
		"script files", "dialogue files"
	};

	private static bool IsMaintenanceCategory(string name)
	{
		string lower = name.ToLowerInvariant();
		foreach (var marker in _maintenanceCategoryMarkers)
			if (lower.Contains(marker)) return true;
		return false;
	}

	/// <summary>
	/// Searches the active game's wiki for <paramref name="query"/> via the MediaWiki API
	/// and populates the wiki results list. Returns <c>false</c> if the query is empty.
	/// </summary>
	private async Task<bool> SearchWiki(string query)
	{
		if (string.IsNullOrEmpty(query)) return false;
		if (string.IsNullOrEmpty(CurrentWikiApiUrl))
		{
			Speak(Loc.T("wiki.cantSearch", _activeWiki?.Title ?? Loc.T("wiki.thisWiki")));
			return false;
		}
		splitWiki.Visible = true;
		SetStatus(Loc.T("wiki.searching"));
		try
		{
			string url = $"{CurrentWikiApiUrl}?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&format=json";
			string json = await NexusService.HttpClient.GetStringAsync(url);
			JObject data = JObject.Parse(json);
			JArray results = (data["query"]?["search"] as JArray) ?? new JArray();
			
			List<WikiResult> wikiResults = new List<WikiResult>();
			foreach (var item in results)
			{
				wikiResults.Add(new WikiResult { Title = item["title"]?.ToString() ?? "", IsCategory = false });
			}
			
			PushWikiState("Search: " + query, wikiResults.Cast<object>().ToList());
			UpdateWikiList(wikiResults);
			Speak(Loc.T("wiki.foundResults", wikiResults.Count));
		}
		catch (Exception ex)
		{
			LogError("Wiki", "Search Error: " + ex.Message);
			Speak(Loc.T("wiki.searchFailed"));
		}
		return true;
	}

	/// <summary>
	/// Loads all pages and sub-categories in a wiki category via the MediaWiki API
	/// and updates the wiki results list.
	/// </summary>
	private async Task<bool> LoadWikiCategory(string category)
	{
		SetStatus(Loc.T("wiki.loadingCategory", category), speak: false);
		try
		{
			// The curated game-wiki categories use friendly names that must be mapped to the wiki's real category
			// titles. Categories fetched live from a mod wiki are already exact titles, so skip the mapping there.
			string mappedCategory = category;
			if (ActiveWikiIsGameWiki && _settings.ActiveGame == "SkyrimSE")
			{
				if (category == "Quests" || category == "Items" || category == "Skills" || category == "NPCs" || category == "Magic" || category == "Factions" || category == "Locations")
					mappedCategory = "Skyrim-" + category;
			}
			else if (ActiveWikiIsGameWiki && _settings.ActiveGame == "Fallout4")
			{
				if (category == "Quests" || category == "Weapons" || category == "Perks" || category == "Characters" || category == "Factions" || category == "Locations" || category == "Items")
					mappedCategory = "Fallout 4 " + category.ToLower();
			}

			string catTitle = mappedCategory.StartsWith("Category:") ? mappedCategory : "Category:" + mappedCategory;
			string url = $"{CurrentWikiApiUrl}?action=query&list=categorymembers&cmtitle={Uri.EscapeDataString(catTitle)}&cmlimit=500&format=json";
			string json = await NexusService.HttpClient.GetStringAsync(url);
			JObject data = JObject.Parse(json);
			JArray members = (data["query"]?["categorymembers"] as JArray) ?? new JArray();

			List<WikiResult> wikiResults = new List<WikiResult>();
			foreach (var item in members)
			{
				string title = item["title"]?.ToString() ?? "";
				bool isCat = title.StartsWith("Category:");
				wikiResults.Add(new WikiResult { Title = title, IsCategory = isCat });
			}
			
			PushWikiState(category, wikiResults.Cast<object>().ToList());
			UpdateWikiList(wikiResults);
			Speak(Loc.T("wiki.categoryLoaded", category, wikiResults.Count));
		}
		catch (Exception ex)
		{
			LogError("Wiki", "Category Error: " + ex.Message);
			Speak(Loc.T("wiki.categoryFailed"));
		}
		return true;
	}

	/// <summary>
	/// Navigates the embedded WebView2 to the wiki article for <paramref name="title"/>.
	/// </summary>
	private async Task<bool> LoadWikiPage(string title)
	{
		await EnsureWebViewsInitializedAsync();
		string url = CurrentWikiBaseUrl + Uri.EscapeDataString(title.Replace(" ", "_"));
		webViewWiki.CoreWebView2?.Navigate(url);
		Speak(Loc.T("wiki.loadingPage", title));
		return true;
	}

	/// <summary>
	/// Pushes the current wiki results onto the back-navigation stack before loading a new page.
	/// </summary>
	private void PushWikiState(string title, List<object> results)
	{
		if (wikiNavStack.Count > 0)
		{
			wikiNavStack.Peek().SelectedIndex = listWikiResults.SelectedIndex;
		}
		wikiNavStack.Push(new WikiNavigationState { Title = title, Results = results });
	}

	/// <summary>Pops the wiki navigation stack and restores the previous results list.</summary>
	private void NavigateBackWiki()
	{
		if (wikiNavStack.Count > 1)
		{
			wikiNavStack.Pop();
			var state = wikiNavStack.Peek();
			UpdateWikiList(state.Results.Cast<WikiResult>().ToList());
			if (state.SelectedIndex >= 0 && state.SelectedIndex < listWikiResults.Items.Count)
				listWikiResults.SelectedIndex = state.SelectedIndex;
			Speak(Loc.T("wiki.backTo", state.Title));
		}
		else Speak(Loc.T("wiki.atTopLevel"));
	}

	/// <summary>Populates <c>listWikiResults</c> with a set of <see cref="WikiResult"/> items.</summary>
	private void UpdateWikiList(List<WikiResult> results)
	{
		listWikiResults.BeginUpdate();
		listWikiResults.Items.Clear();
		foreach (var res in results) listWikiResults.Items.Add(res);
		listWikiResults.EndUpdate();
		if (listWikiResults.Items.Count > 0) listWikiResults.SelectedIndex = 0;
	}

	private async void Wiki_SelectedIndexChanged(object? sender, EventArgs e)
	{
		if (listWikiResults.SelectedItem is WikiResult res && listWikiResults.Focused)
		{
			await Task.Delay(100);
			if (!listWikiResults.Focused) return;
			Speak(Loc.T("common.position", listWikiResults.SelectedIndex + 1, listWikiResults.Items.Count));
		}
	}
}
