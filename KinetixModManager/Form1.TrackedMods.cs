using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// Nexus "Tracked Mods" sync (Mods menu / shortcut). Pulls the mods the user tracks on Nexus for the active game,
/// cross-references them against the mods Nexus updated recently, and surfaces the actionable ones: tracked mods
/// that aren't installed, and installed tracked mods with a newer version available. This catches updates for mods
/// the user is interested in but hasn't installed yet — something the normal installed-mod update check can't see.
/// Enter opens a mod's Nexus files page. It uses two bulk API calls (tracked list + recently-updated list) plus a
/// detail lookup only for the small recently-updated-and-tracked subset, so it stays light on the API quota.
/// </summary>
public partial class Form1
{

	private async Task ShowTrackedMods()
	{
		if (_settings.ActiveGame == "None")
		{
			Speak(Loc.T("tracked.noGame"));
			return;
		}
		if (string.IsNullOrEmpty(_settings.ApiKey))
		{
			Speak(Loc.T("tracked.needConnect"));
			return;
		}
		if (!_nexusService.IsValidated)
		{
			SetStatus(Loc.T("status.connecting"));
			if (!await ValidateNexusConnection())
			{
				ResetStatus();
				Speak(Loc.T("tracked.needConnect"));
				return;
			}
		}

		SetStatus(Loc.T("tracked.checking"), speak: true);
		string domain = _nexusService.CurrentGameDomain;

		List<int> tracked = await _nexusService.GetTrackedModIdsAsync(domain);
		if (tracked.Count == 0)
		{
			ResetStatus();
			Speak(Loc.T("tracked.none"));
			return;
		}

		HashSet<int> updated = await _nexusService.GetRecentlyUpdatedModIdsAsync("1m");
		var recent = tracked.Where(updated.Contains).ToList();

		// Map installed Nexus ids to their installed version, so an installed tracked mod is only flagged when a
		// genuinely newer version is available (not merely because Nexus recorded some activity).
		var installedByNexusId = _allInstalledMods
			.Where(m => !m.IsGroup && !string.IsNullOrEmpty(m.NexusID))
			.GroupBy(m => m.NexusID!, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

		var rows = new List<TrackedRow>();
		for (int i = 0; i < recent.Count; i++)
		{
			SetStatus(Loc.T("tracked.fetching", i + 1, recent.Count), speak: false);
			int id = recent[i];
			JObject? details = await _nexusService.GetModDetailsAsync(id.ToString());
			if (details == null) continue;

			string name = (string?)details["name"] ?? id.ToString();
			string latest = (string?)details["version"] ?? "";
			DateTime? when = ((long?)details["updated_timestamp"]) is long ts && ts > 0
				? DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime
				: null;
			string whenText = when.HasValue ? when.Value.ToString("g") : Loc.T("tracked.recently");

			if (installedByNexusId.TryGetValue(id.ToString(), out GameMod? installed))
			{
				// Installed already: only actionable if the tracked mod now has a newer version.
				if (!IsNewerVersion(installed.Version, latest)) continue;
				rows.Add(new TrackedRow
				{
					ModId = id,
					Installed = true,
					Summary = Loc.T("tracked.rowUpdate", name, installed.Version, latest, whenText)
				});
			}
			else
			{
				rows.Add(new TrackedRow
				{
					ModId = id,
					Installed = false,
					Summary = Loc.T("tracked.rowNotInstalled", name, latest, whenText)
				});
			}
		}
		ResetStatus();

		// Not-installed first (a brand-new mod to grab), then available updates; newest wording aside, keep a stable
		// order the user can rely on.
		rows = rows.OrderBy(r => r.Installed).ToList();

		if (rows.Count == 0)
		{
			Speak(Loc.T("tracked.upToDate", tracked.Count));
			return;
		}
		ShowTrackedModsDialog(domain, tracked.Count, rows);
	}

	private void ShowTrackedModsDialog(string domain, int trackedTotal, List<TrackedRow> rows)
	{
		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("tracked.title"), (container, closeView) =>
			{
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			layout.Controls.Add(new Label { Text = Loc.T("tracked.header", GameDisplayName()), AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8) }, 0, 0);

			var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = Loc.T("tracked.listName"), IntegralHeight = false, HorizontalScrollbar = true };
			foreach (TrackedRow r in rows) list.Items.Add(r);
			if (list.Items.Count > 0) list.SelectedIndex = 0;
			layout.Controls.Add(list, 0, 1);
			container.Controls.Add(layout);

			WireAccessibleDialogList(list);
			// Escape is handled by the view itself (see Form1.InlineView).
			list.KeyDown += (_, e) =>
			{
				if (e.KeyCode != Keys.Enter || list.SelectedItem is not TrackedRow row) return;
				e.Handled = e.SuppressKeyPress = true;
				try
				{
					string url = $"https://www.nexusmods.com/{domain}/mods/{row.ModId}?tab=files";
					Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
				}
				catch (Exception ex) { DiagnosticLog.WriteException("Tracked", "reading the tracked-mods list from Nexus", ex); }
			};

			return list;
		},
		// Through the hint, and without the old heading clause ("Tracked mods … for <game>."), which repeated the
		// title. The summary that follows already says how many need attention and why.
		hint: Loc.T("tracked.summary", rows.Count, rows.Count(r => !r.Installed), rows.Count(r => r.Installed))
			+ " " + Loc.T("tracked.actionHint"));
	}
}
