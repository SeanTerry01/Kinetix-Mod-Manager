namespace KinetixModManager;

/// <summary>
/// Decides when a sentence has really finished, given a reader that can say whether it is speaking.
///
/// <para>
/// Asking "are you speaking?" once is not enough, for two reasons, and each has its own number here. A sentence
/// just handed over has not started yet, so the first answer is a no that means "not yet" — measured at 8 to 17ms
/// on NVDA 2026.3, and allowed more because a reader can be busy. And a no can be a breath between two queued
/// sentences rather than the end of them. NVDA was not seen to do that, but what it reports comes from the
/// synthesizer, and users choose their own.
/// </para>
///
/// <para>
/// Kept apart from any timer or window so the rule can be tested without a screen reader, and so the two waits that
/// use it — one that yields and one that pumps — cannot drift apart.
/// </para>
/// </summary>
public sealed class SpeechFinishWatch
{
	/// <summary>How long a reader gets to begin before silence is taken to mean it has nothing to say.</summary>
	public const int StartGraceMs = 300;

	/// <summary>How long it must stay quiet once it has spoken, so a gap between sentences does not count.</summary>
	public const int QuietMs = 150;

	private readonly long _startedAt;
	private readonly int _maxMs;
	private bool _heardSpeech;
	private long _lastSpeakingAt;

	/// <param name="startedAt">The clock's reading when the wait began, in milliseconds.</param>
	/// <param name="maxMs">When to give up regardless. A wait that never ends would be worse than a cut sentence.</param>
	public SpeechFinishWatch(long startedAt, int maxMs)
	{
		_startedAt = startedAt;
		_maxMs = maxMs;
	}

	/// <summary>Takes the reader's latest answer and says whether the wait is over.</summary>
	public bool IsFinished(long now, bool speaking)
	{
		if (now - _startedAt >= _maxMs) return true;

		if (speaking)
		{
			_heardSpeech = true;
			_lastSpeakingAt = now;
			return false;
		}

		return _heardSpeech
			? now - _lastSpeakingAt >= QuietMs
			: now - _startedAt >= StartGraceMs;
	}
}
