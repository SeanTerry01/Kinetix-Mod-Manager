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
		public override string ToString() => Summary;
	}

	/// <summary>
	/// This game's ignored updates. An ignore is kept by the mod's own id with no game attached, so only the ids of mods
	/// installed here are this game's; Minecraft's loader, version and modpack rows have ids of the manager's own.
	/// </summary>
	private List<IgnoredUpdateRow> IgnoredUpdatesForThisGame()
	{
		var names = _allInstalledMods
			.Where(m => !m.IsGroup && !string.IsNullOrEmpty(m.UniqueId))
			.GroupBy(m => m.UniqueId, StringComparer.Ordinal)
			.ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
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
			rows.Add(new IgnoredUpdateRow { Key = key, Version = version, Summary = Loc.T("ignoredUpdates.row", name, version) });
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

				_settings.IgnoredVersions.Remove(row.Key);
				_settings.Save();

				int index = list.SelectedIndex;
				_soundEngine.Play("enable");
				string said = Loc.T("ignoredUpdates.restored", row.Summary);
				if (list.Items.Count == 1)
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
				try { list.Items.Remove(row); }
				finally { _movingListSilently = false; }
				_announceRowNameOnNextChange = true;
				list.SelectedIndex = Math.Min(index, list.Items.Count - 1);
				AlignListCaretToSelection(list);
			};
			return list;
		},
		hint: Loc.T("ignoredUpdates.hint", rows.Count));
	}
}
