using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
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

/// <summary>The accessibility-mod suite installer dialog and its helpers for Form1.</summary>
public partial class Form1
{
	// -------------------------------------------------------------------------
	// Moonlight Access — the Moonlight Peaks accessibility mod
	// -------------------------------------------------------------------------
	// This is the one suite entry the manager cannot yet fetch on its own, because the mod is not published
	// anywhere it can download from. Everything else about it is wired up: it appears in the suite list, its
	// installed state is detected, and the F3 documentation viewer knows about it. Only the source is missing.
	//
	// TO ENABLE DOWNLOADING, set both constants below and nothing else needs to change:
	//   * published on Nexus     -> Type = "Nexus",        Source = the numeric mod id, e.g. "42"
	//   * GitHub releases        -> Type = "GitHub",       Source = "owner/repo"
	//   * a fixed release asset  -> Type = "GitHubStatic", Source = the full .zip URL
	// While Source is empty the entry is shown as unavailable and the installer skips it with a spoken note,
	// rather than failing partway through installing the rest of the suite.

	/// <summary>How Moonlight Access should be fetched once it is published. See the note above.</summary>
	private const string MoonlightAccessType = "Nexus";

	/// <summary>Where Moonlight Access is fetched from; empty until the mod is published.</summary>
	private const string MoonlightAccessSource = "";

	/// <summary>The plugin GUID Moonlight Access registers itself under, used to spot it among installed mods.</summary>
	private const string MoonlightAccessGuid = "com.moonlightaccess.core";

	private void ShowAccessibilitySuiteDialog()
	{
		string game = _settings.ActiveGame;
		string gameName = GameProfiles.DisplayNameFor(game);

		// ⚠️ Minecraft asks BEFORE the list exists, and it is the one game that has to.
		//
		// Every other game's suite is a fixed list. Minecraft's is one of two rival accessibility mods plus
		// whatever that one needs, so "which mods do you need" has no answer until the user has picked. The
		// list used to be built from the default and the question asked later, during the install — which
		// meant someone who wanted Minecraft Access saw United Minecraft listed as though it were decided,
		// and, worse, picking Minecraft Access at that late prompt still installed United Minecraft's jar,
		// because the loop walked the list built before the question was asked.
		//
		// Asking first makes the list a consequence of the answer, which is what Sean asked for and what
		// removes the bug in the same stroke.
		if (GameProfiles.IsGame(game, GameProfiles.Minecraft) && EnsureAccessModChosen(alwaysAsk: true) is null)
		{
			Speak(Loc.T("common.changesCancelled"));
			return;
		}

		// Shown inside the main window rather than as one of its own — see Form1.InlineView. Escape is handled by
		// the view. The main window used to be hidden while this was open, to keep the per-mod confirmations and
		// progress on the panel rather than flashing to the window behind; being part of that window now achieves
		// the same thing without hiding anything.
		ShowInlineView(Loc.T("suite.installerTitle", gameName), (container, closeView) =>
		{
		TableLayoutPanel layout = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(15),
			RowCount = 3
		};
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 15f));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65f));
		layout.RowStyles.Add(new RowStyle(SizeType.Percent, 20f));

		Label lblTitle = new Label
		{
			Text = Loc.T("suite.statusTitle", gameName),
			Font = new Font("Segoe UI", 14f, FontStyle.Bold),
			AutoSize = true,
			Dock = DockStyle.Fill
		};
		layout.Controls.Add(lblTitle, 0, 0);

		ListBox lstStatus = new ListBox
		{
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 11f),
			AccessibleName = Loc.T("suite.statusName"),
			AccessibleDescription = Loc.T("suite.statusDesc")
		};
		// Announce the position on focus and on each arrow-key move, the same way the main form's lists do
		// (GotFocus also fires when focus returns to the list after another dialog closes).
		lstStatus.GotFocus += List_Enter;
		lstStatus.SelectedIndexChanged += List_SelectedIndexChanged;

		bool loaderInstalled = false;
		bool allModsInstalled = true;
		var suiteItems = new List<SuiteItem>();

		if (GameProfiles.IsGame(game, GameProfiles.StardewValley))
		{
			string gameFolder = ResolveStardewFolder();
			string smapiPath = Path.Combine(gameFolder, "StardewModdingAPI.exe");
			loaderInstalled = File.Exists(smapiPath);

			suiteItems.Add(new SuiteItem("SMAPI (Mod Loader)", loaderInstalled, "Loader", "https://smapi.io"));
			suiteItems.Add(new SuiteItem("Stardew Access", HasModUniqueId("StardewAccess"), "GitHub", "stardew-access/stardew-access"));
			suiteItems.Add(new SuiteItem("Kokoro Library", HasModUniqueId("Kokoro"), "GitHubStatic", "https://github.com/Shockah/Stardew-Valley-Mods/releases/download/release%2Fkokoro%2F3.0.0/Kokoro.3.0.0.zip"));
			suiteItems.Add(new SuiteItem("Project Fluent", HasModUniqueId("ProjectFluent"), "GitHubStatic", "https://github.com/Shockah/Stardew-Valley-Mods/releases/download/release%2Fproject-fluent%2F2.0.0/ProjectFluent.2.0.0.zip"));
		}
		else if (GameProfiles.IsGame(game, GameProfiles.SkyrimSE))
		{
			string gameFolder = string.IsNullOrEmpty(_settings.CurrentGamePath) ? DetectGameFolder(game) : _settings.CurrentGamePath;
			// Not File.Exists on the loader exe: SKSE is installed when its files are there, and plenty of
			// players — most GOG ones — start it through the SSE Engine Fixes preloader and have no loader exe.
			loaderInstalled = ModFileSystem.IsScriptExtenderInstalled(game, gameFolder);

			suiteItems.Add(new SuiteItem("SKSE64 (Script Extender)", loaderInstalled, "Loader", "https://skse.silverlock.org",
				ScriptExtenderStatusLine(game, gameFolder)));
			suiteItems.Add(new SuiteItem("Address Library for SKSE Plugins", HasModNameContains("Address Library") || HasModNameContains("AddressLibrary"), "Nexus", "32444"));
			suiteItems.Add(new SuiteItem("SkyUI", HasModNameContains("SkyUI"), "Nexus", "12604"));
			suiteItems.Add(new SuiteItem("Better MessageBox Controls", HasModNameContains("Better MessageBox Controls") || HasModNameContains("BetterMessageBoxControls"), "Nexus", "1428"));
			suiteItems.Add(new SuiteItem("UIExtensions", HasModNameContains("UIExtensions"), "Nexus", "17561"));
			suiteItems.Add(new SuiteItem("powerofthree's Papyrus Extender", HasModNameContains("Papyrus Extender") || HasModNameContains("PapyrusExtender"), "Nexus", "22854"));
			suiteItems.Add(new SuiteItem("powerofthree's Tweaks", HasModNameContains("powerofthree's Tweaks") || HasModNameContains("powerofthree'sTweaks") || HasModNameContains("po3's Tweaks"), "Nexus", "51073"));
			suiteItems.Add(new SuiteItem("Dylbills Papyrus Functions", HasModNameContains("Dylbills Papyrus Functions") || HasModNameContains("DylbillsPapyrusFunctions") || HasModNameContains("DbMiscFunctions"), "Nexus", "65410"));
			// SSE Engine Fixes has historically shipped as two files on the same Nexus page: the main SKSE plugin
			// (installs like a normal mod) and a "Preloader" whose d3dx9_42.dll must sit in the game root. It is
			// one entry here, installed only when every part this copy's build still calls for is present.
			//
			// Which parts those are is asked of ModPartRules rather than spelled out: from Skyrim 1.7.99 SKSE
			// preloads the plugin itself and the preloader is not part of the mod any more, so hard-coding the
			// d3dx9_42.dll check reported a perfectly complete install as missing on every updated game.
			suiteItems.Add(new SuiteItem("SSE Engine Fixes",
				(HasModNameContains("SSE Engine Fixes") || HasModNameContains("EngineFixes"))
					&& AllNeededPartsInstalled("17230", gameFolder),
				"Nexus", "17230"));
			suiteItems.Add(new SuiteItem("Media Keys Fix", HasModNameContains("Media Keys Fix") || HasModNameContains("MediaKeysFix"), "Nexus", "92948"));
			// "Stay At The System Page - AE" (Nexus 67883) was part of this suite until Skyrim Access stopped
			// needing it. Removed rather than left in as optional: the suite is the list of what you must have for
			// the game to be playable, and anything in it that is not needed is a mod somebody installs, updates
			// and troubleshoots for no reason. Anyone who already has it keeps it — it stays an ordinary installed
			// mod, and nothing here uninstalls anything.
			suiteItems.Add(new SuiteItem("Skyrim Access", HasModNameContains("Skyrim Access") || HasModNameContains("SkyrimAccess") || HasModNameContains("SkyrimTTS"), "Nexus", "181131"));
		}
		else if (GameProfiles.IsGame(game, GameProfiles.MoonlightPeaks))
		{
			string gameFolder = string.IsNullOrEmpty(_settings.CurrentGamePath) ? DetectGameFolder(game) : _settings.CurrentGamePath;
			loaderInstalled = IsBepInExInstalled(gameFolder);

			suiteItems.Add(new SuiteItem("BepInEx (Mod Loader)", loaderInstalled, "Loader", "https://github.com/BepInEx/BepInEx"));
			// Matched by GUID first (the plugin's own stable id) and by name second, so it is recognised whether
			// it was installed through the manager or dropped in by hand.
			suiteItems.Add(new SuiteItem(
				"Moonlight Access",
				HasModUniqueId(MoonlightAccessGuid) || HasModNameContains("Moonlight Access") || HasModNameContains("MoonlightAccess"),
				MoonlightAccessType,
				MoonlightAccessSource));
		}
		else if (GameProfiles.IsGame(game, GameProfiles.Witcher3))
		{
			string gameFolder = string.IsNullOrEmpty(_settings.CurrentGamePath) ? DetectGameFolder(game) : _settings.CurrentGamePath;

			// Nothing to install as a loader: the game loads its own mods folder, and the accessibility mod's
			// native half is an .asi beside the game exe, loaded by the ASI loader its installer places there.
			loaderInstalled = true;

			// Recognised two ways, because either half can be present on its own if an install went wrong: the
			// mod folder the engine loads, and the native plugin beside the exe that does the speaking.
			bool accessInstalled =
				Directory.Exists(Path.Combine(gameFolder, "mods", "modWitcherAccess")) ||
				Directory.Exists(Path.Combine(gameFolder, "mods", "~modWitcherAccess")) ||
				File.Exists(Path.Combine(gameFolder, "bin", "x64", "WitcherAccess.asi"));

			// No source: WitcherAccess is still in testing and is installed by running its author's installer, so
			// the manager reports on it and says where it stands rather than pretending it can fetch it. Install
			// it through Mods, Install Mod From File with the release zip — the manager unpacks it, runs the
			// installer and picks the mod up afterwards.
			suiteItems.Add(new SuiteItem("WitcherAccess", accessInstalled, "Manual", ""));
		}
		else if (GameProfiles.IsGame(game, GameProfiles.Minecraft))
		{
			string root = MinecraftRootFolder();

			// Fabric is the loader, and it is installed without its exe — see FabricInstaller. Judged by the
			// version folder rather than a launcher entry, because the entry can be deleted from the launcher
			// while the install itself is perfectly fine.
			//
			// The version is the pinned one, falling back to whatever is already installed. Requiring a pinned
			// version reported a hand-installed Fabric as missing, purely because the manager had not been the
			// one to put it there.
			string mcVersion = MinecraftGameVersionInUse(root);
			loaderInstalled = mcVersion.Length > 0 && FabricInstaller.IsInstalledFor(root, mcVersion);

			// Adopt what was found, so everything downstream — which mod build to fetch, what the update check
			// compares against — is working from the version actually installed rather than from nothing.
			if (loaderInstalled && _settings.MinecraftGameVersion != mcVersion)
			{
				_settings.MinecraftGameVersion = mcVersion;
				_settings.Save();
			}

			suiteItems.Add(new SuiteItem(
				Loc.T("mc.suite.fabric"), loaderInstalled, "Loader", "https://fabricmc.net",
				loaderInstalled ? FabricStatusLine(root) : ""));

			// Adopt whichever accessibility mod is already installed, the same way the Fabric version above is
			// adopted. Without this the suite quietly assumes the default, so somebody already running
			// Minecraft Access would be shown United Minecraft and Fabric API as missing and offered the pair
			// — installing a second, rival accessibility mod over a working setup.
			if (string.IsNullOrEmpty(_settings.MinecraftAccessModId))
			{
				MinecraftSuiteMod? present = MinecraftSuite.AccessMods
					.FirstOrDefault(m => HasModUniqueId(m.FabricModId));

				if (present != null)
				{
					_settings.MinecraftAccessModId = present.Id;
					_settings.Save();
				}
			}

			// Only the CHOSEN accessibility mod is listed, along with whatever it needs. The user is never asked
			// about Fabric API: whether it is required is a consequence of which mod they picked, and both
			// answers are read from the mods themselves rather than assumed.
			MinecraftSuiteMod chosen = MinecraftSuite.AccessModFor(_settings.MinecraftAccessModId);

			foreach (MinecraftSuiteMod part in MinecraftSuite.InstallPlanFor(chosen))
			{
				suiteItems.Add(new SuiteItem(
					part.DisplayName,
					HasModUniqueId(part.FabricModId),
					part.Origin == MinecraftModOrigin.GitHubRelease ? "GitHubJar" : "ModrinthJar",
					part.Source));
			}
		}
		else
		{
			string gameFolder = string.IsNullOrEmpty(_settings.CurrentGamePath) ? DetectGameFolder(game) : _settings.CurrentGamePath;
			// See the SKSE branch: judged by the script extender's files, not by its loader exe alone.
			loaderInstalled = ModFileSystem.IsScriptExtenderInstalled(game, gameFolder);

			suiteItems.Add(new SuiteItem("F4SE (Script Extender)", loaderInstalled, "Loader", "https://f4se.silverlock.org",
				ScriptExtenderStatusLine(game, gameFolder)));
			suiteItems.Add(new SuiteItem("Address Library for F4SE Plugins", HasModNameContains("Address Library") || HasModNameContains("AddressLibrary"), "Nexus", "47327"));
			suiteItems.Add(new SuiteItem("Mod Configuration Menu (MCM)", HasModNameContains("Mod Configuration Menu") || HasModNameContains("MCM"), "Nexus", "21497"));
			suiteItems.Add(new SuiteItem("Fallout 4 Access", HasModNameContains("Fallout4Access") || HasModNameContains("Fallout 4 Access"), "Nexus", "100314"));
		}

		foreach (var item in suiteItems)
		{
			string statusText = item.IsInstalled ? Loc.T("suite.installed") + item.Detail : Loc.T("suite.notInstalled");
			lstStatus.Items.Add(Loc.T("suite.statusLine", item.Name, statusText));
			// SMAPI (the Stardew "Loader") now installs automatically too, so a missing loader
			// should also enable the Install button rather than being treated as already handled.
			if (!item.IsInstalled)
			{
				allModsInstalled = false;
			}
		}
		if (lstStatus.Items.Count > 0)
		{
			lstStatus.SelectedIndex = 0;
		}

		// "Install Missing Suite Mods" is right for a fixed checklist and wrong for Minecraft, where the screen
		// is now the consequence of a choice just made: what follows it is a continuation, not a repair.
		string installText = GameProfiles.IsGame(game, GameProfiles.Minecraft)
			? Loc.T("suite.installMinecraft")
			: Loc.T("suite.installMissing");

		Button btnInstall = new Button
		{
			Text = allModsInstalled ? Loc.T("suite.suiteInstalled") : installText,
			Enabled = !allModsInstalled,
			Height = 45,
			Dock = DockStyle.Fill,
			Font = new Font("Segoe UI", 12f, FontStyle.Bold)
		};

		// Resolves the best "go download it yourself" page for a suite entry. Nexus mods open straight to
		// their Files tab; loaders open their official site; GitHub mods open their Releases page; and
		// GitHubStatic entries are a direct zip link.
		string SuiteItemUrl(SuiteItem item)
		{
			// An entry with no source yet (a mod the manager knows about but that isn't published) has no page
			// to open, so it falls through to the game's own mod listing rather than a broken link.
			if (string.IsNullOrEmpty(item.Source) && item.Type != "Loader")
			{
				// A game whose mods do not come from Nexus has no Nexus listing to fall back to, and
				// CurrentGameDomain would answer with some other game's — the exact class of wrong answer the
				// GameProfiles registry exists to prevent.
				GameProfile? sourceProfile = GameProfiles.Find(game);
				return sourceProfile?.ModSource == ModSource.Modrinth
					? "https://modrinth.com/mods?g=categories:fabric"
					: $"https://www.nexusmods.com/{_nexusService.CurrentGameDomain}";
			}

			switch (item.Type)
			{
				case "Nexus":
					string domain = _nexusService.CurrentGameDomain;
					return $"https://www.nexusmods.com/{domain}/mods/{item.Source}?tab=files";
				case "GitHub":
				case "GitHubJar":
					return $"https://github.com/{item.Source}/releases";
				case "ModrinthJar":
					return $"https://modrinth.com/mod/{item.Source}";
				case "Loader":
					// The Skyrim/Fallout script extenders are on Nexus now; open their Nexus Files page rather
					// than the legacy Silverlock site. Stardew's loader (SMAPI) keeps its own site URL.
					return GameProfiles.BaseId(game) switch
					{
						"SkyrimSE" => "https://www.nexusmods.com/skyrimspecialedition/mods/30379?tab=files",
						"Fallout4" => "https://www.nexusmods.com/fallout4/mods/42147?tab=files",
						_          => item.Source
					};
				default: // "GitHubStatic" (direct zip link)
					return item.Source;
			}
		}

		// Enter on a list item opens that single mod's download page, so the user can grab missing mods
		// one at a time at their own pace instead of running the whole bulk install. (The bulk install is
		// still available on the button below via Tab.)
		lstStatus.KeyDown += (s, e) =>
		{
			if (e.KeyCode != Keys.Enter) return;
			int idx = lstStatus.SelectedIndex;
			if (idx < 0 || idx >= suiteItems.Count) return;

			e.Handled = true;
			e.SuppressKeyPress = true;
			var item = suiteItems[idx];
			string url = SuiteItemUrl(item);
			Speak(item.IsInstalled
				? Loc.T("suite.openInstalledPage", item.Name)
				: Loc.T("suite.openDownloadPage", item.Name));
			try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
			catch (Exception ex) { LogFailure(item.Name, "Failed to open download page", ex); }
		};

		layout.Controls.Add(lstStatus, 0, 1);
		layout.Controls.Add(btnInstall, 0, 2);

		bool isInstalling = false;
		btnInstall.Click += async delegate
		{
			if (isInstalling) return;
			isInstalling = true;
			btnInstall.Enabled = false;
			btnInstall.Text = Loc.T("suite.installing");
			UseWaitCursor = true;

			// Gathered through the run and said once at the end. See the box below closeView.
			var noBuildFor = new List<string>();

			try
			{
				Speak(Loc.T("suite.startInstall"));
				
				// SMAPI is the Stardew mod loader; install it first (and automatically) so the
				// accessibility mods below have something to load them. Other games' loaders are
				// handled inside the loop via the script-extender installer.
				if (!loaderInstalled && GameProfiles.IsGame(game, GameProfiles.StardewValley))
				{
					loaderInstalled = await InstallSmapiAsync(ResolveStardewFolder());
				}
				// BepInEx is Moonlight Peaks' loader and installs the same way: first, and on its own, so the
				// accessibility mod below has something to load it.
				else if (!loaderInstalled && GameProfiles.IsGame(game, GameProfiles.MoonlightPeaks))
				{
					loaderInstalled = await InstallBepInExAsync(
						string.IsNullOrEmpty(_settings.CurrentGamePath) ? DetectGameFolder(game) : _settings.CurrentGamePath);
				}
				// Fabric is Minecraft's loader and installs the same way: first, and on its own. It is also the
				// one loader the manager installs entirely by itself, with no exe and no download beyond a
				// small JSON file — see FabricInstaller.
				else if (!loaderInstalled && GameProfiles.IsGame(game, GameProfiles.Minecraft))
				{
					MinecraftSuiteMod? forLoader = EnsureAccessModChosen();
					if (forLoader != null)
						loaderInstalled = await InstallFabricAsync(MinecraftRootFolder(), forLoader);
				}

				foreach (var item in suiteItems)
				{
					if (item.IsInstalled) continue;
					if (item.Type == "Loader" && (GameProfiles.IsAnyGame(game, GameProfiles.StardewValley, GameProfiles.MoonlightPeaks, GameProfiles.Minecraft))) continue;

					// A Minecraft mod IS its .jar file. It is copied into the mods folder rather than unpacked,
					// which is what every other path here does with a download — unpacking one would leave a
					// folder of loose classes that Fabric walks straight past.
					if (item.Type is "GitHubJar" or "ModrinthJar")
					{
						MinecraftSuiteMod? part = MinecraftSuite.AccessMods
							.Append(MinecraftSuite.FabricApi)
							.FirstOrDefault(m => m.DisplayName == item.Name);

						if (part != null) await InstallMinecraftModAsync(part, MinecraftRootFolder(), noBuildFor);
						continue;
					}

					// An entry with no source is one the manager knows about but cannot fetch yet (see the
					// Moonlight Access note at the top of this file). Say so and carry on with the rest rather
					// than stopping the whole suite install on it.
					if (string.IsNullOrEmpty(item.Source))
					{
						Speak(Loc.T("suite.noSourceSpeak", item.Name));
						SpeakBox(
							Loc.T("suite.noSourceBox", item.Name),
							Loc.T("suite.noSourceTitle"),
							MessageBoxButtons.OK,
							MessageBoxIcon.Information);
						continue;
					}

					// A mod page the manager knows the shape of — a script extender with one file per game build,
					// or a mod that ships in two parts — is handled by ModPartRules, which picks the exact file
					// for THIS copy of the game. That is what gets a GOG copy the GOG build of SKSE instead of
					// the Steam one, and what stops SSE Engine Fixes arriving without its preloader.
					KnownMod? known = ModPartRules.Find(game, item.Source)
						?? (item.Type == "Loader" ? ScriptExtenderKnownMod(game) : null);
					if (known != null)
					{
						await InstallKnownModPartsAsync(known);
						continue;
					}

					SetStatus(Loc.T("suite.downloading", item.Name));
					Speak(Loc.T("suite.downloading", item.Name));

					string? downloadUrl = null;
					string zipName = $"{item.Name.Replace(" ", "")}_Install" + (item.Type == "Loader" ? ".7z" : ".zip");

					if (item.Type == "GitHub")
					{
						downloadUrl = await GetGitHubLatestReleaseZipUrl(item.Source);
					}
					else if (item.Type == "GitHubStatic")
					{
						downloadUrl = item.Source;
					}
					else if (item.Type == "Nexus")
					{
						if (_nexusService.IsPremium)
						{
							try
							{
								var tempMod = new GameMod { NexusID = item.Source, Name = item.Name };
								ProgressAnnouncer dl = NewProgress(item.Name, installing: false);
								string tempPath = await _nexusService.DownloadModUpdateAsync(tempMod, downloadsPath, dl);
								dl.Complete();
								await InstallFromZip(tempPath, item.Source);
								continue;
							}
							catch (Exception ex)
							{
								LogFailure(item.Name, $"Nexus download failed", ex);
							}
						}
						
						string gameDomain = _nexusService.CurrentGameDomain;
						Speak(Loc.T("suite.manualDownloadSpeak", item.Name));
						Process.Start(new ProcessStartInfo($"https://www.nexusmods.com/{gameDomain}/mods/{item.Source}?tab=files") { UseShellExecute = true });
						SpeakBox(Loc.T("suite.manualDownloadBox1", item.Name), Loc.T("suite.manualDownloadTitle"));
						continue;
					}

					if (!string.IsNullOrEmpty(downloadUrl))
					{
						if (downloadUrl.Contains("nexusmods.com"))
						{
							string nexusId = "42147"; // F4SE mod ID
							if (_nexusService.IsPremium)
							{
								try
								{
									var tempMod = new GameMod { NexusID = nexusId, Name = item.Name };
									ProgressAnnouncer dl = NewProgress(item.Name, installing: false);
									string tempPath = await _nexusService.DownloadModUpdateAsync(tempMod, downloadsPath, dl);
									dl.Complete();
									if (item.Type == "Loader")
									{
										ProgressAnnouncer inst = NewProgress(item.Name, installing: true);
										await ModFileSystem.InstallScriptExtenderAsync(tempPath, _settings.CurrentGamePath, game, LogError, inst);
										inst.Complete();
									}
									else
									{
										await InstallFromZip(tempPath, nexusId);
									}
									continue;
								}
								catch (Exception ex)
								{
									LogFailure(item.Name, $"Nexus download failed", ex);
								}
							}
							
							Speak(Loc.T("suite.manualDownloadSpeak", item.Name));
							Process.Start(new ProcessStartInfo(downloadUrl) { UseShellExecute = true });
							SpeakBox(Loc.T("suite.manualDownloadBox2", item.Name), Loc.T("suite.manualDownloadTitle"));
							continue;
						}

						try
						{
							byte[] bytes = await _nexusService.DownloadBytesAsync(downloadUrl);
							string tempPath = Path.Combine(downloadsPath, zipName);
							File.WriteAllBytes(tempPath, bytes);

							if (item.Type == "Loader")
							{
								SetStatus(Loc.T("suite.installingItem", item.Name), speak: false);
								ProgressAnnouncer inst = NewProgress(item.Name, installing: true);
								await ModFileSystem.InstallScriptExtenderAsync(tempPath, _settings.CurrentGamePath, game, LogError, inst);
								inst.Complete();
							}
							else
							{
								await InstallFromZip(tempPath);
							}
						}
						catch (Exception ex)
						{
							LogFailure(item.Name, $"Download or extraction failed", ex);
							Speak(Loc.T("suite.failedInstallItem", item.Name));
						}
					}
				}

				// Said at the end rather than while the list is being built: the two Minecraft accessibility
				// mods both speak the same screens, so having both installed says everything twice — a symptom
				// that is baffling and whose cause is not guessable from inside the game.
				if (GameProfiles.IsGame(game, GameProfiles.Minecraft))
					WarnAboutRivalAccessMod(MinecraftSuite.AccessModFor(_settings.MinecraftAccessModId));

				Speak(Loc.T("suite.setupComplete"));

				// The sentence above has always ended "Refreshing mod list" and nothing ever refreshed it. Every
				// game was affected: after installing the whole suite the list was exactly as empty as before,
				// and the only way to see what had just been installed was to refresh it by hand.
				//
				// ⚠️ Before closeView, not after. Closing restores focus to the mod list and announces it, so
				// refreshing afterwards would change the list underneath an announcement that had already been
				// made — the same ordering fault that made the list announcement wrong for eight attempts.
				await RefreshModList(checkUpdates: false);
				closeView();

				// ⚠️ After the view has closed, and one box for the whole run rather than one per mod.
				//
				// Raised mid-install, this cut the reader off: a message box takes focus and the reader
				// abandons whatever it was halfway through to read the dialog, so "Downloading Fabric API" and
				// "Fabric API installed" were both lost to the box that followed them. Here the only thing it
				// interrupts is the mod list announcing itself — and that is re-announced when focus returns to
				// the list after the box is dismissed, so nothing is actually lost.
				if (noBuildFor.Count > 0)
					SpeakBox(
						Loc.T("mc.install.noBuildSomeBox", string.Join(", ", noBuildFor), _settings.MinecraftGameVersion),
						Loc.T("mc.install.noBuildTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			catch (Exception ex)
			{
				SpeakBox(Loc.T("suite.installErrorBox", FriendlyError(ex)));
			}
			finally
			{
				UseWaitCursor = false;
				btnInstall.Enabled = true;
				btnInstall.Text = installText;
				isInstalling = false;
				// Whatever happened, don't leave "Installing..." / "Downloading..." stuck in the title.
				ResetStatus();
			}
		};

		container.Controls.Add(layout);
		ApplyScreenReaderPauses(container);
		return lstStatus;
		});
	}

	/// <summary>
	/// Confirms, then removes the installed Skyrim/Fallout 4 script extender (SKSE/F4SE) and the files it placed
	/// in the game folder. Uses the recorded install manifest for an exact removal when present; otherwise falls
	/// back to removing the unambiguous loader and versioned DLLs and tells the user that some script files may
	/// remain. Triggered from the Mods menu (Skyrim/FO4 only).
	/// </summary>
	private void UninstallScriptExtenderCommand()
	{
		string game = _settings.ActiveGame;
		if (!GameProfiles.IsAnyGame(game, GameProfiles.SkyrimSE, GameProfiles.Fallout4))
		{
			Speak(Loc.T("se.uninstallWrongGame"));
			return;
		}

		string gamePath = string.IsNullOrEmpty(_settings.CurrentGamePath) ? DetectGameFolder(game) : _settings.CurrentGamePath;
		string seName = GameProfiles.IsGame(game, GameProfiles.SkyrimSE) ? "SKSE" : "F4SE";

		if (!ModFileSystem.IsScriptExtenderInstalled(game, gamePath))
		{
			Speak(Loc.T("se.uninstallNotInstalled", seName));
			SpeakBox(Loc.T("se.uninstallNotInstalledBox", seName), Loc.T("se.uninstallTitle", seName),
				MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}

		var confirm = SpeakBox(Loc.T("se.uninstallConfirm", seName), Loc.T("se.uninstallTitle", seName),
			MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
		if (confirm != DialogResult.Yes) { Speak(Loc.T("se.uninstallCancelled")); return; }

		try
		{
			var (removed, usedManifest) = ModFileSystem.UninstallScriptExtender(game, gamePath, LogError);
			string msg = usedManifest
				? Loc.T("se.uninstallDone", seName, removed)
				: Loc.T("se.uninstallDonePartial", seName, removed);
			Speak(msg);
			SpeakBox(msg, Loc.T("se.uninstallTitle", seName), MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
		catch (Exception ex)
		{
			Speak(Loc.T("se.uninstallFailed", seName));
			SpeakBox(Loc.T("se.uninstallFailedBox", FriendlyError(ex)), Loc.T("common.error"),
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}

	/// <summary>
	/// Downloads the latest SMAPI installer from its GitHub release and runs it unattended against the
	/// detected Stardew Valley folder, so the user never has to drive SMAPI's interactive console
	/// installer. SMAPI's installer accepts <c>--install</c>, <c>--game-path</c> and <c>--no-prompt</c>
	/// for exactly this scripted scenario. Falls back to opening smapi.io in the browser whenever the
	/// game folder, download, or installer can't be resolved, or the result can't be confirmed. Returns
	/// true only when <c>StardewModdingAPI.exe</c> is present in the game folder afterwards.
	/// </summary>
	private async Task<bool> InstallSmapiAsync(string gameFolder)
	{
		// SMAPI can only install into a real Stardew Valley folder; bail to the manual flow otherwise.
		if (string.IsNullOrEmpty(gameFolder) || !File.Exists(Path.Combine(gameFolder, "Stardew Valley.exe")))
		{
			Speak(Loc.T("suite.smapiNotFoundSpeak"));
			Process.Start(new ProcessStartInfo("https://smapi.io") { UseShellExecute = true });
			SpeakBox(
				Loc.T("suite.smapiNotFoundBox"),
				Loc.T("suite.smapiInstallTitle"));
			return false;
		}

		string tempDir = Path.Combine(Path.GetTempPath(), "SMAPI_" + Path.GetRandomFileName());
		try
		{
			SetStatus(Loc.T("suite.smapiDownloading"));
			Speak(Loc.T("suite.smapiDownloading"));

			string? url = await GetSmapiInstallerZipUrl();
			if (string.IsNullOrEmpty(url)) throw new Exception("Could not resolve the SMAPI download URL.");

			Directory.CreateDirectory(tempDir);
			string zipPath = Path.Combine(tempDir, "SMAPI-installer.zip");
			ProgressAnnouncer progress = NewProgress(Loc.T("suite.smapiName"), installing: false);
			await _nexusService.DownloadFileWithProgressAsync(url, zipPath, progress);
			progress.Complete();

			string extractDir = Path.Combine(tempDir, "extracted");
			await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractDir));

			// The installer sits at "<top-level installer folder>/internal/windows/SMAPI.Installer.exe";
			// search for it so we don't depend on the version number in the folder name.
			string marker = Path.Combine("internal", "windows");
			string? installerExe = Directory.EnumerateFiles(extractDir, "SMAPI.Installer.exe", SearchOption.AllDirectories)
					.FirstOrDefault(p => p.Contains(marker, StringComparison.OrdinalIgnoreCase))
				?? Directory.EnumerateFiles(extractDir, "SMAPI.Installer.exe", SearchOption.AllDirectories).FirstOrDefault();
			if (installerExe == null) throw new Exception("SMAPI.Installer.exe was not found in the download.");

			SetStatus(Loc.T("suite.smapiInstallingStatus"));
			Speak(Loc.T("suite.smapiInstalling"));

			var psi = new ProcessStartInfo(installerExe)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				WorkingDirectory = Path.GetDirectoryName(installerExe)!
			};
			psi.ArgumentList.Add("--install");
			psi.ArgumentList.Add("--game-path");
			psi.ArgumentList.Add(gameFolder);
			psi.ArgumentList.Add("--no-prompt");

			await Task.Run(() =>
			{
				using Process? proc = Process.Start(psi);
				proc?.WaitForExit();
			});

			bool installed = File.Exists(Path.Combine(gameFolder, "StardewModdingAPI.exe"));
			if (installed)
			{
				Speak(Loc.T("suite.smapiInstalled"));
			}
			else
			{
				Speak(Loc.T("suite.smapiUnconfirmed"));
				Process.Start(new ProcessStartInfo("https://smapi.io") { UseShellExecute = true });
			}
			return installed;
		}
		catch (Exception ex)
		{
			LogFailure("SMAPI", "Automatic SMAPI install failed", ex);
			Speak(Loc.T("suite.smapiAutoFailed"));
			Process.Start(new ProcessStartInfo("https://smapi.io") { UseShellExecute = true });
			return false;
		}
		finally
		{
			try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
			catch (Exception ex) { DiagnosticLog.WriteException("Suite", $"clearing the temporary folder {tempDir}", ex); }
		}
	}

	/// <summary>
	/// Resolves the Stardew Valley install folder. SMAPI's Mods folder lives inside the game folder, so
	/// the parent of the configured mods path is the most reliable source — it's correct even when the
	/// game is on a non-default Steam library drive, where registry/Steam-default detection can miss it.
	/// Falls back to registry/default detection only when the mods-path parent isn't a real game folder.
	/// </summary>
	private string ResolveStardewFolder()
	{
		string parent = Path.GetDirectoryName(_settings.CurrentModsPath) ?? "";
		if (!string.IsNullOrEmpty(parent) && File.Exists(Path.Combine(parent, "Stardew Valley.exe")))
			return parent;
		return DetectGameFolder("StardewValley");
	}

	private bool HasModUniqueId(string uniqueId)
	{
		// SMAPI mod IDs are namespaced as "Author.ModName" (e.g. "shoaib.stardewaccess",
		// "Shockah.Kokoro", "Shockah.ProjectFluent"), so an exact match against a bare key like
		// "StardewAccess" never succeeds. Match either the full ID or its trailing "<Author>." segment.
		return _allInstalledMods.Any(m =>
			m.UniqueId.Equals(uniqueId, StringComparison.OrdinalIgnoreCase) ||
			m.UniqueId.EndsWith("." + uniqueId, StringComparison.OrdinalIgnoreCase));
	}

	private bool HasModNameContains(string subStr)
	{
		return _allInstalledMods.Any(m => m.Name.Contains(subStr, StringComparison.OrdinalIgnoreCase) || 
										  m.UniqueId.Contains(subStr, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// What to say after "Installed" on the SKSE/F4SE row: the extender's own version, the game build it is
	/// compiled for, and whether that is the build the user is running.
	///
	/// Neither extender appears in the mod list — it is loose files in the game folder — so this row is the only
	/// place the manager can answer "which one have I got?". It matters most right after a game update, when the
	/// answer decides whether the Mod Configuration Menu and every other extender-based feature still works.
	/// Returns "" when there is nothing extra to say (no extender game, or nothing readable).
	/// </summary>
	private static string ScriptExtenderStatusLine(string game, string gameFolder)
	{
		ScriptExtenderStatus? se = ScriptExtenderInfo.Read(game, gameFolder);
		if (se == null) return "";

		string version = se.ProductVersion.Length > 0 ? Loc.T("se.lineVersion", se.ProductVersion) : "";

		string build = !se.CanCompare
			? ""                                                        // unreadable exe (e.g. under Wine): don't guess
			: se.Match
				? Loc.T("se.lineMatch", se.TargetVersion)
				: Loc.T("se.lineMismatch", se.TargetVersion, se.GameVersion);

		// A previous build's DLL left in the folder is harmless — the loader ignores it — but it is a file the
		// user never chose to keep, so say it is there rather than leaving them to wonder.
		string others = se.OtherBuilds.Count > 0
			? Loc.T("se.lineOtherBuilds", string.Join(", ", se.OtherBuilds))
			: "";

		return version + build + others;
	}
}
