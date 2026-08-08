using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// Getting the right file off a mod page, and noticing when part of a mod never arrived.
///
/// Two things a newcomer cannot reasonably be expected to work out for themselves, both of which fail silently.
/// A script extender ships one build per game version — install the wrong one and it simply never loads, with
/// nothing anywhere saying so. And SSE Engine Fixes needs a second download from the same page, a loose DLL that
/// goes beside the game's exe rather than into the mods folder; miss it and the plugin cannot load either.
///
/// <see cref="ModPartRules"/> holds what each page needs and how to recognise it. This drives that table: it
/// resolves the exact file, downloads it for accounts that can, and sends everyone else to that one file rather
/// than to a Files tab with two dozen entries on it.
/// </summary>
public partial class Form1
{
	/// <summary>
	/// Installs whichever parts of <paramref name="known"/> are missing, and reports what it did.
	///
	/// Parts already present are skipped, so this is safe to run again after a partial install — which is the
	/// normal case, since a free account has to fetch each part through the browser one at a time.
	/// </summary>
	private async Task InstallKnownModPartsAsync(KnownMod known)
	{
		string gameFolder = ActiveGameFolder();
		if (string.IsNullOrEmpty(gameFolder))
		{
			Speak(Loc.T("modparts.noGameFolder", known.DisplayName));
			return;
		}

		var missing = known.Parts.Where(p => !IsPartInstalled(p, gameFolder)).ToList();
		if (missing.Count == 0)
		{
			Speak(Loc.T("modparts.alreadyComplete", known.DisplayName));
			return;
		}

		SetStatus(Loc.T("modparts.checkingFiles", known.DisplayName), speak: true);
		var files = await _nexusService.GetModFilesAsync(known.NexusModId);
		ResetStatus();

		if (files.Count == 0)
		{
			// No file list means no way to name the right file, so fall back to the page itself rather than
			// guessing — the old behaviour, kept for exactly this case.
			OpenModFilesTab(known);
			return;
		}

		string? build = ActiveGameBuild();
		GamePlatform platform = _settings.InstallFor(_settings.ActiveGame)?.Platform ?? GamePlatform.Unknown;

		foreach (ModPart part in missing)
		{
			NexusFileInfo? file = ModPartRules.PickFile(files, part, build, platform);
			if (file == null)
			{
				Speak(Loc.T("modparts.noFileFound", part.Name, known.DisplayName));
				OpenModFilesTab(known);
				continue;
			}

			await FetchAndInstallPartAsync(known, part, file, gameFolder);
		}
	}

	/// <summary>
	/// Downloads one part and puts it where it belongs — or, for an account that cannot be handed a download
	/// link, opens that one file's page and says what to do with it.
	/// </summary>
	private async Task FetchAndInstallPartAsync(KnownMod known, ModPart part, NexusFileInfo file, string gameFolder)
	{
		string what = Loc.T("modparts.partOf", part.Name, known.DisplayName);

		// A free account cannot be given a download link by the API at all — Nexus mints the key that unlocks it
		// on the website, which is what an nxm:// link carries. So the useful thing the manager can still do is
		// remove every choice: name the one file, open the page showing only that file, and say where it goes.
		// The nxm:// handler brings it back here and installs it properly.
		if (!_nexusService.IsPremium)
		{
			string url = ModPartRules.FilePageUrl(_nexusService.CurrentGameDomain, known.NexusModId, file.FileId);
			string where = part.Destination == PartDestination.GameRoot
				? Loc.T("modparts.destGameRoot")
				: Loc.T("modparts.destModsFolder");

			Speak(Loc.T("modparts.manualSpeak", what, file.Name));
			try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
			catch (Exception ex) { LogError(known.DisplayName, "Could not open " + url + ": " + ex.Message); }

			SpeakBox(Loc.T("modparts.manualBox", what, file.Name, where),
				Loc.T("modparts.manualTitle"), MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		try
		{
			SetStatus(Loc.T("modparts.downloading", what), speak: true);
			ProgressAnnouncer progress = NewProgress(what, installing: false);
			string archive = await _nexusService.DownloadFileByIdAsync(
				known.NexusModId, file.FileId, file.FileName, downloadsPath, progress);
			progress.Complete();

			if (part.Destination == PartDestination.GameRoot)
			{
				ProgressAnnouncer install = NewProgress(what, installing: true);
				// A script extender and a preloader both land loose in the game folder, but they are unpacked
				// differently — the extender records a manifest so it can be cleanly uninstalled later.
				if (IsScriptExtenderPart(part))
					await ModFileSystem.InstallScriptExtenderAsync(
						archive, gameFolder, _settings.ActiveGame, LogError, _nexusService, install);
				else
					await ModFileSystem.InstallEnginePreloaderAsync(archive, gameFolder, LogError, _nexusService);
				install.Complete();
			}
			else
			{
				await InstallFromZip(archive, known.NexusModId);
			}

			ResetStatus();
			Speak(Loc.T("modparts.installed", what));
		}
		catch (Exception ex)
		{
			ResetStatus();
			LogError(known.DisplayName, $"{part.Name} failed: {ex.Message}");
			Speak(Loc.T("modparts.failedSpeak", what));
			SpeakBox(Loc.T("modparts.failedBox", what, FriendlyError(ex)), Loc.T("modparts.manualTitle"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}
	}

	/// <summary>
	/// The script-extender entry for a game, looked up by the loader the profile names rather than by a Nexus id
	/// spelled out at the call site. Null for a game with no script extender.
	/// </summary>
	private static KnownMod? ScriptExtenderKnownMod(string game)
	{
		GameProfile? profile = GameProfiles.Find(game);
		if (profile == null || string.IsNullOrEmpty(profile.LoaderExeName)) return null;

		return ModPartRules.For(game).FirstOrDefault(m =>
			m.Parts.Any(p => string.Equals(p.DetectFile, profile.LoaderExeName, StringComparison.OrdinalIgnoreCase)));
	}

	/// <summary>Opens the mod's whole Files tab — the fallback when no single file could be settled on.</summary>
	private void OpenModFilesTab(KnownMod known)
	{
		string url = $"https://www.nexusmods.com/{_nexusService.CurrentGameDomain}/mods/{known.NexusModId}?tab=files";
		Speak(Loc.T("modparts.openingFilesTab", known.DisplayName));
		try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
		catch (Exception ex) { LogError(known.DisplayName, "Could not open " + url + ": " + ex.Message); }
	}

	/// <summary>True when a part is a script extender, which is unpacked with its own uninstall manifest.</summary>
	private static bool IsScriptExtenderPart(ModPart part) =>
		!string.IsNullOrEmpty(part.DetectFile) &&
		part.DetectFile.EndsWith("_loader.exe", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Whether <paramref name="part"/> is already on disk — by a file in the game folder, or by an installed mod
	/// whose name says so. A loose DLL beside the exe is invisible to the mod list, which is exactly why the
	/// file check exists.
	/// </summary>
	private bool IsPartInstalled(ModPart part, string gameFolder)
	{
		if (!string.IsNullOrEmpty(part.DetectFile))
		{
			try
			{
				if (File.Exists(Path.Combine(gameFolder, part.DetectFile))) return true;
			}
			catch { }
		}

		if (string.IsNullOrEmpty(part.DetectModName)) return false;

		return _allInstalledMods.Any(m =>
			(m.Name.Contains(part.DetectModName, StringComparison.OrdinalIgnoreCase) ||
			 m.UniqueId.Contains(part.DetectModName, StringComparison.OrdinalIgnoreCase)) &&
			(string.IsNullOrEmpty(part.DetectModNameExcluding) ||
			 !(m.Name.Contains(part.DetectModNameExcluding, StringComparison.OrdinalIgnoreCase) ||
			   m.UniqueId.Contains(part.DetectModNameExcluding, StringComparison.OrdinalIgnoreCase))));
	}

	/// <summary>The active copy's game folder, detected if it hasn't been recorded yet.</summary>
	private string ActiveGameFolder()
	{
		string folder = _settings.CurrentGamePath;
		if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) return folder;

		folder = DetectGameFolder(_settings.ActiveGame);
		return Directory.Exists(folder) ? folder : "";
	}

	/// <summary>
	/// The active copy's game build as "major.minor.build" — the number a script extender has to match — or
	/// <c>null</c> where the game keeps no such version or its exe cannot be read.
	/// </summary>
	private string? ActiveGameBuild()
	{
		var version = ReadGameRuntimeVersion(_settings.ActiveGame, ActiveGameFolder());
		return version == null ? null : $"{version.Value.major}.{version.Value.minor}.{version.Value.build}";
	}

	// -------------------------------------------------------------------------
	// The standing check
	// -------------------------------------------------------------------------

	/// <summary>
	/// Reports any known mod that is installed but incomplete — the classic case being SSE Engine Fixes with its
	/// preloader missing.
	///
	/// A standing check rather than something the suite installer does, because most people never go near the
	/// suite installer: they install the mod from Nexus themselves, or it arrives in a Collection or an MO2
	/// import. However it got here, half of it not being here is the same problem.
	///
	/// A mod none of whose parts are present is not reported. That is not an incomplete install, it is a mod the
	/// user has not chosen to have, and the requirements report already speaks for the ones that are genuinely
	/// required.
	/// </summary>
	private List<ReportRow> GatherMissingPartFindings()
	{
		var rows = new List<ReportRow>();

		string gameFolder = ActiveGameFolder();
		if (string.IsNullOrEmpty(gameFolder)) return rows;

		foreach (KnownMod known in ModPartRules.For(_settings.ActiveGame))
		{
			if (known.Parts.Count < 2) continue;

			var present = known.Parts.Where(p => IsPartInstalled(p, gameFolder)).ToList();
			if (present.Count == 0 || present.Count == known.Parts.Count) continue;

			foreach (ModPart part in known.Parts.Where(p => !IsPartInstalled(p, gameFolder)))
			{
				rows.Add(new ReportRow
				{
					Text = Loc.T("modparts.rowMissing", known.DisplayName, part.Name,
						part.Destination == PartDestination.GameRoot
							? Loc.T("modparts.destGameRoot")
							: Loc.T("modparts.destModsFolder")),
					// Enter on the row fetches whatever is still missing, through the same table that would have
					// installed it in the first place.
					OnEnter = () => InstallKnownModPartsAsync(known)
				});
			}
		}

		return rows;
	}
}
