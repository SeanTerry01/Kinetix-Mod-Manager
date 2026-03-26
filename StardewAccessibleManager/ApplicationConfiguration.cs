using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace StardewAccessibleManager;

[CompilerGenerated]
internal static class ApplicationConfiguration
{
	public static void Initialize()
	{
		Application.EnableVisualStyles();
		Application.SetCompatibleTextRenderingDefault(defaultValue: false);
		Application.SetHighDpiMode(HighDpiMode.SystemAware);
	}
}
