using System;
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

	/// <summary>Returns the first http/https URL in a line (trimmed of trailing punctuation), or null if none.</summary>
	private static string? ExtractFirstUrl(string line)
	{
		Match m = Regex.Match(line, @"https?://[^\s)\]]+", RegexOptions.IgnoreCase);
		if (!m.Success) return null;
		return m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '"', '\'');
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
			string all = box.Text;
			int caret = Math.Min(box.SelectionStart, all.Length);
			int start = caret > 0 ? all.LastIndexOf('\n', caret - 1) + 1 : 0;
			int end = all.IndexOf('\n', caret);
			if (end < 0) end = all.Length;
			string? url = ExtractFirstUrl(all.Substring(start, end - start));
			if (url == null) return;
			e.Handled = e.SuppressKeyPress = true;
			if (SpeakBox(Loc.T("modinfo.openLinkConfirm", url), Loc.T("modinfo.openLinkTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
			{
				try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
				catch { }
			}
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
