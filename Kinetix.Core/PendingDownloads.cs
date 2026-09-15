using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// One file in a game's downloads folder, with what its name (or, for a Minecraft jar, its manifest) says about it.
/// </summary>
/// <param name="FullPath">Where the file is.</param>
/// <param name="DownloadedAt">When it arrived — the file's last write, which is when the download finished.</param>
/// <param name="Bytes">Its size.</param>
/// <param name="ModId">The Nexus mod id in its name, or the Fabric mod id inside a jar; null when neither is known.</param>
/// <param name="Version">The release it holds, where that is known.</param>
/// <param name="Name">What to call it out loud.</param>
public sealed record DownloadedFile(string FullPath, DateTime DownloadedAt, long Bytes, string? ModId, string? Version, string Name)
{
	public string FileName => Path.GetFileName(FullPath);
}

/// <summary>
/// Which downloads have never been installed — the ones a user said "not now" to when the manager offered.
///
/// <para>
/// <b>Why a record, and why it is not the whole answer.</b> Every archive stays in the downloads folder whether or
/// not it was installed, so the folder alone cannot tell the two apart. From <see cref="AppSettings.InstalledArchivesSince"/>
/// onwards the manager writes down each download's file name as it installs it, and for anything that arrived after
/// that moment the record is the answer: in it means installed, absent means not. Guessing is wrong in exactly the
/// case this list exists for — an optional file from the page of a mod you already have carries the same mod id,
/// and would be hidden as "installed" when it never was.
/// </para>
///
/// <para>
/// Downloads older than the record cannot be answered that way, because nobody wrote them down. For those the list
/// works from what is installed: a mod with the download's id, at the download's release or later, means it went
/// in. A download newer than what is installed is an update that was declined, so it is listed. With no id to go on
/// the download's name is matched against the installed mods' names. That guess can be wrong, but it is only ever
/// made about downloads from before this feature existed.
/// </para>
///
/// <para>
/// A mod that was installed and later removed stays off the list, since it was installed. Reinstalling one of those
/// is what the Reinstall a Downloaded Mod list is for.
/// </para>
/// </summary>
public static class PendingDownloads
{
	/// <param name="downloads">Every mod file in the active game's downloads folder.</param>
	/// <param name="recordedAsInstalled">File names the manager recorded installing, for this game.</param>
	/// <param name="recordedSince">When that record began; null means there is no record to trust yet.</param>
	/// <param name="installed">The mods installed right now.</param>
	/// <param name="idOf">The id an installed mod is known by, in the same terms as <see cref="DownloadedFile.ModId"/>.</param>
	/// <param name="releaseOf">The release of an installed mod: the recorded download version where there is one.</param>
	/// <returns>The downloads never installed, newest first.</returns>
	public static List<DownloadedFile> NotYetInstalled(
		IEnumerable<DownloadedFile> downloads,
		IEnumerable<string> recordedAsInstalled,
		DateTime? recordedSince,
		IReadOnlyList<GameMod> installed,
		Func<GameMod, string?> idOf,
		Func<GameMod, string?> releaseOf)
	{
		var recorded = new HashSet<string>(recordedAsInstalled, StringComparer.OrdinalIgnoreCase);

		return downloads
			.Where(d => !recorded.Contains(d.FileName))
			.Where(d => (recordedSince.HasValue && d.DownloadedAt >= recordedSince.Value)
				|| !LooksInstalled(d, installed, idOf, releaseOf))
			.OrderByDescending(d => d.DownloadedAt)
			.ToList();
	}

	/// <summary>The inference for a download from before the record began. See the class remarks.</summary>
	public static bool LooksInstalled(DownloadedFile download, IReadOnlyList<GameMod> installed,
		Func<GameMod, string?> idOf, Func<GameMod, string?> releaseOf)
	{
		if (!string.IsNullOrWhiteSpace(download.ModId))
		{
			List<GameMod> same = installed
				.Where(m => string.Equals(idOf(m)?.Trim(), download.ModId!.Trim(), StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (same.Count == 0) return false;

			// Installed, and the download does not say which release it is: nothing suggests it is anything but
			// the copy that is there.
			if (string.IsNullOrWhiteSpace(download.Version)) return true;

			// Installed at this release or a later one. An unknown installed release counts as a match too — the
			// mod is there, and calling its download "never installed" on no evidence would be the worse mistake.
			return same.Any(m => string.IsNullOrWhiteSpace(releaseOf(m)) || !ModVersions.IsNewer(releaseOf(m), download.Version));
		}

		var candidate = new GameMod { Name = download.Name };
		return installed.Any(m => ModNameMatch.IsConfident(m, candidate));
	}
}
