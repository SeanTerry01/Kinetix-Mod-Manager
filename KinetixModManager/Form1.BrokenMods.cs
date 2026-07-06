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
		// A finding-level advisory spoken as context in the opening announcement (see the loose-files check below),
		// so its row can stay a short, scannable list of the affected mods instead of a spoken paragraph.
		string? openingNote = null;

		if (_settings.ActiveGame == "StardewValley")
		{
			var mods = _allInstalledMods.Where(m => !m.IsGroup && !string.IsNullOrEmpty(m.UniqueId)).ToList();
			if (mods.Count == 0)
			{
				ShowReportDialog(Loc.T("broken.title"), Loc.T("broken.header", GameDisplayName()), Loc.T("broken.none"), rows, null,
					listName: Loc.T("broken.listName"));
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

			// Loose-file loading (archive invalidation) off while loose-file mods are installed is a classic silent
			// failure: the game ships them but ignores them, so nothing appears to change. Flag it first, naming the
			// mods. Applies only where the toggle is real (Fallout 4) — ArchiveInvalidationIniPath is null for games
			// that always load loose files.
			if (ModFileSystem.ArchiveInvalidationIniPath(_settings.ActiveGame) != null &&
				!ModFileSystem.IsArchiveInvalidationEnabled(_settings.ActiveGame))
			{
				var looseFileMods = enabled.Where(ModHasLooseFiles).ToList();
				if (looseFileMods.Count > 0)
				{
					// The explanation and fix are the same for every affected mod, so speak them once as context
					// (openingNote) and make the row just the list of mods, keeping it short to arrow through.
					openingNote = Loc.T("broken.looseFilesOff");
					rows.Insert(0, new ReportRow { Text = string.Join(", ", looseFileMods.Select(m => m.Name)) });
				}
			}
		}
		else
		{
			ShowReportDialog(Loc.T("broken.title"), Loc.T("broken.header", GameDisplayName()), Loc.T("broken.noData"), rows, null,
				listName: Loc.T("broken.listName"));
			return;
		}

		// Only show the "Press Enter to open the Nexus page" hint when a row actually has that action (Stardew rows
		// carry an OpenUrl; Bethesda incompatibility/warning/loose-file rows don't), so Fallout 4 doesn't get told
		// to press Enter on a "Stardew mod" that does nothing.
		string? hint = rows.Any(r => !string.IsNullOrEmpty(r.OpenUrl)) ? Loc.T("broken.actionHint") : null;
		ShowReportDialog(Loc.T("broken.title"), Loc.T("broken.header", GameDisplayName()), Loc.T("broken.none"), rows, hint,
			listName: Loc.T("broken.listName"), openingNote: openingNote);
	}

	/// <summary>
	/// True when the mod ships loose game assets — files the engine only loads with archive invalidation on. Judged
	/// by recognized asset extensions (textures, meshes, materials, audio, scripts, interface); plugins and packed
	/// BA2 archives don't count, so a plugin-only or fully-packed mod is not flagged. Best-effort; errors mean false.
	/// </summary>
	private static bool ModHasLooseFiles(StardewMod m)
	{
		try
		{
			return Directory.EnumerateFiles(m.FolderPath, "*.*", SearchOption.AllDirectories)
				.Any(f => LooseAssetExtensions.Contains(Path.GetExtension(f)));
		}
		catch { return false; }
	}

	/// <summary>Extensions that indicate a loose game asset (lower-case, with the leading dot).</summary>
	private static readonly HashSet<string> LooseAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".dds", ".nif", ".hkx", ".bgsm", ".bgem", ".tri", ".tga",
		".wav", ".xwm", ".fuz", ".lip", ".pex", ".swf",
	};
}
