using System.IO.Pipes;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System;
using System.Windows.Forms;
using System.Reflection;

namespace StardewAccessibleManager
{
    internal static class Program
    {
        private const string AppGuid = "StardewAccessibleManager-Nexus-Handler";

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        static Program()
        {
            // 1. Set path for Native DLLs (like Tolk.dll) if the lib folder exists
            string libPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib");
            if (Directory.Exists(libPath))
            {
                SetDllDirectory(libPath);
            }

            // 2. Set path for Managed DLLs (like NAudio, Newtonsoft) ONLY if the lib folder exists
            // When publishing as a single file, these are already bundled inside the EXE.
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                if (!Directory.Exists(libPath)) return null;

                string assemblyName = new AssemblyName(args.Name).Name + ".dll";
                string fullPath = Path.Combine(libPath, assemblyName);
                if (File.Exists(fullPath))
                {
                    return Assembly.LoadFrom(fullPath);
                }
                return null;
            };
        }

        [STAThread]
        static void Main(string[] args)
        {
            using (Mutex mutex = new Mutex(false, "Global\\" + AppGuid))
            {
                if (!mutex.WaitOne(0, false))
                {
                    if (args.Length > 0 && args[0].StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using (var client = new NamedPipeClientStream(".", AppGuid, PipeDirection.Out))
                            {
                                client.Connect(1000);
                                using (var writer = new StreamWriter(client))
                                {
                                    writer.WriteLine(args[0]);
                                    writer.Flush();
                                }
                            }
                        }
                        catch { }
                    }
                    return;
                }

                ApplicationConfiguration.Initialize();

                var settings = AppSettings.Load();

                // 1. Show Splash Screen (if enabled)
                if (settings.ShowSplashScreen)
                {
                    using (var splash = new SplashScreen())
                    {
                        splash.ShowDialog();
                    }
                }

                // 2. Launch Main Application
                Application.Run(new Form1(args));
            }
        }
    }
}