# Kinetix Mod Manager — outstanding work

Everything identified so far that is **not yet done**. Findings come from `ARCHITECTURE_REVIEW.md`,
which has the reasoning behind each; this file is the list, not the argument.

Anything resolved gets deleted from here rather than ticked, so the file stays short enough to read.

**Last updated:** 2026-09-14, after per-game sounds and the Minecraft server cue. No unknowns left, only work.
**State:** 1,175 tests passing on Windows and Linux; all five projects build clean, zero warnings.

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
| `IGameLocator` | `Microsoft.Win32.Registry` (19 files) | Decision 1 below — resolving into a Proton prefix is a different contract from finding a native install. |

### 2. Phase 4 — drain `Form1`  ◐ six screens done, and paused on purpose

Done: SMAPI Log, Dependencies, Profiles, Updates, Install, ModList (§22).

**Paused deliberately, not abandoned.** `ShowSettings()` (1,252 lines) and `SetupAccessibleUI()` (1,077)
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

~28,100 lines across 63 partial files, ~232 fields, ~614 methods. Per screen, smallest first:
`SmapiLog` → `Dependencies` → `Profiles` → `Updates` → `Install` → `ModList` → `Settings`.
Target: `Form1` under 5,000 lines. `ShowSettings()` (1,252 lines) and `SetupAccessibleUI()`
(1,077) are 2,329 of those between them.

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
- [ ] **Keyboard shortcuts are an if/else ladder** — `Form1_KeyDown()` (342 lines) and
      `List_KeyDown()` (281). ~35 shortcuts with no command table, so the set cannot be enumerated,
      remapped or tested without running the UI. (`docs/OBJECTIVES.md` lists remappable shortcuts as
      a stretch goal; this is the thing standing in its way.)
- [ ] **`StardewMod.cs` is a one-line file** and `using StardewMod = KinetixModManager.GameMod;` is
      copy-pasted at the top of every `Form1` partial. Vestigial from when this was Stardew-only —
      two names for one type.
- [ ] **No `ConfigureAwait(false)` anywhere.** Harmless while everything runs on the WinForms sync
      context; a deadlock source the moment the core is called from another host. Cheapest to fix
      during extraction rather than after.
- [ ] **Only one interface in the whole codebase** (`IListHeadingRow`), and 50 of 135 files are
      `static class`. A static class takes no dependency and so cannot be swapped per platform.

---

## Accessibility

- [ ] **`health.mcVanilla` is the only string of 1,741 that opens with an emoji** (`⚠️`). It is a
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

- [ ] **`docs/OBJECTIVES.md` is stale** — it still describes a three-game app ("Stardew Valley,
      Skyrim Special Edition, and Fallout 4"). There are six.
- [ ] **`MANUAL.md` is 178 KB and `CHANGELOG.md` is 201 KB**, both copied into the build output.
      Worth splitting per version before they stop being maintainable.
- [ ] **Mixed line endings** — the tree holds both CRLF and LF `.cs` files (33 LF / 18 CRLF in
      `Kinetix.Core`, 20 / 37 in the tests). `.gitattributes` normalises on commit so it does not
      affect what is stored, but it makes a working tree inconsistent. A one-off `renormalize` would
      settle it.
- [ ] **CI on both operating systems.** The whole point of the split is that it stays portable, and
      the sixteen failures found in Phase 1 are the evidence of how quietly that slips. One Linux
      runner can build and test everything, the WinForms app included, with
      `-p:EnableWindowsTargeting=true`.

---

## Decisions

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

- [ ] **Stardew Valley support** — the scanner is in the core (§19) and so is unpacking a download (§23), so
      what stands between here and a Stardew install on Linux is the layout half of `ExtractModAsync` above,
      plus a game picker in the GTK head and Stardew's own paths.
- [ ] **The spike's strings are English literals, not `Loc.T`.** The catalogue is wired and copied to
      its output; using it is the follow-up, and the guard tests should then cover `Kinetix.Gtk` too.
- [ ] **Confirm the AT-SPI routing by ear.** Announcements now go through Orca rather than
      speech-dispatcher (ARCHITECTURE_REVIEW §18); the log confirms the route, but nobody has listened
      to it yet. Check F5, a toggle and a completed search.
- [ ] **A reader started *after* the app is not noticed.** `ScreenReaderPresence` asks once at startup.
      A D-Bus signal subscription would fix it; the fallback speaks in the meantime.
- [ ] **`ISoundEngine` has no Linux implementation.** The `.ogg` theme packs need GStreamer, libsoundio or
      similar. Choosing *which* file to play is done and portable (`SoundThemes`, §24); what is left is the
      playing, plus copying `sounds/**` to the GTK head's output the way `lang/**` already is.
- [ ] **`ISecretStore` has no Linux implementation.** libsecret, for the Nexus key — not needed for a
      Minecraft-only v1, needed for Stardew.
- [ ] **The Minecraft version for search is hard-coded** to 1.21.1 in the spike.
