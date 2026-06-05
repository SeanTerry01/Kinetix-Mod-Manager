# Stardew Valley Accessible Mod Manager - User Manual

Welcome to the Stardew Valley Accessible Mod Manager! This tool is designed for accessibility, providing full keyboard control and screen reader compatibility for managing your Stardew Valley mods.

## Getting Started & First-Time Setup

1.  **Your First Launch**: When you run the manager for the first time, the **Settings Dashboard** will automatically open. This is to ensure your Stardew Valley Mods path is detected correctly and to give you a chance to enter your Nexus Mods API Key.
2.  **Configuring Paths**: The app attempts to find your Mods folder automatically. If it succeeds, the path will be pre-filled. If not, please use the "Browse" button in settings to select your Stardew Valley `Mods` folder.
3.  **Nexus Integration**: To search for mods or check for updates, you **must** provide a **Nexus Mods API Key** (sometimes called a Nexus ID). This is a standard requirement for all mod managers. See the **"Nexus Mods Setup"** section below for instructions on how to get yours for free.
4.  **Closing Settings**: If you aren't ready to configure everything yet, you can press **Escape** or click **Cancel** to close the settings and browse the app. You can reopen this screen at any time by pressing **Ctrl + P**.
5.  **Getting Help**: 
    *   Press **F1** at any time to open this manual.
    *   Press **Shift + F1** while on any tab to hear a context-sensitive list of shortcuts for that specific area.
6.  **Splash Screen**: On every startup, an audio logo plays. You can press **Enter** to skip it and go straight to the main window.

---

## Nexus Mods Setup: Obtaining Your API Key

To use the manager's update and discovery features, you must have a free account on Nexus Mods and provide your unique **API Key** to the app.

### 1. Create a Nexus Mods Account
*   Go to **[nexusmods.com](https://www.nexusmods.com)**.
*   Click **"Register"** in the top right corner.
*   Follow the instructions to create and verify your account.

### 2. Find Your Personal API Key
Once you are logged in to the website:
*   Click your **User Icon** (avatar) in the top right corner of the page.
*   Select **"Settings"** from the dropdown menu.
*   In the settings page, click on the **"API"** tab (this is usually the last tab on the right).
*   Scroll down to the section labeled **"Personal API Key"**.
*   Click the **"Show"** button (you may need to click "Generate" first if it's your first time).
*   **Copy** the long string of letters and numbers that appears.

### 3. Enter the Key into the Mod Manager
*   Open the Mod Manager and press **Ctrl + P** to open the **Settings Dashboard**.
*   Find the box labeled **"Nexus API Key"**.
*   **Paste** your copied key into this box.
*   Press **"Save Settings"** or **Enter**.
*   The manager will play the **"Connect"** sound and display your username in the title bar if successful.

---

## Keyboard Shortcuts

### Global Shortcuts
*   **F1**: Open this User Manual (Internal Window).
*   **Shift + F1**: **Context Help** - Speaks the shortcuts for your current tab.
*   **F5**: Launch Stardew Valley (via SMAPI).
*   **F6**: **Cycle Focus** - Jump between the tab headers and the primary list in each tab (and the web view in the Wiki tab).
*   **Alt**: Access the Menu Bar.
*   **Ctrl + P**: Open the **Settings Dashboard**.
*   **Ctrl + L**: Change/Login with Nexus API Key.
*   **Ctrl + D**: Open your `downloads` folder.
*   **Ctrl + B**: Open your `backups` folder.
*   **Ctrl + Shift + L**: Open the error log.
*   **Ctrl + H**: Open the **Accessibility Controls** guide for the selected game.
*   **Escape**: Close the Manual, Settings, or Sound Demo windows.

### Mod List Shortcuts (Installed Mods Tab)
*   **Space**: Enable or Disable the selected mod.
*   **Delete**: Permanently delete the selected mod folder (creates a backup first).
*   **Ctrl + F**: **Search** - Focus the search bar to filter your installed mods.
*   **Ctrl + J**: **Change Category** - Assign a custom category to the selected mod.
*   **Ctrl + Shift + J**: **Batch Category Action** - Enable or Disable all mods in the currently filtered category.
*   **Ctrl + S**: **Save Profile** - Saves your current enabled/disabled mod setup.
*   **Ctrl + G**: Open the mod's page on Nexus Mods.
*   **Ctrl + Y**: View a detailed list of dependencies.
*   **Ctrl + Q**: **Quick-Fix** - Instantly search for a missing dependency.
*   **Ctrl + K**: Manually assign a Nexus ID.
*   **Ctrl + I**: Install a mod from a `.zip` file.
*   **Ctrl + R**: **Read Description** - Speaks the full summary of the mod.
*   **Apps Key**: View mod details.

### Profiles Tab Shortcuts
*   **Enter**: **Apply Profile** - Automatically enables/disables mods to match the saved setup.
*   **Delete**: Remove the selected profile.

### Backups Tab Shortcuts
*   **Enter**: **Restore Backup** - Re-installs that specific mod version.
*   **Delete**: Permanently remove the backup zip file.
*   **Ctrl + Shift + B**: **Prune Backups** - Deletes all but the most recent archives based on your settings.

### SMAPI Log Tab Shortcuts
*   **Ctrl + F**: Focus the **Log Search** bar.
*   **Enter (in Search bar)**: Filter the log to show only matching lines.
*   **Enter (on a search result)**: Restore the full log view and **jump** to that specific line.
*   **Ctrl + Q**: **Quick-Fix** detected issue (if a missing mod is found).
*   **Ctrl + L**: Upload log to SMAPI.io for troubleshooting help.
*   **F4**: Open the raw log file in Notepad.

### Discovery & Updates Tabs
*   **Enter**: 
    *   In **Updates**: Open the Nexus page for the update.
    *   In **Discovery**: Open the Nexus page for the selected mod.
*   **Delete**: (In Updates tab) **Ignore Update** - Hides this specific version from the updates list.
*   **Ctrl + U**: **Update All** mods (Premium only).
*   **Ctrl + R**: Read the mod's summary.

### Stardew Wiki Tab Shortcuts
*   **Enter (in Search bar)**: Search the Stardew Valley Wiki for the entered text.
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
2.  **Browse by Category**: Use the **Categories** dropdown to quickly find lists of Crops, Fish, Quests, Skills, and more.
3.  **Navigation**: The results list shows pages and sub-categories. You can "drill down" into categories by pressing **Enter** and go back up by pressing **Backspace**.
4.  **Accessible Reading**: When you press **Enter** on a page, it loads in the integrated **Web View**. This view is fully compatible with screen readers, allowing you to use standard web navigation commands:
    *   **H**: Jump between Headings.
    *   **T**: Navigate Tables.
    *   **L**: List links on the page.

---

## Walkthroughs & Guides Integration
The Walkthroughs tab (e.g. **Stardew Walkthroughs**, **Skyrim Walkthroughs**, or **Fallout 4 Walkthroughs**) lists high-quality, text-only walkthroughs and community guides for the selected game.
1.  **Guides List**: Select a guide from the list using the Up/Down arrow keys. The guide content automatically loads in the Web View on selection.
2.  **Accessible Reading**: Press **F6** or **Tab** to enter the **Web View** pane. Since these walkthroughs are text-based guides, screen reader browse-mode commands (like **H** to jump between headings and **T** for tables) work seamlessly to make navigation easy for blind players.
3.  **Exit Content**: Press **F6** at any time to escape the web view and return to the main tab headers.

---

## Mod Discovery (Find New Mods)
The **Find New Mods** tab allows you to browse and search for mods without leaving the app.
1.  **Search**: Type a mod name in the search box and press Enter.
2.  **Types**: Use the dropdown to see **Trending**, **Most Popular**, or **Recent** mods.

---

## Updating Individual Mods via Nexus Mods

When the manager detects an update, it will appear in the **Updates Available** tab. Because Nexus Mods often lists multiple versions (like optional files or older versions), follow these steps to ensure you get the correct update:

### 1. Open the Mod Page
*   Highlight the mod in the **Updates Available** tab and press **Enter**.
*   Your browser will open directly to the **Files** tab for that specific mod.

### 2. Locate the Correct File
*   Use your screen reader's "Heading" or "Link" navigation to find the **"Main Files"** section.
*   The top file is almost always the latest version. Confirm the version number matches what the manager reported.
*   Find the button or link labeled **"Mod Manager Download"** for that specific file and click it.

### 3. Handle Dependencies (If Prompted)
*   If the mod requires other mods to work, a pop-up window labeled **"Additional files required"** may appear.
*   Review the list of dependencies. If you already have them, simply find and click the **"Download"** button at the bottom of this pop-up.

### 4. The Download Page
*   You will be taken to a final download page. Find the button labeled **"Slow Download"** (this is the free option) and click it.
*   A countdown will begin, and then your browser will ask for permission to open the link with **Stardew Valley Accessible Mod Manager**. Confirm this.

### 5. Installation
*   Once the download finishes, the Mod Manager will automatically regain focus and play the **"Connect"** sound.
*   A dialog will ask: **"Downloaded [Mod Name]. Install now?"**
*   Press **Enter** (Yes) to automatically back up your old version and install the new update directly into your Mods folder.

---

## Smart Game Launching (F5)
The manager does more than just launch the game:
*   It announces when it starts launching SMAPI.
*   It speaks and displays *"Game is loaded and running"* once Stardew Valley is active.
*   It instantly detects when you close the game and announces *"Game closed."*

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

---

## Advanced Features

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

### 6. Log Search and Jump
Troubleshooting large logs is easier with the Search feature in the **SMAPI Log** tab. Search for a keyword (like "Error" or a mod name) to filter the list. Selecting a result and pressing **Enter** will restore the full log and position you exactly at that line, allowing you to read the context surrounding the event.

---
*Happy Farming!*
