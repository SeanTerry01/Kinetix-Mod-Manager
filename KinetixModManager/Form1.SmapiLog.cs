using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using DavyKager;
using Microsoft.VisualBasic;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewMod = KinetixModManager.GameMod;

namespace KinetixModManager;

/// <summary>SMAPI log viewing, searching, and uploading for Form1.</summary>
public partial class Form1
{
	/// <summary>
	/// Shows the active game's logs, inside the window and read only.
	///
	/// Where there is more than one it lists them and lets the user pick — a script extender's folder holds the
	/// extender's own log and one for every plugin that writes anything, and which of those matters depends on
	/// what went wrong. That list used to be Explorer, which meant leaving the manager to find your way around
	/// somebody else's file browser.
	/// </summary>
	private void OpenGameLog()
	{
		if (GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.StardewValley)) { OpenRawSmapiLog(); return; }
		if (!GameHasLogTab(_settings.ActiveGame)) { SpeakBox(Loc.T("log.notFound")); return; }

		string folder = GameLogFolder();
		if (!Directory.Exists(folder)) { SpeakBox(Loc.T("log.notFound")); return; }

		ShowLogFolder(Loc.T("log.gameTitle", GameDisplayName()), folder, PrimaryGameLogName());
	}

	// These four were the file-locating half of this screen. They live in GameLogFiles in the core now —
	// finding a file and reading it are not a window's job, and the GTK head needs both. Kept here as
	// one-liners so the call sites around the screen read exactly as they did.

	private string GameLogFolder() =>
		GameLogFiles.LoaderLogFolder(GameProfiles.Find(_settings.ActiveGame), _settings.CurrentGamePath,
			GameProfiles.Find(_settings.ActiveGame)?.IsMinecraft == true ? MinecraftGameFolder() : "");

	private string PrimaryGameLogName() => GameProfiles.Find(_settings.ActiveGame)?.LoaderLogFileName ?? "";

	private static bool GameHasLogTab(string game) => GameLogFiles.HasLoaderLog(GameProfiles.Find(game));

	private static string[] ReadAllLinesShared(string path) => GameLogFiles.ReadAllLinesShared(path);

	private static string ReadAllTextShared(string path) => GameLogFiles.ReadAllTextShared(path);

	/// <summary>Shows SMAPI's own log, unparsed, inside the window and read only.</summary>
	private void OpenRawSmapiLog()
	{
		string folder = GameLogFiles.SmapiLogFolder();
		string latest = GameLogFiles.SmapiLogPath();

		if (File.Exists(latest))
			ShowLogFile(Loc.T("log.gameTitle", GameDisplayName()), latest);
		else if (Directory.Exists(folder))
			ShowLogFolder(Loc.T("log.gameTitle", GameDisplayName()), folder, "SMAPI-latest.txt");
		else
			SpeakBox(Loc.T("smapi.logNotFound"));
	}

	/// <summary>
	/// Reads the SMAPI log file, runs it through <see cref="LogAnalyzer"/>, and populates
	/// <c>listLog</c> with parsed <see cref="LogEntry"/> items.
	/// </summary>
	private void RefreshSmapiLog()
	{
		if (_settings.ActiveGame == "None") return;
		if (listLog == null)
		{
			return;
		}
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs", "SMAPI-latest.txt");
		if (!File.Exists(path))
		{
			return;
		}
		_fullLogEntries.Clear();
		string text = cmbLogFilter.SelectedItem?.ToString() ?? "Errors and Warnings";
		try
		{
			string[] array = ReadAllLinesShared(path);
			for (int i = 0; i < array.Length; i++)
			{
				string text2 = array[i];
				// The level lives inside the header bracket ("[HH:MM:SS ERROR Source]"), so detect it
				// via LogAnalyzer.GetLevel rather than looking for a literal "[ERROR]" that never appears.
				string level = LogAnalyzer.GetLevel(text2);
				bool isError = level == "ERROR";
				bool isWarn = level == "WARN";
				bool flag3 = false;
				if (text == "Full Log")
				{
					flag3 = true;
				}
				else if (text == "Errors Only" && isError)
				{
					flag3 = true;
				}
				else if (text == "Errors and Warnings" && (isError || isWarn))
				{
					flag3 = true;
				}
				else if (text == "Links Only" && !string.IsNullOrEmpty(LogAnalyzer.ExtractUrl(text2)))
				{
					flag3 = true;
				}
				if (flag3)
				{
					_fullLogEntries.Add(new LogEntry
					{
						Text = text2,
						Index = i
					});
				}
			}
		}
		catch (Exception ex)
		{
			LogFailure("SmapiLog", "Failed to parse SMAPI log", ex);
		}
		listLog.BeginUpdate();
		listLog.Items.Clear();
		foreach (LogEntry fullLogEntry in _fullLogEntries)
		{
			listLog.Items.Add(fullLogEntry);
		}
		if (listLog.Items.Count > 0)
		{
			listLog.SelectedIndex = 0;
		}
		listLog.EndUpdate();
	}

	/// <summary>Filters <c>listLog</c> to entries matching the current search box text.</summary>
	private void SearchSmapiLog()
	{
		string query = txtSearchLog.Text.Trim().ToLower();
		if (string.IsNullOrEmpty(query))
		{
			RefreshSmapiLog();
			return;
		}
		List<LogEntry> list = _fullLogEntries.Where((LogEntry e) => e.Text.ToLower().Contains(query)).ToList();
		listLog.BeginUpdate();
		listLog.Items.Clear();
		foreach (LogEntry item in list)
		{
			listLog.Items.Add(item);
		}
		listLog.EndUpdate();
		Speak(Loc.T("smapi.foundResults", list.Count));
		if (listLog.Items.Count > 0)
		{
			listLog.SelectedIndex = 0;
		}
	}

	/// <summary>Uploads the SMAPI log to smapi.io/log and opens the resulting URL in the default browser.</summary>
	private async Task UploadSmapiLog()
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs", "SMAPI-latest.txt");
		if (!File.Exists(path))
		{
			return;
		}
		SetStatus(Loc.T("smapi.uploading"));
		try
		{
			string value = ReadAllTextShared(path);
			FormUrlEncodedContent content = new FormUrlEncodedContent(new KeyValuePair<string, string>[1]
			{
				new KeyValuePair<string, string>("input", value)
			});
			HttpResponseMessage httpResponseMessage = await NexusService.HttpClient.PostAsync("https://smapi.io/log/", content);
			if (httpResponseMessage.IsSuccessStatusCode)
			{
				Process.Start(new ProcessStartInfo(httpResponseMessage.RequestMessage?.RequestUri?.ToString() ?? "https://smapi.io/log/")
				{
					UseShellExecute = true
				});
				Speak(Loc.T("smapi.uploadSuccess"));
			}
		}
		catch (Exception ex)
		{
			SpeakBox(Loc.T("smapi.uploadFailed", FriendlyError(ex)));
		}
		finally
		{
		}
	}

	/// <summary>Opens a single log-line link in the default browser, speaking success or failure.</summary>
	private void OpenLogLink(string url)
	{
		try
		{
			Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
			Speak(Loc.T("smapi.openingModPage"));
		}
		catch (Exception ex)
		{
			LogFailure("SmapiLog", "Could not open log link", ex);
			Speak(Loc.T("smapi.couldNotOpenLink"));
		}
	}

	/// <summary>
	/// Shows an accessible picker when a log line contains more than one link (for example a SMAPI
	/// "no longer compatible" line that lists the Nexus, GitHub, and smapi.io pages). Each link is
	/// listed by a friendly source label plus its URL; Enter on a selection opens it in the default
	/// browser, Escape cancels. Mirrors the keyboard/focus conventions of the app's other list dialogs.
	/// </summary>
	private void ShowLogLinkPicker(List<string> urls)
	{
		// Shown inside the main window rather than as one of its own — see Form1.InlineView. Escape is handled
		// by the view itself.
		ShowInlineView(Loc.T("smapi.linkPickerTitle"), (container, closeView) =>
		{
		ListBox list = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f),
			Name = "listLinkPicker",
			AccessibleName = Loc.T("smapi.linksInLine")
		};
		foreach (string url in urls)
		{
			list.Items.Add($"{LinkLabel(url)}: {url}");
		}
		// Announce position on focus AND on every arrow move, the same way the main lists do. GotFocus alone left
		// the list saying "1 of 3" on the way in and nothing at all while the user arrowed through the links.
		WireAccessibleDialogList(list);
		if (list.Items.Count > 0)
		{
			list.SelectedIndex = 0;
		}
		list.KeyDown += (s, e) =>
		{
			if (e.KeyCode == Keys.Return && list.SelectedIndex >= 0)
			{
				OpenLogLink(urls[list.SelectedIndex]);
				e.Handled = true;
				closeView();
			}
		};

		Label hint = new Label
		{
			Text = Loc.T("smapi.linkPickerHint"),
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Padding = new Padding(4, 0, 0, 0)
		};

		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2
		};
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
		layout.Controls.Add(list, 0, 0);
		layout.Controls.Add(hint, 0, 1);

		container.Controls.Add(layout);
		ApplyScreenReaderPauses(container);
		return list;
		});
	}

	/// <summary>Returns a short, screen-reader-friendly label for a link based on its host (e.g. "Nexus Mods page").</summary>
	private static string LinkLabel(string url)
	{
		if (url.Contains("nexusmods.com", StringComparison.OrdinalIgnoreCase))
		{
			return Loc.T("smapi.linkNexus");
		}
		if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase))
		{
			return url.Contains("/releases", StringComparison.OrdinalIgnoreCase) ? Loc.T("smapi.linkGitHubReleases") : Loc.T("smapi.linkGitHub");
		}
		if (url.Contains("smapi.io", StringComparison.OrdinalIgnoreCase))
		{
			return Loc.T("smapi.linkSmapi");
		}
		try { return new Uri(url).Host; } catch { return Loc.T("smapi.linkGeneric"); }
	}
}
