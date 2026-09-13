using System;
using System.Collections.Generic;

namespace KinetixModManager;

/// <summary>
/// The handful of remembered facts a scan needs: where the game is, and what the user has since said about
/// individual mods.
///
/// Three members rather than the whole settings object, because that is genuinely all the scan reads — and
/// because <c>AppSettings</c> cannot come to the core as it stands: it stores keyboard shortcuts as
/// <c>System.Windows.Forms.Keys</c>. Naming the three turns a dependency on the app into a dependency on
/// what the app happens to know, which is the difference between the scanner being portable and not.
/// </summary>
public interface IModScanContext
{
	/// <summary>The active game's install folder, for the layouts that deploy into it.</summary>
	string CurrentGamePath { get; }

	/// <summary>The user's own category for a mod, keyed by its unique id. Overrides anything detected.</summary>
	IReadOnlyDictionary<string, string> ModCategories { get; }

	/// <summary>The user's own note against a mod, keyed by its unique id.</summary>
	IReadOnlyDictionary<string, string> ModNotes { get; }
}
