using System.Text;

namespace KinetixModManager;

/// <summary>
/// What a keyboard command is called out loud.
///
/// <para>
/// The shortcut table is keyed by identifiers — <c>ViewDescription</c>, <c>PluginSlots</c>, <c>CheckRequirements</c> —
/// and those identifiers are what every settings file already stores, so they cannot change. They are also what the
/// Shortcut Customization list used to show, which meant a list of code names read by a screen reader as run-together
/// words, several of them not naming what the command does at all: <c>ViewDescription</c> opens the mod's full Nexus
/// page, and <c>QuickFix</c> resolves missing requirements. The name is looked up here instead, by
/// <c>"shortcutName." + action</c>, and matches the wording of the menu item the command sits behind.
/// </para>
/// </summary>
public static class ShortcutNames
{
	/// <summary>The prefix every command name is filed under in the language file.</summary>
	public const string KeyPrefix = "shortcutName.";

	/// <summary>
	/// The plain name for <paramref name="action"/>. An action with no name in the language file — one saved by a
	/// newer version, say — is spelt out word by word instead, which reads far better than the key would.
	/// </summary>
	public static string For(string action) =>
		Loc.Has(KeyPrefix + action) ? Loc.T(KeyPrefix + action) : SplitWords(action);

	/// <summary>"CheckRequirements" becomes "Check Requirements"; an acronym run such as "AiLog" stays whole.</summary>
	public static string SplitWords(string identifier)
	{
		var result = new StringBuilder(identifier.Length + 8);
		for (int i = 0; i < identifier.Length; i++)
		{
			char c = identifier[i];
			bool startsWord = i > 0 && char.IsUpper(c) &&
				(char.IsLower(identifier[i - 1]) || (i + 1 < identifier.Length && char.IsLower(identifier[i + 1]) && char.IsUpper(identifier[i - 1])));
			if (startsWord) result.Append(' ');
			result.Append(c);
		}
		return result.ToString();
	}
}
