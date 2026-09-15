using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace KinetixModManager;

/// <summary>
/// Installing a mod from a GitHub repository the user names.
///
/// <para>
/// GitHub is not a catalogue and is not in the source chooser, because there is nothing to search: you cannot
/// ask GitHub for "Stardew Valley mods about fishing". What you can do is name a repository, and a great many
/// mods are released there and nowhere else — which is why the manager has downloaded from GitHub for years
/// for the mods it already knew about, and why being unable to do it for a mod the user found themselves was
/// a gap rather than a missing feature.
/// </para>
/// </summary>
public partial class Form1
{
	/// <summary>
	/// Asks for a repository, finds its newest release, and installs the right file from it.
	///
	/// The prompt takes a pasted URL as readily as <c>owner/repo</c>, because the user is coming from a
	/// browser: asking somebody to read an address aloud to themselves and retype two words out of the middle
	/// of it is asking them to do by hand what a pattern does exactly.
	/// </summary>
	private async Task InstallFromGitHubAsync()
	{
		string? typed = ShowTextPrompt(
			Loc.T("github.installTitle"), Loc.T("github.installPrompt"), "");
		if (typed == null) { Speak(Loc.T("common.changesCancelled")); return; }

		string? repo = GitHubReleases.ParseRepo(typed);
		if (repo == null)
		{
			SpeakBox(Loc.T("github.notARepoBox", typed.Trim()), Loc.T("github.notARepoTitle"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}

		SetStatus(Loc.T("github.looking", repo));

		GitHubRelease? release;
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, GitHubReleases.LatestReleaseApiUrl(repo));
			request.Headers.UserAgent.ParseAdd($"KinetixModManager/{NexusService.AppVersion}");
			using HttpResponseMessage response = await NexusService.HttpClient.SendAsync(request);

			if (!response.IsSuccessStatusCode)
			{
				// A 404 here means one of two quite different things — no such repository, or a repository
				// that has never published a release — and neither is something the user did wrong. Say both.
				ResetStatus();
				SpeakBox(Loc.T("github.noReleaseBox", repo), Loc.T("github.noReleaseTitle"),
					MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			release = GitHubReleases.Parse(await response.Content.ReadAsStringAsync());
		}
		catch (Exception ex)
		{
			ResetStatus();
			LogFailure("GitHub", $"asking GitHub for the latest release of {repo}", ex);
			SpeakBox(Loc.T("github.failedBox", FriendlyError(ex)), Loc.T("github.failedTitle"),
				MessageBoxButtons.OK, MessageBoxIcon.Error);
			return;
		}

		bool minecraft = GameProfiles.Find(_settings.ActiveGame)?.IsMinecraft == true;
		GitHubAsset? asset = release == null ? null : GitHubReleases.PickModAsset(release, minecraft);
		if (asset == null)
		{
			ResetStatus();
			SpeakBox(Loc.T("github.nothingUsableBox", repo), Loc.T("github.nothingUsableTitle"),
				MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}

		try
		{
			Directory.CreateDirectory(downloadsPath);
			string destination = Path.Combine(downloadsPath, asset.Name);

			ProgressAnnouncer progress = NewProgress(asset.Name, installing: false);
			await _nexusService.DownloadFileWithProgressAsync(asset.Url, destination, progress);
			progress.Complete();

			Speak(Loc.T("github.downloaded", asset.Name, release!.TagName));

			// From here it is an ordinary install of a file on disk, and deliberately so: a mod from GitHub is
			// the same mod, and everything the install path does — backing up what it replaces, the FOMOD
			// wizard, recording which release went on — should happen for it too.
			if (minecraft && asset.Name.EndsWith(MinecraftLayout.ModExtension, StringComparison.OrdinalIgnoreCase))
			{
				await InstallMinecraftJarAsync(destination);
			}
			else
			{
				await InstallFromZip(destination, confirmReinstall: true, gitHubRepo: repo);
			}
		}
		catch (Exception ex)
		{
			ResetStatus();
			LogFailure("GitHub", $"installing {asset.Name} from {repo}", ex);
			SpeakBox(Loc.T("github.failedBox", FriendlyError(ex)), Loc.T("github.failedTitle"),
				MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}
}
