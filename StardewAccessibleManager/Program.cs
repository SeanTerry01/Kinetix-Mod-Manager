using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace StardewAccessibleManager;

internal static class Program
{
	private const string AppGuid = "StardewAccessibleManager-Nexus-Handler";

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

	[STAThread]
	private static void Main(string[] args)
	{
		using Mutex mutex = new Mutex(initiallyOwned: false, "Global\\StardewAccessibleManager-Nexus-Handler");
		if (!mutex.WaitOne(0, exitContext: false))
		{
			if (args.Length == 0 || !args[0].StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			try
			{
				using NamedPipeClientStream namedPipeClientStream = new NamedPipeClientStream(".", "StardewAccessibleManager-Nexus-Handler", PipeDirection.Out);
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
		if (AppSettings.Load().ShowSplashScreen)
		{
			using SplashScreen splashScreen = new SplashScreen();
			splashScreen.ShowDialog();
		}
		Application.Run(new Form1(args));
	}
}
