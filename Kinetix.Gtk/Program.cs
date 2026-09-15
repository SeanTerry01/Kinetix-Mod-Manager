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

		// The catalogue lives beside the executable, as it does on Windows. Falling back to the WinForms
		// project's copy keeps the spike usable from a plain `dotnet run` in the source tree.
		Loc.Init("");

		// Before anything can store a key, not after. Secrets.Current defaults to a store that does not
		// actually protect anything, and only the WinForms Program.cs was replacing it — so the first time
		// this head grew a key field, that key would have gone into settings.json in clear text with nothing
		// to notice. Assigned here whether or not a key exists yet, because the ordering is the whole point.
		Secrets.Current = new LibSecretStore();

		KinetixHttp.UserAgent = "KinetixModManager/1.6.0 (github.com/SeanTerry01/Kinetix-Mod-Manager)";

		var app = Gtk.Application.New("com.audiventuregames.kinetix", Gio.ApplicationFlags.DefaultFlags);
		app.OnActivate += (sender, _) =>
		{
			var window = new MainWindow((Gtk.Application)sender);
			window.Present();
		};

		return app.RunWithSynchronizationContext(null);
	}
}
