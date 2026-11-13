namespace KinetixModManager;

/// <summary>
/// Whether the manager opens its change log, at the newest version, as it starts: the first time it runs after an update
/// (Sean, 2026-11-12). Not on a first install, which gets the setup wizard and has nothing it is changed from.
/// </summary>
public static class WhatsNew
{
	/// <summary>
	/// True when this is the first run of a newer version than the one last run.
	/// </summary>
	/// <param name="hasRunBefore">
	/// <see cref="AppSettings.HasCompletedFirstRun"/> as it was loaded, before startup sets it. 1.5.1 saved it already,
	/// which is what tells an update from it apart from a fresh install, since 1.5.1 recorded no version.
	/// </param>
	/// <param name="lastRunVersion"><see cref="AppSettings.LastRunVersion"/>: empty when a version that kept none ran last.</param>
	/// <param name="currentVersion">The version running now.</param>
	public static bool ShouldShow(bool hasRunBefore, string? lastRunVersion, string currentVersion)
	{
		if (!hasRunBefore || !Version.TryParse(currentVersion, out Version? current)) return false;
		// Nothing recorded is a version from before this existed, so older by definition.
		if (string.IsNullOrWhiteSpace(lastRunVersion) || !Version.TryParse(lastRunVersion, out Version? last)) return true;
		// Newer only: going back to an older build, or running the same one again, has nothing new to show.
		return current > last;
	}
}
