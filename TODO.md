# Kinetix Mod Manager — outstanding work

Everything identified so far that is **not yet done**. Findings come from `ARCHITECTURE_REVIEW.md`,
which has the reasoning behind each; this file is the list, not the argument.

Anything resolved gets deleted from here rather than ticked, so the file stays short enough to read.

**Last updated:** 2026-09-15, after Updates and Profiles, and Minecraft stopped being hard-coded.
**State:** 1,327 tests passing on Windows and Linux; all five projects build clean, zero warnings.

---

## Done so far, for context

- ~~**Phase 0** — extract `Kinetix.Core`~~ (`04ee93c`)
- ~~**Phase 1** — the 16 tests that failed off Windows; 7 backslash path literals; the
  `Path.GetInvalidFileNameChars()` bug~~ (`b50bd25`)
- ~~**`en.json`** — two phrases read aloud with their own braces in them, plus a guard so it cannot
  recur~~ (`daf4142`)
- ~~**HTTP connection leak**, the Modrinth User-Agent regression, and speech failing silently~~ (`d727c82`)
- ~~**Phase 2** — the domain model out of `Form1`: 36 types to `Kinetix.Core/Models/`, plus `Loc`,
  `VirtualKeys`, `LoadOrderRule` and `PluginSlots`~~
- ~~**Phase 3, part one** — `IAnnouncer`, `ISoundEngine`, `ISecretStore`, `IDispatcher`, their Windows
  implementations, and `ProgressAnnouncer` moved to the core as the first customer~~
- ~~**Proton vs native** — decided: native, Minecraft + Stardew. See ARCHITECTURE_REVIEW §16~~
- ~~**GTK spike** — `Kinetix.Platform.Linux` (speech-dispatcher, verified speaking) and `Kinetix.Gtk`
  (installed mods, live Modrinth search). See §17~~
- ~~**Orca reads the in-app browser** — confirmed by ear; the last technical unknown~~
- ~~**Phase 4, six screens** — SMAPI Log, Dependencies, Profiles, Updates, Install, ModList. See §22~~
- ~~**The GTK head installs Minecraft mods** — `ModInstaller`, Install button and Ctrl+I~~
- ~~**The archive pipeline** — `ModArchive` in the core: signature routing, zip/7z/rar in managed code,
  the link-aware escape guard, nested archives, staging. `7za.exe` gone. See §23~~
- ~~**Per-game sounds, and Minecraft's connect cue** — `SoundThemes` in the core, the `sounds/Minecraft`
  scaffold, and connect/disconnect wired to joining and leaving a server. See §24~~

---

## The port, in order

The one sequence that matters. Each step leaves the app shipping and the suite green.

### 1. Phase 3, part two — the four remaining interfaces

Done: `IAnnouncer`, `ISoundEngine`, `ISecretStore`, `IDispatcher`. Tolk is now named in two files and
`ProtectedData` in one.

These four are **deliberately not designed yet**, because each depends on a decision that has not been
made. Writing the interface first would be guessing.

| Interface | Replaces | Blocked on |
|---|---|---|
| `IModSource` | `NexusService` / `ModrinthService` | Nothing — just the largest. Reconciling an instance, stateful, key-carrying service with a static stateless one. The most worthwhile of the four. |
| `IPrompts` | `SpeakBox` (177) / `ShowDialog` (12) | The shape follows from draining those screens in Phase 4, and from the inline-prompt system that deliberately avoids modal dialogs. |
| `IBrowserHost` | WebView2 (18 files) | Decision 3 below — embedding WebKitGTK and opening the system browser need different contracts. |
| `IGameLocator` | `Microsoft.Win32.Registry` (3 files) | Decision 1 below — resolving into a Proton prefix is a different contract from finding a native install. |

### 2. Phase 4 — drain `Form1`  ◐ six screens done, and paused on purpose

Done: SMAPI Log, Dependencies, Profiles, Updates, Install, ModList (§22).

**Paused deliberately, not abandoned.** `ShowSettings()` (1,349 lines) and `SetupAccessibleUI()` (1,088)
are what remain, and they are almost entirely widget construction — extracting them would move lines
between files without giving the core anything it can use. The "`Form1` under 5,000 lines" target was set
before anyone looked at what those lines are; it is not a good target.

- [ ] **`ExtractModAsync`'s layout half.** The archive half is done (§23); what is left in that method is what
      an unpacked tree *means* — a Stardew manifest folder, a Bethesda staging tree, a BepInEx plugin, a
      Witcher `mod…` folder. **Take the Stardew branch first:** it is self-contained, entirely portable, and
      it is what the GTK head needs next. Two real defects are sitting in it and should be fixed with the
      tests that come from the move — the multi-mod common-prefix search compares path strings (so `Mods/Auto`
      looks like a parent of `Mods/AutoFish`), and the copy rebases paths with `string.Replace`, which
      replaces every occurrence rather than the leading one.

28,519 lines across 65 partial files. Per screen, smallest first:
`SmapiLog` → `Dependencies` → `Profiles` → `Updates` → `Install` → `ModList` → `Settings`.
`ShowSettings()` and `SetupAccessibleUI()` are 2,437 of those between them, and are the two that should
not be chased — see above.

### 3. Phase 5 — `Kinetix.Gtk`

Build `Kinetix.Platform.Linux` first (speech-dispatcher, libsecret, Steam/Heroic/Lutris detection,
GStreamer) and verify it headless against the core's tests, then write the GTK head against the same
presenters the WinForms head uses.

---

## Worth doing regardless of Linux

These stand on their own merits. None is urgent.

- [ ] **`ModFileSystem.cs` is still a god object** — 2,502 lines (was 3,825). What remains: hard links,
      backups, deployment, `plugins.txt`, INI editing, per-game finalisation, FOMOD finalisation, uninstaller
      registry lookup. Should be about five more classes.
- [ ] **`NexusService` is an instance class, `ModrinthService` is static.** Same job — "where mods
      come from" — two incompatible shapes, so nothing can be source-agnostic without branching on
      the game. This is what `IModSource` is for.
- [ ] **`MinecraftBinding` and `Witcher3Binding` are the same idea twice**; likewise
      `MinecraftLayout` / `Witcher3Layout` / `BethesdaLayout` have no common contract, despite
      `ModLayout` existing as an enum that names exactly what they'd implement.
- [ ] **`AppSettings` is both a DTO and a service** — ~60 serialisable properties *plus* DPAPI
      encryption, file I/O, a 310-line `InitializeDefaults()`, and a `System.Windows.Forms`
      reference. It is passed to `NexusService`, `SoundEngine` and half of `Form1`, so it is the
      de-facto service locator.
- [ ] **`Action<string, string> logError` threaded through ~20 `ModFileSystem` signatures** — a
      hand-rolled logger paid for in signature noise at every level. An `ILog` parameter says it once.
- [ ] **Keyboard shortcuts are dispatched by an if/else ladder** — `Form1_KeyDown()` (342 lines) and
      `List_KeyDown()` (289), 53 actions between them. Remapping itself works: the Shortcut Manager writes
      `AppSettings.Shortcuts`, `IsShortcut` reads it, and `ShortcutDefaultsGuardTests` holds the defaults. What
      the ladder costs is that the *dispatch* cannot be tested without running the UI, and that two actions can
      shadow one another with nothing to catch it. A command table would fix both.
- [ ] **`StardewMod.cs` is a one-line file** and `using StardewMod = KinetixModManager.GameMod;` is
      copy-pasted at the top of every `Form1` partial. Vestigial from when this was Stardew-only —
      two names for one type.
- [ ] **No `ConfigureAwait(false)` anywhere.** Harmless while everything runs on the WinForms sync
      context; a deadlock source the moment the core is called from another host. Cheapest to fix
      during extraction rather than after.
- [ ] **Still heavily static** — 72 static classes across the core and the app. A static class takes no
      dependency and so cannot be swapped per platform. Much better than it was: there are now nine interfaces
      (`IAnnouncer`, `IDispatcher`, `IGameLocator`, `IListHeadingRow`, `IModScanContext`, `IModSource`,
      `IProgressDisplay`, `ISecretStore`, `ISoundEngine`) where there used to be one.

---

## Accessibility

- [ ] **`health.mcVanilla` is the only string of 1,790 that opens with an emoji** (`⚠️`). It is a
      Health Dashboard row, so it is read aloud — announced as "warning sign" or dropped entirely
      depending on the reader's symbol level. The sentence already carries the warning in words.
      **Sean's call**, since it is wording rather than a defect.
- [ ] **Speech timing hacks must not be ported blind.** `SpeakListPosition`'s ~300 ms swallow
      (`Form1.Helpers.cs:1232`) and the `IsSpeaking` workaround (`:1317`) exist because Tolk only
      reports `IsSpeaking` reliably for its own SAPI voice. speech-dispatcher's callbacks are more
      reliable, so the Linux head should solve this properly rather than copy the timing.
- [ ] **`SpeakBox()` couples the dialog to the announcement** — six overloads shadowing
      `MessageBox.Show`. Convenient, but a GTK head cannot reuse the announcement logic without also
      taking the WinForms dialog.

---

## Docs and housekeeping

- [ ] **`MANUAL.md` is 180 KB and `CHANGELOG.md` is 212 KB**, both copied into the build output.
      Worth splitting per version before they stop being maintainable. `ARCHITECTURE_REVIEW.md` is 112 KB and
      is append-only by design, so it grows too — but nothing ships it.
- [ ] **Mixed line endings** — the tree holds both CRLF and LF `.cs` files (73 LF / 25 CRLF in
      `Kinetix.Core`, 37 / 37 in the tests, 68 / 18 in the app). `.gitattributes` normalises on commit so it
      does not affect what is stored, but it makes a working tree inconsistent. A one-off `renormalize` would
      settle it.
- [ ] **CI on both operating systems.** The whole point of the split is that it stays portable, and
      the sixteen failures found in Phase 1 are the evidence of how quietly that slips. One Linux
      runner can build and test everything, the WinForms app included, with
      `-p:EnableWindowsTargeting=true`.

---

## Decisions

**0. A mod source chooser — Sean's ask, 2026-09-14. Researched in ARCHITECTURE_REVIEW §25; needs a scope.**

Yes, and for two of the six games. Minecraft has CurseForge beside Modrinth; Stardew has ModDrop and
CurseForge beside Nexus, and smapi.io already resolves their versions. For Skyrim, Fallout 4, The Witcher 3
and Moonlight Peaks, **Nexus is the only searchable catalogue that exists** — Bethesda.net, ModDB and
LoversLab have no API, and Thunderstore has no Moonlight Peaks community (checked: 326, not among them).

Done: the page URL is kept whatever host names it, `IModSource` and its three covers exist, and the three
search modes are a setting with a per-game preferred source (§26).

- [ ] **CurseForge needs an approved API key** — the same kind of conversation as the Nexus SSO slug, and
      blocked the same way. It is listed in the chooser and in the keys screen today, and says why it cannot be
      used; the moment a key exists it can be pasted in and nothing else has to change to store it. Authors can
      also opt out of third-party downloads per mod, and the API then returns no URL by design; those must open
      in a browser rather than fail.
- [ ] **`IModSource` has no download seam, on purpose.** How a file arrives is per source and there are three
      hand-written paths — Nexus's NXM handler, a Modrinth URL, a GitHub release asset. What happens to the file
      afterwards is per *game* and already generic for all six. §15's rule applies to the missing half: three
      shapes this unalike are not enough to design an interface against. CurseForge would be the fourth, and
      that is when to cut it. See §27.
- [ ] **ModDrop can only ever open a page**, having no public file endpoint. Worth saying in the UI once a
      mod is known to live there, rather than offering a download that cannot happen.

**1. Proton or native? — ANSWERED: all six games. See ARCHITECTURE_REVIEW §20.**

This is an accessible mod manager for games in general. Managing a game's mods on Linux works for all
six and does not depend on the game being able to speak.

Separately, and worth surfacing *to the user* rather than acting on: the access mods for Skyrim,
Fallout 4, The Witcher 3 and Moonlight Peaks drive NVDA or JAWS, which do not exist in a Proton prefix,
so those games may run and stay silent. Minecraft Access uses speech-dispatcher and Stardew Access
supports Linux natively. That is a fact about the games, not the manager.

Under Proton the game's files are still in `steamapps/common`, so mod paths already work everywhere.
Only saves and INIs live inside the prefix — which is why `IGameLocator` asks two questions.

- [ ] **Warn in the UI** which games' access mods are not expected to speak on this platform, so the
      user knows what they are getting rather than finding out in game.

**2. Which GTK binding? — ANSWERED in practice: GirCore 0.7.0.**

Restores in under two seconds, targets GTK4, and the spike is built on it. `GtkSharp` is GTK3-era and
effectively stalled.

**3. WebKitGTK, or open the system browser? — ANSWERED: WebKitGTK, and it is installed and working.**

`webkitgtk-6.0` 2.52.5, linking `gtk-4`. `Kinetix.Gtk/WebKitView.cs` is a hand-written P/Invoke binding
and the window has a Wiki tab. Everything is in place except the answer to the question that matters:

- ~~**Does Orca read the embedded page properly?**~~ **ANSWERED: yes.** Verified by ear on 2026-09-13 —
      Orca reads the embedded page, and F6 cycles between the tab strip and the web view. That was the
      last technical unknown in the Linux port; every remaining item is work rather than risk.
      `IBrowserHost` can now be designed from something that demonstrably functions.

---

## Nexus sign-in — needs Sean, not code

Sean asked for a "Log in to Nexus" button with the login inside the manager's own web view. The
plumbing is built and tested (`Kinetix.Core/NexusSso.cs`, 11 tests) and the button is in the GTK head.
It cannot work yet, for one reason that is not a code problem:

- [ ] **Register the manager with Nexus Mods.** Only approved applications may use single sign-on, and
      approval is what issues the `application` slug the flow needs. That is a conversation with their
      community managers. Until then `NexusApplicationSlug` is empty and the button says so plainly
      rather than failing in a way nobody could act on.
- [ ] **Store the key through `ISecretStore`** once it arrives, and reuse the `connection_token` so an
      interrupted sign-in resumes instead of asking for approval again.
- [ ] **Wire the same button into the WinForms head.** The flow is in the core, so it is the same call;
      only the "show this page" callback differs (WebView2 rather than WebKitGTK).
- [ ] **Never read the login page.** It is the user's own session with Nexus. The value of this flow is
      that the manager receives a revocable key and never sees a password — a note for anyone tempted to
      "simplify" it later by scraping the form.

## The GTK head, from here

### Done, 2026-09-15 — see §28

- ~~The installed list read every game's switched-off state with Minecraft's rule~~ — `InstalledModsView` in
  the core owns it now, and reading and writing ask the same place.
- ~~A game that is not installed was announced as "0 mods installed"~~ — three states, three sentences, and a
  test that no two can be mistaken for each other.
- ~~`.minecraft` was looked for under `~/.config` on Linux~~ — a third defect, found while making the locator
  testable, and green in the tests the whole time because they asserted the Windows answer everywhere.
- ~~Nothing tested either Linux project~~ — `Kinetix.Platform.Linux` is referenced by the test project and
  `LinuxGameLocator` takes a `home` so all four Steam layouts, Flatpak and a Proton prefix can be built in a
  temporary folder.
- ~~`ISecretStore` had to exist before the GTK head stored any key~~ — `LibSecretStore`, verified against a
  live keyring, and the gate moved from `AppSettings` (WinForms-only) to `Secrets.Current` in the core.
- ~~`ISoundEngine` had no Linux implementation~~ — `GStreamerSoundEngine`, and the GTK head plays cues.
- ~~The GTK head's strings were English literals~~ — all through `Loc.T`, and the phrase guard sweeps it.

### What is left, in the order it should happen

- ~~`AppSettings` is in the WinForms project, so the GTK head has no settings~~ — done (§29). Exactly one
  thing in its thousand lines was Windows-only, and the JSON is byte-identical, so every existing settings
  file reads straight in.
- [ ] **`IBrowserHost`** — the last platform seam with no contract. WebKitGTK works and Orca reads it (§21);
      what is missing is the interface, and it needs one shape for an embedded view and another for handing a
      page to the system browser.
- ~~Warn which games' access mods will not speak on this platform~~ — done (§29).
      `GameProfile.AccessModSpeaksOnLinux`, defaulting to false so a new game warns until somebody says
      otherwise, with a test per game and the sentence on the GTK Settings tab.
- [ ] **Say it somewhere harder to miss than Settings.** The warning is on the Settings tab, which is where a
      user goes once. It belongs on the Games list too, beside "not installed", so it is heard while choosing
      rather than after.
- ~~The Minecraft version is hard-coded to 1.21.1~~ — done (§30), along with three other places Minecraft
  was written into the GTK head rather than being an entry in its list.
- [ ] **Screens, cheapest first.** Settings, Updates and Profiles are done (§29, §30), and the mod list's own
      actions — delete, back up, read a note — are in (§31). Eight tabs against the Windows head's sixty-odd
      feature areas. **Next: Dependencies**, then Check My Setup's remaining per-game branches, then the SMAPI
      log once Stardew installs work. Each is the same shape: check the decision is in the core, then draw it
      in GTK.
- [ ] **⚠️ Nexus search and updates are unavailable in the GTK head**, because `NexusService` is 1,387 lines in
      the WinForms project. Five of the six games get a plain sentence saying so rather than an empty list. It
      is the largest remaining thing keeping the two heads apart, and moving it is the same shape of job
      `AppSettings` was — probably smaller than it looks, for the same reason.
- [ ] **The GTK head installs Minecraft mods only.** `InstallSelectedAsync` says so plainly for other games.
      Stardew is the one worth adding next and needs the layout half of `ExtractModAsync` (above).
- [ ] **A reader started *after* the app is not noticed.** `ScreenReaderPresence` asks once at startup. A
      D-Bus signal subscription would fix it; the fallback speaks in the meantime.
- [ ] **GOG, Heroic and Lutris are not searched** — `LinuxGameLocator` covers the four Steam layouts and
      Flatpak. Heroic keeps a JSON library; Lutris keeps YAML.
- [ ] **Stardew Valley support in the GTK head** — the scanner is in the core (§19), unpacking a download is
      (§23), and the enabled-state rule is (§28). What stands between here and a Stardew install on Linux is
      the layout half of `ExtractModAsync`, plus somewhere to keep the game's paths — see the `AppSettings`
      item above.
- [ ] **Confirm the AT-SPI routing by ear.** Announcements go through Orca rather than speech-dispatcher
      (§18); the log confirms the route, but nobody has listened to it yet. Check F5, a toggle, a completed
      search, and now the sound cues.
