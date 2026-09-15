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
