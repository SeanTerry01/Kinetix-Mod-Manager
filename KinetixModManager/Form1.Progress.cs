using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>A selectable <see cref="ProgressFeedback"/> value paired with its localized label for the settings combo.</summary>
internal sealed class ProgressFeedbackChoice
{
	public ProgressFeedback Value { get; }
	private readonly string _display;

	public ProgressFeedbackChoice(ProgressFeedback value, string display)
	{
		Value = value;
		_display = display;
	}

	public override string ToString() => _display;
}

/// <summary>Unified audible/visual progress feedback for long downloads and installs.</summary>
public partial class Form1
{
	/// <summary>Creates a progress reporter for a download (<paramref name="installing"/> false) or install (true).</summary>
	private ProgressAnnouncer NewProgress(string name, bool installing) => new ProgressAnnouncer(this, name, installing);

	private string? _cachedGameDisplayName;
	private string? _cachedGameDisplayKey;

	/// <summary>
	/// The active game's friendly display name, as shown in the title bar. For Skyrim and Fallout 4 this is the
	/// product name (matching Steam) plus the exact build read from the game's exe — "Skyrim Special Edition
	/// (1.6.1170)", "Fallout 4 (1.11.191)". We intentionally do NOT print "Anniversary Edition" / "Next-Gen":
	/// those are paid DLC bundles whose ownership isn't detectable, and Bethesda's free 1.6 / next-gen updates
	/// make an SE/old-gen copy report the same version as an AE/next-gen one — so labeling by version would wrongly
	/// claim DLC the user may not own. The version number is the part that's both accurate and what actually
	/// decides mod-file compatibility. Falls back to the plain name if the exe can't be read. Cached per active
	/// game + game path so the exe isn't read on every status update.
	/// </summary>
	internal string GameDisplayName()
	{
		string key = _settings.ActiveGame + "|" + _settings.CurrentGamePath;
		if (_cachedGameDisplayName != null && _cachedGameDisplayKey == key)
			return _cachedGameDisplayName;
		_cachedGameDisplayKey = key;
		_cachedGameDisplayName = ComputeGameDisplayName();
		return _cachedGameDisplayName;
	}

	/// <summary>Forces the next <see cref="GameDisplayName"/> call to re-read the game version (e.g. after a path change).</summary>
	private void InvalidateGameDisplayName() => _cachedGameDisplayKey = null;

	private string ComputeGameDisplayName()
	{
		string game = _settings.ActiveGame;
		if (game != "SkyrimSE" && game != "Fallout4")
			return "Stardew Valley"; // unchanged fallback for Stardew Valley / no game loaded

		string baseName = game == "SkyrimSE" ? "Skyrim Special Edition" : "Fallout 4";
		(int major, int minor, int build)? ver = ReadGameRuntimeVersion(game, _settings.CurrentGamePath);
		if (ver == null)
			return baseName; // exe unreadable -> plain product name

		// Product name (matching Steam) + the exact build. The version is what determines mod-file compatibility
		// (e.g. Skyrim 1.6+ uses Nexus "AE" files); we deliberately don't translate it into an edition word, which
		// would imply DLC ownership we can't detect. See the summary on GameDisplayName.
		return $"{baseName} ({ver.Value.major}.{ver.Value.minor}.{ver.Value.build})";
	}

	/// <summary>Reads the major.minor.build of a Bethesda game's exe; null if missing or unreadable.</summary>
	private static (int major, int minor, int build)? ReadGameRuntimeVersion(string game, string gamePath)
	{
		if (string.IsNullOrEmpty(gamePath)) return null;
		string exe = game == "SkyrimSE" ? "SkyrimSE.exe" : "Fallout4.exe";
		string path = Path.Combine(gamePath, exe);
		if (!File.Exists(path)) return null;
		try
		{
			var vi = FileVersionInfo.GetVersionInfo(path);
			if (vi.FileMajorPart == 0 && vi.FileMinorPart == 0 && vi.FileBuildPart == 0) return null;
			return (vi.FileMajorPart, vi.FileMinorPart, vi.FileBuildPart);
		}
		catch { return null; }
	}

	/// <summary>"Downloading X" / "Installing X" — the once-only opening line spoken at the start of an operation.</summary>
	private string OpeningPhrase(string name, bool installing) =>
		Loc.T(installing ? "progress.installingName" : "progress.downloadingName", name);

	/// <summary>Updates the title bar with the live percentage for an in-progress download or install.</summary>
	private void SetProgressTitle(string name, bool installing, int pct) =>
		Text = Loc.T(installing ? "progress.titleInstalling" : "progress.titleDownloading", GameDisplayName(), name, pct);

	/// <summary>
	/// One place that turns a stream of percentage updates into the feedback the user actually hears and sees.
	/// Implements <see cref="IProgress{T}"/> so it can be handed straight to the download and extract helpers.
	/// On the first update it speaks the operation name once ("Installing My Big Mod, 0 percent"); after that it
	/// speaks only bare deciles ("10 percent", "20 percent", …) so it never repeats the name. A rising synthesized
	/// tone tracks the percentage continuously (throttled so a fast download doesn't machine-gun beeps), and the
	/// title bar's percentage stays live throughout. Which of these channels are active is decided by
	/// <see cref="AppSettings.ProgressFeedback"/>, so a user can pick tones, speech, both, or off (e.g. to defer
	/// to their screen reader's own progress-bar beeps). All five download/install call sites share this so they
	/// behave identically.
	/// </summary>
	internal sealed class ProgressAnnouncer : IProgress<double>
	{
		private const long ToneThrottleMs = 45;

		private readonly Form1 _form;
		private readonly string _name;
		private readonly bool _installing;

		private bool _opened;
		private bool _done;
		private int _lastSpokenDecile = -1;
		private int _lastTonePct = -1;
		private int _lastTitlePct = -1;
		private long _lastToneTicks;

		public ProgressAnnouncer(Form1 form, string name, bool installing)
		{
			_form = form;
			_name = name;
			_installing = installing;
		}

		private ProgressFeedback Mode => _form._settings.ProgressFeedback;
		private bool TonesOn  => Mode is ProgressFeedback.Tones  or ProgressFeedback.Both;
		private bool SpeechOn => Mode is ProgressFeedback.Speech or ProgressFeedback.Both;

		/// <summary>Receives a 0–100 percentage (called on a background thread by the download/extract helpers).</summary>
		public void Report(double value)
		{
			int pct = (int)Math.Round(value);
			if (pct < 0) pct = 0; else if (pct > 100) pct = 100;

			// First update: announce the name once, prime the tone, and show the title.
			if (!_opened)
			{
				_opened = true;
				_lastSpokenDecile = 0;
				_lastTonePct = pct;
				_lastTitlePct = pct;
				_lastToneTicks = Environment.TickCount64;
				if (TonesOn) _form._soundEngine.PlayTone(pct);
				Ui(() =>
				{
					if (SpeechOn)
						_form.Speak(_form.OpeningPhrase(_name, _installing) + ", " + Loc.T("progress.percentSpoken", 0));
					_form.SetProgressTitle(_name, _installing, pct);
				});
				return;
			}

			// Tones follow the percentage closely, but throttled so a fast download doesn't flood playback.
			if (TonesOn && pct != _lastTonePct && Environment.TickCount64 - _lastToneTicks >= ToneThrottleMs)
			{
				_lastTonePct = pct;
				_lastToneTicks = Environment.TickCount64;
				_form._soundEngine.PlayTone(pct);
			}

			// Speech is deciles only (10, 20, …, 90). 100% is left to the caller's success message / closing tone.
			int decile = (pct / 10) * 10;
			bool speakNow = SpeechOn && decile > _lastSpokenDecile && decile < 100;
			if (speakNow) _lastSpokenDecile = decile;

			// Only touch the UI thread when there is actually something to change — Report can fire thousands of
			// times for a large download, so marshaling the title on every byte would flood the message queue.
			bool titleChanged = pct != _lastTitlePct;
			if (titleChanged) _lastTitlePct = pct;
			if (!speakNow && !titleChanged) return;

			Ui(() =>
			{
				if (speakNow) _form.Speak(Loc.T("progress.percentSpoken", decile));
				if (titleChanged) _form.SetProgressTitle(_name, _installing, pct);
			});
		}

		/// <summary>
		/// Marks the operation finished, playing the closing 100% tone. The spoken "done" is intentionally left to
		/// the caller's own success message (e.g. "X installed!") so there is never a redundant "100 percent".
		/// </summary>
		public void Complete()
		{
			if (_done) return;
			_done = true;
			if (TonesOn) _form._soundEngine.PlayTone(100);
		}

		/// <summary>Runs a UI action on the form's thread without blocking the background download/extract loop.</summary>
		private void Ui(Action action)
		{
			try
			{
				if (_form.IsDisposed) return;
				if (_form.InvokeRequired) _form.BeginInvoke(action);
				else action();
			}
			catch { /* the form may be closing mid-operation */ }
		}
	}
}
