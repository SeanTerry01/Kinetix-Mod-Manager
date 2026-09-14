using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers the connect and disconnect cues for Minecraft, which are about something different from every
/// other game's.
///
/// <para>
/// For Skyrim or Stardew those cues mean Nexus — signed in to where the mods come from. Minecraft's mods come
/// from Modrinth, which has no accounts at all, so there was nothing for them to say. The connection a
/// Minecraft player cares about is the one to a server, and the client writes both ends of it to its own log.
/// </para>
///
/// <para>
/// The two things worth getting right are both about <em>not</em> playing a cue. Singleplayer must stay
/// silent, because a local world is not a connection to anything. And chat must be ignored: the client logs
/// chat to the same file, so another player's "was disconnected" is a sentence in somebody else's game and
/// would otherwise sound the disconnect cue at a player who is still happily online.
/// </para>
/// </summary>
public class MinecraftServerLogTests
{
	// Shaped like a real log: [HH:mm:ss] [Thread/LEVEL]: message
	private const string Joining   = "[12:34:56] [Render thread/INFO]: Connecting to mc.example.net, 25565";
	private const string Stopping  = "[12:41:02] [Render thread/INFO]: Stopping!";

	// ---------------------------------------------------------------------
	// Reading one line
	// ---------------------------------------------------------------------

	[Fact]
	public void JoiningAServerIsRecognisedAndNamesIt()
	{
		Assert.Equal(MinecraftServerSignal.Joining, MinecraftServerLog.Read(Joining, out string server));
		Assert.Equal("mc.example.net", server);
	}

	[Fact]
	public void AnIpAddressAndPortAreRead()
	{
		Assert.Equal(MinecraftServerSignal.Joining,
			MinecraftServerLog.Read("[09:00:00] [Render thread/INFO]: Connecting to 192.168.1.40, 25566", out string server));
		Assert.Equal("192.168.1.40", server);
	}

	[Fact]
	public void TheGameShuttingDownEndsTheConnection()
	{
		Assert.Equal(MinecraftServerSignal.Left, MinecraftServerLog.Read(Stopping, out _));
	}

	[Theory]
	[InlineData("[12:35:00] [Render thread/INFO]: Lost connection: Timed out")]
	[InlineData("[12:35:00] [Render thread/WARN]: Disconnected from the server")]
	[InlineData("[12:35:00] [Render thread/INFO]: Failed to connect to the server")]
	public void AConnectionThatEndedBadlyStillEnds(string line)
	{
		Assert.Equal(MinecraftServerSignal.Left, MinecraftServerLog.Read(line, out _));
	}

	[Fact]
	public void SingleplayerSaysNothing()
	{
		// A local world logs its own server starting and never "Connecting to". A player who only plays alone
		// should hear neither cue rather than one that means nothing.
		Assert.Equal(MinecraftServerSignal.None, MinecraftServerLog.Read(
			"[12:30:00] [Server thread/INFO]: Starting integrated minecraft server version 26.2", out _));
		Assert.Equal(MinecraftServerSignal.None, MinecraftServerLog.Read(
			"[12:40:00] [Server thread/INFO]: Stopping server", out _));
	}

	[Fact]
	public void ChatIsNotAConnection()
	{
		// Somebody else leaving is an event in their game, not this player's.
		Assert.Equal(MinecraftServerSignal.None, MinecraftServerLog.Read(
			"[12:36:11] [Render thread/INFO]: [CHAT] Steve was disconnected", out _));
		Assert.Equal(MinecraftServerSignal.None, MinecraftServerLog.Read(
			"[12:36:12] [Render thread/INFO]: [CHAT] <Alex> Connecting to my base, 5 minutes", out _));
	}

	[Fact]
	public void OrdinaryStartupChatterIsIgnored()
	{
		foreach (string line in new[]
		{
			"[00:24:49] [main/INFO]: Loading Minecraft 26.2 with Fabric Loader 0.19.5",
			"[00:24:52] [Render thread/INFO]: Setting user: SeanTerry01",
			"[00:24:52] [Render thread/INFO]: Backend library: LWJGL version 3.4.1+2",
			"",
		})
		{
			Assert.Equal(MinecraftServerSignal.None, MinecraftServerLog.Read(line, out _));
		}
	}

	// ---------------------------------------------------------------------
	// Following a whole session
	// ---------------------------------------------------------------------

	[Fact]
	public void JoinThenQuitIsOneCueEachWay()
	{
		var session = new MinecraftServerSession();

		Assert.Equal(new[] { MinecraftServerChange.Connected }, session.Read(Joining));
		Assert.True(session.IsConnected);
		Assert.Equal("mc.example.net", session.Server);

		Assert.Equal(new[] { MinecraftServerChange.Disconnected }, session.Read(Stopping));
		Assert.False(session.IsConnected);
	}

	[Fact]
	public void LeavingWithoutHavingJoinedSaysNothing()
	{
		// Quitting out of a singleplayer world reaches "Stopping!" like any other exit. Only a player who
		// joined a server can leave one.
		var session = new MinecraftServerSession();

		Assert.Empty(session.Read(Stopping));
		Assert.Null(session.GameEnded());
	}

	[Fact]
	public void QuittingStraightOutOfAServerStillDisconnects()
	{
		// The case the log cannot be relied on for: the process goes away and whether the client got a line
		// out first is not something to depend on.
		var session = new MinecraftServerSession();
		session.Read(Joining);

		Assert.Equal(MinecraftServerChange.Disconnected, session.GameEnded());
		Assert.Null(session.GameEnded());   // and not twice
	}

	[Fact]
	public void HoppingStraightToAnotherServerIsALeaveAndAJoin()
	{
		// Two connect cues in a row would give the player no way to hear that the first server was left.
		var session = new MinecraftServerSession();
		session.Read(Joining);

		MinecraftServerChange[] changes = session.Read(
			"[12:38:00] [Render thread/INFO]: Connecting to other.example.net, 25565");

		Assert.Equal(new[] { MinecraftServerChange.Disconnected, MinecraftServerChange.Connected }, changes);
		Assert.True(session.IsConnected);
		Assert.Equal("other.example.net", session.Server);
	}

	[Fact]
	public void RejoiningAfterADropWorks()
	{
		var session = new MinecraftServerSession();
		session.Read(Joining);
		session.Read("[12:35:00] [Render thread/INFO]: Lost connection: Timed out");

		Assert.Equal(new[] { MinecraftServerChange.Connected }, session.Read(Joining));
	}

	// ---------------------------------------------------------------------
	// Following the file it all comes out of
	// ---------------------------------------------------------------------

	[Fact]
	public void TheTailStartsAtTheEndOfTheLastRunsLog()
	{
		// The previous run's log is still on disk when the game is started, and it very likely ends with the
		// player joining a server. Reading it from the beginning would announce a connection to a server they
		// left yesterday.
		using var log = new TempLog();
		log.Append(Joining);

		var tail = new MinecraftLogTail(log.Path);

		Assert.Empty(tail.ReadNewLines());
	}

	[Fact]
	public void TheTailPicksUpTheNewRunAfterTheGameRollsTheLogOver()
	{
		using var log = new TempLog();
		log.Append("[08:00:00] [Render thread/INFO]: yesterday's run, several lines long, now irrelevant");
		var tail = new MinecraftLogTail(log.Path);

		log.Truncate();                 // what the game does as it opens the file
		log.Append(Joining);

		Assert.Equal(new[] { Joining }, tail.ReadNewLines());
	}

	[Fact]
	public void AHalfWrittenLineWaitsForTheRestOfItself()
	{
		// The game is appending as this reads. Handing the parser half a message means it never sees the
		// other half.
		using var log = new TempLog();
		var tail = new MinecraftLogTail(log.Path);

		log.AppendRaw("[12:34:56] [Render thread/INFO]: Connecting to mc.exa");
		Assert.Empty(tail.ReadNewLines());

		log.AppendRaw("mple.net, 25565\n");
		Assert.Equal(new[] { Joining }, tail.ReadNewLines());
	}

	[Fact]
	public void WindowsLineEndingsAreNotPartOfTheMessage()
	{
		using var log = new TempLog();
		var tail = new MinecraftLogTail(log.Path);
		log.AppendRaw(Joining + "\r\n");

		Assert.Equal(new[] { Joining }, tail.ReadNewLines());
	}

	[Fact]
	public void ALogThatIsNotThereYetIsNotAFailure()
	{
		string missing = Path.Combine(Path.GetTempPath(), "kinetix-no-log-" + Guid.NewGuid().ToString("N"), "latest.log");

		Assert.Empty(new MinecraftLogTail(missing).ReadNewLines());
	}

	[Fact]
	public async Task FollowingStopsWhenTheGameDoes()
	{
		using var log = new TempLog();
		var tail = new MinecraftLogTail(log.Path, TimeSpan.FromMilliseconds(10));
		var seen = new List<string>();
		using var stop = new CancellationTokenSource();

		Task following = tail.RunAsync(line => { lock (seen) seen.Add(line); }, stop.Token);
		log.Append(Joining);

		// Long enough for several polls, short enough not to slow the suite down.
		await Task.Delay(200);
		stop.Cancel();
		await following;

		lock (seen) Assert.Contains(Joining, seen);
	}

	/// <summary>A log file the game is pretending to write to.</summary>
	private sealed class TempLog : IDisposable
	{
		private readonly string _dir = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(), "kinetix-mclog-" + Guid.NewGuid().ToString("N"));

		public string Path { get; }

		public TempLog()
		{
			Directory.CreateDirectory(_dir);
			Path = System.IO.Path.Combine(_dir, "latest.log");
			File.WriteAllBytes(Path, Array.Empty<byte>());
		}

		public void Append(string line) => AppendRaw(line + "\n");

		public void AppendRaw(string text)
		{
			using var stream = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
			byte[] bytes = Encoding.UTF8.GetBytes(text);
			stream.Write(bytes, 0, bytes.Length);
		}

		public void Truncate() => File.WriteAllBytes(Path, Array.Empty<byte>());

		public void Dispose()
		{
			try { Directory.Delete(_dir, true); } catch { }
		}
	}
}
