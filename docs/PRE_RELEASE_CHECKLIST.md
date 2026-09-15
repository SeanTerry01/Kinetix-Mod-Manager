# Pre-release checklist

Written 2026-08-08 for the release that followed v1.4.5, and kept up since. **Now for v1.6.0**, on branch
`refactor/kinetix-core-split`, **many commits ahead of `origin/master`, unpushed**, version 1.6.0 in both
`KinetixModManager.csproj` and `KinetixModManager\setup.iss`. **1,398 tests**, all five projects build clean
with no warnings.

⚠️ The branch name describes the first thing done on it, not the whole of it. It now carries the core split,
the Linux spike, the archive pipeline, per-game sounds and the mod source chooser.

⚠️ **The only tester zip in `KinetixModManager\Setup\` is the 1.5.2 one, and it is stale** — it predates every
Minecraft commit. Re-cut with `tools\make-test-zip.ps1` before giving anything to a tester.

⚠️ Sections 1.1 to 1.7 are the v1.5.0/v1.5.1 list. They all shipped and are kept because they describe how to
test those areas, not because they are outstanding. **§1.0b is the newest work and the live list; §1.0 is the
rest of v1.6.0; §1.0a is what is still outstanding from v1.5.2.**

This is what has **not** been confirmed by ear, what is known to be unverified, and what is deliberately not in
this release. Everything here builds clean, passes 1,398 tests, and publishes a complete Release folder — none of
it is unfinished work. It is untested work, which is a different thing.

---

## 1. What needs testing by ear

Ordered by how likely a problem is and how much it would cost. Work down the list.

### 1.0b The newest work — none of it confirmed by ear

Everything in this section was written in one day and is covered by tests, which is not the same as having been
heard. The first three are the ones where a problem would cost the most.

*   ⚠️ **`.7z` and `.rar` mods install as they used to.** The biggest behavioural change in the release:
    `7za.exe` is gone and both formats are now unpacked in-process. Install a real `.7z` Skyrim or Fallout 4 mod
    — ideally a large, solid one — and confirm it lands correctly and that progress still climbs to 100. The
    decoder is not the same code as before, and this is the one thing tests cannot settle.
*   ⚠️ **A FOMOD mod inside a `.7z`.** Same reason, one layer deeper: the wizard runs on what came out of the
    archive.
*   ⚠️ **A script extender install.** `InstallScriptExtenderAsync` and the Engine Fixes preloader both routed on
    the file extension before and now sniff the file. Confirm SKSE and F4SE still install from the Accessibility
    Suite.
*   **Per-game sounds.** Load each game in turn and confirm the sounds change with it. Minecraft's theme folder
    is present but has no sounds in it yet, so Minecraft should sound like the Default theme rather than silent
    — silence there is a bug, not an empty folder.
*   **Minecraft's connect and disconnect cues.** Join a multiplayer server and leave it; both should sound.
    Playing a singleplayer world should produce neither. Quitting the game while still on a server should sound
    the disconnect.
*   **The mod source chooser** (Paths & Account). All three modes, on Stardew and on Minecraft. Confirm Skyrim,
    Fallout 4, The Witcher 3 and Moonlight Peaks show the one-line explanation instead of a dropdown.
*   **Alt+O in the Discovery list.** With nothing else usable to search, it should do nothing surprising.
*   **Install from a GitHub repository** (Mods menu). Try `owner/repo` and a pasted address. A repository with
    no releases and a made-up repository should each say which of the two is wrong.
*   **The mod source keys screen** (File menu). Add a key, reopen it and confirm the key reads back correctly;
    edit it; forget it. **Confirm that arrowing down the list does not read any key aloud** — that is the rule
    the screen exists to keep.
*   **Minecraft with no Nexus key at all.** Two defects were fixed here: the mod list and the Discovery tab both
    used to demand a Nexus key for a game that never uses one. On a machine that has never had a Nexus key,
    Minecraft should simply work.

### 1.0 v1.6.0 — Minecraft, the live list

Minecraft is a whole new game rather than a set of fixes, so this section is longer than usual. **Nothing here
moves an existing game's mods**, and nothing in the Minecraft work touches your worlds — saves are never read,
written or moved.

⚠️ **The one thing to be careful with is F5.** It starts the game itself, without the launcher. It has been
run successfully end to end, but it is the newest and most involved code in the release.

#### Confirmed by ear during development
*   **Enabling and disabling** a Minecraft mod (Fabric API) from the Installed Mods tab.
*   **F5** launching straight to the main menu with Fabric and the mods active, going nowhere near the launcher —
    then loading a world, playing, and shutting down cleanly.
*   **Modrinth search** results reading well in the list.
*   **Update checking**: every mod reported up to date, and the Update Check Report correctly empty.
*   **Profiles**: saving a "United Minecraft" profile, disabling two mods, re-applying the profile, and both mods
    coming back enabled.
*   The **Walkthroughs** list.

#### Not yet confirmed — work down this list
*   **Press Enter on a Modrinth search result.** It should offer a choice — install, read the full description,
    open the mod's page — rather than doing nothing. The Download button doing nothing was the original report;
    this is the replacement and it has never been heard.
*   **Minecraft Access as the chosen accessibility mod.** Everything so far has been tested with United Minecraft
    only. The suite should offer the choice, install Minecraft Access as a **single jar with no Fabric API**, and
    warn rather than install if its rival is already there. This is the largest untested path in the release.
*   **Mod settings for United Minecraft** — picked from a list rather than typed, as in the other games.
*   **Check My Setup's "did my mods load" row.** It reads the game's own log and should say plainly what happened
    the last time you played, including the mod count — **and say so even when everything is fine.**
*   **The title bar after closing the game.** It used to stay on "Starting Minecraft..." forever; it should
    return to its resting form once the game exits.
*   **A Minecraft session should not connect to Nexus at all**, including when the manager starts up with
    Minecraft already the active game. It used to connect anyway.
*   **The Nexus API key box should be absent from Settings** while Minecraft is loaded, and Settings should save
    without one.
*   **Ctrl+H**, in its final shape: the game's own controls first, then the accessibility mod's, mouse buttons in
    a section of their own, and no "Keyboard Controls" level above it all. Four rounds of feedback went into
    this; the last two changes have not been heard.
*   **F3** documentation for United Minecraft and Minecraft Access.
*   **The Log tab**, showing the current run's log.
*   **The tab names on a restored session** — "Minecraft Wiki", "Minecraft Walkthroughs", "Minecraft Log", and a
    search box that says "Search the Minecraft Wiki". ⚠️ **Worth checking one other game too** (load Stardew,
    close the manager, reopen it): this bug affected all six games on a reopened session, not just Minecraft.
*   **The wiki Categories dropdown** should offer 14 entries — Blocks, Items, Hostile mobs, Passive mobs, Biomes,
    Structures, Enchantments, Potions, Redstone, Food, Tools, Weapons, Armor, Villagers — not several hundred.
    ⚠️ This changed for **every** game's wiki, so a quick listen on Stardew or Skyrim is worth it.

#### Deliberately not in this release
*   **Online (signed-in) Minecraft.** The code is designed for it and the Azure app registration is done and
    proven, but Mojang has not yet approved the app. Poll with `tools\mcauth-probe.py`: **403 = still pending,
    200 = approved.** Offline mode is complete and is the sensible everyday mode; online is a later addition
    with no rework, and only servers and Realms need it.

---

### 1.0a v1.5.2 — still outstanding

Everything in 1.5.2 is a fix or a diagnostic. **Nothing in it moves your mods.** Two rounds of testing have been
done on a portable build; results are recorded here.

#### Confirmed by the tester
*   **Import and export** of suggestion lists. This was the original report — the export was being abandoned
    between the file dialog and the write, to protect an announcement. Collections and load order shared the
    same bug and the same fix.
*   The **download prompt** reads its question before its choices.

#### Not yet confirmed — work down this list
*   **The install prompt, in full.** Expect exactly two lines, in this order, with nothing between them:
    *"Downloaded <mod>. Install now?"*, then *"Yes, button, Alt+Y"*. **Three separate faults were fixed here in
    turn and every one was found only from the NVDA speech history.** If it is wrong again, send the speech
    history rather than a description — that is what settled each of them.
*   **The title bar after any download or install.** It should go back to its resting form rather than staying
    on "Downloading X... 100%". It never used to reset at all, and the change reaches every operation that
    reports progress, so this is where over-eager clearing would show.
*   **In-window views** — Settings, the reports, the log. They should sound exactly as in 1.5.1. The code that
    stops a view announcing the window title moved into shared machinery; if a view has started saying
    "Kinetix Mod Manager" on the way in, that is this change.
*   **Deleting a mod** should say *"Backing up <mod>"*, not *"Installing <mod>"*.
*   **Answering "search for this mod"** in Check My Setup should land in Discovery with the search already run,
    never in a window called `WindowsFormsParkingWindow`.
*   **Ctrl+Shift+L**, and the game's log from the Log tab. Both open inside the manager now, read only, at the
    newest entry; where a game has several logs you get a list first, most useful first. Notepad is gone.
*   **A missing dependency** should be searched for by name — "Producer Framework Mod" — not by its identifier,
    "Digus.ProducerFrameworkMod".
*   **The wiki Categories dropdown** gives up after 10 seconds instead of 30 and says so rather than sitting
    empty.

#### Needs a machine we do not have
*   **A GOG copy of Skyrim getting the GOG script extender.** Fixed twice over — the folder lookup now asks the
    copy's own recorded store, and the wrong store's file is disqualified rather than merely outscored. Needs
    somebody who owns the game on both stores.
*   **SSE Engine Fixes on Skyrim 1.7.99+**, where the preloader is no longer part of the mod. Blocked on the
    mod's author: at the time of writing no released Engine Fixes runs on 1.7.104 at all.

---

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

### 1.2 Downloading a mod for a game that is not loaded — NEW, 2026-08-11

Rewritten in response to a report: `NXM Error: Error reading JArray from JsonReader` whenever "Mod Manager
Download" was pressed for a game other than the loaded one, or with no game loaded. The cause was the manager
asking Nexus about the **loaded** game while taking the mod and file ids from the link — and, with no session, about
Stardew Valley, its fallback. The link's own game is now what decides, and the old three-of-five-games switch is
gone in favour of `GameProfiles.FindByNexusDomain`.

*   From a game session, download a mod for a **different** game. Expect a choice: switch and install, or save it
    for that game. Saved mods appear in that game's Downloads History (Ctrl+Shift+W).
*   With **no session**, do the same. Expect no question — nothing is being interrupted, so it loads the game and
    installs, saying which.
*   **Moonlight Peaks and The Witcher 3** are the two that could never work before; try them if possible.
*   A game the manager does not support should say so and name the domain, not fail as a parser error.
*   Settings → Mods & Search → "When a download is for another game" (Ask / Switch / Save) should stop the question
    appearing when set to Switch.
*   Covered by 19 unit tests (`NxmLinkTests`), so the parsing and the domain-to-game mapping are proven for all five
    games; what is unproven by ear is the prompt wording, the copy picker, and the switch-then-install sequence.

### 1.3 SKSE and SSE Engine Fixes — the whole file-picking rework

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

### 1.4 FMC Audio Remaster (The Witcher 3)

It was installed one level too deep and has **never loaded**. Repaired on disk on 2026-08-08; the repair itself
is unverified.

*   Launch The Witcher 3 and confirm the mod's audio changes are actually present.
*   Check the game's own mod menu lists it once, under `modFMCAudioRemaster`, and not as a ghost called `mods`.

### 1.5 The shutdown sequence

Measured end to end (speech on the keypress, cue after it, process gone in about 4.5 seconds) but not heard.

*   Press **Alt+F4**, or File → Exit.
*   Expect, in order: the goodbye message straight away, then silence until it finishes, then the disconnect
    cue, then the program closes.
*   Press **Alt+F4 twice quickly** and confirm the message is not cut short.
*   Note: the disconnect cue only plays when a game session is open. Closing a session with Ctrl+Shift+C already
    plays it, so exiting afterwards is silent by design. Say if you would rather hear it on every exit.

### 1.6 The Witcher 3 mod list

Verified by reading the list programmatically, not by ear.

*   Load The Witcher 3. Expect **5 rows**, not 12:
    *   FMC Audio Remaster … version 1.1
    *   Random Encounters Reworked, version unknown
    *   Shared Imports, version unknown
    *   Witcher Access, version unknown
    *   Mod Group: Shared Utils. Contains 8 mods. Collapsed.
*   Expand the group with **Right Arrow** and confirm the eight shared-utility mods read sensibly.

### 1.7 Mod settings offered as choices

From commit `a5f0f2c`, which predates this session and was never ear-tested. It ships in this release.

*   For each game, open a mod's settings and confirm you are given a list to arrow through rather
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
*   **The Visual C++ runtime finding has never been seen on screen.** The rule is proven by unit tests built
    from the exact version set read out of this machine's System32 while it was broken (14.28 core files under
    14.51 satellites), and the healthy path is exercised every time Check My Setup runs here. But the machine
    was repaired before the check existed, so the wording, the row order and the Enter-to-Microsoft action have
    only ever been read in the healthy case, where they stay silent. Faking a mismatch means overwriting a file
    in System32, which is not worth doing to a working machine. A wrong sentence here misleads at the exact
    moment the user is already lost, so it is worth re-reading the two strings in `en.json` before release.
    The natural test is another computer with a mixed runtime, which has not been available yet — if one turns
    up before release, opening Check My Setup on it settles this in seconds.
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
    fully localised (about 1,470 keys) but English-only.
*   **Voice notes on mods** — deferred by earlier decision.
*   **Cyberpunk 2077 as a sixth game** — wanted, blocked on having the game and its accessibility mod installed
    to develop against. Nexus domain `cyberpunk2077`, game id 3333, Steam app 1091500, sold on GOG too.

---

## 4. Release mechanics

From `reference-build-publish-release`. The publish output was verified complete on 2026-08-08.

0.  **Publish your Suggested Mods picks** (see §5 below) — do this *before* the version bump, so the shipped
    list records the version it went out with.
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
*   This release is large: **58 commits as of 2026-08-09**, two new games, and a change to how every per-game
    setting is keyed.
    Existing settings files upgrade in place with nothing to redo, but it is worth keeping a copy of
    `%AppData%\AudiVentureGames\KinetixModManager\settings.json` before the first run of the new build.

---

## 5. Shipping your Suggested Mods picks

The Suggested Mods viewer (**F7**) shows two layers merged:

| Layer | Where | Who it is for |
| --- | --- | --- |
| Shipped | `KinetixModManager\data\suggested-mods.json` | everybody; replaced wholesale by each release |
| Personal | `%AppData%\AudiVentureGames\KinetixModManager\suggested-mods.json` | just you; wins over the shipped one |

Curating happens in the **personal** layer — **Ctrl + Shift + F7** marks the selected mod, **F7** views the
list, **Shift + F7** edits categories. Marking a mod does *not* put it in front of anyone else. Publishing it
does, and that is one command:

```
powershell -ExecutionPolicy Bypass -File tools\update-suggested-defaults.ps1
```

That reads your personal list, writes `KinetixModManager\data\suggested-mods.json`, and prints what it published
broken down by game. **Commit that file with the release and it ships.** Nothing else needs changing: the
project already copies `data\**` to the build output and `setup.iss` already installs `data\*`.

Useful arguments:

*   `-DryRun` — report what would ship and write nothing. Worth running first.
*   `-Games MoonlightPeaks,SkyrimSE` — publish only those games' entries.
*   `-Name` / `-Author` — what the file records about itself.

The script warns (without stopping) about an entry with no reason written, or with neither a Nexus id nor a
GitHub repo — that second one cannot be installed from the list, so it is only a name. `ShippedSuggestionsTests`
then makes those checks binding, along with unknown games, categories the file forgot to carry, duplicate mods,
and a stray byte-order mark. **So the safe loop is: run the script, run the tests, commit.** You never have to
read the JSON.

Two things worth knowing:

*   Your own entries stay in your personal layer after publishing, and the personal layer wins — so you will not
    see a difference locally. Everyone else gets them from the shipped list.
*   Only what you have marked is published. The shipped list is never re-exported into itself, so entries cannot
    accumulate copies of themselves release over release.
