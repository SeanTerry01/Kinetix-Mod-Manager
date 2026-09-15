using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// Finds games installed through Heroic, which is how a great many Linux players own GOG and Epic titles.
///
/// <para>
/// Steam is not the whole of Linux gaming and the manager was behaving as though it were: a Skyrim bought
/// from GOG and installed with Heroic sat in a perfectly ordinary folder that nothing looked in, so the game
/// read as not installed on a machine where it plainly was. Heroic keeps a plain JSON record of everything it
/// has installed, which makes this the same shape of job as <see cref="SteamLibraryLocator"/> rather than a
/// guess at folder names.
/// </para>
///
/// <para>
/// ⚠️ Lutris is <em>not</em> covered, and the reason is worth writing down rather than discovering: it keeps
/// its library in a SQLite database and its per-game settings in YAML, and neither can be read without a
/// dependency this layer does not have. Hand-parsing YAML to avoid taking one on would be the fragile option,
/// not the careful one.
/// </para>
/// </summary>
public static class HeroicLibraryLocator
{
	/// <summary>
	/// Where Heroic keeps its configuration, in the order they are tried. The Flatpak build keeps its own
	/// home, and somebody who installed it that way has no other.
	/// </summary>
	public static IEnumerable<string> ConfigRoots(string home)
	{
		yield return Path.Combine(home, ".config", "heroic");
		yield return Path.Combine(home, ".var", "app", "com.heroicgameslauncher.hgl", "config", "heroic");
	}

	/// <summary>
	/// The files Heroic records installed games in — one per store it supports.
	///
	/// All of them are read rather than only the GOG one: a user who owns a supported game on Epic or the
	/// Amazon store has it installed in exactly the same way, and the manager only cares where the folder is.
	/// </summary>
	private static readonly string[] InstalledFiles =
	{
		Path.Combine("gog_store", "installed.json"),
		Path.Combine("store", "legendary_library.json"),
		Path.Combine("store", "nile_library.json"),
		"installed.json",
	};

	/// <summary>
	/// Every folder Heroic reports as an installed game, on this machine.
	///
	/// Folders that no longer exist are dropped. Heroic's record outlives an uninstall done by hand, and
	/// answering with a path that is not there would have every later step fail for a reason nothing
	/// explains.
	/// </summary>
	public static IReadOnlyList<string> InstalledFolders(string home)
	{
		var folders = new List<string>();

		foreach (string config in ConfigRoots(home))
		{
			if (!Directory.Exists(config)) continue;

			foreach (string relative in InstalledFiles)
			{
				string path = Path.Combine(config, relative);
				if (!File.Exists(path)) continue;

				try { folders.AddRange(ReadInstallPaths(File.ReadAllText(path))); }
				catch (Exception ex)
				{
					// One unreadable record must not cost the others. A half-written file during an install
					// is an ordinary thing to walk into.
					DiagnosticLog.WriteException("Detect", $"reading Heroic's library at {path}", ex);
				}
			}
		}

		return folders
			.Where(Directory.Exists)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>
	/// The install folders named in one of Heroic's library files.
	///
	/// <para>
	/// The shape differs between stores and has changed between Heroic versions — a bare array in some, an
	/// object with an <c>installed</c> or <c>library</c> array in others — so this looks for the property
	/// rather than the structure: any object anywhere in the document carrying an install path counts. That
	/// is deliberately forgiving, because the alternative is a locator that stops working on an upgrade the
	/// user did not know was a breaking one.
	/// </para>
	/// </summary>
	public static IReadOnlyList<string> ReadInstallPaths(string json)
	{
		var found = new List<string>();

		try
		{
			JToken root = JToken.Parse(json);
			IEnumerable<JToken> everything = root is JContainer container
				? new[] { root }.Concat(container.Descendants())
				: new[] { root };

			foreach (JObject node in everything.OfType<JObject>())
			{
				foreach (string key in new[] { "install_path", "installPath" })
				{
					string? path = (string?)node[key];
					if (!string.IsNullOrWhiteSpace(path)) { found.Add(path!.Trim()); break; }
				}

				// Newer Heroic nests it: { "install": { "install_path": "…" } }
				if (node["install"] is JObject install)
				{
					string? nested = (string?)install["install_path"] ?? (string?)install["installPath"];
					if (!string.IsNullOrWhiteSpace(nested)) found.Add(nested!.Trim());
				}
			}
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Detect", "reading a Heroic library file", ex);
		}

		return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	/// <summary>
	/// The folder holding <paramref name="exeName"/>, among everything Heroic has installed, or null.
	///
	/// Matched by the game's own executable rather than by folder name, for the same reason
	/// <see cref="GogLibraryLocator"/> does it: what Heroic or the user called the folder is not something to
	/// guess at, and the executable is the one name the game itself decides.
	/// </summary>
	public static string? FindGameFolder(string home, string? exeName)
	{
		if (string.IsNullOrWhiteSpace(exeName)) return null;

		foreach (string folder in InstalledFolders(home))
		{
			try
			{
				if (File.Exists(Path.Combine(folder, exeName))) return folder;

				// Heroic's record sometimes names the folder a game was installed *into* rather than the one
				// the game ended up in, which for a Windows game under a prefix is one level up.
				string? deeper = Directory.EnumerateDirectories(folder)
					.FirstOrDefault(d => File.Exists(Path.Combine(d, exeName)));
				if (deeper != null) return deeper;
			}
			catch (Exception ex)
			{
				DiagnosticLog.WriteException("Detect", $"looking for {exeName} under {folder}", ex);
			}
		}

		return null;
	}
}
