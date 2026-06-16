using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// The Skyrim SE / Fallout 4 "Log" tab: an accessible viewer for the logs the script extender and its plugins
/// write to <c>Documents\My Games\&lt;game&gt;\F4SE</c> (or <c>\SKSE</c>). Unlike Stardew's single SMAPI log,
/// Bethesda games produce many logs (f4se.log, per-plugin logs, accessibility-mod logs, crash logs), so this
/// tab offers a picker over every <c>.log</c> in that folder plus a keyword filter and search, mirroring the
/// SMAPI log tab's keyboard conventions.
/// </summary>
public partial class Form1
{
	/// <summary>Substrings that mark a log line as notable for the "Errors and Warnings" filter (case-insensitive).</summary>
	private static readonly string[] GameLogNotableKeywords =
	{
		"error", "fail", "warn", "disabled", "could not", "couldn't", "unable",
		"missing", "not found", "exception", "incompatible", "crash", "invalid"
	};

	/// <summary>Re-reads the log folder and reloads the current log. Called on game load and manual refresh.</summary>
	private void RefreshGameLog()
	{
		if (!IsBethesdaGame || cmbGameLog == null) return;
		PopulateGameLogFiles();
	}

	/// <summary>
	/// Fills the log-file picker with every <c>.log</c> in the active game's script-extender folder, newest
	/// first, keeping the current selection if it still exists, otherwise defaulting to the script extender's
	/// own log. Setting the selection loads that log.
	/// </summary>
	private void PopulateGameLogFiles()
	{
		if (cmbGameLog == null || listGameLog == null || !IsBethesdaGame) return;
		string folder = BethesdaLogFolder();
		string? previous = cmbGameLog.SelectedItem as string;

		cmbGameLog.BeginUpdate();
		cmbGameLog.Items.Clear();
		if (Directory.Exists(folder))
		{
			foreach (FileInfo f in new DirectoryInfo(folder).GetFiles("*.log").OrderByDescending(f => f.LastWriteTime))
				cmbGameLog.Items.Add(f.Name);
		}
		cmbGameLog.EndUpdate();

		if (cmbGameLog.Items.Count == 0)
		{
			_gameLogLines.Clear();
			listGameLog.Items.Clear();
			return;
		}

		string defaultLog = _settings.ActiveGame == "SkyrimSE" ? "skse64.log" : "f4se.log";
		int idx = previous != null ? cmbGameLog.Items.IndexOf(previous) : -1;
		if (idx < 0) idx = cmbGameLog.Items.IndexOf(defaultLog);
		if (idx < 0) idx = 0;
		// Force a load even when the index is unchanged (e.g. a manual refresh of the same file).
		if (cmbGameLog.SelectedIndex == idx) LoadSelectedGameLog();
		else cmbGameLog.SelectedIndex = idx;
	}

	/// <summary>Reads the selected log file (shared, so it works while the game is writing it) and shows it.</summary>
	private void LoadSelectedGameLog()
	{
		if (cmbGameLog?.SelectedItem is not string name)
		{
			_gameLogLines.Clear();
			ApplyGameLogView();
			return;
		}
		string path = Path.Combine(BethesdaLogFolder(), name);
		_gameLogLines.Clear();
		try
		{
			if (File.Exists(path)) _gameLogLines.AddRange(ReadAllLinesShared(path));
		}
		catch (Exception ex)
		{
			LogError("GameLog", "Failed to read " + name + ": " + ex.Message);
		}
		ApplyGameLogView();
	}

	/// <summary>Rebuilds <c>listGameLog</c> from the loaded lines, applying the current filter and search box.</summary>
	private void ApplyGameLogView()
	{
		if (listGameLog == null) return;
		int filter = cmbGameLogFilter?.SelectedIndex ?? 0;          // 0 = Full Log, 1 = Errors and Warnings
		string query = txtSearchGameLog?.Text.Trim() ?? "";

		IEnumerable<string> lines = _gameLogLines;
		if (filter == 1) lines = lines.Where(GameLogLineIsNotable);
		if (query.Length > 0) lines = lines.Where(l => l.Contains(query, StringComparison.OrdinalIgnoreCase));

		listGameLog.BeginUpdate();
		listGameLog.Items.Clear();
		foreach (string l in lines) listGameLog.Items.Add(l);
		if (listGameLog.Items.Count > 0) listGameLog.SelectedIndex = 0;
		listGameLog.EndUpdate();
	}

	private static bool GameLogLineIsNotable(string line)
	{
		foreach (string k in GameLogNotableKeywords)
			if (line.Contains(k, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	/// <summary>Applies the search box to the current log and announces how many lines matched.</summary>
	private void SearchGameLog()
	{
		ApplyGameLogView();
		Speak(Loc.T("gamelog.foundResults", listGameLog.Items.Count));
	}

	/// <summary>Keyboard handling for the game-log list: refresh (re-read live log), copy, and open raw file.</summary>
	private void ListGameLog_KeyDown(object? sender, KeyEventArgs e)
	{
		if (IsShortcut(e, "RefreshLog"))
		{
			PopulateGameLogFiles();
			Speak(Loc.T("gamelog.refreshed", listGameLog.Items.Count));
			e.Handled = true;
			e.SuppressKeyPress = true;
			return;
		}
		// Note: the OpenLogFile shortcut (open raw file) is handled globally in ProcessShortcuts.
		// Ctrl+C copies the selected line(s) to the clipboard for pasting into a forum/Discord.
		if (e.Control && e.KeyCode == Keys.C)
		{
			IEnumerable<object> selected = listGameLog.SelectedItems.Count > 0
				? listGameLog.SelectedItems.Cast<object>()
				: (listGameLog.SelectedItem != null ? new[] { listGameLog.SelectedItem } : Array.Empty<object>());
			string copied = string.Join("\r\n", selected.Select(o => o.ToString()));
			if (!string.IsNullOrEmpty(copied))
			{
				Clipboard.SetText(copied);
				int count = copied.Split('\n').Length;
				Speak(count == 1 ? Loc.T("modlist.copiedOne") : Loc.T("modlist.copiedMany", count));
			}
			else
			{
				Speak(Loc.T("modlist.noLinesSelected"));
			}
			e.Handled = true;
			e.SuppressKeyPress = true;
		}
	}
}
