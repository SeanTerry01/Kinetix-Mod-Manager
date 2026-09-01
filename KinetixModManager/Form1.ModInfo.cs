using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using StardewMod = KinetixModManager.GameMod;

namespace KinetixModManager;

/// <summary>
/// On-demand Nexus mod info: the latest changelog and the full mod-page description, shown in an accessible
/// read-only viewer and read aloud. Works on the selected mod in the Installed, Updates, or Find New Mods list
/// (any mod with a Nexus id). Requires a Nexus API key.
/// </summary>
public partial class Form1
{
	/// <summary>Fetches and shows the selected mod's changelog (newest version first).</summary>
	private async void ViewModChangelog()
	{
		StardewMod? mod = SelectedNexusMod();
		if (mod == null || string.IsNullOrEmpty(mod.NexusID)) { Speak(Loc.T("modinfo.noNexusMod")); return; }
		if (string.IsNullOrEmpty(_settings.ApiKey)) { Speak(Loc.T("modinfo.needLogin")); return; }

		Speak(Loc.T("modinfo.loadingChangelog", mod.Name));
		SetStatus(Loc.T("modinfo.loadingChangelog", mod.Name), speak: false);
		try
		{
			JObject? logs = await _nexusService.GetChangelogsAsync(mod.NexusID);
			ResetStatus();
			if (logs == null || !logs.HasValues)
			{
				Speak(Loc.T("modinfo.noChangelog", mod.Name));
				return;
			}
			ShowTextViewer(Loc.T("modinfo.changelogTitle", mod.Name), FormatChangelog(logs));
		}
		catch (Exception ex)
		{
			ResetStatus();
			_soundEngine.Play("error");
			SpeakBox(Loc.T("modinfo.failed", ex.Message));
		}
	}

	/// <summary>Fetches and shows the selected mod's full description from its Nexus page (BBCode stripped).</summary>
	private async void ViewModDescription()
	{
		StardewMod? mod = SelectedNexusMod();
		if (mod == null || string.IsNullOrEmpty(mod.NexusID)) { Speak(Loc.T("modinfo.noNexusMod")); return; }
		if (string.IsNullOrEmpty(_settings.ApiKey)) { Speak(Loc.T("modinfo.needLogin")); return; }

		Speak(Loc.T("modinfo.loadingDescription", mod.Name));
		SetStatus(Loc.T("modinfo.loadingDescription", mod.Name), speak: false);
		try
		{
			JObject? details = await _nexusService.GetModDetailsAsync(mod.NexusID);
			ResetStatus();
			string desc = RichTextToPlain((string?)details?["description"] ?? "");
			if (string.IsNullOrWhiteSpace(desc)) desc = RichTextToPlain((string?)details?["summary"] ?? "");
			if (string.IsNullOrWhiteSpace(desc))
			{
				Speak(Loc.T("modinfo.noDescription", mod.Name));
				return;
			}
			ShowTextViewer(Loc.T("modinfo.descTitle", mod.Name), desc);
		}
		catch (Exception ex)
		{
			ResetStatus();
			_soundEngine.Play("error");
			SpeakBox(Loc.T("modinfo.failed", ex.Message));
		}
	}

	/// <summary>Formats a changelogs object (version → change lines) into readable text, newest version first.</summary>
	private string FormatChangelog(JObject logs)
	{
		var versions = logs.Properties().Select(p => p.Name).ToList();
		// Sort newest-first using the app's numeric version comparison (Nexus doesn't guarantee an order).
		versions.Sort((a, b) => IsNewerVersion(a, b) ? -1 : (IsNewerVersion(b, a) ? 1 : 0));
		versions.Reverse();

		var sb = new StringBuilder();
		foreach (string v in versions)
		{
			sb.AppendLine(Loc.T("modinfo.version", v));
			if (logs[v] is JArray arr)
				foreach (JToken line in arr)
				{
					string s = RichTextToPlain((string?)line ?? "");
					if (s.Length > 0) sb.AppendLine("• " + s);
				}
			sb.AppendLine();
		}
		return sb.ToString().Trim();
	}

	/// <summary>
	/// Converts a Nexus rich-text mod description or changelog (which can mix BBCode and HTML) to plain,
	/// screen-reader-friendly text. Line-break and list tags become newlines; link tags are flattened to
	/// "text (url)" so the URL stays visible and the viewer can offer to open it; every other tag is removed but
	/// its inner text is kept; HTML entities are decoded.
	/// </summary>
	private static string RichTextToPlain(string s)
	{
		if (string.IsNullOrEmpty(s)) return "";
		const RegexOptions IC = RegexOptions.IgnoreCase;
		const RegexOptions ICS = RegexOptions.IgnoreCase | RegexOptions.Singleline;

		// Line breaks (HTML + BBCode).
		s = Regex.Replace(s, @"<br\s*/?>", "\n", IC);
		s = Regex.Replace(s, @"</(p|div|h[1-6]|tr|ul|ol)>", "\n", IC);
		s = Regex.Replace(s, @"<li\b[^>]*>", "\n• ", IC);
		s = Regex.Replace(s, @"<hr\s*/?>", "\n", IC);
		s = Regex.Replace(s, @"\[\*\]", "\n• ", IC);
		s = Regex.Replace(s, @"\[/?(line|hr)\]", "\n", IC);

		// Links: keep the visible text and show the URL after it, so it's readable and openable.
		s = Regex.Replace(s, @"<a\b[^>]*href\s*=\s*[""']?([^""'>\s]+)[""']?[^>]*>(.*?)</a>", "$2 ($1)", ICS);
		s = Regex.Replace(s, @"\[url=([^\]]+)\](.*?)\[/url\]", "$2 ($1)", ICS);
		s = Regex.Replace(s, @"\[url\](.*?)\[/url\]", "$1", ICS);

		// Images: drop them (their content is a URL, not useful spoken).
		s = Regex.Replace(s, @"\[img\b[^\]]*\].*?\[/img\]", " ", ICS);
		s = Regex.Replace(s, @"\[img\b[^\]]*\]", " ", IC);
		s = Regex.Replace(s, @"<img\b[^>]*>", " ", IC);

		// Strip every remaining BBCode and HTML tag, keeping the text between them.
		s = Regex.Replace(s, @"\[/?[a-zA-Z][^\]]*\]", "", IC);
		s = Regex.Replace(s, @"<[^>]+>", "", RegexOptions.Singleline);

		// Decode entities and tidy whitespace.
		s = System.Net.WebUtility.HtmlDecode(s);
		s = Regex.Replace(s, @"[ \t]+\n", "\n");
		s = Regex.Replace(s, @"\n{3,}", "\n\n");
		return s.Trim();
	}

	/// <summary>
	/// Every http/https URL in a line, in order, de-duplicated and trimmed of the punctuation a sentence wraps
	/// around one. A line of prose can easily carry several — a mod's documentation naming its Nexus page and its
	/// source repository in the same breath — and the reader is on the line, not on any one link in it.
	///
	/// Deliberately not <see cref="LogAnalyzer.ExtractUrls"/>, which is the same idea for log lines but also
	/// rewrites a Nexus mod page to its Files tab. That is right for a log, where a link is nearly always
	/// something to download; it is wrong for a document, where following a link should land where the sentence
	/// said it would.
	/// </summary>
	private static List<string> ExtractUrlsInLine(string line)
	{
		var found = new List<string>();
		foreach (Match m in Regex.Matches(line, @"https?://[^\s)\]]+", RegexOptions.IgnoreCase))
		{
			string url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '"', '\'');
			if (url.Length > 0 && !found.Contains(url, StringComparer.OrdinalIgnoreCase)) found.Add(url);
		}
		return found;
	}

	/// <summary>The whole line the caret is sitting on in a text box, without its line ending.</summary>
	private static string LineAtCaret(TextBox box)
	{
		string all = box.Text;
		int caret = Math.Min(box.SelectionStart, all.Length);
		int start = caret > 0 ? all.LastIndexOf('\n', caret - 1) + 1 : 0;
		int end = all.IndexOf('\n', caret);
		if (end < 0) end = all.Length;
		return all.Substring(start, end - start).TrimEnd('\r');
	}

	/// <summary>
	/// Enter on a line that holds a link: confirms, then opens it in the browser. Returns false when the line has
	/// no link, so the caller can leave the key alone.
	///
	/// Shared by every read-only text pane the user arrows through — a mod's description, the manual, the change
	/// log, an accessibility mod's documentation — because a link in a body of text is unreachable to someone
	/// reading it line by line unless the line itself can be acted on. It confirms first: Enter is a key people
	/// press to move on, and launching a browser is not something to do to somebody by surprise.
	/// </summary>
	private bool TryOpenLinkOnCaretLine(TextBox box)
	{
		List<string> urls = ExtractUrlsInLine(LineAtCaret(box));
		if (urls.Count == 0) return false;

		// Several links on one line: choose which, exactly as a log line with several does. The picker is the
		// confirmation in that case, so it does not ask twice.
		if (urls.Count > 1) { ShowLogLinkPicker(urls); return true; }

		if (SpeakBox(Loc.T("modinfo.openLinkConfirm", urls[0]), Loc.T("modinfo.openLinkTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
		{
			try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(urls[0]) { UseShellExecute = true }); }
			catch (Exception ex) { LogFailure("OpenLink", "Could not open link", ex); Speak(Loc.T("smapi.couldNotOpenLink")); }
		}
		return true;
	}

	/// <summary>
	/// Shows text in a scrollable, read-only multiline box the user can arrow through with a screen reader, and
	/// reads it aloud on open (chunked, so long text isn't clipped). Escape closes.
	/// </summary>
	private void ShowTextViewer(string title, string text)
	{
		// Shown inside the main window rather than as one of its own — see Form1.InlineView. Escape is handled
		// by the view itself.
		ShowInlineView(title, (container, closeView) =>
		{
		var box = new TextBox
		{
			Multiline = true,
			ReadOnly = true,
			WordWrap = true,
			ScrollBars = ScrollBars.Vertical,
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			Text = NormalizeNewlines(text),
			AccessibleName = title,
		};
		container.Controls.Add(box);
		// Enter on a line that contains a link offers to open it in the browser (same idea as the log tabs).
		box.KeyDown += (_, e) =>
		{
			if (e.KeyCode != Keys.Enter) return;
			if (TryOpenLinkOnCaretLine(box)) e.Handled = e.SuppressKeyPress = true;
		};

		box.SelectionStart = 0;
		box.SelectionLength = 0;
		return box;
		},
		// Through the hint, so the title says which text this is before the text itself starts. Spoken during
		// build it landed ahead of the title, so a long passage was read out and only then named. The hint is
		// spoken with SpeakLong, so a passage this size is chunked and read whole rather than clipped.
		hint: (Regex.IsMatch(text, @"https?://", RegexOptions.IgnoreCase) ? Loc.T("modinfo.linkHint") + " " : "") + text);
	}
}
