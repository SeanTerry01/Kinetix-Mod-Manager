# Sabotage campaign log

One row per campaign. This record is what makes "choose blind" possible next time: a rate
measured by re-running a published mutant list measures the fix, not the suite. Before starting
a new campaign, read this file and pick mutants somewhere it has not been.

⚠️ **A rate is only comparable to another rate from the same sampling frame.** Every row below
is a sample of a few files, never a survey of the manager.

| Date | Frame | Mutants | Caught | Rate | Notes |
| --- | --- | --- | --- | --- | --- |
| 2026-09-13 | `.campaigns/2026-09-13-minecraft/` — the seven Minecraft source files written that day, plus the Minecraft branch of `UpdateCoverage`. ~8 of the manager's ~90 source files. | 33 | 31 | **93.9%** | The first campaign this repository has run, so mutants were chosen blind by default. Control run green, no backup drift. |

## 2026-09-13 — Minecraft

**Files mutated:** `MinecraftLayout.cs`, `FabricInstaller.cs`, `MinecraftSuite.cs`,
`ModrinthService.cs`, `MinecraftLauncher.cs`, `MinecraftControls.cs`, `MinecraftLaunchLog.cs`,
`UpdateCoverage.cs`.

**Two survivors, both dealt with in `192e593`:**

*   **M02, `MinecraftLayout.IsModFile`.** Loosening the check to "ends with `.disabled`" passed
    the whole suite, because every case in it happened to be either a jar or a file with no
    disabling suffix at all. A stray `readme.txt.disabled` would have shown up in the mod list
    as something the user could switch on. Two cases added; killed and proven red.
*   **M31, the mod-list terminator in `MinecraftLaunchLog`.** Verified as an **equivalent
    mutant** rather than fixed: on a real 205-line log, no line after the terminator matches the
    mod-line pattern at all, so breaking the terminator changes nothing observable. Excluded
    from the rate rather than counted as a miss. The guard stays as cheap insurance.

**⚠️ The campaign's real value was not the rate.** Chasing M31 turned up a genuine bug the whole
green suite had missed: Fabric draws its mod list as a tree and prints the **last** child under
a parent with `\--` where the others use `|--`. The reader knew only the pipe form and silently
dropped 3 of the 51 mods a real log declared. **Worse, that shortfall had already been noticed
and written off in a doc comment as an inherent limitation of reading the log, rather than
investigated.** It was a missing alternation. All 51 are read now. See
`memory/feedback-verify-dont-rationalise.md`.

**⚠️ Anchor gotcha for the next campaign here:** `validate` rejected five anchors because
`MinecraftLayout.cs` is **CRLF** while the other seven files are LF. Check line endings before
writing anchors.

**Never measured:** everything else. The whole of `Form1.*`, `NexusService`, `ModFileSystem`,
the FOMOD installer, load order, profiles, the wiki. `sabotage.py map` will show the blank areas
— go there rather than re-measuring Minecraft.
