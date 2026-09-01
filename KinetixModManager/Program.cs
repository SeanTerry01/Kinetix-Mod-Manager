using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KinetixModManager;

internal static class Program
{
	private const string AppGuid = "KinetixModManager-Nexus-Handler";

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern bool SetDllDirectory(string lpPathName);

	static Program()
	{
		string libPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
		if (Directory.Exists(libPath))
		{
			SetDllDirectory(libPath);
		}
		AppDomain.CurrentDomain.AssemblyResolve += delegate(object? sender, ResolveEventArgs args)
		{
			if (!Directory.Exists(libPath))
			{
				return (Assembly?)null;
			}
			string path = new AssemblyName(args.Name).Name + ".dll";
			string text = Path.Combine(libPath, path);
			return File.Exists(text) ? Assembly.LoadFrom(text) : null;
		};
	}

	/// <summary>The manager's own data folder, where the log lives. Needed before any window exists.</summary>
	private static string DataFolder => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
		"AudiVentureGames", "KinetixModManager");

	/// <summary>
	/// Records an unhandled exception and tells the user, naming the log so they know what to send.
	///
	/// It goes in the ordinary log, not a crash log of its own. There used to be a separate
	/// <c>crash_log.txt</c> that nothing in the app opened or mentioned, so the file a user was asked for was
	/// never the file their crash was in — two real crashes sat there unread for months. One file, and it is the
	/// one Control+Shift+L opens.
	///
	/// UI-thread exceptions (<paramref name="terminating"/> false) are recoverable; AppDomain-level ones are not.
	/// </summary>
	private static void HandleUnhandledException(Exception? ex, bool terminating)
	{
		if (ex == null) return;

		DiagnosticLog.WriteException("Unhandled",
			terminating ? "an error the app could not recover from" : "an error outside any handler", ex);

		string note = terminating
			? "The application must close."
			: "The error has been logged and the application will try to continue.";
		MessageBox.Show("An unexpected error occurred:" + Environment.NewLine + Environment.NewLine
			+ ex.Message + Environment.NewLine + Environment.NewLine + note + Environment.NewLine + Environment.NewLine
			+ "It has been written to the log. Press Control plus Shift plus L in the manager to open the log, "
			+ "or find it at:" + Environment.NewLine + DiagnosticLog.Path,
			"Unexpected Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
	}

	/// <summary>
	/// What to record about the machine and the build before anything has had a chance to go wrong.
	///
	/// Every line here answers a question that otherwise has to be asked of the user by hand, days later, and
	/// often cannot be answered at all: which build was this, was it the installer or the portable zip, was it
	/// running under Wine. A log that does not say which version produced it can be actively misleading, since
	/// the bug being described may already be fixed.
	/// </summary>
	private static IEnumerable<string> MachineHeader()
	{
		yield return "manager   : " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown")
			+ "  (" + AppDomain.CurrentDomain.BaseDirectory + ")";
		yield return "windows   : " + Environment.OSVersion.VersionString + "  " + RuntimeInformation.OSDescription;
		yield return "runtime   : " + RuntimeInformation.FrameworkDescription + "  " + RuntimeInformation.ProcessArchitecture;
	}

	private static void MigrateAppData()
	{
		try
		{
			string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			string oldDir = Path.Combine(appData, "AudiVentureGames", "StardewAccessibleManager");
			string newDir = Path.Combine(appData, "AudiVentureGames", "KinetixModManager");
			if (Directory.Exists(oldDir) && !Directory.Exists(newDir))
			{
				Directory.Move(oldDir, newDir);
			}
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Startup", "moving the old app-data folder to its new name", ex); }
	}

	[STAThread]
	private static void Main(string[] args)
	{
		MigrateAppData();

		using Mutex mutex = new Mutex(initiallyOwned: false, "Global\\KinetixModManager-Nexus-Handler");
		if (!mutex.WaitOne(0, exitContext: false))
		{
			if (args.Length == 0 || !args[0].StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			try
			{
				using NamedPipeClientStream namedPipeClientStream = new NamedPipeClientStream(".", "KinetixModManager-Nexus-Handler", PipeDirection.Out);
				namedPipeClientStream.Connect(1000);
				using StreamWriter streamWriter = new StreamWriter(namedPipeClientStream);
				streamWriter.WriteLine(args[0]);
				streamWriter.Flush();
				return;
			}
			catch
			{
				return;
			}
		}
		ApplicationConfiguration.Initialize();

		// The log comes first, and the safety nets immediately after it — before the settings file is read,
		// before the language is chosen, before any window exists. Everything below this line can fail, and a
		// startup that dies before its own logging is turned on is exactly the failure nobody can report: the
		// app never appears, and there is nothing anywhere saying why. Only what is known without reading
		// anything goes in the header here; the rest is added once it is known.
		DiagnosticLog.Start(Path.Combine(DataFolder, "mod_manager_log.txt"), MachineHeader());

		// Global safety net for unhandled exceptions. Event handlers must be `async void`, so an
		// exception that escapes one cannot be observed by a caller; without this it would crash the
		// app with a raw .NET dialog. Catching it on the UI thread lets us log it and keep running.
		Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
		Application.ThreadException += (s, e) => HandleUnhandledException(e.Exception, terminating: false);
		AppDomain.CurrentDomain.UnhandledException += (s, e) =>
			HandleUnhandledException(e.ExceptionObject as Exception, terminating: e.IsTerminating);

		// The third way a failure escapes, and the quietest of the three. Work started and deliberately not
		// waited on — an update check, an install kicked off from a keypress — has no caller to receive its
		// exception, so .NET holds it until the task is collected and then, by default, discards it. Nothing is
		// shown, nothing is written, and the operation simply never finishes: "I pressed update and nothing
		// happened" with no trace anywhere. It is observed here so it is at least recorded.
		//
		// Marking it observed is deliberate too: unobserved exceptions do not terminate the process on modern
		// .NET, and turning a logged failure into a crash would make things worse, not better.
		TaskScheduler.UnobservedTaskException += (s, e) =>
		{
			DiagnosticLog.WriteException("Background", "work that was started and not waited on", e.Exception);
			e.SetObserved();
		};

		// Load settings once up front so the chosen UI language is active before any window is built.
		// An empty Language follows the Windows display language; English is always the fallback.
		AppSettings startupSettings = AppSettings.Load();
		Loc.Init(startupSettings.Language);
		Thread.CurrentThread.CurrentUICulture = Loc.ActiveCulture;
		CultureInfo.DefaultThreadCurrentUICulture = Loc.ActiveCulture;

		DiagnosticLog.Note("language  : "
			+ (string.IsNullOrEmpty(startupSettings.Language) ? "(follows Windows)" : startupSettings.Language)
			+ "  culture " + CultureInfo.CurrentUICulture.Name);
		DiagnosticLog.Note("game      : " + startupSettings.ActiveGame);

		if (startupSettings.ShowSplashScreen)
		{
			using SplashScreen splashScreen = new SplashScreen();
			splashScreen.ShowDialog();
		}
		Application.Run(new Form1(args));
	}
}
