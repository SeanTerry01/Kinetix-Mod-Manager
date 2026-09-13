# Kinetix Mod Manager — Architecture Review & Linux/GTK Port Assessment

**Reviewed:** 2026-09-13
**Revision:** `a90d5d6` "Bring the docs up to date for six games and record the campaign"
**Version:** 1.6.0
**Reviewer's brief:** assess the state of the code ahead of building a GTK front-end for Linux.

---

## 1. Executive summary

This is a **well-written codebase in a bad shape**. Those are two different things and it matters
which one you're looking at.

The *craft* is genuinely good. The XML doc comments are some of the best I've read in a hobby
project — they explain *why*, not *what*, and they record the bug that motivated the code
(`Form1.UI.cs:50-58` explains an exit crash nobody would guess at). There are 1,009 unit tests.
Every user-facing string already goes through a localisation catalogue — 1,996 `Loc.T()` call
sites, zero `.resx` string tables. Only 2 swallowing `catch {}` blocks out of 415 catches.
Git hygiene is clean: 299 tracked files, no build output committed.

The *structure* is the problem, and it's exactly the one you suspected. **58% of the codebase —
28,719 of 49,426 lines across 63 files — is a single class, `Form1`.** It has roughly 232 fields
and 614 methods. Forty domain model types are declared as private nested classes *inside* it.
The longest method, `ShowSettings()`, is **1,252 lines**.

But here's the finding that changes the shape of your project:

> **A cross-platform core already exists. The author built it by accident and has been
> maintaining it by hand for months.**

The test project (`KinetixModManager.Tests.csproj`) targets plain `net10.0` — not
`net10.0-windows` — and pulls in **50 source files by explicit `<Compile Include>` path**
rather than referencing the app, with a comment saying *"Compile the self-contained FOMOD logic
directly instead of referencing the WinForms app... keeping the build tiny."*

That hand-maintained list is a ready-made manifest for `Kinetix.Core`. I verified it:

```
$ dotnet build KinetixModManager.Tests   →  Build succeeded. 0 Warning(s), 0 Error(s)   (2.4s)
$ dotnet test  KinetixModManager.Tests   →  Passed: 993, Failed: 16, Total: 1009        (199ms)
```

**12,223 lines of this app's domain logic compile and pass their tests on Linux today**, on this
machine, with no changes at all. The 16 failures are all path-separator and drive-letter
assumptions, and they name their own bugs.

So the job is not "port a Windows app to Linux." The job is **"finish a separation the author
started, then write a second head against it."** That's a much better position to start from.

### The numbers

| Layer | Files | LOC | % | State |
|---|---:|---:|---:|---|
| **Proven portable** (in the test project's compile list) | 50 | 12,223 | 25% | Builds & tests on Linux **now** |
| **Non-UI, not yet portable** | 22 | 8,484 | 17% | Needs work — see §5 |
| **`Form1` partials** (the god object) | 63 | 28,719 | 58% | Mixed UI + logic + models |
| **Total** | 135 | 49,426 | | |

Target after the split: **~21,000 LOC of `Kinetix.Core`** (42%), two thin heads over it.

---

## 2. How I assessed this

- Read the solution, both `.csproj` files, `README.md`, `docs/OBJECTIVES.md`.
- Measured: LOC per file, method lengths (AST-ish brace matching), field/method counts on `Form1`,
  nested type declarations, call-site counts for the key seams (`Speak`, `Loc.T`, `SpeakBox`).
- Grepped the Windows-only API surface: `System.Windows.Forms`, `Microsoft.Win32.Registry`,
  `ProtectedData` (DPAPI), `DllImport`, `WebView2`, NAudio, `Environment.SpecialFolder`.
- **Built and ran the test suite on Linux** (.NET 10.0.301, Gentoo) to get evidence rather than
  guesses about what is actually portable.
- Read the largest files end-to-end in outline, and the interesting ones in full:
  `GameProfiles.cs`, `ModEnableState.cs`, `Loc.cs`, `PlatformInfo.cs`, `Form1.cs`,
  `Form1.UI.cs`, `Form1.Accessibility.cs`, `Form1.Helpers.cs`, `NexusService.cs`,
  `ModrinthService.cs`, `SoundEngine.cs`, `AppSettings.cs`.

Note: I could not run the *application* project on Linux — `net10.0-windows` + `UseWindowsForms`
will not restore here, which is expected and is the whole point of the exercise.

---

## 3. What is genuinely good (don't break these)

These are assets. A rewrite that loses them would be a net loss.

1. **`GameProfiles.cs` (673 lines) is a model of how to do this.** A data-driven table of
   `GameProfile` records — id, display name, Steam/GOG ids, executable, mod layout, mod source,
   disabled-mod prefix/suffix. Its own doc comment explains that the per-game data *used* to live
   in "a dozen separate `game switch { ... }` expressions whose `_ =>` default silently meant
   Stardew Valley," and that centralising it "makes an unknown game an obvious failure rather
   than a wrong answer." That is the exact instinct the rest of the codebase needs. It is already
   the seam a GTK head would build against.

2. **Localisation is done and done properly.** `Loc.cs` + `lang/en.json`, flat `"key": "text"`
   pairs, English always loaded as fallback so a partial translation never blanks the UI.
   1,996 call sites. No `.resx`, no designer-generated string tables — **nothing to port**.
   A `SpokenStringGuardTests.cs` guard test exists to stop hardcoded strings creeping back in.

3. **The UI is built imperatively, not in a designer.** There is no `Form1.Designer.cs`. All 331
   control instantiations are hand-written C#. This is usually a smell; here it's a gift. There is
   no `.resx` layout blob and no visual-designer lock-in to reverse-engineer — the layout is
   ordinary code you can read, and the *structure* of each screen transfers to GTK directly.

4. **The test suite is real.** 1,009 tests, 56 test files, using recorded real-world fixtures
   rather than invented ones — the fixture comment explains they used actual Nexus `files.json`
   bodies because "none of that would be reproduced by a fixture invented to match the code."
   There are guard tests (`TabStripGuardTests`, `PerGameTabLabelGuardTests`,
   `ShortcutDefaultsGuardTests`, `AccessibleListGuardTests`) that pin accessibility invariants.

5. **Error handling is disciplined.** 415 `catch` blocks, only 2 empty. There's a single
   `DiagnosticLog` with size capping and repeat suppression that everything writes to.

6. **Wine is already on the author's radar.** `PlatformInfo.cs` detects Wine/Proton/Soda by probing
   `ntdll!wine_get_version`, and `VcRuntimeCheck.cs` softens Windows-only behaviour under it.
   Someone has already thought about this app running on Linux.

7. **The speech surface is small.** Only 4 files touch `Tolk.*` at all, and it funnels through two
   overloads: `Speak(string)` and `Speak(string, bool interrupt)` at `Form1.Helpers.cs:1221-1228`.
   537 call sites, but **one** implementation to replace. That is the single most important fact
   for the port, and it is good news.

---

## 4. The core problem: `Form1` is the application

Everything else in this report is downstream of this.

### 4.1 Scale

```
63 files                 Form1*.cs
28,719 lines             58% of the codebase
~232 fields              on one class
~614 methods             on one class
 153 static members      trapped inside a Form subclass
  40 nested types        the domain model, declared private inside the view
   1 interface           in the entire 49,426-line codebase (IListHeadingRow)
   0 DI container        no Microsoft.Extensions.DependencyInjection
```

### 4.2 The domain model lives inside the view

Forty types are declared as private nested classes in `Form1` partials:

`ConflictRow`, `PluginEntry`, `SaveRow`, `WikiResult`, `DownloadItem`, `ModKeybinds`, `KbEntry`,
`KbSection`, `NavNode`, `CategoryRow`, `CreationEntry`, `CuratorRow`, `IniRow`, `McmRow`,
`Mo2ProfileEntry`, `ModDocSource`, `ModWikiLink`, `PriorityEntry`, `ReportRow`, `SafetyBackupItem`,
`SafetyBackupMeta`, `StardewRow`, `SuggestionRow`, `TrackedRow`, `WalkthroughGuide`,
`WikiNavigationState`, `LoadOrderExport`, `BrokenModFindings`, `RuleItem`, `CpRow`, … and more.

Every one of these is a plain data shape. Every one is something a GTK head would also need.
None of them can be referenced from outside `Form1`. **This is the single largest blocker** — you
cannot write a second UI against a model that is `private` to the first UI.

### 4.3 Application state is ~70 loose mutable fields beside ~55 control references

The field census on `Form1`:

```
36 const     31 bool      15 string    15 int      13 ListBox    12 TabPage
12 static    11 ComboBox   5 TextBox    4 long      3 ToolStripMenuItem
 2 WebView2   2 SplitContainer          1 TabControl  1 SoundEngine  1 NexusService …
```

There is no view-model, no state object, no store. "Which game is active", "is a scan running",
"what's the current filter", "what did we last announce" are all loose `bool`/`string`/`int`
fields sitting next to `ListBox` and `TabPage` references on the same class. UI state and
application state are indistinguishable, so neither can move without the other.

### 4.4 Methods that are entire screens

| Lines | Location | Method |
|---:|---|---|
| **1,252** | `Form1.Settings.cs:35` | `ShowSettings()` |
| **1,077** | `Form1.UI.cs:35` | `SetupAccessibleUI()` |
| 499 | `Form1.SuiteDialog.cs:55` | `ShowAccessibilitySuiteDialog()` |
| 349 | `Form1.FomodWizard.cs:29` | `RunFomodWizard()` |
| 342 | `Form1.Input.cs:31` | `Form1_KeyDown()` |
| 329 | `Form1.ModList.cs:57` | `RefreshModList()` |
| 310 | `AppSettings.cs:593` | `InitializeDefaults()` |
| 307 | `Form1.Accessibility.cs:77` | `ShowDocDrilldown()` |
| 289 | `ModFileSystem.cs:83` | `ScanMods()` |
| 281 | `Form1.Input.cs:483` | `List_KeyDown()` |
| 259 | `ModFileSystem.cs:1813` | `ExtractModAsync()` |

`ShowSettings()` at 1,252 lines builds the settings screen, wires its handlers, validates input,
encrypts the API key, writes the file, and re-announces the result. Six responsibilities, one
method, no seam anywhere in it.

`Form1_KeyDown()` at 342 lines plus `List_KeyDown()` at 281 lines are a hand-rolled command
dispatcher written as an if/else ladder. That's ~35 keyboard shortcuts with no command table —
so the shortcut set cannot be enumerated, remapped, or tested without running the UI.
(`ShortcutDefaultsGuardTests` exists precisely because of this, and has to work around it.)

### 4.5 The partial-file split is by *screen*, not by *layer*

The 63 `Form1.*.cs` files look organised — `Form1.Wiki.cs`, `Form1.Updates.cs`,
`Form1.Minecraft.cs` — and they do make the code navigable. But each file is a **vertical slice
containing all layers**: widget construction, event wiring, business rules, file I/O, HTTP calls,
and speech, interleaved. Splitting a class across files reduces scroll distance; it does not
reduce coupling. Every one of those 63 files can still reach every one of the other 232 fields.

The clearest illustration is `Form1.Accessibility.cs` (1,982 lines), whose name promises an
accessibility layer and delivers none. It actually contains: the manual viewer, the changelog
viewer, the About box, an HTML keybind parser, a JSON keybind walker, an INI/MCM keybind parser,
a BepInEx keybind parser, a Windows virtual-key decoder, a Witcher 3 binding tree builder, and a
config-file editor. Of its 27 static methods, most are **pure functions that belong in Core** —
`ParseKeybindStructure` (`:565`), `ParseBepInExKeybinds` (`:1302`), `ParseMcmKeybinds` (`:1340`),
`DecodeVirtualKey` (`:1443`), `TranslatePunctuation` (`:725`), `HumanizePropertyName` (`:710`) —
and all of them are stuck inside a `Form` subclass.

---

## 5. Portability blockers, in order of difficulty

### 5.1 Hard — needs a new implementation

| Concern | Current | Files | Linux answer |
|---|---|---|---|
| **Screen reader output** | `Tolk.dll` via P/Invoke, 13 `DllImport`s | `Tolk.cs`, 3 others | `speech-dispatcher` (`libspeechd`) or AT-SPI live regions via GTK. **No Tolk on Linux.** |
| **Embedded browser** | WebView2 (Edge/Chromium) | 18 files | `WebKitGTK` (`webkit2gtk-4.1`). No .NET binding ships — needs a thin P/Invoke or gir.core. |
| **Secret storage** | Windows DPAPI `ProtectedData.Protect` | `AppSettings.cs:928-945` | libsecret / Secret Service (GNOME Keyring, KWallet). **DPAPI does not exist on Linux** — this throws, it doesn't degrade. |
| **Audio** | NAudio (`NAudio.WinMM`, `NAudio.Wasapi`) | `SoundEngine.cs`, `SplashScreen.cs` | Windows-only assemblies. Use libsoundio / SDL / GStreamer, or PortAudio. Note the surface is tiny: `Play`, `PlayAsync`, `PlayTone`, `PlayLogoSound`, `StopLogoSound` — 3 call sites. |
| **Game/launcher detection** | `Microsoft.Win32.Registry` | 19 files | Steam on Linux: `~/.steam/steam/steamapps/libraryfolders.vdf` (already parsed by `SteamLibraryLocator.cs` — and that file *already passes its tests on Linux*). GOG/Heroic: `~/Games/Heroic`, Lutris YAML. |
| **Native UI** | WinForms, 331 control instantiations | 63 files | GTK4 + `Gtk.ListView`/`ColumnView`. |

### 5.2 Medium — real bugs, concrete fixes

**Eleven hardcoded backslash relative paths.** These get `Path.Combine`'d and produce mixed
separators on Linux (`…/Moonlight Peaks/BepInEx\plugins`). Confirmed by a failing test:

- `GameProfiles.cs:450` — `@"BepInEx\plugins"`
- `GameProfiles.cs:455` — `@"BepInEx\moonlight-keybinds.json"`
- `GameProfiles.cs:522` — `@"bin\x64\witcher3.exe"`
- `MinecraftControls.cs:56` — `@"config\united_minecraft_keybinds.json"`
- `ModPartRules.cs:284` — `@"Data\SKSE\Plugins\EngineFixes.dll"`
- `Witcher3UserConfig.cs:40` — `@"bin\config\r4game\user_config_matrix\pc"`
- `ModFileSystem.cs:2905` — `@"bin\x64"`, `@"bin\x64_dx12"`, `@"bin\config"`

Fix: `Path.Combine("BepInEx", "plugins")`, or a `RelPath(params string[])` helper.
(The `SOFTWARE\…` registry key strings at `Form1.GameSelection.cs:306-341` and
`ModFileSystem.cs:2811-2813` are *legitimately* backslash-separated — those are registry paths,
not file paths. Leave them; they move to a `IGameLocator` Windows implementation.)

**Four hardcoded `C:\` Stardew defaults** at `Form1.cs:419-422`, plus five more in
`GameProfiles.cs` (`:383`, `:443`, `:468`, `:494`, `:519`). These belong in a platform-specific
defaults provider, not in the shared game table.

**`Path.GetInvalidFileNameChars()` returns a near-empty set on Linux** — just `/` and `\0`,
versus 41 characters on Windows. So `SanitiseFolderName()` (`ModFileSystem.cs:658`) and
`ModFolderTidy.Sanitise()` (`:123`) silently stop sanitising. Confirmed by
`ModFolderTidyTests.ANameWindowsWouldRefuseIsMadeIntoOneItAccepts`:
expected `"Skyrim Reloaded best"`, got `"Skyrim: Reloaded? <best>"`. A Linux build must keep
using the **Windows** invalid-char set, because the mod folders it creates are often read by a
Windows game running under Proton. Hardcode the set; don't ask the platform.

**13 `new HttpClient` instantiations**, most as `using var client = new HttpClient(...)`
(`NexusService.cs:772,776,787,791,854,858`, `ModrinthService.cs:168,280,291`,
`Form1.Minecraft.cs:231`, `FabricInstaller.cs:213`, `LootMasterlist.cs:107`). Disposing an
`HttpClient` disposes its handler and leaks the socket into `TIME_WAIT`; under repeated update
checks this exhausts ephemeral ports. `NexusService.cs:32` already does the right thing with a
`static readonly HttpClient`. This is a real bug on both platforms, not just a port issue.

**25 `async void` methods** (outside event handlers, where it's unavoidable). An exception in one
of these cannot be caught by the caller — it goes straight to the unhandled-exception handler and
kills the process. `SpeakListPosition` (`Form1.Helpers.cs:347`), `SpeakAfterForeignWindow`
(`:423`) and `AnnounceListEmpty` (`:537`) are `async void` **on the speech path** — the most
user-visible place for a silent crash.

**Zero `ConfigureAwait(false)`** anywhere. Harmless while everything runs on the WinForms sync
context; it becomes a deadlock source the moment the core is called from a different host. Fix it
during extraction, not after.

### 5.3 Easy — already portable

`SteamLibraryLocator`, `BethesdaLayout`, `IniDocument`, `SaveGame`, `SmapiVersion`,
`UpdateCoverage`, `ModManifest`, `ModNameMatch`, `NxmLink`, `ModDisplayName`, `BepInExPlugin`,
`GameKeybindExport`, `ContentPatcherConfig`, `BepInExConfigSchema`, `McmConfigSchema`,
`StardewModConfig`, `Witcher3InputSettings`, `Witcher3ModSettings`, `Witcher3Controls`,
`Witcher3UserConfig`, `Witcher3Layout`, `ModPartRules`, `MinecraftLayout`, `MinecraftLauncher`,
`MinecraftLaunchLog`, `ModrinthService`, `MinecraftControls`, `MinecraftSuite`, `FabricInstaller`,
`ScriptExtenderInfo`, `ScriptExtenderPlugins`, `DocOutline`, `SuggestedMod`, `SuggestionList`,
`KeyRepeatFilter`, `ListSections`, `DiagnosticLog`, `FomodConfig/Parser/Installer/ConditionEvaluator`,
`GameMod`, `ModDependency`, `ModHealth`, `ModEnableState`, `ModFolderTidy`, `GameProfiles`,
`GogLibraryLocator`, `VcRuntimeCheck`, `PlatformInfo`.

**These 50 files move to `Kinetix.Core` on day one with no edits.** They are already compiling
against `net10.0` and passing 993 tests on this machine.

---

## 6. Design inconsistencies

These aren't portability issues — they're the kind of thing that makes a codebase harder to
reason about than it needs to be, and they'll bite during the split.

1. **`NexusService` is an instance class; `ModrinthService` is a static class.** Same
   responsibility — "where mods come from" — two incompatible shapes. `NexusService(AppSettings)`
   holds validation state and an API key; `ModrinthService` is stateless statics. Anything that
   wants to be source-agnostic has to branch on the game. This is precisely where an `IModSource`
   interface belongs, and its absence is why `GameProfiles.ModSource` has to be switched on at
   every call site.

2. **`MinecraftBinding` and `Witcher3Binding` are the same idea, twice.** Both have
   `Key` + `Action`; Witcher adds `Context`/`ModName`, Minecraft adds `IsUnbound`/`IsMouse`. No
   shared `IKeyBinding`. The same duplication runs through `MinecraftLayout` / `Witcher3Layout` /
   `BethesdaLayout` — three layout classes, no common contract, despite `ModLayout` existing as
   an enum that names exactly the thing they'd implement.

3. **One interface in 49,426 lines.** `IListHeadingRow`. Everything else is a concrete class or
   a `static class`. **50 of 135 files declare a `static class`.** Static classes have no
   constructor, so they cannot take a dependency, so they cannot be swapped per platform. Every
   static class doing I/O — `ModFileSystem` above all — is a wall the GTK head will hit.

4. **`Action<string, string> logError` threaded through ~20 method signatures** in
   `ModFileSystem` (`:674`, `:821`, `:898`, `:995`, `:1195`, `:1637`, `:1658`, `:2666`, …). This
   is a poor-man's injected logger, paid for in signature noise at every level. An `ILog`
   parameter, or a properly injected `DiagnosticLog` instance, says the same thing once.

5. **`ModFileSystem.cs` (3,825 lines, static) is a second god object.** Its responsibilities:
   NTFS hard links, manifest parsing, mod scanning for *four* different loaders, backup creation
   and pruning, file deployment and purging, `plugins.txt` read/write, INI read/write, archive
   invalidation, archive extraction, FOMOD finalisation, Witcher-3 installer footprint recording,
   Windows uninstaller registry lookup, and folder-name sanitisation. Roughly eight distinct
   responsibilities. This should be ~8 classes.

6. **`using StardewMod = KinetixModManager.GameMod;`** appears at the top of every `Form1` partial,
   and `StardewMod.cs` is a **1-line file**. Vestigial from when this was Stardew-only. Two names
   for one type, plus 24 identical `using` blocks copy-pasted across the partials (they have to be,
   because C# `using` is per-file — another cost of the partial-class approach).

7. **`AppSettings` is both a DTO and a service.** It holds ~60 serialisable properties *and*
   performs DPAPI encryption (`:928-945`), file I/O, defaults initialisation (a 310-line method at
   `:593`), and references `System.Windows.Forms`. It is passed to `NexusService`, `SoundEngine`,
   and half of `Form1` — so it's the de-facto service locator. It is also one of only four non-`Form1`
   files that touch WinForms.

8. **`docs/OBJECTIVES.md` is stale.** It describes a three-game app ("Stardew Valley, Skyrim
   Special Edition, and Fallout 4"); the app now supports six. `MANUAL.md` is 178 KB and
   `CHANGELOG.md` is 201 KB, both shipped into the build output — worth splitting per-version
   before they become unmaintainable.

---

## 7. Proposed target architecture

You suggested `Kinetix.Core` / `.Gtk` / `.Ui` — that's the right instinct. I'd add one project,
because the abstractions need somewhere to live that the *core* can also depend on:

```
Kinetix.Abstractions   (net10.0)   ~300 LOC, new
    │   Pure interfaces + DTOs. No implementation, no dependencies beyond the BCL.
    │   IAnnouncer, ISoundEngine, ISecretStore, IGameLocator, IBrowserHost,
    │   IFileDialogs, IClipboard, IModSource, IDispatcher, ILog
    ▼
Kinetix.Core           (net10.0)   ~21,000 LOC
    │   All domain logic, all models, all parsing, all file/HTTP I/O.
    │   Depends on Abstractions. Depends on NO UI toolkit. Runs headless.
    │   ← the 50 already-portable files land here unchanged on day one
    ▼
    ├── Kinetix.Platform.Windows  (net10.0-windows)  ~1,500 LOC
    │       Tolk, DPAPI, Registry, NAudio, Windows path semantics
    │
    ├── Kinetix.Platform.Linux    (net10.0)          ~1,500 LOC, new
    │       speech-dispatcher, libsecret, Steam/Heroic/Lutris VDF+YAML,
    │       libsoundio or GStreamer
    │
    ├── Kinetix.WinForms          (net10.0-windows)  ~12,000 LOC after extraction
    │       The existing UI, reduced to presentation + event wiring
    │
    └── Kinetix.Gtk               (net10.0)          new
            GTK4 + libadwaita head. AT-SPI via GtkAccessible.

Kinetix.Core.Tests     (net10.0)   1,009 tests → references Core properly
                                   instead of 50 hand-maintained <Compile Include> lines
```

### The eight interfaces that matter

Derived from what the code actually calls today, not from what looks tidy:

```csharp
// 537 call sites funnel into two methods at Form1.Helpers.cs:1221-1228.
// Windows → Tolk.Output. Linux → speech-dispatcher, or GTK live region.
public interface IAnnouncer
{
    void  Speak(string text, bool interrupt = false);
    void  Silence();
    bool  IsSpeaking { get; }   // Tolk only answers this for its own SAPI voice — see Form1.Helpers.cs:1317
    bool  IsAvailable { get; }
}

// 3 call sites. Tiny surface, and the .ogg theme packs are already platform-neutral files.
public interface ISoundEngine
{
    void Play(string name, string? themeOverride = null);
    Task PlayAsync(string name, string? themeOverride = null);
    void PlayTone(int percent);           // progress feedback
    void PlayLogoSound(string theme, string file);
    void StopLogoSound();
}

// AppSettings.cs:928-945. Windows → DPAPI. Linux → libsecret. Fallback → age/AES + file perms 0600.
public interface ISecretStore
{
    string Protect(string plainText);
    string Unprotect(string cipherText);
}

// 19 files touch the Registry for this. Linux has completely different answers.
public interface IGameLocator
{
    IReadOnlyList<string> LibraryRoots();          // Steam libraryfolders.vdf — already portable
    string? SteamInstallPath(string appId);
    string? GogInstallPath(string gogId);
    string? ProtonPrefixFor(string appId);          // Linux-only, returns null on Windows
}

// 18 files reference WebView2. Wiki/walkthrough/discovery browsing.
public interface IBrowserHost
{
    Task NavigateAsync(string url);
    Task<string> ExecuteScriptAsync(string js);     // used for accessible text extraction
    event EventHandler<string> NavigationCompleted;
}

// 177 SpeakBox() call sites + 12 ShowDialog() + the inline-prompt system.
public interface IPrompts
{
    Task<bool>    ConfirmAsync(string message, string title);
    Task<string?> AskAsync(string question, string? initial = null);
    Task<string?> PickFolderAsync(string title, string? start = null);
    Task<string?> PickFileAsync(string title, string filter);
    Task<int>     ChooseAsync(string question, IReadOnlyList<string> options);
}

// Replaces Control.Invoke / BeginInvoke. GTK → GLib.Idle.Add / MainContext.
public interface IDispatcher
{
    void Post(Action action);
    Task<T> InvokeAsync<T>(Func<T> f);
    bool IsOnUiThread { get; }
}

// Unifies NexusService (instance) and ModrinthService (static). See §6.1.
public interface IModSource
{
    Task<(IReadOnlyList<GameMod> Results, int Total)> SearchAsync(string query, int page);
    Task<ModFileInfo?> GetLatestFileAsync(GameMod mod);
    Task<string> DownloadAsync(ModFileInfo file, string destFolder, IProgress<double>? p);
    bool RequiresApiKey { get; }
}
```

### On accessibility specifically

This matters more here than in most ports, since accessibility *is* the product.

**What transfers cleanly to GTK4 + Orca:**
- The keyboard-first design. F6 focus cycling, per-tab context help, `ListBox`-based navigation —
  all of this is `Gtk.ListView` + `Gtk.EventControllerKey` and maps directly.
- `Loc.T()` strings — unchanged.
- `ListSections.cs` ("where am I in a list divided by headings?") — pure, already tested,
  already portable. GTK has no built-in equivalent, so keeping this class is a win.
- `KeyRepeatFilter.cs` (real press vs auto-repeat) — pure, zero BCL dependency, portable as-is.
- The `.ogg` audio theme packs — files, not code.
- The **inline prompt** pattern (`Form1.InlinePrompt.cs`, `Form1.InlineView.cs`): instead of modal
  dialogs, the app swaps content in place. Only 12 `ShowDialog()` calls in the whole app. This is
  a deliberate accessibility choice and it ports *better* to GTK than modal dialogs would.

**What has no clean equivalent and needs design work:**
- **Interrupt semantics.** `Speak(text, interrupt: true)` maps to Tolk's `Output(text, interrupt)`,
  which does a hard cancel. AT-SPI live regions don't have a reliable interrupt; speech-dispatcher
  does (`SPD_IMPORTANT` + `spd_stop`). You will likely want to talk to speech-dispatcher directly
  rather than rely on Orca's live-region handling, which mirrors what Tolk does on Windows.
- **`IsSpeaking`.** `Form1.Helpers.cs:1317` documents that Tolk only reports this reliably for its
  own SAPI voice, and the code works around it with timing. speech-dispatcher's callbacks are
  actually *more* reliable — an improvement, but the timing hacks
  (`SpeakListPosition`'s ~300ms swallow, `Form1.Helpers.cs:1232`) must not be ported blind.
- **`DecodeVirtualKey`/`VirtualKeyName`** (`Form1.Accessibility.cs:1443-1511`) translate Windows
  VK codes to spoken names. Games' config files store VK codes regardless of host OS, so this
  logic stays — but it must move to Core and stop living in a `Form`.

**Accessibility issues I noticed in passing:**
- The three `async void` speech methods (§5.2) — an exception there kills the app mid-announcement.
- `Form1.Helpers.cs:1242-1251` lazily calls `Tolk.Load()` inside `Speak()`. Load failure is
  swallowed and the announcement is silently dropped — the user gets no feedback that speech is
  broken, which is the worst possible failure mode for this app. It should fall back audibly.
- `SpeakBox()` has six overloads (`Form1.MessageBox.cs:133-148`) shadowing `MessageBox.Show`'s
  signatures. Convenient, but it means the *dialog* and the *announcement* are one call — so
  a GTK head can't reuse the announcement logic without also taking the WinForms dialog.

---

## 8. Suggested sequence

Ordered so that each phase leaves the Windows app shipping and the test suite green.

**Phase 0 — free win (½ day). ✅ DONE — see §11.**
Create `Kinetix.Core` (`net10.0`). Move the 50 files the test project already compiles. Replace
the 50 hand-maintained `<Compile Include>` lines with a single `<ProjectReference>`. Nothing
changes behaviourally; the build gets faster and the boundary becomes real and enforced by the
compiler instead of by a comment. **Do this first — it's the highest ratio of value to risk in the
whole project.**

**Phase 1 — fix the 16 failing tests (1–2 days).**
They are a free, precise, self-verifying checklist of portability bugs. Fix the 11 backslash
literals; hardcode the Windows invalid-filename set; make the `C:\` defaults platform-conditional;
make the test fixtures build paths with `Path.Combine` instead of Windows literals. When this is
green on Linux, the core is genuinely portable and you have CI proof of it.

**Phase 2 — extract the 40 nested types (2–3 days).**
Move every nested model out of `Form1` into `Kinetix.Core/Models/`. Purely mechanical, guided by
the compiler. This is what unblocks a second head.

**Phase 3 — the abstractions (3–5 days).**
Define the eight interfaces. Implement `Kinetix.Platform.Windows` by moving the existing Tolk,
DPAPI, Registry and NAudio code behind them. Delete the direct `Tolk.*` calls from `Form1`.
Fix the `HttpClient` disposal and the `async void` speech methods while you're in there.

**Phase 4 — pull logic out of `Form1` (2–3 weeks, the real work).**
Per screen, in this order — smallest and most self-contained first:
`SmapiLog` → `Dependencies` → `Profiles` → `Updates` → `Install` → `ModList` → `Settings`.
Each becomes a `…Presenter`/service in Core with the `Form1` partial reduced to widget wiring.
Target: `Form1` under 5,000 lines total. Break up `ShowSettings()` and `SetupAccessibleUI()` as
part of this — those two alone are 2,329 lines.

**Phase 5 — `Kinetix.Gtk`.**
Only now is this cheap. Build `Kinetix.Platform.Linux` first (speech-dispatcher, libsecret, Steam
VDF, GStreamer), verify it headless against Core's tests, then write the GTK head against the
same presenters the WinForms head uses.

**Parallel track:** set up CI that runs `dotnet test` on **both** Windows and Linux from Phase 0
onwards. Without that, portability regressions creep back in within weeks — the 16 failures found
here are proof of how easily it happens.

---

## 9. Things to decide before you start

1. **Does the Linux build target native Linux games, or Proton?** Stardew, Skyrim SE, Fallout 4
   and Witcher 3 on Linux usually run under Proton, with Windows-shaped paths inside a prefix.
   That means `IGameLocator` needs to resolve *into* `~/.steam/steam/steamapps/compatdata/<id>/pfx/`,
   and mod folder names must keep obeying **Windows** naming rules (§5.2). Minecraft Java is the
   exception — it's natively cross-platform, which makes it by far the best first target.

2. **Is this a fork or a contribution?** The interface extraction is a large, invasive diff on
   someone else's active project (last commit the day the zip was made). Worth agreeing the
   `Kinetix.Core` split with the author up front — Phase 0 and Phase 1 are improvements he'd
   plausibly want regardless of whether Linux ever ships, which makes them a good opening ask.

3. **GTK4 from C#: which binding?** `GirCore` is the current, actively maintained option and
   targets GTK4 + libadwaita. `GtkSharp` is GTK3-era and effectively stalled. WebKitGTK has no
   binding in either — it'll need hand-written P/Invoke, which is the single largest unknown in
   the Linux head. Consider whether the wiki/walkthrough browser could ship as "open in your
   default browser" for v1 and defer WebKitGTK entirely; that removes 18 files' worth of
   WebView2 coupling from the critical path.

4. **Minecraft-first is the pragmatic scope.** It is the only supported game that runs natively
   on Linux, its mod source is Modrinth (no API key, and `ModrinthService.cs` already passes its
   tests on Linux), Fabric installation is already done without the official installer
   (`FabricInstaller.cs`), and `MinecraftLauncher.cs` already builds the `java` command directly.
   A Minecraft-only GTK head is a genuinely achievable first release — and it's the game you
   actually wanted to play.

---

## 10. Bottom line

The instinct in your brief was right: everything is jumbled together, and a
`Core` / `Ui` / `Gtk` separation is the correct fix. What the code doesn't show at a glance is
that **a quarter of the codebase has already been separated** — the author did it to keep his test
project fast, maintained the boundary by hand for months, and never turned it into a project
reference.

Formalising that boundary is a half-day's work and it's where I'd start. The 16 failing Linux
tests are the next half-day, and they hand you a precise bug list for free. After that the real
cost is Phase 4 — draining 28,719 lines of `Form1` — and there is no shortcut through it, only the
consolation that every screen you drain makes both heads better rather than just the new one.

The accessibility work, which is the part that would be genuinely hard to rebuild, is in better
shape than the architecture is: the speech surface is two methods, the strings are already
externalised, and the inline-prompt design ports to GTK more easily than modal dialogs would.


---

## 11. Phase 0 — completed 2026-09-13

The core has been extracted. No behaviour was changed, no logic was touched, and nothing was
committed — the changes sit in the working tree for review.

### What changed

| | |
|---|---|
| **New project** | `Kinetix.Core/Kinetix.Core.csproj` — `net10.0`, assembly `Kinetix.Core`, root namespace left as `KinetixModManager`. Two package references: `K4os.Compression.LZ4`, `Newtonsoft.Json`. |
| **Moved** | The 50 files the test project used to list by hand, `KinetixModManager/` → `Kinetix.Core/`. Pure `git mv`; 49 show as clean renames. |
| **Edited (1 source file)** | `PlatformInfo.cs`: `internal static class` → `public static class`. Its other caller, `ApplicationConfiguration.cs`, stays in the WinForms app, and `internal` does not cross an assembly boundary. A `<remarks>` block records why. |
| **`KinetixModManager.Tests.csproj`** | 50 `<Compile Include="..\KinetixModManager\*.cs" />` lines → one `<ProjectReference>`. |
| **`KinetixModManager.csproj`** | Added `<ProjectReference>` to `Kinetix.Core`. |
| **`KinetixModManager.slnx`** | `Kinetix.Core` added to the solution. |

The namespace was deliberately **not** renamed. Moving 50 files between projects and renaming the
namespace they live in are two separate changes, and doing both at once would bury a mechanical,
reviewable move under 135 files of edits. The assembly is named for the layer; the namespace can
follow in its own pass.

### Verification

All three projects were built, and the suite run, on Linux (.NET 10.0.301, Gentoo):

```
dotnet build Kinetix.Core                      →  succeeded, 0 warnings, 0 errors
dotnet build KinetixModManager.slnx            →  succeeded, 0 warnings, 0 errors
      -p:EnableWindowsTargeting=true              (Kinetix.Core, .Tests and the WinForms app)
dotnet test  KinetixModManager.Tests           →  993 passed / 16 failed / 1009 total
```

**993/16 is byte-for-byte the same result as before the move** — the same 16 path-separator
failures documented in §5.2, no regressions, nothing new.

### A useful discovery: the whole solution compiles on Linux

`dotnet build -p:EnableWindowsTargeting=true` downloads the Windows targeting packs and compiles
`net10.0-windows` + WinForms **on Linux**. The app cannot *run* here, but it compiles clean —
0 errors, 0 warnings — which means:

- The Linux/GTK work can compile-check changes to the WinForms head without a Windows machine.
- CI can build and test **every** project from a single Linux runner from today, which is what
  stops portability regressions creeping back in (§8, parallel track).

This flag is not in any `.csproj`; it was passed on the command line so the build is unchanged for
anyone on Windows. If it turns out to be wanted permanently, a one-line `Directory.Build.props`
is the place for it — it is a no-op on Windows.

### What this bought

The boundary is now the compiler's business rather than a convention in a comment. A file in
`Kinetix.Core` **cannot** reach `System.Windows.Forms`, the registry or DPAPI, because
`net10.0` does not have them. The separation holds by itself.

Concretely: **12,223 lines, 25% of the codebase, are now a declared, referenced, cross-platform
class library** that both a WinForms head and a GTK head can build against. Next is Phase 1 — the
16 failing tests.
