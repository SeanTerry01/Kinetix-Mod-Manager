using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;

namespace KinetixModManager;

/// <summary>
/// WitcherAccess's own player's guide, USER_MANUAL.html, as the Markdown the F3 viewer reads (Sean, 2026-11-12).
///
/// <para>
/// From 1.0 the mod ships the whole guide, in eighteen languages, and installs it into the game folder. It is one page:
/// the English text in <c>&lt;main id="doc"&gt;</c>, and each language complete in its own
/// <c>&lt;template id="tpl-xx"&gt;</c> that the page's language list swaps in. The markup is plain (headings, paragraphs,
/// lists, emphasis, links), so it converts straight into headings the viewer turns into chapters and sections.
/// </para>
/// </summary>
public static class WitcherAccessGuide
{
	/// <summary>The guide's file name, as the mod ships it.</summary>
	public const string FileName = "USER_MANUAL.html";

	/// <summary>
	/// The installed guide, or null. Looked for where an installer would put it: the game folder itself, the mod's own
	/// folder (switched off or not) and beside the game's programs. Never the whole game folder, which is tens of GB.
	/// </summary>
	public static string? FindInstalled(string gameFolder)
	{
		if (string.IsNullOrEmpty(gameFolder) || !Directory.Exists(gameFolder)) return null;
		string[] places =
		{
			gameFolder,
			Path.Combine(gameFolder, "mods", "modWitcherAccess"),
			Path.Combine(gameFolder, "mods", "~modWitcherAccess"),
			Path.Combine(gameFolder, "bin", "x64_dx12"),
			Path.Combine(gameFolder, "bin", "x64"),
		};
		return places.Select(p => Path.Combine(p, FileName)).FirstOrDefault(File.Exists);
	}

	/// <summary>
	/// The guide inside a downloaded WitcherAccess archive, or null: the newest zip in <paramref name="downloadsFolder"/>
	/// whose name says WitcherAccess and that holds the guide. Read from the archive, nothing unpacked, so F3 has it from the
	/// moment the mod has been downloaded.
	/// </summary>
	public static string? ReadFromDownloads(string downloadsFolder)
	{
		if (string.IsNullOrEmpty(downloadsFolder) || !Directory.Exists(downloadsFolder)) return null;
		foreach (FileInfo zip in new DirectoryInfo(downloadsFolder).GetFiles("*.zip")
			.Where(f => f.Name.Contains("WitcherAccess", StringComparison.OrdinalIgnoreCase))
			.OrderByDescending(f => f.LastWriteTimeUtc))
		{
			try
			{
				using ZipArchive archive = ZipFile.OpenRead(zip.FullName);
				ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(e => e.Name.Equals(FileName, StringComparison.OrdinalIgnoreCase));
				if (entry == null) continue;
				using var reader = new StreamReader(entry.Open(), System.Text.Encoding.UTF8);
				return reader.ReadToEnd();
			}
			catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
			{
				DiagnosticLog.WriteException("ModDocs", $"reading {FileName} out of {zip.Name}", ex);
			}
		}
		return null;
	}

	/// <summary>
	/// The guide in one language as Markdown: the template for <paramref name="language"/> (the mod's own codes, such as
	/// "fr" or "cz"), else the English one, else whatever the page shows. Empty when the page holds neither.
	/// </summary>
	public static string ToMarkdown(string html, string language = "en")
	{
		string body = Section(html, $@"<template\s+id=""tpl-{Regex.Escape(language)}""[^>]*>(.*?)</template>")
			?? Section(html, @"<template\s+id=""tpl-en""[^>]*>(.*?)</template>")
			?? Section(html, @"<main[^>]*\bid=""doc""[^>]*>(.*?)</main>")
			?? "";
		if (body.Length == 0) return "";

		// Its own contents list: the viewer's chapters are the table of contents already.
		body = Regex.Replace(body, @"<nav\b.*?</nav>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
		// The source wraps its paragraphs across lines. Spacing first, then the line breaks the markup stands for.
		body = Regex.Replace(body, @"\s+", " ");

		for (int level = 1; level <= 4; level++)
			body = Regex.Replace(body, $@"<h{level}\b[^>]*>(.*?)</h{level}>",
				m => "\n\n" + new string('#', level) + " " + Plain(m.Groups[1].Value) + "\n\n", RegexOptions.IgnoreCase);

		// A link off the page keeps its address, which Enter on the line opens; one within the page is only its words.
		body = Regex.Replace(body, @"<a\b[^>]*href=""(https?://[^""]+)""[^>]*>(.*?)</a>",
			m => $"[{Plain(m.Groups[2].Value)}]({m.Groups[1].Value})", RegexOptions.IgnoreCase);
		body = Regex.Replace(body, @"<a\b[^>]*>(.*?)</a>", "$1", RegexOptions.IgnoreCase);

		body = Regex.Replace(body, @"<li\b[^>]*>", "\n- ", RegexOptions.IgnoreCase);
		body = Regex.Replace(body, @"</(li|ul|ol)>|<(ul|ol)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
		body = Regex.Replace(body, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
		body = Regex.Replace(body, @"</?(p|div)\b[^>]*>", "\n\n", RegexOptions.IgnoreCase);
		body = Regex.Replace(body, @"<[^>]+>", "");
		body = WebUtility.HtmlDecode(body);

		// Tidy: no spaces at the ends of lines, and no more than one blank line in a row.
		body = Regex.Replace(body, @"[ \t]*\n[ \t]*", "\n");
		body = Regex.Replace(body, @"\n{3,}", "\n\n");
		return body.Trim() + "\n";
	}

	private static string? Section(string html, string pattern)
	{
		Match m = Regex.Match(html, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
		return m.Success && m.Groups[1].Value.Trim().Length > 0 ? m.Groups[1].Value : null;
	}

	/// <summary>A heading's or link's words, with any markup inside them gone.</summary>
	private static string Plain(string html) => WebUtility.HtmlDecode(Regex.Replace(html, @"<[^>]+>", "")).Trim();
}
