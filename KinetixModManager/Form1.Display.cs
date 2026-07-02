using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// The low-vision display modes: a high-contrast colour scheme and a global text-size multiplier, applied across
/// the whole UI. The main window is themed once at startup and re-themed whenever the user changes the setting;
/// transient dialogs call <see cref="StyleDialog"/> when they open so they match. Screen-reader output is
/// unaffected — this only changes what is drawn on screen, for users with some usable vision.
/// </summary>
public partial class Form1
{
	/// <summary>
	/// The designed (unscaled) font size of each themed control on the main window, captured the first time the
	/// theme is applied so re-applying a different text size scales from the original rather than compounding.
	/// </summary>
	private readonly Dictionary<Control, float> _baseFontSizes = new();
	private readonly Dictionary<ToolStripItem, float> _baseMenuFontSizes = new();

	/// <summary>True while a non-default display theme is applied to the main window, so we know a later switch back
	/// to the defaults still needs to run (to reset the colours/fonts) rather than being skipped as a no-op.</summary>
	private bool _displayThemeActive;

	/// <summary>The foreground/background pair for a contrast scheme, or <c>null</c> for "leave the normal colours".</summary>
	private static (Color Fore, Color Back)? ContrastColors(DisplayContrast c) => c switch
	{
		DisplayContrast.WhiteOnBlack  => (Color.White, Color.Black),
		DisplayContrast.YellowOnBlack => (Color.Yellow, Color.Black),
		DisplayContrast.BlackOnYellow => (Color.Black, Color.Yellow),
		_ => null
	};

	/// <summary>The text-size multiplier for the active setting (1.0 = unchanged).</summary>
	private float TextScaleFactor() => _settings.TextSize switch
	{
		TextSize.Large      => 1.25f,
		TextSize.ExtraLarge => 1.5f,
		_ => 1f
	};

	/// <summary>Applies the current display settings to the whole main window (control tree, menu, and status bar).</summary>
	private void ApplyDisplayTheme()
	{
		bool isDefault = _settings.DisplayContrast == DisplayContrast.Off && _settings.TextSize == TextSize.Normal;
		// At the defaults with no theme currently applied there is nothing to do — and skipping avoids resetting any
		// control that was intentionally given a custom colour. Once a non-default theme has been applied, we still
		// run when switching back to defaults so the reset actually happens.
		if (isDefault && !_displayThemeActive) return;
		_displayThemeActive = !isDefault;

		var colors = ContrastColors(_settings.DisplayContrast);
		float factor = TextScaleFactor();

		if (colors is { } c) { BackColor = c.Back; ForeColor = c.Fore; }
		else { ResetBackColor(); ResetForeColor(); }

		foreach (Control child in Controls)
			ThemeControlTree(child, colors, factor, _baseFontSizes);

		// The main menu isn't part of the Controls tree; theme it (and its dropdown items) separately.
		if (MainMenuStrip != null)
			ThemeMenu(MainMenuStrip, colors, factor);
	}

	/// <summary>
	/// Applies the current display settings to a transient dialog and its controls just before it is shown. Dialogs
	/// are rebuilt each time they open, so their fonts are always at the designed size — no base-size tracking is
	/// needed (the local dictionary is discarded with the dialog).
	/// </summary>
	private void StyleDialog(Form dialog)
	{
		// No-op when nothing is enabled, so a normal-vision user's dialogs are untouched.
		if (_settings.DisplayContrast == DisplayContrast.Off && _settings.TextSize == TextSize.Normal) return;

		var colors = ContrastColors(_settings.DisplayContrast);
		float factor = TextScaleFactor();
		if (colors is { } c) { dialog.BackColor = c.Back; dialog.ForeColor = c.Fore; }
		var scratch = new Dictionary<Control, float>();
		foreach (Control child in dialog.Controls)
			ThemeControlTree(child, colors, factor, scratch);
		if (dialog.MainMenuStrip != null)
			ThemeMenu(dialog.MainMenuStrip, colors, factor);
	}

	/// <summary>
	/// Recursively sets the contrast colours and scaled font on <paramref name="control"/> and its descendants.
	/// Font sizes scale from the value captured in <paramref name="baseSizes"/> on first visit, so repeated calls
	/// with different sizes don't compound. When <paramref name="colors"/> is null the control's colours are reset
	/// to the Windows defaults (this is how turning High Contrast back off restores the normal look at runtime).
	/// </summary>
	private static void ThemeControlTree(Control control, (Color Fore, Color Back)? colors, float factor, Dictionary<Control, float> baseSizes)
	{
		// --- font ---
		if (!baseSizes.TryGetValue(control, out float baseSize))
		{
			baseSize = control.Font.Size;
			baseSizes[control] = baseSize;
		}
		float targetSize = baseSize * factor;
		if (Math.Abs(control.Font.Size - targetSize) > 0.01f)
			control.Font = new Font(control.Font.FontFamily, targetSize, control.Font.Style);

		// --- colours ---
		if (colors is { } c)
		{
			control.BackColor = c.Back;
			control.ForeColor = c.Fore;
			// Standard buttons ignore BackColor under visual styles; flat style makes the contrast colour take.
			if (control is Button b) b.FlatStyle = FlatStyle.Flat;
		}
		else
		{
			control.ResetBackColor();
			control.ResetForeColor();
			if (control is Button b) b.FlatStyle = FlatStyle.Standard;
		}

		foreach (Control child in control.Controls)
			ThemeControlTree(child, colors, factor, baseSizes);
	}

	/// <summary>Applies the contrast colours and scaled font to a menu strip and every item in it, recursively.</summary>
	private void ThemeMenu(MenuStrip menu, (Color Fore, Color Back)? colors, float factor)
	{
		if (colors is { } c) { menu.BackColor = c.Back; menu.ForeColor = c.Fore; }
		else { menu.ResetBackColor(); menu.ResetForeColor(); }
		foreach (ToolStripItem item in menu.Items)
			ThemeMenuItem(item, colors, factor);
	}

	private void ThemeMenuItem(ToolStripItem item, (Color Fore, Color Back)? colors, float factor)
	{
		if (!_baseMenuFontSizes.TryGetValue(item, out float baseSize))
		{
			baseSize = item.Font.Size;
			_baseMenuFontSizes[item] = baseSize;
		}
		float targetSize = baseSize * factor;
		if (Math.Abs(item.Font.Size - targetSize) > 0.01f)
			item.Font = new Font(item.Font.FontFamily, targetSize, item.Font.Style);

		if (colors is { } c) { item.BackColor = c.Back; item.ForeColor = c.Fore; }
		else { item.ForeColor = SystemColors.ControlText; item.BackColor = SystemColors.Control; }

		if (item is ToolStripDropDownItem dd)
			foreach (ToolStripItem child in dd.DropDownItems)
				ThemeMenuItem(child, colors, factor);
	}
}
