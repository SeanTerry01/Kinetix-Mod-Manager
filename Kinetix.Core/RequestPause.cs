using System.Globalization;
using System.Net;

namespace KinetixModManager;

/// <summary>
/// A site has told the manager to stop asking for a while. Until then, requests for a background check are not sent.
/// Sending them anyway fails every one, and can get an account's key blocked.
/// </summary>
public sealed class RequestPause
{
	/// <summary>Used when a site says to slow down without saying for how long.</summary>
	public static readonly TimeSpan WhenUnsaid = TimeSpan.FromMinutes(5);

	/// <summary>The longest a pause is believed, so a nonsense date can't stop checks for good.</summary>
	public static readonly TimeSpan Longest = TimeSpan.FromHours(1);

	private long _untilTicks;

	/// <summary>Whether requests should be held back at this moment.</summary>
	public bool IsActive(DateTime nowUtc) => Interlocked.Read(ref _untilTicks) > nowUtc.Ticks;

	/// <summary>Holds requests back for this long.</summary>
	public void For(TimeSpan length, DateTime nowUtc)
	{
		if (length <= TimeSpan.Zero) length = WhenUnsaid;
		if (length > Longest) length = Longest;
		Interlocked.Exchange(ref _untilTicks, (nowUtc + length).Ticks);
	}

	/// <summary>
	/// How long a response says to wait: its <c>Retry-After</c> header, then the named header holding a date or Unix
	/// time for when the limit resets. <see cref="TimeSpan.Zero"/> when it says neither.
	/// </summary>
	public static TimeSpan LengthFrom(HttpResponseMessage response, string? resetHeader, DateTime nowUtc)
	{
		if (response.Headers.RetryAfter is { } retry)
		{
			if (retry.Delta is { } delta) return delta;
			if (retry.Date is { } date) return date.UtcDateTime - nowUtc;
		}

		if (resetHeader != null && response.Headers.TryGetValues(resetHeader, out var values))
		{
			foreach (string value in values)
			{
				if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unix))
					return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime - nowUtc;
				if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at))
					return at.UtcDateTime - nowUtc;
			}
		}
		return TimeSpan.Zero;
	}

	/// <summary>Whether this response is a site's way of saying "too many requests".</summary>
	public static bool IsTooMany(HttpResponseMessage response) => response.StatusCode == HttpStatusCode.TooManyRequests;
}
