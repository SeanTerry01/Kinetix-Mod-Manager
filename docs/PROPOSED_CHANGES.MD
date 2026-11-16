# Proposed Changes for Future Development of Kinetix Mod Manager

The following changes are proposed for the future development of Kinetix Mod Manager.

## 1. Separate responsibilities more clearly

- **Kinetix.Core** should contain only Mod Manager backend code.
- **Kinetix.Games** should contain all game-related code. It should provide data to populate the UI in **Kinetix.UI** through APIs/functions exposed by Core. Core should handle the backend orchestration between modules.
- **Kinetix.UI** should handle all UI concerns and populate itself using data from Kinetix.Games.
- **Kinetix.Platforms** should contain OS-specific code used by Kinetix.Core, keeping Core free of system-specific logic.

**Expected outcome:** More manageable code and an easier path to adding new games.

**Design note:** Ideally, Kinetix.Core should provide only the structure (classes, placeholders, etc.) used to build Kinetix.Games code. Kinetix.UI should provide the UI shell and populate it with data from Kinetix.Games.

**Workflow:**  
`Platforms -> Core -> Games -> UI`

**Responsibilities:**

- **Platforms:** OS-specific code, e.g. creating launch shortcuts for a specific OS.
- **Core:** Skeleton for game code, e.g. searching for a game folder.
- **Games:** Uses Core code and assigns game-specific data, e.g. game metadata and configuration.
- **UI:** Empty UI structure that imports game code to populate itself, e.g. a button for creating a launch shortcut.

## 2. Rework Platform and UI organization

- Create a **Kinetix.Platforms** folder with subfolders such as `Windows`, `macOS`, and `Linux`.
- Create a **Kinetix.UI** folder with subfolders such as `WinUI`, `GTK`, and `Avalonia`.

**Expected outcome:** More modular code, the ability to switch the default shipped UI via build options, and support for building multiple UIs (e.g. a Windows Platform build with both WinUI and Avalonia). This should also make the folder structure more manageable and future expansions easier to maintain.

**Open question:** We should clarify the purpose of the existing `KinetixModManager` folder. Is it WinUI-related code, or is it Platform code merged with UI code? If it mixes concerns, it will be harder to manage in its current state. **Action:** Split it into the appropriate `Kinetix.UI` and `Kinetix.Platforms` locations.