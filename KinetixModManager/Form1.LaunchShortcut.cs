using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace KinetixModManager;

/// <summary>
/// Starting a game from a desktop shortcut, without the manager's window (GitHub issue #2). See <see cref="LaunchShortcut"/>
/// for why the shortcut goes through the manager at all.
///
/// <para>
/// Started that way, the manager runs as it always does, but out of sight and quiet: the window exists, fully transparent,
/// with no taskbar button and no Alt+Tab entry, and it never takes the focus, so the screen reader has nothing to announce.
/// It reads the game's mods, starts the game exactly as F5 does, and closes once the game has. Anything F5 would ask
/// (a version mismatch, low memory, BepInEx missing) brings the window up with the question, and from then on it is an
/// ordinary session. So does opening the manager while the game runs.
/// </para>
/// </summary>
public partial class Form1
{
	/// <summary>True while started from a shortcut and not yet shown. See <see cref="RevealFromLaunchOnly"/>.</summary>
	private bool _launchHidden;

	/// <summary>
	/// True while the hidden manager is reading the game's mods. What a session says as it loads ("Connected as", the mod
	/// it lands on) means nothing to someone who never sees it, so it isn't said. Lifted just before the launch, which
	/// says what it is doing in its own words.
	/// </summary>
	private bool _launchQuiet;

	/// <summary>Set once the game this launch was for has started, so a launch that went nowhere opens the window.</summary>
	private bool _launchStartedGame;

	/// <summary>A shortcut's game the manager can't find any more, said once the window is up. See <see cref="TellAboutMissingShortcutGame"/>.</summary>
	private string? _launchMissingGame;

	private const int WS_EX_TOOLWINDOW = 0x00000080;
	private const int WS_EX_TRANSPARENT = 0x00000020;
	private const int WS_EX_APPWINDOW = 0x00040000;
	private const int WS_EX_NOACTIVATE = 0x08000000;

	/// <summary>
	/// Called from the constructor, before anything reads which game is open. Opens the session on the shortcut's game, as
	/// a normal start opens it on the last one, so everything built from here is built for that game.
	/// </summary>
	private void PrepareLaunchFromShortcut(string[] args)
	{
		string? game = LaunchShortcut.GameToLaunch(args);
		if (game == null) return;

		// A key from an older shortcut, or a game uninstalled since: the manager opens as usual and says why. Starting
		// hidden on a game that isn't there would load another game's mods. See EnsureGameInstalledOrOfferPurchase.
		if (GameProfiles.Find(game) == null || !IsGameInstalled(game))
		{
			_launchMissingGame = game;
			return;
		}

		_settings.ActiveGame = game;
		_launchHidden = true;
		_launchQuiet = true;
		Opacity = 0;
		if (IsHandleCreated) UpdateStyles();
	}

	/// <summary>Kept from the foreground while hidden, so the screen reader never hears about the window.</summary>
	protected override bool ShowWithoutActivation => _launchHidden || base.ShowWithoutActivation;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams cp = base.CreateParams;
			// Out of the taskbar and Alt+Tab, never activated, and clicked straight through. Read again by UpdateStyles,
			// which is how the window gets its usual styles back without being rebuilt.
			if (_launchHidden)
				cp.ExStyle = (cp.ExStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT) & ~WS_EX_APPWINDOW;
			return cp;
		}
	}

	/// <summary>
	/// What the hidden manager does in place of the startup opening: read the mods, start the game, and leave the window
	/// out of sight. Called from the Shown handler.
	/// </summary>
	private async Task RunLaunchOnlyAsync()
	{
		string game = _settings.ActiveGame;
		try
		{
			// Reads the mods and brings the game folder up to date, as a normal start does. No update check: nobody is
			// there to hear its result. A deployment that differs from the mods (one changed outside the manager) is
			// put right here, before the game reads it.
			RefreshAllData(checkUpdates: false);

			// Quiet until the list is rebuilt: the rebuild says the row it lands on.
			while (Volatile.Read(ref _refreshInFlight) > 0 && !_shuttingDown) await Task.Delay(25);
			if (_shuttingDown || _settings.ActiveGame != game) return;

			// The launch says what it is doing itself: "Launching Skyrim Special Edition", or Minecraft's "Starting
			// Minecraft offline as …".
			_launchQuiet = false;

			if (GameProfiles.IsGame(game, GameProfiles.Minecraft)) await LaunchMinecraftAsync();
			else LaunchGame();
		}
		catch (Exception ex)
		{
			LogFailure("LaunchShortcut", "Could not start the game from its shortcut", ex);
		}

		// Nothing started, and nothing asked: whatever was said about it, the window is where to do something about it.
		if (!_launchStartedGame && _launchHidden) RevealFromLaunchOnly(userAsked: true);
	}

	/// <summary>Noted by both game trackers as they begin. See <see cref="RunLaunchOnlyAsync"/>.</summary>
	private void NoteGameStartedForShortcut() => _launchStartedGame = true;

	/// <summary>
	/// Brings the hidden manager into view, and from then on it is an ordinary session that stays open after the game.
	/// </summary>
	/// <param name="userAsked">
	/// True when the user opened the manager (from the Start menu, the manager's own shortcut, or a launch that went
	/// nowhere): it then says which session is loaded and lands on the first tab, as a start does. False when it is shown
	/// for a question, which says everything itself.
	/// </param>
	private void RevealFromLaunchOnly(bool userAsked)
	{
		if (!_launchHidden || IsDisposed) return;
		_launchHidden = false;
		_launchQuiet = false;

		// Hidden and shown again around the change: Windows gives a window its taskbar button and its place in Alt+Tab when
		// it is shown, not when its style changes.
		bool visible = Visible;
		if (visible) Visible = false;
		UpdateStyles();
		Opacity = 1;
		if (visible) Visible = true;

		// The opening is over, as far as the lists are concerned. See _startupSpeechInProgress.
		_startupSpeechInProgress = false;
		ForceToForeground();

		if (!userAsked) return;
		Speak(Loc.T("app.sessionLoaded", GameDisplayName()));
		_userMovedSinceLoadBegan = false;
		_focusWhenLandingArmed = GetFocus();
		_landShouldSpeak = true;
		LandOnFirstTab();
	}

	/// <summary>
	/// Called by both game trackers once the game has closed and the manager has tidied up after it. A manager that was
	/// started only to run the game goes too, quietly: "Game closed" has just been said, and there is no goodbye to give
	/// for a window nobody saw.
	/// </summary>
	private async Task CloseIfOnlyLaunchedAsync()
	{
		if (!_launchHidden || _shuttingDown) return;
		_shuttingDown = true;
		_pipeCts.Cancel();

		// As the shutdown does: a deployment pass ended partway leaves files linked that its record doesn't know about.
		long giveUpAt = Environment.TickCount64 + 60000;
		while (Volatile.Read(ref _refreshInFlight) > 0 && Environment.TickCount64 < giveUpAt) await Task.Delay(25);

		_announcer.Dispose();
		_readyToClose = true;
		Close();
	}

	/// <summary>
	/// What a second copy of the manager sent: a request to come to the front, or to start a game. See
	/// <see cref="LaunchShortcut"/>. Interface thread.
	/// </summary>
	private void HandleManagerMessage(string message)
	{
		if (_shuttingDown) return;

		if (LaunchShortcut.IsShowRequest(message))
		{
			if (_launchHidden) RevealFromLaunchOnly(userAsked: true);
			else ForceToForeground();
			return;
		}

		string? game = LaunchShortcut.LaunchRequested(message);
		if (game == null || GameProfiles.Find(game) == null) return;

		// The shortcut pressed again while its game is still running. Starting a second copy helps nobody, and the
		// first press may simply have seemed not to work.
		GameProfile profile = GameProfiles.Require(game);
		if (_settings.ActiveGame == game && !string.IsNullOrEmpty(profile.GameExeName) && IsGameProcessRunning(profile.GameExeName))
		{
			Speak(Loc.T("shortcut.alreadyRunning", GameProfiles.DisplayNameFor(game)));
			return;
		}

		if (_settings.ActiveGame != game)
		{
			// Another game is open. Switching is what F5 on that game would need first, and it is done in view: the
			// switch says what it is doing, and the session it leaves is one the user may have been in the middle of.
			if (_launchHidden) RevealFromLaunchOnly(userAsked: false);
			if (OverlayIsOpen) { Speak(Loc.T("shortcut.busy", GameProfiles.DisplayNameFor(game))); return; }
			SwitchActiveGame(game);
			if (_settings.ActiveGame != game) return;   // refused, and the reason has been given
		}

		Fire(LaunchOnceSettledAsync(game), "LaunchOnceSettledAsync");
	}

	/// <summary>
	/// A shortcut for a game the manager can't find any more, said once the window is up. Called at the end of the startup
	/// opening.
	/// </summary>
	private void TellAboutMissingShortcutGame()
	{
		if (_launchMissingGame is not { } game) return;
		_launchMissingGame = null;
		string name = GameProfiles.Find(game) != null ? GameProfiles.DisplayNameFor(game) : game;
		SpeakBox(Loc.T("shortcut.gameMissingBox", name), Loc.T("shortcut.gameMissingTitle"));
	}

	// -------------------------------------------------------------------------------------------------------------------
	// Making the shortcut
	// -------------------------------------------------------------------------------------------------------------------

	/// <summary>Where the open game's shortcut goes on the desktop, or null with no game open.</summary>
	private string? DesktopShortcutPath()
	{
		string game = _settings.ActiveGame;
		if (game == GameProfiles.NoGame || GameProfiles.Find(game) == null) return null;
		try
		{
			string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
			return desktop.Length == 0 ? null : Path.Combine(desktop, LaunchShortcut.FileNameFor(Loc.T("shortcut.fileName", ShortcutGameName(game))));
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("LaunchShortcut", "working out where the desktop shortcut goes", ex);
			return null;
		}
	}

	/// <summary>True when the open game's shortcut is on the desktop, so the menu offers to remove it instead.</summary>
	private bool DesktopShortcutExists() => DesktopShortcutPath() is { } path && File.Exists(path);

	/// <summary>The Mods menu item: Remove when the shortcut is there (Sean, 2026-11-13), Create when it isn't.</summary>
	private void CreateOrRemoveDesktopShortcut()
	{
		if (DesktopShortcutExists()) RemoveDesktopShortcut();
		else CreateDesktopShortcut();
	}

	/// <summary>
	/// Asks, then takes the open game's shortcut off the desktop. Only the manager's own shortcut, which is the one its name
	/// ends "(Kinetix)": a shortcut made by Steam or by hand is never touched.
	/// </summary>
	private void RemoveDesktopShortcut()
	{
		if (DesktopShortcutPath() is not { } path || !File.Exists(path)) return;
		string fileName = Path.GetFileNameWithoutExtension(path);

		if (SpeakBox(Loc.T("shortcut.removeConfirm", fileName), Loc.T("shortcut.removeTitle"), MessageBoxButtons.YesNo) != DialogResult.Yes)
		{
			Speak(Loc.T("shortcut.kept", fileName));
			return;
		}

		try
		{
			File.Delete(path);
			Speak(Loc.T("shortcut.removed", fileName));
		}
		catch (Exception ex)
		{
			LogFailure("LaunchShortcut", "Could not remove the desktop shortcut", ex);
			_soundEngine.Play("error");
			SpeakBox(Loc.T("shortcut.removeFailedBox", fileName, FriendlyError(ex)), Loc.T("shortcut.removeFailedTitle"));
		}
	}

	/// <summary>
	/// The Mods menu's "Create Desktop Shortcut": puts a shortcut on the desktop that starts the open game, or modpack,
	/// through the manager without showing it.
	/// </summary>
	private void CreateDesktopShortcut()
	{
		string game = _settings.ActiveGame;
		if (DesktopShortcutPath() is not { } path)
		{
			Speak(Loc.T("shortcut.noGame"));
			return;
		}

		string name = ShortcutGameName(game);
		string fileName = Path.GetFileNameWithoutExtension(path);

		// Asked, as removing it is (Sean, 2026-11-13): the two halves of one menu item behave alike.
		if (SpeakBox(Loc.T("shortcut.createConfirm", fileName, name), Loc.T("shortcut.createTitle"), MessageBoxButtons.YesNo) != DialogResult.Yes)
		{
			Speak(Loc.T("shortcut.notCreated"));
			return;
		}

		try
		{
			bool existed = File.Exists(path);

			string manager = Environment.ProcessPath ?? Application.ExecutablePath;
			WriteShortcut(path,
				target: manager,
				arguments: LaunchShortcut.ArgumentsFor(game),
				workingDirectory: Path.GetDirectoryName(manager) ?? "",
				description: Loc.T("shortcut.description", name),
				icon: ShortcutIcon(game, manager));

			Speak(Loc.T(existed ? "shortcut.replaced" : "shortcut.created", fileName, name));
		}
		catch (Exception ex)
		{
			LogFailure("LaunchShortcut", "Could not create the desktop shortcut", ex);
			_soundEngine.Play("error");
			SpeakBox(Loc.T("shortcut.failedBox", name, FriendlyError(ex)), Loc.T("shortcut.failedTitle"));
		}
	}

	/// <summary>The Mods menu item, named for the open game or modpack: Remove once its shortcut is on the desktop.</summary>
	private string ShortcutMenuText()
	{
		string game = _settings.ActiveGame;
		if (game == GameProfiles.NoGame || GameProfiles.Find(game) == null) return Loc.T("menu.createShortcutNoGame");
		return Loc.T(DesktopShortcutExists() ? "menu.removeShortcut" : "menu.createShortcut", ShortcutGameName(game));
	}

	/// <summary>
	/// What the shortcut is called: the game, with the store when there are two copies, or the modpack's own name. Not
	/// <see cref="GameDisplayName()"/>, which carries the game's build: a shortcut called after a build is wrong the day
	/// the game updates.
	/// </summary>
	private string ShortcutGameName(string game)
	{
		if (MinecraftModpacks.ForKey(game, MinecraftModpacks.PacksFolder) is { } pack) return pack.Name;

		string name = GameProfiles.DisplayNameFor(game);
		if (_settings.HasMultipleCopies(game))
			name += " (" + GameProfiles.PlatformDisplayName(_settings.InstallFor(game)?.Platform ?? GameProfiles.PlatformOf(game)) + ")";
		return name;
	}

	/// <summary>The game's own icon where there is an executable to take it from, so the shortcut looks like the game.</summary>
	private string ShortcutIcon(string game, string manager)
	{
		try
		{
			if (GameProfiles.Find(game) is { } profile && !string.IsNullOrEmpty(profile.GameExeName))
			{
				string exe = profile.GameExePathIn(_settings.GamePathOf(game));
				if (File.Exists(exe)) return exe;
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("LaunchShortcut", "finding the game's icon", ex); }
		return manager;
	}

	/// <summary>Writes a Windows shortcut (.lnk) through the shell's own interface, which needs no scripting host.</summary>
	private static void WriteShortcut(string path, string target, string arguments, string workingDirectory, string description, string icon)
	{
		var link = (IShellLinkW)new ShellLink();
		try
		{
			link.SetPath(target);
			link.SetArguments(arguments);
			link.SetWorkingDirectory(workingDirectory);
			link.SetDescription(description);
			link.SetIconLocation(icon, 0);
			((IPersistFile)link).Save(path, fRemember: true);
		}
		finally
		{
			Marshal.FinalReleaseComObject(link);
		}
	}

	[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
	private class ShellLink { }

	[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
	private interface IShellLinkW
	{
		void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
		void GetIDList(out IntPtr ppidl);
		void SetIDList(IntPtr pidl);
		void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
		void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
		void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
		void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
		void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
		void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
		void GetHotkey(out short pwHotkey);
		void SetHotkey(short wHotkey);
		void GetShowCmd(out int piShowCmd);
		void SetShowCmd(int iShowCmd);
		void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
		void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
		void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
		void Resolve(IntPtr hwnd, uint fFlags);
		void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
	}
}
