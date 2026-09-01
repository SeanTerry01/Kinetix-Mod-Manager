using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace KinetixModManager;

/// <summary>
/// Automatic safety snapshots taken before risky Skyrim SE / Fallout 4 operations (purge, rebuild, editing game
/// INIs). Each snapshot captures the files those operations can break — the game INIs and the active plugins.txt —
/// plus the manager's mod-priority and plugin order, so a bad edit or a mis-deploy is one restore away. Snapshots
/// live in the app data folder and the newest few are kept. Restore copies the files back, reinstates the saved
/// load order, and re-deploys. Purely additive: creating a snapshot never blocks the operation it precedes.
/// </summary>
public partial class Form1
{
	private const int MaxSafetyBackups = 8;

	/// <summary>Metadata stored alongside each snapshot's copied files.</summary>
	private sealed class SafetyBackupMeta
	{
		public string Reason { get; set; } = "";
		public DateTime CreatedUtc { get; set; }
		public List<string> ModPriority { get; set; } = new List<string>();
		public List<string> PluginOrder { get; set; } = new List<string>();
	}

	private string SafetyRoot => Path.Combine(dataBasePath, "safety", _settings.ActiveGame);

	/// <summary>The game files a snapshot captures: the three game INIs and the active plugins.txt.</summary>
	private List<(string Label, string Path)> SafetySourceFiles()
	{
		var files = new List<(string, string)>(ModFileSystem.GameIniFiles(_settings.ActiveGame, _settings.CurrentGamePath));
		string plugins = ModFileSystem.ActivePluginsTxtPath(_settings.ActiveGame, _settings.CurrentGamePath);
		if (!string.IsNullOrEmpty(plugins)) files.Add(("plugins.txt", plugins));
		return files;
	}

	/// <summary>
	/// Takes a best-effort safety snapshot for the active Bethesda game, labelled with <paramref name="reason"/>.
	/// Copies each existing source file plus a metadata record of the current load order, then prunes to the newest
	/// <see cref="MaxSafetyBackups"/>. Never throws and never blocks the caller's operation.
	/// </summary>
	private void CreateSafetyBackup(string reason)
	{
		if (!IsBethesdaGame) return;
		try
		{
			string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			string dir = Path.Combine(SafetyRoot, stamp);
			Directory.CreateDirectory(dir);

			foreach (var (_, src) in SafetySourceFiles())
				if (File.Exists(src))
					File.Copy(src, Path.Combine(dir, Path.GetFileName(src)), overwrite: true);

			var meta = new SafetyBackupMeta
			{
				Reason = reason,
				CreatedUtc = DateTime.UtcNow,
				ModPriority = new List<string>(_settings.ModPriority.TryGetValue(_settings.ActiveGame, out var mp) ? mp : new List<string>()),
				PluginOrder = new List<string>(_settings.PluginOrder.TryGetValue(_settings.ActiveGame, out var po) ? po : new List<string>()),
			};
			File.WriteAllText(Path.Combine(dir, "safety.json"), JsonConvert.SerializeObject(meta, Formatting.Indented));

			PruneSafetyBackups();
		}
		catch (Exception ex) { LogFailure("Safety", $"Creating safety snapshot failed", ex); }
	}

	/// <summary>Keeps only the newest <see cref="MaxSafetyBackups"/> snapshots for the active game.</summary>
	private void PruneSafetyBackups()
	{
		try
		{
			if (!Directory.Exists(SafetyRoot)) return;
			var dirs = new DirectoryInfo(SafetyRoot).GetDirectories()
				.OrderByDescending(d => d.Name, StringComparer.Ordinal)
				.Skip(MaxSafetyBackups);
			foreach (DirectoryInfo d in dirs)
				try { d.Delete(recursive: true); }
				catch (Exception ex) { DiagnosticLog.WriteException("Safety", $"deleting the old snapshot {d.FullName}", ex); }
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Safety", "pruning old safety snapshots", ex); }
	}

	/// <summary>One restorable snapshot in the restore list.</summary>
	private sealed class SafetyBackupItem
	{
		public required string Dir;
		public required SafetyBackupMeta Meta;
		public string Summary = "";
		public override string ToString() => Summary;
	}

	/// <summary>Lists the active game's safety snapshots; Enter restores one, Delete removes one.</summary>
	private void ShowRestoreSafetyBackup()
	{
		if (!IsBethesdaGame)
		{
			Speak(Loc.T("safety.notApplicable"));
			return;
		}

		List<SafetyBackupItem> items = LoadSafetyBackups();
		if (items.Count == 0)
		{
			Speak(Loc.T("safety.none"));
			return;
		}

		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("safety.title"), (container, closeView) =>
		{
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			layout.Controls.Add(new Label { Text = Loc.T("safety.header", GameDisplayName()), AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8) }, 0, 0);

			var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = Loc.T("safety.listName"), IntegralHeight = false, HorizontalScrollbar = true };
			foreach (SafetyBackupItem it in items) list.Items.Add(it);
			if (list.Items.Count > 0) list.SelectedIndex = 0;
			layout.Controls.Add(list, 0, 1);
			container.Controls.Add(layout);

			WireAccessibleDialogList(list);
			// Escape is handled by the view itself (see Form1.InlineView).
			list.KeyDown += (_, e) =>
			{
				if (list.SelectedItem is not SafetyBackupItem item) return;

				if (e.KeyCode == Keys.Enter)
				{
					e.Handled = e.SuppressKeyPress = true;
					if (SpeakBox(Loc.T("safety.restoreConfirm", item.Summary), Loc.T("safety.restoreTitle"),
							MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
					{
						closeView();
						RestoreSafetyBackup(item);
					}
				}
				else if (e.KeyCode == Keys.Delete)
				{
					e.Handled = e.SuppressKeyPress = true;
					try { Directory.Delete(item.Dir, recursive: true); } catch (Exception ex) { LogError("Safety", ex.Message); }
					int idx = list.SelectedIndex;
					list.Items.Remove(item);
					_soundEngine.Play("disable");
					if (list.Items.Count == 0) { Speak(Loc.T("safety.deletedLast")); closeView(); return; }
					list.SelectedIndex = Math.Min(idx, list.Items.Count - 1);
					Speak(Loc.T("safety.deleted", list.Items.Count));
				}
			};

			return list;
		},
		// Through the hint so it follows the title instead of arriving ahead of it, and without the old heading
		// clause ("Safety backups for <game>."), which said the title's own words a second time.
		hint: Loc.T(items.Count == 1 ? "safety.countOne" : "safety.count", items.Count) + " " + Loc.T("safety.actionHint"));
	}

	private List<SafetyBackupItem> LoadSafetyBackups()
	{
		var items = new List<SafetyBackupItem>();
		try
		{
			if (!Directory.Exists(SafetyRoot)) return items;
			foreach (DirectoryInfo d in new DirectoryInfo(SafetyRoot).GetDirectories())
			{
				string metaPath = Path.Combine(d.FullName, "safety.json");
				if (!File.Exists(metaPath)) continue;
				SafetyBackupMeta? meta = JsonConvert.DeserializeObject<SafetyBackupMeta>(File.ReadAllText(metaPath));
				if (meta == null) continue;
				items.Add(new SafetyBackupItem
				{
					Dir = d.FullName,
					Meta = meta,
					Summary = Loc.T("safety.row", meta.CreatedUtc.ToLocalTime().ToString("g"), meta.Reason)
				});
			}
		}
		catch (Exception ex) { LogFailure("Safety", $"Reading safety snapshots failed", ex); }
		items.Sort((a, b) => b.Meta.CreatedUtc.CompareTo(a.Meta.CreatedUtc));
		return items;
	}

	/// <summary>Restores a snapshot: copies its files back, reinstates the saved load order, and re-deploys.</summary>
	private void RestoreSafetyBackup(SafetyBackupItem item)
	{
		try
		{
			// Copy each captured file back to its original location.
			foreach (var (_, dest) in SafetySourceFiles())
			{
				string src = Path.Combine(item.Dir, Path.GetFileName(dest));
				if (!File.Exists(src)) continue;
				string? destDir = Path.GetDirectoryName(dest);
				if (destDir != null && !Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
				File.Copy(src, dest, overwrite: true);
			}

			// Reinstate the saved load order in settings so the manager's state matches the restored files.
			if (item.Meta.ModPriority.Count > 0) _settings.ModPriority[_settings.ActiveGame] = new List<string>(item.Meta.ModPriority);
			if (item.Meta.PluginOrder.Count > 0) _settings.PluginOrder[_settings.ActiveGame] = new List<string>(item.Meta.PluginOrder);
			_settings.Save();

			// Re-deploy so the game folder matches the restored priority, and rewrite plugins.txt from the restored order.
			SyncBethesdaDeployment();
			SyncBethesdaPlugins();
			RefreshModPriorityList();
			RefreshPluginOrderList();

			_soundEngine.Play("load_complete");
			Speak(Loc.T("safety.restored", item.Summary));
		}
		catch (Exception ex)
		{
			LogFailure("Safety", $"Restoring safety snapshot failed", ex);
			_soundEngine.Play("error");
			Speak(Loc.T("safety.restoreFailed", FriendlyError(ex)));
		}
	}
}
