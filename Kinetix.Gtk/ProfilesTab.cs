using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;

namespace KinetixModManager.GtkHead;

/// <summary>
/// The Profiles screen: saved sets of which mods are switched on.
///
/// <para>
/// Entirely core-backed — <c>ProfileStore</c> and <c>ModProfile</c> moved there in Phase 4, before there was
/// a second front end to use them, so this window had nothing to write. It is the clearest example of why
/// that work was worth doing: the screen is the list, three buttons, and no rules of its own.
/// </para>
///
/// <para>
/// The one thing it insists on is saying what a switch would <em>do</em> before doing it. Applying a profile
/// moves mods in and out of a game the user is about to play, and a list that only says "Mage" and "Thief"
/// asks them to remember which is which. The count comes first, then the confirmation.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
	private readonly Gtk.ListBox _profiles = Gtk.ListBox.New();
	private ProfilesView _profilesView = ProfilesView.Of(Array.Empty<ModProfile>(), Array.Empty<GameMod>());

	/// <summary>Profiles are per game, and kept beside the manager's own data rather than in the game folder.</summary>
	private string ProfilesFolder =>
		Path.Combine(AppSettings.AppDataFolder, "profiles", _game.Id);

	private Gtk.Widget BuildProfilesTab()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 6);

		var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);

		var apply = Gtk.Button.NewWithLabel(Loc.T("gtk.profilesApply"));
		apply.OnClicked += (_, _) => ApplySelectedProfile();
		buttons.Append(apply);

		var save = Gtk.Button.NewWithLabel(Loc.T("gtk.profilesSave"));
		save.OnClicked += (_, _) => SaveCurrentAsProfile();
		buttons.Append(save);

		var delete = Gtk.Button.NewWithLabel(Loc.T("gtk.profilesDelete"));
		delete.OnClicked += (_, _) => DeleteSelectedProfile();
		buttons.Append(delete);

		box.Append(buttons);

		var nameLabel = Gtk.Label.NewWithMnemonic(Loc.T("gtk.profilesNewName"));
		nameLabel.SetXalign(0);
		box.Append(nameLabel);

		_profileName.SetPlaceholderText(Loc.T("gtk.profilesNewNameHint"));
		nameLabel.SetMnemonicWidget(_profileName);
		box.Append(_profileName);

		_profiles.SetVexpand(true);

		var scroller = Gtk.ScrolledWindow.New();
		scroller.SetChild(_profiles);
		scroller.SetVexpand(true);
		box.Append(scroller);

		LoadProfiles();
		return box;
	}

	private readonly Gtk.Entry _profileName = Gtk.Entry.New();

	private void LoadProfiles()
	{
		List<ModProfile> saved = ProfileStore.LoadAll(
			ProfilesFolder,
			(where, ex) => DiagnosticLog.WriteException("Profiles", where, ex));

		_profilesView = ProfilesView.Of(saved, InstalledAsMods());

		while (_profiles.GetFirstChild() is { } child) _profiles.Remove(child);
		foreach (ProfileRow row in _profilesView.Rows) _profiles.Append(RowLabel(row.Spoken));

		SetStatus(_profilesView.Announcement);
	}

	/// <summary>
	/// The installed list as the profile code wants it.
	///
	/// A profile is keyed by each mod's unique id, so the file name is not enough — this is why the rows
	/// carry their path and the scan is asked again rather than reused from the display list.
	/// </summary>
	private List<GameMod> InstalledAsMods()
	{
		if (string.IsNullOrEmpty(_modsFolder) || !Directory.Exists(_modsFolder)) return new List<GameMod>();

		return ModScanner.ScanMods(
			_modsFolder,
			new Newtonsoft.Json.Linq.JObject(),
			ScanContext.For(_modsFolder),
			_game.Id,
			(where, what) => DiagnosticLog.Write(where, what));
	}

	private void ApplySelectedProfile()
	{
		int i = _profiles.GetSelectedRow()?.GetIndex() ?? -1;
		if (i < 0 || i >= _profilesView.Profiles.Count) { Say(Loc.T("gtk.profilesNoneSelected"), interrupt: true); return; }

		ModProfile profile = _profilesView.Profiles[i];
		List<GameMod> installed = InstalledAsMods();

		IReadOnlyList<ProfileStore.StateChange> changes = ProfileStore.Changes(profile, installed);

		// Said before anything moves, and said as a count rather than as a list — the user is choosing
		// whether to go ahead, not auditing it.
		Say(ProfilesView.DescribeApplying(profile, installed), interrupt: true);
		if (changes.Count == 0) return;

		int done = 0;
		foreach (ProfileStore.StateChange change in changes)
		{
			try
			{
				// Each game's own rule, from the core — a leading dot for Stardew, a suffix for Minecraft.
				string target = ModEnableState.TargetPath(change.Mod.FolderPath, change.Enable, _game.Id);
				if (!string.Equals(target, change.Mod.FolderPath, StringComparison.Ordinal))
				{
					if (Directory.Exists(change.Mod.FolderPath)) Directory.Move(change.Mod.FolderPath, target);
					else File.Move(change.Mod.FolderPath, target);
				}
				done++;
			}
			catch (Exception ex)
			{
				// One mod that will not move is not a reason to abandon the rest half-applied.
				DiagnosticLog.WriteException("Profiles", $"switching {change.Mod.FolderPath}", ex);
			}
		}

		LoadInstalled();
		LoadProfiles();

		_sound.Play(done == changes.Count ? "load_complete" : "error");
		Say(done == changes.Count
			? Loc.T("gtk.profilesApplied", profile.Name, done)
			: Loc.T("gtk.profilesAppliedPartly", profile.Name, done, changes.Count - done), interrupt: true);
	}

	private void SaveCurrentAsProfile()
	{
		string name = _profileName.GetBuffer().GetText().Trim();
		if (name.Length == 0) { Say(Loc.T("gtk.profilesNameNeeded"), interrupt: true); return; }

		try
		{
			// Capture and the name sanitising both come from the core, which is where the bug was found that
			// let a profile called "Mage/Thief" save under one name and refuse to delete under another.
			ModProfile profile = ProfileStore.Capture(name, InstalledAsMods(), null, null, null);
			ProfileStore.Save(ProfilesFolder, profile);

			_profileName.GetBuffer().SetText("", 0);
			LoadProfiles();

			_sound.Play("load_complete");
			Say(Loc.T("gtk.profilesSaved", name), interrupt: true);
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Profiles", $"saving {name}", ex);
			_sound.Play("error");
			Say(Loc.T("gtk.profilesSaveFailed", name, ex.Message), interrupt: true);
		}
	}

	private void DeleteSelectedProfile()
	{
		int i = _profiles.GetSelectedRow()?.GetIndex() ?? -1;
		if (i < 0 || i >= _profilesView.Profiles.Count) { Say(Loc.T("gtk.profilesNoneSelected"), interrupt: true); return; }

		ModProfile profile = _profilesView.Profiles[i];

		// Deleting a profile does not touch a single mod, which is worth saying: it is the one action here
		// that sounds more destructive than it is.
		bool deleted = ProfileStore.Delete(ProfilesFolder, profile.Name);
		LoadProfiles();

		_sound.Play(deleted ? "disable" : "error");
		Say(deleted
			? Loc.T("gtk.profilesDeleted", profile.Name)
			: Loc.T("gtk.profilesDeleteFailed", profile.Name), interrupt: true);
	}
}
