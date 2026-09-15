# Kinetix Mod Manager - Project Objectives

This file tracks the "Pro-Level" features being integrated into the manager to make it the definitive tool for
accessible modding across **Stardew Valley**, **Skyrim Special Edition**, **Fallout 4**, **Moonlight Peaks**,
**The Witcher 3: Wild Hunt** and **Minecraft: Java Edition**.

For the engineering picture — what has moved where, and why — see `ARCHITECTURE_REVIEW.md`. For what is
still outstanding, see `TODO.md`. This file is the feature list.

## ✅ Completed Objectives

### 1. Mod Profiles
*   **Description**: Create and switch between mod setups for different playthroughs.
*   **Status**: Completed.

### 2. Integrated SMAPI Log Viewer
*   **Description**: A clean, navigable way to read game errors without the technical noise.
*   **Status**: Completed.

### 3. Mod Description Reader
*   **Description**: A way to hear the full details of what a mod does without using a browser.
*   **Status**: Completed.

### 4. Dependency "Quick-Fix"
*   **Description**: Automated help for missing mod requirements (Hotkey: Ctrl+Q).
*   **Status**: Completed.

### 5. Settings Dashboard
*   **Description**: Manage Mods Path, API keys, Splash Screen, and Sound Volume (Hotkey: Ctrl+P).
*   **Status**: Completed.

### 6. Automated Backup Management
*   **Description**: Automatically prune old backups to save disk space (Hotkey: Ctrl+Shift+D).
*   **Status**: Completed.

### 7. Installed Mods Search
*   **Description**: Real-time filtering of your installed mods list with audio feedback (Hotkey: Ctrl+F).
*   **Status**: Completed.

### 8. SMAPI Log Analysis
*   **Description**: Intelligent error detection with spoken fix suggestions.
*   **Status**: Completed.

### 9. Audio Theme Packs
*   **Description**: Support for multiple audio themes. The active theme follows the loaded game, so a session
    is told apart by ear before anything is read out, with an optional manual override; per-profile themes and
    a Theme Manager are also supported. A theme that has not recorded a particular sound falls back to the
    Default theme for that one sound, so a half-finished pack is quieter rather than silent.
*   **Status**: Completed. Game-driven switching and the manual-override toggle arrived in v1.2.0; a theme
    folder for every supported game, and the per-sound fallback, in the current release.

### 10. Mod Version "Ignore"
*   **Description**: Hide specific mod updates from the list using the Delete key.
*   **Status**: Completed.

### 11. Mod Category Filtering
*   **Description**: Group and filter mods by categories (e.g., Expansion, Crafting, NPC) for better organization.
*   **Features**: Automatic category detection, manual override (Ctrl+J), and real-time category filtering.
*   **Status**: Completed.

### 12. Batch Category Management
*   **Description**: Enable or disable all mods in a specific category at once.
*   **Features**: One-click category toggle (Ctrl+Shift+J) with confirmation and total count audio feedback.
*   **Status**: Completed.

### 13. Shortcut Customization
*   **Description**: Remap any of the application's shortcuts to different keys.
*   **Features**: A Shortcut Manager listing every action and its current key, changes saved per install, and a
    build-time guard so a default can never go missing.
*   **Status**: Completed. (Was a stretch goal; it is not one any more.)

### 14. Minecraft: Java Edition
*   **Description**: The sixth supported game, and the only one the manager launches itself.
*   **Features**: Fabric installed without a separate installer, the game started directly (F5) so the
    launcher cannot silently start an unmodded profile, mods from Modrinth with no account required, and a
    reading of the game's own log to say whether the run that just happened actually had the mods.
*   **Status**: Completed.

### 15. Choosing where your mods come from
*   **Description**: The user decides which sites are searched for mods, rather than the manager deciding by
    game.
*   **Features**: Three ways to search — every source merged, one source, or a preferred source with the rest
    on request (Alt+O) — with the preferred source remembered per game. Results say which site they came from
    when more than one answered, and a site that could not answer says so rather than leaving a silent gap.
    Games with only one searchable catalogue are told so plainly instead of being given a one-item dropdown.
*   **Status**: Completed.

### 16. Install from a GitHub repository
*   **Description**: Install a mod published on GitHub and nowhere else, by naming the repository.
*   **Features**: Accepts `owner/repo` or a pasted address, finds the newest release, and picks the file that
    is actually the mod — a Fabric sources jar looks right, installs cleanly and does nothing.
*   **Status**: Completed.

### 17. Mod source API keys in one place
*   **Description**: One screen for every site that asks for a key.
*   **Features**: Lists which sites want one and whether you have given it; Enter adds a key or shows the
    stored one in a read-only box; Edit replaces a key typed wrongly; Delete forgets one. Keys are stored
    encrypted and are never read aloud as the list is arrowed through.
*   **Status**: Completed.

---

## 🚀 Future Stretch Goals

### 18. CurseForge as a searchable source
*   **Plan**: A second catalogue for both Minecraft and Stardew Valley.
*   **Blocked on**: CurseForge issuing an API key, which they do for approved applications. It is already
    listed in the source chooser and the keys screen, and says why it cannot be used yet.

### 19. Nexus sign-in instead of a pasted key
*   **Plan**: Sign in on Nexus's own site and have the manager given a key, so it never sees a password and
    the key can be revoked on its own.
*   **Blocked on**: Nexus approving the manager as an application. The flow itself is built and tested.

### 20. A Linux version
*   **Plan**: The same manager, native, for the games whose accessibility mods speak on Linux.
*   **Status**: In progress, and past the uncertain part. The platform-independent half is its own project and
    its tests pass off Windows. Every platform seam but the browser has a Linux answer — speech through
    speech-dispatcher, sounds through GStreamer, keys through the system keyring, games through Steam, its
    Proton prefixes and Heroic — and all of it is under test. Settings, the Nexus and Modrinth services and
    every screen's decisions now live in the shared half, so the GTK window has **eleven tabs**: games,
    installed mods, search and install, updates, profiles, dependencies, backups, the loader log, the wiki,
    Check My Setup and settings. All six games can be searched and update-checked from Linux; **only Minecraft
    mods can be installed there so far**, which is the next real piece of work. See
    `ARCHITECTURE_REVIEW.md` §16–§36 and the Linux section of `TODO.md`.
*   **Worth knowing**: Skyrim, Fallout 4, The Witcher 3 and Moonlight Peaks will have their mods managed
    perfectly on Linux and then play silently, because their accessibility mods drive NVDA or JAWS and neither
    exists inside a Proton prefix. Minecraft and Stardew Valley speak natively. Saying so in the interface is
    an outstanding item and the most important one left.
