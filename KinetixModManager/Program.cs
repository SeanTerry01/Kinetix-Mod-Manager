using System;
using System.IO;
using System.IO.Pipes;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
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

	/// <summary>
	/// Logs an unhandled exception to a crash log and informs the user. UI-thread exceptions
	/// (<paramref name="terminating"/> is false) are recoverable; AppDomain-level ones are not.
	/// </summary>
	private static void HandleUnhandledException(Exception? ex, bool terminating)
	{
		if (ex == null) return;
		try
		{
			string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			string logPath = Path.Combine(appData, "AudiVentureGames", "KinetixModManager", "crash_log.txt");
			Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
			File.AppendAllText(logPath, $"[{DateTime.Now:u}] {ex}{Environment.NewLine}{Environment.NewLine}");
		}
		catch { /* never let logging throw from the crash handler */ }

		string note = terminating
			? "The application must close."
			: "The error has been logged and the application will try to continue.";
		MessageBox.Show("An unexpected error occurred:" + Environment.NewLine + Environment.NewLine
			+ ex.Message + Environment.NewLine + Environment.NewLine + note,
			"Unexpected Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
		catch { }
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

		// Load settings once up front so the chosen UI language is active before any window is built.
		// An empty Language follows the Windows display language; English is always the fallback.
		AppSettings startupSettings = AppSettings.Load();
		Loc.Init(startupSettings.Language);
		Thread.CurrentThread.CurrentUICulture = Loc.ActiveCulture;
		CultureInfo.DefaultThreadCurrentUICulture = Loc.ActiveCulture;

		// Global safety net for unhandled exceptions. Event handlers must be `async void`, so an
		// exception that escapes one cannot be observed by a caller; without this it would crash the
		// app with a raw .NET dialog. Catching it on the UI thread lets us log it and keep running.
		Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
		Application.ThreadException += (s, e) => HandleUnhandledException(e.Exception, terminating: false);
		AppDomain.CurrentDomain.UnhandledException += (s, e) =>
			HandleUnhandledException(e.ExceptionObject as Exception, terminating: e.IsTerminating);

		if (startupSettings.ShowSplashScreen)
		{
			using SplashScreen splashScreen = new SplashScreen();
			splashScreen.ShowDialog();
		}
		Application.Run(new Form1(args));
	}
}
