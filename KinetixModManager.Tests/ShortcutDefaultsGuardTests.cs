using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Guards the default keyboard shortcuts against two commands claiming the same keys.
///
/// <para>
/// <c>IsShortcut</c> compares <c>e.KeyData</c> to each action's mapping and every match runs, so two actions on
/// one combination do not conflict in any way the compiler or the app can report — pressing the key silently
/// runs both, or runs the wrong one, depending on which the handler reaches first. The table is now around fifty
/// entries long and is edited by hand every time a command is added, which is exactly the shape of list where a
/// collision goes unnoticed until somebody reports that a key "stopped working".
/// </para>
///
/// <para>
/// Read out of the source rather than from <c>AppSettings</c> itself: the defaults live in a WinForms type
/// (<c>System.Windows.Forms.Keys</c>) that this project deliberately does not reference, so the table is parsed
/// the same way <see cref="SpokenStringGuardTests"/> parses <c>Loc.T</c> calls.
/// </para>
/// </summary>
public class ShortcutDefaultsGuardTests
{
	/// <summary>
	/// One <c>{ "Action", Keys.X | Keys.Shift }</c> entry from the defaults table: the action name, and its
	/// combination reduced to a form two spellings of the same keys share.
	/// </summary>
	private readonly record struct Binding(string Action, string Combination);

	[Fact]
	public void NoTwoCommandsClaimTheSameKeys()
	{
		var byCombination = new Dictionary<string, List<string>>(StringComparer.Ordinal);

		foreach (Binding binding in DefaultShortcuts())
		{
			// Keys.None means "no default", which several commands legitimately share — the shortcut manager is
			// where those get a key, if the user wants one.
			if (binding.Combination == "None") continue;

			if (!byCombination.TryGetValue(binding.Combination, out List<string>? actions))
				byCombination[binding.Combination] = actions = new List<string>();
			actions.Add(binding.Action);
		}

		var clashes = byCombination
			.Where(pair => pair.Value.Count > 1)
			.Select(pair => string.Join(" and ", pair.Value) + " are all on " + pair.Key)
			.ToList();

		Assert.True(clashes.Count == 0,
			"These commands share a default key combination, so pressing it runs more than one of them:\n  " +
			string.Join("\n  ", clashes));
	}

	[Fact]
	public void NoCommandIsGivenADefaultTwice()
	{
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var duplicates = new List<string>();

		foreach (Binding binding in DefaultShortcuts())
			if (!seen.Add(binding.Action))
				duplicates.Add(binding.Action);

		Assert.True(duplicates.Count == 0,
			"These commands appear more than once in the defaults table; the first one added wins and the rest " +
			"are dead entries:\n  " + string.Join("\n  ", duplicates));
	}

	[Fact]
	public void TheDefaultsTableWasActuallyFound()
	{
		// The two tests above pass vacuously if the parse returns nothing — which is what would happen if the
		// table were reformatted or moved. This makes that failure loud instead.
		List<Binding> bindings = DefaultShortcuts();

		Assert.True(bindings.Count > 30,
			"Only " + bindings.Count + " default shortcuts were read out of AppSettings.cs. The table has " +
			"probably been reformatted or moved, and these tests are no longer checking anything.");

		Assert.Contains(bindings, b => b.Action == "Manual");
		Assert.Contains(bindings, b => b.Action == "CurationMode");
	}

	/// <summary>
	/// Every <c>{ "Action", Keys.… }</c> pair in the defaults table in <c>AppSettings.InitializeDefaults</c>.
	///
	/// The combination is normalised by sorting its <c>Keys.</c> parts, so <c>Keys.J | Keys.Shift | Keys.Control</c>
	/// and <c>Keys.Control | Keys.Shift | Keys.J</c> are recognised as the same combination rather than passing as
	/// two different ones.
	/// </summary>
	private static List<Binding> DefaultShortcuts()
	{
		string source = File.ReadAllText(AppSettingsPath());

		int start = source.IndexOf("new Dictionary<string, Keys>", StringComparison.Ordinal);
		Assert.True(start >= 0, "Could not find the defaults table in AppSettings.cs.");

		var bindings = new List<Binding>();

		foreach (Match match in Regex.Matches(
			source[start..],
			@"\{\s*(?://[^\n]*\n\s*)*""(?<action>[A-Za-z]+)""\s*,\s*(?<keys>Keys\.[A-Za-z0-9]+(?:\s*\|\s*Keys\.[A-Za-z0-9]+)*)\s*\}"))
		{
			string combination = string.Join(
				" | ",
				match.Groups["keys"].Value
					.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
					.Select(part => part["Keys.".Length..])
					.OrderBy(part => part, StringComparer.Ordinal));

			bindings.Add(new Binding(match.Groups["action"].Value, combination));
		}

		return bindings;
	}

	private static string AppSettingsPath() =>
		Path.Combine(RepositoryRoot(), "KinetixModManager", "AppSettings.cs");

	private static string RepositoryRoot()
	{
		DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !File.Exists(Path.Combine(dir.FullName, "KinetixModManager.slnx")))
			dir = dir.Parent;

		Assert.True(dir != null, "Could not find the repository root from " + AppContext.BaseDirectory);
		return dir!.FullName;
	}
}
