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
	/// <param name="interrupt">
	/// False when this follows a sentence that has to be heard out. Straight after "X updated to Y", the answer to this check
	/// cut it off, and the update was the news.
	/// </param>
	private async Task CheckForUpdatesAsync(bool interrupt = true)
	{
		// Nothing to ask about. Asked anyway, this came back as "Everything is up to date, 0 mods were checked" with the
		// completed cue, even for a game that is not installed. The list's own sentence says which of those it is.
		if (_view.Rows.Count == 0)
		{
			_updatesView = ModUpdatesView.CannotCheck(_game, _view.Announcement);
			ShowUpdates(interrupt);
			return;
		}

		// Two ways of asking, because the catalogues answer different questions. Modrinth is asked what the
		// newest build of THIS FILE is, by hash, so nothing has to have been linked first. Nexus is asked
		// about a mod id, so only a mod carrying one can be checked — which is why the two are not one call.
		if (!_game.IsMinecraft)
		{
			await CheckNexusUpdatesAsync();
			return;
		}

		string version = GameVersionInUse();
		if (version.Length == 0)
		{
			_updatesView = ModUpdatesView.CannotCheck(_game, Loc.T("gtk.noGameVersion"));
			ShowUpdates(interrupt);
			return;
		}

		SetStatus(Loc.T("gtk.updatesChecking"));
		Say(Loc.T("gtk.updatesChecking"));

		// A check is not an operation that holds the game in place, so the user may switch while it runs. Its answer is
		// about this game, and is dropped if that is no longer the one on screen.
		GameProfile game = _game;

		try
		{
			List<GameMod> installed = _view.Rows
				.Select(r => new GameMod { FolderPath = r.Path, Name = r.Name, Version = r.Version })
				.ToList();

			// Hashing is disk work and there may be a hundred of them, so it happens off the interface's
			// thread along with the request.
			int unreadable = 0;
			var byHash = await Task.Run(() =>
			{
				var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (GameMod mod in installed)
				{
					try { hashes[ModrinthService.Sha1Of(mod.FolderPath)] = mod.FolderPath; }
					catch (Exception ex)
					{
						// A file that cannot be read is not an update and not a reason to abandon the check. It has
						// not been checked either, and is counted so that nothing claims it was.
						DiagnosticLog.WriteException("Updates", $"hashing {mod.FolderPath}", ex);
						unreadable++;
					}
				}
				return hashes;
			});

			// Null is Modrinth not answering. Empty is Modrinth not hosting any of these, which is a real answer. The call
			// that hides the difference reported a Modrinth outage as everything being up to date.
			Dictionary<string, ModrinthFile>? latest =
				await ModrinthService.GetLatestForHashesOrNullAsync(byHash.Keys.ToList(), version);

			if (_game.Id != game.Id) return;

			if (latest == null)
			{
				_updatesView = ModUpdatesView.Of(game, installed, new Dictionary<string, string>(), unanswered: installed.Count);
				_ui.Post(() => ShowUpdates(interrupt));
				return;
			}

			var latestByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var (hash, file) in latest)
				if (byHash.TryGetValue(hash, out string? path)) latestByPath[path] = file.VersionNumber;

			_updatesView = ModUpdatesView.Of(game, installed, latestByPath, unanswered: unreadable);
			_ui.Post(() => ShowUpdates(interrupt));
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Updates", "checking for updates", ex);
			if (_game.Id != game.Id) return;

			_updatesView = ModUpdatesView.CannotCheck(game, Loc.T("gtk.updatesFailed", ex.Message));
			_ui.Post(() => { _sound.Play("error"); ShowUpdates(interrupt); });
		}
	}

	private void ShowUpdates(bool interrupt = true)
	{
		RefillRows(_updates, _updatesView.Rows.Select(row => row.Spoken));

		SetStatus(_updatesView.Announcement);

		// The cue only for good news. An empty list because nothing could be checked is not a completed
		// operation, and sounding as though it were would be the wrong fact. Nor is a check some of which went unanswered.
		if (_updatesView.IsComplete) _sound.Play("load_complete");
		else if (_updatesView.Unanswered > 0) _sound.Play("error");

		Say(_updatesView.Announcement, interrupt);
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

		if (RefusedWhileChanging()) return;

		ModUpdateRow row = _updatesView.Rows[i];

		// Only a jar from Modrinth can be fetched here. A Nexus row is a folder, which cannot be hashed, and the failure
		// that gave was heard as the update check having failed, with the path read out.
		if (!_game.IsMinecraft)
		{
			Say(Loc.T("gtk.updatesNotFromHere", row.Name), interrupt: true);
			return;
		}

		string mods = ModsFolderFor(_game);
		if (string.IsNullOrEmpty(mods)) { Say(Loc.T("gtk.noModsFolder"), interrupt: true); return; }

		using IDisposable busy = _modOperations.Begin();

		SetStatus(Loc.T("gtk.updatesInstalling", row.Name));
		Say(Loc.T("gtk.updatesInstalling", row.Name));

		string backups = BackupsFolder;
		int keep = _settings.MaxBackupsPerMod;

		try
		{
			// Found again by hash rather than remembered from the check, because the file may have changed
			// in between — and installing the build that matched a hash the mod no longer has is how a
			// "successful" update leaves the old version in place.
			string hash = await Task.Run(() => ModrinthService.Sha1Of(row.Path));
			Dictionary<string, ModrinthFile>? latest =
				await ModrinthService.GetLatestForHashesOrNullAsync(new[] { hash }, GameVersionInUse());

			if (latest == null)
			{
				// Not "no longer has an update waiting", which is a claim about the mod that nothing has learned.
				_ui.Post(() =>
				{
					_sound.Play("error");
					SetStatus(Loc.T("gtk.updatesNoAnswerForOne", row.Name));
					Say(Loc.T("gtk.updatesNoAnswerForOne", row.Name), interrupt: true);
				});
				return;
			}

			if (!latest.TryGetValue(hash, out ModrinthFile? file))
			{
				_ui.Post(() => { _sound.Play("error"); Say(Loc.T("gtk.updatesGone", row.Name), interrupt: true); });
				return;
			}

			string downloads = UpdateDownloadsFolder;
			Directory.CreateDirectory(downloads);
			string downloaded = await ModrinthService.DownloadAsync(file, downloads);

			// The copy being replaced is kept first, and a failed backup throws and stops the update. Otherwise a bad
			// update could not be undone from this head.
			ModInstaller.InstallResult result = await Task.Run(() =>
			{
				ModInstaller.BackUpCopiesBeingReplaced(downloaded, mods, backups, keep);
				return ModInstaller.InstallFile(downloaded, mods);
			});

			_ui.Post(() =>
			{
				LoadInstalled();
				LoadBackups(announce: false);

				// Not "updated": an older copy would not go, and Fabric will not start with two. The new copy was taken back
				// out, unless it wrote over a file of the same name, when it stays beside the stuck one. So the sentence names
				// what is stuck and says nothing about where the new one is.
				if (result.CouldNotRemove.Count > 0)
				{
					string stuck = Loc.T("mc.install.oldCopyStuck", string.Join(", ", result.CouldNotRemove));
					_sound.Play("error");
					SetStatus(stuck);
					Say(stuck, interrupt: true);
					SayIfListFailed();
					return;
				}

				_sound.Play("load_complete");
				SetStatus(Loc.T("gtk.updatesInstalled", row.Name, file.VersionNumber));
				Say(Loc.T("gtk.updatesInstalled", row.Name, file.VersionNumber), interrupt: true);
				SayIfListFailed();

				// The row is gone now, so the list is rebuilt rather than left showing an update that has
				// just been taken.
				_ = CheckForUpdatesAsync(interrupt: false);
			});
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Updates", $"updating {row.Name}", ex);
			_ui.Post(() =>
			{
				_sound.Play("error");
				SetStatus(Loc.T("gtk.updateInstallFailed", row.Name, ex.Message));
				Say(Loc.T("gtk.updateInstallFailed", row.Name, ex.Message), interrupt: true);
			});
		}
	}
}

/// <summary>
/// The Nexus half of the updates check, split out because the question it asks is a different one.
/// </summary>
public sealed partial class MainWindow
{
	private async Task CheckNexusUpdatesAsync()
	{
		if (string.IsNullOrWhiteSpace(_settings.ApiKey))
		{
			// The one thing a user can actually do about it, said plainly, rather than an empty list.
			_updatesView = ModUpdatesView.CannotCheck(_game, Loc.T("gtk.updatesNeedKey", _game.DisplayName));
			ShowUpdates();
			return;
		}

		SetStatus(Loc.T("gtk.updatesChecking"));
		Say(Loc.T("gtk.updatesChecking"));

		// Read now, while this is the game the check is for. Asked later, the rest of the list would go to the new
		// game's page after a switch, with this game's mod ids.
		GameProfile game = _game;
		string domain = _nexus.CurrentGameDomain;

		try
		{
			List<GameMod> installed = InstalledAsMods();

			// The folder was emptied after the list was last read. Nothing was asked, so nothing can be up to date.
			if (installed.Count == 0)
			{
				_updatesView = ModUpdatesView.CannotCheck(game, Loc.T("installed.noneYet", game.DisplayName));
				_ui.Post(() => ShowUpdates());
				return;
			}

			// Only a mod with an id can be asked about. Counting the rest as checked said "40 mods were checked" of a
			// folder where three had been.
			List<GameMod> linked = installed.Where(m => !string.IsNullOrEmpty(m.NexusID)).ToList();

			if (linked.Count == 0)
			{
				_updatesView = ModUpdatesView.CannotCheck(game, Loc.T("gtk.updatesNoneLinked"));
				_ui.Post(() => ShowUpdates());
				return;
			}

			var latestByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			var goneFromNexus = new List<string>();
			int unanswered = 0;

			// Set once the key has been shown to work, so that one hidden mod after another does not cost a request each.
			bool keyWorks = false;

			foreach (GameMod mod in linked)
			{
				// One at a time, and that is what NexusService's own rate limiting expects — it holds a
				// semaphore across these calls precisely so a folder of two hundred mods does not spend the
				// user's hourly quota in one go.
				NexusLatestVersion latest = await _nexus.AskLatestVersionAsync(mod.NexusID!, domain);

				// The user has moved to another game. The rest of this list is no use to them, and costs requests.
				if (_game.Id != game.Id) return;

				NexusVersionAnswer answer = latest.Answer;

				// A 403 may be this one mod, hidden by its author, or may be the key. Only asking about the key can tell, and
				// telling someone their good key was refused because of one mod would send them off to fix the wrong thing.
				if (answer == NexusVersionAnswer.Forbidden)
				{
					NexusConnection connection = keyWorks ? NexusConnection.Connected : await _nexus.CheckConnectionAsync();
					if (_game.Id != game.Id) return;

					keyWorks = connection == NexusConnection.Connected;
					answer = connection switch
					{
						NexusConnection.Connected => NexusVersionAnswer.ModGone,
						NexusConnection.KeyRejected => NexusVersionAnswer.KeyRefused,
						_ => NexusVersionAnswer.NoAnswer,
					};
				}

				switch (answer)
				{
					case NexusVersionAnswer.KeyRefused:
						// Every other mod would get the same answer, and asking on would only spend requests to learn it.
						_updatesView = ModUpdatesView.CannotCheck(game, Loc.T("gtk.updatesKeyRefused"));
						_ui.Post(() => { _sound.Play("error"); ShowUpdates(); });
						return;

					case NexusVersionAnswer.ModGone:
						goneFromNexus.Add(mod.Name);
						break;

					case NexusVersionAnswer.Answered when !string.IsNullOrWhiteSpace(latest.Version):
						latestByPath[mod.FolderPath] = latest.Version!;
						break;

					// An outage or a rate limit, not a mod with nothing newer. Anything else reports a check that learned
					// nothing as everything being up to date.
					default:
						unanswered++;
						break;
				}
			}

			_updatesView = ModUpdatesView.Of(game, linked, latestByPath, unanswered,
				toldToSlowDown: _nexus.Pause.IsActive(DateTime.UtcNow),
				notLinked: installed.Count - linked.Count,
				goneFromSite: goneFromNexus);
			_ui.Post(() => ShowUpdates());
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Updates", "asking Nexus for updates", ex);
			if (_game.Id != game.Id) return;

			_updatesView = ModUpdatesView.CannotCheck(game, Loc.T("gtk.updatesFailed", ex.Message));
			_ui.Post(() => { _sound.Play("error"); ShowUpdates(); });
		}
	}
}
