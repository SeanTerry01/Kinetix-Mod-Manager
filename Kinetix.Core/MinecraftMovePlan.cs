namespace KinetixModManager;

/// <summary>
/// What happens to each mod when the setup moves to another Minecraft version.
///
/// <para>
/// Three outcomes, not two. A mod with a build for the new version is updated; a mod that already accepts it needs
/// nothing; and the rest have to be switched off, because Fabric does not skip a mod built for another version, it
/// refuses to start the game and names every offender.
/// </para>
/// </summary>
public sealed class MinecraftMovePlan
{
	/// <summary>Mods with a build for the new version, wherever it is published.</summary>
	public required IReadOnlyList<GameMod> ToUpdate { get; init; }

	/// <summary>Mods that can stay as they are.</summary>
	public required IReadOnlyList<GameMod> FineAsTheyAre { get; init; }

	/// <summary>Mods with no build that would stop the game starting, so they are switched off.</summary>
	public required IReadOnlyList<GameMod> ToSwitchOff { get; init; }

	/// <summary>
	/// Sorts the mods.
	///
	/// ⚠️ Modrinth is not the only place a build can be. United Minecraft is published on GitHub alone, so going by
	/// Modrinth meant the manager offered a move because the mod's new release existed, then switched that same mod
	/// off as having no build: the game started with no speech, having told the player the mod "cannot run".
	/// </summary>
	/// <param name="mods">The installed mods.</param>
	/// <param name="hasBuild">Whether a build for the new version is published anywhere.</param>
	/// <param name="survives">Whether the installed copy can stay as it is.</param>
	public static MinecraftMovePlan Decide(IEnumerable<GameMod> mods, Func<GameMod, bool> hasBuild, Func<GameMod, bool> survives)
	{
		var all = mods.ToList();
		List<GameMod> toUpdate = all.Where(hasBuild).ToList();
		List<GameMod> fine = all.Except(toUpdate).Where(survives).ToList();
		List<GameMod> off = all.Except(toUpdate).Except(fine).ToList();

		return new MinecraftMovePlan { ToUpdate = toUpdate, FineAsTheyAre = fine, ToSwitchOff = off };
	}

	/// <summary>
	/// Which of the mods that could not be updated have to be switched off after all. A mod whose update failed is
	/// still the old build, and if that build cannot run on the new version, leaving it on stops the game starting.
	/// </summary>
	public static IReadOnlyList<GameMod> FailedThatMustGoOff(IEnumerable<GameMod> failed, Func<GameMod, bool> survives) =>
		failed.Where(m => !survives(m)).ToList();

	/// <summary>Whether a plan switches off the mod the player's speech depends on, which they must hear before agreeing.</summary>
	public bool SwitchesOffAccessMod(string accessModId) =>
		accessModId.Length > 0 && ToSwitchOff.Any(m => string.Equals(m.UniqueId, accessModId, StringComparison.OrdinalIgnoreCase));
}
