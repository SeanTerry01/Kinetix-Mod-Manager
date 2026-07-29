using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>
/// Version handling for the SMAPI web API (smapi.io), which drives the Stardew Valley update check.
/// Self-contained (BCL only) so it can be unit tested without the app's runtime.
/// </summary>
public static class SmapiVersion
{
	/// <summary>
	/// Coerces a version string into the form the SMAPI web API accepts (<c>major.minor[.patch][-tag][+build]</c>).
	/// This matters far more than it looks: smapi.io rejects the <b>entire batch</b> — returning an empty array,
	/// with a 200 status — as soon as one entry carries a version it cannot parse. A single mod whose manifest
	/// says "1", "v1.2.3", "1.0.0.0" or nothing at all was therefore enough to silently wipe out the whole
	/// Stardew update check, which is why real updates went missing from the Updates tab. Note "0.0.0" is
	/// rejected too (SMAPI forbids an all-zero version), so it can never be used as a placeholder.
	/// Returns "" when nothing usable can be salvaged; callers send that (the API tolerates an empty version
	/// and still returns the mod's database entry, just no update suggestion).
	/// </summary>
	public static string Sanitize(string? version)
	{
		if (string.IsNullOrWhiteSpace(version)) return "";
		string text = version.Trim();
		if (text.Length > 1 && (text[0] == 'v' || text[0] == 'V') && char.IsDigit(text[1]))
			text = text.Substring(1);

		// Split off the prerelease/build suffix so only the numeric core is normalised.
		int suffixAt = text.IndexOfAny(new[] { '-', '+', ' ' });
		string core = suffixAt >= 0 ? text.Substring(0, suffixAt) : text;
		string suffix = suffixAt >= 0 ? text.Substring(suffixAt) : "";
		if (suffix.StartsWith(" ")) suffix = "";   // trailing prose ("1.2 for SMAPI 4") is not a version tag

		var numbers = new List<int>();
		foreach (string part in core.Split('.'))
		{
			if (!int.TryParse(part.Trim(), out int n) || n < 0) break;
			numbers.Add(n);
			if (numbers.Count == 3) break;         // the API takes at most major.minor.patch
		}
		if (numbers.Count == 0) return "";
		while (numbers.Count < 2) numbers.Add(0);  // "1" -> "1.0"
		if (numbers.All(n => n == 0)) return "";   // an all-zero version is rejected outright

		return string.Join(".", numbers) + suffix;
	}
}
