using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KinetixModManager;

namespace KinetixModManager.GtkHead;

/// <summary>
/// The Log screen: what the game's mod loader said last time it ran.
///
/// <para>
/// It opens on problems rather than on everything, which is the one decision that makes a log usable by ear.
/// A SMAPI log is thousands of lines of a game starting normally; a list that begins at line one asks the
/// user to arrow through all of it to reach the four lines explaining the crash.
/// </para>
///
/// <para>
/// Every rule about what a line means is <see cref="LogAnalyzer"/>'s, and has been in the core since Phase 4
/// — including the fix suggestions, which is why each row can carry one without this window knowing anything
/// about SMAPI.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
	private readonly Gtk.ListBox _log = Gtk.ListBox.New();
	private GameLogView _logView = GameLogView.NoLog();
	private LogFilter _logFilter = LogFilter.Problems;

	private Gtk.Widget BuildLogTab()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 6);

		var buttons = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);

		var read = Gtk.Button.NewWithLabel(Loc.T("gtk.logRead"));
		read.OnClicked += (_, _) => LoadLog();
		buttons.Append(read);

		var filterLabel = Gtk.Label.NewWithMnemonic(Loc.T("gtk.logShow"));
		buttons.Append(filterLabel);

		var filter = Gtk.DropDown.NewFromStrings(new[]
		{
			Loc.T("gtk.logProblems"), Loc.T("gtk.logErrors"), Loc.T("gtk.logEverything"),
		});
		filter.SetSelected(0);
		filter.OnNotify += (_, args) =>
		{
			if (args.Pspec.GetName() != "selected") return;

			_logFilter = filter.GetSelected() switch
			{
				1 => LogFilter.ErrorsOnly,
				2 => LogFilter.Everything,
				_ => LogFilter.Problems,
			};
			LoadLog();
		};
		filterLabel.SetMnemonicWidget(filter);
		buttons.Append(filter);

		box.Append(buttons);

		_log.SetVexpand(true);

		var scroller = Gtk.ScrolledWindow.New();
		scroller.SetChild(_log);
		scroller.SetVexpand(true);
		box.Append(scroller);

		return box;
	}

	private void LoadLog()
	{
		string path = LogPathForGame();

		if (string.IsNullOrEmpty(path) || !File.Exists(path))
		{
			// No log is a different answer from a log with nothing wrong in it: one is a game that has not
			// been run since the loader was installed, the other is a game that ran fine.
			_logView = GameLogView.NoLog();
		}
		else
		{
			try
			{
				// Read with sharing, because the game may still be running and holding it open — which is
				// exactly when somebody wants to read it.
				_logView = GameLogView.Of(GameLogFiles.ReadAllLinesShared(path), _logFilter);
			}
			catch (Exception ex)
			{
				DiagnosticLog.WriteException("Log", $"reading {path}", ex);
				_logView = GameLogView.NoLog();
			}
		}

		while (_log.GetFirstChild() is { } child) _log.Remove(child);
		foreach (GameLogRow row in _logView.Rows) _log.Append(RowLabel(row.Spoken));

		SetStatus(_logView.Announcement);
		Say(_logView.Announcement, interrupt: true);
	}

	/// <summary>
	/// Where this game's loader writes its log, or empty for a game that keeps none.
	///
	/// Stardew's SMAPI log lives in the player's own folder rather than under the game, which is why the
	/// core answers this rather than it being built from the install path here.
	/// </summary>
	private string LogPathForGame()
	{
		if (!GameLogFiles.HasLoaderLog(_game)) return "";

		string install = _locator.InstallFolder(_game) ?? "";
		string minecraftRoot = _game.IsMinecraft
			? (_locator.InstallFolder(_game) ?? MinecraftLayout.DefaultRootFolder)
			: "";

		return GameLogFiles.LoaderLogPath(_game, install, minecraftRoot);
	}
}
