using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KinetixModManager;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Tests here swap out <see cref="MinecraftAuth"/>'s HTTP client, clock and delay, and the process-wide
/// <see cref="Secrets"/> store — all static — so they must not run beside anything else.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MinecraftAuthCollection
{
	public const string Name = "Minecraft sign-in (static state)";
}

/// <summary>
/// Signing in to Minecraft, against a fake Microsoft that answers the way the real one did for
/// <c>tools\mcauth-probe.py</c>.
///
/// ⚠️ The tests that matter most are the ones asserting a token does NOT appear somewhere. The probe promised
/// never to print one, and that promise had only ever been exercised on the failure path — the first success
/// wrote a live token to disk. So every path here, success included, is checked for leaks.
/// </summary>
[Collection(MinecraftAuthCollection.Name)]
public sealed class MinecraftAuthTests : IDisposable
{
	private const string MsAccess = "MS-ACCESS-SECRET-1111";
	private const string MsRefresh = "MS-REFRESH-SECRET-2222";
	private const string MsRefreshRotated = "MS-REFRESH-SECRET-ROTATED-3333";
	private const string XblToken = "XBL-SECRET-4444";
	private const string XstsToken = "XSTS-SECRET-5555";
	private const string McToken = "MC-ACCESS-SECRET-6666";
	private const string DeviceCode = "DEVICE-CODE-SECRET-7777";
	private const string RawUuid = "7b05bd6329944bcb97b179772ffcc404";
	private const string DashedUuid = "7b05bd63-2994-4bcb-97b1-79772ffcc404";

	private static readonly string[] AllSecrets =
		{ MsAccess, MsRefresh, MsRefreshRotated, XblToken, XstsToken, McToken, DeviceCode };

	private readonly FakeMicrosoft _fake = new();
	private readonly ISecretStore _secretsBefore = Secrets.Current;
	private DateTime _now = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
	private readonly List<TimeSpan> _delays = new();

	public MinecraftAuthTests()
	{
		MinecraftAuth.HttpOverride = new HttpClient(_fake);
		MinecraftAuth.UtcNow = () => _now;
		MinecraftAuth.Delay = (span, cancel) =>
		{
			cancel.ThrowIfCancellationRequested();
			_delays.Add(span);
			_now += span;
			return Task.CompletedTask;
		};
		Secrets.Current = new MarkingSecretStore();
	}

	public void Dispose()
	{
		MinecraftAuth.HttpOverride = null;
		MinecraftAuth.UtcNow = () => DateTime.UtcNow;
		MinecraftAuth.Delay = Task.Delay;
		Secrets.Current = _secretsBefore;
	}

	// -------------------------------------------------------------------------
	// The whole chain
	// -------------------------------------------------------------------------

	[Fact]
	public async Task ASignInWaitsForThePlayerThenWalksTheWholeChain()
	{
		_fake.On("/devicecode", HttpStatusCode.OK, DeviceCodeBody());
		_fake.On("/token", HttpStatusCode.BadRequest, new JObject { ["error"] = "authorization_pending" });
		_fake.On("/token", HttpStatusCode.BadRequest, new JObject { ["error"] = "slow_down" });
		_fake.On("/token", HttpStatusCode.OK, MicrosoftTokenBody(MsRefresh));
		QueueXboxAndMinecraft();

		MinecraftDeviceCode code = await MinecraftAuth.RequestDeviceCodeAsync();
		Assert.Equal("ABCD1234", code.UserCode);
		Assert.Equal("https://www.microsoft.com/link", code.VerificationUri);

		MinecraftSession session = await MinecraftAuth.WaitForSignInAsync(code);

		Assert.Equal("SeanPlays", session.Username);
		Assert.Equal(DashedUuid, session.Uuid);
		Assert.Equal(McToken, session.AccessToken);
		Assert.Equal(MsRefresh, session.RefreshToken);

		// slow_down means five more seconds on every poll after it, not just the next one.
		Assert.Equal(new[] { 5, 5, 10 }, _delays.Select(d => (int)d.TotalSeconds));

		// Each step hands the previous step's token on, in the shape the service expects.
		Assert.Contains("d=" + MsAccess, _fake.BodyOf("/user/authenticate"));
		Assert.Contains(XblToken, _fake.BodyOf("/xsts/authorize"));
		Assert.Contains($"XBL3.0 x=HASH;{XstsToken}", _fake.BodyOf("/login_with_xbox"));
		Assert.Equal("Bearer " + McToken, _fake.AuthorizationOf("/minecraft/profile"));
		Assert.Contains("client_id=" + MinecraftAuth.ClientId, _fake.BodyOf("/devicecode"));
		Assert.Contains("offline_access", Uri.UnescapeDataString(_fake.BodyOf("/devicecode").Replace('+', ' ')));
	}

	[Fact]
	public async Task TheSessionExpiresALittleBeforeMinecraftSaysItDoes()
	{
		_fake.On("/token", HttpStatusCode.OK, MicrosoftTokenBody(MsRefresh));
		QueueXboxAndMinecraft();

		MinecraftSession session = await MinecraftAuth.WaitForSignInAsync(Code());

		// Polled once (5 s), then the token's 86,400 s, less the five-minute margin.
		DateTime expected = new DateTime(2026, 9, 17, 12, 0, 5, DateTimeKind.Utc).AddSeconds(86400).AddMinutes(-5);
		Assert.Equal(expected, session.AccessTokenExpiresUtc);
	}

	// -------------------------------------------------------------------------
	// Every way it can fail, and what each is called
	// -------------------------------------------------------------------------

	[Fact]
	public async Task DecliningOnTheMicrosoftPageIsReportedAsDeclined()
	{
		_fake.On("/token", HttpStatusCode.BadRequest, new JObject { ["error"] = "authorization_declined" });

		var ex = await Assert.ThrowsAsync<MinecraftAuthException>(() => MinecraftAuth.WaitForSignInAsync(Code()));
		Assert.Equal(MinecraftAuthFailure.Declined, ex.Failure);
	}

	[Fact]
	public async Task ACodeThatRunsOutIsReportedAsExpiredWithoutAskingMicrosoftAgain()
	{
		MinecraftDeviceCode code = Code(expiresInSeconds: 3);

		var ex = await Assert.ThrowsAsync<MinecraftAuthException>(() => MinecraftAuth.WaitForSignInAsync(code));
		Assert.Equal(MinecraftAuthFailure.CodeExpired, ex.Failure);
		Assert.Empty(_fake.Requests);
	}

	[Fact]
	public async Task CancellingWhileWaitingIsReportedAsCancelled()
	{
		using var cancel = new CancellationTokenSource();
		cancel.Cancel();

		var ex = await Assert.ThrowsAsync<MinecraftAuthException>(() => MinecraftAuth.WaitForSignInAsync(Code(), cancel.Token));
		Assert.Equal(MinecraftAuthFailure.Cancelled, ex.Failure);
	}

	[Theory]
	[InlineData(2148916233L, MinecraftAuthFailure.NoXboxAccount)]
	[InlineData(2148916235L, MinecraftAuthFailure.RegionBlocked)]
	[InlineData(2148916236L, MinecraftAuthFailure.NeedsAgeVerification)]
	[InlineData(2148916237L, MinecraftAuthFailure.NeedsAgeVerification)]
	[InlineData(2148916238L, MinecraftAuthFailure.ChildAccount)]
	[InlineData(2148916227L, MinecraftAuthFailure.Unexpected)]
	public async Task XboxRefusingTheAccountSaysWhy(long xerr, MinecraftAuthFailure expected)
	{
		_fake.On("/token", HttpStatusCode.OK, MicrosoftTokenBody(MsRefresh));
		_fake.On("/user/authenticate", HttpStatusCode.OK, XboxBody());
		_fake.On("/xsts/authorize", HttpStatusCode.Unauthorized, new JObject { ["XErr"] = xerr, ["Message"] = "" });

		var ex = await Assert.ThrowsAsync<MinecraftAuthException>(() => MinecraftAuth.WaitForSignInAsync(Code()));
		Assert.Equal(expected, ex.Failure);
		Assert.Contains(xerr.ToString(), ex.Message);
	}

	[Fact]
	public async Task MinecraftRefusingTheAppItselfIsNotBlamedOnThePlayer()
	{
		_fake.On("/token", HttpStatusCode.OK, MicrosoftTokenBody(MsRefresh));
		_fake.On("/user/authenticate", HttpStatusCode.OK, XboxBody());
		_fake.On("/xsts/authorize", HttpStatusCode.OK, XstsBody());
		_fake.On("/login_with_xbox", HttpStatusCode.Forbidden, new JObject { ["errorMessage"] = "Invalid app registration" });

		var ex = await Assert.ThrowsAsync<MinecraftAuthException>(() => MinecraftAuth.WaitForSignInAsync(Code()));
		Assert.Equal(MinecraftAuthFailure.AppNotAllowed, ex.Failure);
	}

	[Fact]
	public async Task AnAccountWithNoJavaProfileIsSaidToHaveNone()
	{
		_fake.On("/token", HttpStatusCode.OK, MicrosoftTokenBody(MsRefresh));
		_fake.On("/user/authenticate", HttpStatusCode.OK, XboxBody());
		_fake.On("/xsts/authorize", HttpStatusCode.OK, XstsBody());
		_fake.On("/login_with_xbox", HttpStatusCode.OK, MinecraftTokenBody());
		_fake.On("/minecraft/profile", HttpStatusCode.NotFound, new JObject { ["error"] = "NOT_FOUND" });

		var ex = await Assert.ThrowsAsync<MinecraftAuthException>(() => MinecraftAuth.WaitForSignInAsync(Code()));
		Assert.Equal(MinecraftAuthFailure.NoJavaProfile, ex.Failure);
	}

	[Fact]
	public async Task NoNetworkIsReportedAsNoNetwork()
	{
		_fake.Throw = new HttpRequestException("No such host is known.");

		var ex = await Assert.ThrowsAsync<MinecraftAuthException>(() => MinecraftAuth.RequestDeviceCodeAsync());
		Assert.Equal(MinecraftAuthFailure.NetworkFailed, ex.Failure);
	}

	[Fact]
	public async Task ARefreshMicrosoftNoLongerAcceptsMeansSigningInAgain()
	{
		_fake.On("/token", HttpStatusCode.BadRequest, new JObject { ["error"] = "invalid_grant" });

		var ex = await Assert.ThrowsAsync<MinecraftAuthException>(() => MinecraftAuth.RefreshAsync(MsRefresh));
		Assert.Equal(MinecraftAuthFailure.SignInAgain, ex.Failure);
		Assert.Contains("grant_type=refresh_token", _fake.BodyOf("/token"));
	}

	[Fact]
	public async Task RefreshingWithNothingStoredMeansSigningInAgainWithoutAskingMicrosoft()
	{
		var ex = await Assert.ThrowsAsync<MinecraftAuthException>(() => MinecraftAuth.RefreshAsync(""));
		Assert.Equal(MinecraftAuthFailure.SignInAgain, ex.Failure);
		Assert.Empty(_fake.Requests);
	}

	// -------------------------------------------------------------------------
	// ⚠️ Never a token in a message
	// -------------------------------------------------------------------------

	/// <summary>
	/// A service that answers with something unexpected, with credentials in the body — the shape of the probe's
	/// leak. The message must still carry the step and status, and nothing from the body but the error code.
	/// </summary>
	[Fact]
	public async Task AnUnexpectedAnswerIsDescribedWithoutQuotingItsBody()
	{
		_fake.On("/token", HttpStatusCode.OK, MicrosoftTokenBody(MsRefresh));
		_fake.On("/user/authenticate", HttpStatusCode.OK, XboxBody());
		_fake.On("/xsts/authorize", HttpStatusCode.OK, XstsBody());
		_fake.On("/login_with_xbox", HttpStatusCode.InternalServerError, new JObject
		{
			["error"] = "server_error",
			["error_description"] = "echo of " + XstsToken,
			["access_token"] = McToken
		});

		var ex = await Assert.ThrowsAsync<MinecraftAuthException>(() => MinecraftAuth.WaitForSignInAsync(Code()));

		Assert.Equal(MinecraftAuthFailure.Unexpected, ex.Failure);
		Assert.Contains("500", ex.Message);
		Assert.Contains("server_error", ex.Message);
		AssertNoSecretIn(ex.ToString());
	}

	[Fact]
	public async Task NoFailureAnywhereInTheChainPutsATokenInItsMessage()
	{
		// Fail at each step in turn, with every body stuffed with secrets.
		string[] steps = { "/user/authenticate", "/xsts/authorize", "/login_with_xbox", "/minecraft/profile" };
		foreach (string failing in steps)
		{
			_fake.Reset();
			_fake.On("/token", HttpStatusCode.OK, MicrosoftTokenBody(MsRefresh));
			foreach (string step in steps)
			{
				if (step == failing)
				{
					_fake.On(step, HttpStatusCode.BadGateway, SecretStuffedBody());
					break;
				}
				_fake.On(step, HttpStatusCode.OK, step switch
				{
					"/user/authenticate" => XboxBody(),
					"/xsts/authorize" => XstsBody(),
					_ => MinecraftTokenBody()
				});
			}

			var ex = await Assert.ThrowsAsync<MinecraftAuthException>(() => MinecraftAuth.WaitForSignInAsync(Code()));
			AssertNoSecretIn(ex.ToString());
		}
	}

	// -------------------------------------------------------------------------
	// The stored account
	// -------------------------------------------------------------------------

	[Fact]
	public void ARememberedAccountKeepsBothTokensEncrypted()
	{
		var settings = new AppSettings();
		MinecraftAccounts.Remember(settings, Session(MsRefresh, expires: _now.AddHours(20)));

		MinecraftAccountRecord stored = settings.MinecraftAccount!;
		Assert.Equal("SeanPlays", stored.Username);
		Assert.Equal(DashedUuid, stored.Uuid);
		Assert.Equal(MarkingSecretStore.Mark + MsRefresh, stored.RefreshTokenEncrypted);
		Assert.Equal(MarkingSecretStore.Mark + McToken, stored.AccessTokenEncrypted);

		// And the file that gets written carries neither in the clear.
		string json = Newtonsoft.Json.JsonConvert.SerializeObject(settings);
		Assert.DoesNotContain("\"" + MsRefresh, json);
		Assert.DoesNotContain("\"" + McToken, json);
	}

	[Fact]
	public async Task AFreshStoredTokenIsUsedWithoutAskingMicrosoft()
	{
		var settings = new AppSettings();
		MinecraftAccounts.Remember(settings, Session(MsRefresh, expires: _now.AddHours(20)));

		(MinecraftIdentity identity, bool refreshed) = await MinecraftAccounts.OnlineAsync(settings);

		Assert.False(refreshed);
		Assert.Empty(_fake.Requests);
		Assert.Equal(McToken, identity.AccessToken);
		Assert.Equal("msa", identity.UserType);
		Assert.Equal(DashedUuid, identity.Uuid);
		Assert.False(identity.IsOffline);
	}

	[Fact]
	public async Task ATokenNearlyOutIsRefreshedAndTheRotatedRefreshTokenStored()
	{
		var settings = new AppSettings();
		MinecraftAccounts.Remember(settings, Session(MsRefresh, expires: _now.AddMinutes(10)));

		_fake.On("/token", HttpStatusCode.OK, MicrosoftTokenBody(MsRefreshRotated));
		QueueXboxAndMinecraft();

		(MinecraftIdentity identity, bool refreshed) = await MinecraftAccounts.OnlineAsync(settings);

		Assert.True(refreshed);
		Assert.Contains("refresh_token=" + MsRefresh, _fake.BodyOf("/token"));
		// ⚠️ The one that matters: Microsoft has retired the old refresh token, so the new one must be kept.
		Assert.Equal(MarkingSecretStore.Mark + MsRefreshRotated, settings.MinecraftAccount!.RefreshTokenEncrypted);
		Assert.Equal(McToken, identity.AccessToken);
	}

	[Fact]
	public async Task PlayingOnlineWithNoAccountMeansSigningIn()
	{
		var ex = await Assert.ThrowsAsync<MinecraftAuthException>(() => MinecraftAccounts.OnlineAsync(new AppSettings()));
		Assert.Equal(MinecraftAuthFailure.SignInAgain, ex.Failure);
	}

	[Fact]
	public void SigningOutAlsoSwitchesOnlinePlayOff()
	{
		var settings = new AppSettings { MinecraftPlayOnline = true };
		MinecraftAccounts.Remember(settings, Session(MsRefresh, expires: _now.AddHours(20)));

		MinecraftAccounts.Forget(settings);

		Assert.Null(settings.MinecraftAccount);
		Assert.False(settings.MinecraftPlayOnline);
	}

	[Fact]
	public void OfflinePlayUsesTheSignedInPlayerEvenWithNoLauncherFiles()
	{
		string root = TempDir();
		try
		{
			var settings = new AppSettings();
			MinecraftAccounts.Remember(settings, Session(MsRefresh, expires: _now));

			MinecraftIdentity identity = MinecraftAccounts.Offline(settings, root)!;

			Assert.Equal("SeanPlays", identity.Username);
			Assert.Equal(DashedUuid, identity.Uuid);
			Assert.True(identity.IsOffline);
			Assert.Equal("legacy", identity.UserType);
		}
		finally { Directory.Delete(root, true); }
	}

	[Fact]
	public void OfflinePlayFallsBackToTheLauncherWhenNobodyIsSignedIn()
	{
		string root = TempDir();
		try
		{
			WriteLauncherAccount(root, "ffffffffffffffffffffffffffffffff", "LauncherName");

			MinecraftIdentity identity = MinecraftAccounts.Offline(new AppSettings(), root)!;

			Assert.Equal("LauncherName", identity.Username);
			Assert.Equal("ffffffff-ffff-ffff-ffff-ffffffffffff", identity.Uuid);
			Assert.Null(MinecraftAccounts.Offline(new AppSettings(), TempDirThatIsDeleted()));
		}
		finally { Directory.Delete(root, true); }
	}

	[Fact]
	public void SigningInAsSomeoneOtherThanTheLauncherPlayerIsNoticed()
	{
		string root = TempDir();
		try
		{
			WriteLauncherAccount(root, "ffffffffffffffffffffffffffffffff", "SomeoneElse");
			Assert.True(MinecraftAccounts.DiffersFromLauncher(Session(MsRefresh, _now), root));

			WriteLauncherAccount(root, RawUuid, "SeanPlays");
			Assert.False(MinecraftAccounts.DiffersFromLauncher(Session(MsRefresh, _now), root));
		}
		finally { Directory.Delete(root, true); }
	}

	[Fact]
	public void SigningInWithNoLauncherFilesIsNotADifference()
	{
		string root = TempDir();
		try { Assert.False(MinecraftAccounts.DiffersFromLauncher(Session(MsRefresh, _now), root)); }
		finally { Directory.Delete(root, true); }
	}

	// -------------------------------------------------------------------------
	// Saying the code
	// -------------------------------------------------------------------------

	[Theory]
	[InlineData("AB1", "Alfa, Bravo, One")]
	[InlineData("x0z-9", "X-ray, Zero, Zulu, Dash, Nine")]
	[InlineData("Q W", "Quebec, Whiskey")]
	public void ACodeIsSpelledOneWordPerCharacter(string code, string expected) =>
		Assert.Equal(expected, PhoneticSpelling.Spell(code));

	[Fact]
	public void ACharacterWithNoWordIsKeptRatherThanDropped() =>
		Assert.Equal("Alfa, ?, Bravo", PhoneticSpelling.Spell("A?B"));

	// -------------------------------------------------------------------------
	// Fixtures
	// -------------------------------------------------------------------------

	private static void AssertNoSecretIn(string text)
	{
		foreach (string secret in AllSecrets)
			Assert.DoesNotContain(secret, text);
	}

	private MinecraftDeviceCode Code(int expiresInSeconds = 900) => new()
	{
		UserCode = "ABCD1234",
		DeviceCode = DeviceCode,
		VerificationUri = "https://www.microsoft.com/link",
		IntervalSeconds = 5,
		ExpiresUtc = _now.AddSeconds(expiresInSeconds)
	};

	private static MinecraftSession Session(string refresh, DateTime expires) => new()
	{
		Username = "SeanPlays",
		Uuid = DashedUuid,
		AccessToken = McToken,
		AccessTokenExpiresUtc = expires,
		RefreshToken = refresh
	};

	private void QueueXboxAndMinecraft()
	{
		_fake.On("/user/authenticate", HttpStatusCode.OK, XboxBody());
		_fake.On("/xsts/authorize", HttpStatusCode.OK, XstsBody());
		_fake.On("/login_with_xbox", HttpStatusCode.OK, MinecraftTokenBody());
		_fake.On("/minecraft/profile", HttpStatusCode.OK, new JObject { ["id"] = RawUuid, ["name"] = "SeanPlays" });
	}

	private static JObject DeviceCodeBody() => new()
	{
		["user_code"] = "ABCD1234",
		["device_code"] = DeviceCode,
		["verification_uri"] = "https://www.microsoft.com/link",
		["expires_in"] = 900,
		["interval"] = 5,
		["message"] = "To sign in, use a web browser..."
	};

	private static JObject MicrosoftTokenBody(string refresh) => new()
	{
		["token_type"] = "Bearer",
		["access_token"] = MsAccess,
		["refresh_token"] = refresh,
		["expires_in"] = 3600
	};

	private static JObject XboxBody() => new()
	{
		["Token"] = XblToken,
		["DisplayClaims"] = new JObject { ["xui"] = new JArray(new JObject { ["uhs"] = "HASH" }) }
	};

	private static JObject XstsBody() => new()
	{
		["Token"] = XstsToken,
		["DisplayClaims"] = new JObject { ["xui"] = new JArray(new JObject { ["uhs"] = "HASH" }) }
	};

	private static JObject MinecraftTokenBody() => new()
	{
		["access_token"] = McToken,
		["token_type"] = "Bearer",
		["expires_in"] = 86400
	};

	private static JObject SecretStuffedBody() => new()
	{
		["error"] = "bad_gateway",
		["error_description"] = string.Join(" ", AllSecrets),
		["Token"] = XblToken,
		["access_token"] = McToken,
		["refresh_token"] = MsRefresh
	};

	private static string TempDir()
	{
		string dir = Path.Combine(Path.GetTempPath(), "kmm-mcauth-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	private static string TempDirThatIsDeleted()
	{
		string dir = TempDir();
		Directory.Delete(dir);
		return dir;
	}

	private static void WriteLauncherAccount(string root, string rawUuid, string name)
	{
		var doc = new JObject
		{
			["activeAccountLocalId"] = "local1",
			["accounts"] = new JObject
			{
				["local1"] = new JObject
				{
					["accessToken"] = "",
					["minecraftProfile"] = new JObject { ["id"] = rawUuid, ["name"] = name }
				}
			}
		};
		File.WriteAllText(Path.Combine(root, "launcher_accounts_microsoft_store.json"), doc.ToString());
	}

	/// <summary>"Encrypts" by prefixing, so a test can tell a protected value from a plain one.</summary>
	private sealed class MarkingSecretStore : ISecretStore
	{
		public const string Mark = "PROTECTED:";
		public string Protect(string plainText) => Mark + plainText;
		public string Unprotect(string cipherText) =>
			cipherText.StartsWith(Mark, StringComparison.Ordinal) ? cipherText.Substring(Mark.Length) : cipherText;
	}

	/// <summary>
	/// Answers each URL path from its own queue, in order, and remembers what was sent. A request nobody queued
	/// an answer for fails the test loudly rather than hanging.
	/// </summary>
	private sealed class FakeMicrosoft : HttpMessageHandler
	{
		private readonly Dictionary<string, Queue<(HttpStatusCode, JObject)>> _answers = new();

		public List<(string Path, string Body, string Authorization)> Requests { get; } = new();

		public Exception? Throw { get; set; }

		public void On(string pathEnding, HttpStatusCode status, JObject body)
		{
			if (!_answers.TryGetValue(pathEnding, out var queue))
				_answers[pathEnding] = queue = new Queue<(HttpStatusCode, JObject)>();
			queue.Enqueue((status, body));
		}

		public void Reset()
		{
			_answers.Clear();
			Requests.Clear();
		}

		public string BodyOf(string pathEnding) =>
			Requests.First(r => r.Path.EndsWith(pathEnding, StringComparison.Ordinal)).Body;

		public string AuthorizationOf(string pathEnding) =>
			Requests.First(r => r.Path.EndsWith(pathEnding, StringComparison.Ordinal)).Authorization;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
		{
			if (Throw is not null) throw Throw;

			string path = request.RequestUri!.AbsolutePath;
			string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancel);
			Requests.Add((path, body, request.Headers.Authorization?.ToString() ?? ""));

			string? key = _answers.Keys.FirstOrDefault(k => path.EndsWith(k, StringComparison.Ordinal));
			if (key is null || _answers[key].Count == 0)
				throw new InvalidOperationException($"The test did not expect a request to {path}.");

			(HttpStatusCode status, JObject answer) = _answers[key].Dequeue();
			return new HttpResponseMessage(status)
			{
				Content = new StringContent(answer.ToString(), Encoding.UTF8, "application/json")
			};
		}
	}
}
