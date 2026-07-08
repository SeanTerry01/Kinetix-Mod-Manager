using System;
using System.IO;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// Free-up-space cleanup (View menu). Clears the manager's own leftover scratch — the app-update extraction folder
/// and stale extractor/installer staging directories it may have left in the system temp folder after a crash —
/// and reports how much was reclaimed. It only touches directories the manager itself created (matched by its own
/// unique name prefixes) and only ones older than an hour, so an install running right now is never disturbed.
/// Downloaded mod archives are left alone (remove those per-item from "Reinstall a Downloaded Mod").
/// </summary>
public partial class Form1
{
	/// <summary>Temp-dir name prefixes the manager creates; only these are ever removed by the cleanup.</summary>
	private static readonly string[] AppTempPrefixes = { "SMAPI_", "Extender_", "Preloader_" };

	private void FreeUpSpace()
	{
		long freed = 0;

		// 1. The app-update extraction folder in the active game's downloads folder.
		if (_settings.ActiveGame != "None")
			freed += TryDeleteDir(Path.Combine(downloadsPath, "KMM_Update_Extracted"));

		// 2. Stale extractor/installer staging dirs left in the system temp folder (older than an hour so a running
		// install is never touched).
		try
		{
			string temp = Path.GetTempPath();
			DateTime cutoff = DateTime.Now.AddHours(-1);
			foreach (string prefix in AppTempPrefixes)
				foreach (string dir in Directory.GetDirectories(temp, prefix + "*"))
				{
					try { if (Directory.GetLastWriteTime(dir) > cutoff) continue; } catch { continue; }
					freed += TryDeleteDir(dir);
				}
		}
		catch (Exception ex) { LogError("Cleanup", $"Scanning temp folder failed: {ex.Message}"); }

		_soundEngine.Play(freed > 0 ? "load_complete" : "disable");
		Speak(freed > 0 ? Loc.T("cleanup.freed", FormatBytes(freed)) : Loc.T("cleanup.nothing"));
	}

	/// <summary>Deletes a directory if it exists and returns the bytes it occupied (0 on failure or absence).</summary>
	private long TryDeleteDir(string dir)
	{
		try
		{
			if (!Directory.Exists(dir)) return 0;
			long size = DirectorySize(dir);
			Directory.Delete(dir, recursive: true);
			return size;
		}
		catch (Exception ex) { LogError("Cleanup", $"Deleting {dir} failed: {ex.Message}"); return 0; }
	}

	/// <summary>Best-effort recursive byte size of a directory; unreadable entries are skipped.</summary>
	private static long DirectorySize(string dir)
	{
		long total = 0;
		try
		{
			foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
			{
				try { total += new FileInfo(f).Length; } catch { }
			}
		}
		catch { }
		return total;
	}
}
