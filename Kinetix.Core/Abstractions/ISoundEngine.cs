using System;
using System.Threading.Tasks;

namespace KinetixModManager;

/// <summary>
/// The short sounds that tell the user what happened without making them wait for a sentence.
///
/// A sound carries a fact faster than speech can, and crucially it can land *while* the reader is talking:
/// the confirmation that a mod was enabled arrives without interrupting the name of the next one. That is
/// why these are a separate channel from <see cref="IAnnouncer"/> rather than a decoration on it.
///
/// The sounds themselves are ordinary <c>.ogg</c> files in swappable theme folders, so they are not part of
/// this contract and need no porting. Only the playing of them is platform work: NAudio on Windows, and
/// something like GStreamer or libsoundio elsewhere.
/// </summary>
public interface ISoundEngine
{
	/// <summary>Plays the named sound from the active theme, or from <paramref name="themeOverride"/>.</summary>
	void Play(string name, string? themeOverride = null);

	/// <summary>
	/// Plays it without blocking the caller. Used where the sound accompanies work that is already running,
	/// so that a slow disk cannot stall the interface behind a sound effect.
	/// </summary>
	Task PlayAsync(string name, string? themeOverride = null);

	/// <summary>
	/// A synthesised tone whose pitch rises with <paramref name="percent"/>, for progress.
	///
	/// Not a sound file, because the whole point is that it is continuous: a download reads as a rising
	/// pitch rather than as a number repeated every ten percent, which is both faster to follow and far less
	/// tiring over a long transfer.
	/// </summary>
	void PlayTone(int percent);

	/// <summary>Plays the startup sound belonging to a theme. Long enough to need stopping, hence the pair.</summary>
	void PlayLogoSound(string theme, string file);

	/// <summary>Stops the startup sound, e.g. because the user has moved on and does not want to wait for it.</summary>
	void StopLogoSound();
}
