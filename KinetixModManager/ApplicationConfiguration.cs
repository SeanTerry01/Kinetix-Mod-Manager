using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace KinetixModManager;

[CompilerGenerated]
internal static class ApplicationConfiguration
{
	public static void Initialize()
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		// Under Wine the per-thread DPI awareness APIs (GetThreadDpiAwarenessContext) are implemented
		// incompletely and throw when WinForms opens a menu/dropdown, which closes the whole app
		// (Wine bug 56042). Running DPI-unaware makes WinForms skip those calls entirely. On real
		// Windows we keep SystemAware for crisp rendering; unaware mode's only downside is mild blur on
		// high-DPI displays, which doesn't matter to a screen-reader user. This lets Linux/Wine users
		// run the app without changing their Wine configuration.
		Application.SetHighDpiMode(PlatformInfo.IsWine ? HighDpiMode.DpiUnaware : HighDpiMode.SystemAware);
	}
}
