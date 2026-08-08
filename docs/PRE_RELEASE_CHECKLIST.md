# Pre-release checklist

Written 2026-08-08, for the release that follows v1.4.5. At that point the branch
`feature/moonlight-peaks-and-inline-windows` was **51 commits ahead of origin/master**, unpushed, with the
version still reading 1.4.5 in both places.

This is what has **not** been confirmed by ear, what is known to be unverified, and what is deliberately not in
this release. Everything here builds clean, passes 365 tests, and publishes a complete Release folder — none of
it is unfinished work. It is untested work, which is a different thing.

---

## 1. What needs testing by ear

Ordered by how likely a problem is and how much it would cost. Work down the list.

### 1.1 Mods stored inside the game folder — HIGHEST RISK

Never tested, and it is the only thing in this release that **moves your mods**.

*   Load **Fallout 4** rather than Skyrim for the first try — fewer mods, and less to lose if it goes wrong.
*   Settings (Ctrl+P) → **Paths & Account** tab → check **"Store this copy's mods inside the game folder"**.
*   Expect: a spoken warning naming the mod count and saying that uninstalling the game through Steam or GOG
    would delete them, then a confirmation. Answer Yes.
*   Expect afterwards: "Moved N mods", the mod list unchanged, and every mod still enabled.
*   Check on disk that `<game folder>\KinetixMods` now holds them and the old `%AppData%` staging folder is gone.
*   Then **uncheck it** and confirm they come back.
*   Something is wrong if: the mod list empties, mods lose their enabled state, or the old folder still has files
    in it afterwards. Nothing is deleted until everything has been copied, so the mods should exist somewhere in
    every failure case.

### 1.2 SKSE and SSE Engine Fixes — the whole file-picking rework

Unproven against a live install. This is the feature the GOG tester asked for.

*   Load **Skyrim Special Edition** → Mods menu → Game and Maintenance → **Install Skyrim Accessibility Suite**.
*   Expect for **SKSE**: it works out the build from your game's own exe and fetches the file matching it. Your
    copy is Steam, so it should land the Steam build, not the GOG one.
*   Expect for **SSE Engine Fixes**: **two** downloads, not one — the main plugin into the mods folder, and the
    preloader (`d3dx9_42.dll`) loose in the game folder.
*   Something is wrong if: only one Engine Fixes file arrives, the same file arrives twice, or SKSE reports a
    version mismatch when you press F5.

**Do not test the incomplete-mod check by deleting `d3dx9_42.dll` from the game folder.** It will not work, and
nothing is broken when it doesn't: the preloader is installed as a staged root-folder mod, so the next mod-list
refresh deploys it straight back. That is the deployment engine doing its job.

You do not need to arrange the test anyway — as of 2026-08-08 this install already fails it for real. **Check My
Setup reports "SSE Engine Fixes is installed but incomplete: Part 1 — the SKSE plugin is missing."** That is
accurate: the preloader is installed and the plugin it exists to load is not, so Engine Fixes has never done
anything here. Its own log says so — `failed to search skse plugin directory`, then `loader finished`. Pressing
Enter on that row should fetch the main plugin and put it in the mods folder.

### 1.3 FMC Audio Remaster (The Witcher 3)

It was installed one level too deep and has **never loaded**. Repaired on disk on 2026-08-08; the repair itself
is unverified.

*   Launch The Witcher 3 and confirm the mod's audio changes are actually present.
*   Check the game's own mod menu lists it once, under `modFMCAudioRemaster`, and not as a ghost called `mods`.

### 1.4 The shutdown sequence

Measured end to end (speech on the keypress, cue after it, process gone in about 4.5 seconds) but not heard.

*   Press **Alt+F4**, or File → Exit.
*   Expect, in order: the goodbye message straight away, then silence until it finishes, then the disconnect
    cue, then the program closes.
*   Press **Alt+F4 twice quickly** and confirm the message is not cut short.
*   Note: the disconnect cue only plays when a game session is open. Closing a session with Ctrl+Shift+C already
    plays it, so exiting afterwards is silent by design. Say if you would rather hear it on every exit.

### 1.5 The Witcher 3 mod list

Verified by reading the list programmatically, not by ear.

*   Load The Witcher 3. Expect **5 rows**, not 12:
    *   FMC Audio Remaster … version 1.1
    *   Random Encounters Reworked, version unknown
    *   Shared Imports, version unknown
    *   Witcher Access, version unknown
    *   Mod Group: Shared Utils. Contains 8 mods. Collapsed.
*   Expand the group with **Right Arrow** and confirm the eight shared-utility mods read sensibly.

### 1.6 Mod settings offered as choices

From commit `a5f0f2c`, which predates this session and was never ear-tested. It ships in this release.

*   For each of the four games, open a mod's settings and confirm you are given a list to arrow through rather
    than a file to type into.

---

## 2. Known gaps

Things we know we have **not** established. None of them block the release; all of them could produce a report
after it.

*   **The free Nexus account path is untested and cannot be tested here.** If your account is premium, the
    manager downloads directly and never exercises the deep-link route — which is the entire point of the
    Engine Fixes work. It will reach users unverified. A tester without premium would settle it in five minutes.
*   **Whether a Nexus deep link can auto-start the manager download is unknown.** The manager opens the page
    showing only the correct file, which works, but the user still clicks "Mod Manager Download". Adding
    `&nmm=1` to the URL may make Nexus fire it automatically — never confirmed against a live page.
*   **Two copies of one game is proven only by a stand-in.** The second copy was simulated with an empty
    `SkyrimSE.exe` and a GOG marker file in `C:\GOG Games\Skyrim Anniversary Edition`, then removed. Detection,
    labelling and the separate mods path all behaved correctly, but no real second copy has ever been loaded.
    Everything the single-copy path does was exercised repeatedly and is safe.
*   **Fallout 4's GOG per-player folder is still a guess.** `Fallout4 GOG`, inferred from Skyrim's pattern. It
    was never read out of a real GOG Fallout 4 executable, because there wasn't one to read. A wrong value shows
    up as the install simply not being recognised as GOG.
*   **The Witcher 3 family grouping can under-group.** Folders are grouped by a shared `mod_family_*` name,
    which is the author's own statement that they belong together. A framework that ships its pieces under
    unrelated names will still fill several rows. This was chosen over guessing which helper belongs to which
    mod, which can group things wrongly.

---

## 3. Deliberately not in this release

The full reasoning for each lives in `docs/FUTURE_DEVELOPMENT_PLANS.txt` §6.

*   **Vortex import** — blocked on finding a Vortex user to test against. MO2 import already works.
*   **FOMOD answer replay in Collections** — record the wizard choices so a Collection reinstalls a scripted mod
    unattended, instead of asking the same questions again. Buildable now.
*   **Zip-bundle Collections** — export a Collection as one zip with an embedded readme, pinning exact file IDs
    rather than "latest main file", so a shared setup reinstalls as the same versions.
*   **Downloadable language packs** — worth doing only once non-English translations exist. The manager is
    fully localised (about 1,418 keys) but English-only.
*   **Voice notes on mods** — deferred by earlier decision.
*   **Cyberpunk 2077 as a sixth game** — wanted, blocked on having the game and its accessibility mod installed
    to develop against. Nexus domain `cyberpunk2077`, game id 3333, Steam app 1091500, sold on GOG too.

---

## 4. Release mechanics

From `reference-build-publish-release`. The publish output was verified complete on 2026-08-08.

1.  **Bump the version in both places** — `<Version>` in `KinetixModManager/KinetixModManager.csproj` and
    `AppVersion` in `KinetixModManager/setup.iss`. Both currently read **1.4.5**. Suggested: **1.5.0** — two new
    games (Moonlight Peaks, The Witcher 3), per-copy game installs, and the windows-to-panels conversion is well
    past a patch release.
2.  **Re-publish after the bump and check the exe's FileVersion.** A Debug build does not pick up a version
    change, so this is the step that catches a bump that did not take:
    `dotnet publish KinetixModManager/KinetixModManager.csproj -c Release -r win-x64 --self-contained true`
3.  **Confirm the publish folder is complete** at
    `KinetixModManager\bin\Release\net10.0-windows\win-x64\publish\`. It should contain the exe, `MANUAL.md`,
    `CHANGELOG.md`, and the `lang`, `docs`, `data` and `sounds` folders. `sounds` must hold all six themes,
    including **The Witcher 3** — that theme was untracked in git until 2026-08-08 and would not have shipped.
    (`profiles` is legitimately absent; `setup.iss` marks it `skipifsourcedoesntexist`.)
4.  **Build the installer with Inno Setup** from `KinetixModManager\setup.iss`.
5.  **Tag and publish the GitHub release**, using `--clobber` when replacing an asset.
6.  **Push the branch.** It has never been pushed — this release is its first appearance on origin.

### Also worth knowing

*   The bundled Moonlight Peaks keybind reader (`data\plugins\MoonlightKeybindExport.dll`) is built in a
    **separate repository**. It is present and current as of 2026-08-02. If it ever changes it has to be rebuilt
    and copied across by hand — nothing in this build does it for you.
*   This release is large: 51 commits, two new games, and a change to how every per-game setting is keyed.
    Existing settings files upgrade in place with nothing to redo, but it is worth keeping a copy of
    `%AppData%\AudiVentureGames\KinetixModManager\settings.json` before the first run of the new build.
