using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic;
using StardewMod = KinetixModManager.GameMod;

namespace KinetixModManager;

/// <summary>
/// Mod collections: export the current enabled loadout to a portable, shareable JSON "recipe", and install a
/// collection by re-downloading each mod from Nexus and arranging it in the recorded load order. A collection
/// holds only references (Nexus ids + order), never the mod files, so it stays small and redistributes nothing.
/// </summary>
public partial class Form1
{
	/// <summary>The default folder for collection files, created on demand.</summary>
	private string CollectionsDir()
	{
		string dir = Path.Combine(dataBasePath, "collections");
		try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
		catch (Exception ex) { DiagnosticLog.WriteException("Collection", $"creating the collections folder {dir}", ex); }
		return dir;
	}

	/// <summary>Strips characters that aren't valid in a Windows file name so a collection name can seed the save dialog.</summary>
	private static string MakeSafeFileName(string name)
	{
		foreach (char c in Path.GetInvalidFileNameChars())
			name = name.Replace(c, '_');
		return string.IsNullOrWhiteSpace(name) ? "Collection" : name.Trim();
	}

	/// <summary>
	/// Gathers the enabled, non-group mods that make up the current loadout, highest load-order priority first.
	/// For Skyrim/Fallout 4 this follows the saved mod-priority order; other games have no priority, so it's the
	/// installed-list order.
	/// </summary>
	private List<StardewMod> EnabledModsInOrder()
	{
		IEnumerable<StardewMod> enabled = _allInstalledMods.Where(m => !m.IsGroup && m.IsEnabled);
		if (!IsBethesdaGame) return enabled.ToList();

		EnsureModPriorityList();
		var byKey = enabled
			.GroupBy(PriorityKey, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

		var ordered = new List<StardewMod>();
		foreach (string name in _settings.ModPriority[_settings.ActiveGame])
			if (byKey.TryGetValue(name, out StardewMod? mod)) ordered.Add(mod);
		// Safety net: include any enabled mod that somehow isn't in the priority list yet.
		foreach (StardewMod mod in enabled)
			if (!ordered.Contains(mod)) ordered.Add(mod);
		return ordered;
	}

	/// <summary>
	/// Exports the current enabled loadout to a collection file the user picks. Mods without a Nexus id can't be
	/// re-downloaded, so they're recorded only as "you'll need to supply these yourself" rather than dropped.
	/// </summary>
	private async Task ExportCollection()
	{
		if (_settings.ActiveGame == "None")
		{
			Speak(Loc.T("collection.noGame"));
			return;
		}

		var collection = new Collection
		{
			Game = _settings.ActiveGame,
			AppVersion = NexusService.AppVersion,
			CreatedUtc = DateTime.UtcNow
		};

		int index = 0;
		foreach (StardewMod mod in EnabledModsInOrder())
		{
			if (string.IsNullOrEmpty(mod.NexusID))
			{
				collection.UnavailableLocal.Add(mod.Name);
				continue;
			}
			collection.Mods.Add(new CollectionMod
			{
				NexusId = mod.NexusID,
				Name = mod.Name,
				Version = mod.Version,
				PriorityIndex = index++,
				Required = true
			});
		}

		if (collection.Mods.Count == 0)
		{
			Speak(Loc.T("collection.exportEmpty"));
			return;
		}

		// ShowTextPrompt, not Interaction.InputBox. InputBox is a window of its own whose text box carries no
		// accessible name, so a screen reader announced it as nothing but "edit" followed by "OK" — the question
		// itself was never attached to the field being answered. It also costs the spoken window caption on the
		// way in and again on the way out, which is the whole reason the rest of the app's screens moved inside
		// the main window. The inline prompt labels its field and stays put.
		string? name = ShowTextPrompt(
			Loc.T("collection.nameTitle"),
			Loc.T("collection.namePrompt"),
			Loc.T("collection.nameDefault"));

		if (name == null) { Speak(Loc.T("common.changesCancelled")); return; }

		name = name.Trim();
		if (name.Length == 0) { Speak(Loc.T("collection.nameEmpty")); return; }
		collection.Name = name;

		using SaveFileDialog dialog = new SaveFileDialog
		{
			Filter = Loc.T("collection.fileFilter"),
			InitialDirectory = CollectionsDir(),
			FileName = MakeSafeFileName(name) + ".json"
		};
		DialogResult picked = dialog.ShowDialog();

		// Before anything is said or shown: the picker closing sets the reader off re-reading the main window,
		// which otherwise lands on top of the next sentence or swallows a prompt's question.
		// Settled for the timing alone — the answer must not decide whether the work happens.
		await SettleAfterForeignWindowAsync();
		if (picked != DialogResult.OK) return;

		try
		{
			collection.Save(dialog.FileName);
			var written = new FileInfo(dialog.FileName);
			if (!written.Exists || written.Length == 0)
				throw new IOException(Loc.T("share.saveVanished", dialog.FileName));
			DiagnosticLog.Write("Collection", $"saved {written.Length} bytes to {dialog.FileName}");
		}
		catch (Exception ex)
		{
			_soundEngine.Play("error");
			SpeakBox(Loc.T("collection.exportFailed", FriendlyError(ex)));
			return;
		}

		_soundEngine.Play("load_complete");
		SpeakWithBearings(Loc.T("collection.exported", collection.Mods.Count, collection.UnavailableLocal.Count));
	}

	/// <summary>
	/// Installs a collection the user picks: re-downloads each listed mod from Nexus, installs it, then applies
	/// the collection's load order. Premium accounts download automatically; free accounts can't (a Nexus API
	/// restriction), so their mods are routed to a manual-download queue shown in the end-of-run report. A
	/// pre-flight summary lets the user review what will happen before committing. Installs run one at a time;
	/// a failure on one mod is recorded and the rest continue.
	/// </summary>
	private async Task InstallCollectionAsync()
	{
		if (_settings.ActiveGame == "None") { Speak(Loc.T("collection.noGame")); return; }
		if (string.IsNullOrEmpty(_settings.ApiKey)) { Speak(Loc.T("collection.noLogin")); return; }

		using OpenFileDialog dialog = new OpenFileDialog
		{
			Filter = Loc.T("collection.fileFilter"),
			InitialDirectory = CollectionsDir()
		};
		DialogResult picked = dialog.ShowDialog();

		// Before anything is said or shown: the picker closing sets the reader off re-reading the main window,
		// which otherwise lands on top of the next sentence or swallows a prompt's question.
		// Settled for the timing alone — the answer must not decide whether the work happens.
		await SettleAfterForeignWindowAsync();
		if (picked != DialogResult.OK) return;

		Collection? collection = Collection.Load(dialog.FileName);
		if (collection == null || collection.Mods.Count == 0)
		{
			_soundEngine.Play("error");
			SpeakWithBearings(Loc.T("collection.loadFailed"));
			return;
		}
		// A collection is game-specific (mod ids and load order only mean anything for the game it was built for).
		if (!string.Equals(collection.Game, _settings.ActiveGame, StringComparison.OrdinalIgnoreCase))
		{
			SpeakBox(Loc.T("collection.gameMismatch", collection.Name, collection.Game), Loc.T("collection.installTitle"), MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}

		// Premium accounts download automatically; free accounts can't (Nexus API rule), so their mods go to a
		// manual-download queue shown in the report. The pre-flight summary reflects which path applies.
		bool premium = _nexusService.IsPremium;
		int autoCount = premium ? collection.Mods.Count : 0;
		int manualCount = premium ? 0 : collection.Mods.Count;

		string summary = Loc.T("collection.preflightSummary",
			collection.Name, collection.Mods.Count, autoCount, manualCount, collection.UnavailableLocal.Count);
		// Only the premium auto-install path can pop a FOMOD wizard mid-batch, so warn about setup questions then.
		if (premium) summary += Loc.T("collection.preflightFomodNote");
		var preflightLines = new List<string>();
		foreach (CollectionMod cm in collection.Mods)
			preflightLines.Add(Loc.T(premium ? "collection.preflightAuto" : "collection.preflightManual", cm.Name));
		foreach (string local in collection.UnavailableLocal)
			preflightLines.Add(Loc.T("collection.preflightUnavailable", local));

		if (!ShowCollectionPreflight(summary, preflightLines)) return;

		var installedDisplay = new List<string>();
		var installedNames = new List<string>();
		var manual = new List<CollectionMod>();
		var skipped = new List<CollectionMod>();
		var failed = new List<(CollectionMod Mod, string Reason)>();

		for (int i = 0; i < collection.Mods.Count; i++)
		{
			CollectionMod cm = collection.Mods[i];
			if (!premium)
			{
				// Free account: can't auto-download, so queue every mod for manual download.
				manual.Add(cm);
				continue;
			}

			// The progress announcer voices "Downloading 3 of 40: SkyUI" plus deciles, so keep the status line silent.
			SetStatus(Loc.T("collection.installingStatus", i + 1, collection.Mods.Count, cm.Name), speak: false);
			try
			{
				var stub = new GameMod { NexusID = cm.NexusId, Name = cm.Name, Version = cm.Version };
				ProgressAnnouncer progress = NewProgress(Loc.T("collection.modProgressName", i + 1, collection.Mods.Count, cm.Name), installing: false);
				string zip = await _nexusService.DownloadModUpdateAsync(stub, downloadsPath, progress);
				progress.Complete();

				// A scripted (FOMOD) mod opens the accessible install wizard so the user picks their own options and
				// hits OK; the batch then carries on to the next mod. Plain mods install straight through (the selector
				// is only consulted for FOMOD archives). confirmOverwrite null => silent overwrite of an existing copy.
				string installedName = await ModFileSystem.ExtractModAsync(
					zip, _settings.CurrentModsPath, _allInstalledMods, backupsPath, _settings.MaxBackupsPerMod,
					_settings.ActiveGame, LogError, cm.NexusId, _nexusService, null, _settings.CurrentGamePath,
					fomodSelector: ShowFomodWizardAsync, installProgress: null, confirmOverwrite: null);

				installedNames.Add(installedName);
				installedDisplay.Add(cm.Name);
			}
			catch (OperationCanceledException)
			{
				// The user cancelled this mod's FOMOD setup wizard — skip it and keep going, rather than failing.
				skipped.Add(cm);
			}
			catch (Exception ex)
			{
				failed.Add((cm, ex.Message));
				LogFailure("Collection", $"Failed to install {cm.Name}", ex);
			}
		}

		await RefreshModList(checkUpdates: false);

		// Apply the collection's load order for Skyrim/Fallout 4: move its mods to the top in recorded order,
		// keeping any other installed mods below them, then re-link so the new winner order takes effect.
		if (IsBethesdaGame && installedNames.Count > 0)
		{
			EnsureModPriorityList();
			List<string> priority = _settings.ModPriority[_settings.ActiveGame];
			var top = installedNames.Distinct(StringComparer.OrdinalIgnoreCase)
				.Where(n => priority.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
			var rest = priority.Where(n => !top.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
			_settings.ModPriority[_settings.ActiveGame] = top.Concat(rest).ToList();
			_settings.Save();
			SyncBethesdaDeployment(new HashSet<string>(installedNames, StringComparer.OrdinalIgnoreCase));
			RefreshModPriorityList();
		}

		ResetStatus();
		_soundEngine.Play(failed.Count == 0 ? "load_complete" : "error");
		ShowCollectionReport(collection, installedDisplay, manual, skipped, failed);
	}

	/// <summary>
	/// Shows the pre-flight review before a collection install: a spoken summary plus an arrowable list of every
	/// mod and what will happen to it (auto-download, manual download, or unavailable). Returns true if the user
	/// chooses Install, false if they cancel. Modal and screen-reader friendly, mirroring <see cref="ShowReportDialog"/>.
	/// </summary>
	private bool ShowCollectionPreflight(string summary, List<string> lines)
	{
		// Cancel unless the user actively chooses Install, so leaving any other way — Escape included — installs
		// nothing. Captured before the view closes and returned once it has.
		bool install = false;

		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("collection.installTitle"), (container, closeView) =>
		{
		var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

		layout.Controls.Add(new Label { Text = summary, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8) }, 0, 0);

		// A short label, not the summary sentence: a list's name is read every time focus arrives on it, so a
		// paragraph there is heard again on every Tab press. The summary is spoken once, below, and shown above.
		var list = new ListBox { Dock = DockStyle.Fill, AccessibleName = Loc.T("collection.preflightListName"), IntegralHeight = false, HorizontalScrollbar = true };
		foreach (string line in lines) list.Items.Add(line);
		WireAccessibleDialogList(list);
		layout.Controls.Add(list, 0, 1);

		var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
		var btnInstall = new Button { Text = Loc.T("collection.preflightInstallBtn"), AutoSize = true };
		var btnCancel = new Button { Text = Loc.T("collection.preflightCancelBtn"), AutoSize = true };
		btnInstall.Click += (_, _) => { install = true; closeView(); };
		btnCancel.Click += (_, _) => closeView();
		buttons.Controls.Add(btnInstall);
		buttons.Controls.Add(btnCancel);
		layout.Controls.Add(buttons, 0, 2);

		container.Controls.Add(layout);

		return list;
		},
		// Through the hint, so it follows the title rather than arriving ahead of it. The summary describes what
		// is about to be installed rather than repeating the title, so it is kept whole. See Form1.InlineView.
		hint: summary + " " + Loc.T("collection.preflightOpen"));

		return install;
	}

	/// <summary>
	/// Shows the end-of-run report: what installed, what still needs a manual download (free accounts, or a mod
	/// the API wouldn't serve), and what failed. Manual and failed rows open the mod's Nexus files page on Enter,
	/// where the user clicks "Mod Manager Download" to install it through the existing nxm:// handler.
	/// </summary>
	private void ShowCollectionReport(Collection collection, List<string> installed, List<CollectionMod> manual, List<CollectionMod> skipped, List<(CollectionMod Mod, string Reason)> failed)
	{
		string domain = _nexusService.CurrentGameDomain;
		string FilesUrl(CollectionMod m) => $"https://www.nexusmods.com/{domain}/mods/{m.NexusId}?tab=files";

		var rows = new List<ReportRow>();
		foreach (string name in installed)
			rows.Add(new ReportRow { Text = Loc.T("collection.reportInstalled", name) });
		foreach (CollectionMod m in manual)
			rows.Add(new ReportRow { Text = Loc.T("collection.reportManual", m.Name), OpenUrl = FilesUrl(m) });
		foreach (CollectionMod m in skipped)
			rows.Add(new ReportRow { Text = Loc.T("collection.reportSkipped", m.Name), OpenUrl = FilesUrl(m) });
		foreach ((CollectionMod m, string reason) in failed)
			rows.Add(new ReportRow { Text = Loc.T("collection.reportFailed", m.Name, reason), OpenUrl = FilesUrl(m) });

		string header = Loc.T("collection.reportHeader", collection.Name, installed.Count, manual.Count, skipped.Count, failed.Count);
		string? hint = (manual.Count > 0 || skipped.Count > 0 || failed.Count > 0) ? Loc.T("collection.reportActionHint") : null;
		ShowReportDialog(Loc.T("collection.reportTitle"), header, Loc.T("collection.reportEmpty"), rows, hint);
	}
}
