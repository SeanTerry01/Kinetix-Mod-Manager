namespace KinetixModManager;

/// <summary>What a full check of one Nexus mod page last found, and when.</summary>
public sealed class NexusCheckRecord
{
	public string Latest { get; set; } = "";
	public DateTime CheckedUtc { get; set; }
}

/// <summary>
/// Lets an update check skip the two requests a mod would cost when Nexus's own list says the page has not changed.
///
/// A check costs up to two requests per mod, so 1,000 linked mods could spend most of a day's allowance to learn that
/// almost nothing moved. Nexus publishes, for one request, every mod of a game updated in the last month.
/// </summary>
public static class NexusCheckMemory
{
	/// <summary>
	/// Shorter than the month Nexus's list covers, so a change can never fall between the record and the list.
	/// </summary>
	public static readonly TimeSpan Trusted = TimeSpan.FromDays(25);

	/// <summary>
	/// The version to use without asking Nexus, or null when it has to be asked.
	/// An empty list counts as no list: a wrong "nothing changed" hides updates for weeks, and a full check only costs requests.
	/// </summary>
	public static string? RememberedLatest(NexusCheckRecord? record, string nexusId, IReadOnlySet<int>? updatedThisMonth, DateTime nowUtc)
	{
		if (record == null || updatedThisMonth == null || updatedThisMonth.Count == 0) return null;
		if (!int.TryParse(nexusId, out int id) || updatedThisMonth.Contains(id)) return null;

		TimeSpan age = nowUtc - record.CheckedUtc;
		if (age < TimeSpan.Zero || age > Trusted) return null;
		return string.IsNullOrWhiteSpace(record.Latest) ? null : record.Latest;
	}
}
