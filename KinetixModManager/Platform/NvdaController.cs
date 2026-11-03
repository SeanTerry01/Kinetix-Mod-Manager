using System.Runtime.InteropServices;

namespace KinetixModManager;

/// <summary>
/// The one thing the manager asks NVDA directly instead of through Tolk: whether it is speaking.
///
/// <para>
/// Tolk's own <c>IsSpeaking</c> answers no while NVDA is talking, because NVDA had no way to say otherwise until
/// 2026.3 added <c>nvdaController_isSpeaking</c> to its controller client. This binds the same
/// <c>nvdaControllerClient64.dll</c> Tolk loads for its speech, so it resolves from the same place.
/// </para>
///
/// <para>
/// Calling this costs almost nothing, so nothing is cached: measured at about 0.02ms with NVDA running and 0.1ms
/// without, and an NVDA that is upgraded or restarted while the manager is open starts answering on the next call.
/// </para>
/// </summary>
internal static class NvdaController
{
	/// <summary>Older NVDA: the client reached it, and it does not know this call. RPC_S_UNKNOWN_IF.</summary>
	private const int NvdaTooOld = 1717;

	/// <summary>NVDA is not running. RPC_S_SERVER_UNAVAILABLE.</summary>
	private const int NvdaNotRunning = 1722;

	[DllImport("nvdaControllerClient64.dll", CallingConvention = CallingConvention.StdCall)]
	private static extern int nvdaController_isSpeaking([MarshalAs(UnmanagedType.I1)] out bool speaking);

	/// <summary>Set once a broken client has been logged, because this is polled and the log would fill.</summary>
	private static bool _reportedUnusableClient;

	/// <summary>
	/// Asks NVDA whether it is producing speech. Returns <c>false</c> when it cannot answer, which callers treat
	/// as not knowing: <paramref name="speaking"/> says nothing in that case. Never throws.
	/// </summary>
	public static bool TryIsSpeaking(out bool speaking)
	{
		speaking = false;
		try
		{
			int status = nvdaController_isSpeaking(out speaking);
			if (status == 0) return true;

			if (status is not (NvdaTooOld or NvdaNotRunning))
				ReportUnusableClient("it returned error " + status);
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
		{
			// Speech itself may be fine: Tolk loads the client separately, and an older copy left beside the program
			// speaks perfectly well while lacking this call. The cost is only the old timing.
			ReportUnusableClient(ex.GetType().Name);
		}

		speaking = false;
		return false;
	}

	private static void ReportUnusableClient(string why)
	{
		if (_reportedUnusableClient) return;
		_reportedUnusableClient = true;
		DiagnosticLog.Write("Speech", "Could not ask NVDA whether it is speaking: " + why);
	}
}
