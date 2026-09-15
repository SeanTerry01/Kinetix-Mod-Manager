using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KinetixModManager;

/// <summary>Why an installed-mods list looks the way it does.</summary>
public enum InstalledModsStatus
{
	/// <summary>A mods folder was found and read. It may still be empty.</summary>
	Ready,

	/// <summary>The game could not be found on this machine at all.</summary>
	NotInstalled,

	/// <summary>The game is here, but nothing has created its mods folder yet.</summary>
	NoModsFolder,
}

/// <summary>
/// One installed mod, and the sentence that describes it.
///
/// <para>
/// The sentence is the whole row as far as a screen reader is concerned, which is why it is built in one
/// piece rather than assembled from several labels sitting beside each other. A reader works through a
/// row's contents in order, so three labels become three stops to arrow past instead of one fact.
/// </para>
/// </summary>
public sealed record InstalledModRow(string Path, string Name, string Version, bool Enabled)
{
	/// <summary>What the reader says for this row.</summary>
	public string Spoken
	{
		get
		{
			string name = string.IsNullOrWhiteSpace(Name) ? System.IO.Path.GetFileName(Path) : Name;
			string version = string.IsNullOrWhiteSpace(Version) ? "" : " " + Version;

			// State first. The user is arrowing a list looking for what is switched off, and putting it last
			// means hearing the whole name before the one word being listened for.
			return Enabled ? $"{name}{version}" : Loc.T("installed.rowDisabled", name + version);
		}
	}

	public override string ToString() => Spoken;
}

/// <summary>
/// The installed-mods list as a front end shows it: the rows, and the one sentence to say when it appears.
///
/// <para>
/// Here rather than in a window because both halves are decisions, and both were got wrong in the GTK spike
/// in ways that only a blind user would ever notice. The spike read every game's enabled state with
/// <em>Minecraft's</em> rule while writing it with the right one — so a disabled Stardew mod, which is a
/// folder with a leading dot, read back as switched on, and switching it on again moved it to the name it
/// already had. And a game that was not installed at all was announced as "0 mods installed", which is
/// indistinguishable by ear from a game that is installed and empty. The first is a reason to go and install
/// something; the second is a reason to conclude the manager does not support your game.
/// </para>
/// </summary>
public sealed class InstalledModsView
{
	private InstalledModsView(
		InstalledModsStatus status, GameProfile? game, string modsFolder, IReadOnlyList<InstalledModRow> rows)
	{
		Status = status;
		Game = game;
		ModsFolder = modsFolder;
		Rows = rows;
	}

	public InstalledModsStatus Status { get; }

	public GameProfile? Game { get; }

	/// <summary>The folder the rows came from, or <c>""</c> when there was none to read.</summary>
	public string ModsFolder { get; }

	public IReadOnlyList<InstalledModRow> Rows { get; }

	/// <summary>How many of the rows are switched off.</summary>
	public int DisabledCount => Rows.Count(r => !r.Enabled);

	/// <summary>The game could not be found, so there is nothing to read.</summary>
	public static InstalledModsView NotInstalled(GameProfile? game) =>
		new(InstalledModsStatus.NotInstalled, game, "", Array.Empty<InstalledModRow>());

	/// <summary>The game is here but its mods folder has never been created.</summary>
	public static InstalledModsView NoModsFolder(GameProfile? game, string expectedFolder) =>
		new(InstalledModsStatus.NoModsFolder, game, expectedFolder, Array.Empty<InstalledModRow>());

	/// <summary>
	/// What a scan found, with each mod's switched-on state read by <em>this game's</em> rule — a leading dot
	/// for Stardew, a tilde for The Witcher, a move out of <c>plugins</c> for BepInEx, a suffix for Minecraft.
	/// <see cref="ModEnableState"/> is the one place that knows which, and it is the same place the toggle
	/// writes through; asking it here is what keeps reading and writing in agreement.
	/// </summary>
	public static InstalledModsView Of(GameProfile? game, string modsFolder, IEnumerable<GameMod> scanned)
	{
		var rows = scanned
			.Select(m => new InstalledModRow(
				m.FolderPath,
				m.Name,
				m.Version,
				ModEnableState.IsEnabled(m.FolderPath, game?.Id ?? "")))
			.ToList();

		return new InstalledModsView(InstalledModsStatus.Ready, game, modsFolder, rows);
	}

	/// <summary>
	/// The sentence to say when this list appears.
	///
	/// It always names the game, because a front end that switches games needs the user to hear which one
	/// they landed on — and it says what is actually true rather than a count that happens to be zero.
	/// </summary>
	public string Announcement
	{
		get
		{
			string name = Game?.DisplayName ?? "";

			return Status switch
			{
				InstalledModsStatus.NotInstalled => Loc.T("installed.notInstalled", name),
				InstalledModsStatus.NoModsFolder => Loc.T("installed.noModsFolder", name),
				_ when Rows.Count == 0 => Loc.T("installed.noneYet", name),
				_ when DisabledCount == 0 => Loc.T("installed.allOn", name, Rows.Count),
				_ => Loc.T("installed.someOff", name, Rows.Count, DisabledCount),
			};
		}
	}

	/// <summary>
	/// The line for a status bar: where the mods were read from, or why they were not.
	///
	/// Separate from <see cref="Announcement"/> because they are read at different moments — the
	/// announcement when the list appears, this when the user goes looking for detail — and because a path
	/// is a poor thing to say out loud unprompted.
	/// </summary>
	public string StatusLine => Status switch
	{
		InstalledModsStatus.NotInstalled => Announcement,
		InstalledModsStatus.NoModsFolder => Loc.T("installed.folderWouldBe", ModsFolder),
		_ => Loc.T("installed.readFrom", Rows.Count, ModsFolder),
	};
}
