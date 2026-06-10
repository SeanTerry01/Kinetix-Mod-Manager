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

/// <summary>Mod and application update checking and installation for Form1.</summary>
public partial class Form1
{
	/// <summary>
	/// Queries the Nexus Mods REST API or GitHub Releases for the latest version of a group of mods.
	/// Adds any mods with newer versions to <c>listUpdates</c>. Rate-limited by <c>_apiSemaphore</c> for Nexus.
	/// </summary>
	private async Task CheckForUpdates(List<StardewMod> group)
	{
		try
		{
			string? latestVersion = null;
			if (!string.IsNullOrEmpty(group[0].NexusID))
			{
				latestVersion = await _nexusService.GetLatestVersionAsync(group[0].NexusID ?? "");
			}
			else if (!string.IsNullOrEmpty(group[0].GitHubRepo))
			{
				latestVersion = await GetGitHubLatestReleaseVersionAsync(group[0].GitHubRepo ?? "");
			}

			if (latestVersion == null) return;

			if (_settings.IgnoredVersions.TryGetValue(group[0].UniqueId, out string? ignored) && ignored == latestVersion)
				return;

			foreach (StardewMod mod in group)
			{
				mod.LatestVersion = latestVersion;
				if (IsNewerVersion(mod.Version, latestVersion))
				{
					Invoke(delegate
					{
						listUpdates.BeginUpdate();
						if (!listUpdates.Items.Contains(mod))
						{
							mod.IsUpdateResult = true;
							listUpdates.Items.Add(mod);
						}
						listUpdates.EndUpdate();
					});
				}
			}
		}
		finally
		{
			CompleteUpdateCheckUnit();
		}
	}

	/// <summary>
	/// Decrements the count of in-flight update-check units (Nexus/GitHub groups plus the optional
	/// smapi.io batch) and, once the last one finishes, ends the loading state, releases the
	/// single-batch guard, plays the completion cue, and announces the final result. Shared by every
	/// check unit so the cue and announcement fire exactly once regardless of how many sources ran.
	/// </summary>
	// A SMAPI program update found during the smapi.io check, surfaced only after the whole update
	// run finishes so its prompt doesn't interrupt the mod-update announcement. Null when none.
	private (string Current, string Latest, string Url)? _pendingSmapiUpdate;

	private void CompleteUpdateCheckUnit()
	{
		if (Interlocked.Decrement(ref _activeChecks) <= 0)
		{
			_isLoading = false;
			// This batch is done; release the guard so the next update check can start.
			Interlocked.Exchange(ref _updateCheckRunning, 0);
			_soundEngine.Play("load_complete");
			var pendingSmapi = _pendingSmapiUpdate;
			_pendingSmapiUpdate = null;
			Invoke(delegate
			{
				Speak(listUpdates.Items.Count > 0
					? $"Update check complete. Found {listUpdates.Items.Count} mod updates."
					: "Update check complete. All mods are up-to-date.");
				if (pendingSmapi is { } s)
					NotifySmapiUpdateAvailable(s.Current, s.Latest, s.Url);
			});
		}
	}

	/// <summary>
	/// Update-check unit that queries the SMAPI web API (smapi.io) for the supplied Stardew Valley mods and
	/// adds any with a newer suggested version to <c>listUpdates</c>. This is the primary Stardew check: it
	/// catches mods the manifest-only Nexus check misses because their update key is missing or broken. When
	/// the API resolves a mod to a Nexus page, the mod's <see cref="GameMod.NexusID"/> is back-filled so the
	/// existing download/open-page actions work on it. Also surfaces a SMAPI program update if one is offered.
	/// Runs alongside the Nexus group checks and de-dupes against them by reusing the same mod instances.
	/// </summary>
	private async Task CheckUpdatesViaSmapiApi(List<StardewMod> installed, string smapiVersion, string gameVersion)
	{
		try
		{
			var entries = new List<(string Id, string Version, IEnumerable<string> UpdateKeys)>();
			foreach (StardewMod mod in installed)
			{
				if (mod.IsGroup || string.IsNullOrEmpty(mod.UniqueId)) continue;
				var keys = new List<string>();
				if (!string.IsNullOrEmpty(mod.NexusID)) keys.Add("Nexus:" + mod.NexusID);
				if (!string.IsNullOrEmpty(mod.GitHubRepo)) keys.Add("GitHub:" + mod.GitHubRepo);
				entries.Add((mod.UniqueId, mod.Version, keys));
			}
			// Include SMAPI itself so the same call reports a SMAPI program update.
			entries.Add(("SMAPI", smapiVersion, new[] { "GitHub:Pathoschild/SMAPI", "Nexus:2400" }));

			var updates = await _nexusService.GetSmapiUpdatesAsync(entries, smapiVersion, gameVersion);
			if (updates == null) return; // Service unreachable; the Nexus fallback check still runs.

			if (updates.TryGetValue("SMAPI", out var smapiUpd) && IsNewerVersion(smapiVersion, smapiUpd.Version))
			{
				// Defer the prompt until the whole check completes (see CompleteUpdateCheckUnit).
				_pendingSmapiUpdate = (smapiVersion, smapiUpd.Version, smapiUpd.Url);
			}

			foreach (StardewMod mod in installed)
			{
				if (mod.IsGroup || string.IsNullOrEmpty(mod.UniqueId)) continue;
				if (!updates.TryGetValue(mod.UniqueId, out var upd)) continue;
				if (!IsNewerVersion(mod.Version, upd.Version)) continue;

				if (_settings.IgnoredVersions.TryGetValue(mod.UniqueId, out string? ignored) && ignored == upd.Version)
					continue;

				mod.LatestVersion = upd.Version;
				// Back-fill a Nexus ID or GitHub repo from the suggested page when the manifest lacked a
				// usable update key, so the mod becomes actionable by the existing update/open-page flow.
				if (string.IsNullOrEmpty(mod.NexusID) && string.IsNullOrEmpty(mod.GitHubRepo) && !string.IsNullOrEmpty(upd.Url))
				{
					var nexus = Regex.Match(upd.Url, @"nexusmods\.com/stardewvalley/mods/(\d+)", RegexOptions.IgnoreCase);
					if (nexus.Success)
					{
						mod.NexusID = nexus.Groups[1].Value;
					}
					else
					{
						var gh = Regex.Match(upd.Url, @"github\.com/([^/]+/[^/]+?)(?:/|$)", RegexOptions.IgnoreCase);
						if (gh.Success) mod.GitHubRepo = gh.Groups[1].Value;
					}
				}

				Invoke(delegate
				{
					listUpdates.BeginUpdate();
					if (!listUpdates.Items.Contains(mod))
					{
						mod.IsUpdateResult = true;
						listUpdates.Items.Add(mod);
					}
					listUpdates.EndUpdate();
				});
			}
		}
		catch (Exception ex)
		{
			LogError("SmapiUpdate", "SMAPI update check failed: " + ex.Message);
		}
		finally
		{
			CompleteUpdateCheckUnit();
		}
	}

	/// <summary>
	/// Announces an available SMAPI program update and offers to install it in place automatically. SMAPI is
	/// updated by re-running its installer over the existing install, which is exactly what <see
	/// cref="InstallSmapiAsync"/> does, so "Yes" downloads the latest installer and runs it unattended. SMAPI
	/// is intentionally not added to the updatable mod list because it is the loader, not a mod. Declining
	/// leaves the install untouched; any failure inside the installer falls back to opening smapi.io.
	/// </summary>
	private async void NotifySmapiUpdateAvailable(string current, string latest, string url)
	{
		_soundEngine.Play("connect");
		Speak($"A SMAPI update is available. You have {current}; version {latest} is available.");
		if (MessageBox.Show(
				$"A SMAPI update is available.\n\nInstalled: {current}\nLatest: {latest}\n\n" +
				"KinetixModManager can download and install it for you automatically. Update SMAPI now?",
				"SMAPI Update Available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
			return;

		// SMAPI's installer updates an existing install in place, so the same routine used for a fresh
		// install performs the update; it self-reports success/failure by voice and handles its own fallback.
		await InstallSmapiAsync(DetectGameFolder("StardewValley"));
	}

	/// <summary>
	/// Best-effort detection of the installed SMAPI and Stardew Valley versions for the smapi.io request.
	/// Reads them from the SMAPI log header when present (it records "SMAPI x.y.z with Stardew Valley a.b.c"),
	/// otherwise falls back to the StardewModdingAPI.dll file version and a current game-version default.
	/// </summary>
	private (string Smapi, string Game) DetectStardewVersions()
	{
		string smapi = "";
		string game = "";
		try
		{
			string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
				"StardewValley", "ErrorLogs", "SMAPI-latest.txt");
			if (File.Exists(logPath))
			{
				foreach (string line in File.ReadLines(logPath))
				{
					var m = Regex.Match(line, @"SMAPI\s+(\d+\.\d+\.\d+)\s+with\s+Stardew Valley\s+(\d+\.\d+(?:\.\d+)?)",
						RegexOptions.IgnoreCase);
					if (m.Success) { smapi = m.Groups[1].Value; game = m.Groups[2].Value; break; }
				}
			}
		}
		catch { }

		if (string.IsNullOrEmpty(smapi))
		{
			try
			{
				string gameFolder = Path.GetDirectoryName(_settings.CurrentModsPath) ?? "";
				string dll = Path.Combine(gameFolder, "StardewModdingAPI.dll");
				if (File.Exists(dll))
				{
					var fv = System.Diagnostics.FileVersionInfo.GetVersionInfo(dll);
					if (!string.IsNullOrEmpty(fv.FileVersion))
						smapi = fv.FileVersion!.Split('+', ' ')[0];
				}
			}
			catch { }
		}

		if (string.IsNullOrEmpty(smapi)) smapi = "4.0.0";
		if (string.IsNullOrEmpty(game)) game = "1.6.15";
		return (smapi, game);
	}

	/// <summary>
	/// Downloads the latest file for <paramref name="mod"/> from Nexus Mods or GitHub and installs it
	/// via <see cref="InstallFromZip"/>. Pass <paramref name="silent"/> as <c>true</c> to suppress
	/// per-mod spoken feedback during a batch update.
	/// </summary>
    private async Task DownloadAndInstallUpdate(StardewMod mod, bool silent = false)
    {
        if (!string.IsNullOrEmpty(mod.GitHubRepo))
        {
            try
            {
                SetStatus($"Updating {mod.Name} from GitHub...");
                if (!silent) Speak($"Downloading {mod.Name}...");

                string? downloadUrl = await GetGitHubLatestReleaseZipUrl(mod.GitHubRepo);
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    throw new Exception("Could not retrieve latest release ZIP from GitHub.");
                }

                string destinationPath = Path.Combine(downloadsPath, $"{mod.UniqueId}_github_latest.zip");
                
                int lastPctSpoken = 0;
                var progress = new Progress<double>(pct =>
                {
                    Invoke(delegate
                    {
                        Text = $"Downloading {mod.Name}... {pct:F0}%";
                        int rounded = (int)(pct / 10.0) * 10;
                        if (rounded > lastPctSpoken && rounded < 100)
                        {
                            lastPctSpoken = rounded;
                            Speak($"{rounded}%");
                        }
                    });
                });

                await _nexusService.DownloadFileWithProgressAsync(downloadUrl, destinationPath, progress);
                
                string name = await ModFileSystem.ExtractModAsync(
                    destinationPath, _settings.CurrentModsPath, _allInstalledMods,
                    backupsPath, _settings.MaxBackupsPerMod, _settings.ActiveGame, LogError,
                    mod.NexusID, _nexusService, mod.GitHubRepo, _settings.CurrentGamePath);

                if (_settings.ActiveGame == "SkyrimSE" || _settings.ActiveGame == "Fallout4")
                {
                    string modDir = Path.Combine(_settings.CurrentModsPath, name);
                    string gameData = Path.Combine(_settings.CurrentGamePath, "Data");
                    ModFileSystem.DeployModFiles(modDir, gameData, true, LogError);
                    ModFileSystem.SyncPluginsFile(modDir, _settings.ActiveGame, true, LogError);
                }

                Invoke(delegate
                {
                    listUpdates.BeginUpdate();
                    for (int i = listUpdates.Items.Count - 1; i >= 0; i--)
                    {
                        if (listUpdates.Items[i] is StardewMod m &&
                            !string.IsNullOrEmpty(m.GitHubRepo) &&
                            m.GitHubRepo.Equals(mod.GitHubRepo, StringComparison.OrdinalIgnoreCase))
                            listUpdates.Items.RemoveAt(i);
                    }
                    listUpdates.EndUpdate();
                    _ = RefreshModList(checkUpdates: false);
                });

                if (!silent)
                {
                    _soundEngine.Play("connect");
                    Speak($"{mod.Name} updated successfully from GitHub.");
                    SetStatus($"Connected as {_nexusService.NexusUser}");
                }
                return;
            }
            catch (Exception ex)
            {
                _soundEngine.Play("error");
                LogError(mod.Name, "GitHub Download/Install Failure: " + ex.Message);
                MessageBox.Show($"Update failed for {mod.Name} from GitHub: {ex.Message}");
                return;
            }
        }

        if (!_nexusService.IsPremium)
        {
            if (!silent) OpenModPage();
            return;
        }
        try
        {
            SetStatus($"Updating {mod.Name}...");
            if (!silent) Speak($"Downloading {mod.Name}...");

            int lastReportedPercent = 0;
            Progress<double>? progress = null;
            if (!silent)
            {
                progress = new Progress<double>(pct =>
                {
                    int percent = (int)Math.Round(pct);
                    if (percent >= lastReportedPercent + 10)
                    {
                        lastReportedPercent = (percent / 10) * 10;
                        SetStatus($"Downloading {mod.Name}... {lastReportedPercent}%");
                    }
                    else
                    {
                        Invoke(delegate
                        {
                            string gameName = _settings.ActiveGame switch
                            {
                                "SkyrimSE" => "Skyrim Special Edition",
                                "Fallout4" => "Fallout 4",
                                _ => "Stardew Valley"
                            };
                            Text = $"{gameName} Kinetix Mod Manager - Status: Downloading {mod.Name}... {percent}%";
                        });
                    }
                });
            }

            string tempPath = await _nexusService.DownloadModUpdateAsync(mod, downloadsPath, progress);
            await InstallFromZip(tempPath, mod.NexusID);
            Invoke(delegate
            {
                listUpdates.BeginUpdate();
                for (int i = listUpdates.Items.Count - 1; i >= 0; i--)
                {
                    if (listUpdates.Items[i] is StardewMod m &&
                        !string.IsNullOrEmpty(m.NexusID) &&
                        m.NexusID.Equals(mod.NexusID, StringComparison.OrdinalIgnoreCase))
                        listUpdates.Items.RemoveAt(i);
                }
                listUpdates.EndUpdate();
                _ = RefreshModList(checkUpdates: false);
            });
            if (!silent)
            {
                _soundEngine.Play("connect");
                Speak($"{mod.Name} updated successfully.");
                SetStatus($"Connected as {_nexusService.NexusUser}");
            }
        }
        catch (Exception ex)
        {
            _soundEngine.Play("error");
            LogError(mod.Name, "Download/Install Failure: " + ex.Message);
            Invoke(delegate { MessageBox.Show($"Update failed for {mod.Name}: {ex.Message}"); });
        }
    }

	private async Task<string?> GetGitHubLatestReleaseVersionAsync(string repo)
	{
		try
		{
			using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
			req.Headers.UserAgent.ParseAdd($"KinetixModManager/{NexusService.AppVersion}");
			using var resp = await NexusService.HttpClient.SendAsync(req);
			if (!resp.IsSuccessStatusCode) return null;

			JObject json = JObject.Parse(await resp.Content.ReadAsStringAsync());
			string? tag = json["tag_name"]?.ToString();
			if (tag == null) return null;
			return tag.StartsWith("v") ? tag.Substring(1) : tag;
		}
		catch { return null; }
	}

	private async Task AutoMatchNexusIDs()
	{
		int matchCount = 0;
		int totalMods = 0;
		_isLoading = true;
		_ = RunLoadingLoop();
		Speak("Starting auto-matching process for installed mods.");
		
		try
		{
			var targetMods = _allInstalledMods.Where(m => !m.IsGroup && string.IsNullOrEmpty(m.NexusID) && string.IsNullOrEmpty(m.GitHubRepo)).ToList();
			totalMods = targetMods.Count;

			if (totalMods == 0)
			{
				Speak("No unmatched mods found.");
				_isLoading = false;
				_soundEngine.Play("load_complete");
				return;
			}

			int current = 0;
			foreach (var mod in targetMods)
			{
				current++;
				SetStatus($"Matching {current}/{totalMods}: {mod.Name}...");
				Speak($"Searching for {mod.Name}");

				var (results, total) = await _nexusService.SearchModsAsync("Search", mod.Name, 1, 5);
				if (results.Count > 0)
				{
					GameMod? bestMatch = null;
					foreach (var result in results)
					{
						if (result.Name.Equals(mod.Name, StringComparison.OrdinalIgnoreCase))
						{
							bestMatch = result;
							break;
						}
					}

					if (bestMatch == null)
					{
						var top = results[0];
						if (top.Name.Contains(mod.Name, StringComparison.OrdinalIgnoreCase) || 
							mod.Name.Contains(top.Name, StringComparison.OrdinalIgnoreCase))
						{
							bestMatch = top;
						}
					}

					if (bestMatch != null)
					{
						mod.NexusID = bestMatch.NexusID;
						
						string manifestPath = Path.Combine(mod.FolderPath, ".manager_manifest.json");
						if (_settings.ActiveGame == "StardewValley")
						{
							manifestPath = Path.Combine(mod.FolderPath, "manifest.json");
						}

						if (File.Exists(manifestPath))
						{
							JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
							if (_settings.ActiveGame == "StardewValley")
							{
								manifest["UpdateKeys"] = new JArray($"Nexus:{bestMatch.NexusID}");
							}
							else
							{
								manifest["NexusID"] = bestMatch.NexusID;
							}
							File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
						}
						
						matchCount++;
						Speak($"Matched with {bestMatch.Name}.");
					}
					else
					{
						Speak("No confident match found.");
					}
				}
				else
				{
					Speak("No search results found.");
				}

				await Task.Delay(500);
			}

			_isLoading = false;
			_soundEngine.Play("load_complete");
			Speak($"Auto-matching complete. Successfully matched {matchCount} out of {totalMods} mods.");
			_ = RefreshModList(checkUpdates: false);
		}
		catch (Exception ex)
		{
			_isLoading = false;
			_soundEngine.Play("error");
			LogError("AutoMatch", "Auto-match failed: " + ex.Message);
			MessageBox.Show("Auto-matching failed: " + ex.Message, "Error");
			Speak("Auto-matching failed.");
		}
	}

    private async Task CheckForAppUpdates(bool manual)
	{
		if (manual) Speak("Checking for manager updates...");
		try
		{
			NexusService.AppReleaseInfo? release = await _nexusService.GetLatestAppReleaseAsync();
			if (release == null) { if (manual) { Speak("Update check failed."); } return; }
			string tag = release.TagName;
			string target = tag.StartsWith("v") ? tag.Substring(1) : tag;
			if (IsNewerVersion(NexusService.AppVersion, target))
			{
				_soundEngine.Play("connect");
				Speak("A new version of the manager is available: " + tag + ".");
				if (!string.IsNullOrEmpty(release.DownloadUrl))
				{
					if (MessageBox.Show("Version " + tag + " is available! Would you like to automatically download and install this update?",
							"Update Available", MessageBoxButtons.YesNo) == DialogResult.Yes)
					{
						await DownloadAndInstallAppUpdateAsync(release);
					}
				}
				else
				{
					if (MessageBox.Show("Version " + tag + " is available! However, no automatic installer was found. Would you like to open the download page?",
							"Update Available", MessageBoxButtons.YesNo) == DialogResult.Yes)
					{
						Process.Start(new ProcessStartInfo(
							"https://github.com/SeanTerry01/Kinetix-Mod-Manager/releases/latest")
						{ UseShellExecute = true });
					}
				}
			}
			else if (manual)
			{
				Speak("The manager is up to date.");
				MessageBox.Show("You are running the latest version of Kinetix Mod Manager.", "Up to Date");
			}
		}
		catch (Exception ex)
		{
			if (manual) { Speak("Update check failed."); MessageBox.Show("Could not check for updates: " + ex.Message); }
		}
	}

	private async Task DownloadAndInstallAppUpdateAsync(NexusService.AppReleaseInfo release)
	{
		string originalTitle = Text;
		try
		{
			_isLoading = true;
			_ = RunLoadingLoop();
			Speak("Downloading manager update.");

			if (!Directory.Exists(downloadsPath))
			{
				Directory.CreateDirectory(downloadsPath);
			}

			string destinationPath = Path.Combine(downloadsPath, release.FileName);
			
			int lastPctSpoken = 0;
			var progress = new Progress<double>(pct =>
			{
				Invoke(delegate
				{
					Text = $"Downloading Manager Update... {pct:F0}%";
					int rounded = (int)(pct / 10.0) * 10;
					if (rounded > lastPctSpoken && rounded < 100)
					{
						lastPctSpoken = rounded;
						Speak($"{rounded}%");
					}
				});
			});

			await _nexusService.DownloadFileWithProgressAsync(release.DownloadUrl, destinationPath, progress);

			_isLoading = false;
			Speak("Download complete. Preparing installation.");

			string installerPath = destinationPath;

			// If it's a zip file, extract it
			if (release.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
			{
				string tempExtractDir = Path.Combine(downloadsPath, "KMM_Update_Extracted");
				if (Directory.Exists(tempExtractDir))
				{
					Directory.Delete(tempExtractDir, true);
				}
				Directory.CreateDirectory(tempExtractDir);

				System.IO.Compression.ZipFile.ExtractToDirectory(destinationPath, tempExtractDir);

				string[] exeFiles = Directory.GetFiles(tempExtractDir, "*.exe", SearchOption.AllDirectories);
				if (exeFiles.Length == 0)
				{
					throw new FileNotFoundException("Could not find any executable installer inside the downloaded zip archive.");
				}
				installerPath = exeFiles[0];
			}

			if (!File.Exists(installerPath))
			{
				throw new FileNotFoundException("Installer executable not found.");
			}

			Speak("Starting installer. The manager will now close to complete the update.");
			MessageBox.Show("The installer will now launch. Kinetix Mod Manager will close to allow the update to complete.", "Installing Update");

			Process.Start(new ProcessStartInfo(installerPath)
			{
				UseShellExecute = true
			});

			Application.Exit();
		}
		catch (Exception ex)
		{
			_isLoading = false;
			Text = originalTitle;
			LogError("AppUpdate", "Self-update failed: " + ex.Message);
			MessageBox.Show("Self-update failed: " + ex.Message, "Update Error");
			Speak("Update failed.");
		}
	}
}
