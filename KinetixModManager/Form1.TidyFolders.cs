using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;
using StardewMod = KinetixModManager.GameMod;

namespace KinetixModManager;

/// <summary>
/// Tidying the names of the folders mods are installed into (Mods menu).
///
/// <para>
/// A mod fetched from Nexus arrives in a folder named after the download —
/// <c>Achievements Mods Enabler SE-AE-245-1-41-1715217907</c> — and that name stays on disk for as long as the mod
/// is installed. The manager itself has never been confused by it, because it reads the mod's real name out of the
/// <c>.manager_manifest.json</c> beside it, but anybody who opens the mods folder sees a wall of serial numbers.
/// This renames those folders after the mods in them.
/// </para>
///
/// <para>
/// Nothing about the mods changes. SMAPI, BepInEx and the Witcher's engine all find mods without caring what the
/// folder is called, and a Bethesda mod's deployed files are hard links, which follow the file rather than the
/// path. What <em>does</em> care is the manager's own bookkeeping: mod priority, forced conflict winners, the
/// record of which mod deployed which file, saved profiles and the Witcher's <c>mods.settings</c> all name mods by
/// their folder. Every one of those is rewritten in the same pass — see <see cref="MigrateFolderReferences"/>,
/// which is the part of this worth being careful about.
/// </para>
/// </summary>
public partial class Form1
{
	/// <summary>Entry point (Mods menu): shows what would be renamed, and renames it if the user agrees.</summary>
	private async void TidyModFolderNames()
	{
		if (_settings.ActiveGame == "None")
		{
			Speak(Loc.T("tidy.noGame"));
			return;
		}

		string modsPath = _settings.CurrentModsPath;
		if (!Directory.Exists(modsPath))
		{
			Speak(Loc.T("tidy.noFolder"));
			return;
		}

		GameProfile? profile = GameProfiles.Find(_settings.ActiveGame);
		bool isWitcher = profile?.IsWitcher3 == true;
		string disabledPrefix = profile?.DisabledModPrefix ?? ".";

		// The mod's own name comes from the list the manager has already scanned, which is the same name the user
		// hears — so a tidied folder reads the same in Explorer as it does in the mod list.
		var namesByFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (StardewMod mod in _allInstalledMods.Where(m => !m.IsGroup && m.FolderPath.Length > 0))
			namesByFolder[Path.GetFileName(mod.FolderPath)] = mod.Name;

		var folders = Directory.GetDirectories(modsPath)
			.Select(Path.GetFileName)
			.Where(name => !string.IsNullOrEmpty(name))
			.Select(name => new ModFolderTidy.ModFolder(
				name!, namesByFolder.TryGetValue(name!, out string? display) ? display : ""))
			.ToList();

		var (renames, skips) = ModFolderTidy.Plan(folders, disabledPrefix, isWitcher);

		if (renames.Count == 0)
		{
			Speak(Loc.T("tidy.nothingToDo", GameDisplayName()));
			return;
		}

		if (!ShowTidyPreview(renames, skips)) return;

		// A mod being deployed or scanned while its folder moves out from under it is the one way this can do
		// damage, so the rename runs on its own and the list is rebuilt from disk afterwards.
		var (renamed, failed) = await System.Threading.Tasks.Task.Run(() => ApplyTidy(modsPath, renames));

		if (renamed.Count > 0)
		{
			MigrateFolderReferences(renamed, isWitcher);
			_settings.Save();
			await RefreshModList(checkUpdates: false);
		}

		SpeakBox(failed.Count == 0
			? Loc.T("tidy.done", renamed.Count)
			: Loc.T("tidy.doneWithFailures", renamed.Count, failed.Count, string.Join(", ", failed.Take(3))));
	}

	/// <summary>
	/// Reads the proposed renames out and asks whether to go ahead. Returns true when the user said yes.
	///
	/// The list is shown in full rather than summarised, because the whole point of a preview is that nothing
	/// happens to a folder the user has not heard named.
	/// </summary>
	private bool ShowTidyPreview(List<FolderTidyRename> renames, List<FolderTidySkip> skips)
	{
		var lines = new List<string> { Loc.T("tidy.previewHeader", renames.Count, GameDisplayName()) };
		lines.AddRange(renames.Select((r, i) => Loc.T("tidy.previewRow", i + 1, r.From, r.To)));
		lines.AddRange(skips.Select(s => Loc.T("tidy.previewSkip", s.Folder, s.Reason)));

		ShowTextViewer(Loc.T("tidy.previewTitle"), string.Join(Environment.NewLine, lines));

		return SpeakBox(Loc.T("tidy.confirm", renames.Count), Loc.T("common.confirm"),
			MessageBoxButtons.YesNo) == DialogResult.Yes;
	}

	/// <summary>
	/// Does the renaming, and reports which folders would not move.
	///
	/// A folder that cannot be renamed — open in Explorer, a file inside it locked by the game — is left exactly
	/// as it was and reported. It is never worth failing the other twenty for it, and a half-renamed folder is not
	/// a state this can produce: <see cref="Directory.Move"/> either moves the folder or does not.
	/// </summary>
	private static (List<FolderTidyRename> Renamed, List<string> Failed) ApplyTidy(
		string modsPath, List<FolderTidyRename> renames)
	{
		var renamed = new List<FolderTidyRename>();
		var failed = new List<string>();

		foreach (FolderTidyRename rename in renames)
		{
			string from = Path.Combine(modsPath, rename.From);
			string to = Path.Combine(modsPath, rename.To);
			try
			{
				// Checked again here rather than trusted from the plan: the plan was made before the user was
				// asked, and a mod could have been installed while they were reading it.
				if (!Directory.Exists(from) || Directory.Exists(to))
				{
					failed.Add(rename.From);
					continue;
				}
				Directory.Move(from, to);
				renamed.Add(rename);
			}
			catch (Exception ex)
			{
				DiagnosticLog.WriteException("TidyFolders", $"renaming {rename.From} to {rename.To}", ex);
				failed.Add(rename.From);
			}
		}

		return (renamed, failed);
	}

	/// <summary>
	/// Rewrites everything the manager records against a mod's folder name, for the folders that actually moved.
	///
	/// <para>
	/// This is the half that makes renaming safe, and every store it touches was found by asking what would break:
	/// mod priority decides which mod wins a file conflict, the forced winners are the user's own answers to those
	/// conflicts, the deployment manifest says which mod owns each file the manager put in the game folder, saved
	/// profiles record which mods were on, and the Witcher keeps its own enabled/priority list in
	/// <c>mods.settings</c>. A rename without these would quietly reset a load order.
	/// </para>
	///
	/// <para>
	/// Categories and notes need no migration: they are keyed by the mod's UniqueID, which lives in the manifest
	/// inside the folder and travels with it. The one exception is a mod whose UniqueID <em>is</em> its folder name
	/// — a local mod with nothing else to identify it — which is why those keys are moved too.
	/// </para>
	/// </summary>
	private void MigrateFolderReferences(List<FolderTidyRename> renamed, bool isWitcher)
	{
		var newNameFor = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (FolderTidyRename rename in renamed) newNameFor[rename.From] = rename.To;

		string game = _settings.ActiveGame;
		string Renamed(string name) => newNameFor.TryGetValue(name, out string? now) ? now : name;

		// Which mod wins a file conflict, in order.
		if (_settings.ModPriority.TryGetValue(game, out List<string>? priority))
			_settings.ModPriority[game] = priority.Select(Renamed).ToList();

		// The conflicts the user settled by hand, whose values name the winning mod.
		if (_settings.FileWinnerOverrides.TryGetValue(game, out Dictionary<string, string>? overrides))
			_settings.FileWinnerOverrides[game] = overrides.ToDictionary(
				pair => pair.Key, pair => Renamed(pair.Value), StringComparer.OrdinalIgnoreCase);

		// Which mod owns each file already deployed into the game folder. The files themselves are untouched —
		// they are hard links, which point at the file's contents and not at the folder it was linked from.
		DeploymentManifest manifest = DeploymentManifest.Load(game);
		if (manifest.Deployed.Count > 0)
		{
			manifest.Deployed = manifest.Deployed.ToDictionary(
				pair => pair.Key, pair => Renamed(pair.Value), StringComparer.OrdinalIgnoreCase);
			manifest.Save(game);
		}

		// A local mod identified by nothing but its folder name carries its category and note under that name.
		foreach (FolderTidyRename rename in renamed)
		{
			MoveKey(_settings.ModCategories, rename.From, rename.To);
			MoveKey(_settings.ModNotes, rename.From, rename.To);
			MoveKey(_settings.IgnoredVersions, rename.From, rename.To);
		}

		MigrateProfiles(newNameFor);
		if (isWitcher) MigrateWitcherModSettings(renamed);
	}

	/// <summary>Moves one dictionary entry to a new key, leaving an absent key absent.</summary>
	private static void MoveKey(Dictionary<string, string> map, string from, string to)
	{
		if (!map.TryGetValue(from, out string? value)) return;
		map.Remove(from);
		map[to] = value;
	}

	/// <summary>
	/// Rewrites the folder names inside every saved profile. A profile that names no renamed mod is left alone
	/// rather than rewritten identically, so a game's profiles are not all touched to fix one other game's.
	/// </summary>
	private void MigrateProfiles(Dictionary<string, string> newNameFor)
	{
		if (!Directory.Exists(profilesPath)) return;

		foreach (string path in Directory.GetFiles(profilesPath, "*.json"))
		{
			try
			{
				ModProfile? profile = JsonConvert.DeserializeObject<ModProfile>(File.ReadAllText(path));
				if (profile == null) continue;

				bool touched = false;

				var states = new Dictionary<string, bool>();
				foreach (var pair in profile.ModStates)
				{
					if (newNameFor.TryGetValue(pair.Key, out string? now)) { states[now] = pair.Value; touched = true; }
					else states[pair.Key] = pair.Value;
				}

				List<string>? order = profile.ModPriority;
				if (order != null && order.Any(newNameFor.ContainsKey))
				{
					order = order.Select(n => newNameFor.TryGetValue(n, out string? now) ? now : n).ToList();
					touched = true;
				}

				if (!touched) continue;

				profile.ModStates = states;
				profile.ModPriority = order;
				File.WriteAllText(path, JsonConvert.SerializeObject(profile, Formatting.Indented));
			}
			catch (Exception ex)
			{
				DiagnosticLog.WriteException("TidyFolders", $"updating the profile {Path.GetFileName(path)}", ex);
			}
		}
	}

	/// <summary>
	/// Moves each renamed Witcher 3 mod's entry in <c>mods.settings</c>, which is the game's own record of what is
	/// enabled and in what order — and is keyed by folder name, so a rename without this switches a mod back on.
	/// </summary>
	private void MigrateWitcherModSettings(List<FolderTidyRename> renamed)
	{
		GameProfile? profile = GameProfiles.Find(_settings.ActiveGame);
		string gameFolder = Path.GetDirectoryName(_settings.CurrentModsPath.TrimEnd(Path.DirectorySeparatorChar)) ?? "";
		string settingsPath = profile == null || gameFolder.Length == 0
			? ""
			: Witcher3ModSettings.PathFor(profile.UserDataDirectoryFor(gameFolder));
		if (settingsPath.Length == 0) return;

		try
		{
			var entries = Witcher3ModSettings.Read(settingsPath)
				.ToDictionary(e => e.Name, e => e, StringComparer.OrdinalIgnoreCase);

			foreach (FolderTidyRename rename in renamed)
			{
				string oldKey = Witcher3ModSettings.BareName(rename.From);
				if (!entries.TryGetValue(oldKey, out Witcher3ModSettings.ModEntry? entry)) continue;

				string newKey = Witcher3ModSettings.BareName(rename.To);
				Witcher3ModSettings.SetEnabled(settingsPath, newKey, entry.Enabled);
				if (entry.Priority is int priority) Witcher3ModSettings.SetPriority(settingsPath, newKey, priority);
				Witcher3ModSettings.Remove(settingsPath, oldKey);
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("TidyFolders", "updating the Witcher's mods.settings after renaming", ex);
		}
	}
}
