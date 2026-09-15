using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// The downloads history / re-install list (Mods menu / shortcut). Every mod archive the manager has downloaded
/// stays in the per-game downloads folder, so this lists them newest-first and lets the user re-install one with a
/// keypress — no re-searching Nexus — or delete it to free space. Works for every game (the downloads folder is
/// per active game). Re-install routes through the same InstallFromZip path as picking an archive by hand.
/// </summary>
public partial class Form1
{
	private static readonly string[] DownloadArchiveExtensions = { ".zip", ".7z", ".rar" };

	/// <summary>Entry point (Mods menu / shortcut): lists the active game's downloaded archives for re-install.</summary>
	private void ShowDownloadsHistory()
	{
		if (_settings.ActiveGame == "None")
		{
			Speak(Loc.T("downloads.noGame"));
			return;
		}

		List<DownloadItem> items;
		try
		{
			items = Directory.EnumerateFiles(downloadsPath)
				.Where(f => DownloadArchiveExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
				.Select(f => new FileInfo(f))
				.Select(fi => new DownloadItem
				{
					FullPath = fi.FullName,
					Size = fi.Length,
					Date = fi.LastWriteTime,
					Summary = Loc.T("downloads.row", fi.Name, FormatBytes(fi.Length), fi.LastWriteTime.ToString("g"))
				})
				.OrderByDescending(d => d.Date)
				.ToList();
		}
		catch (Exception ex)
		{
			LogFailure("Downloads", $"Listing downloads failed", ex);
			items = new List<DownloadItem>();
		}

		if (items.Count == 0)
		{
			Speak(Loc.T("downloads.none"));
			return;
		}

		ShowDownloadsDialog(items);
	}

	private void ShowDownloadsDialog(List<DownloadItem> items)
	{
		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("downloads.title"), (container, closeView) =>
			{
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			layout.Controls.Add(new Label { Text = Loc.T("downloads.header", GameDisplayName()), AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8) }, 0, 0);

			var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = Loc.T("downloads.listName"), IntegralHeight = false, HorizontalScrollbar = true };
			foreach (DownloadItem d in items) list.Items.Add(d);
			if (list.Items.Count > 0) list.SelectedIndex = 0;
			layout.Controls.Add(list, 0, 1);
			container.Controls.Add(layout);

			WireAccessibleDialogList(list);
			bool busy = false;
			// Escape is handled by the view itself (see Form1.InlineView).
			list.KeyDown += async (_, e) =>
			{
				if (list.SelectedItem is not DownloadItem item) return;
				if (busy && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Delete))
				{
					// One at a time: the install in progress still has this list under it.
					e.Handled = e.SuppressKeyPress = true;
					return;
				}

				if (e.KeyCode == Keys.Enter)
				{
					e.Handled = e.SuppressKeyPress = true;
					// Same route as picking an archive by hand; it runs its own progress and overwrite prompt. The list
					// stays open, and every download is still in it afterwards, so the user can go on to the next.
					busy = true;
					try { await InstallFromZip(item.FullPath, confirmReinstall: true); }
					finally { busy = false; }
				}
				else if (e.KeyCode == Keys.Delete)
				{
					e.Handled = e.SuppressKeyPress = true;
					DeleteDownload(closeView, list, item);
				}
			};

			return list;
		},
		// Through the hint, and without the old heading clause ("Downloaded mods for <game>."), which said the
		// title's own words again. See Form1.InlineView: a Speak during build lands ahead of the title.
		hint: Loc.T(items.Count == 1 ? "downloads.countOne" : "downloads.count", items.Count)
			+ " " + Loc.T("downloads.actionHint"));
	}

	/// <summary>
	/// Entry point (Mods menu / Ctrl+Shift+P): the downloads that were never installed — the ones the user said "not
	/// now" to when the manager offered. Installed downloads are left out even though they are still in the folder;
	/// the Reinstall list is where those are. See <see cref="PendingDownloads"/> for how the two are told apart.
	/// </summary>
	private void ShowPendingDownloads()
	{
		if (_settings.ActiveGame == "None")
		{
			Speak(Loc.T("downloads.noGame"));
			return;
		}

		bool minecraft = GameProfiles.Find(_settings.ActiveGame)?.IsMinecraft == true;
		List<DownloadedFile> pending;
		try
		{
			var downloads = new List<DownloadedFile>();
			if (Directory.Exists(downloadsPath))
			{
				foreach (FileInfo fi in new DirectoryInfo(downloadsPath).EnumerateFiles())
				{
					string ext = fi.Extension.ToLowerInvariant();
					if (minecraft ? ext != MinecraftLayout.ModExtension : !DownloadArchiveExtensions.Contains(ext)) continue;
					try { downloads.Add(DescribeDownload(fi, minecraft)); }
					catch (Exception ex)
					{
						// One file that cannot be read (still being written, held open) costs its details, not the list.
						DiagnosticLog.WriteException("Downloads", $"reading {fi.FullName}", ex);
						downloads.Add(new DownloadedFile(fi.FullName, fi.LastWriteTime, fi.Length, null, null,
							Path.GetFileNameWithoutExtension(fi.Name)));
					}
				}
			}

			_settings.InstalledArchives.TryGetValue(_settings.ActiveGame, out List<string>? recorded);
			pending = PendingDownloads.NotYetInstalled(
				downloads, recorded ?? new List<string>(), _settings.InstalledArchivesSince, _allInstalledMods,
				// A Minecraft mod is known by its Fabric id, which is its UniqueId; everything else by its Nexus page.
				idOf: m => minecraft ? m.UniqueId : m.NexusID,
				releaseOf: m => (minecraft ? null : InstalledDownloadVersion(DownloadKey(m))) ?? m.Version);
		}
		catch (Exception ex)
		{
			LogFailure("Downloads", "Listing downloads not yet installed failed", ex);
			pending = new List<DownloadedFile>();
		}

		if (pending.Count == 0)
		{
			Speak(Loc.T("pending.none"));
			return;
		}

		string? sayAfterClosing = null;
		bool viewGone = false;
		string game = _settings.ActiveGame;

		ShowInlineView(Loc.T("pending.title"), (container, closeView) =>
		{
			var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = Loc.T("pending.listName"), IntegralHeight = false, HorizontalScrollbar = true };
			foreach (DownloadedFile d in pending)
				list.Items.Add(new DownloadItem
				{
					FullPath = d.FullPath,
					Size = d.Bytes,
					Date = d.DownloadedAt,
					// The mod's name before the file's: "Skyrim Access-181131-1-2-3-1723456789.7z" is not something to
					// listen to when the name, the release and the date are what tell rows apart.
					Summary = string.IsNullOrWhiteSpace(d.Version)
						? Loc.T("pending.row", d.Name, d.DownloadedAt.ToString("g"), FormatBytes(d.Bytes))
						: Loc.T("pending.rowVersion", d.Name, d.Version!, d.DownloadedAt.ToString("g"), FormatBytes(d.Bytes))
				});
			list.SelectedIndex = 0;
			container.Controls.Add(list);

			WireAccessibleDialogList(list);

			// Takes out whatever has now been installed, and closes the list once nothing is left in it. Run before
			// focus comes back from the install's own prompts (see Form1.StayingLists), and again when it finishes.
			bool Refresh()
			{
				if (viewGone || list.IsDisposed) return false;
				_settings.InstalledArchives.TryGetValue(game, out List<string>? recorded);
				List<DownloadItem> remaining = list.Items.Cast<DownloadItem>()
					.Where(i => File.Exists(i.FullPath) &&
								!(recorded?.Contains(Path.GetFileName(i.FullPath), StringComparer.OrdinalIgnoreCase) ?? false))
					.ToList();
				if (remaining.Count == list.Items.Count) return true;
				if (remaining.Count == 0)
				{
					sayAfterClosing = Loc.T("pending.allInstalled");
					closeView();
					return false;
				}
				ReplaceRowsSilently(list, remaining, (a, b) => a.FullPath == b.FullPath);
				return true;
			}
			RefreshBeforeFocusReturns(list, Refresh);

			bool busy = false;
			list.KeyDown += async (_, e) =>
			{
				if (list.SelectedItem is not DownloadItem item) return;
				if (busy && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Delete))
				{
					e.Handled = e.SuppressKeyPress = true;
					return;
				}

				if (e.KeyCode == Keys.Enter)
				{
					e.Handled = e.SuppressKeyPress = true;
					string path = item.FullPath;
					int countBefore = list.Items.Count;

					busy = true;
					try
					{
						if (minecraft)
							await InstallMinecraftJarAsync(path);
						else
							await InstallFromZip(path, ModDisplayName.ModIdFromArchiveName(Path.GetFileName(path)), confirmReinstall: true);
					}
					finally { busy = false; }

					if (viewGone || list.IsDisposed || !Refresh()) return;
					// A Minecraft install says it is done without opening anything, so focus never left the list and
					// nothing has read the row that took the installed one's place. The other games end on a message
					// that returns focus here, which reads it already.
					if (list.Focused && list.Items.Count != countBefore && list.SelectedItem != null)
						Speak(RowThenPosition(list.SelectedItem, PositionTextFor(list)));
				}
				else if (e.KeyCode == Keys.Delete)
				{
					e.Handled = e.SuppressKeyPress = true;
					DeleteDownload(closeView, list, item);
				}
			};

			return list;
		},
		onClosed: () =>
		{
			viewGone = true;
			if (sayAfterClosing != null) Speak(sayAfterClosing);
		},
		hint: Loc.T(pending.Count == 1 ? "pending.countOne" : "pending.count", pending.Count)
			+ " " + Loc.T("pending.actionHint"));
	}

	/// <summary>What the pending list needs to know about one file in the downloads folder.</summary>
	private static DownloadedFile DescribeDownload(FileInfo fi, bool minecraft)
	{
		if (minecraft)
		{
			// A jar's name is whatever its author called the file; the id and version inside it are what match it to
			// an installed mod.
			FabricModInfo info = MinecraftLayout.ReadModInfo(fi.FullName);
			return new DownloadedFile(fi.FullName, fi.LastWriteTime, fi.Length,
				info.IsUnreadable || info.Id.Length == 0 ? null : info.Id,
				info.IsUnreadable ? null : info.Version,
				info.Name.Length > 0 ? info.Name : Path.GetFileNameWithoutExtension(fi.Name));
		}

		string? modId = ModDisplayName.ModIdFromArchiveName(fi.Name);
		return new DownloadedFile(fi.FullName, fi.LastWriteTime, fi.Length, modId,
			ModScanner.ExtractVersionFromFileName(fi.Name, modId),
			ModDisplayName.ForSpeech(Path.GetFileNameWithoutExtension(fi.Name), modId));
	}

	/// <summary>
	/// Notes that the download at <paramref name="archivePath"/> has been installed, so it stops being offered as one
	/// that never was. Called after every install that succeeds; anything outside the downloads folder is not a
	/// download and is ignored.
	/// </summary>
	private void RecordDownloadInstalled(string archivePath)
	{
		try
		{
			string? folder = Path.GetDirectoryName(Path.GetFullPath(archivePath));
			if (folder == null || !string.Equals(folder.TrimEnd(Path.DirectorySeparatorChar),
					Path.GetFullPath(downloadsPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
				return;

			if (!_settings.InstalledArchives.TryGetValue(_settings.ActiveGame, out List<string>? names))
				_settings.InstalledArchives[_settings.ActiveGame] = names = new List<string>();

			string name = Path.GetFileName(archivePath);
			if (names.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
			names.Add(name);
			_settings.Save();
		}
		catch (Exception ex)
		{
			// The mod is installed either way. The cost of failing here is only that its download is offered again.
			LogFailure("Downloads", "Recording an installed download failed", ex);
		}
	}

	/// <summary>Sends a downloaded archive to the Recycle Bin after confirmation and drops it from the list.</summary>
	private void DeleteDownload(Action closeView, ListBox list, DownloadItem item)
	{
		if (SpeakBox(Loc.T("downloads.deleteConfirm", Path.GetFileName(item.FullPath)), Loc.T("downloads.deleteTitle"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
			return;

		try
		{
			Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.FullPath,
				Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
				Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
		}
		catch (Exception ex)
		{
			LogFailure("Downloads", $"Deleting a download failed", ex);
			_soundEngine.Play("error");
			Speak(Loc.T("downloads.deleteFailed", FriendlyError(ex)));
			return;
		}

		int idx = list.SelectedIndex;
		list.Items.Remove(item);
		_soundEngine.Play("disable");
		if (list.Items.Count == 0) { Speak(Loc.T("downloads.deletedLast")); closeView(); return; }
		list.SelectedIndex = Math.Min(idx, list.Items.Count - 1);
		Speak(Loc.T("downloads.deleted", list.Items.Count));
	}

	/// <summary>Formats a byte count as a short human-readable size (e.g. "12.3 MB") for spoken/displayed rows.</summary>
	private static string FormatBytes(long bytes)
	{
		string[] units = { "bytes", "KB", "MB", "GB" };
		double size = bytes;
		int unit = 0;
		while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
		return unit == 0 ? $"{bytes} {units[unit]}" : $"{size:0.#} {units[unit]}";
	}
}
