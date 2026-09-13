using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace KinetixModManager;

/// <summary>
/// Saving, listing and applying mod profiles — a named snapshot of which mods were switched on.
///
/// <para>
/// Profiles are how one install serves several playthroughs, and for this program's users they are worth
/// more than convenience: rebuilding a mod set by hand means arrowing a list of a hundred folders and
/// toggling the right forty by ear. Getting one back should be one action.
/// </para>
///
/// <para>
/// Lifted out of Form1.Profiles by Phase 4. Deciding what a profile contains, where it is kept and what
/// applying it would change are all answerable without a window; only the confirming and the announcing
/// are not.
/// </para>
/// </summary>
public static class ProfileStore
{
	private const string Extension = ".json";

	/// <summary>
	/// The file a profile is kept in.
	///
	/// The name is run through <see cref="WindowsFileName.ToFolderName"/>, which it previously was not:
	/// both the save and the delete paths built <c>name + ".json"</c> straight from what the user typed. A
	/// profile called "Mage/Thief" wrote into a subfolder that did not exist and failed; one ending in a dot
	/// or a space saved under a name Windows then refused to open. Worse than either, save and delete
	/// sanitised nothing and so could disagree, leaving a profile that could be listed and not removed.
	/// </summary>
	public static string FileNameFor(string profileName)
	{
		string safe = WindowsFileName.ToFolderName(profileName ?? "");
		return safe.Length == 0 ? "" : safe + Extension;
	}

	/// <summary>The full path of a profile's file, or empty when the name cannot be made into one.</summary>
	public static string PathFor(string folder, string profileName)
	{
		string file = FileNameFor(profileName);
		return file.Length == 0 || string.IsNullOrEmpty(folder) ? "" : Path.Combine(folder, file);
	}

	/// <summary>
	/// A profile describing the mods as they are now.
	///
	/// <paramref name="modPriority"/> and <paramref name="pluginOrder"/> are the Bethesda load order, and
	/// null for a game that has none. Null rather than empty on purpose: applying a profile that never
	/// captured an order must leave the current one alone, and an empty list would instead clear it.
	/// </summary>
	public static ModProfile Capture(
		string name,
		IEnumerable<GameMod> mods,
		string? theme = null,
		IEnumerable<string>? modPriority = null,
		IEnumerable<string>? pluginOrder = null)
	{
		var profile = new ModProfile { Name = name, ThemeOverride = theme };

		foreach (GameMod mod in mods ?? Enumerable.Empty<GameMod>())
			if (!string.IsNullOrEmpty(mod.UniqueId))
				profile.ModStates[mod.UniqueId] = mod.IsEnabled;

		if (modPriority != null) profile.ModPriority = modPriority.ToList();
		if (pluginOrder != null) profile.PluginOrder = pluginOrder.ToList();
		return profile;
	}

	/// <summary>Writes a profile. Returns the path written, or empty when the name was unusable.</summary>
	public static string Save(string folder, ModProfile profile)
	{
		string path = PathFor(folder, profile.Name);
		if (path.Length == 0) return "";

		Directory.CreateDirectory(folder);
		File.WriteAllText(path, JsonConvert.SerializeObject(profile, Formatting.Indented));
		return path;
	}

	/// <summary>
	/// Every profile in the folder, in name order.
	///
	/// One unreadable file does not stop the rest being listed — a profile saved by an older build, or half
	/// written when something crashed, should cost the user that profile and not all of them.
	/// </summary>
	public static List<ModProfile> LoadAll(string folder, Action<string, Exception>? onError = null)
	{
		var profiles = new List<ModProfile>();
		if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return profiles;

		foreach (string path in Directory.GetFiles(folder, "*" + Extension).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
		{
			try
			{
				ModProfile? profile = JsonConvert.DeserializeObject<ModProfile>(File.ReadAllText(path));
				if (profile != null) profiles.Add(profile);
			}
			catch (Exception ex)
			{
				onError?.Invoke(Path.GetFileName(path), ex);
			}
		}

		return profiles;
	}

	/// <summary>Removes a profile. True when a file was actually deleted.</summary>
	public static bool Delete(string folder, string profileName)
	{
		string path = PathFor(folder, profileName);
		if (path.Length == 0 || !File.Exists(path)) return false;

		File.Delete(path);
		return true;
	}

	/// <summary>One mod whose state has to change for a profile to be satisfied.</summary>
	public readonly record struct StateChange(GameMod Mod, bool Enable);

	/// <summary>
	/// What applying <paramref name="profile"/> would actually change, and nothing else.
	///
	/// A mod the profile never mentioned is left exactly as it is, rather than being switched off for not
	/// appearing — a profile saved before a mod was installed must not uninstall it by implication. And a
	/// mod already in the right state is not touched, which is what keeps applying a profile quick instead
	/// of renaming every folder on disk to the name it already has.
	/// </summary>
	public static IReadOnlyList<StateChange> Changes(ModProfile profile, IEnumerable<GameMod> mods)
	{
		var changes = new List<StateChange>();
		if (profile?.ModStates == null || mods == null) return changes;

		foreach (GameMod mod in mods)
		{
			if (mod == null || string.IsNullOrEmpty(mod.UniqueId)) continue;
			if (!profile.ModStates.TryGetValue(mod.UniqueId, out bool wanted)) continue;
			if (mod.IsEnabled == wanted) continue;

			changes.Add(new StateChange(mod, wanted));
		}

		return changes;
	}
}
