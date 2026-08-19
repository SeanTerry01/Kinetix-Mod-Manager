namespace KinetixModManager;

/// <summary>
/// A tab strip that lines its accessible children up with the tab numbering its own window sends out, so a screen
/// reader announces the tab you just moved to.
///
/// <para>
/// A WinForms <see cref="TabControl"/> is a native tab window with a managed accessible tree laid over it, and the
/// two disagree about what child number one is. The native window numbers only the tabs, so arrowing onto the
/// second tab sends a focus event for child two. The managed tree puts the selected page's *content* first and the
/// tab headers after it, so child two is the first tab. Every announcement therefore arrives pointing one tab
/// behind, at an object that reports itself as neither focused nor selected — measured on a stock TabControl:
/// </para>
/// <code>
/// pressing Right, moving from "Installed Mods" to "Browse":
///   EVENT FOCUS idChild=2 -> role=page tab name="Installed Mods" state=focusable+selectable
/// </code>
/// <para>
/// A reader that checks whether the object it was handed actually has the focus finds that it does not, discards
/// the event, and asks the window who has the focus instead. What answers is the strip itself — which is why every
/// tab change was read as "tab control" before the tab's name, on the main window and in Settings alike. It was
/// never the reader's naming of the strip: silencing the name and removing it were both tried, and neither could
/// have helped, because the strip is not what the reader was being pointed at.
/// </para>
/// <para>
/// Putting the tabs first and the page's content last is the whole fix. Child one is then the first tab, matching
/// the window's own numbering, and the same key press now reports:
/// </para>
/// <code>
///   EVENT FOCUS idChild=2 -> role=page tab name="Browse" state=selected+focused+focusable+selectable
/// </code>
/// <para>
/// The content pane keeps its place in the tree, just at the end, so object navigation into the page still works.
/// </para>
/// </summary>
internal class AccessibleTabControl : TabControl
{
	protected override AccessibleObject CreateAccessibilityInstance() => new StripAccessibleObject(this);

	/// <summary>The strip itself: a list of page tabs, with the selected page's content bringing up the rear.</summary>
	private sealed class StripAccessibleObject : ControlAccessibleObject
	{
		private readonly AccessibleTabControl _tabs;

		public StripAccessibleObject(AccessibleTabControl tabs) : base(tabs) => _tabs = tabs;

		public override AccessibleRole Role => AccessibleRole.PageTabList;

		public override int GetChildCount() => _tabs.TabCount + (_tabs.SelectedTab is null ? 0 : 1);

		// One object per position, kept for the strip's lifetime. A reader compares the object it is handed against
		// the one it already has to decide whether anything moved; handing out a fresh object each time makes every
		// query look like a change. Each one reads its page live, so tabs coming and going as the game changes need
		// no invalidation.
		private readonly Dictionary<int, TabAccessibleObject> _tabObjects = new();

		public override AccessibleObject? GetChild(int index)
		{
			if (index >= 0 && index < _tabs.TabCount)
			{
				if (!_tabObjects.TryGetValue(index, out TabAccessibleObject? tab))
					_tabObjects[index] = tab = new TabAccessibleObject(this, _tabs, index);
				return tab;
			}

			// One past the last tab is the selected page's content, kept reachable but out of the tabs' numbering.
			if (index == _tabs.TabCount) return _tabs.SelectedTab?.AccessibilityObject;
			return null;
		}

		/// <summary>
		/// The tab headers are not windows of their own, so the strip holds the keyboard focus and has to name the
		/// selected tab as the thing within it that has it. Answering with the strip is what produced the stray
		/// "tab control".
		/// </summary>
		public override AccessibleObject? GetFocused() => _tabs.Focused ? GetSelected() : null;

		public override AccessibleObject? GetSelected() =>
			_tabs.SelectedIndex >= 0 ? GetChild(_tabs.SelectedIndex) : null;

		public override AccessibleObject? HitTest(int x, int y)
		{
			Point client = _tabs.PointToClient(new Point(x, y));
			for (int i = 0; i < _tabs.TabCount; i++)
				if (_tabs.GetTabRect(i).Contains(client)) return GetChild(i);

			return base.HitTest(x, y);
		}
	}

	/// <summary>One tab header. Its name is the page's caption, which is what the reader speaks.</summary>
	private sealed class TabAccessibleObject : AccessibleObject
	{
		private readonly AccessibleObject _strip;
		private readonly AccessibleTabControl _tabs;
		private readonly int _index;

		public TabAccessibleObject(AccessibleObject strip, AccessibleTabControl tabs, int index)
		{
			_strip = strip;
			_tabs = tabs;
			_index = index;
		}

		// Tabs are added and removed as the game changes, so nothing here caches the page.
		private TabPage? Page => _index >= 0 && _index < _tabs.TabCount ? _tabs.TabPages[_index] : null;

		public override AccessibleObject Parent => _strip;

		public override AccessibleRole Role => AccessibleRole.PageTab;

		public override string? Name
		{
			get => Page?.Text;
			set { }
		}

		/// <summary>Empty rather than null: a tab's caption is its name, and repeating it as a value says it twice.</summary>
		public override string? Value
		{
			get => string.Empty;
			set { }
		}

		public override string DefaultAction => Loc.T("common.switchToTab");

		public override AccessibleStates State
		{
			get
			{
				AccessibleStates state = AccessibleStates.Selectable | AccessibleStates.Focusable;
				if (_tabs.SelectedIndex != _index) return state;

				state |= AccessibleStates.Selected;
				// The strip holds the focus, so only the selected tab can claim it, and only while it does.
				if (_tabs.Focused) state |= AccessibleStates.Focused;
				return state;
			}
		}

		public override Rectangle Bounds =>
			Page is null ? Rectangle.Empty : _tabs.RectangleToScreen(_tabs.GetTabRect(_index));

		public override void DoDefaultAction()
		{
			if (Page is not null) _tabs.SelectedIndex = _index;
		}

		public override void Select(AccessibleSelection flags)
		{
			if (Page is null) return;

			if ((flags & (AccessibleSelection.TakeSelection | AccessibleSelection.TakeFocus)) != 0)
			{
				_tabs.SelectedIndex = _index;
				if ((flags & AccessibleSelection.TakeFocus) != 0) _tabs.Focus();
			}
		}
	}
}
