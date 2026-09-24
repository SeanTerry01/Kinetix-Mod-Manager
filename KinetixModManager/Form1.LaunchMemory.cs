using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// The memory check before a game starts, for every game. The deciding is <see cref="LaunchMemoryCheck"/>, in the
/// core; this is asking Windows, naming the programs, and asking the player.
/// </summary>
public partial class Form1
{
	[StructLayout(LayoutKind.Sequential)]
	private struct MemoryStatusEx
	{
		public uint Length;
		public uint MemoryLoad;
		public ulong TotalPhys;
		public ulong AvailPhys;
		public ulong TotalPageFile;
		public ulong AvailPageFile;
		public ulong TotalVirtual;
		public ulong AvailVirtual;
		public ulong AvailExtendedVirtual;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

	/// <summary>
	/// How much memory Windows can still promise, RAM and page file together — <c>ullAvailPageFile</c>, despite the
	/// name — or <c>null</c> when it cannot be asked.
	/// </summary>
	private static long? AvailableCommitBytes()
	{
		try
		{
			var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
			return GlobalMemoryStatusEx(ref status) ? (long)status.AvailPageFile : null;
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Launch", "asking Windows how much memory is left", ex);
			return null;
		}
	}

	/// <summary>Every running program's committed memory, by process name. Programs that cannot be read are skipped.</summary>
	private static List<MemoryUser> RunningProgramsMemory()
	{
		var users = new List<MemoryUser>();
		int self = Environment.ProcessId;

		foreach (Process process in Process.GetProcesses())
		{
			using (process)
			{
				try
				{
					if (process.Id == self) continue;
					users.Add(new MemoryUser(process.ProcessName, process.PrivateMemorySize64));
				}
				catch (Exception ex)
				{
					// A process that ended mid-scan, or one we may not look at. Only reached when memory is already
					// short, which is rare, so a line each costs nothing worth saving.
					DiagnosticLog.WriteException("Launch", "reading a running program's memory", ex);
				}
			}
		}

		return users;
	}

	/// <summary>
	/// The name a person would recognise for a process — "Docker Desktop Backend" rather than "com.docker.backend" —
	/// from its file's own description, falling back to the process name when that cannot be read.
	/// </summary>
	private static string FriendlyProgramName(string processName)
	{
		try
		{
			foreach (Process process in Process.GetProcessesByName(processName))
			{
				using (process)
				{
					string? description = process.MainModule?.FileVersionInfo.FileDescription;
					if (!string.IsNullOrWhiteSpace(description) &&
						!string.Equals(description.Trim(), processName, StringComparison.OrdinalIgnoreCase))
						return $"{description.Trim()} ({processName})";
				}
			}
		}
		catch (Exception ex)
		{
			// Another user's or an elevated process: its plain name will do.
			DiagnosticLog.WriteException("Launch", $"reading {processName}'s description", ex);
		}

		return processName;
	}

	/// <summary>
	/// Checks there is memory enough to start <paramref name="game"/>, and when there is not, says how much is left,
	/// what the game needs, and which programs are holding the most — then asks whether to start anyway. True to go
	/// ahead. Never stands in the way when the answer cannot be had.
	/// </summary>
	private bool ConfirmEnoughMemoryToLaunch(string game, string gameName)
	{
		if (GameProfiles.Find(game) is not { } profile || AvailableCommitBytes() is not long available) return true;

		long needed = LaunchMemoryCheck.NeededBytesFor(profile, _settings.MinecraftMaxMemoryMb);
		if (available >= needed) return true;   // the usual case: nothing is scanned and nothing is said

		MemoryVerdict verdict = LaunchMemoryCheck.Evaluate(available, needed, RunningProgramsMemory());
		DiagnosticLog.Write("Launch", $"low memory before starting {gameName}: {available / 1048576} MB available, " +
									  $"{needed / 1048576} MB wanted; biggest: " +
									  string.Join(", ", verdict.Biggest.Select(b => $"{b.Name} {b.Bytes / 1048576} MB")));

		string biggest = string.Join("; ", verdict.Biggest.Select(b =>
			Loc.T("launch.lowMemoryProgram", FriendlyProgramName(b.Name), FormatBytes(b.Bytes))));

		string question = verdict.Biggest.Count > 0
			? Loc.T("launch.lowMemory", FormatBytes(available), gameName, FormatBytes(needed), biggest)
			: Loc.T("launch.lowMemoryNoNames", FormatBytes(available), gameName, FormatBytes(needed));

		_soundEngine.Play("error");
		bool go = SpeakBox(question, Loc.T("launch.lowMemoryTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
			== DialogResult.Yes;
		if (!go) Speak(Loc.T("launch.lowMemoryNotStarted", gameName));
		return go;
	}
}
