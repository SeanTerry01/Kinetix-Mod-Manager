using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using StardewMod = KinetixModManager.GameMod;

namespace KinetixModManager;

/// <summary>
/// The Suggested Mods list a player reads: mods that remove a wall this game still has once it is playable at
/// all, grouped by the kind of wall, each saying why it is there.
///
/// <para>
/// Deliberately not the accessibility suite. That one answers "what does this game need before it can be played
/// by ear", and everything in it is required. This one is advice — every entry is optional, several make the
/// game easier as well as more reachable, and the reason line exists so a player can decline one on purpose
/// rather than trusting the label. The two lists are kept apart because they make different promises.
/// </para>
/// </summary>
public partial class Form1
{
	/// <summary>
	/// The suggestions that ship with the manager, beside the executable.
	///
	/// Read-only in practice — under Program Files for an installed copy — and replaced wholesale by the next
	/// release, which is exactly why the user's own suggestions live somewhere else entirely.
	/// </summary>
	private static string ShippedSuggestionsPath =>
		Path.Combine(AppContext.BaseDirectory, "data", "suggested-mods.json");

	/// <summary>
	/// The list that ships with the manager, or an empty one when there isn't a readable file there.
	///
	/// <para>
	/// Read through <see cref="SuggestionSharing.Parse"/> rather than the plain store, so that the shipped file
	/// may be in either shape — a bare array of entries, or a proper exported list with a name, an author and the
	/// categories it uses. That matters because the obvious way to produce this file is to curate a list in the
	/// manager and export it, and an exported file is the second shape: read as a bare array it would throw, be
	/// caught, and come back empty, leaving the shipped list silently absent with nothing to say why.
	/// </para>
	/// </summary>
	private static SuggestionList LoadShippedSuggestions()
	{
		try
		{
			return File.Exists(ShippedSuggestionsPath)
				? SuggestionSharing.Parse(File.ReadAllText(ShippedSuggestionsPath))
				: new SuggestionList();
		}
		catch { return new SuggestionList(); }
	}

	/// <summary>
	/// Everything to show for the game that is open: the shipped list with the user's own entries over the top.
	/// </summary>
	private List<SuggestedMod> SuggestionsForActiveGame(SuggestionList shipped)
	{
		string game = GameProfiles.BaseId(_settings.ActiveGame);

		return SuggestedModStore
			.Merge(shipped.Entries, SuggestedModStore.Load(CuratorFilePath))
			.Where(e => string.Equals(GameProfiles.BaseId(e.Game), game, StringComparison.Ordinal))
			.ToList();
	}

	/// <summary>
	/// Whether a suggested mod is already installed.
	///
	/// Tried strongest first. A Nexus id is an exact fact and settles it outright; a manifest id is nearly as
	/// good; the name is a guess, and only reached when the entry carries neither — which happens for a mod
	/// marked from the installed list before Auto Match had given it an id.
	/// </summary>
	private bool IsSuggestionInstalled(SuggestedMod entry)
	{
		if (!string.IsNullOrWhiteSpace(entry.NexusId) &&
			_allInstalledMods.Any(m => string.Equals(m.NexusID, entry.NexusId, StringComparison.OrdinalIgnoreCase)))
			return true;

		if (!string.IsNullOrWhiteSpace(entry.UniqueId) && HasModUniqueId(entry.UniqueId))
			return true;

		return !string.IsNullOrWhiteSpace(entry.Name) && HasModNameContains(entry.Name);
	}

	/// <summary>One row of the suggested list: either a category heading, or a mod under it.</summary>
	private sealed class SuggestionRow
	{
		/// <summary>The mod this row is about, or <c>null</c> when the row is a heading.</summary>
		public SuggestedMod? Entry { get; init; }

		/// <summary>What the screen reader reads for this row.</summary>
		public required string Text { get; init; }

		public bool IsHeading => Entry == null;

		public override string ToString() => Text;
	}

	/// <summary>
	/// Shows the suggested mods for the game that is open, grouped under the category headings in the order the
	/// categories are kept.
	///
	/// <para>
	/// Headings rather than a category on every row: arrowing through twenty rows that each begin "Can't be done
	/// by ear" buries the mod's name behind a phrase already heard, whereas a heading says it once and the group
	/// under it can be skipped in a few presses when it isn't wanted.
	/// </para>
	/// </summary>
	private void ShowSuggestedMods()
	{
		// The list is per game and says so in its title, so without one open there is nothing it could show and
		// nothing sensible for the title to name.
		if (_settings.ActiveGame == "None")
		{
			Speak(Loc.T("suggested.noGame"));
			return;
		}

		string gameName = GameProfiles.DisplayNameFor(GameProfiles.BaseId(_settings.ActiveGame));
		SuggestionList shipped = LoadShippedSuggestions();
		List<SuggestedMod> entries = SuggestionsForActiveGame(shipped);

		if (entries.Count == 0)
		{
			// Nothing for this game is a real answer, not a failure — the list is filled in game by game, and
			// saying which game is empty is what stops it sounding like something went wrong.
			SpeakBox(Loc.T("suggested.emptyBox", gameName), Loc.T("suggested.title", gameName));
			return;
		}

		// The shipped list's own categories are folded in for display only, never saved. A shipped entry filed
		// under a heading this user has never made would otherwise land in "Uncategorised" — correct, in that
		// nothing is lost, but a poor showing for a list that arrived describing itself perfectly well.
		List<SuggestionCategory> categories =
			SuggestionSharing.MergeCategories(SuggestionCategoryStore.Load(CategoryFilePath), shipped.Categories);

		ShowInlineView(Loc.T("suggested.title", gameName), (container, closeView) =>
		{
			var list = new ListBox
			{
				Dock = DockStyle.Fill,
				Font = new Font("Segoe UI", 11f),
				IntegralHeight = false,
				HorizontalScrollbar = true,
				AccessibleName = Loc.T("suggested.listName"),
				AccessibleDescription = Loc.T("suggested.listDesc")
			};

			void Fill(int select)
			{
				_movingListSilently = true;
				try
				{
					list.BeginUpdate();
					list.Items.Clear();

					void AddGroup(string heading, List<SuggestedMod> inGroup)
					{
						if (inGroup.Count == 0) return;

						list.Items.Add(new SuggestionRow
						{
							Text = Loc.T("suggested.heading", heading, inGroup.Count)
						});

						foreach (SuggestedMod entry in inGroup.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase))
						{
							string state = IsSuggestionInstalled(entry)
								? Loc.T("suggested.installed")
								: Loc.T("suggested.notInstalled");

							list.Items.Add(new SuggestionRow
							{
								Entry = entry,
								Text = entry.Reason.Length > 0
									? Loc.T("suggested.row", entry.Name, state, entry.Reason)
									: Loc.T("suggested.rowNoReason", entry.Name, state)
							});
						}
					}

					// A category nobody has filed anything under gets no heading here. It still exists and can
					// still be picked when marking; it simply has nothing to say about this game.
					foreach (SuggestionCategory category in categories)
						AddGroup(CategoryLabel(category), entries
							.Where(e => string.Equals(e.Category, category.Id, StringComparison.OrdinalIgnoreCase))
							.ToList());

					// Anything filed under a category that no longer exists. Import re-files these and the
					// category manager will not delete one without moving its mods, so this should stay empty —
					// but a list is grouped by the categories it knows, and an entry with no heading to sit under
					// would be silently absent rather than visibly odd. Showing it wrongly beats losing it.
					var known = new HashSet<string>(categories.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
					AddGroup(Loc.T("curator.categoryUnknown"),
						entries.Where(e => !known.Contains(e.Category)).ToList());

					list.EndUpdate();

					if (list.Items.Count > 0)
						list.SelectedIndex = Math.Clamp(select, 0, list.Items.Count - 1);
				}
				finally { _movingListSilently = false; }
			}

			Fill(0);
			list.GotFocus += List_Enter;
			list.SelectedIndexChanged += List_SelectedIndexChanged;

			list.KeyDown += async (s, e) =>
			{
				if (list.SelectedItem is not SuggestionRow row) return;

				// The same key that opens a mod's page from the main lists, so it means one thing everywhere —
				// and it has to be matched here rather than left to the window, because an overlay turns the
				// window's key preview off while it is up (see RunOverlay).
				if (IsShortcut(e, "OpenModPage"))
				{
					e.Handled = true;
					e.SuppressKeyPress = true;
					if (row.Entry != null) OpenSuggestedModPage(row.Entry);
					return;
				}

				if (e.KeyCode != Keys.Enter) return;
				e.Handled = true;
				e.SuppressKeyPress = true;

				if (row.Entry == null)
				{
					Speak(row.Text);
					return;
				}
				if (IsSuggestionInstalled(row.Entry))
				{
					Speak(Loc.T("suggested.alreadyInstalled", row.Entry.Name));
					return;
				}

				if (SpeakBox(Loc.T("suggested.installConfirm", row.Entry.Name),
					Loc.T("suggested.title", gameName), MessageBoxButtons.YesNo) != DialogResult.Yes)
				{
					Speak(Loc.T("suggested.installCancelled"));
					return;
				}

				int at = list.SelectedIndex;
				await InstallSuggestedModAsync(row.Entry);

				// Rebuilt so the row this came from now reads "installed" rather than still offering to fetch
				// something that is already there.
				Fill(at);
			};
			// Escape is handled by the view itself (see Form1.InlineView).

			container.Controls.Add(list);
			return list;
		},
		hint: Loc.T("suggested.hint", entries.Count, GetShortcutString("OpenModPage")));
	}

	/// <summary>Opens a suggested mod's page, so it can be read about before being installed.</summary>
	private void OpenSuggestedModPage(SuggestedMod entry)
	{
		string url;
		if (!string.IsNullOrWhiteSpace(entry.GitHubRepo))
			url = "https://github.com/" + entry.GitHubRepo;
		else if (!string.IsNullOrWhiteSpace(entry.NexusId))
			url = $"https://www.nexusmods.com/{_nexusService.CurrentGameDomain}/mods/{entry.NexusId}";
		else
		{
			Speak(Loc.T("suggested.noPage", entry.Name));
			return;
		}

		try
		{
			Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
			Speak(Loc.T("suggested.openedPage", entry.Name));
		}
		catch (Exception ex)
		{
			LogError(entry.Name, "Could not open " + url + ": " + ex.Message);
			Speak(Loc.T("suggested.pageFailed", entry.Name));
		}
	}

	/// <summary>
	/// Fetches and installs a suggested mod.
	///
	/// <para>
	/// A free Nexus account cannot be handed a download link by the API at all — the key that unlocks one is
	/// minted on the website and arrives as an <c>nxm://</c> link — so for those the useful thing is to open the
	/// mod's files page and let the handler bring the download back here. That is the same split
	/// <see cref="FetchAndInstallPartAsync"/> makes, for the same reason.
	/// </para>
	/// </summary>
	private async Task InstallSuggestedModAsync(SuggestedMod entry)
	{
		if (string.IsNullOrWhiteSpace(entry.NexusId))
		{
			// Nothing to fetch from: a GitHub-hosted suggestion, or one marked before it had an id. Opening the
			// page is still the whole of what the manager can usefully do.
			OpenSuggestedModPage(entry);
			return;
		}

		if (!_nexusService.IsPremium)
		{
			string url = $"https://www.nexusmods.com/{_nexusService.CurrentGameDomain}/mods/{entry.NexusId}?tab=files";
			Speak(Loc.T("suggested.manualSpeak", entry.Name));
			try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
			catch (Exception ex) { LogError(entry.Name, "Could not open " + url + ": " + ex.Message); }

			SpeakBox(Loc.T("suggested.manualBox", entry.Name), Loc.T("suggested.manualTitle"));
			return;
		}

		try
		{
			SetStatus(Loc.T("suggested.downloading", entry.Name), speak: true);

			var target = new StardewMod { NexusID = entry.NexusId, Name = entry.Name };
			ProgressAnnouncer progress = NewProgress(entry.Name, installing: false);
			string archive = await _nexusService.DownloadModUpdateAsync(target, downloadsPath, progress);
			progress.Complete();

			await InstallFromZip(archive, entry.NexusId, displayName: entry.Name);
		}
		catch (Exception ex)
		{
			LogFailure(entry.Name, "Suggested mod install failed", ex);
			Speak(Loc.T("suggested.installFailed", entry.Name, ex.Message));
		}
	}
}
