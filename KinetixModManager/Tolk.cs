using System.Runtime.InteropServices;

namespace DavyKager;

public sealed class Tolk
{
	[DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
	private static extern void Tolk_Load();

	[DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool Tolk_IsLoaded();

	[DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
	private static extern void Tolk_Unload();

	[DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
	private static extern void Tolk_TrySAPI([MarshalAs(UnmanagedType.I1)] bool trySAPI);

	[DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
	private static extern nint Tolk_DetectScreenReader();

	[DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool Tolk_Output([MarshalAs(UnmanagedType.LPWStr)] string str, [MarshalAs(UnmanagedType.I1)] bool interrupt);

	[DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool Tolk_IsSpeaking();

	[DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.I1)]
	private static extern bool Tolk_Silence();

	private Tolk()
	{
	}

	public static void Load()
	{
		Tolk_Load();
	}

	public static bool IsLoaded()
	{
		return Tolk_IsLoaded();
	}

	public static void Unload()
	{
		Tolk_Unload();
	}

	public static void TrySAPI(bool trySAPI)
	{
		Tolk_TrySAPI(trySAPI);
	}

	public static string? DetectScreenReader()
	{
		nint num = Tolk_DetectScreenReader();
		if (num == IntPtr.Zero)
		{
			return null;
		}
		return Marshal.PtrToStringUni(num);
	}

	public static bool Output(string str, bool interrupt = false)
	{
		return Tolk_Output(str, interrupt);
	}

	public static bool IsSpeaking()
	{
		return Tolk_IsSpeaking();
	}

	public static bool Silence()
	{
		return Tolk_Silence();
	}
}
