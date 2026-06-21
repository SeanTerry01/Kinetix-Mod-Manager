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
	/// Plays a short synthesized tone whose pitch rises with <paramref name="percent"/> (0–100), used as
	/// non-verbal progress feedback during downloads and installs. The tone is generated on the fly — no audio
	/// files are needed — so any percentage maps to its own pitch. Fire-and-forget on a background thread;
	/// rapid successive calls overlap briefly, which gives the climbing "sweep" feel as progress advances.
	/// Honours <see cref="AppSettings.SoundVolume"/>.
	/// </summary>
	/// <param name="percent">Progress percentage, clamped to 0–100. Higher = higher pitch.</param>
	public void PlayTone(int percent)
	{
		Task.Run(() =>
		{
			try
			{
				if (percent < 0) percent = 0;
				else if (percent > 100) percent = 100;

				const int sampleRate = 44100;
				const double durationSec = 0.09;
				int sampleCount = (int)(sampleRate * durationSec);

				// Map 0–100% to a roughly even-sounding pitch ramp (logarithmic so each step feels equal).
				double freq = 300.0 * Math.Pow(1400.0 / 300.0, percent / 100.0);

				// Tones sit a little under the named-event volume so they don't fatigue on long downloads.
				double volume = (_settings.SoundVolume / 100.0) * 0.45;
				int fade = (int)(sampleRate * 0.006); // ~6 ms fade in/out to avoid clicks
				short[] samples = new short[sampleCount];
				for (int i = 0; i < sampleCount; i++)
				{
					double env = 1.0;
					if (i < fade) env = (double)i / fade;
					else if (i > sampleCount - fade) env = (double)(sampleCount - i) / fade;
					double s = Math.Sin(2.0 * Math.PI * freq * i / sampleRate) * env * volume;
					samples[i] = (short)(s * short.MaxValue);
				}

				byte[] bytes = new byte[sampleCount * 2];
				Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

				using var ms = new MemoryStream(bytes);
				var raw = new RawSourceWaveStream(ms, new WaveFormat(sampleRate, 16, 1));
				using WaveOutEvent output = new WaveOutEvent();
				output.Init(raw);
				output.Play();
				while (output.PlaybackState == PlaybackState.Playing)
					Thread.Sleep(10);
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
