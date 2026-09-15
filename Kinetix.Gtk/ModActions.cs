using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KinetixModManager;

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
	/// Delete pressed on the installed list. The first press arms, the second does it.
	/// </summary>
	private void DeletePressed()
	{
		int i = _installed.GetSelectedRow()?.GetIndex() ?? -1;
		if (i < 0 || i >= _view.Rows.Count) { Say(Loc.T("gtk.noModSelected"), interrupt: true); return; }

		InstalledModRow row = _view.Rows[i];

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
		SetStatus(Loc.T("gtk.deleting", row.Name));
		Say(Loc.T("gtk.deleting", row.Name));

		try
		{
			// Backed up before anything is removed, and awaited rather than started — a backup that is still
			// being written when the original is deleted is not a backup. Zipping a large mod takes long
			// enough to be worth doing off the interface's thread.
			await Task.Run(() => BackupStore.CreateBackup(row.Path, row.Name, BackupsFolder));
			await Task.Run(() =>
			{
				if (Directory.Exists(row.Path)) Directory.Delete(row.Path, recursive: true);
				else File.Delete(row.Path);
			});

			_ui.Post(() =>
			{
				LoadInstalled();
				_sound.Play("disable");
				Say(Loc.T("gtk.deleted", row.Name), interrupt: true);
				SetStatus(_view.StatusLine);
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

		InstalledModRow row = _view.Rows[i];
		SetStatus(Loc.T("gtk.backingUp", row.Name));
		Say(Loc.T("gtk.backingUp", row.Name));

		try
		{
			await Task.Run(() =>
			{
				BackupStore.CreateBackup(row.Path, row.Name, BackupsFolder);
				// Kept to the same limit the Windows head keeps, so a mod backed up before every change does
				// not quietly fill the disk.
				BackupStore.PruneBackups(row.Name, BackupsFolder, _settings.MaxBackupsPerMod);
			});

			_ui.Post(() =>
			{
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
		GameMod? mod = InstalledAsMods().FirstOrDefault(m =>
			string.Equals(m.FolderPath, row.Path, StringComparison.Ordinal));

		string id = mod?.UniqueId ?? "";
		string note = id.Length > 0 && _settings.ModNotes.TryGetValue(id, out string? saved) ? saved : "";

		Say(note.Length == 0
			? Loc.T("gtk.noteNone", row.Name)
			: Loc.T("gtk.note", row.Name, note), interrupt: true);
	}
}
