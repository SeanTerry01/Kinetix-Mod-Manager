using System;
using KinetixModManager;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers <see cref="NexusSso"/>, the sign-in that saves the user hunting an API key out of a web page by
/// ear and pasting it back.
///
/// The message shapes are tested rather than the socket, and deliberately: the flow cannot be run end to
/// end until Nexus has approved the application and issued a slug, so the parts that can be checked now
/// had better be checked now. Every shape here comes from Nexus's own SSO integration demo.
/// </summary>
public class NexusSsoTests
{
	[Fact]
	public void TheOpeningMessageCarriesTheIdAndAsksForProtocolTwo()
	{
		JObject sent = JObject.Parse(NexusSso.ConnectMessage("abc-123", connectionToken: null));

		Assert.Equal("abc-123", (string?)sent["id"]);
		Assert.Equal(2, (int?)sent["protocol"]);
		// Null rather than absent: the server distinguishes a first connection from a resumed one.
		Assert.Equal(JTokenType.Null, sent["token"]!.Type);
	}

	[Fact]
	public void AKnownTokenIsSentBackSoAnInterruptedSignInCanResume()
	{
		JObject sent = JObject.Parse(NexusSso.ConnectMessage("abc-123", "tok-789"));

		Assert.Equal("tok-789", (string?)sent["token"]);
	}

	[Fact]
	public void TheApprovalUrlNamesBoththeRequestAndTheApplication()
	{
		string url = NexusSso.ApprovalUrl("abc-123", "kinetix");

		Assert.StartsWith("https://www.nexusmods.com/sso?", url);
		Assert.Contains("id=abc-123", url);
		Assert.Contains("application=kinetix", url);
	}

	[Fact]
	public void AnIdNeedingEscapingIsEscaped()
	{
		string url = NexusSso.ApprovalUrl("a b&c", "my app");

		Assert.DoesNotContain("a b&c", url);
		Assert.Contains("application=my%20app", url);
	}

	[Fact]
	public void TheConnectionTokenIsReadFromTheFirstReply()
	{
		var reply = NexusSso.ParseReply("""{"success":true,"data":{"connection_token":"tok-789"},"error":null}""");

		Assert.Equal("tok-789", reply.ConnectionToken);
		Assert.False(reply.HasApiKey);
		Assert.Null(reply.Error);
	}

	[Fact]
	public void TheApiKeyIsReadFromTheReplyThatCarriesIt()
	{
		var reply = NexusSso.ParseReply("""{"success":true,"data":{"api_key":"KEY-ABC"},"error":null}""");

		Assert.True(reply.HasApiKey);
		Assert.Equal("KEY-ABC", reply.ApiKey);
	}

	[Fact]
	public void AnErrorComesBackAsOneRatherThanAsSilence()
	{
		// Usually a user who declined. A sign-in that simply never finishes tells them nothing at all.
		var reply = NexusSso.ParseReply("""{"success":false,"data":null,"error":"Request denied"}""");

		Assert.Equal("Request denied", reply.Error);
		Assert.False(reply.HasApiKey);
	}

	[Fact]
	public void AFailureWithNoMessageStillReportsSomethingSayable()
	{
		var reply = NexusSso.ParseReply("""{"success":false,"data":null,"error":null}""");

		Assert.False(string.IsNullOrWhiteSpace(reply.Error));
	}

	[Fact]
	public void RubbishOnTheSocketIsReportedRatherThanThrown()
	{
		// This is read from a network in the middle of a sign-in the user is watching. Throwing here would
		// surface as a crash during login rather than as "that did not work".
		var reply = NexusSso.ParseReply("not json at all");

		Assert.False(reply.HasApiKey);
		Assert.False(string.IsNullOrWhiteSpace(reply.Error));
	}

	[Fact]
	public void AnEmptyMessageIsSimplyNothingToActOn()
	{
		var reply = NexusSso.ParseReply("");

		Assert.False(reply.HasApiKey);
		Assert.Null(reply.Error);
	}

	[Fact]
	public void EveryRequestIdIsItsOwn()
	{
		Assert.NotEqual(NexusSso.NewRequestId(), NexusSso.NewRequestId());
	}
}
