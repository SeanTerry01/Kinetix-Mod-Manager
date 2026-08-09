using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// The "Support Development" view in the Help menu: a short message from the author and two ways to give.
///
/// Both destinations are ordinary web links, so both are a button that opens a browser. Cash App is the one
/// worth a note, because it is widely believed not to have one: choosing a $cashtag creates a public payment
/// page at <c>cash.app/$tag</c>, which is exactly the equivalent of a PayPal.Me link. Verified against the real
/// tag before this was written, rather than assumed.
///
/// The cashtag can also be copied on its own. That is not a fallback for a missing link — it is for the case
/// the link cannot serve: Cash App is a phone app, and someone reading this on a desktop very often wants the
/// tag in hand to type into the phone in their other hand. A link cannot cross that gap; the clipboard can.
/// </summary>
public partial class Form1
{
	/// <summary>Where the PayPal button goes. Not localized — a payment address is the same in every language.</summary>
	private const string PayPalUrl = "https://paypal.me/chipper15";

	/// <summary>The Cash App tag, and the public payment page it creates. Kept next to each other so the two can
	/// never drift apart — the page is nothing but the tag with a prefix.</summary>
	private const string CashTag = "$SeanTerry01";
	private const string CashAppUrl = "https://cash.app/" + CashTag;

	private void ShowDonate()
	{
		string body = Loc.T("donate.body").Replace("\n", Environment.NewLine);

		// Shown inside the main window rather than as one of its own — see Form1.InlineView.
		ShowInlineView(Loc.T("donate.viewTitle"), (container, closeView) =>
		{
			TableLayoutPanel layout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				Padding = new Padding(15),
				ColumnCount = 1,
				RowCount = 2
			};
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));

			// Read-only multiline box so a screen reader can read or arrow through the message line by line —
			// the same shape as About, which is the view this most resembles.
			TextBox tbMessage = new TextBox
			{
				Dock = DockStyle.Fill,
				Multiline = true,
				ReadOnly = true,
				ScrollBars = ScrollBars.Vertical,
				Font = new Font("Segoe UI", 12f),
				Text = body,
				// A real name, not a blank one and not none. A control the user lands on must be named or the
				// reader goes looking for a name nearby and reads the main window's search box — this box
				// announced itself as "Search" until it was given one. It is named for what it holds rather than
				// repeating the heading, so nothing is said twice. See SilentAccessibleName in Form1.Helpers.
				AccessibleName = Loc.T("donate.messageName"),
				TabStop = true
			};
			tbMessage.GotFocus += delegate { tbMessage.Select(0, 0); };

			// Left to right in the order most people will want them, and the order they are Tabbed through.
			FlowLayoutPanel buttons = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.LeftToRight,
				WrapContents = false,
				AccessibleName = " "
			};

			buttons.Controls.Add(MakeDonateButton(Loc.T("donate.paypal"), Loc.T("donate.paypalName"),
				delegate { OpenDonateLink(PayPalUrl, Loc.T("donate.paypalService")); }));

			buttons.Controls.Add(MakeDonateButton(Loc.T("donate.cashApp"), Loc.T("donate.cashAppName"),
				delegate { OpenDonateLink(CashAppUrl, Loc.T("donate.cashAppService")); }));

			buttons.Controls.Add(MakeDonateButton(Loc.T("donate.copyTag"), Loc.T("donate.copyTagName"),
				delegate
				{
					// Spoken rather than shown in a box: this is a confirmation, not a decision, and a dialog
					// here would take focus off the buttons the user is still working through.
					if (TryCopy(CashTag)) Speak(Loc.T("donate.copiedTag", CashTag));
					else Speak(Loc.T("donate.copyFailed", CashTag));
				}));

			Button btnClose = new Button
			{
				Text = Loc.T("common.close"),
				AutoSize = true,
				Height = 45,
				Margin = new Padding(6, 0, 6, 0),
				Font = new Font("Segoe UI", 12f, FontStyle.Bold),
				AccessibleName = Loc.T("donate.close")
			};
			btnClose.Click += delegate { closeView(); };
			buttons.Controls.Add(btnClose);

			layout.Controls.Add(tbMessage, 0, 0);
			layout.Controls.Add(buttons, 0, 1);
			container.Controls.Add(layout);

			// Focus starts on the message so it is read first — the buttons are one Tab away, and a user who
			// opened this menu item already knows what they came for.
			return tbMessage;
		}, hint: Loc.T("donate.hint"));
	}

	/// <summary>One button in the row, sized to its own text so a longer translation is not clipped.</summary>
	private static Button MakeDonateButton(string text, string accessibleName, EventHandler onClick)
	{
		Button b = new Button
		{
			Text = text,
			AutoSize = true,
			Height = 45,
			Margin = new Padding(6, 0, 6, 0),
			Font = new Font("Segoe UI", 12f, FontStyle.Bold),
			AccessibleName = accessibleName
		};
		b.Click += onClick;
		return b;
	}

	/// <summary>
	/// Opens a payment page, and says so — a browser can take a moment to come up, and silence in the meantime
	/// reads as a button that did nothing.
	///
	/// When no browser can be started the link is put on the clipboard instead of reporting a dead end. That is
	/// the difference between "this feature is broken" and "paste this somewhere" — and it is a real case, not a
	/// theoretical one: under Wine there may be no Windows browser registered at all.
	/// </summary>
	private void OpenDonateLink(string url, string serviceName)
	{
		try
		{
			Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
			Speak(Loc.T("donate.opening", serviceName));
		}
		catch (Exception ex)
		{
			LogError("Donate", $"Could not open {url}: {ex.Message}");

			if (TryCopy(url)) SpeakBox(Loc.T("donate.openFailedCopied", serviceName, url), Loc.T("donate.viewTitle"));
			else SpeakBox(Loc.T("donate.openFailed", serviceName, url), Loc.T("donate.viewTitle"));
		}
	}

	/// <summary>
	/// Puts text on the clipboard, reporting whether it landed. The clipboard is a shared resource another
	/// program can hold open, so this genuinely fails sometimes; claiming a copy that did not happen would leave
	/// someone pasting the wrong thing into a payment field.
	/// </summary>
	private bool TryCopy(string text)
	{
		try
		{
			Clipboard.SetText(text);
			return true;
		}
		catch (Exception ex)
		{
			LogError("Donate", $"Could not write to the clipboard: {ex.Message}");
			return false;
		}
	}
}
