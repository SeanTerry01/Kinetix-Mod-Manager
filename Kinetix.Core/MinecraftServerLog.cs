using System;
using System.Text.RegularExpressions;

namespace KinetixModManager;

/// <summary>What one line of Minecraft's client log says about the player's connection to a server.</summary>
public enum MinecraftServerSignal
{
	/// <summary>Nothing about a server connection.</summary>
	None,

	/// <summary>The player is joining a multiplayer server or a Realm.</summary>
	Joining,

	/// <summary>The connection ended — left, kicked, timed out, or the game is shutting down.</summary>
	Left,
}

/// <summary>
/// Reading the player's connection to a Minecraft server out of the game's own log.
///
/// <para>
/// Minecraft is the odd one out among the supported games, and this is where it shows. For every other game
/// the manager's connect and disconnect cues are about <em>Nexus</em> — signing in to where the mods come
/// from. Minecraft's mods come from Modrinth, which needs no account at all, so those cues had nothing to
/// say. The connection that matters to a Minecraft player is the one to a server, and the client announces
/// both ends of it in <c>logs/latest.log</c>.
/// </para>
///
/// <para>
/// Singleplayer deliberately produces nothing here. A local world logs
/// <c>Starting integrated minecraft server</c> and never <c>Connecting to</c>, so a player who only plays
/// alone hears neither cue rather than hearing one that means nothing.
/// </para>
/// </summary>
public static class MinecraftServerLog
{
	// [12:34:56] [Render thread/INFO]: Connecting to mc.example.net, 25565
	//
	// From ConnectScreen, and it has read the same for many years. Realms comes through the same screen once
	// the address is resolved, so joining a Realm reads as joining a server — which is what it is.
	private static readonly Regex Joining = new(
		@"^Connecting to (?<server>.+?), (?<port>\d{1,5})$", RegexOptions.Compiled);

	// The one certain end: the client is shutting down.
	private const string Stopping = "Stopping!";

	// Best effort, and marked as such. Leaving a server for the menu is not something the client states
	// plainly in every version, so these cover the ways a connection audibly ends — dropped, kicked, timed
	// out — and the game closing or the process exiting is what catches the rest. Adding a pattern here is
	// the whole change needed if a version turns out to say it differently.
	private static readonly Regex Left = new(
		@"^(Lost connection|Connection lost|Disconnected|Client disconnected|Failed to connect)\b",
		RegexOptions.Compiled | RegexOptions.IgnoreCase);

	// [12:34:56] [Render thread/INFO]: <message>
	private static readonly Regex LogLine = new(
		@"^\[[0-9:]+\]\s*\[[^\]]*\]:\s*(?<message>.*)$", RegexOptions.Compiled);

	/// <summary>
	/// What <paramref name="line"/> says, and the server it names when it names one.
	///
	/// Chat is excluded outright. The client writes chat to the same log, so "PlayerName was disconnected"
	/// arriving in chat is an ordinary event in somebody else's game — and matching it would play the
	/// disconnect cue at a player who is still happily connected.
	/// </summary>
	public static MinecraftServerSignal Read(string line, out string server)
	{
		server = "";
		if (string.IsNullOrWhiteSpace(line)) return MinecraftServerSignal.None;

		Match framed = LogLine.Match(line.Trim());
		string message = (framed.Success ? framed.Groups["message"].Value : line).Trim();

		if (message.StartsWith("[CHAT]", StringComparison.OrdinalIgnoreCase)) return MinecraftServerSignal.None;

		Match joining = Joining.Match(message);
		if (joining.Success)
		{
			server = joining.Groups["server"].Value.Trim();
			return MinecraftServerSignal.Joining;
		}

		if (message.Equals(Stopping, StringComparison.Ordinal) || Left.IsMatch(message))
			return MinecraftServerSignal.Left;

		return MinecraftServerSignal.None;
	}
}

/// <summary>Which way a player's connection to a server just went.</summary>
public enum MinecraftServerChange { Connected, Disconnected }

/// <summary>
/// The player's connection to a server, followed across a run of the game.
///
/// <para>
/// A state machine rather than a line filter, because the cues have to make sense as a pair. Only a player
/// who joined a server can leave one — so quitting a singleplayer world says nothing — and hopping straight
/// from one server to another is a leave and a join rather than two joins.
/// </para>
/// </summary>
public sealed class MinecraftServerSession
{
	/// <summary>True while the player is on a server.</summary>
	public bool IsConnected { get; private set; }

	/// <summary>The server the player is on, or <c>""</c>.</summary>
	public string Server { get; private set; } = "";

	/// <summary>
	/// Feeds one log line in and returns every change it caused, in order.
	///
	/// Normally none or one. Two when the player jumped straight from one server to another, which is a
	/// leave and a join and should sound like one — the alternative is two connect cues in a row and no way
	/// to hear that the first server was ever left.
	/// </summary>
	public MinecraftServerChange[] Read(string line)
	{
		MinecraftServerSignal signal = MinecraftServerLog.Read(line, out string server);

		switch (signal)
		{
			case MinecraftServerSignal.Joining when !IsConnected:
				IsConnected = true;
				Server = server;
				return new[] { MinecraftServerChange.Connected };

			// Server hopping: the old connection ended, whether or not the client said so.
			case MinecraftServerSignal.Joining:
				Server = server;
				return new[] { MinecraftServerChange.Disconnected, MinecraftServerChange.Connected };

			case MinecraftServerSignal.Left when IsConnected:
				IsConnected = false;
				Server = "";
				return new[] { MinecraftServerChange.Disconnected };

			default:
				return Array.Empty<MinecraftServerChange>();
		}
	}

	/// <summary>
	/// The game has stopped. Reports a disconnection if the player was still on a server, which is the case
	/// the log cannot be relied on for: quitting from inside a server closes the process, and whether the
	/// client got a line out first is not something to depend on.
	/// </summary>
	public MinecraftServerChange? GameEnded()
	{
		if (!IsConnected) return null;

		IsConnected = false;
		Server = "";
		return MinecraftServerChange.Disconnected;
	}
}
