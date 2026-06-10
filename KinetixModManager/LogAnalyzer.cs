using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace KinetixModManager;

public static class LogAnalyzer
{
	private static readonly Dictionary<string, string> FixRules = new Dictionary<string, string>
	{
		{ "requires the 'Content Patcher' mod", "Install 'Content Patcher' from Nexus. It is required for this mod to work." },
		{ "requires the 'Json Assets' mod", "Install 'Json Assets' from Nexus. It is required for this mod to work." },
		{ "requires the 'ExpandedPreconditionsUtility' mod", "Install 'Expanded Preconditions Utility'. This expansion mod depends on it." },
		{ "requires the 'SpaceCore' mod", "Install 'SpaceCore' from Nexus. Many modern mods require this framework." },
		{ "requires the 'Producer Framework Mod' mod", "Install 'Producer Framework Mod'. It is needed for custom machines." },
		{ "is no longer compatible", "This mod is outdated and broken. Check for a newer version or an 'Unofficial Update'." },
		{ "Multiple copies of", "You have this mod installed twice. Delete one of the folders to avoid crashes." },
		{ "SMAPI is out of date", "Your SMAPI version is old. Download the latest installer from SMAPI.io." },
		{ "skipped because it's an empty folder", "You have an empty folder in your Mods directory. You can safely delete it." },
		{ "manifest\\.json is missing", "This mod folder is missing its manifest. It might be a sub-folder or a corrupted download." }
	};

	public static string GetSuggestedFix(string logLine)
	{
		foreach (KeyValuePair<string, string> fixRule in FixRules)
		{
			if (Regex.IsMatch(logLine, fixRule.Key, RegexOptions.IgnoreCase))
			{
				return fixRule.Value;
			}
		}
		return "";
	}

	public static string ExtractMissingModId(string logLine)
	{
		Match match = Regex.Match(logLine, "requires the '([^']+)' mod", RegexOptions.IgnoreCase);
		if (match.Success)
		{
			return match.Groups[1].Value;
		}
		return "";
	}

	/// <summary>
	/// Returns the SMAPI log level token for a line (ERROR, WARN, ALERT, INFO, DEBUG, TRACE), upper-cased,
	/// or an empty string for continuation lines (e.g. stack traces) that have no header. SMAPI formats each
	/// entry as "[HH:MM:SS LEVEL Source] message", so the level sits inside the leading bracket beside the
	/// timestamp — it is NOT the literal "[ERROR]". Matching the bracketed level is what makes the log filter
	/// actually catch errors and warnings.
	/// </summary>
	public static string GetLevel(string logLine)
	{
		Match m = Regex.Match(logLine, @"^\[\d{2}:\d{2}:\d{2}\s+([A-Za-z]+)\b");
		return m.Success ? m.Groups[1].Value.ToUpperInvariant() : "";
	}

	/// <summary>Returns the source/mod name from a log line's header (e.g. "Content Patcher"), or "" if none.</summary>
	public static string GetSource(string logLine)
	{
		Match m = Regex.Match(logLine, @"^\[\d{2}:\d{2}:\d{2}\s+[A-Za-z]+\s+(.+?)\]");
		return m.Success ? m.Groups[1].Value.Trim() : "";
	}

	/// <summary>Strips the "[HH:MM:SS LEVEL Source]" header from a log line, leaving just the message body.</summary>
	public static string StripHeader(string logLine)
	{
		return Regex.Replace(logLine, @"^\[[^\]]*\]\s*", "").Trim();
	}

	/// <summary>
	/// Produces a plain-language diagnosis of a SMAPI log line — what it means and how to fix it — for the
	/// log viewer's quick-fix action. Recognized error/warning patterns get a specific explanation; anything
	/// else falls back to a generic summary built from the line's level, source, and message so the user
	/// still gets a useful starting point. Continuation lines (no header) point back to the line above.
	/// </summary>
	public static string Diagnose(string logLine)
	{
		string level = GetLevel(logLine);
		string source = GetSource(logLine);
		if (string.IsNullOrEmpty(source)) source = "a mod";

		Match m = Regex.Match(logLine, @"-\s*(.+?)\s+[\d.]+\s+because it's no longer compatible", RegexOptions.IgnoreCase);
		if (m.Success)
		{
			return $"What this means: The mod \"{m.Groups[1].Value.Trim()}\" is not compatible with your current SMAPI or "
				+ "Stardew Valley version, so SMAPI skipped it (the mod will not run).\n\n"
				+ "How to fix: This line usually includes a link to the mod's page — press Enter on it to open the Files "
				+ "tab and install a newer version. If there is no compatible update yet, the mod is outdated; remove it "
				+ "until the author updates it.";
		}

		m = Regex.Match(logLine, @"-\s*(.+?)\s+[\d.]+\s+because it needs newer versions of some mods:\s*(.+)", RegexOptions.IgnoreCase);
		if (m.Success)
		{
			return $"What this means: The mod \"{m.Groups[1].Value.Trim()}\" was skipped because a mod it depends on is out "
				+ $"of date. It needs: {m.Groups[2].Value.Trim()}\n\n"
				+ "How to fix: Update the dependency named above to the required version (the Updates tab can check for it), "
				+ "then restart the game.";
		}

		if (Regex.IsMatch(logLine, @"Harmony patch .*encountered an error while attempting to transpile", RegexOptions.IgnoreCase))
		{
			return $"What this means: \"{source}\" tried to modify the game with a Harmony patch and it failed — usually "
				+ "because the mod is outdated and patches game code that a Stardew Valley update changed.\n\n"
				+ $"How to fix: Update \"{source}\" to its latest version. If it is already current, it may not support your "
				+ "game version yet; disable it and check its mod page for a compatibility note.";
		}

		if (Regex.IsMatch(logLine, @"Error occurred while registering command", RegexOptions.IgnoreCase))
		{
			return $"What this means: \"{source}\" tried to add a console command that failed to register — often because two "
				+ "mods use the same command name, or the mod is out of date.\n\n"
				+ $"How to fix: This is usually harmless. If \"{source}\" misbehaves, update it; otherwise it can be ignored.";
		}

		if (Regex.IsMatch(logLine, @"Object ID .*does not exist", RegexOptions.IgnoreCase))
		{
			return $"What this means: \"{source}\" referenced a game item that isn't loaded. This usually means a content pack "
				+ "is missing, disabled, or loaded in the wrong order.\n\n"
				+ "How to fix: Make sure the mod that adds the missing item is installed and enabled. If you recently removed a "
				+ "mod, this item may have belonged to it. It is often safe to ignore if the game still plays correctly.";
		}

		string simple = GetSuggestedFix(logLine);
		if (!string.IsNullOrEmpty(simple))
		{
			return "What this means: SMAPI reported a known issue on this line.\n\nHow to fix: " + simple;
		}

		if (level == "ERROR" || level == "WARN" || level == "ALERT")
		{
			return $"What this means: This is a {level} reported by \"{source}\":\n\n{StripHeader(logLine)}\n\n"
				+ $"How to fix: There is no specific fix recorded for this one. Try updating or disabling \"{source}\". For more "
				+ "help, upload the full log to SMAPI.io from this tab and share the link on the mod's page or the Stardew "
				+ "Valley Discord.";
		}
		if (string.IsNullOrEmpty(level))
		{
			return "What this means: This line is part of a longer message (such as a stack trace) and has no problem of its "
				+ "own.\n\nHow to fix: Read the ERROR or WARN line just above it for the actual issue.";
		}
		return $"What this means: This is an informational {level} line from \"{source}\", not a problem.\n\nHow to fix: No action needed.";
	}

	/// <summary>
	/// Extracts the first http(s) URL from a log line, or an empty string if there is none. SMAPI's
	/// "you can update N mods" lines list each mod's page URL, so this lets the log viewer open the
	/// page for an update its own checker may have missed. Trailing punctuation the log wraps around
	/// the URL (for example a closing parenthesis or period) is trimmed, and nexusmods.com mod pages
	/// are normalized to the Files tab (?tab=files) so the user lands on the downloads, matching how
	/// opening a mod from the updates list behaves.
	/// </summary>
	public static string ExtractUrl(string logLine)
	{
		Match match = Regex.Match(logLine, @"https?://[^\s]+", RegexOptions.IgnoreCase);
		if (!match.Success)
		{
			return "";
		}
		string url = match.Value.TrimEnd('.', ',', ';', ':', ')', ']', '}', '>', '"', '\'');

		Match nexus = Regex.Match(url, @"^(https?://www\.nexusmods\.com/[^/]+/mods/\d+)", RegexOptions.IgnoreCase);
		if (nexus.Success)
		{
			return nexus.Groups[1].Value + "?tab=files";
		}
		return url;
	}
}
