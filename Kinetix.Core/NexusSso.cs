using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// Signing in to Nexus Mods without the user ever typing an API key.
///
/// <para>
/// Today the manager asks for a Personal API Key, which means leaving the program, finding the right page
/// on nexusmods.com, locating a long random string among the page furniture, and copying it back. That is
/// a poor experience for anyone and a genuinely hostile one by ear. Nexus's Single Sign-On exists exactly
/// so applications do not have to ask: the user approves the application on a Nexus page, and the key
/// arrives over a websocket.
/// </para>
///
/// <para>
/// The flow, which this implements:
/// connect to <c>wss://sso.nexusmods.com</c> and send <c>{ id, token, protocol: 2 }</c> where <c>id</c> is
/// a UUID this application made up; the server answers with a <c>connection_token</c> worth keeping for
/// reconnects; the user is sent to <c>nexusmods.com/sso?id=…&amp;application=…</c> to approve; and the API
/// key then arrives on the same socket. A ping every thirty seconds keeps it open while the user reads.
/// </para>
///
/// <para>
/// <b>Note for whoever finishes this:</b> the manager has to be registered with Nexus before SSO will work
/// at all — only approved applications may use it, and approval is what supplies the
/// <c>application</c> slug. That is a conversation with their community managers, not a code change.
/// </para>
///
/// <para>
/// <b>What must not be done:</b> the login page is the user's own session with Nexus. Whether it is shown
/// in the system browser or inside the manager's own web view, the program must never read, store or
/// intercept what they type into it. The entire point of this flow is that an application receives a
/// revocable key and never sees a password.
/// </para>
/// </summary>
public static class NexusSso
{
	/// <summary>The SSO websocket endpoint.</summary>
	public const string SocketUrl = "wss://sso.nexusmods.com";

	/// <summary>The protocol version this speaks. Version 2 is the one that carries a connection token.</summary>
	public const int ProtocolVersion = 2;

	/// <summary>Keeps the socket alive while the user is reading the approval page.</summary>
	public static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30);

	/// <summary>
	/// The message that opens a session. <paramref name="connectionToken"/> is null the first time and the
	/// token from a previous session afterwards, which is what lets an interrupted sign-in resume rather
	/// than asking the user to approve all over again.
	/// </summary>
	public static string ConnectMessage(string requestId, string? connectionToken) =>
		new JObject
		{
			["id"] = requestId,
			["token"] = connectionToken is null ? JValue.CreateNull() : new JValue(connectionToken),
			["protocol"] = ProtocolVersion
		}.ToString(Newtonsoft.Json.Formatting.None);

	/// <summary>Where the user approves the application. Opened in the manager's own web view.</summary>
	public static string ApprovalUrl(string requestId, string applicationSlug) =>
		$"https://www.nexusmods.com/sso?id={Uri.EscapeDataString(requestId)}" +
		$"&application={Uri.EscapeDataString(applicationSlug)}";

	/// <summary>What arrived on the socket: a connection token, an API key, an error, or nothing useful.</summary>
	public readonly record struct Reply(string? ConnectionToken, string? ApiKey, string? Error)
	{
		public bool HasApiKey => !string.IsNullOrEmpty(ApiKey);
	}

	/// <summary>
	/// Reads one server message.
	///
	/// Kept separate from the socket so the shapes can be tested without a network, which matters here
	/// more than usual: this cannot be exercised end to end until Nexus has approved the application, so
	/// the parsing had better be right on its own.
	/// </summary>
	public static Reply ParseReply(string json)
	{
		if (string.IsNullOrWhiteSpace(json)) return new Reply(null, null, null);

		try
		{
			JObject message = JObject.Parse(json);

			// An explicit failure is worth surfacing as itself rather than as silence; the usual cause is a
			// user who declined, and telling them so is better than a sign-in that simply never finishes.
			string? error = (string?)message["error"];
			if ((bool?)message["success"] == false && string.IsNullOrEmpty(error))
				error = "Nexus refused the sign-in request.";

			// "data" is a JSON null on every failure reply, and indexing a null JValue throws rather than
			// giving null back - which turned a perfectly clear "Request denied" into an unexpected-message
			// error, hiding the one thing the user needed told. Hence the type test rather than ?[].
			var data = message["data"] as JObject;
			return new Reply(
				ConnectionToken: (string?)data?["connection_token"],
				ApiKey: (string?)data?["api_key"],
				Error: string.IsNullOrEmpty(error) ? null : error);
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Nexus", "reading an SSO message", ex);
			return new Reply(null, null, "The sign-in service sent something unexpected.");
		}
	}

	/// <summary>A fresh request id. One per sign-in attempt.</summary>
	public static string NewRequestId() => Guid.NewGuid().ToString();

	/// <summary>
	/// Runs a sign-in to completion and returns the API key.
	///
	/// <paramref name="showApprovalPage"/> is handed the URL the user has to approve at, and is where the
	/// caller puts it in front of them — in the manager's own web view, which is the point of the feature.
	/// </summary>
	public static async Task<string> SignInAsync(
		string applicationSlug,
		Action<string> showApprovalPage,
		string? connectionToken = null,
		CancellationToken cancellation = default)
	{
		string requestId = NewRequestId();

		using var socket = new ClientWebSocket();
		await socket.ConnectAsync(new Uri(SocketUrl), cancellation);
		await SendAsync(socket, ConnectMessage(requestId, connectionToken), cancellation);

		using var pings = new CancellationTokenSource();
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, pings.Token);
		Task pinging = PingAsync(socket, linked.Token);

		try
		{
			bool sentToApprove = false;
			var buffer = new byte[8192];

			while (!cancellation.IsCancellationRequested)
			{
				WebSocketReceiveResult received = await socket.ReceiveAsync(buffer, cancellation);
				if (received.MessageType == WebSocketMessageType.Close)
					throw new InvalidOperationException("Nexus closed the sign-in connection.");

				Reply reply = ParseReply(Encoding.UTF8.GetString(buffer, 0, received.Count));
				if (reply.Error is not null) throw new InvalidOperationException(reply.Error);
				if (reply.HasApiKey) return reply.ApiKey!;

				// The approval page is only worth opening once the server has acknowledged the request;
				// opening it first races the connection and can show the user an id the server never saw.
				if (!sentToApprove && reply.ConnectionToken is not null)
				{
					sentToApprove = true;
					showApprovalPage(ApprovalUrl(requestId, applicationSlug));
				}
			}
		}
		finally
		{
			pings.Cancel();
			try { await pinging; } catch { /* the ping loop ending is how it is meant to stop */ }
		}

		throw new OperationCanceledException(cancellation);
	}

	private static Task SendAsync(ClientWebSocket socket, string message, CancellationToken cancellation) =>
		socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, cancellation);

	private static async Task PingAsync(ClientWebSocket socket, CancellationToken cancellation)
	{
		while (!cancellation.IsCancellationRequested && socket.State == WebSocketState.Open)
		{
			try { await Task.Delay(PingInterval, cancellation); }
			catch (OperationCanceledException) { return; }

			if (socket.State != WebSocketState.Open) return;
			try { await SendAsync(socket, "ping", cancellation); }
			catch (Exception ex) { DiagnosticLog.WriteException("Nexus", "keeping the SSO connection open", ex); return; }
		}
	}
}
