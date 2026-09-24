using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// Choosing which Minecraft to work on — the player's own, or one of their modpacks — and the Minecraft Packs tab.
///
/// <para>
/// A pack is loaded as a session of its own, under its own install key (see
/// <see cref="MinecraftModpacks.InstallKeyPrefix"/>), so everything the manager already does for a game — the mod
/// list, switching mods off, backups, updates, logs, Ctrl+H — works on a pack unchanged and never reaches into the
/// player's own mods. What a pack is NOT is a game: it is never in the games list. Opening Minecraft asks which
/// Minecraft, and the Minecraft Packs tab lists them all. That was Sean's call, so that nobody mistakes a modpack
/// for a separate game.
/// </para>
/// </summary>
public partial class Form1
{
	private TabPage tabMinecraftPacks = null!;
	private ListBox listMinecraftPacks = null!;

	/// <summary>The search tab's "Search for" choice — mods or modpacks. Minecraft only; see ShowMinecraftPacksTabFor.</summary>
	private ComboBox? cmbDiscoveryContent;
	private Label? _lblDiscoveryContent;

	/// <summary>The pack last read for the loaded session, and the key it was read for.</summary>
	private (string Key, MinecraftPack? Pack) _activePackCache;

	/// <summary>Newer versions found for installed packs, by pack folder. Filled by the update check.</summary>
	private readonly Dictionary<string, ModpackVersion> _packUpdates = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>One row of the Minecraft Packs tab: the player's own Minecraft (no pack), or a pack.</summary>
	private sealed class MinecraftSetupRow
	{
		public MinecraftPack? Pack { get; init; }
		public string Text { get; init; } = "";
		public override string ToString() => Text;
	}

	/// <summary>
	/// The modpack loaded in this session, or <c>null</c> when the session is the player's own Minecraft (or not
	/// Minecraft at all). Read from the pack's folder, and kept until the session changes.
	/// </summary>
	private MinecraftPack? ActiveMinecraftPack()
	{
		string key = _settings.ActiveGame;
		if (!MinecraftModpacks.IsPackKey(key)) return null;
		if (_activePackCache.Key == key && _activePackCache.Pack != null) return _activePackCache.Pack;

		MinecraftPack? pack = MinecraftModpacks.ForKey(key, MinecraftModpacks.PacksFolder);
		_activePackCache = (key, pack);
		return pack;
	}

	/// <summary>
	/// Whether a mod in this session came with the loaded modpack, rather than being one the player added. Always
	/// false outside a pack's session.
	///
	/// What the update warnings turn on: a pack's mods are a set its maker tested together, and updating them one by
	/// one quietly takes the pack away from that — which, in a game heard rather than seen, tends to show up as
	/// something going quiet rather than as an error.
	/// </summary>
	private bool CameWithPack(GameMod mod)
	{
		if (ActiveMinecraftPack() is not { } pack || string.IsNullOrEmpty(mod.FolderPath)) return false;

		string name = Path.GetFileName(mod.FolderPath);
		string suffix = MinecraftDisabledSuffix;
		if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - suffix.Length);

		return pack.ProvidedFiles.Contains("mods/" + name, StringComparer.OrdinalIgnoreCase);
	}

	/// <summary>The install key of the player's own Minecraft — its first, bare-keyed copy.</summary>
	private string OwnMinecraftKey() =>
		_settings.InstallsOf(GameProfiles.Minecraft).FirstOrDefault()?.Key ?? GameProfiles.Minecraft;

	/// <summary>The Minecraft version the player's own setup is on, whichever session is loaded.</summary>
	private string OwnMinecraftVersion() =>
		_settings.MinecraftGameVersion.Length > 0
			? _settings.MinecraftGameVersion
			: FabricInstaller.DetectInstalledGameVersion(MinecraftRootFolder());

	// -------------------------------------------------------------------------
	// Opening a session
	// -------------------------------------------------------------------------

	/// <summary>
	/// Opens a game's session — asking first, for Minecraft, which Minecraft: the player's own or one of their
	/// modpacks. With no packs installed there is nothing to ask, and Minecraft opens exactly as it always did.
	/// </summary>
	private void OpenGameSession(string key)
	{
		if (GameProfiles.IsGame(key, GameProfiles.Minecraft) && !MinecraftModpacks.IsPackKey(key))
		{
			IReadOnlyList<MinecraftPack> packs = MinecraftModpacks.FindInstalled(MinecraftModpacks.PacksFolder);
			if (packs.Count > 0)
			{
				string? chosen = ChooseMinecraftSetup(packs);
				if (chosen != null) SwitchToMinecraftSetup(chosen);
				return;
			}
		}

		SwitchActiveGame(key);
	}

	/// <summary>
	/// Asks which Minecraft to open. Returns its install key, or <c>null</c> when the player backed out — in which
	/// case the session they were in is left exactly as it was.
	/// </summary>
	private string? ChooseMinecraftSetup(IReadOnlyList<MinecraftPack> packs)
	{
		var keys = new Dictionary<string, string>(StringComparer.Ordinal);
		string ownVersion = OwnMinecraftVersion();
		keys[ownVersion.Length > 0 ? Loc.T("mc.pack.chooseOwn", ownVersion) : Loc.T("mc.pack.chooseOwnNoVersion")]
			= OwnMinecraftKey();

		foreach (MinecraftPack pack in packs)
		{
			string label = Loc.T("mc.pack.choosePack", pack.Name, pack.MinecraftVersion);
			// Two copies of one pack share a name; the folder is what tells them apart.
			if (keys.ContainsKey(label)) label = Loc.T("mc.pack.choosePackCopy", label, Path.GetFileName(pack.Folder));
			keys[label] = MinecraftModpacks.InstallKeyFor(pack);
		}

		// Opens on whichever was used last, so opening the same pack again is one key press.
		string last = MinecraftModpacks.IsPackKey(_settings.ActiveGame) ? _settings.ActiveGame : _settings.LastMinecraftSetup;
		string current = keys.FirstOrDefault(k => k.Value == last).Key ?? keys.Keys.First();

		string? picked = ShowChoiceList(Loc.T("mc.pack.chooseTitle"), Loc.T("mc.pack.chooseListName"),
			keys.Keys.ToList(), current, Loc.T("mc.pack.chooseHint"));

		return picked != null && keys.TryGetValue(picked, out string? key) ? key : null;
	}

	/// <summary>
	/// Loads the player's own Minecraft, or a pack, as the session. A pack's folders are recorded under its key
	/// first, which is all the rest of the manager needs to treat it as a game of its own.
	/// </summary>
	private void SwitchToMinecraftSetup(string key)
	{
		if (MinecraftModpacks.ForKey(key, MinecraftModpacks.PacksFolder) is { } pack)
		{
			_settings.GamePaths[key] = pack.Folder;
			_settings.GameModsPaths[key] = pack.ModsFolder;
			try { Directory.CreateDirectory(pack.ModsFolder); }
			catch (Exception ex) { DiagnosticLog.WriteException("Minecraft", $"creating {pack.ModsFolder}", ex); }
		}

		_settings.LastMinecraftSetup = key;
		_activePackCache = default;

		// Already loaded: SwitchActiveGame would do nothing, and saying nothing would sound like a failure.
		if (_settings.ActiveGame == key)
		{
			_settings.Save();
			Speak(Loc.T("session.switched", GameDisplayName()));
			return;
		}

		SwitchActiveGame(key);
	}

	// -------------------------------------------------------------------------
	// The Minecraft Packs tab
	// -------------------------------------------------------------------------

	/// <summary>Builds the tab. Shown only in Minecraft sessions — the player's own and every pack's.</summary>
	private void BuildMinecraftPacksTab()
	{
		tabMinecraftPacks = new TabPage(Loc.T("tab.minecraftPacks"));

		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45f));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

		var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };

		Button Add(string text, Action onClick)
		{
			var button = new Button { Text = text, AutoSize = true, Height = 35 };
			button.Click += delegate { onClick(); };
			buttons.Controls.Add(button);
			return button;
		}

		Add(Loc.T("mc.pack.btnInstallFile"), InstallModpackFromFilePicker);
		Add(Loc.T("mc.pack.btnSearch"), SearchForModpacks);
		Add(Loc.T("mc.pack.btnImport"), () => Fire(ImportFromModrinthAppAsync(), "ImportFromModrinthAppAsync"));
		Add(Loc.T("mc.pack.btnCheckUpdates"), () => Fire(CheckAllModpackUpdatesAsync(announce: true), "CheckAllModpackUpdatesAsync"));
		Add(Loc.T("mc.versions.button"), () => Fire(RemoveUnusedMinecraftVersionsAsync(), "RemoveUnusedMinecraftVersionsAsync"));

		listMinecraftPacks = new ListBox
		{
			Dock = DockStyle.Fill,
			Name = "listMinecraftPacks",
			Font = new Font("Segoe UI", 12f),
			AccessibleName = Loc.T("mc.pack.listName"),
			AccessibleDescription = Loc.T("mc.pack.listDescription")
		};
		listMinecraftPacks.GotFocus += List_Enter;
		listMinecraftPacks.SelectedIndexChanged += ListModPriority_SelectedIndexChanged;
		listMinecraftPacks.KeyDown += ListMinecraftPacks_KeyDown;
		listMinecraftPacks.DoubleClick += delegate { Fire(ShowMinecraftSetupActionsAsync(), "ShowMinecraftSetupActionsAsync"); };

		layout.Controls.Add(buttons, 0, 0);
		layout.Controls.Add(listMinecraftPacks, 0, 1);
		tabMinecraftPacks.Controls.Add(layout);
	}

	/// <summary>Adds or removes the tab for the session being loaded. It sits right after Installed.</summary>
	private void ShowMinecraftPacksTabFor(string game)
	{
		bool wanted = GameProfiles.IsGame(game, GameProfiles.Minecraft);
		bool present = mainTabs.TabPages.Contains(tabMinecraftPacks);

		if (wanted && !present) mainTabs.TabPages.Insert(Math.Min(1, mainTabs.TabPages.Count), tabMinecraftPacks);
		else if (!wanted && present) mainTabs.TabPages.Remove(tabMinecraftPacks);

		// The search tab's mods-or-modpacks choice belongs to Minecraft too. Leaving it set to modpacks when the
		// session changes would make the next Minecraft search quietly look for packs, so it goes back to mods.
		if (cmbDiscoveryContent != null)
		{
			cmbDiscoveryContent.Visible = wanted;
			if (_lblDiscoveryContent != null) _lblDiscoveryContent.Visible = wanted;
			cmbDiscoveryContent.SelectedIndex = 0;
		}
	}

	/// <summary>
	/// Rebuilds the list: the player's own Minecraft first, then every pack, each saying its Minecraft version,
	/// whether it will speak, whether an update is waiting, and which one is loaded now. Keeps the selection.
	/// </summary>
	private async Task RefreshMinecraftPacksListAsync()
	{
		if (listMinecraftPacks == null || !GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.Minecraft)) return;

		string packsFolder = MinecraftModpacks.PacksFolder;
		string active = _settings.ActiveGame;
		string ownVersion = OwnMinecraftVersion();

		// Reading which accessibility mod each pack has means opening its jars, so it is done off the UI thread.
		List<MinecraftSetupRow> rows = await Task.Run(() =>
		{
			var built = new List<MinecraftSetupRow>();

			string own = ownVersion.Length > 0 ? Loc.T("mc.pack.rowOwn", ownVersion) : Loc.T("mc.pack.rowOwnNoVersion");
			if (!MinecraftModpacks.IsPackKey(active)) own = Loc.T("mc.pack.rowCurrent", own);
			built.Add(new MinecraftSetupRow { Text = own });

			foreach (MinecraftPack pack in MinecraftModpacks.FindInstalled(packsFolder))
			{
				IReadOnlyList<MinecraftSuiteMod> access = AccessModsInPack(pack);
				string speech = access.Count > 0 ? access[0].DisplayName : Loc.T("mc.pack.rowNoAccessMod");

				string text = pack.PackVersion.Length > 0
					? Loc.T("mc.pack.row", pack.Name, pack.PackVersion, pack.MinecraftVersion, speech)
					: Loc.T("mc.pack.rowNoVersion", pack.Name, pack.MinecraftVersion, speech);

				if (_packUpdates.TryGetValue(pack.Folder, out ModpackVersion? update))
					text = Loc.T("mc.pack.rowUpdate", text, update.VersionNumber);
				if (MinecraftModpacks.InstallKeyFor(pack) == active)
					text = Loc.T("mc.pack.rowCurrent", text);

				built.Add(new MinecraftSetupRow { Pack = pack, Text = text });
			}

			return built;
		});

		string? keep = (listMinecraftPacks.SelectedItem as MinecraftSetupRow)?.Pack?.Folder;
		int keepIndex = listMinecraftPacks.SelectedIndex;

		listMinecraftPacks.BeginUpdate();
		listMinecraftPacks.Items.Clear();
		foreach (MinecraftSetupRow row in rows) listMinecraftPacks.Items.Add(row);

		int select = keep != null ? rows.FindIndex(r => r.Pack?.Folder == keep) : keepIndex;
		listMinecraftPacks.SelectedIndex = Math.Clamp(select, 0, rows.Count - 1);
		listMinecraftPacks.EndUpdate();
	}

	private void ListMinecraftPacks_KeyDown(object? sender, KeyEventArgs e)
	{
		if (e.KeyCode == Keys.Return || (e.KeyCode == Keys.F10 && e.Shift) || e.KeyCode == Keys.Apps)
		{
			e.Handled = e.SuppressKeyPress = true;
			Fire(ShowMinecraftSetupActionsAsync(), "ShowMinecraftSetupActionsAsync");
		}
		else if (e.KeyCode == Keys.Delete && listMinecraftPacks.SelectedItem is MinecraftSetupRow { Pack: { } pack })
		{
			e.Handled = e.SuppressKeyPress = true;
			Fire(DeleteModpackAsync(pack), "DeleteModpackAsync");
		}
	}

	/// <summary>
	/// What can be done with the selected row, as a list to choose from — the same shape as Enter on an update.
	/// Only what makes sense is offered: switching to the setup already loaded is not.
	/// </summary>
	private async Task ShowMinecraftSetupActionsAsync()
	{
		if (listMinecraftPacks.SelectedItem is not MinecraftSetupRow row) return;

		var actions = new List<(string Label, Func<Task> Run)>();

		if (row.Pack is not { } pack)
		{
			if (!MinecraftModpacks.IsPackKey(_settings.ActiveGame))
			{
				Speak(Loc.T("mc.pack.ownAlreadyLoaded"));
				return;
			}
			actions.Add((Loc.T("mc.pack.actionSwitchOwn"), () => { SwitchToMinecraftSetup(OwnMinecraftKey()); return Task.CompletedTask; }));
		}
		else
		{
			string key = MinecraftModpacks.InstallKeyFor(pack);
			bool loaded = _settings.ActiveGame == key;

			if (!loaded)
				actions.Add((Loc.T("mc.pack.actionSwitch"), () => { SwitchToMinecraftSetup(key); return Task.CompletedTask; }));
			actions.Add((Loc.T("mc.pack.actionPlay"), () => LaunchMinecraftAsync(pack)));
			if (_packUpdates.TryGetValue(pack.Folder, out ModpackVersion? update))
				actions.Add((Loc.T("mc.pack.actionUpdate", update.VersionNumber), () => UpdateModpackAsync(pack, update)));
			else if (pack.ModrinthProjectId.Length > 0)
				actions.Add((Loc.T("mc.pack.actionCheckUpdate"), () => CheckModpackUpdateAsync(pack, announce: true)));
			actions.Add((Loc.T("mc.pack.actionCopyWorld"), () => OfferToCopyWorldAsync(pack, askFirst: false)));
			actions.Add((Loc.T("mc.pack.actionOpenFolder"), () => { OpenFolder(pack.Folder); return Task.CompletedTask; }));
			actions.Add((Loc.T("mc.pack.actionDelete"), () => DeleteModpackAsync(pack)));
		}

		string? picked = ShowChoiceList(row.Pack?.Name ?? Loc.T("mc.pack.ownName"), Loc.T("mc.pack.actionsListName"),
			actions.Select(a => a.Label).ToList(), actions[0].Label, Loc.T("mc.pack.actionsHint"));
		if (picked == null) return;

		await actions.First(a => a.Label == picked).Run();
	}

	/// <summary>Opens a folder in File Explorer. Never throws.</summary>
	private void OpenFolder(string folder)
	{
		try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true }); }
		catch (Exception ex) { LogFailure("Minecraft", $"Could not open {folder}", ex); }
	}

	/// <summary>The same file picker as Install from file, offering modpacks only.</summary>
	private void InstallModpackFromFilePicker()
	{
		using var dialog = new OpenFileDialog
		{
			InitialDirectory = downloadsPath,
			Filter = Loc.T("mc.pack.fileFilter")
		};
		if (dialog.ShowDialog() == DialogResult.OK)
			Fire(InstallModpackFromFileAsync(dialog.FileName), "InstallModpackFromFileAsync");
	}

	// -------------------------------------------------------------------------
	// Deleting a pack
	// -------------------------------------------------------------------------

	/// <summary>
	/// Deletes a pack: its folder goes to the Recycle Bin, worlds and all, after a question that names the worlds.
	///
	/// A pack's worlds live inside it, which is the one thing about deleting a pack a player might not expect —
	/// so they are named, not counted. If the pack is the session loaded now, the player's own Minecraft is loaded
	/// first, because the session cannot keep pointing at a folder that is no longer there.
	/// </summary>
	private async Task DeleteModpackAsync(MinecraftPack pack)
	{
		List<string> worlds = WorldsIn(pack.Folder);
		string question = worlds.Count == 0
			? Loc.T("mc.pack.deleteConfirm", pack.Name)
			: Loc.T("mc.pack.deleteConfirmWorlds", pack.Name, worlds.Count, string.Join(", ", worlds));

		if (SpeakBox(question, Loc.T("common.confirmDelete"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
			!= DialogResult.Yes)
		{
			Speak(Loc.T("common.changesCancelled"));
			return;
		}

		string key = MinecraftModpacks.InstallKeyFor(pack);
		if (_settings.ActiveGame == key) SwitchToMinecraftSetup(OwnMinecraftKey());

		try
		{
			await Task.Run(() => Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(pack.Folder,
				Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
				Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin));
		}
		catch (Exception ex)
		{
			// The usual cause is the game still running from that folder, holding its files open.
			LogFailure("Minecraft", $"Could not delete the modpack {pack.Name}", ex);
			SpeakBox(Loc.T("mc.pack.deleteFailed", pack.Name, FriendlyError(ex)), Loc.T("common.error"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}

		// Its backups and downloads are left where they are: filed under the same key, they come back if the same
		// pack is ever installed again, and they are the player's to prune like any other backup.
		_settings.GamePaths.Remove(key);
		_settings.GameModsPaths.Remove(key);
		if (_settings.LastMinecraftSetup == key) _settings.LastMinecraftSetup = "";
		_settings.Save();
		_packUpdates.Remove(pack.Folder);

		Speak(Loc.T("mc.pack.deleted", pack.Name));
		await RefreshMinecraftPacksListAsync();
		await OfferToRemovePacksVersionAsync(pack);
	}

	/// <summary>The worlds in a Minecraft folder: every folder under <c>saves</c> that holds a <c>level.dat</c>.</summary>
	private static List<string> WorldsIn(string gameFolder)
	{
		string saves = Path.Combine(gameFolder, "saves");
		if (!Directory.Exists(saves)) return new List<string>();

		return Directory.EnumerateDirectories(saves)
			.Where(d => File.Exists(Path.Combine(d, "level.dat")))
			.Select(Path.GetFileName)
			.OfType<string>()
			.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
			.ToList();
	}
}
