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

**~~25 `async void` methods — an exception kills the process.~~** **Correction (verified
2026-09-13): this is wrong for this codebase, and the codebase is ahead of the criticism.**
`Program.cs:141-157` installs three handlers, not one: `Application.SetUnhandledExceptionMode(
CatchException)` plus `Application.ThreadException` catches what escapes an `async void` on the UI
thread, `AppDomain.CurrentDomain.UnhandledException` catches the rest, and
`TaskScheduler.UnobservedTaskException` catches fire-and-forget `Task`s that no one awaited. All
three log, and `HandleUnhandledException` (`Program.cs:55`) shows the user a dialog naming the log
path and keeps running. The comments there already set out the exact reasoning, including why an
unobserved task is marked observed rather than escalated. The 29 `async void` methods are
consequently a normal WinForms idiom here rather than a defect, and no change was made.

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

**Phase 1 — fix the 16 failing tests (1–2 days). ✅ DONE — see §12.**
They are a free, precise, self-verifying checklist of portability bugs. Fix the 11 backslash
literals; hardcode the Windows invalid-filename set; make the `C:\` defaults platform-conditional;
make the test fixtures build paths with `Path.Combine` instead of Windows literals. When this is
green on Linux, the core is genuinely portable and you have CI proof of it.

**Phase 2 — extract the 40 nested types (2–3 days). ✅ DONE — see §14.**
Move every nested model out of `Form1` into `Kinetix.Core/Models/`. Purely mechanical, guided by
the compiler. This is what unblocks a second head.

**Phase 3 — the abstractions (3–5 days). ◐ PARTLY DONE — see §15.**
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


---

## 12. Phase 1 — completed 2026-09-13

**All 1,016 tests now pass on Linux.** They were 993 passing and 16 failing when this review began.

### What the 16 failures actually were

Not all the same thing, and the difference decided each fix. Six were **production bugs** that would
have shipped; ten were tests asserting a separator rather than a behaviour.

**Production bugs — the code was wrong**

| Fix | Was |
|---|---|
| `GameProfiles.cs` Moonlight Peaks mods folder | `@"BepInEx\plugins"` → `Path.Combine("BepInEx", "plugins")` |
| `GameProfiles.cs` Moonlight keybind export | `@"BepInEx\moonlight-keybinds.json"` → composed |
| `GameProfiles.cs` Witcher 3 executable | `@"bin\x64\witcher3.exe"` → composed |
| `MinecraftControls.cs` United Minecraft keybinds | `@"config\united_minecraft_keybinds.json"` → composed (`const` → `static readonly`) |
| `ModPartRules.cs` Engine Fixes detect file | `@"Data\SKSE\Plugins\EngineFixes.dll"` → composed |
| `Witcher3UserConfig.cs` config matrix | `@"bin\config\r4game\user_config_matrix\pc"` → composed |
| `ModFileSystem.cs` Witcher game-owned folders | `@"bin\x64"`, `@"bin\x64_dx12"`, `@"bin\config"` → composed |

Every one of these is fed to `Path.Combine`. A backslash is only a separator on Windows, so off it
the literal is read as part of one long file name and the lookup silently finds nothing. The
Moonlight keybind one was not theoretical: `GameKeybindExportTests` was already failing because the
live export could never be found, so the app fell back to the bundled snapshot and would have shown
stale keybindings.

A stale comment went with the Witcher fix. It said *"every path built from this one goes through
Path.Combine, which takes the relative path in its stride"* — true on Windows, and the reason the
bug looked safe. It now says why the literal could not stay.

**The invalid-character bug — new file, `Kinetix.Core/WindowsFileName.cs`**

`ModFolderTidy.Sanitise` and `ModFileSystem.SanitiseFolderName` both asked
`Path.GetInvalidFileNameChars()` which characters to strip. That answers *for the host*: 41
characters on Windows, **2** on Linux. So off Windows neither function crashed — they quietly
stopped sanitising, and a mod folder called `Skyrim: Reloaded? <best>` would be created happily and
then be unopenable by the game.

And a game does have to open it. These are Windows games; on Linux they run under Proton, and a name
Linux accepts but Windows does not is a name the game cannot read. The rule belongs to the game, not
to the host, so the set is now stated outright and is the same answer on both. Both doc comments
already *said* "characters Windows forbids" — the code had simply stopped meaning it.

Six tests cover the new type, including one that asserts it matches `Path.GetInvalidFileNameChars()`
exactly when the host really is Windows.

**Test-fixture fixes — the code was right**

Ten tests hardcoded Windows paths (`@"D:\SteamLibrary\steamapps\common\..."`) or Windows-separator
expectations. They now build both sides with `Path.Combine` via a new
`KinetixModManager.Tests/TestPaths.cs`, so they test which folder a mod lands in rather than which
character separates one from the next. On Windows they produce byte-for-byte the same strings as
before.

The Maven-coordinate theory in `MinecraftLauncherTests` is the clearest case of the code being
right: `MavenToRelativePath` uses `Path.DirectorySeparatorChar` deliberately, because its output goes
on the classpath handed to `java`, and Minecraft is the one supported game that genuinely runs on
Linux. The test was asserting Windows. Since an `[InlineData]` argument must be a compile-time
constant, the expectation is now written with forward slashes and composed in the test body.

### The `en.json` audit

Checked separately, after Sean mentioned he thought something was wrong with it. He was right, and
it was worth finding — **two messages were being read aloud with their own punctuation in them.**

`Loc.T` returns the key itself when a key is missing, and catches `FormatException` and returns the
**unformatted template** when a phrase is given fewer values than it has placeholders. Both failures
are silent to a sighted developer and both are read out by the screen reader.

| Bug | Effect |
|---|---|
| `reports.dupId` — *"UniqueID "{0}" is used by {1} mods: {2}"* called with **2** of 3 values (`Form1.Reports.cs:72`) | Check My Setup read out *"UniqueID open brace zero close brace is used by open brace one close brace mods"*. The mod count was never passed. |
| `suggested.manualTitle` — *"Download {0}"* called with **0** values (`Form1.SuggestedMods.cs:318`) | The non-Premium manual-download dialog was titled literally `Download {0}`. |

Both are fixed, and **a new guard test makes the whole class of bug impossible to reintroduce**:
`EveryPhraseIsGivenAsManyValuesAsItAsksFor` fails the build when any `Loc.T` call passes fewer
arguments than its phrase has placeholders. It was verified by reintroducing the `reports.dupId` bug
and watching it fail with the exact file, line and phrase.

Otherwise the file is in good order, and better order than most of the codebase:

- Valid JSON, UTF-8 BOM (harmless — `File.ReadAllText` strips it), **1,739 keys, no duplicates**.
- **No missing keys.** Every one of the 1,639 literal keys in the source exists. `SpokenStringGuardTests`
  already guarded this, and it was doing its job.
- **No dead keys.** 18 never appear as a literal, and all 18 are assembled at runtime —
  `settings.contrast.` + 4 enum members, `settings.textSize.` + 3, `curator.category` + 4 ids,
  `sound.` + the 7 `SoundEngine.SoundDescriptions` entries. All 32 were checked by hand and every one
  resolves.
- No malformed format items, no empty values. The ten values with leading or trailing space are all
  deliberate — they are named `…Suffix`, `…Note`, `…Tag` and are concatenated onto other sentences.

One thing left alone, because it is a content decision rather than a defect: `health.mcVanilla` is
the **only** string of 1,739 that begins with an emoji (`⚠️`, U+26A0 + U+FE0F). It is a
`ReportRow.Text` in the Health Dashboard, so it is read aloud, and depending on the reader's symbol
level it is announced as "warning sign" or dropped entirely. The sentence after it already carries
the warning in words. Worth removing for consistency, but that is Sean's call.

`SpokenStringGuardTests.AppSourceFiles()` was also widened to sweep `Kinetix.Core` as well as the
app. Core has no `Loc.T` call today because `Loc` is still in the app — but `Loc` belongs in the core
eventually, and a guard that silently stopped covering the phrases on the day they moved, while
staying green, would be worse than no guard.

### Verification

```
dotnet build KinetixModManager.slnx -p:EnableWindowsTargeting=true  →  0 warnings, 0 errors
dotnet test  KinetixModManager.Tests                                →  1016 passed, 0 failed
```

Nothing here changes behaviour on Windows: every path fix produces the identical string there, and
the invalid-character set is Windows' own.

### Still outstanding from this review

Phase 1 fixed the portability bugs the tests could see. These were found by reading and are not yet
done:

These were all addressed in the follow-up commit; see `TODO.md` for what genuinely remains.

Next is Phase 2: lifting the 40 domain models out of `Form1` (§4.2).


---

## 13. The three robustness items — resolved 2026-09-13

Sean asked for a judgement rather than a menu, so each of the three was decided on its merits. One
turned out not to be a problem at all.

### 1. Leaking HTTP connections — real, fixed

Twelve call sites wrote `using var client = new HttpClient(...)`. That reads like careful resource
handling and is close to the opposite: disposing an `HttpClient` disposes the handler beneath it, and
the pooled TCP connection goes to `TIME_WAIT` instead of back to the pool. An update check across
forty mods burned forty connections the OS then held for minutes each. The symptom nobody would
trace to the cause: update checks start failing to connect for a while, then start working again.

`Kinetix.Core/KinetixHttp.cs` (new) holds two long-lived clients — `Api` (60s) and `Downloads`
(30 min) — on a `SocketsHttpHandler` with `PooledConnectionLifetime = 5 min`, which is what makes a
static client safe: it retires pooled connections so a long-running process still notices DNS
changes. All twelve sites now use them. `NexusService.HttpClient` was already doing the right thing
and was left alone.

Where a request needs its own headers — the Nexus API key — it now builds an `HttpRequestMessage`
through the existing `BuildRequest` helper rather than setting `DefaultRequestHeaders` on a shared
client, which would have leaked that key onto every other caller's requests.

**A Phase 0 regression was caught here.** `ModrinthService` and `FabricInstaller` built their
User-Agent from `Assembly.GetExecutingAssembly()`. That was the app while those files were compiled
into it; since the split it is `Kinetix.Core`, whose version is its own. The Modrinth User-Agent had
therefore quietly become `1.0.0`, and Modrinth asks for a User-Agent it can identify and contact.
The app now hands `KinetixHttp.UserAgent` the real version and the project address at startup
(`Program.cs`), and `Kinetix.Core.csproj` carries `<Version>1.6.0</Version>` so its assembly is not
silently unversioned.

Two more instances of the §12 invalid-character bug were found and fixed while in here
(`NexusService.cs:765` and `:980`, both `Path.GetInvalidFileNameChars()` on a downloaded file name).

### 2. `async void` — not a defect; the review was wrong

**Correction.** §5.2 claimed an exception escaping an `async void` "goes straight to the
unhandled-exception handler and kills the process". Not here. `Program.cs:141-157` installs three
nets — `Application.ThreadException` (with `UnhandledExceptionMode.CatchException`),
`AppDomain.CurrentDomain.UnhandledException`, and `TaskScheduler.UnobservedTaskException` — all of
which log, and the first two show the user a dialog naming the log file and let the app carry on.
The comments there already explain the reasoning, down to why an unobserved task is marked observed
rather than escalated into a crash.

The 29 `async void` methods are the ordinary WinForms idiom, and they are covered. **No change
made.** Converting them to `async Task` would be churn without a defect behind it.

### 3. Speech failing silently — real, fixed

The narrow thing that *was* true, and worth more than the other two.

When Tolk will not load, `Speak()` logged it — with a comment noting that "the entire symptom is
silence, with no error to see, by definition" — and then returned having said nothing. The log is
the one place the affected user cannot look, because they are waiting for the app to talk.

It now tells them, once per session, through the two channels that do not depend on Tolk:

- **A sound.** The audio engine is NAudio and has nothing to do with Tolk, so it still works.
- **An ordinary message box.** Tolk failing is not the screen reader failing — it usually means
  `Tolk.dll` is missing or antivirus has blocked it, while NVDA or JAWS is running perfectly and
  reading window contents over MSAA/UIA as always. A plain WinForms dialog is therefore very likely
  to be read aloud in exactly the case where nothing spoken through Tolk can be.

Once only, guarded by a flag: the failure recurs on every phrase, and a dialog per phrase would be a
far worse accessibility bug than the one being reported. Two new strings, `speech.unavailableTitle`
and `speech.unavailableBody`, which the §12 guard tests already cover.


---

## 14. Phase 2 — completed 2026-09-13

**The domain model is out of `Form1`.** 36 types moved to `Kinetix.Core/Models/`, plus four
supporting types that had to come with them. `Form1` is 28,213 lines, down from 28,719, and — far
more to the point — **a second front end can now name every one of these types.**

### A correction to §4.2 first

It said "40 types are declared as private nested classes in `Form1` partials". Close, but not right:
**35 were nested, and five** — `WikiNavigationState`, `WikiResult`, `WalkthroughGuide`,
`LanguageOption`, `ModWikiLink` — **were already top-level `public` classes** simply parked at the
bottom of `Form1.cs`, after the class closes. Those were never inaccessible, only misfiled. The
blocker was real for the other 35.

### What moved

| File | Types |
|---|---|
| `Models/LoadOrderModels.cs` | `PriorityEntry`, `PluginEntry`, `CreationEntry`, `LoadOrderExport`, `RuleItem`, `ConflictRow`, `PluginSlotUsage` |
| `Models/KeybindModels.cs` | `KbEntry`, `KbSection`, `ModKeybinds`, `NavNode` |
| `Models/WikiModels.cs` | `WikiNavigationState`, `WikiResult`, `WalkthroughGuide`, `ModWikiLink` |
| `Models/ModSettingRows.cs` | `IniRow`, `IniFileChoice`, `CpRow`, `McmRow`, `StardewRow`, `McmValue` |
| `Models/ListRowModels.cs` | `SaveRow`, `TrackedRow`, `SuiteItem`, `ModDocSource`, `DiscoveryLoadMoreRow`, `HistoryScope`, `LanguageOption`, `GameNotInstalledChoice` |
| `Models/CuratorModels.cs` | `CuratorRow`, `CategoryRow`, `SuggestionRow` |
| `Models/BackupModels.cs` | `SafetyBackupMeta`, `SafetyBackupItem`, `DownloadItem` |
| `Models/ReportModels.cs` | `ReportRow`, `BrokenModFindings` |
| `Models/ImportModels.cs` | `Mo2Mod`, `Mo2ProfileEntry` |

### Four things that had to move with them

Each of these was pure logic that had been private to the window, and each was needed by a model:

- **`Loc`** → `Kinetix.Core/Loc.cs`. Nearly every row's `ToString()` calls `Loc.T`, since a row's job
  is to say how it reads aloud. The localisation catalogue was always core rather than UI; it just
  lived in the app. (`lang/**` stays in the app project, which is where it is copied to output from.)
- **`VirtualKeys`** → `Kinetix.Core/VirtualKeys.cs`. `DecodeVirtualKey` and `VirtualKeyName` turn a
  Windows virtual-key code into "Page Up". They stay Windows-shaped deliberately, and the new file
  says why: the codes belong to the *games*, which write them into their own config files whatever
  machine they are played on. This is the §4.5 example made concrete — the one place in the program
  that knows how to say a key out loud, unreachable by anything else.
- **`LoadOrderRule`** → its own file, lifted out of `AppSettings` where it was nested. It describes a
  fact about a game's plugins, not about how this program stores settings. Serialisation is
  unaffected: Newtonsoft writes by property name and the project sets no `TypeNameHandling`, so rules
  saved by an older build read back unchanged.
- **`PluginSlots`** → the 255 / 4096 plugin ceilings, previously `private const` on `Form1`. Facts
  about Skyrim and Fallout 4, not about the window that displays them.

### Three types deliberately stayed

- **`PromptChoice`** — a `readonly record struct` holding a `DialogResult`. Genuinely WinForms.
- **`ProgressAnnouncer`** — 111 lines holding a `Form1` reference and calling `Speak`, `_soundEngine`
  and `SetProgressTitle`. It is not a model at all; it is a presenter, and it is waiting on
  `IAnnouncer` and `ISoundEngine` in Phase 3. Moving it now would only move the coupling.
- **`AppTab`** — an enum naming the tab strip. A UI concept, and it belongs with the UI.

### The guard caught its own gap

`SpokenStringGuardTests` went red immediately after the move: its source sweep was
`TopDirectoryOnly`, so the phrases used by the new `Models/` folder registered as unreferenced. That
is exactly the coverage loss the §12 note predicted — a guard silently ceasing to cover the thing it
guards — and it failed loudly instead. The sweep is now recursive, skipping `bin/` and `obj/`.

### Verification

```
dotnet build KinetixModManager.slnx -p:EnableWindowsTargeting=true  →  0 warnings, 0 errors
dotnet test  KinetixModManager.Tests                                →  1016 passed, 0 failed
```

Next is Phase 3: the eight interfaces (§7). `ProgressAnnouncer` is the natural first customer.


---

## 15. Phase 3 — four seams of eight, 2026-09-13

Four of the eight interfaces in §7 are defined, implemented and wired. The other four are deliberately
not, and the reason is given below — it is not that they ran out of time.

### What was built

| Interface | Windows implementation | What it removes from the rest of the program |
|---|---|---|
| `IAnnouncer` | `Platform/TolkAnnouncer.cs` | Every direct Tolk call |
| `ISoundEngine` | `SoundEngine` (already had the shape) | NAudio |
| `ISecretStore` | `Platform/DpapiSecretStore.cs` | DPAPI, out of `AppSettings` |
| `IDispatcher` | `Platform/WinFormsDispatcher.cs` | `Control.Invoke` / `BeginInvoke` |

The measurable result:

- **Tolk is now named in two files** — its own P/Invoke declarations, and the one class implementing
  `IAnnouncer`. It was four. All ~537 `Speak(...)` call sites are untouched, because `Form1.Speak`
  became a delegation rather than a rewrite: one seam, no call-site churn.
- **`ProtectedData` is named in one file.** `AppSettings` now holds an `ISecretStore` and knows nothing
  about how a secret is kept. This was one of the genuine "will not run at all off Windows" blockers,
  since `ProtectedData` throws rather than degrading.
- `PlainTextSecretStore` in the core is the default, so settings can be read and written with no
  keyring at all — which is what the tests need. It is named for what it does on purpose: a class with
  a reassuring name that stores a key in the clear is how a build ships doing exactly that.

`IAnnouncer` extends `IDisposable`, which was not in the §7 sketch. A speech connection is a resource
on every platform — Tolk is unloaded, speech-dispatcher is closed — and the shutdown path has to say
its goodbye and *wait* before letting go, because disposing mid-sentence cuts the sentence off.

### The first real customer, and the proof it works

`ProgressAnnouncer` was the natural test of whether these seams are real, and it moved to
`Kinetix.Core/ProgressAnnouncer.cs`.

It used to hold a `Form1` and reach through it for five different things. It now takes `IAnnouncer`,
`ISoundEngine`, `IDispatcher` and a new three-member `IProgressDisplay` — the user's chosen feedback
mode, the sentence that opens an operation, and somewhere to put a live percentage — which `Form1`
implements with methods it already had. Only the last of those is genuinely a window's job, and
splitting it that way is what let the actual behaviour move: the decile rule, the tone throttling, and
the rule about when it is worth crossing to the UI thread at all.

**That behaviour is now tested.** `ProgressAnnouncerTests` has 8 tests covering things that previously
could not be exercised without standing up a window: that the operation is named once rather than on
every update, that only whole deciles are spoken (a large download reports thousands of times), that
100% is deliberately never spoken because the caller's own success message follows it, that Tones-only
says nothing and Speech-only plays nothing, that Off means off, and that finishing resets the display
rather than leaving a stale "Downloading Project Fluent 100%" to be read out later — which once landed
between a prompt's question and its answer.

`ProgressFeedback` moved to the core with it: it names a user preference, not a widget.

### The four that were not built, and why

`IPrompts`, `IBrowserHost`, `IGameLocator` and `IModSource` are **not** defined yet. Writing an
interface nobody implements is a guess dressed as a design, and each of these is a guess for a
different reason:

- **`IPrompts`** — 177 `SpeakBox` calls plus 12 `ShowDialog`, and the real design question is the
  inline-prompt system, which deliberately avoids modal dialogs. The right shape follows from draining
  those screens in Phase 4, not from sketching it now.
- **`IBrowserHost`** — depends on an unanswered question (see TODO): whether the first Linux release
  embeds WebKitGTK at all, or opens the system browser. Those two need different interfaces.
- **`IGameLocator`** — depends on the Proton-vs-native decision. A locator that resolves into a
  compatdata prefix is a different contract from one that finds a native install.
- **`IModSource`** — the most worthwhile of the four, and the largest: it means reconciling
  `NexusService` (instance, stateful, API key) with `ModrinthService` (static, stateless). Worth its
  own change rather than being tacked onto this one.

### Verification

```
dotnet build KinetixModManager.slnx -p:EnableWindowsTargeting=true  →  0 warnings, 0 errors
dotnet test  KinetixModManager.Tests                                →  1024 passed, 0 failed
```


---

## 16. Proton vs native — answered by the shipped docs, 2026-09-13

> **Superseded in part by §20.** The speech findings below are correct and worth keeping. The conclusion
> drawn from them — scoping the Linux build to two games — was wrong, and is corrected there.

This was listed as a decision needing an answer before the GTK head could be designed. It turns out the
repository already answers it, in the accessibility-mod documentation the app itself bundles and shows
under F3.

**The question is not whether Proton is accessible. It is whether the game's accessibility mod can
still speak once it is inside a Proton prefix** — and for half the supported games, it cannot.

| Game | Runs natively on Linux? | How its access mod speaks | Verdict |
|---|---|---|---|
| **Minecraft Java** | Yes (it is Java) | `minecraft-access.md:169` — *"On Linux, the mod uses Speech Dispatcher for speech output"*, plus eSpeak NG | ✅ Works |
| **Stardew Valley** | Yes (native Steam build) | `stardew-access.md:3` — *"accessible to blind screen reader users on Windows, Linux and Mac OS"* | ✅ Works |
| **Skyrim SE** | No — Proton | `skyrim-access.md:3` — *"It uses the NVDA screen reader to voice nearly everything"* | ❌ Silent |
| **Fallout 4** | No — Proton | `fallout4-access.md:11` — *"works with the NVDA, JAWS, and SAPI screen readers"* | ❌ Silent |
| **Witcher 3** | No — Proton | WitcherAccess drives a Windows reader | ❌ Silent |
| **Moonlight Peaks** | No — Proton | BepInEx plugin, Windows speech | ❌ Silent |

NVDA and JAWS are Windows programs. They do not run inside a Proton prefix, and Wine's SAPI is not a
substitute. So a Proton-hosted Skyrim would load Skyrim Access perfectly, start, play — and say nothing.
That is precisely the failure mode this manager exists to prevent, and the one the CHANGELOG already
describes for Minecraft: *"The game starts, plays perfectly, and simply never speaks."*

Managing mods for a game that cannot talk to you is not a smaller feature. It is a worse one than not
offering it, because the user has no way to tell a broken install from an unsupported one.

### The decision

**Native. Two games: Minecraft Java and Stardew Valley.** Both run natively, both have accessibility
mods that speak through speech-dispatcher — *the same speech-dispatcher the GTK head talks to*, which
means one working speech stack rather than two.

Three things follow from this, and each simplifies the work:

1. **`IGameLocator` no longer needs Proton prefix resolution** for a first release. It needs
   `~/.steam/steam/steamapps/common/Stardew Valley` and `~/.minecraft`. `SteamLibraryLocator` already
   parses `libraryfolders.vdf` and already passes its tests on Linux.
2. **Mod folder names must still obey Windows rules** — `WindowsFileName` stays exactly as it is. Stardew
   mods are shared between machines and SMAPI is cross-platform, so a name Linux accepts and Windows
   refuses is still a bug.
3. **The Nexus API key is not needed for a first release.** Minecraft uses Modrinth (no key) and
   Stardew's mods are on Nexus — so Nexus is needed for Stardew but not for a Minecraft-only v1, which
   makes Minecraft-first cheaper still.

Proton support is not ruled out forever. It becomes worth building the day a Skyrim access mod can speak
on Linux, and not before.

---

## 17. The GTK head — a working spike, 2026-09-13

Two new projects, and they run.

### `Kinetix.Platform.Linux` (`net10.0`, no UI toolkit)

`SpeechDispatcherAnnouncer : IAnnouncer` — the Linux counterpart to `TolkAnnouncer`, P/Invoking
`libspeechd.so.2`. **Verified speaking aloud on this machine** (speech-dispatcher 0.12.1, eSpeak NG).

It is deliberately not a port of the Tolk one. Tolk hands text to whichever screen reader is running;
speech-dispatcher *is* the speech layer. There is no bridge to be unloaded from underneath it, and
stopping speech is a real operation rather than a best effort. `IsSpeaking` returns false honestly, with
a comment saying so — speech-dispatcher can answer it properly through threaded-mode callbacks, which
this does not use yet, and that is the one place Linux can do better than Windows rather than worse.

Kept free of GTK on purpose: speech, secrets and game detection are not a toolkit's business, and the
same implementations would serve a headless build or a different front end.

### `Kinetix.Gtk` (`net10.0`, GirCore 0.7.0)

A real window, driving `Kinetix.Core` directly. **Installed** lists the mods in `~/.minecraft/mods`,
reading each jar's `fabric.mod.json` through `MinecraftLayout.ReadModInfo` — the same reader the WinForms
build uses, unchanged — and Space enables or disables one through `MinecraftLayout.PathWithEnabled`, so
the rule about what a disabled mod is called stays in the core. **Find Mods** searches Modrinth live
through `ModrinthService.SearchAsync`, no API key. F6 cycles focus, F5 refreshes, Ctrl+F searches: the
Windows shortcuts, on purpose, so nobody has to learn the app twice.

It shares `lang/en.json` with the WinForms build rather than copying it — two front ends disagreeing
about what a sentence says would be worse than either wording.

Accessibility decisions worth recording, since they are the point of the exercise:

- **A row is one label holding a whole sentence**, not a grid of cells. Orca reads a row's contents in
  order, so three labels become three stops to arrow through instead of one fact. This is the same
  reasoning as every `ToString()` on the row types in `Kinetix.Core/Models`.
- **Disabled comes first** in a row's wording — "disabled, Sodium 0.5.8" — because someone arrowing a
  list to find what is switched off should not have to hear the whole name first.
- **Focus moves are announced.** Orca describes the widget that gained focus but not why, and a list it
  has already described reads as a bare row, leaving the user unsure the key did anything.
- **The search box is labelled by a `Gtk.Label` with a mnemonic**, not by placeholder text, which Orca
  does not treat as a name.
- **The status line is spoken as well as shown.** A label changing is silent to a screen reader.

### What the spike does not do, and does not pretend to

- Minecraft only. Stardew needs `ScanMods`, which is still in `ModFileSystem` in the WinForms app.
- Its strings are English literals rather than `Loc.T` calls. The catalogue is wired and available;
  using it is Phase 4 work, and the guard tests deliberately do not cover this project yet.
- No installing, updating, profiles, backups, wiki or walkthroughs.
- The Minecraft version for search is hard-coded to 1.21.1.

### Does building this now block anything later?

No — and that was the question worth asking. It is ~450 lines against interfaces that already existed,
so it commits nothing: it holds an `IAnnouncer` and an `IDispatcher` and calls into the core, exactly as
the WinForms head does. Nothing in `Kinetix.Core`, `KinetixModManager` or the tests changed to
accommodate it. When Phase 4 produces presenters, this window becomes their first consumer rather than
something to be unpicked.

What it buys is proof, of the three things that were still assumptions: that the core genuinely runs on
Linux, that a real screen reader reads a GTK head built this way, and that the seams from Phase 3 are the
right shape — `IAnnouncer` needed no change at all to gain a second implementation.

### The one thing that is now a hard blocker

**WebKitGTK is not installed on this machine**, and `webkit2gtk-4.1` / `webkitgtk-6.0` are absent. The
wiki, walkthrough and mod-description browsers are required to be in-app rather than opening a separate
browser window, so `IBrowserHost` has to be WebKitGTK and there is no .NET binding for it — it needs
hand-written P/Invoke. That is now the largest single unknown in the Linux head, and it is on the TODO.


---

## 18. Speech routing on Linux — a correction from a real user, 2026-09-13

The spike's first announcer talked to speech-dispatcher directly. That was wrong, and it was the kind of
wrong that only a screen-reader user finds. Cody's report, testing the window with Orca running:

> *"When I press buttons like F5 it speaks through speech dispatcher which is slow speech rate and all
> that and I can't interrupt it. Everything should go through Orca unless Orca is not running."*

### Why it was wrong

speech-dispatcher is the layer **underneath** Orca, not beside it. Going straight to it produced a second
voice, at the daemon's default rate and voice, ignoring every preference the user had configured — and
uninterruptible, because Orca's interrupt key silences Orca, not some other client of the same daemon.

The mistake was reasoning from the wrong half of the Windows design. Tolk looked like "the thing that
speaks", so speech-dispatcher looked like its Linux equivalent. **Tolk does not speak.** It asks NVDA or
JAWS to speak, which is why the user's voice, rate and interrupt key all keep working on Windows. The
faithful port of that is not a speech daemon — it is asking the screen reader.

### What it does now

`gtk_accessible_announce` (GTK 4.14+, present in 4.20.4 here) posts the text through AT-SPI as an
announcement on the window, and Orca reads it in the user's own voice, at their rate, obeying their
interrupt key. `interrupt: true` maps to the HIGH (assertive) priority and `false` to MEDIUM (polite),
which is the same distinction the call sites have always meant.

speech-dispatcher survives as the fallback for when no screen reader is running at all — because an
announcement with nothing listening is silence, and silence is this program's worst failure.

### Detecting a screen reader, and the property that lies

`org.a11y.Status.**ScreenReaderEnabled**` sounds exactly like the question and returns **false** on this
machine with Orca running and reading. It reflects a desktop setting Orca does not necessarily set, and
is only dependable inside a full GNOME session. Trusting it would have routed every announcement to the
fallback on precisely the machines that least need one.

`org.a11y.Status.**IsEnabled**` is the one that answers truthfully — accessibility is on and the AT-SPI
bus is live. Verified at runtime: the log now reads *"a screen reader is running; announcements go
through AT-SPI"*.

### The larger rule, which is not a Windows rule

**Let the reader read. Announce only what it cannot infer.**

With Tolk, the app is handed the reader's queue, so the Windows build announces list rows and focus moves
itself. Under AT-SPI, Orca is already doing that — so the same announcements arrive as duplicates, and an
assertive one cuts off Orca's own description to repeat it. Removed from the spike accordingly:

- **Row selection.** Orca reads the focused row. The row is one label holding the whole sentence exactly
  so that what Orca reads is the right thing.
- **F6 focus moves.** Orca names the widget that gains focus.

Kept, because Orca has no way to know them, and made polite rather than assertive so they queue behind it:
the mod count on opening, the result of a toggle (the row's text changes but focus does not move, so Orca
has no reason to re-read it), search result counts, and errors.

This is a genuine behavioural difference between the two heads rather than a bug that was fixed, and it is
the strongest argument yet for `IAnnouncer` being an interface rather than a shared implementation: the
two platforms do not merely spell speech differently, they divide the work differently.

**Not yet verified by ear.** The change is correct by design and the routing is confirmed in the log, but
nobody has listened to it since it was made.


---

## 19. Mod scanning moved to the core, 2026-09-13

`ModFileSystem` was the second-largest problem in this review after `Form1`: 3,825 lines, `static`, doing
roughly eight jobs. It is now **3,114**, and the 748 lines that left are the half a second front end needed
first — a mod manager that cannot list your mods has nothing to show you.

The split is along one line: **reading what is installed, against changing it.** Scanning all four layouts
went to `Kinetix.Core/ModScanner.cs`, with the five manifest-parsing helpers that only scanning uses
(`ParseNexusId`, `ParseGitHubRepo`, `DetectCategory`, `ExtractVersionFromFileName`,
`CompareVersionsNewer`). Enabling, deploying, backing up, extracting and writing `plugins.txt` stayed in
the app, where the Windows-only parts of them belong.

### Two things had to be untangled

**`AppSettings` could not come along** — it stores keyboard shortcuts as `System.Windows.Forms.Keys`. But
the scan reads only three things from it, so `IModScanContext` names exactly those: `CurrentGamePath`,
`ModCategories`, `ModNotes`. `AppSettings` implements it in three lines. A dependency on the app became a
dependency on what the app happens to know.

**`ScanMods` deleted files.** Six hundred lines into a method named for reading, on finding two copies of
one mod, it undeployed the older one, backed it up, and deleted its folder — on every refresh, with
nothing in the name to suggest it. The behaviour is unchanged and is still what Windows does, but it is now
an `Action<GameMod>? removeSuperseded` parameter supplied by the caller. It is visible in the signature,
it sits in `Form1.ModList.cs` where a reader will find it, and **a caller that passes nothing gets a scan
that only ever reads** — which is what the GTK head passes.

### It has tests now, for the first time

Seven of them, and they could not have existed a day ago: scanning lived in the WinForms app, and the test
project deliberately does not reference that app, so the most-run logic in the program — every refresh,
every game switch, every install ends in a scan — was never exercised.

They pin the Minecraft layout, which is the one worth pinning first: the only layout where a mod is a file
rather than a folder, where the metadata is inside a zip, and where a mod is disabled by a suffix rather
than a prefix. That a jar is read by its manifest rather than its file name; that a `.jar.disabled` is
still listed, because hiding it would leave no way to switch it back on; that a jar with no readable
manifest still appears, because Fabric will try to load it whatever the manager thinks; and that scanning
deletes nothing when no removal is supplied.

### The GTK head now runs the same scan as Windows

It had been enumerating jars by hand, because the real scan was unreachable — and that copy would have
drifted from the real thing the first time a rule changed. It now calls `ModScanner.ScanMods`, the same
call the WinForms build makes, with no `removeSuperseded` so it only reads.

This is also what unblocks **Stardew Valley** in the Linux head, which was the largest remaining gap after
§16 settled on Minecraft and Stardew as the two native games.

### Verification

```
dotnet build KinetixModManager.slnx -p:EnableWindowsTargeting=true  →  0 warnings, 0 errors
dotnet test  KinetixModManager.Tests                                →  1031 passed, 0 failed
```


---

## 20. Correction: this is a mod manager for games, not for Minecraft — 2026-09-13

§16 established a real fact and then drew the wrong conclusion from it. Cody's correction:

> *"Every game supported here needs to work so the program shouldn't be specifically for Minecraft. It is
> an accessible mod manager for games in general, just so you understand."*

He is right, and the error is worth naming because it is an easy one to repeat.

### What was conflated

Two different questions got answered as one:

1. **Can the manager manage this game's mods on Linux?** For all six: yes. Installing, enabling, sorting
   load order, checking updates — none of that depends on the game being able to speak.
2. **Will the game itself talk to you when you play it under Proton?** For Skyrim, Fallout 4, The Witcher 3
   and Moonlight Peaks: probably not, because their access mods drive NVDA or JAWS.

The second is a fact about the **games**, not about this program. Dropping four games from the manager on
account of it is like refusing to sell someone a screwdriver because their shed has no light. People mod on
one machine and play on another; access mods gain platforms; and a user is entitled to decide for
themselves. The right response is to **tell** the user what to expect, never to withhold the game.

### A second thing §16 got wrong, in the manager's favour

It implied Proton support means re-teaching the manager where mods live. It does not. **Under Proton the
game's files sit in `steamapps/common/<Game>` exactly as a native install would** — Proton changes how a
game runs, not where it is installed. What lives inside the compatibility prefix is the *Windows user
profile*: `Documents\My Games` with Skyrim's INIs and saves, and `%LOCALAPPDATA%`.

So mod paths, which are relative to the game folder, already work on Linux for every game. Only saves and
INIs need the prefix. That is a much smaller job than §16 implied, and it is why `IGameLocator` asks two
questions rather than one.

### What changed as a result

- **`IGameLocator`** (`Kinetix.Core/Abstractions/`) — `InstallFolder`, `UserDocumentsFolder`,
  `LibraryRoots`. Two questions, because on Linux they have different answers.
- **`LinuxGameLocator`** (`Kinetix.Platform.Linux`) — finds installs through `SteamLibraryLocator`, which
  was already in the core and already passing its tests off Windows, because a Steam library is laid out
  identically on both. Covers the four places Steam installs itself, Flatpak included, and resolves a
  Proton prefix's `drive_c` for the user-profile side.
- **The GTK head has a Games tab**, built from `GameProfiles.All` rather than naming any game. Uninstalled
  games are listed and say so — a blind user who cannot see an empty list has no way to tell "not
  supported" from "not installed yet", and the first is a reason to give up on the program.
- **Enabling and disabling goes through `ModEnableState`**, so each game's own rule applies — a leading dot
  for Stardew, a tilde for The Witcher, a move out of `plugins` for BepInEx, a suffix for Minecraft.
  Reimplementing Minecraft's rule locally, which the spike did, is how two front ends start to disagree
  about what "disabled" means.

### The wiki URLs moved to where the per-game data lives

Opening a wiki needed the URLs, and they were two `game switch { ... }` expressions inside
`Form1.Wiki.cs` — the exact shape `GameProfiles` exists to replace, and its own doc comment says so. They
are now `WikiApiUrl` and `WikiArticleBase` on `GameProfile`, filled in for all six games, and `Form1` looks
them up instead of switching.

---

## 21. The in-app browser works — 2026-09-13

`webkitgtk-6.0` 2.52.5 is installed and links `gtk-4`, which is the build that matters: `webkit2gtk-4.1` is
the GTK3 one and will not embed in a GTK4 window however similar the name looks.

`Kinetix.Gtk/WebKitView.cs` is a hand-written P/Invoke binding — there is no .NET binding for WebKitGTK and
GirCore does not ship one. The surface needed is small, which is what makes it viable: load a URL, read the
title and address, go back, reload. The native widget is adopted into GirCore's object system with
`InstanceWrapper.WrapHandle`, so it sits in a GirCore layout as an ordinary child.

The window has a **Wiki** tab that loads the active game's wiki in-app.

**Confirmed by ear on 2026-09-13: Orca reads the embedded page, and F6 cycles between the tab strip and
the web view.** That was the last technical unknown in the Linux port. Everything remaining is work rather
than risk — no piece of this is now waiting on something that might turn out to be impossible.

Getting there took one bug worth recording, because it is the kind that looks like a platform limitation
and is not. `CycleFocus` had been written as "page 0, or everything else" when there were two tabs; adding
the Games tab shifted every page number underneath it, so F6 on the Wiki tab was toggling controls
belonging to a different tab and the web view could not be reached at all. The symptom — "I can't tab into
the webview" — is indistinguishable from WebKitGTK not being focusable. It is a list of stops per tab now,
which cannot drift the same way.

### Verification

```
dotnet build KinetixModManager.slnx -p:EnableWindowsTargeting=true  →  0 warnings, 0 errors
dotnet test  KinetixModManager.Tests                                →  1031 passed, 0 failed
```


---

## 22. Phase 4 — six screens, and where it stops being worth it, 2026-09-13

Six screens drained, in the order §8 set out. The pattern held throughout: what **locates, decides or
reads** goes to the core; what draws and wires stays; and the call sites around each screen are left
reading as they did, behind one-line wrappers.

| Screen | What moved | Tests |
|---|---|---:|
| SMAPI Log | `LogAnalyzer`, `LogEntry`, `GameLogFiles`, per-game log names | 9 |
| Dependencies | `BethesdaPlugins`, `ModDependencies` | 10 |
| Profiles | `ModProfile`, `ProfileStore` | 14 |
| Updates | `ModVersions` — the update decision itself | 23 |
| Install | `ModInstaller` | 9 |
| ModList | `BackupStore`, `BackupItem` | 10 |

**1,009 → 1,117 tests.** Every one of the 108 covers a rule that had no way of being checked while it
lived inside a window.

### The numbers, without dressing

| | Start | Now |
|---|---:|---:|
| `ModFileSystem` | 3,825 | **2917** |
| `Form1` | 28,719 | 27909 |
| `Kinetix.Core` | 0 | **15860** across 90 files |

`Form1` moved about 3%, and that is the honest picture rather than a disappointing one. What comes out of
these screens is decision logic — dense, small, and the part a second front end cannot do without. What
stays is widget construction, which genuinely belongs to a WinForms head. `ModFileSystem` is where the
reduction actually happened, because it was full of logic that had no business being there.

### Two defects found by extracting

- **Profile names were never sanitised.** Save and delete each built `name + ".json"` from raw user input,
  and *differently* — so a profile called `Mage/Thief` failed to save, one ending in a dot saved under a
  name Windows refused to open, and an awkward name could be listed and then refuse to be deleted. One rule
  now, with a test that saves and deletes an awkward name to prove the two paths agree.
- **`ScanMods` deleted mod folders** (§19). Still the most significant thing found in this whole pass.

### Where Phase 4 should stop

**Here, for now.** `ShowSettings()` (1,252 lines) and `SetupAccessibleUI()` (1,077) are the two biggest
methods left and together are 2,329 lines — but they are almost entirely widget construction and event
wiring. Extracting them would move lines between files without giving the core anything it can use, and
§8's target of "`Form1` under 5,000 lines" was a number chosen before anyone had looked at what those lines
actually are. It is not a good target and should not be chased.

What is worth doing next is not more of this. It is `ExtractModAsync` — the archive pipeline, and the
largest remaining thing in `ModFileSystem` — because it is what stops the GTK head installing mods for any
game but Minecraft. That is a capability, not a tidy-up.

## 23. The archive pipeline moved to the core, 2026-09-14

§22 named this as the one thing worth doing next, on the grounds that it is a capability rather than a
tidy-up. It is done, and it turned out to be three things rather than one.

### What moved

`Kinetix.Core/ModArchive.cs`, 399 lines, and with it everything underneath an install that is about
*containers* rather than about *layouts*:

| | |
|---|---|
| `DetectFormat` / `FormatOf` | signature sniffing, with the extension as the fallback it always was |
| `Extract` | one entry point; routes zip / 7z / rar and reports 0–100 |
| `GuardAgainstEscapedEntries` | the post-extraction check, now link-aware (below) |
| `ExtractNested` | one level of archives-inside-archives |
| `TempRootFor` | staging on the destination's own filesystem |
| `ReadModIds` | the Stardew ids inside a zip, without extracting it |
| `ModArchiveContentException`, `DescribeMissingManifest` | what the user is told when the download is the wrong one |

`ModFileSystem` is down from 2,917 lines to 2,502, and has lost four different ways of deciding how to unpack something. It now calls
`ModArchive.Extract` from all four of its install paths, which is itself a fix: three of them routed on the
file extension alone, so a `.7z` named `.zip` — which is exactly what the NXM resolver produces when the CDN
gives no filename — reached `ZipFile` and failed with "End of Central Directory record could not be found".
Only `ExtractModAsync` had been taught to sniff the bytes.

### 7za.exe is gone

The Windows app unpacked `.7z` by downloading `7za920.zip` from 7-zip.org on first use, extracting
`7za.exe` from it, and shelling out. That is a 2010 binary, unsigned, fetched over the network in the middle
of an install, and absent by definition on Linux — one of the reasons the GTK head could only install a mod
that arrives as a single `.jar`.

SharpCompress 0.49.1 reads the same archives in-process, including the LZMA2 and BCJ2 filters 7za920
predates. Removing the download removes a platform dependency, a supply-chain surface and a failure mode
("the install just stopped") that no log would have explained, and it very likely fixes more archives than
it breaks: 7za920 cannot read some of what modern 7-Zip writes.

One detail is worth recording because it was a deliberate choice rather than an accident. Both 7z and RAR
are commonly **solid** — every file in one compressed block — and asking SharpCompress for entry 200 off an
`IArchive` decompresses the 199 before it again. A hundred-file mod extracted that way takes minutes.
`ModArchive` reads forwards through a single `IReader` instead, which is the difference between an install
and something the user reports as a hang.

### The escape guard was checking the wrong thing

The old check walked the extracted tree and compared each entry's *path* against the extraction root. That
catches `../../autoexec` in an entry name. It does not catch a **symlink** whose name is entirely innocent
and whose target is the user's home directory — and what happens immediately after extraction is a copy,
hard-link or move into the game folder.

The guard now resolves links and refuses one leading outside, follows none of them (so two links pointing at
each other cannot loop), and runs on every install path rather than one. Entry names are also rejected
*before* any byte is written rather than after, on both the zip and the SharpCompress paths.

No archive has been seen doing this. It was reachable, which was the point.

### Two portability defects fixed on the way

- **Backslashes in zip entry names.** An archive written on Windows stores `ExampleMod\manifest.json`. On
  Linux that is one file with an odd name at the top level, and a mod whose folder structure has been
  flattened — an install that reports success and does nothing. Now read as the separator it is.
- **`TempRootFor` on a machine without drive letters.** The old rule was "the destination drive's root plus
  `KinetixModManager.tmp`", which off Windows means trying to create a directory in `/` every single time,
  failing, logging it, and falling back to the system temp folder — losing the same-filesystem guarantee the
  whole thing exists for, so the final move became a full copy. Elsewhere it now stages beside the mods
  folder, which is the same filesystem by construction.

### Tests

**1,117 → 1,143.** The 26 cover format detection (including the mislabelled `.7z` that is the commonest real
failure), extraction with folders intact, progress reaching 100, all three escape cases, nested archives,
reading ids without extracting, and where staging lands.

One fixture is a **real solid LZMA2 7z**, made with 7-Zip 23.01 and committed at 4.8 KB. That is the whole
point of it: the change being tested is that a genuine 7z can be read without `7za.exe`, and a hand-built
container would only have tested the assumptions the code already makes.

### What this does not do

It does not move `ExtractModAsync`. What is left in that method after the archive work is *layout*: what an
unpacked Stardew manifest folder, Bethesda staging tree, BepInEx plugin or Witcher `mod…` folder means, and
where its files belong. That is the next thing, and the Stardew branch is the piece to take first — it is
self-contained, entirely portable, and the GTK head's next stated goal. Two real defects are sitting in it:

- The common-prefix search for a multi-mod download compares path *strings*, so `Mods/Auto` is treated as a
  parent of `Mods/AutoFish`.
- The copy rebases every path with `string.Replace(source, destination)`, which replaces **every** occurrence
  rather than the leading one — so a mod with its own name repeated deeper in the tree lands in the wrong place.

Neither is reachable from the archive layer, and neither should be fixed without the tests that come from
moving the code.

## 24. Per-game sounds, and what "connect" means in Minecraft, 2026-09-14

Sean asked for two things that sound like one. The first turned out to be mostly built already; the second is
new, and is the more interesting of the two.

### The theme already followed the game

`GameProfiles` has carried a `SoundTheme` per game since Minecraft support landed, `AppSettings.ThemeForGame`
maps a game to it, and `AllowManualTheme` is the opt-out for a user who would rather choose. Minecraft's
profile already said `SoundTheme = "Minecraft"`. The only thing missing was the folder.

So the work was a scaffold — `sounds/Minecraft/` with a folder per event and a note in each saying what
belongs there, plus `sounds/README.txt` describing the convention — and one defect that the scaffold itself
would have triggered.

**A folder that existed and was empty made the manager silent.** `SoundEngine` fell back to the Default theme
when the theme's folder was *missing*, then looked for `.ogg` files and returned quietly if there were none.
That is exactly the state of a theme somebody is halfway through authoring: every folder there, most of them
empty. The manager would have stopped making the sound it used to make, with no error anywhere, in the one
channel a blind user cannot check for themselves.

The rule is now `SoundThemes.Resolve` in the core, judged on the **file** rather than the folder, and the
lookup, the theme-for-game map and the installed-theme list all live there — which is also the groundwork for
`ISoundEngine` on Linux, since choosing the file is the portable half of playing a sound. Three separate
`Directory.GetDirectories(themesPath)` calls in Settings and the Sound Demo became one `SoundThemes.Installed`,
which additionally gives the theme list a stable order with Default first, rather than whatever order the
filesystem returned.

### Minecraft's connect cue was about nothing

For every other game, connect and disconnect mean Nexus: signed in to where the mods come from, or not.
**Minecraft's mods come from Modrinth, which has no accounts at all**, so for Minecraft those cues were
attached to nothing — and worse, one of them fired at a player who could do nothing about it (below).

The connection a Minecraft player has is to a server, and the client states both ends of it in
`logs/latest.log`. `MinecraftServerLog` reads a line, `MinecraftServerSession` keeps the state, and
`MinecraftLogTail` follows the file while the game runs. Four decisions are worth recording:

- **Singleplayer stays silent.** A local world logs `Starting integrated minecraft server` and never
  `Connecting to`, so a player who only plays alone hears neither cue rather than one that means nothing.
- **Chat is excluded outright.** The client writes chat to the same log, so `[CHAT] Steve was disconnected` is
  a sentence in somebody else's game. Matching it would sound the disconnect cue at a player who is still
  online — a false positive that would have been very hard to trace back to its cause.
- **The tail starts at the end of the previous run's log.** That log is still on disk when the game is
  started and very likely ends with the player joining a server, so reading from the beginning would announce
  a connection to a server they left yesterday. The game truncates the file as it opens it, which the tail
  notices (the length drops below where it is) and then reads the new run from its start.
- **Process exit is the backstop.** Quitting straight out of a server closes the process, and whether the
  client got a line out first is not something to depend on. `GameEnded()` reports the disconnection if the
  session was still open, and reports it once.

Leaving a server for the menu is the one transition the client does not state plainly in every version. The
patterns for it are a short, named list in one place, and everything else — dropped, kicked, timed out, quit —
is covered. Extending it is a one-line change with a test beside it.

It plays a sound and says nothing. The game is in front of the player with its own accessibility mod talking;
the manager taking the floor over the top of that would be worse than silence, which is precisely what a
short cue is for.

### Two defects found by asking what the cue meant

- **Minecraft demanded a Nexus API key it never uses.** `RefreshModList` gated on an empty `ApiKey` before it
  checked `UsesNexus`, so a Minecraft player with no Nexus account — which is every Minecraft player who has
  never modded another game — opened the manager to the disconnect cue, Settings forced open, and an empty mod
  list. Nothing they could have typed would have satisfied it.
- **Four places played `connect` for things that connect to nothing**: a load-order sort that changed
  something, the AI provider test passing, and an app or SMAPI update being found. All four are now
  `load_complete`, which is what they are — and it makes the connect cue worth listening for again.

### Tests

**1,143 → 1,175.** Twelve on theme resolution — including the empty-folder fallback, the README-only folder,
and a stable choice between several files — and a guard that fails the build if a game asks for a theme that
is not there or a theme is missing an event folder. Twenty on the Minecraft cue: singleplayer, chat, server
hopping, rejoining after a drop, quitting from inside a server, and the tail's own awkward cases — a
half-written line, a rolled-over log, CRLF, and a log that does not exist yet.
