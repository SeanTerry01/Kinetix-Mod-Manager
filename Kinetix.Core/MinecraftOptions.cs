using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>What <see cref="MinecraftOptions.EnsureNarratorOn"/> found and did.</summary>
public enum NarratorFix
{
	/// <summary>The Narrator was already on in some mode; nothing was written.</summary>
	AlreadyOn,
	/// <summary>It was off, and has been switched on. Worth telling the player — they may have turned it off.</summary>
	TurnedOn,
	/// <summary>There was no options.txt yet — the game has never run — so one was written with the Narrator on.</summary>
	Created
}

/// <summary>
/// The one game setting United Minecraft cannot work without: Minecraft's own Narrator.
///
/// <para>
/// ⚠️ United Minecraft does not speak by itself. It takes over Minecraft's Narrator and sends what the Narrator would
/// have said to the screen reader. So with the Narrator off — which is how a brand-new install starts, with no
/// <c>options.txt</c> at all — the game plays normally and says nothing. Found on a friend's computer, where the
/// manager had installed everything correctly and the only thing heard was the window title; Ctrl+B in the game
/// fixed it on the spot.
/// </para>
/// </summary>
public static class MinecraftOptions
{
	/// <summary>The Narrator mode written when switching it on: "System", which reads the menus — what Sean plays with.</summary>
	public const int NarratorSystem = 3;

	/// <summary>Minecraft's value for the Narrator being off.</summary>
	public const int NarratorOff = 0;

	private const string NarratorKey = "narrator:";

	/// <summary>
	/// Makes sure the Narrator is on in <paramref name="optionsTxtPath"/>, changing nothing else in the file.
	///
	/// <para>
	/// A new file is given <paramref name="dataVersion"/> as its <c>version</c> line when it is known. Without one,
	/// Minecraft takes the file for one written by a very old version and runs every settings upgrade it has over it
	/// — harmless for a single line as far as is known, but a guess this avoids when it can.
	/// </para>
	/// </summary>
	public static NarratorFix EnsureNarratorOn(string optionsTxtPath, int? dataVersion)
	{
		if (!File.Exists(optionsTxtPath))
		{
			Directory.CreateDirectory(Path.GetDirectoryName(optionsTxtPath)!);
			string created = (dataVersion is { } v ? $"version:{v}\n" : "") + NarratorKey + NarratorSystem + "\n";
			File.WriteAllText(optionsTxtPath, created, new UTF8Encoding(false));
			return NarratorFix.Created;
		}

		string text = File.ReadAllText(optionsTxtPath);
		string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
		string[] lines = text.Split(newline);

		int at = Array.FindIndex(lines, l => l.StartsWith(NarratorKey, StringComparison.Ordinal));
		if (at >= 0)
		{
			// Any mode but off is left exactly as the player chose it — Chat, All and System all reach United
			// Minecraft. Something unreadable is treated as off, which is what Minecraft itself falls back to.
			bool on = int.TryParse(lines[at].Substring(NarratorKey.Length).Trim(), out int mode) && mode != NarratorOff;
			if (on) return NarratorFix.AlreadyOn;

			lines[at] = NarratorKey + NarratorSystem;
			text = string.Join(newline, lines);
		}
		else
		{
			// No narrator line at all means Minecraft's default, which is off.
			if (text.Length > 0 && !text.EndsWith(newline, StringComparison.Ordinal)) text += newline;
			text += NarratorKey + NarratorSystem + newline;
		}

		File.WriteAllText(optionsTxtPath, text, new UTF8Encoding(false));
		return NarratorFix.TurnedOn;
	}

	/// <summary>
	/// The data version of the first of <paramref name="versionIds"/> whose game jar can be read, or <c>null</c>.
	///
	/// Read from <c>version.json</c> inside the jar (<c>world_version</c>), which is the number Minecraft writes as
	/// the first line of <c>options.txt</c>. ⚠️ The jar is not always under the vanilla version's name: the launcher
	/// copies it into the Fabric profile's folder, which is why more than one id is tried.
	/// </summary>
	public static int? DataVersionOf(string root, params string[] versionIds)
	{
		foreach (string id in versionIds.Where(id => !string.IsNullOrEmpty(id)).Distinct())
		{
			string jar = Path.Combine(root, "versions", id, id + ".jar");
			if (!File.Exists(jar)) continue;

			try
			{
				using ZipArchive zip = ZipFile.OpenRead(jar);
				ZipArchiveEntry? entry = zip.GetEntry("version.json");
				if (entry is null) continue;

				using var reader = new StreamReader(entry.Open());
				if ((int?)JObject.Parse(reader.ReadToEnd())["world_version"] is { } version) return version;
			}
			catch (Exception ex)
			{
				DiagnosticLog.WriteException("Minecraft", $"reading the data version from {jar}", ex);
			}
		}

		return null;
	}
}
