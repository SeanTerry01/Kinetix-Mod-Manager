using System;

namespace KinetixModManager;

/// <summary>
/// A keyboard shortcut as a number: a virtual-key code with modifier bits above it.
///
/// <para>
/// These are exactly the values <c>System.Windows.Forms.Keys</c> uses, and deliberately so — every settings
/// file ever written by the manager holds them, so an installation that upgrades reads its own shortcuts
/// back unchanged. What this buys is that <see cref="AppSettings"/> no longer needs the WinForms enum to say
/// what a shortcut is, which was the single thing keeping the whole settings class out of the core and
/// therefore out of reach of any second front end.
/// </para>
///
/// <para>
/// Windows-shaped numbering on every platform, like <see cref="VirtualKeys"/> and for the same reason: the
/// codes are a shared vocabulary for "which key", not a claim about the machine. A GTK head converts at its
/// own edge, exactly as the WinForms head does.
/// </para>
/// </summary>
public static class Shortcut
{
	/// <summary>No key bound. The Shortcut Manager writes this when a user clears one.</summary>
	public const int None = 0;

	public const int Shift = 0x10000;
	public const int Control = 0x20000;
	public const int Alt = 0x40000;

	/// <summary>The key itself, with the modifier bits taken off.</summary>
	public const int KeyMask = 0xFFFF;

	/// <summary>A letter key. <c>A</c> is 65, as it is in ASCII and in every virtual-key table.</summary>
	public static int Letter(char letter) => char.ToUpperInvariant(letter);

	/// <summary>A function key: <c>Function(1)</c> is F1, which is 112.</summary>
	public static int Function(int number) => 0x6F + number;

	/// <summary>The key without its modifiers.</summary>
	public static int KeyOf(int shortcut) => shortcut & KeyMask;

	public static bool HasShift(int shortcut) => (shortcut & Shift) != 0;
	public static bool HasControl(int shortcut) => (shortcut & Control) != 0;
	public static bool HasAlt(int shortcut) => (shortcut & Alt) != 0;

	/// <summary>
	/// What a shortcut is called when it is read out: "Ctrl + Shift + D", "F5", or the word for nothing bound.
	///
	/// In the order the modifiers are usually said rather than the order the bits sit in, because this is
	/// read aloud far more often than it is looked at.
	/// </summary>
	public static string Describe(int shortcut)
	{
		if (shortcut == None) return Loc.T("shortcut.unbound");

		var parts = new System.Collections.Generic.List<string>();
		if (HasControl(shortcut)) parts.Add("Ctrl");
		if (HasShift(shortcut)) parts.Add("Shift");
		if (HasAlt(shortcut)) parts.Add("Alt");
		parts.Add(VirtualKeys.VirtualKeyName(KeyOf(shortcut)));

		return string.Join(" + ", parts);
	}
}
