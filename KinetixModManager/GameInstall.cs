using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace KinetixModManager;

/// <summary>
/// One installed copy of a game. Someone can own the same game twice — the Steam copy and the GOG one, side by
/// side — and the two are genuinely different installs: different folders, different builds, different per-player
/// data folders, and mods deployed into one are simply not in the other.
///
/// <see cref="Key"/> is what makes them separable. Every per-game dictionary in <see cref="AppSettings"/>, and
/// every folder the manager keeps under <c>%AppData%</c> (the deployment manifest, downloads, backups, safety
/// snapshots, save backups, search history), is keyed by that string — so keying it by the copy rather than by
/// the game is what makes all of them per-copy at once. A game's first copy keeps the bare game id as its key,
/// which is why none of this disturbs the settings file of someone who owns one copy of each game.
/// </summary>
public class GameInstall
{
	/// <summary>The install key: the bare game id for a game's first copy, else <c>"&lt;gameId&gt;@&lt;platform&gt;"</c>.</summary>
	public string Key { get; set; } = "";

	/// <summary>Which game this is a copy of — always a bare id from <see cref="GameProfiles"/>.</summary>
	public string GameId { get; set; } = "";

	/// <summary>The store this copy came from, as far as detection could establish.</summary>
	[JsonConverter(typeof(StringEnumConverter))]
	public GamePlatform Platform { get; set; } = GamePlatform.Unknown;

	/// <summary>Where this copy is installed.</summary>
	public string Folder { get; set; } = "";

	/// <summary>
	/// Whether this copy's mods are staged inside its own game folder rather than under <c>%AppData%</c>.
	///
	/// Only meaningful for the games that stage (Skyrim SE, Fallout 4); the rest keep their mods in the game
	/// folder regardless, because that is where their loader looks. Off for copies the manager already knew
	/// about, so nobody's mods move without being asked.
	/// </summary>
	public bool ModsInGameFolder { get; set; }

	/// <summary>
	/// How this copy is named to the user — "Skyrim Special Edition (GOG)". The suffix is the caller's decision,
	/// because a platform is only worth saying when there is another copy to tell it apart from.
	/// </summary>
	public string DisplayName(bool withPlatform)
	{
		string name = GameProfiles.DisplayNameFor(GameId);
		string platform = GameProfiles.PlatformDisplayName(Platform);
		return withPlatform && platform.Length > 0 ? $"{name} ({platform})" : name;
	}
}
