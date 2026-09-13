# Kinetix Mod Manager

A fully keyboard-driven, screen-reader-compatible mod manager for **Stardew Valley**, **Skyrim Special Edition**, **Fallout 4**, **Moonlight Peaks**, **The Witcher 3: Wild Hunt** and **Minecraft: Java Edition**, built for the accessibility community. Designed to work with NVDA, JAWS, and SAPI-based readers out of the box via [Tolk](https://github.com/dkager/tolk).

---

## Features

- **Installed Mods** — browse, enable/disable, delete, and search your mod list with real-time audio feedback
- **Update Checking** — detects available updates via the Nexus Mods API, or via Modrinth for Minecraft; supports one-click "Update All" for Premium members
- **Mod Discovery** — search Nexus by keyword or browse trending/popular/recent mods without leaving the app; Minecraft searches Modrinth instead, and needs no API key
- **Mod Profiles** — save and restore different enabled/disabled mod configurations for different playthroughs
- **Automatic Backups** — zips the current mod folder before every update or deletion; configurable retention limit
- **Dependency Viewer** — shows required and optional dependencies for the selected mod, flagging missing or outdated ones
- **SMAPI Log Viewer** — parses your latest SMAPI log, filters by level, and suggests fixes for common errors (Stardew Valley)
- **Integrated Game Wiki** — built-in, screen-reader-friendly wiki browser (Stardew Valley Wiki, UESP for Skyrim, Fallout Wiki, Minecraft Wiki) with category drilling
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
| Moonlight Peaks | BepInEx | moonlightpeaks.wiki.gg |
| The Witcher 3: Wild Hunt | `mods` folder (no loader) | witcher.fandom.com |
| Minecraft: Java Edition | Fabric (installed by the manager, no separate installer) | minecraft.wiki |

If you own the same game on both Steam and GOG, both copies appear in the menu and each keeps its own mods.

Minecraft is the exception to the rule that a game needs its own launcher: press **F5** and the manager starts
the game itself, with Fabric and your mods loaded, without going near the Minecraft launcher. You still sign in
through the launcher once.

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
| Launch game (via SMAPI for Stardew; via Fabric, without the launcher, for Minecraft) | F5 |
| Cycle focus (tabs ↔ list ↔ web view) | F6 |
| Settings | Ctrl + P |
| Install from .zip | Ctrl + I |
| Search installed mods | Ctrl + F |
| Check/update all mods | Ctrl + U |
| Save profile | Ctrl + S |
| View dependencies | Ctrl + Y |
| Quick-fix missing dep | Ctrl + Q |
| Change a mod's settings | Ctrl + E |
| Edit a mod's config file directly | Ctrl + Shift + M |
| Edit a mod's manifest | Ctrl + M |
| Check my setup | Ctrl + Shift + K |
| Delete old backups | Ctrl + Shift + D |

See `MANUAL.md` for the complete shortcut reference.

---

## Project Structure

The solution is three projects.

| Project | Target | Purpose |
|---|---|---|
| `Kinetix.Core` | `net10.0` | The rules, the parsers and the file and HTTP work, with no user interface. Game profiles, mod scanning rules, FOMOD, Modrinth, the Minecraft launcher and Fabric installer, save and INI readers, the localisation catalogue, and the domain models under `Models/`. |
| `KinetixModManager` | `net10.0-windows` | The WinForms application: the screen it draws, the keys it listens for, and the Windows-only pieces. Those now sit behind interfaces in `Platform/` — Tolk, DPAPI and the UI-thread dispatcher — with the registry, NAudio and WebView2 still to follow. |
| `KinetixModManager.Tests` | `net10.0` | 1,024 xUnit tests against `Kinetix.Core`. |

`Kinetix.Core` targets plain `net10.0` rather than `net10.0-windows` deliberately: it cannot reach
`System.Windows.Forms`, the registry or DPAPI, so the separation is enforced by the compiler rather than
by everyone remembering it.

Inside the app project:

| File(s) | Purpose |
|---|---|
| `Form1.cs` + `Form1.*.cs` | UI and orchestration, split into partial-class files by concern (Wiki, Updates, Settings, Install, Profiles, etc.) |
| `AppSettings.cs` | Settings load/save with DPAPI key encryption |
| `NexusService.cs` | All Nexus and GitHub API communication |
| `ModFileSystem.cs` | Mod scanning, backup management, zip installation |
| `SoundEngine.cs` | Audio playback via NAudio + NVorbis |
| `LogAnalyzer.cs` | SMAPI log parsing and fix-rule engine |

### Building

```
dotnet build KinetixModManager.slnx
dotnet test  KinetixModManager.Tests
```

The core and the tests build anywhere .NET 10 runs. To compile the WinForms app on a non-Windows
machine — useful for checking a change has not broken it, though it cannot run there — add
`-p:EnableWindowsTargeting=true`.

---

## License

This project is provided as-is for personal and community use.
