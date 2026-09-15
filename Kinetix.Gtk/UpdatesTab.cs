using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KinetixModManager;

namespace KinetixModManager.GtkHead;

/// <summary>
/// The Updates screen: what is installed that has a newer version waiting.
///
/// <para>
/// Checked by the hash of each file rather than by a stored mod id, which is why this works at all on a head
/// with no Nexus: Modrinth will take a list of SHA-1s and answer with the newest build of whatever it
/// recognises. Nothing has to have been linked to a mod page first, and a mod the catalogue has never seen
/// is simply not mentioned — which is the right answer for a hand-built mod, not an error.
/// </para>
///
/// <para>
/// Everything about what a row says and what is announced lives in <see cref="ModUpdatesView"/>, in the core.
/// The distinction that matters there is between "everything is up to date" and "nothing here could be
/// checked": both are an empty list, and only one of them means the user has nothing to do.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
	private readonly Gtk.ListBox _updates = Gtk.ListBox.New();
	private ModUpdatesView _updatesView = ModUpdatesView.CannotCheck(null, "");

	private Gtk.Widget BuildUpdatesTab()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 6);

		var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);

		var check = Gtk.Button.NewWithLabel(Loc.T("gtk.updatesCheck"));
		check.OnClicked += (_, _) => _ = CheckForUpdatesAsync();
		buttons.Append(check);

		var install = Gtk.Button.NewWithLabel(Loc.T("gtk.updatesInstall"));
		install.OnClicked += (_, _) => _ = InstallSelectedUpdateAsync();
		buttons.Append(install);

		box.Append(buttons);

		_updates.SetVexpand(true);

		var scroller = Gtk.ScrolledWindow.New();
		scroller.SetChild(_updates);
		scroller.SetVexpand(true);
		box.Append(scroller);

		return box;
	}

	/// <summary>
	/// Asks the catalogue about every installed mod at once.
	///
	/// One request for the whole folder rather than one per mod — the API takes a list of hashes, and asking
	/// fifty times would be fifty round trips and a rate limit besides.
	/// </summary>
	private async Task CheckForUpdatesAsync()
	{
		if (!_game.IsMinecraft)
		{
			// Honest about the boundary rather than an empty list. Nexus's service is still in the WinForms
			// project, so the games whose updates come from there cannot be checked from here yet.
			_updatesView = ModUpdatesView.CannotCheck(_game, Loc.T("gtk.updatesNotHere", _game.DisplayName));
			ShowUpdates();
			return;
		}

		string version = GameVersionInUse();
		if (version.Length == 0)
		{
			_updatesView = ModUpdatesView.CannotCheck(_game, Loc.T("gtk.noGameVersion"));
			ShowUpdates();
			return;
		}

		SetStatus(Loc.T("gtk.updatesChecking"));
		Say(Loc.T("gtk.updatesChecking"));

		try
		{
			List<GameMod> installed = _view.Rows
				.Select(r => new GameMod { FolderPath = r.Path, Name = r.Name, Version = r.Version })
				.ToList();

			// Hashing is disk work and there may be a hundred of them, so it happens off the interface's
			// thread along with the request.
			var byHash = await Task.Run(() =>
			{
				var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (GameMod mod in installed)
				{
					try { hashes[ModrinthService.Sha1Of(mod.FolderPath)] = mod.FolderPath; }
					catch (Exception ex)
					{
						// A file that cannot be read is not an update and not a reason to abandon the check.
						DiagnosticLog.WriteException("Updates", $"hashing {mod.FolderPath}", ex);
					}
				}
				return hashes;
			});

			Dictionary<string, ModrinthFile> latest =
				await ModrinthService.GetLatestForHashesAsync(byHash.Keys.ToList(), version);

			var latestByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var (hash, file) in latest)
				if (byHash.TryGetValue(hash, out string? path)) latestByPath[path] = file.VersionNumber;

			_updatesView = ModUpdatesView.Of(_game, installed, latestByPath);
			_ui.Post(ShowUpdates);
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Updates", "checking for updates", ex);
			_updatesView = ModUpdatesView.CannotCheck(_game, Loc.T("gtk.updatesFailed", ex.Message));
			_ui.Post(() => { _sound.Play("error"); ShowUpdates(); });
		}
	}

	private void ShowUpdates()
	{
		while (_updates.GetFirstChild() is { } child) _updates.Remove(child);
		foreach (ModUpdateRow row in _updatesView.Rows) _updates.Append(RowLabel(row.Spoken));

		SetStatus(_updatesView.Announcement);

		// The cue only for good news. An empty list because nothing could be checked is not a completed
		// operation, and sounding as though it were would be the wrong fact.
		if (_updatesView.Status == ModUpdatesStatus.Checked) _sound.Play("load_complete");

		Say(_updatesView.Announcement, interrupt: true);
	}

	/// <summary>
	/// Installs the selected update over the copy already there.
	///
	/// The same install path a fresh one takes, so what it does about an older copy of the same mod is
	/// whatever <see cref="ModInstaller"/> does — a second copy left beside the new one is a game that
	/// refuses to start on a duplicate mod id.
	/// </summary>
	private async Task InstallSelectedUpdateAsync()
	{
		int i = _updates.GetSelectedRow()?.GetIndex() ?? -1;
		if (i < 0 || i >= _updatesView.Rows.Count) { Say(Loc.T("gtk.noModSelected"), interrupt: true); return; }

		ModUpdateRow row = _updatesView.Rows[i];
		string mods = ModsFolderFor(_game);
		if (string.IsNullOrEmpty(mods)) { Say(Loc.T("gtk.noModsFolder"), interrupt: true); return; }

		SetStatus(Loc.T("gtk.updatesInstalling", row.Name));
		Say(Loc.T("gtk.updatesInstalling", row.Name));

		try
		{
			// Found again by hash rather than remembered from the check, because the file may have changed
			// in between — and installing the build that matched a hash the mod no longer has is how a
			// "successful" update leaves the old version in place.
			string hash = await Task.Run(() => ModrinthService.Sha1Of(row.Path));
			Dictionary<string, ModrinthFile> latest =
				await ModrinthService.GetLatestForHashesAsync(new[] { hash }, GameVersionInUse());

			if (!latest.TryGetValue(hash, out ModrinthFile? file))
			{
				_ui.Post(() => { _sound.Play("error"); Say(Loc.T("gtk.updatesGone", row.Name), interrupt: true); });
				return;
			}

			string downloads = Path.Combine(AppSettings.AppDataFolder, "downloads");
			Directory.CreateDirectory(downloads);
			string downloaded = await ModrinthService.DownloadAsync(file, downloads);
			ModInstaller.InstallResult result = ModInstaller.InstallFile(downloaded, mods);

			_ui.Post(() =>
			{
				LoadInstalled();
				_sound.Play("load_complete");
				SetStatus(Loc.T("gtk.updatesInstalled", row.Name, file.VersionNumber));
				Say(Loc.T("gtk.updatesInstalled", row.Name, file.VersionNumber), interrupt: true);

				// The row is gone now, so the list is rebuilt rather than left showing an update that has
				// just been taken.
				_ = CheckForUpdatesAsync();
			});
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Updates", $"updating {row.Name}", ex);
			_ui.Post(() =>
			{
				_sound.Play("error");
				SetStatus(Loc.T("gtk.updatesFailed", ex.Message));
				Say(Loc.T("gtk.updatesFailed", ex.Message), interrupt: true);
			});
		}
	}
}
