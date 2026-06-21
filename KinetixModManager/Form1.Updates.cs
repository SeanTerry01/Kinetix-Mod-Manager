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
					? Loc.T("updates.checkComplete", listUpdates.Items.Count)
					: Loc.T("updates.checkCompleteNone"));
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
		Speak(Loc.T("updates.smapiAvailableSpeak", current, latest));
		if (MessageBox.Show(
				Loc.T("updates.smapiBox", current, latest),
				Loc.T("updates.smapiTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
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
        // Updates install the mod enabled; remember if it was disabled so we can turn it back off afterward.
        bool wasDisabled = !mod.IsEnabled;
        if (!string.IsNullOrEmpty(mod.GitHubRepo))
        {
            try
            {
                SetStatus(Loc.T("updates.updatingGitHub", mod.Name));
                if (!silent) Speak(Loc.T("updates.downloading", mod.Name));

                string? downloadUrl = await GetGitHubLatestReleaseZipUrl(mod.GitHubRepo);
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    throw new Exception("Could not retrieve latest release ZIP from GitHub.");
                }

                string destinationPath = Path.Combine(downloadsPath, $"{mod.UniqueId}_github_latest.zip");

                ProgressAnnouncer? dlProgress = silent ? null : NewProgress(mod.Name, installing: false);
                await _nexusService.DownloadFileWithProgressAsync(downloadUrl, destinationPath, dlProgress);
                dlProgress?.Complete();

                ProgressAnnouncer? instProgress = silent ? null : NewProgress(mod.Name, installing: true);
                string name = await ModFileSystem.ExtractModAsync(
                    destinationPath, _settings.CurrentModsPath, _allInstalledMods,
                    backupsPath, _settings.MaxBackupsPerMod, _settings.ActiveGame, LogError,
                    mod.NexusID, _nexusService, mod.GitHubRepo, _settings.CurrentGamePath, null, instProgress);
                instProgress?.Complete();

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
                });

                await RefreshModList(checkUpdates: false);

                // Reconcile assets after the rescan; forceRelink replaces the old version's hard links,
                // which would otherwise still point at the now-deleted previous file data.
                if (IsBethesdaGame)
                {
                    SyncBethesdaDeployment(new HashSet<string>(new[] { name }, StringComparer.OrdinalIgnoreCase));
                    Invoke(delegate { RefreshModPriorityList(); });
                }

                await ReapplyDisabledIfNeeded(mod, wasDisabled);

                if (!silent)
                {
                    _soundEngine.Play("connect");
                    Speak(Loc.T("updates.githubSuccess", mod.Name));
                    SetStatus(Loc.T("status.connectedAs", _nexusService.NexusUser));
                    AnnounceUpdatesListEmptyIfFocused();
                }
                return;
            }
            catch (Exception ex)
            {
                _soundEngine.Play("error");
                LogError(mod.Name, "GitHub Download/Install Failure: " + ex.Message);
                MessageBox.Show(Loc.T("updates.githubFailBox", mod.Name, ex.Message));
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
            SetStatus(Loc.T("updates.updating", mod.Name));
            if (!silent) Speak(Loc.T("updates.downloading", mod.Name));

            ProgressAnnouncer? progress = silent ? null : NewProgress(mod.Name, installing: false);
            string tempPath = await _nexusService.DownloadModUpdateAsync(mod, downloadsPath, progress);
            progress?.Complete();
            await InstallFromZip(tempPath, mod.NexusID, silent: silent);
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
            });
            await RefreshModList(checkUpdates: false);
            await ReapplyDisabledIfNeeded(mod, wasDisabled);
            if (!silent)
            {
                _soundEngine.Play("connect");
                Speak(Loc.T("updates.success", mod.Name));
                SetStatus(Loc.T("status.connectedAs", _nexusService.NexusUser));
                AnnounceUpdatesListEmptyIfFocused();
            }
        }
        catch (Exception ex)
        {
            _soundEngine.Play("error");
            LogError(mod.Name, "Download/Install Failure: " + ex.Message);
            Invoke(delegate { MessageBox.Show(Loc.T("updates.failBox", mod.Name, ex.Message)); });
        }
    }

	/// <summary>
	/// Announces "List is empty" after the last available update has been installed and removed in place. A screen
	/// reader only re-reads a list's state on a focus change, so emptying it programmatically would otherwise stay
	/// silent. We put focus on the now-empty Updates list (so the reader speaks its name) and then add the empty
	/// status, but only while the Updates tab is showing — we never yank focus if the user has moved on.
	/// </summary>
	private void AnnounceUpdatesListEmptyIfFocused()
	{
		if (_isLoading || listUpdates.Items.Count != 0 || CurrentTab() != AppTab.Updates)
			return;
		// Focusing fires GotFocus -> List_Enter -> AnnounceListEmpty; if it is already focused that path does not
		// fire, so call the announcer directly too. AnnounceListEmpty de-dupes, so this never doubles up.
		if (!listUpdates.Focused)
			listUpdates.Focus();
		AnnounceListEmpty(listUpdates);
	}

	/// <summary>
	/// If <paramref name="original"/> was disabled before being updated, re-disables the freshly installed copy
	/// (updates always install a mod enabled), so updating never silently turns a mod back on. The new copy is
	/// matched by Nexus ID or GitHub repo; its folder is renamed to the disabled form (a leading dot) and the mod
	/// list re-reconciled so deployment and plugins.txt reflect the disabled state.
	/// </summary>
	private async Task ReapplyDisabledIfNeeded(StardewMod original, bool wasDisabled)
	{
		if (!wasDisabled) return;
		StardewMod? updated = _allInstalledMods.FirstOrDefault(m => !m.IsGroup && m.IsEnabled &&
			((!string.IsNullOrEmpty(original.NexusID) && original.NexusID.Equals(m.NexusID, StringComparison.OrdinalIgnoreCase)) ||
			 (!string.IsNullOrEmpty(original.GitHubRepo) && original.GitHubRepo.Equals(m.GitHubRepo, StringComparison.OrdinalIgnoreCase))));
		if (updated == null) return;
		try
		{
			string dir = Path.GetDirectoryName(updated.FolderPath) ?? "";
			string folderName = Path.GetFileName(updated.FolderPath);
			if (folderName.StartsWith(".")) return;   // already disabled
			string target = Path.Combine(dir, "." + folderName);
			Directory.Move(updated.FolderPath, target);
			updated.FolderPath = target;
			updated.IsEnabled = false;
			_soundEngine.Play("disable");
			await RefreshModList(checkUpdates: false);
		}
		catch (Exception ex)
		{
			LogError(original.Name, "Could not re-disable after update: " + ex.Message);
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
		Speak(Loc.T("updates.autoMatchStart"));
		
		try
		{
			var targetMods = _allInstalledMods.Where(m => !m.IsGroup && string.IsNullOrEmpty(m.NexusID) && string.IsNullOrEmpty(m.GitHubRepo)).ToList();
			totalMods = targetMods.Count;

			if (totalMods == 0)
			{
				Speak(Loc.T("updates.noUnmatched"));
				_isLoading = false;
				_soundEngine.Play("load_complete");
				return;
			}

			int current = 0;
			foreach (var mod in targetMods)
			{
				current++;
				SetStatus(Loc.T("updates.matchingStatus", current, totalMods, mod.Name));
				Speak(Loc.T("updates.searchingFor", mod.Name));

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
						Speak(Loc.T("updates.matchedWith", bestMatch.Name));
					}
					else
					{
						Speak(Loc.T("updates.noConfidentMatch"));
					}
				}
				else
				{
					Speak(Loc.T("updates.noResults"));
				}

				await Task.Delay(500);
			}

			_isLoading = false;
			_soundEngine.Play("load_complete");
			Speak(Loc.T("updates.autoMatchComplete", matchCount, totalMods));
			_ = RefreshModList(checkUpdates: false);
		}
		catch (Exception ex)
		{
			_isLoading = false;
			_soundEngine.Play("error");
			LogError("AutoMatch", "Auto-match failed: " + ex.Message);
			MessageBox.Show(Loc.T("updates.autoMatchFailBox", ex.Message), Loc.T("common.error"));
			Speak(Loc.T("updates.autoMatchFailed"));
		}
	}

    private async Task CheckForAppUpdates(bool manual)
	{
		if (manual) Speak(Loc.T("updates.checkingManager"));
		try
		{
			NexusService.AppReleaseInfo? release = await _nexusService.GetLatestAppReleaseAsync();
			if (release == null) { if (manual) { Speak(Loc.T("updates.checkFailed")); } return; }
			string tag = release.TagName;
			string target = tag.StartsWith("v") ? tag.Substring(1) : tag;
			if (IsNewerVersion(NexusService.AppVersion, target))
			{
				_soundEngine.Play("connect");
				Speak(Loc.T("updates.newVersion", tag));
				if (!string.IsNullOrEmpty(release.DownloadUrl))
				{
					if (MessageBox.Show(Loc.T("updates.versionAvailDownload", tag),
							Loc.T("updates.updateAvailTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
					{
						await DownloadAndInstallAppUpdateAsync(release);
					}
				}
				else
				{
					if (MessageBox.Show(Loc.T("updates.versionAvailNoInstaller", tag),
							Loc.T("updates.updateAvailTitle"), MessageBoxButtons.YesNo) == DialogResult.Yes)
					{
						Process.Start(new ProcessStartInfo(
							"https://github.com/SeanTerry01/Kinetix-Mod-Manager/releases/latest")
						{ UseShellExecute = true });
					}
				}
			}
			else if (manual)
			{
				Speak(Loc.T("updates.upToDate"));
				MessageBox.Show(Loc.T("updates.upToDateBox"), Loc.T("updates.upToDateTitle"));
			}
		}
		catch (Exception ex)
		{
			if (manual) { Speak(Loc.T("updates.checkFailed")); MessageBox.Show(Loc.T("updates.checkFailBox", ex.Message)); }
		}
	}

	private async Task DownloadAndInstallAppUpdateAsync(NexusService.AppReleaseInfo release)
	{
		string originalTitle = Text;
		try
		{
			_isLoading = true;
			_ = RunLoadingLoop();
			Speak(Loc.T("updates.downloadingUpdate"));

			if (!Directory.Exists(downloadsPath))
			{
				Directory.CreateDirectory(downloadsPath);
			}

			string destinationPath = Path.Combine(downloadsPath, release.FileName);

			ProgressAnnouncer progress = NewProgress(Loc.T("updates.appUpdateName"), installing: false);
			await _nexusService.DownloadFileWithProgressAsync(release.DownloadUrl, destinationPath, progress);
			progress.Complete();

			_isLoading = false;
			Speak(Loc.T("updates.downloadComplete"));

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

			Speak(Loc.T("updates.startingInstaller"));
			MessageBox.Show(Loc.T("updates.installerBox"), Loc.T("updates.installingTitle"));

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
			MessageBox.Show(Loc.T("updates.selfUpdateFailBox", ex.Message), Loc.T("updates.updateErrorTitle"));
			Speak(Loc.T("updates.failed"));
		}
	}
}
