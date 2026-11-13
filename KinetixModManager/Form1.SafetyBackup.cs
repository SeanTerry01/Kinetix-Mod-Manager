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
	/// <param name="extraFile">One more file a change is about to touch, such as a mod's own settings file.</param>
	private void CreateSafetyBackup(string reason, string? extraFile = null)
	{
		if (!IsBethesdaGame) return;
		try
		{
			string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
			string dir = Path.Combine(SafetyRoot, stamp);
			Directory.CreateDirectory(dir);

			foreach (var (_, src) in SafetySourceFiles())
				if (File.Exists(src))
					CopyIntoSnapshot(src, Path.Combine(dir, Path.GetFileName(src)));

			var meta = new SafetyBackupMeta
			{
				Reason = reason,
				CreatedUtc = DateTime.UtcNow,
				ModPriority = new List<string>(_settings.ModPriority.TryGetValue(_settings.ActiveGame, out var mp) ? mp : new List<string>()),
				PluginOrder = new List<string>(_settings.PluginOrder.TryGetValue(_settings.ActiveGame, out var po) ? po : new List<string>()),
				ExtraFile = extraFile,
				HeldOff = _settings.DeploymentHeldOff.Contains(_settings.ActiveGame),
			};
			if (extraFile != null && File.Exists(extraFile))
				CopyIntoSnapshot(extraFile, Path.Combine(dir, SafetyExtraFileName));
			AtomicFile.WriteAllText(Path.Combine(dir, "safety.json"), JsonConvert.SerializeObject(meta, Formatting.Indented));

			PruneSafetyBackups();
		}
		catch (Exception ex) { LogFailure("Safety", $"Creating safety snapshot failed", ex); }
	}

	/// <summary>Keeps only the newest <see cref="MaxSafetyBackups"/> snapshots for the active game.</summary>
	private const string SafetyExtraFileName = "extra-file";

	private void PruneSafetyBackups()
	{
		try
		{
			if (!Directory.Exists(SafetyRoot)) return;
			var dirs = new DirectoryInfo(SafetyRoot).GetDirectories()
				.OrderByDescending(d => d.Name, StringComparer.Ordinal)
				.Skip(MaxSafetyBackups);
			foreach (DirectoryInfo d in dirs)
				try { DeleteSnapshot(d.FullName); }
				catch (Exception ex) { DiagnosticLog.WriteException("Safety", $"deleting the old snapshot {d.FullName}", ex); }
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Safety", "pruning old safety snapshots", ex); }
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

		// The restore runs with the view still open, and its result is said as the view closes, ahead of the list it goes
		// back to. Run once the view had gone, the list announced itself between "Restoring" and "Restored" (Sean,
		// 2026-11-12). Said from the key handler with a plain Speak, before that, the result was lost to the focus going
		// back: a failed restore gave its error sound and no reason. sayOnClosing is what holds it until it has been heard.
		bool restoring = false;
		string? sayAfterClosing = null;

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
			list.KeyDown += async (_, e) =>
			{
				if (list.SelectedItem is not SafetyBackupItem item) return;
				if (restoring && e.KeyCode is Keys.Enter or Keys.Delete)
				{
					e.Handled = e.SuppressKeyPress = true;
					Speak(Loc.T("safety.stillRestoring"));
					return;
				}

				if (e.KeyCode == Keys.Enter)
				{
					e.Handled = e.SuppressKeyPress = true;
					if (SpeakBox(Loc.T("safety.restoreConfirm", item.Summary), Loc.T("safety.restoreTitle"),
							MessageBoxButtons.YesNo) == DialogResult.Yes)
					{
						restoring = true;
						try { sayAfterClosing = await RestoreSafetyBackupAsync(item); }
						finally { restoring = false; }
						closeView();
					}
				}
				else if (e.KeyCode == Keys.Delete)
				{
					e.Handled = e.SuppressKeyPress = true;
					// It asks, like every other list, and a delete that failed is not reported as done. Said before the question
					// closes, and the row goes once that has been heard. See ActBeforeReturning.
					SpeakBox(Loc.T("safety.deleteConfirm", item.Summary), Loc.T("common.confirmDelete"), MessageBoxButtons.YesNo, answer =>
					{
						if (answer != DialogResult.Yes) return null;
						try { DeleteSnapshot(item.Dir); }
						catch (Exception ex)
						{
							LogFailure("Safety", $"Could not delete the safety backup {item.Dir}", ex);
							_soundEngine.Play("error");
							return new ResultBeforeReturning(Loc.T("safety.deleteFailed", FriendlyError(ex)));
						}
						_soundEngine.Play("disable");
						return RowRemoved(list, item, closeView, Loc.T("safety.deleted", list.Items.Count - 1), Loc.T("safety.deletedLast"));
					});
				}
			};

			return list;
		},
		// Through the hint so it follows the title instead of arriving ahead of it, and without the old heading
		// clause ("Safety backups for <game>."), which said the title's own words a second time.
		hint: Loc.T(items.Count == 1 ? "safety.countOne" : "safety.count", items.Count) + " " + Loc.T("safety.actionHint"),
		whyEscapeWaits: () => restoring ? Loc.T("safety.stillRestoring") : null,
		sayOnClosing: () => sayAfterClosing);
	}

	private List<SafetyBackupItem> LoadSafetyBackups()
	{
		var items = new List<SafetyBackupItem>();
		try
		{
			if (!Directory.Exists(SafetyRoot)) return items;
			foreach (DirectoryInfo d in new DirectoryInfo(SafetyRoot).GetDirectories())
			{
				// One at a time. A single damaged record, read first, used to end the loop and say there were no backups.
				try
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
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
				{
					DiagnosticLog.WriteException("Safety", $"reading the snapshot in {d.FullName}", ex);
				}
			}
		}
		catch (Exception ex) { LogFailure("Safety", $"Reading safety snapshots failed", ex); }
		items.Sort((a, b) => b.Meta.CreatedUtc.CompareTo(a.Meta.CreatedUtc));
		return items;
	}

	/// <summary>
	/// A file copied into a snapshot, writable. File.Copy keeps the read-only flag, so a snapshot of a protected plugins.txt
	/// held a read-only copy, and deleting that snapshot or pruning it was refused.
	/// </summary>
	private static void CopyIntoSnapshot(string from, string to)
	{
		File.Copy(from, to, overwrite: true);
		FileAttributes attributes = File.GetAttributes(to);
		if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(to, attributes & ~FileAttributes.ReadOnly);
	}

	/// <summary>Deletes a snapshot folder, through any read-only file an older snapshot was taken with.</summary>
	private static void DeleteSnapshot(string dir)
	{
		foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
		{
			FileAttributes attributes = File.GetAttributes(file);
			if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
		}
		Directory.Delete(dir, recursive: true);
	}

	/// <summary>
	/// Puts a saved file back over the live one, through a read-only flag, and leaves the flag as it found it.
	///
	/// ⚠️ plugins.txt is made read-only when the manager protects the load order from the game, and game INI files often are
	/// on purpose. Copying over one failed with "access was denied", so restoring the snapshot taken before a purge gave an
	/// error and left the purge in place. Measured on the test copy 2026-11-10.
	/// </summary>
	private static void CopyOverKeepingReadOnly(string from, string to)
	{
		bool wasReadOnly = File.Exists(to) && (File.GetAttributes(to) & FileAttributes.ReadOnly) != 0;
		if (wasReadOnly) File.SetAttributes(to, File.GetAttributes(to) & ~FileAttributes.ReadOnly);
		try
		{
			File.Copy(from, to, overwrite: true);
		}
		finally
		{
			if (wasReadOnly && File.Exists(to)) File.SetAttributes(to, File.GetAttributes(to) | FileAttributes.ReadOnly);
		}
	}

	/// <summary>
	/// Restores a snapshot: copies its files back, reinstates the saved load order, and re-deploys. Returns what to say about
	/// it, which the view says as it closes; null when there is nothing to add.
	/// </summary>
	private async Task<string?> RestoreSafetyBackupAsync(SafetyBackupItem item)
	{
		string game = _settings.ActiveGame;
		if (ModOperationRunning) return Loc.T("session.busy");
		using IDisposable busy = BeginModOperation();
		Speak(Loc.T("safety.restoring"));
		SetStatus(Loc.T("safety.restoring"), speak: false);

		// A refresh rewrites plugins.txt, and this is about to put an old one back.
		while (Volatile.Read(ref _refreshInFlight) > 0) await Task.Delay(25);
		if (_settings.ActiveGame != game) { ResetStatus(); return null; }

		try
		{
			// Copy each captured file back to its original location.
			foreach (var (_, dest) in SafetySourceFiles())
			{
				string src = Path.Combine(item.Dir, Path.GetFileName(dest));
				if (!File.Exists(src)) continue;
				string? destDir = Path.GetDirectoryName(dest);
				if (destDir != null && !Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
				CopyOverKeepingReadOnly(src, dest);
			}

			if (item.Meta.ExtraFile is { Length: > 0 } extra)
			{
				string saved = Path.Combine(item.Dir, SafetyExtraFileName);
				if (File.Exists(saved))
				{
					if (Path.GetDirectoryName(extra) is { Length: > 0 } extraFolder) Directory.CreateDirectory(extraFolder);
					CopyOverKeepingReadOnly(saved, extra);
				}
				// It did not exist before the change, so it goes.
				else if (File.Exists(extra)) File.Delete(extra);
			}

			// The snapshot taken before a purge has to undo the purge, and left held, the deployment below would do nothing.
			lock (_deploymentLock)
			{
				if (item.Meta.HeldOff) _settings.DeploymentHeldOff.Add(game);
				else _settings.DeploymentHeldOff.Remove(game);
			}

			// Reinstate the saved load order in settings so the manager's state matches the restored files.
			if (item.Meta.ModPriority.Count > 0) _settings.ModPriority[_settings.ActiveGame] = new List<string>(item.Meta.ModPriority);
			if (item.Meta.PluginOrder.Count > 0) _settings.PluginOrder[_settings.ActiveGame] = new List<string>(item.Meta.PluginOrder);
			_settings.Save();

			// Re-deploy so the game folder matches the restored priority, and rewrite plugins.txt from the restored order.
			// Off this thread: on a large setup a deployment takes seconds, and the window would seem dead.
			bool trustworthy = ModListIsTrustworthy;
			if (trustworthy)
			{
				await Task.Run(() => SyncBethesdaDeployment());
				SyncBethesdaPlugins();
			}
			RefreshModPriorityList();
			RefreshPluginOrderList();

			ResetStatus();
			_soundEngine.Play("load_complete");
			// Both of those do nothing without a list to go by, and it still said "restored".
			return Loc.T(trustworthy ? "safety.restored" : "safety.restoredNotDeployed", item.Summary);
		}
		catch (Exception ex)
		{
			LogFailure("Safety", $"Restoring safety snapshot failed", ex);
			ResetStatus();
			_soundEngine.Play("error");
			return Loc.T("safety.restoreFailed", FriendlyError(ex));
		}
	}
}
