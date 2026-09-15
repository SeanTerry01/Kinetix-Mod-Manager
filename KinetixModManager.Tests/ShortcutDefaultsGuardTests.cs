using System;
using System.Collections.Generic;
using System.Linq;
using KinetixManagerSettings = KinetixModManager.AppSettings;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Guards the default keyboard shortcuts against two commands claiming the same keys.
///
/// <para>
/// <c>IsShortcut</c> compares the pressed key to each action's mapping and every match runs, so two actions on
/// one combination do not conflict in any way the compiler or the app can report — pressing the key silently
/// runs both, or runs the wrong one, depending on which the handler reaches first. The table is around fifty
/// entries and is edited by hand every time a command is added, which is exactly the shape of list where a
/// collision goes unnoticed until somebody reports that a key "stopped working".
/// </para>
///
/// <para>
/// These used to read the table by <em>parsing AppSettings.cs with a regular expression</em>, because the
/// defaults were typed as <c>System.Windows.Forms.Keys</c> and this project deliberately does not reference
/// WinForms. Settings now live in the core, so the table is simply called — which is both shorter and
/// stronger: a reformat or a move can no longer make the guard quietly stop checking anything, and what is
/// asserted is the dictionary the program actually starts with rather than the text that produces it.
/// </para>
/// </summary>
public class ShortcutDefaultsGuardTests
{
	/// <summary>The shortcuts a fresh installation starts with.</summary>
	private static Dictionary<string, int> Defaults()
	{
		var settings = new KinetixManagerSettings();
		settings.InitializeDefaults();
		return settings.Shortcuts;
	}

	[Fact]
	public void NoTwoCommandsClaimTheSameKeys()
	{
		var byCombination = new Dictionary<int, List<string>>();

		foreach (var (action, combination) in Defaults())
		{
			// No key bound means "no default", which several commands legitimately share — the Shortcut
			// Manager is where those get a key, if the user wants one.
			if (combination == Shortcut.None) continue;

			if (!byCombination.TryGetValue(combination, out List<string>? actions))
				byCombination[combination] = actions = new List<string>();
			actions.Add(action);
		}

		var clashes = byCombination
			.Where(pair => pair.Value.Count > 1)
			.Select(pair => string.Join(" and ", pair.Value) + " are all on " + Shortcut.Describe(pair.Key))
			.ToList();

		Assert.True(clashes.Count == 0,
			"These commands share a default key combination, so pressing it runs more than one of them:\n  " +
			string.Join("\n  ", clashes));
	}

	[Fact]
	public void TheDefaultsTableIsActuallyThere()
	{
		// The test above passes vacuously on an empty table, which is what a bad merge would leave behind.
		Dictionary<string, int> defaults = Defaults();

		Assert.True(defaults.Count > 30,
			"Only " + defaults.Count + " default shortcuts were found. The table has probably been damaged, " +
			"and these tests are no longer checking anything.");

		Assert.Contains("Manual", defaults.Keys);
		Assert.Contains("CurationMode", defaults.Keys);
	}

	[Fact]
	public void TheDefaultsSurviveBeingInitialisedTwice()
	{
		// InitializeDefaults runs on every load and must only fill in what is missing. If it overwrote, a
		// user's remapped key would be reset to the default every time the manager started — which is the
		// sort of thing that gets reported as "it forgets my shortcuts" and is hard to see in the code.
		var settings = new KinetixManagerSettings();
		settings.InitializeDefaults();
		settings.Shortcuts["Manual"] = Shortcut.Letter('Z') | Shortcut.Control;

		settings.InitializeDefaults();

		Assert.Equal(Shortcut.Letter('Z') | Shortcut.Control, settings.Shortcuts["Manual"]);
	}

	[Fact]
	public void EveryDefaultIsAKeyAndNotJustModifiers()
	{
		// A combination of nothing but Ctrl and Shift can never be pressed, so an action bound to one would
		// silently have no shortcut at all.
		var modifiersOnly = Defaults()
			.Where(pair => pair.Value != Shortcut.None && Shortcut.KeyOf(pair.Value) == 0)
			.Select(pair => pair.Key)
			.ToList();

		Assert.True(modifiersOnly.Count == 0,
			"These commands are bound to modifier keys with no key to press:\n  " + string.Join("\n  ", modifiersOnly));
	}

	[Fact]
	public void AShortcutReadsBackAsSomethingAPersonWouldSay()
	{
		// It is read aloud far more often than it is looked at.
		Assert.Equal("F1", Shortcut.Describe(Shortcut.Function(1)));
		Assert.Equal("Ctrl + P", Shortcut.Describe(Shortcut.Letter('P') | Shortcut.Control));
		Assert.Equal("Ctrl + Shift + D", Shortcut.Describe(Shortcut.Letter('D') | Shortcut.Shift | Shortcut.Control));
	}

	[Fact]
	public void TheNumbersAreTheOnesEverySettingsFileAlreadyHolds()
	{
		// The whole reason a shortcut is an int rather than a new type of the manager's own: an installation
		// that upgrades has to read its own saved shortcuts back unchanged. These are the values
		// System.Windows.Forms.Keys uses, and they are not free to drift.
		Assert.Equal(112, Shortcut.Function(1));      // Keys.F1
		Assert.Equal(65, Shortcut.Letter('a'));       // Keys.A, whatever case it is written in
		Assert.Equal(0x10000, Shortcut.Shift);
		Assert.Equal(0x20000, Shortcut.Control);
		Assert.Equal(0x40000, Shortcut.Alt);
		Assert.Equal(131152, Shortcut.Letter('P') | Shortcut.Control);   // Keys.P | Keys.Control
	}
}
