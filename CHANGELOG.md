# Version 1.4.1

A round of fixes and quality-of-life additions on top of 1.4.0: you can now uninstall the script extender and get warned when it no longer matches your game, mod searches can be remembered and re-run, and several rough edges around updating, status messages, and the install prompt have been smoothed out.

---

## ✨ New in Version 1.4.1

### 🧩 Script extender management (Skyrim & Fallout 4)
*   **Uninstall the script extender**: SKSE and F4SE install into the game folder rather than as normal mods, so there was no way to remove them. A new **Mods → "Uninstall Script Extender (SKSE/F4SE)"** command cleanly removes the files it installed (after a confirmation).
*   **Version-mismatch warning**: SKSE/F4SE only load when built for your exact game version, so after a game update they silently stop working — which is the usual reason the **Mod Configuration Menu** disappears. The manager now checks this before launching and warns you (and tells you to reinstall it to match), instead of leaving you to wonder why your mods went quiet.
*   **Links go to Nexus**: Opening the SKSE/F4SE entry in the Accessibility Suite now goes to its Nexus page.

### 🔍 Search history
*   **Remember and re-run your searches**: Turn on **"Save Mod Search History"** in **Settings** and the manager keeps the mod searches you run. Open them with the new **History** button on the Search for Mods tab or **Ctrl + Shift + H**: choose "All searches" or a specific date, then press **Enter** on any term to run it again. History is kept **per game**, and you can clear it at any time. It's **off by default**.

### 🛠️ Improvements & fixes
*   **SMAPI download shows progress**: Installing SMAPI now reports its download progress like other downloads.
*   **Stale status messages cleared**: The window title now returns to your Nexus connection (or "Ready") after an operation finishes, so it no longer keeps showing "Installing…" or "Rebuilding deployment" long after it's done.
*   **Disabled mods stay disabled after updating**: Updating a mod you'd turned off no longer silently turns it back on.
*   **The install prompt reliably comes to the front**: After a Mod Manager Download, the "Install now?" question now takes focus on its own, instead of staying hidden behind the browser on some computers (you no longer have to Alt+Tab to find it).
*   **"List is empty" is announced**: When you clear the last available update, the Updates list now says it's empty right away instead of waiting for you to move focus away and back.
*   **Suite tidy-up**: Removed the standalone "SkyrimAccessibility" entry from the Skyrim suite — it's now part of Skyrim Access.
*   **Manual updated** to cover the FOMOD installer, the controls viewer, the navigable manual and change log, search history, and the script-extender tools.

---

# Version 1.4.0

A big step for accessible mod installing: configurable "FOMOD" mods now open a fully keyboard-driven, spoken installer wizard instead of failing, and the controls window (Ctrl+H) has been rebuilt into an easy drill-down list that reads each mod's real, documented keybinds — including Fallout 4 and Skyrim mods that set their keys through MCM. The manual and change log are now navigable in the same way, plus a handful of fixes.

---

## ✨ New in Version 1.4.0

### 🧩 Accessible FOMOD installer (Skyrim & Fallout 4)
*   **Configurable mods now install correctly**: Mods that ship a guided "FOMOD" installer — such as Immersive Sounds Compendium — used to install wrong because their option menus couldn't be read. They now open a fully keyboard-driven, screen-reader-friendly wizard: each option's description is spoken as you move through it, the step and your progress ("Step 2 of 6") are announced, checkboxes and radio buttons read their checked state, and your picks install exactly as a sighted user's would.
*   **Smart options are handled properly**: Options the mod marks as required, recommended, or not-usable are read out with their status and can't be toggled into an invalid state, and any conditional files are installed only when their conditions are met.

### ⌨️ Reworked controls viewer (Ctrl+H)
*   **A clear drill-down list**: The accessibility-controls window is now a single navigable list. Use **Up/Down** to move, **Right or Enter** to open a group, and **Left or Backspace** to go back. Press **Ctrl+E** to edit a mod's configuration when one is available, and **Shift+F1** for help.
*   **Controls come from each mod, so they're always current**: Instead of a hardcoded list that could go stale, the manager reads the controls each mod actually documents — its README or guide, or its config — captured when the mod is installed.
*   **MCM keybinds for Fallout 4 and Skyrim**: Mods that set their keys through MCM (such as Fallout 4 Access and Extended Dialogue Interface) now have those keybinds read straight from MCM, showing the real key each action is bound to — including any you've changed in-game.

### 📖 Navigable manual and change log
*   **Open and read by section**: The User Manual (**F1**) and Change Log (**F2**) now use the same drill-down — a list of sections on the left that you open to read the text on the right. In the change log, each version opens to its own list of changes, so you can jump straight to what's new in a release.

### 🛠️ Other improvements & fixes
*   **"100 results" now really loads 100**: Choosing 100 results per load in mod search returned only 80, because Nexus limits each request to 80. The manager now fetches the rest automatically so you get the full amount you asked for.
*   **Installs no longer blocked by a full system drive**: Mods now extract on the same drive as your mods folder, so a full C: drive won't stop an install when your mods live on another drive.
*   **Steadier list navigation**: In the controls, manual, and change-log lists, the Left and Right arrows only move between levels now — they no longer occasionally move the selection the way Up and Down do.

---

# Version 1.3.0

A major update for Skyrim Special Edition and Fallout 4 modders: full load-order management with import and export, a Creations manager, one-click importing from Mod Organizer 2, and an in-app game-log viewer — alongside `.rar` support, a "results per load" control for mod searches, an in-app Change Log you can open with F2, and a range of accessibility refinements. The manual has been fully updated to cover everything.

---

## ✨ New in Version 1.3.0

### 🎮 Load order management (Skyrim & Fallout 4)
*   **Mod Priority tab**: Decide which mod wins when two of them change the same file. Reorder with **Ctrl+Up / Ctrl+Down**, and each mod reads how many files it overrides or is overridden in, so you can judge its standing by ear.
*   **Plugin Order tab**: View and reorder your active plugins (the `plugins.txt` load order). Masters load first automatically, and **F8 auto-sorts** the whole order so every plugin loads after the masters it needs — using LOOT's community rules when they're available.
*   **Export and Import Load Order**: From the **Mods** menu, save your mod priority and plugin order to a file and restore it later — handy as a backup or to move a setup between computers.

### 📥 Import from Mod Organizer 2 (Skyrim & Fallout 4)
*   **Bring your MO2 setup across**: From the **Mods** menu choose "Import from Mod Organizer 2", pick your MO2 folder and profile, and the manager copies your mods, applies their priority and plugin order, and activates the same plugins — all without changing your MO2 setup.
*   **Nexus IDs filled in automatically**: MO2 doesn't store them, so after importing the manager matches your mods to their Nexus pages by name, which makes update checks and "open mod page" work for them.

### 🧩 Creations manager (Skyrim & Fallout 4)
*   **A new Creations tab** lists the Bethesda Creations installed in your game, whether each one is active, and whether it's a master or light master. Press **Space** to activate or deactivate the selected Creation. (Creations are still downloaded inside the game, from its Creations menu — no mod manager downloads them.)

### 🪵 In-app game logs (Skyrim & Fallout 4)
*   **A new Log tab** — "Skyrim Logs" or "Fallout 4 Logs" — lets you read the logs your script extender and its plugins write, including `f4se.log` / `skse64.log`, the per-mod logs, crash logs, and your accessibility mod's log. Choose a log from the dropdown, filter to "Errors and Warnings", search it, and press **Ctrl+Shift+R** to refresh it live — even while the game is running. This is where you'll spot a plugin that stopped loading after a game update.

### 🌐 Translatable interface
*   **The whole app is now translatable**: Every piece of text the program shows or speaks lives in language files, and a **Language** option has been added to Settings, so the manager is ready for translators to add new languages without any code changes. *Only English is included for now* — other languages will be added over time as translations are completed, so you won't see new language choices until those translations exist.

### 🔍 Finding mods
*   **Choose how many results load at a time**: A new **Results** dropdown on the renamed **Search for Mods** tab (10, 20, 30, 50, or 100). Set a default that sticks across sessions in **Settings**.
*   **"Load more results" is now part of the list**: Instead of tabbing to a separate button, a row at the bottom of the results loads the next batch when you press **Enter**, then drops you on the first newly loaded result. It isn't counted in the "X of Y" position announcements.

### 🛠️ Other improvements & fixes
*   **Install `.rar` and `.7z` mods**: The installer now extracts `.rar` and `.7z` archives, not just `.zip`.
*   **In-app Change Log**: Press **F2** (or Help → View Change Log) to read what's new, in the same navigable window as the manual.
*   **The log menu now matches the game**: The View menu's log option opens the right log for the active game — the SMAPI log for Stardew Valley, or the script-extender log for Skyrim and Fallout 4 — instead of always offering the SMAPI log.
*   **Sound Demo**: The first sound is now selected when the demo opens, and each sound's name is no longer spoken twice.
*   **Settings focus**: The Settings window now lands on the first field instead of the Save button.
*   **Clearer dropdown announcements**: Tabbing onto a log or filter dropdown now tells you how many lines it shows, and the name is spoken before the count — on the SMAPI log filter as well.
*   **Fallout 4 suite & F4SE installer**: The Address Library is now part of the Fallout 4 accessibility suite, and the built-in F4SE installer fetches the current build from Nexus (Silverlock no longer hosts the up-to-date version).
*   **Fully updated manual** covering all of the above.

---

# Version 1.2.5

A big update: a new Mod Wikis browser with per-wiki search, a language filter for finding mods, an easier way to install the accessibility suite one mod at a time, an About dialog, support for installing on Windows 11 on ARM, and a fully refreshed manual — alongside fixes for the SMAPI log viewer and mod-group announcements.

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

### 📚 Mod Wikis browser
*   **New "Mod Wikis" dropdown on the Wiki tab**: Choose which wiki you want to use. As well as the main game wiki, it lists dedicated wikis for popular content mods — world-expansion wikis for Stardew Valley (Stardew Valley Expanded, Ridgeside Village, East Scarp, Sunberry Village, and more) and large quest / new-land mod wikis for Skyrim and Fallout 4 (the Elder Scrolls Mods Wiki, Legacy of the Dragonborn, Enderal, Sim Settlements 2, and others).
*   **Search and categories follow the wiki you pick**: Selecting a wiki points the Search box, the Categories list, and the page view at that wiki. Categories are now pulled live from the chosen wiki, so they always match what you're browsing (and on multi-game wikis they're scoped to the active game).
*   **Browse-only wikis are handled gracefully**: A few mod wikis can't be searched from inside the app; selecting one opens it in the view and the manager tells you it's browse-only.

### 🌐 Language filter for finding mods
*   **Filter mod searches by language**: The Find New Mods tab has a new **Language** dropdown. It defaults to English and remembers your choice. The list shows the languages that actually have mods for your current game, with a count for each, and a "Any language" option turns the filter off. It applies to keyword searches and the Trending, Most Popular, and Recent lists.

### ♿ Easier accessibility-suite installs
*   **Install suite mods one at a time, at your own pace**: In the Accessibility Suite Installer you can now select any mod in the list and press **Enter** to open just that mod's download page (the Files tab on Nexus, or the official site / GitHub releases for others). This is a calmer alternative to the "Install Missing Suite Mods" button when several mods are needed and you'd rather handle them one by one.

### ℹ️ About dialog
*   **New "About Kinetix Mod Manager" in the Help menu**: A standard About dialog showing the program description, version, publisher, website, and licensing — read aloud and fully keyboard accessible.

### 🎮 Polish
*   **Games are now listed alphabetically** in the Games menu and on the game-selection screen.

### 📖 A fully refreshed manual
*   **Clearer Nexus API key instructions**: Rewritten as step-by-step stages that explain what an API key is, give direct web links so you don't have to hunt through menus or on-screen pictures, point out the "Copy API Key" button, and include a troubleshooting section.
*   **Now covers all three games**: The manual has been generalized beyond Stardew Valley to describe Stardew Valley, Skyrim Special Edition, and Fallout 4 throughout, with game-specific features clearly called out.
*   **Both ways to download from Nexus are explained**: A new section spells out that you can use either the **Mod Manager Download** button (the manager installs it for you) or **Manual Download** + Ctrl+I, and walks through the "Slow Download" button and the required-mods page step by step.
*   **No more empty help topics**: The "Keyboard Shortcuts" and "Advanced Features" topics now open with an overview of what's in them instead of appearing blank.

### 🐛 Fixes
*   **Focus returns to the manager after a "Mod Manager Download"**: When you download a mod with the "Mod Manager Download" button on Nexus, the "Install now?" prompt now reliably comes to the front and takes keyboard and screen-reader focus, instead of sometimes opening behind your browser.

---

# Version 1.2.4

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

# Version 1.2.3

Automates SMAPI for Stardew Valley — installing it, and keeping it up to date — so a new modder never has to use SMAPI's console installer or a download page.

---

## ✨ New in Version 1.2.3

### 🤖 Automatic SMAPI install and update
*   **One-step SMAPI install**: The Accessibility Suite Installer now installs SMAPI for you. It downloads the latest installer straight from SMAPI's official GitHub release and runs it silently against your detected Stardew Valley folder, so you no longer have to drive SMAPI's interactive console installer by hand. You hear spoken progress throughout — "Downloading SMAPI", "Installing SMAPI", and "SMAPI installed successfully".
*   **Update SMAPI in place**: When a mod update check finds a newer SMAPI (Stardew Valley reports this through the smapi.io check added in 1.2.2's groundwork), the manager now offers to download and install the update for you automatically, instead of just opening the download page. Choosing "No" leaves your install untouched.
*   **Safe fallbacks**: If your Stardew Valley folder can't be found, the download fails, or the install can't be confirmed afterwards, the manager says so and opens smapi.io so you always have a way forward.

---

# Version 1.2.2

Adds the ability to edit a mod's config and manifest files directly inside the manager, building on the 1.2.1 fixes.

---

## ✨ New in Version 1.2.2

### 📝 Edit a mod's config and manifest in-program
*   **Edit Config File (Ctrl+E)**: Open the selected mod's `config.json` in the built-in JSON editor to change mod settings without leaving the manager — for example, setting where the Stardew Valley "Skip Intro" mod skips to (such as `Load`). If a mod hasn't generated its config yet (most do so the first time the game runs with the mod enabled), the manager says so instead of failing.
*   **Edit Manifest File (Ctrl+M)**: Open the selected mod's manifest (`manifest.json` for Stardew Valley, or the manager's `.manager_manifest.json` for Skyrim/Fallout 4) to fix details directly — most usefully a version number a mod author forgot to bump, which otherwise keeps the mod flagged for updates. Saving a manifest edit re-scans the installed list so the corrected version takes effect immediately.
*   **Safe, accessible editing**: Both use the same in-program editor as the mod keybind config — Ctrl+S to save, Escape to cancel, JSON validation that refuses to save malformed files, an unsaved-changes prompt, and spoken prompts throughout. Both actions appear in the Mods menu, in the Installed-tab context help (Shift+F1), and in the Shortcut Customization dialog, so the keys can be rebound.

---

# Version 1.2.1

A small bug-fix release addressing two issues found when running the manager on a PC where not every supported game is installed.

---

## 🐛 Bug Fixes in Version 1.2.1

### 🎮 Loading a session for an uninstalled game
*   **No more wrong-game mods**: Loading a game session for a game that isn't installed previously fell back to the Stardew Valley Mods folder, silently showing Stardew's mods under the wrong game. The manager now detects this, announces that the session is for a game that hasn't been installed, and asks whether you'd like to purchase it.
*   **Guided purchase flow**: Answering "Yes" lets you choose **Steam** or **GOG**, then opens that store's page for the game in your default browser. At startup, a saved-but-uninstalled active game now returns you to the game-selection screen instead of loading another game's mods.

### 🔄 Duplicate update-check results
*   **One completion, accurate count**: Checking for mod updates while a check was already running (for example, the automatic startup check) could replay the "update check complete" sound repeatedly and report an inflated count full of duplicate entries. Update checks are now single-batch: a second check is held off until the first finishes, so the completion cue plays once and the count is correct.

---

# Version 1.2.0

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

# Version 1.1.0 (Major Release)

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
