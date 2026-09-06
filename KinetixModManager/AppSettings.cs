using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace KinetixModManager;

/// <summary>
/// How the manager gives audible progress feedback during long downloads and installs.
/// <see cref="Tones"/> plays a rising synthesized pitch, <see cref="Speech"/> speaks the name once
/// then bare deciles ("10 percent", ...). <see cref="Off"/> is useful for users who already rely on
/// their screen reader's own progress-bar beeps (e.g. NVDA's "Progress bar output").
/// </summary>
public enum ProgressFeedback
{
	Off,
	Tones,
	Speech,
	Both
}

/// <summary>
/// A high-contrast colour scheme for low-vision users, applied over the whole UI. <see cref="Off"/> keeps the
/// normal Windows colours; the others force a bold foreground/background pair the user finds easiest to read.
/// </summary>
public enum DisplayContrast
{
	Off,
	WhiteOnBlack,
	YellowOnBlack,
	BlackOnYellow
}

/// <summary>Global UI text-size multiplier for low-vision users. <see cref="Normal"/> leaves fonts unchanged.</summary>
public enum TextSize
{
	Normal,
	Large,
	ExtraLarge
}

/// <summary>
/// What to do when a "Mod Manager Download" turns out to be for a game other than the loaded one — the user was
/// browsing Nexus, found something for another game, and pressed the button.
///
/// The file is always downloaded either way; the link's key expires within minutes, so there is no such thing as
/// deferring the download itself. What this chooses is whether the manager also leaves the session the user was in.
/// </summary>
public enum CrossGameDownloadAction
{
	/// <summary>Ask, naming both games. The default: it says what game the mod turned out to be for.</summary>
	Ask,

	/// <summary>Load the mod's game and install straight away, ending whatever session was open.</summary>
	SwitchAndInstall,

	/// <summary>Keep the session and file the download under its own game, to install from Downloads History later.</summary>
	SaveForLater
}

/// <summary>A cached AI model entry (the id sent to the API plus the name shown in the dropdown). Serialized in
/// <see cref="AppSettings.AiModelCache"/> so a refreshed model list survives across sessions.</summary>
public class AiModelChoice
{
	public string Id { get; set; } = "";
	public string Display { get; set; } = "";
}

public class AppSettings
{
	public string ModsPath { get; set; } = "";

	/// <summary>
	/// The loaded session's install key — a bare game id for a game's only copy, <c>"SkyrimSE@Gog"</c> for a
	/// second one, or "None". Pass it to <see cref="GameProfiles.Find"/> and friends, which resolve either form;
	/// never compare it to a game id directly (see <see cref="GameProfiles.IsGame"/>).
	/// </summary>
	public string ActiveGame { get; set; } = "None";

	/// <summary>
	/// Every copy of every game the manager knows about. Absent from an older settings file, in which case
	/// <see cref="InitializeDefaults"/> synthesises one entry per game from the paths already recorded — so an
	/// upgrading user's session, mods and history all stay exactly where they were.
	/// </summary>
	public List<GameInstall> GameInstalls { get; set; } = new List<GameInstall>();

	/// <summary>The recorded copies of <paramref name="gameId"/>, primary (bare-keyed) copy first.</summary>
	public List<GameInstall> InstallsOf(string gameId) => GameInstallRules.Of(GameInstalls, gameId);

	/// <summary>The copy identified by <paramref name="installKey"/>, or <c>null</c> if none is recorded.</summary>
	public GameInstall? InstallFor(string? installKey) => GameInstallRules.WithKey(GameInstalls, installKey);

	/// <summary>
	/// True when <paramref name="gameId"/> has more than one copy recorded — the one condition under which a
	/// platform is worth saying out loud. With a single copy the games menu, the title bar and every report read
	/// exactly as they did before any of this existed.
	/// </summary>
	public bool HasMultipleCopies(string gameId) => GameInstallRules.HasMultipleCopies(GameInstalls, gameId);

	public Dictionary<string, string> GameModsPaths { get; set; } = new Dictionary<string, string>();

	public Dictionary<string, string> GamePaths { get; set; } = new Dictionary<string, string>();

	[JsonIgnore]
	public string CurrentModsPath
	{
		get
		{
			if (GameModsPaths.TryGetValue(ActiveGame, out string? path) && !string.IsNullOrEmpty(path))
				return path;
			return ModsPath;
		}
		set
		{
			GameModsPaths[ActiveGame] = value;
			if (GameProfiles.IsGame(ActiveGame, GameProfiles.StardewValley))
				ModsPath = value;
		}
	}

	/// <summary>
	/// Where <paramref name="game"/> is installed, or <c>""</c> if it has no folder recorded.
	///
	/// Needed wherever a game other than the active one is being worked on, and by anything that has to know
	/// <em>which copy</em> of a game it is dealing with — a GOG install keeps its settings and load order in a
	/// different place from the Steam one, so the folder is what settles that.
	/// </summary>
	public string GamePathOf(string game) =>
		GamePaths.TryGetValue(game, out string? path) ? path : "";

	[JsonIgnore]
	public string CurrentGamePath
	{
		get
		{
			if (GamePaths.TryGetValue(ActiveGame, out string? path))
				return path;
			return "";
		}
		set
		{
			GamePaths[ActiveGame] = value;
		}
	}

	/// <summary>
	/// Runtime-only plain-text API key. Never serialized directly — stored encrypted via ApiKeyEncrypted.
	/// </summary>
	[JsonIgnore]
	public string ApiKey { get; set; } = "";

	/// <summary>
	/// Serialized field: DPAPI-encrypted, Base64-encoded API key. Use ApiKey for all runtime access.
	/// </summary>
	public string ApiKeyEncrypted { get; set; } = "";

	// -------------------------------------------------------------------------
	// AI-assisted features (opt-in; user supplies their own provider API key)
	// -------------------------------------------------------------------------

	/// <summary>Master switch for AI-assisted features (currently the AI log diagnosis).</summary>
	public bool AiEnabled { get; set; }

	/// <summary>Selected AI provider id (e.g. "Anthropic"). See <see cref="AiService.Providers"/>.</summary>
	public string AiProvider { get; set; } = "Anthropic";

	/// <summary>Selected model id for the active provider (e.g. "claude-haiku-4-5").</summary>
	public string AiModel { get; set; } = "";

	/// <summary>Base URL for the OpenAI-compatible custom provider (e.g. "https://openrouter.ai/api/v1"). Unused by other providers.</summary>
	public string AiCustomBaseUrl { get; set; } = "";

	/// <summary>
	/// Runtime-only per-provider plain-text API keys, keyed by provider id. Never serialized directly —
	/// stored encrypted per provider via <see cref="AiApiKeysEncrypted"/>.
	/// </summary>
	[JsonIgnore]
	public Dictionary<string, string> AiApiKeys { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>Serialized field: per-provider DPAPI-encrypted, Base64 API keys. Use <see cref="AiApiKeys"/> at runtime.</summary>
	public Dictionary<string, string> AiApiKeysEncrypted { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// The live model catalog last fetched via the AI Settings tab's "Refresh model list", per provider id. Kept so
	/// the model dropdown shows the real, current models (and can re-select a saved model that isn't in the small
	/// curated fallback list) without re-fetching every time Settings opens. Refreshing again replaces the entry and
	/// reports what changed. Empty/absent for a provider means "no refresh yet — use the curated fallback".
	/// </summary>
	public Dictionary<string, List<AiModelChoice>> AiModelCache { get; set; } = new Dictionary<string, List<AiModelChoice>>(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// False until the manager has shown its one-time first-launch setup (the Settings dialog, so a brand-new
	/// user can enter their game folders and Nexus key before anything else). Set true after the first run so it
	/// never nags again. Existing users who upgrade are marked done silently without the popup.
	/// </summary>
	public bool HasCompletedFirstRun { get; set; } = false;

	/// <summary>
	/// The games whose keybind reader the manager has already offered to install, whatever the user answered.
	///
	/// The offer is made once and then never again — a "no" is a decision, not something to be asked about on
	/// every session, and deleting the reader afterwards is a decision too. Recorded per game id so a second
	/// game with a reader of its own asks in its own right.
	/// </summary>
	public List<string> KeybindReaderOffered { get; set; } = new List<string>();

	public bool ShowSplashScreen { get; set; } = true;

	public bool RandomLogoStartup { get; set; } = true;

	public string SelectedLogoFile { get; set; } = "";

	public bool CheckForUpdatesAtStartup { get; set; } = true;

	/// <summary>Check for a new version of the manager itself when the program starts.</summary>
	public bool CheckForManagerUpdatesAtStartup { get; set; } = true;

	/// <summary>
	/// What happens when a browser download turns out to be for a game other than the loaded one.
	/// <see cref="CrossGameDownloadAction.Ask"/> by default — the manager should not end a session the user is in
	/// the middle of without saying so.
	/// </summary>
	[JsonConverter(typeof(StringEnumConverter))]
	public CrossGameDownloadAction CrossGameDownloads { get; set; } = CrossGameDownloadAction.Ask;

	/// <summary>Speak the welcome / shortcut-hint message at startup (and wait for it before loading).</summary>
	public bool SpeakStartupMessage { get; set; } = true;

	/// <summary>Speak the "shutting down" message (and wait for it) when the manager closes.</summary>
	public bool SpeakShutdownMessage { get; set; } = true;

	/// <summary>
	/// Master switch for the manager's UI sound effects (named events like connect/enable/error and the
	/// startup logo sound). When false, those sounds are silenced. Does not affect spoken screen-reader
	/// output or the download/install progress feedback, which has its own setting (<see cref="ProgressFeedback"/>).
	/// </summary>
	public bool EnableUiSounds { get; set; } = true;

	public int SoundVolume { get; set; } = 80;

	/// <summary>
	/// Audible progress feedback mode for downloads and installs. Defaults to <see cref="ProgressFeedback.Both"/>
	/// (rising tones plus spoken deciles). Serialized as a readable string. See <see cref="ProgressAnnouncer"/>.
	/// </summary>
	[JsonConverter(typeof(StringEnumConverter))]
	public ProgressFeedback ProgressFeedback { get; set; } = ProgressFeedback.Both;

	public int MaxBackupsPerMod { get; set; } = 5;

	/// <summary>Language to restrict Find New Mods (Discovery) searches to. Empty string means "Any language".</summary>
	public string DiscoveryLanguage { get; set; } = "English";

	/// <summary>
	/// How many search results to fetch per page in the Discovery tab, both for the initial search and
	/// each "Load more". This is the persisted default; the Discovery tab's own selector starts from this
	/// value but its in-session changes are not saved back here — only the Settings dialog persists it.
	/// </summary>
	public int DiscoverySearchPageSize { get; set; } = 20;

	/// <summary>When true, mod text searches are recorded to a per-game history the user can browse and re-run.
	/// Off by default; toggled in Settings. See <see cref="SearchHistoryStore"/>.</summary>
	public bool SaveSearchHistory { get; set; } = false;

	/// <summary>
	/// Whether the curation tools are switched on: marking a mod as one worth suggesting to other players
	/// (<c>MarkSuggestion</c>), and the list of everything marked so far (<c>SuggestedList</c>). Toggled with
	/// <c>CurationMode</c>, which works whether or not curation is currently on. See <see cref="SuggestedModStore"/>.
	///
	/// Off by default rather than hidden. Building a suggestion list is a real thing a user may want to do — and
	/// has to be, for one list to be shareable with another player — but it is not what most people open the
	/// manager for, so nothing about it is in the way until it is asked for.
	/// </summary>
	public bool CuratorMode { get; set; } = false;

	/// <summary>
	/// Mods the user has told the manager arrive with another mod, per game:
	/// <c>game -> child UniqueID -> parent UniqueID</c>.
	///
	/// The manager works out most of these itself, from mods sharing a folder or a download (see
	/// <see cref="UpdateCoverage"/>). What it cannot see is a mod that came from the same mod page as another —
	/// an optional file downloaded separately, say — and was unpacked somewhere unrelated: nothing on disk
	/// connects the two. Rather than report such a mod as un-checkable forever, the user can point it at the mod
	/// it belongs to, once, and it is then treated exactly like the mods that arrived in one archive.
	/// </summary>
	public Dictionary<string, Dictionary<string, string>> ModBundledWith { get; set; } = new();

	/// <summary>
	/// Which release of each download is installed, per game: <c>game -> "Nexus:23737" -> "1.0.2"</c>.
	///
	/// A mod's manifest version is the author's number for that mod, which is not the same thing as the version
	/// of the download it came in — authors routinely ship a "1.0.2" release whose manifests still say "1.0.0",
	/// and one download often installs several mods with versions of their own. Comparing a manifest version
	/// against the mod page's version therefore reports an update that installing can never satisfy: the files
	/// arrive, the manifest still says the old number, and the mod is offered again forever (with two mods from
	/// one download, alternately). Recording what was actually installed makes the comparison honest and needs
	/// no rewriting of files the mod author shipped. See <c>Form1.CheckForUpdates</c>.
	/// </summary>
	public Dictionary<string, Dictionary<string, string>> InstalledDownloadVersions { get; set; } = new();

	/// <summary>
	/// Skyrim SE / Fallout 4 only. When true (the default) the manager guards the game's <c>plugins.txt</c>:
	/// it is marked read-only whenever the manager writes it, so the game cannot rewrite the active plugin
	/// list behind the user's back. Both games do exactly that when a new game is started — they deactivate
	/// Creations and reshuffle the load order — which is what this setting exists to prevent. The manager
	/// clears the flag for its own writes, and clears it permanently when the setting is turned off.
	/// </summary>
	public bool ProtectPluginOrder { get; set; } = true;

	/// <summary>
	/// UI language for the whole program, as a two-letter code (e.g. "es"). Empty string means
	/// "follow the Windows display language". English is always the fallback. See <see cref="Loc"/>.
	/// </summary>
	public string Language { get; set; } = "";

	/// <summary>High-contrast colour scheme applied across the UI for low-vision users. Off = normal Windows colours.</summary>
	[JsonConverter(typeof(StringEnumConverter))]
	public DisplayContrast DisplayContrast { get; set; } = DisplayContrast.Off;

	/// <summary>Global UI text-size multiplier for low-vision users. Normal leaves fonts at their designed size.</summary>
	[JsonConverter(typeof(StringEnumConverter))]
	public TextSize TextSize { get; set; } = TextSize.Normal;

	public string CurrentTheme { get; set; } = "Default";

	/// <summary>
	/// When <c>false</c> (default) the sound theme strictly follows the loaded game via
	/// <see cref="ThemeForGame"/>. When <c>true</c> the user's manually chosen
	/// <see cref="CurrentTheme"/> is honoured and persists across game switches and restarts.
	/// </summary>
	public bool AllowManualTheme { get; set; } = false;

	/// <summary>
	/// Maps an active-game identifier to its sound-theme folder name (under <c>sounds/</c>).
	/// Games without a dedicated theme, or no game loaded ("None"), fall back to "Default".
	/// The SoundEngine also falls back to the Default theme per-sound, so a game whose theme
	/// folder does not exist yet (e.g. Skyrim/Fallout 4 before their themes are authored) will
	/// simply play the Default sounds.
	/// </summary>
	public static string ThemeForGame(string game) => GameProfiles.Find(game)?.SoundTheme ?? "Default";

	public Dictionary<string, string> IgnoredVersions { get; set; } = new Dictionary<string, string>();

	/// <summary>
	/// Requirement-check warnings the user has chosen to hide, per game (keyed by active-game id). Each entry is a
	/// stable "ignore key" identifying one warning (e.g. a mod's Nexus requirement that's actually optional or
	/// satisfied by an alternative the manager can't detect). See the requirements report and <c>ResetIgnoredRequirements</c>.
	/// </summary>
	public Dictionary<string, List<string>> IgnoredRequirements { get; set; } = new Dictionary<string, List<string>>();

	public Dictionary<string, string> ModCategories { get; set; } = new Dictionary<string, string>();

	/// <summary>
	/// Personal free-text notes the user has attached to mods, keyed by mod UniqueID. Spoken when the mod is
	/// selected in the installed list (e.g. "keep disabled until year 2"). Empty/removed entries mean no note.
	/// </summary>
	public Dictionary<string, string> ModNotes { get; set; } = new Dictionary<string, string>();

	/// <summary>
	/// Per-game mod priority order for Skyrim SE / Fallout 4, deciding which mod's loose files win when
	/// two mods provide the same file. Keyed by game id ("SkyrimSE", "Fallout4"); the value lists mod
	/// folder names highest priority first (index 0 wins conflicts). Stardew Valley does not use this.
	/// </summary>
	public Dictionary<string, List<string>> ModPriority { get; set; } = new Dictionary<string, List<string>>();

	/// <summary>
	/// Per-game plugin load order for Skyrim SE / Fallout 4: the order of active <c>.esp/.esm/.esl</c>
	/// files written to plugins.txt. Keyed by game id; the value lists plugin file names in load order
	/// (kept masters-first). Base-game/DLC masters are implicit and never stored here.
	/// </summary>
	public Dictionary<string, List<string>> PluginOrder { get; set; } = new Dictionary<string, List<string>>();

	/// <summary>One persistent load-order rule: <see cref="Plugin"/> must load after <see cref="After"/>.</summary>
	public class LoadOrderRule
	{
		public string Plugin { get; set; } = "";
		public string After { get; set; } = "";
	}

	/// <summary>
	/// Persistent user load-order rules per Skyrim SE / Fallout 4 game ("always load X after Y"), applied by the
	/// plugin auto-sort on top of masters and LOOT rules. A rule that names a plugin not in the current load order is
	/// ignored. Empty for a game with no rules.
	/// </summary>
	public Dictionary<string, List<LoadOrderRule>> LoadOrderRules { get; set; } =
		new Dictionary<string, List<LoadOrderRule>>();

	/// <summary>
	/// Per-file conflict winner overrides for Skyrim SE / Fallout 4: for a game, maps a deployed file path (relative
	/// to the game root, e.g. <c>Data\textures\x.dds</c>) to the mod (priority-key / folder name) the user forced to
	/// win that path, regardless of mod priority. An override is honored only while that mod still provides the path;
	/// a stale entry is ignored by the deployment sync. Empty for a game with no overrides.
	/// </summary>
	public Dictionary<string, Dictionary<string, string>> FileWinnerOverrides { get; set; } =
		new Dictionary<string, Dictionary<string, string>>();

	/// <summary>
	/// Last game executable version the manager saw for each Bethesda game, as "major.minor.build" (e.g.
	/// "1.6.1170"). Keyed by game id. Used by the game-update guardian: when the exe's version differs from the
	/// value stored here on a later load, the game was updated (usually by Steam) since we last looked, so SKSE/F4SE
	/// and any DLL plugins likely need updating. An absent key means "not yet recorded" and never triggers a warning.
	/// </summary>
	public Dictionary<string, string> LastSeenGameVersion { get; set; } = new Dictionary<string, string>();

	public Dictionary<string, Keys> Shortcuts { get; set; } = new Dictionary<string, Keys>();

	public static string AppDataFolder
	{
		get
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudiVentureGames", "KinetixModManager");
			if (!Directory.Exists(text))
			{
				Directory.CreateDirectory(text);
			}
			return text;
		}
	}

	private static string SettingsPath => Path.Combine(AppDataFolder, "settings.json");

	/// <summary>
	/// Appends a timestamped line to the shared log in the app data folder. Used so settings
	/// failures are recorded rather than silently swallowed. Never throws.
	/// </summary>
	private static void Log(string msg) => DiagnosticLog.Write("Settings", msg);

	public static AppSettings Load()
	{
		try
		{
			string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
			if (File.Exists(text) && !File.Exists(SettingsPath))
			{
				try
				{
					File.Copy(text, SettingsPath, overwrite: true);
				}
				catch (Exception ex)
				{
					DiagnosticLog.WriteException("Settings", $"copying the settings file from {text}", ex);
				}
			}
			if (File.Exists(SettingsPath))
			{
				AppSettings? settings = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
				settings.InitializeDefaults();

				// Decrypt the stored API key into the runtime property.
				// If ApiKeyEncrypted is present, use it; otherwise fall back to the legacy
				// plain-text "ApiKey" field that older settings files may still contain.
				if (!string.IsNullOrEmpty(settings.ApiKeyEncrypted))
				{
					settings.ApiKey = DecryptApiKey(settings.ApiKeyEncrypted);
				}
				else
				{
					// Migration path: read the legacy plain-text field if it was serialized
					// by an older version, then encrypt and save going forward.
					string? legacyKey = null;
					try
					{
						var raw = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(SettingsPath));
						legacyKey = raw["ApiKey"]?.ToString();
					}
					catch (Exception ex)
					{
						DiagnosticLog.WriteException("Settings", "reading the stored API key", ex);
					}

					if (!string.IsNullOrEmpty(legacyKey))
					{
						settings.ApiKey = legacyKey;
						settings.Save(); // re-save immediately with encryption
					}
				}

				// Decrypt per-provider AI keys into the runtime dictionary (deserialization loses the
				// case-insensitive comparer, so rebuild it).
				settings.AiApiKeysEncrypted ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				settings.AiApiKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (var kv in settings.AiApiKeysEncrypted)
				{
					string dec = DecryptApiKey(kv.Value);
					if (!string.IsNullOrEmpty(dec)) settings.AiApiKeys[kv.Key] = dec;
				}

				// Deserialization loses the case-insensitive comparer on the model-cache dictionary; rebuild it so
				// provider-id lookups stay case-insensitive like the key dictionaries above.
				if (settings.AiModelCache != null && settings.AiModelCache.Comparer != StringComparer.OrdinalIgnoreCase)
					settings.AiModelCache = new Dictionary<string, List<AiModelChoice>>(settings.AiModelCache, StringComparer.OrdinalIgnoreCase);

				return settings;
			}
		}
		catch (Exception ex)
		{
			// A corrupt or unreadable settings file would otherwise reset all config silently.
			Log("Failed to load settings, falling back to defaults: " + ex.Message);
		}
		AppSettings appSettings = new AppSettings();
		appSettings.InitializeDefaults();
		return appSettings;
	}

	public void InitializeDefaults()
	{
		// Every supported game gets an entry in both path maps, so a game added in a later version appears for
		// users upgrading with an existing settings file rather than only for fresh installs.
		if (GameModsPaths == null) GameModsPaths = new Dictionary<string, string>();
		if (GamePaths == null) GamePaths = new Dictionary<string, string>();
		foreach (string gameId in GameProfiles.AllIds)
		{
			// Stardew's mods path predates the per-game map and is still mirrored in the legacy ModsPath field.
			if (!GameModsPaths.ContainsKey(gameId))
				GameModsPaths[gameId] = gameId == GameProfiles.StardewValley ? ModsPath : "";
			if (!GamePaths.ContainsKey(gameId)) GamePaths[gameId] = "";
		}

		// Give every game the manager already had a folder for a recorded copy, keyed by the bare game id — which
		// is the key its mods, deployment manifest, backups and history are already filed under. Nothing moves and
		// nothing is re-detected; the copy simply gains a name for what it always was. A settings file written
		// before install keys existed therefore upgrades in place, and detection adds any SECOND copy later.
		GameInstalls ??= new List<GameInstall>();
		foreach (string gameId in GameProfiles.AllIds)
		{
			if (GameInstalls.Any(i => i.Key == gameId)) continue;

			string folder = GamePathOf(gameId);
			if (string.IsNullOrEmpty(folder)) continue;

			GameInstalls.Add(new GameInstall
			{
				Key      = gameId,
				GameId   = gameId,
				Folder   = folder,
				// Left Unknown rather than guessed: the folder alone doesn't say which store it came from, and a
				// wrong platform would pick the wrong script-extender build. Detection fills this in when it runs.
				Platform = GamePlatform.Unknown,
				// Existing copies keep their %AppData% staging folder until the user chooses otherwise. Moving
				// someone's mods as a side effect of an update is not a thing to do quietly.
				ModsInGameFolder = false
			});
		}

		if (InstalledDownloadVersions == null) InstalledDownloadVersions = new Dictionary<string, Dictionary<string, string>>();
		if (ModBundledWith == null) ModBundledWith = new Dictionary<string, Dictionary<string, string>>();
		if (ModPriority == null) ModPriority = new Dictionary<string, List<string>>();
		if (PluginOrder == null) PluginOrder = new Dictionary<string, List<string>>();
		if (IgnoredRequirements == null) IgnoredRequirements = new Dictionary<string, List<string>>();
		if (AiModelCache == null) AiModelCache = new Dictionary<string, List<AiModelChoice>>(StringComparer.OrdinalIgnoreCase);
		if (LastSeenGameVersion == null) LastSeenGameVersion = new Dictionary<string, string>();
		if (LoadOrderRules == null) LoadOrderRules = new Dictionary<string, List<LoadOrderRule>>();
		if (FileWinnerOverrides == null) FileWinnerOverrides = new Dictionary<string, Dictionary<string, string>>();
		// Deserialization doesn't preserve the case-insensitive comparer on the inner path->winner maps; rebuild it
		// so a path lookup matches regardless of case (paths and folder names are compared case-insensitively).
		foreach (string key in new List<string>(FileWinnerOverrides.Keys))
			FileWinnerOverrides[key] = new Dictionary<string, string>(FileWinnerOverrides[key], StringComparer.OrdinalIgnoreCase);

		if (string.IsNullOrEmpty(ActiveGame)) ActiveGame = "None";
		Shortcuts ??= new Dictionary<string, Keys>();

		// The "Delete Old Backups" action's internal key was renamed from "PruneBackups" to "DeleteOldBackups" so
		// it reads correctly in the Shortcut Manager. Carry any saved binding across to the new key (done before the
		// defaults are filled in below so a custom binding survives), and bump the legacy defaults — Ctrl+B (which
		// clashed with Open Backups Folder) and the later Ctrl+Shift+B — to the new Ctrl+Shift+D default.
		if (Shortcuts.TryGetValue("PruneBackups", out Keys oldPruneKey))
		{
			Shortcuts.Remove("PruneBackups");
			if (oldPruneKey == (Keys.B | Keys.Control) || oldPruneKey == (Keys.B | Keys.Shift | Keys.Control))
				oldPruneKey = Keys.D | Keys.Shift | Keys.Control;
			if (!Shortcuts.ContainsKey("DeleteOldBackups"))
				Shortcuts["DeleteOldBackups"] = oldPruneKey;
		}

		foreach (KeyValuePair<string, Keys> item in new Dictionary<string, Keys>
		{
			{
				"Manual",
				Keys.F1
			},
			{
				"ChangeLog",
				Keys.F2
			},
			{
				"ModDocs",
				Keys.F3
			},
			{
				"ContextHelp",
				Keys.F1 | Keys.Shift
			},
			{
				"ControlsHelp",
				Keys.H | Keys.Control
			},
			{
				"LaunchGame",
				Keys.F5
			},
			{
				"OpenLogFile",
				Keys.F4
			},
			{
				"Settings",
				Keys.P | Keys.Control
			},
			{
				"Login",
				Keys.L | Keys.Control
			},
			{
				"InstallZip",
				Keys.I | Keys.Control
			},
			{
				"OpenModPage",
				Keys.G | Keys.Control
			},
			{
				"OpenDownloads",
				Keys.D | Keys.Control
			},
			{
				"OpenBackups",
				Keys.B | Keys.Control
			},
			{
				"ManualID",
				Keys.K | Keys.Control
			},
			{
				"ChangeCategory",
				Keys.J | Keys.Control
			},
			{
				"BatchCategory",
				Keys.J | Keys.Shift | Keys.Control
			},
			{
				"ShowDependencies",
				Keys.Y | Keys.Control
			},
			{
				"QuickFix",
				Keys.Q | Keys.Control
			},
			{
				"Search",
				Keys.F | Keys.Control
			},
			{
				"UpdateAll",
				Keys.U | Keys.Control
			},
			{
				// Acts on the selected Updates row, like Delete does. Not on a letter that spells "installed":
				// Ctrl+I and Ctrl+Shift+I are both taken, and Insert is out of the question - screen readers own
				// that key, so a binding on it would be pressed at them, not at us.
				"MarkVersionInstalled",
				Keys.Y | Keys.Shift | Keys.Control
			},
			{
				"SaveProfile",
				Keys.S | Keys.Control
			},
			{
				"ReadDescription",
				Keys.R | Keys.Control
			},
			{
				"OpenConfig",
				Keys.E | Keys.Control
			},
			{
				"OpenManifest",
				Keys.M | Keys.Control
			},
			{
				// The pair to OpenManifest: both open a file itself in the JSON editor, so they sit together on M.
				// Ctrl+E is the settings list, which is what a mod's config normally deserves; this is the way to
				// the file behind it for a setting the list cannot offer.
				"OpenConfigFile",
				Keys.M | Keys.Shift | Keys.Control
			},
			{
				"DeleteOldBackups",
				Keys.D | Keys.Shift | Keys.Control
			},
			{
				"OpenErrorLog",
				Keys.L | Keys.Shift | Keys.Control
			},
			{
				"RefreshLog",
				Keys.R | Keys.Shift | Keys.Control
			},
			{
				"RefreshAll",
				Keys.None
			},
			{
				"RefreshInstalled",
				Keys.None
			},
			{
				"CycleFocus",
				Keys.F6
			},
			{
				"AutoSort",
				Keys.F8
			},
			{
				"SearchHistory",
				Keys.H | Keys.Shift | Keys.Control
			},
			{
				"FileConflicts",
				Keys.F | Keys.Shift | Keys.Control
			},
			{
				"CheckRequirements",
				Keys.Q | Keys.Shift | Keys.Control
			},
			{
				"Endorse",
				Keys.E | Keys.Shift | Keys.Control
			},
			{
				"ApiCredits",
				Keys.A | Keys.Shift | Keys.Control
			},
			{
				"ExportCollection",
				Keys.X | Keys.Shift | Keys.Control
			},
			{
				"InstallCollection",
				Keys.N | Keys.Shift | Keys.Control
			},
			{
				"EditNote",
				Keys.O | Keys.Shift | Keys.Control
			},
			{
				"DiagnoseAi",
				Keys.F9
			},
			{
				"ViewChangelog",
				Keys.G | Keys.Shift | Keys.Control
			},
			{
				"ViewDescription",
				Keys.I | Keys.Shift | Keys.Control
			},
			{
				"CheckBrokenMods",
				Keys.B | Keys.Shift | Keys.Control
			},
			{
				"HealthCheck",
				Keys.K | Keys.Shift | Keys.Control
			},
			{
				"PluginSlots",
				Keys.U | Keys.Shift | Keys.Control
			},
			{
				"SaveManager",
				Keys.V | Keys.Shift | Keys.Control
			},
			{
				"DownloadsHistory",
				Keys.W | Keys.Shift | Keys.Control
			},
			{
				"TrackedMods",
				Keys.T | Keys.Shift | Keys.Control
			},
			// The curation keys sit together on F7, which is the only bare function key left: F1 to F6, F8 and F9
			// are taken above, F10 opens the menu bar for Windows, and F12 is spoken for by screen-reader add-ons
			// — a key the app never receives is not a free key. They are remappable like everything else here
			// precisely because that collision will happen to someone else on some other key.
			// Reading the suggested list is the one of these four that is not about curating, so it gets no key
			// by default — the Mods menu is enough for something opened occasionally, and this way it costs
			// nobody a combination they were using. It is in the table so it can be given one in the shortcut
			// manager, which an unlisted command could never be.
			{
				"SuggestedMods",
				Keys.None
			},
			{
				"CurationMode",
				Keys.F7 | Keys.Shift | Keys.Control
			},
			{
				"MarkSuggestion",
				Keys.F7
			},
			{
				"SuggestedList",
				Keys.F7 | Keys.Shift
			}
		})
		{
			if (!Shortcuts.ContainsKey(item.Key))
			{
				Shortcuts[item.Key] = item.Value;
			}
		}
	}

	public void Save()
	{
		try
		{
			// Encrypt the API key before serialization so it is never written as plain text.
			ApiKeyEncrypted = EncryptApiKey(ApiKey);
			// Encrypt each per-provider AI key the same way.
			AiApiKeysEncrypted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var kv in AiApiKeys)
				if (!string.IsNullOrEmpty(kv.Value))
					AiApiKeysEncrypted[kv.Key] = EncryptApiKey(kv.Value);
			string contents = JsonConvert.SerializeObject(this, Formatting.Indented);
			File.WriteAllText(SettingsPath, contents);
		}
		catch (Exception ex)
		{
			Log("Save error: " + ex.Message);
		}
	}

	// -------------------------------------------------------------------------
	// DPAPI helpers — CurrentUser scope so only this Windows account can read
	// -------------------------------------------------------------------------

	private static string EncryptApiKey(string plainText)
	{
		if (string.IsNullOrEmpty(plainText)) return "";
		try
		{
			byte[] data = Encoding.UTF8.GetBytes(plainText);
			byte[] encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
			return Convert.ToBase64String(encrypted);
		}
		catch
		{
			// If DPAPI is unavailable for any reason, fall back to plain text so the
			// app remains functional (e.g., in a sandbox without a user profile).
			return plainText;
		}
	}

	private static string DecryptApiKey(string encryptedText)
	{
		if (string.IsNullOrEmpty(encryptedText)) return "";
		try
		{
			byte[] data = Convert.FromBase64String(encryptedText);
			byte[] decrypted = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
			return Encoding.UTF8.GetString(decrypted);
		}
		catch
		{
			// Fallback: treat as plain text (handles migration from pre-encryption settings).
			return encryptedText;
		}
	}
}
