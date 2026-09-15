using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace KinetixModManager;

/// <summary>One recorded mod search: the term typed and when it was searched (local time).</summary>
public class SearchHistoryEntry
{
	public string Term { get; set; } = "";
	public DateTime Date { get; set; }
}

/// <summary>
/// Per-game store of mod text-search history, kept in the manager's AppData (not in any game folder). Each game
/// has its own file, so the active game's session only ever sees its own searches. Entries are tiny (a string
/// plus a timestamp), so the history is effectively unlimited; a generous cap only stops the file growing forever.
/// </summary>
public static class SearchHistoryStore
{
	/// <summary>Upper bound on stored entries per game — far beyond any human's history, just a safety valve.</summary>
	private const int MaxEntries = 5000;

	private static string HistoryPath(string game) =>
		Path.Combine(AppSettings.AppDataFolder, "search_history", game + ".json");

	/// <summary>Loads a game's search history, newest first. Returns an empty list when none exists.</summary>
	public static List<SearchHistoryEntry> Load(string game)
	{
		try
		{
			string path = HistoryPath(game);
			if (!File.Exists(path)) return new List<SearchHistoryEntry>();
			var entries = JsonConvert.DeserializeObject<List<SearchHistoryEntry>>(File.ReadAllText(path));
			return (entries ?? new List<SearchHistoryEntry>())
				.OrderByDescending(e => e.Date)
				.ToList();
		}
		catch { return new List<SearchHistoryEntry>(); }
	}

	/// <summary>Appends a search term to a game's history (best-effort; never throws into the search flow). A
	/// blank term is ignored, and an immediate repeat of the most recent term is collapsed to the new time.</summary>
	public static void Add(string game, string term)
	{
		term = term?.Trim() ?? "";
		if (string.IsNullOrEmpty(term) || string.IsNullOrEmpty(game) || game == "None") return;
		try
		{
			// Stored oldest-first on disk; Load re-sorts newest-first for display.
			var entries = LoadRaw(game);
			if (entries.Count > 0 && string.Equals(entries[^1].Term, term, StringComparison.OrdinalIgnoreCase))
				entries[^1].Date = DateTime.Now;          // same term again — just refresh its time
			else
				entries.Add(new SearchHistoryEntry { Term = term, Date = DateTime.Now });

			if (entries.Count > MaxEntries)
				entries.RemoveRange(0, entries.Count - MaxEntries);

			string path = HistoryPath(game);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, JsonConvert.SerializeObject(entries, Formatting.Indented));
		}
		catch (Exception ex) { DiagnosticLog.WriteException("History", "saving a search to the history", ex); }
	}

	/// <summary>
	/// Removes one search term from a game's history and returns how many stored entries went with it.
	///
	/// Every occurrence of the term goes, not just the most recent one: the history list shows each term once
	/// however many times it was searched, so leaving older copies behind would delete a row that immediately
	/// came back. Matching ignores case for the same reason the list de-duplicates that way.
	/// </summary>
	public static int RemoveTerm(string game, string term)
	{
		term = term?.Trim() ?? "";
		if (string.IsNullOrEmpty(term) || string.IsNullOrEmpty(game) || game == "None") return 0;

		try
		{
			var entries = LoadRaw(game);
			int removed = entries.RemoveAll(e => string.Equals(e.Term, term, StringComparison.OrdinalIgnoreCase));
			if (removed == 0) return 0;

			string path = HistoryPath(game);
			if (entries.Count == 0)
			{
				// Nothing left: drop the file entirely rather than leave an empty one behind, which is exactly
				// what Clear does and what Load already treats as "no history".
				if (File.Exists(path)) File.Delete(path);
			}
			else
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				File.WriteAllText(path, JsonConvert.SerializeObject(entries, Formatting.Indented));
			}
			return removed;
		}
		catch { return 0; }
	}

	/// <summary>Deletes a game's entire search history.</summary>
	public static void Clear(string game)
	{
		try
		{
			string path = HistoryPath(game);
			if (File.Exists(path)) File.Delete(path);
		}
		catch (Exception ex) { DiagnosticLog.WriteException("History", "clearing the search history", ex); }
	}

	private static List<SearchHistoryEntry> LoadRaw(string game)
	{
		try
		{
			string path = HistoryPath(game);
			if (!File.Exists(path)) return new List<SearchHistoryEntry>();
			return JsonConvert.DeserializeObject<List<SearchHistoryEntry>>(File.ReadAllText(path)) ?? new List<SearchHistoryEntry>();
		}
		catch { return new List<SearchHistoryEntry>(); }
	}
}
