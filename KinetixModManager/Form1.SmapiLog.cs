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
	/// <summary>Opens the SMAPI log file in Notepad for manual inspection.</summary>
	private void OpenRawSmapiLog()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "ErrorLogs", "SMAPI-latest.txt");
		if (File.Exists(text))
		{
			Process.Start(new ProcessStartInfo("notepad.exe", text)
			{
				UseShellExecute = true
			});
		}
		else
		{
			MessageBox.Show(Loc.T("smapi.logNotFound"));
		}
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
			LogError("SmapiLog", "Failed to parse SMAPI log: " + ex.Message);
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
			MessageBox.Show(Loc.T("smapi.uploadFailed", ex.Message));
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
			LogError("SmapiLog", "Could not open log link: " + ex.Message);
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
		Form dialog = new Form
		{
			Text = Loc.T("smapi.linkPickerTitle"),
			Size = new Size(560, 320),
			StartPosition = FormStartPosition.CenterScreen,
			KeyPreview = true
		};
		dialog.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) dialog.Close(); };

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
		// Announce position on focus the same way the main lists do.
		list.GotFocus += List_Enter;
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
				dialog.Close();
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

		dialog.Controls.Add(layout);
		dialog.Shown += (s, e) => list.Focus();
		dialog.ShowDialog();
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
