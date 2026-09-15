using System;
using System.IO;
using KinetixModManager;

namespace KinetixModManager.GtkHead;

/// <summary>
/// Starts the GTK front end.
///
/// The order matters and mirrors Program.cs in the WinForms app: the log first, so a startup that dies has
/// somewhere to say so; then the language, so the first thing spoken is already translated; then the window.
/// </summary>
public static class Program
{
	public static int Main(string[] args)
	{
		string dataFolder = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"AudiVentureGames", "KinetixModManager");
		Directory.CreateDirectory(dataFolder);

		DiagnosticLog.Start(Path.Combine(dataFolder, "gtk_log.txt"), new[]
		{
			"front end : GTK4",
			"runtime   : " + Environment.Version,
			"os        : " + Environment.OSVersion
		});

		// Before the settings are read, not after, because reading them decrypts the stored keys. Secrets.Current
		// defaults to a store that protects nothing, and only the WinForms startup used to replace it — so the
		// first time this head grew somewhere to type a key, that key would have gone into settings.json in clear
		// text with nothing anywhere to notice.
		Secrets.Current = new LibSecretStore();

		// Settings, at last. They were unreachable from here until AppSettings moved to the core: this head
		// recomputed everything from the game locator on every run, so nothing it learned — which game you were
		// on, where your mods live, whether you wanted sounds — survived closing the window.
		AppSettings settings = AppSettings.Load();

		// After the settings, so the first thing spoken is already in the user's own language rather than in
		// English until they next restart.
		Loc.Init(settings.Language);

		KinetixHttp.UserAgent = "KinetixModManager/1.6.0 (github.com/SeanTerry01/Kinetix-Mod-Manager)";

		var app = Gtk.Application.New("com.audiventuregames.kinetix", Gio.ApplicationFlags.DefaultFlags);
		app.OnActivate += (sender, _) =>
		{
			var window = new MainWindow((Gtk.Application)sender, settings);
			window.Present();
		};

		return app.RunWithSynchronizationContext(null);
	}
}
