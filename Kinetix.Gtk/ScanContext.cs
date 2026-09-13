using System;
using System.Collections.Generic;
using KinetixModManager;

namespace KinetixModManager.GtkHead;

/// <summary>
/// The three remembered facts <see cref="ModScanner"/> asks for, with nothing remembered yet.
///
/// The spike has no settings file of its own, so the categories and notes a user has assigned come back
/// empty and the scan falls through to what it can work out from the mods themselves. That is the right
/// behaviour rather than a stub: a fresh install on Windows looks exactly the same.
/// </summary>
public sealed class ScanContext : IModScanContext
{
	private static readonly Dictionary<string, string> None = new();

	public required string CurrentGamePath { get; init; }
	public IReadOnlyDictionary<string, string> ModCategories => None;
	public IReadOnlyDictionary<string, string> ModNotes => None;

	public static ScanContext For(string gamePath) => new() { CurrentGamePath = gamePath };
}
