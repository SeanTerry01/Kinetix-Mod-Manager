using System;
using System.Linq;
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
		string name = WithoutExtension(archiveName.Trim());

		if (IsOpaque(name)) return "";

		// Where the mod's name ends is exactly where its mod id begins, so the name is cut at the id rather than
		// by recognising the tail that follows it. Finding the id is the same job whether the caller wants the id
		// (ModIdFromArchiveName) or the name in front of it, so both go through one place — which is what stops
		// the two drifting apart, and what makes every shape Nexus uses work here, not just the two the old
		// patterns knew. Anything with a mod id numbered under 100, or with no upload timestamp on the end, used
		// to keep its whole tail: a Moonlight Peaks mod installed into a folder called
		// "HouseStorageAnywhere 7 2 2026-08-22T02-14Z ifoLiAHsd".
		int at = -1;
		ArchiveTail? found = FindModId(name);
		if (found is { } tail && (string.IsNullOrEmpty(modId) || tail.ModId == modId))
			at = tail.At;
		else if (!string.IsNullOrEmpty(modId))
			// The caller knows the id for certain and the shape was one this cannot read positionally. Trust the
			// id and cut at its last appearance — Nexus's machinery is always at the end, so a mod whose own name
			// happens to contain its id keeps it.
			at = LastIndexOfKnownId(name, modId);

		if (at > 0)
		{
			name = name.Substring(0, at);
		}
		else
		{
			// No id to cut at. The old patterns still earn their place here for the one case the finder refuses
			// on purpose: a mod whose name is nothing but digits, where there is no letter to mark where the name
			// ends and so no safe way to tell a name from an id.
			name = NexusTail.Replace(name, "");
			name = NexusTailDated.Replace(name, "");
		}

		// Nexus turns spaces into hyphens or underscores in some file names; a run of them left behind by the trim
		// above reads as a stutter to a screen reader.
		name = Regex.Replace(name, @"[\s_-]+$", "");
		name = Regex.Replace(name, @"\s{2,}", " ").Trim();

		return IsOpaque(name) ? "" : name;
	}

	/// <summary>
	/// Where <paramref name="modId"/> last appears as a separated part of <paramref name="name"/>, in any of the
	/// shapes Nexus separates parts with, or <c>-1</c> when it does not. Last rather than first because the id is
	/// part of the machinery on the end: a mod called "Mod 2 Deluxe" that happens to be mod 2 keeps its name.
	/// </summary>
	private static int LastIndexOfKnownId(string name, string modId)
	{
		int best = -1;
		foreach (string pattern in new[] { $"-{modId}-", $" {modId} ", $"_{modId}_" })
		{
			int at = name.LastIndexOf(pattern, StringComparison.OrdinalIgnoreCase);
			if (at > best) best = at;
		}
		return best;
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

	/// <summary>
	/// The Nexus mod id an archive belongs to, or <c>null</c> when the name does not say.
	///
	/// <para>
	/// This is how the manager answers "have I already downloaded this?" without keeping a separate ledger that
	/// could drift out of step with the folder. The downloads folder <em>is</em> the record: every archive Nexus
	/// hands over carries the mod id in its name, so a search result can be matched against what is actually on
	/// disk — and an archive the user deletes stops being claimed, which a ledger would go on doing.
	/// </para>
	///
	/// <para>
	/// Three name shapes are in circulation, all of them present in a long-standing downloads folder, and the id
	/// sits in a different place in each. Every rule reads the id <b>positionally</b> rather than by looking for
	/// the digits anywhere in the name: a mod id of 2 or 7 would otherwise match the version digits of a hundred
	/// unrelated archives, and a wrong "you downloaded this" sends someone hunting their disk for a file that was
	/// never there.
	/// </para>
	/// </summary>
	public static string? ModIdFromArchiveName(string? archiveName)
	{
		if (string.IsNullOrWhiteSpace(archiveName)) return null;
		return FindModId(WithoutExtension(archiveName.Trim()))?.ModId;
	}

	/// <summary>A mod id found in an archive name, and the character it starts at — which is also where the mod's
	/// own name ends, so <see cref="Clean"/> and <see cref="ModIdFromArchiveName"/> can share one set of rules.</summary>
	private readonly record struct ArchiveTail(string ModId, int At);

	/// <summary>The archive name without a trailing archive extension, which is not part of any of the shapes
	/// below. Only extensions that look like one are dropped, so a mod named "Mod v1.2" keeps its ".2".</summary>
	private static string WithoutExtension(string name)
	{
		Match extension = Regex.Match(name, @"\.(zip|7z|rar|001)$", RegexOptions.IgnoreCase);
		return extension.Success ? name.Substring(0, extension.Index) : name;
	}

	/// <summary>
	/// Locates the Nexus mod id inside an archive name that has already had its extension removed.
	///
	/// <para>
	/// Three naming shapes are in circulation, all of them present in a long-standing downloads folder, and the id
	/// sits in a different place in each. Every rule reads the id <b>positionally</b> rather than looking for the
	/// digits anywhere in the name: a mod id of 2 or 7 would otherwise match the version digits of a hundred
	/// unrelated archives.
	/// </para>
	/// </summary>
	private static ArchiveTail? FindModId(string name)
	{
		// An opaque content-server id carries no mod id — nothing to read, and nothing worth guessing at.
		if (IsOpaque(name)) return null;

		// Newest Nexus shape, space separated and dated: "<name> <modId> <version> <date> <hash>". The date is
		// rigid enough to anchor from, so counting back from the end lands on the id exactly. A single-digit id
		// counts: a young game's mods are numbered from 1, and Moonlight Peaks' are 7, 11, 33.
		Match dated = Regex.Match(name,
			@"\s(?<id>[1-9]\d{0,8})\s\S+\s\d{4}-\d{2}-\d{2}T\d{2}-\d{2}Z(?:\s+\S+)?$");
		if (dated.Success) return new ArchiveTail(dated.Groups["id"].Value, dated.Index);

		// "<name>_<modId>_file_<fileId>" — the shape the manager asks for when it downloads one named file from a
		// mod's Files tab, rather than whichever file Nexus offers by default.
		Match byFile = Regex.Match(name, @"_(?<id>[1-9]\d{0,8})_file_\d+$");
		if (byFile.Success) return new ArchiveTail(byFile.Groups["id"].Value, byFile.Index);

		// The long-standing shape: "<name>-<modId>-<version parts>[-<upload time>]". Each numeric part is tested
		// in turn against three questions, and all three have to answer yes.
		string[] parts = name.Split('-');
		int at = 0;
		for (int i = 1; i < parts.Length - 1; i++)
		{
			at += parts[i - 1].Length + 1;                    // the '-' immediately before parts[i]

			// Could it be an id at all, and is everything behind it Nexus's own tail rather than more name?
			if (!IsPlausibleModId(parts[i])) continue;
			if (!LooksLikeNexusTail(parts, i + 1)) continue;

			// Is there a name in front of it? A file called nothing but numbers has no place where its name ends,
			// so nothing in it is safe to read as an id.
			if (!parts.Take(i).Any(p => p.Any(char.IsLetter))) continue;

			// And is this the id, or still part of the name? An id sitting directly after the name is the id.
			// One sitting behind further numbers is only believable when it is too large to be a version piece:
			// authors put their own dashed versions in file names, so "UIExtensions v1-2-0-17561-1-2-0" offers a
			// 2 and a 0 before reaching the real mod id, while "Some Mod-0-1-2" is a version all the way down and
			// must be left alone entirely.
			bool followsTheName = parts[i - 1].Any(char.IsLetter);
			bool tooBigForAVersion = parts[i].Length >= 5;
			if (!followsTheName && !tooBigForAVersion) continue;

			return new ArchiveTail(parts[i], at - 1);
		}

		return null;
	}

	/// <summary>
	/// Whether everything from <paramref name="from"/> onwards is the tail Nexus appends after a mod id: version
	/// pieces, which are short numbers or words like <c>6a</c> or <c>unofficial</c>, optionally ending in the
	/// nine-to-eleven digit upload time. A long number anywhere else means the real mod id is still ahead.
	/// </summary>
	private static bool LooksLikeNexusTail(string[] parts, int from)
	{
		if (from >= parts.Length) return false;   // an id with nothing after it is a name ending in a number

		for (int i = from; i < parts.Length; i++)
		{
			if (!IsAllDigits(parts[i])) continue;              // "6a", "unofficial" — version pieces, fine
			if (parts[i].Length <= 4) continue;                // an ordinary version number
			bool last = i == parts.Length - 1;
			if (!(last && parts[i].Length is >= 9 and <= 11)) return false;
		}
		return true;
	}

	/// <summary>True for a part that is nothing but digits — "47327", not "47327 " and not "1a".</summary>
	private static bool IsAllDigits(string part) =>
		part.Length > 0 && part.All(char.IsDigit);

	/// <summary>
	/// Whether a part could be a Nexus mod id at all: digits only, and not starting with a zero. Ids are counted
	/// from 1 per game and never padded, so "0" and "007" are version pieces wearing an id's clothes — the guard
	/// that keeps a folder called "SMAPI 4-0-2" from being read as mod 0 and cut down to "SMAPI 4".
	/// </summary>
	private static bool IsPlausibleModId(string part) =>
		part.Length is > 0 and <= 9 && part[0] != '0' && part.All(char.IsDigit);
}
