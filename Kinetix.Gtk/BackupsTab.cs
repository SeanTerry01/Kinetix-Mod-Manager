using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KinetixModManager;

namespace KinetixModManager.GtkHead;

/// <summary>
/// The Backups screen: the copies kept before anything was overwritten or deleted, and putting one back.
///
/// <para>
/// This closes the loop the delete action opened (§31). Taking a backup before deleting is only worth doing
/// if the backup can be got at again, and until now the Linux build could make them and never restore one —
/// which is the shape of a promise rather than a feature.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
	private readonly Gtk.ListBox _backups = Gtk.ListBox.New();
	private BackupsView _backupsView = BackupsView.Of(Array.Empty<BackupItem>());

	/// <summary>The backup a second Restore would put back, for the same reason delete arms first.</summary>
	private string? _armedForRestore;

	private Gtk.Widget BuildBackupsTab()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 6);

		var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);

		var restore = Gtk.Button.NewWithLabel(Loc.T("gtk.backupsRestore"));
		restore.OnClicked += (_, _) => RestorePressed();
		buttons.Append(restore);

		var refresh = Gtk.Button.NewWithLabel(Loc.T("gtk.backupsRefresh"));
		refresh.OnClicked += (_, _) => LoadBackups(announce: true);
		buttons.Append(refresh);

		var forget = Gtk.Button.NewWithLabel(Loc.T("gtk.backupsDelete"));
		forget.OnClicked += (_, _) => DeleteSelectedBackup();
		buttons.Append(forget);

		box.Append(buttons);

		_backups.SetVexpand(true);
		// Moving off a row calls off an armed restore, for the same reason it does for delete: the backup
		// being confirmed is no longer the one under the cursor.
		_backups.OnRowSelected += (_, _) => CancelArmedRestore();

		var scroller = Gtk.ScrolledWindow.New();
		scroller.SetChild(_backups);
		scroller.SetVexpand(true);
		box.Append(scroller);

		LoadBackups(announce: false);
		return box;
	}

	private void LoadBackups(bool announce)
	{
		_backupsView = BackupsView.Of(BackupStore.List(BackupsFolder));

		while (_backups.GetFirstChild() is { } child) _backups.Remove(child);
		foreach (BackupRow row in _backupsView.Rows) _backups.Append(RowLabel(row.Spoken));

		SetStatus(_backupsView.Announcement);
		if (announce) Say(_backupsView.Announcement, interrupt: true);
	}

	private void CancelArmedRestore()
	{
		if (_armedForRestore == null) return;

		_armedForRestore = null;
		Say(Loc.T("gtk.backupsRestoreCancelled"), interrupt: true);
	}

	/// <summary>
	/// Restoring is an overwrite, so it asks twice — the same inline arm-and-confirm the delete action uses.
	/// </summary>
	private void RestorePressed()
	{
		int i = _backups.GetSelectedRow()?.GetIndex() ?? -1;
		if (i < 0 || i >= _backupsView.Rows.Count) { Say(Loc.T("gtk.backupsNoneSelected"), interrupt: true); return; }

		BackupRow row = _backupsView.Rows[i];

		if (!string.Equals(_armedForRestore, row.Path, StringComparison.Ordinal))
		{
			_armedForRestore = row.Path;

			// Said with what it would overwrite, because "restore this" and "replace what you have with
			// this" are the same action and only the second one is honest about it.
			bool somethingThere = _view.Rows.Any(r =>
				string.Equals(r.Name, row.ModName, StringComparison.OrdinalIgnoreCase));

			string what = BackupsView.DescribeRestoring(row, somethingThere);
			Say(what + " " + Loc.T("gtk.backupsRestoreArm"), interrupt: true);
			SetStatus(what);
			return;
		}

		_armedForRestore = null;
		_ = RestoreAsync(row);
	}

	private async Task RestoreAsync(BackupRow row)
	{
		string mods = ModsFolderFor(_game);
		if (string.IsNullOrEmpty(mods)) { Say(Loc.T("gtk.noModsFolder"), interrupt: true); return; }

		SetStatus(Loc.T("gtk.backupsRestoring", row.ModName));
		Say(Loc.T("gtk.backupsRestoring", row.ModName));

		try
		{
			// The same unpacker every install uses, so a backup made on Windows restores here and the
			// escape guard applies to it too — a zip is a zip, whoever wrote it.
			await Task.Run(() =>
			{
				ModArchive.Extract(row.Path, mods);
				ModArchive.GuardAgainstEscapedEntries(mods);
			});

			_ui.Post(() =>
			{
				LoadInstalled();
				_sound.Play("load_complete");
				SetStatus(Loc.T("gtk.backupsRestored", row.ModName));
				Say(Loc.T("gtk.backupsRestored", row.ModName), interrupt: true);
			});
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Backup", $"restoring {row.Path}", ex);
			_ui.Post(() =>
			{
				_sound.Play("error");
				SetStatus(Loc.T("gtk.backupsRestoreFailed", row.ModName, ex.Message));
				Say(Loc.T("gtk.backupsRestoreFailed", row.ModName, ex.Message), interrupt: true);
			});
		}
	}

	/// <summary>Forgets one kept copy. It touches nothing that is installed, which is worth saying.</summary>
	private void DeleteSelectedBackup()
	{
		int i = _backups.GetSelectedRow()?.GetIndex() ?? -1;
		if (i < 0 || i >= _backupsView.Rows.Count) { Say(Loc.T("gtk.backupsNoneSelected"), interrupt: true); return; }

		BackupRow row = _backupsView.Rows[i];

		try
		{
			File.Delete(row.Path);
			LoadBackups(announce: false);
			_sound.Play("disable");
			Say(Loc.T("gtk.backupsDeleted", row.ModName), interrupt: true);
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Backup", $"deleting {row.Path}", ex);
			_sound.Play("error");
			Say(Loc.T("gtk.backupsDeleteFailed", ex.Message), interrupt: true);
		}
	}
}
