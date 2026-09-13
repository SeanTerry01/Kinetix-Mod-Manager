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

	/// <summary>
	/// The folder where the script extender (and most F4SE/SKSE plugins) write their logs for the active
	/// Bethesda game: <c>Documents\My Games\&lt;game&gt;\F4SE</c> or <c>\SKSE</c>. Empty outside Skyrim/FO4.
	///
	/// The per-player folder is asked for by COPY, not by game. A GOG install writes to "Skyrim Special Edition
	/// GOG", so a hardcoded Steam folder name here would have shown the wrong copy's log — silently, and most
	/// confusingly of all on the machine where both are installed and both logs exist.
	/// </summary>
	private string BethesdaLogFolder()
	{
		if (!IsBethesdaGame) return "";

		GameProfile profile = GameProfiles.Require(_settings.ActiveGame);
		string userData = profile.UserDataDirectoryFor(_settings.CurrentGamePath);
		if (string.IsNullOrEmpty(userData)) return "";

		return Path.Combine(userData, GameProfiles.IsGame(_settings.ActiveGame, GameProfiles.Fallout4) ? "F4SE" : "SKSE");
	}

	/// <summary>
	/// True when the active game's mod loader keeps a log the manager can show in the Log tab. Skyrim SE and
	/// Fallout 4 have their script-extender logs; Moonlight Peaks has BepInEx's <c>LogOutput.log</c>; The Witcher 3
	/// has whatever its hooked mods write beside the game exe. Stardew Valley is deliberately excluded — it has
	/// its own richer SMAPI Log tab instead.
	/// </summary>
	private static bool GameHasLogTab(string game)
	{
		GameProfile? profile = GameProfiles.Find(game);
		return profile != null &&
			(profile.IsBethesda || profile.IsBepInEx || profile.IsWitcher3 || profile.IsMinecraft);
	}

	/// <summary>
	/// The folder the active game's loader writes its logs to: <c>Documents\My Games\&lt;game&gt;\SKSE</c> (or
	/// <c>\F4SE</c>) for the Bethesda games, and the game's own <c>BepInEx</c> folder for Moonlight Peaks, where
	/// BepInEx writes <c>LogOutput.log</c> next to its config and plugins. Empty for games with no loader log.
	/// </summary>
	private string GameLogFolder()
	{
		GameProfile? profile = GameProfiles.Find(_settings.ActiveGame);
		if (profile == null) return "";
		if (profile.IsBethesda) return BethesdaLogFolder();
		if (profile.IsBepInEx)
		{
			string root = _settings.CurrentGamePath;
			return string.IsNullOrEmpty(root) ? "" : Path.Combine(root, "BepInEx");
		}
		// The Witcher 3 writes no log of its own, but the mods that hook it do, and they write beside the game's
		// executable — which is where a player looking for "why did my mod not load" needs to be pointed.
		if (profile.IsWitcher3)
		{
			string root = _settings.CurrentGamePath;
			return string.IsNullOrEmpty(root) ? "" : Path.Combine(root, "bin", "x64");
		}
		// Minecraft writes one log per run to .minecraft\logs, and it is the only place that answers the
		// question that matters: whether the game just started with the mods or without them. A vanilla launch
		// and a modded one look identical from outside — the game runs either way and says nothing.
		if (profile.IsMinecraft) return Path.Combine(MinecraftRootFolder(), "logs");
		return "";
	}

	/// <summary>The loader's own log file name for the active game — the one the Log tab opens by default.</summary>
	private string PrimaryGameLogName() => GameProfiles.BaseId(_settings.ActiveGame) switch
	{
		"SkyrimSE"       => "skse64.log",
		"Fallout4"       => "f4se.log",
		"MoonlightPeaks" => "LogOutput.log",
		// No engine log exists; the accessibility mod's own log is the one worth opening first.
		"Witcher3"       => "WitcherAccess.log",
		// Minecraft rotates every previous run into a .log.gz and keeps only the current one as plain text.
		"Minecraft"      => "latest.log",
		_                => ""
	};

	/// <summary>Shows SMAPI's own log, unparsed, inside the window and read only.</summary>
	private void OpenRawSmapiLog()
	{
		string folder = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs");
		string latest = Path.Combine(folder, "SMAPI-latest.txt");

		if (File.Exists(latest))
			ShowLogFile(Loc.T("log.gameTitle", GameDisplayName()), latest);
		else if (Directory.Exists(folder))
			ShowLogFolder(Loc.T("log.gameTitle", GameDisplayName()), folder, "SMAPI-latest.txt");
		else
			SpeakBox(Loc.T("smapi.logNotFound"));
	}

	/// <summary>
	/// Reads every line of a file even while another process holds it open for writing. SMAPI keeps
	/// SMAPI-latest.txt open for the whole game session, and <see cref="File.ReadAllLines"/> opens with
	/// only <see cref="FileShare.Read"/>, so it throws a sharing violation while the game is running and
	/// the log appears empty. Opening with <see cref="FileShare.ReadWrite"/> lets us read the live log
	/// without disturbing SMAPI's writer.
	/// </summary>
	private static string[] ReadAllLinesShared(string path)
	{
		using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using StreamReader reader = new StreamReader(stream);
		List<string> lines = new List<string>();
		string? line;
		while ((line = reader.ReadLine()) != null)
		{
			lines.Add(line);
		}
		return lines.ToArray();
	}

	/// <summary>Reads a whole file as text while another process (SMAPI) has it open for writing. See <see cref="ReadAllLinesShared"/>.</summary>
	private static string ReadAllTextShared(string path)
	{
		using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using StreamReader reader = new StreamReader(stream);
		return reader.ReadToEnd();
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
