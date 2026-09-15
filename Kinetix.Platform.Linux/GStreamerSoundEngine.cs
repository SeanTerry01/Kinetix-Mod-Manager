using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace KinetixModManager;

/// <summary>
/// The Linux <see cref="ISoundEngine"/>: GStreamer, which is already on any desktop that plays audio at all
/// and needs no new package for the manager.
///
/// <para>
/// Only the playing is here. <em>Which</em> file a named event maps to — the loaded game's theme, or the
/// Default theme's where this one has not recorded that sound yet — is <see cref="SoundThemes"/>'s decision,
/// in the core, so both front ends resolve a name to a file the same way and get the same per-sound
/// fallback. A second implementation of that rule is how two heads start disagreeing about what the app
/// sounds like.
/// </para>
///
/// <para>
/// Every pipeline is described by a string and built by <c>gst_parse_launch</c>. That is a deliberate choice
/// over assembling elements and setting properties one at a time: the alternative is <c>g_object_set</c>,
/// which is variadic and has to be P/Invoked once per property type, and none of it would be any more
/// checkable than the string is. GStreamer parses the string, and a typo fails loudly at that point rather
/// than halfway through a pipeline.
/// </para>
/// </summary>
public sealed class GStreamerSoundEngine : ISoundEngine
{
	private const string Library = "libgstreamer-1.0.so.0";

	private readonly string _themesPath;
	private readonly Func<string> _currentTheme;
	private readonly Func<bool> _soundsEnabled;
	private readonly Func<int> _volume;

	private readonly object _logoLock = new();
	private IntPtr _logoPipeline = IntPtr.Zero;

	private static readonly Lazy<bool> Ready = new(Initialise, LazyThreadSafetyMode.ExecutionAndPublication);

	/// <param name="themesPath">The <c>sounds</c> folder holding the theme directories.</param>
	/// <param name="currentTheme">The theme to play from, read fresh each time so a game switch is heard at once.</param>
	/// <param name="soundsEnabled">The user's master switch.</param>
	/// <param name="volume">0 to 100.</param>
	/// <remarks>
	/// Four callbacks rather than a settings object, because <c>AppSettings</c> lives in the WinForms app and
	/// this cannot see it. What this actually needs is four answers, and asking for them directly is both
	/// smaller and honest about the dependency.
	/// </remarks>
	public GStreamerSoundEngine(
		string themesPath, Func<string> currentTheme, Func<bool> soundsEnabled, Func<int> volume)
	{
		_themesPath = themesPath;
		_currentTheme = currentTheme;
		_soundsEnabled = soundsEnabled;
		_volume = volume;
	}

	/// <summary>True when GStreamer started. False means the app is silent, not broken.</summary>
	public bool IsAvailable => Ready.Value;

	public void Play(string name, string? themeOverride = null)
	{
		// Started and not waited for, but still observed: a theme with a malformed file would otherwise go
		// quiet with nothing anywhere saying why, which on an app whose feedback IS sound is the worst way
		// for it to fail.
		_ = Observe();

		async Task Observe()
		{
			try { await PlayAsync(name, themeOverride); }
			catch (Exception ex) { DiagnosticLog.WriteException("Sound", $"playing the \"{name}\" sound", ex); }
		}
	}

	public Task PlayAsync(string name, string? themeOverride = null)
	{
		if (!_soundsEnabled() || !IsAvailable) return Task.CompletedTask;

		string? file = SoundThemes.Resolve(_themesPath, themeOverride ?? _currentTheme(), name);
		if (file == null) return Task.CompletedTask;

		return Task.Run(() =>
		{
			try { RunToEnd(PlaybinFor(file), TimeSpan.FromSeconds(30)); }
			catch (Exception ex) { DiagnosticLog.WriteException("Sound", $"playing the \"{name}\" sound", ex); }
		});
	}

	/// <summary>
	/// The rising progress tone, generated rather than played from a file so that every percentage has its
	/// own pitch — the point being that a download reads as a continuous climb rather than as a number
	/// repeated every ten percent.
	/// </summary>
	public void PlayTone(int percent)
	{
		if (!IsAvailable) return;

		Task.Run(() =>
		{
			try
			{
				percent = Math.Clamp(percent, 0, 100);

				// The same mapping the Windows engine uses, so the two builds sound alike: 300 Hz to 1400 Hz,
				// logarithmically, which makes each step of progress feel like the same step.
				double frequency = 300.0 * Math.Pow(1400.0 / 300.0, percent / 100.0);

				// Tones sit under the named-event volume so they do not fatigue over a long download.
				double level = Level() * 0.45;

				// audiotestsrc measures its length in buffers of 1024 samples; at 44.1 kHz four of them is
				// about 90 ms, which is the length the Windows tone uses.
				string pipeline =
					$"audiotestsrc wave=sine freq={Num(frequency)} volume={Num(level)} num-buffers=4 " +
					"! audioconvert ! audioresample ! autoaudiosink";

				RunToEnd(Parse(pipeline), TimeSpan.FromSeconds(2));
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Sound", "playing a progress tone", ex); }
		});
	}

	public void PlayLogoSound(string theme, string file)
	{
		if (!_soundsEnabled() || !IsAvailable) return;

		// Arrowing a list of logos starts one preview per item; without stopping the one already playing they
		// pile up and nothing can be told apart.
		StopLogoSound();

		string path = Path.Combine(_themesPath, theme, "logo", file);
		if (!File.Exists(path)) return;

		Task.Run(() =>
		{
			IntPtr pipeline = IntPtr.Zero;
			try
			{
				pipeline = PlaybinFor(path);
				if (pipeline == IntPtr.Zero) return;

				lock (_logoLock) { _logoPipeline = pipeline; }
				RunToEnd(pipeline, TimeSpan.FromMinutes(2), alreadyOwned: true);
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Sound", "playing the logo sound", ex); }
			finally
			{
				lock (_logoLock)
				{
					if (_logoPipeline == pipeline) _logoPipeline = IntPtr.Zero;
				}
				if (pipeline != IntPtr.Zero) Release(pipeline);
			}
		});
	}

	public void StopLogoSound()
	{
		IntPtr playing;
		lock (_logoLock) { playing = _logoPipeline; _logoPipeline = IntPtr.Zero; }
		if (playing == IntPtr.Zero) return;

		// Setting the state to NULL ends the stream, which ends the wait inside RunToEnd — the same shape the
		// Windows engine uses, where stopping the device ends its own wait.
		try { gst_element_set_state(playing, StateNull); }
		catch (Exception ex) { DiagnosticLog.WriteException("Sound", "stopping the logo sound", ex); }
	}

	// -------------------------------------------------------------------------
	// GStreamer
	// -------------------------------------------------------------------------

	private const int StateNull = 1;
	private const int StatePlaying = 4;

	/// <summary>Bus message types: ERROR is 1 &lt;&lt; 11, EOS is 1 &lt;&lt; 0.</summary>
	private const uint MessageErrorOrEos = (1u << 11) | 1u;

	private const ulong Forever = ulong.MaxValue;

	[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
	private static extern void gst_init(IntPtr argc, IntPtr argv);

	[DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
	private static extern IntPtr gst_parse_launch(string pipelineDescription, out IntPtr error);

	[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
	private static extern int gst_element_set_state(IntPtr element, int state);

	[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr gst_element_get_bus(IntPtr element);

	[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr gst_bus_timed_pop_filtered(IntPtr bus, ulong timeoutNanoseconds, uint types);

	[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
	private static extern void gst_message_unref(IntPtr message);

	[DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
	private static extern void gst_object_unref(IntPtr obj);

	private static bool Initialise()
	{
		try
		{
			gst_init(IntPtr.Zero, IntPtr.Zero);
			return true;
		}
		catch (DllNotFoundException)
		{
			// A normal state on a minimal install, and not worth a stack trace. The manager runs silent.
			DiagnosticLog.Write("Sound", "GStreamer is not installed, so the manager will not play sounds");
			return false;
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Sound", "starting GStreamer", ex);
			return false;
		}
	}

	private IntPtr PlaybinFor(string file)
	{
		// A file URI rather than a path: playbin takes a URI, and letting Uri build it escapes the spaces
		// and apostrophes that theme folders like "The Witcher 3" and mod names are full of.
		string uri = new Uri(Path.GetFullPath(file)).AbsoluteUri;
		return Parse($"playbin uri=\"{uri}\" volume={Num(Level())}");
	}

	private static IntPtr Parse(string description)
	{
		IntPtr pipeline = gst_parse_launch(description, out IntPtr error);
		if (error != IntPtr.Zero || pipeline == IntPtr.Zero)
		{
			DiagnosticLog.Write("Sound", $"GStreamer would not build the pipeline: {description}");
			return IntPtr.Zero;
		}
		return pipeline;
	}

	/// <summary>
	/// Plays a pipeline and waits for it to finish, then tears it down.
	///
	/// The wait is on the pipeline's bus rather than a sleep, so a sound of any length is followed exactly —
	/// which is what the shutdown cue needs, being the one caller that genuinely cannot be cut short.
	/// </summary>
	private static void RunToEnd(IntPtr pipeline, TimeSpan limit, bool alreadyOwned = false)
	{
		if (pipeline == IntPtr.Zero) return;

		IntPtr bus = IntPtr.Zero;
		try
		{
			gst_element_set_state(pipeline, StatePlaying);

			bus = gst_element_get_bus(pipeline);
			if (bus == IntPtr.Zero) return;

			// A ceiling as well as a wait. A sound file that never reports the end of its stream would
			// otherwise hold this thread for the life of the program.
			ulong timeout = limit == TimeSpan.MaxValue ? Forever : (ulong)(limit.TotalMilliseconds * 1_000_000);
			IntPtr message = gst_bus_timed_pop_filtered(bus, timeout, MessageErrorOrEos);
			if (message != IntPtr.Zero) gst_message_unref(message);
		}
		finally
		{
			if (bus != IntPtr.Zero) gst_object_unref(bus);
			if (!alreadyOwned) Release(pipeline);
			else gst_element_set_state(pipeline, StateNull);
		}
	}

	private static void Release(IntPtr pipeline)
	{
		try
		{
			gst_element_set_state(pipeline, StateNull);
			gst_object_unref(pipeline);
		}
		catch (Exception ex) { DiagnosticLog.WriteException("Sound", "releasing a sound", ex); }
	}

	/// <summary>The user's volume as playbin wants it: 0 to 1.</summary>
	private double Level() => Math.Clamp(_volume(), 0, 100) / 100.0;

	/// <summary>
	/// A number GStreamer will parse. Invariant culture on purpose: a machine set to a locale that writes
	/// "0,8" produces a pipeline description GStreamer reads as two arguments, and the sound goes silent
	/// for everyone in half of Europe.
	/// </summary>
	private static string Num(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
