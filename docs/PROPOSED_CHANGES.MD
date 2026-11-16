These changes are proposed for future use and development of Kinetix Mod Manager.

1. Further split of concepts:
    - Kinetix.Core should offer only code related to Mod Manager UI.
    - Kinetix.Games should offer all code related to Games. This code should then populate UI by using API(functions, or whatever else) from core.
Expected outcome will be that code is more manageable and we can easyly add new games.

2. Platform and UI rework.
    - We should make folders Kinetix.Platforms and Kinetix.GUI. They will have subfolders such as Windows,MacOS,Linux and WinUI, GTK, Avalonia respectfully.
Expected outcome will make managing code in more modular way allowing us to switch default shipped UI with simple build options or build for bultiple UIs. E.g. Windows Platform build with both WinUI and Avalonia UI. Therefore more manageable folder structure, and simpler maintaining of future expansions.
Note: We should also understand what KinetixModManger folder is? Is that WinUI related code or is it Platform merged with UI code? If later that will be less managable at it's current state.

