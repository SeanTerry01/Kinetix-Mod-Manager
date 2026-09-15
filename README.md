# Kinetix Mod Manager

A fully keyboard-driven, screen-reader-compatible mod manager for **Stardew Valley**, **Skyrim Special Edition**, **Fallout 4**, **Moonlight Peaks**, **The Witcher 3: Wild Hunt** and **Minecraft: Java Edition**, built for the accessibility community. Designed to work with NVDA, JAWS, and SAPI-based readers out of the box via [Tolk](https://github.com/dkager/tolk).

---

## Features

- **Installed Mods** — browse, enable/disable, delete, and search your mod list with real-time audio feedback
- **Update Checking** — detects available updates via the Nexus Mods API, via Modrinth for Minecraft, and for Stardew Valley via SMAPI's own mod database, which also covers mods hosted on ModDrop and CurseForge; one-click "Update All" for Premium members
- **Mod Discovery** — search for mods without leaving the app, and **choose where they are searched for**: every source at once, one source, or a preferred source with the rest a keypress away. Minecraft searches Modrinth and needs no account at all
- **Install from GitHub** — name a repository (or paste its address) and the newest release is downloaded and installed, for the many mods published there and nowhere else
- **Mod Source API Keys** — one screen listing every site that wants a key, whether you have given it one, and the key itself when you ask for it; stored encrypted, and never read aloud in passing
- **Mod Profiles** — save and restore different enabled/disabled mod configurations for different playthroughs
- **Automatic Backups** — zips the current mod folder before every update or deletion; configurable retention limit
- **Dependency Viewer** — shows required and optional dependencies for the selected mod, flagging missing or outdated ones
- **SMAPI Log Viewer** — parses your latest SMAPI log, filters by level, and suggests fixes for common errors (Stardew Valley)
- **Integrated Game Wiki** — built-in, screen-reader-friendly wiki browser (Stardew Valley Wiki, UESP for Skyrim, Fallout Wiki, Minecraft Wiki) with category drilling
- **Walkthroughs** — read community walkthroughs and guides for the active game inside the app
- **Audio Themes** — feedback sounds are `.ogg` files in swappable theme packs, and the theme **follows the game you load**, so a Skyrim session sounds like Skyrim before anything is read out. Adding a game's sounds is dropping files into a folder. For Minecraft the connect and disconnect cues follow the game's own log, so they sound when you join and leave a multiplayer server
- **NXM Protocol** — registers as an `nxm://` handler so "Mod Manager Download" buttons on Nexus open the app directly
- **Secure API Key Storage** — every mod site key, and your AI provider key, are stored encrypted using Windows DPAPI (never plain text on disk)

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
- A free [Nexus Mods account](https://www.nexusmods.com) with a Personal API Key — for the five games whose mods come from Nexus. **Minecraft needs no account of any kind**: its mods come from Modrinth
- The target game installed (point the Mods Path to wherever your `Mods` / game data folder lives)

---

## Getting Started

1. Download the latest `KinetixModManager_Setup.exe` from the [Releases](https://github.com/SeanTerry01/Kinetix-Mod-Manager/releases) page and run the installer.
2. Launch `KinetixModManager.exe`. On first launch, the Settings dialog opens automatically.
3. Choose your active game from the **Games** menu, then confirm or browse to that game's `Mods` folder.
4. Paste your Nexus Mods API key — either in Settings, or from **File → Mod source API keys...**, which lists every site that wants one. Skip this entirely if you are here for Minecraft.
5. Press **Save Settings**. The app connects and loads your mod list.

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
| Search the other mod sources too (Discovery list) | Alt + O |
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

The solution is five projects. The last two are experimental and are not part of the Windows release.

| Project | Target | Purpose |
|---|---|---|
| `Kinetix.Core` | `net10.0` | The rules, the parsers and the file and HTTP work, with no user interface. Settings (`AppSettings`), the Nexus and Modrinth services, game profiles, mod scanning rules, FOMOD, archive extraction, the mod-source catalogue and search planner, sound-theme resolution, Modrinth, GitHub releases, the Minecraft launcher and Fabric installer, save and INI readers, the localisation catalogue, and the domain models under `Models/`. |
| `KinetixModManager` | `net10.0-windows` | The WinForms application: the screen it draws, the keys it listens for, and the Windows-only pieces. Those now sit behind interfaces in `Platform/` — Tolk, DPAPI and the UI-thread dispatcher — with the registry, NAudio and WebView2 still to follow. |
| `KinetixModManager.Tests` | `net10.0` | 1,371 xUnit tests against `Kinetix.Core` and `Kinetix.Platform.Linux`. |
| `Kinetix.Platform.Linux` | `net10.0` | The Linux answers to the same platform questions: speech through speech-dispatcher, sounds through GStreamer, secrets through libsecret, and finding an installed game through Steam (four layouts, Flatpak included), its Proton prefixes, and Heroic. No UI toolkit. |
| `Kinetix.Gtk` | `net10.0` | A GTK4 front end for Linux: games, installed mods, mod search and install, updates, profiles, dependencies, an embedded wiki Orca reads, per-game sounds and settings. Nine tabs against the Windows head's sixty-odd. All six games can be searched and update-checked; only Minecraft mods can be installed from here so far. See `ARCHITECTURE_REVIEW.md` §17 and §28–§34. |

`Kinetix.Core` targets plain `net10.0` rather than `net10.0-windows` deliberately: it cannot reach
`System.Windows.Forms`, the registry or DPAPI, so the separation is enforced by the compiler rather than
by everyone remembering it.

Inside the app project:

| File(s) | Purpose |
|---|---|
| `Form1.cs` + `Form1.*.cs` | UI and orchestration, split into partial-class files by concern (Wiki, Updates, Settings, Install, Profiles, etc.) |
| `ModFileSystem.cs` | Putting an unpacked mod where each game wants it: deployment, hard links, `plugins.txt`, INI editing, FOMOD finalisation |
| `SoundEngine.cs` | Audio playback via NAudio + NVorbis |
| `LogAnalyzer.cs` | SMAPI log parsing and fix-rule engine |

### Building

```
dotnet build KinetixModManager.slnx
dotnet test  KinetixModManager.Tests/KinetixModManager.Tests.csproj
```

The core and the tests build anywhere .NET 10 runs. To compile the WinForms app on a non-Windows
machine — useful for checking a change has not broken it, though it cannot run there — add
`-p:EnableWindowsTargeting=true`.

---

## License

This project is provided as-is for personal and community use.
