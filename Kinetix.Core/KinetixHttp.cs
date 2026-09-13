using System;
using System.Net;
using System.Net.Http;

namespace KinetixModManager;

/// <summary>
/// The HTTP clients everything shares, held open for the life of the process.
///
/// <para>
/// They are shared because the alternative was leaking sockets. A dozen call sites used to write
/// <c>using var client = new HttpClient(...)</c>, which reads like careful resource handling and is very
/// nearly the opposite: disposing an <see cref="HttpClient"/> disposes the handler underneath it, and the
/// TCP connection that handler was pooling goes into <c>TIME_WAIT</c> rather than back into the pool. A
/// single update check that touches forty mods therefore burned forty connections that the operating
/// system then held for a couple of minutes each. On a large mod list, run a few times in a session, that
/// reaches the ephemeral port limit — and the symptom is not an error anyone would connect to the cause:
/// update checks simply start failing to connect, for a while, and then start working again.
/// </para>
///
/// <para>
/// The rule .NET actually wants is the one <c>NexusService.HttpClient</c> already followed: one client,
/// kept. <see cref="SocketsHttpHandler.PooledConnectionLifetime"/> is what makes keeping it safe — it
/// retires pooled connections periodically so a long-running process still notices DNS changes, which is
/// the one real argument against a static client.
/// </para>
///
/// <para>
/// There are two rather than one because the timeouts genuinely differ, and a timeout is per-client:
/// an API call that has not answered in a minute has failed, while a mod download may legitimately run
/// for half an hour. Anything needing per-request headers — a Nexus API key — should build an
/// <see cref="HttpRequestMessage"/> and send it on one of these, never mutate
/// <see cref="HttpClient.DefaultRequestHeaders"/>, which on a shared client would leak that header onto
/// every other caller's requests.
/// </para>
/// </summary>
public static class KinetixHttp
{
	/// <summary>
	/// What the manager calls itself to a server, set once at startup by the app — which is the only part
	/// that knows the build number, since this assembly has a version of its own and it is not the app's.
	///
	/// It carries the project's address because Modrinth asks for a User-Agent it can identify and contact,
	/// and is entitled to one; a request that turns up anonymous is one they are within their rights to
	/// throttle. The default below is therefore descriptive rather than a placeholder: if startup never gets
	/// as far as setting this, an under-versioned but honest User-Agent is a great deal better than
	/// <c>KinetixModManager</c> on its own.
	/// </summary>
	public static string UserAgent { get; set; } =
		"KinetixModManager (github.com/SeanTerry01/Kinetix-Mod-Manager)";

	private static readonly Lazy<HttpClient> _api = new(() => Build(TimeSpan.FromSeconds(60)));
	private static readonly Lazy<HttpClient> _downloads = new(() => Build(TimeSpan.FromMinutes(30)));

	/// <summary>For API calls and small files: a minute is long enough for any of them to have failed.</summary>
	public static HttpClient Api => _api.Value;

	/// <summary>
	/// For mod downloads, which are large and are allowed to take their time. Note that this timeout covers
	/// getting the response, not reading its body — callers streaming a download still need their own stall
	/// watchdog, as <c>NexusService.DownloadFileWithProgressAsync</c> has.
	/// </summary>
	public static HttpClient Downloads => _downloads.Value;

	/// <summary>
	/// Built lazily so the User-Agent set during startup is the one that gets used. Reading it at field
	/// initialisation instead would capture the placeholder, since type initialisation happens on first
	/// touch and that can easily come first.
	/// </summary>
	private static HttpClient Build(TimeSpan timeout)
	{
		var handler = new SocketsHttpHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
			// Long enough to be worth pooling, short enough that a machine that changed network, or a service
			// that moved, is not talking to a dead address for the rest of the session.
			PooledConnectionLifetime = TimeSpan.FromMinutes(5)
		};

		var client = new HttpClient(handler) { Timeout = timeout };
		client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
		return client;
	}
}
