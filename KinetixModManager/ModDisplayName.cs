using System;
using System.Text.RegularExpressions;

namespace KinetixModManager;

/// <summary>
/// Turns the name of a downloaded archive into the mod's name, as it should be spoken and as a mod folder should be
/// called.
///
/// <para>
/// Nexus does not hand out the name a person would use. A download arrives as
/// <c>Skyrim Access-181131-1-2-3-1723456789.7z</c> — the mod's name, then its mod id, then its version with the
/// dots turned into dashes, then the moment it was uploaded — and on some content servers it arrives with no name at
/// all, just an opaque id like <c>99824770-6ed9-4868-9f98-b54fb58ecad6</c>. Both were being read out as if they were
/// the mod's name ("downloading 99824770 dash 6ed9 dash…") and both were being used to name the folder the mod was
/// installed into.
/// </para>
///
/// <para>
/// The two jobs are deliberately separate. The archive keeps its original file name on disk, because the version and
/// the mod id inside it are what later tell the update check which release is installed; it is only what the user
/// <em>hears</em>, and what the mod's folder is <em>called</em>, that this cleans up.
/// </para>
/// </summary>
public static class ModDisplayName
{
	/// <summary>
	/// A Nexus-generated tail: the mod id, then optional version parts, then the upload timestamp. Anchored to the
	/// end so a mod whose own name contains numbers keeps them — only the trailing machinery is removed.
	/// </summary>
	private static readonly Regex NexusTail = new(
		@"-\d{3,9}(?:-[0-9A-Za-z]+)*?-\d{9,11}$", RegexOptions.Compiled);

	/// <summary>
	/// The newer Nexus tail, which is separated by spaces and dates itself rather than counting seconds:
	/// <c>Address Library All in One (1.7.104.0) v13 32444 13 2026-08-27T15-29Z Ae46W7Fw2</c> is the mod id
	/// 32444, the version 13, when it was uploaded, and a per-download hash.
	///
	/// Worth its own pattern because the old one cannot see it at all — no hyphens, no unix timestamp — so every
	/// mod downloaded since Nexus changed the format kept the whole tail in its folder name. That folder name is
	/// what the manager falls back to searching by when it cannot identify a mod, and searching for a mod id and
	/// a datestamp finds nothing, which is exactly how "it looks for the folder name" was reported.
	/// </summary>
	private static readonly Regex NexusTailDated = new(
		@"\s+\d{3,9}\s+\S+\s+\d{4}-\d{2}-\d{2}T\d{2}-\d{2}Z(?:\s+\S+)?$",
		RegexOptions.Compiled);

	/// <summary>A name with no name in it: an id the content server made up, carrying nothing a person can read.</summary>
	public static bool IsOpaque(string? name)
	{
		if (string.IsNullOrWhiteSpace(name)) return true;
		string trimmed = name.Trim();

		if (Guid.TryParse(trimmed, out _)) return true;

		// Long strings of nothing but hex digits and separators are ids too, whatever shape they come in.
		if (trimmed.Length < 16) return false;
		foreach (char c in trimmed)
		{
			bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
			if (!hex && c != '-' && c != '_') return false;
		}
		return true;
	}

	/// <summary>
	/// The mod's name as taken from <paramref name="archiveName"/> (with or without its extension), or an empty
	/// string when the name holds nothing readable and the caller should ask Nexus instead.
	/// </summary>
	/// <param name="modId">
	/// The Nexus mod id when it is known. Given one, the split is exact — everything before <c>-&lt;modId&gt;-</c> —
	/// rather than inferred from the shape of the tail.
	/// </param>
	public static string Clean(string? archiveName, string? modId = null)
	{
		if (string.IsNullOrWhiteSpace(archiveName)) return "";

		string name = archiveName.Trim();

		// Drop a trailing extension only when it looks like one, so a mod named "Mod v1.2" keeps its ".2".
		Match extension = Regex.Match(name, @"\.(zip|7z|rar|001)$", RegexOptions.IgnoreCase);
		if (extension.Success) name = name.Substring(0, extension.Index);

		if (IsOpaque(name)) return "";

		if (!string.IsNullOrEmpty(modId))
		{
			int at = name.IndexOf("-" + modId + "-", StringComparison.OrdinalIgnoreCase);
			if (at > 0) name = name.Substring(0, at);
		}

		name = NexusTail.Replace(name, "");
		name = NexusTailDated.Replace(name, "");

		// Nexus turns spaces into hyphens or underscores in some file names; a run of them left behind by the trim
		// above reads as a stutter to a screen reader.
		name = Regex.Replace(name, @"[\s_-]+$", "");
		name = Regex.Replace(name, @"\s{2,}", " ").Trim();

		return IsOpaque(name) ? "" : name;
	}

	/// <summary>
	/// What to call the mod in speech: the cleaned archive name, falling back to <paramref name="knownName"/> (the
	/// name Nexus itself gives the mod) and finally to the raw name, so something is always said.
	/// </summary>
	public static string ForSpeech(string? archiveName, string? modId = null, string? knownName = null)
	{
		string cleaned = Clean(archiveName, modId);
		if (cleaned.Length > 0) return cleaned;
		if (!string.IsNullOrWhiteSpace(knownName)) return knownName.Trim();
		return archiveName?.Trim() ?? "";
	}
}
