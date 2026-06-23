using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// "Mod Documentation" viewer (F3): surfaces the active game's accessibility-mod documentation as the same
/// screen-reader-friendly drill-down the manual and change log use. The top level is a chooser of one or more
/// docs; opening one lists its sections (table of contents) with the text in the read-only pane.
///
/// Each doc is sourced with a bundle-plus-refresh strategy: a copy ships with the app as the always-available,
/// offline baseline, and a background refresh pulls the latest — a GitHub README for Stardew Access, or the live
/// Nexus mod description for the Nexus-only Skyrim Access and Fallout 4 Access — into a per-user cache that is
/// shown the next time the viewer opens.
/// </summary>
public partial class Form1
{
	/// <summary>One documentation source for the active game.</summary>
	/// <param name="Title">Display name shown in the chooser (e.g. "Stardew Access").</param>
	/// <param name="BundledFile">App-relative path to the shipped offline copy (e.g. "docs/stardew-access.md").</param>
	/// <param name="GitHubRawUrls">Raw Markdown files combined into one doc on refresh; empty for Nexus-only mods.</param>
	/// <param name="NexusModId">Nexus mod id whose live description is pulled on refresh; null for GitHub-sourced mods.</param>
	private sealed record ModDocSource(
		string Title,
		string BundledFile,
		IReadOnlyList<string> GitHubRawUrls,
		string? NexusModId);

	/// <summary>The documentation sources offered for the active game. Designed so adding another doc is one line.</summary>
	private List<ModDocSource> DocSourcesForActiveGame()
	{
		const string sdaBase = "https://raw.githubusercontent.com/stardew-access/stardew-access/development/docs/";
		return _settings.ActiveGame switch
		{
			"SkyrimSE" => new List<ModDocSource>
			{
				new ModDocSource("Skyrim Access", "docs/skyrim-access.md", Array.Empty<string>(), "181131"),
			},
			"Fallout4" => new List<ModDocSource>
			{
				new ModDocSource("Fallout 4 Access", "docs/fallout4-access.md", Array.Empty<string>(), "100314"),
			},
			"StardewValley" => new List<ModDocSource>
			{
				// The README is just an index; the genuinely useful pages (keybindings, features, commands) are
				// sibling files. Combining them gives the drill-down real depth, each file becoming a top section.
				new ModDocSource("Stardew Access", "docs/stardew-access.md", new[]
				{
					sdaBase + "README.md",
					sdaBase + "setup.md",
					sdaBase + "features.md",
					sdaBase + "keybindings.md",
					sdaBase + "commands.md",
					sdaBase + "config.md",
				}, null),
			},
			_ => new List<ModDocSource>(),
		};
	}

	/// <summary>F3: opens the active game's accessibility-mod documentation as a drill-down chooser.</summary>
	private async void ShowModDocs()
	{
		List<ModDocSource> sources = DocSourcesForActiveGame();
		if (sources.Count == 0)
		{
			SpeakBox(Loc.T("moddocs.noDocsForGame"));
			return;
		}

		// Fetch the latest copy now (and cache it) so the viewer opens on the current docs; offline/not-logged-in
		// sources fall back fast to the cached or bundled copy, so this stays responsive without a connection.
		SetStatus(Loc.T("moddocs.loading"));
		Speak(Loc.T("moddocs.loading"));

		var roots = new List<DocNode>();
		foreach (ModDocSource src in sources)
			roots.Add(BuildDocSourceNode(src, await ResolveDocForDisplayAsync(src)));

		// Clear the transient "loading" message so it doesn't linger in the status bar after the docs are shown.
		ResetStatus();

		ShowDocDrilldown(roots, Loc.T("moddocs.windowTitle"), Loc.T("moddocs.toc"), Loc.T("moddocs.topicInfo"));
	}

	/// <summary>Turns one source's Markdown into a chooser node: its parsed sections become children, so opening it
	/// lists its table of contents. An empty/unparsable doc becomes a leaf carrying whatever text we have.</summary>
	private DocNode BuildDocSourceNode(ModDocSource src, string markdown)
	{
		var node = new DocNode(src.Title);
		if (string.IsNullOrWhiteSpace(markdown))
		{
			node.Content = Loc.T("moddocs.unavailable", src.Title);
			return node;
		}

		string clean = CleanMarkdownForReading(markdown);
		List<DocNode> parsed = NormalizeDocRoots(ParseDocTree(clean.Split('\n')));
		CollapseRedundantLevels(parsed);
		PruneAndFillDocNodes(parsed);

		if (parsed.Count > 0)
		{
			node.Children.AddRange(parsed);
			node.Content = Loc.T("moddocs.chooserInfo", src.Title);
		}
		else
		{
			node.Content = clean.Trim();
		}

		// A multiline TextBox only renders a line break on a carriage-return + line-feed pair; a lone line feed
		// (as Markdown and our parsing produce) shows as an invisible control character with no break. Normalise
		// the whole tree to CRLF so paragraphs and list lines actually start on new lines in the reader.
		NormalizeNodeNewlines(node);
		return node;
	}

	/// <summary>Rewrites every newline in a node's text (and its descendants') to CRLF so the read-only TextBox
	/// renders real line breaks.</summary>
	private static void NormalizeNodeNewlines(DocNode node)
	{
		node.Content = node.Content.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
		foreach (DocNode child in node.Children) NormalizeNodeNewlines(child);
	}

	/// <summary>Heading labels that are navigation scaffolding rather than readable content, so they are dropped
	/// from a parsed doc (the drill-down is itself the table of contents, and cross-doc "Other Pages" links don't
	/// resolve here).</summary>
	private static readonly string[] _docNavCruft =
	{
		"table of contents", "table of content", "contents", "other pages", "other page"
	};

	/// <summary>Cleans up a parsed doc tree for reading: drops navigation-only sections, then fills any heading that
	/// has sub-sections but no body text of its own (e.g. a "Features" heading whose text lives entirely in its
	/// sub-headings) by gathering its sub-sections' text — so landing on that heading reads the whole section
	/// instead of an empty pane, while the sub-sections remain available to drill into.</summary>
	private static void PruneAndFillDocNodes(List<DocNode> nodes)
	{
		nodes.RemoveAll(n => _docNavCruft.Contains(n.Label.Trim().ToLowerInvariant()));

		foreach (DocNode n in nodes)
		{
			PruneAndFillDocNodes(n.Children);   // clean the sub-tree first so the rollup below is complete

			if (n.Children.Count > 0 && string.IsNullOrWhiteSpace(n.Content))
			{
				var sb = new StringBuilder();
				foreach (DocNode child in n.Children)
				{
					sb.AppendLine(child.Label);
					string body = child.Content.Trim();
					if (body.Length > 0) sb.AppendLine(body);
					sb.AppendLine();
				}
				n.Content = sb.ToString().Trim();
			}
		}
	}

	/// <summary>The most up-to-date copy available without blocking: the refreshed cache if present, otherwise the
	/// offline copy shipped beside the executable. Returns empty if neither exists.</summary>
	private string ReadCachedOrBundledDoc(ModDocSource src)
	{
		try
		{
			string cache = DocCachePath(src);
			if (File.Exists(cache)) return File.ReadAllText(cache);
		}
		catch (Exception ex) { LogError("ModDocs", $"Cache read failed for {src.Title}: {ex.Message}"); }

		try
		{
			string bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
				src.BundledFile.Replace('/', Path.DirectorySeparatorChar));
			if (File.Exists(bundled)) return File.ReadAllText(bundled);
		}
		catch (Exception ex) { LogError("ModDocs", $"Bundled read failed for {src.Title}: {ex.Message}"); }

		return "";
	}

	/// <summary>Per-user cache path for a refreshed doc (under the app data folder's DocCache directory).</summary>
	private static string DocCachePath(ModDocSource src)
	{
		string safe = Regex.Replace(src.Title, @"[^A-Za-z0-9_-]+", "-");
		return Path.Combine(dataBasePath, "DocCache", safe + ".md");
	}

	/// <summary>Returns the best copy to display: the freshly fetched online doc if one is available (also written
	/// to the cache for offline use later), otherwise the cached copy from a previous fetch, otherwise the bundled
	/// offline baseline. Online failures (offline, not logged in, API error) fall through silently to the fallback.</summary>
	private async Task<string> ResolveDocForDisplayAsync(ModDocSource src)
	{
		try
		{
			string fresh = await FetchFreshDocAsync(src);
			if (!string.IsNullOrWhiteSpace(fresh))
			{
				try
				{
					string cache = DocCachePath(src);
					Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
					File.WriteAllText(cache, fresh);
				}
				catch (Exception ex) { LogError("ModDocs", $"Cache write failed for {src.Title}: {ex.Message}"); }
				return fresh;
			}
		}
		catch (Exception ex)
		{
			LogError("ModDocs", $"Online fetch failed for {src.Title}: {ex.Message}");
		}

		return ReadCachedOrBundledDoc(src);
	}

	/// <summary>Pulls a source's latest Markdown: the combined GitHub doc files for a GitHub source, or the live
	/// Nexus mod description (converted from BBCode) for a Nexus-only source when the user is logged in. Returns
	/// empty when there is nothing fresh to fetch (e.g. a Nexus source with no API key).</summary>
	private async Task<string> FetchFreshDocAsync(ModDocSource src)
	{
		if (src.GitHubRawUrls.Count > 0)
		{
			// Fetch the parts concurrently (keeping order), and let any single part fail without losing the rest.
			string[] parts = await Task.WhenAll(src.GitHubRawUrls.Select(async url =>
			{
				try { return await NexusService.HttpClient.GetStringAsync(url); }
				catch (Exception ex) { LogError("ModDocs", $"Doc part failed ({url}): {ex.Message}"); return ""; }
			}));

			var sb = new StringBuilder();
			foreach (string part in parts)
			{
				if (!string.IsNullOrWhiteSpace(part))
				{
					sb.Append(part.TrimEnd());
					sb.Append("\n\n");
				}
			}
			return sb.ToString();
		}

		// Nexus-only docs live on the mod page; the description needs an API key to fetch.
		if (!string.IsNullOrEmpty(src.NexusModId) && !string.IsNullOrEmpty(_settings.ApiKey))
		{
			JObject? details = await _nexusService.GetModDetailsAsync(src.NexusModId);
			string bb = details?["description"]?.ToString() ?? "";
			if (!string.IsNullOrWhiteSpace(bb))
			{
				string name = details?["name"]?.ToString() ?? src.Title;
				return $"# {name}\n\n{BBCodeToMarkdown(bb)}";
			}
		}

		return "";
	}

	/// <summary>Strips elements that read poorly in a plain-text pane — images/badges, HTML comments and stray
	/// tags, and link targets (the link text is kept) — while leaving headings and prose intact. Applied to every
	/// doc before it is parsed into the drill-down.</summary>
	private static string CleanMarkdownForReading(string markdown)
	{
		string s = markdown.Replace("\r\n", "\n").Replace("\r", "\n");
		s = Regex.Replace(s, @"<!--.*?-->", "", RegexOptions.Singleline);                  // HTML comments
		// Turn line-break and block tags into real newlines BEFORE stripping tags, so HTML content (e.g. a Nexus
		// description) keeps its paragraph structure instead of collapsing into one run-on block.
		s = Regex.Replace(s, @"(?i)<br\s*/?>", "\n");
		s = Regex.Replace(s, @"(?i)</(p|div|li|ul|ol|tr|h[1-6]|blockquote)\s*>", "\n");
		s = Regex.Replace(s, @"(?m)^\s*!\[[^\]]*\]\([^)]*\)\s*$", "");                     // image-only lines
		s = Regex.Replace(s, @"!\[[^\]]*\]\([^)]*\)", "");                                  // inline images
		s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]*\)", "$1");                               // [text](url) -> text
		s = Regex.Replace(s, @"</?[a-zA-Z][^>]*>", "");                                     // stray HTML tags
		s = Regex.Replace(s, @"\n{3,}", "\n\n");                                            // collapse blank runs
		return s;
	}

	/// <summary>Converts the BBCode/HTML a Nexus mod description uses into the lightweight Markdown the document
	/// viewer parses. The key step is promoting standalone [size]/[b] lines to headings, which gives the otherwise
	/// flat description the table of contents the drill-down needs; lists become bullets, links keep their text,
	/// and presentational tags are dropped.</summary>
	private static string BBCodeToMarkdown(string bb)
	{
		string s = bb.Replace("\r\n", "\n").Replace("\r", "\n");

		// Nexus descriptions use HTML line-break/block tags; turn them into real newlines first so the heading and
		// list detection below sees one item per line and the text doesn't run together.
		s = Regex.Replace(s, @"(?i)<br\s*/?>", "\n");
		s = Regex.Replace(s, @"(?i)</(p|div|li|ul|ol|tr|h[1-6]|blockquote)\s*>", "\n");
		s = Regex.Replace(s, @"(?i)\[br\]", "\n");

		// A [size]/[b] run that occupies its own line is almost always a section title.
		s = Regex.Replace(s, @"(?im)^\s*\[size=\d+\]\s*\[b\]\s*(.+?)\s*\[/b\]\s*\[/size\]\s*$", "## $1");
		s = Regex.Replace(s, @"(?im)^\s*\[size=\d+\]\s*(.+?)\s*\[/size\]\s*$", "## $1");
		s = Regex.Replace(s, @"(?im)^\s*\[b\]\s*(.+?)\s*\[/b\]\s*$", "### $1");
		s = Regex.Replace(s, @"(?is)\[/?size[^\]]*\]", "");

		// Lists.
		s = Regex.Replace(s, @"(?is)\[\*\]\s*", "\n- ");
		s = Regex.Replace(s, @"(?is)\[/?list[^\]]*\]", "\n");

		// Links and images.
		s = Regex.Replace(s, @"(?is)\[url=[^\]]*\]\s*(.+?)\s*\[/url\]", "$1");
		s = Regex.Replace(s, @"(?is)\[url\]\s*(.+?)\s*\[/url\]", "$1");
		s = Regex.Replace(s, @"(?is)\[/?img[^\]]*\]", "");

		// Remaining presentational tags.
		s = Regex.Replace(s, @"(?is)\[/?(b|i|u|s|center|left|right|color|quote|font|spoiler|youtube|line|heading|indent|sup|sub)[^\]]*\]", "");

		// HTML entities and any stray tags.
		s = s.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
			 .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&nbsp;", " ");
		s = Regex.Replace(s, @"</?[a-zA-Z][^>]*>", "");

		s = Regex.Replace(s, @"[ \t]+\n", "\n");
		s = Regex.Replace(s, @"\n{3,}", "\n\n");
		return s.Trim();
	}
}
