using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>A Modrinth App pack that has not been brought across, and what would go with its folder.</summary>
public sealed record UnimportedModrinthPack(ModrinthAppInstance Instance, IReadOnlyList<string> Worlds)
{
	/// <summary>Whether removing it loses anything: mods or worlds. An empty leftover folder loses nothing.</summary>
	public bool HasContent => Instance.ModCount > 0 || Worlds.Count > 0;
}

/// <summary>
/// Clearing away what the Modrinth App leaves on the computer once everything worth keeping has been brought into
/// the manager: its own copy of Minecraft (game files, sounds, libraries, Java), its packs, and its browser cache.
///
/// <para>
/// ⚠️ The manager deletes another program's files here, which is the kind of thing that is easy to regret, so the
/// safeguards are the feature: only the four exact folders the app uses by default; never while the app is installed
/// or running; every pack not yet brought across named — with its worlds — before anything is asked; and everything
/// to the Recycle Bin rather than gone. The imported packs are unaffected: importing makes real copies, and the
/// newer app's habit of hard-linking identical files together only ever happens inside its own folder.
/// </para>
/// </summary>
public static class ModrinthAppCleanup
{
	/// <summary>How the app appears in Windows' list of installed programs.</summary>
	public const string InstalledName = "Modrinth App";

	/// <summary>The app's process, to refuse while it runs.</summary>
	public const string ProcessName = "Modrinth App";

	/// <summary>
	/// The app's data folders that exist here: its current and older data in <c>AppData\Roaming</c>, and its browser
	/// cache under each name in <c>AppData\Local</c>. Nothing else is ever considered.
	/// </summary>
	public static IReadOnlyList<string> DataFolders(string appData, string localAppData) =>
		new[]
		{
			Path.Combine(appData, "ModrinthApp"),
			Path.Combine(appData, "com.modrinth.theseus"),
			Path.Combine(localAppData, "ModrinthApp"),
			Path.Combine(localAppData, "com.modrinth.theseus"),
		}
		.Where(Directory.Exists)
		.ToList();

	/// <summary>
	/// The app's packs that no manager pack was imported from, each with the worlds it holds — what the player would
	/// lose, named before they are asked.
	/// </summary>
	public static IReadOnlyList<UnimportedModrinthPack> NotImported(
		IEnumerable<ModrinthAppInstance> instances, IEnumerable<MinecraftPack> managerPacks)
	{
		var imported = new HashSet<string>(
			managerPacks.Select(p => Normalise(p.ImportedFrom)).Where(f => f.Length > 0),
			StringComparer.OrdinalIgnoreCase);

		return instances
			.Where(i => !imported.Contains(Normalise(i.Folder)))
			.Select(i => new UnimportedModrinthPack(i, MinecraftWorlds.FindIn(i.Folder).Select(w => w.Name).ToList()))
			.ToList();
	}

	/// <summary>
	/// The combined size of the folders, for the question. An over-estimate on the newer app, which hard-links one file
	/// into several packs and so stores it once while this counts it for each — which is why it is said as "about".
	/// </summary>
	public static long ApproximateSize(IEnumerable<string> folders)
	{
		long total = 0;
		foreach (string folder in folders)
		{
			try { total += Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }
			catch (Exception ex) { DiagnosticLog.WriteException("Minecraft", $"measuring {folder}", ex); }
		}
		return total;
	}

	private static string Normalise(string? folder) =>
		string.IsNullOrWhiteSpace(folder) ? "" : Path.GetFullPath(folder).TrimEnd('\\', '/');
}
