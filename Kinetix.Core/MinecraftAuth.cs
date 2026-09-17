using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>A sign-in code Microsoft has issued, waiting for the player to type it in a browser.</summary>
public sealed class MinecraftDeviceCode
{
	/// <summary>What the player types — short, letters and digits. Safe to show and say; it is useless alone.</summary>
	public required string UserCode { get; init; }

	/// <summary>
	/// What the manager polls with. ⚠️ A credential for as long as the code is live — whoever holds it receives
	/// the tokens when the player finishes signing in. Never shown, never logged.
	/// </summary>
	public required string DeviceCode { get; init; }

	/// <summary>Where the code is typed. In practice <c>https://www.microsoft.com/link</c>.</summary>
	public required string VerificationUri { get; init; }

	public required int IntervalSeconds { get; init; }

	public required DateTime ExpiresUtc { get; init; }
}

/// <summary>A signed-in Minecraft session: who the player is, and the credentials that prove it.</summary>
public sealed class MinecraftSession
{
	public required string Username { get; init; }

	/// <summary>Dashed uuid.</summary>
	public required string Uuid { get; init; }

	/// <summary>The Minecraft access token the game is started with. Lasts about a day.</summary>
	public required string AccessToken { get; init; }

	public required DateTime AccessTokenExpiresUtc { get; init; }

	/// <summary>
	/// The Microsoft refresh token that gets the next access token without asking the player again.
	///
	/// ⚠️ Microsoft ROTATES it: every refresh hands back a new one, and the one just used should be considered
	/// spent. Whatever stores a session must store this value every time, not only on first sign-in.
	/// </summary>
	public required string RefreshToken { get; init; }
}

/// <summary>Why a sign-in or refresh did not produce a session. The UI turns each into a sentence.</summary>
public enum MinecraftAuthFailure
{
	/// <summary>Microsoft, Xbox or Minecraft could not be reached at all.</summary>
	NetworkFailed,
	/// <summary>The player said no on the Microsoft page.</summary>
	Declined,
	/// <summary>The code was not used in time.</summary>
	CodeExpired,
	/// <summary>The stored sign-in is no longer accepted — the player has to sign in again.</summary>
	SignInAgain,
	/// <summary>The Microsoft account has no Xbox profile yet. Creating one at xbox.com fixes it.</summary>
	NoXboxAccount,
	/// <summary>A child account that an adult has to add to a Microsoft family first.</summary>
	ChildAccount,
	/// <summary>Xbox Live is not available in the account's country.</summary>
	RegionBlocked,
	/// <summary>The account needs adult verification before Xbox Live will sign it in (South Korea).</summary>
	NeedsAgeVerification,
	/// <summary>Signed in fine, but the account owns no Minecraft Java Edition, or has never picked a player name.</summary>
	NoJavaProfile,
	/// <summary>
	/// Minecraft's own service refused the manager itself. The app's permission to use the Minecraft API has
	/// been withdrawn, or the Azure registration behind it has broken. Not something the player can fix.
	/// </summary>
	AppNotAllowed,
	/// <summary>The player cancelled while the manager was waiting.</summary>
	Cancelled,
	/// <summary>Anything else. The message carries the step and status — never a body, never a token.</summary>
	Unexpected
}

/// <summary>
/// A sign-in that did not work. <see cref="Exception.Message"/> names the step and the HTTP status for the
/// diagnostic log; it is built from those alone and never from a response body, so it cannot carry a token.
/// </summary>
public sealed class MinecraftAuthException : Exception
{
	public MinecraftAuthFailure Failure { get; }

	public MinecraftAuthException(MinecraftAuthFailure failure, string message, Exception? inner = null)
		: base(message, inner)
	{
		Failure = failure;
	}
}

/// <summary>
/// Signing in to Minecraft with a Microsoft account: device code, then Xbox Live, then XSTS, then Minecraft.
///
/// <para>
/// The device-code flow is the accessible one, and that is why it was chosen over the browser-redirect flow
/// every other launcher uses. The manager is given a short code to say; the player types it into
/// microsoft.com/link in their own browser, where their screen reader already works; nothing has to be
/// embedded, and no password ever passes through the manager.
/// </para>
///
/// <para>
/// The chain was proven end to end by <c>tools\mcauth-probe.py</c> before any of this was written, and the
/// constants below are the ones that worked. ⚠️ The probe also taught the rule this file lives by: its promise
/// never to print a token had only ever run on the failure path, and the first success wrote one to disk. So
/// nothing here puts a response body into an exception, a log line or a message — only step names, HTTP
/// statuses and Microsoft's own error codes.
/// </para>
/// </summary>
public static class MinecraftAuth
{
	/// <summary>The Azure app registration. Public by design: a device-code client has no secret.</summary>
	public const string ClientId = "dfae7b53-ed9a-4573-acb6-02928d7224ac";

	/// <summary>⚠️ <c>consumers</c>, not <c>common</c>. Personal Microsoft accounts only, and <c>common</c> fails.</summary>
	public const string Tenant = "consumers";

	/// <summary><c>offline_access</c> is what makes Microsoft hand back a refresh token at all.</summary>
	public const string Scope = "XboxLive.signin offline_access";

	private static readonly string MicrosoftBase = $"https://login.microsoftonline.com/{Tenant}/oauth2/v2.0";

	/// <summary>
	/// Set by the tests so they can answer for Microsoft; nothing else should set it.
	///
	/// ⚠️ An override rather than a settable property initialised to <see cref="KinetixHttp.Api"/>: an
	/// initialiser would build that client the first time this class is touched, which can be before startup
	/// has set the User-Agent. See <see cref="KinetixHttp"/>.
	/// </summary>
	public static HttpClient? HttpOverride { get; set; }

	private static HttpClient Http => HttpOverride ?? KinetixHttp.Api;

	/// <summary>How the poll waits between attempts. Replaceable for the same reason.</summary>
	public static Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

	/// <summary>How the flow tells the time. Replaceable so the tests can expire a code without waiting.</summary>
	public static Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

	// -------------------------------------------------------------------------
	// Step 1: the code the player types
	// -------------------------------------------------------------------------

	public static async Task<MinecraftDeviceCode> RequestDeviceCodeAsync(CancellationToken cancel = default)
	{
		(HttpStatusCode status, JObject? body) = await PostFormAsync($"{MicrosoftBase}/devicecode",
			new Dictionary<string, string> { ["client_id"] = ClientId, ["scope"] = Scope },
			"requesting a sign-in code", cancel).ConfigureAwait(false);

		if (status != HttpStatusCode.OK || body is null)
			throw Unexpected("requesting a sign-in code", status, body);

		string? userCode = (string?)body["user_code"];
		string? deviceCode = (string?)body["device_code"];
		string? uri = (string?)body["verification_uri"];
		if (string.IsNullOrEmpty(userCode) || string.IsNullOrEmpty(deviceCode) || string.IsNullOrEmpty(uri))
			throw new MinecraftAuthException(MinecraftAuthFailure.Unexpected,
				"Microsoft's sign-in code response was missing a field.");

		return new MinecraftDeviceCode
		{
			UserCode = userCode!,
			DeviceCode = deviceCode!,
			VerificationUri = uri!,
			IntervalSeconds = Math.Max(1, (int?)body["interval"] ?? 5),
			ExpiresUtc = UtcNow().AddSeconds((int?)body["expires_in"] ?? 900)
		};
	}

	// -------------------------------------------------------------------------
	// Step 2: waiting for the player, then the rest of the chain
	// -------------------------------------------------------------------------

	/// <summary>
	/// Waits for the player to finish on the Microsoft page, then completes the whole chain to a Minecraft
	/// session. Throws <see cref="MinecraftAuthException"/> for every way that can end other than success,
	/// cancellation included.
	/// </summary>
	public static async Task<MinecraftSession> WaitForSignInAsync(MinecraftDeviceCode code, CancellationToken cancel = default)
	{
		int interval = code.IntervalSeconds;

		while (true)
		{
			try { await Delay(TimeSpan.FromSeconds(interval), cancel).ConfigureAwait(false); }
			catch (OperationCanceledException ex)
			{
				throw new MinecraftAuthException(MinecraftAuthFailure.Cancelled, "Sign-in was cancelled.", ex);
			}

			if (UtcNow() >= code.ExpiresUtc)
				throw new MinecraftAuthException(MinecraftAuthFailure.CodeExpired, "The sign-in code expired unused.");

			(HttpStatusCode status, JObject? body) = await PostFormAsync($"{MicrosoftBase}/token",
				new Dictionary<string, string>
				{
					["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
					["client_id"] = ClientId,
					["device_code"] = code.DeviceCode
				},
				"waiting for the Microsoft sign-in", cancel).ConfigureAwait(false);

			if (status == HttpStatusCode.OK && body is not null)
				return await CompleteAsync(MicrosoftTokens(body, "waiting for the Microsoft sign-in"), cancel).ConfigureAwait(false);

			switch ((string?)body?["error"])
			{
				case "authorization_pending":
					continue;
				case "slow_down":
					interval += 5;
					continue;
				case "authorization_declined":
					throw new MinecraftAuthException(MinecraftAuthFailure.Declined, "The player declined the Microsoft sign-in.");
				case "expired_token":
					throw new MinecraftAuthException(MinecraftAuthFailure.CodeExpired, "The sign-in code expired unused.");
				default:
					throw Unexpected("waiting for the Microsoft sign-in", status, body);
			}
		}
	}

	/// <summary>
	/// Gets a fresh session from a stored refresh token, with no player involvement. The session returned
	/// carries a NEW refresh token that must replace the stored one.
	/// </summary>
	public static async Task<MinecraftSession> RefreshAsync(string refreshToken, CancellationToken cancel = default)
	{
		if (string.IsNullOrEmpty(refreshToken))
			throw new MinecraftAuthException(MinecraftAuthFailure.SignInAgain, "There is no stored sign-in to refresh.");

		(HttpStatusCode status, JObject? body) = await PostFormAsync($"{MicrosoftBase}/token",
			new Dictionary<string, string>
			{
				["grant_type"] = "refresh_token",
				["client_id"] = ClientId,
				["refresh_token"] = refreshToken,
				["scope"] = Scope
			},
			"refreshing the Microsoft sign-in", cancel).ConfigureAwait(false);

		if (status != HttpStatusCode.OK || body is null)
		{
			// invalid_grant is Microsoft's answer to a refresh token that has expired, been revoked, or had its
			// password changed underneath it. All of them mean the same thing to the player.
			if ((string?)body?["error"] == "invalid_grant")
				throw new MinecraftAuthException(MinecraftAuthFailure.SignInAgain,
					"Microsoft no longer accepts the stored sign-in (invalid_grant).");
			throw Unexpected("refreshing the Microsoft sign-in", status, body);
		}

		return await CompleteAsync(MicrosoftTokens(body, "refreshing the Microsoft sign-in"), cancel).ConfigureAwait(false);
	}

	private static (string Access, string Refresh) MicrosoftTokens(JObject body, string step)
	{
		string? access = (string?)body["access_token"];
		string? refresh = (string?)body["refresh_token"];
		if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh))
			throw new MinecraftAuthException(MinecraftAuthFailure.Unexpected,
				$"Microsoft's answer while {step} had no {(string.IsNullOrEmpty(access) ? "access" : "refresh")} token.");
		return (access!, refresh!);
	}

	/// <summary>Microsoft token to Xbox Live to XSTS to Minecraft to the player's profile.</summary>
	private static async Task<MinecraftSession> CompleteAsync((string Access, string Refresh) microsoft, CancellationToken cancel)
	{
		// --- Xbox Live ---
		(HttpStatusCode status, JObject? xbl) = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate",
			new JObject
			{
				["Properties"] = new JObject
				{
					["AuthMethod"] = "RPS",
					["SiteName"] = "user.auth.xboxlive.com",
					["RpsTicket"] = "d=" + microsoft.Access
				},
				["RelyingParty"] = "http://auth.xboxlive.com",
				["TokenType"] = "JWT"
			}, "signing in to Xbox Live", cancel).ConfigureAwait(false);

		string? xblToken = (string?)xbl?["Token"];
		string? userHash = (string?)xbl?["DisplayClaims"]?["xui"]?[0]?["uhs"];
		if (status != HttpStatusCode.OK || string.IsNullOrEmpty(xblToken) || string.IsNullOrEmpty(userHash))
			throw Unexpected("signing in to Xbox Live", status, xbl);

		// --- XSTS ---
		(status, JObject? xsts) = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize",
			new JObject
			{
				["Properties"] = new JObject
				{
					["SandboxId"] = "RETAIL",
					["UserTokens"] = new JArray(xblToken)
				},
				["RelyingParty"] = "rp://api.minecraftservices.com/",
				["TokenType"] = "JWT"
			}, "getting Xbox permission for Minecraft", cancel).ConfigureAwait(false);

		if (status == HttpStatusCode.Unauthorized)
			throw XstsFailure((long?)xsts?["XErr"] ?? 0);

		string? xstsToken = (string?)xsts?["Token"];
		if (status != HttpStatusCode.OK || string.IsNullOrEmpty(xstsToken))
			throw Unexpected("getting Xbox permission for Minecraft", status, xsts);

		// --- Minecraft ---
		(status, JObject? mc) = await PostJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox",
			new JObject { ["identityToken"] = $"XBL3.0 x={userHash};{xstsToken}" },
			"signing in to Minecraft", cancel).ConfigureAwait(false);

		// The one status the probe spent five days collecting. It means Minecraft's service does not recognise the
		// manager as an approved app — which, now that it is approved, means that approval has gone away.
		if (status == HttpStatusCode.Forbidden)
			throw new MinecraftAuthException(MinecraftAuthFailure.AppNotAllowed,
				"Minecraft's service refused the app itself (HTTP 403 from login_with_xbox).");

		string? mcToken = (string?)mc?["access_token"];
		if (status != HttpStatusCode.OK || string.IsNullOrEmpty(mcToken))
			throw Unexpected("signing in to Minecraft", status, mc);

		// A little short of what Minecraft says, so a token is never handed to a game in its last minutes.
		int lifetime = (int?)mc?["expires_in"] ?? 86400;
		DateTime expires = UtcNow().AddSeconds(lifetime).AddMinutes(-5);

		// --- Who the player is ---
		(status, JObject? profile) = await GetJsonAsync("https://api.minecraftservices.com/minecraft/profile",
			mcToken!, "reading the Minecraft profile", cancel).ConfigureAwait(false);

		// 404 is the answer for an account with no Java Edition profile: it does not own the game, or it does
		// and has never been through the launcher's "choose a name" step.
		if (status == HttpStatusCode.NotFound)
			throw new MinecraftAuthException(MinecraftAuthFailure.NoJavaProfile,
				"The Microsoft account has no Minecraft Java Edition profile (HTTP 404).");

		string? id = (string?)profile?["id"];
		string? name = (string?)profile?["name"];
		if (status != HttpStatusCode.OK || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
			throw Unexpected("reading the Minecraft profile", status, profile);

		return new MinecraftSession
		{
			Username = name!,
			Uuid = MinecraftIdentity.FormatUuid(id!),
			AccessToken = mcToken!,
			AccessTokenExpiresUtc = expires,
			RefreshToken = microsoft.Refresh
		};
	}

	/// <summary>
	/// XSTS refuses an account for reasons the player CAN act on, and names them only by number. These are the
	/// documented ones; anything else is reported with its number so it can be looked up.
	/// </summary>
	public static MinecraftAuthException XstsFailure(long xerr) => xerr switch
	{
		2148916233 => new(MinecraftAuthFailure.NoXboxAccount, "XSTS: the account has no Xbox profile (2148916233)."),
		2148916235 => new(MinecraftAuthFailure.RegionBlocked, "XSTS: Xbox Live is unavailable in the account's country (2148916235)."),
		2148916236 or 2148916237 => new(MinecraftAuthFailure.NeedsAgeVerification, $"XSTS: the account needs adult verification ({xerr})."),
		2148916238 => new(MinecraftAuthFailure.ChildAccount, "XSTS: a child account must be added to a family (2148916238)."),
		_ => new(MinecraftAuthFailure.Unexpected, $"XSTS refused the account (XErr {xerr}).")
	};

	// -------------------------------------------------------------------------
	// HTTP. Every answer comes back as a status and a body; nothing here throws for a status code.
	// -------------------------------------------------------------------------

	private static Task<(HttpStatusCode, JObject?)> PostFormAsync(
		string url, Dictionary<string, string> form, string step, CancellationToken cancel)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
		return SendAsync(request, step, cancel);
	}

	private static Task<(HttpStatusCode, JObject?)> PostJsonAsync(
		string url, JObject body, string step, CancellationToken cancel)
	{
		var request = new HttpRequestMessage(HttpMethod.Post, url)
		{
			Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json")
		};
		return SendAsync(request, step, cancel);
	}

	private static Task<(HttpStatusCode, JObject?)> GetJsonAsync(
		string url, string bearer, string step, CancellationToken cancel)
	{
		var request = new HttpRequestMessage(HttpMethod.Get, url);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
		return SendAsync(request, step, cancel);
	}

	private static async Task<(HttpStatusCode, JObject?)> SendAsync(
		HttpRequestMessage request, string step, CancellationToken cancel)
	{
		using (request)
		{
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			try
			{
				using HttpResponseMessage response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
				string text = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

				JObject? body = null;
				if (!string.IsNullOrWhiteSpace(text))
				{
					try { body = JObject.Parse(text); }
					catch (JsonException) { /* not JSON — the status is what gets reported */ }
				}
				return (response.StatusCode, body);
			}
			catch (OperationCanceledException ex) when (cancel.IsCancellationRequested)
			{
				throw new MinecraftAuthException(MinecraftAuthFailure.Cancelled, $"Cancelled while {step}.", ex);
			}
			catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.IO.IOException)
			{
				// The inner exception is a network error; it holds no request body, so it is safe to keep.
				throw new MinecraftAuthException(MinecraftAuthFailure.NetworkFailed,
					$"Could not reach the sign-in service while {step}: {ex.Message}", ex);
			}
		}
	}

	/// <summary>
	/// A failure described by step, status and Microsoft's error CODE — the short identifier, never the
	/// description beside it, and never anything else from the body.
	/// </summary>
	private static MinecraftAuthException Unexpected(string step, HttpStatusCode status, JObject? body)
	{
		string? code = (string?)body?["error"];
		string detail = string.IsNullOrEmpty(code) || code!.Length > 64 ? "" : $", error '{code}'";
		return new MinecraftAuthException(MinecraftAuthFailure.Unexpected,
			$"Unexpected answer while {step}: HTTP {(int)status}{detail}.");
	}
}

/// <summary>
/// The spoken form of a sign-in code: one word per character, so "B" and "D" and "3" can never be confused.
/// Sean found a code readable this way and not otherwise.
/// </summary>
public static class PhoneticSpelling
{
	private static readonly Dictionary<char, string> Words = new()
	{
		['A'] = "Alfa", ['B'] = "Bravo", ['C'] = "Charlie", ['D'] = "Delta", ['E'] = "Echo", ['F'] = "Foxtrot",
		['G'] = "Golf", ['H'] = "Hotel", ['I'] = "India", ['J'] = "Juliett", ['K'] = "Kilo", ['L'] = "Lima",
		['M'] = "Mike", ['N'] = "November", ['O'] = "Oscar", ['P'] = "Papa", ['Q'] = "Quebec", ['R'] = "Romeo",
		['S'] = "Sierra", ['T'] = "Tango", ['U'] = "Uniform", ['V'] = "Victor", ['W'] = "Whiskey",
		['X'] = "X-ray", ['Y'] = "Yankee", ['Z'] = "Zulu",
		['0'] = "Zero", ['1'] = "One", ['2'] = "Two", ['3'] = "Three", ['4'] = "Four", ['5'] = "Five",
		['6'] = "Six", ['7'] = "Seven", ['8'] = "Eight", ['9'] = "Nine", ['-'] = "Dash"
	};

	/// <summary>
	/// "AB1" becomes "Alfa, Bravo, One". Commas, so a screen reader pauses between characters. Anything not in
	/// the table is passed through as itself rather than dropped — a dropped character is a wrong code.
	/// </summary>
	public static string Spell(string text) =>
		string.Join(", ", text.Where(c => !char.IsWhiteSpace(c))
			.Select(c => Words.TryGetValue(char.ToUpperInvariant(c), out string? word) ? word : c.ToString()));
}
