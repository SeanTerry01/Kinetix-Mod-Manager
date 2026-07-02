using System;
using System.Runtime.InteropServices;

namespace KinetixModManager;

/// <summary>
/// Runtime host detection. Currently exposes whether the app is running under Wine (or a Wine-based
/// runner such as Proton or Soda), which lets us soften Windows-only behaviours that Wine implements
/// incompletely — notably the per-thread DPI awareness APIs that crash WinForms when menus open.
/// </summary>
internal static class PlatformInfo
{
	private static readonly Lazy<bool> _isWine = new Lazy<bool>(DetectWine);

	/// <summary>True when running under Wine or a Wine-based compatibility layer.</summary>
	public static bool IsWine => _isWine.Value;

	[DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
	private static extern IntPtr GetModuleHandleA(string lpModuleName);

	[DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
	private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

	private static bool DetectWine()
	{
		try
		{
			IntPtr ntdll = GetModuleHandleA("ntdll.dll");
			if (ntdll == IntPtr.Zero) return false;
			// wine_get_version is exported only by Wine's ntdll; it never exists on real Windows.
			return GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
		}
		catch
		{
			return false;
		}
	}
}
