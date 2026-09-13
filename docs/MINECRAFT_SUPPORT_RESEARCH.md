# Minecraft (Java Edition) support - research

Research notes for adding Minecraft Java Edition as the sixth supported game.
Written 2026-09-11.

**Status: BUILT.** Minecraft shipped as the sixth game in 1.6.0 (26 commits,
2026-09-12 and 13). This document is kept as the reasoning behind the code and as
the record of what was measured rather than assumed - it is not a plan any more.
Two things it does not know about, both found during implementation:

*   Bumping the Fabric profile's `lastUsed` is **not** sufficient. Quick Play world
    tiles carry their own `configId` in `launcher_quick_play.json` and override the
    selected installation, so a world launched from the home screen runs whichever
    installation it was first played with. See section 3.
*   The whole online sign-in chain is now registered and proven, and is waiting only
    on Mojang's approval of the app. See section 8, and poll `tools\mcauth-probe.py`.

Everything in the "Verified" sections was checked against a real install on this
machine or against a live API, and the evidence is recorded so it can be
re-checked when it goes stale. Everything in "Open questions" is genuinely
undecided, not shorthand for something already settled.

---

## 1. The bug that started this, and what it proved

The first thing to understand about Minecraft is that its launcher is where the
accessibility problem actually lives, not the game.

Fabric, Fabric API and United Minecraft were all installed correctly by hand.
The mods still did not load. The cause was not the mods, the loader, or a
version mismatch - it was that **the launcher launched the vanilla profile
instead of the Fabric one**, and nothing anywhere said so.

Three independent proofs, all from the default `.minecraft` folder:

1. `launcher_log.txt` line 542:
   `resolveVersionID: Resolved latest-release:latest-release to 26.2`.
   The launch resolved `latest-release`, not `fabric-loader-26.2`.
2. `launcher_log.txt` line 1696 is the literal Java classpath. It lists about
   ninety jars and contains **neither `fabric-loader-0.19.5.jar` nor
   `sponge-mixin`**. Knot was never on the classpath.
3. `logs/latest.log` opens with `[Datafixer Bootstrap #0/INFO]`. A Fabric launch
   always opens with `Loading Minecraft <ver> with Fabric Loader <ver>` followed
   by `Loading N mods:`. Neither line appears in `latest.log` or in the rotated
   `2026-09-11-1.log.gz`.

The clincher: `.minecraft\config\` did not exist at all. United Minecraft writes
its config file there the first time it loads, so the mod had never once loaded.

**Why it happened.** `launcher_profiles.json` carries
`"profileSorting": "ByLastPlayed"`, and the vanilla profile's `lastUsed`
(`2026-09-11T20:54:24.830Z`) was newer than the Fabric profile's
(`20:53:48.486Z`). The Play button follows the most recently used installation.

**Why this matters for the manager.** "Choose the Fabric profile from the
installations dropdown" is a step the user can silently get wrong, in a launcher
UI that is awkward with a screen reader, with no feedback of any kind when it
goes wrong. The game starts, plays fine, and simply says nothing. That is
exactly the class of failure this manager exists to remove.

**Therefore: the manager must own the profile selection.** See section 3 - it is
a one-line write to `launcher_profiles.json` and it makes this failure mode
impossible.

### Diagnostic recipe, for the log viewer

Reading the first line of `logs/latest.log` distinguishes the two cases with
total reliability:

- `Loading Minecraft 26.2 with Fabric Loader 0.19.5` - Fabric ran. Good.
- `Datafixer Bootstrap` - vanilla ran. The mods folder was ignored.

The `Loading N mods:` block that follows lists every mod the loader accepted, by
id and version. That is the authoritative "what is actually installed" answer
and it should drive the Minecraft health check, not a directory listing.

Second, weaker tell: `config\` exists and is non-empty once any mod has loaded.

---

## 2. The two accessibility mods

There are two, they need different things, and the user should never have to
know the difference.

### United Minecraft

- Repo: `blindgoofball/united-Minecraft`, by NibbleNerds. GPL-3.0-or-later.
- Mod id `united_minecraft`. Latest at time of writing: `1.1.0+mc26.2`,
  published 2026-09-11.
- Declared dependencies, read from `fabric.mod.json` inside the jar:
  `fabricloader >=0.19.3`, `minecraft ~26.2`, `java >=25`, `fabric-api *`.
- **Fabric API is a hard dependency and is NOT bundled.** Two jars needed.
- Speech goes through Prism, which is bundled. No Tolk, no separate screen
  reader shim, unlike the Bethesda and Witcher 3 access mods. Falls back to
  Minecraft's own narrator when no backend is available.
- Config: a single file shared across all worlds and servers, under
  `.minecraft\config\`.
- **Distribution: GitHub releases only.** Not on Modrinth (searched, nothing).
  One `.jar` asset per release, tag shaped `v1.1.0+mc26.2` - the Minecraft
  version is in the tag, which makes version matching trivial.
- In-game narrator toggle is Ctrl+B, or Options > Accessibility Settings.

### Minecraft Access

- Repo: `khanshoaib3/minecraft-access`. Modrinth project id `jGzXyfdm`.
- Latest at time of writing: `1.12.0`, supports MC `26.2`, loaders `fabric` and
  `neoforge` (the project's own docs call the NeoForge path "not recommended").
- Modrinth dependencies: `fabric-api` (P7dR8mSH), `cloth-config` (9s6osm5g) and
  `balm` (MBAkmtvl) are all marked **`embedded`** - shipped jar-in-jar inside the
  mod's own file. `jade` (nvQzSEkH) is `optional`.
- **Needs nothing else. One jar.**
- Distribution: Modrinth, CurseForge and GitHub.
- Config lives under `.minecraft\config\minecraft_access\`. Documented at
  docs.mcaccess.org/config.

### What this means for the suite

The dependency difference is fully machine-readable - from `fabric.mod.json` for
United Minecraft, from the Modrinth version API for Minecraft Access. So the
suite entry should be a **choice**, not a checklist:

> Which accessibility mod do you want? United Minecraft, or Minecraft Access.

Pick one and the manager resolves everything else: Fabric API is added silently
for United Minecraft and silently skipped for Minecraft Access. The user never
encounters the words "Fabric API".

**They should very probably be mutually exclusive.** Both hook narration on the
same screens and running both will almost certainly double-speak everything.
This has NOT been tested - see open questions. Assume exclusive until proven
otherwise; a radio choice that refuses the second is the safe default, and it is
also the simpler UI.

---

## 3. Verified: Fabric installs with no exe, no Java, no admin

This is the finding that makes the whole feature tractable.

`fabric-installer-1.1.2.exe` is a GUI wrapper. The manager does not need it.

Fabric publishes a meta API at `https://meta.fabricmc.net/v2/`, and the endpoint

    GET /v2/versions/loader/{mcVersion}/{loaderVersion}/profile/json

returns **exactly the file the installer writes**. Verified by fetching
`/v2/versions/loader/26.2/0.19.5/profile/json` and diffing it against the
`versions\fabric-loader-0.19.5-26.2\fabric-loader-0.19.5-26.2.json` that the exe
had just produced on this machine: same `id`, same `inheritsFrom`, same
`mainClass` (`net.fabricmc.loader.impl.launch.knot.KnotClient`), same library
list with the same hashes.

So "install Fabric" is three plain file operations:

1. `GET /v2/versions/game` - newest stable Minecraft version (`"stable": true`).
   `GET /v2/versions/loader/{mc}` - newest stable loader for it.
2. Write the profile JSON to
   `versions\fabric-loader-{loader}-{mc}\fabric-loader-{loader}-{mc}.json`.
3. Add or update the entry in `launcher_profiles.json`, and **set its `lastUsed`
   to now** so the launcher preselects it.

The launcher downloads the loader libraries itself on first launch. No admin
prompt, no GUI, nothing a screen reader has to fight.

Step 3 is also the permanent fix for section 1.

Other useful endpoints:

- `GET /v2/versions/installer` - installer builds on Maven, if the jar is ever
  wanted. `1.1.2` is current and marked stable.
- `GET /v2/versions/game` includes snapshots and release candidates, flagged
  `"stable": false`. Filter on that or the manager will happily install Fabric
  for a release candidate.

### Launcher profile file notes

- `launcher_profiles.json` is version 6. It has **no `selectedProfile` key** -
  that was removed. Selection is driven by `lastUsed` plus the `profileSorting`
  setting.
- `launcher_ui_state_microsoft_store.json` looks like a candidate for holding the
  selection but does not. It is JSON behind a `#$ ... $#` "DO NOT EDIT" preamble,
  and the only Fabric reference in it is a seen-dialog flag. Leave it alone.
- Do not write `launcher_profiles.json` while the launcher is running - it
  rewrites the file on exit and will discard the change. Check for the launcher
  and game processes first.

---

## 4. Where Minecraft fits the manager's architecture

The architecture holds up. `ModLayout` is switched on only inside
`GameProfiles.cs` (8 sites); every other consumer goes through the `IsBepInEx` /
`IsBethesda` / `IsWitcher3` helpers. Witcher 3, the most recent game added,
touched 18 sites across 9 files, 8 of them in `ModFileSystem.cs`. That is the
realistic blast radius to plan for.

### What is genuinely new

**A mod is a loose file, not a folder.** Every existing layout is
folder-per-mod. Fabric mods are bare `.jar` files in one directory. This is the
real work and it lands in `ModFileSystem.cs`.

**Disabling is a rename, by suffix.** Verified by extracting
`DirectoryModCandidateFinder.class` from `fabric-loader-0.19.5.jar` and reading
its string constants: it tests for `.jar` and nothing else. So `foo.jar.disabled`
is skipped by the loader. Structurally the same idea as Stardew's leading dot and
Witcher 3's `~` prefix, but `DisabledModPrefix` needs a suffix sibling.

**Detection breaks the current model.** Minecraft is not a Steam or GOG game and
has no game folder at all.

- The copy on this machine is the Microsoft Store package
  `Microsoft.MinecraftJavaEdition_8wekyb3d8bbwe`, launched via
  `shell:AppsFolder\Microsoft.MinecraftJavaEdition_8wekyb3d8bbwe!Game`.
  The `Id` is `Game`, from the package's `AppxManifest.xml`. There is no
  `MinecraftLauncher.exe` anywhere on this disk.
- Other users will have the standalone launcher
  (`%ProgramFiles(x86)%\Minecraft Launcher\MinecraftLauncher.exe`), or a
  third-party one (Prism, Modrinth App, CurseForge) with its own per-instance
  directories.
- The root is `%APPDATA%\.minecraft`. Mods live at `%APPDATA%\.minecraft\mods`.

`GameProfile` declares `SteamAppId`, `GameExeName` and `DefaultInstallFolder` as
`required`. Minecraft is the first game that has none of them, so those need to
become optional, or the profile needs a notion of "not a store game". Worth
doing properly rather than with sentinel values - the whole point of the
`GameProfiles` registry was that an unknown game fails obviously instead of
quietly inheriting Stardew's data.

### What is easier than expected

**Metadata is richer than any other supported game.** `fabric.mod.json` inside
each jar gives id, name, version, authors, license, contact URLs, and a full
dependency map with version ranges. Better than Stardew's `manifest.json`, and
far better than Witcher 3, which gives nothing at all. Same idea as Stardew, just
zipped - read it with `System.IO.Compression`.

**Update checking gets easier, not harder.** Modrinth states which Minecraft
version each file targets, so the "a mod's version is not its download's version"
problem from the update-check overhaul largely evaporates.

### Mod browsing moves off Nexus

Nexus does have a Minecraft section, but it is mostly maps and legacy content.
Fabric mods live on Modrinth and CurseForge.

Modrinth's API is open and good:

- No API key, no auth. Rate limit 300/minute (confirmed from
  `X-Ratelimit-Limit` on a live call). Send a descriptive `User-Agent`.
- `GET /v2/project/{id|slug}` - loaders, all supported game versions.
- `GET /v2/project/{id}/version?game_versions=["26.2"]&loaders=["fabric"]` -
  per-version files with direct CDN download URLs, plus resolved dependencies
  tagged `required` / `optional` / `embedded` / `incompatible`.
- `GET /v2/search?facets=[["versions:26.2"],["categories:fabric"],["project_type:mod"]]`

This makes Minecraft the first game where `NexusService` is not the browse
backend, so an abstraction over "where mods come from" is needed. That is real
work, but Modrinth is a better API than Nexus v1, and the `embedded` dependency
type is precisely what makes the two-accessibility-mods problem disappear.

### ✅ Profiles already work — decided 2026-09-13, nothing was built

The manager's existing Profiles feature turned out to need no Minecraft work at all. A profile stores mod id
→ enabled and applies it through `ModFileSystem.SetModEnabled`, both of which are game-agnostic, and
`SetModEnabled` already knows the `.jar.disabled` rename. Saving and switching profiles works as it stands.

It also solves the two-access-mods problem for free, without separate instances: one profile with United
Minecraft on and Minecraft Access off, another the reverse, in one mods folder — and the player's worlds are
untouched either way, because nothing moves.

The `gameDir` design below is therefore **not implemented**, and is kept only as the answer if separate
instances are ever wanted for their own sake (chiefly: somewhere to try a new Minecraft version without
disturbing a working setup). The cost that made it the wrong default is in the last paragraph.

### If separate instances are ever wanted: `gameDir`

The manager's existing Profiles feature has a natural Minecraft equivalent, and it is not a
new invention: a launcher profile (Modrinth calls it an "instance") takes an optional
`gameDir` key. Point one at its own directory and it gets its own `mods\`, `config\`,
`saves\` and `options.txt`, fully isolated from every other profile.

So a Kinetix profile = a launcher installation + its `gameDir`. Same feature the other games
already have, different plumbing.

This also dissolves the two-access-mods problem for free: United Minecraft in one profile,
Minecraft Access in another, no double speech, switch by picking a profile. And it gives
somewhere safe to try a new Minecraft version without disturbing a working setup - which
matters a great deal here, see below.

The trade-off to decide deliberately: a separate `gameDir` means **separate worlds**. A save
does not follow the player between profiles unless the manager shares the saves folder on
purpose.

### Game updates are the real long-term risk

Minecraft breaks mods hard on every game update. `26.3-rc-2` is already sitting
in the `versions\` folder on this machine. When 26.3 lands, every mod needs a
rebuild **and** the Fabric profile needs regenerating for the new game version.

The existing Game Update Prep feature would earn its keep here more than in any
other supported game. Strong argument for pinning the manager to the newest
Minecraft version that both Fabric and the chosen access mod support, rather than
the newest Minecraft outright - that is the difference between a working install
and a silent one.

---

## 5. Verified: the manager can launch the game itself, with no launcher

Tested end to end on 2026-09-12 and it works. The manager can start Minecraft directly,
with Fabric and the access mod loaded and speaking, without the launcher being involved at
any point.

### What the launch needs

- **Java**: the launcher bundles it. `%LOCALAPPDATA%\Packages\
  Microsoft.4297127D64EC6_8wekyb3d8bbwe\LocalCache\Local\runtime\java-runtime-epsilon\
  windows-x64\java-runtime-epsilon\bin\javaw.exe` on a Microsoft Store install. The
  version JSON names the runtime it wants in `javaVersion.component`, so this is derivable
  rather than hard-coded.
- **Classpath and JVM flags**: reconstructible from the version JSONs, which is exactly what
  the launcher does. 96 entries for Fabric 0.19.5 on 26.2.
- **Main class**: `net.fabricmc.loader.impl.launch.knot.KnotClient` from the Fabric profile
  JSON, plus the `-DFabricMcEmu= net.minecraft.client.main.Main ` flag it also specifies.
- **Game arguments**: the template is in the version JSON under `arguments.game` -
  `--username --version --gameDir --assetsDir --assetIndex --uuid --accessToken --clientId
  --xuid --versionType`.

⚠️ **`--clientId` and `--xuid` must be omitted, not passed empty.** An empty argument cannot
be reliably passed through a Windows process launcher; `Start-Process` rejects the whole
list. Omitting them is fine.

⚠️ **The launcher deletes its natives folder on exit.** It extracts to `.minecraft\bin\<sha1>\`
and cleans up when the game closes, so the manager cannot rely on the leftovers - it must
own its own natives directory. This will present as a mysterious failure on a machine where
the launcher happened to clean up, and as a mysterious success where it did not.

⚠️ **The launcher does not log the game arguments**, only the JVM ones, because the game
arguments carry the access token. So `launcher_log.txt` is a good source for the JVM half
and useless for the other half.

### Offline mode works, and it is the right default here

Launched with a placeholder `--accessToken 0` and `--userType legacy`:

    [00:55:04] Loading Minecraft 26.2 with Fabric Loader 0.19.5
    [00:55:04] Loading 49 mods:  ... fabric-api 0.160.0+26.2, united_minecraft 1.1.0
    [00:55:16] Setting user: SeanTerry01
    [00:55:17] Loaded Prism speech backend 'NVDA' from
               ...\.minecraft\united_minecraft\prism-native\prism.dll

Reached the main menu, loaded a world, played, saved and shut down cleanly. **United
Minecraft picked up NVDA and spoke.**

Exactly three failures in the whole session, all 401s, all multiplayer-only:

- `/player/attributes` - the skin. Cosmetic.
- `/player/certificates` - the signed-chat key pair. Servers only.
- Realms authentication.

Nothing else. Singleplayer is completely unaffected.

For a screen reader user this makes offline the sensible **everyday** mode: no network
round-trip, no token refresh, nothing that can fail between pressing a key and hearing the
main menu. Online becomes a mode you deliberately switch into for a server.

### ⚠️ Identity: pass the REAL uuid, or you become a different player

This is the part that would have quietly eaten a save.

Minecraft keys a character to its uuid, not its name. In `saves\<world>\players\` there are
three files per player, all named for the uuid:

    players/data/<uuid>.dat            inventory, position, health
    players/advancements/<uuid>.json
    players/stats/<uuid>.json

The usual offline-launcher trick is to derive a uuid by hashing `OfflinePlayer:<name>` into
a version 3 uuid. For this account that yields `3ddff6e6-d2fc-3f1a-8923-e04acba3e4e3`,
against the real `7b05bd63-2994-4bcb-97b1-79772ffcc404`. **Different uuid means a brand-new
character in an existing world** - empty inventory, back at spawn, no advancements. The old
character is not deleted, just orphaned, which is worse: it looks like the manager ate the
save.

The game does not validate the token in singleplayer. Identity comes purely from `--uuid`
and `--username`. So **offline mode passes the real uuid**, and an offline session is
byte-for-byte the same player as an online one.

Verified: after an offline session, all three files under `7b05bd63-...` had updated
timestamps, the stats recorded the session (`walk_one_cm` 37994, `leave_game` 8), and no
`3ddff6e6-...` file existed anywhere in the save.

The real uuid is available without any credential: `minecraftProfile.id` and
`minecraftProfile.name` in `launcher_accounts_microsoft_store.json`. That file holds **no**
token - `accessToken`, `azureToken` and `mojangClientToken` are all empty strings. The
credential lives encrypted in `launcher_msa_credentials_microsoft_store.bin`, which is why
the manager cannot piggyback on an existing launcher login and must either sign in itself
or run offline.

### The launcher cannot be driven from outside

Checked, so nobody re-checks it:

- The Store package registers one URI protocol, `ms-xbl-71a0e6a2` - an Xbox Live auth
  callback. There is no `minecraft://`.
- Its `appExecutionAlias` is `MCPlaceholderStub.exe`. A placeholder, not an entry point.

There is no "launch profile X" back door. It is launch Java ourselves, or accept the
launcher's UI.

---

## 6. Account and identity design

**DECIDED 2026-09-12: build both modes.** Offline is proven to cover singleplayer completely,
but online ships too, so servers, Realms and skins all work. This is not a fallback
arrangement - both are first-class, and the user moves between them at will.

**Sequencing, though: ship offline first.** Online is blocked on an Azure directory that a
personal Microsoft account does not have (see the blocked note below), and *nothing else needs
it*. The mod layout, the Fabric install, profiles and F5-to-game can all be built, shipped and
ear-tested against offline mode alone. Online then lands as a pure addition whenever the Azure
and approval paperwork clears, with no rework - the identity plumbing is the same either way,
because offline already passes a real uuid.

Sign in **once**, then choose online or offline freely, in either direction, at any time.
This is Prism Launcher's rule and it solves two problems with one decision: the manager
learns the real uuid (so offline sessions are the same character), and a mod manager that
launches Minecraft is not doing so with no account behind it at all.

Identity resolution, in order:

1. The manager's own Microsoft sign-in (OAuth device-code flow). Gives a refresh token, so
   online mode works and the uuid is authoritative.
2. Failing that, read `minecraftProfile.id` and `.name` out of the official launcher's
   accounts file. Public data, no credential, and present for anyone who has ever played.
3. Failing that, a typed name with a derived offline uuid - and the manager must say plainly
   that this creates a **new** character.

The device-code flow is worth wanting for its own sake, not just tolerating: the manager
speaks a short code, the user types it into a browser where their screen reader already
works well, once, ever. That is a better sign-in than the launcher's.

### Azure registration - the long pole, start it first

Online mode needs an approved Azure application. This is the only part of Minecraft support
with an external waiting period, and it blocks nothing else, so it should be started before
any code is written.

Settings for the app registration:

- **No client secret.** A public client; the manager must never hold one.
- **Tenant `consumers`**, not `common` and not `organizations`. Other tenants error out,
  and AAD work/school accounts cannot authenticate against Minecraft at all.
- **Scope `XboxLive.signin`.** Not the `common` scope.
- **Device code flow**, so no redirect URI and no temporary local HTTP server. This is both
  the simpler implementation and by far the more accessible sign-in.

⚠️ **You must fail a login before you can ask for approval.** Microsoft wants to see activity
on the app before whitelisting it, so the order is: register the app, attempt a sign-in,
receive the 403 from `api.minecraftservices.com`, *then* submit the form. Submitting first
does not work. This is the single most surprising step and the easiest one to waste a week on.

Approval form: <https://aka.ms/mce-reviewappid>. Until it is granted, every call to
`api.minecraftservices.com` returns 403. Allow up to 24 hours after approval to propagate.

#### ✅ RESOLVED 2026-09-12 - the app is registered and the whole auth chain works

    Application (client) ID   dfae7b53-ed9a-4573-acb6-02928d7224ac
    Display name              Kinetix Mod Manager
    Sign-in audience          AzureADandPersonalMicrosoftAccount
    Public client             true          (no secret; device-code flow)
    Tenant                    Default Directory, 2f2088dc-365d-41eb-8d36-a1035cb1af3f

Not a secret - a public client id ships inside the app by definition.

Created with one command, no portal:

    az ad app create --display-name "Kinetix Mod Manager" \
        --sign-in-audience AzureADandPersonalMicrosoftAccount \
        --is-fallback-public-client true

**The full chain was then probed end to end and every step passed except the last:**

    Microsoft device code   OK
    Xbox Live               OK
    XSTS                    OK
    api.minecraftservices.com/authentication/login_with_xbox
        -> HTTP 403  {"errorMessage": "Invalid app registration,
                       see https://aka.ms/AppRegInfo"}

That 403 is the unapproved-app response, and it is exactly what is wanted at this stage:
Microsoft has now seen the app attempt a sign-in, which is the precondition for review.
**Nothing is left to design - only the approval is outstanding.**

**Review request submitted 2026-09-12** via <https://aka.ms/mce-reviewappid>, with the client id,
the tenant id, <https://github.com/SeanTerry01/Kinetix-Mod-Manager> as the associated site, and a
justification covering the accessibility case plus the security shape (public client, device-code
flow, `XboxLive.signin` only, token stored locally, each user signs into their own account).
The only response was a generic "thank you for contacting Mojang Studios" - there is no ticket
number, no status page and no published timeline.

**So do not wait on an email - poll instead.** Re-run `tools\mcauth-probe.py` every few days:
a **403** means still pending, a **200** means approved and online mode can be built. That is
ground truth from Minecraft's own API. The probe needs an interactive device-code sign-in each
time (the 403 only occurs after the Microsoft/Xbox/XSTS steps), so it cannot be automated.
It prints statuses only and never emits a token.

#### ⚠️ Getting there: a personal Microsoft account has no directory, and you need one

Recorded because it cost an evening. **A personal Microsoft account cannot register an
app without first creating an Azure account.** There is no free path around this.

What was tried, in order:

1. `az login --use-device-code --allow-no-subscriptions` - **sign-in succeeded** (Microsoft
   showed the confirmation page) but the CLI stored nothing: `azureProfile.json` came back as
   just `{"installationId": ...}`, no subscriptions, no tenants, and no `msal_token_cache.json`
   was written at all. Every later command then said "Please run 'az login'". The login was
   fine; there was simply no tenant to work in.
2. `entra.microsoft.com` - refused outright: *"Selected user account does not exist in tenant
   'Microsoft Services' and cannot access the application ... The account needs to be added as
   an external user in the tenant first."*
3. `portal.azure.com/#create/Microsoft.AzureActiveDirectory` (create a tenant directly) -
   `{"errorCode":"401", "subscriptionId":"", "details":"No access"}`. The tenant-creation blade
   needs a subscription context.

**Why.** A personal Microsoft account signing in to Azure lands in a shared placeholder tenant
called **"Microsoft Services"**, which has no directory attached. Users, groups and app
registrations cannot be created there. This is the normal default for every personal account,
not a misconfiguration, and the portal does *not* auto-provision a directory on a bare sign-in
(it only does so as part of creating a subscription).

**So the only route is the Azure account signup**, which asks for a credit card - a temporary
authorisation of roughly $1, refunded. Worth being clear about what that does and does not buy:
the **app registration itself is free permanently** (a Microsoft Entra ID Free feature, no trial
to lapse, no later bill), and no Azure resource is ever deployed, so nothing can accrue charges.
The card is identity verification.

**Done 2026-09-12.** The signup created "Default Directory" and "Azure subscription 1", and
`az login --use-device-code --allow-no-subscriptions` then found the tenant immediately. ⚠️ The
subscription will be **disabled** after 30 days unless upgraded to pay-as-you-go - which is
fine and expected. Do not upgrade: the directory and the app registration live in the tenant,
not the subscription, and survive it being disabled. If online sign-in ever breaks for no
apparent reason, check this first.

One further unknown, flagged rather than asserted: one low-quality source suggested Minecraft
API approval may also want Xbox developer program (ID@Xbox) registration. Unconfirmed. If true
it is a third cost on the online-mode path, so establish it before committing.

The token exchange, once approved, is a five-step chain: Microsoft OAuth2 (device code) →
Xbox Live → XSTS → Minecraft services → entitlements check → profile (which is where the
uuid and username come from). Each step is a plain HTTPS POST; nothing exotic.

State shape: **one** account record in settings - username, uuid, mode, refresh token. Not
per-profile; mode should switch without touching the mod setup, and profile should switch
without re-authenticating. Sign-in status and current mode both belong in Check My Setup so
that F5 can never be a surprise.

---

## 7. Reference: the verified-good setup

Kept so a future session can tell a real regression from a moved target.

    Minecraft            26.2 (Java Edition, Microsoft Store package)
    Fabric loader        0.19.5
    Fabric API           0.160.0+26.2        (2 542 898 bytes)
    United Minecraft     1.1.0+mc26.2        (2 449 696 bytes)
    Java                 25+ (required by MC 26.2 and by both mods)

    Profile id           fabric-loader-0.19.5-26.2
    Profile name         fabric-loader-26.2
    Mods folder          %APPDATA%\.minecraft\mods
    Config folder        %APPDATA%\.minecraft\config
    Logs                 %APPDATA%\.minecraft\logs\latest.log

---

## 8. Open questions

1. **Do the two access mods conflict?** Strongly suspected - both hook narration
   on the same screens, so expect double speech. Not tested. Needs one ear test.
   Until then, treat them as mutually exclusive. Note that per-profile `gameDir`
   (section 4) makes this moot for anyone who keeps them in separate profiles.
2. **Third-party launchers.** Prism, Modrinth App and CurseForge keep mods in
   per-instance folders, so "the mods folder" becomes plural and the manager
   needs an instance picker. Support them, or ship default-launcher-only first?
3. **Keep Minecraft Access at all?** United Minecraft is newer and more actively
   released. Supporting both roughly doubles the suite work for a feature whose
   whole value is that the user does not have to choose.
4. **Pin to a known-good Minecraft version?** See the game-updates note above.
   Recommended: pin to the newest stable MC that Fabric *and* the chosen access
   mod both support, not the newest MC outright.
5. **Does an external Fabric API alongside Minecraft Access cause trouble?**
   Fabric's jar-in-jar resolution should prefer the higher version and ignore the
   nested copy, so it ought to be harmless. Not tested. Matters if the user
   installs both access mods' dependency sets, or switches between them.
6. ~~**Do profiles share saves?**~~ **Moot as of 2026-09-13.** The existing profiles
   feature already works for Minecraft and moves nothing on disk, so worlds are
   never affected. The question only returns if separate instances are built.
7. ~~**Is route 1 worth the Azure approval?**~~ **Decided 2026-09-12: yes - build both
   modes.** See section 6. The Azure registration is the only item with an external
   waiting period, so it goes first.
