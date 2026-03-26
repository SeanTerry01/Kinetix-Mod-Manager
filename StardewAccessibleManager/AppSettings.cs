using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace StardewAccessibleManager;

public class AppSettings
{
	public string ModsPath { get; set; } = "";

	public string ApiKey { get; set; } = "";

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

	private static string SettingsPath
	{
		get
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudiVentureGames", "StardewAccessibleManager");
			if (!Directory.Exists(text))
			{
				Directory.CreateDirectory(text);
			}
			return Path.Combine(text, "settings.json");
		}
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
				AppSettings? obj2 = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
				obj2.InitializeDefaults();
				return obj2;
			}
		}
		catch
		{
		}
		AppSettings appSettings = new AppSettings();
		appSettings.InitializeDefaults();
		return appSettings;
	}

	public void InitializeDefaults()
	{
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
			string contents = JsonConvert.SerializeObject(this, Formatting.Indented);
			File.WriteAllText(SettingsPath, contents);
		}
		catch (Exception ex)
		{
			try
			{
				File.AppendAllText("mod_manager_log.txt", $"[{DateTime.Now:HH:mm:ss}] Settings Save Error: {ex.Message}\n");
			}
			catch
			{
			}
		}
	}
}
