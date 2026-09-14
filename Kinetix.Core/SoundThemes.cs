using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// Which sound file to play, for the game that is loaded.
///
/// <para>
/// The manager's feedback is sound as much as speech, and the sounds are per game on purpose: a Skyrim
/// session and a Stardew session are told apart by ear, before anything is read out. That is why a theme is
/// chosen from the loaded game by default rather than picked in Settings — <see cref="ForGame"/> — and why
/// a user who does want to choose has to say so once (<c>AllowManualTheme</c>).
/// </para>
///
/// <para>
/// Adding a game's sounds is meant to be nothing but dropping files in: make
/// <c>sounds/&lt;theme&gt;/&lt;event&gt;/</c> and put an <c>.ogg</c> in it. Any event not authored yet falls
/// back to the Default theme's, so a half-finished pack is quieter than Default rather than silent — which
/// is the distinction that matters when the app's feedback IS the sound.
/// </para>
/// </summary>
public static class SoundThemes
{
	/// <summary>The theme every other theme falls back to, one event at a time.</summary>
	public const string DefaultTheme = "Default";

	/// <summary>
	/// Every named sound event, which is also the set of folders a complete theme holds. The logo folder is
	/// deliberately not here: it holds startup sounds chosen by file name, not one sound for one event.
	/// </summary>
	public static readonly IReadOnlyList<string> EventNames = new[]
	{
		"connect",
		"disconnect",
		"enable",
		"disable",
		"error",
		"loading_indicator",
		"load_complete",
	};

	/// <summary>
	/// The sound theme belonging to a game id, or <see cref="DefaultTheme"/> for a game that declares none
	/// and for no game at all.
	/// </summary>
	public static string ForGame(string? game) => GameProfiles.Find(game)?.SoundTheme ?? DefaultTheme;

	/// <summary>
	/// The file to play for <paramref name="name"/> in <paramref name="theme"/>, or null when neither the
	/// theme nor Default has one.
	///
	/// <para>
	/// The fall back to Default is per event and is decided on the <em>file</em>, not the folder. That is the
	/// whole point: a new game's theme starts as empty folders and gains sounds one at a time, and until this
	/// was judged on the file, an authored-but-empty folder meant the manager simply said nothing where it
	/// used to say something — a regression that no error would have reported, in the one channel a blind
	/// user cannot check for themselves.
	/// </para>
	/// </summary>
	public static string? Resolve(string themesPath, string theme, string name)
	{
		return FirstSoundIn(themesPath, theme, name)
			?? (string.Equals(theme, DefaultTheme, StringComparison.OrdinalIgnoreCase)
				? null
				: FirstSoundIn(themesPath, DefaultTheme, name));
	}

	/// <summary>
	/// The themes present on disk, in the order the Settings list should show them: Default first, because it
	/// is the one every other theme falls back to, then the rest alphabetically.
	/// </summary>
	public static IReadOnlyList<string> Installed(string themesPath)
	{
		if (!Directory.Exists(themesPath)) return Array.Empty<string>();

		try
		{
			return Directory.EnumerateDirectories(themesPath)
				.Select(Path.GetFileName)
				.Where(n => !string.IsNullOrEmpty(n))
				.Select(n => n!)
				.OrderBy(n => string.Equals(n, DefaultTheme, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
				.ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Sound", $"listing the sound themes in {themesPath}", ex);
			return Array.Empty<string>();
		}
	}

	/// <summary>The first <c>.ogg</c> in one theme's folder for one event, or null if there is none.</summary>
	private static string? FirstSoundIn(string themesPath, string theme, string name)
	{
		try
		{
			string dir = Path.Combine(themesPath, theme, name);
			if (!Directory.Exists(dir)) return null;

			// Ordered, so which sound plays does not depend on what order the filesystem happens to hand
			// the folder back in — the same theme should sound the same on two machines.
			return Directory.EnumerateFiles(dir, "*.ogg").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Sound", $"looking for the \"{name}\" sound in the {theme} theme", ex);
			return null;
		}
	}
}
