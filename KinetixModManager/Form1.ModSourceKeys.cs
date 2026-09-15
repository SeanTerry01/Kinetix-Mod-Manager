using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// The mod-source keys screen: which sites want setting up, and setting them up.
///
/// <para>
/// Its own screen rather than a row on a settings tab, because a key is about the user's account with a site
/// and not about any one game. Burying each key in the settings of the games that use it would mean somebody
/// who plays Minecraft could not find the Nexus key they set up a year ago.
/// </para>
///
/// <para>
/// One list, and Enter does the obvious thing to the row you are on: add a key where there is none, show the
/// one there is where there is. Nothing is hidden behind a button that has to be found first, which matters
/// most for the case a user reaches this screen for — a key typed wrongly months ago, that they want to look
/// at and replace.
/// </para>
/// </summary>
public partial class Form1
{
	/// <summary>Opens the list of sites that want a key.</summary>
	private void ShowModSourceKeys()
	{
		ShowInlineView(Loc.T("sourcekeys.title"), (container, closeView) =>
		{
			var layout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10)
			};
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			// Silent, like every other inline list: the title and hint have just named this view, and the list
			// is the whole of it — a name here would be read before every row.
			var list = new ListBox
			{
				Dock = DockStyle.Fill,
				AccessibleName = SilentAccessibleName,
				IntegralHeight = false,
				HorizontalScrollbar = true
			};
			layout.Controls.Add(list, 0, 0);

			var buttons = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, AutoSize = true
			};
			var btnEdit = new Button { Text = Loc.T("sourcekeys.edit"), AutoSize = true };
			var btnRemove = new Button { Text = Loc.T("sourcekeys.remove"), AutoSize = true };
			var btnGetKey = new Button { Text = Loc.T("sourcekeys.getKey"), AutoSize = true };
			buttons.Controls.AddRange(new Control[] { btnEdit, btnRemove, btnGetKey });
			layout.Controls.Add(buttons, 0, 1);

			container.Controls.Add(layout);

			void Reload(string? keepSourceId = null)
			{
				list.BeginUpdate();
				list.Items.Clear();
				foreach (ModSourceKeyRow row in ModSourceCredentials.Rows(_settings.ModSourceApiKey))
					list.Items.Add(row);
				list.EndUpdate();

				int keep = keepSourceId == null
					? 0
					: list.Items.Cast<ModSourceKeyRow>().ToList().FindIndex(r => r.Source.Id == keepSourceId);
				if (list.Items.Count > 0) list.SelectedIndex = Math.Max(0, keep);

				// The buttons follow the row, so a row with no key does not offer to remove one.
				bool hasKey = list.SelectedItem is ModSourceKeyRow { HasKey: true };
				btnEdit.Enabled = list.SelectedItem != null;
				btnRemove.Enabled = hasKey;
				btnGetKey.Enabled = list.SelectedItem is ModSourceKeyRow r2 && r2.Source.ApiKeyUrl.Length > 0;
			}

			Reload();

			list.GotFocus += List_Enter;
			list.SelectedIndexChanged += List_SelectedIndexChanged;
			list.SelectedIndexChanged += delegate
			{
				bool hasKey = list.SelectedItem is ModSourceKeyRow { HasKey: true };
				btnEdit.Enabled = list.SelectedItem != null;
				btnRemove.Enabled = hasKey;
				btnGetKey.Enabled = list.SelectedItem is ModSourceKeyRow r && r.Source.ApiKeyUrl.Length > 0;
			};

			void ActOnSelected(bool forceEdit)
			{
				if (list.SelectedItem is not ModSourceKeyRow row) return;

				// Enter on a row with no key asks for one; on a row with a key it shows it. Edit forces the
				// asking either way, which is the whole point of the button: a key typed wrongly cannot be
				// corrected by a screen that only ever shows it back to you.
				if (row.HasKey && !forceEdit) ShowStoredKey(row);
				else if (AskForKey(row)) Reload(row.Source.Id);
			}

			list.KeyDown += (_, e) =>
			{
				// Left/Right would move the selection like Up/Down in a single-column list; suppress them.
				if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) { e.Handled = e.SuppressKeyPress = true; }
				else if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; ActOnSelected(forceEdit: false); }
				else if (e.KeyCode == Keys.Delete) { e.Handled = e.SuppressKeyPress = true; RemoveKey(list.SelectedItem as ModSourceKeyRow, Reload); }
			};

			btnEdit.Click += delegate { ActOnSelected(forceEdit: true); };
			btnRemove.Click += delegate { RemoveKey(list.SelectedItem as ModSourceKeyRow, Reload); };
			btnGetKey.Click += delegate
			{
				if (list.SelectedItem is not ModSourceKeyRow row || row.Source.ApiKeyUrl.Length == 0) return;
				try { Process.Start(new ProcessStartInfo(row.Source.ApiKeyUrl) { UseShellExecute = true }); }
				catch (Exception ex) { LogFailure("ModSourceKeys", $"opening {row.Source.ApiKeyUrl}", ex); }
			};

			return list;
		},
		// As the hint, so it follows the title rather than arriving ahead of it.
		hint: ModSourceCredentials.Summarise(ModSourceCredentials.Rows(_settings.ModSourceApiKey))
			+ " " + Loc.T("sourcekeys.hintKeys"));
	}

	/// <summary>
	/// Shows the stored key in a box that cannot be typed into, so it can be read back and checked.
	///
	/// Read-only rather than hidden, because the user is on their own machine looking at their own key, and
	/// the reason anybody opens this is to find out whether what they typed months ago is what they meant. It
	/// is still never spoken by the list itself — see <see cref="ModSourceKeyRow.Describe"/>.
	/// </summary>
	private void ShowStoredKey(ModSourceKeyRow row)
	{
		string key = _settings.ModSourceApiKey(row.Source.Id);

		ShowInlineView(Loc.T("sourcekeys.viewTitle", row.Source.DisplayName), (container, closeView) =>
		{
			var layout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10)
			};
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

			var box = new TextBox
			{
				Text = key,
				ReadOnly = true,
				Dock = DockStyle.Fill,
				Font = new Font("Segoe UI", 10f),
				AccessibleName = Loc.T("sourcekeys.viewBoxName", row.Source.DisplayName)
			};
			layout.Controls.Add(box, 0, 0);

			var btnChange = new Button { Text = Loc.T("sourcekeys.change"), AutoSize = true };
			btnChange.Click += delegate
			{
				closeView();
				if (AskForKey(row)) Speak(Loc.T("sourcekeys.saved", row.Source.DisplayName));
			};
			layout.Controls.Add(btnChange, 0, 1);

			container.Controls.Add(layout);
			box.SelectAll();
			return box;
		},
		hint: Loc.T("sourcekeys.viewHint", row.Source.DisplayName));
	}

	/// <summary>
	/// Asks for a key and stores it. Returns true when something was saved.
	///
	/// The box starts empty even when a key is already stored. Pre-filling it would mean anybody correcting a
	/// mistyped key has to clear a field full of characters they cannot see the shape of, and the old key is
	/// one screen away if they want to compare.
	/// </summary>
	private bool AskForKey(ModSourceKeyRow row)
	{
		// A site that will sign the user in is worth saying so, once, before they go hunting for a key on its
		// website. Nexus is the one that does, and its flow is built and waiting on the manager being approved
		// as an application — so this says what is true today rather than offering a button that cannot work.
		string prompt = row.Source.Credential == ModSourceCredential.ApiKeyOrSignIn
			? Loc.T("sourcekeys.promptOrSignIn", row.Source.DisplayName)
			: Loc.T("sourcekeys.prompt", row.Source.DisplayName);

		string? typed = ShowTextPrompt(
			Loc.T("sourcekeys.askTitle", row.Source.DisplayName), prompt, "");
		if (typed == null) { Speak(Loc.T("common.changesCancelled")); return false; }

		string? refusal = ModSourceCredentials.WhyNotUsable(typed);
		if (refusal != null) { Speak(refusal); return false; }

		_settings.SetModSourceApiKey(row.Source.Id, typed.Trim());
		_settings.Save();
		Speak(Loc.T("sourcekeys.saved", row.Source.DisplayName));
		return true;
	}

	/// <summary>Forgets a stored key, after asking — it cannot be got back from here.</summary>
	private void RemoveKey(ModSourceKeyRow? row, Action<string?> reload)
	{
		if (row is not { HasKey: true }) { Speak(Loc.T("sourcekeys.nothingToRemove")); return; }

		if (SpeakBox(Loc.T("sourcekeys.confirmRemove", row.Source.DisplayName), Loc.T("sourcekeys.confirmRemoveTitle"),
				MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			Speak(Loc.T("common.changesCancelled"));
			return;
		}

		_settings.SetModSourceApiKey(row.Source.Id, "");
		_settings.Save();
		Speak(Loc.T("sourcekeys.removed", row.Source.DisplayName));
		reload(row.Source.Id);
	}
}
