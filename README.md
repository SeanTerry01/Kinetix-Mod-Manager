# Kinetix Mod Manager

A fully keyboard-driven, screen-reader-compatible mod manager for **Stardew Valley**, **Skyrim Special Edition**, and **Fallout 4**, built for the accessibility community. Designed to work with NVDA, JAWS, and SAPI-based readers out of the box via [Tolk](https://github.com/dkager/tolk).

---

## Features

- **Installed Mods** — browse, enable/disable, delete, and search your mod list with real-time audio feedback
- **Update Checking** — detects available updates via the Nexus Mods API; supports one-click "Update All" for Premium members
- **Mod Discovery** — search Nexus by keyword or browse trending/popular/recent mods without leaving the app
- **Mod Profiles** — save and restore different enabled/disabled mod configurations for different playthroughs
- **Automatic Backups** — zips the current mod folder before every update or deletion; configurable retention limit
- **Dependency Viewer** — shows required and optional dependencies for the selected mod, flagging missing or outdated ones
- **SMAPI Log Viewer** — parses your latest SMAPI log, filters by level, and suggests fixes for common errors (Stardew Valley)
- **Integrated Game Wiki** — built-in, screen-reader-friendly wiki browser (Stardew Valley Wiki, UESP for Skyrim, Fallout Wiki) with category drilling
- **Walkthroughs** — read community walkthroughs and guides for the active game inside the app
- **Audio Themes** — all feedback sounds are `.ogg` files organized into swappable theme packs
- **NXM Protocol** — registers as an `nxm://` handler so "Mod Manager Download" buttons on Nexus open the app directly
- **Secure API Key Storage** — Nexus API key is stored encrypted using Windows DPAPI (never plain text on disk)

---

## Supported Games

| Game | Mod Loader | Wiki Source |
|---|---|---|
| Stardew Valley | SMAPI | stardewvalleywiki.com |
| Skyrim Special Edition | SKSE64 | en.uesp.net |
| Fallout 4 | F4SE | fallout.fandom.com |

Switch the active game from the **Games** menu.

---

## Requirements

- Windows 10 or 11 (64-bit)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- A free [Nexus Mods account](https://www.nexusmods.com) with a Personal API Key
- The target game installed (point the Mods Path to wherever your `Mods` / game data folder lives)

---

## Getting Started

1. Download the latest `KinetixModManager_Setup.exe` from the [Releases](https://github.com/SeanTerry01/Kinetix-Mod-Manager/releases) page and run the installer.
2. Launch `KinetixModManager.exe`. On first launch, the Settings dialog opens automatically.
3. Choose your active game from the **Games** menu, then confirm or browse to that game's `Mods` folder.
4. Paste your Nexus Mods API key (Settings → Nexus API Key field).
5. Press **Save Settings**. The app connects to Nexus and loads your mod list.

Press **F1** at any time to open the full User Manual, or **Shift + F1** for context-sensitive shortcuts on the active tab.

---

## Key Shortcuts

| Action | Shortcut |
|---|---|
| Open Manual | F1 |
| Context Help (tab shortcuts) | Shift + F1 |
| Launch game (via SMAPI for Stardew) | F5 |
| Cycle focus (tabs ↔ list ↔ web view) | F6 |
| Settings | Ctrl + P |
| Install from .zip | Ctrl + I |
| Search installed mods | Ctrl + F |
| Check/update all mods | Ctrl + U |
| Save profile | Ctrl + S |
| View dependencies | Ctrl + Y |
| Quick-fix missing dep | Ctrl + Q |
| Prune old backups | Ctrl + Shift + B |

See `MANUAL.md` for the complete shortcut reference.

---

## Project Structure

| File(s) | Purpose |
|---|---|
| `Form1.cs` + `Form1.*.cs` | UI and orchestration, split into partial-class files by concern (Wiki, Updates, Settings, Install, Profiles, etc.) |
| `AppSettings.cs` | Settings load/save with DPAPI key encryption |
| `NexusService.cs` | All Nexus and GitHub API communication |
| `ModFileSystem.cs` | Mod scanning, backup management, zip installation |
| `SoundEngine.cs` | Audio playback via NAudio + NVorbis |
| `LogAnalyzer.cs` | SMAPI log parsing and fix-rule engine |

---

## License

This project is provided as-is for personal and community use.
