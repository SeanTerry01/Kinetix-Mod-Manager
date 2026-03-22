using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace StardewAccessibleManager
{
    public class AppSettings
    {
        public string ModsPath { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public bool ShowSplashScreen { get; set; } = true;
        public bool RandomLogoStartup { get; set; } = true;
        public string SelectedLogoFile { get; set; } = "";
        public bool CheckForUpdatesAtStartup { get; set; } = true;
        public int SoundVolume { get; set; } = 80; // 0 to 100
        public int MaxBackupsPerMod { get; set; } = 5;
        public string CurrentTheme { get; set; } = "Default";
        
        // Key: Mod UniqueID, Value: Version string to ignore
        public Dictionary<string, string> IgnoredVersions { get; set; } = new Dictionary<string, string>();

        // Key: Mod UniqueID, Value: Category Name
        public Dictionary<string, string> ModCategories { get; set; } = new Dictionary<string, string>();

        // Action Name -> Keys (includes modifiers)
        public Dictionary<string, Keys> Shortcuts { get; set; } = new Dictionary<string, Keys>();

        private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                    settings.InitializeDefaults();
                    return settings;
                }
            }
            catch { }
            var s = new AppSettings();
            s.InitializeDefaults();
            return s;
        }

        public void InitializeDefaults()
        {
            var defaults = new Dictionary<string, Keys>
            {
                { "Manual", Keys.F1 },
                { "ContextHelp", Keys.F1 | Keys.Shift },
                { "LaunchGame", Keys.F5 },
                { "OpenLogFile", Keys.F4 },
                { "Settings", Keys.Control | Keys.P },
                { "Login", Keys.Control | Keys.L },
                { "InstallZip", Keys.Control | Keys.I },
                { "OpenModPage", Keys.Control | Keys.G },
                { "OpenDownloads", Keys.Control | Keys.D },
                { "OpenBackups", Keys.Control | Keys.B },
                { "ManualID", Keys.Control | Keys.K },
                { "ChangeCategory", Keys.Control | Keys.J },
                { "BatchCategory", Keys.Control | Keys.Shift | Keys.J },
                { "ShowDependencies", Keys.Control | Keys.Y },
                { "QuickFix", Keys.Control | Keys.Q },
                { "Search", Keys.Control | Keys.F },
                { "UpdateAll", Keys.Control | Keys.U },
                { "SaveProfile", Keys.Control | Keys.S },
                { "ReadDescription", Keys.Control | Keys.R },
                { "PruneBackups", Keys.Control | Keys.Shift | Keys.B },
                { "OpenErrorLog", Keys.Control | Keys.Shift | Keys.L },
                { "RefreshAll", Keys.None },
                { "RefreshInstalled", Keys.None }
            };

            foreach (var d in defaults)
            {
                if (!Shortcuts.ContainsKey(d.Key))
                    Shortcuts[d.Key] = d.Value;
            }
        }

        public void Save()
        {
            try
            {
                string json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                try { File.AppendAllText("mod_manager_log.txt", $"[{DateTime.Now:HH:mm:ss}] Settings Save Error: {ex.Message}\n"); } catch { }
            }
        }
    }
}