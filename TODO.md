# Kinetix Mod Manager — outstanding work

Everything identified so far that is **not yet done**. Findings come from `ARCHITECTURE_REVIEW.md`,
which has the reasoning behind each; this file is the list, not the argument.

Anything resolved gets deleted from here rather than ticked, so the file stays short enough to read.

**Last updated:** 2026-09-13, after mod scanning moved to the core.
**State:** 1,031 tests passing on Windows and Linux; all five projects build clean.

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

### 2. Phase 4 — drain `Form1` (the long one)

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

- [ ] **`ModFileSystem.cs` is still a god object** — 3,114 lines after scanning left (was 3,825).
      What remains: hard links, backups, deployment, `plugins.txt`, INI editing, archive extraction,
      FOMOD finalisation, uninstaller registry lookup. Should be about six more classes.
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

**1. Proton or native? — ANSWERED: native, Minecraft and Stardew Valley only.**

Not because Proton is inaccessible in general, but because the *access mods* for the other four games
speak by driving NVDA or JAWS, which do not exist inside a Proton prefix. Skyrim would load its access
mod, start, play, and say nothing — the exact silent failure this manager exists to prevent. Minecraft
Access uses speech-dispatcher on Linux and Stardew Access supports Linux natively; both are quoted in
ARCHITECTURE_REVIEW §16 from the docs this repo already ships.

Consequences: `IGameLocator` needs no Proton prefix resolution for v1; `WindowsFileName` still applies
(Stardew mods are shared with Windows machines); a Minecraft-only v1 needs no Nexus API key at all.

**2. Which GTK binding? — ANSWERED in practice: GirCore 0.7.0.**

Restores in under two seconds, targets GTK4, and the spike is built on it. `GtkSharp` is GTK3-era and
effectively stalled.

**3. WebKitGTK, or open the system browser? — ANSWERED: WebKitGTK. The browser must be in-app.**

Now the largest unknown in the Linux head, and the one piece with no easy path:

- [ ] **WebKitGTK is not installed on the dev machine.** Neither `webkit2gtk-4.1` nor `webkitgtk-6.0`.
      On Gentoo that is `net-libs/webkit-gtk`, and it is a long compile.
- [ ] **There is no .NET binding for it.** GirCore does not ship one. It needs hand-written P/Invoke
      over `WebKitWebView`, or a binding generated from the GIR file.
- [ ] **The accessibility question is unanswered and matters most.** WebView2 exposes page content to
      NVDA through UIA; WebKitGTK exposes it through AT-SPI, which Orca reads. Whether the wiki-reading
      flow — heading navigation, category drilling, the F6 cycle into the web view — behaves the same
      needs **testing before `IBrowserHost` is designed around it**.

---

## The GTK head, from here

- [ ] **Stardew Valley support** — the scanner is in the core now (ARCHITECTURE_REVIEW §19), so this is
      no longer blocked. It needs a game picker in the GTK head and Stardew's own paths.
- [ ] **The spike's strings are English literals, not `Loc.T`.** The catalogue is wired and copied to
      its output; using it is the follow-up, and the guard tests should then cover `Kinetix.Gtk` too.
- [ ] **Confirm the AT-SPI routing by ear.** Announcements now go through Orca rather than
      speech-dispatcher (ARCHITECTURE_REVIEW §18); the log confirms the route, but nobody has listened
      to it yet. Check F5, a toggle and a completed search.
- [ ] **A reader started *after* the app is not noticed.** `ScreenReaderPresence` asks once at startup.
      A D-Bus signal subscription would fix it; the fallback speaks in the meantime.
- [ ] **`ISoundEngine` has no Linux implementation.** The `.ogg` theme packs need GStreamer,
      libsoundio or similar. The spike is silent apart from speech.
- [ ] **`ISecretStore` has no Linux implementation.** libsecret, for the Nexus key — not needed for a
      Minecraft-only v1, needed for Stardew.
- [ ] **The Minecraft version for search is hard-coded** to 1.21.1 in the spike.
