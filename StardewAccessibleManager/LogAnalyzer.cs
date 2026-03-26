using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace StardewAccessibleManager;

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
}
