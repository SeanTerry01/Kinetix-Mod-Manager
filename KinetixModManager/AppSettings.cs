using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace KinetixModManager;

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

	public bool ShowSplashScreen { get; set; } = true;

	public bool RandomLogoStartup { get; set; } = true;

	public string SelectedLogoFile { get; set; } = "";

	public bool CheckForUpdatesAtStartup { get; set; } = true;

	public int SoundVolume { get; set; } = 80;

	public int MaxBackupsPerMod { get; set; } = 5;

	public string CurrentTheme { get; set; } = "Default";

	public Dictionary<string, string> IgnoredVersions { get; set; } = new Dictionary<string, string>();

	public Dictionary<string, string> ModCategories { get; set; } = new Dictionary<string, string>();

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

		if (string.IsNullOrEmpty(ActiveGame)) ActiveGame = "None";
		foreach (KeyValuePair<string, Keys> item in new Dictionary<string, Keys>
		{
			{
				"Manual",
				Keys.F1
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
				"PruneBackups",
				Keys.B | Keys.Shift | Keys.Control
			},
			{
				"OpenErrorLog",
				Keys.L | Keys.Shift | Keys.Control
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
