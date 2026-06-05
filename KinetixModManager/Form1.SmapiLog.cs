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
			MessageBox.Show("SMAPI log not found.");
		}
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
			string[] array = File.ReadAllLines(path);
			for (int i = 0; i < array.Length; i++)
			{
				string text2 = array[i];
				bool flag = text2.Contains("[ERROR]");
				bool flag2 = text2.Contains("[WARN]");
				bool flag3 = false;
				if (text == "Full Log")
				{
					flag3 = true;
				}
				else if (text == "Errors Only" && flag)
				{
					flag3 = true;
				}
				else if (text == "Errors and Warnings" && (flag || flag2))
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
		Speak($"Found {list.Count} results. Enter to jump to line in full view.");
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
		SetStatus("Uploading log to SMAPI.io...");
		try
		{
			string value = File.ReadAllText(path);
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
				Speak("Log uploaded successfully.");
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("Upload failed: " + ex.Message);
		}
		finally
		{
		}
	}
}
