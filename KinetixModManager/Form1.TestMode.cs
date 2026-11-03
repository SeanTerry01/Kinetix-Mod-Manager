namespace KinetixModManager;

/// <summary>What only a test copy offers. See <see cref="TestMode"/>.</summary>
public partial class Form1
{
	private ToolStripMenuItem BuildTestModeMenu()
	{
		var menu = new ToolStripMenuItem(Loc.T("test.menu"));

		// Nexus's button belongs to the real copy, so this is how a Nexus link reaches a test one.
		menu.DropDownItems.Add(Loc.T("test.menuLink"), null, delegate
		{
			string link = "";
			try { link = Clipboard.GetText().Trim(); }
			catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
			{
				DiagnosticLog.WriteException("TestMode", "reading the clipboard", ex);
			}

			if (!link.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
			{
				Speak(Loc.T("test.noLink"));
				return;
			}
			Fire(HandleNxmUrl(link), "HandleNxmUrl");
		});

		menu.DropDownItems.Add(Loc.T("test.menuFree"), null, delegate
		{
			bool nowFree = TestMode.Current?.PretendFreeNexus != true;
			TestMode.SetPretendFreeNexus(nowFree);
			Speak(Loc.T(nowFree ? "test.freeOn" : "test.freeOff"));
		});

		return menu;
	}

	/// <summary>Says why a folder outside the test folder was refused.</summary>
	private void SayTestModeRefused(TestModeSettings test)
	{
		_soundEngine.Play("error");
		SpeakBox(Loc.T("test.folderRefused", test.Root), Loc.T("test.title"), MessageBoxButtons.OK);
	}
}
