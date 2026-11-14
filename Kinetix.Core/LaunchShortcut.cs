namespace KinetixModManager;

/// <summary>
/// The desktop shortcut that starts a game without opening the manager (GitHub issue #2, asked for 2026-09-06).
///
/// <para>
/// The shortcut starts the manager itself, with <c>--launch</c> and the game's install key, rather than the game's own
/// loader. A shortcut to skse64_loader.exe already works, and anyone can make one, but it skips everything F5 checks
/// before a game starts: a script extender built for another game version, an Address Library behind the game, BepInEx
/// missing, too little memory, a deployment still being written. Each of those ends in a game that starts and never
/// speaks, and the warning is the only explanation anyone gets. Minecraft can't be started from a file at all: the
/// manager builds its Java command and signs in. So the manager starts out of sight, makes the same checks, starts the
/// game the same way, and closes when the game does. The window only appears when there is a question to answer.
/// </para>
/// </summary>
public static class LaunchShortcut
{
	/// <summary>What the shortcut passes, followed by the install key of the game to start.</summary>
	public const string LaunchSwitch = "--launch";

	/// <summary>
	/// The game a shortcut asks for, or null when the manager was started some other way. The key is checked against the
	/// games the manager knows by the caller; this only reads the command line.
	/// </summary>
	public static string? GameToLaunch(IReadOnlyList<string> args)
	{
		for (int i = 0; i < args.Count - 1; i++)
		{
			if (string.Equals(args[i], LaunchSwitch, StringComparison.OrdinalIgnoreCase))
			{
				string key = args[i + 1].Trim();
				return key.Length == 0 ? null : key;
			}
		}
		return null;
	}

	/// <summary>The arguments a shortcut for <paramref name="installKey"/> carries. Quoted, since a modpack's key can hold spaces.</summary>
	public static string ArgumentsFor(string installKey) => LaunchSwitch + " \"" + installKey.Replace("\"", "") + "\"";

	// ---------------------------------------------------------------------------------------------------------------
	// Messages from a second copy of the manager to the one already running, over the pipe nxm:// links use.
	// ---------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// Starts every message that isn't an nxm:// link. Neither a link nor a game's install key can start with it.
	/// </summary>
	private const string MessagePrefix = "kinetix:";

	private const string ShowMessage = MessagePrefix + "show";
	private const string LaunchMessagePrefix = MessagePrefix + "launch:";

	/// <summary>Sent when the manager is opened while it is already running: the running one comes to the front.</summary>
	public static string ShowRequest => ShowMessage;

	/// <summary>Sent when a shortcut is used while the manager is already running: the running one starts the game.</summary>
	public static string LaunchRequest(string installKey) => LaunchMessagePrefix + installKey;

	/// <summary>True when <paramref name="line"/> came from <see cref="ShowRequest"/>.</summary>
	public static bool IsShowRequest(string line) => string.Equals(line.Trim(), ShowMessage, StringComparison.OrdinalIgnoreCase);

	/// <summary>The game a <see cref="LaunchRequest"/> asks for, or null when <paramref name="line"/> is not one.</summary>
	public static string? LaunchRequested(string line)
	{
		string trimmed = line.Trim();
		if (!trimmed.StartsWith(LaunchMessagePrefix, StringComparison.OrdinalIgnoreCase)) return null;
		string key = trimmed.Substring(LaunchMessagePrefix.Length).Trim();
		return key.Length == 0 ? null : key;
	}

	/// <summary>True for anything this class sends, so the pipe hands only nxm:// links to the download handler.</summary>
	public static bool IsManagerMessage(string line) => line.TrimStart().StartsWith(MessagePrefix, StringComparison.OrdinalIgnoreCase);

	// ---------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// The shortcut's file name for <paramref name="name"/>, such as "Skyrim Special Edition (Kinetix).lnk".
	/// </summary>
	/// <param name="name">What the shortcut is called, without ".lnk": the game's name and the manager's.</param>
	/// <remarks>
	/// The manager's name is part of it on purpose. Steam's own shortcut is called after the game alone, and writing over
	/// it would quietly change what a shortcut the player already relies on does. A character Windows won't allow in a
	/// file name, such as the colon in "The Witcher 3: Wild Hunt" or one in a modpack's name, becomes a space.
	/// </remarks>
	public static string FileNameFor(string name)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		var cleaned = new System.Text.StringBuilder(name.Length);
		foreach (char c in name)
			cleaned.Append(Array.IndexOf(invalid, c) >= 0 || c is ':' or '*' or '?' or '"' or '<' or '>' or '|' or '/' or '\\' ? ' ' : c);

		// Runs of spaces left by what was taken out, and the dots and spaces Windows drops from the end of a name.
		string collapsed = System.Text.RegularExpressions.Regex.Replace(cleaned.ToString(), @"\s+", " ").Trim().TrimEnd('.', ' ');
		if (collapsed.Length == 0) collapsed = "Kinetix";
		return collapsed + ".lnk";
	}
}
