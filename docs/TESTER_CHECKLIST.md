# Kinetix Mod Manager 1.6.0 — tester checklist

Thank you for doing this. The headline of this build is **Minecraft (Java Edition), the sixth supported game** —
and with it, a manager that can start Minecraft, install it, update it, sign you in, move you to a new version
and install whole modpacks, without the official launcher ever being opened. Around that sit a lot of fixes to
the other five games: updates, logs, spoken prompts, and how mods are named and found.

Most of the Minecraft side has been used on exactly one computer, with one Microsoft account and one of the two
accessibility mods. That is why you are reading this: every setup that is not that one is new ground.

**What would help most:** section 1 first, then section 2, which is everything that changes files on your disk.
Then section 3 if you play Minecraft at all, and whichever parts of section 4 apply to you. Sections 5 and 6 are
a quick pass for a second sitting.

---

## 1. Before you start

*   **There is nothing to install.** Unzip the folder wherever you like and run `KinetixModManager.exe` from
    inside it. Your normally installed copy is untouched, and you can delete the folder when you are done. Only
    one copy can run at a time — if the test build seems to do nothing when you start it, close the installed one
    first.
*   ⚠️ **Back up your settings file, and put it back before you open your installed copy again.** This one is
    not optional this time. The file is
    `%AppData%\AudiVentureGames\KinetixModManager\settings.json`. Paste that path into File Explorer's address bar
    and copy the file somewhere safe. **The test build shares it with your installed copy** — the same settings,
    staged mods, downloads and backups — so you are testing against your real setup.
    *   **Why putting it back matters:** your installed copy is 1.5.1, which has never heard of Minecraft. Once this
        build has recorded a Minecraft install in that file, 1.5.1 can fail to start at all. Closing the test build,
        then copying your saved `settings.json` back over the new one, returns everything to how your installed copy
        left it. Anything you changed in Settings while testing, and a Minecraft sign-in, go with it. That is
        expected.
*   **Nexus downloads will come to the test build while you are testing.** The manager points Nexus's
    "Mod Manager Download" button at itself every time it starts, so whichever copy ran last owns it. When you are
    finished, restore your settings, then run your installed copy once and it takes the button back.
*   **Confirm you are on the right build.** Help → About should say **1.6.0**. If it says anything else, stop and
    say so — nothing below is worth testing on the wrong build.
*   **Note what you are running:** your screen reader and its version, your Windows version, whether your Nexus
    account is free or premium, which games you have and which store each came from — and, for Minecraft, which
    accessibility mod you use and which Minecraft version you are on.

---

## 2. Highest priority — anything that changes your files

### 2.1 Moving Minecraft to a new version

**Needs: Minecraft with mods, on a version older than the newest one your accessibility mod supports.** This
changes your game, installs files and switches mods off. It has been done on one machine, and the first attempts
broke that machine's game twice — which is how the checks now in front of it came to exist.

*   Run an update check (**Updates Available** tab). A newer Minecraft should appear **as a spoken question**,
    naming the version you are on and the one available — but only once your accessibility mod has a build for it.
*   Say yes, and before anything is written you should hear **what will happen to each of your mods**: which will
    update, and which have no build for the new version and will be **switched off**. Then a second question.
*   If the version is not on your computer yet, the manager offers to **download the game itself** from Mojang,
    telling you the size first. Java is included when the version needs a new one.
*   Afterwards: press **F5** and play. Then open **Check My Setup** (**Ctrl + Shift + K**) — a line starting
    "Your mods loaded" says which Minecraft and Fabric versions ran last time, and how many mods Fabric accepted.
*   **Report it if:** a mod was deleted rather than switched off, a mod you had switched off came back on, the game
    does not start, a world will not load, or anything was changed before you said yes the second time.
*   Reassurance: nothing is deleted. A switched-off mod is renamed to `.jar.disabled` and is back in one keypress,
    and every mod is backed up before an update replaces it. The previous version's files stay on disk.

### 2.2 Tidy Mod Folder Names

**Needs: a game whose mod folders are named like `Hunterborn-7900-1-6-2`** — any Skyrim, Fallout 4 or Moonlight
Peaks setup that has had mods from Nexus for a while.

*   **Mods → Install and Update Mods → Tidy Mod Folder Names.** A preview of every rename should open first, and
    nothing should change until you say yes.
*   Afterwards check the things that hang off a folder name: **Mod Priority** and **Plugin Order** unchanged, any
    **profile** still switching the right mods, any mod you had **switched off** still off.
*   The Witcher 3: folders must keep their `mod` prefix, or the game stops loading them without a word.
*   **Report it if:** a load order resets, a disabled mod comes back on, or a game stops seeing a mod.

### 2.3 Updates

*   **Update All (Ctrl + U) on a free Nexus account** should update everything that does not come from Nexus —
    every Minecraft mod, and anything from GitHub — and say beforehand how many Nexus ones it will skip.
*   **Enter on an update** should offer **update it now** or **open its page** when the manager can fetch the
    update itself. A Nexus update still opens its page.
*   **A mod you had switched off stays off** after it updates, and you are asked whether to switch it on.
*   **An update should never go backwards.** One was found installing a six-week-older build of a Skyrim plugin
    and stopping the game starting. After updating, glance at the version of anything that misbehaves.
*   **Report it if:** an update installs an older version, lands in the wrong game, or a disabled mod comes back on.

### 2.4 Deleting keeps a backup

*   Delete a mod, then look in **Backups**: the copy should be there. For **Minecraft** this was broken — a mod is
    a single `.jar` file, and the backup quietly did nothing while saying it had been kept.
*   If a backup cannot be made, the delete should **stop and say why** rather than go ahead.

---

## 3. Minecraft

Load Minecraft from the **Games** menu. The manager expects the normal `.minecraft` folder.

### 3.1 Setting up from nothing

*   **Mods → Game and Maintenance → Install Minecraft Accessibility Suite.** It asks **which accessibility mod**
    first — United Minecraft or Minecraft Access — then shows exactly what will be installed.
*   ⚠️ **Minecraft Access is the valuable one to try.** The author plays with United Minecraft; the Minecraft
    Access path has been built and tested but not played through.
*   Fabric installs with no installer window, for a Minecraft version your accessibility mod supports. If that
    version is not on your computer, you are asked about downloading it, with its size.
*   **Listen to the install itself:** nothing should be said twice, and one summary at the end should say what was
    installed and what was not. You should land in the mod list afterwards, with the new mods in it.

### 3.2 Starting the game (F5)

*   **F5 starts the game itself**, with Fabric and your mods, and says **"Starting Minecraft online as"** or
    **"offline as"** your name.
*   You should be **your own character** in your worlds — same inventory, same place — whichever mode you start in.
*   If the game is missing files it needs, it fetches them first and says how many. Minecraft installs are
    sometimes missing sounds, and nothing else would ever have said so.
*   After playing: **Check My Setup (Ctrl + Shift + K)** should say whether your mods loaded, even when they did.

### 3.3 Signing in, and online or offline play

**Needs: a Microsoft account that owns Minecraft Java Edition.** This has run on one account, once. Every other
account is new, and the unusual ones are the most valuable.

*   **Mods → Game and Maintenance → Minecraft Account.** The menu entry itself should already say the mode — for
    example "Minecraft Account: offline play, not signed in".
*   **Sign in with Microsoft.** The code should be spoken one word per character ("Alfa, Bravo, One").
    **Open the sign-in page** opens microsoft.com/link with the code already copied. Sign in there. The screen
    should **close by itself** when you finish, then ask whether to play online from now on.
*   **Say yes, and listen:** the connect sound, "Connected to Minecraft as" your name, and the title bar (NVDA+T)
    saying the same. Close the manager and reopen it: it should connect again by itself on the way in.
*   **Switch to offline play** in the same screen: the disconnect sound, and the title saying "Offline play".
    Switch to another game and back: the title should still say which mode you are in.
*   **Online, try a server or a Realm.** That is the whole point of online play, and it has not yet been tried.
*   **Worth reporting word for word**, if your account is one of these: a child or family account, an account that
    has never had an Xbox profile, an account in another country, or an account different from the one the
    Minecraft launcher uses. Each should get a plain sentence saying what to do. None should show an error code.
*   **Report it if:** anything you hear or see includes a long string of letters and numbers that looks like a
    password. It should never happen, and it matters more than anything else in this section.

### 3.4 Mods

*   **Search for Mods** uses Modrinth — no Nexus key needed. **Enter** on a result offers download and install,
    the full description, or the mod's page.
*   **Update check** matches your mods by their files, so nothing needs linking. There may be a **Fabric Loader**
    row as well.
*   **Ctrl + Q** looks up missing requirements on Modrinth and installs them.
*   **Ctrl + E** on a mod offers its settings as a list, where the mod describes them.
*   **Ctrl + H** reads your real controls, including your accessibility mod's. On Minecraft 26.3 the key names
    changed underneath; if a key is named wrongly, say which.
*   **F3** reads your accessibility mod's documentation. **F4** is "Open Minecraft and Fabric Log".
*   **Ctrl + Shift + P** lists mod `.jar` files you downloaded and never installed.
*   **Sounds:** joining a server plays the connect sound and leaving it the disconnect. Your own worlds play
    neither.

### 3.5 If you only play Minecraft

*   **Try it on a setup that has never had a Nexus key.** The manager used to demand one and then refuse to show
    your mods or search without it. It should simply work.

### 3.6 Modpacks

**Brand new in this build, and the newest thing in it.** Everything here has been built and checked in code, but
not yet played through by anyone. A modpack is a whole setup — its own mods, settings and worlds — and each one
gets a folder of its own, so trying one should never change your own Minecraft. **If it ever does, stop and say
so first.** A good pack to try is **Visually Impaired Access Mods+Fabric**, made for blind players; it uses
Minecraft Access.

*   **Install one.** On the new **Minecraft Packs** tab (right after Installed), choose **Search for Modpacks**,
    search, and press Enter on a pack. Or download a `.mrpack` file from Modrinth and use **Install from file**.
    You should hear one question with the pack's version, its Minecraft and Fabric versions, and the download
    size — then, once it is in, **whether it will speak**, an offer to copy one of your worlds in, and "Play now?"
*   **Play it.** It should start, speak, and **stay running**. ⚠️ The very first try of this closed three seconds
    after starting; that is fixed. If a game now closes by itself, you should hear **why** — please send that
    sentence word for word.
*   **Close the manager and reopen Minecraft.** It should ask **which Minecraft**: your own, or the pack. Pick each
    in turn. In the pack's session: the title bar should name the pack, the Installed tab should list the pack's
    mods (not yours), and F5 should say the pack's name.
*   **Back in your own Minecraft**, check your own mods and worlds are exactly as they were.
*   **Copy a world in** (Enter on the pack, then Copy one of your worlds into it). A world from a newer Minecraft
    than the pack should get a warning first. Your original must be untouched.
*   **In the pack's session, check for mod updates**, then try **Update All**: it should ask whether to update only
    the mods you added, or the pack's too — and Enter on one of the pack's own mods should warn before updating it.
*   **Switch a pack mod off, then update the pack** if an update is offered. The mod you switched off should
    **stay off**, and your own changes to the pack's settings should survive. You are told where the backup went.
*   **Import from Modrinth App** (on the tab, or Game and Maintenance) — if you have the Modrinth App with a pack in
    it. It should copy the pack across and leave the Modrinth App's copy alone. Say what it listed, even if it is
    only "nothing found".
*   **Only if you are finished with the Modrinth App:** Game and Maintenance → **Remove Modrinth App Leftover
    Files**. With the app still installed it should offer Windows' uninstaller; afterwards it should name any pack
    you have not brought across before asking, and send the folders to the Recycle Bin.
*   **Delete a pack** (Delete on the tab). The question should name its worlds, and the folder should go to the
    Recycle Bin.
*   **Game and Maintenance → Remove Unused Minecraft Versions.** It should list only versions nothing uses, never
    the one your own Minecraft plays or any pack's, each with its size. Remove one, then check your own Minecraft and
    your packs still start.
*   **Report it if:** anything happens to your own Minecraft, a pack starts without speaking when it said it would,
    a mod you switched off came back on, or a pack's session lists mods that are not the pack's.

### 3.6a Low memory (any game)

*   Before a game starts, the manager now checks the computer has memory to spare. You should normally hear nothing
    new. **If you ever hear a low-memory warning, please send it word for word**, with what was open at the time.

### 3.7 A Modrinth key (optional)

**Needs: a Modrinth account** — or making one, which is worth reporting on in itself: say how Modrinth's sign-up
and token pages went with your screen reader.

*   **File → Mod source API keys.** Modrinth should be listed as **optional**, not as something missing.
*   Press Enter on it, and follow what the prompt says to make a token on Modrinth's site. Paste it in. You should
    hear **"Connected to Modrinth as"** your name, or a plain sentence saying it was not accepted.
*   On the search tab, set **Search for** to **Followed on Modrinth**: the mods and packs you follow should be
    listed. Press Enter on any Modrinth result: there should be **Follow on Modrinth** or **Stop following**.

---

## 4. Needs a particular setup

Each item says what it needs. Skip the ones that do not apply.

### 4.1 Downloading from Nexus on a free account

**Needs: a Nexus account that is not premium.** Install any mod from Nexus through the manager. The Nexus page
should open showing the right file; press **Mod Manager Download** there, and the download should come back into
**Kinetix** and install. If Vortex takes it, or nothing happens, say so — and say what you had to do.

### 4.2 A download for a game you do not have open

Press **Mod Manager Download** on a Skyrim mod while Stardew is loaded, and again with no game loaded. You should
be asked whether to switch games and install now or save it for later, and the question should be heard **before**
the Yes button.

### 4.3 Skyrim that will not start

**Needs: a Skyrim that hangs or closes on launch.** Do not break one on purpose. If yours does, **Check My Setup**
should name the plugin it stopped on, at the top of the list. Please send its exact words.

### 4.4 Skyrim 1.7.99 or newer, with SSE Engine Fixes

The preloader (`d3dx9_42.dll`) is no longer needed from 1.7.99, and the manager should stop asking for it. If it
is still in your game folder, **Check My Setup** should list it under **"No longer used"** and offer to move it to
the Recycle Bin — only if the Engine Fixes you have installed is 7.0.21 or newer.

### 4.5 A GOG copy of Skyrim

Install SKSE from the suite and confirm you get the **GOG** build: press **F5** and check SKSE does not complain.
Update checks should keep offering GOG builds too.

### 4.6 A mod from GitHub, and your site keys

*   **Mods → "Install a mod from a GitHub repository..."** Type `owner/repo`, or paste the repository's address.
*   **File → "Mod source API keys..."** Arrow up and down: **your keys must never be read aloud** — only site names
    and whether a key is saved.
*   **Settings → Paths & Account → "When searching for mods"** has three modes for Minecraft and Stardew. The
    other four games show one line explaining why they have no choice.

---

## 5. A quick pass through the other five games

These all shipped before, so this is a check that nothing broke.

*   **Every game:** enable, disable, install from a zip (**Ctrl + I**, and a `.7z` if you have one — they no longer
    need a downloaded helper), **Ctrl + E**, **Ctrl + H**, **F3**.
*   **F4** should be named for each game's own log — SMAPI, the script extender, BepInEx, or the Witcher mod log —
    and open it **inside the manager**, never in Notepad.
*   **Search results** should say when you last downloaded a mod ("You downloaded this 4 days ago") and when it was
    last updated.
*   **The Witcher 3:** deleting a mod must not offer to run **WitcherAccess's** uninstaller.
*   **Moonlight Peaks:** switch off two mods; they should read as themselves, not as a group with no name.
*   **Skyrim:** the suite no longer includes Stay At The System Page. If you have it, it stays installed.

---

## 6. Speech and keyboard

The author cannot check this part himself, because he knows what it is supposed to say.

*   **Prompts:** the question first, then the button — including a prompt that appears after a browser download.
*   **Lists you work through keep you in them:** **Ctrl + Shift + W**, **Ctrl + Shift + P**, and the rows of
    **Check My Setup**. Deal with one item and you should stay in the list, on the next item.
*   **The title bar is never stale.** After any download or install, NVDA+T should not still say "Downloading".
*   **Shortcut Customization** (Help menu) reads each command by its menu name, not a code name.
*   **Saving a file** (an export, say) ends with a box giving the file name and the folder it went to.
*   **Every list:** each row announces its position once, with no stray word in front.

---

## 7. Please do not report these

All known, all deliberate.

*   **The manager is English only.** It is fully translatable; no translations exist yet.
*   **Vortex import is missing.** Held back until someone who uses Vortex can test it. MO2 import works.
*   **CurseForge is listed and cannot be used.** It needs an application key CurseForge has not issued.
*   **Nexus's "sign in on the website" button is not there.** It is built and waiting on Nexus's approval.
*   **Witcher 3 mods say "version unknown" and name no author.** Witcher mod folders do not carry either.
*   **Mods installed before this build keep their old folder names** until you run Tidy Mod Folder Names.
*   **United Minecraft has no wiki or guides yet**, so only Minecraft Access contributes documentation.
*   **F5 does not go through the official Minecraft launcher.** The manager starts the game itself, on purpose:
    the launcher can quietly start a world without your mods.
*   **Cyberpunk 2077 is not supported.**

---

## 8. How to report

For each problem, the five things that make it fixable:

1.  **Which game was loaded** (and which store's copy, if you have more than one).
2.  **What you did** — the exact key you pressed, or the menu path you took.
3.  **What you expected** to happen or hear.
4.  **What actually happened.** For speech, **your screen reader's speech history** beats any description —
    NVDA's Speech Viewer, or its history, copied as it stands. If the answer is "nothing at all", say that; silence
    is a real symptom.
5.  **Whether it happens every time** or you only saw it once.

Files to attach when something goes wrong:

*   `%AppData%\AudiVentureGames\KinetixModManager\mod_manager_log.txt` — **everything** goes here now, crashes
    included, and each session starts by saying which build wrote it. **Ctrl + Shift + L** opens it. If there is a
    `mod_manager_log.1.txt` beside it, send that too.
*   For Minecraft: `%AppData%\.minecraft\logs\latest.log`, from the run that went wrong. It lists which mods loaded.

Sort what you find into three buckets, and lead with the first one:

*   **It did the wrong thing to my files** — mods moved, lost, disabled, deleted, or a game changed. Send these
    first, and stop using that feature until you hear back.
*   **It said the wrong thing, or said nothing.**
*   **It looked or read oddly, but nothing was harmed.** Worth sending, lowest priority.

Anything you found confusing counts, even where the program is working exactly as designed. If a screen needed
explaining, the screen is wrong, not you.

---

*Build 1.6.0, for testing. Not yet released — please do not pass it on.*
