# Unreleased

## 🐧 Linux: every game can be searched and update-checked now

*   **Nexus Mods works from the Linux build.** It was the last thing keeping five of the six games at arm's
    length there — searching for a Skyrim or Stardew mod, and checking what you have installed for updates,
    both needed a part of the manager that had never left the Windows side. It has now, and nothing about it
    changed on the way: the code turned out to have been portable all along.
*   Searching a Nexus game from Linux needs your API key, the same as on Windows, and says so if you have not
    added one yet.
*   **Nothing changes on Windows.** This is the same code in a different project.

## 🐧 Linux: a Dependencies screen, and games installed with Heroic are found

*   **A Dependencies screen.** It lists only what is actually wrong — a mod whose requirements are all present
    and switched on does not appear, because a list where the hundreds of things that are fine bury the three
    that are not is no use to anybody, and least of all by ear. Rows are ordered by how much each one matters,
    and each says the problem before it says any names: missing, switched off, too old, then optional extras.
*   **Games installed through Heroic are found now.** The manager only ever looked where Steam puts things,
    which meant a GOG or Epic game installed with Heroic — a perfectly ordinary folder — read as not installed
    at all. It reads Heroic's own record of what it has installed, including the Flatpak version of it.
*   Still not found: games managed by **Lutris**. Reading its library needs something the manager cannot do
    yet, and guessing at it would be worse than saying so.

## 🐛 A mod could be deleted when its backup had failed

*   **Windows and Linux both.** Deleting a mod takes a backup first — that is the whole reason deleting is
    offered at all. If that backup failed, for any reason at all (a backups folder that cannot be written to,
    a full disk, a file another program has open), the failure went into the log and **the mod was deleted
    anyway, with the manager reporting that a copy had been kept**. Nothing was thrown and nothing was said.
    Deleting now stops when the backup does, and tells you why.
*   **Two backups of the same mod in the same second** used to collide, and the second one failed — which,
    with the fix above, is now enough to refuse a delete. They no longer collide, however many happen at once.

## ✨ Linux: Backups and a Log screen

*   **A Backups screen.** The copies kept before anything was overwritten or deleted, newest first — which is
    what you want, because you are looking at this list after something went wrong and the copy you want is
    almost always the last one taken before it. Restoring asks twice and tells you first whether it would
    replace what you have installed.
*   **A Log screen**, showing what your game's mod loader said last time it ran. It opens on **errors and
    warnings** rather than the whole log, because a SMAPI log is thousands of lines of a game starting
    normally, and each line carries its suggested fix with it rather than hiding it behind another keypress.

## 🐛 Deleting a Minecraft mod kept no backup, and said it had

*   **This one affects Windows too.** The manager backs a mod up before deleting it, and that backup is made
    by zipping the mod's **folder**. A Minecraft mod is not a folder — it is a single `.jar` file — so the
    backup step quietly did nothing and the delete went ahead reporting that a copy had been kept. Nothing
    failed and nothing was logged; the only way to find out was to want the mod back. Mods that are a single
    file are now backed up properly.

## 🐧 Linux: deleting, backing up, notes, and a warning where you will hear it

*   **Delete a mod from the installed list.** It asks twice, in the list rather than in a pop-up window:
    Delete once says what would go, Delete again does it, and any other key — or simply moving to another mod
    — calls it off. A backup is taken first and finished before anything is removed.
*   **Ctrl+B** backs up the selected mod without deleting it, and **Ctrl+N** reads out any note you have
    written for it. Notes are shared with the Windows build, so one written there is the same note here.
*   **The games list now tells you which games will not talk to you**, instead of only mentioning it in
    Settings. Settings is somewhere you go once; the games list is where you choose what to spend an evening
    on. It also says plainly that the mods for those games are still managed — the warning is about the game
    being silent, not about the manager refusing to help.

## 🐧 Linux: Updates and Profiles, and Minecraft is just a game in the list

*   **Minecraft is no longer written into the Linux build.** It was the version number (fixed at 1.21.1, which
    was quietly wrong for anybody on a different one), the game the window opened on, the only place it knew
    how to search, and the only game it would install for. All four now come from the game you have loaded and
    the settings you have chosen — Minecraft is an entry in the list like the other five.
*   **An Updates screen.** It checks your mods by their file contents rather than by a link stored somewhere,
    so nothing has to have been matched to a mod page first, and a mod it has never seen is simply not
    mentioned rather than reported as a problem. It tells "everything is up to date" apart from "nothing here
    could be checked", which look the same on screen and mean very different things.
*   **A Profiles screen.** Save the mods you have switched on as a named setup and switch between them. Before
    it changes anything it says what it would do — how many mods would go on, and how many off.
*   *(Written before Nexus Mods worked on Linux — see the entry above, which arrived later the same day and
    lifted this limit entirely.)*

## 🐧 Linux: settings, a Settings screen, and a warning that matters

*   Still nothing that changes the Windows build, except one thing worth knowing: **your shortcuts are stored
    the same way they always were.** The settings file moved into the shared half of the program, and the
    format is byte-identical, so an upgrade reads everything back unchanged.
*   **The Linux window remembers things now.** Which game you were on, where its mods are, whether you want
    sounds and how loud. Before this it worked all of that out afresh every time it started.
*   **It has a Settings screen** — the mods folder for the loaded game, with what was found automatically
    shown beside it, sounds and volume, and which site to search for mods.
*   **⚠️ It now tells you which games will not talk to you on Linux.** Skyrim, Fallout 4, The Witcher 3 and
    Moonlight Peaks have their mods managed perfectly well here, and then play in silence, because their
    accessibility mods drive NVDA or JAWS and neither runs under Proton. Minecraft and Stardew Valley speak
    natively. That is a fact about those mods rather than about this manager, and the manager saying so is the
    difference between knowing that up front and finding out after an evening of installing.

## 🐧 Linux: the manager can now speak, sound, and keep a secret

*   Nothing here changes the Windows build. It is the Linux side catching up, and it is listed because the two
    share a core — three of the fixes below were in code both builds run.
*   **Sounds work on Linux**, through GStreamer, with the same per-game themes and the same fall back to the
    Default theme for a sound a theme has not recorded.
*   **Keys are stored encrypted on Linux**, through the system keyring that GNOME Keyring and KWallet both
    answer to — the equivalent of what Windows has always done with DPAPI.
*   **The Linux window speaks every sentence through the same phrase catalogue the Windows one uses**, so it
    can be translated, and a missing phrase now fails the build instead of going quiet.

## 🐛 Three Linux bugs, two of which only a screen reader user would ever hit

*   **A switched-off mod read as switched on for every game except Minecraft.** The Linux window worked out
    whether a mod was off using Minecraft's rule — does the file end in `.jar` — while switching one off used
    the right rule for the game. So a disabled Stardew mod was announced as enabled, and turning it on again
    did nothing. Both now ask the same place.
*   **A game you have not installed was announced as "0 mods installed"**, which sounds exactly like a game
    that is installed and empty. One of those means go and install something; the other means the manager does
    not support your game. They now say three different things: not installed, no mods folder yet, and no mods.
*   **Minecraft read as missing on Linux even when it was there.** The manager looked for `.minecraft` in the
    wrong folder — `~/.config/.minecraft`, which no Minecraft install has ever used, instead of `~/.minecraft`.

## ✨ You choose where your mods come from

*   **A new setting on the Paths tab: "When searching for mods."** Three options — search **every** source and merge the results, search **one** source only, or search your **preferred** source with the others available on request. Choosing one of the last two shows a second dropdown naming which source that is, and it is remembered **per game**, because the sites are: Minecraft's mods live on Modrinth and CurseForge, Stardew's on Nexus, ModDrop and CurseForge.
*   **Nothing changes until you ask it to.** The default searches your preferred source only, which is exactly what the manager did before — Modrinth for Minecraft, Nexus for everything else.
*   In the preferred-source mode, when there is somewhere else worth asking, the manager says so and **Alt+O** in the Discovery list searches those too.
*   **When more than one source answers, every result says which one it came from** — and when only one was asked, it does not, because the same three words on a hundred rows tell you nothing. A mod that turns up on two sites is shown once, from whichever you prefer.
*   **A source that cannot answer says so out loud** instead of leaving a quiet gap in the results. A site being down or a missing key reads as what it is, not as "there are no mods".
*   **Skyrim, Fallout 4, The Witcher 3 and Moonlight Peaks get no dropdown**, and one sentence saying why: Nexus is the only place with a searchable catalogue of their mods. Bethesda.net, ModDB and LoversLab have no way in for a program like this one. A choice with one option in it is not a choice.
*   **CurseForge is listed, and says why it cannot be used yet.** It is a real second catalogue for Minecraft and Stardew, and it needs an API key that CurseForge issues to approved applications — a conversation rather than a setting, like the Nexus sign-in. It is there so you can see the manager knows about it and what is in the way.

## ✨ Install a mod straight from GitHub

*   **Mods → "Install a mod from a GitHub repository..."** Type `owner/repo`, or just **paste the address** of the repository's page from your browser — the clone command and the releases page work too. The manager finds the newest release, downloads the right file and installs it exactly as it would a file you picked yourself: backups, the FOMOD wizard, and a record of which release went on so it can tell you about the next one.
*   GitHub is not in the source dropdown, and that is deliberate — there is no catalogue to search. You cannot ask GitHub for "Stardew mods about fishing". What you can do is name a repository, and plenty of mods are released there and nowhere else.
*   **It knows which file is the mod.** A Fabric mod publishes `sodium-0.6.0.jar` and `sodium-0.6.0-sources.jar` side by side, and installing the second gives you a mods folder that looks right, a game that starts and no mod at all. Release notes, checksums and signatures are skipped, and a `.zip` is preferred to a `.7z` or `.rar` when a release offers all three.
*   When there is nothing installable, it says which of the two reasons applies: no such repository, or a repository whose author has never published a release.

## ✨ One place for your mod site keys

*   **File → "Mod source API keys..."** lists every site that asks for one and whether you have given it. Press **Enter** on a site: if it has no key, you are asked to type one; if it has, the key is shown in a **read-only box** so you can check it against what you meant to type. **Edit key** asks for a new one either way — which is the point, because a key typed wrongly months ago cannot be fixed by a screen that only shows it back to you. **Delete** forgets one, after asking.
*   **Arrowing down the list never reads your keys out loud.** A row says the site's name and whether a key is saved, nothing more. The key itself is only spoken on the one row you open — a credential read out in passing is read out in whatever room you are sitting in.
*   There is also a button that **opens the site's page for getting a key**, so you are not left to find it.
*   **Sites that need nothing are not listed.** Modrinth and GitHub need no account at all, so three rows saying "nothing to do here" would only be in the way.
*   **Keys are stored encrypted**, the same way your Nexus key and your AI provider key already were.
*   **Sites with a login rather than a key end up with a key too** — that is what a login is for. Nexus can sign you in on its own website and hand the manager a key without you typing one, which is better because the manager never sees your password and you can cancel the key without changing it. That is built and waiting on Nexus approving the manager as an application; until then the screen says so plainly rather than offering a button that cannot work.

## ✨ Minecraft has its own sounds, and its connect cue means joining a server

*   **The sound theme has always followed the game you load** — Skyrim sounds like Skyrim, Stardew sounds like Stardew, so you know which session you are in before anything is read out. Minecraft was the one supported game with no theme of its own. Its folder is now there: drop `.ogg` files into `sounds\Minecraft\connect\`, `\error\` and the rest and they are picked up, with **no setting to change and nothing to register**. Each folder has a note in it saying what belongs there, and `sounds\README.txt` explains the whole convention for anyone making a theme.
*   **A sound you have not recorded yet plays the Default theme's**, one sound at a time. That already worked when a folder was missing — but a folder that existed and was *empty* made the manager go silent for that event instead, which is the normal state of a theme somebody is halfway through making. Fixed, and now tested.
*   **The connect and disconnect sounds now mean something real in Minecraft.** For every other game they say the manager has signed in to Nexus Mods. Minecraft's mods come from Modrinth, which has no accounts, so those cues had nothing to say. They now follow the connection a Minecraft player actually has: **connect when you join a multiplayer server or a Realm, disconnect when you leave it** — including being dropped, kicked or timed out, and quitting the game while still on a server. Hopping straight from one server to another plays both, so you can hear the first one end.
*   Your own worlds make no sound either way. A singleplayer world is not a connection to anything.
*   It is a **sound and nothing else** — no spoken announcement. The game is in front of you with its own accessibility mod talking, and the manager speaking over it would be worse than saying nothing.

## 🔒 A mod archive can no longer point at your own files

*   Mod archives were checked to make sure nothing in them escaped the folder they were unpacked into — but only by looking at the names inside. A **link** (a shortcut the filesystem follows) named something ordinary like `textures`, pointing at your documents, passed that check, and what happens next in an install is copying that folder into the game. Links are now followed and refused if they lead anywhere outside.
*   The check used to run on one install path. It now runs on all of them, including the script extender and the Engine Fixes preloader.
*   No archive from Nexus or Modrinth has ever been seen doing this. It was possible, which was enough.

## 🐛 A mod hosted somewhere other than Nexus or GitHub went missing

*   The manager asks SMAPI's mod database about your Stardew mods, and that database knows about **ModDrop**, **CurseForge** and **Chucklefish** as well as Nexus and GitHub. It would hand back a mod's newest version and its page, and the manager kept only the Nexus and GitHub ones — so a mod hosted anywhere else was then reported to you as one it **could not track**, moments after being told exactly where the mod was. Mods now keep their page whatever site it names, it is remembered between runs, and Check My Setup counts them as covered.

## 🐛 A mod from a site the manager could not download from did nothing at all

*   Pressing **Enter** on a search result decided what to do by asking "does this have a Modrinth id", which really meant "is this Minecraft". A result from anywhere else fell through to opening its page, and opening the page only knew about Nexus and Modrinth — so a result from any other site did nothing. Enter now asks what the site can actually do, and anything the manager cannot fetch itself opens its page, which is the one thing every site can do.

## 🐛 Minecraft asked for a Nexus key it never uses

*   Opening the manager with Minecraft loaded and no Nexus API key played the **disconnect** sound, forced the Settings window open and left the mod list empty. Nothing you could have typed would have fixed it, because Minecraft's mods come from Modrinth and the key is never used for them. Only games that actually get their mods from Nexus ask for one now.

## 🐛 Minecraft mods could not be discovered without a Nexus account

*   Opening the **Discovery** tab with Minecraft loaded and no Nexus API key said "log in first" and searched nothing — sending you off to sign in to a service your game has no relationship with. Minecraft's mods come from Modrinth, which has no accounts. It searches now.

## 🐛 Two messages that read out their own punctuation

*   The **duplicate UniqueID** line in Check My Setup was written to say "UniqueID *X* is used by *N* mods: *list*", but was never given the count. String formatting failed, the failure was caught, and the sentence came out as the raw template — so the screen reader read the braces aloud: "UniqueID open brace zero close brace is used by open brace one close brace mods". It now says the number.
*   The **manual download** dialog, the one that opens when your Nexus account is not Premium, had the title "Download {0}" for the same reason. It now names the mod.

## 🐛 A profile with a slash or a dot in its name

*   Profiles were saved to a file named after whatever you typed, with nothing checked. A profile called **"Mage/Thief"** failed to save at all; one ending in a dot or a space saved under a name Windows then refused to open. Worst of the three, saving and deleting disagreed about the name — so a profile could appear in the list and then refuse to be deleted. All three are fixed, and the same rule is now used everywhere a name becomes a file.

## 🔧 Under the bonnet: .7z and .rar mods no longer need a downloaded helper

*   Mods that arrive as **.7z** used to be unpacked by `7za.exe` — a program the manager quietly downloaded from 7-zip.org the first time you installed one, and then ran. It is now unpacked by the manager itself.
*   What that fixes for you: installing a .7z mod no longer needs a working internet connection at that moment, no longer fails behind a company firewall or a fussy antivirus, and no longer depends on a helper program from 2010 that could not read some newer archives at all.
*   **Solid archives** — the kind where every file shares one compressed block, which is most .7z mods — are now read straight through instead of file by file. A large mod that used to sit there looking frozen now extracts at the speed it should.
*   A mod archive written on Windows keeps its folder structure when unpacked anywhere else. This makes no difference on Windows and is the difference between a working and a silently broken install elsewhere.
*   All of this moved into **Kinetix.Core**, so unpacking a mod is no longer something only the Windows app knows how to do.

## 🔧 The connect sound is for connections

*   Four places played the **connect** sound for things that connect to nothing: a load-order sort that changed something, the AI provider test passing, and an app or SMAPI update being found. All four now play **load complete**, which is what they are. It also means the connect cue is worth listening for again.

## 🔧 Under the bonnet: the screen reader is now one replaceable part

*   Nothing you can see changed, and nothing you do works differently.
*   Every spoken word in the manager — all five hundred-odd places that say something — used to call **Tolk**, the bridge to NVDA and JAWS, directly. They now go through a single seam, and Tolk is named in exactly two files instead of being spread through the app.
*   The same was done for sounds, for the way your API key is encrypted, and for the plumbing that gets background work back onto the window.
*   Why it matters: those four are the pieces that only exist on Windows. Isolating them is what makes a version of this manager for another operating system possible at all — and on Windows, everything behaves exactly as it did.
*   The progress feedback (the rising tone, the "20 percent", the title bar) moved out of the window entirely and is now covered by its own tests, which could not be written before because testing it meant opening the app.

## 🔧 Under the bonnet: the core is its own project now

*   Nothing you can see changed here, and nothing you do works differently.
*   The parts of the manager that have no user interface — game profiles, mod scanning rules, FOMOD, Modrinth, the Minecraft launcher and Fabric installer, the save and INI readers — now live in a project of their own, **Kinetix.Core**, instead of being mixed in with the window.
*   They were already being built separately: the test project had been listing fifty files by hand for months so the tests would not have to load WebView2, NAudio and Tolk. That hand-written list is now a real project reference, so a new file is picked up by itself rather than when somebody remembers.
*   The point of the split is that Kinetix.Core cannot reach Windows-only things even by accident — the compiler will not let it. That keeps the rules honest, and it is what a version of this manager for another operating system would be built on.

## 🧭 Paths that were spelled the Windows way

*   Seven places wrote a folder path with backslashes in it — the Moonlight Peaks mods folder, its keybind export, The Witcher 3's executable, United Minecraft's keybind file, and three others. Each is now assembled properly. **No difference on Windows**; it is what the same code would need anywhere else.
*   Folder-name cleaning asked the computer which characters were forbidden, rather than which characters *the game* forbids. On Windows those are the same question. They are not everywhere, and the mod folders this manager makes are read by Windows games. The rule is now stated outright.

## ✅ Tests

*   **1,398 passing**, up from 1,009 — and all of them now pass off Windows too, where sixteen used to fail.
*   Much of that is mod scanning, backups, profiles, dependency checks and the update decision, none of which could be tested before: they lived inside the window, and the test project deliberately does not load the window.
*   A new guard catches the bug at the top of this list: it fails the build if any message is given fewer values than it has placeholders. There were two.
*   Twenty-six of them cover unpacking a downloaded mod, including one that extracts a **real .7z** rather than a stand-in — the only way to know that reading one without `7za.exe` actually works.
*   A new guard fails the build if a game asks for a sound theme that is not there, or if a theme is missing a folder — the kind of gap that plays the Default sounds and is never noticed.

---

# Version 1.6.0

## ✨ New: Minecraft (Java Edition) is the sixth supported game

*   Mods, updates, controls, documentation, profiles and launching — Minecraft now has all of it, with **United Minecraft** or **Minecraft Access** as your accessibility mod.

### 🚀 You do not need the Minecraft launcher

*   **Press F5 and the manager starts the game itself**, with Fabric and your mods loaded, going nowhere near the launcher.
*   ⚠️ **This is the whole reason Minecraft support exists.** In the launcher, choosing an installation and launching a world are two separate things, and **the second quietly overrides the first**. You can select the Fabric installation correctly, launch a world from the home screen, and get an unmodded game — because the world tile remembers whichever installation it was first played with. Nothing tells you. The game starts, plays perfectly, and simply never speaks. This was found the hard way, twice, before a line of code was written.
*   You still need the launcher **once**, to sign in. After that the manager plays as **you** — your own username and your own character — so your worlds, inventory and advancements are exactly where you left them. Servers and Realms need a live sign-in and are not available this way; singleplayer is unaffected.

### 🔧 Fabric installs itself, with no installer to run

*   Fabric is Minecraft's mod loader and nothing loads without it. The **Accessibility Suite Installer** sets it up directly — no installer program to download, no graphical window to get through, no administrator prompt.
*   It installs for a Minecraft version your accessibility mod actually supports, **not simply the newest one**. Fabric is usually ready for a new Minecraft version weeks before the mods are, and installing for a version nothing is built for gives you a game that starts perfectly and stays silent.
*   A Fabric you installed yourself, before you ever had this manager, is recognised and used rather than reported missing.

### 🧩 Two accessibility mods, and you are never asked about the plumbing

*   **United Minecraft** (newer, updated more often) or **Minecraft Access** (older, and what many blind players have used for years). The suite asks once and remembers.
*   They need different things installed alongside them — one needs Fabric API as a separate file, the other carries its dependencies inside itself — and **you are never asked about any of that**. Both facts are read from the mods themselves, so they stay right when either mod changes.
*   Already have one of them? The manager notices and uses it, rather than offering to install its rival over the top.

### 🔍 Mods come from Modrinth, so no Nexus key is needed

*   Searching, installing and updating all work as they do for any other game, but through **Modrinth**. **No Nexus API key is required**, and the key box is hidden while a Minecraft session is open.
*   ⚠️ Previously an empty Nexus key **blocked the entire Settings dialog from saving** — so somebody who only plays Minecraft could not change their sound volume without first pasting a key they would never use. The key is now asked for only when the loaded game actually needs one.
*   Press **Enter** on a search result to choose: download and install, read the full description, or open the mod's page.
*   Only mods with a build for the Minecraft version you are on are offered. A mod built for another version installs cleanly and then loads nothing at all.
*   **Update checking is better here than anywhere else in the manager.** Modrinth recognises a mod by the file itself, so nothing has to be linked by hand and there is no auto-matching to run.

### 🔎 "Did my mods actually load?"

*   An unmodded Minecraft looks and sounds exactly like a modded one that failed, so **Check My Setup** now reads the game's own log and says plainly which happened the last time you played — including how many mods Fabric accepted.
*   It says so **even when everything is fine**. In a game whose usual failure is silence, an all-clear that tells you nothing leaves you no better off than before you asked.
*   Minecraft also gets a **Log** tab, showing the current run's log.

### ⌨️ Controls and documentation

*   **Ctrl + H** shows Minecraft's controls and your accessibility mod's, read from the files the game itself reads — so they are your **real** bindings, including anything you have remapped. No helper mod needed.
*   Both are grouped into sections, because the same key legitimately does several different things: United Minecraft binds Left arrow to three separate actions, each on a different screen. Mouse buttons are a section of their own, so you can see at a glance which actions are not on the keyboard.
*   **F3** shows United Minecraft's and Minecraft Access's documentation, with offline copies included so it works before you are online.

### 📋 Profiles

*   Profiles work for Minecraft exactly as they do elsewhere, and nothing on disk is moved — so **your worlds are never involved**. A profile for each accessibility mod is the tidiest way to try the other one.

### 📖 Wiki and walkthroughs

*   A searchable **Minecraft Wiki** tab with categories, and a **Minecraft Walkthroughs** tab: eleven guides from the wiki and three from Minecraft Access, with **navigation first** — finding your way is the hardest part of Minecraft without sight, and it is the one thing the game hands a sighted player for free.
*   ⚠️ Before this, a Minecraft session showed **Stardew Valley's** wiki and Stardew's walkthroughs. Not a missing feature — the wrong game's, which is worse: there is nothing to see that would tell you the guide being read out belongs to another game entirely. Six separate switches decided this and every one of them ended in a default of Stardew Valley, so a game they did not list did not get nothing, it got somebody else's.
*   Every category and every guide URL was requested and checked before being listed. Four plausible-looking categories and four obvious-sounding tutorials do not exist and were dropped; a page that does not exist comes back empty, which reads as "this game has nothing here".
*   United Minecraft is weeks old and has no wiki or guides yet, so only Minecraft Access contributes documentation. Its README is read in full by **F3**.

---

## 🐛 Fixed: the Wiki, Walkthroughs and Log tabs kept Stardew Valley's names

*   **Every game was affected, not just Minecraft**, and only on the sessions most people have: the manager reopens the game you last used, and it was reopening it under Stardew Valley's tab names. Switching games by hand named them correctly, which is why this survived five games — the labels are right the moment you go looking for the bug.
*   ⚠️ The renaming lived inside the game-switch, which does nothing at all when asked for the game that is already loaded. That is exactly what startup is: the active game is restored from settings *before* the window is built, so the switch had nothing to do and the tabs kept the names they were built with. Those names are Stardew Valley's, for every game.
*   The Log tab and the wiki search box were missing Minecraft from their lists as well, so a Minecraft session heard **"Search Stardew Wiki"** from the box it was typing into. Both fixed, and a test now requires every one of these lists to name every game the manager supports, and requires the renaming to be done somewhere other than the game-switch.

---

## 🔧 Changed: a game wiki's Categories list is hand-picked again

*   Categories for a **game** wiki come from a short curated list per game, rather than being fetched from the wiki and ranked by size. Mod wikis are unchanged and still fetch theirs live.
*   ⚠️ **The live ranking was wrong on any large wiki, and had been for some time.** The API returns categories **alphabetically**, so asking for five hundred of them from a wiki that has thousands returns an A-to-B slice — "the biggest categories" quietly meant "the biggest categories beginning with A or B", and the list never reached M for Mobs. What did survive was mostly the wiki's own bookkeeping (Blanked userpages, Articles to be expanded) and, on Minecraft, categories belonging to **Bedrock Edition** — a different edition of the game from the one being modded.
*   The curated lists are checked against each wiki by hand and are short enough to hold in your head, which a list of five hundred never was.

---

## 🔧 Changed: the Accessibility Controls viewer is now "Game and Mod Controls"

*   The name was accurate when the list held only an accessibility mod's keys. It has held the game's own controls and every mod's for some time, so it now says so — in the Mods menu and in the window title.
*   ⚠️ It is also **one level shallower** for every game. Named sections used to sit under a "Keyboard Controls" heading, which is only worth a level of its own when there is a "Gamepad Controls" beside it to be told apart from. On its own it divided nothing, and cost a keypress and a spoken word on the way to everything you came for.

---

## 🐛 Fixed: deleting a Witcher 3 mod offered to uninstall a different mod

*   Reported from a real session: deleting **Random Encounters Reworked** and each of the mods that came with it asked, every single time, whether to run **WitcherAccess's** uninstaller — the accessibility mod, which was not being deleted. Answering yes would have removed it.
*   ⚠️ **The rule had a fallback, and the fallback was the whole problem.** A Witcher 3 mod can be installed by running the author's own installer, which writes files the manager never sees, so the manager offers to run that installer's uninstaller when the mod is removed. It found the uninstaller "whose name matches this mod — **or else the first one we found**". Typically only one program registers an uninstaller inside a Witcher folder, so every mod that matched nothing fell through to that one.
*   **There is no fallback now.** Without positive evidence that an uninstaller belongs to the mod being deleted, no offer is made. Leaving a few of a mod's files behind is a far smaller harm than removing a different mod the user still wants — and here, the mod they need in order to play at all.
*   The matching itself is more forgiving in exchange, so a mod's own uninstaller is still recognised: case, spaces and punctuation are ignored, the engine's `mod` prefix and the `~` that marks a mod disabled are stripped, and either name may be the longer one — "modWitcherAccess", "~modWitcherAccess" and "Witcher Access v0.3" are all the same mod. A name of fewer than four characters is never matched, because at that length it would eventually collide with something inside an unrelated program's name.

---

## 🔧 Changed: Stay At The System Page is no longer part of the Skyrim accessibility suite

*   **Skyrim Access no longer needs it**, so it has been removed from the suite.
*   Removed rather than kept as optional. The suite is the list of what you *must* have for the game to be playable — anything in it that is not needed is a mod somebody installs, keeps updated, and troubleshoots for no reason.
*   ⚠️ **Nothing is uninstalled.** If you already have it, it stays exactly where it is as an ordinary installed mod, still listed and still update-checked. It simply stops being something the suite asks you for, and the suite no longer reports it missing on a copy that does not have it.

---

## 🐛 Fixed: an installed mod was labelled with the mod page's version, not the version installed

*   The manager recorded a newly installed mod's version by asking the **mod's page** what its current version was, rather than looking at the **file it had just installed**. Those are different facts, and the difference is not cosmetic.
*   ⚠️ **This is what made the downgrade above invisible.** The archive was 6.4.3; the page said 6.5.2; the manifest was written as 6.5.2. So:
    *   the installed list read out **6.5.2**,
    *   the update check compared **6.5.2** against the page and found nothing to do — so it would never have corrected itself,
    *   and reinstalling the correct file said **"powerofthree's Papyrus Extender (version 6.5.2) is already installed. Overwrite it with this copy?"** about a folder that held 6.4.3.
    
    A mod that was silently six weeks out of date looked like the most up-to-date thing on the machine, and the one thing on screen that could have revealed it said the opposite.
*   **The version now comes from the copy that was installed**, in order of who is best placed to know: the archive's own `info.xml` where it has one, then the version Nexus puts in the download's file name, and only then — when neither says anything, as with a hand-renamed archive — the mod page, where it is a better guess than nothing.
*   The page is still the authority on the mod's **name, author and description**. It was only ever wrong about the version, and only because the version describes a file rather than a mod.
*   Both install paths had the same line, the ordinary one and The Witcher 3's; both are fixed, and the rule now lives in one place with the reasoning attached.
*   ⚠️ **Mods already installed keep whatever version was recorded at the time.** A manifest is only rewritten when the mod is reinstalled, so a wrong label stays until then — reinstalling the mod corrects it.

---

## 🐛 Fixed: an update could quietly install an OLDER build, and stop the game starting

*   Reported as a Skyrim that would not launch — the game hung with **po3_PapyrusExtender.dll (Not Responding)**, hours after a normal round of mod updates. SKSE's own log ended mid-sentence:

    ```
    loading plugin "powerofthree's Papyrus Extender"
    ```

    and nothing after it. The plugin refuses a game version it does not know and says so in a window that opens *behind* the game, so it reads as a hang rather than an error.
*   **The update had gone backwards.** Papyrus Extender 6.5.1 had been running fine for a week; the update replaced it with **6.4.3, six weeks older** — and built before the game version installed on the machine.
*   ⚠️ **The cause was the Steam/GOG rule reading the wrong text.** A mod page that ships one build per store says so in its **file names** — SKSE's are "Skyrim Script Extender (SKSE64) GOG" and "... Steam". The check was reading each file's **description** too, which is where an author lists the runtimes a single universal build supports. Papyrus Extender's says:

    ```
    Supports SE 1.5.97
    Supports SE/AE 1.6.1170 or 1.6.1179 GOG
    Supports SE/AE 1.7.99+
    ```

    One mention of GOG in that list marked the current file as "the GOG build", so a **Steam** owner had it ruled out — along with every other recent release, since they all say it. What was left was the newest file that happened not to mention GOG, which was the old one.
*   **The store is now read from the file's name only.** Checked against both pages it matters on: every one of SKSE's GOG files carries GOG in its name, so the split it was written for still works exactly as before, while a universal file stops being mistaken for somebody else's build.
*   This could affect any mod whose one file lists its supported runtimes and mentions GOG among them — quietly, because a downgrade installs like any other update. It is worth a look at the version of anything that started misbehaving after an update.
*   The rule now lives in one place rather than two, so the update path and the part picker cannot disagree about it.

---

## ✨ New: Tidy Mod Folder Names

*   **Mods menu → Tidy Mod Folder Names.** Renames the folders your mods are installed into after the mods themselves, so the mods folder reads like a list of mods instead of a list of serial numbers:

    ```
    Hunterborn-7900-1-6-2                                        -> Hunterborn SE
    Media Keys Fix 92948 1.0.2 2026-08-21T18-58Z Yj6wQRo26       -> Media Keys Fix SKSE
    Achievements Mods Enabler SE-AE-245-1-41-1715217907          -> Achievements Mods Enabler SE-AE
    ```
*   **This is the convention Mod Organizer 2 uses**, and the manager was already doing the harder half of it. MO2 gives each mod a readable folder and keeps the mod id, version and source archive in a `meta.ini` beside it; this manager writes a `.manager_manifest.json` for exactly that purpose, which is why the mod list has always sounded right no matter what the folder was called. Naming the folder after the mod is the half that was missing. **Vortex** made the other choice — its staging folders keep the whole Nexus suffix and it hides the suffix in its own interface — which is what a mods folder full of `-1-41-1715217907` looks like when nobody finishes the job.
*   **Nothing happens without a preview.** The full list of "this becomes that" opens first, every row of it, and nothing is renamed until you say yes.
*   ⚠️ **The mods themselves do not care, and that is why this is safe.** SMAPI, BepInEx and the Witcher's engine all find mods without reference to the folder name, and a Bethesda mod's deployed files are hard links, which follow the file rather than the path it was linked from.
*   ⚠️ **The manager's own bookkeeping does care**, and all of it is rewritten in the same pass: mod priority, the file conflicts you settled by hand, the record of which mod deployed which file, every saved profile, and the Witcher's own `mods.settings`. A rename without those would quietly reset a load order, which is the failure this was written around.
*   What is deliberately left alone:
    *   **A folder that is already properly named** — which is most of them, and every Stardew one, because those arrive named by the author. Rewriting 148 tidy names to fix 23 untidy ones would be the worse tool.
    *   **Whether a mod is switched off.** The leading dot (or the Witcher's tilde) is what disables a mod, and it survives the rename — dropping it would silently switch a mod back on.
    *   **The Witcher's `mod` prefix**, without which the engine ignores a folder entirely and says nothing. A Witcher mod keeps its folder-style name rather than taking the spoken one, because a folder called "Brothers In Arms" is a mod that has stopped loading.
*   Two mods that want the same folder do not get it: the second becomes "SkyUI (2)", and an existing tidy folder is never taken from the mod that already has it.
*   Names come from the mod's page on Nexus, so a few are more correct than the folder was: `NoStamina-SKSE` is really *Unlimited Stamina - NG*, and `Puzzle Pillar Auto-Unlock` is really *Puzzle Pillar Auto-Solve*.

---

## 🐛 Fixed: mods installed into folders named after Nexus's bookkeeping

*   A mod could end up installed into a folder called **"HouseStorageAnywhere 7 2 2026-08-22T02-14Z ifoLiAHsd"**, and be read out that way, instead of just **"HouseStorageAnywhere"**.
*   **The cause was one assumption about Nexus's file names**, made when the tail was first trimmed off them: that a mod id is at least three digits, and that an upload timestamp always follows it. Neither holds.
    *   ⚠️ **A young game's mods are numbered from 1.** Moonlight Peaks' mods are 7, 11, 33, 85 — so *every* Moonlight Peaks mod with an id under 100 kept its whole tail. It was worst exactly where it was least noticed.
    *   ⚠️ **Older downloads carry no timestamp at all**: "Easy Hacking-266-1-0", "Hunterborn-7900-1-6-2", "Carry Weight Modifiers-2176-1-1-3".
    *   ⚠️ **And a third shape was never handled**, the one the manager itself asks for when it fetches a single named file: "Skyrim Access_181131_file_772480".
*   **The name is now cut at the mod id**, found by the same rules that read a mod id out for the Search Mods tab. One set of rules, so the two cannot disagree about where a mod's name ends — and adding "you downloaded this" to a search result and fixing this turned out to be the same problem seen from two sides.
*   Three questions have to answer yes before any number is taken for a mod id, because a wrong cut takes away part of a real name: it has to be a possible id (never 0, never zero-padded), everything behind it has to look like Nexus's tail, and there has to be a name in front of it. A number sitting behind *other* numbers only counts when it is too big to be a version piece — which is how **"UIExtensions v1-2-0-17561-1-2-0"** is read as mod 17561 rather than mod 2, and why **"SMAPI 4-0-2"** is left alone completely.
*   Checked against a real installation rather than invented examples: **170 downloads and 213 installed mod folders**. 21 of them are now read correctly that were not, every other one is untouched, and every single change removes a tail — none renames anything.
*   **The same rule now answers the question everywhere it is asked.** "Where is the mod id in this name?" had grown five separate answers scattered through the manager — the folder namer, the mod scan, the BepInEx scan, the installer's reinstall check and the Witcher 3 folder namer — and four of them were the same weak pattern: the first "-digits-" in the name, three digits minimum. They all defer to one implementation now, so a name that reads correctly in one place cannot read wrongly in another.
    *   ⚠️ **The BepInEx scan was the live casualty.** It recovers a mod's Nexus id from its folder name when the plugin does not declare one — and with a three-digit floor it could not do that for Moonlight Peaks at all, whose mods are numbered 7, 11, 33. A mod with no id recovered is a mod that never gets update-checked.
    *   The one place deliberately left stricter is the check on a file **you** picked from anywhere on your disk, which still refuses to guess when a name is ambiguous. There, "ModBackup-12345-old.zip" offering up mod 12345 would quietly attach another mod's updates to yours, and refusing costs only an automatic link.
*   Mods already installed into a badly named folder keep that folder; this fixes what happens from here on.

---

## ✨ New: search results say when you last downloaded the mod

*   A result you have already pulled down now says so, and when:

    ```
    Cape Stardew (ID: 14635). Installed. You downloaded this 4 days ago. 12,204 downloads, 903 endorsements. Updated 3 weeks ago. …
    ```
*   **The point is to stop you fetching the same mod twice.** Nexus tells you this on the mod page — and going to the page to find out is exactly the trip the rest of the row exists to save. Now the answer is in the list, next to everything else you would judge a result by.
*   ⚠️ **Nexus's API does not report it.** "You last downloaded this" is built from the website's own logs and is not offered to any program, so the manager answers the question from what it can see: **the archives in that game's downloads folder.** That is a better answer than a ledger would be, in two ways:
    *   **Every download already in your folder counts**, including ones from long before this was written. Nothing had to be recorded in advance.
    *   **Deleting an archive stops the claim.** The row goes quiet again, so hearing "you downloaded this" always means the file is still there to use — which is the whole reason for wanting to know.
*   It reads as **"You downloaded this 2 days ago"** rather than "Downloaded 2 days ago", so it cannot be confused with the mod's own **download count** three words later in the same row.
*   The row is now in two halves: **what you have** — installed, downloaded — then **what everyone else thinks** — downloads, endorsements, last updated. Both halves come before the description, which is the longest thing to sit through and the last thing you need.
*   ⚠️ **Reading a mod id out of a download's name is fussier than it looks**, and getting it wrong would send somebody hunting their disk for a file that was never there. Three naming shapes are in circulation, and the rules were checked against a real downloads folder five games deep: 132 of 170 archives identified, and the 38 declined are genuinely nameless — content-server ids with no name in them, GitHub releases, and files placed in the folder by hand. One real trap is guarded by name: `UIExtensions v1-2-0-17561-1-2-0` is mod **17561**, not mod 2, because the author put their own dashed version in front of Nexus's.

---

## ✨ New: search results say when the mod was last updated

*   Every result in **Search Mods** now reads how long ago the mod was last touched, right after its download and endorsement counts:

    ```
    Serena's Grimoire (ID: 23). 3,428 downloads, 50 endorsements. Updated 3 weeks ago. Dark magic, dramatic rituals.
    ```
*   **It is the one thing the counts cannot tell you.** A mod with a hundred thousand downloads and four years of silence behind it is a very different prospect from the same mod updated last week, and the two used to sound identical in the list. Finding out meant leaving the manager and opening the mod's page — the exact trip the rest of the row exists to save.
*   The age is worded the way Nexus words it on the mod page, with the unit growing to fit the gap: *"5 minutes ago"*, *"5 hours ago"*, *"6 days ago"*, *"3 weeks ago"*, *"1 month ago"*, *"4 years ago"*. Nothing ever reads *"1 weeks ago"* or *"24 hours ago"*.
*   It sits with the counts, **before** the description, for the same reason they do: everything that can rule a result out is said before the part that takes the longest to hear.
*   Nexus reports the date on the same request that fetches the results, so nothing about a search got slower and no extra API requests are spent.

---

## 🐛 Fixed: the window's title read itself out between a prompt and its answer

*   From the speech history again, one step further on:

    ```
    Downloaded Project Fluent. Install now?
    Stardew Valley Kinetix Mod Manager - Status: Downloading Project Fluent... 100%  window
    Yes  button  Alt+ y
    ```

    The question is in the right place now. The title bar in the middle of it is not.
*   **Two things were wrong, and both are worth fixing on their own.**
*   ⚠️ **The title was stale.** Finishing a download played its completion tone but never put the title back, so the window went on reading *"Status: Downloading Project Fluent... 100%"* long after the download had finished. Untidy on its own — a whole wrong sentence once anything makes the screen reader read the window out. Finishing any operation now restores the title.
*   ⚠️ **A prompt did not silence the window, though an in-window view always had.** With no accessible name of its own, a window falls back to its caption — which here carries the game and the live status. Anything over the window now blanks its name for as long as it is up, prompts included, and puts it back exactly as found. One place does this for both, rather than views having their own copy of it.
*   Together: the question, then the choice, and nothing in between.

---

## 🐛 Fixed: the prompt read out the button before the question

*   From a tester's NVDA speech history, which is the only thing that could have settled it:

    ```
    Yes  button  Alt+ y
    Downloaded Project Fluent. Install now?
    ```

    The right way round is the question first, then the choice.
*   ⚠️ **The wait was in the wrong place, and "later" is not the same as "first".** The question was being held back until the screen reader had finished reacting to the window being pulled to the front. But by the time it was held back, focus had already landed on the **Yes** button and the reader had already begun saying so — so delaying our sentence did not protect it, it just moved it behind the reader's.
*   **The manager now waits before the prompt appears at all**, rather than after. Focus then arrives into a settled room, and the question interrupts the reader's reaction to that focus — which is what always put it first. The choice follows a moment later, as it should.
*   ⚠️ **A correction worth recording: a blank accessible name does not silence a button.** The reader falls back to the button's visible text, so "Yes, button, Alt+Y" is announced either way — the speech history proves it. The comment in the code claiming otherwise has been fixed, because that belief is what made the wrong fix look right.
*   In-window views raised the same way (the "which copy of the game is this for?" question, and the cross-game download question) get the same treatment.

---

## ✨ New: every log opens inside the manager, read only

*   Not just the manager's own — the game's too. **Notepad is gone from the manager entirely.** Reading a log no longer means leaving the program you are already in, and no log can be edited or deleted by accident while you read it.
*   **The game's logs now list themselves.** A script extender's folder holds the extender's own log and one for every plugin that writes anything — fifteen of them on a well-modded Skyrim — and which of those has the answer depends entirely on what went wrong. Opening the game's log now shows that list, **the one most likely to matter first**, then the rest by how recently they were written. Enter reads one; Escape goes back.
*   That list used to be **Explorer**, opened when the main log did not exist yet: somebody else's file browser, to be navigated by a person who cannot see it, to find a file they then had to open in a third program.
*   Each log opens **at its newest entry**, since a log is read backwards from the thing that just happened. A very long one shows its most recent part and says where it was cut, so nobody concludes the earlier sessions never happened.
*   The keys are the ones every other screen uses: arrows to move, **Ctrl + End** for the very end, Escape to close. An empty log says so rather than opening a blank box.

---

## 🐛 Fixed: a prompt read out its question and then said nothing about the button under your finger

*   The other half of the announcement fix, reported from testing. The question was read — and then the focused **Yes** was not, so the only way to hear it was to Tab away and Shift+Tab back.
*   **Only half the timing had moved.** The choices deliberately start out unnamed so nothing competes with the question, and giving them their names back a moment later is itself the change that makes the screen reader announce the focused one. Making the question wait for the reader to settle delayed the question but not the naming, so the names came back **while the question was still being spoken** — and a name change made mid-sentence is not one the reader reports.
*   The two are now chained rather than started together, so the gap between them is the same as it always was, wherever the question starts.

---

## 🐛 Fixed: deleting a mod announced that it was installing one

*   Delete a mod and it says it is backing it up first — then immediately *"Installing Content Patcher, 0 percent"*, which is the opposite of what is happening.
*   Progress announcements only ever had two phrasings, downloading and installing, so everything that reported progress had to pick one of them. Backing up picked "installing". There is now a third, and the title bar matches it.

---

## 🐛 Fixed: answering a report could leave you in a window called WindowsFormsParkingWindow

*   Say yes to "search for this mod" in Check My Setup and you could land somewhere with that name — a piece of internal Windows Forms scaffolding that should never be seen, let alone focused.
*   **Closing a report only raises a flag; the panel comes down a moment later.** So the tab switch and the search were being done *underneath a report that was still there and still held the keyboard*. Removing a panel while something inside it has focus makes Windows Forms park that control on a hidden holding window, and the focus goes with it.
*   Whatever a chosen row asks for now runs **after** the report has actually gone and focus has been put back. As a second guard, focus is moved out of any overlay before it is taken down, so this cannot happen from any other prompt or view either.

---

## ✨ New: the log opens inside the manager, read only (Ctrl + Shift + L)

*   It used to open in Notepad, where the file can be edited or deleted by accident — and where a screen reader user has left the manager entirely to read it.
*   It now opens as an in-window view like every other screen: a read-only box, arrow keys to move through it, **Ctrl + End** for the newest entry, Escape to close. Nothing in it can be changed.
*   It opens **at the end**, where whatever just went wrong is. A log is read backwards from the thing that happened, and starting at the top of a two-megabyte file means paging through months to reach it.
*   Very long logs show the most recent part rather than the whole file, and say so where they are cut, so nobody concludes the earlier sessions never happened. The full path is given in the opening announcement for anyone who wants the file itself.
*   The **game's** log (SMAPI, SKSE) still opens in Notepad — that one is not the manager's to own.

---

## 🐛 Fixed: a slow wiki stalled the manager for thirty seconds at a time

*   Found in a tester's log, which is the first time anything like this has been visible at all: the wiki category list timing out, repeatedly.
*   Loading a wiki's categories is a heavy request — five hundred categories with their page counts — and it was using the shared thirty-second timeout. A wiki having a slow day therefore cost half a minute on **every game switch and every wiki change**, silently.
*   It now gives up after ten seconds, and says so: the dropdown reads **"Categories could not be loaded - search the wiki instead"** rather than sitting empty, which is indistinguishable from a wiki that genuinely has no categories.

---

## 🐛 Fixed: a file you saved was sometimes never written at all

*   Export your suggested mods, name the file, pick a folder, press Save — and the file is not there. No error, no sound, nothing in the log. It looked like a problem with saving files and was never anything of the kind.
*   ⚠️ **The export was being abandoned before it began, to protect an announcement.** When a file dialog closes, the screen reader starts re-reading the main window, and the manager waits about half a second for that to pass so its next sentence is not talked over. That wait reports back whether it still has the floor — and **the answer was being used to decide whether to carry on at all**. So if anything else happened to speak during that half second — a refresh finishing, a status line, an update check — the export stopped dead, between the dialog closing and the file being written.
*   That is why it worked on one machine and not another: it is a race, and it is lost more often on a busy one.
*   **The wait still happens; it no longer decides whether your work does.** **Seven** places had it: exporting and importing suggested mods, exporting and importing a collection, exporting and importing a load order, and the Mod Organizer 2 import. An eighth had quietly dropped the explanation of why a chosen game folder was rejected.
*   A test now fails the build if a file dialog's work is ever again made to depend on the screen reader settling.

---

## ✨ New: saving a file now tells you it saved, and proves it first

*   Saving is the one thing where "it seemed to work" is worth nothing: the file is either there when you go looking or it is not, and you find out much later.
*   **A box now confirms the save**, giving the file name **and the full path** — the folder being the half that was actually in doubt. A box rather than a spoken line, because a sentence can be missed and a box is still there when you come back to it.
*   **The manager checks before it says so.** After writing, it goes and looks: the file has to exist and have something in it. A save can be redirected or undone underneath you by folder virtualisation, a sync client or security software, and every one of those lets the write itself finish perfectly happily.
*   **A failed save now says so plainly**, names where it was trying to write, says what went wrong, points at the log, and suggests somewhere you are always allowed to write. Before this, a failure that happened after the file dialog was invisible.
*   Both outcomes go to the log — the successful one with the number of bytes written and the full path, so a report about a missing file can be answered from the log alone.

---

## 🐛 Fixed: the manager searched for things Nexus has never heard of

*   Ask it to find a missing dependency and it searched for **`Digus.ProducerFrameworkMod`**. That is the mod's identifier — the name its author gives it in code — and it appears nowhere on Nexus. The search came back empty and you were told the mod could not be found, while its page sat there under the name **Producer Framework Mod**.
*   **An identifier is not a name, and it is now never searched for as though it were.** What identifies the mod is the part after the author, split back into words: `Pathoschild.Automate` searches for **Automate**, `Sandman53.AbilitiesExperienceBars` for **Abilities Experience Bars**. Where the name is spread across several parts — `CocumiT.TQP.crystal.lighting.fixtures` — everything after the author is searched for together, because the last part alone ("fixtures") would find half the site.
*   **The rows still say the identifier.** It is what the mod's manifest asks for and what you will see written in a log, so it stays on screen. It is only what gets *searched for* that changed.
*   **Four separate places were doing this** and all four are fixed: resolving a mod's requirements, the missing-dependency lines in Check My Setup and the requirements report, the quick fix on a log line that names a missing mod, and the row for a DLL plugin the script extender refused.

---

## 🐛 Fixed: a mod was searched for by the name of the folder it was downloaded into

*   The other half of the same complaint. When the manager cannot identify a mod it falls back to searching for what the mod's folder is called — and for anything installed from Nexus, that folder is named after the download.
*   ⚠️ **Nexus changed how it names downloads, and the manager could no longer see the difference.** It used to end a file name with the mod id and a run of numbers; it now writes something like `DbMiscFunctions 65410 10.4 2026-08-27T05-37Z L5WQbqhzr` — spaces instead of hyphens, and a date instead of a count of seconds. The old trimming could not match that at all, so **every mod installed since the change kept the mod id, the version and the timestamp in its folder name**, and that whole string is what got searched for.
*   The new shape is now trimmed too, so that folder searches for **Db Misc Functions**. Where the mod's Nexus id is known it is used to make the split exact, which also handles a tail that ends in a version rather than a date — `Carry Weight Modifiers-2176-1-1-3` searches for **Carry Weight Modifiers**.
*   ⚠️ **The trimming now happens before the name is taken apart, not after.** Splitting first left fragments that no longer looked like a tail — so `65410 10` and `4 2026-08-27T05-37Z L5WQbqhzr` each became search terms of their own.
*   **What the mod calls itself still leads.** The folder is the fallback for a mod with nothing to read, never a replacement for a name that is already right.
*   **"Find this mod on Nexus" now tries every name the mod goes by**, in the same order the automatic matcher already used, stopping at the first that finds anything. It had been trying one name and giving up.

---

## ✨ New: nothing fails quietly any more

*   **93 places in the manager caught a failure and then threw it away.** Most had a good reason to carry on — an unreadable folder really does read the same as an empty one — but *carrying on* and *saying nothing at all* are two different decisions, and only the first of them was ever meant. Carrying on is still what happens. Doing it in silence is not.
*   **91 of them now record what happened**, with what was being attempted at the time. The two left are a user cancelling a wizard and a shutdown signal, which are not failures.
*   Some of what this reaches had been genuinely invisible:
    *   **a file that did not get deployed into the game folder** — the mod looks installed and is quietly incomplete;
    *   **a mod's details failing to save**, so it re-derives them every scan and its Nexus link keeps going missing;
    *   **the record of what the script extender installed** failing to write, which is what a later clean uninstall depends on;
    *   **an old backup that would not delete** after you asked for it to be pruned;
    *   **the screen-reader bridge failing to reload** after NVDA restarts — for the person relying on it, the entire symptom is silence, with no error to see by definition.
*   ⚠️ **One kind of failure is recorded 20 times and then summarised**, because some of these sit inside a loop over every file of every mod. One unreadable folder there is worth knowing about; ten thousand is a log nobody can read and a report nobody can send, with the one failure that mattered buried in the middle of it.
*   A test fails the build if a new silent catch appears. The exception is the log itself, which has to swallow: it is called from the crash handler, and a logger that throws while recording a crash destroys the very record it was writing.

---

## 🐛 Fixed: the manager could crash as it closed

*   Found by reading the crash log that nobody had been able to read — the first thing the new one-file log turned up, from a crash recorded on 8 August.
*   Closing the window disposes the menu bar, and disposing a menu bar takes Windows out of menu mode, which raises **one last "menu deactivated" event on the way out**. The manager listens for that event because leaving the Alt menu restores focus to the list underneath without announcing it — so it re-announces where you are. On the way out there is no window left to post that announcement to, and the attempt threw.
*   The announcement is now skipped when the window is already going away. There was never anything to announce at that point.

---

## ✨ New: one log, and everything that goes wrong is now in it

*   ⚠️ **There were two logs, and the one you were asked for was the wrong one.** Ordinary failures went to `mod_manager_log.txt`, which the File menu and **Control + Shift + L** open. Crashes went to a separate `crash_log.txt` that nothing in the manager ever opened, named, or mentioned — so someone reporting a crash sent a file with no crash in it, while the file holding the answer sat unread beside it. Two real crashes had been recorded there since June and never seen.
*   **There is now one file, everything goes in it, and it is the one Control + Shift + L already opens.** When a crash dialog appears it says so, and gives the path, so there is no guessing about what to send.
*   **Every session starts with a header saying what produced it** — the manager's exact version and where it is installed, the Windows and .NET versions, the language and culture, and the game that was loaded. A log that does not say which build wrote it can be actively misleading, since the problem being described may already be fixed.
*   **Failures are written out in full**: the exception's type, its stack, and *every* inner exception — not just the outermost message, which is routinely the least informative part of the chain ("One or more errors occurred"). Each entry also says **what the manager was trying to do at the time**, which is the half a stack trace cannot supply and the half that makes a report reproducible. 64 places that had been logging a bare message now log the whole thing.
*   Timestamps are full dates in a fixed format, not the bare `14:07:02` they were. A log arrives days later and often spans several sessions, and a bare time cannot be placed against "it broke on Tuesday".
*   The log rolls over at 2 MB, keeping one previous generation as `mod_manager_log.1.txt`. The cause of a visible break is often in the session before it, so the older file is moved aside rather than thrown away.

---

## 🐛 Fixed: work started in the background could fail without a trace

*   *"I pressed update mods and it stopped"* had nothing behind it — no error, no dialog, nothing in the log — and this is why.
*   **About fifty things the manager does are started and deliberately not waited for**, so the window stays responsive while an update check or an install runs. The cost of writing it that way is that there is no longer anyone to receive a failure: the work throws, nothing is thrown anywhere the app is looking, and the operation simply never finishes. Silently. Which is precisely what was reported.
*   **Every one of those now reports its own failure into the log**, naming the operation that failed. There is a third safety net behind them for anything missed, alongside the two that already caught crashes.
*   A test now fails the build if new background work is started the old way, because the old way is the one that comes naturally and a single new one puts a silent hole back in the log.
*   The same gap existed for **sounds** — a malformed file in a sound theme would make the manager go quiet with no explanation, which on an app whose feedback is sound is a confusing thing to have happen silently.
*   ⚠️ **Logging now starts before the settings file is read**, rather than after. A startup that failed before its own logging was switched on was the one failure nobody could report at all: the manager never appeared, and nothing anywhere said why.

---

## ✨ New: when the game will not start, the manager names the mod that stopped it

*   A game that launches and then closes, or hangs on a window that says nothing useful, has been the one failure the manager could not explain. Every other broken plugin is **refused**: the script extender writes a line saying which one and why, and Check My Setup reads those out. A plugin that instead hangs, crashes, or puts up its own error box and kills the game writes no such line — it stops the log mid-sentence and takes the game with it.
*   **That silence is itself the evidence.** The script extender writes `loading plugin "X"` before it hands over, and the matching verdict afterwards. A log whose last plugin has no verdict, and which never reached `init complete`, ended inside that plugin. It is the last thing anyone knew before the game went.
*   **Check My Setup** (**Ctrl + Shift + K**) now says so in as many words, at the top of the list because it outranks everything under it — the rest of that report describes a game that runs and does less than it should, this one describes a game that does not run: *"Skyrim Special Edition stopped while loading the mod plugin EngineFixes, and did not finish starting."* **Enter** finds that mod in your list.
*   Recorded from the machine that prompted it: Skyrim updated to 1.7.104, and the SSE Engine Fixes installed was still February's 7.0.20 — a build that puts up an error box and terminates the game the moment it finds it did not preload. The log ends on the bare line `loading plugin "EngineFixes"` and nothing anywhere else names it.
*   **A game that hangs instead of closing is covered too, and it is the worse of the two.** A plugin that never returns leaves a live game process with no window, using no processor time — so the manager reports the game as running, which is true and no use at all, while nothing is on screen. It now says what is actually happening: *"Skyrim Special Edition is running but has not finished starting: it is stuck loading the mod plugin SkyrimAccess, and has written nothing since."*
*   ⚠️ **What tells a stuck launch from a healthy one is the log going quiet, not the process.** A launch in progress writes continuously, a line per plugin, and loading one takes moments — so a log with a plugin still outstanding and nothing written for a minute is not a load in progress, whatever the process list says. Without that test the check would have to stay silent whenever the game was up, which is exactly when a stuck launch needs explaining.
*   `preinit complete` is written before any plugin is loaded and ends with the same words as the line that says the load finished. It is matched as a whole line, not as text found somewhere, or every stalled launch would be called healthy.

---

## 🐛 Fixed: an update was missed because the mod page's own version number was out of date

*   The update check said **all mods are up to date** while SSE Engine Fixes **7.0.21** had been sitting on its Files tab for a week — on a Skyrim that had just updated and would not launch without it.
*   **The page was both lying and telling the truth.** A Nexus mod page carries a version number of its own, and the author maintains it **by hand, separately from uploading the file**. Upload and forget, and the page reads 7.0.20 while its own Files tab offers 7.0.21. The manager read the page and believed it, because that number was the only thing it had ever asked for.
*   **When the page says there is nothing new, the files are now asked as well.** The check takes the highest version among the files the page currently offers as its **main** download, and reports an update when that beats what you have installed. An author who forgets to bump the number no longer hides a release from you.
*   Only what the page is actually offering counts. The bundles filed as **optional**, and the mod's whole history sitting under **old version** or **archived**, are ignored — reading versions off those would invent updates out of files nobody is being offered.
*   ⚠️ **It costs a second look-up, so it is only taken when the first answer was "up to date"**, and it is skipped when your Nexus allowance is running low — a thorough check is worth an extra call, and running you out of them is not. When it is skipped the check answers exactly as it did before.

---

## 🐛 Fixed: the preloader was called a leftover while the installed mod was still using it

*   The new **"No longer used"** finding went by your game's version alone. Update Skyrim to 1.7.99 or newer and it offered to clear the preloader out — even when the **SSE Engine Fixes actually installed** was an older release, the kind that still calls the preloader's entry point.
*   That is advice pointing the wrong way. The file is not left over; it is a file that copy of the mod is still trying to use, and what that setup needs is the newer mod, not a deletion.
*   **Both halves now have to be true**: the game past the version where the script extender took over the preloading, *and* the installed mod at the release that stopped doing it itself (7.0.21). An installed version the manager cannot read counts as too old — nobody should be told to delete a file on a guess.
*   The clean-up now takes the preloader's own log file (`d3dx9_42.log`) too, rather than leaving one file of four behind.

---

## 🐛 Fixed: a prompt raised from a browser download read out its buttons and not its question

*   Click **Mod Manager Download** on Nexus and the manager asks *"Downloaded Such-and-such. Install now?"*. What was actually spoken was **"Yes. No."** — the choices, without the question they answer.
*   **Nothing was wrong with the prompt.** The download starts in your browser, so the browser owns the screen by the time the file lands; the manager pulls itself to the front to ask. That is a foreground change, the screen reader is told about it, and it works out what to say on its own schedule — a fraction of a second later. Anything the manager said in between was wiped by the reader's own announcement, which lands last. Speaking sooner could never win that race.
*   **The question now waits for the window to stop being news before it is asked** — the same wait the manager already uses for its opening announcement at startup, for the same reason. The buttons still read normally afterwards.
*   This covers every prompt and list the manager raises after pulling itself to the front, which is all three of them: the install question, "which copy of the game is this download for?", and the cross-game download question. Prompts raised while you are already in the manager are unchanged — there is no foreground change to wait out, and they speak immediately as before.

---

## 🐛 Fixed: a GOG copy of Skyrim was handed the Steam script extender

*   Install SKSE while you are in the **GOG** copy of Skyrim and you could get the **Steam** build. It installs without complaint and then never loads — the two are compiled against different program files, and the wrong one simply does nothing.
*   **Two things had to go wrong together, and both did.** The manager picked the file by reading the version number off your game's own program file, which is normally the best possible evidence. But on a machine with both copies of Skyrim installed, it could read that number off the **other** copy: a session keyed to the game's first copy carries no note of which store that copy came from, and the search for "where is Skyrim?" is ordered Steam-first, so a GOG session that had to go looking got the Steam folder back. The Steam build then matched the version exactly and won on the strength of it.
*   **Both halves are fixed.** The folder question now asks the copy's own recorded store before anything else, so a GOG session is answered with the GOG folder even where its key says nothing about stores. And the store is no longer a hint that a version match can overrule: on a page that offers a separate GOG file, **the Steam build is not a worse answer for a GOG copy, it is not an answer at all**, and is dropped before anything is scored. Get the version number wrong now and the worst that happens is a different build of the right one.
*   **The same rule now applies to updates.** Checking a mod for updates used its own file-picking, which took the author's flagged main download — and on a page that splits by store, the flagged one is the Steam file. A GOG copy could therefore be *updated* onto the wrong store's build even after being installed correctly.
*   Pages that offer one build for everybody are untouched. Fallout 4's script extender has never had a GOG-specific file, and requiring one would have sent GOG owners to the Files tab to pick by hand — so the store only ever disqualifies a file where the page actually tells the stores apart.

---

## ✨ New: SSE Engine Fixes no longer asks for a preloader your game has outgrown

*   SSE Engine Fixes has always needed **two** downloads: the main plugin, and a **preloader** (`d3dx9_42.dll`) that goes loose in the Skyrim folder. **From Skyrim 1.7.99 it does not.** The script extender learned to load the plugin early by itself, and the mod's own installer stopped shipping the preloader for that version.
*   The manager was still insisting on it. On an updated game it reported a perfectly complete install as *"installed but incomplete"*, and the Accessibility Suite would go and fetch a file with nothing to load it.
*   **It now reads your game's version and asks only where it is still needed.** On **1.5.97 and the 1.6 line the preloader is still mandatory** — without it the plugin puts up an error box and closes the game — and nothing changes there. On **1.7.99 and newer** Engine Fixes is a one-part mod, and a one-part mod is never incomplete.
*   ⚠️ **A version the manager cannot read counts as still needing it.** That is the safe way to be wrong: the worst case is a line in a report about a file you do not need, rather than being told to delete the file without which your game will not start.

---

## ✨ New: Check My Setup offers to clear out a preloader your game has stopped using

*   Migrating is the other half of the above, and it arrives without you doing anything: your game updates, and a file that was mandatory last week is sitting in your game folder loaded by nothing. Nothing would ever have mentioned it — a loose DLL beside the game's program file is invisible to the mod list, which is exactly why it was easy to miss on the way in.
*   **Check My Setup** (**Ctrl + Shift + K**) now reports it under its own heading, **"No longer used"** — separate from incomplete mods, and counted separately in the spoken summary, because nothing here is broken. *"No longer used: SSE Engine Fixes: Part 2 — the preloader is still in your game folder but is no longer used."*
*   **Enter** on that line names the files it would remove — `d3dx9_42.dll`, `tbb.dll` and `tbbmalloc.dll` — and asks before touching any of them. They go to the **Recycle Bin**, never straight out, so going back to an older version of the game is still possible. Leaving them alone is a perfectly good answer: they do no harm.

---

## ✨ New: a mod you install from a zip yourself is checked for updates like any other (Ctrl + I)

*   A mod installed with **Ctrl + I** arrived with no Nexus page recorded, so the update check could not cover it until you ran **Auto-match Nexus IDs** and hoped the name search found it. With a lot of mods installed that way, keeping up meant visiting each mod's page by hand.
*   **The page was there to be read all along.** Nexus puts the mod's own ID in the file name it gives you — `Granny's Recipe Box-23737-1-0-2-1715181269.zip` is mod **23737** — and the moment you pick that file is the moment it is known for certain. Afterwards nothing on disk says where the mod came from, which is why recovering it later takes a search and a guess. It is now read straight from the name, and the mod is checkable from then on with nothing else to do.
*   **The release you installed is recorded with it**, so the mod is compared against the version you actually have rather than whatever its own files claim — the same thing that stops a mod being offered the same update forever.
*   ⚠️ **A renamed file is treated as unknown, on purpose.** The `" (1)"` a browser adds to a second download, or a name you have changed yourself, no longer follows the convention — and a name carrying two numbers that could each be a mod ID is refused rather than guessed between. A wrong page is worse than none: it offers another mod's version and "updating" fetches an unrelated download. Nothing is lost either way, since Auto-match can still find those by name.
*   Mods copied into place by hand are unchanged: they have no file name to read, and **Auto-match Nexus IDs** (Mods → Install and Update Mods) remains the way to link them.

---

## 🐛 Fixed: disabling a second Moonlight Peaks mod put them both in a nameless group

*   Switch off one Moonlight Peaks mod and it read normally. Switch off a second and the two of them vanished into a **mod group with no name** — a row you had to open to find your own disabled mods in.
*   **They were being grouped by a folder that isn't one.** The installed list groups mods by the top-level folder they share, measured from your mods folder. Every other game disables a mod by renaming it where it stands, so it stays inside that folder — but Moonlight Peaks uses BepInEx, which pays no attention to folder names and would happily go on loading a renamed mod. A disabled mod there has to **move out** of the scanned folder entirely, into `plugins-disabled` — which sits *beside* `plugins`, not inside it.
*   Measured from the mods folder, the path to a disabled mod therefore starts by walking back **out** of it, and that step out was being read as the folder they shared. It is the same step for every disabled mod, so the second one to arrive turned the pair into a group — one that could not be named, because a step out of a folder is not a name.
*   **Each disabled mod is now keyed by its own folder**, so it reads as itself, exactly as it did while it was switched on. Mods that genuinely share a folder are unaffected and still group as before.
*   Nothing moved on disk and nothing needs redoing — disabled mods stay in `plugins-disabled`, which is what keeps them switched off.
*   The same miscount was also behind the group key used when collapsing a group from one of the mods inside it, so that path is fixed with it.

---

# Version 1.5.1

A fix for Skyrim and Fallout 4: an up-to-date script extender is no longer reported as the wrong one, and you can now ask the manager which version you have. The manager also learned to explain the silent one — the game that launches perfectly and does nothing. The manual, the change log and the mod documentation all gained a proper search.

---

## ✨ New: The Witcher 3's controls, by situation or by key (Ctrl + H)

*   The controls list read The Witcher 3 as one flat list of keys, and the game does not survive being read that way. The interact key answered with a single sentence seventy-five items long — *"E: Bury Body, Place Trophy, Hide In, Dispose Paint, Take Paint Purple, Take Paint Yellow…"* — and the Home key with *"Toggle Hud, Announce, Keys First, Hist First, Map Announce"*, which is five unrelated things from two different programs.
*   **The reason is in the file.** The Witcher 3 declares its bindings once per *situation* — exploring, in combat, on horseback, in a boat, swimming — and it has forty-two of them. Gathering by key alone threw away the one thing that separates one item from the next, which is where you have to be for it to happen.
*   **The game's entry now opens onto two ways in, and you pick the question you have.** **By situation** answers "what can I press right now?" — Exploring on foot, In combat, On horseback, Sailing a boat, Casting signs, Menus and panels — with the keys under each. **By key** answers "what does this key do?" — every key once, alphabetically, and opening one lists each thing it does alongside the situation it applies in. Neither is a summary: both hold every binding you have.
*   **A key that does one thing is a line; a key that does several is a group.** The interact key is one row — *"E, 79 things it does"* — and its seventy-five interactions are there when you open it, rather than read at you when you pass by.
*   **What applies everywhere is listed once, under its own heading.** The game does not reference its general bindings from the situations that use them, it copies them out in full — those same seventy-five interactions are written again under Exploring, Combat, Swimming, Diving and both Ciri contexts. Saying them six times would bury what is genuinely particular to swimming, so they sit under **Interacting with things**, and Swimming is left holding what is true of swimming. This is the game's own distinction, not a guess: it marks those blocks itself.
*   ⚠️ **A mod's keys are now the mod's entry, not the game's.** A Witcher 3 mod declares its keys in the game's own file, on the same keys the game uses, which is exactly why they were impossible to tell apart. **WitcherAccess** has its own entry with its 39 controls, one to a line, and the game's list has only the game's.
*   **The situations are named after what you are doing**, not after what the file calls them: "On horseback", not `Horse_Replacer_Ciri`; "Interacting with things", not `BASE_INTERACTIONS_KEYBOARD`. Where the game splits one thing across several contexts for its own reasons — six of them for attacking alone — they fold into the one heading a player would recognise. A context the manager doesn't know is still shown, tidied into words, because a binding under an odd heading is findable and a binding left out is not.
*   The engine's own scaffolding is left out: debug keys, an empty context, a scene-loading placeholder. A row named after one of those is a row that cannot be acted on.

---

## ✨ New: lists with headings now count you within your section

*   A list divided into sections used to number every row alike, headings included. A settings list of two groups read *"3 of 35"* — a number spanning both groups and counting the headings themselves, which answered neither "how far through this group am I" nor "how much is left". The headings were numbered too, which is a position for something that is not an item.
*   **Each row is now numbered within its own section, and headings are not numbered at all.** So a settings list reads *"General, heading"*, then *"Aim enabled, on, 1 of 4"*, and further down *"Sounds, heading"*, then *"Sound: Hit, on, 1 of 28"*.
*   This is done in the one place every list's position announcement goes through, so **it applies to every list in the manager that has headings** — settings lists, reports, and anything added later. A list with no headings is counted whole, exactly as before, and mod groups in the installed list are unaffected: those are rows you can act on, not headings.

---

## ✨ New: The Witcher 3 mod settings, from the settings key (Ctrl + E)

*   A Witcher 3 mod's settings were unreachable from the manager: pressing the settings key on one found nothing, because there is nothing in the mod's folder to find. **The Witcher 3 keeps every mod's settings in the player's own `Documents\The Witcher 3\user.settings`**, under a section named after the group — `[WAGeneral]`, `[WASounds]` — while the *menu* those settings belong to is declared in a separate file in the game folder.
*   The manager now reads both. A mod that ships an Options → Mods menu gets **the same settings list Skyrim and Fallout 4 mods get**: every setting with its own label, offered as a choice or a number in its proper range, rather than a file to type into. **WitcherAccess** comes through with **32 settings** across General and Sounds.
*   **The author's own defaults are shown for settings the game has not written yet.** The Witcher 3 only writes a setting into `user.settings` once it has been touched in-game, so on a fresh install a mod's settings are simply absent — which is exactly when reading them from outside is most useful. The defaults come from the menu file itself, so the list is complete from the start.
*   **The two silent ways an edit gets thrown away are now called out before you make one.** If the game is running, it rewrites that file wholesale on exit and your change disappears with no warning — the manager says so and lets you decide. If the file has been marked read-only, *the game itself* silently fails to save any in-game change too; the manager offers to make it writable.
*   Labels are made readable rather than spoken as identifiers: `wa_snd_enemy_ping` is offered as **"Sound: Enemy ping"**. The real labels live in a compiled `.w3strings` file that no outside program can read, so the setting's own name is tidied up instead.
*   The mod's **Glossary** group is not shown. Its thirteen entries are "play this sound" buttons that store no value and only the game can carry out — listing them would offer thirteen things that cannot be done from here.
*   Group headings are read as the section's name rather than the mod's own label for it: **"General"** and **"Sounds"**, not "Wageneral" and "Wasounds".

---

## ✨ New: a Witcher 3 mod's controls are named, and say which mod they belong to

*   The controls list (**Ctrl + H**) showed a mod's own bindings as the identifiers the game stores — **"N: WA Compass"**. They were there, but read as code rather than controls, and nothing said they came from a mod rather than the game.
*   A mod names its actions after itself, and its folder is named after itself too, so the initials in `WA_Compass` can be matched against `modWitcherAccess`. Those bindings now read **"Compass (Witcher Access)"** — which also tells them apart from the game's own controls where both sit under one key.
*   Nothing is assumed about any particular mod. A prefix matching no installed mod is left exactly as the file wrote it, and where two installed mods share the same initials neither claims the action — a guess about what a control does is worse than the identifier it came from.

---

## 🐛 Fixed: Skyrim Access installed but silent — the NVDA file was left where the game could not find it

*   Skyrim Access ships `nvdaControllerClient.dll` **inside an "NVDACC" folder**, and an earlier build kept it in `Data\Root`. Neither is the top level of the archive, and only top-level loose files were ever treated as belonging in the game's root folder — so the DLL was installed as ordinary `Data` content, where nothing can load it. The game started perfectly and never spoke, and the fix being passed around by hand was "go and find that file yourself and copy it next to the exe".
*   **A file like this now goes beside the game's .exe wherever the archive keeps it**, with the folders around it dropped. Windows resolves a DLL asked for by bare name against the running .exe's own folder and looks nowhere else, which is why any tidier location fails. The same applies to the other screen-reader bridge files a mod might ship — `Tolk.dll`, the JAWS, ZoomText and Dolphin clients — since they all load the same way.
*   **Mods already installed are repaired too, with nothing to reinstall.** The rule is applied when deploying as well as when installing, so the next refresh puts the file where it belongs. This also covers mods that were staged by the Mod Organizer 2 importer or put in place by hand.
*   **An archive carrying both a 32- and a 64-bit build gets the right one.** Only one copy can sit beside the exe; Skyrim Special Edition and Fallout 4 are 64-bit, and installing the 32-bit copy would fail in exactly the same silent way. A build in an `x64` (or `win64`, `amd64`) folder wins, and failing any such hint the copy nearest the top of the archive does. The copy that loses is still installed, just left where it was — an archive never comes out lighter than it went in.
*   Removing the mod removes the file again. Everything deployed into the game folder is tracked, so this is taken back out with the rest of the mod, and the folder it sat in is only tidied away if nothing else is left in it.

---

## 🐛 Fixed: holding a shortcut ran its command over and over

*   Holding **Refresh Everything** started a refresh per key repeat — three or four for a key held about a second — and the runs then argued with each other, announcing *"Refreshing everything"* and *"an update check is already in progress"* in turn. Choosing the same command from the **Mods** menu behaved perfectly, which is the tell: a menu item cannot auto-repeat.
*   **Windows repeats KeyDown for as long as a key is held**, and every one of the window's shortcuts is a one-shot — refresh, open the manual, launch the game. Each repeat ran the whole command again. Repeats are now ignored, on the rule that **a repeat cannot have a key release in between**, so pressing the same shortcut twice deliberately still runs it twice however quickly it is done. Typing is untouched: a held Backspace in a search box still repeats, because the key is left to whatever has focus.
*   **A second fault underneath it, which a held key only exposed.** The "a refresh is already running" guard was released the moment the update check was *launched*, not when it finished — and the check is the slow part. For its whole duration another Refresh Everything was waved through, so even two deliberate presses produced two full folder rescans. A running update check now counts as busy for Refresh Everything, which is a refresh *and* a check.
*   **Refresh Installed Mods is deliberately not blocked by a running check.** Rescanning the folder has nothing to do with asking Nexus about versions, and that difference is the whole point of having two commands.

---

## ✨ New: settle a mod that is offered the same update forever (Ctrl + Shift + Y)

*   Some mods are offered an update that installing never settles — the row returns on the next check, at the same version, however many times you take it. **Stardew Voices** is a real example: Nexus says 2.0.3.5, the mod's own manifest says 2.0.3, and no update can ever close that gap.
*   **The two numbers are not allowed to agree.** SMAPI requires a semantic version in a manifest — in its own words, *"should be formatted like 1.2, 1.2.30, or 1.2.30-beta"* — while a Nexus version field is free text with no rule at all. An author who publishes a four-part version on Nexus has a manifest that must still carry a three-part one.
*   ⚠️ **Editing the manifest to match is not the answer, and the manual now says so plainly.** SMAPI does not warn about a version it cannot parse; it refuses the mod and skips it. The mod stops loading entirely — a far worse problem than the one being solved, and a much quieter one.
*   In the **Updates** tab, **Ctrl + Shift + Y** now records the offered version as the one you already have. The row goes and stays gone — **and anything genuinely newer is still reported**, because what is stored is a version to compare against, not a version to hide. That is what separates it from **Delete**, which mutes one specific version and nothing else.
*   It is also in the **Mods** menu, it is remappable like every other shortcut, and **Shift + F1** on the Updates tab now reads it out along with the rest of that tab's keys.
*   This only ever mattered for mods that arrived outside the manager. When the manager installs a download it already records the release it actually installed and compares against that — which is why most mods never showed the problem at all.

---

## 🐛 Fixed: a required mod reported as "not installed" when it was installed and enabled

*   Check My Setup (**Ctrl + Shift + K**) and the requirements report could claim *"Artisan Valley requires "DIGUS.ProducerFrameworkMod", which is not installed"* while Producer Framework Mod sat in the list, enabled, and the game loaded both without complaint.
*   **The two mods spelled the same id differently.** A Stardew mod's `UniqueID` is typed by hand in two places by two different authors — once by the mod itself, once by every mod that needs it — and they drift apart. Producer Framework Mod calls itself `Digus.ProducerFrameworkMod`; the packs written for it ask for `DIGUS.ProducerFrameworkMod`, the spelling its author used at the time. **SMAPI matches these ids ignoring case**, so the mods load; the manager compared them letter for letter and decided the mod was absent.
*   Requirements are now matched **ignoring case and any stray spaces**, the way SMAPI matches them — so the report agrees with what the game does.
*   **Where a mod is installed twice, an enabled copy now satisfies the requirement in preference to a disabled one**, since the enabled copy is what the game will load. Previously whichever copy the scan happened to reach first decided the answer, so a leftover disabled duplicate could produce "installed, but not enabled" about a mod that was enabled.
*   This mattered beyond the report: the same matching decides what the dependency view (**Ctrl + Y**) shows, what "resolve missing requirements" offers to go and fetch, and whether a mod's row in the installed list ends with *"Warning: missing required dependencies"* — so a wrongly-matched id followed you around the list, spoken every time you arrowed past that mod.

---

## ✨ New: the Suggested Mods list now ships with suggestions in it

*   The Suggested Mods viewer (**F7**) has always shown two layers merged — the list that ships with the manager, and your own marks over the top. Until now the shipped layer was **empty**, so a new user opened the viewer to nothing at all.
*   This release ships **20 suggestions across all four moddable games** — 10 for Moonlight Peaks, 6 for Skyrim Special Edition, and 2 each for Fallout 4 and Stardew Valley — each with the reason it is there.
*   Your own marks still win over the shipped ones, so nothing you have curated changes.

---

## ✨ New: why the game launches fine and the mods still do nothing

*   A matching SKSE/F4SE is only the **first link**. The real chain is **game → script extender → Address Library → DLL plugins → the mods that need them**, and a break anywhere below the script extender is **silent**: the game starts, the script extender loads, the plugins are skipped, and nothing anywhere says so. When one of those plugins is the accessibility mod, the entire symptom is a game that opens normally and then never speaks.
*   **Before launching, the manager now checks the Address Library.** Most DLL plugins need it, and it ships one data file per game build — so when the game updates ahead of it, *every* plugin that uses it stops loading at once. The manager compares your game's build against the data files actually installed and, if yours is not among them, says so plainly and names the newest build the library does cover.
*   **Check My Setup now reads the script extender's own log** and lists, by name, each DLL plugin that was refused the last time you played, sorted into the two problems that have different cures: *"it needs an Address Library for game version 1.11.240"* versus *"it is built for a different version of the game"*. The first waits on the Address Library's author; the second waits on that mod's own author.
*   Support files that were never plugins — `msdia140.dll`, shipped by the crash loggers — are **not** reported. The script extender mentions them on every run and they are entirely normal; flagging them would be crying wolf about a healthy install.
*   This also explains something that reads as a contradiction: the update check can say **"all mods are up to date"** while the game is unplayable. That check compares your mods against Nexus, and if no author has published a build for the new game version yet, there is genuinely no update to find. Your mods are not behind Nexus — they are behind the game.
*   Written from a real case: Fallout 4 updated to 1.11.240 on 18 August 2026 and F4SE 0.7.9 arrived the same day, so the script-extender check was satisfied and said nothing — while Buffout 4, the crash logger and Fallout 4 Access were all disabled waiting on an Address Library newer than any that existed, and MCM and the Extended Dialogue Interface were disabled as builds for the previous game version.

---

## ✨ New: search the manual, the change log and the mod docs (Ctrl + F)

*   **Ctrl + F** in the **User Manual** (**F1**), the **Change Log** (**F2**) or the **Mod Documentation** viewer (**F3**) searches **every line of every section at once**. Type a phrase, press **Enter**, and you get a list of the lines that contain it.
*   Until now these were drill-downs and nothing else, which is a good way to read a document whose shape you already know and a poor way to answer *"where does it say anything about F4SE?"* — the list of sections can only offer the headings someone thought to write, and everything else had to be found by opening sections one at a time.
*   **Each result says which section it is in**, after the matching text: *"…the installed F4SE must match your game's version — in Launching the Game, Script Extender Check."* The text comes first because that is what tells one result from another; the section is the context for it. A long line is shortened around the phrase, with its Markdown punctuation taken off so the row reads as words rather than asterisks.
*   **Enter on a result takes you there** — the section opens and the cursor lands on that exact line, which is then read out along with which result it is. **Left Arrow** still walks back out a level at a time, exactly as if you had opened the section yourself.
*   **F3 moves to the next result, Shift + F3 to the previous**, without going back to the list. They wrap around at either end and say so. **Escape** on the results list keeps the results, so F3 still steps through them; **Ctrl + F** starts a new search.

---

## ✨ New: follow a link from inside the manual and the mod documentation

*   In the read-only text of any of the three document viewers, **Enter on a line containing a web address** asks whether to open it in your browser — the same as pressing Enter on a link in a game log or a mod's description. A manual full of Nexus and GitHub addresses was of little use when the only way to follow one was to write it down and type it in again. A line holding **several** addresses offers a short list to choose from, as a log line does.
*   **An accessibility mod's documentation keeps its link addresses now.** Tidying a mod's page up for reading used to throw the address away and keep only the words, which reads better but leaves a link that nobody can follow — and these mods do link to things worth reaching. A link now reads as its words followed by its address, matching how a mod's description has always been shown.
*   Links pointing **inside** the document, or at a file in the mod's own source repository, still keep just their words. Nothing here could open those, so reading their addresses aloud would be noise charged against nothing — and documentation written for GitHub is full of them.

---

## 🐛 Fixed: a manual section is now made of lines, not one enormous line

*   **The text pane in the F1 and F2 viewers had no line breaks in it.** The whole of a section arrived as a single unbroken line, so arrowing down through it moved nowhere and there was nothing to read line by line. The manual's own line endings were being handed to the text box in a form it does not break lines on.
*   It also had to be fixed for the search above to mean anything: "go to that line" needs there to be lines.

---

## ✨ New: a list entry that opens says so, and says how

*   In the document viewers and the **Accessibility Controls** viewer (**Ctrl + H**), an entry that has sub-topics now names the keys that move in and out — *"3 of 21, has sub-topics. Press right arrow to open, left arrow to go back."* — the same hint a mod group carries in the installed mods list. The entry is a door, and nothing else about it said that it was one.

---

## 🐛 Fixed: SKSE reported as "not installed" when it was installed (GOG)

*   **The manager decided whether the script extender was installed by looking for `skse64_loader.exe` and nothing else.** But the loader is only one way to *start* SKSE — many players, and most GOG ones, load it through the SSE Engine Fixes preloader instead and have no loader exe at all. Their perfectly working SKSE was reported as missing, in the Accessibility Suite list and in Check My Setup alike, and the installer offered to install it again over the top.
*   The script extender is now judged by **its files** — the versioned runtime DLL or the loader — because the DLL is the script extender and the loader is a convenience. Launching is unaffected: the manager still starts the game through the loader where there is one, and through the game's own exe where there is not.
*   **"I don't know where your game is" is no longer reported as "it isn't installed".** Those are different statements and only one of them was ever true. When the game folder is unknown the report says so and points at Settings; when the folder is known but empty of a script extender, the report **names the folder it searched** — which is also how you spot the manager looking at the wrong copy of a game you own twice.
*   **A second copy is no longer answered with the first one's folder.** Where the manager had to fall back to detecting the game, it took whichever copy it found first and the probes run Steam-first — so on a machine with both, a GOG session could be handed the Steam folder and every question after it answered about a copy you were not playing. Detection now honours the store the session names.

---

## 🐛 Fixed: the script extender's version is the one that will actually load

*   Installing a newer script extender replaces the loader but only adds **its own** build's file, leaving the previous build's file in place for the game you are still running. The manager read the version off the loader, so a folder holding a 2.3.0 loader alongside the 2.2.6 file the game would really load reported **2.3.0** — a version that was present but not in use.
*   It now reports the version of the file that will actually be loaded for your game build, and lists the other builds present separately.

---

## 🐛 Fixed: an up-to-date SKSE/F4SE no longer reports itself as out of date

*   **After updating the script extender, every launch still warned that it didn't match your game** — *"The installed F4SE is built for game version 1.11.221, but your game is version 1.11.240"* — even though the correct build had just been installed and would have loaded perfectly well.
*   SKSE and F4SE ship **one file per game version**, named for the version it serves, and the loader uses only the one matching the game you are running. Installing a newer script extender therefore leaves the previous version's file in the folder — nothing removes it, and nothing needs to. The manager's pre-launch check, however, looked at whichever of those files it happened to find first and compared that one alone. Once two were present, it was as likely to pick the old one as the new one.
*   The check now considers **every** script-extender file in the game folder. If any of them is built for the game you are running, the script extender will load and the manager says nothing — which is the truth of it. A warning now means what it always should have: that **no** installed build matches your game.
*   The **leftover file itself is left alone.** It is harmless, it costs nothing, and if you ever roll your game back to the older build it is the file that will make the script extender work again.

---

## ✨ New: find out which script extender you have installed

*   SKSE and F4SE install as loose files in the game folder rather than as mods, so they have never appeared in your mod list — which left **no way to ask what was installed**. Now the **Accessibility Suite** status list answers it: *"F4SE (Script Extender): Installed, version 0.7.9, built for game 1.11.240, which matches your game."*
*   **Two numbers, because two numbers matter.** The first is the script extender's own version, written the way its download page writes it; the second is the game build it was compiled against — which is the one that decides whether it loads at all. Where they disagree the line says so plainly, and **Install Missing Mods** puts the matching build in place.
*   The same line mentions any files **left by earlier installs**, so a file in your game folder that you never chose to keep isn't a mystery.
*   **A script extender built for the wrong game version is now a Check My Setup finding**, listed alongside a missing one. From inside the game the two are indistinguishable — the script extender simply isn't there — so the health check now names the difference instead of reporting all-clear.

---

# Version 1.5.0

Moonlight Peaks and The Witcher 3 join the manager as fully supported games, alongside Stardew Valley, Skyrim Special Edition and Fallout 4.

---

## 🐛 Fixed: moving between tabs no longer says "tab control" first

*   **Every tab change was read as the role of the strip before the name of the tab** — "tab control, Installed Mods" — on the main window and in Settings alike, on every single press. It is now just the tab.
*   The fault was in the tab strip's own bookkeeping, and it took measuring what the screen reader actually receives to find, rather than reading the code. A tab strip announces the focused tab **by number**, and the numbering it sent out did not match the list it published: the selected page's contents were counted first, pushing every tab one place along. So each announcement named the tab **before** the one you had moved to, and described it as neither focused nor selected. Given something that self-evidently could not be right, the reader discarded it and fell back on describing the strip itself — which is the "tab control" that was heard. The two now agree, so the tab you moved to is the tab that is named.
*   Three earlier attempts had gone after the strip's *name* instead, on the reasonable theory that the reader was reading a label it should have ignored. None of them could have worked: the strip was never what the reader was being pointed at.
*   **The opening now plays in one piece, and the manager is properly the window you are in.** As the session loads, the screen reader names the window and the tab you have landed on, and everything the manager says follows in order behind it — the welcome, which session is loaded, the connection to Nexus. Nothing cuts into anything else, at any speech rate: the manager no longer waits a guessed length of time for speech to finish, because a wait tuned to one person's speech rate is wrong for everyone else's.

---

## 🐛 Fixed: filtering the installed mods list no longer loses your mod groups

*   **Narrowing the list threw away its grouping, permanently.** Choosing anything in the Status dropdown — or typing in the search box, or picking a category — replaced the grouped list with a flat one, and setting it back to **All Mods** did not bring the grouping back. On a Stardew Valley setup of 198 mods, 147 grouped rows became 198 loose ones and stayed that way.
*   Two different parts of the manager knew how to build that list, and only one of them knew about mod groups. The filters were calling the other one, which had never done anything but flatten. They now both go through the one that groups, which is what the code had claimed to do all along.
*   **Mod groups now survive filtering**, and are collapsed or expanded exactly as you last left them rather than being reset. A group is built from the mods that **matched**, so a group under **Enabled Only** saying "Contains 2 mods" means two enabled ones. A folder with only one match is shown as an ordinary row rather than a group of one.

---

## ✨ New: two more ways to sort the installed mods, and counts that explain themselves

*   **Single Mods Only** and **Mod Groups Only** join the Status dropdown. The first four options sort by what you have done with a mod — all, enabled, disabled, has a note — while these two sort by its **shape**: whether it stands on its own or shares a folder with other mods. Between them they account for every mod you have, so the two counts adding up to your total is a quick check that nothing has been missed.
*   **The sorting is heard before the numbers.** The counts used to arrive first and your screen reader's name for the option you had just moved to second, so you sat through two figures to learn which view you had landed in. The manager now waits for the reader to name the option and follows with the counts — and does not name the option itself, which for a while had it said at both ends of the sentence. Arrow quickly past several options and only the one you stop on is counted.
*   **Where mod groups are involved, the rows are counted as well as the mods** — *"All Mods. 198 mods found. 147 rows, including 31 mod groups."* A collapsed group is one row standing for several mods, so the two numbers are rarely the same, and a list that looks far shorter than its total was simply confusing. Where a sorting holds no groups, only the total is given.
*   **Every setting of the dropdown is announced, including All Mods.** Widening back out is as much an answer as narrowing was, and the one option that stayed silent read as the manager having missed the keypress.

---

## ✨ New: reach a mod's config file itself, as well as its settings

*   **Ctrl + Shift + M opens the selected mod's `config.json` directly in the editor**, the same way Ctrl + M opens its manifest. It is also on the Mods menu, under Selected Mod, as **Edit Selected Mod's Config File Directly**.
*   This closes a gap the settings list created. Offering a mod's settings as a list of choices is the better way to change them, and Ctrl + E still does exactly that — but a list can only offer what the mod's author described. A setting they never documented, or a value outside the ones they listed, was left with no way in at all once the list took over.
*   The two now read as a pair: **Ctrl + E** for the settings, **Ctrl + Shift + M** for the file behind them, alongside **Ctrl + M** for the manifest. A mod with no `config.json` yet says so, and points at Ctrl + E — most mods only write one the first time the game runs with the mod enabled.
*   **On Moonlight Peaks it opens the right file.** That game's mods are BepInEx plugins, which keep their settings in a `.cfg` in `BepInEx\config` rather than a `config.json` beside the mod — so looking next to the mod found nothing and reported every mod as having no config file, when nearly all of them have one. It now looks where Ctrl + E looks, so the two always agree about which file a mod's settings live in, and the file is saved as it is rather than being checked as JSON it was never meant to be.

---

## 🐛 Fixed: you stay on the mod you were working on

*   **Refreshing the mod list no longer moves you to a different mod.** Anything that re-scanned your mods — saving a config or manifest, installing, enabling, disabling — rebuilt the list and left you on whichever mod happened to end up first, rather than the one you were on. Halfway down a long list, that means finding your place again every time.
*   The list already knew how to put the selection back; it read which mod to return to *from the list itself*, and by then the list had been emptied, so there was nothing left to read. The mod you were on is now remembered before the rescan starts and restored when it finishes.
*   **Editing a mod's settings or manifest returns you to that mod**, whether you saved with Ctrl + S or left with Escape, and whichever editor the mod turned out to need — the raw JSON editor, a Content Patcher pack's settings, a Mod Configuration Menu, or a BepInEx config file.
*   This applies to every game, not only Stardew Valley. A mod that is no longer there when the list comes back — you just deleted it, or you switched games — still lands you at the top, which is the only sensible answer.
*   **You are told which mod you landed on, not just its number.** The manager only ever announced the position — "twelve of a hundred and forty-seven" — and left the mod's name to the screen reader, which reads out the row you arrow onto. But a reader only announces a row *you* moved to. When the program moves the selection, or hands focus back to the list as a screen closes, the reader treats it as focus never having left and says nothing — so all that was heard was a position belonging to no mod in particular, and the only way to learn which mod it was was to arrow off the row and back onto it.
*   **Closing any in-window screen now says which mod you have come back to**, and so does any move the manager makes on your behalf. A move you make yourself with the arrow keys is unchanged, so nothing is ever said twice.
*   **And it no longer reads out a mod you were never on first.** A list keeps two positions — what is selected, and where its focus rectangle sits — and a keypress moves both while setting the selection from code moves only one. The stale focus rectangle is what a screen reader reads when focus arrives, announcing that row and adding "not selected" before the real one was ever mentioned. The two are now put back together before focus returns.
*   **The list is put back on your mod before you arrive, not after.** This was the whole of it. Closing a settings or manifest screen handed focus back to the list first and corrected the selection second — so everything that speaks on arrival spoke about whichever mod the list happened to be sitting on, and the correction that followed arrived as a *second* announcement, cutting off the first mid-sentence. What you heard was a mod you were never on, then your mod. The correction now happens inside the close, before focus moves, so there is one arrival and one thing said about it.
*   **An announcement that has been overtaken is now dropped rather than spoken and interrupted.** When two of them were in flight for the same arrival, the older one still said its piece until the newer one talked over it. The older one now simply stops: the last word belongs to whichever knew where you actually were.
*   **And the list still says what it is.** Silencing the wrong announcement silenced the right half of it as well, so coming back from a screen went straight to the mod without ever saying "Installed Mods List" — leaving nothing to say which of the manager's lists you had landed in. The name is now spoken by the manager itself, in the order a screen reader would have used it: the list, then the mod, then where that mod sits. Moving *within* a list does not repeat it, because you have not left.
*   **Coming back to a list that never lost focus is announced too.** Closing a screen sometimes leaves focus where it already was, and focusing a control that already has focus tells a screen reader nothing — so on those occasions the list came back in complete silence.
*   **Narrowing the list with the search box keeps your place too**, where the mod you were on is still among the results.

---

## 🐛 Fixed: every screen now opens the same way

*   **A screen says its name, then what is in it, then the row you landed on** — in that order, everywhere. Sixteen screens used to say their opening *before* their own title, because anything spoken while a screen is being built is said ahead of the title the manager adds afterwards. So the instructions arrived first and the name of the screen second, which is backwards, and on several screens the name was then said a third time by the list inside it.
*   The worst of these repeated themselves outright: **Restore a Safety Backup** opened with "Safety backups for Skyrim", **Load Order Rules** with "Load order rules for Skyrim", and the same for **Tracked Mods**, **Reinstall a Downloaded Mod**, **Manage Save Games** and **Choose File Conflict Winners**. Each now opens with its title once, then the count and the keys.
*   The game's name went with those repeated openings. It is still written on the screen, and the window title has said which game you are in all along — but if you would rather hear it, say so and it comes back.
*   **A long opening is no longer cut short.** Screens whose opening *is* their content — a mod's description, an answer from the AI assistant — are now read in chunks like every other long passage, instead of as one utterance a screen reader can silently truncate.
*   **The FOMOD installer names itself before its first question.** Every later step already announced itself as you pressed Next or Back; only the first arrived ahead of the wizard's own title.

---

## 🐛 Fixed: changing a setting tells you what it changed to

*   **"MenuClosedAnnouncements set to false" is spoken again** the moment you press Enter on a value. It was being said and then silenced before it could be heard, so the only way to learn what a setting had become was to arrow off the row and back onto it.
*   The manager silences the screen reader for a moment when it moves focus itself, to stop the reader announcing a row you were never on. Silence cannot tell the reader's voice from the manager's, so anything the manager said in that moment went with it — and choosing a value closes a screen, which is exactly when that silence was armed. Saying something deliberately now ends the silence rather than falling into it.
*   This applies to every settings editor: Content Patcher packs, Mod Configuration Menus, Stardew configs and BepInEx `.cfg` files alike.

---

## 🐛 Fixed: lists that told you nothing about where you were

*   **Six lists never announced your position.** Arrowing through them read out each row and nothing else — no "four of eleven" — so there was no way to tell a long list from a short one, or to know you had reached the end without walking off it. This affected the **game picker** the manager opens with, the **theme manager**, the **search history**, the **store page chooser**, the **collection review** before an install, and the **link picker** in a SMAPI log line.
*   **The MO2 profile list and the log link picker were each half-wired**, in opposite directions: one announced the position while arrowing but said nothing when you tabbed into it, the other said "one of three" on the way in and then went quiet.
*   In all of these, **Left and Right now do nothing**, as they do everywhere else in the manager. A single-column list treats them exactly like Up and Down, which reads as the selection jumping about for no reason you pressed.
*   The collection review list also **named itself with its whole summary sentence** — "42 mods, 30 will download automatically…" — which a screen reader repeats every time focus lands on it. It now has a short name, and the summary is spoken once, when the review opens.
*   A test now checks every list in the manager for both halves of this wiring, so a new screen cannot ship half-announced.

---

## 🐛 Fixed: mods are called by their names, not by their download's file name

*   **The manager now says a mod's name, and only its name.** Downloading, installing, "installed", and the new cross-game download question all used to read out whatever the download happened to be called — which is not the mod's name, and sometimes is not a name at all.
*   Nexus hands out a file called `Skyrim Access-181131-1-2-3-1723456789.7z`: the mod's name, then its mod id, then its version with the dots turned into dashes, then the moment it was uploaded. All of that was being read aloud. **Only the name is now spoken**, and a number the author put in the name themselves — "Mod Configuration Menu 1.11.221" — is kept, because that one is part of what the mod is called.
*   Some downloads arrive with **no name at all**: certain Nexus content servers answer with an opaque id like `99824770-6ed9-4868-9f98-b54fb58ecad6`, which was then read out one character group at a time as though it were the mod. When the file name holds nothing readable, **the mod's name is now fetched from its Nexus page instead**. That is the only case that makes the extra request, so every other download is exactly as quick as it was.
*   **Mods no longer install into folders named after the download.** A folder called `Address Library - All In One-47327-1-11-221-1780112703` is now simply `Address Library - All In One`. The archive itself keeps its original file name, deliberately — the id and version buried in it are what later tell the update check which release you have installed.
*   **Mods installed before this release are read out by their names too.** Their folders keep the names they have, since renaming a folder would break the record of what has been deployed into your game — but the manager no longer reads the folder name aloud when it knows better.
*   A download that arrived with no name also arrived with **no file extension**, which quietly kept it out of Downloads History. Those are now saved under a readable name so they can be found and re-installed like anything else.

---

## 🐛 Fixed: downloading a mod for a game you don't have open

*   **"Mod Manager Download" now works whatever game you have loaded, or none at all.** Finding a mod on Nexus while another game was open — or with the manager closed, or sitting on the game list — reported `NXM Error: Error reading JArray from JsonReader`, which named nothing you could do anything about.
*   The cause: every download link carries the game it is for, and the manager was ignoring it and **asking Nexus about the loaded game instead** — or, with no game loaded, about Stardew Valley, its fallback. So it asked for a Skyrim mod's id under another game's name, Nexus quite correctly said there is no such mod in that game, and the reply was an error message where the manager expected a list of download servers. The link's own game is now what counts, so the download no longer depends on your session at all.
*   It also only knew **three of the five games**. Moonlight Peaks and The Witcher 3 were missing from the list it consulted, so a download for either could never work from another game — they now come from the same registry as everything else, and a sixth game would need no change here.
*   **A mod for a game the manager doesn't support now says so** — "That mod is for a game this manager doesn't support yet" — instead of failing as a parser error. And where Nexus itself refuses a download, **Nexus's own explanation is what you hear**, which is usually that the link has expired and needs pressing again.
*   Downloading is now separate from installing, because only installing needs a session. So the file is fetched first, filed under the game it is actually for, and **then** you are asked what to do:
    *   **Switch to that game and install it now**, or **save it for that game and stay where you are** — in which case it is waiting in that game's **Downloads History** the next time you load it, one keypress from installed. Escape keeps the download and changes nothing else.
    *   With **no game loaded** there is nothing to interrupt, so it just loads the game and installs, saying which game it went to.
    *   If you own that game **twice**, you are asked which copy the mod is for — nothing in the link says, and guessing would file someone's mod under a copy they weren't thinking of.
    *   Settings (Ctrl+P) → **Mods & Search** → **"When a download is for another game"** turns the question into an answer: *Ask me each time* (the default), *Switch to that game and install*, or *Save it for that game*.
*   Two smaller consequences of the same wrong assumption went with it: a cross-game download used to be **filed in the loaded game's downloads folder** rather than its own, and the "is this an upgrade or a re-install?" check **asked about the wrong game**, so it could ask you to confirm overwriting a mod that was really an update.

---

## 🐛 Fixed: refreshing, switching games, and Witcher 3 mod names

*   **Switching games could leave the previous game's mods on screen.** If an update check was still running, the switch's refresh was refused outright — so the title read "Moonlight Peaks" above a list of Skyrim or Stardew mods, and switching again didn't help. Re-scanning your mods folder has nothing to do with checking Nexus for versions, and it now always happens.
*   Two refreshes could also overlap — a slow one started before a switch, and the switch's own — and whichever finished last won, which was usually the game you had just left. Only the most recent refresh can now put anything on screen.
*   **"Refreshing everything" was announced twice**, with "update check already in progress" between the two. Holding a Refresh key a moment too long repeats it, which sent the command twice; a repeat now simply says **"Already refreshing"**. **Refresh Installed Mods** also said "Refreshed" before it had done anything — it now says "Refreshing" when it starts and "Refreshed" when the scan has actually finished.
*   **Moonlight Peaks mods showed the version they were before you updated them.** A mod updated from 1.0.0 to 1.1 kept reading 1.0.0, while the update check — quite correctly — said it was up to date, because the two were answering from different places. The version came from BepInEx's log, which only records what it saw the last time the **game** ran; update your mods between play sessions and it is describing files that no longer exist. The mod's own declaration is now what counts, and a log older than the mod's files is not consulted at all.
*   **The goodbye message is now spoken when you ask to exit, not as the program is going.** It sometimes didn't start until the shutdown was already under way, because the manager announced it and then waited by blocking — and speech needs the program responsive to be produced at all, so the wait was holding up the very message it was waiting for. Choosing Exit or pressing Alt+F4 now speaks the message straight away, waits for it to finish, plays the disconnect sound, waits for **that** to finish, and only then closes. Pressing Alt+F4 again while it is talking no longer cuts it short. (A Windows log-off still closes immediately — there is no time to be granted there.)
*   **The keyboard shortcut for Open Error Log never opened anything.** It looked for the log by name alone, which searches the folder the program was started from rather than the folder the log is actually written to, so it always concluded there was no log and did nothing — in silence, with no way to tell that from a shortcut that had failed. The menu item was looking in the right place all along; both now go through the same code, and if the log really is empty the manager says so.
*   **The tab strips no longer announce themselves before every tab.** Moving along the main tabs read "Search" in front of each tab name — the strip had no name, and a screen reader will not accept that: it goes looking and reads whatever text it finds nearby. Naming it only replaced one unwanted word with another, since the strip is announced every time the selection moves. Both the main tabs and the Settings tabs are now silent, so you hear the tab and nothing else.
*   **The About screen's text said "Search" before it said anything else**, for the same reason and with the same cause: the text box had no name of its own, so the reader borrowed the search box's. It is now called "Program information", which is what it holds — and deliberately not what the heading above it already says.
*   **A Witcher 3 mod packaged inside a `mods` folder installed one level too deep** — into `mods\mods\` — where the game never looks, so it silently never loaded. `mods` starts with `mod`, so it matched the engine's own naming rule and was mistaken for the mod itself.
*   **Witcher 3 mods no longer claim a version and author nobody wrote.** A Witcher mod folder carries neither — the game never needed them — so the manager used to fill in "Unknown" and "1.0.0". An invented 1.0.0 is worse than no version at all, because it looks like something you can act on. They now read **"version unknown"**, and mods already installed are corrected too.
*   **Witcher 3 mod names are readable.** `modRandomEncountersReworked` reads as "Random Encounters Reworked" and `mod_sharedutils_glossary` as "Shared Utils: Glossary".
*   **A framework that ships as a dozen folders is now one row.** Witcher mods all sit side by side with nothing to say which belong together, but a shared `mod_family_*` name is the author saying so. One real install went from 12 rows to 5, with eight shared-utility folders behind a single group.

---

## ✨ New: Suggested Mods — the mods that remove a wall, and why

### 🎯 A second list, with a different promise

*   The **Accessibility Suite** installs what a game *needs* before it can be played by ear at all. **Suggested Mods** answers the question that comes after that one: the game is playable now, so which mods remove a wall that is still there? Auto-fishing and time control for Stardew Valley; auto-tool-select and quick spells for Moonlight Peaks; audio lockpicking and the puzzle-pillar solvers for Skyrim and Fallout 4.
*   The two are kept as **separate lists on purpose**, because they promise different things. Everything in the suite is required. **Nothing in Suggested Mods is** — every entry is optional, several make the game easier as well as more reachable, and each one says *why* it is there so you can turn it down on purpose rather than taking a list's word for it. It is called "Suggested" rather than "Recommended" for the same reason.
*   Open it from the **Mods** menu → **Suggested Mods** → **"Suggested Mods for This Game"**.

### 📖 Reading the list

*   Mods are **grouped under category headings** that say how many are beneath them — *"Can't be done by ear, 3 mods"* — so a group you don't want can be skipped in a couple of presses. The alternative, repeating the category at the start of every row, buries the mod's name behind a phrase you have already heard.
*   Each row reads as the mod's **name**, whether it is **Installed** or **Not installed**, and the reason it was suggested: *"Auto-Fishing. Not installed. The fishing minigame is a moving bar with no audio cue."* The installed check uses the strongest evidence available — Nexus ID, then mod ID, then name — so a mod you installed by hand is still recognised.
*   **Enter** installs the mod you are on, after a confirmation. **Ctrl + G** opens its page so you can read about it first, the same key that opens a mod's page everywhere else. A game with nothing suggested for it yet says so plainly, because the list is filled in game by game and an empty one is an answer rather than a fault.

### 🗂️ Your list and the shipped list stay separate

*   There are **two sources**: the list that comes with the manager, and your own. You see them together, and where both describe the same mod **yours wins**.
*   That split is the whole point. The shipped list is replaced by each new version of the manager, and your own list is never touched by an update — so a better shipped list can arrive without overwriting a reason you took the trouble to write, and nothing you write can freeze an out-of-date copy of the shipped list in place.

### ✍️ Building a list of your own

*   **Ctrl + Shift + F7** turns **Curation Mode** on or off. It is off to begin with, and while it is off the submenu holds only the commands that make sense — reading a list and importing one. Turn it on and three more appear, so the menu never offers a command you cannot use.
*   **F7 marks the mod you are on, or unmarks it if it is already marked.** It works in your **Installed Mods** list *and* in **Find New Mods** — which matters, because otherwise you could only suggest a mod you had already installed, and recommending something for five games would mean installing it five times.
*   Marking asks two things: a **category**, picked from a list, and a **reason**, in one line. The reason is the part worth care over — *"Auto-Fishing"* tells a reader nothing, while *"the fishing minigame is a moving bar with no audio cue"* tells them at once whether they need it. **Unmarking asks you to confirm**, because it throws away that sentence and nothing can bring it back.
*   **Shift + F7** opens the mods you have marked, across every game. **Enter** changes a category or reason and **Delete** removes an entry. This is the only place to edit one, so that F7 means membership and nothing else — one key, one meaning.

### 🏷️ Categories you can shape

*   Four come with the manager: **Can't be done by ear**, **Much slower by ear**, **Bug fix or stability**, and **Makes the game easier**. That last one exists so a mod which also changes the difficulty says so, and a player who would rather keep the challenge can pass it by.
*   The first two are also the test worth applying when you are unsure a mod belongs at all: **does the mechanic have to be read off the screen as it happens?** If it does, the mod that removes it is a good suggestion. That question settles most cases without needing an opinion about whether the mod is any good.
*   **You are not limited to those four.** When none of them fit, **"New category..."** at the bottom of the pick list makes one on the spot — you are not sent to another screen and made to start the mod again.
*   **Mods → Suggested Mods → Manage Categories** lists them in the order they are offered, each saying how many mods it holds. **Enter** renames (clearing the box puts a built-in's original name back), **Ctrl + N** adds, **Ctrl + Up / Ctrl + Down** reorders, and **Delete** removes. Deleting one that is in use **asks where its mods should go** and re-files them, so nothing is ever left pointing at a category that no longer exists. The four built-ins can be renamed and reordered but not deleted, since the shipped list is filed under them.

### 🤝 Sharing a list with somebody else

*   **Export Your Suggested Mods** writes your list to a file to pass on, asking for a name and an author — leave the author empty to stay anonymous. Only **your own** entries are exported, never the shipped list: a file carrying shipped entries would plant a frozen copy of them into the recipient's personal list, where they would then win over every future update. The categories your entries actually use travel with the file, so your headings arrive with them.
*   **Import a Suggested Mods List** takes one in, and is available whether or not Curation Mode is on — receiving somebody's list is not the same as building one. Before anything is written it tells you what the file would do: *"3 new suggestions, 2 you already have described differently, and 4 already the same. Import it?"*
*   **Where you disagree, you choose how to settle it** — keep your wording for all of them, use theirs for all of them, or **see each one and decide**. The bulk answers are there because a long list overlapping yours in a dozen places should not be a dozen prompts; the third is there because choosing between "mine" and "theirs" in bulk means choosing blind.
*   Deciding one at a time shows you **both versions**, and asks about the **category and the reason separately** — so you can take their category and keep your own wording, which a single "mine or theirs" cannot express. A half you already agree about is not asked at all, so differing only over wording never makes you confirm the same category over and over. **Escape** at any point abandons the import with nothing written.
*   **Duplicates are not something you have to watch for.** A mod is recognised by its Nexus ID (or its GitHub repository, or its mod ID, or failing those its name) scoped to the game, so importing the same file twice changes nothing the second time, and a mod that has merely been renamed is not treated as a disagreement.

---

## ✨ New: A way to support development, for anyone who wants one

*   **Help → Support Development (Donate)** opens a short note from the author and two ways to give: **PayPal** and **Cash App**. Both are buttons that open the payment page in your browser, and there is also a **Copy Cash Tag** button, which is the easier route if you'd rather pay from the Cash App on your phone than in a browser.
*   The message lists both addresses **in full, as text you can read with your screen reader**, rather than hiding them behind buttons you have to trust. Where money is involved, being able to check where it's going before you click matters more than tidiness.
*   It is **only ever a menu item**. Nothing prompts you, nothing appears at startup, and nothing is withheld from anyone who never opens it — the manager is free and stays free.
*   If no browser can be opened — which does happen on Linux under Wine — the link is copied to the clipboard instead, and the manager says so rather than appearing to do nothing.

---

## ✨ New: Check My Setup now checks Windows itself

*   **Check My Setup (Ctrl + Shift + K) now looks at the Microsoft Visual C++ Redistributable**, the piece of Windows that every mod written as a program file depends on — which is all of the accessibility mods, in all five games. It reports the fault first, ahead of everything else, because unlike a missing requirement or a file conflict this one affects **every game at once**.
*   The fault it finds is a runtime whose files **no longer match each other**. A game or an installer replaces part of the set with an older copy, and afterwards the pieces come from different versions and refuse to load together. Windows' own list of installed programs still reports the newer version, so there is nothing to notice.
*   This was worth building because of **how it fails, not how often**. There is no crash and no error message — the game starts, and the accessibility mod simply never speaks. That is indistinguishable from a mod you've set up wrong, and there is nothing to read to tell the difference. It cost the author an evening across two games before the cause turned up; the check now states it in a sentence.
*   The finding **names the files that are out of step and the version the rest of the set is on**, and **Enter** opens Microsoft's download page. It deliberately links the page rather than a direct download, because the direct link is pinned to one version — and offering someone an older build than they already have gets refused as a downgrade instead of repairing anything, which is its own dead end.
*   Nothing is reported when the runtime is healthy, and the check is **skipped entirely under Wine on Linux**, where these files belong to the compatibility layer and their version numbers don't mean the same thing.

---

## ✨ New: Two copies of the same game, and the right file for the one you're on

### 🎮 Own a game twice? Both copies now show up
*   If you own the **same game on both Steam and GOG**, both copies now appear in the **Games** menu, each named by its store — "Skyrim Special Edition (Steam)" and "Skyrim Special Edition (GOG)". The store is only shown when there are actually two copies to tell apart, so nothing looks any different if you own one.
*   **Each copy keeps its own everything**: its own mod list, load order, plugin order, mod priorities, conflict overrides, profiles, backups, downloads, save backups and search history. Switching between them switches all of it, and the title bar and every report say which copy you are in.
*   Before this, the two copies shared all of that, and shared it **silently** — mods showed as deployed when their files were actually in the other copy's `Data` folder. Nothing warned you, because as far as the manager was concerned there was only ever one Skyrim.
*   Copies are told apart by **what is in the folder, not where it is**: every GOG install leaves a marker file behind, which is the evidence used. This matters because GOG installs "Skyrim Special Edition" into a folder called "Skyrim Anniversary Edition", and because a Steam copy sitting in a folder named "GOG Games" must not be mistaken for a GOG one.
*   When a second copy turns up, the manager **says so once**, names it and says where it is, and leaves the choice of which to use to you.
*   Nothing changes for anyone who owns one copy of each game: existing settings, mods and history are carried over untouched, with no re-detection and nothing to redo.

### 📁 Mods can live in the game folder now
*   Skyrim SE and Fallout 4 stage their mods in the manager's own folder. You can now **keep them inside the game folder instead**, in a `KinetixMods` folder — which is what Stardew Valley, Moonlight Peaks and The Witcher 3 already do. Settings → Paths, per copy.
*   Two reasons to want it: the mods travel with the game, and because they end up on the same drive as it, the manager can **link** files into the game instead of copying them, which stops a large setup taking up twice the space.
*   One reason to think about it first, which the manager now says plainly before moving anything: **uninstalling the game through Steam or GOG deletes the game folder, and the mods inside it go too.** Verifying the game's files can remove them as well.
*   Existing setups are left exactly where they are — nothing moves unless you ask. Copies the manager meets for the first time start out in the game folder.
*   Nothing is deleted during a move: the mods are copied across first and only removed from the old place once they have all arrived.

### 🔌 The right SKSE, and the Engine Fixes part everyone misses
*   **SKSE now matches your game.** It ships a different build for each game version — and the Steam and GOG copies of Skyrim are different versions, `1.6.1170` against `1.6.1179`. The manager reads the build straight off your game's own program file and fetches the matching one. Previously it took whichever file the author had marked as the main download, which is the **Steam** build — so if you play the GOG copy you were given an SKSE that installs perfectly and then silently never loads. The same applies to F4SE and Fallout 4's version.
*   **SSE Engine Fixes needs two downloads from the same page**, and the second one — the preloader — is a loose file that goes next to the game's program file rather than in the mods folder. Miss it and the mod cannot load. The manager now handles both halves for **every** account, not just Nexus Premium ones.
*   **If you are not a Nexus Premium member**, the manager can no longer download for you — but it can now still work out exactly which file you need, open the page showing **only that file**, say what it is called and where it goes, and install it properly when it comes back. Previously it opened the whole Files tab and left you to find the right one among more than a hundred entries.
*   **Check My Setup now reports a half-installed mod**, so a missing preloader is caught however the mod arrived — installed by hand, from a Collection, or through the Mod Organizer 2 import — with a key to fetch the missing part there and then.

### 🐛 Fixed
*   The **script extender log** (SKSE/F4SE) opened the Steam copy's log even when you were working on the GOG copy, for the same reason the load order used to: the folder name was assumed rather than looked up.
*   SKSE used to be fetched by **reading silverlock.org's front page** and taking the first link that looked right, falling back to a version hard-coded into the manager when that failed. It had no way of knowing which game version you were on. That is gone; Nexus carries the same builds and says which version each one is for.

---

## ✨ New: The Witcher 3: Wild Hunt support

### 🐺 A fifth game
*   **The Witcher 3: Wild Hunt** now appears in the game list, the **Games** menu, the Settings paths picker and the store links. It is found automatically wherever it is installed, on any drive — and under **either** of the two Steam app ids it sells under (Wild Hunt and the Complete Edition) and **either** GOG edition, so it is found whichever version you own.
*   It gets what the other games get: the installed mod list, install and uninstall, enable and disable, backups, profiles, collections, mod notes, the Nexus **Find New Mods** search, update checks, endorsements and the API-request counter — all against The Witcher 3's own Nexus listing.
*   A **Witcher Wiki** tab (searchable, with categories), a **Witcher 3 Walkthroughs** tab of quest, bestiary, alchemy, character-development and Gwent guides, and the Nexus modding wiki in the **Mod Wikis** dropdown.
*   **A Witcher 3 sound theme**, drawn from the game's own audio, which the manager switches to whenever you load that session — the connect and disconnect cues, enable and disable, errors, loading and the startup logo.
*   **Edit Game Configuration** opens `user.settings` and `input.settings`; the **Save Manager** handles the saves in `Documents\The Witcher 3\gamesaves`; the **log tab** shows what mods write beside the game exe.
*   **F5 starts the game itself, not its launcher.** The Witcher 3 normally opens through `REDprelauncher.exe` — a graphical window a screen reader cannot use, and the thing that decides between the DirectX 11 and DirectX 12 builds. The manager now starts `bin\x64\witcher3.exe` directly, the **DirectX 11** build the accessibility mod is developed against, from wherever your copy is installed.

### 📁 Mods that load, rather than mods that sit there
*   The Witcher 3 loads a folder in `mods` **only if it is named `mod` something**, and says nothing at all when it isn't — the game starts perfectly and the mod simply never runs. Installing from an archive that unpacks to another name now **renames it so it loads**.
*   **Disabling a mod prefixes its folder with `~`**, which is what takes it out of the running for that same reason. Enabling takes it back off.
*   The game keeps its own list of mods in `mods.settings`, and that list is what its **in-game mod menu** shows. The manager now keeps it in step, so the two never disagree — and deleting a mod removes its entry rather than leaving a ghost in the menu.
*   Mods that also install a **`dlc` folder or menu XMLs under `bin`** have those parts put where they belong **and remembered**, so uninstalling takes them with it instead of leaving them behind.

### 💿 Mods that install themselves
*   Some mods — the accessibility mod among them — ship as their author's **installer program**, because what they install goes to four places at once that no file copy would get right. The manager now **downloads and unpacks the archive, finds the installer, asks before running anything, and waits** while you go through it. When the installer closes, the manager picks up again and adds the mod to your list exactly as it would any other install.
*   **And it now knows what the installer did.** The manager takes a record of the game folder before the installer runs and again afterwards, and keeps the difference as the mod's footprint — the plugin beside the game exe, its sounds folder, the config XML it replaced. Without that, a mod installed this way could never be cleanly removed or switched off, because the manager never saw those files arrive.
*   **Deleting such a mod runs its own uninstaller**, when it registered one, rather than guessing: the uninstaller holds the installer's own list of every file it wrote. You are asked first, and declining still removes the mod folder as before.
*   **Disabling one now actually disables it.** Renaming the mod folder does nothing to a plugin sitting beside the game's executable — the game loads it regardless, exactly as BepInEx once kept loading renamed Moonlight Peaks mods. Those files are now moved out of the game into a holding folder and moved back, unchanged, when you enable the mod again.

### ⌨️ Controls read from your own game
*   The Witcher 3 writes every key binding into `input.settings` and rewrites it whenever you remap one, so the controls list (**Ctrl+H**) shows **your real, current keys** — no plugin needed, and no "these are the defaults" caveat.
*   A key that does several things in different places lists all of them, because all of them are true. Movement reads as **Move Forward / Backward / Left / Right** rather than as the raw controller axis the file actually stores. Controller bindings are left out of a keyboard list.

---

## ✨ New: Moonlight Peaks support

### 🌙 A fourth game
*   **Moonlight Peaks** now appears in the game list, the **Games** menu, the Settings paths picker, and the "where to buy it" store links. The manager finds it automatically wherever Steam has put it, on any drive.
*   It gets everything the other games get: the installed mod list, install and uninstall, enable and disable, backups, profiles, collections, mod notes, the Nexus **Find New Mods** search, update checks, endorsements, and the API-request counter — all working against Moonlight Peaks' own Nexus Mods listing.
*   A **Moonlight Peaks Wiki** tab (searchable, with categories for Characters, Crops, Farming, Cooking, Crafting, Fishing and Locations), a **Moonlight Peaks Walkthroughs** tab, and the community Fandom wiki and guides site in the **Mod Wikis** dropdown.

### 🔌 BepInEx, handled for you
*   Moonlight Peaks loads mods through **BepInEx**, and the manager now manages it the way it already manages SMAPI and SKSE/F4SE. Load a session without BepInEx installed and it **says so out loud and offers to install it** — which matters here, because without BepInEx the game starts normally with none of your mods running and nothing in the game explains why.
*   BepInEx is pinned to **5.4.23.5**, the version Moonlight Peaks mods are built against. If you already run a different version, the manager mentions it once and leaves your install alone.
*   Pressing **F5** with BepInEx missing warns you *before* the game starts.

### 🔀 Disabling a mod actually disables it
*   BepInEx takes no notice of folder names, so the leading-dot trick that disables a Stardew or Skyrim mod would have left a Moonlight Peaks mod running. Disabling one now **moves it to a `BepInEx\plugins-disabled` folder** instead, and enabling moves it back. The mod is never altered or re-downloaded.
*   A mod previously installed as a bare `.dll` dropped into `plugins` is tidied into a folder of its own, so it can be listed, toggled, backed up and removed like any other. Shared library files that aren't mods are left alone.

### 🏷️ Real mod names and versions
*   Moonlight Peaks mods carry no manifest file, and their program files frequently report a version of `0.0.0.0`. The manager now reads each mod's **declared name, version and ID from the mod itself**, falling back to what BepInEx recorded in its log. The installed list shows the name the author gave the mod rather than the folder or download name.

### ⚙️ Mod settings and documentation
*   **Mods → Edit Mod Settings (Config Files)** lists every installed mod that has settings, by name, and opens it in the same accessible editor used for the Skyrim and Fallout INI files.
*   **F3 (Mod Documentation)** builds a settings reference for every installed mod from its own configuration file — each setting with the author's description, the current value, the default and the accepted values. Because it reads what's installed, it always matches the version you actually have.

### 📋 BepInEx Log tab
*   A **BepInEx Log** tab, working like the Skyrim and Fallout log tabs: the filter for errors and warnings, the search box, **Ctrl + Shift + R** to re-read it live, **F4** to open it, **Ctrl + C** to copy lines. BepInEx records every mod it loaded with its version, so it's the quickest way to confirm a mod is running.

### ⌨️ The controls list knows Moonlight Peaks
*   **The game's own controls (Ctrl+H)** are now listed for Moonlight Peaks — all 33 keys, each with every action it performs, because one key routinely does several things on different screens. They're there **the moment you install the manager**: a snapshot of the game's stock bindings ships inside it, so the list works before you have installed a single mod or launched the game once.
*   Install the **Moonlight Keybind Export** mod and start the game, and the list switches to **your own bindings**, including anything you have remapped. The list says which of the two you're looking at, and when your bindings were read, so defaults are never mistaken for yours.
*   Moonlight Peaks resolves its controls while it runs and stores nothing readable on disk, which is why the game has to be asked. Nothing is hardcoded and nothing is guessed.
*   **The Keybind Reader comes with the manager** — there is nothing to go and download. The first time you load a Moonlight Peaks session, the manager explains why the game needs one and **asks** whether to install it; nothing is put into your game folder unless you say yes. Your answer is remembered either way and you are never asked again, and deleting the reader later is treated as your decision, not something to undo. The manual explains the whole arrangement under **Accessibility Controls Viewer → The Keybind Reader**.
*   **Moonlight Peaks mods now appear in the list at all.** Their keys live in each mod's BepInEx configuration file, outside the mod's own folder, which is the one place the controls list never looked — so every Moonlight Peaks mod came up empty. Each entry is now just the key it's set to and what it does.
*   **Ctrl+E / Edit Configuration** works from the controls list for Moonlight Peaks mods, opening the accessible INI editor rather than refusing the file.

### 🩹 Fixed in the controls list
*   **Moonlight Peaks was showing Stardew Valley's controls** as its own. The base-game list ended in a fallback to Stardew for any game without one of its own, so a fourth game silently inherited a third game's keys. A game with no controls list of its own now shows none, rather than another game's.
*   **Stardew Access was missing entirely** from Stardew Valley's controls list. It installs one folder deeper than the list looked, and its keybinding page sits deeper still — so the mod that most needs to be in there was the one mod that wasn't. Both are now found: 92 documented keybinds with the author's own descriptions, alongside its current settings. Any other mod installed in a nested folder appears now too.
*   **F3 (Mod Documentation)** no longer reads out the entire list of accepted values for a key setting — several hundred key names in one unbroken line. The setting's type, default and current value all stay.

## ✨ Also new

### 🔎 Browse Nexus by category

*   **Find New Mods gains a "Nexus category" dropdown**, listing Nexus's own classification — Armour, Gameplay, Audio, Patches and the rest. The list is built from the categories that **actually have mods for the game you are managing**, most-populated first and with a count beside each, so you get "Armour (1247)" rather than a walk through every category Nexus has ever defined, most of which any one game has never used. It reloads when you switch games, because Skyrim's categories are nothing like Stardew Valley's.
*   **The dropdown belongs to that one search type.** It appears when you choose **Nexus Categories** and goes away — and stops applying — for any other Type. A filter narrowing your results with no control anywhere on screen to say so is exactly how the language filter used to read as "these mods aren't on Nexus", so the control and the filter come and go together. The manager says so when the dropdown appears, since a control arriving mid-toolbar is easy to miss.
*   Choosing that Type without picking a category says so and moves you to the category list, rather than quietly listing something else.
*   Unlike the language filter, the category is **not remembered between sessions**: it is a choice about the search in front of you rather than a standing preference. And it does not have the language filter's hiding problem — every mod on Nexus has a category, because uploading one requires choosing it.
*   The manual's **Searching for Mods** section now also explains what each of the other Type options actually does, which had only ever been spelled out for **All**.

### ✅ Search results say when you already have the mod

*   **A result you have already installed now reads "Installed"**, right after its name and before its download and endorsement counts — *"Serena's Grimoire (ID: 23). Installed. 3,428 downloads…"*. It leads because it is the one fact that can end your interest in a row outright: a mod already in your mods folder needs no further thought, and no reason to open its page or start a download you would only have to undo.
*   Only mods you **do** have are marked. Saying "not installed" on every one of a hundred results would bury the handful that matter.
*   **The marks keep themselves current.** Results are checked when they are fetched and re-checked on every rescan of your mods, so a mod you download and install while your results are still on screen starts saying "Installed" without you searching again. Your place in the list is kept — losing it would mean finding it again by ear — and if it is the row you are actually on that changed, the manager says so rather than letting it change silently under you.
*   A mod is recognised by its **Nexus ID** where the manager knows it, and otherwise by **name and author** using the same strict rules Auto Match uses — a partial name match only counts when the author agrees. That second route is the one that earns its keep: plenty of installed mods have never been linked to a Nexus page, and those are exactly the ones you would otherwise re-download by accident. Nothing looser is used, because a wrong "Installed" would talk you out of a mod you do not actually have.

### 🌱 Stardew mod settings you can actually choose from
*   **Edit Mod Config** on a **Content Patcher** mod now opens a **settings list** instead of raw JSON. Each setting is read as "name: current value", and **Enter** offers **the values the mod author allows** — pick one from a list rather than typing a string and hoping. Their explanation of the setting is read out as the chooser opens, where they wrote one.
*   This closes a real gap rather than tidying one. The file these mods leave you to edit holds only the answers: a setting reads `"ObeliskOptions": "vanilla"`, and that `glass`, `garden`, `Yri` and `Juffuffles` are the alternatives is written somewhere else entirely. Changing a setting meant reading the author's own files or using an in-game menu.
*   **Delete** puts a setting back to **the author's default** — the one edit that was genuinely hard to make by hand, since the default isn't in the file being edited. The setting is removed rather than overwritten, so it keeps following the author if they change that default in a later version.
*   Works on a mod that has **never been run**: with no settings file yet, every setting shows the author's default and the file is created as soon as one is changed. Mods that aren't Content Patcher packs open in the JSON editor exactly as before.
*   Free-text settings are typed **inside the window** rather than in a pop-up box of their own — the same in-window treatment the rest of the manager now gets.

### 🗄️ "Delete Old Backups" no longer reports 0 every time
*   The command could only ever say *"Deleted 0 old backups"* — not because pruning was broken, but because there was never anything to prune. Old backups are already trimmed to your per-mod limit **automatically, every time a backup is made**, so by the time you ask, everything is already within the limit.
*   It now says so plainly, and offers the clean-up you actually came for: keeping only the **newest** backup of each mod, telling you how many that would remove before it does anything. Choosing No changes nothing.

### 💬 Prompts happen inside the window, not in a window of their own
*   **Every** confirmation and message in the manager — around 117 of them — is now shown as a panel laid over the window that raised it, instead of a separate message box. Nothing new opens, so nothing announces a new window.
*   This removes the noise that came with it. A message box made the screen reader read out a window caption and the name of the focused button *before* the question was ever heard; closing it made the reader re-read the window underneath, talking over whatever the action had just reported — which is how results like *"Deleted X. 15 searches left."* went missing halfway through. Neither can be suppressed from outside a message box. Neither happens now.
*   A prompt reads as **the question, then the choice your fingers are on** — *"Delete "auto" from the search history? Yes, Alt Y."* The keys are unchanged: access keys (Alt+Y, Alt+N) work, **Enter** takes the focused choice, **Escape** cancels, and **Tab** moves between the choices. The rest of the window is disabled while a prompt is up, so nothing behind it can be reached by mistake.
*   Prompts follow your **High Contrast** and **text size** settings, which message boxes never did.
*   Confirmations raised from the **Search History** window also used that window's full title — *"Search History - Press Escape to Close"* — as their caption, so that whole phrase was read ahead of every question. Prompts now carry a short caption naming the action ("Delete Search", "Clear Search History").
*   If a prompt ever has no window to appear in (before the main window exists, or from a background thread), it falls back to an ordinary message box — a prompt that can't be shown must never be a prompt that's silently skipped.

### 🗑️ Remove a single search from the history
*   Press **Delete** on a term in the **Search History** window to remove just that one, after a *"Delete &lt;term&gt; from the search history?"* confirmation. Until now the only way to be rid of a search you mistyped — one that found nothing and stayed in the list forever — was to clear the entire history.
*   Every recording of that term is removed, so it doesn't reappear the moment the list redraws; the manager then says how many searches are left and puts you on the entry that took its place, so several can be tidied up in a row.

### 📈 Downloads and endorsements in the search results
*   Every result on the **Find New Mods** tab now says **how many downloads and endorsements** the mod has — *"Serena's Grimoire (ID: 23). 3,428 downloads, 50 endorsements. Dark magic, dramatic rituals…"* — so you can judge whether a mod is widely used and well liked without leaving the manager to open its page.
*   They're read **before** the description deliberately: they're the fastest way to rule a mod in or out, so you can move to the next result without waiting through a description you've already decided against.
*   A mod with genuinely no downloads yet reads as "0 downloads" rather than quietly omitting the figure, so an unknown value and a real zero can't be confused.

### 🔎 An "All" listing in the search types
*   The **Type** dropdown on the Find New Mods tab gains **All**, which lists **every mod the game has** in **alphabetical order**. Unlike Trending or Most Popular nothing is left out, and because the order is alphabetical you can tell where you got to — so it works for hunting down a specific mod, or for going through the catalogue to find the mods you already have. It honours the Language dropdown like the other browse modes.

### 🗣️ The language filter now admits what it's hiding
*   Nexus only knows a mod's language if its author filled that field in, and **most don't** — so a language filter, *including the default English*, silently excludes them. On Moonlight Peaks that means searching with English finds 5 of the game's 80 mods; on Stardew Valley it hides about half of the 32,000.
*   Whenever a language filter is in force and mods exist outside it, the manager now says so: *"That is 5 of this game's 80 mods. The English language filter hides the rest…"* A thin result no longer looks like the mods simply aren't on Nexus.

### 🔗 Auto-match finds far more mods
*   **Auto-match Nexus IDs** used to search only for the name a mod calls itself, and its one fallback — a partial name match backed by the same author — could never apply to a Moonlight Peaks mod, because a BepInEx plugin doesn't record an author the manager can read. On a real 11-mod install it linked 2.
*   It now tries **every name a mod goes by**: the declared name, **the folder it was installed into**, and **the parts of its plugin ID** (which conventionally hold both the author's handle and the mod's real name). Run-together names are split into words too, since Nexus searches by word — a page called "Mod Menu" is found by "Mod Menu", never by "modmenu". The same 11-mod install now links **7** — every one that has a Nexus page at all.
*   The bar for accepting a match is unchanged and still deliberately high: an exact match on one of those names, or a partial match with the author agreeing. Mods it isn't sure of are still left unlinked rather than pointed at the wrong page.

## 🛠️ Fixes

### ⏳ Deleting a mod says what it's doing
*   Deleting a mod zips it into your backups folder first, and for a large mod that took long enough to look like the manager had frozen — it said nothing and the window stopped responding.
*   It now announces *"Deleting <mod>. Backing it up first…"*, reports progress through **your chosen download/install feedback** (tones, spoken percentages, both, or off), and does the work off the interface thread so the window stays responsive. Progress follows **bytes rather than file count**, so a mod that is one big archive plus a few small files doesn't leap to 90% and then stall.

### 🏷️ Mods you installed yourself now get their real details, and can actually be checked for updates
*   ⚠️ **Linking a mod by hand stopped it ever reporting an update.** Pressing **Ctrl + K** and entering a Nexus ID wrote *the mod page's current version* in as your installed version — so the manager immediately believed you were up to date and never offered that mod an update again. The GitHub path did the same with the latest release tag. The installed version is now left exactly as it is: it's the version you actually have, and it's what an update has to be compared against.
*   **Auto-match now fills in the details, not just the link.** A mod the manager didn't install has no author and no description recorded anywhere on disk — nothing local ever knew them. Once its page is identified, its **author** and **summary** are fetched and saved to the mod's manifest, so they survive a rescan and show in the mod list. Mods linked on an earlier run are filled in too, so you don't have to unlink anything; a second run costs nothing because only mods that are actually missing something are fetched.
*   A mod whose name the manager could only take from its folder (an archive unpacked as `SkyUI-12604-5-2SE`) is renamed to its real page name. A mod that declares its own name — a Stardew manifest, a BepInEx plugin — keeps it, since that's the name it goes by in game.
*   **Installing a mod outside Stardew Valley now records which release went on disk.** That was already done for Stardew; for Skyrim, Fallout 4 and Moonlight Peaks it wasn't, so their update checks fell back to comparing the version a mod declares against the version on its page — which, for the many mods whose authors never bump the number inside the mod, offers the same update forever.

### 🔁 The search bar says what it is doing, once

*   Pressing Enter on the **"Load more results"** row read *"Loading more Search…"* and then read it again. The search bar announced it, and then setting the title-bar status announced the very same sentence a second time, because updating the status speaks by default.
*   Only the announcement is spoken now; the title bar still shows the status without reading it out on top. A fresh search had the milder version of the same fault — *"Starting mod Search…"* followed by *"Running Search…"*, two sentences for one event — and now says it once.
*   **And each search type now says what it is actually fetching.** One template with the dropdown's label dropped into it produced *"Starting mod Recent…"* and *"Starting mod Nexus Categories…"* — the wiring showing through, since those are labels chosen to read well in a dropdown rather than things you can start. Each mode now has its own sentence: *"Searching for dragon."*, *"Listing every mod, A to Z."*, *"Listing the most downloaded mods."*, *"Listing the most recently updated mods."*
*   The two modes with something particular to name now name it — a search says the **term** you typed, and a category browse says the **category**: *"Listing Armour mods, most downloaded first."* That is the difference between knowing the dropdown took your choice and having to go back and check it.

### 🔁 The focused mod is no longer announced twice after an action
*   Enabling, disabling or deleting a mod read the focused entry — name, author, version, category, status — **twice**. Two separate causes, both fixed:
    *   Refreshing the mod list refills the **Category** filter dropdown, and changing that dropdown's contents raises its own "selection changed" event, which rebuilt the list; the refresh then rebuilt it again, and clearing the dropdown could trigger a third. The filter dropdowns are now left quiet while they are refilled from code, which also removes two wasted rebuilds of the whole list per action.
    *   After rebuilding, the manager spoke the selected row's **full text plus its position**. But rebuilding re-selects the row, which the screen reader reads by itself — so the row was read twice. It now adds only "X of Y", exactly as it does when you arrow onto a row.
*   This covers every action that refreshes a list: toggling, deleting, installing, updating and applying a profile. The Available Updates and Backups lists had the same duplicate and are fixed too.

### 🧹 Status messages no longer stick in the title bar
*   **Enabling or disabling a mod**, **batch enable/disable by category**, **deleting a mod**, **applying a profile** and **installing BepInEx** all left their message sitting in the title bar as though it were the program's current state, until something else happened to replace it. Each now speaks its outcome and then returns the title to your Nexus connection status, matching every other action in the manager. Failures reset it too.

### 🚀 "Game closed" no longer announced while the game is still starting
*   Launching a game said *"Launching…"* **twice**. The status update already speaks, and the launch code spoke the same sentence again on top of it. Fixed for all games.
*   A Steam game started from its own program file notices it wasn't launched by Steam and **restarts itself**: the game loads, your mods load and announce themselves, then it shuts down and starts again about fifteen seconds later. The manager was watching the program it started rather than the game, so it announced *"Game closed"* the moment that first copy exited — mid-restart, while the game was still coming up — and then spoke the Nexus connection over the top of it. It now follows the game itself, allows for a restart during the first minutes of a session, and reports a real close within a few seconds. This also fixes Skyrim and Fallout 4, where SKSE's and F4SE's loaders exit immediately after starting the game and produced the same false "closed".
*   **Moonlight Peaks now starts through Steam**, which avoids that restart entirely — so the game loads once and your mods announce themselves once. Mods are unaffected: BepInEx loads through a file beside the game program regardless of who starts it. Games with their own loader (SMAPI, SKSE, F4SE) are never routed through Steam, since bypassing the loader would mean no mods; nor are non-Steam copies.
*   After a game closes, the manager no longer *speaks* the Nexus connection status a few seconds later — that read as though something else had happened. The title bar still returns to it.

### 🗣️ "No update key" no longer said to games that have no such thing
*   The Update Coverage Report told users of every game that an unlinked mod's *"manifest lists no update key"*. Update keys are a **Stardew Valley** concept that SMAPI reads from a mod's `manifest.json`; a Moonlight Peaks, Skyrim or Fallout 4 mod has neither the file nor the field, so the report described something that doesn't exist and gave nothing to act on. Those games now hear that the mod has no Nexus mod ID yet, and are pointed at Auto-match or at linking it themselves.

## 🛠️ Internal

*   The per-game constants — Steam app id, executable, mods folder, Nexus domain — used to be spread across a dozen separate lookups that each quietly defaulted to Stardew Valley for anything unrecognised. They now live in one place, so an unsupported game is an obvious failure rather than a wrong answer.

# Version 1.4.5

A round of fixes for things that bit real installs: deleting mods that ship read-only files, installing mods that arrive as 7z or RAR, and the Settings window announcing where you've landed.

---

## 🛠️ Fixes in Version 1.4.5

### 🔊 Settings announces the active tab again
*   Opening **Settings** (**Ctrl + P**) now speaks the tab you land on — "Paths & Account Tab" — instead of going quiet. Focus was landing on the tab strip correctly, but the tab name wasn't being spoken; it now is.

### 🗑️ Deleting mods with read-only files (e.g. SkyPatcher)
*   **Delete Mod** no longer fails with *"Access to the path '…' is denied."* Some mods (SkyPatcher among them) ship files marked read-only, which blocked the delete; the manager now clears that attribute first, the same way installs already did. The fix also covers removing read-only files from your game folder when a mod is disabled or deleted.

### 📦 Installing 7z and RAR mods that looked like ".zip"
*   Fixed *"Install failed: End of Central Directory record could not be found."* This happened when a mod downloaded as a **7z or RAR** archive but ended up with a `.zip` name, so the manager tried to open it as a zip. It now detects the real archive type from the file's contents (not its name) and uses the right extractor, so these mods install correctly.

# Version 1.4.4

Prefer the manager quiet? You can now switch its sound effects off completely from the Audio tab, while keeping spoken progress feedback if you want it.

---

## ✨ New in Version 1.4.4

### 🔇 Turn UI sounds off
*   A new **"Enable UI Sounds"** checkbox sits at the top of the **Audio** tab in Settings (**Ctrl + P**). Uncheck it to silence all of the manager's sound effects — the connect, enable, disable, and error cues, plus the startup logo sound.
*   When it's off, the rest of the audio options (volume, sound theme, and the logo selector) **hide themselves**, since they no longer apply — leaving just the **download and install feedback** selector, which has its own setting and keeps working even with UI sounds off. So you can run completely silent, or keep only the spoken/tone progress feedback, whichever you prefer.
*   Toggling it speaks "UI sounds enabled / disabled" so you get confirmation by ear, and your choice is remembered when you save.

# Version 1.4.3

Read each accessibility mod's own documentation without leaving the manager, hear every prompt spoken aloud, and find your way around a tidier, tabbed Settings window. This release is a big pass on the things you hear and how you move through the program.

---

## ✨ New in Version 1.4.3

### 📖 Mod Documentation viewer (F3)
*   **Press F3** (or **Help → Mod Documentation**) to open the documentation for your current game's accessibility mod: **Stardew Access**, **Skyrim Access**, or **Fallout 4 Access**. No more hunting for a README on your computer or searching GitHub.
*   **Familiar, accessible layout.** It uses the exact same viewer as the User Manual: a table-of-contents list on the left and a read-only text area on the right. Arrow through the sections, press **Right** or **Enter** to open one that has sub-topics, **Left** or **Backspace** to go back, **Tab** to read the text, and **Escape** to close. Position ("3 of 8") and "has sub-topics" are announced just like the manual.
*   **Always works, stays current.** A copy of each guide ships with the manager, so it opens instantly and works offline. In the background it also refreshes from the source — the live GitHub documentation for Stardew Access, or (when you're logged in to Nexus) the mod's current Nexus page for Skyrim Access and Fallout 4 Access — so the next time you open it, it's up to date.
*   **Deep for Stardew Access.** The Stardew guide pulls together the mod's overview, setup, features, full keybindings, commands, and configuration pages into one navigable document.
*   **Remappable.** F3 is the default; change it in **Settings → Shortcut Manager** (action name "ModDocs") if you'd rather use another key.

### 🗂️ Settings is now organized into tabs
*   The Settings window is split into **Paths & Account**, **Startup**, **Audio**, **Mods & Search**, and **Language** tabs. It opens on the tab strip so you can arrow between tabs, then Tab into each tab's controls. Every existing setting is still there, just grouped.

### 🔊 Every prompt is now spoken
*   Yes/No, OK, and other message boxes now **speak their message** through the screen reader, the same way the rest of the app talks — so you always hear the question, not just the focused button (and it works under SAPI too, not only NVDA).

### 👋 Spoken welcome and goodbye
*   On startup the manager greets you with **"Welcome to Kinetix Mod Manager"** plus the F1 / Shift+F1 hints, and waits for that to finish before it starts loading and connecting, so nothing talks over it.
*   On exit it says a short **goodbye** and waits for it to finish before closing.
*   Both can be turned off on the **Settings → Startup** tab.

### ⏸️ Clearer screen-reader reading
*   Comboboxes, checkboxes, and lists now pause between the field name and its value or state — "Sound volume. 100" rather than running them together — throughout the main window and the dialogs.

### 🔎 Search tab and Settings tweaks
*   On the **Search** tab the **search history** button now sits right after the search box and before the search-type selector, and that selector finally has a proper spoken label ("Search type").
*   The volume and "max backups" controls are now **dropdowns** instead of spin boxes, which the screen reader announces cleanly (the old spinners read their name twice).
*   New **"Check for Manager Updates at Startup"** setting, separate from the mod-update check, so you can control each on its own.
*   The "Random Logo at Startup" checkbox lives with the logo selector on the **Audio** tab now, and both appear only when **Show Splash Screen** is on.

### 🗑️ "Delete Old Backups"
*   The Backups tab's "Prune Old Backups" is now **"Delete Old Backups"**, on **Ctrl + Shift + D** (its old key clashed with **Open Backups Folder**). Existing keybindings are migrated automatically.

### 🛠️ Fixes
*   **Updating from a mod's page no longer asks to overwrite.** Downloading a newer version through "Mod Manager Download" now installs it as an update instead of prompting — the "already installed, overwrite?" confirmation is only for re-installing the same or an older copy.
*   **Downloads can't hang forever.** If a download stalls with no data (the old "stuck at 94%"), it now times out with a clear message and cleans up the partial file, so you can just try again without restarting the manager.
*   **"Access denied" installs are handled.** Installing a mod whose files were marked read-only (e.g. SkyPatcher on some setups) no longer fails — read-only files are cleared before overwriting/deleting, with a clearer error and a retry if something is briefly locked.
*   **FOMOD installer button order.** On a FOMOD's final step you now tab **Back → Install → Cancel**, so Install comes before Cancel.
*   **Accessibility Suite & Sound Demo lists** announce the item position correctly and no longer say it twice when you enter them.
*   **About window** now hides the main window while it's open, and reads cleanly (the title and version aren't repeated three times).
*   **Sound Demo** closes with **Escape** from anywhere in the dialog, not only from the sound list.

# Version 1.4.2

Long downloads and installs now tell you how far along they are — by ear. Big mods (200 MB and up) can take a while, so the manager now plays a rising tone and/or speaks the percentage as they download **and** as they install, and you can choose which in Settings.

---

## ✨ New in Version 1.4.2

### 🔊 Audible progress for downloads and installs
*   **Installing now has its own progress.** Previously only downloads reported progress; the install/extract step was silent, which felt frozen on large mods. Installs now report a real percentage too (extracted from the archive as it unpacks).
*   **Rising tones.** During any download or install, a short tone climbs in pitch from 0% to 100%, so you can hear progress moving without it talking over you. The tones are generated on the fly — no extra sound files.
*   **Spoken percentages, kept light.** When speech is on, the manager says the name once — "Downloading *mod name*, 0 percent" — then just the bare deciles after that: "10 percent", "20 percent", and so on, rather than repeating the mod name every time.
*   **Pick what you hear.** A new **"Download and install feedback"** setting lets you choose **Tones**, **Speech**, **Both** (the default), or **Off**. Off is handy if you already rely on your screen reader's own progress-bar beeps (for example NVDA's "Progress bar output").
*   **Everywhere it matters.** The same feedback is used for Mod Manager Downloads, manual installs, single-mod updates, the SMAPI/script-extender setup, and the manager's own self-update.

### 🏷️ Skyrim & Fallout 4 show their version
*   The title bar now includes the **detected game version** of the loaded Bethesda game — for example **"Skyrim Special Edition (1.6.1170)"** or **"Fallout 4 (1.11.191)"** — read from the game's executable. The version is what decides which mod files are compatible (e.g. Skyrim 1.6+ uses the files Nexus labels "AE"). The name matches what Steam shows; if the version can't be read, just the plain name is shown. *(It deliberately doesn't print "Anniversary Edition" / "Next-Gen": those are paid DLC bundles whose ownership can't be detected — Bethesda's free updates make every copy report the same version regardless of which you bought.)*

### 🔎 Mod health: conflicts and requirements
*   **File Conflict Report** (Mods menu, or **Ctrl + Shift + F**). On Skyrim and Fallout 4 it lists every loose file that more than one enabled mod provides, telling you which mod **wins** and which are **overridden** — arrow through them to judge your load order by ear. On Stardew Valley (where each mod loads from its own folder and nothing overwrites) it instead lists any mods that **share a UniqueID**, which breaks SMAPI.
*   **Check Mod Requirements** (Mods menu, or **Ctrl + Shift + Q**). Scans your enabled mods for problems and lists them:
    *   **Stardew:** required dependencies (now including a content pack's host mod) that are **missing, disabled, or too old**. Press **Enter** on one to search for it.
    *   **Skyrim / Fallout 4:** plugins whose **master file isn't installed** (the classic missing-master that stops a plugin loading), a **missing script extender**, and each mod's **Nexus "Requirements"** that you don't have installed. Press **Enter** to open the missing mod's page.

### ♻️ Reinstall confirmation
*   Installing a mod you **already have** — whether by **Ctrl + I** or a **Mod Manager Download** — now asks first: *"{mod} (version X) is already installed. Overwrite it with this copy?"* Choose No and your existing copy is left untouched. Mod *updates* still overwrite without prompting, as before. (Deleting a mod already asks for confirmation and makes a safety backup first.)

### 🙈 Hide requirement warnings that don't apply
*   In the **Check Mod Requirements** report, press **Delete** on any warning to hide it for good — for the false positives the manager can't auto-detect (a Nexus requirement that's actually optional, or one satisfied by an alternative like SKSE standing in for a DLL loader). Hidden warnings are remembered per game, and **Mods → Reset Hidden Requirement Warnings** brings them all back.

### 🛠️ Fixes
*   **Manually installing SKSE/F4SE now lands in the game root.** Installing the script extender as a regular mod (Ctrl+I, a Mod Manager Download, or the manual route many non-premium users take for F4SE) used to drop the loader and DLLs entirely — the installer mistook the extender's `Data` sub-folder for the whole mod and only copied the scripts. It now recognizes the script extender and places its loader/DLLs in the game folder and its scripts in `Data`, exactly like the Accessibility Suite already did.
*   **Accessibility Suite installs keep your focus.** While the Accessibility Suite installer is open, the main window is now hidden, so each "installed" confirmation returns you to the suite panel instead of bouncing focus to the main window behind it. The main window comes back when you close the suite. (Ordinary installs from Find New Mods or Ctrl+I are unchanged.)
*   **First-launch setup actually opens now.** On a brand-new install the Settings window opens on its own so you can enter your Nexus key and game/mod folders straight away — previously it only appeared after you'd already picked a game, leaving nowhere obvious to start. It happens only once, and existing users aren't nagged after updating.
*   **Requirements check is less noisy.** It no longer flags **VR-only** requirements (like "VR Address Library for SKSEVR") for the flat games, it now shows the **mod author's note** about a requirement when there is one, and it spells out that a listed Nexus requirement may be optional or have an alternative — so it reads as "worth checking" rather than a hard error.
*   **Active Creations are protected from cleanup.** Creation Club / Anniversary content (including `_ResourcePack.esl`) is never treated as a removable "ghost" plugin, so it isn't dropped during the plugin sync.
*   **Requirements check now understands Creations.** The master-file check used to look only inside mod folders, so it wrongly reported Creation Club masters (and other plugins that live in the game's Data folder) as "not installed" even when they were active. It now checks against the real load order and the Data folder, and distinguishes *"not installed"* from *"installed but not enabled"* (e.g. a Creation toggled off that another mod needs).
*   **`_ResourcePack.esl` now appears on the Creations tab.** The Anniversary-edition resource pack — which USSEP, Alternate Start, and many AE mods require as a master — couldn't be toggled before because it doesn't start with "cc". It's now listed alongside the Creations so you can enable it.
*   **The Creations tab no longer goes missing.** When the app started straight into a Skyrim/Fallout 4 session, the Creations tab could be absent (it was only added when you switched games, not at startup). It's now built in from the start, so it's always there for those games.
*   **Uninstalled mods no longer leave "ghost" plugins.** When a Skyrim/Fallout 4 mod was removed, its plugin could linger in the Plugin Order list (and `plugins.txt`) because the manager re-adopted the leftover entry even though the file was gone. It now only keeps plugins whose file is actually present in the game's Data folder, so a removed mod's plugin clears on the next refresh.
*   **"Loading more results" no longer sticks.** After loading another page of search results, the title returns to your Nexus connection status instead of staying on "Loading more…" indefinitely.
*   **Empty lists announce reliably.** "List is empty" is now spoken in the right order (list name first), when the last available update is cleared, and when you Alt+Tab back to an empty list.

---

# Version 1.4.1

A round of fixes and quality-of-life additions on top of 1.4.0: you can now uninstall the script extender and get warned when it no longer matches your game, mod searches can be remembered and re-run, and several rough edges around updating, status messages, and the install prompt have been smoothed out.

---

## ✨ New in Version 1.4.1

### 🧩 Script extender management (Skyrim & Fallout 4)
*   **Uninstall the script extender**: SKSE and F4SE install into the game folder rather than as normal mods, so there was no way to remove them. A new **Mods → "Uninstall Script Extender (SKSE/F4SE)"** command cleanly removes the files it installed (after a confirmation).
*   **Version-mismatch warning**: SKSE/F4SE only load when built for your exact game version, so after a game update they silently stop working — which is the usual reason the **Mod Configuration Menu** disappears. The manager now checks this before launching and warns you (and tells you to reinstall it to match), instead of leaving you to wonder why your mods went quiet.
*   **Links go to Nexus**: Opening the SKSE/F4SE entry in the Accessibility Suite now goes to its Nexus page.

### 🔍 Search history
*   **Remember and re-run your searches**: Turn on **"Save Mod Search History"** in **Settings** and the manager keeps the mod searches you run. Open them with the new **History** button on the Search for Mods tab or **Ctrl + Shift + H**: choose "All searches" or a specific date, then press **Enter** on any term to run it again. History is kept **per game**, and you can clear it at any time. It's **off by default**.

### 🛠️ Improvements & fixes
*   **SMAPI download shows progress**: Installing SMAPI now reports its download progress like other downloads.
*   **Stale status messages cleared**: The window title now returns to your Nexus connection (or "Ready") after an operation finishes, so it no longer keeps showing "Installing…" or "Rebuilding deployment" long after it's done.
*   **Disabled mods stay disabled after updating**: Updating a mod you'd turned off no longer silently turns it back on.
*   **The install prompt reliably comes to the front**: After a Mod Manager Download, the "Install now?" question now takes focus on its own, instead of staying hidden behind the browser on some computers (you no longer have to Alt+Tab to find it).
*   **"List is empty" is announced**: When you clear the last available update, the Updates list now says it's empty right away instead of waiting for you to move focus away and back.
*   **Suite tidy-up**: Removed the standalone "SkyrimAccessibility" entry from the Skyrim suite — it's now part of Skyrim Access.
*   **Manual updated** to cover the FOMOD installer, the controls viewer, the navigable manual and change log, search history, and the script-extender tools.

---

# Version 1.4.0

A big step for accessible mod installing: configurable "FOMOD" mods now open a fully keyboard-driven, spoken installer wizard instead of failing, and the controls window (Ctrl+H) has been rebuilt into an easy drill-down list that reads each mod's real, documented keybinds — including Fallout 4 and Skyrim mods that set their keys through MCM. The manual and change log are now navigable in the same way, plus a handful of fixes.

---

## ✨ New in Version 1.4.0

### 🧩 Accessible FOMOD installer (Skyrim & Fallout 4)
*   **Configurable mods now install correctly**: Mods that ship a guided "FOMOD" installer — such as Immersive Sounds Compendium — used to install wrong because their option menus couldn't be read. They now open a fully keyboard-driven, screen-reader-friendly wizard: each option's description is spoken as you move through it, the step and your progress ("Step 2 of 6") are announced, checkboxes and radio buttons read their checked state, and your picks install exactly as a sighted user's would.
*   **Smart options are handled properly**: Options the mod marks as required, recommended, or not-usable are read out with their status and can't be toggled into an invalid state, and any conditional files are installed only when their conditions are met.

### ⌨️ Reworked controls viewer (Ctrl+H)
*   **A clear drill-down list**: The accessibility-controls window is now a single navigable list. Use **Up/Down** to move, **Right or Enter** to open a group, and **Left or Backspace** to go back. Press **Ctrl+E** to edit a mod's configuration when one is available, and **Shift+F1** for help.
*   **Controls come from each mod, so they're always current**: Instead of a hardcoded list that could go stale, the manager reads the controls each mod actually documents — its README or guide, or its config — captured when the mod is installed.
*   **MCM keybinds for Fallout 4 and Skyrim**: Mods that set their keys through MCM (such as Fallout 4 Access and Extended Dialogue Interface) now have those keybinds read straight from MCM, showing the real key each action is bound to — including any you've changed in-game.

### 📖 Navigable manual and change log
*   **Open and read by section**: The User Manual (**F1**) and Change Log (**F2**) now use the same drill-down — a list of sections on the left that you open to read the text on the right. In the change log, each version opens to its own list of changes, so you can jump straight to what's new in a release.

### 🛠️ Other improvements & fixes
*   **"100 results" now really loads 100**: Choosing 100 results per load in mod search returned only 80, because Nexus limits each request to 80. The manager now fetches the rest automatically so you get the full amount you asked for.
*   **Installs no longer blocked by a full system drive**: Mods now extract on the same drive as your mods folder, so a full C: drive won't stop an install when your mods live on another drive.
*   **Steadier list navigation**: In the controls, manual, and change-log lists, the Left and Right arrows only move between levels now — they no longer occasionally move the selection the way Up and Down do.

---

# Version 1.3.0

A major update for Skyrim Special Edition and Fallout 4 modders: full load-order management with import and export, a Creations manager, one-click importing from Mod Organizer 2, and an in-app game-log viewer — alongside `.rar` support, a "results per load" control for mod searches, an in-app Change Log you can open with F2, and a range of accessibility refinements. The manual has been fully updated to cover everything.

---

## ✨ New in Version 1.3.0

### 🎮 Load order management (Skyrim & Fallout 4)
*   **Mod Priority tab**: Decide which mod wins when two of them change the same file. Reorder with **Ctrl+Up / Ctrl+Down**, and each mod reads how many files it overrides or is overridden in, so you can judge its standing by ear.
*   **Plugin Order tab**: View and reorder your active plugins (the `plugins.txt` load order). Masters load first automatically, and **F8 auto-sorts** the whole order so every plugin loads after the masters it needs — using LOOT's community rules when they're available.
*   **Export and Import Load Order**: From the **Mods** menu, save your mod priority and plugin order to a file and restore it later — handy as a backup or to move a setup between computers.

### 📥 Import from Mod Organizer 2 (Skyrim & Fallout 4)
*   **Bring your MO2 setup across**: From the **Mods** menu choose "Import from Mod Organizer 2", pick your MO2 folder and profile, and the manager copies your mods, applies their priority and plugin order, and activates the same plugins — all without changing your MO2 setup.
*   **Nexus IDs filled in automatically**: MO2 doesn't store them, so after importing the manager matches your mods to their Nexus pages by name, which makes update checks and "open mod page" work for them.

### 🧩 Creations manager (Skyrim & Fallout 4)
*   **A new Creations tab** lists the Bethesda Creations installed in your game, whether each one is active, and whether it's a master or light master. Press **Space** to activate or deactivate the selected Creation. (Creations are still downloaded inside the game, from its Creations menu — no mod manager downloads them.)

### 🪵 In-app game logs (Skyrim & Fallout 4)
*   **A new Log tab** — "Skyrim Logs" or "Fallout 4 Logs" — lets you read the logs your script extender and its plugins write, including `f4se.log` / `skse64.log`, the per-mod logs, crash logs, and your accessibility mod's log. Choose a log from the dropdown, filter to "Errors and Warnings", search it, and press **Ctrl+Shift+R** to refresh it live — even while the game is running. This is where you'll spot a plugin that stopped loading after a game update.

### 🌐 Translatable interface
*   **The whole app is now translatable**: Every piece of text the program shows or speaks lives in language files, and a **Language** option has been added to Settings, so the manager is ready for translators to add new languages without any code changes. *Only English is included for now* — other languages will be added over time as translations are completed, so you won't see new language choices until those translations exist.

### 🔍 Finding mods
*   **Choose how many results load at a time**: A new **Results** dropdown on the renamed **Search for Mods** tab (10, 20, 30, 50, or 100). Set a default that sticks across sessions in **Settings**.
*   **"Load more results" is now part of the list**: Instead of tabbing to a separate button, a row at the bottom of the results loads the next batch when you press **Enter**, then drops you on the first newly loaded result. It isn't counted in the "X of Y" position announcements.

### 🛠️ Other improvements & fixes
*   **Install `.rar` and `.7z` mods**: The installer now extracts `.rar` and `.7z` archives, not just `.zip`.
*   **In-app Change Log**: Press **F2** (or Help → View Change Log) to read what's new, in the same navigable window as the manual.
*   **The log menu now matches the game**: The View menu's log option opens the right log for the active game — the SMAPI log for Stardew Valley, or the script-extender log for Skyrim and Fallout 4 — instead of always offering the SMAPI log.
*   **Sound Demo**: The first sound is now selected when the demo opens, and each sound's name is no longer spoken twice.
*   **Settings focus**: The Settings window now lands on the first field instead of the Save button.
*   **Clearer dropdown announcements**: Tabbing onto a log or filter dropdown now tells you how many lines it shows, and the name is spoken before the count — on the SMAPI log filter as well.
*   **Fallout 4 suite & F4SE installer**: The Address Library is now part of the Fallout 4 accessibility suite, and the built-in F4SE installer fetches the current build from Nexus (Silverlock no longer hosts the up-to-date version).
*   **Fully updated manual** covering all of the above.

---

# Version 1.2.5

A big update: a new Mod Wikis browser with per-wiki search, a language filter for finding mods, an easier way to install the accessibility suite one mod at a time, an About dialog, support for installing on Windows 11 on ARM, and a fully refreshed manual — alongside fixes for the SMAPI log viewer and mod-group announcements.

---

## ✨ New in Version 1.2.5

### 🪵 SMAPI log viewer
*   **The log now loads while the game is running**: Previously, opening the SMAPI Log tab while Stardew Valley was running showed no entries in any filter — even "Full Log" — because the game keeps the log file open and the manager couldn't read it. The manager now reads the live log without disturbing the game, so you can review errors mid-session.
*   **Refresh the log on demand**: Press Ctrl+Shift+R on the SMAPI Log tab to re-read the log at any time, including while the game is running. You'll hear how many entries were found.

### 🔊 Cleaner mod-group announcements
*   **No more stray "first mod" when collapsing a group**: Collapsing a group while focused on one of its mods no longer briefly announces the first mod in the list before landing on the group.
*   **Expanding and collapsing no longer repeats itself**: Each expand or collapse now reads the group line once — stating "Expanded" or "Collapsed" and the position — instead of speaking the same information several times over.

### 💻 ARM Windows support
*   **The installer now runs on Windows 11 on ARM**: Previously the installer refused to run on ARM PCs (such as Snapdragon-based Surface and Copilot+ devices), reporting an incompatible architecture before installing anything. It now installs on any PC that can run 64-bit apps — including ARM machines, where the manager runs through Windows 11's built-in x64 emulation.

### 📚 Mod Wikis browser
*   **New "Mod Wikis" dropdown on the Wiki tab**: Choose which wiki you want to use. As well as the main game wiki, it lists dedicated wikis for popular content mods — world-expansion wikis for Stardew Valley (Stardew Valley Expanded, Ridgeside Village, East Scarp, Sunberry Village, and more) and large quest / new-land mod wikis for Skyrim and Fallout 4 (the Elder Scrolls Mods Wiki, Legacy of the Dragonborn, Enderal, Sim Settlements 2, and others).
*   **Search and categories follow the wiki you pick**: Selecting a wiki points the Search box, the Categories list, and the page view at that wiki. Categories are now pulled live from the chosen wiki, so they always match what you're browsing (and on multi-game wikis they're scoped to the active game).
*   **Browse-only wikis are handled gracefully**: A few mod wikis can't be searched from inside the app; selecting one opens it in the view and the manager tells you it's browse-only.

### 🌐 Language filter for finding mods
*   **Filter mod searches by language**: The Find New Mods tab has a new **Language** dropdown. It defaults to English and remembers your choice. The list shows the languages that actually have mods for your current game, with a count for each, and a "Any language" option turns the filter off. It applies to keyword searches and the Trending, Most Popular, and Recent lists.

### ♿ Easier accessibility-suite installs
*   **Install suite mods one at a time, at your own pace**: In the Accessibility Suite Installer you can now select any mod in the list and press **Enter** to open just that mod's download page (the Files tab on Nexus, or the official site / GitHub releases for others). This is a calmer alternative to the "Install Missing Suite Mods" button when several mods are needed and you'd rather handle them one by one.

### ℹ️ About dialog
*   **New "About Kinetix Mod Manager" in the Help menu**: A standard About dialog showing the program description, version, publisher, website, and licensing — read aloud and fully keyboard accessible.

### 🎮 Polish
*   **Games are now listed alphabetically** in the Games menu and on the game-selection screen.

### 📖 A fully refreshed manual
*   **Clearer Nexus API key instructions**: Rewritten as step-by-step stages that explain what an API key is, give direct web links so you don't have to hunt through menus or on-screen pictures, point out the "Copy API Key" button, and include a troubleshooting section.
*   **Now covers all three games**: The manual has been generalized beyond Stardew Valley to describe Stardew Valley, Skyrim Special Edition, and Fallout 4 throughout, with game-specific features clearly called out.
*   **Both ways to download from Nexus are explained**: A new section spells out that you can use either the **Mod Manager Download** button (the manager installs it for you) or **Manual Download** + Ctrl+I, and walks through the "Slow Download" button and the required-mods page step by step.
*   **No more empty help topics**: The "Keyboard Shortcuts" and "Advanced Features" topics now open with an overview of what's in them instead of appearing blank.

### 🐛 Fixes
*   **Focus returns to the manager after a "Mod Manager Download"**: When you download a mod with the "Mod Manager Download" button on Nexus, the "Install now?" prompt now reliably comes to the front and takes keyboard and screen-reader focus, instead of sometimes opening behind your browser.

---

# Version 1.2.4

Overhauls the Stardew Valley SMAPI log viewer so errors are easier to find, understand, and act on — plus a small accessibility-suite cleanup.

---

## ✨ New in Version 1.2.4

### 🪵 A more useful SMAPI log viewer
*   **Errors and Warnings filters actually work now**: The "Errors Only" and "Errors and Warnings" filters previously came back empty because of how SMAPI labels its log lines. They now correctly show error and warning entries.
*   **New "Links Only" filter**: Show just the log lines that contain a link — handy for spotting update notices and mod pages at a glance.
*   **Open links from the log**: Press Enter on a log line that has a link to open it in your browser (Nexus pages open on the Files tab). If a line lists several links — for example a "no longer compatible" notice that points to Nexus, GitHub, and SMAPI.io — a picker appears so you can choose which to open. The line announcement tells you whether Enter opens a page or offers a choice.
*   **Diagnose an error**: With a log line selected, use the Quick-Fix shortcut to get a plain-language explanation of what the line means and how to fix it, with specific guidance for common problems (incompatible mods, missing dependency versions, failed Harmony patches, command-registration errors, and missing object IDs).
*   **Select and copy lines**: Select one or more log lines and press Ctrl+C to copy them to the clipboard, so you can paste them into a forum post or Discord without opening SMAPI-latest.txt by hand.

### 🧹 Accessibility suite cleanup
*   **Removed Accessible Tiles** from the Stardew Valley accessibility suite, since that functionality is now built into Stardew Access.

---

# Version 1.2.3

Automates SMAPI for Stardew Valley — installing it, and keeping it up to date — so a new modder never has to use SMAPI's console installer or a download page.

---

## ✨ New in Version 1.2.3

### 🤖 Automatic SMAPI install and update
*   **One-step SMAPI install**: The Accessibility Suite Installer now installs SMAPI for you. It downloads the latest installer straight from SMAPI's official GitHub release and runs it silently against your detected Stardew Valley folder, so you no longer have to drive SMAPI's interactive console installer by hand. You hear spoken progress throughout — "Downloading SMAPI", "Installing SMAPI", and "SMAPI installed successfully".
*   **Update SMAPI in place**: When a mod update check finds a newer SMAPI (Stardew Valley reports this through the smapi.io check added in 1.2.2's groundwork), the manager now offers to download and install the update for you automatically, instead of just opening the download page. Choosing "No" leaves your install untouched.
*   **Safe fallbacks**: If your Stardew Valley folder can't be found, the download fails, or the install can't be confirmed afterwards, the manager says so and opens smapi.io so you always have a way forward.

---

# Version 1.2.2

Adds the ability to edit a mod's config and manifest files directly inside the manager, building on the 1.2.1 fixes.

---

## ✨ New in Version 1.2.2

### 📝 Edit a mod's config and manifest in-program
*   **Edit Config File (Ctrl+E)**: Open the selected mod's `config.json` in the built-in JSON editor to change mod settings without leaving the manager — for example, setting where the Stardew Valley "Skip Intro" mod skips to (such as `Load`). If a mod hasn't generated its config yet (most do so the first time the game runs with the mod enabled), the manager says so instead of failing.
*   **Edit Manifest File (Ctrl+M)**: Open the selected mod's manifest (`manifest.json` for Stardew Valley, or the manager's `.manager_manifest.json` for Skyrim/Fallout 4) to fix details directly — most usefully a version number a mod author forgot to bump, which otherwise keeps the mod flagged for updates. Saving a manifest edit re-scans the installed list so the corrected version takes effect immediately.
*   **Safe, accessible editing**: Both use the same in-program editor as the mod keybind config — Ctrl+S to save, Escape to cancel, JSON validation that refuses to save malformed files, an unsaved-changes prompt, and spoken prompts throughout. Both actions appear in the Mods menu, in the Installed-tab context help (Shift+F1), and in the Shortcut Customization dialog, so the keys can be rebound.

---

# Version 1.2.1

A small bug-fix release addressing two issues found when running the manager on a PC where not every supported game is installed.

---

## 🐛 Bug Fixes in Version 1.2.1

### 🎮 Loading a session for an uninstalled game
*   **No more wrong-game mods**: Loading a game session for a game that isn't installed previously fell back to the Stardew Valley Mods folder, silently showing Stardew's mods under the wrong game. The manager now detects this, announces that the session is for a game that hasn't been installed, and asks whether you'd like to purchase it.
*   **Guided purchase flow**: Answering "Yes" lets you choose **Steam** or **GOG**, then opens that store's page for the game in your default browser. At startup, a saved-but-uninstalled active game now returns you to the game-selection screen instead of loading another game's mods.

### 🔄 Duplicate update-check results
*   **One completion, accurate count**: Checking for mod updates while a check was already running (for example, the automatic startup check) could replay the "update check complete" sound repeatedly and report an inflated count full of duplicate entries. Update checks are now single-batch: a second check is held off until the first finishes, so the completion cue plays once and the count is correct.

---

# Version 1.2.0

This release builds on the 1.1.0 multi-game foundation with a game-aware audio theme system, broad accessibility and keyboard-focus fixes, and a smoother Skyrim Engine Fixes install.

---

## 🚀 What's New in Version 1.2.0

### 🎵 Game-Aware Audio Themes
*   **Themes follow the loaded game**: The sound theme now switches automatically to match the active game (Stardew Valley, Skyrim, or Fallout 4), falling back to the Default theme when no game is loaded.
*   **Manual override**: A new "Set theme manually" checkbox in Settings lets you pick a specific theme that persists across game switches and restarts; it announces its state when toggled.
*   **New themes**: Added complete Skyrim and Fallout 4 sound themes (the manager falls back to Default for any sound a theme does not provide).

### ♿ Accessibility & Keyboard Focus
*   **F6 focus cycle**: Fixed the Wiki and Walkthrough tabs so F6 correctly cycles results list → web view → tab headers and can move *into* the web view.
*   **Cleaner tab order**: Removed the split-view divider from the keyboard tab order, so Tab moves straight from the results list to the web view (no more stray "pane").
*   **List position announcements**: Every list now speaks its "X of Y" position whenever it receives focus — via F6, Tab, mouse click, returning from a dialog, or exiting the Alt menu — matching what you hear when arrowing.

### 🛡️ Nexus Session Handling
*   **Disconnect on session close**: Closing the current game session now properly disconnects from Nexus (with the matching theme's disconnect cue), and exiting the program only plays a disconnect when a game is still loaded.

### 📦 Skyrim Engine Fixes Install
*   **One-step SSE Engine Fixes**: Merged the two-part listing into a single suite entry that automatically installs the Part 2 preloader (`d3dx9_42.dll`) into the game root for premium accounts, with corrected manual instructions otherwise.

### 📚 More Walkthroughs
*   Added three additional verified walkthrough links for each supported game.

### 🧹 Build Hygiene
*   Removed an invalid managed reference to the native `Tolk.dll`, demoted a benign WebView2 `WindowsBase` conflict to a message, and fixed nullable-reference warnings (the project now builds with zero warnings).

---

# Version 1.1.0 (Major Release)

This release marks a major milestone, transforming the application from a Stardew Valley-specific manager into a multi-game accessible modding suite renamed **Kinetix Mod Manager**, while adding critical performance, stability, and installation fixes.

---

## 🚀 What's New in Version 1.1.0

### 🎮 Rebranding & Multi-Game Support
*   **Renamed to Kinetix Mod Manager**: The application has been fully renamed and rebranded to support a broader range of games.
*   **New Game Support**: Added full support for three titles:
    *   *Stardew Valley*
    *   *The Elder Scrolls V: Skyrim Special Edition*
    *   *Fallout 4*
*   **Dynamic Registry-Based Detection**: The manager now queries Windows Registry paths to automatically detect Steam and GOG installations for all three games. It hides unsupported games from the menu by default so you only see what is installed.
*   **Interactive Store Purchase Guide**: On initial launch, if no supported games are detected, a voice-guided wizard helps direct users to Steam and GOG stores to purchase or configure games.

### ⚡ NXM Download & Performance Improvements
*   **Resolved Large Mod Timeout/Hangs**: Removed redundant API queries (such as querying `files.json`) when resolving `nxm://` protocol download links, which previously caused timeouts on large mods.
*   **Stream-Based Downloads**: Replaced high-memory array downloads with memory-efficient stream buffers (`DownloadFileWithProgressAsync`), preventing system slowdowns when installing very large mods.
*   **Accessible Download Progress Speeches**: Wired up real-time progress updates. The active download percentage is displayed in the window title, and the manager calls your screen reader to **speak out milestones every 10%** (e.g., "Downloading... 10%", "20%", etc.), ensuring you are always informed of download status.

### 🛡️ AppData Directory Migration & Settings
*   **UserData Migration**: Moved all application-written directories (downloads, backups, profiles, and logs) from the application execution directory to the user's Local AppData folder (`%APPDATA%\AudiVentureGames\KinetixModManager`). On startup, the manager automatically migrates all files from the old `StardewAccessibleManager` directory to ensure no settings, profiles, or backups are lost.
*   **WebView2 Crash Fix**: Configured the internal WebView2 browser (used for the Wiki) to use a persistent user data folder under AppData, resolving startup crashes.
*   **Single-Instance Pipe Rename**: Upgraded single-instance IPC handlers (mutex and named pipes) to prevent instance conflicts when handling browser links.

### 📦 Installer & Dependency Fixes
*   **Bundled Native DLLs**: Corrected the Inno Setup configuration (`setup.iss`) to include all required native screen reader libraries (`Tolk.dll`, `nvdaControllerClient.dll`, and `nvdaControllerClient64.dll`) in the installer. This fixes issues where screen reader integration did not work out-of-the-box upon installation.
*   **Path Correction**: Standardized installer script file sources to point to the correct 64-bit release build folder (`win-x64\publish`).

### ♿ Web View Keyboard & Focus Fixes
*   **F6 Now Exits the Web View**: In the Wiki and Walkthrough tabs, pressing **F6** inside the embedded web page now reliably moves focus back out to the tab headers. Previously F6 could move *into* the web view but never out of it (the content runs in a separate browser process that the app couldn't intercept).
*   **Ctrl+Home / Ctrl+End Fix Tab Order**: Jumping to the top or bottom of a page now also repositions keyboard focus there, so a subsequent **Tab** / **Shift+Tab** continues from the right place instead of where you previously were.
*   **Predictable Page Edges**: **Shift+Tab** at the top of a page returns to the results/guides list, and **Tab** at the bottom moves to the tab headers, instead of Chromium wrapping focus around to the other end of the page.

### 🛡️ Stability & Reliability
*   **Global Error Handling**: Unexpected errors are now caught, logged to a crash log, and shown in an accessible dialog instead of silently crashing the app.
*   **Async Hardening**: Update routines no longer risk an unhandled crash mid-run, and a Web View initialization race that could occasionally leave a page blank has been fixed.
*   **Better Diagnostics**: Settings, profile-load, mod-ID-map, folder-migration, and SMAPI-log failures are now recorded in `mod_manager_log.txt` instead of failing silently.

### 🔧 Under the Hood
*   **Codebase Refactor**: The main form was split from a single ~6,800-line file into 18 focused modules (by feature area) with no change in behavior, making future maintenance and fixes much easier.

---

## 📜 Previous Versions Recap

### Version 1.0.1
*   Fixed Nexus Mods discovery and API querying.
*   Improved settings load/save reliability with secure Windows DPAPI key encryption.
*   Introduced initial SMAPI log analyzer rules.
*   Enhanced keyboard focus behavior between list views and menus.
*   Fixed layout constraints in the main screen and splash screen windows.

### Version 1.0.0 (Initial Release)
*   First public release of the Stardew Valley Accessible Mod Manager.
*   Keyboard-only and screen reader integration (NVDA, JAWS, and SAPI) via Tolk.
*   Installed mod browser, search, profile manager, and backup zip engine.
*   Built-in Wiki viewer.
