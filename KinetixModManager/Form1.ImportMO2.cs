using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// One-way importer that brings a user's Mod Organizer 2 (MO2) setup into Kinetix Mod Manager, to ease the
/// transition for Skyrim SE / Fallout 4 users. MO2 deploys mods through a virtual file system and stores its
/// state as plain files, so we can read it without MO2 running: each mod is a folder under <c>mods/</c>, the
/// per-profile <c>modlist.txt</c> holds the conflict order and enabled state, and <c>loadorder.txt</c> /
/// <c>plugins.txt</c> hold the plugin load order and active set. We copy the mod folders into our own mods
/// directory (never moving or altering the MO2 setup) and translate the orders into our model. Vortex, which
/// uses a different deployment model and a binary state database, is intentionally not supported here.
/// </summary>
public partial class Form1
{
	/// <summary>A regular MO2 mod read from modlist.txt: its folder name and whether it is enabled.</summary>
	private readonly record struct Mo2Mod(string Name, bool Enabled);

	/// <summary>
	/// Menu entry point. Walks the user through picking their MO2 folder and profile, then copies the mods and
	/// applies the order. Skyrim SE / Fallout 4 only (the games whose load order this manager models).
	/// </summary>
	private async void ImportFromMO2()
	{
		if (!IsBethesdaGame)
		{
			Speak(Loc.T("mo2.notApplicable"));
			return;
		}
		string game = _settings.ActiveGame;
		if (string.IsNullOrEmpty(_settings.CurrentModsPath))
		{
			Speak(Loc.T("mo2.needModsPath"));
			MessageBox.Show(Loc.T("mo2.needModsPath"));
			return;
		}

		// 1. Locate the MO2 folder (the one containing "mods" and "profiles").
		string baseDir;
		using (var dlg = new FolderBrowserDialog { Description = Loc.T("mo2.pickFolder"), UseDescriptionForTitle = true })
		{
			if (dlg.ShowDialog() != DialogResult.OK) { Speak(Loc.T("common.changesCancelled")); return; }
			baseDir = dlg.SelectedPath;
		}

		var ini = Mo2ReadIni(Path.Combine(baseDir, "ModOrganizer.ini"));
		string modsDir = Mo2ResolveDir(ini, "Settings/mod_directory", baseDir, "mods");
		string profilesDir = Mo2ResolveDir(ini, "Settings/profiles_directory", baseDir, "profiles");
		if (!Directory.Exists(modsDir) || !Directory.Exists(profilesDir))
		{
			Speak(Loc.T("mo2.notFound"));
			MessageBox.Show(Loc.T("mo2.notFound"));
			return;
		}

		// Soft game check: refuse only if the instance is confidently for a different supported game.
		string? iniGame = Mo2GameToInternal(Mo2GetValue(ini, "General/gameName"));
		if (iniGame != null && !string.Equals(iniGame, game, StringComparison.OrdinalIgnoreCase))
		{
			Speak(Loc.T("mo2.gameMismatch", GameDisplayName(iniGame), GameDisplayName(game)));
			MessageBox.Show(Loc.T("mo2.gameMismatch", GameDisplayName(iniGame), GameDisplayName(game)));
			return;
		}

		// 2. Let the user pick which profile to import (defaulting to MO2's currently-selected one).
		List<string> profiles = Mo2ListProfiles(profilesDir);
		if (profiles.Count == 0)
		{
			Speak(Loc.T("mo2.noProfiles"));
			MessageBox.Show(Loc.T("mo2.noProfiles"));
			return;
		}
		string? profileDir = Mo2ChooseProfile(profiles, Mo2GetValue(ini, "General/selected_profile"));
		if (profileDir == null) { Speak(Loc.T("common.changesCancelled")); return; }
		string profileName = Path.GetFileName(profileDir.TrimEnd(Path.DirectorySeparatorChar));

		// 3. Read the mod list (highest priority first; separators, foreign DLC, and comments excluded).
		List<Mo2Mod> mods = Mo2ReadModlist(Path.Combine(profileDir, "modlist.txt"));
		if (mods.Count == 0)
		{
			Speak(Loc.T("mo2.noMods"));
			MessageBox.Show(Loc.T("mo2.noMods"));
			return;
		}

		int enabledCount = mods.Count(m => m.Enabled);
		if (MessageBox.Show(Loc.T("mo2.confirm", profileName, mods.Count, enabledCount), Loc.T("mo2.title"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			Speak(Loc.T("common.changesCancelled"));
			return;
		}

		// 4. Copy the mod folders into our mods directory (background; never touches the MO2 setup).
		Speak(Loc.T("mo2.importing"));
		string destRoot = _settings.CurrentModsPath;
		int copied = 0, skipped = 0, missing = 0;
		try
		{
			await Task.Run(() =>
			{
				for (int i = 0; i < mods.Count; i++)
				{
					Mo2Mod m = mods[i];
					string src = Path.Combine(modsDir, m.Name);
					if (!Directory.Exists(src)) { missing++; continue; }

					// Skip if either the enabled or disabled (dot-prefixed) variant already exists here, so a
					// re-run does not clobber or duplicate mods the user already has in the manager.
					if (Directory.Exists(Path.Combine(destRoot, m.Name)) ||
						Directory.Exists(Path.Combine(destRoot, "." + m.Name)))
					{ skipped++; continue; }

					string dest = Path.Combine(destRoot, m.Enabled ? m.Name : "." + m.Name);
					SetStatus(Loc.T("mo2.copying", i + 1, mods.Count, m.Name), speak: false);
					Mo2CopyModFolder(src, dest);
					copied++;
				}
			});
		}
		catch (Exception ex)
		{
			_soundEngine.Play("error");
			SetStatus(Loc.T("mo2.error", ex.Message));
			MessageBox.Show(Loc.T("mo2.error", ex.Message));
			return;
		}

		// 5. Apply the conflict order (modlist order = highest priority first, matching our model) and the
		//    plugin load order. Seed the game's plugins.txt with the active set first so SyncBethesdaPlugins
		//    adopts external entries such as Creations (which live in Data, not in any mod folder).
		_settings.ModPriority[game] = mods.Select(m => m.Name).ToList();
		List<string> activePlugins = Mo2ReadActivePluginOrder(profileDir, game);
		_settings.PluginOrder[game] = new List<string>(activePlugins);
		ModFileSystem.WritePluginsTxt(game, activePlugins, LogError);
		_settings.Save();

		_soundEngine.Play("connect");
		Speak(Loc.T("mo2.done", copied, enabledCount, mods.Count - enabledCount, activePlugins.Count, skipped, missing));

		// 6. Rescan and deploy the imported mods for real and reconcile plugins. Awaited so
		// _allInstalledMods is populated before the auto-match step below.
		await RefreshModList(checkUpdates: false);
		RefreshBackupsList();
		RefreshProfilesList();

		// 7. MO2 names its mod folders by display name without the Nexus ID, so imported mods arrive
		// without one — which would block update checks and "open mod page". Auto-match fills in the
		// IDs by searching Nexus for each mod's name (it no-ops if everything already has an ID).
		await AutoMatchNexusIDs();
	}

	// -------------------------------------------------------------------------
	// MO2 file parsing
	// -------------------------------------------------------------------------

	/// <summary>
	/// Reads modlist.txt into the regular mods it lists, in file order (which MO2 stores highest priority
	/// first — the first line wins conflicts, matching our <see cref="AppSettings.ModPriority"/> convention).
	/// Comment lines (<c>#</c>), separators (names ending <c>_separator</c>), and foreign/DLC entries
	/// (<c>*</c>, which have no mod folder) are skipped.
	/// </summary>
	private static List<Mo2Mod> Mo2ReadModlist(string modlistPath)
	{
		var result = new List<Mo2Mod>();
		if (!File.Exists(modlistPath)) return result;
		foreach (string raw in File.ReadAllLines(modlistPath))
		{
			string line = raw.Trim();
			if (line.Length < 2 || line[0] == '#') continue;
			char prefix = line[0];
			if (prefix != '+' && prefix != '-') continue; // '*' = foreign DLC/Creation: no folder to copy
			string name = line.Substring(1).Trim();
			if (name.Length == 0 || name.EndsWith("_separator", StringComparison.OrdinalIgnoreCase)) continue;
			result.Add(new Mo2Mod(name, prefix == '+'));
		}
		return result;
	}

	/// <summary>
	/// Builds the active plugin load order from an MO2 profile: the load order from <c>loadorder.txt</c>
	/// (falling back to <c>plugins.txt</c> order), filtered to the active set from <c>plugins.txt</c> and
	/// with the implicit base-game/DLC masters removed — the same shape as <see cref="AppSettings.PluginOrder"/>.
	/// </summary>
	private List<string> Mo2ReadActivePluginOrder(string profileDir, string game)
	{
		var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string pluginsPath = Path.Combine(profileDir, "plugins.txt");
		var pluginsLines = File.Exists(pluginsPath)
			? File.ReadAllLines(pluginsPath).Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#').ToList()
			: new List<string>();
		// Asterisk form: "*name" is active, bare "name" is present-but-inactive. Older form lists only
		// active plugins with no asterisks, so when none are present every listed plugin counts as active.
		bool asteriskForm = pluginsLines.Any(l => l.StartsWith("*"));
		foreach (string l in pluginsLines)
		{
			if (asteriskForm) { if (l.StartsWith("*")) active.Add(l.Substring(1).Trim()); }
			else active.Add(l);
		}

		// Order: prefer loadorder.txt (all plugins, load order); else the plugins.txt order.
		string loadorderPath = Path.Combine(profileDir, "loadorder.txt");
		List<string> order = File.Exists(loadorderPath)
			? File.ReadAllLines(loadorderPath).Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#').ToList()
			: pluginsLines.Select(l => l.StartsWith("*") ? l.Substring(1).Trim() : l).ToList();

		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var ordered = new List<string>();
		foreach (string name in order)
		{
			if (!active.Contains(name)) continue;
			if (ModFileSystem.IsBaseMaster(game, name)) continue;
			if (seen.Add(name)) ordered.Add(name);
		}
		return ordered;
	}

	/// <summary>Copies a mod folder, skipping MO2 housekeeping files (meta.ini and *.mohidden).</summary>
	private static void Mo2CopyModFolder(string src, string dest)
	{
		string canonical = Path.GetFullPath(src).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
		{
			string name = Path.GetFileName(file);
			if (name.Equals("meta.ini", StringComparison.OrdinalIgnoreCase)) continue;
			if (name.EndsWith(".mohidden", StringComparison.OrdinalIgnoreCase)) continue;

			string rel = Path.GetFullPath(file).Substring(canonical.Length);
			string destFile = Path.Combine(dest, rel);
			string? destDir = Path.GetDirectoryName(destFile);
			if (destDir != null && !Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
			File.Copy(file, destFile, overwrite: true);
		}
	}

	// -------------------------------------------------------------------------
	// ModOrganizer.ini helpers
	// -------------------------------------------------------------------------

	/// <summary>Reads a flat "Section/key" -> value map from an INI file. Returns empty if absent/unreadable.</summary>
	private static Dictionary<string, string> Mo2ReadIni(string path)
	{
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!File.Exists(path)) return map;
		try
		{
			string section = "";
			foreach (string raw in File.ReadAllLines(path))
			{
				string line = raw.Trim();
				if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
				if (line[0] == '[' && line[^1] == ']') { section = line.Substring(1, line.Length - 2).Trim(); continue; }
				int eq = line.IndexOf('=');
				if (eq <= 0) continue;
				string key = line.Substring(0, eq).Trim();
				string value = line.Substring(eq + 1).Trim();
				map[$"{section}/{key}"] = value;
			}
		}
		catch { /* unreadable ini: callers fall back to defaults */ }
		return map;
	}

	/// <summary>Gets a raw INI value (unwrapping MO2's <c>@ByteArray(...)</c> encoding), or null if absent.</summary>
	private static string? Mo2GetValue(Dictionary<string, string> ini, string key)
	{
		if (!ini.TryGetValue(key, out string? v) || string.IsNullOrEmpty(v)) return null;
		const string token = "@ByteArray(";
		if (v.StartsWith(token, StringComparison.OrdinalIgnoreCase) && v.EndsWith(")"))
			v = v.Substring(token.Length, v.Length - token.Length - 1);
		return v.Trim();
	}

	/// <summary>
	/// Resolves a directory from the INI (honouring an explicit override and MO2's <c>%BASE_DIR%</c> token),
	/// falling back to <paramref name="defaultSub"/> under <paramref name="baseDir"/>.
	/// </summary>
	private static string Mo2ResolveDir(Dictionary<string, string> ini, string key, string baseDir, string defaultSub)
	{
		string? v = Mo2GetValue(ini, key);
		if (string.IsNullOrEmpty(v)) return Path.Combine(baseDir, defaultSub);
		v = v.Replace("%BASE_DIR%", baseDir).Replace('/', Path.DirectorySeparatorChar);
		return Path.IsPathRooted(v) ? v : Path.Combine(baseDir, v);
	}

	/// <summary>Returns the MO2 profile folders that contain a modlist.txt, sorted by name.</summary>
	private static List<string> Mo2ListProfiles(string profilesDir) =>
		Directory.GetDirectories(profilesDir)
			.Where(d => File.Exists(Path.Combine(d, "modlist.txt")))
			.OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
			.ToList();

	/// <summary>One row of the profile chooser: a profile and its mod counts, read aloud on focus.</summary>
	private sealed class Mo2ProfileEntry
	{
		public string Dir = "";
		public string Summary = "";
		public override string ToString() => Summary;
	}

	/// <summary>
	/// Shows an accessible dialog listing the MO2 profiles (with mod and enabled counts) and returns the chosen
	/// profile folder, or null if cancelled. Selection defaults to <paramref name="defaultName"/> (MO2's
	/// currently-selected profile) so the common case is one keypress.
	/// </summary>
	private string? Mo2ChooseProfile(List<string> profiles, string? defaultName)
	{
		var entries = profiles.Select(d =>
		{
			var mods = Mo2ReadModlist(Path.Combine(d, "modlist.txt"));
			return new Mo2ProfileEntry
			{
				Dir = d,
				Summary = Loc.T("mo2.profileItem", Path.GetFileName(d), mods.Count, mods.Count(m => m.Enabled))
			};
		}).ToList();

		using var dlg = new Form
		{
			Text = Loc.T("mo2.chooseProfileTitle"),
			Size = new Size(480, 420),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true,
			MinimizeBox = false,
			MaximizeBox = false
		};
		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(12) };
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));

		layout.Controls.Add(new Label { Text = Loc.T("mo2.chooseProfilePrompt"), AutoSize = true }, 0, 0);
		var lb = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			AccessibleName = Loc.T("mo2.profileList")
		};
		foreach (var e in entries) lb.Items.Add(e);
		// Default to MO2's selected profile, else the first entry.
		int defaultIdx = 0;
		if (!string.IsNullOrEmpty(defaultName))
		{
			int found = entries.FindIndex(e =>
				string.Equals(Path.GetFileName(e.Dir), defaultName, StringComparison.OrdinalIgnoreCase));
			if (found >= 0) defaultIdx = found;
		}
		lb.SelectedIndex = defaultIdx;
		lb.SelectedIndexChanged += async delegate
		{
			if (lb.SelectedItem == null) return;
			await Task.Delay(100);
			if (!lb.Focused) return;
			Speak(Loc.T("common.position", lb.SelectedIndex + 1, lb.Items.Count));
		};
		lb.KeyDown += delegate (object? s, KeyEventArgs ke)
		{
			if (ke.KeyCode == Keys.Return && lb.SelectedItem != null)
			{
				dlg.DialogResult = DialogResult.OK;
				dlg.Close();
			}
		};
		layout.Controls.Add(lb, 0, 1);

		var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
		var btnOk = new Button { Text = Loc.T("common.confirm"), Width = 110, Height = 34, DialogResult = DialogResult.OK };
		var btnCancel = new Button { Text = Loc.T("common.cancel"), Width = 100, Height = 34, DialogResult = DialogResult.Cancel };
		buttons.Controls.Add(btnOk);
		buttons.Controls.Add(btnCancel);
		layout.Controls.Add(buttons, 0, 2);

		dlg.Controls.Add(layout);
		dlg.AcceptButton = btnOk;
		dlg.CancelButton = btnCancel;
		dlg.Shown += delegate { lb.Focus(); };

		return dlg.ShowDialog() == DialogResult.OK && lb.SelectedItem is Mo2ProfileEntry chosen ? chosen.Dir : null;
	}

	/// <summary>Maps an MO2 game name to our internal game id, or null for games this manager does not model.</summary>
	private static string? Mo2GameToInternal(string? mo2GameName) => mo2GameName?.Trim() switch
	{
		"Fallout 4" => "Fallout4",
		"Skyrim Special Edition" => "SkyrimSE",
		_ => null
	};
}
