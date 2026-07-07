using System;
using System.Collections.Generic;
using System.IO;
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

	public string ActiveGame { get; set; } = "None";

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
			if (ActiveGame == "StardewValley")
				ModsPath = value;
		}
	}

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

	public bool ShowSplashScreen { get; set; } = true;

	public bool RandomLogoStartup { get; set; } = true;

	public string SelectedLogoFile { get; set; } = "";

	public bool CheckForUpdatesAtStartup { get; set; } = true;

	/// <summary>Check for a new version of the manager itself when the program starts.</summary>
	public bool CheckForManagerUpdatesAtStartup { get; set; } = true;

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
	public static string ThemeForGame(string game) => game switch
	{
		"StardewValley" => "Stardew Valley",
		"SkyrimSE"      => "Skyrim",
		"Fallout4"      => "Fallout 4",
		_               => "Default"
	};

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
	private static void Log(string msg)
	{
		try
		{
			File.AppendAllText(Path.Combine(AppDataFolder, "mod_manager_log.txt"),
				$"[{DateTime.Now:HH:mm:ss}] Settings: {msg}\n");
		}
		catch { /* logging must never throw */ }
	}

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
				catch
				{
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
					catch { }

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
		if (GameModsPaths == null) GameModsPaths = new Dictionary<string, string>();
		if (!GameModsPaths.ContainsKey("StardewValley")) GameModsPaths["StardewValley"] = ModsPath;
		if (!GameModsPaths.ContainsKey("SkyrimSE")) GameModsPaths["SkyrimSE"] = "";
		if (!GameModsPaths.ContainsKey("Fallout4")) GameModsPaths["Fallout4"] = "";

		if (GamePaths == null) GamePaths = new Dictionary<string, string>();
		if (!GamePaths.ContainsKey("StardewValley")) GamePaths["StardewValley"] = "";
		if (!GamePaths.ContainsKey("SkyrimSE")) GamePaths["SkyrimSE"] = "";
		if (!GamePaths.ContainsKey("Fallout4")) GamePaths["Fallout4"] = "";

		if (ModPriority == null) ModPriority = new Dictionary<string, List<string>>();
		if (PluginOrder == null) PluginOrder = new Dictionary<string, List<string>>();
		if (IgnoredRequirements == null) IgnoredRequirements = new Dictionary<string, List<string>>();
		if (AiModelCache == null) AiModelCache = new Dictionary<string, List<AiModelChoice>>(StringComparer.OrdinalIgnoreCase);
		if (LastSeenGameVersion == null) LastSeenGameVersion = new Dictionary<string, string>();

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
