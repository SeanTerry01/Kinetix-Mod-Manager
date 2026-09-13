using System;

namespace KinetixModManager;

/// <summary>
/// How the manager gives audible progress feedback during long downloads and installs.
/// <see cref="Tones"/> plays a rising synthesized pitch, <see cref="Speech"/> speaks the name once
/// then bare deciles ("10 percent", ...). <see cref="Off"/> is useful for users who already rely on
/// their screen reader's own progress-bar beeps (e.g. NVDA's "Progress bar output").
/// </summary>
public enum ProgressFeedback
{
	Off,
	Tones,
	Speech,
	Both
}
