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

	// SKSE and F4SE used to be resolved here, and no longer are — see ModPartRules and InstallKnownModPartsAsync.
	//
	// SKSE came from scraping silverlock.org's front page for the first "skse64_" link, with a hardcoded
	// skse64_2_02_06.7z if that failed. Two things were wrong with it and both were silent. The page lists a
	// build per game version, so "the first link" was never known to be the right one; and it has no notion of
	// which game the user is running, so a GOG copy got a Steam build that installs cleanly and then never
	// loads. F4SE had already given up on the same approach and returned a fixed Nexus files-tab URL.
	//
	// Nexus carries the same builds silverlock does, and its files.json — readable by every account, premium or
	// not — states the game version each file is for. Asking it, and matching against the build read off the
	// user's own exe, replaces both.
}
