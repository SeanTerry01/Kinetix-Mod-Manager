using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace KinetixModManager;

/// <summary>
/// A portable, shareable "recipe" for a modded setup: the ordered list of mods (by Nexus id, version, and
/// load-order position) that make up a loadout, saved as a single JSON file. It does NOT contain the mod files
/// themselves — installing a collection re-downloads each mod from Nexus — so the file stays tiny and can be
/// shared freely without redistributing anyone's mod (and authors still get the downloads/endorsements).
/// Round-trips with <see cref="Form1.ExportCollection"/> and the collection installer.
/// </summary>
public class Collection
{
	/// <summary>Schema version, so a future reader can migrate or reject an older/newer file.</summary>
	public int SchemaVersion { get; set; } = 1;

	/// <summary>Display name of the collection.</summary>
	public string Name { get; set; } = "";

	/// <summary>The game this collection targets, matching <see cref="AppSettings.ActiveGame"/> (e.g. "SkyrimSE").</summary>
	public string Game { get; set; } = "";

	/// <summary>The app version that created the file (informational).</summary>
	public string AppVersion { get; set; } = "";

	/// <summary>When the file was created, in UTC.</summary>
	public DateTime CreatedUtc { get; set; }

	/// <summary>The mods that make up the collection, highest load-order priority first.</summary>
	public List<CollectionMod> Mods { get; set; } = new List<CollectionMod>();

	/// <summary>
	/// Names of enabled mods that could not be included because they have no Nexus id (manually installed or
	/// hosted off Nexus). Listed so whoever installs the collection knows they must supply these themselves.
	/// </summary>
	public List<string> UnavailableLocal { get; set; } = new List<string>();

	/// <summary>Loads a collection from <paramref name="path"/>, or <c>null</c> if it can't be read/parsed.</summary>
	public static Collection? Load(string path)
	{
		try { return JsonConvert.DeserializeObject<Collection>(File.ReadAllText(path)); }
		catch { return null; }
	}

	/// <summary>Writes the collection to <paramref name="path"/> as indented JSON. May throw on I/O error.</summary>
	public void Save(string path)
	{
		File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
	}
}

/// <summary>One mod within a <see cref="Collection"/>.</summary>
public class CollectionMod
{
	/// <summary>Nexus numeric mod id — the only thing needed to re-download the mod.</summary>
	public string NexusId { get; set; } = "";

	/// <summary>Display name, used in the install summary and progress.</summary>
	public string Name { get; set; } = "";

	/// <summary>Version recorded when the collection was made (informational in this version).</summary>
	public string Version { get; set; } = "";

	/// <summary>0-based load-order position; 0 is highest priority (wins loose-file conflicts).</summary>
	public int PriorityIndex { get; set; }

	/// <summary>Whether the mod is essential to the collection (vs optional). Always true in this version.</summary>
	public bool Required { get; set; } = true;

	/// <summary>
	/// Recorded FOMOD installer choices to replay on install, or <c>null</c> to use the installer's defaults.
	/// Reserved for a later step: capturing a user's custom FOMOD choices during a normal install isn't wired
	/// yet, so this is always null for now and scripted (FOMOD) mods install with their default options.
	/// </summary>
	public FomodSelection? FomodSelection { get; set; }
}
