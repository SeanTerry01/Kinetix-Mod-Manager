using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>What the shortcut manager makes of a key it was just given.</summary>
public enum ShortcutCheck
{
	/// <summary>It can be used.</summary>
	Usable,

	/// <summary>A bare letter or digit, which would also fire while typing in any text box.</summary>
	WouldStealTyping,

	/// <summary>Another action already has it.</summary>
	AlreadyTaken
}

/// <summary>Whether a key can be given to an action.</summary>
public static class ShortcutRules
{
	private const int FirstFunctionKey = 112; // F1
	private const int LastFunctionKey = 135;  // F24
	private const int KeyCodeMask = 0xFFFF;

	/// <summary>
	/// Checks a key for an action.
	///
	/// ⚠️ It used to accept anything. Two actions on one key both fired, and a bare letter was swallowed by every text box
	/// in the manager from then on, because the window sees each key before the control does.
	/// </summary>
	/// <param name="keyData">The key, with its modifiers, in the form the settings store it.</param>
	/// <param name="action">The action being given the key.</param>
	/// <param name="shortcuts">Every action's current key.</param>
	/// <param name="takenBy">The action that already has it, when that is the answer.</param>
	public static ShortcutCheck Check(int keyData, string action, IReadOnlyDictionary<string, int> shortcuts, out string? takenBy)
	{
		takenBy = shortcuts
			.Where(s => s.Key != action && s.Value == keyData)
			.Select(s => s.Key)
			.FirstOrDefault();
		if (takenBy != null) return ShortcutCheck.AlreadyTaken;

		int key = keyData & KeyCodeMask;
		bool functionKey = key >= FirstFunctionKey && key <= LastFunctionKey;
		bool hasCommandModifier = (keyData & (Shortcut.Control | Shortcut.Alt)) != 0;

		return functionKey || hasCommandModifier ? ShortcutCheck.Usable : ShortcutCheck.WouldStealTyping;
	}
}
