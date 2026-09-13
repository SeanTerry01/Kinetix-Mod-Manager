using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KinetixModManager;

namespace KinetixModManager.GtkHead;

/// <summary>
/// The one window. Two tabs: the mods that are installed, and searching Modrinth for more.
///
/// <para>
/// Every decision here is about what a screen reader will make of it, because that is the only interface
/// this program really has. A row is a single label holding a whole sentence rather than a grid of cells,
/// so arrowing down a list gives one fact per press instead of three. Focus moves are announced explicitly,
/// because Orca will describe the widget that gained focus but not why it did. Nothing depends on a colour,
/// a position, or a pointer.
/// </para>
///
/// <para>
/// It covers Minecraft only, deliberately. Minecraft Java is the one supported game that runs natively on
/// Linux <em>and</em> whose accessibility mods speak here — Minecraft Access uses speech-dispatcher, the same
/// speech-dispatcher this window talks to. Skyrim and Fallout 4 would run under Proton, but their access mods
/// speak by driving NVDA, which does not exist on Linux, so the game would start and then say nothing.
/// </para>
/// </summary>
public sealed class MainWindow
{
	private readonly Gtk.ApplicationWindow _window;
	private readonly IAnnouncer _announcer = new SpeechDispatcherAnnouncer();
	private readonly IDispatcher _ui = new GlibDispatcher();

	private readonly Gtk.Notebook _tabs = Gtk.Notebook.New();
	private readonly Gtk.ListBox _installed = Gtk.ListBox.New();
	private readonly Gtk.ListBox _results = Gtk.ListBox.New();
	private readonly Gtk.Entry _search = Gtk.Entry.New();
	private readonly Gtk.Label _status = Gtk.Label.New("");

	private readonly List<ModRow> _rows = new();
	private readonly List<GameMod> _found = new();
	private string _modsFolder = "";

	public MainWindow(Gtk.Application app)
	{
		_window = Gtk.ApplicationWindow.New(app);
		_window.SetTitle("Kinetix Mod Manager — Minecraft");
		_window.SetDefaultSize(900, 620);

		var root = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
		root.SetMarginTop(8); root.SetMarginBottom(8);
		root.SetMarginStart(8); root.SetMarginEnd(8);

		_tabs.SetVexpand(true);
		_tabs.AppendPage(BuildInstalledTab(), Gtk.Label.New("Installed Mods"));
		_tabs.AppendPage(BuildDiscoverTab(), Gtk.Label.New("Find Mods"));
		root.Append(_tabs);

		// A status line that is also spoken. On its own a label change is silent to a screen reader — Orca
		// has no reason to look at it — so anything worth putting here is worth saying out loud as well.
		_status.SetXalign(0);
		root.Append(_status);

		_window.SetChild(root);
		AddKeyboardShortcuts();

		LoadInstalled();
	}

	public void Present()
	{
		_window.Present();
		// Said rather than shown. The window title is announced by Orca on focus, but the count of what was
		// found is the thing the user actually opened the program to learn.
		Say($"Kinetix Mod Manager. {_rows.Count} Minecraft mods installed. Press F6 to move between the tabs and the list.");
	}

	// -------------------------------------------------------------------------
	// Installed
	// -------------------------------------------------------------------------

	private Gtk.Widget BuildInstalledTab()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 6);

		var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
		var toggle = Gtk.Button.NewWithLabel("Enable or disable (Space)");
		toggle.OnClicked += (_, _) => ToggleSelected();
		var refresh = Gtk.Button.NewWithLabel("Refresh (F5)");
		refresh.OnClicked += (_, _) => { LoadInstalled(); Say($"{_rows.Count} mods installed."); };
		buttons.Append(toggle);
		buttons.Append(refresh);
		box.Append(buttons);

		_installed.SetVexpand(true);
		// Announce the row the user has landed on. GTK tells Orca a row is selected, but the sentence the
		// row carries is what matters, and saying it ourselves is also what lets the wording match the
		// Windows build exactly.
		_installed.OnRowSelected += (_, args) =>
		{
			int i = args.Row?.GetIndex() ?? -1;
			if (i >= 0 && i < _rows.Count) Say(_rows[i].Spoken, interrupt: true);
		};

		var scroller = Gtk.ScrolledWindow.New();
		scroller.SetChild(_installed);
		scroller.SetVexpand(true);
		box.Append(scroller);
		return box;
	}

	private void LoadInstalled()
	{
		_rows.Clear();
		while (_installed.GetFirstChild() is { } child) _installed.Remove(child);

		string root = MinecraftLayout.DefaultRootFolder;
		_modsFolder = MinecraftLayout.ModsFolderFor(root);

		if (!Directory.Exists(_modsFolder))
		{
			SetStatus($"No Minecraft mods folder at {_modsFolder}.");
			return;
		}

		foreach (string jar in Directory.EnumerateFiles(_modsFolder)
			.Where(f => f.EndsWith(MinecraftLayout.ModExtension, StringComparison.OrdinalIgnoreCase)
					 || f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
			.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
		{
			// Straight out of the core: the same reader the WinForms build uses, unchanged.
			FabricModInfo info = MinecraftLayout.ReadModInfo(jar);
			_rows.Add(new ModRow { JarPath = jar, Info = info, Enabled = MinecraftLayout.IsEnabledModFile(jar) });
		}

		foreach (ModRow row in _rows) _installed.Append(RowLabel(row.Spoken));
		SetStatus($"{_rows.Count} mods in {_modsFolder}");
	}

	private void ToggleSelected()
	{
		int i = _installed.GetSelectedRow()?.GetIndex() ?? -1;
		if (i < 0 || i >= _rows.Count) { Say("No mod is selected.", interrupt: true); return; }

		ModRow row = _rows[i];
		// The rule for what a disabled mod is called lives in the core, not here. Fabric accepts a candidate
		// only when it ends in ".jar", so the suffix is what takes a mod out of the running.
		string target = MinecraftLayout.PathWithEnabled(row.JarPath, !row.Enabled, ".disabled");

		try
		{
			File.Move(row.JarPath, target);
			LoadInstalled();
			_installed.SelectRow(_installed.GetRowAtIndex(Math.Min(i, Math.Max(0, _rows.Count - 1))));
			Say($"{row.Info.Name} {(row.Enabled ? "disabled" : "enabled")}.", interrupt: true);
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Mods", $"toggling {row.JarPath}", ex);
			Say($"Could not change {row.Info.Name}. {ex.Message}", interrupt: true);
		}
	}

	// -------------------------------------------------------------------------
	// Discover
	// -------------------------------------------------------------------------

	private Gtk.Widget BuildDiscoverTab()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 6);

		var bar = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
		// A visible label bound to the entry: that is what gives the entry an accessible name, and it is
		// why this is a Label with a mnemonic rather than placeholder text, which Orca does not treat as one.
		var caption = Gtk.Label.NewWithMnemonic("_Search Modrinth:");
		caption.SetMnemonicWidget(_search);
		_search.SetHexpand(true);
		_search.OnActivate += (_, _) => _ = SearchAsync();
		var go = Gtk.Button.NewWithLabel("Search");
		go.OnClicked += (_, _) => _ = SearchAsync();
		bar.Append(caption); bar.Append(_search); bar.Append(go);
		box.Append(bar);

		_results.SetVexpand(true);
		_results.OnRowSelected += (_, args) =>
		{
			int i = args.Row?.GetIndex() ?? -1;
			if (i >= 0 && i < _found.Count)
				Say($"{_found[i].Name}. {Trim(_found[i].Description)}", interrupt: true);
		};

		var scroller = Gtk.ScrolledWindow.New();
		scroller.SetChild(_results);
		scroller.SetVexpand(true);
		box.Append(scroller);
		return box;
	}

	private async Task SearchAsync()
	{
		string term = _search.GetBuffer().GetText();
		if (string.IsNullOrWhiteSpace(term)) { Say("Type something to search for first.", interrupt: true); return; }

		SetStatus($"Searching Modrinth for {term}…");
		Say($"Searching for {term}.", interrupt: true);

		try
		{
			// Live, from the core, with no API key — which is the reason Minecraft is the sensible first
			// game for a Linux build. Every other supported game needs a Nexus key.
			var (results, total) = await ModrinthService.SearchAsync(term, "1.21.1", 0, 25);

			_ui.Post(() =>
			{
				_found.Clear(); _found.AddRange(results);
				while (_results.GetFirstChild() is { } child) _results.Remove(child);
				foreach (GameMod m in _found) _results.Append(RowLabel($"{m.Name} — {Trim(m.Description)}"));

				SetStatus($"{_found.Count} of {total} results for {term}");
				Say($"{_found.Count} results. Press Tab then arrow down to read them.");
			});
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Modrinth", $"searching for {term}", ex);
			_ui.Post(() => { SetStatus("Search failed."); Say($"Search failed. {ex.Message}", interrupt: true); });
		}
	}

	// -------------------------------------------------------------------------
	// Shared
	// -------------------------------------------------------------------------

	/// <summary>A row that is exactly one label, so a screen reader reads one sentence per arrow press.</summary>
	private static Gtk.Widget RowLabel(string text)
	{
		var label = Gtk.Label.New(text);
		label.SetXalign(0);
		label.SetMarginTop(4); label.SetMarginBottom(4);
		label.SetMarginStart(6); label.SetMarginEnd(6);
		label.SetEllipsize(Pango.EllipsizeMode.End);
		return label;
	}

	private static string Trim(string s) =>
		string.IsNullOrEmpty(s) ? "" : (s.Length <= 140 ? s : s.Substring(0, 140) + "…");

	private void SetStatus(string text) => _status.SetText(text);

	private void Say(string text, bool interrupt = false) => _announcer.Speak(text, interrupt);

	/// <summary>
	/// The keys the Windows build uses, kept the same on purpose: someone who has learned one should not have
	/// to learn the other. F6 cycles focus, F5 refreshes, Ctrl+F goes to the search box, Space toggles a mod.
	/// </summary>
	private void AddKeyboardShortcuts()
	{
		var keys = Gtk.EventControllerKey.New();
		keys.OnKeyPressed += (_, args) =>
		{
			bool ctrl = (args.State & Gdk.ModifierType.ControlMask) != 0;

			switch (args.Keyval)
			{
				case 0xFFC3:                     // F6
					CycleFocus();
					return true;
				case 0xFFC2:                     // F5
					LoadInstalled();
					Say($"Refreshed. {_rows.Count} mods installed.", interrupt: true);
					return true;
				case 0x066 when ctrl:            // Ctrl+F
					_tabs.SetCurrentPage(1);
					_search.GrabFocus();
					Say("Search Modrinth.", interrupt: true);
					return true;
				case 0x020 when _installed.HasFocus:   // Space
					ToggleSelected();
					return true;
			}
			return false;
		};
		_window.AddController(keys);
	}

	/// <summary>
	/// Moves focus on, and says where it went.
	///
	/// The saying is the point. Orca announces the widget that receives focus, but a list it has already
	/// described reads as a bare row, and the user is left working out whether the key did anything.
	/// </summary>
	private void CycleFocus()
	{
		if (_tabs.GetCurrentPage() == 0)
		{
			if (_installed.HasFocus) { _tabs.GrabFocus(); Say("Tabs.", interrupt: true); }
			else { _installed.GrabFocus(); Say("Installed mods list.", interrupt: true); }
		}
		else
		{
			if (_search.HasFocus) { _results.GrabFocus(); Say("Results list.", interrupt: true); }
			else { _search.GrabFocus(); Say("Search box.", interrupt: true); }
		}
	}
}
