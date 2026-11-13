namespace KinetixModManager;

/// <summary>
/// The updates Delete on the Updates tab was told to ignore, and the way back from that.
///
/// <para>
/// Ignoring was one way only. Once a version was ignored it was never offered again and nothing in the manager could
/// undo it, short of editing settings.json. Sean found three Stardew updates hidden that way on 2026-11-11, ignored
/// while the ignore itself was being tested, with no means of getting them back.
/// </para>
/// </summary>
public partial class Form1
{
	private sealed class IgnoredUpdateRow
	{
		public string Key { get; init; } = "";
		public string Version { get; init; } = "";
		public string Summary { get; init; } = "";
		/// <summary>A version recorded with Ctrl+Shift+Y rather than an ignored one. Its key is the download's, "Nexus:1428".</summary>
		public bool Recorded { get; init; }
		public string Name { get; init; } = "";
		/// <summary>The download an ignored mod came from, "Nexus:15792", so the ones ignored together are undone together.</summary>
		public string DownloadKey { get; init; } = "";
		/// <summary>For a recorded version, how many other mods came in the same download, and so are settled by it too.</summary>
		public int Others { get; init; }
		public override string ToString() => Summary;
	}

	/// <summary>
	/// This game's ignored updates. An ignore is kept by the mod's own id with no game attached, so only the ids of mods
	/// installed here are this game's; Minecraft's loader, version and modpack rows have ids of the manager's own.
	/// </summary>
	private List<IgnoredUpdateRow> IgnoredUpdatesForThisGame()
	{
		var byId = _allInstalledMods
			.Where(m => !m.IsGroup && !string.IsNullOrEmpty(m.UniqueId))
			.GroupBy(m => m.UniqueId, StringComparer.Ordinal)
			.ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
		var names = byId.ToDictionary(p => p.Key, p => p.Value.Name, StringComparer.Ordinal);
		bool minecraft = GameProfiles.Find(_settings.ActiveGame)?.IsMinecraft == true;

		var rows = new List<IgnoredUpdateRow>();
		foreach ((string key, string version) in _settings.IgnoredVersions)
		{
			string? name = names.GetValueOrDefault(key);
			if (name == null && minecraft)
				name = key switch
				{
					FabricLoaderRowId => Loc.T("ignoredUpdates.fabricLoader"),
					MinecraftVersionRowId => Loc.T("ignoredUpdates.minecraft"),
					ModpackRowId => Loc.T("ignoredUpdates.modpack"),
					_ => null
				};
			if (name == null) continue;
			string download = byId.TryGetValue(key, out GameMod? installed) ? DownloadKey(installed) : "";
			rows.Add(new IgnoredUpdateRow
			{
				Key = key, Version = version, Name = name, Summary = Loc.T("ignoredUpdates.row", name, version),
				DownloadKey = download is "Nexus:" or "GitHub:" ? "" : download
			});
		}

		// And the versions recorded with Ctrl+Shift+Y, which hide an update the same way and had no way back either. Only
		// while the record is still the one Ctrl+Shift+Y made: an install since then replaced it with a known one.
		// One record covers a whole download, so its row names the others it took off the Updates list with it: Ctrl+Shift+Y
		// on one of Bush Bloom Mod's examples took all three away, and the row named only the one (Sean, 2026-11-12). The names
		// are the ones noted when it was pressed (see NoteMarked), so the row says exactly what left the list.
		if (_settings.MarkedInstalledVersions.TryGetValue(_settings.ActiveGame, out var marks))
			foreach ((string key, string settled) in marks)
				if (InstalledDownloadVersion(key) is { } recorded)
				{
					string[] settledNames = settled.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
					string modName = settledNames.FirstOrDefault() ?? key;
					List<string> others = settledNames.Skip(1).ToList();
					rows.Add(new IgnoredUpdateRow
					{
						Key = key, Version = recorded, Recorded = true, Name = modName, Others = others.Count,
						Summary = others.Count == 0
							? Loc.T("ignoredUpdates.recordedRow", modName, recorded)
							: Loc.T("ignoredUpdates.recordedRowWithOthers", modName, recorded, others.Count, string.Join(", ", others))
					});
				}

		return rows.OrderBy(r => r.Summary, StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	/// <summary>Lists this game's ignored updates; Enter or Delete stops ignoring one, and the next update check offers it again.</summary>
	private void ShowIgnoredUpdates()
	{
		if (_settings.ActiveGame == GameProfiles.NoGame) return;
		List<IgnoredUpdateRow> rows = IgnoredUpdatesForThisGame();
		if (rows.Count == 0)
		{
			Speak(Loc.T("ignoredUpdates.none"));
			return;
		}

		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("ignoredUpdates.title"), (container, closeView) =>
		{
			var list = new ListBox
			{
				Dock = DockStyle.Fill,
				Font = new Font("Segoe UI", 12f),
				AccessibleName = Loc.T("ignoredUpdates.listName"),
				IntegralHeight = false
			};
			foreach (IgnoredUpdateRow row in rows) list.Items.Add(row);
			list.SelectedIndex = 0;
			container.Controls.Add(list);

			WireAccessibleDialogList(list);
			// Escape is handled by the view itself (see Form1.InlineView).
			list.KeyDown += (_, e) =>
			{
				if (e.KeyCode is not (Keys.Delete or Keys.Enter) || list.SelectedItem is not IgnoredUpdateRow row) return;
				e.Handled = e.SuppressKeyPress = true;

				string said;
				// Undone together, as they were ignored: the others from the same download, ignored at the same version.
				List<IgnoredUpdateRow> together = row.Recorded || row.DownloadKey.Length == 0
					? new List<IgnoredUpdateRow> { row }
					: list.Items.OfType<IgnoredUpdateRow>()
						.Where(r => ReferenceEquals(r, row) || (!r.Recorded && r.Version == row.Version &&
							r.DownloadKey.Equals(row.DownloadKey, StringComparison.OrdinalIgnoreCase)))
						.ToList();
				if (row.Recorded)
				{
					// Back to comparing against what the mod says it is, as before Ctrl+Shift+Y was pressed.
					ForgetInstalledDownloadVersion(row.Key);
					said = row.Others > 0
						? Loc.T("ignoredUpdates.recordForgottenWithOthers", row.Name, row.Version, row.Others)
						: Loc.T("ignoredUpdates.recordForgotten", row.Name, row.Version);
				}
				else
				{
					foreach (IgnoredUpdateRow r in together) _settings.IgnoredVersions.Remove(r.Key);
					_settings.Save();
					said = together.Count > 1
						? Loc.T("ignoredUpdates.restoredWithOthers", row.Summary, together.Count - 1)
						: Loc.T("ignoredUpdates.restored", row.Summary);
				}

				int above = together.Count(r => list.Items.IndexOf(r) < list.SelectedIndex);
				int index = list.SelectedIndex - above;
				_soundEngine.Play("enable");
				if (list.Items.Count == together.Count)
				{
					Speak(said + " " + Loc.T("ignoredUpdates.restoredLast"));
					LetSpeechFinish();
					closeView();
					return;
				}

				// The result first and heard out, then the row it lands on, once. Taking the row out first had NVDA read the
				// row that moved up, and then SayResultAndLandOn named it again in its own sentence (heard 2026-11-11).
				Speak(said);
				LetSpeechFinish();
				_movingListSilently = true;
				try { foreach (IgnoredUpdateRow r in together) list.Items.Remove(r); }
				finally { _movingListSilently = false; }
				_announceRowNameOnNextChange = true;
				list.SelectedIndex = Math.Clamp(index, 0, list.Items.Count - 1);
				AlignListCaretToSelection(list);
			};
			return list;
		},
		hint: Loc.T("ignoredUpdates.hint", rows.Count));
	}
}
