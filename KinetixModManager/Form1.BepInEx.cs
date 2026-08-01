using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// BepInEx support for Moonlight Peaks: telling whether the loader is installed, installing it, and warning
/// when the installed version isn't the one the game's mods are built against.
///
/// This is the BepInEx equivalent of what the manager already does for SMAPI and SKSE/F4SE. It matters more
/// here than it looks: BepInEx doesn't announce itself in-game, so without the loader a user simply starts
/// Moonlight Peaks, finds none of their mods running, and has nothing to go on. Checking it when the session
/// loads turns that into a spoken sentence and an offer to fix it.
/// </summary>
public partial class Form1
{
	/// <summary>
	/// The BepInEx release the manager installs and expects. Moonlight Peaks' mods are, as of this writing, all
	/// built against BepInEx 5, and a plugin built for 5.x will not load under 6.x — the plugin API changed. So
	/// this is deliberately pinned to a known-good 5.x build rather than tracking "latest", which would one day
	/// silently hand users a 6.x that loads none of their mods.
	/// </summary>
	private const string BepInExPinnedVersion = "5.4.23.5";

	/// <summary>The 64-bit Windows build of <see cref="BepInExPinnedVersion"/>. Moonlight Peaks is 64-bit only.</summary>
	private const string BepInExDownloadUrl =
		"https://github.com/BepInEx/BepInEx/releases/download/v" + BepInExPinnedVersion +
		"/BepInEx_win_x64_" + BepInExPinnedVersion + ".zip";

	/// <summary>Guards against re-running the session check when the same game is reloaded.</summary>
	private bool _bepInExCheckedThisSession;

	/// <summary>True when the active game loads its mods through BepInEx.</summary>
	private bool IsBepInExGame => GameProfiles.Find(_settings.ActiveGame)?.IsBepInEx == true;

	/// <summary>
	/// True when BepInEx is installed in <paramref name="gameRoot"/>. Both halves have to be there: the
	/// <c>winhttp.dll</c> shim next to the game executable is what gets BepInEx running at all, and the core
	/// assembly is what actually loads plugins. A game folder with only one of them is a half-finished install
	/// (or a leftover from a manual delete) and loads nothing.
	/// </summary>
	private static bool IsBepInExInstalled(string gameRoot)
	{
		if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return false;
		return File.Exists(Path.Combine(gameRoot, "winhttp.dll")) &&
			   File.Exists(Path.Combine(gameRoot, "BepInEx", "core", "BepInEx.dll"));
	}

	/// <summary>
	/// The installed BepInEx version, or <c>null</c> when it can't be determined. The core assembly's file
	/// version is the direct answer; BepInEx also writes its version into the first line of its log, which
	/// covers an install whose DLL version was stripped.
	/// </summary>
	private static string? DetectBepInExVersion(string gameRoot)
	{
		if (string.IsNullOrEmpty(gameRoot)) return null;

		try
		{
			string core = Path.Combine(gameRoot, "BepInEx", "core", "BepInEx.dll");
			if (File.Exists(core))
			{
				FileVersionInfo info = FileVersionInfo.GetVersionInfo(core);
				if (!string.IsNullOrEmpty(info.FileVersion) && !info.FileVersion.StartsWith("0.0.0"))
					return info.FileVersion.Trim();
			}
		}
		catch { }

		try
		{
			// e.g. "[Message:   BepInEx] BepInEx 5.4.23.5 - Moonlight Peaks (7/12/2026 12:39:28 AM)"
			string log = Path.Combine(gameRoot, "BepInEx", "LogOutput.log");
			if (File.Exists(log))
			{
				using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				using var reader = new StreamReader(stream);
				string? first = reader.ReadLine();
				if (first != null)
				{
					Match m = Regex.Match(first, @"BepInEx\s+(?<version>\d+(?:\.\d+)+)");
					if (m.Success) return m.Groups["version"].Value;
				}
			}
		}
		catch { }

		return null;
	}

	/// <summary>
	/// Checks BepInEx once per loaded session and speaks what it finds: offers to install it when it is missing,
	/// and points out a version that differs from the one the manager installs. Never blocks the session — a
	/// user who wants to play unmodded, or who is deliberately running a different BepInEx, just says no.
	/// </summary>
	private async Task CheckBepInExForSessionAsync()
	{
		if (!IsBepInExGame || _bepInExCheckedThisSession) return;
		_bepInExCheckedThisSession = true;

		string gameRoot = _settings.CurrentGamePath;
		if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) return;

		if (!IsBepInExInstalled(gameRoot))
		{
			await OfferBepInExInstallAsync(gameRoot);
			return;
		}

		string? installed = DetectBepInExVersion(gameRoot);
		if (installed != null && !VersionsMatch(installed, BepInExPinnedVersion))
		{
			Speak(Loc.T("bepinex.versionDiffersSpeak"));
			SpeakBox(
				Loc.T("bepinex.versionDiffersBox", installed, BepInExPinnedVersion),
				Loc.T("bepinex.versionDiffersTitle"),
				MessageBoxButtons.OK,
				MessageBoxIcon.Information);
		}
	}

	/// <summary>
	/// Compares two BepInEx version strings by their numbers alone, so "5.4.23.5" and "5.4.23.5.0" agree and a
	/// trailing build suffix doesn't raise a false warning.
	/// </summary>
	private static bool VersionsMatch(string a, string b)
	{
		static string Trim(string v)
		{
			string[] parts = v.Split('.');
			int last = parts.Length - 1;
			while (last > 0 && parts[last] == "0") last--;
			return string.Join('.', parts, 0, last + 1);
		}
		return Trim(a.Trim()) == Trim(b.Trim());
	}

	/// <summary>
	/// Tells the user BepInEx is missing and offers to install it. Returns <c>true</c> when it ended up
	/// installed, so callers that need it (the launch path) can decide what to do next.
	/// </summary>
	private async Task<bool> OfferBepInExInstallAsync(string gameRoot)
	{
		Speak(Loc.T("bepinex.missingSpeak"));
		DialogResult choice = SpeakBox(
			Loc.T("bepinex.missingBox", GameProfiles.DisplayNameFor(_settings.ActiveGame), BepInExPinnedVersion),
			Loc.T("bepinex.missingTitle"),
			MessageBoxButtons.YesNo,
			MessageBoxIcon.Warning);

		if (choice != DialogResult.Yes) return false;

		return await InstallBepInExAsync(gameRoot);
	}

	/// <summary>
	/// Downloads the pinned BepInEx release and unpacks it over the game folder, which is exactly how BepInEx is
	/// meant to be installed. Existing mods are safe: the archive carries only the loader's own files, so
	/// unpacking it over an install with plugins already in place replaces the loader and leaves them alone.
	/// </summary>
	private async Task<bool> InstallBepInExAsync(string gameRoot)
	{
		string tempDir = Path.Combine(Path.GetTempPath(), "KinetixBepInEx_" + Path.GetRandomFileName());

		try
		{
			_isLoading = true;
			SetStatus(Loc.T("bepinex.installing", BepInExPinnedVersion));

			Directory.CreateDirectory(tempDir);
			string zipPath = Path.Combine(tempDir, "BepInEx.zip");

			ProgressAnnouncer progress = NewProgress(Loc.T("bepinex.name"), installing: false);
			await _nexusService.DownloadFileWithProgressAsync(BepInExDownloadUrl, zipPath, progress);
			progress.Complete();

			await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, gameRoot, overwriteFiles: true));

			// The plugins folder is what the manager installs mods into, and a fresh BepInEx may not create it
			// until its first run.
			string plugins = Path.Combine(gameRoot, "BepInEx", "plugins");
			Directory.CreateDirectory(plugins);

			// A session whose mods path was never resolved (BepInEx wasn't installed when the game was detected)
			// now has somewhere real to point at.
			if (string.IsNullOrEmpty(_settings.CurrentModsPath) || !Directory.Exists(_settings.CurrentModsPath))
			{
				_settings.CurrentModsPath = plugins;
				_settings.Save();
			}

			_isLoading = false;
			_soundEngine.Play("load_complete");
			Speak(Loc.T("bepinex.installedSpeak", BepInExPinnedVersion));

			await RefreshModList(checkUpdates: false);
			ResetStatus();
			return true;
		}
		catch (Exception ex)
		{
			_isLoading = false;
			LogError("BepInEx", "Install failed: " + ex.Message);
			SpeakBox(
				Loc.T("bepinex.installFailedBox", FriendlyError(ex)),
				Loc.T("bepinex.installFailedTitle"),
				MessageBoxButtons.OK,
				MessageBoxIcon.Error);
			return false;
		}
		finally
		{
			try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
		}
	}

	/// <summary>
	/// Called just before a BepInEx game is launched. Without the loader the game starts perfectly happily with
	/// none of the user's mods, which is a confusing thing to discover from inside the game, so this says so
	/// first and offers to install it. Returns <c>false</c> only when the user chose to abandon the launch.
	/// </summary>
	private bool ConfirmBepInExBeforeLaunch(string gameRoot)
	{
		if (!IsBepInExGame || IsBepInExInstalled(gameRoot)) return true;

		Speak(Loc.T("bepinex.launchWithoutSpeak"));
		DialogResult choice = SpeakBox(
			Loc.T("bepinex.launchWithoutBox"),
			Loc.T("bepinex.missingTitle"),
			MessageBoxButtons.YesNo,
			MessageBoxIcon.Warning);

		return choice == DialogResult.Yes;
	}
}
