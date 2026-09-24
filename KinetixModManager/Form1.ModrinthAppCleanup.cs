using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KinetixModManager;

/// <summary>
/// Clearing away the Modrinth App's files once the player has moved to the manager — including starting Windows' own
/// uninstaller for it. What is safe to remove is decided in <see cref="ModrinthAppCleanup"/>; the safeguards it
/// describes are enforced here, in order, before anything is asked.
/// </summary>
public partial class Form1
{
	/// <summary>
	/// The Modrinth App's entry in Windows' list of installed programs — whichever of the per-user and all-users lists
	/// holds it — as its uninstaller's path, or <c>null</c> when it is not installed.
	/// </summary>
	private static string? ModrinthAppUninstaller()
	{
		(RegistryKey Hive, string Path)[] lists =
		{
			(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
			(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
			(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
		};

		foreach ((RegistryKey hive, string path) in lists)
		{
			try
			{
				using RegistryKey? list = hive.OpenSubKey(path);
				if (list == null) continue;

				foreach (string name in list.GetSubKeyNames())
				{
					using RegistryKey? entry = list.OpenSubKey(name);
					if (!string.Equals(entry?.GetValue("DisplayName") as string, ModrinthAppCleanup.InstalledName,
							StringComparison.OrdinalIgnoreCase))
						continue;

					// "C:\Program Files\Modrinth App\uninstall.exe" — quoted, and sometimes followed by arguments.
					string command = (entry?.GetValue("UninstallString") as string ?? "").Trim();
					if (command.StartsWith('"'))
					{
						int close = command.IndexOf('"', 1);
						return close > 1 ? command.Substring(1, close - 1) : command.Trim('"');
					}
					return command;
				}
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Minecraft", $"reading {path}", ex); }
		}

		return null;
	}

	private static bool ModrinthAppRunning()
	{
		try { return Process.GetProcessesByName(ModrinthAppCleanup.ProcessName).Length > 0; }
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", "looking for the Modrinth App running", ex);
			return false;
		}
	}

	/// <summary>
	/// Removes the Modrinth App's leftover files, once it is uninstalled — offering to start its uninstaller when it
	/// is not, and naming every pack not yet brought across before asking anything.
	/// </summary>
	/// <param name="offeredAfterImport">True when offered straight after an import, where silence is right if there is nothing to do.</param>
	private async Task RemoveModrinthAppLeftoversAsync(bool offeredAfterImport = false)
	{
		string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

		IReadOnlyList<string> folders = ModrinthAppCleanup.DataFolders(appData, localAppData);
		if (folders.Count == 0)
		{
			if (!offeredAfterImport) Speak(Loc.T("mc.cleanup.nothing"));
			return;
		}

		if (ModrinthAppRunning())
		{
			SpeakBox(Loc.T("mc.cleanup.running"), Loc.T("mc.cleanup.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		// Never the files of a program that is still installed: it would be left broken. Windows' own uninstaller
		// goes first, started from here when the player wants it.
		if (ModrinthAppUninstaller() is { } uninstaller)
		{
			if (!await UninstallModrinthAppAsync(uninstaller)) return;

			folders = ModrinthAppCleanup.DataFolders(appData, localAppData);
			if (folders.Count == 0)
			{
				Speak(Loc.T("mc.cleanup.uninstallerTookAll"));
				return;
			}
		}

		IReadOnlyList<UnimportedModrinthPack> left = (await Task.Run(() => ModrinthAppCleanup.NotImported(
				ModrinthAppImport.FindAll(appData, Path.GetTempPath()),
				MinecraftModpacks.FindInstalled(MinecraftModpacks.PacksFolder))))
			.Where(p => p.HasContent)
			.ToList();

		long size = await Task.Run(() => ModrinthAppCleanup.ApproximateSize(folders));

		if (left.Count > 0)
		{
			// Named, with their worlds, before the player is asked anything — and importing comes first in the list.
			string importFirst = Loc.T("mc.cleanup.importFirst");
			string removeAnyway = Loc.T("mc.cleanup.removeAnyway", FormatBytes(size));
			string? picked = ShowChoiceList(Loc.T("mc.cleanup.title"), Loc.T("mc.cleanup.listName"),
				new[] { importFirst, removeAnyway }, importFirst,
				Loc.T("mc.cleanup.notImportedHint", left.Count, string.Join("; ", left.Select(DescribeUnimported))));

			if (picked is null) { Speak(Loc.T("common.changesCancelled")); return; }
			if (picked == importFirst)
			{
				await ImportFromModrinthAppAsync();
				return;
			}
		}
		else if (SpeakBox(Loc.T("mc.cleanup.confirm", FormatBytes(size), folders.Count, string.Join(", ", folders)),
					 Loc.T("mc.cleanup.title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			Speak(Loc.T("common.changesCancelled"));
			return;
		}

		var failed = new List<string>();
		bool stopped = false;
		SetStatus(Loc.T("mc.cleanup.removing"));
		foreach (string folder in folders)
		{
			try
			{
				await Task.Run(() => Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(folder,
					Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
					Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin));
			}
			catch (OperationCanceledException)
			{
				// Windows asked — most likely "too big for the Recycle Bin, delete permanently?" — and the player said
				// no. What has not gone yet stays, and nothing more is attempted.
				stopped = true;
				break;
			}
			catch (Exception ex)
			{
				LogFailure("Minecraft", $"Could not remove {folder}", ex);
				failed.Add(folder);
			}
		}
		ResetStatus();

		if (stopped) Speak(Loc.T("mc.cleanup.stopped"));
		else if (failed.Count > 0)
			SpeakBox(Loc.T("mc.cleanup.someFailed", string.Join(", ", failed)), Loc.T("mc.cleanup.title"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
		else Speak(Loc.T("mc.cleanup.done"));
	}

	private static string DescribeUnimported(UnimportedModrinthPack pack) =>
		pack.Worlds.Count > 0
			? Loc.T("mc.cleanup.packWithWorlds", pack.Instance.Name, pack.Instance.ModCount, string.Join(", ", pack.Worlds))
			: Loc.T("mc.cleanup.packNoWorlds", pack.Instance.Name, pack.Instance.ModCount);

	/// <summary>
	/// Offers to start Windows' uninstaller for the Modrinth App, then waits for the player to say it has finished and
	/// checks that it really has. Returns true only when the app is no longer installed.
	///
	/// <para>
	/// Waited on by asking rather than by watching the process: this kind of uninstaller copies itself somewhere
	/// temporary, starts the copy and closes at once, so the process started here ends long before the uninstall does.
	/// </para>
	/// </summary>
	private async Task<bool> UninstallModrinthAppAsync(string uninstaller)
	{
		if (SpeakBox(Loc.T("mc.cleanup.stillInstalled"), Loc.T("mc.cleanup.title"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			Speak(Loc.T("common.changesCancelled"));
			return false;
		}

		try
		{
			if (!File.Exists(uninstaller)) throw new FileNotFoundException(uninstaller);
			Process.Start(new ProcessStartInfo(uninstaller) { UseShellExecute = true });
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			// 1223: the Windows permission prompt was answered No.
			Speak(Loc.T("mc.cleanup.uninstallDeclined"));
			return false;
		}
		catch (Exception ex)
		{
			LogFailure("Minecraft", "Could not start the Modrinth App's uninstaller", ex);
			SpeakBox(Loc.T("mc.cleanup.uninstallFailed", FriendlyError(ex)), Loc.T("mc.cleanup.title"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return false;
		}

		SpeakBox(Loc.T("mc.cleanup.waitForUninstaller"), Loc.T("mc.cleanup.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);

		// A moment for Windows to finish updating its list after the uninstaller's last window closes.
		await Task.Delay(1000);
		if (ModrinthAppUninstaller() != null)
		{
			SpeakBox(Loc.T("mc.cleanup.stillThere"), Loc.T("mc.cleanup.title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
			return false;
		}

		return true;
	}

	/// <summary>
	/// After an import: when every Modrinth App pack with anything in it has now been brought across, offers to clear
	/// the app's files away. Asked before the "play now" question, which may start the game and take the screen.
	/// </summary>
	private async Task OfferModrinthAppCleanupAfterImportAsync()
	{
		string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		bool anythingLeft = await Task.Run(() => ModrinthAppCleanup.NotImported(
				ModrinthAppImport.FindAll(appData, Path.GetTempPath()),
				MinecraftModpacks.FindInstalled(MinecraftModpacks.PacksFolder))
			.Any(p => p.HasContent));
		if (anythingLeft) return;

		if (SpeakBox(Loc.T("mc.cleanup.offerAfterImport"), Loc.T("mc.cleanup.title"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			await RemoveModrinthAppLeftoversAsync(offeredAfterImport: true);
	}
}
