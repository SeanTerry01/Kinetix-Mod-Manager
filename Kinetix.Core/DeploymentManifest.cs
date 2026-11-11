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

	/// <summary>Where files this game's mods replaced are kept. See <see cref="OriginalFiles"/>. Not saved.</summary>
	[Newtonsoft.Json.JsonIgnore]
	public string OriginalsFolder { get; set; } = "";

	/// <summary>How many files the last pass could not change, usually because the game has them open. Not saved.</summary>
	[Newtonsoft.Json.JsonIgnore]
	public int Failures { get; set; }

	/// <summary>Loads the manifest for <paramref name="game"/>, or an empty one if none exists yet.</summary>
	public static DeploymentManifest Load(string game)
	{
		DeploymentManifest manifest = Read(game);
		manifest.OriginalsFolder = Path.Combine(AppSettings.AppDataFolder, "deployment", game + "-originals");
		return manifest;
	}

	/// <summary>
	/// The record was there and could not be read. Not saved. ⚠️ Not the same as no record: taken as "nothing deployed", every
	/// deployed file was then kept as one of the game's own, and a mod switched off later had its own copy put back as the
	/// "original", in the game for good. A pass that finds this changes nothing and says a Rebuild is needed.
	/// </summary>
	[Newtonsoft.Json.JsonIgnore]
	public bool Unreadable { get; set; }

	private static DeploymentManifest Read(string game)
	{
		string path = ManifestPath(game);
		if (!File.Exists(path)) return new DeploymentManifest();

		// A sharing violation, an antivirus scanning it, is usually over in a moment.
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			try
			{
				var m = JsonConvert.DeserializeObject<DeploymentManifest>(File.ReadAllText(path));
				if (m?.Deployed == null) break;
				// Deserialization does not preserve the case-insensitive comparer, so rebuild it.
				m.Deployed = new Dictionary<string, string>(m.Deployed, StringComparer.OrdinalIgnoreCase);
				return m;
			}
			catch (IOException ex) when (attempt < 3)
			{
				DiagnosticLog.WriteException("Deploy", "reading the record of which files are deployed, trying again", ex);
				Thread.Sleep(200);
			}
			catch (Exception ex)
			{
				DiagnosticLog.WriteException("Deploy", "reading the record of which files are deployed", ex);
				break;
			}
		}
		return new DeploymentManifest { Unreadable = true };
	}

	/// <summary>Writes the manifest for <paramref name="game"/>. Never throws.</summary>
	public void Save(string game)
	{
		try
		{
			string path = ManifestPath(game);
			string? dir = Path.GetDirectoryName(path);
			if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
			AtomicFile.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Deploy", "saving the record of which files are deployed", ex); }
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
