# Kinetix Mod Manager — outstanding work

Everything identified so far that is **not yet done**. Findings come from `ARCHITECTURE_REVIEW.md`,
which has the reasoning behind each; this file is the list, not the argument.

Anything resolved gets deleted from here rather than ticked, so the file stays short enough to read.

**Last updated:** 2026-09-13, after Phase 3 (part one).
**State:** 1,024 tests passing on Windows and Linux; all three projects build clean.

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

- [ ] **`ModFileSystem.cs` is a second god object** — 3,825 lines, `static`, doing roughly eight
      jobs: hard links, manifest parsing, mod scanning for four loaders, backups, deployment,
      `plugins.txt`, INI editing, archive extraction, FOMOD finalisation, uninstaller registry
      lookup, folder-name sanitising. Should be about eight classes.
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

## Decisions needed before the GTK head

Not tasks — questions whose answers change the design.

1. **Proton or native?** Stardew, Skyrim SE, Fallout 4 and Witcher 3 on Linux run under Proton, with
   Windows-shaped paths inside a prefix. `IGameLocator` would have to resolve into
   `~/.steam/steam/steamapps/compatdata/<id>/pfx/`, and mod folder names must keep obeying Windows
   rules — which is why `WindowsFileName` exists. Minecraft Java is the exception and is natively
   cross-platform, which makes it the obvious first target.
2. **Which GTK binding?** `GirCore` is current and targets GTK4 + libadwaita. `GtkSharp` is GTK3-era
   and effectively stalled.
3. **WebKitGTK, or defer it?** No .NET binding exists for either, so it needs hand-written P/Invoke
   — the single largest unknown in the Linux head. Shipping v1 with "open in your default browser"
   would take 18 files' worth of WebView2 coupling off the critical path.
