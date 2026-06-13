# Release Notes & Changelog: Version 1.2.5

Fixes for the SMAPI log viewer and for how mod groups are announced while you navigate, plus support for installing on Windows 11 on ARM.

---

## ✨ New in Version 1.2.5

### 🪵 SMAPI log viewer
*   **The log now loads while the game is running**: Previously, opening the SMAPI Log tab while Stardew Valley was running showed no entries in any filter — even "Full Log" — because the game keeps the log file open and the manager couldn't read it. The manager now reads the live log without disturbing the game, so you can review errors mid-session.
*   **Refresh the log on demand**: Press Ctrl+Shift+R on the SMAPI Log tab to re-read the log at any time, including while the game is running. You'll hear how many entries were found.

### 🔊 Cleaner mod-group announcements
*   **No more stray "first mod" when collapsing a group**: Collapsing a group while focused on one of its mods no longer briefly announces the first mod in the list before landing on the group.
*   **Expanding and collapsing no longer repeats itself**: Each expand or collapse now reads the group line once — stating "Expanded" or "Collapsed" and the position — instead of speaking the same information several times over.

### 💻 ARM Windows support
*   **The installer now runs on Windows 11 on ARM**: Previously the installer refused to run on ARM PCs (such as Snapdragon-based Surface and Copilot+ devices), reporting an incompatible architecture before installing anything. It now installs on any PC that can run 64-bit apps — including ARM machines, where the manager runs through Windows 11's built-in x64 emulation.

---

# Release Notes & Changelog: Version 1.2.4

Overhauls the Stardew Valley SMAPI log viewer so errors are easier to find, understand, and act on — plus a small accessibility-suite cleanup.

---

## ✨ New in Version 1.2.4

### 🪵 A more useful SMAPI log viewer
*   **Errors and Warnings filters actually work now**: The "Errors Only" and "Errors and Warnings" filters previously came back empty because of how SMAPI labels its log lines. They now correctly show error and warning entries.
*   **New "Links Only" filter**: Show just the log lines that contain a link — handy for spotting update notices and mod pages at a glance.
*   **Open links from the log**: Press Enter on a log line that has a link to open it in your browser (Nexus pages open on the Files tab). If a line lists several links — for example a "no longer compatible" notice that points to Nexus, GitHub, and SMAPI.io — a picker appears so you can choose which to open. The line announcement tells you whether Enter opens a page or offers a choice.
*   **Diagnose an error**: With a log line selected, use the Quick-Fix shortcut to get a plain-language explanation of what the line means and how to fix it, with specific guidance for common problems (incompatible mods, missing dependency versions, failed Harmony patches, command-registration errors, and missing object IDs).
*   **Select and copy lines**: Select one or more log lines and press Ctrl+C to copy them to the clipboard, so you can paste them into a forum post or Discord without opening SMAPI-latest.txt by hand.

### 🧹 Accessibility suite cleanup
*   **Removed Accessible Tiles** from the Stardew Valley accessibility suite, since that functionality is now built into Stardew Access.

---

# Release Notes & Changelog: Version 1.2.3

Automates SMAPI for Stardew Valley — installing it, and keeping it up to date — so a new modder never has to use SMAPI's console installer or a download page.

---

## ✨ New in Version 1.2.3

### 🤖 Automatic SMAPI install and update
*   **One-step SMAPI install**: The Accessibility Suite Installer now installs SMAPI for you. It downloads the latest installer straight from SMAPI's official GitHub release and runs it silently against your detected Stardew Valley folder, so you no longer have to drive SMAPI's interactive console installer by hand. You hear spoken progress throughout — "Downloading SMAPI", "Installing SMAPI", and "SMAPI installed successfully".
*   **Update SMAPI in place**: When a mod update check finds a newer SMAPI (Stardew Valley reports this through the smapi.io check added in 1.2.2's groundwork), the manager now offers to download and install the update for you automatically, instead of just opening the download page. Choosing "No" leaves your install untouched.
*   **Safe fallbacks**: If your Stardew Valley folder can't be found, the download fails, or the install can't be confirmed afterwards, the manager says so and opens smapi.io so you always have a way forward.

---

# Release Notes & Changelog: Version 1.2.2

Adds the ability to edit a mod's config and manifest files directly inside the manager, building on the 1.2.1 fixes.

---

## ✨ New in Version 1.2.2

### 📝 Edit a mod's config and manifest in-program
*   **Edit Config File (Ctrl+E)**: Open the selected mod's `config.json` in the built-in JSON editor to change mod settings without leaving the manager — for example, setting where the Stardew Valley "Skip Intro" mod skips to (such as `Load`). If a mod hasn't generated its config yet (most do so the first time the game runs with the mod enabled), the manager says so instead of failing.
*   **Edit Manifest File (Ctrl+M)**: Open the selected mod's manifest (`manifest.json` for Stardew Valley, or the manager's `.manager_manifest.json` for Skyrim/Fallout 4) to fix details directly — most usefully a version number a mod author forgot to bump, which otherwise keeps the mod flagged for updates. Saving a manifest edit re-scans the installed list so the corrected version takes effect immediately.
*   **Safe, accessible editing**: Both use the same in-program editor as the mod keybind config — Ctrl+S to save, Escape to cancel, JSON validation that refuses to save malformed files, an unsaved-changes prompt, and spoken prompts throughout. Both actions appear in the Mods menu, in the Installed-tab context help (Shift+F1), and in the Shortcut Customization dialog, so the keys can be rebound.

---

# Release Notes & Changelog: Version 1.2.1

A small bug-fix release addressing two issues found when running the manager on a PC where not every supported game is installed.

---

## 🐛 Bug Fixes in Version 1.2.1

### 🎮 Loading a session for an uninstalled game
*   **No more wrong-game mods**: Loading a game session for a game that isn't installed previously fell back to the Stardew Valley Mods folder, silently showing Stardew's mods under the wrong game. The manager now detects this, announces that the session is for a game that hasn't been installed, and asks whether you'd like to purchase it.
*   **Guided purchase flow**: Answering "Yes" lets you choose **Steam** or **GOG**, then opens that store's page for the game in your default browser. At startup, a saved-but-uninstalled active game now returns you to the game-selection screen instead of loading another game's mods.

### 🔄 Duplicate update-check results
*   **One completion, accurate count**: Checking for mod updates while a check was already running (for example, the automatic startup check) could replay the "update check complete" sound repeatedly and report an inflated count full of duplicate entries. Update checks are now single-batch: a second check is held off until the first finishes, so the completion cue plays once and the count is correct.

---

# Release Notes & Changelog: Version 1.2.0

This release builds on the 1.1.0 multi-game foundation with a game-aware audio theme system, broad accessibility and keyboard-focus fixes, and a smoother Skyrim Engine Fixes install.

---

## 🚀 What's New in Version 1.2.0

### 🎵 Game-Aware Audio Themes
*   **Themes follow the loaded game**: The sound theme now switches automatically to match the active game (Stardew Valley, Skyrim, or Fallout 4), falling back to the Default theme when no game is loaded.
*   **Manual override**: A new "Set theme manually" checkbox in Settings lets you pick a specific theme that persists across game switches and restarts; it announces its state when toggled.
*   **New themes**: Added complete Skyrim and Fallout 4 sound themes (the manager falls back to Default for any sound a theme does not provide).

### ♿ Accessibility & Keyboard Focus
*   **F6 focus cycle**: Fixed the Wiki and Walkthrough tabs so F6 correctly cycles results list → web view → tab headers and can move *into* the web view.
*   **Cleaner tab order**: Removed the split-view divider from the keyboard tab order, so Tab moves straight from the results list to the web view (no more stray "pane").
*   **List position announcements**: Every list now speaks its "X of Y" position whenever it receives focus — via F6, Tab, mouse click, returning from a dialog, or exiting the Alt menu — matching what you hear when arrowing.

### 🛡️ Nexus Session Handling
*   **Disconnect on session close**: Closing the current game session now properly disconnects from Nexus (with the matching theme's disconnect cue), and exiting the program only plays a disconnect when a game is still loaded.

### 📦 Skyrim Engine Fixes Install
*   **One-step SSE Engine Fixes**: Merged the two-part listing into a single suite entry that automatically installs the Part 2 preloader (`d3dx9_42.dll`) into the game root for premium accounts, with corrected manual instructions otherwise.

### 📚 More Walkthroughs
*   Added three additional verified walkthrough links for each supported game.

### 🧹 Build Hygiene
*   Removed an invalid managed reference to the native `Tolk.dll`, demoted a benign WebView2 `WindowsBase` conflict to a message, and fixed nullable-reference warnings (the project now builds with zero warnings).

---

# Release Notes & Changelog: Version 1.1.0 (Major Release)

This release marks a major milestone, transforming the application from a Stardew Valley-specific manager into a multi-game accessible modding suite renamed **Kinetix Mod Manager**, while adding critical performance, stability, and installation fixes.

---

## 🚀 What's New in Version 1.1.0

### 🎮 Rebranding & Multi-Game Support
*   **Renamed to Kinetix Mod Manager**: The application has been fully renamed and rebranded to support a broader range of games.
*   **New Game Support**: Added full support for three titles:
    *   *Stardew Valley*
    *   *The Elder Scrolls V: Skyrim Special Edition*
    *   *Fallout 4*
*   **Dynamic Registry-Based Detection**: The manager now queries Windows Registry paths to automatically detect Steam and GOG installations for all three games. It hides unsupported games from the menu by default so you only see what is installed.
*   **Interactive Store Purchase Guide**: On initial launch, if no supported games are detected, a voice-guided wizard helps direct users to Steam and GOG stores to purchase or configure games.

### ⚡ NXM Download & Performance Improvements
*   **Resolved Large Mod Timeout/Hangs**: Removed redundant API queries (such as querying `files.json`) when resolving `nxm://` protocol download links, which previously caused timeouts on large mods.
*   **Stream-Based Downloads**: Replaced high-memory array downloads with memory-efficient stream buffers (`DownloadFileWithProgressAsync`), preventing system slowdowns when installing very large mods.
*   **Accessible Download Progress Speeches**: Wired up real-time progress updates. The active download percentage is displayed in the window title, and the manager calls your screen reader to **speak out milestones every 10%** (e.g., "Downloading... 10%", "20%", etc.), ensuring you are always informed of download status.

### 🛡️ AppData Directory Migration & Settings
*   **UserData Migration**: Moved all application-written directories (downloads, backups, profiles, and logs) from the application execution directory to the user's Local AppData folder (`%APPDATA%\AudiVentureGames\KinetixModManager`). On startup, the manager automatically migrates all files from the old `StardewAccessibleManager` directory to ensure no settings, profiles, or backups are lost.
*   **WebView2 Crash Fix**: Configured the internal WebView2 browser (used for the Wiki) to use a persistent user data folder under AppData, resolving startup crashes.
*   **Single-Instance Pipe Rename**: Upgraded single-instance IPC handlers (mutex and named pipes) to prevent instance conflicts when handling browser links.

### 📦 Installer & Dependency Fixes
*   **Bundled Native DLLs**: Corrected the Inno Setup configuration (`setup.iss`) to include all required native screen reader libraries (`Tolk.dll`, `nvdaControllerClient.dll`, and `nvdaControllerClient64.dll`) in the installer. This fixes issues where screen reader integration did not work out-of-the-box upon installation.
*   **Path Correction**: Standardized installer script file sources to point to the correct 64-bit release build folder (`win-x64\publish`).

### ♿ Web View Keyboard & Focus Fixes
*   **F6 Now Exits the Web View**: In the Wiki and Walkthrough tabs, pressing **F6** inside the embedded web page now reliably moves focus back out to the tab headers. Previously F6 could move *into* the web view but never out of it (the content runs in a separate browser process that the app couldn't intercept).
*   **Ctrl+Home / Ctrl+End Fix Tab Order**: Jumping to the top or bottom of a page now also repositions keyboard focus there, so a subsequent **Tab** / **Shift+Tab** continues from the right place instead of where you previously were.
*   **Predictable Page Edges**: **Shift+Tab** at the top of a page returns to the results/guides list, and **Tab** at the bottom moves to the tab headers, instead of Chromium wrapping focus around to the other end of the page.

### 🛡️ Stability & Reliability
*   **Global Error Handling**: Unexpected errors are now caught, logged to a crash log, and shown in an accessible dialog instead of silently crashing the app.
*   **Async Hardening**: Update routines no longer risk an unhandled crash mid-run, and a Web View initialization race that could occasionally leave a page blank has been fixed.
*   **Better Diagnostics**: Settings, profile-load, mod-ID-map, folder-migration, and SMAPI-log failures are now recorded in `mod_manager_log.txt` instead of failing silently.

### 🔧 Under the Hood
*   **Codebase Refactor**: The main form was split from a single ~6,800-line file into 18 focused modules (by feature area) with no change in behavior, making future maintenance and fixes much easier.

---

## 📜 Previous Versions Recap

### Version 1.0.1
*   Fixed Nexus Mods discovery and API querying.
*   Improved settings load/save reliability with secure Windows DPAPI key encryption.
*   Introduced initial SMAPI log analyzer rules.
*   Enhanced keyboard focus behavior between list views and menus.
*   Fixed layout constraints in the main screen and splash screen windows.

### Version 1.0.0 (Initial Release)
*   First public release of the Stardew Valley Accessible Mod Manager.
*   Keyboard-only and screen reader integration (NVDA, JAWS, and SAPI) via Tolk.
*   Installed mod browser, search, profile manager, and backup zip engine.
*   Built-in Wiki viewer.
