# Sabotage campaign: 2026-09-13-minecraft

Catch rate: 31/33 = 93.9%
Frame:      33 mutants chosen blind on 2026-09-13 across KinetixModManager.
            The seven Minecraft source files written in this session, plus the Minecraft branch added to UpdateCoverage. Chosen blind: this is the first campaign this repository has ever run, so there is no previous mutant list to avoid. A sample of the new Minecraft surface, NOT a survey of the manager's ~90 source files.
            A sample, not a survey. Do not compare it to a rate from another frame.

## Survivors -- the output of the campaign (2)

| id | file | what it breaks, in user terms |
| --- | --- | --- |
| M02 | `KinetixModManager/MinecraftLayout.cs` | Any file ending in .disabled is listed as a mod, so a stray readme.txt.disabled appears in the mod list as something the user can enable. |
| M31 | `KinetixModManager/MinecraftLaunchLog.cs` | Ordinary log lines after the mod list are counted as mods, so the report of what loaded is padded with nonsense. |

Concentration: `KinetixModManager/MinecraftLayout.cs` x1, `KinetixModManager/MinecraftLaunchLog.cs` x1

Now read the *pattern*, not the lines. Ask, in this order:
1. Which survivors are in code no test file ever names? (`test_census.py`)
2. Which are in a class that *has* a thorough-looking test file, but this one
   sentence has no case? (a fix with two callers gets one test)
3. Which share a caller, a boundary, or a data shape?

## Caught (31)
- M01: 0 test(s) fired
- M03: 0 test(s) fired
- M04: 0 test(s) fired
- M05: 0 test(s) fired
- M06: 0 test(s) fired
- M07: 0 test(s) fired
- M08: 0 test(s) fired
- M09: 0 test(s) fired
- M10: 0 test(s) fired
- M11: 0 test(s) fired
- M12: 0 test(s) fired
- M13: 0 test(s) fired
- M14: 0 test(s) fired
- M15: 0 test(s) fired
- M16: 0 test(s) fired
- M17: 0 test(s) fired
- M18: 0 test(s) fired
- M19: 0 test(s) fired
- M20: 0 test(s) fired
- M21: 0 test(s) fired
- M22: 0 test(s) fired
- M23: 0 test(s) fired
- M24: 0 test(s) fired
- M25: 0 test(s) fired
- M26: 0 test(s) fired
- M27: 0 test(s) fired
- M28: 0 test(s) fired
- M29: 0 test(s) fired
- M30: 0 test(s) fired
- M32: 0 test(s) fired
- M33: 0 test(s) fired

_Generated 2026-09-13T03:15:39. Logs: logs/_
