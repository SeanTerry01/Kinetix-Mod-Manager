using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using StardewMod = KinetixModManager.GameMod;

namespace KinetixModManager;

/// <summary>
/// The "known broken mods" check. It cross-references installed mods against a community-maintained list to warn
/// about ones that are broken, abandoned, obsolete, or incompatible — problems no Nexus update would surface.
/// Stardew Valley uses the official SMAPI compatibility list; Skyrim/Fallout 4 use the LOOT masterlist's curated
/// incompatibilities and warnings. Both are best-effort and offline-tolerant: no data simply means no warnings.
/// </summary>
public partial class Form1
{
	/// <summary>
	/// Checks the installed mods against the active game's community compatibility data and shows an accessible
	/// report of anything flagged as broken/abandoned/obsolete (Stardew) or incompatible/warned (Skyrim/Fallout 4).
	/// Runs the lookup with a spoken progress status; an empty result reports that nothing is known to be broken.
	/// </summary>
	private async Task ShowBrokenModsReport()
	{
		if (_settings.ActiveGame == "None")
		{
			Speak(Loc.T("broken.noGame"));
			return;
		}

		var rows = new List<ReportRow>();
		string domain = _nexusService.CurrentGameDomain;

		if (_settings.ActiveGame == "StardewValley")
		{
			var mods = _allInstalledMods.Where(m => !m.IsGroup && !string.IsNullOrEmpty(m.UniqueId)).ToList();
			if (mods.Count == 0)
			{
				ShowReportDialog(Loc.T("broken.title"), Loc.T("broken.header", GameDisplayName()), Loc.T("broken.none"), rows, null);
				return;
			}

			SetStatus(Loc.T("broken.checking"), speak: true);
			Dictionary<string, SmapiCompatibility.Result> compat =
				await SmapiCompatibility.CheckAsync(mods.Select(m => m.UniqueId), LogError);
			ResetStatus();

			foreach (StardewMod mod in mods)
			{
				if (!compat.TryGetValue(mod.UniqueId, out SmapiCompatibility.Result? r) || !r.IsProblem) continue;
				string summary = RichTextToPlain(r.Summary);
				rows.Add(new ReportRow
				{
					Text = Loc.T("broken.stardewRow", mod.Name, r.Status, summary),
					OpenUrl = string.IsNullOrEmpty(mod.NexusID) ? null : $"https://www.nexusmods.com/{domain}/mods/{mod.NexusID}"
				});
			}
		}
		else if (IsBethesdaGame)
		{
			SetStatus(Loc.T("broken.checking"), speak: true);
			LootMasterlist? ml = await LootMasterlist.LoadAsync(_settings.ActiveGame, LogError);
			ResetStatus();
			if (ml == null)
			{
				ShowReportDialog(Loc.T("broken.title"), Loc.T("broken.header", GameDisplayName()), Loc.T("broken.noData"), rows, null);
				return;
			}

			var enabled = _allInstalledMods.Where(m => !m.IsGroup && m.IsEnabled).ToList();
			// Every plugin this load order actually ships, and the subset that's active (loads in game).
			var installedPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (StardewMod m in enabled)
				foreach (string p in ModPluginFiles(m))
					installedPlugins.Add(Path.GetFileName(p));
			var activePlugins = new HashSet<string>(
				ModFileSystem.ReadActivePlugins(_settings.ActiveGame), StringComparer.OrdinalIgnoreCase);

			// Map a plugin back to the mod that ships it, for friendlier report lines.
			var ownerByPlugin = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (StardewMod m in enabled)
				foreach (string p in ModPluginFiles(m))
					ownerByPlugin[Path.GetFileName(p)] = m.Name;
			string Owner(string plugin) => ownerByPlugin.TryGetValue(plugin, out string? n) ? n : plugin;

			foreach (string plugin in activePlugins)
			{
				// Incompatibilities: only a real problem when the conflicting plugin is ALSO installed.
				if (ml.PluginIncompatibilities.TryGetValue(plugin, out List<string>? incs))
					foreach (string other in incs.Where(installedPlugins.Contains))
						rows.Add(new ReportRow { Text = Loc.T("broken.incRow", Owner(plugin), plugin, other) });

				// Unconditional curated warnings/errors for this plugin.
				if (ml.PluginWarnings.TryGetValue(plugin, out List<string>? warns))
					foreach (string w in warns)
						rows.Add(new ReportRow { Text = Loc.T("broken.warnRow", Owner(plugin), plugin, w) });
			}

			// Collapse identical lines (an incompatibility can be listed from both sides).
			rows = rows.GroupBy(r => r.Text).Select(g => g.First()).ToList();
		}
		else
		{
			ShowReportDialog(Loc.T("broken.title"), Loc.T("broken.header", GameDisplayName()), Loc.T("broken.noData"), rows, null);
			return;
		}

		string? hint = rows.Count > 0 ? Loc.T("broken.actionHint") : null;
		ShowReportDialog(Loc.T("broken.title"), Loc.T("broken.header", GameDisplayName()), Loc.T("broken.none"), rows, hint);
	}
}
