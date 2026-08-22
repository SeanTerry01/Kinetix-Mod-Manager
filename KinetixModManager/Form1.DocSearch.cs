using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace KinetixModManager;

/// <summary>
/// The results list for a search inside the document viewers — the manual (F1), the change log (F2) and an
/// accessibility mod's documentation (F3), all of which are the one drill-down in <c>ShowDocDrilldown</c>.
///
/// The searching itself is <see cref="DocOutline.Find"/>; this is only how its answer is offered. See the
/// summary on <see cref="DocOutline"/> for why a drill-down needed a search in the first place.
/// </summary>
public partial class Form1
{
	/// <summary>How a match reads as a row: the matching text, then the section it sits in. That order is
	/// deliberate — the text is what tells one result from another, and the section is context for it.</summary>
	private static string DocMatchRow(DocMatch m) => Loc.T("docsearch.resultRow", m.Snippet, m.Section);

	/// <summary>
	/// Shows the matches as a list and returns the one chosen, or null if the user backed out. Each row answers
	/// both "is this the one I want?" and "where will this take me?" without having to go there and come back.
	/// </summary>
	private DocMatch? ShowDocSearchResults(List<DocMatch> matches, string phrase)
	{
		DocMatch? chosen = null;

		ShowInlineView(Loc.T("docsearch.resultsTitle", phrase), (container, closeView) =>
		{
			var list = new ListBox
			{
				Dock = DockStyle.Fill,
				Font = new Font("Segoe UI", 12f),
				AccessibleName = Loc.T("docsearch.resultsName", phrase),
				IntegralHeight = false,
				HorizontalScrollbar = true
			};

			// The rows are strings, not the matches themselves: a ListBox shows an item's ToString(), and a
			// DocMatch is a search result rather than a thing that knows how it should be read out.
			foreach (DocMatch m in matches) list.Items.Add(DocMatchRow(m));
			if (list.Items.Count > 0) list.SelectedIndex = 0;

			container.Controls.Add(list);
			WireAccessibleDialogList(list);

			list.KeyDown += delegate (object? s, KeyEventArgs e)
			{
				if (e.KeyCode != Keys.Enter) return;
				e.Handled = e.SuppressKeyPress = true;
				if (list.SelectedIndex >= 0) chosen = matches[list.SelectedIndex];
				closeView();
			};

			AttachViewHelp(container, () => Speak(Loc.T("docsearch.resultsHelp")));
			return list;
		},
		hint: Loc.T(matches.Count == 1 ? "docsearch.resultsHintOne" : "docsearch.resultsHint", matches.Count, phrase));

		return chosen;
	}
}
