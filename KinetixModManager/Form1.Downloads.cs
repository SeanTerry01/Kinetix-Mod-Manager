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

/// <summary>Mod-loader (SMAPI/SKSE/F4SE) download-URL resolution for Form1.</summary>
public partial class Form1
{
	private async Task<string?> GetGitHubLatestReleaseZipUrl(string repo)
	{
		try
		{
			using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
			req.Headers.UserAgent.ParseAdd($"KinetixModManager/{NexusService.AppVersion}");
			using var resp = await NexusService.HttpClient.SendAsync(req);
			if (!resp.IsSuccessStatusCode) return null;

			JObject json = JObject.Parse(await resp.Content.ReadAsStringAsync());
			JArray? assets = json["assets"] as JArray;
			if (assets != null)
			{
				foreach (var asset in assets)
				{
					string name = asset["name"]?.ToString() ?? "";
					string url = asset["browser_download_url"]?.ToString() ?? "";
					if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(url))
					{
						return url;
					}
				}
			}
		}
		catch { }
		return null;
	}

	/// <summary>
	/// Resolves the download URL of the latest SMAPI installer zip from the Pathoschild/SMAPI GitHub
	/// release. Deliberately picks the plain <c>SMAPI-x.y.z-installer.zip</c> asset and skips the
	/// <c>double-zipped</c> asset (which exists only so browsers don't auto-extract it, and would
	/// otherwise unpack to another zip) as well as any "for developers" variant. Returns null if the
	/// release or a suitable asset can't be found, in which case the caller falls back to the browser.
	/// </summary>
	private async Task<string?> GetSmapiInstallerZipUrl()
	{
		try
		{
			using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/Pathoschild/SMAPI/releases/latest");
			req.Headers.UserAgent.ParseAdd($"KinetixModManager/{NexusService.AppVersion}");
			using var resp = await NexusService.HttpClient.SendAsync(req);
			if (!resp.IsSuccessStatusCode) return null;

			JObject json = JObject.Parse(await resp.Content.ReadAsStringAsync());
			if (json["assets"] is not JArray assets) return null;

			string? fallback = null;
			foreach (var asset in assets)
			{
				string name = asset["name"]?.ToString() ?? "";
				string url = asset["browser_download_url"]?.ToString() ?? "";
				if (string.IsNullOrEmpty(url) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
				if (name.Contains("double-zipped", StringComparison.OrdinalIgnoreCase)) continue;
				if (name.Contains("developer", StringComparison.OrdinalIgnoreCase)) continue;
				if (name.Contains("installer", StringComparison.OrdinalIgnoreCase)) return url;
				fallback ??= url;
			}
			return fallback;
		}
		catch { }
		return null;
	}

	private async Task<string?> GetSkse64DownloadUrl()
	{
		try
		{
			using var req = new HttpRequestMessage(HttpMethod.Get, "https://skse.silverlock.org/");
			req.Headers.UserAgent.ParseAdd($"KinetixModManager/{NexusService.AppVersion}");
			using var resp = await NexusService.HttpClient.SendAsync(req);
			if (!resp.IsSuccessStatusCode) return null;

			string html = await resp.Content.ReadAsStringAsync();
			int idx = html.IndexOf("skse64_", StringComparison.OrdinalIgnoreCase);
			if (idx != -1)
			{
				int start = html.LastIndexOf("href=\"", idx, StringComparison.OrdinalIgnoreCase);
				if (start != -1)
				{
					start += 6;
					int end = html.IndexOf("\"", start, StringComparison.OrdinalIgnoreCase);
					if (end != -1)
					{
						string relUrl = html.Substring(start, end - start);
						if (!relUrl.StartsWith("http"))
						{
							return "https://skse.silverlock.org/" + relUrl.TrimStart('/');
						}
						return relUrl;
					}
				}
			}
		}
		catch { }
		return "https://skse.silverlock.org/beta/skse64_2_02_06.7z"; // Fallback
	}

	private async Task<string?> GetF4seDownloadUrl()
	{
		try
		{
			using var req = new HttpRequestMessage(HttpMethod.Get, "https://f4se.silverlock.org/");
			req.Headers.UserAgent.ParseAdd($"KinetixModManager/{NexusService.AppVersion}");
			using var resp = await NexusService.HttpClient.SendAsync(req);
			if (!resp.IsSuccessStatusCode) return null;

			string html = await resp.Content.ReadAsStringAsync();
			int idx = html.IndexOf("f4se_", StringComparison.OrdinalIgnoreCase);
			if (idx != -1)
			{
				int start = html.LastIndexOf("href=\"", idx, StringComparison.OrdinalIgnoreCase);
				if (start != -1)
				{
					start += 6;
					int end = html.IndexOf("\"", start, StringComparison.OrdinalIgnoreCase);
					if (end != -1)
					{
						string relUrl = html.Substring(start, end - start);
						if (relUrl.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
						{
							if (!relUrl.StartsWith("http"))
							{
								return "https://f4se.silverlock.org/" + relUrl.TrimStart('/');
							}
							return relUrl;
						}
					}
				}
			}
		}
		catch { }
		return "https://www.nexusmods.com/fallout4/mods/42147?tab=files"; // Fallback to Nexus
	}
}
