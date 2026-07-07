using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// Plugin-limit awareness for Skyrim SE / Fallout 4. The engine addresses regular plugins with a one-byte index,
/// so it loads at most 255 of them (0x00–0xFE; 0xFF is reserved) — and the implicit base-game/DLC masters, plus
/// every enabled mod's regular plugin, all count toward that ceiling. ESL / light-flagged plugins load into a
/// separate container (index 0xFE) with room for 4096, so they are tracked as their own pool. Going over the
/// regular limit is a classic hard failure: the game silently drops plugins or won't launch. This surfaces the
/// current usage on demand, warns automatically as the cap approaches, and feeds a finding into the Health Check.
/// </summary>
public partial class Form1
{
	private const int RegularPluginCap = 255;   // usable regular indices 0x00–0xFE (0xFF reserved)
	private const int RegularPluginNear = 250;  // start warning within the last handful of slots
	private const int LightPluginCap = 4096;    // the 0xFE light container: indices 0x000–0xFFF
	private const int LightPluginNear = 4000;

	/// <summary>A snapshot of how many regular and light plugin slots the active load order is using.</summary>
	private readonly struct PluginSlotUsage
	{
		public int RegularUsed { get; init; }
		public int LightUsed { get; init; }
		public int RegularRemaining => Math.Max(0, RegularPluginCap - RegularUsed);
		public bool RegularOver => RegularUsed > RegularPluginCap;
		public bool RegularNear => RegularUsed >= RegularPluginNear; // true once over, too
		public bool LightOver => LightUsed > LightPluginCap;
		public bool LightNear => LightUsed >= LightPluginNear;
		/// <summary>True when anything is worth warning about (regular or light pool near/over its cap).</summary>
		public bool AnyConcern => RegularNear || LightNear;
	}

	/// <summary>
	/// Counts the plugin slots the active Bethesda load order uses. Regular = present base-game/DLC masters plus
	/// every non-light plugin in the order; light = the ESL / light-flagged plugins, which use a separate pool.
	/// The light classification comes from the same TES4-header flags the Plugin Order list uses (so an ESL-flagged
	/// .esp is correctly counted as light), falling back to the extension when a plugin's flags aren't cached.
	/// </summary>
	private PluginSlotUsage GetPluginSlotUsage()
	{
		string game = _settings.ActiveGame;
		int regular = CountBaseMastersPresent(game);
		int light = 0;

		if (_settings.PluginOrder.TryGetValue(game, out List<string>? order) && order != null)
		{
			foreach (string name in order)
			{
				bool isLight = _pluginClass.TryGetValue(name, out var f)
					? f.Light
					: ModFileSystem.ReadPluginFlags(name).IsLight;
				if (isLight) light++;
				else regular++;
			}
		}

		return new PluginSlotUsage { RegularUsed = regular, LightUsed = light };
	}

	/// <summary>
	/// How many implicit base-game/DLC masters are actually installed — they occupy regular slots but never appear
	/// in the Plugin Order list. Counts files present in the game's Data folder (DLC ownership varies, especially
	/// on Fallout 4); if the game path is unknown, falls back to the full set so the estimate errs high, not low.
	/// </summary>
	private int CountBaseMastersPresent(string game)
	{
		IReadOnlyCollection<string> baseMasters = ModFileSystem.BaseMasters(game);
		string gameRoot = _settings.CurrentGamePath;
		if (string.IsNullOrEmpty(gameRoot)) return baseMasters.Count;

		string dataDir = Path.Combine(gameRoot, "Data");
		int count = 0;
		foreach (string name in baseMasters)
		{
			try { if (File.Exists(Path.Combine(dataDir, name))) count++; }
			catch { count++; } // unreadable path: assume present rather than undercount toward the cap
		}
		return count;
	}

	/// <summary>
	/// Speaks the current plugin-slot usage on demand (Mods menu / shortcut): how many regular and light slots are
	/// in use, how many regular slots remain, and a warning when either pool is near or over its cap. Only meaningful
	/// for Skyrim SE / Fallout 4; other games are told it doesn't apply.
	/// </summary>
	private void AnnouncePluginSlotUsage()
	{
		if (!IsBethesdaGame)
		{
			Speak(Loc.T("pluginlimit.notApplicable"));
			return;
		}

		PluginSlotUsage u = GetPluginSlotUsage();
		string msg = Loc.T("pluginlimit.summary", u.RegularUsed, RegularPluginCap, u.RegularRemaining, u.LightUsed, LightPluginCap);
		string? warn = PluginLimitWarning(u);
		if (warn != null) msg += " " + warn;
		Speak(msg);
	}

	/// <summary>The single most-severe warning phrase for a usage snapshot, or null when nothing is near a cap.</summary>
	private static string? PluginLimitWarning(PluginSlotUsage u) =>
		u.RegularOver ? Loc.T("pluginlimit.warnOver")
		: u.RegularNear ? Loc.T("pluginlimit.warnNear", u.RegularRemaining)
		: u.LightOver ? Loc.T("pluginlimit.warnLightOver")
		: u.LightNear ? Loc.T("pluginlimit.warnLightNear")
		: null;

	/// <summary>Games for which the plugin-limit near/over warning has already been spoken this session, so entering
	/// the Plugin Order tab repeatedly doesn't nag. Cleared for a game once its usage drops back under the threshold,
	/// so re-approaching the cap warns again.</summary>
	private readonly HashSet<string> _pluginLimitWarnedGames = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// GotFocus handler for the Plugin Order list: when usage is near or over a cap, speaks the warning once per
	/// approach (guarded by <see cref="_pluginLimitWarnedGames"/>) so a user managing plugins hears the danger on
	/// arrival without asking, but isn't nagged on every focus/arrow. Runs alongside <see cref="List_Enter"/>; the
	/// short delay lets that finish announcing the list position first, then the warning is queued after it.
	/// </summary>
	private void ListPluginOrder_WarnOnFocus(object? sender, EventArgs e)
	{
		if (!IsBethesdaGame) return;
		string game = _settings.ActiveGame;
		PluginSlotUsage u = GetPluginSlotUsage();
		if (!u.AnyConcern)
		{
			_pluginLimitWarnedGames.Remove(game); // back under the threshold — re-arm the warning
			return;
		}
		if (!_pluginLimitWarnedGames.Add(game)) return; // already warned this approach
		string? warn = PluginLimitWarning(u);
		if (warn == null) return;

		var timer = new System.Windows.Forms.Timer { Interval = 350 };
		timer.Tick += (s, _) =>
		{
			timer.Stop();
			timer.Dispose();
			Speak(warn, interrupt: false);
		};
		timer.Start();
	}

	/// <summary>
	/// A Health Check finding for the plugin limit, or null when both pools are comfortably under their caps.
	/// Stateless and re-runnable, so it slots straight into the aggregated Setup Health Check.
	/// </summary>
	private ReportRow? GatherPluginLimitFinding()
	{
		if (!IsBethesdaGame) return null;
		PluginSlotUsage u = GetPluginSlotUsage();
		if (u.RegularOver) return new ReportRow { Text = Loc.T("pluginlimit.rowOver", u.RegularUsed, RegularPluginCap) };
		if (u.RegularNear) return new ReportRow { Text = Loc.T("pluginlimit.rowNear", u.RegularUsed, RegularPluginCap, u.RegularRemaining) };
		if (u.LightOver) return new ReportRow { Text = Loc.T("pluginlimit.rowLightOver", u.LightUsed, LightPluginCap) };
		if (u.LightNear) return new ReportRow { Text = Loc.T("pluginlimit.rowLightNear", u.LightUsed, LightPluginCap) };
		return null;
	}
}
