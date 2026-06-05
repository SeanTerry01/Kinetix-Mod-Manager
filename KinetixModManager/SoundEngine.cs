using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Vorbis;
using NAudio.Wave;

namespace KinetixModManager;

/// <summary>
/// Handles all audio playback for the application using NAudio and NVorbis.
/// Sounds are loaded from the active theme folder under the application's "sounds" directory.
/// All playback is fire-and-forget on a background thread so the UI is never blocked.
/// </summary>
public class SoundEngine
{
	/// <summary>
	/// Human-readable descriptions for every named sound event, used in the Sound Demo dialog.
	/// </summary>
	public static readonly IReadOnlyDictionary<string, string> SoundDescriptions =
		new Dictionary<string, string>
		{
			{ "connect",            "Played when successfully connected to Nexus Mods." },
			{ "disconnect",         "Played when you are disconnected, need to enter an API key, or when the program closes." },
			{ "enable",             "Played when one or more mods are enabled." },
			{ "disable",            "Played when one or more mods are disabled or deleted." },
			{ "error",              "Played when an error occurs, such as a failed download." },
			{ "loading_indicator",  "A pulsing sound that plays while the manager is checking for updates in the background." },
			{ "load_complete",      "Played when the manager has finished checking all mods for updates." }
		};

	private readonly string _themesPath;
	private readonly AppSettings _settings;

	/// <summary>
	/// Initialises the sound engine.
	/// </summary>
	/// <param name="themesPath">Absolute path to the root sounds/themes directory.</param>
	/// <param name="settings">Live application settings (read for <c>CurrentTheme</c> and <c>SoundVolume</c>).</param>
	public SoundEngine(string themesPath, AppSettings settings)
	{
		_themesPath = themesPath;
		_settings   = settings;
	}

	/// <summary>
	/// Plays a named sound event on a background thread.
	/// Looks up the first <c>.ogg</c> file in <c>&lt;themesPath&gt;/&lt;theme&gt;/&lt;name&gt;/</c>.
	/// Falls back to the Default theme folder if the active theme does not contain the sound.
	/// </summary>
	/// <param name="name">Name of the sound sub-folder (e.g. <c>"connect"</c>).</param>
	/// <param name="themeOverride">
	/// Optional theme name to use instead of <see cref="AppSettings.CurrentTheme"/>.
	/// </param>
	public void Play(string name, string? themeOverride = null)
	{
		Task.Run(() =>
		{
			try
			{
				string theme = themeOverride ?? _settings.CurrentTheme;
				string soundDir = Path.Combine(_themesPath, theme, name);
				if (!Directory.Exists(soundDir))
					soundDir = Path.Combine(_themesPath, "Default", name);

				if (!Directory.Exists(soundDir))
					return;

				string[] files = Directory.GetFiles(soundDir, "*.ogg");
				if (files.Length == 0)
					return;

				using VorbisWaveReader reader = new VorbisWaveReader(files[0]);
				using WaveOutEvent output = new WaveOutEvent();
				output.Volume = (float)_settings.SoundVolume / 100f;
				output.Init(reader);
				output.Play();
				while (output.PlaybackState == PlaybackState.Playing)
					Thread.Sleep(100);
			}
			catch { /* audio failures are non-fatal */ }
		});
	}

	/// <summary>
	/// Plays a logo sound file on a background thread.
	/// Used to preview startup logo sounds in the Settings dialog.
	/// </summary>
	/// <param name="theme">Theme name (sub-folder of <c>themesPath</c>).</param>
	/// <param name="file">File name of the <c>.ogg</c> file inside the theme's <c>logo/</c> sub-folder.</param>
	public void PlayLogoSound(string theme, string file)
	{
		Task.Run(() =>
		{
			try
			{
				string path = Path.Combine(_themesPath, theme, "logo", file);
				if (!File.Exists(path))
					return;

				using VorbisWaveReader reader = new VorbisWaveReader(path);
				using WaveOutEvent output = new WaveOutEvent();
				output.Volume = (float)_settings.SoundVolume / 100f;
				output.Init(reader);
				output.Play();
				while (output.PlaybackState == PlaybackState.Playing)
					Thread.Sleep(100);
			}
			catch { /* audio failures are non-fatal */ }
		});
	}
}
