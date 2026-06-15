using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using StardewMod = KinetixModManager.GameMod;

namespace KinetixModManager;

/// <summary>
/// Skyrim SE / Fallout 4 load-order support: the per-game mod priority order that decides which mod's
/// loose files win conflicts, the conflict-aware deployment sync that applies it, and the reorderable
/// "Mod Priority" tab. Stardew Valley is unaffected (its mods deploy in place with no Data folder).
/// </summary>
public partial class Form1
{
	/// <summary>True while a Skyrim SE or Fallout 4 session is loaded (the games that use load order).</summary>
	private bool IsBethesdaGame =>
		_settings.ActiveGame == "SkyrimSE" || _settings.ActiveGame == "Fallout4";

	/// <summary>File conflicts detected by the most recent <see cref="SyncBethesdaDeployment"/> pass.</summary>
	private List<FileConflict> _lastConflicts = new List<FileConflict>();

	/// <summary>Suppresses a load-order list's own position announcement during a programmatic reorder.</summary>
	private bool _suppressPrioritySpeak;

	/// <summary>Master/light classification of each managed plugin, from the most recent plugin sync.</summary>
	private Dictionary<string, (bool Master, bool Light)> _pluginClass =
		new Dictionary<string, (bool, bool)>(StringComparer.OrdinalIgnoreCase);

	/// <summary>Source file path of each managed plugin (mod folder or game Data), from the last plugin sync.</summary>
	private Dictionary<string, string> _pluginPaths =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// The stable identity of a Skyrim/Fallout mod for priority purposes: its folder name without the
	/// leading dot that marks a disabled folder, so enabling/disabling never changes its priority slot.
	/// </summary>
	private static string PriorityKey(StardewMod m)
	{
		string folder = Path.GetFileName(m.FolderPath.TrimEnd(Path.DirectorySeparatorChar));
		return folder.StartsWith(".") ? folder.Substring(1) : folder;
	}

	/// <summary>
	/// Brings the active game's saved priority list in line with what is actually installed: drops mods
	/// that no longer exist and inserts newly installed mods at the top (highest priority, so the mod you
	/// just installed wins conflicts by default). Persists any change. No-op outside Skyrim/Fallout 4.
	/// </summary>
	private void EnsureModPriorityList()
	{
		if (!IsBethesdaGame) return;
		string game = _settings.ActiveGame;
		if (!_settings.ModPriority.TryGetValue(game, out List<string>? order) || order == null)
		{
			order = new List<string>();
			_settings.ModPriority[game] = order;
		}

		var present = _allInstalledMods
			.Where(m => !m.IsGroup)
			.Select(PriorityKey)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		bool changed = order.RemoveAll(n => !present.Contains(n, StringComparer.OrdinalIgnoreCase)) > 0;
		if (order.Count == 0)
		{
			// First-time population: seed alphabetically (highest priority at the top) so the initial order
			// is predictable. Mod priority only affects loose-file conflicts, so the exact seed is cosmetic.
			foreach (string name in present.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
				order.Add(name);
			changed = changed || present.Count > 0;
		}
		else
		{
			// Incremental: a newly installed mod takes the highest priority (top) so it wins conflicts by default.
			foreach (string name in present)
				if (!order.Contains(name, StringComparer.OrdinalIgnoreCase)) { order.Insert(0, name); changed = true; }
		}
		if (changed) _settings.Save();
	}

	/// <summary>Returns the enabled mods as (folder name, folder path) pairs ordered highest priority first.</summary>
	private List<(string Name, string FolderPath)> GetOrderedEnabledMods()
	{
		var result = new List<(string, string)>();
		if (!IsBethesdaGame) return result;
		EnsureModPriorityList();
		var byKey = _allInstalledMods
			.Where(m => !m.IsGroup && m.IsEnabled)
			.GroupBy(PriorityKey, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
		foreach (string name in _settings.ModPriority[_settings.ActiveGame])
		{
			if (byKey.TryGetValue(name, out StardewMod? mod))
				result.Add((name, mod.FolderPath));
		}
		return result;
	}

	/// <summary>
	/// Reconciles the game folder so each file is provided by its highest-priority enabled mod, records
	/// the resulting conflicts in <see cref="_lastConflicts"/>, and persists the deployment manifest.
	/// Safe to call for any game; does nothing unless a Skyrim/Fallout 4 session with a known game path
	/// is loaded.
	/// </summary>
	private List<FileConflict> SyncBethesdaDeployment(HashSet<string>? forceRelink = null)
	{
		if (!IsBethesdaGame) return new List<FileConflict>();
		string game = _settings.ActiveGame;
		string gameRoot = _settings.CurrentGamePath;
		if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return _lastConflicts;

		var ordered = GetOrderedEnabledMods();
		var manifest = DeploymentManifest.Load(game);
		_lastConflicts = ModFileSystem.SyncDeployment(gameRoot, ordered, manifest, LogError, forceRelink);
		manifest.Save(game);
		return _lastConflicts;
	}

	/// <summary>
	/// Manual safety-net command (Mods menu): forces a full re-link of every enabled mod's files into the
	/// game folder and rewrites plugins.txt, after confirmation. Useful if the game's Data folder ever gets
	/// into an inconsistent state. It never deletes any installed mod, and only removes deployed files the
	/// manifest already tracks — it does not touch files it has no record of.
	/// </summary>
	private void RebuildDeployment()
	{
		if (!IsBethesdaGame)
		{
			Speak(Loc.T("loadorder.rebuildNotApplicable"));
			return;
		}
		if (MessageBox.Show(Loc.T("loadorder.rebuildConfirm"), Loc.T("loadorder.rebuildTitle"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
			return;

		SetStatus(Loc.T("loadorder.rebuilding"));
		var forceAll = new HashSet<string>(
			_allInstalledMods.Where(m => !m.IsGroup && m.IsEnabled).Select(PriorityKey),
			StringComparer.OrdinalIgnoreCase);
		List<FileConflict> conflicts = SyncBethesdaDeployment(forceAll);
		SyncBethesdaPlugins();
		RefreshModPriorityList();
		RefreshPluginOrderList();

		_soundEngine.Play("connect");
		Speak(Loc.T("loadorder.rebuilt", forceAll.Count, conflicts.Count));
	}

	/// <summary>One row of the Mod Priority list: a mod and its current conflict standing.</summary>
	private sealed class PriorityEntry
	{
		public string Key = "";
		public string Display = "";
		public bool Enabled;
		public string Summary = "";
		public override string ToString() => Summary;
	}

	/// <summary>
	/// Rebuilds the Mod Priority list from the saved order and the latest conflict scan. Each row reads
	/// its mod name, enabled state, and how many files it overrides or is overridden in, so a screen-reader
	/// user can judge a mod's standing by ear. The list order is the priority order (top = wins).
	/// </summary>
	private void RefreshModPriorityList()
	{
		if (listModPriority == null || !IsBethesdaGame) return;
		EnsureModPriorityList();

		var overrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		var overridden = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (FileConflict c in _lastConflicts)
		{
			overrides[c.Winner] = overrides.GetValueOrDefault(c.Winner) + 1;
			foreach (string l in c.Losers)
				overridden[l] = overridden.GetValueOrDefault(l) + 1;
		}

		var byKey = _allInstalledMods
			.Where(m => !m.IsGroup)
			.GroupBy(PriorityKey, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

		string? restoreKey = (listModPriority.SelectedItem as PriorityEntry)?.Key;

		_suppressPrioritySpeak = true;
		listModPriority.BeginUpdate();
		listModPriority.Items.Clear();
		foreach (string name in _settings.ModPriority[_settings.ActiveGame])
		{
			byKey.TryGetValue(name, out StardewMod? mod);
			var entry = new PriorityEntry
			{
				Key = name,
				Display = mod?.Name ?? name,
				Enabled = mod?.IsEnabled ?? false
			};

			int wins = overrides.GetValueOrDefault(name);
			int loses = overridden.GetValueOrDefault(name);
			string conflict = (wins, loses) switch
			{
				(> 0, > 0) => Loc.T("loadorder.conflictBoth", wins, loses),
				(> 0, 0)   => Loc.T("loadorder.conflictWins", wins),
				(0, > 0)   => Loc.T("loadorder.conflictLoses", loses),
				_          => ""
			};
			string state = entry.Enabled ? "" : Loc.T("loadorder.disabledSuffix");
			entry.Summary = $"{entry.Display}{state}{conflict}";
			listModPriority.Items.Add(entry);
		}

		if (restoreKey != null)
		{
			for (int i = 0; i < listModPriority.Items.Count; i++)
			{
				if (listModPriority.Items[i] is PriorityEntry e && string.Equals(e.Key, restoreKey, StringComparison.OrdinalIgnoreCase))
				{
					listModPriority.SelectedIndex = i;
					break;
				}
			}
		}
		if (listModPriority.SelectedIndex == -1 && listModPriority.Items.Count > 0)
			listModPriority.SelectedIndex = 0;
		listModPriority.EndUpdate();
		_suppressPrioritySpeak = false;
	}

	/// <summary>Announces the focused priority row's position, unless suppressed during a reorder.</summary>
	private async void ListModPriority_SelectedIndexChanged(object? sender, EventArgs e)
	{
		if (_suppressPrioritySpeak) return;
		if (sender is not ListBox { SelectedItem: not null } list || _isLoading || !list.Focused) return;
		await Task.Delay(100);
		if (!list.Focused) return;
		Speak(Loc.T("common.position", list.SelectedIndex + 1, list.Items.Count));
	}

	/// <summary>
	/// Handles Ctrl+Up / Ctrl+Down to move the selected mod up or down in priority. After a move the
	/// order is persisted, the deployment is re-synced (which is fast: only files whose winner changed are
	/// relinked), the list is rebuilt with fresh conflict counts, and the new position is announced.
	/// </summary>
	private void ListModPriority_KeyDown(object? sender, KeyEventArgs e)
	{
		if (!(e.Control && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))) return;
		e.Handled = true;
		e.SuppressKeyPress = true;

		if (listModPriority.SelectedItem is not PriorityEntry entry) return;
		List<string> order = _settings.ModPriority[_settings.ActiveGame];
		int idx = order.FindIndex(n => string.Equals(n, entry.Key, StringComparison.OrdinalIgnoreCase));
		if (idx < 0) return;

		bool up = e.KeyCode == Keys.Up;
		int target = up ? idx - 1 : idx + 1;
		if (target < 0 || target >= order.Count)
		{
			Speak(Loc.T(up ? "loadorder.atTop" : "loadorder.atBottom"));
			return;
		}

		(order[idx], order[target]) = (order[target], order[idx]);
		_settings.Save();
		SyncBethesdaDeployment();

		_suppressPrioritySpeak = true;
		RefreshModPriorityList();
		listModPriority.SelectedIndex = target;
		_suppressPrioritySpeak = false;

		_soundEngine.Play(up ? "enable" : "disable");
		string movedEntry = (listModPriority.SelectedItem as PriorityEntry)?.Summary ?? entry.Display;
		Speak(Loc.T(up ? "loadorder.movedUp" : "loadorder.movedDown", movedEntry, target + 1, order.Count));
	}

	// -------------------------------------------------------------------------
	// Plugin load order (plugins.txt)
	// -------------------------------------------------------------------------

	private static bool InMasterGroup((bool Master, bool Light) c) => c.Master || c.Light;

	/// <summary>
	/// Rebuilds the active plugin load order from the enabled mods' plugins (plus any external active
	/// entries no installed mod provides, e.g. Creation Club content), normalizes it masters-first,
	/// persists it, and writes plugins.txt. Base-game/DLC masters stay implicit. No-op outside Skyrim/FO4.
	/// </summary>
	private void SyncBethesdaPlugins()
	{
		if (!IsBethesdaGame) return;
		string game = _settings.ActiveGame;
		string gameRoot = _settings.CurrentGamePath;

		var cls = new Dictionary<string, (bool Master, bool Light)>(StringComparer.OrdinalIgnoreCase);
		var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var allModPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// Plugins supplied by installed mods. Only enabled mods contribute active plugins; the full set
		// (enabled + disabled) is tracked so a disabled mod's plugin is not re-adopted as "external".
		foreach (StardewMod mod in _allInstalledMods.Where(m => !m.IsGroup))
		{
			if (!Directory.Exists(mod.FolderPath)) continue;
			foreach (string file in Directory.GetFiles(mod.FolderPath, "*.*", SearchOption.AllDirectories))
			{
				string name = Path.GetFileName(file);
				if (!ModFileSystem.IsPluginFile(name)) continue;
				allModPlugins.Add(name);
				if (ModFileSystem.IsBaseMaster(game, name)) continue;
				if (mod.IsEnabled && !cls.ContainsKey(name))
				{
					cls[name] = ModFileSystem.ReadPluginFlags(file);
					paths[name] = file;
				}
			}
		}

		// The current plugins.txt order, used both to preserve external entries and to seed new entries in
		// the order the game currently loads them (so an existing curated load order is not scrambled).
		List<string> existingActive = ModFileSystem.ReadActivePlugins(game);

		// Preserve external active entries (e.g. Creation Club) that no installed mod provides.
		foreach (string name in existingActive)
		{
			if (ModFileSystem.IsBaseMaster(game, name)) continue;
			if (allModPlugins.Contains(name) || cls.ContainsKey(name)) continue;
			string dataPath = string.IsNullOrEmpty(gameRoot) ? "" : Path.Combine(gameRoot, "Data", name);
			bool hasData = !string.IsNullOrEmpty(dataPath) && File.Exists(dataPath);
			cls[name] = hasData ? ModFileSystem.ReadPluginFlags(dataPath) : ModFileSystem.ReadPluginFlags(name);
			if (hasData) paths[name] = dataPath;
		}

		if (!_settings.PluginOrder.TryGetValue(game, out List<string>? order) || order == null)
		{
			order = new List<string>();
			_settings.PluginOrder[game] = order;
		}

		bool changed = order.RemoveAll(n => !cls.ContainsKey(n)) > 0;
		// Add new plugins following the existing plugins.txt order first (preserves the user's current load
		// order), then any remaining plugins (present in mods but not yet listed) at the end.
		foreach (string name in existingActive)
		{
			if (cls.ContainsKey(name) && !order.Contains(name, StringComparer.OrdinalIgnoreCase)) { order.Add(name); changed = true; }
		}
		foreach (string name in cls.Keys)
		{
			if (!order.Contains(name, StringComparer.OrdinalIgnoreCase)) { order.Add(name); changed = true; }
		}

		// Masters and light masters load before regular plugins; keep that grouping, stable within each.
		var masters = order.Where(n => InMasterGroup(cls[n])).ToList();
		var normals = order.Where(n => !InMasterGroup(cls[n])).ToList();
		var normalized = masters.Concat(normals).ToList();
		if (!normalized.SequenceEqual(order, StringComparer.OrdinalIgnoreCase))
		{
			_settings.PluginOrder[game] = normalized;
			order = normalized;
			changed = true;
		}
		if (changed) _settings.Save();

		_pluginClass = cls;
		_pluginPaths = paths;
		ModFileSystem.WritePluginsTxt(game, order, LogError);
		// The list UI is refreshed by the caller on the UI thread (see RefreshModList).
	}

	/// <summary>One row of the Plugin Order list: a plugin file and its master/light classification.</summary>
	private sealed class PluginEntry
	{
		public string Name = "";
		public bool Master;
		public bool Light;
		public string Summary = "";
		public override string ToString() => Summary;
	}

	/// <summary>
	/// Rebuilds the Plugin Order list from the saved order. Each row reads the plugin file name and whether
	/// it is a master or light master, so the masters-first grouping is audible. Order = load order (top
	/// loads first).
	/// </summary>
	private void RefreshPluginOrderList()
	{
		if (listPluginOrder == null || !IsBethesdaGame) return;
		string? restore = (listPluginOrder.SelectedItem as PluginEntry)?.Name;

		_suppressPrioritySpeak = true;
		listPluginOrder.BeginUpdate();
		listPluginOrder.Items.Clear();
		if (_settings.PluginOrder.TryGetValue(_settings.ActiveGame, out List<string>? order) && order != null)
		{
			foreach (string name in order)
			{
				bool master, light;
				if (_pluginClass.TryGetValue(name, out var found)) { master = found.Master; light = found.Light; }
				else { var f = ModFileSystem.ReadPluginFlags(name); master = f.IsMaster; light = f.IsLight; }
				string tag = light ? Loc.T("loadorder.lightTag") : (master ? Loc.T("loadorder.masterTag") : "");
				listPluginOrder.Items.Add(new PluginEntry { Name = name, Master = master, Light = light, Summary = name + tag });
			}
		}
		if (restore != null)
		{
			for (int i = 0; i < listPluginOrder.Items.Count; i++)
			{
				if (listPluginOrder.Items[i] is PluginEntry e && string.Equals(e.Name, restore, StringComparison.OrdinalIgnoreCase))
				{
					listPluginOrder.SelectedIndex = i;
					break;
				}
			}
		}
		if (listPluginOrder.SelectedIndex == -1 && listPluginOrder.Items.Count > 0)
			listPluginOrder.SelectedIndex = 0;
		listPluginOrder.EndUpdate();
		_suppressPrioritySpeak = false;
	}

	/// <summary>
	/// Handles Ctrl+Up / Ctrl+Down to move the selected plugin within its group. Moves that would cross the
	/// masters/regular boundary are refused (the engine forces masters first, so such a move would not stick).
	/// After a move the order is persisted, plugins.txt is rewritten, and the new position is announced.
	/// </summary>
	private void ListPluginOrder_KeyDown(object? sender, KeyEventArgs e)
	{
		if (!(e.Control && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))) return;
		e.Handled = true;
		e.SuppressKeyPress = true;

		if (listPluginOrder.SelectedItem is not PluginEntry entry) return;
		List<string> order = _settings.PluginOrder[_settings.ActiveGame];
		int idx = order.FindIndex(n => string.Equals(n, entry.Name, StringComparison.OrdinalIgnoreCase));
		if (idx < 0) return;

		bool up = e.KeyCode == Keys.Up;
		int target = up ? idx - 1 : idx + 1;
		if (target < 0 || target >= order.Count)
		{
			Speak(Loc.T(up ? "loadorder.atTop" : "loadorder.atBottom"));
			return;
		}

		bool curMaster = InMasterGroup(_pluginClass.TryGetValue(entry.Name, out var cc) ? cc : ModFileSystem.ReadPluginFlags(entry.Name));
		bool otherMaster = InMasterGroup(_pluginClass.TryGetValue(order[target], out var oc) ? oc : ModFileSystem.ReadPluginFlags(order[target]));
		if (curMaster != otherMaster)
		{
			Speak(Loc.T("loadorder.masterBoundary"));
			return;
		}

		(order[idx], order[target]) = (order[target], order[idx]);
		_settings.Save();
		ModFileSystem.WritePluginsTxt(_settings.ActiveGame, order, LogError);

		_suppressPrioritySpeak = true;
		RefreshPluginOrderList();
		listPluginOrder.SelectedIndex = target;
		_suppressPrioritySpeak = false;

		_soundEngine.Play(up ? "enable" : "disable");
		string moved = (listPluginOrder.SelectedItem as PluginEntry)?.Summary ?? entry.Name;
		Speak(Loc.T(up ? "loadorder.pluginMovedUp" : "loadorder.pluginMovedDown", moved, target + 1, order.Count));
	}

	/// <summary>LOOT masterlist for the game in <see cref="_masterlistGame"/>, cached in memory for the session.</summary>
	private LootMasterlist? _masterlistCache;
	private string _masterlistGame = "";

	/// <summary>
	/// Auto-sort entry point used by the menu/shortcut: fetches (or reuses) LOOT's masterlist rules for the
	/// active game, then runs the sort. Falls back silently to a dependency-only sort when the masterlist is
	/// unavailable (offline, unsupported game, or parse failure).
	/// </summary>
	private async Task AutoSortPluginsAsync()
	{
		if (!IsBethesdaGame) return;
		if (!_settings.PluginOrder.TryGetValue(_settings.ActiveGame, out List<string>? ord) || ord == null || ord.Count == 0)
		{
			Speak(Loc.T("loadorder.sortNoPlugins"));
			return;
		}
		Speak(Loc.T("loadorder.sortStart"));
		LootMasterlist? ml = await EnsureMasterlistAsync(_settings.ActiveGame);
		AutoSortPlugins(ml);
	}

	/// <summary>Returns the cached masterlist for <paramref name="game"/>, loading it on first use.</summary>
	private async Task<LootMasterlist?> EnsureMasterlistAsync(string game)
	{
		if (_masterlistCache != null && _masterlistGame == game) return _masterlistCache;
		LootMasterlist? ml = await LootMasterlist.LoadAsync(game, LogError);
		if (ml != null) { _masterlistCache = ml; _masterlistGame = game; }
		return ml;
	}

	/// <summary>
	/// Sorts the plugin load order so every plugin loads after the masters it declares and after any LOOT
	/// <c>after</c> rules, with LOOT group order as the soft tie-break and masters-first kept intact. When
	/// <paramref name="ml"/> is <c>null</c> this degrades to a pure master-dependency sort. Writes plugins.txt
	/// and announces the result.
	/// </summary>
	private void AutoSortPlugins(LootMasterlist? ml)
	{
		if (!IsBethesdaGame) return;
		string game = _settings.ActiveGame;
		if (!_settings.PluginOrder.TryGetValue(game, out List<string>? order) || order == null || order.Count == 0)
		{
			Speak(Loc.T("loadorder.sortNoPlugins"));
			return;
		}

		// Hard ordering constraints per plugin: its header masters plus any LOOT "after" rules, restricted
		// to plugins we manage (base-game masters load implicitly).
		var inSet = new HashSet<string>(order, StringComparer.OrdinalIgnoreCase);
		var preds = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (string name in order)
		{
			var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (_pluginPaths.TryGetValue(name, out string? p) && File.Exists(p))
				foreach (string m in ModFileSystem.ReadPluginMasters(p))
					if (inSet.Contains(m) && !string.Equals(m, name, StringComparison.OrdinalIgnoreCase)) set.Add(m);
			if (ml != null && ml.PluginAfter.TryGetValue(name, out List<string>? afters))
				foreach (string a in afters)
					if (inSet.Contains(a) && !string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) set.Add(a);
			preds[name] = set;
		}

		bool IsMasterName(string n) => InMasterGroup(_pluginClass.TryGetValue(n, out var c) ? c : (false, false));
		int GroupIdx(string n) => ml?.GroupIndexFor(n) ?? 0;

		// Masters always load before regular plugins; sort each partition by constraints, with LOOT group
		// order (then original position) as the preference among otherwise-free choices.
		var masterGroup = order.Where(IsMasterName).ToList();
		var normalGroup = order.Where(n => !IsMasterName(n)).ToList();
		var sorted = TopoSort(masterGroup, preds, GroupIdx)
			.Concat(TopoSort(normalGroup, preds, GroupIdx))
			.ToList();

		bool changed = !sorted.SequenceEqual(order, StringComparer.OrdinalIgnoreCase);
		if (changed)
		{
			_settings.PluginOrder[game] = sorted;
			_settings.Save();
			ModFileSystem.WritePluginsTxt(game, sorted, LogError);
			_suppressPrioritySpeak = true;
			RefreshPluginOrderList();
			_suppressPrioritySpeak = false;
		}

		_soundEngine.Play(changed ? "connect" : "load_complete");
		string scope = ml != null ? Loc.T("loadorder.scopeLoot") : Loc.T("loadorder.scopeDeps");
		Speak(changed ? Loc.T("loadorder.sorted", sorted.Count, scope) : Loc.T("loadorder.sortNoChange", scope));
	}

	/// <summary>
	/// Priority-aware Kahn topological sort: each plugin follows its <paramref name="preds"/> that are in the
	/// group, and among ready nodes the one with the lowest (group index, original position) is taken first.
	/// Any dependency cycle falls back to input order, so the sort only moves what the constraints require.
	/// </summary>
	private static List<string> TopoSort(List<string> group, Dictionary<string, HashSet<string>> preds, Func<string, int> groupIdx)
	{
		var member = new HashSet<string>(group, StringComparer.OrdinalIgnoreCase);
		var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < group.Count; i++) index[group[i]] = i;

		var indeg = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		foreach (string p in group) { indeg[p] = 0; dependents[p] = new List<string>(); }
		foreach (string p in group)
			foreach (string m in preds[p])
				if (member.Contains(m) && !string.Equals(m, p, StringComparison.OrdinalIgnoreCase))
				{
					dependents[m].Add(p);
					indeg[p]++;
				}

		var ready = group.Where(p => indeg[p] == 0).ToList();
		var result = new List<string>(group.Count);
		while (ready.Count > 0)
		{
			ready.Sort((a, b) =>
			{
				int c = groupIdx(a).CompareTo(groupIdx(b));
				return c != 0 ? c : index[a].CompareTo(index[b]);
			});
			string n = ready[0];
			ready.RemoveAt(0);
			result.Add(n);
			foreach (string d in dependents[n])
				if (--indeg[d] == 0) ready.Add(d);
		}
		foreach (string p in group)
			if (!result.Contains(p, StringComparer.OrdinalIgnoreCase)) result.Add(p);
		return result;
	}
}
