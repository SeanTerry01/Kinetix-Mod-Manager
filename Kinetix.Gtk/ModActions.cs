namespace KinetixModManager.GtkHead;

/// <summary>
/// What can be done to the mod you are standing on: delete it, back it up, or write yourself a note.
///
/// <para>
/// Deleting is the only thing in this program that destroys something the user cannot get back from inside
/// it, so it is the only thing here that asks twice. It asks <em>inline</em> rather than in a dialog, which
/// is the same choice the Windows head made when it replaced its message boxes: a modal window takes the
/// reader somewhere else and brings it back, and for a confirmation that is more disruption than the question
/// is worth. Delete arms, Delete again goes ahead, and anything else — including simply moving off the row —
/// calls it off.
/// </para>
///
/// <para>
/// A backup is taken first regardless. That is what makes the second press a reasonable thing to ask for
/// rather than a trap: the mod is in the backups folder before the original goes.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
	/// <summary>The mod a second Delete would remove, or null when nothing is armed.</summary>
	private string? _armedForDelete;

	private string BackupsFolder => Path.Combine(AppSettings.AppDataFolder, "backups", _game.Id);

	/// <summary>
	/// Where The Witcher 3's files outside a mod are looked for, or empty for any other game. See
	/// <see cref="Witcher3ModFiles.GameFolderFor"/>: the mods folder may be one the user typed, anywhere at all.
	/// </summary>
	private string WitcherGameFolder() =>
		_game.IsWitcher3 ? Witcher3ModFiles.GameFolderFor(_modsFolder, _locator.InstallFolder(_game)) : "";

	/// <summary>
	/// Says so, refreshes the list and returns true when a row's mod is no longer on disk.
	///
	/// Checked here because <see cref="BackupStore.CreateBackup"/> returns without a word when the path has gone, which has
	/// callers that rely on it. So a stale row was announced as backed up, or as deleted with a backup kept, with no backup.
	/// </summary>
	private bool RefusedAsGone(InstalledModRow row)
	{
		if (File.Exists(row.Path) || Directory.Exists(row.Path)) return false;

		LoadInstalled();
		_sound.Play("error");
		Say(Loc.T("gtk.modGone", row.Name), interrupt: true);
		SayIfListFailed();
		return true;
	}

	/// <summary>
	/// Says so and returns true for a row whose path is the mods folder itself, which Delete or Space would do to every
	/// mod at once. See <see cref="ModOnDisk.IsTheModsFolderItself"/>.
	/// </summary>
	private bool RefusedAsTheWholeModsFolder(InstalledModRow row)
	{
		if (!ModOnDisk.IsTheModsFolderItself(row.Path, _modsFolder)) return false;

		_sound.Play("error");
		Say(Loc.T("gtk.rowIsModsFolder", row.Name), interrupt: true);
		return true;
	}

	/// <summary>
	/// Delete pressed on the installed list. The first press arms, the second does it.
	/// </summary>
	private void DeletePressed()
	{
		int i = _installed.GetSelectedRow()?.GetIndex() ?? -1;
		if (i < 0 || i >= _view.Rows.Count) { Say(Loc.T("gtk.noModSelected"), interrupt: true); return; }

		// Pressed again during the backup, this would ask again and delete twice.
		if (RefusedWhileChanging()) return;

		InstalledModRow row = _view.Rows[i];
		if (RefusedAsTheWholeModsFolder(row)) return;

		if (!string.Equals(_armedForDelete, row.Path, StringComparison.Ordinal))
		{
			// Armed, and said in full. "Press Delete again" on its own would not say what is about to go.
			_armedForDelete = row.Path;
			Say(Loc.T("gtk.deleteArm", row.Name), interrupt: true);
			SetStatus(Loc.T("gtk.deleteArm", row.Name));
			return;
		}

		_armedForDelete = null;
		_ = DeleteAsync(row);
	}

	/// <summary>Anything other than a second Delete calls it off, including moving to another mod.</summary>
	private void CancelArmedDelete()
	{
		if (_armedForDelete == null) return;

		_armedForDelete = null;
		Say(Loc.T("gtk.deleteCancelled"), interrupt: true);
	}

	private async Task DeleteAsync(InstalledModRow row)
	{
		if (RefusedAsGone(row)) return;

		using IDisposable busy = _modOperations.Begin();

		SetStatus(Loc.T("gtk.deleting", row.Name));
		Say(Loc.T("gtk.deleting", row.Name));

		string backups = BackupsFolder;
		string gameId = _game.Id;
		string gameFolder = WitcherGameFolder();
		Action<string, string> log = (where, what) => DiagnosticLog.Write(where, what);

		try
		{
			// Backed up before anything is removed, and awaited rather than started — a backup that is still
			// being written when the original is deleted is not a backup. Zipping a large mod takes long
			// enough to be worth doing off the interface's thread.
			// A BepInEx mod's patchers are removed with it, so its backup carries a copy, taken out again once it is made.
				await Task.Run(() =>
				{
					BepInExPatcherFiles.StashInto(row.Path, gameId, log);
					try { BackupStore.CreateBackup(row.Path, row.Name, backups); }
					finally { BepInExPatcherFiles.ClearStash(row.Path); }
				});
			IReadOnlyList<string> stuck = await Task.Run(() =>
			{
				// A Witcher 3 mod's dlc and bin parts live in the game folder, and the record of them is inside the folder
				// about to go, so they go first. Without this it said "deleted" with those parts still loading.
				// A BepInEx mod's patchers likewise.
					IReadOnlyList<string> notRemoved = Witcher3ModFiles.RemoveExtras(row.Path, gameId, gameFolder, log)
						.Concat(BepInExPatcherFiles.Remove(row.Path, gameId, log)).ToList();

				// ⚠️ The folder stays while any of them does, because its manifest is the only record of where they are.
				if (notRemoved.Count > 0) return notRemoved;

				// Clears read-only flags and retries a passing lock, which a plain delete fails on.
				ModOnDisk.Delete(row.Path);

				Witcher3ModFiles.Forget(row.Path, gameId, gameFolder, log);
				return notRemoved;
			});

			_ui.Post(() =>
			{
				LoadBackups(announce: false);
				LoadInstalled();

				if (stuck.Count > 0)
				{
					string partly = Loc.T("gtk.deletedPartly", row.Name, string.Join(", ", stuck));
					_sound.Play("error");
					SetStatus(partly);
					Say(partly, interrupt: true);
					SayIfListFailed();
					return;
				}

				_sound.Play("disable");
				Say(Loc.T("gtk.deleted", row.Name), interrupt: true);
				SetStatus(_view.StatusLine);
				SayIfListFailed();
			});
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Mods", $"deleting {row.Path}", ex);
			_ui.Post(() =>
			{
				_sound.Play("error");
				SetStatus(Loc.T("gtk.deleteFailed", row.Name, ex.Message));
				Say(Loc.T("gtk.deleteFailed", row.Name, ex.Message), interrupt: true);
			});
		}
	}

	/// <summary>
	/// Backs the selected mod up without removing it — the thing worth doing before editing a config by hand
	/// or trying a new version.
	/// </summary>
	private async Task BackupSelectedAsync()
	{
		int i = _installed.GetSelectedRow()?.GetIndex() ?? -1;
		if (i < 0 || i >= _view.Rows.Count) { Say(Loc.T("gtk.noModSelected"), interrupt: true); return; }

		if (RefusedWhileChanging()) return;

		InstalledModRow row = _view.Rows[i];
		if (RefusedAsGone(row)) return;

		using IDisposable busy = _modOperations.Begin();

		SetStatus(Loc.T("gtk.backingUp", row.Name));
		Say(Loc.T("gtk.backingUp", row.Name));

		string backups = BackupsFolder;
		int keep = _settings.MaxBackupsPerMod;

		try
		{
			await Task.Run(() =>
			{
				BackupStore.CreateBackup(row.Path, row.Name, backups);
				// Kept to the same limit the Windows head keeps, so a mod backed up before every change does
				// not quietly fill the disk.
				BackupStore.PruneBackups(row.Name, backups, keep);
			});

			_ui.Post(() =>
			{
				LoadBackups(announce: false);
				_sound.Play("load_complete");
				SetStatus(Loc.T("gtk.backedUp", row.Name));
				Say(Loc.T("gtk.backedUp", row.Name), interrupt: true);
			});
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Backup", $"backing up {row.Path}", ex);
			_ui.Post(() =>
			{
				_sound.Play("error");
				SetStatus(Loc.T("gtk.backupFailed", row.Name, ex.Message));
				Say(Loc.T("gtk.backupFailed", row.Name, ex.Message), interrupt: true);
			});
		}
	}

	/// <summary>
	/// Reads out the note attached to the selected mod, if it has one.
	///
	/// Notes are kept by the mod's unique id in settings, which is why this is worth having on a second head
	/// at all: a note written on Windows is the same note here.
	/// </summary>
	private void SpeakNoteForSelected()
	{
		int i = _installed.GetSelectedRow()?.GetIndex() ?? -1;
		if (i < 0 || i >= _view.Rows.Count) { Say(Loc.T("gtk.noModSelected"), interrupt: true); return; }

		InstalledModRow row = _view.Rows[i];
		GameMod? mod = _scanned.FirstOrDefault(m =>
			string.Equals(m.FolderPath, row.Path, StringComparison.Ordinal));

		string id = mod?.UniqueId ?? "";
		string note = id.Length > 0 && _settings.ModNotes.TryGetValue(id, out string? saved) ? saved : "";

		Say(note.Length == 0
			? Loc.T("gtk.noteNone", row.Name)
			: Loc.T("gtk.note", row.Name, note), interrupt: true);
	}
}
