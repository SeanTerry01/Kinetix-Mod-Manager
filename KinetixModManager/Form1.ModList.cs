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

/// <summary>Installed-mod list, backups list, and Nexus connection/linking for Form1.</summary>
public partial class Form1
{
	/// <summary>
	/// Convenience wrapper that refreshes the mod list, backups list, profiles list,
	/// and SMAPI log in one call.
	/// </summary>
	private void RefreshAllData(bool checkUpdates)
	{
		if (_settings.ActiveGame == "None") return;
		_ = RefreshModList(checkUpdates);
		RefreshBackupsList();
		RefreshProfilesList();
		RefreshSmapiLog();
	}

	/// <summary>
	/// Scans the Mods folder, parses every manifest.json, resolves dependencies, applies the
	/// category map, and rebuilds <c>listInstalled</c>. Optionally fires update checks against
	/// the Nexus API when <paramref name="checkUpdates"/> is <c>true</c>.
	/// </summary>
	private async Task RefreshModList(bool checkUpdates)
	{
		if (_settings.ActiveGame == "None") return;
		if (string.IsNullOrEmpty(_settings.ApiKey))
		{
			if (!_isSettingsOpen)
			{
				SetStatus("Authentication Required");
				_soundEngine.Play("disconnect");
				ShowSettings();
			}
			return;
		}
		SetStatus("Connecting to Nexus...");
		if (!(await ValidateNexusConnection()))
		{
			SetStatus("Authentication Failed - Check API Key");
			_soundEngine.Play("error");
			return;
		}
		SetStatus("Connected as " + _nexusService.NexusUser);
		_soundEngine.Play("connect");
		Invoke(delegate
		{
			listInstalled.BeginUpdate();
			if (checkUpdates)
			{
				listUpdates.BeginUpdate();
			}
			listInstalled.Items.Clear();
			if (checkUpdates)
			{
				listUpdates.Items.Clear();
			}
		});
		_allInstalledMods.Clear();
		if (!Directory.Exists(_settings.CurrentModsPath))
		{
			Invoke(delegate
			{
				listInstalled.EndUpdate();
				if (checkUpdates)
				{
					listUpdates.EndUpdate();
				}
			});
			MessageBox.Show("Mods path invalid.");
			return;
		}
		string idMapPath = Path.Combine(AppSettings.AppDataFolder, "mod_id_map.json");
		string legacyIdMapPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mod_id_map.json");
		if (File.Exists(legacyIdMapPath) && !File.Exists(idMapPath))
		{
			try
			{
				File.Copy(legacyIdMapPath, idMapPath, overwrite: true);
			}
			catch {}
		}
		JObject nexusIdMap = (File.Exists(idMapPath) ? JObject.Parse(File.ReadAllText(idMapPath)) : new JObject()) ?? new JObject();
		_allInstalledMods = ModFileSystem.ScanMods(_settings.CurrentModsPath, nexusIdMap, _settings, _settings.ActiveGame, LogError);
		ModFileSystem.ResolveDependencies(_allInstalledMods, IsNewerVersion);
		HashSet<string> hashSet = new HashSet<string>(_allInstalledMods.Select(m => m.Category));
		Invoke(delegate
		{
			cmbCategoryFilter.BeginUpdate();
			string text2 = cmbCategoryFilter.SelectedItem?.ToString() ?? "All Categories";
			cmbCategoryFilter.Items.Clear();
			cmbCategoryFilter.Items.Add("All Categories");
			foreach (string item2 in hashSet.OrderBy((string c) => c))
			{
				cmbCategoryFilter.Items.Add(item2);
			}
			if (cmbCategoryFilter.Items.Contains(text2))
			{
				cmbCategoryFilter.SelectedItem = text2;
			}
			else
			{
				cmbCategoryFilter.SelectedIndex = 0;
			}
			cmbCategoryFilter.EndUpdate();
		});
		Invoke(delegate
		{
			RebuildInstalledListBox();
			listInstalled.EndUpdate();

			int oldSelectedIndex = listUpdates.SelectedIndex;
			object? oldSelectedItem = listUpdates.SelectedItem;

			listUpdates.BeginUpdate();
			for (int i = listUpdates.Items.Count - 1; i >= 0; i--)
			{
				if (listUpdates.Items[i] is StardewMod updateMod)
				{
					var installedMod = _allInstalledMods.FirstOrDefault(m =>
						(!string.IsNullOrEmpty(updateMod.UniqueId) && m.UniqueId == updateMod.UniqueId) ||
						(!string.IsNullOrEmpty(updateMod.Name) && m.Name.Equals(updateMod.Name, StringComparison.OrdinalIgnoreCase))
					);

					if (installedMod == null)
					{
						listUpdates.Items.RemoveAt(i);
					}
					else if (!IsNewerVersion(installedMod.Version, updateMod.LatestVersion))
					{
						listUpdates.Items.RemoveAt(i);
					}
				}
			}

			if (listUpdates.Items.Count > 0)
			{
				if (oldSelectedItem != null && listUpdates.Items.Contains(oldSelectedItem))
				{
					listUpdates.SelectedItem = oldSelectedItem;
				}
				else
				{
					listUpdates.SelectedIndex = Math.Min(Math.Max(0, oldSelectedIndex), listUpdates.Items.Count - 1);
				}

				if (listUpdates.Focused && listUpdates.SelectedItem != null)
				{
					string itemText = listUpdates.SelectedItem.ToString() ?? "";
					Speak($"{itemText}. {listUpdates.SelectedIndex + 1} of {listUpdates.Items.Count}");
				}
			}
			else if (listUpdates.Focused && oldSelectedIndex != -1)
			{
				Speak("List is empty.");
			}
			listUpdates.EndUpdate();

			if (checkUpdates)
			{
				listUpdates.BeginUpdate();
			}
		});
		if (!checkUpdates)
		{
			return;
		}
		List<IGrouping<string, StardewMod>> list = (from m in _allInstalledMods
			where !string.IsNullOrEmpty(m.NexusID) || !string.IsNullOrEmpty(m.GitHubRepo)
			group m by (!string.IsNullOrEmpty(m.NexusID) ? "Nexus:" + m.NexusID : "GitHub:" + m.GitHubRepo)).ToList();
		if (list.Count == 0)
		{
			_isLoading = false;
			_soundEngine.Play("load_complete");
			Speak("No mods found with Nexus IDs or GitHub Repos to check for updates.");
			return;
		}
		_isLoading = true;
		Interlocked.Exchange(ref _activeChecks, list.Count);
		Speak("Checking for updates.");
		_ = RunLoadingLoop();
		foreach (IGrouping<string, StardewMod> item3 in list)
		{
			_ = CheckForUpdates(item3.ToList());
		}
	}

	/// <summary>
	/// Re-renders <c>listInstalled</c> from <c>_allInstalledMods</c>, applying the current search
	/// query and category filter, and grouping mods by their top-level sub-folder.
	/// </summary>
	private void RebuildInstalledListBox()
	{
		string query = txtSearchInstalled.Text.Trim().ToLower();
		string category = cmbCategoryFilter.SelectedItem?.ToString() ?? "All Categories";
		bool flag = !string.IsNullOrEmpty(query) || category != "All Categories";
		listInstalled.BeginUpdate();
		StardewMod? stardewMod = listInstalled.SelectedItem as StardewMod;
		listInstalled.Items.Clear();
		foreach (IGrouping<string, StardewMod> item2 in from g in _allInstalledMods.Where((StardewMod m) => !m.IsGroup).GroupBy(delegate(StardewMod m)
			{
				string relativePath = Path.GetRelativePath(_settings.CurrentModsPath, m.FolderPath);
				int num2 = relativePath.IndexOf(Path.DirectorySeparatorChar);
				return (num2 != -1) ? relativePath.Substring(0, num2) : relativePath;
			})
			orderby g.Key
			select g)
		{
			List<StardewMod> list = item2.ToList();
			List<StardewMod> list2 = list.Where((StardewMod m) => (string.IsNullOrEmpty(query) || m.Name.ToLower().Contains(query) || m.Author.ToLower().Contains(query)) && (category == "All Categories" || m.Category == category)).ToList();
			if (list2.Count == 0)
			{
				continue;
			}
			if (flag || list.Count == 1)
			{
				foreach (StardewMod item3 in list2)
				{
					item3.IsSubMod = false;
					item3.IsGroup = false;
					listInstalled.Items.Add(item3);
				}
				continue;
			}
			bool flag2 = _expandedGroups.Contains(item2.Key);
			StardewMod item = new StardewMod
			{
				IsGroup = true,
				GroupName = item2.Key,
				IsExpanded = flag2,
				UniqueId = "GROUP:" + item2.Key,
				SubMods = list,
				FolderPath = Path.Combine(_settings.ModsPath, item2.Key)
			};
			listInstalled.Items.Add(item);
			if (!flag2)
			{
				continue;
			}
			foreach (StardewMod item4 in list2)
			{
				item4.IsSubMod = true;
				item4.IsGroup = false;
				listInstalled.Items.Add(item4);
			}
		}
		if (stardewMod != null)
		{
			for (int num = 0; num < listInstalled.Items.Count; num++)
			{
				if (listInstalled.Items[num] is StardewMod stardewMod2 && stardewMod2.UniqueId == stardewMod.UniqueId)
				{
					listInstalled.SelectedIndex = num;
					break;
				}
			}
		}
		if (listInstalled.SelectedIndex == -1 && listInstalled.Items.Count > 0)
		{
			listInstalled.SelectedIndex = 0;
		}
		if (listInstalled.Focused && listInstalled.SelectedItem != null)
		{
			string itemText = listInstalled.SelectedItem.ToString() ?? "";
			Speak($"{itemText}. {listInstalled.SelectedIndex + 1} of {listInstalled.Items.Count}");
		}
		listInstalled.EndUpdate();
	}

	/// <summary>Scans the backups directory and repopulates <c>listBackups</c> with <see cref="BackupItem"/> entries.</summary>
	private void RefreshBackupsList()
	{
		if (_settings.ActiveGame == "None") return;
		if (listBackups == null)
		{
			return;
		}
		int oldIndex = listBackups.SelectedIndex;
		string? oldName = (listBackups.SelectedItem as BackupItem)?.Name;

		listBackups.BeginUpdate();
		listBackups.Items.Clear();
		if (Directory.Exists(backupsPath))
		{
			string[] files = Directory.GetFiles(backupsPath, "*.zip");
			foreach (string text in files)
			{
				string text2 = Path.GetFileNameWithoutExtension(text);
				if (text2.Length > 16 && text2[text2.Length - 16] == '_')
				{
					text2 = text2.Substring(0, text2.Length - 16);
				}
				listBackups.Items.Add(new BackupItem
				{
					Name = text2,
					FullPath = text
				});
			}
		}

		if (listBackups.Items.Count > 0)
		{
			int newIndex = 0;
			if (!string.IsNullOrEmpty(oldName))
			{
				for (int i = 0; i < listBackups.Items.Count; i++)
				{
					if ((listBackups.Items[i] as BackupItem)?.Name == oldName)
					{
						newIndex = i;
						break;
					}
				}
			}
			listBackups.SelectedIndex = Math.Min(Math.Max(newIndex, oldIndex), listBackups.Items.Count - 1);

			if (listBackups.Focused && listBackups.SelectedItem != null)
			{
				Speak($"{listBackups.SelectedItem}. {listBackups.SelectedIndex + 1} of {listBackups.Items.Count}");
			}
		}
		else if (listBackups.Focused && oldIndex != -1)
		{
			Speak("List is empty.");
		}
		listBackups.EndUpdate();
	}

	/// <summary>
	/// Calls the Nexus Mods /users/validate endpoint to confirm the stored API key is valid.
	/// Returns <c>true</c> on success; speaks an error and returns <c>false</c> otherwise.
	/// </summary>
	private async Task<bool> ValidateNexusConnection() =>
		await _nexusService.ValidateAsync();

	/// <summary>Prompts the user to enter or replace their Nexus Mods API key, then refreshes the mod list.</summary>
	private void PromptForApiKey()
	{
		string text = Interaction.InputBox("Paste API Key:", "Nexus Login", _settings.ApiKey);
		if (!string.IsNullOrEmpty(text))
		{
			_settings.ApiKey = text.Trim();
			_settings.Save();
			_ = RefreshModList(checkUpdates: true);
		}
	}

	/// <summary>
	/// Prompts the user to enter a Nexus Mods ID or GitHub repo for the selected mod
	/// and updates its manifest.
	/// </summary>
	private async Task LinkModUpdateSource()
	{
		if (listInstalled.SelectedItem is StardewMod stardewMod3)
		{
			string currentId = stardewMod3.NexusID ?? stardewMod3.GitHubRepo ?? "";
			string input = Interaction.InputBox(
				$"Enter the Nexus Mod ID or GitHub Repo (owner/repo) for \"{stardewMod3.Name}\":",
				"Link Mod to Update Source",
				currentId
			).Trim();

			if (!string.IsNullOrEmpty(input))
			{
				bool isGitHub = input.Contains("/");
				string? val = (input == "0") ? null : input;

				try
				{
					string manifestPath = Path.Combine(stardewMod3.FolderPath, ".manager_manifest.json");
					if (_settings.ActiveGame == "StardewValley")
					{
						manifestPath = Path.Combine(stardewMod3.FolderPath, "manifest.json");
					}

					if (!File.Exists(manifestPath))
					{
						var tempManifest = new JObject();
						File.WriteAllText(manifestPath, tempManifest.ToString());
					}

					JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));

					if (isGitHub)
					{
						stardewMod3.GitHubRepo = val;
						stardewMod3.NexusID = null;

						if (_settings.ActiveGame == "StardewValley")
						{
							manifest["UpdateKeys"] = new JArray($"GitHub:{val}");
						}
						else
						{
							manifest["GitHubRepo"] = val;
							manifest["NexusID"] = null;
						}
					}
					else
					{
						stardewMod3.NexusID = val;
						stardewMod3.GitHubRepo = null;

						if (_settings.ActiveGame == "StardewValley")
						{
							manifest["UpdateKeys"] = new JArray($"Nexus:{val}");
						}
						else
						{
							manifest["NexusID"] = val;
							manifest["GitHubRepo"] = null;
						}
					}

					File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));

					try
					{
						string mapPath = Path.Combine(AppSettings.AppDataFolder, "mod_id_map.json");
						JObject mapObj = (File.Exists(mapPath) ? JObject.Parse(File.ReadAllText(mapPath)) : new JObject()) ?? new JObject();
						mapObj[stardewMod3.UniqueId] = val;
						File.WriteAllText(mapPath, mapObj.ToString());
					}
					catch (Exception ex) { LogError("ModIdMap", "Failed to persist Nexus ID mapping: " + ex.Message); }

					if (!string.IsNullOrEmpty(val))
					{
						if (isGitHub)
						{
							Speak("Updating mod details from GitHub...");
							using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{val}");
							req.Headers.UserAgent.ParseAdd($"KinetixModManager/{NexusService.AppVersion}");
							using var resp = await NexusService.HttpClient.SendAsync(req);
							if (resp.IsSuccessStatusCode)
							{
								var details = JObject.Parse(await resp.Content.ReadAsStringAsync());
								stardewMod3.Name = details["name"]?.ToString() ?? stardewMod3.Name;
								stardewMod3.Description = details["description"]?.ToString() ?? stardewMod3.Description;
								
								string? latestTag = await GetGitHubLatestReleaseVersionAsync(val);
								if (latestTag != null)
								{
									stardewMod3.Version = latestTag;
								}

								manifest = JObject.Parse(File.ReadAllText(manifestPath));
								manifest["Name"] = stardewMod3.Name;
								manifest["Version"] = stardewMod3.Version;
								manifest["Description"] = stardewMod3.Description;
								File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
							}
						}
						else
						{
							Speak("Updating mod details from Nexus...");
							var details = await _nexusService.GetModDetailsAsync(val);
							if (details != null)
							{
								stardewMod3.Name = details["name"]?.ToString() ?? stardewMod3.Name;
								stardewMod3.Version = details["version"]?.ToString() ?? stardewMod3.Version;
								stardewMod3.Author = details["author"]?.ToString() ?? stardewMod3.Author;
								stardewMod3.Description = details["summary"]?.ToString() ?? stardewMod3.Description;

								manifest = JObject.Parse(File.ReadAllText(manifestPath));
								manifest["Name"] = stardewMod3.Name;
								manifest["Version"] = stardewMod3.Version;
								manifest["Author"] = stardewMod3.Author;
								manifest["Description"] = stardewMod3.Description;
								File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));
							}
						}
					}

					Speak("Mod linked successfully.");
					_ = RefreshModList(checkUpdates: false);
				}
				catch (Exception ex)
				{
					MessageBox.Show("Failed to save Update Source: " + ex.Message, "Error");
					Speak("Failed to link mod.");
				}
			}
		}
	}
}
