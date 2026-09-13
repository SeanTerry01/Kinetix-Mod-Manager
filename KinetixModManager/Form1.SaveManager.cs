using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// The accessible savegame manager for Skyrim SE / Fallout 4 (Mods menu / shortcut). It lists every save with its
/// character, level, location, playtime and date; reads each save's embedded plugin list; and flags saves that
/// depend on a plugin no longer active (a mod removed or disabled since the save was made) — the situation that
/// silently corrupts or crashes a save. From the list the user can hear a save's full details, back it up, or send
/// it to the Recycle Bin. Co-save files (SKSE/F4SE) are moved and backed up alongside their save.
/// </summary>
public partial class Form1
{
	/// <summary>Co-save extensions written next to a save by the script extender; handled alongside the save.</summary>
	private static readonly string[] CoSaveExtensions = { ".skse", ".f4se" };

	/// <summary>
	/// Entry point (Mods menu / shortcut): locates the active game's saves, parses them off the UI thread with a
	/// spoken status, then shows the accessible list. Only meaningful for Skyrim SE / Fallout 4.
	/// </summary>
	private async Task ShowSaveManager()
	{
		if (!IsBethesdaGame)
		{
			Speak(Loc.T("saves.notApplicable"));
			return;
		}

		string game = _settings.ActiveGame;
		(string folder, string ext) = ModFileSystem.SavesLocation(game, _settings.GamePathOf(game));
		if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
		{
			Speak(Loc.T("saves.noFolder", GameDisplayName(game)));
			return;
		}

		string[] files;
		try { files = Directory.GetFiles(folder, "*" + ext); }
		catch (Exception ex) { LogFailure("Saves", $"Enumerating saves failed", ex); files = Array.Empty<string>(); }
		if (files.Length == 0)
		{
			Speak(Loc.T("saves.none", GameDisplayName(game)));
			return;
		}

		SetStatus(Loc.T("saves.reading"), speak: true);
		List<SaveRow> rows = await Task.Run(() => BuildSaveRows(game, files));
		ResetStatus();

		ShowSaveManagerDialog(game, rows);
	}

	/// <summary>Parses every save file and builds its display row, newest first. Runs on a background thread.</summary>
	private List<SaveRow> BuildSaveRows(string game, string[] files)
	{
		// The set of plugins that would load right now: the active plugins plus the implicit base-game/DLC masters.
		// A save master outside this set is no longer active, so the save references content it can't load.
		var activeNow = new HashSet<string>(ModFileSystem.ReadActivePlugins(game, _settings.GamePathOf(game)), StringComparer.OrdinalIgnoreCase);
		foreach (string bm in ModFileSystem.BaseMasters(game)) activeNow.Add(bm);

		var rows = new List<SaveRow>();
		foreach (string file in files)
		{
			SaveGame? save = SaveGameParser.Parse(file, m => LogError("Saves", m));
			if (save == null) continue;

			List<string> missing = save.PluginsRead
				? save.Masters.Where(m => !activeNow.Contains(m)).ToList()
				: new List<string>();
			rows.Add(new SaveRow { Save = save, MissingPlugins = missing, Summary = BuildSaveSummary(save, missing) });
		}
		rows.Sort((a, b) => b.Save.SaveTime.CompareTo(a.Save.SaveTime));
		return rows;
	}

	/// <summary>The one-line spoken/displayed summary for a save row.</summary>
	private static string BuildSaveSummary(SaveGame save, List<string> missing)
	{
		string name = string.IsNullOrWhiteSpace(save.CharacterName) ? Loc.T("saves.unnamed") : save.CharacterName;
		string loc = string.IsNullOrWhiteSpace(save.Location) ? Loc.T("saves.unknownLocation") : save.Location;
		string text = Loc.T("saves.row", name, save.Level, loc, save.SaveTime.ToString("g"));
		if (!save.PluginsRead) text += Loc.T("saves.rowPluginsUnknown");
		else if (missing.Count > 0) text += Loc.T("saves.rowMissing", missing.Count);
		return text;
	}

	private void ShowSaveManagerDialog(string game, List<SaveRow> rows)
	{
		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("saves.title"), (container, closeView) =>
		{
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			layout.Controls.Add(new Label { Text = Loc.T("saves.header", GameDisplayName(game)), AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8) }, 0, 0);

			var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = Loc.T("saves.listName"), IntegralHeight = false, HorizontalScrollbar = true };
			foreach (SaveRow r in rows) list.Items.Add(r);
			if (list.Items.Count > 0) list.SelectedIndex = 0;
			layout.Controls.Add(list, 0, 1);
			container.Controls.Add(layout);

			WireAccessibleDialogList(list);
			// Escape is handled by the view itself (see Form1.InlineView).
			list.KeyDown += (_, e) =>
			{
				if (list.SelectedItem is not SaveRow row) return;

				if (e.KeyCode == Keys.Enter)
				{
					e.Handled = e.SuppressKeyPress = true;
					Speak(BuildSaveDetails(row));
				}
				else if (e.KeyCode == Keys.B)
				{
					e.Handled = e.SuppressKeyPress = true;
					BackupSave(row);
				}
				else if (e.KeyCode == Keys.Delete)
				{
					e.Handled = e.SuppressKeyPress = true;
					DeleteSave(closeView, list, row);
				}
			};

			return list;
		},
		// Through the hint, and without the old heading clause ("Save games for <game>."), which repeated the
		// title. See Form1.InlineView: a Speak during build lands ahead of the title.
		hint: SaveManagerHint(rows));
	}

	/// <summary>
	/// What the save manager says on the way in, after its title: how many saves there are, how many have a
	/// problem worth knowing about before loading one, and what the keys do.
	/// </summary>
	private static string SaveManagerHint(List<SaveRow> rows)
	{
		int problems = rows.Count(r => r.MissingPlugins.Count > 0);
		string hint = Loc.T(rows.Count == 1 ? "saves.countOne" : "saves.count", rows.Count);
		if (problems > 0) hint += " " + Loc.T("saves.countProblems", problems);
		return hint + " " + Loc.T("saves.actionHint");
	}

	/// <summary>The full spoken details for a save when the user presses Enter on its row.</summary>
	private static string BuildSaveDetails(SaveRow row)
	{
		SaveGame s = row.Save;
		string name = string.IsNullOrWhiteSpace(s.CharacterName) ? Loc.T("saves.unnamed") : s.CharacterName;
		string loc = string.IsNullOrWhiteSpace(s.Location) ? Loc.T("saves.unknownLocation") : s.Location;
		string playtime = string.IsNullOrWhiteSpace(s.Playtime) ? Loc.T("saves.unknownPlaytime") : s.Playtime;
		string text = Loc.T("saves.details", name, s.Level, loc, playtime, s.SaveTime.ToString("g"), s.Masters.Count);

		if (!s.PluginsRead)
			text += " " + Loc.T("saves.detailsPluginsUnknown");
		else if (row.MissingPlugins.Count == 0)
			text += " " + Loc.T("saves.detailsNoMissing");
		else
			text += " " + Loc.T("saves.detailsMissing", row.MissingPlugins.Count, string.Join(", ", row.MissingPlugins));
		return text;
	}

	/// <summary>Copies a save (and any co-save) into the app's save-backup folder.</summary>
	private void BackupSave(SaveRow row)
	{
		try
		{
			string dir = Path.Combine(dataBasePath, "SaveBackups", _settings.ActiveGame);
			Directory.CreateDirectory(dir);
			foreach (string src in SaveFileGroup(row.Save))
				File.Copy(src, Path.Combine(dir, Path.GetFileName(src)), overwrite: true);
			_soundEngine.Play("load_complete");
			Speak(Loc.T("saves.backedUp", Path.GetFileNameWithoutExtension(row.Save.FilePath)));
		}
		catch (Exception ex)
		{
			LogFailure("Saves", $"Save backup failed", ex);
			_soundEngine.Play("error");
			Speak(Loc.T("saves.backupFailed", FriendlyError(ex)));
		}
	}

	/// <summary>Sends a save (and any co-save) to the Recycle Bin after confirmation, and drops it from the list.</summary>
	private void DeleteSave(Action closeView, ListBox list, SaveRow row)
	{
		string label = Path.GetFileNameWithoutExtension(row.Save.FilePath);
		if (SpeakBox(Loc.T("saves.deleteConfirm", label), Loc.T("saves.deleteTitle"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
			return;

		try
		{
			foreach (string path in SaveFileGroup(row.Save))
				Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
					Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
					Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
		}
		catch (Exception ex)
		{
			LogFailure("Saves", $"Save delete failed", ex);
			_soundEngine.Play("error");
			Speak(Loc.T("saves.deleteFailed", FriendlyError(ex)));
			return;
		}

		int idx = list.SelectedIndex;
		list.Items.Remove(row);
		_soundEngine.Play("disable");
		if (list.Items.Count == 0) { Speak(Loc.T("saves.deletedLast")); closeView(); return; }
		list.SelectedIndex = Math.Min(idx, list.Items.Count - 1);
		Speak(Loc.T("saves.deleted", list.Items.Count));
	}

	/// <summary>A save file plus any co-save (SKSE/F4SE) sharing its base name, limited to files that exist.</summary>
	private static IEnumerable<string> SaveFileGroup(SaveGame save)
	{
		yield return save.FilePath;
		string dir = Path.GetDirectoryName(save.FilePath) ?? "";
		string stem = Path.GetFileNameWithoutExtension(save.FilePath);
		foreach (string ext in CoSaveExtensions)
		{
			string co = Path.Combine(dir, stem + ext);
			if (File.Exists(co)) yield return co;
		}
	}
}
