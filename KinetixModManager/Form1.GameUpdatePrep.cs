using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// The "prepare for a game update / restore afterwards" pair (Mods menu, Skyrim SE / Fallout 4). A Steam or GOG
/// update overwrites the game folder, which can collide with the manager's deployed loose files and, worse, break
/// SKSE/F4SE. Preparing takes a safety snapshot and returns the game folder to vanilla (undeploying mods) so the
/// update installs cleanly; restoring re-deploys every enabled mod afterwards and runs the game-update guardian so
/// any script-extender mismatch is flagged. Pairs with <see cref="CheckGameUpdateGuardian"/>.
/// </summary>
public partial class Form1
{
	/// <summary>Snapshots + undeploys mods so a pending game update installs against a clean vanilla folder.</summary>
	private void PrepareForGameUpdate()
	{
		if (!IsBethesdaGame)
		{
			Speak(Loc.T("prep.notApplicable"));
			return;
		}
		string game = _settings.ActiveGame;
		string gameRoot = _settings.CurrentGamePath;
		if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot))
		{
			Speak(Loc.T("prep.noGamePath"));
			return;
		}

		var manifest = DeploymentManifest.Load(game);
		if (manifest.Deployed.Count == 0)
		{
			Speak(Loc.T("prep.alreadyVanilla"));
			return;
		}
		if (SpeakBox(Loc.T("prep.confirm", manifest.Deployed.Count), Loc.T("prep.confirmTitle"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
			return;

		CreateSafetyBackup(Loc.T("prep.reason"));
		SetStatus(Loc.T("prep.working"));
		int removed = ModFileSystem.PurgeDeployment(gameRoot, manifest, LogError);
		manifest.Save(game);
		_lastConflicts = new List<FileConflict>();
		RefreshModPriorityList();
		RefreshPluginOrderList();
		ResetStatus();

		_soundEngine.Play("load_complete");
		Speak(Loc.T("prep.done", removed));
	}

	/// <summary>Re-deploys every enabled mod after a game update, then runs the update guardian to flag SKSE/F4SE.</summary>
	private void RestoreAfterUpdate()
	{
		if (!IsBethesdaGame)
		{
			Speak(Loc.T("prep.notApplicable"));
			return;
		}
		if (SpeakBox(Loc.T("prep.restoreConfirm"), Loc.T("prep.restoreTitle"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
			return;

		SetStatus(Loc.T("prep.restoreWorking"));
		var forceAll = new HashSet<string>(
			_allInstalledMods.Where(m => !m.IsGroup && m.IsEnabled).Select(PriorityKey),
			StringComparer.OrdinalIgnoreCase);
		int conflicts = SyncBethesdaDeployment(forceAll).Count;
		SyncBethesdaPlugins();
		RefreshModPriorityList();
		RefreshPluginOrderList();
		ResetStatus();

		_soundEngine.Play("load_complete");
		Speak(Loc.T("prep.restoreDone", forceAll.Count, conflicts));

		// The exe version very likely changed during the update; let the guardian warn about SKSE/F4SE if so.
		CheckGameUpdateGuardian();
	}
}
