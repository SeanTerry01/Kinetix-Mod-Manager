using System.Net;
using System.Net.Http;
using Xunit;

namespace KinetixModManager.Tests;

public class UpdateCheckQuotaTests
{
	private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

	private static NexusCheckRecord Record(string latest = "1.2", int daysAgo = 1) =>
		new() { Latest = latest, CheckedUtc = Now.AddDays(-daysAgo) };

	[Fact]
	public void APageNotOnTheListKeepsWhatItLastSaid()
	{
		var updated = new HashSet<int> { 7 };
		Assert.Equal("1.2", NexusCheckMemory.RememberedLatest(Record(), "42", updated, Now));
	}

	[Fact]
	public void APageOnTheListIsAskedAgain()
	{
		var updated = new HashSet<int> { 42 };
		Assert.Null(NexusCheckMemory.RememberedLatest(Record(), "42", updated, Now));
	}

	[Fact]
	public void NoListOrAnEmptyOneMeansAsking()
	{
		Assert.Null(NexusCheckMemory.RememberedLatest(Record(), "42", null, Now));
		Assert.Null(NexusCheckMemory.RememberedLatest(Record(), "42", new HashSet<int>(), Now));
	}

	[Fact]
	public void APageNeverCheckedIsAsked()
	{
		Assert.Null(NexusCheckMemory.RememberedLatest(null, "42", new HashSet<int> { 7 }, Now));
	}

	[Theory]
	[InlineData(24, true)]
	[InlineData(26, false)]
	[InlineData(-1, false)] // A clock moved back can't make an old answer look new.
	public void AnAnswerIsOnlyTrustedWhileTheListStillCoversIt(int daysAgo, bool trusted)
	{
		string? got = NexusCheckMemory.RememberedLatest(Record(daysAgo: daysAgo), "42", new HashSet<int> { 7 }, Now);
		Assert.Equal(trusted, got != null);
	}

	[Fact]
	public void TheMonthTheListCoversIsLongerThanTheTrust()
	{
		// Nexus's "1m" period is a calendar month, so the shortest it can be is 28 days.
		Assert.True(NexusCheckMemory.Trusted < TimeSpan.FromDays(28));
	}

	[Fact]
	public void APauseLastsAsLongAsItWasToldTo()
	{
		var pause = new RequestPause();
		Assert.False(pause.IsActive(Now));

		pause.For(TimeSpan.FromMinutes(10), Now);

		Assert.True(pause.IsActive(Now.AddMinutes(9)));
		Assert.False(pause.IsActive(Now.AddMinutes(11)));
	}

	[Fact]
	public void AnUnsaidPauseIsShortAndANonsenseOneIsCapped()
	{
		var unsaid = new RequestPause();
		unsaid.For(TimeSpan.Zero, Now);
		Assert.True(unsaid.IsActive(Now.AddMinutes(1)));
		Assert.False(unsaid.IsActive(Now + RequestPause.WhenUnsaid));

		var nonsense = new RequestPause();
		nonsense.For(TimeSpan.FromDays(400), Now);
		Assert.False(nonsense.IsActive(Now + RequestPause.Longest));
	}

	[Fact]
	public void RetryAfterIsBelievedFirst()
	{
		using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
		response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
		response.Headers.TryAddWithoutValidation("x-rl-hourly-reset", "2026-11-03 13:00:00 +0000");

		Assert.Equal(TimeSpan.FromSeconds(90), RequestPause.LengthFrom(response, "x-rl-hourly-reset", Now));
	}

	[Theory]
	[InlineData("2026-11-03 12:30:00 +0000")] // How Nexus writes it.
	[InlineData("1793709000")]                // How GitHub writes it: Unix seconds for 12:30.
	public void AResetHeaderIsReadInEitherForm(string value)
	{
		using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
		response.Headers.TryAddWithoutValidation("x-reset", value);

		Assert.Equal(TimeSpan.FromMinutes(30), RequestPause.LengthFrom(response, "x-reset", Now));
	}

	[Fact]
	public void AResponseSayingNothingGivesNoLength()
	{
		using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
		response.Headers.TryAddWithoutValidation("x-reset", "whenever");
		Assert.Equal(TimeSpan.Zero, RequestPause.LengthFrom(response, "x-reset", Now));
	}

	[Theory]
	[InlineData(HttpStatusCode.TooManyRequests, true)]
	[InlineData(HttpStatusCode.Forbidden, false)]
	[InlineData(HttpStatusCode.OK, false)]
	public void OnlyTooManyRequestsCountsByStatusAlone(HttpStatusCode status, bool tooMany)
	{
		using var response = new HttpResponseMessage(status);
		Assert.Equal(tooMany, RequestPause.IsTooMany(response));
	}
}
