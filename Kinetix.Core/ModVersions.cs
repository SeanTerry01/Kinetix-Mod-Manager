using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// Deciding whether a mod has an update waiting.
///
/// <para>
/// Harder than comparing two numbers, because the two numbers are often not comparable. SMAPI requires a
/// mod's manifest to carry a semantic version - two or three numbers - while a Nexus version field is free
/// text, so an author who publishes "2.0.3.5" has a manifest that must still read "2.0.3". Compare those
/// two directly and the mod is offered the same update forever; and editing the manifest to match is not a
/// fix, because SMAPI then refuses to parse it and skips the mod entirely.
/// </para>
///
/// <para>
/// The way out is to compare against the release the manager knows it installed, when it knows one, and to
/// fall back to the manifest only when it does not. That is what <see cref="HasPendingUpdate"/> does, and
/// it is the single place the question is answered.
/// </para>
///
/// <para>
/// Nagging is not a cosmetic failure here. An Updates list that reports work which is already done is one
/// the user stops trusting, and the list is how they find out a mod they depend on has been fixed.
/// </para>
/// </summary>
public static class ModVersions
{
	/// <summary>
	/// Compares two dot-separated version strings. Returns <c>true</c> if <paramref name="target"/>
	/// is numerically greater than <paramref name="current"/>.
	/// </summary>
	public static bool IsNewer(string? current, string? target)
	{
		if (string.IsNullOrEmpty(target))
		{
			return false;
		}
		if (string.IsNullOrEmpty(current))
		{
			return true;
		}
		string[] array = current.Split('.');
		string[] array2 = target.Split('.');
		for (int i = 0; i < Math.Max(array.Length, array2.Length); i++)
		{
			int result;
			int num = ((i < array.Length && int.TryParse(array[i], out result)) ? result : 0);
			int result2;
			int num2 = ((i < array2.Length && int.TryParse(array2[i], out result2)) ? result2 : 0);
			if (num2 > num)
			{
				return true;
			}
			if (num > num2)
			{
				return false;
			}
		}
		return false;
	}

	/// <summary>
	/// The key identifying the one download a mod came from - its Nexus page or its GitHub repository.
	/// </summary>
	public static string DownloadKey(GameMod mod) =>
		!string.IsNullOrEmpty(mod.NexusID) ? "Nexus:" + mod.NexusID : "GitHub:" + mod.GitHubRepo;

	/// <summary>
	/// Whether <paramref name="installed"/> still has an update pending at <paramref name="latestVersion"/>.
	///
	/// <paramref name="recordedVersion"/> is the release of this mod's download the manager knows went on
	/// disk, or null when it has never installed one. It is preferred over the manifest for the reason given
	/// on this class: the manifest may be incapable of expressing what the author actually published.
	/// </summary>
	public static bool HasPendingUpdate(GameMod installed, string? latestVersion, string? recordedVersion)
	{
		if (installed == null || string.IsNullOrWhiteSpace(latestVersion)) return false;

		string? recorded = UpdateCoverage.HasUpdateLink(installed) ? recordedVersion : null;
		return IsNewer(recorded ?? installed.Version, latestVersion);
	}

	/// <summary>
	/// The later of a mod's known latest version and a newly arrived <paramref name="candidate"/>.
	///
	/// Taking the maximum rather than the most recent answer is what makes the result independent of which
	/// check finished first. Two sources are asked - smapi.io and Nexus - and a slower, older Nexus answer
	/// could otherwise overwrite a newer one, leaving a row that reads "Current: 2.2.0. Latest: 2.2.0".
	/// </summary>
	public static string? Best(string? known, string? candidate)
	{
		if (string.IsNullOrWhiteSpace(candidate)) return known;
		return string.IsNullOrEmpty(known) || IsNewer(known, candidate) ? candidate : known;
	}

	/// <summary>
	/// Whether a mod is missing the details only its mod page can supply, and is therefore worth asking
	/// about. A locally-installed mod has a placeholder author and description until something fills them in.
	/// </summary>
	public static bool NeedsDetails(GameMod mod)
	{
		if (mod == null) return false;

		bool noAuthor = string.IsNullOrWhiteSpace(mod.Author) ||
						mod.Author.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
						mod.Author.Equals("User", StringComparison.OrdinalIgnoreCase);

		string description = (mod.Description ?? "").Trim();
		bool noDescription = description.Length == 0 ||
							 description == "Installed local mod." ||
							 description == "Installed BepInEx plugin.";

		return noAuthor || noDescription;
	}
}
