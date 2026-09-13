using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using StardewMod = KinetixModManager.GameMod;

namespace KinetixModManager;

/// <summary>
/// The dependency tree view (what the selected mod requires and what requires it), the reverse-dependency
/// lookup used to warn before a delete, and the one-click resolver that finds and installs a mod's missing
/// required mods. All three adapt to the active game: Stardew uses manifest dependencies (UniqueIDs); Skyrim
/// and Fallout 4 use plugin masters (offline) plus each mod's online Nexus "Requirements" (best-effort).
/// </summary>
public partial class Form1
{
	/// <summary>Enumerates the plugin file paths (.esp/.esm/.esl) a Skyrim/Fallout 4 mod ships, IO errors ignored.</summary>
	private static IEnumerable<string> ModPluginFiles(StardewMod mod) => ModDependencies.PluginFiles(mod);

	// ---------------------------------------------------------------------
	// Dependency tree view
	// ---------------------------------------------------------------------

	/// <summary>
	/// Shows the dependency view for the selected installed mod: a "Requires" section (its dependencies and
	/// whether each is satisfied) and a "Required by" section (which other installed mods depend on it). Stardew
	/// reads manifest dependencies; Skyrim/Fallout 4 read plugin masters offline and append each mod's online
	/// Nexus "Requirements" when a key is present. Rows are actionable: Enter searches Discovery for a missing
	/// Stardew dependency, or opens a missing Nexus requirement's page. Reuses the shared accessible report dialog.
	/// </summary>
	private async void ShowDependencies()
	{
		if (listInstalled.SelectedItem is not StardewMod mod || mod.IsGroup)
		{
			Speak(Loc.T("deps.selectMod"));
			return;
		}

		var rows = new List<ReportRow>();
		rows.Add(new ReportRow { Text = Loc.T("deps.requiresHeader") });
		int requiresBefore = rows.Count;

		if (GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley))
		{
			foreach (ModDependency dep in mod.Dependencies)
			{
				string status = dep.IsPresent
					? (dep.IsEnabled ? (dep.IsNewEnough ? Loc.T("deps.statusOk") : Loc.T("deps.statusOutdated", dep.MinimumVersion ?? "")) : Loc.T("deps.statusDisabled"))
					: Loc.T("deps.statusMissing");
				string kind = dep.IsRequired ? Loc.T("modactions.depReq") : Loc.T("modactions.depOpt");
				rows.Add(new ReportRow
				{
					Text = Loc.T("deps.requiresRow", dep.UniqueId, status, kind),
					// The identifier is what the manifest declares, and it is not a name Nexus has ever heard of.
					SearchTerm = (!dep.IsPresent && dep.IsRequired)
						? ModNameMatch.SearchTermForIdentifier(dep.UniqueId)
						: null
				});
			}
		}
		else if (IsBethesdaGame)
		{
			// Offline: this mod's plugins and their non-base masters, marked satisfied when the master is present.
			var activePlugins = new HashSet<string>(
				ModFileSystem.ReadActivePlugins(_settings.ActiveGame, _settings.CurrentGamePath), StringComparer.OrdinalIgnoreCase);
			string dataDir = string.IsNullOrEmpty(_settings.CurrentGamePath) ? "" : Path.Combine(_settings.CurrentGamePath, "Data");
			foreach (string plugin in ModPluginFiles(mod))
			{
				foreach (string master in BethesdaPlugins.ReadPluginMasters(plugin))
				{
					if (BethesdaPlugins.IsBaseMaster(_settings.ActiveGame, master)) continue;
					bool active = activePlugins.Contains(master);
					bool present = active || (!string.IsNullOrEmpty(dataDir) && File.Exists(Path.Combine(dataDir, master)));
					string status = active ? Loc.T("deps.statusOk") : (present ? Loc.T("deps.statusDisabled") : Loc.T("deps.statusMissing"));
					rows.Add(new ReportRow { Text = Loc.T("deps.masterRow", Path.GetFileName(plugin), master, status) });
				}
			}

			// Online (best-effort): the mod's Nexus "Requirements" tab, flagged when not installed.
			if (!string.IsNullOrEmpty(mod.NexusID) && !string.IsNullOrEmpty(_settings.ApiKey))
			{
				var installedIds = new HashSet<string>(
					_allInstalledMods.Where(m => !m.IsGroup && !string.IsNullOrEmpty(m.NexusID)).Select(m => m.NexusID!),
					StringComparer.OrdinalIgnoreCase);
				SetStatus(Loc.T("deps.checkingReqs", mod.Name), speak: false);
				List<NexusService.ModRequirementInfo> reqs = await _nexusService.GetModRequirementsAsync(mod.NexusID!);
				ResetStatus();
				foreach (NexusService.ModRequirementInfo req in reqs)
				{
					if (req.External || IsVrOnlyRequirement(req.ModName)) continue;
					bool satisfied = !string.IsNullOrEmpty(req.ModId) && installedIds.Contains(req.ModId);
					string status = satisfied ? Loc.T("deps.statusOk") : Loc.T("deps.statusMissing");
					string url = !string.IsNullOrEmpty(req.Url) ? req.Url
						: $"https://www.nexusmods.com/{_nexusService.CurrentGameDomain}/mods/{req.ModId}";
					rows.Add(new ReportRow
					{
						Text = Loc.T("deps.nexusReqRow", req.ModName, status),
						OpenUrl = satisfied ? null : url
					});
				}
			}
		}

		if (rows.Count == requiresBefore)
			rows.Add(new ReportRow { Text = Loc.T("deps.requiresNone") });

		// Reverse: which installed mods depend on this one.
		rows.Add(new ReportRow { Text = Loc.T("deps.requiredByHeader") });
		List<string> dependents = GetReverseDependents(mod);
		if (dependents.Count == 0)
			rows.Add(new ReportRow { Text = Loc.T("deps.requiredByNone") });
		else
			foreach (string line in dependents)
				rows.Add(new ReportRow { Text = line });

		ShowReportDialog(Loc.T("deps.title", mod.Name), Loc.T("deps.header", mod.Name),
			Loc.T("deps.requiresNone"), rows, Loc.T("deps.actionHint"));
	}

	/// <summary>
	/// Finds the installed mods that declare a required dependency on <paramref name="target"/>. Stardew matches
	/// manifest dependencies by UniqueID; Skyrim/Fallout 4 match by plugin master (any other mod whose plugin lists
	/// one of the target's plugins as a master). Offline and best-effort — it never touches the network, so it is
	/// safe to run inside a delete confirmation. Returns one human-readable line per dependent.
	/// </summary>
	private List<string> GetReverseDependents(StardewMod target) =>
		ModDependencies.Dependents(target, _allInstalledMods, _settings.ActiveGame)
			.Select(d => d.IsDeclared
				? Loc.T("deps.reverseStardew", d.Mod.Name)
				: Loc.T("deps.reverseBethesda", d.Mod.Name, d.ViaPlugin, d.Master))
			.Distinct()
			.ToList();

	// ---------------------------------------------------------------------
	// One-click resolver
	// ---------------------------------------------------------------------

	/// <summary>
	/// Resolves the selected mod's missing required mods. On Skyrim/Fallout 4, missing Nexus "Requirements" that
	/// have a Nexus id are auto-downloaded and installed for premium accounts (reusing the collection install
	/// path); free accounts and off-Nexus requirements are queued to a manual-download report. On Stardew (where
	/// dependencies are UniqueIDs with no Nexus id) each missing required dependency is offered as a Discovery
	/// search. A single confirmation precedes any download.
	/// </summary>
	private async Task ResolveDependenciesAsync()
	{
		if (listInstalled.SelectedItem is not StardewMod mod || mod.IsGroup)
		{
			Speak(Loc.T("deps.selectMod"));
			return;
		}

		if (GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley))
		{
			var missing = mod.Dependencies.Where(d => d.IsRequired && !d.IsPresent).Select(d => d.UniqueId).Distinct().ToList();
			if (missing.Count == 0) { Speak(Loc.T("deps.resolveNoneMissing")); return; }

			// The row still NAMES the identifier, because that is what the mod's manifest asks for and what the
			// user will see written elsewhere. What it SEARCHES for is the mod's actual name.
			var rows = missing.Select(uid => new ReportRow
			{
				Text = Loc.T("deps.resolveMissingStardew", uid),
				SearchTerm = ModNameMatch.SearchTermForIdentifier(uid)
			}).ToList();
			ShowReportDialog(Loc.T("deps.resolveTitle"), Loc.T("deps.resolveHeaderStardew", mod.Name, missing.Count),
				Loc.T("deps.resolveNoneMissing"), rows, Loc.T("deps.resolveHintStardew"));
			return;
		}

		if (!IsBethesdaGame) { Speak(Loc.T("deps.resolveNotSupported")); return; }
		if (string.IsNullOrEmpty(mod.NexusID)) { Speak(Loc.T("deps.resolveNoNexusId", mod.Name)); return; }
		if (string.IsNullOrEmpty(_settings.ApiKey)) { Speak(Loc.T("deps.resolveNoLogin")); return; }

		// Gather the mod's Nexus requirements and keep the ones that aren't already installed.
		Speak(Loc.T("deps.checkingReqs", mod.Name));
		SetStatus(Loc.T("deps.checkingReqs", mod.Name), speak: false);
		List<NexusService.ModRequirementInfo> reqs = await _nexusService.GetModRequirementsAsync(mod.NexusID!);
		ResetStatus();

		var installedIds = new HashSet<string>(
			_allInstalledMods.Where(m => !m.IsGroup && !string.IsNullOrEmpty(m.NexusID)).Select(m => m.NexusID!),
			StringComparer.OrdinalIgnoreCase);
		string seId = GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.SkyrimSE) ? "30379" : "42147";

		var toGet = new List<NexusService.ModRequirementInfo>(); // has a Nexus id, not installed, downloadable
		var manualOnly = new List<NexusService.ModRequirementInfo>(); // off-Nexus / no id — user must fetch themselves
		foreach (NexusService.ModRequirementInfo req in reqs)
		{
			if (IsVrOnlyRequirement(req.ModName)) continue;
			if (!string.IsNullOrEmpty(req.ModId) && req.ModId == seId) continue; // script extender handled elsewhere
			if (!string.IsNullOrEmpty(req.ModId) && installedIds.Contains(req.ModId)) continue; // satisfied
			if (req.External || string.IsNullOrEmpty(req.ModId)) manualOnly.Add(req);
			else toGet.Add(req);
		}
		// De-dupe by mod id (a mod can list the same requirement twice).
		toGet = toGet.GroupBy(r => r.ModId, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

		if (toGet.Count == 0 && manualOnly.Count == 0) { Speak(Loc.T("deps.resolveNoneMissing")); return; }

		bool premium = _nexusService.IsPremium;
		string domain = _nexusService.CurrentGameDomain;
		string FilesUrl(NexusService.ModRequirementInfo m) => !string.IsNullOrEmpty(m.Url)
			? m.Url : $"https://www.nexusmods.com/{domain}/mods/{m.ModId}?tab=files";

		// Premium can auto-download the ones with a Nexus id; free accounts must fetch everything manually.
		int autoCount = premium ? toGet.Count : 0;
		int manualCount = manualOnly.Count + (premium ? 0 : toGet.Count);
		string prompt = Loc.T("deps.resolveConfirm", mod.Name, autoCount, manualCount);
		if (SpeakBox(prompt, Loc.T("deps.resolveTitle"), MessageBoxButtons.YesNo) != DialogResult.Yes)
		{
			Speak(Loc.T("deps.resolveCancelled"));
			return;
		}

		var installed = new List<string>();
		var installedNames = new List<string>();
		var manual = new List<NexusService.ModRequirementInfo>(manualOnly);
		var failed = new List<(NexusService.ModRequirementInfo Req, string Reason)>();

		if (premium)
		{
			for (int i = 0; i < toGet.Count; i++)
			{
				NexusService.ModRequirementInfo req = toGet[i];
				SetStatus(Loc.T("deps.resolveInstalling", i + 1, toGet.Count, req.ModName), speak: false);
				try
				{
					var stub = new GameMod { NexusID = req.ModId, Name = req.ModName };
					ProgressAnnouncer progress = NewProgress(Loc.T("deps.resolveProgressName", i + 1, toGet.Count, req.ModName), installing: false);
					string zip = await _nexusService.DownloadModUpdateAsync(stub, downloadsPath, progress);
					progress.Complete();
					string installedName = await ModFileSystem.ExtractModAsync(
						zip, _settings.CurrentModsPath, _allInstalledMods, backupsPath, _settings.MaxBackupsPerMod,
						_settings.ActiveGame, LogError, req.ModId, _nexusService, null, _settings.CurrentGamePath,
						fomodSelector: ShowFomodWizardAsync, installProgress: null, confirmOverwrite: null);
					installedNames.Add(installedName);
					installed.Add(req.ModName);
				}
				catch (OperationCanceledException)
				{
					manual.Add(req); // user cancelled the FOMOD wizard — leave it for a manual pass
				}
				catch (Exception ex)
				{
					failed.Add((req, FriendlyError(ex)));
					LogFailure("Resolve", $"Failed to install requirement {req.ModName}", ex);
				}
			}
		}
		else
		{
			manual.AddRange(toGet); // free account: everything goes to the manual queue
		}

		await RefreshModList(checkUpdates: false);
		ResetStatus();
		_soundEngine.Play(failed.Count == 0 ? "load_complete" : "error");

		// Report what happened, reusing the shared dialog. Manual/failed rows open the Nexus files page on Enter.
		var rows2 = new List<ReportRow>();
		foreach (string name in installed)
			rows2.Add(new ReportRow { Text = Loc.T("deps.resolveReportInstalled", name) });
		foreach (NexusService.ModRequirementInfo m in manual)
			rows2.Add(new ReportRow { Text = Loc.T("deps.resolveReportManual", m.ModName), OpenUrl = FilesUrl(m) });
		foreach ((NexusService.ModRequirementInfo m, string reason) in failed)
			rows2.Add(new ReportRow { Text = Loc.T("deps.resolveReportFailed", m.ModName, reason), OpenUrl = FilesUrl(m) });

		string header = Loc.T("deps.resolveReportHeader", mod.Name, installed.Count, manual.Count, failed.Count);
		string? hint = (manual.Count > 0 || failed.Count > 0) ? Loc.T("deps.resolveReportHint") : null;
		ShowReportDialog(Loc.T("deps.resolveTitle"), header, Loc.T("deps.resolveNoneMissing"), rows2, hint);
	}
}
