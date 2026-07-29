using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// Turns raw exceptions into short, spoken-friendly explanations of *why* an action failed (disk full, access
/// denied, file in use, network problem, and so on) instead of a bare or cryptic message. Screen-reader users
/// can't scan a stack trace, so a one-line cause that names the likely fix is far more useful than the default
/// text. <see cref="FriendlyError"/> maps the exception; <see cref="SpeakError"/> plays the error cue and speaks
/// a "couldn't do X: reason" line in one call.
/// </summary>
public partial class Form1
{
	// Win32 error codes surfaced in IOException.HResult (low 16 bits), via the 0x8007xxxx facility.
	private const int ERROR_DISK_FULL      = 0x70;
	private const int ERROR_HANDLE_DISK_FULL = 0x27;
	private const int ERROR_SHARING_VIOLATION = 0x20;
	private const int ERROR_LOCK_VIOLATION = 0x21;

	/// <summary>
	/// Returns a short, localized explanation of the most likely cause of <paramref name="ex"/>. Falls back to the
	/// exception's own message when the type isn't one of the common, explainable cases, so no information is lost.
	/// </summary>
	private static string FriendlyError(Exception ex)
	{
		// Unwrap the wrappers that hide the real cause (aggregated/awaited faults).
		while ((ex is AggregateException or TargetInvocationException) && ex.InnerException != null)
			ex = ex.InnerException;

		switch (ex)
		{
			case ModFileSystem.ModArchiveContentException:
				return Loc.T("err.noModInArchive");
			case UnauthorizedAccessException:
				return Loc.T("err.accessDenied");
			case PathTooLongException:
				return Loc.T("err.pathTooLong");
			case DirectoryNotFoundException:
			case FileNotFoundException:
				return Loc.T("err.notFound");
			case IOException io:
			{
				int code = io.HResult & 0xFFFF;
				if (code == ERROR_DISK_FULL || code == ERROR_HANDLE_DISK_FULL) return Loc.T("err.diskFull");
				if (code == ERROR_SHARING_VIOLATION || code == ERROR_LOCK_VIOLATION) return Loc.T("err.fileInUse");
				return string.IsNullOrWhiteSpace(io.Message) ? Loc.T("err.io") : io.Message;
			}
			case TaskCanceledException:
			case TimeoutException:
				return Loc.T("err.timedOut");
			case HttpRequestException:
				return Loc.T("err.network");
			default:
				return string.IsNullOrWhiteSpace(ex.Message) ? Loc.T("err.generic") : ex.Message;
		}
	}

	/// <summary>
	/// Plays the error cue and speaks "<paramref name="what"/>: <em>reason</em>", where the reason comes from
	/// <see cref="FriendlyError"/>. Use for user-initiated actions that previously failed silently or with only a
	/// raw message. <paramref name="what"/> is an already-localized short action phrase (e.g. "Couldn't delete the mod").
	/// </summary>
	private void SpeakError(string what, Exception ex)
	{
		_soundEngine.Play("error");
		Speak(Loc.T("err.actionFailed", what, FriendlyError(ex)));
	}
}
