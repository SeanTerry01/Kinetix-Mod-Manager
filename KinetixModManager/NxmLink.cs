using System;

namespace KinetixModManager;

/// <summary>
/// What an <c>nxm://</c> link carries. Nexus mints one when the user presses "Mod Manager Download" in their
/// browser, and it names four things: the game, the mod, the file, and a short-lived key authorising the download.
///
/// <para>
/// The game matters more than it looks. A link is complete on its own — <c>nxm://skyrimspecialedition/mods/…</c>
/// says which game it is for whether or not that game happens to be the one loaded in the manager. Reading the
/// game from the link rather than from the open session is what lets a mod be downloaded straight from a browser
/// with a different game loaded, or none at all. Doing the opposite produced the bug this type exists to prevent:
/// the manager asked Nexus for a Skyrim mod id under the loaded game's name (and, with no session, under Stardew
/// Valley's, its fallback), Nexus answered "no such mod in that game" as a JSON object rather than the expected
/// array, and the user was shown a JSON parser error that named nothing they could act on.
/// </para>
///
/// <para>
/// <see cref="Query"/> is kept verbatim, including its leading <c>?</c>, because the key and expiry inside it have
/// to reach the API untouched — they are what authorise a free account's download, and they expire quickly.
/// </para>
/// </summary>
public readonly record struct NxmLink(string GameDomain, string ModId, string FileId, string Query)
{
	/// <summary>
	/// Reads a link of the form <c>nxm://&lt;game&gt;/mods/&lt;modId&gt;/files/&lt;fileId&gt;?key=…&amp;expires=…</c>.
	/// Returns <c>false</c> for anything else rather than throwing, because the input arrives from outside the
	/// program — the browser, or a command line — and a malformed one is a message to the user, not a crash.
	/// </summary>
	public static bool TryParse(string? url, out NxmLink link)
	{
		link = default;
		if (string.IsNullOrWhiteSpace(url)) return false;
		if (!url.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase)) return false;

		Uri parsed;
		try { parsed = new Uri(url); }
		catch (UriFormatException) { return false; }

		string domain = parsed.Host;
		if (domain.Length == 0) return false;

		// AbsolutePath is "/mods/<modId>/files/<fileId>", so splitting leaves an empty first element.
		string[] parts = parsed.AbsolutePath.Split('/');
		if (parts.Length < 5) return false;
		if (!parts[1].Equals("mods", StringComparison.OrdinalIgnoreCase)) return false;
		if (!parts[3].Equals("files", StringComparison.OrdinalIgnoreCase)) return false;
		if (parts[2].Length == 0 || parts[4].Length == 0) return false;

		link = new NxmLink(domain.ToLowerInvariant(), parts[2], parts[4], parsed.Query);
		return true;
	}
}
