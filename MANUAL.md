# Kinetix Mod Manager - User Manual

Welcome to Kinetix Mod Manager! This is a fully keyboard-driven, screen-reader-accessible mod manager built for the blind and visually impaired gaming community. It currently supports **Stardew Valley**, **Skyrim Special Edition**, and **Fallout 4**, and works with NVDA, JAWS, and SAPI-based screen readers via Tolk.

You choose which game you're managing from the **Games** menu (press **Alt**, then arrow to **Games**), and the manager tailors its mod list, updates, wiki, and other features to that game. Most of this manual applies to every supported game; where something is specific to one game — such as the SMAPI log viewer for Stardew Valley — it is called out.

## Getting Started & First-Time Setup

1.  **Your First Launch**: When you run the manager for the first time, the **Settings Dashboard** will automatically open. This is to ensure the active game's mods folder is detected correctly and to give you a chance to enter your Nexus Mods API Key.
2.  **Configuring Paths**: The app attempts to find your mods folder automatically for the selected game (the `Mods` folder for Stardew Valley, or the `Data` folder for Skyrim Special Edition and Fallout 4). If it succeeds, the path will be pre-filled. If not, please use the "Browse" button in settings to select it.
3.  **Nexus Integration**: To search for mods or check for updates, you **must** provide a **Nexus Mods API Key** (sometimes called a Nexus ID). This is a standard requirement for all mod managers. See the **"Nexus Mods Setup"** section below for instructions on how to get yours for free.
4.  **Closing Settings**: If you aren't ready to configure everything yet, you can press **Escape** or click **Cancel** to close the settings and browse the app. You can reopen this screen at any time by pressing **Ctrl + P**.
5.  **Getting Help**: 
    *   Press **F1** at any time to open this manual. It opens as a navigable list: arrow **Up / Down** through the sections, press **Right Arrow or Enter** to open a section that has sub-topics, **Left Arrow or Backspace** to go back, and **Tab** to move into the text on the right and read it line by line.
    *   Press **F2** at any time to open the **Change Log**. It uses the same navigable window — a list of versions you open (with **Right Arrow or Enter**) to read what changed in each one.
    *   Press **Shift + F1** while on any tab to hear a context-sensitive list of shortcuts for that specific area.
6.  **Splash Screen**: On every startup, an audio logo plays. You can press **Enter** to skip it and go straight to the main window.

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

*   **Paths & Account** — Choose which game you're configuring, set its mods and game folders (with **Browse** buttons), and enter your **Nexus API Key**.
*   **Startup** — Show or hide the splash screen, choose whether to check for mod and manager updates at launch, and turn the spoken **welcome** and **goodbye** messages on or off.
*   **Audio** — All sound options (see below).
*   **Display** — Low-vision visual options: a **high-contrast colour scheme** (white-on-black, yellow-on-black, or black-on-yellow) and a **text size** (Normal, Large, or Extra Large). Both apply across the whole program and take effect as soon as you save — no restart needed. They change only what's drawn on screen and never affect screen-reader speech, so leaving them at their defaults keeps the normal appearance.
*   **Mods & Search** — Search results per load, maximum backups kept per mod, and whether to save your search history.
*   **AI** — Turn on optional AI features, pick an AI provider and model, and enter your own API key (see "AI Log Diagnosis" below).
*   **Language** — Pick the manager's display language, or leave it on **Automatic** to follow Windows.

### The Audio tab in detail

*   **Enable UI Sounds** — This is the **first** control on the tab and acts as a master switch for the manager's sound effects (the connect, enable, disable, error sounds, and the startup logo). **Uncheck it to turn all of those sounds off.** When it is unchecked, the rest of the audio options (volume, sound theme, and the logo selector) are **hidden**, since they no longer apply — leaving only the download and install feedback option, which is controlled separately and still works even with UI sounds off.
*   **Sound Volume** — A dropdown from 0 to 100 for the overall loudness of those sound effects.
*   **Set theme manually / Current Audio Theme** — By default the sound theme follows the game you're managing; check **Set theme manually** to choose a specific theme from the dropdown instead.
*   **Random Logo at Startup / Select Specific Logo** — These appear only when **Show Splash Screen** (on the Startup tab) is enabled, and let you pick which startup logo sound plays.
*   **Download and install feedback** — Choose how long downloads and installs report progress: **Tones**, **Speech**, **Both**, or **Off**. This is independent of the **Enable UI Sounds** switch above, so you can keep progress feedback even with the other sounds turned off (or vice versa).

---

## Keyboard Shortcuts

Kinetix Mod Manager is fully keyboard-driven. The shortcuts are grouped by where they apply, and each group has its own topic in this manual's contents list, just below this one. Here is what each group covers:

*   **Global Shortcuts**: Keys that work anywhere in the app — opening this manual, context help, launching the game, cycling focus with F6, opening Settings, logging in with your Nexus key, checking your remaining Nexus API requests, opening the downloads, backups, and error-log folders, and more.
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
*   **F3**: Open the **Mod Documentation** viewer for the active game's accessibility mod (see "Mod Documentation Viewer" below).
*   **Shift + F1**: **Context Help** - Speaks the shortcuts for your current tab.
*   **F5**: Launch the active game through its mod loader (SMAPI for Stardew Valley, SKSE for Skyrim Special Edition, F4SE for Fallout 4).
*   **F6**: **Cycle Focus** - Jump between the tab headers and the primary list in each tab (and the web view in the Wiki tab).
*   **F9**: **Diagnose Log with AI** - Send the current SMAPI or game log to your chosen AI provider for a plain-language explanation and fixes (opt-in; see "AI Log Diagnosis" below).
*   **Alt**: Access the Menu Bar.
*   **Ctrl + P**: Open the **Settings Dashboard**.
*   **Ctrl + L**: Change/Login with Nexus API Key.
*   **Ctrl + Shift + A**: Speak your remaining Nexus **API requests** for this hour and today (see "Checking Your Nexus API Requests" below).
*   **Ctrl + D**: Open your `downloads` folder.
*   **Ctrl + B**: Open your `backups` folder.
*   **Ctrl + Shift + L**: Open the error log.
*   **Ctrl + H**: Open the **Accessibility Controls** viewer for the active game (a navigable drill-down of every control — see "Accessibility Controls Viewer" below).
*   **Ctrl + Shift + H**: Open your **Search History** for the active game (see "Searching for Mods" below).
*   **Escape**: Close the Manual, Settings, or Sound Demo windows.

### Mod List Shortcuts (Installed Mods Tab)
*   **Space**: Enable or Disable the selected mod.
*   **Delete**: Permanently delete the selected mod folder (creates a backup first).
*   **Ctrl + F**: **Search** - Focus the search bar to filter your installed mods.
*   **Ctrl + J**: **Change Category** - Assign a custom category to the selected mod.
*   **Ctrl + Shift + J**: **Batch Category Action** - Enable or Disable all mods in the currently filtered category.
*   **Ctrl + Shift + O**: **Add or Edit Note** - Attach a personal note to the selected mod, spoken whenever you select it (see "Mod Notes" below).
*   **Ctrl + S**: **Save Profile** - Saves your current enabled/disabled mod setup.
*   **Ctrl + Shift + X**: **Export Collection** - Save your current mods as a shareable Collection file (see "Mod Collections" below).
*   **Ctrl + Shift + N**: **Install Collection** - Install a Collection file (see "Mod Collections" below).
*   **Ctrl + G**: Open the mod's page on Nexus Mods.
*   **Ctrl + Shift + E**: **Endorse or un-endorse** the selected mod on Nexus Mods (see "Endorsing Mods" below).
*   **Ctrl + Shift + G**: **View Changelog** - the selected mod's version history from Nexus (see "Mod Changelog and Full Description" below).
*   **Ctrl + Shift + I**: **View Full Description** - the selected mod's complete Nexus page description (see below). Both also work in the Updates and Find New Mods lists.
*   **Ctrl + Y**: **View Dependencies** - Opens the dependency view for the selected mod: what it *requires* (and whether each is installed) and what is *required by* it (other installed mods that depend on it). See "Dependency View and Resolver" below.
*   **Ctrl + Q**: **Resolve Missing Requirements** - Finds every missing required mod for the selected mod and, on Skyrim/Fallout 4, offers to download and install them automatically. See "Dependency View and Resolver" below.
*   **Ctrl + Shift + B**: **Check for Broken Mods** - Cross-references your installed mods against the community compatibility list and reports any known to be broken, abandoned, obsolete, or incompatible. See "Checking Your Mods" below.
*   **Ctrl + K**: Manually assign a Nexus ID.
*   **Ctrl + I**: Install a mod from an archive file. **`.zip`, `.7z`, and `.rar`** archives are all supported.
*   **Ctrl + R**: **Read Description** - Speaks the full summary of the mod.
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
    *   In **Updates**: Open the Nexus page for the update.
    *   In **Search for Mods**: Open the Nexus page for the selected mod — **or**, on the **"Load more results"** row at the bottom of the list, load the next batch of results.
*   **Delete**: (In Updates tab) **Ignore Update** - Hides this specific version from the updates list.
*   **Ctrl + U**: **Update All** mods (Premium only).
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
The Wiki tab (e.g. **Stardew Wiki**, **Skyrim Wiki**, or **Fallout 4 Wiki**) provides a built-in, accessible way to browse the official game wiki.
1.  **Search**: Type any item, quest, villager, or mechanic into the search box and press **Enter**.
2.  **Browse by Category**: Use the **Categories** dropdown to quickly find lists of pages — for example Villagers, Crops, and Fish in Stardew Valley, or Quests, Factions, Weapons, and Locations in Skyrim and Fallout 4. The categories listed always match the wiki you are currently browsing.
3.  **Switch Wikis with the Mod Wikis dropdown**: The **Mod Wikis** dropdown lets you choose which wiki you are searching. Alongside the main game wiki, it lists dedicated wikis for popular content mods — world-expansion wikis for Stardew Valley (such as Stardew Valley Expanded, Ridgeside Village, and East Scarp), and large quest or new-land mod wikis for Skyrim and Fallout 4 (such as the Elder Scrolls Mods Wiki, Legacy of the Dragonborn, and Sim Settlements 2). Choosing a wiki points the Search box, the Categories dropdown, and the Web View at that wiki. A few wikis are "browse-only" (they open in the Web View, but can't be searched in-app); the manager tells you when that's the case.
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
The Walkthroughs tab (e.g. **Stardew Walkthroughs**, **Skyrim Walkthroughs**, or **Fallout 4 Walkthroughs**) lists high-quality, text-only walkthroughs and community guides for the selected game.
1.  **Guides List**: Select a guide from the list using the Up/Down arrow keys. The guide content automatically loads in the Web View on selection.
2.  **Accessible Reading**: Press **F6** or **Tab** to enter the **Web View** pane. Since these walkthroughs are text-based guides, screen reader browse-mode commands (like **H** to jump between headings and **T** for tables) work seamlessly to make navigation easy for blind players.
3.  **Exit Content**: Press **F6** at any time to escape the web view and return to the main tab headers.

---

## Searching for Mods
The **Search for Mods** tab allows you to browse and search for mods on Nexus Mods without leaving the app.
1.  **Search**: Type a mod name in the search box and press Enter.
2.  **Types**: Use the **Type** dropdown to see **Trending**, **Most Popular**, or **Recent** mods.
3.  **Language Filter**: Use the **Language** dropdown to restrict results to a single language. It defaults to **English**, and your choice is remembered between sessions. The list is built from the languages that actually have mods for the game you're currently managing, with a count beside each — for example "English (16362)". Choose **"Any language"** at the top of the list to turn the filter off and see results in every language. The filter applies to keyword searches and to the Trending, Most Popular, and Recent lists alike.
    *   *Note:* The language comes from how each mod's author tagged it on Nexus Mods, so on rare occasions a mod tagged incorrectly by its author may still appear. Switching to "Any language" always shows the complete results.
4.  **Results per load**: Use the **Results** dropdown to choose how many results to load at a time (10, 20, 30, 50, or 100). Changing it here applies to your current searches only; to make a number stick between sessions, set **"Search Results per Load"** in **Settings** instead — the Search tab then starts from your saved choice.
5.  **Loading more results**: When more results are available than were loaded, a **"Load more results"** row sits at the very bottom of the results list. Arrow down to it and press **Enter** to load the next batch — the new results are added to the list, your focus lands on the first new one, and the "Load more results" row moves to the new bottom. It isn't counted in the "X of Y" position announcements, so the real results still read "1 of 20", "2 of 20", and so on.
6.  **Search History**: The manager can remember the searches you run so you can browse and repeat them. It is **off by default** — turn on **"Save Mod Search History"** in **Settings** to start recording. Once it's on, open your history with the **History** button beside the search box, or by pressing **Ctrl + Shift + H** anywhere. In the history window:
    *   Choose **"All searches"** or a specific **date** from the **Show** dropdown (most recent first), then press **Tab** to move into the list of terms.
    *   Press **Enter** on a term to run that search again — the manager fills the search box and searches straight away.
    *   Press **Clear History** to erase it (you'll be asked to confirm first).
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

## Accessibility Controls Viewer (Ctrl + H)

Press **Ctrl + H** at any time to open the **Accessibility Controls** viewer for the active game. It gathers, in one place, the keyboard and gamepad controls you actually have — so you can look up "what does this key do" without leaving the manager.

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

---

## Mod Documentation Viewer (F3)

Press **F3** at any time (or choose **Help → Mod Documentation**) to open the how-to-play documentation for the accessibility mod that matches your current game — **Stardew Access** for Stardew Valley, **Skyrim Access** for Skyrim Special Edition, or **Fallout 4 Access** for Fallout 4. This saves you hunting for a README on your computer or searching for it on GitHub.

It opens in the **same navigable window as the User Manual**:

*   The window starts on a short list of the available documents (the **chooser**). **Up / Down Arrow** move through it.
*   **Right Arrow or Enter**: Open the selected document — or, once inside, open a section that has sub-topics — moving into its deeper list.
*   **Left Arrow or Backspace**: Go back up to the previous list.
*   **Tab**: Move into the read-only text on the right and read the selected section line by line. **Shift + Tab** returns to the list.
*   **Shift + F1**: Hear help for this window. **Escape** closes it.

How it stays current and works offline:

*   A copy of each guide **ships with the manager**, so the viewer opens instantly and works with no internet connection.
*   In the background it also **refreshes from the source** — the live GitHub documentation for Stardew Access, or (when you are logged in to Nexus) the mod's current Nexus page for Skyrim Access and Fallout 4 Access. The refreshed copy is shown the **next** time you open the viewer.
*   For exact, up-to-the-minute keybindings of an installed mod, the **Accessibility Controls** viewer (**Ctrl + H**) reads the live keys from the mod's config/MCM; the Mod Documentation viewer is the broader "how do I use this mod" guide.

F3 is the default shortcut and can be remapped in **Settings → Shortcut Manager** (the action is named "ModDocs").

---

## Accessibility Suite Installer
Each supported game needs a set of foundation mods to be accessible (its mod loader plus the screen-reader and helper mods). The **Accessibility Suite Installer** gathers them all in one place. Open it from the **Mods** menu → **"Install [Game] Accessibility Suite"** (the exact name reflects the active game).

The dialog shows a **status list**, with one line per required mod telling you whether it is **Installed** or **Not Installed**. The suite differs per game — for example SMAPI and Stardew Access for Stardew Valley; SKSE64, the Address Library, SkyUI, and others for Skyrim Special Edition; and F4SE, the Address Library, the Mod Configuration Menu, and Fallout 4 Access for Fallout 4.

There are two ways to get the missing mods:

1.  **Install everything at once**: Tab to the **"Install Missing Suite Mods"** button and press Enter or Spacebar. The manager downloads and installs every missing mod for you. (If you are a Nexus Premium member, Nexus mods install automatically; otherwise the manager opens each mod's page so you can download it manually.) This is the quickest option, but with several mods it can mean a lot of pages opening at once.
2.  **Get them one at a time, at your own pace**: Select a mod in the status list and press **Enter**. The manager opens just that one mod's download page — the **Files** tab on Nexus Mods, or the official site / GitHub releases page for other mods. From there you can download it with **either** the **Mod Manager Download** button (the manager will then offer to install it for you) **or** the **Manual Download** button (then press **Ctrl + I** to install the ZIP) — see **"Downloading Mods from Nexus Mods"** above for the full steps. Repeat for each mod whenever you're ready. This is handy when several mods are needed and opening them all together would be overwhelming.

You can mix the two approaches freely, and you can re-open the installer at any time to check what's still missing.

**Uninstalling the script extender (Skyrim & Fallout 4):** SKSE and F4SE install into the game folder itself rather than as normal mods, so they don't show up in your mod list. To remove one cleanly, open the **Mods** menu and choose **"Uninstall Script Extender (SKSE/F4SE)"**. The manager removes the files it installed (asking you to confirm first). Remember that anything relying on the script extender — including the Mod Configuration Menu — stops working until you reinstall it, so only do this if you mean to.

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

## Smart Game Launching (F5)
The manager does more than just launch the game:
*   It announces when it starts launching the game through its mod loader (SMAPI for Stardew Valley, SKSE for Skyrim Special Edition, F4SE for Fallout 4).
*   It speaks and displays *"Game is loaded and running"* once the game is active.
*   It instantly detects when you close the game and announces *"Game closed."*
*   **Script-extender version check (Skyrim & Fallout 4):** before launching, it checks that the installed **SKSE/F4SE matches your game's version**. SKSE and F4SE only work when built for the exact game build, so if your game has updated and the script extender no longer matches, it silently won't load — which is the usual reason the **Mod Configuration Menu** and other script-extender features suddenly disappear. When this happens the manager **warns you and asks whether to launch anyway**; to fix it, reinstall the script extender from the Accessibility Suite so it matches your game.

---

## Load Order Management (Skyrim & Fallout 4)

Skyrim Special Edition and Fallout 4 decide how mods combine using **load order**, and the manager gives you dedicated tabs for it. They appear automatically for those games, right after the Installed Mods tab. Stardew Valley does not use load order, so these tabs don't appear for it.

### Mod Priority
When two mods provide the same loose file, the one with **higher priority wins**. The **Mod Priority** tab lists your mods highest-priority-first (the top of the list wins conflicts). Each row also tells you how many files that mod **overrides** or **is overridden in**, so you can judge its standing by ear. Press **Ctrl + Up** or **Ctrl + Down** to move the selected mod higher or lower; the change is applied immediately and the new position is announced.

### Plugin Order
The **Plugin Order** tab is the load order of your plugin files (the `.esp`, `.esm`, and `.esl` files), written to `plugins.txt`. Plugins higher in the list load first. Masters and light masters always load before regular plugins, and the base game and its add-ons are handled automatically and not listed. Press **Ctrl + Up / Ctrl + Down** to move a plugin (it can't cross the boundary between masters and regular plugins). Press **F8** to **auto-sort**: the manager arranges every plugin to load after the masters it needs, using LOOT's community rules when it can reach them, and a master-dependency sort otherwise.

### Creations
**Creations** (formerly Creation Club content) are official add-ons you download **inside the game**, from its **Creations** menu — or queue from the Bethesda.net website to your linked account. No mod manager downloads them for you; the game installs them into its own folder. The **Creations** tab lists the Creations already installed in your game, whether each is **Active**, and whether it's a master or light master. Press **Space** to activate or deactivate the selected one. To change where a Creation loads, use the Plugin Order tab.

### Exporting and Importing Your Load Order
From the **Mods** menu you can **Export Load Order** to save your current mod priority and plugin order to a file, and **Import Load Order** to apply a saved file later — for example as a backup, or to move a setup between computers. Importing replaces the current order and re-applies it; it never adds or removes your mods, and it only accepts a file that was exported for the same game.

---

## Checking Your Mods: Conflicts and Requirements

The reports on the **Mods** menu help you spot problems. Each opens as a simple list you can arrow through, and **Escape** closes them.

### File Conflict Report (Ctrl + Shift + F)
This shows where your mods collide.

*   **Skyrim & Fallout 4:** it lists every loose file that **more than one enabled mod provides**. Each line names the file, the mod whose copy **wins**, and the mods it **overrides**. This is the detailed view behind the "overrides / overridden in" counts on the Mod Priority tab — use it to decide whether your priority order is putting the right mod on top.
*   **Stardew Valley:** Stardew mods each load from their own folder and never overwrite each other, so there are no file conflicts. Instead the report lists any mods that **share a UniqueID**, which stops SMAPI from loading them — something you'd want to fix by removing the duplicate.

### Check Mod Requirements (Ctrl + Shift + Q)
This scans your **enabled** mods and lists anything they need but don't have. Press **Enter** on a line to act on it.

*   **Stardew Valley:** required mods that are **missing, disabled, or older** than a mod asks for — including the host mod a content pack (such as a Content Patcher pack) needs. Enter offers to **search** for the missing mod in the Find New Mods tab.
*   **Skyrim & Fallout 4:** plugins whose **master file isn't installed** (a missing master stops a plugin loading), a **missing script extender** (SKSE/F4SE), and each mod's **Nexus "Requirements"** that you don't have installed. Enter opens the missing mod's page. Because the Nexus part checks each mod online, a large load order can take a moment — the title bar shows the progress.

### Check for Broken Mods (Ctrl + Shift + B)
This checks your installed mods against a community-maintained compatibility list and reports the ones known to have problems — issues that no Nexus update would tell you about. It needs an internet connection; if the list can't be reached it simply says so.

*   **Stardew Valley:** uses the official **SMAPI compatibility list**. It flags mods marked **Broken**, **Obsolete**, or **Abandoned**, with a short note explaining the status. Press **Enter** on a mod to open its Nexus page.
*   **Skyrim & Fallout 4:** uses the **LOOT masterlist**. It flags a plugin that is **incompatible with another plugin you also have installed**, plus any curated **warning or error** notes LOOT records for your active plugins. Only unconditional warnings are shown, so an item appears only when it definitely applies.

If nothing is flagged, the report tells you none of your mods are on the known-broken list.

---

## Dependency View and Resolver

Two tools on the Installed Mods tab (and the **Mods** menu) help you manage what your mods depend on.

*   **View Dependencies (Ctrl + Y)** opens a list with two parts for the selected mod: **Requires** (each dependency and whether it's installed, disabled, out of date, or missing) and **Required by** (the other installed mods that depend on this one). On Stardew this reads each mod's manifest; on Skyrim/Fallout 4 it reads plugin masters and, when you're logged in, the mod's online Nexus requirements. Press **Enter** on a missing item to search for or open it.
*   **Resolve Missing Requirements (Ctrl + Q)** gathers **all** of the selected mod's missing required mods at once. On Skyrim/Fallout 4, if you have a Nexus **Premium** account it downloads and installs each one automatically (opening the accessible FOMOD wizard when a mod needs setup choices); free accounts and off-Nexus requirements are listed in a report you can open and download manually. On Stardew it lists every missing required mod so you can search for each. You're asked to confirm once before anything downloads.

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

If you're moving to Kinetix Mod Manager from **Mod Organizer 2 (MO2)**, you can bring your existing setup across. Open the **Mods** menu and choose **"Import from Mod Organizer 2"**.

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

(For Stardew Valley, the equivalent is the **SMAPI Log** tab described elsewhere in this manual.)

---

## AI Log Diagnosis

When a log has you stuck, Kinetix can send it to an AI service that reads it and explains — in plain language — what's going wrong and how to fix it, with numbered steps. This is **optional and off by default**. The manager's own instant, offline checks (like the SMAPI log's Quick-Fix) are always your first line; the AI is the "I'm still stuck" step.

### One-time setup (Settings → AI tab)

1. Open **Settings** (Ctrl + P) and go to the **AI** tab. (While "Enable AI features" is unchecked, the rest of the tab is hidden; checking it reveals the settings below and is announced aloud.)
2. Check **Enable AI features**.
3. Choose an **AI Provider** — **Anthropic (Claude)**, **OpenAI (GPT)**, **Google (Gemini)**, or **OpenAI-compatible / Custom endpoint**. The last one works with any service that speaks OpenAI's API — OpenRouter, or a local server like Ollama or LM Studio — and shows an extra **Endpoint base URL** box (enter the URL the service gives you, usually ending in `/v1`). For a local server that needs no key, put any placeholder such as `local` in the key box.
4. Paste your **API key** for that provider. You create the key on the provider's website; usage is billed to **your** account. The key is stored **encrypted** on your PC (the same protection used for your Nexus key) and is kept per-provider, so you can switch providers without re-entering keys. The help text under the key box tells you where to get a key for the chosen provider.
5. Choose a **Model**. The list starts with a few common models; press **Refresh model list** to pull the provider's **current** catalog using your key (so new models show up and retired ones drop off), or choose **Custom** and type any model ID yourself.
6. Press **Test Connection** — the manager makes one tiny request and speaks whether it worked, so you can confirm the key before relying on it.
7. Save.

### Using it

On the **SMAPI Log** tab (Stardew Valley) or the **Log** tab (Skyrim / Fallout 4), press **F9** (or Tools menu → "Diagnose Log with AI"). If a log line is selected, the manager sends the lines around it; otherwise it sends the end of the log (where errors usually are), along with your enabled mod list for context. It speaks "Analyzing…", then opens a chat window with the explanation and fix steps and reads it aloud.

**Keep the conversation going.** That window has a **follow-up box** and a **Send** button — type another question ("what if that doesn't work?", "which file exactly?") and press **Enter** or **Send**, and the AI answers with the earlier exchange as context. Each reply is read aloud and added to the transcript above. Press **Close** or **Escape** when you're done.

### Other ways to ask

The same AI help is available beyond logs:

*   **When a mod fails to install** — the failure message offers to ask your AI provider what went wrong.
*   **In the File Conflict and Check Mod Requirements reports** — arrow to a flagged item and press **F9** to ask the AI about that specific finding.
*   **Any modding question** — Tools menu → **Ask AI a Question…**, type your question, and chat about the answer.

A few things to know:

*   It's **opt-in and pay-as-you-go** — nothing is sent anywhere unless you've enabled AI and entered your own key, and each question costs a small amount on your provider account (usually a fraction of a cent on a cheaper model).
*   What's sent is the **relevant excerpt or question plus your enabled mod list** — no personal files.
*   If AI isn't set up, these actions explain how to turn it on rather than doing anything.

---

## Sound Cues Explained
The manager uses audio cues to provide feedback. Demo these under **Help -> Sound Demo**.

1.  **Connect**: Played on successful login.
2.  **Disconnect**: Played when the API key is invalid, you are logged out, or when the program closes.
3.  **Enable**: Played when one or more mods are enabled.
4.  **Disable**: Played when one or more mods are disabled or deleted.
5.  **Error**: Played when a download fails or an installation error occurs.
6.  **Loading Indicator**: A pulsing sound that plays while the app is checking for updates.
7.  **Load Complete**: Played when the update check is finished.

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

### 1. Mod Profiles
Profiles allow you to have different mod setups for different playthroughs. Save your current list with **Ctrl + S**, and switch between them in the **Profiles** tab. Profiles also remember your active **Audio Theme**.

### 2. Automatic & Smart Backups
Before every update, re-installation, or deletion, the manager automatically zips your current mod folder. By default, it keeps the **last 5 backups** for each mod to save space. You can adjust this limit in **Settings**.

### 3. Audio Theme Packs
You can customize all application sounds via **Help -> Audio Theme Manager**. Create new themes, open their folders to drop in custom `.ogg` files, and switch between them in **Settings**.

### 4. Splash Screen Customization
If you have multiple audio files in your theme's `logo` folder, you can:
*   **Enable Randomization**: The manager will pick a different logo sound every time it starts.
*   **Select a Specific Logo**: Disable randomization in **Settings** and choose your favorite sound from the list. You can press **Space** in the settings list to preview the sound.

### 5. Management Safety
All management windows (Settings, Shortcuts, Themes) now feature a **Save and Cancel** system. Pressing **Escape** or the **Cancel** button will discard any changes made since the window was opened, confirmed by a spoken audio cue.

### 6. Log Search and Jump (Stardew Valley)
Troubleshooting large logs is easier with the Search feature in the **SMAPI Log** tab (available when Stardew Valley is the active game). Search for a keyword (like "Error" or a mod name) to filter the list. Selecting a result and pressing **Enter** will restore the full log and position you exactly at that line, allowing you to read the context surrounding the event.

---
*Happy Modding!*
