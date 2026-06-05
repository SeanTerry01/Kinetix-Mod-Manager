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
