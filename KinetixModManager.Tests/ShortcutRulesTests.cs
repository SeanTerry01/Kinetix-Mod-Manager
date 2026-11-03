using System.Collections.Generic;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

public sealed class ShortcutRulesTests
{
	private static readonly int LetterQ = Shortcut.Letter('Q');
	private const int F5 = 116;

	private static readonly Dictionary<string, int> Shortcuts = new()
	{
		["Launch"] = F5,
		["Refresh"] = Shortcut.Letter('R') | Shortcut.Control,
	};

	private static ShortcutCheck Check(int key, string action = "Search") => ShortcutRules.Check(key, action, Shortcuts, out _);

	[Fact]
	public void ControlOrAltWithALetterIsFine()
	{
		Assert.Equal(ShortcutCheck.Usable, Check(LetterQ | Shortcut.Control));
		Assert.Equal(ShortcutCheck.Usable, Check(LetterQ | Shortcut.Alt));
	}

	[Fact]
	public void AFunctionKeyOnItsOwnIsFine()
	{
		Assert.Equal(ShortcutCheck.Usable, Check(120));
	}

	[Fact]
	public void ABareLetterIsRefusedBecauseItWouldStealTyping()
	{
		// The window sees each key before the control, so this would have fired in every text box in the manager.
		Assert.Equal(ShortcutCheck.WouldStealTyping, Check(LetterQ));
	}

	[Fact]
	public void ShiftAloneDoesNotMakeALetterSafe()
	{
		// Shift+Q is a capital Q in a text box.
		Assert.Equal(ShortcutCheck.WouldStealTyping, Check(LetterQ | Shortcut.Shift));
	}

	[Fact]
	public void AKeyAnotherActionHasIsRefusedAndNamesIt()
	{
		ShortcutCheck check = ShortcutRules.Check(F5, "Search", Shortcuts, out string? takenBy);

		Assert.Equal(ShortcutCheck.AlreadyTaken, check);
		Assert.Equal("Launch", takenBy);
	}

	[Fact]
	public void AnActionMayKeepItsOwnKey()
	{
		Assert.Equal(ShortcutCheck.Usable, ShortcutRules.Check(F5, "Launch", Shortcuts, out _));
	}
}
