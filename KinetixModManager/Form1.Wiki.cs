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
                    Speak((mainTabs.SelectedTab?.Text ?? "") + " Tab");
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
                        Speak("Wiki Results List");
                    }
                    else
                    {
                        listWalkthroughs.Focus();
                        Speak("Walkthrough Guides List");
                    }
                }));
            }
        };

        if (isWiki) _wikiAccessibilityAttached = true;
        else _walkthroughAccessibilityAttached = true;
    }

	/// <summary>
	/// Searches the Stardew Valley wiki for <paramref name="query"/> via the MediaWiki API
	/// and populates the wiki results list. Returns <c>false</c> if the query is empty.
	/// </summary>
	private string CurrentWikiApiUrl => _settings.ActiveGame switch
	{
		"SkyrimSE" => "https://en.uesp.net/w/api.php",
		"Fallout4" => "https://fallout.fandom.com/api.php",
		_ => "https://stardewvalleywiki.com/mediawiki/api.php"
	};

	private string CurrentWikiBaseUrl => _settings.ActiveGame switch
	{
		"SkyrimSE" => "https://en.uesp.net/wiki/",
		"Fallout4" => "https://fallout.fandom.com/wiki/",
		_ => "https://stardewvalleywiki.com/"
	};

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
	/// Searches the active game's wiki for <paramref name="query"/> via the MediaWiki API
	/// and populates the wiki results list. Returns <c>false</c> if the query is empty.
	/// </summary>
	private async Task<bool> SearchWiki(string query)
	{
		if (string.IsNullOrEmpty(query)) return false;
		splitWiki.Visible = true;
		SetStatus("Searching Wiki...");
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
			Speak($"Found {wikiResults.Count} results.");
		}
		catch (Exception ex)
		{
			LogError("Wiki", "Search Error: " + ex.Message);
			Speak("Wiki search failed.");
		}
		return true;
	}

	/// <summary>
	/// Loads all pages and sub-categories in a wiki category via the MediaWiki API
	/// and updates the wiki results list.
	/// </summary>
	private async Task<bool> LoadWikiCategory(string category)
	{
		SetStatus("Loading Category: " + category, speak: false);
		try
		{
			string mappedCategory = category;
			if (_settings.ActiveGame == "SkyrimSE")
			{
				if (category == "Quests" || category == "Items" || category == "Skills" || category == "NPCs" || category == "Magic" || category == "Factions" || category == "Locations")
					mappedCategory = "Skyrim-" + category;
			}
			else if (_settings.ActiveGame == "Fallout4")
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
			Speak($"{category} category loaded with {wikiResults.Count} items.");
		}
		catch (Exception ex)
		{
			LogError("Wiki", "Category Error: " + ex.Message);
			Speak("Failed to load wiki category.");
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
		Speak("Loading page: " + title);
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
			Speak("Back to " + state.Title);
		}
		else Speak("Already at top level.");
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
			Speak($"{listWikiResults.SelectedIndex + 1} of {listWikiResults.Items.Count}");
		}
	}
}
