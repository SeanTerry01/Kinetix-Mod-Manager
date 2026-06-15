using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace KinetixModManager;

/// <summary>
/// Records every file the manager has deployed (hard-linked or copied) into a Bethesda game's
/// folder and which installed mod currently owns it. Persisted per game so deployment can be
/// reconciled across launches: orphaned files are removed, lower-priority "losers" are restored
/// when a higher-priority mod is removed, and file conflicts are detected. Stardew Valley deploys
/// mods in place (no Data folder), so it never uses this.
/// </summary>
public class DeploymentManifest
{
	/// <summary>
	/// Maps a destination path (relative to the game's root folder, e.g. <c>Data\textures\x.dds</c>,
	/// or a root file such as <c>binkw64.dll</c>) to the mod folder name that currently owns it.
	/// </summary>
	public Dictionary<string, string> Deployed { get; set; } =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private static string ManifestPath(string game) =>
		Path.Combine(AppSettings.AppDataFolder, "deployment", game + ".json");

	/// <summary>Loads the manifest for <paramref name="game"/>, or an empty one if none exists yet.</summary>
	public static DeploymentManifest Load(string game)
	{
		try
		{
			string path = ManifestPath(game);
			if (File.Exists(path))
			{
				var m = JsonConvert.DeserializeObject<DeploymentManifest>(File.ReadAllText(path));
				if (m != null)
				{
					// Deserialization does not preserve the case-insensitive comparer, so rebuild it.
					m.Deployed = new Dictionary<string, string>(m.Deployed, StringComparer.OrdinalIgnoreCase);
					return m;
				}
			}
		}
		catch { /* a corrupt manifest falls back to empty; the next sync rebuilds it from disk */ }
		return new DeploymentManifest();
	}

	/// <summary>Writes the manifest for <paramref name="game"/>. Never throws.</summary>
	public void Save(string game)
	{
		try
		{
			string path = ManifestPath(game);
			string? dir = Path.GetDirectoryName(path);
			if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
			File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
		}
		catch { /* persistence is best-effort; an unsaved manifest is rebuilt on the next sync */ }
	}
}

/// <summary>
/// One deployed file path provided by more than one enabled mod. <see cref="Winner"/> is the mod
/// whose copy is currently on disk (the highest-priority provider); <see cref="Losers"/> are the
/// mods whose copy was overridden.
/// </summary>
public class FileConflict
{
	/// <summary>Path of the contested file, relative to the game root.</summary>
	public string RelativePath { get; set; } = "";

	/// <summary>Folder name of the mod whose file won (highest priority).</summary>
	public string Winner { get; set; } = "";

	/// <summary>Folder names of the mods whose file was overridden.</summary>
	public List<string> Losers { get; set; } = new List<string>();
}
