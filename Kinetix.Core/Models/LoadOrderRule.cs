using System;

namespace KinetixModManager;

/// <summary>
/// One persistent load-order rule: <see cref="Plugin"/> must load after <see cref="After"/>.
///
/// Kept beside the other load-order types rather than nested inside AppSettings, where it used to live. It
/// describes a fact about a game's plugins, not a fact about how this program stores settings — and while it
/// was nested, anything wanting to reason about load order had to name the settings class to do it.
///
/// Moving it does not change the settings file. Newtonsoft writes these by property name and the project sets
/// no TypeNameHandling, so a rule saved by an older build reads back into this type unchanged.
/// </summary>
public class LoadOrderRule
{
	public string Plugin { get; set; } = "";
	public string After { get; set; } = "";
}
