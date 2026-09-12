using System;
using System.Collections.Generic;
using System.Linq;

namespace KinetixModManager;

/// <summary>Where a Minecraft mod is fetched from. The two access mods are not published in the same place.</summary>
public enum MinecraftModOrigin
{
	/// <summary>A GitHub repository's releases; <c>Source</c> is <c>owner/repo</c>.</summary>
	GitHubRelease,

	/// <summary>A Modrinth project; <c>Source</c> is the project slug or id.</summary>
	Modrinth
}

/// <summary>One mod the Minecraft accessibility suite can install.</summary>
public sealed class MinecraftSuiteMod
{
	/// <summary>The stable key stored in settings. Never shown to the user.</summary>
	public required string Id { get; init; }

	/// <summary>The name the user sees and hears.</summary>
	public required string DisplayName { get; init; }

	/// <summary>
	/// The mod's own id from its <c>fabric.mod.json</c> — how an installed copy is recognised, whether the
	/// manager put it there or the user dropped it in by hand.
	/// </summary>
	public required string FabricModId { get; init; }

	public required MinecraftModOrigin Origin { get; init; }

	/// <summary><c>owner/repo</c> for GitHub, or the project slug for Modrinth.</summary>
	public required string Source { get; init; }

	/// <summary>
	/// Whether Fabric API has to be installed alongside this mod as a separate jar.
	///
	/// This is the entire difference between the two access mods, and the reason the user must never be asked
	/// about it: Minecraft Access ships Fabric API, Cloth Config and Balm nested inside its own jar, so it
	/// installs as a single file. United Minecraft declares Fabric API as an ordinary dependency and does not
	/// bundle it, so it needs two. Both facts are read from the mods themselves rather than assumed — see
	/// <see cref="FabricModInfo.NestedJars"/>.
	/// </summary>
	public required bool NeedsFabricApi { get; init; }

	/// <summary>Where the mod's own documentation lives, for the F3 viewer.</summary>
	public string DocsUrl { get; init; } = "";
}

/// <summary>
/// The Minecraft accessibility suite: which mods it can install, and what each one drags in behind it.
///
/// <para>
/// Minecraft is the first supported game with <em>two</em> competing accessibility mods rather than one, and
/// they are not interchangeable in what they need installed. Both are supported deliberately: Minecraft Access
/// is the older of the two and blind players have been using it for years, so "the newer one is better" is not
/// a good enough reason to strand them. Familiarity is an accessibility concern in its own right.
/// </para>
///
/// <para>
/// What the user is asked is therefore only ever "which mod do you want?" — never anything about Fabric API,
/// which is a consequence of that answer and not a decision anyone should have to make.
/// </para>
/// </summary>
public static class MinecraftSuite
{
	public const string UnitedMinecraftId = "UnitedMinecraft";
	public const string MinecraftAccessId = "MinecraftAccess";

	/// <summary>Fabric API, needed by United Minecraft and bundled inside Minecraft Access.</summary>
	public static readonly MinecraftSuiteMod FabricApi = new()
	{
		Id             = "FabricApi",
		DisplayName    = "Fabric API",
		FabricModId    = "fabric-api",
		Origin         = MinecraftModOrigin.Modrinth,
		Source         = "fabric-api",
		NeedsFabricApi = false
	};

	/// <summary>
	/// The accessibility mods, in the order they are offered. United Minecraft leads because it is the more
	/// actively released of the two, not because the other is deprecated.
	/// </summary>
	public static readonly IReadOnlyList<MinecraftSuiteMod> AccessMods = new[]
	{
		new MinecraftSuiteMod
		{
			Id             = UnitedMinecraftId,
			DisplayName    = "United Minecraft",
			FabricModId    = "united_minecraft",
			// GitHub releases only - it is not published on Modrinth. One .jar asset per release, and the
			// Minecraft version is in the tag (v1.1.0+mc26.2), which makes version matching straightforward.
			Origin         = MinecraftModOrigin.GitHubRelease,
			Source         = "blindgoofball/united-Minecraft",
			// Declares fabric-api as a hard dependency and does not bundle it.
			NeedsFabricApi = true,
			DocsUrl        = "https://github.com/blindgoofball/united-Minecraft"
		},
		new MinecraftSuiteMod
		{
			Id             = MinecraftAccessId,
			DisplayName    = "Minecraft Access",
			FabricModId    = "minecraft_access",
			Origin         = MinecraftModOrigin.Modrinth,
			Source         = "minecraft-access",
			// Ships Fabric API, Cloth Config and Balm jar-in-jar, so nothing is installed alongside it.
			NeedsFabricApi = false,
			DocsUrl        = "https://docs.mcaccess.org/"
		}
	};

	/// <summary>The mod chosen when the user has not picked one. United Minecraft: newer and more actively released.</summary>
	public static MinecraftSuiteMod Default => AccessMods[0];

	/// <summary>The access mod with this id, or <see cref="Default"/> for an unset or unrecognised one.</summary>
	public static MinecraftSuiteMod AccessModFor(string? id) =>
		AccessMods.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;

	/// <summary>The names shown in the "which accessibility mod?" chooser, in offer order.</summary>
	public static IReadOnlyList<string> AccessModNames =>
		AccessMods.Select(m => m.DisplayName).ToList();

	/// <summary>The access mod with this display name, or <c>null</c> when the name matches none.</summary>
	public static MinecraftSuiteMod? AccessModByDisplayName(string? displayName) =>
		AccessMods.FirstOrDefault(m => string.Equals(m.DisplayName, displayName, StringComparison.Ordinal));

	/// <summary>
	/// Everything that has to end up in the mods folder for <paramref name="chosen"/> to work, in install
	/// order — dependencies first, so a half-finished install is never a mod without the API it needs.
	/// </summary>
	public static IReadOnlyList<MinecraftSuiteMod> InstallPlanFor(MinecraftSuiteMod chosen)
	{
		var plan = new List<MinecraftSuiteMod>();
		if (chosen.NeedsFabricApi) plan.Add(FabricApi);
		plan.Add(chosen);
		return plan;
	}

	/// <summary>
	/// The access mod the user has NOT chosen — the one to warn about if it turns up installed.
	///
	/// Both mods hook narration on the same screens, so running the two together is expected to double-speak
	/// everything. Treated as mutually exclusive until that is ear-tested and proven otherwise; the safe
	/// direction is to warn rather than to let someone discover it mid-game.
	/// </summary>
	public static IEnumerable<MinecraftSuiteMod> RivalsOf(MinecraftSuiteMod chosen) =>
		AccessMods.Where(m => !string.Equals(m.Id, chosen.Id, StringComparison.Ordinal));
}
