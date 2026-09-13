namespace KinetixModManager;

/// <summary>
/// Tells a real key press apart from the auto-repeat Windows sends while a key is held down.
///
/// <para>
/// Windows delivers a stream of identical KeyDown messages for one held key, and a window's shortcuts are
/// one-shot commands: refresh everything, open the manual, launch the game. Held a moment too long, each of
/// them ran again per repeat — a Refresh Everything held for a second started three or four refreshes, which
/// then argued with each other about which was already running.
/// </para>
///
/// <para>
/// The rule is that a repeat cannot have a release in between. A second deliberate press of the same key is
/// therefore still a press, because the release that separates them clears the held key. That is the whole
/// distinction, and it needs no timers and no guesses about how fast a person types.
/// </para>
///
/// <para>
/// Keys are taken as the plain integer of <c>KeyData</c> rather than the WinForms enum, so this rule can be
/// unit tested without the test project referencing WinForms — the same reason the shortcut defaults are read
/// out of the source rather than loaded.
/// </para>
/// </summary>
public sealed class KeyRepeatFilter
{
	/// <summary>The key combination currently held, or null when nothing is.</summary>
	private int? _held;

	/// <summary>
	/// Whether this KeyDown is a genuine press rather than auto-repeat of the key already held. Records the key
	/// either way, so the repeats that follow are recognised.
	/// </summary>
	public bool IsFirstPress(int keyData)
	{
		bool first = _held != keyData;
		_held = keyData;
		return first;
	}

	/// <summary>A key came up: whatever was held is no longer held, so the next press is a press.</summary>
	public void Released() => _held = null;

	/// <summary>
	/// The window lost focus. Released is never seen when a key is let go over another window — alt-tabbing
	/// mid-press — and without this the key would still look held when the user came back to press it again.
	/// </summary>
	public void FocusLost() => _held = null;
}
