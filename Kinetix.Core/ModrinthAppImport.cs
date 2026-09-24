using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace KinetixModManager;

/// <summary>One modpack (an "instance") found in the Modrinth App's data.</summary>
public sealed class ModrinthAppInstance
{
	/// <summary>The instance's folder, under the app's <c>profiles</c>.</summary>
	public required string Folder { get; init; }

	public required string Name { get; init; }
	public string MinecraftVersion { get; init; } = "";

	/// <summary>The app's own word for the loader: <c>fabric</c>, <c>forge</c>, <c>neoforge</c>, <c>quilt</c>, <c>vanilla</c>.</summary>
	public string Loader { get; init; } = "";

	public string LoaderVersion { get; init; } = "";

	/// <summary>The Modrinth project and version it is linked to, when the app still links it.</summary>
	public string LinkedProjectId { get; init; } = "";
	public string LinkedVersionId { get; init; } = "";

	public int ModCount { get; init; }

	/// <summary>True for an instance from the app's older data folder, which kept no database.</summary>
	public bool FromOlderApp { get; init; }

	public bool IsFabric => string.Equals(Loader, "fabric", StringComparison.OrdinalIgnoreCase);

	/// <summary>Everything needed to start it is known.</summary>
	public bool HasVersions => MinecraftVersion.Length > 0 && (LoaderVersion.Length > 0 || !IsFabric);
}

/// <summary>
/// Finding the Modrinth App's modpacks and copying one into the manager — the Modrinth App's counterpart of the
/// Mod Organizer 2 import. Nothing of the app's is changed: its instance stays exactly where it was, still working
/// in the app.
///
/// <para>
/// ⚠️ There are TWO data folders. The app kept its instances in <c>%AppData%\com.modrinth.theseus</c> until 2025,
/// with no database, and now keeps them in <c>%AppData%\ModrinthApp</c> with their details in SQLite
/// (<c>app.db</c>, table <c>profiles</c>). Both were found on the development machine, each holding a pack.
/// </para>
///
/// <para>
/// The database is read from a COPY, taken with its write-ahead log. The app keeps <c>app.db</c> in WAL mode and
/// may be running; reading the live file risks both a lock and reading around changes still sitting in the log.
/// </para>
/// </summary>
public static class ModrinthAppImport
{
	/// <summary>Folders an import leaves behind: logs, crash reports and Fabric's cache are the app's run history, not the pack.</summary>
	public static readonly IReadOnlyList<string> SkippedFolders = new[] { "logs", "crash-reports", ".fabric" };

	/// <summary>The app's data folders that exist on this computer, newest app first.</summary>
	public static IReadOnlyList<(string Folder, bool Older)> DataFolders(string appData) =>
		new[] { (Path.Combine(appData, "ModrinthApp"), false), (Path.Combine(appData, "com.modrinth.theseus"), true) }
			.Where(f => Directory.Exists(Path.Combine(f.Item1, "profiles")))
			.ToList();

	/// <summary>Every instance in every data folder, by name.</summary>
	public static IReadOnlyList<ModrinthAppInstance> FindAll(string appData, string scratchFolder) =>
		DataFolders(appData)
			.SelectMany(f => FindIn(f.Folder, f.Older, scratchFolder))
			.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
			.ToList();

	/// <summary>The instances in one data folder: its database's details where it has one, else what the files say.</summary>
	public static IReadOnlyList<ModrinthAppInstance> FindIn(string dataFolder, bool older, string scratchFolder)
	{
		string profiles = Path.Combine(dataFolder, "profiles");
		if (!Directory.Exists(profiles)) return Array.Empty<ModrinthAppInstance>();

		Dictionary<string, ProfileRow> rows = ReadProfiles(Path.Combine(dataFolder, "app.db"), scratchFolder);
		var found = new List<ModrinthAppInstance>();

		foreach (string folder in Directory.EnumerateDirectories(profiles))
		{
			string path = Path.GetFileName(folder);
			rows.TryGetValue(path, out ProfileRow? row);
			(string game, string loaderVersion) = row is null ? VersionsFromFiles(folder) : ("", "");

			found.Add(new ModrinthAppInstance
			{
				Folder = folder,
				Name = row?.Name is { Length: > 0 } n ? n : path,
				MinecraftVersion = row?.GameVersion ?? game,
				Loader = row?.Loader ?? (loaderVersion.Length > 0 ? "fabric" : ""),
				LoaderVersion = row?.LoaderVersion ?? loaderVersion,
				LinkedProjectId = row?.LinkedProjectId ?? "",
				LinkedVersionId = row?.LinkedVersionId ?? "",
				ModCount = CountMods(folder),
				FromOlderApp = older
			});
		}

		return found;
	}

	/// <summary>
	/// The pack's own version, from the instance's folder name — the app names folders <c>Name-2.2.3</c>. Only a
	/// guess, used only where nothing better is known, and <c>""</c> when the folder does not have that shape.
	/// </summary>
	public static string VersionFromFolderName(string folderName, string name)
	{
		string prefix = name + "-";
		return folderName.StartsWith(prefix, StringComparison.Ordinal) && folderName.Length > prefix.Length
			? folderName.Substring(prefix.Length)
			: "";
	}

	/// <summary>
	/// The Minecraft and Fabric versions an instance last ran, from what the game left behind: Fabric's cache folder
	/// (<c>.fabric\remappedJars\minecraft-1.21.5-0.16.14</c>), else the log. An instance never started has neither.
	/// </summary>
	public static (string MinecraftVersion, string LoaderVersion) VersionsFromFiles(string instanceFolder)
	{
		string remapped = Path.Combine(instanceFolder, ".fabric", "remappedJars");
		if (Directory.Exists(remapped))
		{
			foreach (string dir in Directory.EnumerateDirectories(remapped, "minecraft-*"))
			{
				// minecraft-<game>-<loader>; the loader is the last part, and a game version never has a hyphen
				// before a release, so the split is on the last hyphen.
				string rest = Path.GetFileName(dir).Substring("minecraft-".Length);
				int cut = rest.LastIndexOf('-');
				if (cut > 0) return (rest.Substring(0, cut), rest.Substring(cut + 1));
			}
		}

		MinecraftLaunchOutcome outcome = MinecraftLaunchLog.ReadLatest(instanceFolder);
		return outcome.FabricLoaded ? (outcome.GameVersion, outcome.LoaderVersion) : ("", "");
	}

	private static int CountMods(string folder)
	{
		string mods = Path.Combine(folder, "mods");
		return Directory.Exists(mods)
			? Directory.EnumerateFiles(mods).Count(f => MinecraftLayout.IsModFile(f, GameProfiles.Find(GameProfiles.Minecraft)?.DisabledModSuffix))
			: 0;
	}

	private sealed record ProfileRow(string Name, string GameVersion, string Loader, string LoaderVersion,
		string LinkedProjectId, string LinkedVersionId);

	/// <summary>Reads the <c>profiles</c> table from a copy of the database. An absent or unreadable one gives nothing.</summary>
	private static Dictionary<string, ProfileRow> ReadProfiles(string database, string scratchFolder)
	{
		var rows = new Dictionary<string, ProfileRow>(StringComparer.OrdinalIgnoreCase);
		if (!File.Exists(database)) return rows;

		string copyFolder = Path.Combine(scratchFolder, "modrinth-app-db-" + Guid.NewGuid().ToString("N"));
		try
		{
			Directory.CreateDirectory(copyFolder);
			string copy = Path.Combine(copyFolder, "app.db");
			foreach (string suffix in new[] { "", "-wal", "-shm" })
				if (File.Exists(database + suffix)) File.Copy(database + suffix, copy + suffix);

			using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
				{ DataSource = copy, Pooling = false }.ToString());
			connection.Open();

			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = "SELECT path, name, game_version, mod_loader, mod_loader_version, " +
								  "linked_project_id, linked_version_id FROM profiles";
			using SqliteDataReader reader = command.ExecuteReader();
			while (reader.Read())
			{
				string Text(int i) => reader.IsDBNull(i) ? "" : reader.GetString(i);
				rows[Text(0)] = new ProfileRow(Text(1), Text(2), Text(3), Text(4), Text(5), Text(6));
			}
		}
		catch (Exception ex)
		{
			// A newer app that changed its tables is not a reason to import nothing: the files still say enough.
			DiagnosticLog.WriteException("Minecraft", "reading the Modrinth App's list of instances", ex);
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			try { Directory.Delete(copyFolder, recursive: true); }
			catch (Exception ex) { DiagnosticLog.WriteException("Minecraft", $"removing {copyFolder}", ex); }
		}

		return rows;
	}

	/// <summary>
	/// Copies an instance's folder into <paramref name="target"/>, leaving out its run history (see
	/// <see cref="SkippedFolders"/>). Everything else comes across — mods, their settings, worlds, resource packs,
	/// and any screen reader files the pack keeps at the top of its folder. Returns the number of files copied.
	/// </summary>
	public static int CopyInstance(string instanceFolder, string target)
	{
		int copied = 0;

		void Copy(string from, string to, bool top)
		{
			Directory.CreateDirectory(to);
			foreach (string file in Directory.EnumerateFiles(from))
			{
				File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
				copied++;
			}
			foreach (string dir in Directory.EnumerateDirectories(from))
			{
				string name = Path.GetFileName(dir);
				if (top && SkippedFolders.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
				Copy(dir, Path.Combine(to, name), top: false);
			}
		}

		Copy(instanceFolder, target, top: true);
		return copied;
	}
}
