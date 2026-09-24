using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>One version folder under <c>.minecraft\versions</c> that nothing the manager runs needs.</summary>
public sealed class UnusedMinecraftVersion
{
	/// <summary>The folder's name — <c>1.21.4</c>, or <c>fabric-loader-0.19.5-26.2</c> for a Fabric one.</summary>
	public required string Id { get; init; }

	public long Bytes { get; init; }

	/// <summary>
	/// The official launcher's installations that start this version, by name. Removing the version leaves them
	/// pointing at nothing, so they are removed with it — and named first.
	/// </summary>
	public IReadOnlyList<string> LauncherInstallations { get; init; } = Array.Empty<string>();

	public bool IsFabric => Id.StartsWith("fabric-loader-", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Finding the Minecraft versions nothing uses any more, so they can be removed.
///
/// <para>
/// Every version of Minecraft the manager or the official launcher has ever installed stays in
/// <c>.minecraft\versions</c> for good — a modpack on an older Minecraft adds one, and moving the player's own game
/// to a new version leaves the old one behind. Each is 25 to 40 MB of the game itself, with a Fabric folder beside
/// it that often holds a second copy of the same jar.
/// </para>
///
/// <para>
/// ⚠️ "In use" follows <c>inheritsFrom</c>. A Fabric version is a thin layer on a vanilla one, and on this machine
/// the 26.3 folder held only its JSON while its jar lived in the Fabric folder — so keeping the Fabric folder and
/// removing the vanilla one it builds on would leave a game that cannot start. Only whole version folders are ever
/// removed: the sounds, libraries and Java that versions share are left alone, because working out which of those
/// nothing needs is risky and saves little.
/// </para>
/// </summary>
public static class MinecraftVersionCleanup
{
	/// <summary>The launcher's special targets, which name no folder.</summary>
	private static readonly HashSet<string> LauncherAliases = new(StringComparer.OrdinalIgnoreCase)
		{ "latest-release", "latest-snapshot" };

	/// <summary>
	/// Every version folder that none of <paramref name="inUse"/> needs, with its size and the launcher installations
	/// that point at it.
	/// </summary>
	/// <param name="inUse">The version ids that are started: the player's own and every pack's. Their parents are kept too.</param>
	public static IReadOnlyList<UnusedMinecraftVersion> FindUnused(string root, IEnumerable<string> inUse)
	{
		string versions = Path.Combine(root, "versions");
		if (!Directory.Exists(versions)) return Array.Empty<UnusedMinecraftVersion>();

		HashSet<string> keep = WithParents(root, inUse);
		Dictionary<string, List<string>> launcher = LauncherInstallationsByVersion(root);

		var unused = new List<UnusedMinecraftVersion>();
		foreach (string folder in Directory.EnumerateDirectories(versions))
		{
			string id = Path.GetFileName(folder);
			if (keep.Contains(id)) continue;

			// An installation starting a Fabric version also depends on the vanilla one under it.
			var installations = new List<string>(launcher.GetValueOrDefault(id) ?? new List<string>());
			foreach ((string child, List<string> names) in launcher)
				if (!string.Equals(child, id, StringComparison.OrdinalIgnoreCase) &&
					WithParents(root, new[] { child }).Contains(id))
					installations.AddRange(names);

			unused.Add(new UnusedMinecraftVersion
			{
				Id = id,
				Bytes = SizeOf(folder),
				LauncherInstallations = installations.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
			});
		}

		return unused.OrderBy(v => v.IsFabric).ThenBy(v => v.Id, StringComparer.OrdinalIgnoreCase).ToList();
	}

	/// <summary><paramref name="ids"/> and every version they inherit from, read from the version files.</summary>
	public static HashSet<string> WithParents(string root, IEnumerable<string> ids)
	{
		var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string start in ids.Where(i => !string.IsNullOrWhiteSpace(i)))
		{
			string? id = start;
			for (int depth = 0; id != null && depth < 8 && all.Add(id); depth++)
				id = (string?)MinecraftLauncher.ReadVersionJson(root, id)?["inheritsFrom"];
		}
		return all;
	}

	/// <summary>The official launcher's installations, grouped by the version each starts. Aliases are left out.</summary>
	public static Dictionary<string, List<string>> LauncherInstallationsByVersion(string root)
	{
		var byVersion = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		string path = MinecraftLayout.LauncherProfilesPathFor(root);
		if (!File.Exists(path)) return byVersion;

		try
		{
			JObject doc = FabricInstaller.ParsePreservingDates(File.ReadAllText(path));
			foreach (JProperty profile in (doc["profiles"] as JObject ?? new JObject()).Properties())
			{
				string version = (string?)profile.Value["lastVersionId"] ?? "";
				if (version.Length == 0 || LauncherAliases.Contains(version)) continue;

				string name = (string?)profile.Value["name"] is { Length: > 0 } n ? n : profile.Name;
				if (!byVersion.TryGetValue(version, out List<string>? names)) byVersion[version] = names = new List<string>();
				names.Add(name);
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", "reading the launcher's installations", ex);
		}

		return byVersion;
	}

	/// <summary>
	/// Takes out of <c>launcher_profiles.json</c> every installation that starts one of <paramref name="versionIds"/>,
	/// leaving everything else exactly as written — dates included. Returns how many went. The launcher must be
	/// closed: it rewrites this file when it exits, and would put them straight back.
	/// </summary>
	public static int RemoveLauncherInstallations(string root, IReadOnlyCollection<string> versionIds)
	{
		string path = MinecraftLayout.LauncherProfilesPathFor(root);
		if (!File.Exists(path) || versionIds.Count == 0) return 0;

		JObject doc = FabricInstaller.ParsePreservingDates(File.ReadAllText(path));
		if (doc["profiles"] is not JObject profiles) return 0;

		List<JProperty> gone = profiles.Properties()
			.Where(p => versionIds.Contains((string?)p.Value["lastVersionId"] ?? "", StringComparer.OrdinalIgnoreCase))
			.ToList();
		foreach (JProperty p in gone) p.Remove();

		if (gone.Count > 0) File.WriteAllText(path, doc.ToString(Newtonsoft.Json.Formatting.Indented));
		return gone.Count;
	}

	private static long SizeOf(string folder)
	{
		try { return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", $"measuring {folder}", ex);
			return 0;
		}
	}
}
