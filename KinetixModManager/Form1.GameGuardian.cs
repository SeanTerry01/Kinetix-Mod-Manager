using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// The game-update / script-extender guardian. Steam (or GOG) can update a Bethesda game's executable in the
/// background, and when it does, SKSE/F4SE and every DLL-based plugin built against the old runtime stop loading
/// — the classic "my game won't launch after an update" disaster, which nothing else in the manager catches.
///
/// The guardian records the game exe's version each time a Bethesda session loads and, on a later load, warns once
/// if that version changed since we last looked. It is intentionally a one-shot-per-change notice, not a persistent
/// nag: after warning it stores the new version, so it stays quiet until the next actual update.
/// </summary>
public partial class Form1
{
	/// <summary>
	/// Compares the active Bethesda game's current exe version against the last version the manager recorded and,
	/// if it changed (and a previous version was on record), warns that the script extender and DLL plugins likely
	/// need updating — offering to open the script-extender page when one is installed. Records the current version
	/// whenever it differs so the warning fires exactly once per update. No-ops for Stardew, an unreadable/missing
	/// exe (e.g. under Wine), or the first time a game is seen.
	/// </summary>
	private void CheckGameUpdateGuardian()
	{
		if (!IsBethesdaGame) return;

		string game = _settings.ActiveGame;
		string gamePath = _settings.CurrentGamePath;
		(int major, int minor, int build)? ver = ReadGameRuntimeVersion(game, gamePath);
		if (ver == null) return; // exe missing or unreadable (e.g. Wine/Linux) — nothing to compare

		string current = $"{ver.Value.major}.{ver.Value.minor}.{ver.Value.build}";
		_settings.LastSeenGameVersion.TryGetValue(game, out string? previous);

		// Persist the current version whenever it changed (or was never recorded) so the warning fires once per
		// actual update rather than every load.
		bool changed = !string.IsNullOrEmpty(previous) && !string.Equals(previous, current, StringComparison.Ordinal);
		if (!string.Equals(previous, current, StringComparison.Ordinal))
		{
			_settings.LastSeenGameVersion[game] = current;
			_settings.Save();
		}

		if (!changed) return; // first time we've seen this game, or the version is unchanged
		string previousVersion = previous!; // non-null whenever changed is true

		// The exe changed under us — almost always a Steam update. Alert the user before they try to launch.
		_soundEngine.Play("error");
		string productName = GameDisplayName(game);
		string seName = game == "SkyrimSE" ? "SKSE" : "F4SE";

		if (ModFileSystem.IsScriptExtenderInstalled(game, gamePath))
		{
			// Offer to open the script-extender page so the user can grab a build matching the new runtime.
			if (SpeakBox(Loc.T("guardian.updatedSe", productName, previousVersion, current, seName),
					Loc.T("guardian.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
			{
				string seId = game == "SkyrimSE" ? "30379" : "42147"; // SKSE64 / F4SE Nexus ids
				string url = $"https://www.nexusmods.com/{_nexusService.CurrentGameDomain}/mods/{seId}";
				try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
			}
		}
		else
		{
			SpeakBox(Loc.T("guardian.updatedNoSe", productName, previousVersion, current),
				Loc.T("guardian.title"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}
}
