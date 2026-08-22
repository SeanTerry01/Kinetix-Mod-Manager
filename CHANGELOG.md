# Version 1.5.1

A fix for Skyrim and Fallout 4: an up-to-date script extender is no longer reported as the wrong one, and you can now ask the manager which version you have. The manager also learned to explain the silent one — the game that launches perfectly and does nothing. The manual, the change log and the mod documentation all gained a proper search.

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
