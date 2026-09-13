# Kinetix Mod Manager 1.5.0 — tester checklist

Thank you for doing this. This build is large: two new games, a change to how every per-game setting is stored,
and about thirty screens that used to be separate windows and now open inside the main window. A lot of it has
never been used by anyone but its author, and some of it has never been used at all.

**What would help most:** everything in section 2 and section 3. Section 2 is the code that moves your files, so
it is where a bug costs the most. Section 3 is the code that has never run on a real setup — most of it needs
circumstances the author does not have, which is the whole reason you are reading this.

If you have an hour, do section 1, then section 2, then whichever parts of section 3 apply to you. The rest can
wait for a second sitting.

---

## 1. Before you start

*   **There is nothing to install.** Unzip the folder wherever you like and run `KinetixModManager.exe` from
    inside it. Your normally installed copy of the manager is untouched, and you can delete the folder when you
    are done. Only one copy can run at a time — if the test build seems to do nothing when you start it, close
    the installed one first.
*   **Keep a copy of your settings file.** It is at
    `%AppData%\AudiVentureGames\KinetixModManager\settings.json`. **The test build shares this with your installed
    copy** — the same settings, the same staged mods, downloads and backups. That is deliberate, so you are
    testing against your real setup, but it means this build can change things your installed copy will then see.
    This release also changes how per-game settings are keyed, and upgrades your file in place with nothing for
    you to redo — but that upgrade has only ever run on one machine. Paste the path into File Explorer's address
    bar and copy the file somewhere safe. It costs ten seconds.
*   **Nexus downloads will come to the test build while you are testing.** The manager points Nexus's
    "Mod Manager Download" button at itself every time it starts, so whichever copy ran last owns it. That is what
    you want here; when you are finished, run your installed copy once and it takes the button back.
*   **Confirm you are on the right build.** Help → About should say **1.5.0**. If it says anything else, stop and
    say so — nothing below is worth testing on the wrong build.
*   **Note what you are running:** your screen reader and its version, your Windows version, and — this matters
    for section 3 — **whether your Nexus account is free or premium**.
*   Optional but useful: which games you have, and which store each came from (Steam, GOG, or something else).

---

## 2. Highest priority — anything that moves your files

### 2.1 Storing mods inside the game folder

This is new, and it is the only thing in this release that **moves your mods**. It has never been run.

*   Load **Fallout 4** rather than Skyrim for the first try, or whichever of the two has fewer mods.
*   Settings (**Ctrl+P**) → **Paths & Account** tab → tick **"Store this copy's mods inside the game folder"**.
*   **You should hear:** a warning that names how many mods are about to move, and says that uninstalling the game
    through Steam or GOG would then delete them. Then a confirmation to answer. Answer Yes.
*   **Then you should hear:** "Moved N mods" — and the mod list should look exactly as it did, with every mod
    still enabled.
*   **On disk:** `<your game folder>\KinetixMods` should now hold them, and the old staging folder
    (`%AppData%\AudiVentureGames\KinetixModManager\Fallout4Mods`, or `SkyrimSEMods` for Skyrim) should be gone.
*   Now **untick it** and confirm the mods come back, still enabled.
*   **Report it if:** the mod list empties or loses rows, mods come back disabled, or the old folder still has
    files left in it afterwards.
*   Reassurance while you try it: nothing is deleted until everything has been copied, so in every failure case
    your mods still exist in one of the two places.

### 2.2 The ordinary things, on a real mod list

Quick pass, but do it on a game you care about — these run on every mod you own.

*   **Enable and disable** a mod. Both should be spoken, and the state should survive a restart of the manager.
*   **Delete** a mod. If you have a mod with read-only files — SkyPatcher is the usual example — delete that one:
    it used to fail and was fixed in 1.4.5, and this release rewrote a lot of code around it.
*   **Install from a zip** (**Ctrl+I**). Try a `.7z` or `.rar` too if you have one.
*   **Refresh** the list, and **switch games** while it is refreshing. You should never see one game's mods under
    another game's name in the title bar. A repeated refresh should say **"Already refreshing"** rather than
    announcing itself twice.
*   For Skyrim or Fallout 4: open **Plugin Order** and **Mod Priority**, move something, and confirm the change is
    still there after a restart. **F8** auto-sorts.
*   **Report it if:** anything changes on disk that you did not ask for, or an action reports success while the
    list says otherwise.

---

## 3. Never tested — this is where you are irreplaceable

Each item says what it needs. Skip the ones that do not apply.

### 3.1 Downloading from Nexus on a free account — the single most valuable test

**Needs: a Nexus account that is not premium.** On a premium account the manager downloads directly and never
touches this path, so it has never once been exercised. It will reach users unverified unless you do this.

*   Connect your account (**Ctrl+L**), then install any mod from Nexus through the manager.
*   **You should get:** the Nexus page opening with only the correct file shown, and you click **"Mod Manager
    Download"** on it yourself.
*   **The part to watch:** does clicking that hand the download to **Kinetix**? It should come back into the
    manager and install. If Vortex or Nexus Mod Manager grabs it, or nothing happens at all, say so — that is the
    `nxm://` handoff and it is exactly what needs proving.
*   Please report **what you had to do**, not just whether it worked. If you had to click twice, or the page
    showed the wrong file, or you were left on a page with nothing obvious to press, that is the finding.

### 3.2 Downloading a mod for a game you don't have open — brand new

**Needs: nothing special.** Rewritten on 2026-08-11 after a report, and not yet used by anyone.

Until this release, pressing **Mod Manager Download** for one game while another was loaded — or with no game
loaded at all — failed with `NXM Error: Error reading JArray from JsonReader`. The link's game is now what decides
everything, so this should work from anywhere. Please try all four shapes of it:

*   **From a different game.** Load Stardew Valley, then find a Skyrim mod on Nexus and press Mod Manager Download.
    *   Expect: the download runs, then a list to choose from — **switch to Skyrim SE and install it now**, or
        **save it for Skyrim SE and stay here**. Escape should keep the download and leave you where you are.
    *   Choose **save**, then load Skyrim and open **Downloads History** (<kbd>Ctrl+Shift+W</kbd>): the mod should
        be sitting there, installable with one keypress.
*   **With no game loaded.** Sit on the game list, or close the manager entirely, then press Mod Manager Download.
    *   Expect: no question — it says which game it is switching to, loads it, and installs. There is nothing to
        interrupt, so it does not ask.
*   **For Moonlight Peaks and The Witcher 3 specifically.** These two were missing from the old code entirely and
    could never work from another session. If you have either, they are the most valuable ones to try.
*   **For a game you don't own or haven't set up**, and if you can, **for a game the manager doesn't support** (any
    Cyberpunk 2077 mod will do). Both should say something plain. Neither should mention JSON.
*   Settings (Ctrl+P) → **Mods & Search** → **"When a download is for another game"** has three answers. Try
    **Switch to that game and install** and confirm the question stops appearing.
*   **Report it if:** you see any error naming JSON or JArray, the mod installs into the **wrong game**, the file
    ends up in the wrong game's Downloads History, or Escape loses the download.

### 3.3 SKSE and SSE Engine Fixes

**Needs: Skyrim Special Edition.** The file-picking behind this was rewritten and never proven against a live
install.

*   Mods menu → Game and Maintenance → **Install Skyrim Accessibility Suite**.
*   **SKSE:** the manager reads your game's own exe to work out the build and fetches the matching file. If your
    Skyrim is from GOG it must fetch the **GOG** build, not the Steam one. Press **F5** afterwards — a version
    mismatch reported by SKSE means it picked wrong.
*   **SSE Engine Fixes:** expect **two** downloads, not one — the main plugin into your mods folder, and the
    preloader `d3dx9_42.dll` loose in the game folder.
*   **Report it if:** only one Engine Fixes file arrives, the same file arrives twice, or SKSE complains about its
    version.
*   **Please do not test this by deleting `d3dx9_42.dll`.** The preloader is a managed mod, so the next refresh
    puts it straight back. That is correct behaviour, not a bug.

### 3.4 A GOG copy of Skyrim or Fallout 4

**Needs: the GOG version of either game.**

*   Load it and confirm the manager finds it, and that the title bar names it.
*   For **GOG Skyrim**, the important thing is that it edits the right folders: your INI files, saves and load
    order live under `Documents\My Games\Skyrim Special Edition GOG` — **not** the plain
    `Skyrim Special Edition` folder, which belongs to the Steam copy. If a setting you change in the manager does
    not show up in the game, this is the first thing to suspect.
*   For **GOG Fallout 4** there is a known guess: the manager expects a per-player folder called
    `Fallout4 GOG`, inferred from Skyrim's pattern and never read off a real GOG Fallout 4. If the install is
    simply not recognised as GOG, that guess is wrong — please say so, and say what the folder is actually called
    on your machine.

### 3.5 Owning the same game twice

**Needs: the same game on both Steam and GOG.** This has only ever been faked with an empty exe.

*   Both copies should appear in the **Games** menu, each named by its store — "Skyrim Special Edition (Steam)"
    and "Skyrim Special Edition (GOG)".
*   Each copy should keep its **own** mod list. Switch between them and confirm one copy's mods never appear under
    the other.

### 3.6 Linux, under Wine

**Needs: running the manager under Wine.**

*   Does the window scale readably, and does your screen reader see the controls?
*   Does Steam game detection find games on drives other than the default one?
*   Where a link would open a browser (Help → Support Development, or a mod page), it should either open one or
    say it has copied the link to the clipboard — never appear to do nothing.

### 3.7 If Check My Setup reports the Visual C++ runtime

**Needs: a machine whose Visual C++ runtime is broken** — which you cannot arrange on purpose, and should not try
to.

*   **Ctrl+Shift+K** runs Check My Setup. On a healthy machine this finding stays silent, and that is all that has
    ever been observed.
*   If it **does** report a mismatched runtime on any machine you run this on, that is a genuinely useful sighting:
    please send the **exact wording**, and say whether pressing **Enter** on that row opened Microsoft's download
    page. It is the first row of the report by design, because that fault silences every accessibility mod in
    every game at once.

---

## 4. One pass per game

Load each game you own and walk the list. What to expect that is specific to each:

### The Witcher 3 (new)

*   The mod list should read **5 rows**, not 12: FMC Audio Remaster (version 1.1), Random Encounters Reworked,
    Shared Imports, Witcher Access, and **"Mod Group: Shared Utils. Contains 8 mods. Collapsed."** — this
    assumes a setup like the author's; on yours the count will differ, but a framework's many folders should
    collapse into one group row.
*   **Right Arrow** expands a group. The mods inside should read sensibly.
*   **Ctrl+H** should read your real controls, taken from `input.settings`.
*   If you use **FMC Audio Remaster**: it was installed one folder too deep and never loaded; that was repaired
    but not verified. Launch the game and confirm its audio changes are actually there, and that the game's own
    mod menu lists it once as `modFMCAudioRemaster` — not as a ghost entry called `mods`.

### Moonlight Peaks (new)

*   Mod versions should match what you actually have installed. If you have updated a mod since you last played,
    the manager should show the **new** version, not the one from before.
*   **F3** (Mod Documentation) should give you a settings reference built from each installed BepInEx plugin's own
    config file — a list of plugins to drill into, each with the author's own description of every setting.
*   **Ctrl+H** should read the game's keybindings.

### Skyrim Special Edition and Fallout 4

*   Load order, Creations, plugin slots (**Ctrl+Shift+U**), file conflicts (**Ctrl+Shift+F**).
*   **F4** opens the script extender log, **F5** launches the game.

### Stardew Valley

*   **F3** should offer the Stardew Access documentation, and it should still work with no internet connection.
*   Update checks: a mod SMAPI cannot place should be reported as unknown, not silently dropped.

### All six games

*   Open a mod's **settings** and confirm you are offered a **list to arrow through**, not a file to type into.
    This is the same feature in five different shapes and most of them have not been heard.
*   **Ctrl+H** (controls) and **F3** (mod docs) should either give you something or **say** they have nothing for
    this game. Silence is a bug; "no documentation for this game" is not.

---

## 5. Speech and keyboard

This is the part where the author cannot check his own work, because he knows what it is supposed to say.

*   **Mods should be called by their names.** Downloading, installing, "installed" and the mod list should all say
    the mod's name and nothing else — never `Address Library - All In One-47327-1-11-221-1780112703`, and never a
    string like `99824770-6ed9-4868-9f98-b54fb58ecad6`. A number the author put in the name themselves ("Mod
    Configuration Menu 1.11.221") is correct and should stay. Newly installed mods should land in folders named
    that way too; mods installed before this build keep their old folder names on disk by design, but should still
    be **read out** by their proper names.
*   **Every list:** arrow through it. Each row should announce its position **once** — not twice, and not with a
    stray word in front of it.
*   **The tab strips should be silent.** Moving along the main tabs, or the tabs inside Settings, should read the
    tab name and nothing else. If you hear an extra word — "Search" is the one that kept appearing — report the
    word and where you heard it.
*   **Screens that open inside the main window.** About thirty screens that used to be separate windows now open
    as panels inside the main one. For each you open: does focus land somewhere sensible, does **Escape** get you
    back to the mod list, and does the screen read with **its own** name? A panel that announces a name belonging
    to something else — or to the window behind it — is the specific bug this conversion can cause. Report the
    wording verbatim.
*   **F6** should cycle focus between the tab headers and the list.
*   **Alt+F4, or File → Exit.** You should hear the goodbye message **straight away**, then silence while it
    finishes, then a disconnect cue, then the program closes — about four and a half seconds. Press **Alt+F4
    twice quickly** and confirm the message is not cut short.
*   **Sounds.** Settings → Audio has an **Enable UI Sounds** toggle and six themes, including a new **The Witcher
    3** one. Try each theme briefly and say if any sound is missing, too loud, or clearly the wrong one.
*   **Ctrl+Shift+L** opens the manager's own error log, or says the log is empty. Either answer is correct;
    silence is not.

---

## 6. New in this release, worth a look

Not risky, but new, so worth your ears once:

*   **Help → Support Development (Donate)** — a note and two ways to give, with both addresses readable as text.
    Nothing should ever prompt you about this; it is only a menu item. Report it if anything nags you.
*   **Check My Setup (Ctrl+Shift+K)** — should read as an ordered list of findings, most important first, with
    **Enter** doing something useful on a row.
*   **Mod update coverage** — a report of which mods the manager can and cannot check for updates, and why.
*   **The INI editor**, **archive invalidation**, and **Purge** for deployment (Skyrim and Fallout 4).
*   **Mods that install to the game's root folder** rather than the mods folder.
*   **The FOMOD wizard**, **Collections** (Ctrl+Shift+N to install, Ctrl+Shift+X to export), and **MO2 import**.

---

## 7. Please do not report these

All known, all deliberate. Reporting them costs you time and tells us nothing new.

*   **The manager is English only.** It is fully translatable — about 1,470 phrases — but no translations exist
    yet.
*   **Vortex import is missing.** Deliberately held back until someone who uses Vortex can test it. MO2 import
    works.
*   **Witcher 3 mods say "version unknown" and name no author.** Witcher mod folders genuinely do not carry
    either. An invented "1.0.0" was worse, because it looked like something you could act on.
*   **A Witcher framework that still fills several rows instead of grouping.** Grouping follows the author's own
    `mod_family_*` naming; a mod that ships its pieces under unrelated names cannot be grouped without guessing,
    and guessing groups things wrongly.
*   **No disconnect sound when you exit without a game session open.** By design. Closing a session with
    **Ctrl+Shift+C** plays that cue, so exiting afterwards is silent. Do say if you would rather hear it on every
    exit — that is a preference, not a bug.
*   **`d3dx9_42.dll` reappearing after you delete it** — see 3.3.
*   **An empty or missing `profiles` folder** in the install directory.
*   **Cyberpunk 2077 is not supported.** Wanted; blocked on having the game to develop against.

---

## 8. How to report

For each problem, the five things that make it fixable:

1.  **Which game was loaded** (and which store's copy, if you have more than one).
2.  **What you did** — the exact key you pressed, or the menu path you took.
3.  **What you expected** to happen or hear.
4.  **What actually happened** — and if it was speech, the wording **as spoken**, as close as you can get it. If
    the answer is "nothing at all", say that; silence is a real symptom and one of the most useful ones.
5.  **Whether it happens every time** or you only saw it once.

Two files to attach when something goes wrong:

*   `%AppData%\AudiVentureGames\KinetixModManager\mod_manager_log.txt` — the manager's own log. **Ctrl+Shift+L**,
    or File → Open Error Log.
*   `%AppData%\AudiVentureGames\KinetixModManager\crash_log.txt` — only exists if the manager actually crashed. If
    it is there, it is the most useful file you can send.

Sort what you find into three buckets, and lead with the first one:

*   **It did the wrong thing to my files** — mods moved, lost, disabled, deleted, or a game folder changed. Send
    these first, and stop using that feature until you hear back.
*   **It said the wrong thing, or said nothing.** Wrong wording, a name that belongs to something else, a
    position announced twice, a key that does nothing in silence.
*   **It looked or read oddly, but nothing was harmed.** Worth sending, lowest priority.

Anything you found confusing counts as a finding, even where the program is working exactly as designed. If a
screen needed explaining, the screen is wrong, not you.

---

*Build 1.5.0, for testing. Not yet released — please do not share the installer on.*
