# Kinetix Mod Manager - User Manual

Welcome to Kinetix Mod Manager! This is a fully keyboard-driven, screen-reader-accessible mod manager built for the blind and visually impaired gaming community. It currently supports **Stardew Valley**, **Skyrim Special Edition**, **Fallout 4**, **Moonlight Peaks**, **The Witcher 3: Wild Hunt**, and **Minecraft (Java Edition)**, and works with NVDA, JAWS, and SAPI-based screen readers via Tolk.

You choose which game you're managing from the **Games** menu (press **Alt**, then arrow to **Games**), and the manager tailors its mod list, updates, wiki, and other features to that game. Most of this manual applies to every supported game; where something is specific to one game — such as the SMAPI log viewer for Stardew Valley — it is called out.

## Getting Started & First-Time Setup

1.  **Your First Launch (Setup Wizard)**: When you run the manager for the first time, a guided **Setup Wizard** opens automatically. It's a short, spoken **checklist** of the three things needed to get going — **choose a game**, **set your Nexus API key**, and **confirm your game folder** — and it tells you which steps are **done** and which are **not done yet**. Arrow up and down the steps and press **Enter** on one to complete it (this opens the game chooser, or the Settings window). The checklist refreshes as you finish each step, so you always know what's left. Press **Escape** to close it and finish later. You can reopen it any time from **Help → Setup Wizard (Getting Started)**.
2.  **Configuring Paths**: The app attempts to find your mods folder automatically for the selected game (the `Mods` folder for Stardew Valley, the `Data` folder for Skyrim Special Edition and Fallout 4, the `BepInEx\plugins` folder for Moonlight Peaks, the `mods` folder for The Witcher 3, or the `mods` folder inside `.minecraft` for Minecraft). It detects games on **any drive**, including secondary Steam libraries, and finds both the Steam and GOG copies of the games sold on both. If it succeeds, the path will be pre-filled. If not, please use the "Browse" button in settings to select it.
    *   **If a game can't be found**, picking it offers a small dialog with three choices — **Locate Installed Folder** (browse to the folder yourself, useful for unusual install locations or when running under Linux/Wine, where you point it at the game folder on your `Z:` drive), **View Store Links** (where to buy the game), or **Cancel**.
3.  **Nexus Integration**: To search for mods or check for updates, you **must** provide a **Nexus Mods API Key** (sometimes called a Nexus ID). This is a standard requirement for all mod managers. See the **"Nexus Mods Setup"** section below for instructions on how to get yours for free.
4.  **Closing Settings**: If you aren't ready to configure everything yet, you can press **Escape** or click **Cancel** to close the settings and browse the app. You can reopen this screen at any time by pressing **Ctrl + P**.
5.  **Getting Help**: 
    *   Press **F1** at any time to open this manual. It opens as a navigable list: arrow **Up / Down** through the sections, press **Right Arrow or Enter** to open a section that has sub-topics, **Left Arrow or Backspace** to go back, and **Tab** to move into the text on the right and read it line by line. Press **Ctrl + F** to search the whole manual for a phrase — see [Searching a Document](#searching-a-document-ctrl--f).
    *   Press **F2** at any time to open the **Change Log**. It uses the same navigable window — a list of versions you open (with **Right Arrow or Enter**) to read what changed in each one. **Ctrl + F** searches it too.
    *   Press **Shift + F1** while on any tab to hear a context-sensitive list of shortcuts for that specific area.
6.  **Splash Screen**: On every startup, an audio logo plays. You can press **Enter** to skip it and go straight to the main window.
7.  **On the game list**: while the "select a game" list is showing — at startup, or after you close a session — **F1** (manual), **F2** (change log), and **Ctrl + P** (Settings) all work, so you can read the manual or set a game folder before loading anything. When you close one of those windows, focus returns to the game list. **Escape** exits the manager.

---

## Nexus Mods Setup: Obtaining Your API Key

To search for new mods and check for updates, the manager needs to talk to Nexus Mods on your behalf. It does this using something called an **API key**. Think of the API key as a long, private password — made up of letters and numbers — that proves to Nexus Mods that the requests are really coming from your account.

Getting a key is **free**, you only have to do it **once**, and the manager remembers it securely afterwards. The whole process has three stages: (1) make a free Nexus account, (2) copy your personal key from the Nexus website, and (3) paste it into the manager. Each stage is broken down step by step below.

> **Tip for screen reader users:** The Nexus website changes its layout from time to time, which is why the steps below also give you **direct web addresses** you can type or paste into your browser to jump straight to the right page, instead of hunting through menus.

### Stage 1: Create a Free Nexus Mods Account

If you already have a Nexus Mods account, skip to Stage 2.

1.  Open your web browser and go to **https://www.nexusmods.com**.
2.  Find and activate the **"Register"** link. It is near the top of the page. (With a screen reader, you can press the letter **B** to jump between buttons, or use "find" to search the page for the word "Register".)
3.  Fill in the requested details — a username, your email address, and a password — then submit the form.
4.  Nexus will send you a **confirmation email**. Open it and activate the verification link inside. Your account is not fully active until you do this.
5.  Return to the Nexus website and **sign in** with your new username and password.

### Stage 2: Copy Your Personal API Key

You must be **signed in** to the Nexus website for this stage to work.

1.  Go directly to your API settings page by entering this exact address in your browser's address bar:
    **https://www.nexusmods.com/users/myaccount?tab=api**
    *   This link takes you straight to the right page, so you do not need to find any menus, buttons, or on-screen pictures. If for some reason it doesn't open the API page, first go to **https://www.nexusmods.com/users/myaccount** (your account settings), then on that page use your screen reader's "find" command to search for the link or tab named **"API Keys"** and activate it.
2.  This page has two parts. Near the top is a list of **"Application"** keys for specific tools — **you do not need these**. Keep moving **down the page** until you reach the section titled **"Personal API Key"**. This is the one you want.
3.  In the Personal API Key section:
    *   If you see a button such as **"Generate"** or **"Request Api Key"**, activate it once. (You only need to do this the very first time — it creates your key.)
    *   Your key will then be shown as a very long line of letters and numbers (usually 50 or more characters, sometimes with dashes).
4.  **Copy the entire key.** There are two easy ways to do this:
    *   **Easiest — use the copy button:** Right next to the key there is a button labeled **"Copy API Key"** (some screen readers may announce it simply as **"Copy"**). Move to that button and press **Enter** (or Spacebar) to copy the whole key straight to your clipboard. You don't have to enter the key field at all, and this guarantees you get the complete key.
    *   **By hand:** Alternatively, put your cursor in the key field, press **Ctrl + A** to select all of it, then **Ctrl + C** to copy.
    *   Either way, make sure you copy the **whole** thing, with no spaces before or after it. Copying only part of the key is the most common reason setup fails — which is why the **"Copy API Key"** button is the recommended option.

### Stage 3: Enter the Key into the Manager

1.  Switch back to Kinetix Mod Manager.
2.  Press **Ctrl + P** to open the **Settings Dashboard**.
3.  Tab to the field labeled **"Nexus API Key"**.
4.  Press **Ctrl + V** to **paste** your copied key into the field.
5.  Activate the **"Save Settings"** button (or press **Enter**).
6.  If the key is correct, the manager plays the **"Connect"** sound and shows your Nexus username in the window title bar. You are now connected.

### If It Doesn't Work

*   **You hear the "Disconnect" sound, or nothing happens.** The key was most likely copied incompletely or has extra spaces. Go back to Stage 2, copy the **entire** key again with **Ctrl + A** then **Ctrl + C**, and re-paste it.
*   **You're not sure a key was ever created.** Return to the Personal API Key section (Stage 2) and look for the **"Generate" / "Request Api Key"** button. If it's still there, your key hasn't been created yet — activate it.
*   **You can change or re-enter your key at any time** by pressing **Ctrl + L** in the manager, or by reopening the Settings Dashboard with **Ctrl + P**.
*   Your key is stored **encrypted** on your own computer and is never shown in plain text or shared with anyone but Nexus Mods.

---

## The Settings Dashboard (Ctrl + P)

Open the Settings Dashboard at any time with **Ctrl + P**. Everything you can configure lives here, and it is now organized into **tabs** so related options are grouped together instead of in one long list.

### Moving around the tabs

1.  When the dashboard opens, your focus lands on the **tab strip** at the top. Press **Left Arrow** and **Right Arrow** to move between the tabs; your screen reader announces each tab's name as you land on it.
2.  When you reach the tab you want, press **Tab** to move down into that tab's controls. From there, **Tab** and **Shift + Tab** move forward and backward through the fields on that tab.
3.  To go back to the tab strip and switch tabs, press **Shift + Tab** until you return to the tab name, or use **Ctrl + Tab** / **Ctrl + Shift + Tab** to jump straight between tabs from anywhere in the dashboard.
4.  The **"Save Settings"** button (at the bottom, reachable with **Tab** or by pressing **Enter**) applies the settings on **every** tab at once — you do not need to save each tab separately. Press **Escape** or **Cancel** to close without saving.

### What's on each tab

*   **Paths & Account** — Choose which game you're configuring, set its mods and game folders (with **Browse** buttons), and enter your **Nexus API Key**. This is also where you say **where your mods are searched for** — see "Choosing where your mods come from" below. If you own the same game twice, each copy is listed separately here so you can set each one's folders — see "Owning the Same Game Twice" below. For Skyrim and Fallout 4 there is also a **"Store this copy's mods inside the game folder"** checkbox, described in that same section.
*   **Startup** — Show or hide the splash screen, choose whether to check for mod and manager updates at launch, and turn the spoken **welcome** and **goodbye** messages on or off.
*   **Audio** — All sound options (see below).
*   **Display** — Low-vision visual options: a **high-contrast colour scheme** (white-on-black, yellow-on-black, or black-on-yellow) and a **text size** (Normal, Large, or Extra Large). Both apply across the whole program and take effect as soon as you save — no restart needed. They change only what's drawn on screen and never affect screen-reader speech, so leaving them at their defaults keeps the normal appearance.
*   **Mods & Search** — Search results per load, maximum backups kept per mod, whether to save your search history, **"When a download is for another game"** (see "Downloading a Mod for a Game You Don't Have Open" below), and (for Skyrim and Fallout 4) **Protect Plugin Order and Creations**, which stops the game switching your Creations off when you start a new game — see "Keeping Your Creations On and Your Plugins in Order" below.
*   **AI** — Turn on optional AI features, pick an AI provider and model, and enter your own API key (see "AI Log Diagnosis" below).
*   **Language** — Pick the manager's display language, or leave it on **Automatic** to follow Windows.

### Choosing where your mods come from

Different games keep their mods in different places, and some keep them in more than one. On the **Paths & Account** tab:

*   **"When searching for mods"** offers three ways to work, and you pick the one you like:
    *   **Search every source and merge the results** — the most thorough. A mod on a site you never visit can still turn up.
    *   **Search one source only** — the quietest to listen to. No duplicates, nothing merged.
    *   **Search my preferred source, and the others on request** — the default, and what the manager has always done. When there is somewhere else worth asking, it says so, and **Alt + O** in the Discovery list searches those too.
*   **"Preferred mod source"** appears whenever the first setting needs one, and it is remembered **per game** — because the sites differ per game. Minecraft's mods are on Modrinth and CurseForge; Stardew Valley's are on Nexus, ModDrop and CurseForge.
*   **When more than one source answers, each result says which one it came from.** When only one was asked it does not, because the same three words on every row of a hundred tell you nothing. A mod that appears on two sites is listed once, from whichever you prefer.
*   **Skyrim, Fallout 4, The Witcher 3 and Moonlight Peaks have no dropdown**, and one line saying why: Nexus is the only place with a searchable catalogue of their mods. Bethesda.net, ModDB and similar sites have no way in for a program like this one.
*   **CurseForge is listed but cannot be used yet.** It needs an API key that CurseForge issues to approved applications — a conversation with them rather than a setting. It is shown, with the reason, so you can see that the manager knows about it.

### Your mod site keys, all in one place

Some mod sites want an API key before they will answer. From the **File** menu, choose **"Mod source API keys..."** — it lists every site that asks for one and whether you have given it.

*   **Press Enter on a site.** If it has no key, you are asked to type or paste one. If it already has one, the key appears in a **read-only box** so you can read it back and check it against what you meant to type.
*   **"Edit key"** asks for a new one whichever the case — which is the point of it. A key you typed wrongly months ago cannot be put right by a screen that only ever shows it back to you.
*   **"Forget key"** (or the **Delete** key on a row) removes one, after asking. The manager cannot get it back for you.
*   **"Open the site to get a key"** takes you straight to the page where that site issues them.
*   **Arrowing down the list never reads your keys out loud.** Each row says the site's name and whether a key is saved, and nothing else. The key is only spoken on the one row you open, because a credential read out in passing is read out in whatever room you happen to be in.
*   **Keys are stored encrypted on your computer**, the same way your Nexus key and your AI provider key already were, and they are never sent anywhere except to the site they belong to.
*   **Sites that need nothing are not listed at all.** Modrinth and GitHub need no account to search or download, so there is nothing for you to do about them.

**What about a site with a login rather than a key?** A login ends in a key too — that is what it is for. Nexus can sign you in on its own website and hand the manager a key without you ever typing one, which is better: the manager never sees your password, and you can cancel that key later without changing it. That is built and waiting on Nexus approving the manager as an application, and until then the screen tells you so rather than offering you a button that cannot work.

### Installing a mod straight from GitHub

Some mods are only ever published on GitHub. From the **Mods** menu, choose **"Install a mod from a GitHub repository..."**, then type `owner/repo` — or simply **paste the address** of the repository's page out of your browser. The releases page, a link to a single release and the clone command all work too.

The manager finds the newest release, works out which of its files is the actual mod, downloads it and installs it exactly as it would a file you had picked yourself — backing up what it replaces, running the FOMOD wizard if there is one, and recording which release went on so it can tell you when the next one appears.

GitHub is not one of the sources in the dropdown above, and that is on purpose: there is no catalogue to search. You cannot ask GitHub for "Stardew mods about fishing". You can name a repository, which is what this does.

### The Audio tab in detail

*   **Enable UI Sounds** — This is the **first** control on the tab and acts as a master switch for the manager's sound effects (the connect, enable, disable, error sounds, and the startup logo). **Uncheck it to turn all of those sounds off.** When it is unchecked, the rest of the audio options (volume, sound theme, and the logo selector) are **hidden**, since they no longer apply — leaving only the download and install feedback option, which is controlled separately and still works even with UI sounds off.
*   **Sound Volume** — A dropdown from 0 to 100 for the overall loudness of those sound effects.
*   **Set theme manually / Current Audio Theme** — By default the sound theme follows the game you're managing, so a Skyrim session and a Stardew session sound different from the moment you load them; check **Set theme manually** to choose a specific theme from the dropdown instead. A theme the manager hasn't got a particular sound for falls back to the Default theme for that one sound, so a part-finished theme is never silent.
*   **The connect and disconnect sounds mean something different in Minecraft.** For every other game they tell you the manager has signed in to Nexus Mods, or lost that connection. Minecraft's mods come from Modrinth, which has no accounts at all, so for Minecraft they follow the connection you actually care about: **connect plays when you join a multiplayer server or a Realm, and disconnect when you leave it** — including when you're dropped, kicked or timed out, and when you quit the game while still on a server. Playing on your own worlds produces neither, because a singleplayer world isn't a connection to anything.
*   **Random Logo at Startup / Select Specific Logo** — These appear only when **Show Splash Screen** (on the Startup tab) is enabled, and let you pick which startup logo sound plays.
*   **Download and install feedback** — Choose how long downloads and installs report progress: **Tones**, **Speech**, **Both**, or **Off**. This is independent of the **Enable UI Sounds** switch above, so you can keep progress feedback even with the other sounds turned off (or vice versa).

---

## Owning the Same Game Twice

Some people own a game on **both Steam and GOG** — bought it once, then picked it up again in a sale, or kept a DRM-free copy alongside the Steam one. The manager treats those as two separate games, because that is what they are.

### Both copies in the Games menu

When two copies of a game are found, the **Games** menu lists each one with its store in the name: *"Skyrim Special Edition (Steam)"* and *"Skyrim Special Edition (GOG)"*. The store is named **only** when there are two copies to tell apart, so if you own one copy of a game its entry reads exactly as it always has.

The title bar names the copy you are in too, so you always know which one you are working on without having to check.

The first time a second copy turns up, the manager tells you it found one and where it is. It does not choose for you — both are in the menu, and you switch between them the same way you switch games.

### Each copy keeps its own mods

Switching between copies switches **everything** that belongs to that copy: its installed mods, load order, plugin order, mod priorities, conflict overrides, profiles, backups, downloads, save backups and search history.

This is the part worth knowing: **installing a mod for one copy does not install it for the other.** They are separate setups. If you want the same mods in both, install them in both.

Your saves, INI files and load order were already kept separate by the two copies themselves — Skyrim's GOG release stores those under a different folder name than the Steam release does — and the manager follows that.

### Which copy is which

The manager works out a copy's store from **what is in its folder**, not from where the folder is. Every GOG install leaves a small marker file behind, and that is what gets checked. This matters more than it sounds: GOG installs "Skyrim Special Edition" into a folder called *"Skyrim Anniversary Edition"*, and a Steam copy could easily be sitting in a folder called "GOG Games" without being a GOG copy at all.

### Where a copy's mods are stored

Skyrim SE and Fallout 4 normally stage their mods in the manager's own folder, away from the game. On the **Paths & Account** tab of Settings there is a checkbox — **"Store this copy's mods inside the game folder"** — that puts them in a `KinetixMods` folder inside the game instead, which is what Stardew Valley, Moonlight Peaks and The Witcher 3 already do.

Two reasons you might want that:

*   The mods travel with the game — useful when you have two copies and want each to be self-contained.
*   Because they end up on the same drive as the game, the manager can **link** files into the game rather than copying them, so a large setup does not take up twice the space.

One reason to think about it first, which the manager says before it moves anything:

*   **Uninstalling the game through Steam or GOG deletes the game folder, and your mods go with it.** Using the store's "verify game files" option can remove them too. In the manager's own folder they survive both.

Ticking or unticking the box moves the mods straight away, after asking. Nothing is deleted during the move: the mods are copied to the new place first and only removed from the old one once they have all arrived, so if anything goes wrong they are still where they were.

If you already have mods staged in the manager's folder, they stay there until you choose otherwise — an update never moves them for you.

---

## Downloading a Mod for a Game You Don't Have Open

You do not have to load a game before downloading a mod for it. Browsing Nexus, finding something you want and pressing **Mod Manager Download** works with a different game open, with no game open, and with the manager closed altogether — every download link names the game it is for, and the manager reads it from there.

What happens next depends on where you were:

*   **You were in that game already.** Nothing changes: the mod downloads and you are asked whether to install it, as always.
*   **You were in a different game.** The mod is downloaded first — a download link expires within minutes, so waiting is not an option — and then you are asked what to do:
    *   **Switch to that game and install it now.** The manager loads the mod's game and carries on with the install.
    *   **Save it for that game and stay here.** Nothing about your session changes. The mod is filed under its own game, so the next time you load that game it is waiting in **Downloads History** (Ctrl + Shift + W), one keypress from installed.
    *   **Escape** does the same as saving it, and says so. The download is never thrown away.
*   **No game was open.** There is nothing to interrupt, so the manager loads the mod's game and installs it, telling you which game it went to.
*   **You own that game twice.** You are asked which copy the mod is for. The link does not say, and the two copies are separate setups — installing into one does not install into the other.

If you would rather not be asked, Settings (Ctrl + P) → **Mods & Search** → **"When a download is for another game"** offers three answers: **Ask me each time** (the default), **Switch to that game and install**, or **Save it for that game**. The mod is downloaded whichever you choose; the setting only decides whether the manager also leaves the session you are in.

Two things it will tell you rather than fail at: a mod for a game the manager does not support says so and names the game as Nexus calls it, and a mod for a supported game you have not set up yet asks you to load that game once from the **Games** menu first.

---

## Confirmations and Messages

When the manager needs to ask you something — "Delete this mod?", "Launch anyway?" — the question appears **inside the window you're already in**, not in a separate pop-up window. You'll hear **the question first, then the choice your fingers are on**, for example *"Delete "auto" from the search history? Yes, Alt Y."*

*   **Enter** takes the choice you're on, and **Tab** moves between the choices.
*   **Alt plus the underlined letter** picks a choice directly — **Alt + Y** for Yes, **Alt + N** for No.
*   **Escape** cancels (answering No, or Cancel where there is one).

While a question is on screen the rest of the window is switched off, so nothing behind it can be reached by mistake. Answer it and you're returned to exactly where you were.

---

## Keyboard Shortcuts

Kinetix Mod Manager is fully keyboard-driven. The shortcuts are grouped by where they apply, and each group has its own topic in this manual's contents list, just below this one. Here is what each group covers:

*   **Global Shortcuts**: Keys that work anywhere in the app — opening this manual, context help, launching the game, cycling focus with F6, opening Settings, logging in with your Nexus key, checking your remaining Nexus API requests, opening the downloads, backups, and error-log folders, turning Curation Mode on or off, and more.
*   **Mod List Shortcuts (Installed Mods Tab)**: Managing your installed mods — enabling, disabling, deleting, searching, categorising, adding notes, saving profiles, exporting and installing Collections, endorsing mods, viewing dependencies, installing from a zip, reading descriptions, and opening a mod's Nexus page.
*   **Profiles Tab Shortcuts**: Applying and deleting saved mod setups.
*   **Backups Tab Shortcuts**: Restoring, deleting, and pruning your automatic mod backups.
*   **SMAPI Log Tab Shortcuts (Stardew Valley only)**: Searching the SMAPI log, jumping to a line, diagnosing issues, and uploading the log for help. This tab appears only when Stardew Valley is the active game.
*   **Search & Updates Tabs**: Opening mod pages, loading more search results, ignoring an update, updating all mods, and reading summaries.
*   **Load Order Tabs (Skyrim & Fallout 4 only)**: Reordering mod priority and plugin load order, auto-sorting plugins, and activating or deactivating Creations. These tabs appear only for Skyrim Special Edition and Fallout 4.
*   **Log Tab (Skyrim & Fallout 4 only)**: Viewing the game's script-extender and plugin logs, filtering and searching them, and refreshing them live.
*   **Wiki Tab Shortcuts**: Searching the active game's wiki, opening pages and categories, and moving between the search box, dropdowns, results list, and the web view.

You can also press **Shift + F1** on any tab at any time to hear the shortcuts for just that tab.

### Global Shortcuts
*   **F1**: Open this User Manual (Internal Window).
*   **F2**: Open the **Change Log** (what's new and fixed in each version).
*   **F3**: Open the **Mod Documentation** viewer for the active game's accessibility mod (see "Mod Documentation Viewer" below). Inside any of the three document viewers, F3 instead moves to the next search result.
*   **Shift + F1**: **Context Help** - Speaks the shortcuts for your current tab.
*   **Ctrl + F** (inside the F1, F2 or F3 viewer): Search the whole document for a phrase; **F3** / **Shift + F3** then step through the results. See "Searching a Document" below.
*   **F5**: Launch the active game through its mod loader (SMAPI for Stardew Valley, SKSE for Skyrim Special Edition, F4SE for Fallout 4).
*   **F6**: **Cycle Focus** - Jump between the tab headers and the primary list in each tab (and the web view in the Wiki tab).
*   **F9**: **Diagnose Log with AI** - Send the current SMAPI or game log to your chosen AI provider for a plain-language explanation and fixes (opt-in; see "AI Log Diagnosis" below).
*   **Alt**: Access the Menu Bar.
*   **Ctrl + P**: Open the **Settings Dashboard**.
*   **Ctrl + L**: Change/Login with Nexus API Key.
*   **Ctrl + Shift + A**: Speak your remaining Nexus **API requests** for this hour and today (see "Checking Your Nexus API Requests" below).
*   **Ctrl + D**: Open your `downloads` folder.
*   **Ctrl + B**: Open your `backups` folder.
*   **Ctrl + Shift + L**: **Open the log** - the manager's record of anything that failed or crashed, including what it was doing at the time. This is the file to send when reporting a problem. It records failures the manager recovers from as well as ones you see, so a problem that left no visible trace is still in there. It opens inside the manager as a read-only view, at the newest entry - **Ctrl + End** for the very end, Escape to close. Nothing in it can be changed by accident, and the full path to the file is given when it opens.

**The game's logs open the same way.** Where the game has more than one - a script extender's folder holds the extender's own log and one for every plugin that writes anything - you get a list of them, the one most likely to have the answer first and the rest by how recently they were written. Press **Enter** on one to read it and **Escape** to go back to the list. Nothing opens in Notepad any more, and no log can be changed while you are reading it.
*   **Ctrl + H**: Open the **Game and Mod Controls** viewer for the active game (a navigable drill-down of every control — see "Game and Mod Controls Viewer" below).
*   **Ctrl + Shift + H**: Open your **Search History** for the active game (see "Searching for Mods" below).
*   **Ctrl + Shift + F7**: Turn **Curation Mode** on or off — the switch that reveals the commands for building your own Suggested Mods list. Works either way round, since turning it on is what it is for (see "Suggested Mods" below).
*   **F7**: **Mark or unmark** the selected mod as a suggested mod. Works in the **Installed Mods** list and in **Find New Mods**. Only while Curation Mode is on.
*   **Shift + F7**: Open **the mods you have marked**, across every game, where their categories and reasons can be edited. Only while Curation Mode is on.
*   **Escape**: Close the Manual, Settings, or Sound Demo windows.

### Mod List Shortcuts (Installed Mods Tab)
*   **Space**: Enable or Disable the selected mod.
*   **Delete**: Permanently delete the selected mod folder (creates a backup first). Because that backup is a zip of the whole mod, a large one takes a moment: the manager says *"Deleting <mod>. Backing it up first…"* and reports progress using whatever download-and-install feedback you've chosen in Settings (tones, spoken percentages, both, or off), then confirms *"Deleted <mod>"* when it's done.
*   **Ctrl + F**: **Search** - Focus the search bar to filter your installed mods.
*   **Ctrl + J**: **Change Category** - Assign a custom category to the selected mod.
*   **Ctrl + Shift + J**: **Batch Category Action** - Enable or Disable all mods in the currently filtered category.
*   **Ctrl + Shift + O**: **Add or Edit Note** - Attach a personal note to the selected mod, spoken whenever you select it (see "Mod Notes" below).
*   **Ctrl + S**: **Save Profile** - Saves your current enabled/disabled mod setup.
*   **Ctrl + Shift + X**: **Export Collection** - Save your current mods as a shareable Collection file (see "Mod Collections" below).
*   **Ctrl + Shift + N**: **Install Collection** - Install a Collection file (see "Mod Collections" below).
*   **Ctrl + G**: Open the mod's page on Nexus Mods.
*   **Ctrl + Shift + E**: **Endorse or un-endorse** the selected mod on Nexus Mods (see "Endorsing Mods" below).
*   **Ctrl + Shift + G**: **View Selected Mod's Changelog** - the selected mod's version history from Nexus (see "Mod Changelog and Full Description" below).
*   **Ctrl + Shift + I**: **View Selected Mod's Full Description** - the selected mod's complete Nexus page description (see below). Both also work in the Updates and Find New Mods lists.
*   **Ctrl + Y**: **View Dependencies** - Opens the dependency view for the selected mod: what it *requires* (and whether each is installed) and what is *required by* it (other installed mods that depend on it). See "Dependency View and Resolver" below.
*   **Ctrl + Q**: **Resolve Missing Requirements** - Finds every missing required mod for the selected mod and, on Skyrim/Fallout 4 and Minecraft, offers to download and install them automatically. See "Dependency View and Resolver" below.
*   **Ctrl + Shift + B**: **Check for Broken Mods** - Cross-references your installed mods against the community compatibility list and reports any known to be broken, abandoned, obsolete, or incompatible. See "Checking Your Mods" below.
*   **Ctrl + Shift + K**: **Check My Setup** - Runs the Windows runtime, requirements, plugin-limit, broken-mod, and file-conflict checks together and gives one spoken summary. See "Check My Setup (Setup Health Check)" below.
*   **Ctrl + Shift + T**: **Check Tracked Mods for Updates** - Lists the mods you track on Nexus that need attention. See "Tracked Mods" below.
*   **Ctrl + Shift + W**: **Reinstall a Downloaded Mod** - Reinstall from your downloads folder without re-searching. See "Reinstalling a Downloaded Mod" below.
*   **Ctrl + Shift + P**: **Install a Download Not Yet Installed** - The mods you downloaded but said "not now" to when the manager offered to install them. See "Installing a Download You Skipped" below.
*   **Ctrl + Shift + U**: **Plugin Slot Usage** (Skyrim & Fallout 4) - Speaks how many plugin slots you're using. See "Plugin Limit Awareness" below.
*   **Ctrl + Shift + V**: **Manage Save Games** (Skyrim & Fallout 4) - Browse, back up, and delete your saves. See "Savegame Manager" below.
*   **Ctrl + K**: Manually assign a Nexus ID.
*   **Ctrl + I**: Install a mod from an archive file. **`.zip`, `.7z`, and `.rar`** archives are all supported.
*   **Ctrl + R**: **Read Description** - Speaks the full summary of the mod.
*   **Ctrl + E**: **Change the Mod's Settings** - Where the mod's author has described its settings, they are offered as a **list to arrow through**, with each setting's explanation and the values it accepts. See "The Mods Menu" above for how this differs from the two below.
*   **Ctrl + Shift + M**: **Edit the Config File Directly** - Opens the mod's `config.json` as text in the JSON editor, for a setting the author never described or a value outside the ones they listed. Most mods only write a config file the first time the game runs with the mod enabled.
*   **Ctrl + M**: **Edit the Manifest** - Opens the mod's manifest, which holds its identity — name, version and update links — rather than its settings.
*   **Apps Key**: View mod details.

### Profiles Tab Shortcuts
*   **Enter**: **Apply Profile** - Automatically enables/disables mods to match the saved setup.
*   **Delete**: Remove the selected profile.

### Backups Tab Shortcuts
*   **Enter**: **Restore Backup** - Re-installs that specific mod version.
*   **Delete**: Permanently remove the backup zip file.
*   **Ctrl + Shift + D**: **Delete Old Backups** - Deletes all but the most recent archives based on your settings.

### SMAPI Log Tab Shortcuts (Stardew Valley only)
*The SMAPI Log tab appears only when Stardew Valley is the active game, since SMAPI is its mod loader.*
*   **Ctrl + F**: Focus the **Log Search** bar.
*   **Enter (in Search bar)**: Filter the log to show only matching lines.
*   **Enter (on a search result)**: Restore the full log view and **jump** to that specific line.
*   **Ctrl + Q**: **Quick-Fix** detected issue (if a missing mod is found).
*   **Ctrl + L**: Upload log to SMAPI.io for troubleshooting help.
*   **F4**: Open the raw log file in Notepad.

### Search & Updates Tabs
*   **Enter**: 
    *   In **Updates**, on an update the manager can fetch itself — anything from Modrinth or a GitHub release, and every Minecraft mod — you're asked **whether to update it now or open its page**, with update first. Escape leaves it alone. For a Nexus update, Enter opens its page as before, because fetching one needs the Mod Manager Download button or a Premium account.
    *   In **Updates**, on the **Fabric Loader** or **Minecraft** row: install it, or start moving to the new Minecraft version. Those aren't mods and have no page. See "Keeping Fabric and Minecraft up to date".
    *   In **Search for Mods**: Open the Nexus page for the selected mod — **or**, on the **"Load more results"** row at the bottom of the list, load the next batch of results.
*   **Delete**: (In Updates tab) **Ignore Update** - Hides this specific version from the updates list.
*   **Ctrl + Shift + Y**: (In Updates tab) **Mark As Already Installed** - Records the offered version as the one you already have. See "When a mod is offered the same update forever" below.
*   **Ctrl + U**: **Update All** mods. A **Nexus Premium** account is only needed for the updates that come from Nexus; mods from Modrinth or GitHub releases are updated whatever account you have, and the confirmation says how many Nexus updates will be skipped.
*   **Ctrl + R**: Read the mod's summary.

### Load Order Tabs (Skyrim & Fallout 4 only)
*These tabs — **Mod Priority**, **Plugin Order**, and **Creations** — appear only for Skyrim Special Edition and Fallout 4. See the "Load Order Management" section below for what they do.*
*   **Ctrl + Up / Ctrl + Down**: Move the selected mod (Mod Priority tab) or plugin (Plugin Order tab) higher or lower in the order.
*   **F8**: **Auto-sort** the plugin load order (Plugin Order tab), arranging every plugin to load after the masters it needs, using LOOT rules when available.
*   **Space**: (Creations tab) Activate or deactivate the selected Creation.

### Log Tab Shortcuts (Skyrim & Fallout 4 only)
*The **Log** tab (shown as "Fallout 4 Logs" or "Skyrim Logs") appears only for those games. See the "Game Logs" section below.*
*   **Ctrl + Shift + R**: **Refresh** the log — re-reads it live, even while the game is running.
*   **F4**: Open the currently selected log file in Notepad.
*   **Ctrl + C**: Copy the selected line(s) to the clipboard.

### Wiki Tab Shortcuts
*   **Enter (in Search bar)**: Search the active game's wiki for the entered text.
*   **Enter (on a Result)**: 
    *   If it's a **Page**: Load the content into the Web View.
    *   If it's a **Category**: Drill down into that category to see its members.
*   **Backspace (on a Result)**: Go back up to the previous category level or search results.
*   **F6**: Quickly jump from the Results list to the Web View content, then back to the Tab headers.
*   **Tab**: Move focus between the Search box, Categories dropdown, Results list, and the Web View.

---

## How the Mods Menu Is Organized

The **Mods** menu (press **Alt**, then arrow to **Mods**) keeps its most common actions — **Install from Archive**, **Update All Mods**, and **Launch Game** — at the top, and groups everything else into **submenus** so you're not faced with one long list. Arrow to a submenu and press **Right Arrow** or **Enter** to open it. The submenus are:

*   **Selected Mod** — actions on the mod highlighted in your list: edit note, settings, config file, or manifest; view dependencies; resolve missing requirements; view changelog or description; endorse; verify files.

There are three ways into a mod's own files and settings, and it's worth knowing which does what:

*   **Ctrl + E — Change Selected Mod's Settings.** The usual one. Where the mod's author has described its settings — a Content Patcher pack, a Mod Configuration Menu, a BepInEx config, or an ordinary Stardew config the manager can make sense of — you get a **list to arrow through** with the author's own explanation of each setting and the values it accepts, rather than a file to type into.
*   **Ctrl + Shift + M — Edit Selected Mod's Config File Directly.** The same `config.json` opened as text in the JSON editor. Use it for a setting the author never described, or a value outside the ones they listed, which a list of choices cannot offer. Most mods only write a config file the first time the game runs with the mod enabled, so a brand-new mod may not have one yet.
*   **Ctrl + M — Edit Selected Mod's Manifest.** The mod's identity — its name, version and update links — rather than its settings.
*   **Install and Update Mods** — auto-match Nexus IDs, reinstall a downloaded mod, and check tracked mods for updates.
*   **Suggested Mods** — read the suggested mods for this game, and import or export a list. With **Curation Mode** switched on it also holds the commands for building a list of your own: mark or unmark the selected mod, review the mods you have marked, and manage your categories. Those three are not shown while Curation Mode is off, so the menu never offers you a command you cannot use. See "Suggested Mods" below.
*   **Profiles and Collections** — save a profile, export or install a Collection, import from Mod Organizer 2.
*   **Health and Reports** — Check My Setup, Check Mod Requirements, Check for Broken Mods, File Conflicts, Update Coverage Report, Plugin Slot Usage, and Reset Ignored Requirements.
*   **Load Order and Files** (Skyrim & Fallout 4 only) — auto-sort, choose file-conflict winners, load-order rules, rebuild or purge deployment, and export or import your load order. This whole submenu is hidden for Stardew Valley.
*   **Game and Maintenance** — install the accessibility suite, uninstall the script extender, edit game INI files, manage save games, prepare for and restore after a game update, and restore a safety backup.

Every command still has its usual keyboard shortcut, which works no matter which submenu it now lives in.

---

## Navigation Cycle (F6)
The **F6** key is a powerful tool for quickly moving your focus between major areas of the application without having to press Tab many times.

1.  **From Tab Headers**: If you are sitting on a tab name (like "Installed Mods"), press **F6** to jump directly into the list of mods.
2.  **From a List**: If you are browsing a list, press **F6** to jump back up to the Tab headers. This is the fastest way to switch tabs after you've finished managing your mods.
3.  **Wiki & Walkthroughs Special Cycle**: In the **Wiki** and **Walkthroughs** tabs, F6 follows a three-step cycle:
    *   Press **F6** from the tab name to jump to the **Results / Guides List**.
    *   Press **F6** again to jump into the **Web View** (where the actual page/guide content is).
    *   Press **F6** one more time to jump back to the **Tab Headers**.

This shortcut is especially useful when a page is very long, as it allows you to escape the web content and get back to your results/guides list or other tabs instantly.

---

## Wiki Integration
The Wiki tab (e.g. **Stardew Wiki**, **Skyrim Wiki**, **Fallout 4 Wiki**, **Moonlight Peaks Wiki**, **Witcher Wiki**, or **Minecraft Wiki**) provides a built-in, accessible way to browse the game's wiki.
1.  **Search**: Type any item, quest, villager, or mechanic into the search box and press **Enter**.
2.  **Browse by Category**: Use the **Categories** dropdown to quickly find lists of pages — for example Villagers, Crops, and Fish in Stardew Valley; Quests, Factions, Weapons, and Locations in Skyrim and Fallout 4; Characters, Crops, Farming, Cooking, and Crafting in Moonlight Peaks; or Blocks, Hostile mobs, Biomes, Structures, Enchantments, and Redstone in Minecraft. A game wiki's categories are a short list chosen and checked by hand, so they are the sections you would actually go looking for rather than several hundred entries you would have to read past. A **mod** wiki's categories are fetched from that wiki itself, since it has no hand-picked list.
3.  **Switch Wikis with the Mod Wikis dropdown**: The **Mod Wikis** dropdown lets you choose which wiki you are searching. Alongside the main game wiki, it lists dedicated wikis for popular content mods — world-expansion wikis for Stardew Valley (such as Stardew Valley Expanded, Ridgeside Village, and East Scarp), and large quest or new-land mod wikis for Skyrim and Fallout 4 (such as the Elder Scrolls Mods Wiki, Legacy of the Dragonborn, and Sim Settlements 2). Moonlight Peaks is new enough that no content mod has a wiki of its own yet, so its dropdown offers the community Fandom wiki, a guides site, and its Nexus Mods listing instead. Choosing a wiki points the Search box, the Categories dropdown, and the Web View at that wiki. A few wikis are "browse-only" (they open in the Web View, but can't be searched in-app); the manager tells you when that's the case.
4.  **Navigation**: The results list shows pages and sub-categories. You can "drill down" into categories by pressing **Enter** and go back up by pressing **Backspace**.
5.  **Accessible Reading**: When you press **Enter** on a page, it loads in the integrated **Web View**. This view is fully compatible with screen readers, allowing you to use standard web navigation commands:
    *   **H**: Jump between Headings.
    *   **T**: Navigate Tables.
    *   **L**: List links on the page.
6.  **Moving In and Out of the Page**: While focus is inside the Web View:
    *   **Ctrl+Home** jumps to the top of the page (and the heading), **Ctrl+End** to the bottom — and your **Tab** order continues from there.
    *   **Shift+Tab** at the top of the page returns to the Results list; **Tab** at the bottom moves to the tab headers.
    *   **F6** leaves the Web View and returns to the tab headers from anywhere.

---

## Walkthroughs & Guides Integration
The Walkthroughs tab (e.g. **Stardew Walkthroughs**, **Skyrim Walkthroughs**, **Fallout 4 Walkthroughs**, **Moonlight Peaks Walkthroughs**, **Witcher 3 Walkthroughs**, or **Minecraft Walkthroughs**) lists high-quality, text-only walkthroughs and community guides for the selected game. Minecraft's list leads with navigation guides, since finding your way is the hardest part of the game without sight, and ends with the guides written for Minecraft Access.
1.  **Guides List**: Select a guide from the list using the Up/Down arrow keys. The guide content automatically loads in the Web View on selection.
2.  **Accessible Reading**: Press **F6** or **Tab** to enter the **Web View** pane. Since these walkthroughs are text-based guides, screen reader browse-mode commands (like **H** to jump between headings and **T** for tables) work seamlessly to make navigation easy for blind players.
3.  **Exit Content**: Press **F6** at any time to escape the web view and return to the main tab headers.

---

## Filtering the Installed Mods List

Above the **Installed Mods** list are three controls that narrow down what's shown, and they **combine** — so you can, for example, see only *disabled* mods in the *Gameplay* category:

*   **Search box (Ctrl + F):** type any text to show only mods whose **name, author, or note** contains it. (Searching your notes means you can find a mod by something you wrote about it.)
*   **Category dropdown:** show only mods in the category you pick, or **All Categories**.
*   **Status dropdown:** show **All Mods**, **Enabled Only**, **Disabled Only**, **Has a Note**, **Single Mods Only**, or **Mod Groups Only**.

### What the Status dropdown offers

The first four sort by a mod's **state** — what you have done with it:

*   **All Mods** — everything you have installed.
*   **Enabled Only** and **Disabled Only** — the mods currently switched on, or currently switched off.
*   **Has a Note** — only mods you have attached a note to with Ctrl + Shift + O.

The last two sort by a mod's **shape** — whether it stands on its own or shares a folder with others:

*   **Single Mods Only** — mods that are the only thing in their folder.
*   **Mod Groups Only** — mods that share a folder with others, shown as the groups they belong to.

Between them, those two account for every mod you have: none is in both, and none is left out of both. If the two counts add up to your full total, nothing has been missed.

### Mod groups, and why the counts differ

Mods that live under the same top-level folder are gathered into a **mod group**: one row standing for several mods, which you **expand with Right or Plus** and **collapse with Left or Minus**. Large mods that ship as a family of related pieces would otherwise fill the list with entries you never manage separately.

Grouping applies whatever the filters say, and a group is built from the mods that **matched** — so a group under **Enabled Only** that says "Contains 2 mods" means two *enabled* mods, not two mods of which some are off. A folder with only one match is shown as an ordinary row rather than a group of one. Groups stay collapsed or expanded exactly as you last left them, filter or no filter.

Because a collapsed group keeps its mods off the screen, **the number of mods and the number of rows are rarely the same**. Whenever any group is on show, the manager tells you both:

> *"All Mods. 198 mods found. 147 rows, including 31 mod groups."*

198 mods, but 147 rows to arrow through, because 31 of those rows are groups holding the rest. Expand them all and the rows would instead outnumber the mods. Where a sorting has no groups in it, only the total is given — *"Disabled Only. 2 mods found."*

Your screen reader names the sorting itself as you arrow onto it, and the manager holds its counts back until that has been said — so you hear **which view you are in first, and the numbers after**. The counts are given for every setting of the dropdown, including All Mods, so widening back out is confirmed as clearly as narrowing was. Arrow quickly past several options and only the one you stop on is counted; the ones you passed through are dropped rather than read out late.

Clear the search box and set both dropdowns back to "All" to see your full list again.

---

## Searching for Mods
The **Search for Mods** tab allows you to browse and search for mods on Nexus Mods without leaving the app.
1.  **Search**: Type a mod name in the search box and press Enter.
2.  **Types**: Use the **Type** dropdown to choose what the tab lists. Each one asks Nexus a different question, and the order results come back in is what really separates them:
    *   **Search** looks for the words you typed in the search box, and is the only mode that uses it. ⚠️ It matches **whole words, not fragments**: searching *"modmenu"* finds nothing, while *"Mod Menu"* finds the page. If a search comes back empty, try spacing the name out as its author would have written it.
    *   **All** lists **every mod the game has**, in **alphabetical order** by name. This is the one to use when you're looking for a specific mod you already know exists, or working through the whole catalogue to find the mods you already have installed — unlike Trending or Most Popular, nothing is left out, and because the order is alphabetical you can always tell where you got to. Pair it with a large **Results per load** to pull in more at a time.
    *   **Trending** lists the mods with the **most endorsements** first — what the game's players have actually recommended, rather than what has merely been downloaded a lot.
    *   **Most Popular** lists the mods with the **most downloads** first. The best-known mods for a game, and usually where to start if you're new to modding it.
    *   **Recent** lists the **most recently updated** mods first. Good for seeing what is still being worked on, and poor for finding the established mods, which may not have needed a change in years.
    *   **Nexus Categories** browses one category of the game's catalogue, **most downloaded first**. Choosing it reveals a **Nexus category** dropdown beside the Type — see the next step.
    Every mode except **Search** ignores the search box.
3.  **Nexus Category Filter**: Set **Type** to **Nexus Categories** and a **Nexus category** dropdown appears beside it, listing Nexus's own categories — Armour, Gameplay, Audio, Patches, and so on. Pick one and press **Search**: you get that category's mods with the **most downloaded first**, which is usually what you want when looking into a category you don't know yet. The manager says so when the dropdown appears, since a control arriving mid-toolbar is easy to miss.
    *   The list is built from the categories that **actually have mods for the game you're managing**, most-populated first and with a count beside each, so you see "Armour (1247)" rather than a long list of categories this game has never used.
    *   **The dropdown, and the filter, belong to this one search type.** Choose any other Type and it disappears and stops applying — a filter quietly narrowing your results with no control anywhere to say so is exactly how the language filter used to look like "these mods aren't on Nexus".
    *   If you choose this Type without picking a category, the manager says so and moves you to the category list rather than quietly listing something else.
    *   Your choice is **not remembered between sessions** and resets to "Any category" — a category is a choice about the search in front of you rather than a standing preference. It also reloads when you switch games, since Skyrim's categories are nothing like Stardew Valley's.
    *   This filter does **not** have the language filter's hiding problem: every mod on Nexus has a category, because uploading one requires choosing it.
    *   ⚠️ Not to be confused with the **Category** filter on the **Installed Mods** tab, which sorts your *own* mods by labels *you* assign with Ctrl + J. This one is Nexus's own classification of mods you don't have yet.
4.  **Language Filter**: Use the **Language** dropdown to restrict results to a single language. It defaults to **English**, and your choice is remembered between sessions. The list is built from the languages that actually have mods for the game you're currently managing, with a count beside each — for example "English (16362)". Choose **"Any language"** at the top of the list to turn the filter off and see results in every language. The filter applies to keyword searches and to the All, Trending, Most Popular, and Recent lists alike.
    *   ⚠️ **Important — the language filter hides more than you'd expect.** Nexus Mods only knows a mod's language if its author filled that field in, and **most authors leave it blank**. Those mods are then excluded by *any* language filter, including English, even though they're in English. The effect can be dramatic on newer games: Moonlight Peaks has 80 mods, but only 5 of them declare English, so searching with the English filter finds 5. For this reason, whenever a language filter is in force and it's hiding mods, the manager now **tells you** — "That is 5 of this game's 80 mods…" — so a thin result never looks like the mods simply aren't there. If you're hunting for a particular mod, set **Language** to **"Any language"**.
5.  **What each result tells you**: A result reads as its **name and Nexus ID**, then **"Installed"** if you already have it, then **when you last downloaded it** if you have, then **how many downloads and endorsements** it has, then **how long ago it was last updated**, then its **short description** — for example *"Serena's Grimoire (ID: 23). Installed. You downloaded this 2 days ago. 3,428 downloads, 50 endorsements. Updated 3 weeks ago. Dark magic, dramatic rituals…"*. Everything before the description is there so you can move on to the next result without sitting through a description you've already ruled out.

    The row is in two halves: **what you have** (installed, downloaded) comes first, then **what everyone else thinks** (downloads, endorsements, how recently it was updated).

    The last-updated age is worded the way Nexus words it on the mod page, and the unit grows with the gap: *"5 minutes ago"*, *"5 hours ago"*, *"3 weeks ago"*, *"4 years ago"*. It is the one thing the download count cannot tell you — a mod with a hundred thousand downloads and four years of silence behind it is a very different prospect from the same mod updated last week, and until now the only way to tell them apart was to leave the manager and open the mod's page.

    *   **"You downloaded this 2 days ago"** means the archive is **in this game's downloads folder right now**, waiting — press the Downloads History shortcut to find it and re-install it without fetching it again. It is worded in the second person on purpose, so it can't be mistaken for the mod's *download count* a moment later in the same row.
    *   The manager works this out by reading the downloads folder itself rather than keeping a list, which has two consequences worth knowing. **Everything already in your folder counts**, including mods you downloaded long before this feature existed. And **deleting an archive stops the claim** — the row goes quiet again, so you're never sent hunting for a file you've since cleared out.
    *   A few downloads can't be recognised: ones Nexus handed over with no name at all (a long string of letters and numbers), mods fetched from GitHub rather than Nexus, and archives you placed in the folder yourself. Those simply say nothing, rather than guessing.
    *   **"Installed" comes first because it is the one fact that can end your interest in a row outright** — a mod already in your mods folder needs no further thought, and there is no point reading its downloads or opening its page. Only mods you *do* have are marked; saying "not installed" on every one of a hundred rows would bury the few that matter.
    *   The manager recognises a mod you already have by its **Nexus ID** where it knows it, and otherwise by matching the **name and author** — the same careful rules **Auto Match** uses, so a partial name match only counts when the author agrees too. That second route matters because plenty of installed mods have never been linked to a Nexus page, and those are exactly the ones you'd otherwise re-download by accident. A wrong "Installed" would talk you out of a mod you don't have, so nothing looser is used — which does mean a mod under a very different name may not be spotted.
    *   **The marks keep themselves up to date.** Results are checked when they're fetched, and re-checked every time the manager rescans your mods — so a mod you download and install while your search results are still on screen starts saying "Installed" without you having to search again. Your place in the results list is kept, and if it's the row you're actually sitting on that changed, the manager tells you so.
    *   *Note:* the short description is however much of it Nexus provides, which for a long one is **not all of it** — the API supplies it already cut off at around 240 characters, often mid-sentence. That's Nexus's own limit and nothing can recover the rest from there; press **Ctrl + Shift + I** to read the mod's **full description** instead.
6.  **Results per load**: Use the **Results** dropdown to choose how many results to load at a time (10, 20, 30, 50, or 100). Changing it here applies to your current searches only; to make a number stick between sessions, set **"Search Results per Load"** in **Settings** instead — the Search tab then starts from your saved choice.
7.  **Loading more results**: When more results are available than were loaded, a **"Load more results"** row sits at the very bottom of the results list. Arrow down to it and press **Enter** to load the next batch — the new results are added to the list, your focus lands on the first new one, and the "Load more results" row moves to the new bottom. It isn't counted in the "X of Y" position announcements, so the real results still read "1 of 20", "2 of 20", and so on.
8.  **Search History**: The manager can remember the searches you run so you can browse and repeat them. It is **off by default** — turn on **"Save Mod Search History"** in **Settings** to start recording. Once it's on, open your history with the **History** button beside the search box, or by pressing **Ctrl + Shift + H** anywhere. In the history window:
    *   Choose **"All searches"** or a specific **date** from the **Show** dropdown (most recent first), then press **Tab** to move into the list of terms.
    *   Press **Enter** on a term to run that search again — the manager fills the search box and searches straight away.
    *   Press **Delete** on a term to remove **just that one** — useful for a search you mistyped, which found nothing and would otherwise sit in the list forever. You're asked *"Delete <term> from the search history?"* first; answer **No** and nothing changes. After a deletion the manager tells you how many searches are left and moves you onto the entry that took its place, so you can tidy up several in a row without re-navigating. Every recording of that term goes, so it doesn't reappear.
    *   Press **Clear History** to erase it all (you'll be asked to confirm first).
    *   History is kept **per game**, so a Skyrim session shows only Skyrim searches, Fallout 4 only Fallout 4, and Stardew Valley only Stardew searches.

---

## Downloading Mods from Nexus Mods (Two Ways)

Whenever you're on a mod's page on the Nexus Mods website — whether you got there from the Find New Mods tab, the Accessibility Suite Installer, or the Updates tab — there are **two** ways to download it, and **either one works**. A common misunderstanding is that the only way is "Manual Download" followed by Ctrl+I; in fact the **"Mod Manager Download"** button is usually the easier choice, because the manager then installs the mod for you.

On the mod's **Files** tab, each file has two buttons:

*   **Mod Manager Download (recommended, most automatic):** Find and activate the **"Mod Manager Download"** button. After the download finishes (see "Finishing the download" below), Kinetix Mod Manager comes to the front by itself and asks **"Downloaded [mod]. Install now?"** — press **Enter** (Yes) and it installs automatically. You do **not** need to press Ctrl+I with this method.
*   **Manual Download (you install it yourself):** Find and activate the **"Manual Download"** button. After the download finishes, switch back to Kinetix Mod Manager and press **Ctrl + I** to pick the downloaded ZIP and install it.

### Finishing the download (applies to both buttons)

After you activate either button, Nexus sometimes shows a page that lists the mod's **required mods** at the top:

*   **If the page shows required mods at the top:** find and press **Enter** on the **"Download"** button (the one near that list of requirements). Then on the **next** page, find the **"Slow Download"** button and press **Enter** to start the download.
*   **If there are no required mods at the top:** simply find the **"Slow Download"** button and press **Enter** to start the download.

("Slow Download" is the free option. "Fast Download" is only for Nexus Premium members.)

### Hearing progress while it downloads and installs

Large mods (200 MB and up) can take a while to download **and** to install, so the manager gives you audible progress for both steps. By default it does two things at once: it plays a short tone that **rises in pitch** as the percentage climbs, and it **speaks** the name once — "Downloading *mod name*, 0 percent" — then just the bare numbers after that ("10 percent", "20 percent", and so on). The window title also shows the live percentage.

You choose how much of this you hear with the **"Download and install feedback"** setting in **Settings** (Ctrl + P):

*   **Both** (default) — rising tones and spoken deciles.
*   **Tones** — only the rising tones, no speech.
*   **Speech** — only the spoken percentages, no tones.
*   **Off** — neither. Pick this if you'd rather rely on your screen reader's own progress-bar feedback (for example NVDA's **Progress bar output** beeps), so you don't hear two sets of cues at once.

---

## Reinstalling a Downloaded Mod (Ctrl + Shift + W)

Every mod archive the manager downloads stays in its downloads folder, so you can reinstall one later **without searching Nexus again** — useful if you removed a mod and want it back, or an install didn't take. Press **Ctrl + Shift + W** (or **Mods → Install and Update Mods → Reinstall a Downloaded Mod**).

A list of your downloaded archives opens, newest first, each showing its file name, size, and date. Press **Enter** on one to reinstall it (it installs exactly as if you'd picked the file by hand, with the same progress feedback and, if the mod has options, the FOMOD wizard). Press **Delete** to send an archive you no longer need to the Recycle Bin and free up space — this only removes the download, not the installed mod. The list is per game, so you only see downloads for the game you're currently managing. **The list stays open after an install**, so you can go straight on to the next one; press **Escape** when you're done.

---

## Installing a Download You Skipped (Ctrl + Shift + P)

When a download finishes, the manager asks whether to install it. If you said **No**, the file stays in your downloads folder. Press **Ctrl + Shift + P** (or **Mods → Install and Update Mods → Install a Download Not Yet Installed**) to come back to it.

This list shows **only the downloads that have never been installed**, newest first, each with the mod's name, its version where known, when you downloaded it, and its size. Downloads you did install are left out, even though they are still in the folder. **Ctrl + Shift + W** lists all of them. Press **Enter** to install the selected one, or **Delete** to send it to the Recycle Bin. For Minecraft, the list shows mod `.jar` files.

**You stay in the list while you work through it.** Once a mod installs, it leaves the list and you land on the download that took its place, which is read out. When the last one is installed, the list closes and says so.

**How it tells them apart:** from this version on, the manager notes each download as it installs it. So a download you skipped is always listed, even an optional file from the page of a mod you already have. Downloads from before this version were never noted either way. For those, the manager checks what you have installed. If the same mod is installed at that version or a newer one, the download is left out. If the download is a newer version than the one installed, it's an update you skipped, so it's listed.

A mod you installed and later removed is not listed here, because it was installed. Use **Ctrl + Shift + W** to reinstall it.

---

## Tidying Mod Folder Names

When a mod comes from Nexus, the folder it installs into is named after the download it arrived in — `Hunterborn-7900-1-6-2`, or `Media Keys Fix 92948 1.0.2 2026-08-21T18-58Z Yj6wQRo26`. The manager has never read those names to you: it writes a small `.manager_manifest.json` beside each mod and takes the mod's real name from there. But open the mods folder in File Explorer and you're looking at a wall of serial numbers.

Choose **Mods → Install and Update Mods → Tidy Mod Folder Names** to rename those folders after the mods in them, so the folder on disk reads the same as the row in your list.

*   **You see the whole list first.** Every "this becomes that" opens in a viewer, and nothing on disk changes until you answer **Yes**.
*   **Your setup is carried across.** Mod priority, the file conflicts you settled by hand, the record of which mod deployed which file, every saved profile, and (on The Witcher 3) the game's own `mods.settings` are all rewritten to match the new names in the same pass.
*   **The mods themselves don't notice.** SMAPI, BepInEx and the Witcher's engine all find mods without caring what the folder is called, and Bethesda mods that are already deployed stay deployed — those files are hard links, which follow the file rather than the folder.
*   **Already-tidy folders are left completely alone**, which is most of them and effectively all Stardew mods, since those arrive named by their author.
*   **A disabled mod stays disabled.** The leading dot — or the tilde on The Witcher 3 — is what switches a mod off, and it survives the rename.
*   **Witcher 3 folders keep their `mod` prefix**, which the engine requires and complains about silently when it's missing, so those keep a folder-style name (`modBrothersInArms`) rather than the spoken one.
*   If two mods want the same folder name, the second becomes "SkyUI (2)"; a folder that is already correctly named is never taken away from the mod that has it.
*   A folder that can't be renamed — because it's open in Explorer, or a file inside it is in use — is left exactly as it was and named in the summary at the end. Nothing is left half-done.

One thing worth expecting: the new names come from the mod's page on Nexus, so a few are more correct than the folder was. `NoStamina-SKSE` is really *Unlimited Stamina - NG*, and `Puzzle Pillar Auto-Unlock` is really *Puzzle Pillar Auto-Solve*.

This is the same convention **Mod Organizer 2** uses — a readable folder per mod, with the mod id and version in a sidecar file. (**Vortex** takes the opposite approach: its folders keep the full Nexus suffix and it hides the suffix inside its own interface.)

---

## Mod Notes

You can attach a personal, free-text **note** to any installed mod — a reminder to yourself such as "keep disabled until year 2" or "conflicts with the lighting mod". The note is spoken as part of the mod's entry whenever you select it in the **Installed Mods** list, so you're reminded exactly when it matters.

1.  Select a mod in the **Installed Mods** list.
2.  Press **Ctrl + Shift + O**.
3.  Type your note and press **Enter**. If the mod already has a note, it's pre-filled so you can edit it.
4.  The manager confirms with "Note saved for [mod]." From then on, selecting that mod reads its note at the end of its entry.

To **clear** a note, open it with **Ctrl + Shift + O**, delete all the text, and press **Enter**; the manager asks you to confirm before removing it (so cancelling never wipes a note by accident). Notes are saved with your settings and stay with the mod across sessions.

---

## Mod Changelog and Full Description

For any mod that comes from Nexus, you can pull two things straight from its Nexus page into a readable, scrollable window — without opening a browser. Both work on the selected mod in the **Installed Mods**, **Updates**, or **Find New Mods** list, and both read the content aloud when they open (press **Escape** to close, or arrow through the text at your own pace).

*   **Ctrl + Shift + G — Changelog:** the mod's version history, newest version first, so you can see exactly what changed — handy before deciding to update, or after, to know what you got.
*   **Ctrl + Shift + I — Full Description:** the complete write-up from the mod's Nexus page, not the short one-line summary. The page's formatting is converted to plain text so it reads cleanly.

The mod page's formatting (bold, headings, HTML, and so on) is converted to clean plain text so it reads naturally.

**Opening links.** If a changelog or description contains links, the link's address is shown right after its text, and you can open it: arrow to a line that has a link and press **Enter**. The manager asks "Open this link in your web browser?" with the address, and opens it on **Yes** — exactly like pressing Enter on a log line that has a link. When a page has links, the viewer says so when it opens.

Both need you to be connected with your Nexus API key, and both fetch live from Nexus, so give them a moment on a slow connection. If a mod has no Nexus id (a manually installed local mod), there's nothing to fetch and the manager tells you so.

---

## Endorsing Mods

Endorsing a mod on Nexus Mods is a quick way to thank its author — endorsements are the main way authors see that people value their work. Kinetix lets you do it without leaving the app.

1.  Select a mod that came from Nexus in your **Installed Mods**, **Updates**, or **Find New Mods** list.
2.  Press **Ctrl + Shift + E**.
3.  The manager checks whether you have already endorsed it, then asks the matching question — **"Endorse [mod] on Nexus?"** if you haven't, or **"Remove your endorsement from [mod]?"** if you have. Press **Yes** to confirm or **No** to cancel; nothing happens until you confirm.
4.  The result is spoken, for example "Endorsed [mod]."

A few things to know:

*   You must be **logged in** with your Nexus API key, since endorsing is tied to your account.
*   Nexus only lets you endorse a mod you have **downloaded and used for a short while**. If it's too soon, the manager says so rather than failing silently.
*   You can't endorse your **own** mods — Nexus reports this and the manager passes it along.
*   The shortcut **toggles**: press it again on a mod you've already endorsed to withdraw the endorsement (this is called "abstaining" on Nexus).

---

## Checking Your Nexus API Requests

Nexus Mods limits how many API requests each account can make per hour and per day. Most people never come close, but during heavy use — a big Collection install, or checking updates on a very large load order — it can be handy to know where you stand.

Press **Ctrl + Shift + A** anywhere to hear your remaining requests, for example **"212 Nexus API requests remaining this hour, 2160 remaining today."**

This costs **no request of its own**: the manager remembers the counts Nexus reports on every normal call (logging in, checking for updates, and so on), so it simply tells you the most recent figures. If you have just started the app and haven't made a call yet, it will say the counts aren't known yet — check for updates or connect first, then try again. You must be logged in with your API key.

---

## Installing Configurable Mods (FOMOD Installer)

Some mods — especially larger Skyrim Special Edition and Fallout 4 mods like **Immersive Sounds Compendium** — don't simply unpack into your game. They ship a built-in **installer (called a FOMOD)** that asks you to choose options, such as which features, patches, or sound sets you want. Kinetix Mod Manager runs these in a fully keyboard-driven, screen-reader-friendly **wizard**.

When you install such a mod (with **Ctrl + I**, or after a Mod Manager Download), the wizard opens automatically. You don't have to do anything special — and a mod with no options simply installs as before.

Using the wizard:

*   **Read the options**: Each step presents a group of options as **radio buttons** (pick one) or **checkboxes** (pick any number). Arrow or Tab to each one and its **description is spoken**, so you know what it does before choosing.
*   **Make your choices**: Press **Space** to select a radio button or check/uncheck a checkbox; the manager announces "checked" or "unchecked" as you toggle. Sensible defaults are pre-selected, so if you're unsure you can just move forward.
*   **Required and unavailable options**: Options the mod marks as **required** stay selected, and options that are **not usable** with your current setup can't be selected — but both are still read aloud with their status, so nothing is hidden from you.
*   **Track your progress**: The window title shows where you are, such as "Step 2 of 6" (read it any time with your screen reader's title command, e.g. **NVDA + T**).
*   **Move through the steps**: Tab to the **Next** button to continue; some choices reveal or hide later steps, and the button reads **Install** on the final step. Press **Cancel** or **Escape** at any point to stop without installing anything.

Your selections are installed exactly as a sighted user's would be, including any extra files the mod adds based on the options you picked.

---

## Game and Mod Controls Viewer (Ctrl + H)

Press **Ctrl + H** at any time to open the **Game and Mod Controls** viewer for the active game. It gathers, in one place, the keyboard and gamepad controls you actually have — so you can look up "what does this key do" without leaving the manager.

It is a single **drill-down list**:

*   **Up / Down Arrow**: Move through the current list.
*   **Right Arrow or Enter**: Open the selected item when it's a group — for example a mod, or a "Keyboard Controls" / "Gamepad Controls" category — moving into its deeper list.
*   **Left Arrow or Backspace**: Go back up to the previous list.
*   **Ctrl + E**: Edit the selected mod's configuration file, when it has one.
*   **Shift + F1**: Hear help for this window. **Escape** closes it.

What it shows:

*   **Base Game Controls (Vanilla Defaults)**: the standard controls for the active game.
*   **Each installed mod's own controls**: read straight from what the mod documents — its README or guide, or its config file — captured when the mod was installed. Because they come from the mod itself, they stay correct even when the mod updates; nothing is hard-coded.
*   **MCM keybinds (Skyrim & Fallout 4)**: mods that set their keys through the **Mod Configuration Menu** (such as Fallout 4 Access and Extended Dialogue Interface) have those keybinds read directly from MCM, showing the real key each action is bound to — including any you've changed in-game.
*   **Moonlight Peaks mod keys**: read from each mod's BepInEx configuration file, showing the key it is currently set to and what that key does.
*   **The Witcher 3's own keys**: read straight from the game's `input.settings`, so the list is always **your** bindings, including any you have remapped in the game's options. They are offered two ways — see below.

### The Witcher 3: two ways through your controls

The Witcher 3 has more bindings than any other game the manager handles, and it stores them in a way that makes a plain list unreadable. It declares its controls **once per situation** — while exploring, in combat, on horseback, in a boat, swimming — and forty-two situations is not a misprint. Gather those by key and the interact key answers with one sentence seventy-five items long, beginning *"E: Bury Body, Place Trophy, Hide In, Dispose Paint, Take Paint Purple…"*. Every word of it is true, and none of it is usable.

So the game's entry opens onto **two ways in**, and you pick the question you actually have:

*   **By situation** answers *"what can I press right now?"* — **Exploring on foot**, **In combat**, **On horseback**, **Sailing a boat**, **Swimming**, **Casting signs**, **Menus and panels**, and so on. Open one to get its keys.
*   **By key** answers *"what does this key do?"* — every key once, in alphabetical order, and opening one lists each thing it does with the situation it applies in.

Neither is a summary: both hold every binding you have. A few things worth knowing about how they read:

*   **A key that does one thing is a line; a key that does several is a group.** So the interact key is one row saying **"E, 79 things it does"**, and its seventy-five interactions are there for you when you open it — rather than read at you when you pass by.
*   **Controls that apply everywhere are listed once, under their own heading.** The game copies its general bindings into every situation that uses them — the same seventy-five interactions are written out again under Exploring, Combat, Swimming, Diving and both Ciri contexts. Listing them each time would bury whatever is genuinely particular to swimming, so they sit under **Interacting with things**, **Moving around** or **Menus and panels**, and a situation is left holding what is true there and nowhere else.
*   **A mod's keys are the mod's entry, not the game's.** A Witcher 3 mod declares its keys in the game's own `input.settings`, on the same keys the game uses — which is how the Home key came to read *"Toggle Hud, Announce, Keys First, Hist First, Map Announce"*: five unrelated things from two different programs on one line. **WitcherAccess** now has its own entry with its 39 controls, one per line, and the game's list has only the game's.
*   **The game's internal contexts are not shown.** A handful of its forty-two are engine scaffolding rather than situations — debug keys, an empty context, a scene-loading placeholder — and a row named after one is a row you can do nothing with.

### The Witcher 3: what's different

The Witcher 3 needs no mod loader — no SMAPI, no script extender, no BepInEx. The game loads the contents of its own `mods` folder, and that is all a mod normally needs.

*   **A mod is a folder called `mod` something.** That prefix is not a convention, it is the rule the game loads by: a folder that doesn't start with `mod` is ignored, silently, with no error and no clue in-game. When you install a mod from an archive that unpacks to some other name, the manager renames it so it will actually load.
*   **Disabling a mod puts a `~` in front of its folder name**, which takes it out of the running for exactly that reason. Enabling takes the `~` back off. Your files are never altered or re-downloaded.
*   **The game keeps its own list of mods** in `Documents\The Witcher 3\mods.settings`, and that list is what the game's own mod menu shows. The manager keeps it in step, so the two never disagree about what is switched on. Deleting a mod removes it from that list too.
*   **Mod names are read out readably.** A Witcher mod folder carries no name, author or version of its own — the game never needed them — so the folder name is all there is to go on. `modRandomEncountersReworked` is read as "Random Encounters Reworked" and `mod_sharedutils_glossary` as "Shared Utils: Glossary".
*   **No version is invented.** Where a mod says nothing about its version, the list says **"version unknown"** rather than making one up. A made-up 1.0.0 looks like something you can act on, and it would make every such mod appear older than any release on Nexus.
*   **A framework that ships as a dozen folders is shown as one.** Many Witcher mods install shared-utility folders alongside themselves — Random Encounters Reworked brings ten. Folders sharing a `mod_family_*` name are collapsed into a single group you can expand with Right Arrow, so they take one row in the list instead of ten.
*   **Mods that install files elsewhere in the game** — a `dlc` folder, or the menu XMLs under `bin` — have those parts put where they belong and **remembered**, so uninstalling the mod takes them with it instead of leaving them behind.
*   **Mods that ship as an installer program.** Some mods, the accessibility mod among them, install themselves by running a setup program, because what they install goes to several places at once that no file copy would get right. The manager downloads and unpacks the archive, finds the installer, **asks you before running anything**, and waits while you go through it. When the installer closes, the manager picks up again and adds the mod to your list like any other. Use **Mods → Install Mod From File** with the release archive.
    *   **The manager records what the installer did**, by comparing the game folder before and after, so it knows which files outside the mod folder belong to that mod.
    *   **Deleting the mod offers to run its own uninstaller** if it registered one — that uninstaller holds the installer's list of every file it wrote, which is more than the manager could work out for itself. Saying no still removes the mod folder.
    *   **Disabling the mod moves those outside files out of the game** into a holding folder called `_KinetixDisabledMods`, and enabling puts them back unchanged. This matters: a plugin sitting beside the game's executable is loaded by the game whatever the mod folder is called, so renaming the folder alone would leave a "disabled" mod still running.
*   **Editing the game's settings**: **Mods → Edit Game Configuration** opens `user.settings` and `input.settings` in the accessible settings editor. They are INI files despite the extension.
*   **Saves** live in `Documents\The Witcher 3\gamesaves` and are handled by the Save Manager.
*   **The log tab** shows the logs that mods write beside the game's executable in `bin\x64` — the game itself keeps none.
*   **Launching with F5 skips the game's launcher.** The Witcher 3 normally starts through `REDprelauncher.exe`, a graphical window that a screen reader cannot use, and that launcher is also what chooses between the DirectX 11 and DirectX 12 builds. The manager starts `bin\x64\witcher3.exe` directly instead — the **DirectX 11** build, which is the one the accessibility mod is developed against — wherever your copy of the game is installed.

### The Keybind Reader (Moonlight Peaks)

Stardew Valley, Skyrim and Fallout 4 all use fixed, published controls, so the manager simply knows them. **Moonlight Peaks does not work that way.** It decides its key bindings while the game is running, using a system called Rewired, and stores them nowhere on your computer that another program can read. If you remap a key in the game's own options, that change lives inside the game's save data in a form no outside tool can make sense of.

That leaves the manager two choices, and it uses both:

*   **A snapshot of the game's standard keys ships inside the manager.** This is what you see straight away — before you have installed a single mod, and even if you have never launched the game. The controls list calls it **"Game Controls (Default Bindings)"**.
*   **The Keybind Reader reads your real keys.** This is a very small mod (15 KB) that does one thing: while the game runs, it writes your current key bindings out to a file the manager can read. It has no keys of its own, no menus and no effect on how the game plays. With it installed, the controls list shows **"Game Controls (Your Bindings)"** and tells you the date they were read — so you always know whether you are looking at your keys or the standard ones.

**You are asked before anything is installed.** The first time you load a Moonlight Peaks session with BepInEx present, the manager offers to install the Keybind Reader and explains why. Your answer is remembered either way, and you are never asked again. Nothing is downloaded — the reader is already included with the manager — and if you say no, everything still works using the standard keys.

After you say yes, **play the game once**: the reader writes its file while the game runs, so your own bindings appear in the controls list from then on. Remapping a key later is picked up the next time you play. If you ever delete the reader from your mods, the manager takes that as your decision and does not put it back.

---

## Searching a Document (Ctrl + F)

The **User Manual** (**F1**), the **Change Log** (**F2**) and the **Mod Documentation** viewer (**F3**) are all the same navigable window, so they all search the same way.

A drill-down list is a good way to read a document whose shape you already know, and a poor way to answer *"where does it say anything about F4SE?"* — the list of sections can only offer the headings someone thought to write. **Ctrl + F** searches every line of every section at once, whichever section you happen to be in.

*   **Ctrl + F** asks what to find. Type a phrase and press **Enter**. Capital letters don't matter.
*   You get a **list of matching lines**. Each one reads as the matching text followed by **the section it is in** — *"…the installed F4SE must match your game's version — in Launching the Game, Script Extender Check"* — so you can tell the results apart and know where each will take you before you go. Long lines are shortened around the phrase; the whole line is one keypress away.
*   **Enter** on a result **takes you there**: the viewer opens the section holding it and puts the cursor on that exact line in the text on the right, then reads out which result it is and what the line says. **Left Arrow** still walks back out of the section a level at a time, exactly as if you had opened it yourself.
*   **F3** moves to the **next** result and **Shift + F3** to the **previous** one, without going back to the list. They wrap around at either end and say so.
*   **Escape** on the results list goes back without moving, but **keeps the results** — F3 still steps through them.
*   **Ctrl + F** again searches for something else, replacing the previous results.

**Following a link (all three viewers).** In the read-only text on the right, press **Enter** on a line that contains a web address and the manager asks whether to open it in your browser. If the line holds **more than one** address you get a short list to choose from, exactly as a game-log line with several links does.

For the **Mod Documentation** viewer (**F3**) this means a link now reads as its words followed by its address — *"the Stardew Access Nexus page (https://www.nexusmods.com/stardewvalley/mods/16205)"* — because a document read line by line has no other way to offer a link at all. Links that point **inside** the document (to another heading) or at a file in the mod's own source repository keep just their words as before: nothing here could open those, so reading their addresses aloud would be noise for nothing.

**Knowing which sections open.** When you land on a list entry that has sub-topics, it now tells you so and tells you the keys — *"3 of 21, has sub-topics. Press right arrow to open, left arrow to go back."* — the same hint a mod group carries in the installed mods list. The **Game and Mod Controls** viewer (**Ctrl + H**) says the same thing on its groups.

---

## Mod Documentation Viewer (F3)

Press **F3** at any time (or choose **Help → Mod Documentation**) to open the how-to-play documentation for the accessibility mod that matches your current game — **Stardew Access** for Stardew Valley, **Skyrim Access** for Skyrim Special Edition, or **Fallout 4 Access** for Fallout 4. This saves you hunting for a README on your computer or searching for it on GitHub.

It opens in the **same navigable window as the User Manual**:

*   The window starts on a short list of the available documents (the **chooser**). **Up / Down Arrow** move through it.
*   **Right Arrow or Enter**: Open the selected document — or, once inside, open a section that has sub-topics — moving into its deeper list.
*   **Left Arrow or Backspace**: Go back up to the previous list.
*   **Tab**: Move into the read-only text on the right and read the selected section line by line. **Shift + Tab** returns to the list.
*   **Ctrl + F**: Search the whole document for a phrase, then **F3** / **Shift + F3** for the next and previous result. See [Searching a Document](#searching-a-document-ctrl--f).
*   **Shift + F1**: Hear help for this window. **Escape** closes it.

How it stays current and works offline:

*   A copy of each guide **ships with the manager**, so the viewer opens instantly and works with no internet connection.
*   In the background it also **refreshes from the source** — the live GitHub documentation for Stardew Access, or (when you are logged in to Nexus) the mod's current Nexus page for Skyrim Access and Fallout 4 Access. The refreshed copy is shown the **next** time you open the viewer.
*   For exact, up-to-the-minute keybindings of an installed mod, the **Game and Mod Controls** viewer (**Ctrl + H**) reads the live keys from the mod's config/MCM; the Mod Documentation viewer is the broader "how do I use this mod" guide.

F3 is the default shortcut and can be remapped in **Settings → Shortcut Manager** (the action is named "ModDocs").

---

## Minecraft (Java Edition)

Minecraft works differently from every other game here, in ways worth knowing before you start.

### You do not need the Minecraft launcher

Press **F5** and the manager starts the game itself, with Fabric and your mods loaded, going nowhere near the
Minecraft launcher.

This is the main reason Minecraft support exists. In the launcher, **choosing an installation and launching a
world are two separate things, and the second quietly overrides the first**. You can select the Fabric
installation correctly, then launch a world from the home screen, and get an unmodded game — because the world
tile remembers which installation it was first played with. Nothing tells you. The game starts, plays
perfectly, and simply never speaks.

It plays as you — your own username and your own character — so your worlds, inventory and advancements are
exactly where you left them. The manager also **installs Minecraft itself** when a version you want is not on
the computer, and repairs one that is missing pieces — see "Installing and repairing Minecraft itself" below.

### Signing in, and online or offline play

Minecraft starts **offline** unless you say otherwise, and offline covers singleplayer completely. What it
does not cover is anything that checks a live sign-in: servers, Realms and custom skins.

Open the **Mods** menu, choose **Game and Maintenance**, then **Minecraft Account**. The menu entry itself says
the mode — for example "Minecraft Account: online play, connected as Sean" — and the screen it opens starts
with the same line, then explains what your next launch will do. It has the buttons that change either.

With online play on, the manager **checks your sign-in with Microsoft when it opens Minecraft**: you hear the
connected sound and "Connected to Minecraft as" your name, and the title bar keeps saying so. In offline play
the title bar says "Offline play" instead. If the check
fails you hear the error sound and why — nothing is decided yet, because F5 will ask whether to start offline.
Switching to online play checks straight away; switching to offline, or signing out, plays the disconnected
sound.

**Signing in** is done with a short code, once:

*   Press **Sign in with Microsoft**. The manager is given a code and says it one word per character — "Alfa,
    Bravo, One" — so there is nothing to squint at or guess.
*   Press **Open the sign-in page** and your browser opens at microsoft.com/link with the code already copied,
    ready to paste. You can also read the code from the screen, or have it said again.
*   Sign in there as usual. **The manager never sees your password.** When you finish, this screen closes by
    itself — there is nothing to press when you come back.
*   Kinetix then asks whether to play online from now on. Either answer is fine; you can change it whenever you
    like.

**Switching modes** is one button in the same place, in either direction, at any time. **F5 always says which
mode it is starting in**, so the difference is never silent.

Your saved sign-in is encrypted on this computer so that it is useless on any other, and it renews itself
quietly — Minecraft's own sign-in lasts about a day. If it ever stops being accepted (a changed password, or a
long time away), Kinetix says so and asks you to sign in again. **Sign out** removes it and goes back to
offline play; your worlds, mods and characters are untouched.

If an online launch cannot sign in — no internet, say — the manager tells you why and asks whether to start
this session offline instead. It never switches modes behind your back.

You do not need the official Minecraft launcher for any of this. If you have signed in to it before, Kinetix
can read who you are from it and play offline as you without any sign-in at all.

### Installing Fabric

Fabric is Minecraft's mod loader, and nothing loads without it. Open the **Accessibility Suite Installer** and
choose Install — the manager sets it up itself. There is no installer program to download and no graphical
window to get through.

It installs Fabric for a Minecraft version it knows your accessibility mod supports, rather than simply the
newest one. Fabric is usually ready for a new Minecraft version weeks before the mods are, and installing for
a version with no mods built for it gives you a game that starts perfectly and stays silent.

### Installing and repairing Minecraft itself

The manager downloads Minecraft for you when it needs to. This is what every third-party launcher does: the
game's files all come from Mojang's own servers with published checksums, and each one is verified as it
arrives.

It happens in two situations:

*   **A version you do not have.** Moving to a new Minecraft version, or setting the game up for the first
    time, no longer requires you to open the official launcher and play the version once first. You are told
    how many files and how many megabytes before anything starts, and asked whether to go ahead — a whole
    version is several hundred megabytes and can take a long time on a slow connection. Progress is spoken as
    it goes.
*   **A version you do have, but which is missing pieces.** Before every launch the manager checks the files
    the game loads, not only the ones that would stop it starting. A Minecraft install can end up missing
    individual sounds, language files or textures, and when it does the game starts, plays and looks
    completely normal — so nothing tells you. On the machine this was found on, four files were missing and
    all four were sounds. Anything absent is fetched before the game starts, and you are told how many.

**Java is fetched too, when a version wants one you have not got.** Minecraft has changed Java runtime twice
in recent memory, and the version it asks for is not something most computers have. You are asked first, and
told the size. It is installed alongside your game, inside your `.minecraft` folder, and nothing else on the
computer is changed.

**None of this needs an account or a password.** Signing in stays the launcher's job; the manager only reads
who you already are.

**Nothing is downloaded when nothing is missing.** The check reads what is already on the disk and only asks
Mojang when something is genuinely absent, so it costs nothing on a normal launch and the game still starts
with no internet connection at all. If a download is interrupted, whatever arrived is kept — trying again
picks up where it stopped rather than starting over.

### If the game does not start

Minecraft needs a set of its own files for each version, quite apart from your mods. The manager starts the game
itself, so **it checks those files are there and fetches any that are missing before launching**, verifying each
one against the checksum Mojang publishes. This matters most right after a Minecraft update, when files for the
new version may never have been downloaded.

If something still fails, the game's own log is the place to look: **F5 launches the game and the manager reads
its log**, and the log itself is in `.minecraft\logs\latest.log`. Two kinds of failure read very differently:

*   **"Incompatible mods found"** lists mods that cannot run on your Minecraft version. Fabric refuses to start
    until they're switched off — see below.
*   **A Java error naming a class** (`NoClassDefFoundError`) is a missing file rather than a mod problem.
*   **"Errors in the currently selected data pack(s)"** when opening a world is a mod too, but a quieter one: it
    loaded because it never said which Minecraft version it was for, and then its content couldn't be read. The
    log names the pack, and **Check My Setup** names the mod, because it asks the catalogue what your installed
    build supports rather than trusting the mod's own word.

### Keeping Fabric and Minecraft up to date

An update check for Minecraft looks at two things besides your mods, and each appears in the **Updates** list as
its own row:

*   **Fabric Loader** — a newer build of the loader for the Minecraft version you're already on. Press **Enter**
    to install it. Your mods are unaffected, because the Minecraft version doesn't change. Close the Minecraft
    launcher first: it rewrites its own settings when it closes and would undo the change.
*   **Minecraft** — a newer Minecraft version. This one only appears **once your accessibility mod has a build
    for it**, since moving before that gives you a game that starts perfectly and never speaks.

When a check finds a new Minecraft version, you're also **asked about it directly**, once the check has finished
speaking. The question names the version you're on and the version available, because a new Minecraft version
decides whether every mod you depend on still loads. Saying yes leads to the move described below, which asks
again before changing anything. Saying no leaves the row in the Updates list for whenever you want it, and you
won't be asked about that same version again for the rest of the session.

Pressing **Enter** on the Minecraft row moves your setup to the new version. Before anything is changed, you're
told what will happen to each of your mods, and asked whether to go ahead. Saying yes downloads the new
Minecraft version if you do not already have it, installs Fabric for it, and updates every mod that has a build
for it. You do not need to play the new version in the official launcher first.

**A mod that can't run on the new version is switched off as part of the move**, and the manager names them all,
both before and afterwards. This is not tidiness: **Fabric refuses to start the game at all** if a mod is built
for another Minecraft version, listing every offender. Switching them off is what keeps the game launching.
Nothing is deleted — switch one back on in your Installed Mods list once its author releases a build.

Some mods need no new build. A mod asking for "26.2 or newer" is happy on 26.3, and the manager can tell those
apart from a mod demanding 26.2 exactly by reading what each one declares, so it only switches off the ones that
would really stop the game.

If a mod you depend on is in that list, say **No** and stay where you are. Your old Minecraft version and its
Fabric installation both remain on disk.

**Update All never moves you to a new Minecraft version.** That row is left for you to choose deliberately; Update
All takes the Fabric loader and your mods.

### Two accessibility mods

Minecraft has two, and you pick one:

*   **United Minecraft** — the newer of the two, updated more often.
*   **Minecraft Access** — older, and what many blind players have used for years.

The suite asks once and remembers. Everything either one needs is installed for you; you are never asked about
Fabric API or anything else underneath.

If you already have one of them installed, the manager notices and uses that, and does not offer to install the
other over the top. Running both at once will probably say everything twice, so if you want to try the other
one, the tidiest way is a **profile** for each: save a profile with one enabled and the other disabled, another
the reverse, and switch between them. Your worlds are untouched either way — nothing moves.

### Finding mods

Minecraft's mods come from **Modrinth**, not Nexus Mods, so **no Nexus API key is needed** and the key box is
hidden while a Minecraft session is open.

Search works as it does for any other game. Press **Enter** on a result and you are asked what to do:

*   **Download and install** — fetches the mod and puts it straight into your mods folder.
*   **Read the full description**.
*   **Open the mod's page** in your browser.

Only mods with a build for the Minecraft version you are on are offered. A mod built for another version
installs cleanly and then loads nothing at all.

Update checking works differently and rather better here: Modrinth recognises a mod by the file itself, so
nothing has to be linked by hand and there is no auto-matching to run.

### Enabling and disabling

A Minecraft mod is a single `.jar` file rather than a folder. Disabling one renames it so Fabric skips it, and
enabling puts the name back. Nothing is moved or deleted.

### Did my mods actually load?

Because an unmodded Minecraft looks and sounds exactly like a modded one that failed, **Check My Setup** reads
the game's own log and tells you plainly which happened the last time you played — including how many mods
Fabric accepted. If the answer is that they did not load, it says so and tells you what to do about it. The
full log is on the **Log** tab.

---

## Accessibility Suite Installer
Each supported game needs a set of foundation mods to be accessible (its mod loader plus the screen-reader and helper mods). The **Accessibility Suite Installer** gathers them all in one place. Open it from the **Mods** menu → **Game and Maintenance** → **"Install [Game] Accessibility Suite"** (the exact name reflects the active game).

The dialog shows a **status list**, with one line per required mod telling you whether it is **Installed** or **Not Installed**. The suite differs per game — for example SMAPI and Stardew Access for Stardew Valley; SKSE64, the Address Library, SkyUI, and others for Skyrim Special Edition; and F4SE, the Address Library, the Mod Configuration Menu, and Fallout 4 Access for Fallout 4.

**How "installed" is decided.** The script extender counts as installed when **its files** are in the game folder — the versioned runtime file (`skse64_1_6_1170.dll`) or the loader (`skse64_loader.exe`). The runtime file *is* the script extender; the loader is one convenient way to start it. This matters on **GOG** especially, where many players load SKSE through the SSE Engine Fixes preloader and have no loader at all: judging by the loader alone reported their working SKSE as missing. Launching is unaffected — the manager starts the game through the loader where there is one and through the game's own program where there is not.

**Which script extender have I got? (Skyrim & Fallout 4)** SKSE and F4SE install as loose files in the game folder rather than as mods, so they never appear in your mod list — and this status list is where you can ask what is installed. The version reported is the version of the file that will **actually load for your game build**, which is not always the newest file present: installing a newer script extender replaces the loader but only adds its own build's file, leaving the previous one in place for the game you are still running. Their line gives you both numbers that matter: *"F4SE (Script Extender): Installed, version 0.7.9, built for game 1.11.240, which matches your game."* The first is the script extender's own version, as written on its download page; the second is the game build it was compiled against, which is what decides whether it loads at all. If the two don't line up the line says so — *"built for game 1.11.221, but your game is 1.11.240, so it will not load — reinstall it"* — and pressing **Install Missing Mods** puts the matching build in place. The line may also mention files "left by earlier installs and now unused": each version of the script extender ships its own file named for the game build it serves, and installing a new one leaves the previous file behind. That is normal and harmless — the script extender only ever loads the file matching the game you are running — and the manager mentions it only so a file you never chose to keep isn't a mystery.

There are two ways to get the missing mods:

1.  **Install everything at once**: Tab to the **"Install Missing Suite Mods"** button and press Enter or Spacebar. The manager downloads and installs every missing mod for you. (If you are a Nexus Premium member, Nexus mods install automatically; otherwise the manager opens each mod's page so you can download it manually.) This is the quickest option, but with several mods it can mean a lot of pages opening at once.
2.  **Get them one at a time, at your own pace**: Select a mod in the status list and press **Enter**. The manager opens just that one mod's download page — the **Files** tab on Nexus Mods, or the official site / GitHub releases page for other mods. From there you can download it with **either** the **Mod Manager Download** button (the manager will then offer to install it for you) **or** the **Manual Download** button (then press **Ctrl + I** to install the ZIP) — see **"Downloading Mods from Nexus Mods"** above for the full steps. Repeat for each mod whenever you're ready. This is handy when several mods are needed and opening them all together would be overwhelming.

You can mix the two approaches freely, and you can re-open the installer at any time to check what's still missing.

### Getting the right file, and getting all of it

Some mod pages hold more than one download, and picking the wrong one — or missing one entirely — leaves you with a mod that installs perfectly and then does nothing. The manager now sorts both cases out for you.

**The right build of SKSE and F4SE.** These ship a separate file for each version of the game, and installing one built for a different version means it silently never loads. This bites hardest if you own Skyrim on **both** Steam and GOG, because the two are different versions — the GOG release is a slightly higher build number than the Steam one, and each has its own SKSE file on the same Nexus page. The manager reads the version straight off your game's own program file and fetches the file that matches **the copy you are in**, so the GOG copy gets the GOG build without you having to know any of this.

**The store decides it outright, not just the version number.** Where a mod page offers a separate GOG file, the Steam one is never offered to a GOG copy and the GOG one is never offered to a Steam copy — whatever the version numbers say. This matters because a version number has to be read off a program file, and on a machine with both copies installed it can be read off the wrong one; when that happened, the Steam build looked like a perfect match and was installed into a GOG game, where it does nothing. The same rule now applies when a mod is **updated**, not only when it is first installed. Pages that offer one build for everybody — F4SE's does — are unaffected.

**SSE Engine Fixes and its preloader.** Its main plugin installs like a normal mod. For a long time it also needed a second file from the same page — the **preloader** — which goes loose in the game folder next to the game's program file rather than into the mods folder, and without which the main plugin cannot load. It was easy to miss, because nothing tells you it exists unless you read all the way down the Files tab. The manager fetches both halves and puts each where it belongs.

**From Skyrim 1.7.99 the preloader is no longer part of that mod.** The script extender learned to load the plugin early by itself, so there is no second download and nothing to put in the game folder. The manager checks your game's own version and asks for the preloader only on the versions that still need it — **1.5.97 and the 1.6 line still do**, and there the plugin will put up an error and close the game without it. If you already have the preloader from before your game updated, see the next paragraph.

**Clearing out a preloader your game has outgrown.** If `d3dx9_42.dll` is still sitting in your Skyrim folder on 1.7.99 or newer, **Check My Setup** (Ctrl + Shift + K) reports it under **"No longer used"**. It is not doing any harm — nothing loads it any more — so you can leave it. **Enter** on that line lists the files (`d3dx9_42.dll`, `tbb.dll`, `tbbmalloc.dll`), asks first, and sends them to the **Recycle Bin** rather than deleting them, so you can put them back if you ever go back to an older version of the game. It is offered only once **both** are true: your game is past the version where the script extender took over the preloading, *and* the Engine Fixes you have installed is the release that stopped needing it. An older Engine Fixes still calls that file, so on that setup nothing is offered — what is needed there is the mod's own update.

**If you are not a Nexus Premium member**, the manager cannot download for you — that restriction is Nexus's, not the manager's. What it does instead is remove the guesswork: it works out exactly which file you need, opens the page showing **only that file**, tells you what the file is called and where it goes, and then installs it properly when it comes back. Use the **Mod Manager Download** button and the file returns to the manager on its own. When a mod comes in two parts you are walked through them one at a time.

**If a mod is already half-installed**, **Check My Setup** (Ctrl + Shift + K) reports it — for example *"SSE Engine Fixes is installed but incomplete: Part 2 — the preloader is missing"* — with **Enter** on that line to fetch the missing part. This works no matter how the mod arrived: installed by hand, brought in by a Collection, or carried over by the Mod Organizer 2 import. A part your game version no longer needs is never reported as missing.

**Uninstalling the script extender (Skyrim & Fallout 4):** SKSE and F4SE install into the game folder itself rather than as normal mods, so they don't show up in your mod list — the status list above is where to look up which version you have. To remove one cleanly, open the **Mods** menu → **Game and Maintenance** and choose **"Uninstall Script Extender (SKSE/F4SE)"**. The manager removes the files it installed (asking you to confirm first). Remember that anything relying on the script extender — including the Mod Configuration Menu — stops working until you reinstall it, so only do this if you mean to.

---

## Suggested Mods

The **Accessibility Suite** above is the set of mods a game *needs* before it can be played by ear at all. **Suggested Mods** answers a different question: once the game is playable, which mods remove a wall that is still there?

These are two separate lists with two separate promises, and it is worth keeping them straight. Everything in the suite is required. **Nothing in the Suggested Mods list is required** — every entry is optional, several of them make the game easier as well as more reachable, and each one tells you *why* it is there so you can decide for yourself rather than taking the list's word for it.

Open it from the **Mods** menu → **Suggested Mods** → **"Suggested Mods for This Game"**. It has no keyboard shortcut by default, but you can give it one in **Settings → Shortcut Manager** (the action is named "SuggestedMods").

### Reading the list

The list is grouped under **category headings**, each saying how many mods are under it — for example *"Can't be done by ear, 3 mods"*. The mods for that category follow it, and then the next heading. If a category has nothing in it for this game, it gets no heading at all.

Grouping this way means you can skip a whole group in a couple of presses when it isn't what you're after, rather than hearing the same category name repeated at the start of every row.

Each mod's row reads as its **name**, then whether it is **Installed** or **Not installed**, then the reason it was suggested. For example:

> *Auto-Fishing. Not installed. The fishing minigame is a moving bar with no audio cue.*

The installed check is made on the strongest evidence available — the mod's Nexus ID first, then its manifest ID, then its name — so a mod you installed by hand is still recognised.

Two keys work on a mod:

*   **Enter** — install it. The manager asks you to confirm first. If you are a Nexus Premium member it downloads and installs on its own; otherwise it opens the mod's **Files** page and installs it when the download comes back (the same arrangement described under the Accessibility Suite, and for the same reason — Nexus does not give the API a download link for a free account). Pressing Enter on a mod you already have simply says so.
*   **Ctrl + G** — open the mod's page, so you can read about it before deciding. This is the same key that opens a mod's page everywhere else in the manager.

Pressing **Enter** on a category heading just re-reads it. **Escape** closes the list.

If a game has nothing suggested for it yet, the manager says so plainly — the list is filled in game by game, and an empty one is an answer rather than a fault.

### Where the suggestions come from

There are two sources and they are kept deliberately separate:

*   **The list that ships with the manager**, which is replaced whole by each new version.
*   **Your own list**, kept with your settings, which an update never touches.

You see both together, and where the two describe the same mod, **yours wins**. That is the point of splitting them: a new version of the manager can bring a better shipped list without overwriting a reason you took the trouble to write, and nothing you write can freeze an out-of-date copy of the shipped list in place.

### Curation Mode: building your own list

Everything above is about reading a list. To *build* one, turn on **Curation Mode** with **Ctrl + Shift + F7**. The manager says which way you have just switched it, and it stays switched between sessions.

Curation Mode is off to begin with, and while it is off the Suggested Mods submenu holds only the two commands that make sense — reading the list and importing one. Turn it on and three more appear.

With Curation Mode on:

*   **F7** — **mark** the mod you are on as one worth suggesting, or **unmark** it if it is already on your list. This works both in your **Installed Mods** list and in **Find New Mods**, which matters: you can suggest a mod you have never installed, instead of having to install something on all six games just to recommend it.
*   **Shift + F7** — open **the mods you have marked**, across every game.

All three can be remapped in **Settings → Shortcut Manager** (the actions are named "CurationMode", "MarkSuggestion" and "SuggestedList"). That is worth knowing if your screen reader already claims one of them: F7 was chosen because it is the last bare function key the manager does not use, but a screen-reader add-on can lay claim to any key, in which case the app never sees it and rebinding is the fix.

Marking a mod asks you two things. First a **category** — what kind of wall this mod removes — picked from a list. Then a **reason**, in one line. The reason is the part worth taking care over: *"Auto-Fishing"* tells a reader nothing, whereas *"the fishing minigame is a moving bar with no audio cue"* tells them at once whether they need it. You can leave the reason empty and it will say so, but the entry is much less use that way.

**Unmarking asks you to confirm**, because it throws away the reason you typed and nothing here can bring it back.

### The mods you have marked (Shift + F7)

This list shows every mod you have marked, for all games, reading as *game, mod, category, reason*. Three keys work on a row:

*   **Enter** — change the category and reason. This is the *only* place to edit them: **F7** now means membership and nothing else, so that one key has one meaning.
*   **Delete** — remove the entry, after confirming.
*   **Escape** — close the list.

### Categories

A category is the heading a suggestion is filed under, and what the Suggested Mods list groups by. Four come with the manager:

*   **Can't be done by ear** — the mechanic it removes cannot be done by ear at all: a timing minigame, a visual puzzle, a moving target.
*   **Much slower by ear** — the mechanic *can* be done by ear, but costs several times more time or keystrokes than it should.
*   **Bug fix or stability** — an unofficial patch, a crash fix, a performance mod. Everyone benefits from these, sighted or not.
*   **Makes the game easier** — it makes the game easier as well as more reachable. Worth saying out loud, because a player who would rather keep the challenge can then pass it by on purpose.

Those first two are the test worth applying when you are unsure whether a mod belongs at all: **does the mechanic have to be read off the screen as it happens?** If it does, the mod that removes it is a good suggestion. That question decides most cases without needing an opinion about whether the mod is any good.

You are not limited to those four. When you are marking a mod and none of them fit, choose **"New category..."** at the bottom of the pick list and give it a name — you are not sent away to another screen and made to start the mod again.

### Managing categories

**Mods** menu → **Suggested Mods** → **Manage Categories** lists them in the order they are offered, each saying how many mods are filed under it and whether it came with the manager. Four keys:

*   **Enter** — rename it. On one of the four built-in categories, clearing the box puts its original name back, because a rename sits in front of the original rather than replacing it.
*   **Ctrl + N** — add a category.
*   **Ctrl + Up / Ctrl + Down** — move it earlier or later. This order is what the pick list and the Suggested Mods headings both follow, so the ones you reach for most can sit at the top.
*   **Delete** — remove it. If mods are filed under it, the manager asks **where they should go** and re-files them; nothing is ever left pointing at a category that no longer exists.

The four built-in categories can be renamed and reordered but **not deleted**. The list that ships with the manager is filed under them, so removing one would leave those entries with no heading to appear under.

### Sharing a list: export

**Mods** menu → **Suggested Mods** → **Export Your Suggested Mods** writes your list to a file you can pass to somebody else.

It asks for a **name** for the list (whoever opens it hears this) and an **author** — leave the author empty to stay anonymous. If you have marked mods for more than one game it also asks whether to export just the game you are in or everything; with only one game's worth marked there is nothing to decide, so it doesn't ask.

Only **your own** suggestions are exported, never the list that shipped with the manager. That is deliberate: a file containing shipped entries would plant a frozen copy of them into the recipient's personal list, where they would then win over every future update.

The categories your entries actually use travel with the file, so the person opening it gets your headings and not just bare names. Categories you have that nothing in the file uses are left out.

### Sharing a list: import

**Mods** menu → **Suggested Mods** → **Import a Suggested Mods List** takes a file in. It is available whether or not Curation Mode is on, because receiving somebody's list is not the same as building one.

Before anything is written, the manager tells you what the file would do:

> *Sean's Moonlight Peaks picks, by Sean. 3 new suggestions, 2 you already have described differently, and 4 already the same. Import it?*

Answer **No** and nothing changes. Answer **Yes** and, **if any of them clash**, you are asked how to settle it — three ways:

*   **Keep what I already wrote, for all of them.**
*   **Use this list's wording, for all of them.**
*   **Let me see each one and decide.**

The first two are one decision for the whole file, which is what you want when a long list overlaps yours in a dozen places and you already know which you prefer. The third goes through the clashing mods one at a time, and for each one shows you **both versions** so you are not choosing blind.

When you decide one by one, the **category** and the **reason** are asked separately, each as a list of two reading *"Mine: ..."* and *"Theirs: ..."*. That is what lets you take their category but keep your own wording, or the other way round — a single "mine or theirs" cannot express that. A half the two of you already agree about is not asked at all, so if you differ only over wording you are never made to confirm the same category over and over.

The title of each question says which mod and how far through you are — *"Reason for Quick Spells - 2 of 3"*. **Escape** at any point abandons the whole import with nothing written.

Afterwards the manager reports what happened — added, replaced or decided, and how many were already the same — and mentions any new categories that came with the file. Anything that landed somewhere you'd rather it hadn't can be corrected from the **Shift + F7** list.

Duplicates are not something you need to watch for. A mod is recognised as the same suggestion by its Nexus ID (or its GitHub repository, or its mod ID, or failing all of those its name), scoped to the game, so importing the same file twice changes nothing the second time. A mod that has merely been renamed is recognised as the same mod and does not count as a disagreement.

### Where the files are kept

Both your marked list and your categories live with your settings, in `%AppData%\AudiVentureGames\KinetixModManager`, as `suggested-mods.json` and `suggestion-categories.json`. The list that ships with the manager sits in the `data` folder next to the program itself. You never need to touch any of them by hand, but they are plain text if you ever want to look.

---

## How the Update Check Works

When you check for updates, the manager works out the latest version of each installed mod from two sources:

*   **The mod's own link.** A mod that records a Nexus mod ID or a GitHub repository is checked against that page directly.
*   **SMAPI's mod database (Stardew Valley).** Every installed Stardew mod is also looked up by its **unique ID** on smapi.io — the same service SMAPI itself uses when it tells you about updates in its log window. This finds updates for mods whose `manifest.json` has a missing or wrong update key, so what the manager reports lines up with what SMAPI reports.

**Disabled mods are checked too.** A mod you've switched off is still yours, and an update to it is still worth
knowing about — often it's the very thing that makes the mod usable again. Its update appears in the list like any
other.

**A disabled mod stays disabled after it updates.** Installing a mod always puts it in switched on, so afterwards
the manager puts it back the way you had it, and asks whether you'd rather switch it on now that the new version
is in — because a mod is often parked precisely because it was broken. Answer No (or press Escape) and it stays
off. During **Update All** nothing is asked: every mod that was off stays off, and the finishing announcement
names them.

**Your old copy is kept.** Every update backs the mod up first, Minecraft's single `.jar` files included, so you
can go back to the version you had from the Backups tab.

Where both sources have an answer and they disagree, the manager takes the **newer** of the two. A Nexus page's version field is typed in by the author and often lags the files actually on the page, so the mod database is frequently ahead of it. A mod is only ever listed in the Updates tab when the version it would fetch really is newer than the one you have.

When that lookup identifies a mod, the manager also **remembers its Nexus page**, so a mod that arrived with no link becomes fully actionable — you can download its update and open its page like any other. This happens quietly during a normal update check; nothing in the mod's own files is edited.

### When a mod is offered the same update forever

A few mods are offered an update that installing never settles: the row comes back on the next check, at the same version, no matter how many times you take it.

This happens when **a mod's own version number cannot match the one on its download page**. SMAPI requires a *semantic* version in `manifest.json` — SMAPI's own wording is *"should be formatted like 1.2, 1.2.30, or 1.2.30-beta"* — while a Nexus version field is free text with no rule at all. An author who publishes **2.0.3.5** on Nexus therefore has a manifest that must still say **2.0.3**, and those two will never agree. Stardew Voices is one real example.

**Do not edit the manifest to match.** SMAPI does not warn about a version it cannot parse — it refuses the mod outright and skips it, so the mod stops loading altogether. That trades a nagging row for a mod that silently does nothing, which is much harder to notice. If someone has already tried it, put the original version back.

Normally the manager avoids the whole problem: when it installs a download it **records the release it actually installed**, and compares against that rather than against any manifest. The mods this affects are the ones that arrived some other way — installed by hand, or before that recording existed — leaving nothing to compare against but the manifest.

To settle one, arrow to it in the **Updates** tab and press **Ctrl + Shift + Y** (**Mods → Mark Selected Update As Already Installed**). Confirm, and the offered version is recorded as the one you have. The row goes, and stays gone — **but anything genuinely newer is still reported**, because what is stored is a version to compare against, not a version to hide. That is the difference between this and **Delete** (Ignore Update), which mutes one specific version and nothing else.

Two other routes reach the same place: **update the mod through the manager once**, which records the release as a matter of course, or leave its original download in the manager's downloads folder, where the Update Coverage repair can read the version out of the Nexus file name.

### Mods that arrive together in one download

One Nexus download often unpacks into **several mods** — a main mod plus the content packs that ship with it — and usually only one of them records the update key. The others are still covered: updating the one that carries the key reinstalls the whole download, and everything in it. The manager recognises this (a mod inside another mod's folder, or sharing its top-level folder under `Mods` with a linked mod) and treats those mods as covered rather than reporting them as unchecked. This is why the "couldn't be checked" number is much smaller than the number of mods without their own update key.

### The Update Coverage Report

To see exactly where every mod stands, choose **Mods → Health and Reports → Update Coverage Report**. It opens as an accessible list and tells you up front how many of your installed mods can be checked and how many can't.

*   **Mods that can't be checked are listed first**, each with its name, version, unique ID, and the folder it lives in — enough to find it on disk. Press **Enter** on one and the manager **searches Nexus for it** and offers what it finds as a list: arrow through the results — likely matches, judged by name and author, come first — and press **Enter** on the right one to link it. You don't need to know the mod's ID. Two more items sit at the end of that list: **type a Nexus mod ID yourself**, and **"This mod came with another mod I have installed"** (see below).
*   **You stay in the report while you fix mods.** Once a mod is linked, or said to come with another mod, the list of results closes and you're back in the report. That mod is gone from it, and you land on the next one. When nothing is left to fix, the report closes and says every mod can now be checked.
*   **Mods covered by another download are listed after them**, each naming the mod whose update carries it and that mod's Nexus ID — so you can confirm nothing has been quietly skipped.
*   **The whole list is also written to the error log** (open it with **Ctrl + Shift + L**), including every mod's unique ID, folder, and update link. That's the copy to read at leisure, or to paste into a bug report.

Each mod it can't check says **why**: either its `manifest.json` lists no update key at all, or the key is there but blank — `"UpdateKeys": [""]`, or `"Nexus: "` with no number after it. Both are things the mod's author left out, not something wrong with your setup; the mod simply doesn't record where it came from. Giving it a Nexus ID (with **Enter** here, or **Ctrl + K** in the mod list) fixes it permanently, and the manager remembers the link in its own file rather than editing the author's manifest.

Two things the report doesn't nag you about, because they're normal: mods that **ship with SMAPI** (Console Commands, Save Backup, Error Handler) update when SMAPI does, and mods **covered by another download**, as described above.

### "This mod came with another mod"

Sometimes a mod has no page of its own because it isn't really a separate download — it's an **optional file from another mod's page**, or an extra that arrived alongside a mod you installed. When it unpacks into a folder of its own, nothing on your computer connects the two: no update key, no shared folder, nothing the manager can follow. Only you know where it came from.

So tell it once. In the report, press **Enter** on the mod, then choose **"This mod came with another mod I have installed"**. You get a list of the mods that *can* be checked; press **Enter** on the one this mod arrives with. From then on it's treated exactly like the mods that came in a single archive: covered by that mod, never reported as unchecked, and never asked to update on its own. The choice is remembered per game, and if you ever uninstall the mod you attached it to, it simply goes back to being listed so you can decide again.

Before it reports, the manager tries to repair the links itself, from most reliable source to least:

1.  **Your downloaded files.** A Nexus download is named like `Granny's Recipe Box-23737-1-0-2-1715181269.zip` — the number after the name is the Nexus mod ID — and the archive lists every mod inside it. Matching the two links each installed mod to the exact page it came from, including the extra mods a single download unpacks. This is exact, not a guess.
2.  **SMAPI's mod database** (Stardew Valley), which maps a mod's unique ID straight to its page.

Anything still unlinked after that is what the report lists. From then on the manager records the Nexus page of **every mod a download installs** at install time, so newly installed mods never end up in this state.

**That includes a mod you install from a zip yourself (Ctrl + I).** A file downloaded from Nexus carries the mod's own ID in its name — `Granny's Recipe Box-23737-1-0-2-1715181269.zip` is mod **23737** — and the moment you pick that file is the moment it is known for certain. The manager reads it from the name and records both the page and the release you installed, so the mod is checkable from then on with nothing else to do. It also means the version it compares against is the one you actually have, rather than whatever the mod's own files claim.

This only works while the file still has the name Nexus gave it. A file your browser renamed (the `" (1)"` a second download picks up), or one you renamed yourself, is treated as unknown — and so is a name with two numbers in it that could each be a mod ID, because the manager will not guess between them. Nothing is lost when that happens: the mod installs exactly as before and **Auto-match Nexus IDs** can still find it by name afterwards.

At the end of an update check you hear how many updates were found, plus — if any apply — how many mods **couldn't be checked at all** because nothing knows where they came from: no Nexus or GitHub link, not in SMAPI's database, and not part of another mod's download. Those are usually mods copied in by hand. Use **Auto-match Nexus IDs** (Mods → Install and Update Mods) to link them. It works from the most reliable source to the least: your downloaded files first, then SMAPI's mod database, and only then a Nexus search by name.

**Every name a mod goes by is tried, not just the one it calls itself.** A mod's page is very often titled differently from the mod itself, so the search also uses **the folder it was installed into** and, for a Moonlight Peaks BepInEx mod, **the parts of its plugin ID** — which conventionally contain both the author's handle and the mod's real name. Run-together names are split into words as well, because Nexus searches by word: a page called "Mod Menu" is found by "Mod Menu" but not by "modmenu". So a plugin calling itself "Bigger Stacks for Items" in a folder called `BiggerStacks` finds the page named "BiggerStacks"; one calling itself "Moonlight Peaks Mod Menu" with the plugin ID `elsia.modmenu` finds the page named "Mod Menu".

The bar for accepting a result stays high, because a mod linked to the wrong page reports someone else's version and "updating" it downloads an unrelated archive. A result is accepted only when **one of those names matches exactly** (ignoring case and punctuation), or **one name contains the other and the author agrees** — where the author may come from the plugin ID, so "Always Show Item Value" is confidently matched to the page "Always Show Item Value (sell price)" because the plugin ID `padme4000.…` matches the page's author Padme4000. Leading content-pack tags are ignored, so `[CP] Stoned Valley` matches "Stoned Valley". The game's own name and framework words (`moonlightpeaks`, `bepinex`, `com`, and so on) are never treated as a mod's name or author. **A mod it isn't confident about is left unlinked rather than pointed at the wrong page** — press **Ctrl + K** on it to enter the ID yourself.

> **If an update fails with "that download doesn't contain a mod for this game"**, the mod is almost certainly linked to the **wrong Nexus page** — so the file it downloaded belongs to some other mod. Select the mod in the Installed list, press **Ctrl + K**, and enter the correct Nexus mod ID (the number in its Nexus web address).

### Mods that don't report their new version

A mod's version — the number inside its `manifest.json` — is the author's number for *that mod*, which is not the same thing as the version of the **download** it came in. Authors routinely publish a "1.0.2" release whose manifests still say "1.0.0", and one download often installs several mods that each carry a version of their own.

Comparing those numbers against the mod page's version would offer an update that installing can never satisfy: the new files arrive, the manifest still says the old number, and the mod is offered again on the next check — forever. With two mods from one download it was worse: installing it rewrites both folders, so the two took turns asking to be updated, back and forth.

So the manager **remembers which release of each download is installed** and compares that instead. It learns it when it installs or updates something, and — for mods you downloaded through it earlier — from the file still sitting in your downloads folder. Nothing the mod author shipped is rewritten to make this work.

Two things follow from this:

*   **One row per download.** When several installed mods come from the same Nexus page, the Updates tab lists that download once, named after the main mod, rather than once per mod inside it. Installing it updates all of them, as it always did.
*   **A single mod whose author forgot to bump the version** still gets its manifest corrected after an update, so the version it reports (in SMAPI's log, for instance) is accurate. That only happens when one mod comes from the download — never for the extras bundled alongside it, whose numbers are their own.

---

## Updating Individual Mods via Nexus Mods

When the manager detects an update, it will appear in the **Updates Available** tab. Because Nexus Mods often lists multiple versions (like optional files or older versions), follow these steps to ensure you get the correct update:

### 1. Open the Mod Page
*   Highlight the mod in the **Updates Available** tab and press **Enter**.
*   Your browser will open directly to the **Files** tab for that specific mod.

### 2. Locate the Correct File
*   Use your screen reader's "Heading" or "Link" navigation to find the **"Main Files"** section.
*   The top file is almost always the latest version. Confirm the version number matches what the manager reported.

### 3. Download and Install It
*   Download the file with **either** the **Mod Manager Download** or the **Manual Download** button, following the steps in **"Downloading Mods from Nexus Mods"** above (including the "Slow Download" button and the required-mods case).
*   **If you used Mod Manager Download:** once the download finishes, the manager automatically comes to the front, plays the **"Connect"** sound, and asks **"Downloaded [Mod Name]. Install now?"** Press **Enter** (Yes) to back up your old version and install the update into the active game's mods folder.
*   **If you used Manual Download:** once the ZIP finishes downloading, switch to the manager and press **Ctrl + I** to install it.

---

## Tracked Mods (Ctrl + Shift + T)

On the Nexus website you can **track** mods you're interested in — including ones you haven't installed yet. The manager can check that tracked list for you and point out the ones worth acting on. This catches updates the normal Updates tab can't: the Updates tab only knows about mods you already have, whereas this also flags **mods you track but haven't installed** that have had a recent release.

Press **Ctrl + Shift + T** (or **Mods → Install and Update Mods → Check Tracked Mods for Updates**). The manager reads your tracked list for the current game, cross-references it with the mods Nexus has updated recently, and lists the ones that need attention: **tracked mods you haven't installed** (with their latest version and update date), and **installed tracked mods that now have a newer version**. Press **Enter** on any of them to open its Nexus **Files** page so you can download it. If nothing needs attention, it tells you so. You must be connected with your Nexus API key.

---

## Savegame Manager (Ctrl + Shift + V, Skyrim & Fallout 4)

The **Savegame Manager** gives you an accessible way to browse, back up, and delete your Skyrim and Fallout 4 saves, and — uniquely — to find out when a save depends on a mod you've since removed. Press **Ctrl + Shift + V** (or **Mods → Game and Maintenance → Manage Save Games**).

Your saves are listed newest-first, each read as the **character name, level, location, and save date**. A save that needs a plugin that's **no longer active** (because you removed or disabled the mod) is flagged in its entry — loading such a save can crash the game or lose content, so this is worth knowing before you continue a character.

On a save in the list:

*   **Enter** speaks its **full details** — character, level, location, in-game playtime, save date, how many plugins it was made with, and, if any are missing, exactly which plugins it needs that are no longer active.
*   **B** **backs up** the save (and its script-extender co-save) to the manager's save-backup folder.
*   **Delete** sends the save (and its co-save) to the **Recycle Bin** after you confirm, so it can be recovered if you change your mind.

---

## Smart Game Launching (F5)
The manager does more than just launch the game:
*   It announces when it starts launching the game through its mod loader (SMAPI for Stardew Valley, SKSE for Skyrim Special Edition, F4SE for Fallout 4). Moonlight Peaks has no separate loader to run — BepInEx hooks the game itself — so for that game the manager **asks Steam to start it**, which is also what stops the game restarting itself (see below).
*   It speaks and displays *"Game is loaded and running"* once the game is active.
*   It detects when you close the game and announces *"Game closed."*
    *   **Why that isn't always instant.** A Steam game started from its own program file notices it wasn't launched by Steam and **restarts itself** — the game loads, your mods load and announce themselves, and then the whole thing shuts down and starts again about fifteen seconds later. The manager used to take that first shutdown at face value and announce *"Game closed"* while the game was in fact still starting. It now follows the game itself rather than the program it started, and allows for a restart during the first few minutes of a session, so a startup restart is no longer mistaken for you quitting. Once a session has been running a while, closing the game is announced within a few seconds as before.
    *   For Moonlight Peaks the restart is avoided altogether, because the manager asks Steam to start the game in the first place. Your mods are unaffected either way: BepInEx loads through a file sitting beside the game program, whoever starts it. Games launched through SMAPI, SKSE or F4SE are **never** routed through Steam, since bypassing those loaders would mean no mods at all; nor is a non-Steam copy, which is started directly as always.
*   **Script-extender version check (Skyrim & Fallout 4):** before launching, it checks that the installed **SKSE/F4SE matches your game's version**. SKSE and F4SE only work when built for the exact game build, so if your game has updated and the script extender no longer matches, it silently won't load — which is the usual reason the **Mod Configuration Menu** and other script-extender features suddenly disappear. When this happens the manager **warns you and asks whether to launch anyway**; to fix it, reinstall the script extender from the Accessibility Suite so it matches your game. The check looks at **every** script-extender file in your game folder, not just one, so the file left behind by your previous install is correctly ignored rather than being mistaken for the one in use.

---

## Load Order Management (Skyrim & Fallout 4)

Skyrim Special Edition and Fallout 4 decide how mods combine using **load order**, and the manager gives you dedicated tabs for it. They appear automatically for those games, right after the Installed Mods tab. Stardew Valley does not use load order, so these tabs don't appear for it.

### Mod Priority
When two mods provide the same loose file, the one with **higher priority wins**. The **Mod Priority** tab lists your mods highest-priority-first (the top of the list wins conflicts). Each row also tells you how many files that mod **overrides** or **is overridden in**, so you can judge its standing by ear. Press **Ctrl + Up** or **Ctrl + Down** to move the selected mod higher or lower; the change is applied immediately and the new position is announced.

### Plugin Order
The **Plugin Order** tab is the load order of your plugin files (the `.esp`, `.esm`, and `.esl` files), written to `plugins.txt`. Plugins higher in the list load first. Masters and light masters always load before regular plugins, and the base game and its add-ons are handled automatically and not listed. Press **Ctrl + Up / Ctrl + Down** to move a plugin (it can't cross the boundary between masters and regular plugins). Press **F8** to **auto-sort**: the manager arranges every plugin to load after the masters it needs, using LOOT's community rules when it can reach them, and a master-dependency sort otherwise.

### Creations
**Creations** (formerly Creation Club content) are official add-ons you download **inside the game**, from its **Creations** menu — or queue from the Bethesda.net website to your linked account. No mod manager downloads them for you; the game installs them into its own folder. The **Creations** tab lists the Creations already installed in your game, whether each is **Active**, and whether it's a master or light master. Press **Space** to activate or deactivate the selected one. To change where a Creation loads, use the Plugin Order tab.

### Keeping Your Creations On and Your Plugins in Order

Skyrim Special Edition and Fallout 4 rewrite their own plugin list when you start a new game: they **switch off every Creation** and **reshuffle your load order**, undoing what you set up. The manager guards against this in three ways, all automatic:

*   **The plugin list is protected.** Whenever the manager writes `plugins.txt` — and again just before it launches the game with **F5** — it marks that file **read-only**, which the game quietly gives up on rewriting. Your order and your active Creations survive starting a new game.
*   **Anything the game did undo is put back.** When the game closes, and again whenever the mod list is refreshed, the manager compares the game's plugin list against your saved order. If Creations were switched off or plugins were moved, it restores your setup and tells you so — *"The game had turned off 3 plugins or Creations. They have been switched back on and your load order restored."* This also covers launching the game outside the manager, from Steam.
*   **Turning a Creation off still works.** Deactivating a Creation yourself with **Space** on the Creations tab is remembered as your choice, so the protection never switches it back on.

One thing to know: because the game can no longer edit that file, a Creation you download **while playing** may not switch itself on. Come back to the manager, choose **File → Refresh All**, and press **Space** on it in the Creations tab to activate it.

If you'd rather the manager left `plugins.txt` alone — for instance because another tool wants to write it — uncheck **"Protect Plugin Order and Creations"** on the **Mods & Search** tab in Settings (**Ctrl + P**). Unchecking it also clears the read-only mark straight away.

### Exporting and Importing Your Load Order
From the **Mods menu → Load Order and Files** submenu you can **Export Load Order** to save your current mod priority and plugin order to a file, and **Import Load Order** to apply a saved file later — for example as a backup, or to move a setup between computers. Importing replaces the current order and re-applies it; it never adds or removes your mods, and it only accepts a file that was exported for the same game.

### Plugin Limit Awareness (Ctrl + Shift + U)

Skyrim and Fallout 4 can load only a limited number of plugins: **255 regular** plugins (the base game and its add-ons count toward this), plus a **separate pool of up to 4096 light (ESL)** plugins. Going over the regular limit is a hard failure — the game silently drops plugins or won't launch — so it's worth knowing where you stand.

Press **Ctrl + Shift + U** (or **Mods → Health and Reports → Plugin Slot Usage**) to hear a summary such as *"Plugin slots: 231 of 255 regular plugins used, 24 remaining. 40 of 4096 light plugins used."* If you're near or over a limit, it adds a warning and suggests converting eligible plugins to light (ESL) to free up regular slots. The manager also **warns you automatically** the first time you focus the **Plugin Order** list while you're near or over the cap, so you're not caught out. (A plugin flagged as light — an `.esl`, or an ESL-flagged `.esp` — counts against the light pool, not the regular 255.)

### Choosing File Conflict Winners

When two enabled mods provide the **same** loose file, the higher-priority mod wins by default. But sometimes you want mod A's version of one file and mod B's version of another, even though A outranks B overall — something plain priority order can't express. **Mods → Load Order and Files → Choose File Conflict Winners** lets you override the winner for a specific file.

It lists every contested file with its current winner. Press **Enter** on a file to **cycle** its winner through the mods that provide it (the manager announces each new winner and re-deploys immediately), or **Delete** to revert that file to normal priority order. Your overrides are saved per game and re-applied every time the manager deploys; if you later remove the mod you forced to win, the override is simply ignored and the file falls back to priority order.

### Persistent Load-Order Rules

Beyond moving plugins one at a time, you can set **standing rules** — "always load this plugin after that one" — that survive an auto-sort, the way Mod Organizer 2 and LOOT let you. This is handy for a patch that must always come after the mod it patches.

*   **Add a rule:** on the **Plugin Order** tab, select the plugin you want to constrain, then choose **Mods → Load Order and Files → Add Load Order Rule for Selected Plugin**. A list of the other plugins opens: press **Enter** on one to load your selected plugin **after** it, or **B** to load it **before** it.
*   **Manage rules:** choose **Mods → Load Order and Files → Manage Load Order Rules** to hear your rules ("X loads after Y") and press **Delete** to remove one.

Rules are applied the next time you **auto-sort** with **F8**, alongside master dependencies and LOOT's own rules.

---

## Checking Your Mods: Conflicts and Requirements

The reports in the **Mods menu → Health and Reports** submenu help you spot problems. Each opens as a simple list you can arrow through, and **Escape** closes them.

### Check My Setup (Setup Health Check) (Ctrl + Shift + K)

**A mod page's version number is maintained by hand.** Authors upload a new file and sometimes forget to change the version shown on the page, so the page can read older than the release it is offering. When the page claims there is nothing new, the manager now also reads the page's **Files** tab and compares the newest **main** download against what you have installed — so a forgotten version number no longer hides an update from you. Files filed as optional, old version or archived are ignored, since those are not what the page is offering. This costs an extra look-up, so it is taken only when the first answer was "up to date", and it is skipped when your Nexus API allowance is running low.

If you'd rather run everything at once than open each report separately, choose **Check My Setup** (or press **Ctrl + Shift + K**). It runs all the individual checks in one pass — the **Windows runtime** (see below), **missing requirements** (missing masters, script extender, and Nexus requirements), the **plugin limit**, **incomplete mods** (a mod that needs two downloads and only got one, such as SSE Engine Fixes without its preloader — press **Enter** on that line to fetch the missing part), **files no longer used** (a part of a mod your game version has outgrown, such as the Engine Fixes preloader on Skyrim 1.7.99 and newer — **Enter** offers to send it to the Recycle Bin), **known-broken or incompatible mods**, and **file conflicts** — and gives you a **single spoken summary** broken down by category, for example *"Setup health check found 3 problems: 1 missing requirement, 2 broken or incompatible mods."* If everything's fine it says *"No problems found. Your setup looks healthy."*

The findings then appear as one combined list, most serious first. Each row keeps the same actions it has in its own report: press **Enter** to search for or open a missing mod, **Delete** to hide a requirement warning that doesn't apply to you, or **F9** to ask the AI about that finding (if AI is set up). You stay in the list after fetching a missing part or removing a leftover one, and that line disappears once it's fixed. Only searching for a missing mod takes you out of the list, because the search happens on the Find New Mods tab. This is the quickest way to check a setup is sound — for example after installing several mods, or before launching.

#### The Windows runtime check

One of the checks isn't about your mods at all — it's about Windows, and it comes first in the list because a problem there affects **every game at once**.

Many mods are program files rather than data, including all of the accessibility mods. They all rely on a shared piece of Windows called the **Microsoft Visual C++ Redistributable**. Occasionally a game or an installer replaces part of it with an older copy, leaving a set of files that no longer match each other — and mods that depend on it then stop loading.

This is worth calling out because of how it goes wrong: **there is usually no error.** The game starts normally and the mod simply never speaks. Windows' own list of installed programs still reports the newer version, so nothing looks amiss. It's an easy fault to spend an evening chasing in the wrong place.

If your files don't match, Check My Setup says so, names the ones that are out of step, and offers **Enter** to open Microsoft's download page. Install the latest version from there (choose **Repair** if it offers to, and take the newest one — an older installer will refuse as a downgrade), then restart the game. If your runtime is fine, or the manager is running under Wine on Linux where the check doesn't apply, nothing is reported.

### File Conflict Report (Ctrl + Shift + F)
This shows where your mods collide.

*   **Skyrim & Fallout 4:** it lists every loose file that **more than one enabled mod provides**. Each line names the file, the mod whose copy **wins**, and the mods it **overrides**. This is the detailed view behind the "overrides / overridden in" counts on the Mod Priority tab — use it to decide whether your priority order is putting the right mod on top.
*   **Stardew Valley:** Stardew mods each load from their own folder and never overwrite each other, so there are no file conflicts. Instead the report lists any mods that **share a UniqueID**, which stops SMAPI from loading them — something you'd want to fix by removing the duplicate.

### Check Mod Requirements (Ctrl + Shift + Q)
This scans your **enabled** mods and lists anything they need but don't have. Press **Enter** on a line to act on it.

*   **Stardew Valley:** required mods that are **missing, disabled, or older** than a mod asks for — including the host mod a content pack (such as a Content Patcher pack) needs. Enter offers to **search** for the missing mod in the Find New Mods tab.
    *   A required mod is named by the `UniqueID` in its manifest, and that id is matched **ignoring capitalisation and stray spaces**, exactly as SMAPI matches it. Authors do not always agree on how to spell one — Producer Framework Mod calls itself `Digus.ProducerFrameworkMod` while the packs that need it ask for `DIGUS.ProducerFrameworkMod` — and the game loads them regardless, so this report treats them as the same mod too.
    *   Where the same mod is installed twice, an **enabled** copy is what counts, since that is the copy the game will load.
*   **Skyrim & Fallout 4:** plugins whose **master file isn't installed** (a missing master stops a plugin loading), a **missing script extender** (SKSE/F4SE) — or one installed for a **different game version**, which from inside the game looks exactly the same, because it never loads — and each mod's **Nexus "Requirements"** that you don't have installed. Enter opens the missing mod's page. Because the Nexus part checks each mod online, a large load order can take a moment — the title bar shows the progress.

### Check for Broken Mods (Ctrl + Shift + B)
This checks your installed mods against a community-maintained compatibility list and reports the ones known to have problems — issues that no Nexus update would tell you about. It needs an internet connection; if the list can't be reached it simply says so.

*   **Stardew Valley:** uses the official **SMAPI compatibility list**. It flags mods marked **Broken**, **Obsolete**, or **Abandoned**, with a short note explaining the status. Press **Enter** on a mod to open its Nexus page.
*   **Skyrim & Fallout 4:** uses the **LOOT masterlist**. It flags a plugin that is **incompatible with another plugin you also have installed**, plus any curated **warning or error** notes LOOT records for your active plugins. Only unconditional warnings are shown, so an item appears only when it definitely applies.

If nothing is flagged, the report tells you none of your mods are on the known-broken list.

---

## Deployment Tools (Skyrim & Fallout 4)

When you enable a mod or change the load order, the manager automatically **deploys** — it links each enabled mod's files into the game's folder and updates the plugin list so the game loads them. Stardew Valley loads mods from their own folders, so these tools are for Skyrim and Fallout 4 only. Three commands let you check and control that deployment directly, which is handy if the game's folder ever gets into an odd state. On the **Mods** menu, **Verify Selected Mod's Files Are Installed** is under **Selected Mod**, while **Rebuild** and **Purge Mod Deployment** are under **Load Order and Files**.

*   **Verify Selected Mod's Files Are Installed** checks the mod highlighted on the Installed Mods tab and reports how many of its files are **in place**, how many are **missing** from the game folder, and how many are **overridden** by a higher-priority mod. Missing files mean the mod isn't really active on disk even if it looks enabled — the decisive check when a mod "should" be working but isn't.
*   **Rebuild Mod Deployment** force re-links **every** enabled mod's files into the game folder and rewrites the plugin list, after you confirm. Use it as a safety net if deployment ever looks inconsistent. It never deletes any of your installed mods.
*   **Purge Mod Deployment (Restore Vanilla Game Folder)** does the opposite: it **removes every file the manager deployed** from the game folder, returning it to a clean, un-modded state — while leaving all your installed mods untouched in the manager. Use it when you want to run the game vanilla for a moment, or to fully reset the game folder before rebuilding. Only files the manager put there are removed; it never touches anything it didn't deploy. Because the manager re-deploys automatically, the next time you enable a mod or change the load order everything comes back — or choose **Rebuild Mod Deployment** to re-apply it all at once.

### Editing Game Settings (INI files)

Skyrim and Fallout 4 keep their settings in **INI files** (things like resolution, field of view, or mod-required tweaks). Choose **Edit Game Settings (INI Files)** from the **Mods** menu → **Game and Maintenance** to change them without opening a text editor or risking a typo that stops the game launching.

First you pick which file to edit — the main INI, the preferences INI, or the **custom** INI (the safe place for your own tweaks; the manager creates it if it doesn't exist yet). Then you get a navigable list of every setting, each read as **"[Section] key = value"**:

*   **Arrow** through the settings, or type the first letter to jump.
*   **Enter** on a setting to change its value — you type the new value and it's saved straight away.
*   **Delete** removes a setting after you confirm.
*   The **Add Setting** button adds a new one: it asks for the section, the setting name, and the value.

Every change is written back immediately, and all your comments and other settings are left exactly as they were.

### Editing a Mod's Settings (Stardew Valley)

Choose **Edit Mod Config** for the selected mod to change its settings from inside the manager. What you get depends on the mod.

**Content Patcher packs** — most Stardew content mods — get a proper **settings list**. Content Patcher mods let their author declare exactly which values each setting accepts, and the manager reads that, so you get a list of the mod's settings, each read as **"setting name: current value"**:

*   **Arrow** through the settings.
*   **Enter** on one to choose its value from **the list of values the author allows** — no typing and no guessing. The author's own explanation of the setting, if they wrote one, is read out as the chooser opens, along with the current value. Settings that take a number or free text instead ask you to type one.
*   **Delete** puts a setting back to **the mod author's default**. This is worth knowing about, because the default isn't written in the file you'd otherwise be editing — it lives in the mod's own definition, so putting a setting back by hand means going and looking it up.
*   Changes are saved as you make them, and anything else in the file is left alone. **Escape** closes the list.

Why this matters: the file a Content Patcher mod leaves you to edit contains only the answers, not the questions. A setting reads `"ObeliskOptions": "vanilla"` with nothing to say that `glass`, `garden`, `Yri` and `Juffuffles` are the alternatives — so changing it normally means reading the mod author's own files, or using an in-game menu. The manager puts the two halves back together.

A mod that has **never been run** still works here: it has no settings file yet, so every setting shows the author's default, and the file is created the moment you change one.

**Every other mod** gets the plain JSON editor as before — the mod's `config.json` in a text box, with **Ctrl + S** to save and a syntax check before it's written.

### Editing a Mod's Settings (The Witcher 3)

A Witcher 3 mod that adds a page to the game's **Options → Mods** menu gets the same settings list, reached the same way — **Ctrl + E** on the mod. **WitcherAccess** has 32 settings across its General and Sounds groups.

This one needs explaining because nothing about it is where you would expect:

*   **The settings are not in the mod's folder.** The Witcher 3 keeps every mod's settings in **`Documents\The Witcher 3\user.settings`**, in a section named after the group — `[WAGeneral]`, `[WASounds]` — while the menu itself is declared in a file in the game folder. The manager reads both and puts them back together, which is why pressing Ctrl + E on the mod now shows something.
*   **A mod you have never configured in-game still works here.** The Witcher 3 writes a setting into that file only once it has been touched, so on a fresh install a mod's settings are simply missing from it. The list shows **the author's own defaults** in that case, taken from the menu definition, so it is complete from the start.
*   ⚠️ **Close the game before changing anything.** The Witcher 3 rewrites `user.settings` when it exits, so a change made while it is running is overwritten when you quit — silently, with nothing to say it happened. The manager warns you if it sees the game running and lets you decide.
*   ⚠️ **If the file is read-only, nothing saves — including from inside the game.** This one has caught people out: settings changed in the game's own menu appear to work and are gone next time. The manager checks, and offers to make the file writable.
*   **Key bindings are a separate matter.** They live in `Documents\The Witcher 3\input.settings`, and the game reads that file **only at startup** — so a change to a binding does not apply until the game is fully restarted. The manager's controls list (**Ctrl + H**) reads that file.

Settings whose entries are buttons — WitcherAccess's **Glossary** group is thirteen "play this sound" previews — are not listed, since only the game itself can carry those out.

### Mods that install to the game's root folder (ENB, ReShade, script extenders)

Most Skyrim and Fallout 4 mods install into the game's **Data** folder, but some — graphics injectors like **ENB** and **ReShade**, or tools that sit next to the game's `.exe` — need their files in the game's **root** folder instead. The manager detects these automatically when you install them: their game-root files (such as `d3d11.dll`, `dxgi.dll`, `enbseries\`, or an archive's `Root\` folder) are deployed to the root, while any `Data` files in the same archive still go to Data. You don't have to do anything special — install them like any other mod, and enable/disable and Purge/Rebuild treat them the same as the rest. (Full script extenders like SKSE and F4SE are still installed directly, as before.)

**Screen-reader files are placed for you.** An accessibility mod such as **Skyrim Access** needs `nvdaControllerClient.dll` sitting directly beside the game's `.exe` — Windows looks for a file asked for by name in the game's own folder and nowhere else, so anywhere tidier simply fails, and the symptom is a game that starts perfectly and never speaks. Mods do not agree on where to keep it: Skyrim Access ships it inside an `NVDACC` folder, and an older build kept it in `Data\Root`. **The manager now finds it wherever it is and puts it in the right place**, along with the other screen-reader bridge files a mod might ship (`Tolk.dll` and the JAWS, ZoomText and Dolphin clients). If a mod ships both a 32- and a 64-bit build, the 64-bit one is used, since Skyrim Special Edition and Fallout 4 are both 64-bit.

This applies to mods **already installed** as well — you don't need to reinstall anything, just refresh. And removing the mod takes the file back out again, like everything else it installed.

---

## Surviving a Game Update (Skyrim & Fallout 4)

The single most common way a modded Bethesda game breaks is a **Steam or GOG update**: it changes the game's `.exe`, and then **SKSE/F4SE and any DLL-based plugins built for the old version stop loading** — which is usually why the Mod Configuration Menu and other features suddenly vanish. The manager helps you both catch this and plan for it.

### The Game-Update Guardian (automatic)

Every time you load a Skyrim or Fallout 4 session, the manager quietly records the game's version. If it notices the version **changed since last time**, it warns you **once** — for example *"Skyrim Special Edition updated from version 1.6.640 to 1.6.1170 since you last opened the manager. SKSE and any DLL-based plugins almost always need a matching update before the game will launch."* When a script extender is installed it also offers to open its Nexus page so you can grab the matching build. It's a one-time heads-up per update, not a nag. (This is separate from the pre-launch check on **F5**, which verifies the script extender matches right before the game starts.)

### When the script extender is fine and the mods still don't load

A matching SKSE/F4SE is only the **first link**. The full chain is:

**game → script extender → Address Library → DLL plugins → the mods that need them**

A break anywhere below the script extender is **silent**: the game launches, the script extender loads, and the plugins are quietly skipped. There is no error message, no crash, and nothing on screen to read — the accessibility mod simply says nothing. This is the single most confusing thing that can happen to a modded Bethesda game, so the manager now checks the rest of the chain too.

**The Address Library.** Most DLL plugins depend on it, and it ships **one data file per game build**. When the game updates, there is no file for the new build until the Address Library's author publishes one — and until then *every* plugin that uses it refuses to start. Before launching, the manager compares your game's build against the data files in `Data\F4SE\Plugins` (or `Data\SKSE\Plugins`) and warns if yours is behind, naming the newest build it covers. Nothing about your setup can fix this one; it is waited out.

**When the game does not start at all.** **Check My Setup** (**Ctrl + Shift + K**) reads the script extender's log for the plugin the game was still loading when it stopped, and names it: *“Skyrim Special Edition stopped while loading the mod plugin EngineFixes, and did not finish starting.”* **Enter** finds that mod in your list. This is a different problem from the ones below — a plugin the script extender *refuses* is written down and skipped, and the game still runs; a plugin that hangs, crashes or puts up its own error box writes nothing at all and takes the game with it, so the only trace is a log that stops mid-sentence. It usually means that mod needs an update for your version of the game.

**A game that hangs rather than closing** is reported too, and it is the more confusing of the two: a plugin that never returns leaves a live game process with **no window**, so the manager reports the game as running while nothing is on screen. You will be told *“Skyrim Special Edition is running but has not finished starting: it is stuck loading the mod plugin SkyrimAccess”*, and you can close the game from Task Manager. What separates a stuck launch from a healthy one is the log going quiet — a launch in progress writes a line per plugin and loading one takes moments, so a plugin still outstanding with nothing written for a minute is not a load in progress.

**What actually failed last time.** **Check My Setup** (**Ctrl + Shift + K**) also reads the script extender's own log and lists, by name, each DLL plugin it refused and why — either *"it needs an Address Library for game version X"* or *"it is built for a different version of the game"*, which are two different problems with two different people to wait on. Support files that were never plugins (such as `msdia140.dll`, shipped by the crash loggers) are not reported: the script extender mentions them every run and they are perfectly normal.

**Why "all mods are up to date" can be true at the same time.** The update check compares your mods against **Nexus**. If a mod's author hasn't released a build for the new game version yet, there is genuinely no update to find — your mods aren't behind Nexus, they are behind *the game*. Both messages are correct, and together they tell you the honest answer: wait, or put the game back on the version your mods were built for.

### Prepare for a Game Update, and Restore Afterwards

If you know an update is coming, you can plan for it from **Mods → Game and Maintenance**:

*   **Prepare for Game Update** takes a safety snapshot and temporarily **undeploys your mods**, returning the game folder to vanilla so the update installs cleanly against unmodified files. Your installed mods are kept safe in the manager — only the deployed (linked) files are removed.
*   After the update finishes, choose **Restore Mods After Game Update** to **redeploy every enabled mod** back into the game folder. It then runs the guardian automatically, so if the update changed the game version you're reminded to update the script extender.

---

## Safety Backups (Skyrim & Fallout 4)

Before the operations most likely to disrupt a setup — **purging** or **rebuilding** deployment, and **editing the game's INI files** — the manager automatically takes a **safety snapshot**. Each snapshot captures the game's INI files and the active plugin list (`plugins.txt`) plus your saved mod priority and plugin order. The most recent **eight** snapshots per game are kept; older ones are removed automatically. You don't have to do anything to create them.

If a change goes wrong, choose **Mods → Game and Maintenance → Restore a Safety Backup**. It lists the snapshots newest-first, each labelled with when it was taken and what it was taken before (for example *"Before editing SkyrimPrefs.ini"*). Press **Enter** on one to restore it — after you confirm, the manager copies those files back, reinstates the saved load order, and re-deploys — or **Delete** to remove a snapshot you don't need.

---

## Dependency View and Resolver

Two tools on the Installed Mods tab (and the **Mods** menu → **Selected Mod** submenu) help you manage what your mods depend on.

*   **View Dependencies (Ctrl + Y)** opens a list with two parts for the selected mod: **Requires** (each dependency and whether it's installed, disabled, out of date, or missing) and **Required by** (the other installed mods that depend on this one). On Stardew this reads each mod's manifest; on Skyrim/Fallout 4 it reads plugin masters and, when you're logged in, the mod's online Nexus requirements. Press **Enter** on a missing item to search for or open it.
*   **Resolve Missing Requirements (Ctrl + Q)** gathers **all** of the selected mod's missing required mods at once. On Skyrim/Fallout 4, if you have a Nexus **Premium** account it downloads and installs each one automatically (opening the accessible FOMOD wizard when a mod needs setup choices); free accounts and off-Nexus requirements are listed in a report you can open and download manually. On Stardew it lists every missing required mod so you can search for each. **On Minecraft it looks each missing mod up on Modrinth and installs the build for your Minecraft version**, naming anything it couldn't find — which matters more here than anywhere else, because Fabric refuses to start the game at all while a mod's requirement is missing. You're asked to confirm once before anything downloads.

**Check My Setup and the requirements report also cover Minecraft now**, including the finding that matters most: a mod built for a different Minecraft version than the one you're running. That single mod stops the game launching, and the report names it, the version it wants, and the version you have.

**Before you delete a mod**, the delete confirmation now warns you if other installed mods depend on it, so you don't accidentally break your setup.

---

## Mod Collections

A **Collection** is a shareable "recipe" for a whole modded setup. It lists every mod in your setup — which mods, which versions, and what order they load in — saved as a single small file. It does **not** contain the mod files themselves, so it stays tiny and is safe to share: installing a Collection re-downloads each mod fresh from Nexus, so authors still get their downloads and endorsements. Collections are great for backing up your own setup to reinstall later, moving a setup to another PC, or sharing a known-good setup with a friend.

### Exporting a Collection (Ctrl + Shift + X)

From the **Installed Mods** tab, press **Ctrl + Shift + X**. The manager gathers your currently **enabled** mods (in load-order priority on Skyrim and Fallout 4), asks you to name the Collection, and saves it to a file you choose. Mods that didn't come from Nexus can't be re-downloaded, so they're recorded in the file as ones the person installing will need to supply themselves; the manager tells you how many that was.

### Installing a Collection (Ctrl + Shift + N)

Press **Ctrl + Shift + N** and pick a Collection file. Before anything happens you get a **review screen**: a spoken summary — for example "This collection has 40 mods. 38 will download automatically, 2 need manual download" — and an arrowable list of every mod and what will happen to it. Press **Enter** to start installing, or **Escape** to cancel.

The manager then installs the mods **one at a time**, with the same audible progress as any other download, and moves on to the next automatically. When it finishes you get a **report** listing what installed, what still needs a manual download, what you chose to skip, and anything that failed — press **Enter** on any of those rows to open its Nexus page.

A few things to know:

*   **Premium** Nexus accounts download every mod automatically. **Free** accounts can't (a Nexus rule), so their mods are listed in the report for you to fetch by hand with the Mod Manager Download button — each one still installs through the manager when you click it.
*   The Collection must be for the **game you currently have loaded**; the manager checks and tells you if it's for a different game.
*   If a mod has a **FOMOD** option wizard, it opens during the install so you can choose your options; after you select **OK**, the next mod continues automatically.

---

## Importing from Mod Organizer 2 (Skyrim & Fallout 4)

If you're moving to Kinetix Mod Manager from **Mod Organizer 2 (MO2)**, you can bring your existing setup across. Open the **Mods** menu → **Profiles and Collections** and choose **"Import from Mod Organizer 2"**.

1.  Pick your MO2 folder — the one that contains the `mods` and `profiles` folders. (On most setups this is under `...\AppData\Local\ModOrganizer\<your game>`.)
2.  Choose which **profile** to import from the list, which shows each profile's mod count and how many are enabled.
3.  Confirm. The manager **copies** each mod into its own mods folder (your MO2 setup is never changed), applies the mod priority and plugin order, and activates the same plugins — including any Creations you had active.

Importing copies the mods, so it can take a while for large lists, and any mods you already have in the manager are skipped. Afterwards, the manager automatically fills in the Nexus IDs for the imported mods (MO2 doesn't store them), so update checks and "open mod page" work for them.

> **Note:** This manager deploys real files into the game's folder, whereas MO2 uses a virtual overlay. After importing, launch the game through this manager or directly — not through MO2 at the same time.

---

## Game Logs (Skyrim & Fallout 4)

For Skyrim Special Edition and Fallout 4, a **Log** tab — shown as **"Skyrim Logs"** or **"Fallout 4 Logs"** — lets you read the logs the game's **script extender** and its plugins write, without leaving the app. This is where you find out, for example, that a plugin failed to load because the game updated.

*   **Log dropdown**: Choose which log file to view. It lists every log in the script-extender folder, newest first — such as `f4se.log` (or `skse64.log`), the per-mod logs, crash logs, and your accessibility mod's log.
*   **Filter dropdown**: Choose **Full Log**, or **Errors and Warnings** to keep only the lines that look like problems. As you move through the choices, the manager tells you how many lines each one shows.
*   **Search box**: Type text and press **Enter** to keep only matching lines.
*   **Refresh**: Press **Ctrl + Shift + R** to re-read the log at any time, even while the game is running — handy right after a crash.
*   **Open or copy**: Press **F4** to open the selected log in Notepad, or **Ctrl + C** to copy the selected line(s) to the clipboard.

(For Stardew Valley, the equivalent is the **SMAPI Log** tab described elsewhere in this manual. For Moonlight Peaks, it is the **BepInEx Log** tab described below.)

---

## Moonlight Peaks and BepInEx

Moonlight Peaks loads its mods through **BepInEx**, a mod loader for Unity games. It works differently from SMAPI or the Skyrim/Fallout script extenders in a few ways that are worth knowing, because the manager handles them for you.

### BepInEx itself

*   **There is nothing separate to launch.** BepInEx hooks into the game through a file called `winhttp.dll` that sits next to the game program. That means pressing **F5** starts Moonlight Peaks normally, and that *is* the modded launch — there's no "launch with mods" versus "launch without". Because there's no loader to run, the manager asks **Steam** to start the game, which avoids the game restarting itself and loading your mods twice.
*   **The manager installs BepInEx for you.** When you load a Moonlight Peaks session and BepInEx isn't there, the manager says so out loud and offers to download and install it. This matters more here than for other games: without BepInEx the game starts perfectly happily with **none** of your mods running, and nothing in the game tells you why. You can also install it any time from **Mods → Install Accessibility Suite**, where it's listed as "BepInEx (Mod Loader)".
*   **The version is pinned deliberately.** The manager installs **BepInEx 5.4.23.5**, because Moonlight Peaks mods are built against BepInEx 5 and will not load under BepInEx 6. If you already have a different version installed, the manager mentions it once and otherwise leaves it alone.
*   If you press **F5** with BepInEx missing, the manager warns you before the game starts, so you don't spend ten minutes wondering where your mods went.

### Where mods live, and how enabling works

Moonlight Peaks mods are folders of program files (`.dll`) inside `BepInEx\plugins` in the game folder. Unlike the other supported games, they are **not** disabled by renaming the folder — BepInEx pays no attention to folder names and would happily keep loading a "disabled" mod. Instead, when you disable a mod (**Space** on the installed list) the manager **moves it to a `BepInEx\plugins-disabled` folder** next door, and moves it back when you enable it. The mod stays completely intact either way, so nothing is lost and nothing is re-downloaded.

If you've previously installed a mod by dropping a single `.dll` straight into `plugins`, the manager tidies it into a folder of its own the first time it scans, so it can be listed, enabled, disabled, backed up and removed like any other mod. Shared library files that aren't mods themselves are left exactly where they are.

### Mod names and versions

Moonlight Peaks mods don't ship a manifest file the way Stardew mods do. Instead, each one declares its name, version and ID inside the program file itself, and the manager reads that — so the installed list shows the name the author gave the mod, not the name of the folder or the download. Where a mod is quiet about its version, the manager falls back to what BepInEx recorded in its log when it loaded the mod.

### Mod settings (Mods menu)

Most Moonlight Peaks mods keep their settings in a configuration file that BepInEx writes for them, and those files contain the author's own explanation of what every setting does. Two features use this:

*   **Mods → Edit Mod Settings (Config Files)** lists every installed mod that has settings, by name. Choose one to edit it in the same accessible editor used for the Skyrim and Fallout INI files.
*   **F3 (Mod Documentation)** builds a settings reference for every installed mod, straight from those same files — so each mod's settings are listed with the author's description, the current value, the default, and the accepted values. Because it's read from what's installed, it always matches the version you actually have. If nothing appears, run the game once so the mods can write their settings files.

### The game's own key bindings

Moonlight Peaks settles its controls while it runs, and keeps them nowhere another program can read — so unlike the other three games, the manager cannot simply know what your keys are. It handles this with a snapshot of the game's standard keys that ships inside it, plus an optional 15 KB **Keybind Reader** mod that writes your real bindings out while you play. You are asked once whether to install the reader, and the controls list always says which of the two you are looking at. See **[The Keybind Reader](#the-keybind-reader-moonlight-peaks)** under the Game and Mod Controls Viewer for the full explanation.

### The BepInEx Log tab

Moonlight Peaks gets a **BepInEx Log** tab, working exactly like the Skyrim and Fallout log tabs described above. BepInEx writes a single `LogOutput.log` recording every mod it loaded, with each mod's name and version, plus anything the mods themselves reported. It's the quickest way to confirm a mod is actually running, and the first place to look when one isn't.

---

## AI Log Diagnosis

When a log has you stuck, Kinetix can send it to an AI service that reads it and explains — in plain language — what's going wrong and how to fix it, with numbered steps. This is **optional and off by default**. The manager's own instant, offline checks (like the SMAPI log's Quick-Fix) are always your first line; the AI is the "I'm still stuck" step.

### One-time setup (Settings → AI tab)

1. Open **Settings** (Ctrl + P) and go to the **AI** tab. (While "Enable AI features" is unchecked, the rest of the tab is hidden; checking it reveals the settings below and is announced aloud.)
2. Check **Enable AI features**.
3. Choose an **AI Provider** — **Anthropic (Claude)**, **OpenAI (GPT)**, **Google (Gemini)**, or **OpenAI-compatible / Custom endpoint**. The last one works with any service that speaks OpenAI's API — OpenRouter, or a local server like Ollama or LM Studio — and shows an extra **Endpoint base URL** box (enter the URL the service gives you, usually ending in `/v1`). For a local server that needs no key, put any placeholder such as `local` in the key box.
4. Paste your **API key** for that provider. You create the key on the provider's website; usage is billed to **your** account. The key is stored **encrypted** on your PC (the same protection used for your Nexus key) and is kept per-provider, so you can switch providers without re-entering keys. The help text under the key box tells you where to get a key for the chosen provider.
5. Choose a **Model**. The list starts with a few common models; press **Refresh model list** to pull the provider's **current** catalog using your key. The manager **remembers** that list, so it keeps showing the real models (and your saved choice) the next time you open Settings — you don't have to refresh every time. Refreshing again reports what changed since last time ("2 new, 1 removed"). You can also choose **Custom** and type any model ID yourself.
6. Press **Test Connection** — the manager makes one tiny request and speaks whether it worked, so you can confirm the key before relying on it.
7. Save.

### Using it

On the **SMAPI Log** tab (Stardew Valley) or the **Log** tab (Skyrim / Fallout 4), press **F9** (or **View** menu → "Diagnose Log with AI"). If a log line is selected, the manager sends the lines around it; otherwise it sends the end of the log (where errors usually are), along with your enabled mod list for context. It speaks "Analyzing…", then opens a chat window with the explanation and fix steps and reads it aloud.

**Keep the conversation going.** That window has a **follow-up box** and a **Send** button — type another question ("what if that doesn't work?", "which file exactly?") and press **Enter** or **Send**, and the AI answers with the earlier exchange as context. Each reply is read aloud and added to the transcript above. Press **Close** or **Escape** when you're done.

### Other ways to ask

The same AI help is available beyond logs:

*   **When a mod fails to install** — the failure message offers to ask your AI provider what went wrong.
*   **In the File Conflict and Check Mod Requirements reports** — arrow to a flagged item and press **F9** to ask the AI about that specific finding.
*   **Any modding question** — **View** menu → **Ask AI a Question…**, type your question, and chat about the answer.

A few things to know:

*   It's **opt-in and pay-as-you-go** — nothing is sent anywhere unless you've enabled AI and entered your own key, and each question costs a small amount on your provider account (usually a fraction of a cent on a cheaper model).
*   What's sent is the **relevant excerpt or question plus your enabled mod list** — no personal files.
*   If AI isn't set up, these actions explain how to turn it on rather than doing anything.

---

## Sound Cues Explained
The manager uses audio cues to provide feedback. Demo these under **Help -> Sound Demo**.

The sounds are kept short on purpose. They play while your screen reader is talking, so the sound tells you what happened without interrupting the sentence.

1.  **Connect**: Played when you connect to where the game's mods come from, such as logging in to Nexus Mods. **For Minecraft** it plays when you join a multiplayer server instead, since Modrinth has no account to log in to. The manager reads this from the game's own log.
2.  **Disconnect**: Played when the API key is invalid, you are logged out, or the program closes. **For Minecraft** it plays when you leave a multiplayer server.
3.  **Enable**: Played when one or more mods are enabled.
4.  **Disable**: Played when one or more mods are disabled or deleted.
5.  **Error**: Played when something fails: a download, an install, or a check.
6.  **Loading Indicator**: A pulsing sound that plays while a background update check is running.
7.  **Load Complete**: Played when an operation finishes: an install, an update, an import, a check, or a load-order sort.
8.  **Logo**: The startup sound. A theme can hold several; see "Splash Screen Customization" below.

**Every game sounds different.** The sound theme follows the game you are managing, so you can tell by ear which game you have loaded, before anything is read out. Each game's sounds are in its own theme; see "Audio Theme Packs" below.

You can turn all of these sounds off with the **Enable UI Sounds** checkbox at the top of the **Audio** tab in Settings (**Ctrl + P**). The spoken download/install progress feedback is separate and stays available — see "The Settings Dashboard" above.

---

## Advanced Features

Beyond the basics of enabling and updating mods, Kinetix Mod Manager includes several power-user features. Each one has its own topic in the contents list, just below this one. Here is what each covers:

*   **Mod Profiles**: Save different enabled/disabled mod setups for different playthroughs and switch between them; profiles also remember your audio theme.
*   **Automatic & Smart Backups**: The manager zips your mods before every update, re-installation, or deletion, and keeps a configurable number of recent backups.
*   **Audio Theme Packs**: Customise every sound the app makes, create your own themes, and switch between them.
*   **Splash Screen Customization**: Randomise the startup logo sound or pick a specific one.
*   **Management Safety**: Settings, Shortcut, and Theme windows use a Save/Cancel system, so pressing Escape or Cancel discards your changes.
*   **Log Search and Jump (Stardew Valley)**: Search the SMAPI log and jump straight to a matching line in context.
*   **Free Up Space**: Clear the temporary files the manager leaves behind, and see how much was reclaimed.

### 1. Mod Profiles
Profiles allow you to have different mod setups for different playthroughs. Save your current list with **Ctrl + S**, and switch between them in the **Profiles** tab. Profiles also remember your active **Audio Theme**.

### 2. Automatic & Smart Backups
Before every update, re-installation, or deletion, the manager automatically zips your current mod folder. By default, it keeps the **last 5 backups** for each mod to save space. You can adjust this limit in **Settings**.

### 3. Audio Theme Packs
You can customize all application sounds via **Help -> Audio Theme Manager**. Create new themes, open their folders to drop in custom `.ogg` files, and switch between them in **Settings**.

**Which theme plays.** Each theme is a folder inside the `sounds` folder where the manager is installed. Every game has its own theme, named after the game: `Stardew Valley`, `Skyrim`, `Fallout 4`, `Moonlight Peaks`, `The Witcher 3` and `Minecraft`. Load a game and its theme plays. To pick a theme yourself instead, check **Set theme manually** on the **Audio** tab in Settings.

**How a theme is laid out.** Inside a theme folder there is one folder per sound, named exactly:

*   `connect`
*   `disconnect`
*   `enable`
*   `disable`
*   `error`
*   `loading_indicator`
*   `load_complete`
*   `logo`

Put one `.ogg` file in each folder; its file name does not matter. The `logo` folder is the exception: it can hold as many `.ogg` files as you like, and you choose between them in Settings. See "Sound Cues Explained" above for when each sound plays.

**A theme does not have to be finished.** Any sound a theme is missing plays the **Default** theme's sound instead. So a half-made theme is never silent, just partly Default. Nothing else is needed to add sounds: no setting to change and nothing to register. Drop the files in and they are used.

**Keep sounds short**, for the reason given in "Sound Cues Explained": they play over your screen reader.

### 4. Splash Screen Customization
If you have multiple audio files in your theme's `logo` folder, you can:
*   **Enable Randomization**: The manager will pick a different logo sound every time it starts.
*   **Select a Specific Logo**: Disable randomization in **Settings** and choose your favorite sound from the list. You can press **Space** in the settings list to preview the sound.

### 5. Management Safety
All management windows (Settings, Shortcuts, Themes) now feature a **Save and Cancel** system. Pressing **Escape** or the **Cancel** button will discard any changes made since the window was opened, confirmed by a spoken audio cue.

### 6. Log Search and Jump (Stardew Valley)
Troubleshooting large logs is easier with the Search feature in the **SMAPI Log** tab (available when Stardew Valley is the active game). Search for a keyword (like "Error" or a mod name) to filter the list. Selecting a result and pressing **Enter** will restore the full log and position you exactly at that line, allowing you to read the context surrounding the event.

### 7. Free Up Space (Clear Temporary Files)
Over time the manager can leave temporary files behind — the folder it extracts app updates into, and staging folders from installs that were interrupted by a crash. Choose **View → Free Up Space (Clear Temporary Files)** to clean them up; it tells you how much was reclaimed, for example *"Freed 84.2 MB of temporary files."* It only removes the manager's own leftover scratch (never your downloaded mod archives — remove those individually from **Reinstall a Downloaded Mod**), and never touches anything from an install that's still in progress.

---

## Supporting Development

Kinetix Mod Manager is free, and built and maintained in the author's spare time. If you'd like to help keep it going, choose **Help → Support Development (Donate)**.

The view opens with a short message from the author, and the message itself lists both addresses in full so you can read them with your screen reader rather than take them on trust. **Tab** from the message to reach three buttons:

*   **Donate with PayPal** — opens `paypal.me/chipper15` in your browser.
*   **Donate with Cash App** — opens the Cash App payment page for `$SeanTerry01` in your browser.
*   **Copy Cash Tag** — copies `$SeanTerry01` to the clipboard, which is the easier route if you're going to pay from the Cash App on your phone rather than in a browser. It confirms out loud when the copy has been made.

**Escape** closes the view, and nothing here ever prompts you, nags you, or interrupts anything else in the manager — it's a menu item you visit only if you want to.

If a browser can't be opened (which can happen on Linux under Wine), the manager copies the link to the clipboard instead and tells you so, rather than failing silently.

---
*Happy Modding!*
