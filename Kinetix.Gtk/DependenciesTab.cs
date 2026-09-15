using System;
using System.Collections.Generic;
using System.Linq;
using KinetixModManager;

namespace KinetixModManager.GtkHead;

/// <summary>
/// The Dependencies screen: which installed mods are missing something they need.
///
/// <para>
/// Another one that is almost entirely core-backed. <c>ModHealth.ResolveDependencies</c> works out whether
/// each declared dependency is present, switched on and new enough; <see cref="DependenciesView"/> decides
/// what is worth showing and in what order. The window is a list and a button.
/// </para>
///
/// <para>
/// It lists only trouble. A mod whose dependencies are all satisfied produces no row, because a list where
/// the nine hundred things that are fine bury the three that are not is unusable — and far more so by ear
/// than on screen, where at least the eye can skim.
/// </para>
/// </summary>
public sealed partial class MainWindow
{
	private readonly Gtk.ListBox _dependencies = Gtk.ListBox.New();
	private DependenciesView _dependenciesView = DependenciesView.Of(Array.Empty<GameMod>());

	private Gtk.Widget BuildDependenciesTab()
	{
		var box = Gtk.Box.New(Gtk.Orientation.Vertical, 6);

		var check = Gtk.Button.NewWithLabel(Loc.T("gtk.depsCheck"));
		check.OnClicked += (_, _) => CheckDependencies();
		box.Append(check);

		_dependencies.SetVexpand(true);

		var scroller = Gtk.ScrolledWindow.New();
		scroller.SetChild(_dependencies);
		scroller.SetVexpand(true);
		box.Append(scroller);

		return box;
	}

	private void CheckDependencies()
	{
		SetStatus(Loc.T("gtk.depsChecking"));

		List<GameMod> installed = InstalledAsMods();

		// The same resolution the Windows head runs, and the same version comparison, so the two builds never
		// disagree about whether a dependency is new enough.
		ModHealth.ResolveDependencies(installed, ModVersions.IsNewer);
		_dependenciesView = DependenciesView.Of(installed);

		while (_dependencies.GetFirstChild() is { } child) _dependencies.Remove(child);
		foreach (DependencyRow row in _dependenciesView.Rows) _dependencies.Append(RowLabel(row.Spoken));

		// The completed cue only when there is nothing wrong. Sounding "done" over a list of broken mods
		// would be the wrong fact, and the error cue over an optional dependency would be the other one.
		_sound.Play(_dependenciesView.SeriousCount > 0 ? "error" : "load_complete");

		SetStatus(_dependenciesView.Announcement);
		Say(_dependenciesView.Announcement, interrupt: true);
	}
}
