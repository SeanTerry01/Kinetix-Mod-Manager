using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace KinetixModManager;

/// <summary>
/// Installing Minecraft itself — so that moving to a new version, or setting the game up for the first time,
/// never ends in "now open the official launcher".
///
/// <para>
/// The manager could already start a version, install Fabric for it and update every mod to it. What it could
/// not do was put the version there: it assumed the official launcher had fetched the game, which is true of
/// every version the player has played and of no version they have not. So the move offered in the Updates
/// list pinned the setup to something that did not exist on disk, and the failure arrived later, during a
/// launch, as a Java stack trace.
/// </para>
///
/// <para>
/// ⚠️ And the repair half is not a nicety. Sean's 26.3 — a version the official launcher had installed — was
/// missing four asset objects, and all four were <c>.ogg</c> sounds. The game started, played, and looked
/// entirely normal to anyone watching it. For a player who hears the game rather than sees it, a missing asset
/// is not cosmetic, so what runs before a launch checks the assets too and not only the files that would stop
/// the game starting.
/// </para>
///
/// <para>
/// Everything comes from Mojang's own public endpoints against published checksums, which is how third-party
/// launchers have always done this. No account and no credential is involved: signing in remains the
/// launcher's job and <see cref="MinecraftIdentity"/> reads the result of it. The deciding —
/// which files, where they go, what they should hash to — is in <see cref="MinecraftGameFiles"/> and tested
/// there. What is here is the fetching, the asking and the saying.
/// </para>
/// </summary>
public partial class Form1
{
	/// <summary>
	/// How many files to fetch at once. A full asset index is around 5,000 objects of a few kilobytes each,
	/// where the round trip costs more than the transfer, so one at a time would take the better part of an
	/// hour for something that takes minutes. Eight is polite to a service handing out free files.
	/// </summary>
	private const int MinecraftFetchParallelism = 8;

	/// <summary>
	/// Above this, the user is asked before anything downloads rather than told while it does.
	///
	/// A repair is usually a handful of files and interrupting someone to approve four sounds would be its own
	/// kind of rudeness. A whole version is hundreds of megabytes and possibly an hour of someone's evening —
	/// that is a decision, and it is theirs.
	/// </summary>
	private const long MinecraftAskFirstBytes = 64L * 1024 * 1024;

	/// <summary>One file to fetch and where it goes. Absolute, because runtimes land outside the game root.</summary>
	private readonly record struct MinecraftFetch(string Url, string Destination, string? Sha1, long Bytes);

	/// <summary>
	/// Makes sure a Minecraft version is actually on this computer, and says whether what needed it can go
	/// ahead.
	///
	/// <para>
	/// Cheap when there is nothing to do, which is the common case and the one that runs before every launch:
	/// the version's own JSON is read from disk, the asset index beside it, and the answer is worked out
	/// without a single network call. Mojang is asked only when something is genuinely absent — which also
	/// means a launch still works with no internet at all, as long as the game is complete.
	/// </para>
	///
	/// <para>
	/// Returns <c>true</c> when the version is ready, including when it was already ready and when this is not
	/// a version Mojang publishes at all — a Fabric profile, or something the player made — since those are
	/// not ours to fetch and refusing to continue over one would break setups that work today.
	/// </para>
	/// </summary>
	/// <param name="launchVersionId">
	/// The profile that will actually be started, when there is one. Only the jar check needs it, and it needs
	/// it badly: the launcher copies the game jar into the Fabric profile's folder, so without this a machine
	/// happily running modded 26.3 is told its 41 MB jar is missing. See
	/// <see cref="MinecraftGameFiles.MissingClientJar"/>.
	/// </param>
	private async Task<bool> EnsureMinecraftVersionAsync(string root, string gameVersion, string? launchVersionId = null)
	{
		if (root.Length == 0 || gameVersion.Length == 0) return true;

		// Cleared up front, not only on the way out: a run the user declined halfway leaves a plan behind, and
		// the next run would then count someone else's Java against its own download.
		_pendingRuntimePlan = null;

		try
		{
			JObject? versionJson = MinecraftLauncher.ReadVersionJson(root, gameVersion);

			// Nothing on disk for this version: the manifest has to say where its JSON comes from before
			// anything else can be worked out.
			if (versionJson is null)
			{
				JObject? manifest = await FetchJsonAsync(MinecraftGameFiles.VersionManifestUrl);
				if (manifest is null) return AskToGoOnWithoutMojang(gameVersion);

				MinecraftVersionSource? source = MinecraftGameFiles.VersionSource(manifest, gameVersion);
				if (source is null) return true;   // not a version Mojang publishes; leave it alone

				if (!await FetchOneAsync(new MinecraftFetch(source.Value.Url,
						Path.Combine(root, MinecraftGameFiles.VersionJsonRelativePath(gameVersion)),
						source.Value.Sha1, 0)))
					return FailedToInstall(gameVersion, 1);

				versionJson = MinecraftLauncher.ReadVersionJson(root, gameVersion);
				if (versionJson is null) return FailedToInstall(gameVersion, 1);
			}

			// The asset index lists the assets, so it has to be in hand before they can be counted. It is
			// around half a megabyte and several versions share one.
			JObject? assetIndex = await EnsureAssetIndexAsync(root, versionJson);

			var wanted = new List<MinecraftFetch>();

			if (MinecraftGameFiles.MissingClientJar(versionJson, gameVersion, root, File.Exists, launchVersionId) is { } jar)
				wanted.Add(new MinecraftFetch(jar.Url, Path.Combine(root, jar.RelativePath), jar.Sha1, jar.Bytes));

			int missingAssets = 0;
			if (assetIndex is not null)
			{
				foreach (MinecraftDownload asset in MinecraftGameFiles.MissingAssets(assetIndex, root, File.Exists))
				{
					wanted.Add(new MinecraftFetch(asset.Url, Path.Combine(root, asset.RelativePath), asset.Sha1, asset.Bytes));
					missingAssets++;
				}
			}

			// The Java runtime is its own question with its own answer, so it is asked separately and its
			// files are appended to the same download rather than run as a second one.
			if (!await AddMissingJavaRuntimeAsync(root, versionJson, gameVersion, wanted)) return false;

			if (wanted.Count == 0) return true;

			return await FetchTheGameAsync(gameVersion, wanted, missingAssets);
		}
		catch (Exception ex)
		{
			// A version that is in fact complete must not be blocked by a fault in the checking of it. When
			// something really is missing, the launch fails a moment later and says so properly.
			LogFailure("Minecraft", $"Could not check whether Minecraft {gameVersion} is fully installed", ex);
			return true;
		}
	}

	/// <summary>
	/// Reads the asset index for a version, fetching it first if it is not there. <c>null</c> when the version
	/// names no index or it could not be had — in which case the assets are simply not checked, rather than
	/// the whole launch being stopped over them.
	/// </summary>
	private async Task<JObject?> EnsureAssetIndexAsync(string root, JObject versionJson)
	{
		string id = MinecraftGameFiles.AssetIndexId(versionJson);
		if (id.Length == 0) return null;

		string path = Path.Combine(root, MinecraftGameFiles.AssetIndexRelativePath(id));

		if (!File.Exists(path) &&
			MinecraftGameFiles.MissingAssetIndex(versionJson, root, File.Exists) is { } index &&
			!await FetchOneAsync(new MinecraftFetch(index.Url, path, index.Sha1, index.Bytes)))
			return null;

		try { return File.Exists(path) ? JObject.Parse(await File.ReadAllTextAsync(path)) : null; }
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", $"reading the asset index {id}", ex);
			return null;
		}
	}

	/// <summary>
	/// Adds the Java runtime this version wants to the download, when no launcher on this machine already has
	/// it. Answers <c>false</c> only when the user was asked and said no.
	///
	/// <para>
	/// This is the step that used to end the story. <see cref="MinecraftLauncher.BuildPlan"/> could say which
	/// Java a version needed and that it was absent, and then had nothing to offer but "start it once through
	/// the Minecraft launcher" — which is the sentence this whole feature exists to delete. Minecraft has
	/// moved runtime twice in recent memory, so a version bump that also bumps Java is not a rare case.
	/// </para>
	/// </summary>
	private async Task<bool> AddMissingJavaRuntimeAsync(
		string root, JObject versionJson, string gameVersion, List<MinecraftFetch> wanted)
	{
		string component = MinecraftGameFiles.JavaComponent(versionJson);
		if (MinecraftLauncher.FindBundledJava(component, root).Length > 0) return true;

		JObject? all = await FetchJsonAsync(MinecraftGameFiles.JavaRuntimeManifestUrl);
		if (all is null) return true;   // reported by the launch itself if it turns out to matter

		string platform = MinecraftGameFiles.RuntimePlatform(
			MinecraftLauncher.CurrentOsName, MinecraftLauncher.CurrentOsArch);

		string manifestUrl = MinecraftGameFiles.RuntimeManifestUrl(all, platform, component);
		if (manifestUrl.Length == 0)
		{
			// Said plainly rather than retried. Mojang publishing no build of this runtime for this machine is
			// a fact, and no amount of downloading will change it.
			Speak(Loc.T("mc.game.javaUnavailableSpeak", gameVersion));
			SpeakBox(Loc.T("mc.game.javaUnavailableBox", gameVersion, component, platform),
				Loc.T("mc.game.javaTitle"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return true;
		}

		JObject? manifest = await FetchJsonAsync(manifestUrl);
		if (manifest is null) return true;

		string componentRoot = MinecraftGameFiles.RuntimeRootFor(root, component, platform);
		MinecraftRuntimePlan plan = MinecraftGameFiles.MissingRuntimeFiles(manifest, componentRoot, File.Exists);
		if (plan.Files.Count == 0) return true;

		string version = MinecraftGameFiles.RuntimeVersionName(all, platform, component);
		if (SpeakBox(Loc.T("mc.game.javaConfirm", gameVersion, version, FormatBytes(plan.TotalBytes)),
				Loc.T("mc.game.javaTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			Speak(Loc.T("common.changesCancelled"));
			return false;
		}

		foreach (string directory in plan.Directories)
		{
			try { Directory.CreateDirectory(Path.Combine(componentRoot, directory)); }
			catch (Exception ex) { DiagnosticLog.WriteException("Minecraft", $"creating {directory}", ex); }
		}

		foreach (MinecraftRuntimeFile file in plan.Files)
			wanted.Add(new MinecraftFetch(file.Url, Path.Combine(componentRoot, file.RelativePath), file.Sha1, file.Bytes));

		_pendingRuntimePlan = (componentRoot, plan);
		return true;
	}

	/// <summary>
	/// The permissions and links a fetched Java runtime needs once its files have landed — held here because
	/// they can only be applied after the download, and are meaningless on Windows.
	/// </summary>
	private (string Root, MinecraftRuntimePlan Plan)? _pendingRuntimePlan;

	/// <summary>
	/// Asks if it is worth asking, downloads, and says what happened.
	///
	/// <para>
	/// The size is given in the question, because "a few files" and "most of an evening" are the same sentence
	/// otherwise. Below the threshold nothing is asked: a repair of four sounds that interrupts to request
	/// permission for four sounds has traded one annoyance for a worse one.
	/// </para>
	/// </summary>
	private async Task<bool> FetchTheGameAsync(string gameVersion, List<MinecraftFetch> wanted, int missingAssets)
	{
		long total = wanted.Sum(f => f.Bytes);

		// The Java runtime was asked about on its own terms, with its own size, a moment ago. Counting it here
		// too would put a second question in front of someone who has just answered this one.
		int javaFiles = _pendingRuntimePlan?.Plan.Files.Count ?? 0;
		long approved = _pendingRuntimePlan?.Plan.TotalBytes ?? 0;
		int gameFiles = wanted.Count - javaFiles;

		bool ask = total - approved >= MinecraftAskFirstBytes;
		if (ask && SpeakBox(Loc.T("mc.game.confirm", gameVersion, gameFiles, FormatBytes(total - approved)),
				Loc.T("mc.game.title", gameVersion), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
		{
			Speak(Loc.T("common.changesCancelled"));
			return false;
		}

		// Four situations, because they are genuinely four different things to be told. Assets are named in
		// the third: a player who has just heard the game was missing sounds knows why it went quiet last
		// session. And Java gets its own, because calling a Java runtime "Minecraft's own files" is a small
		// lie that makes the next sentence about it incomprehensible.
		string opening = ask
			? Loc.T("mc.game.fetching", gameVersion, wanted.Count, FormatBytes(total))
			: gameFiles == 0
				? Loc.T("mc.game.fetchingJava", gameVersion, FormatBytes(approved))
				: missingAssets > 0
					? Loc.T("mc.game.repairingAssets", gameVersion, gameFiles)
					: Loc.T("mc.game.repairing", gameVersion, gameFiles);

		Speak(opening);
		SetStatus(opening, speak: false);

		List<string> failed = await FetchAllAsync(wanted, Loc.T("mc.game.progressName", gameVersion));
		ApplyPendingRuntimePlan();

		ResetStatus();

		if (failed.Count > 0) return FailedToInstall(gameVersion, failed.Count);

		Speak(Loc.T("mc.game.done", gameVersion));
		return true;
	}

	/// <summary>Marks a fetched runtime's files executable and makes its links. Both no-ops on Windows.</summary>
	private void ApplyPendingRuntimePlan()
	{
		if (_pendingRuntimePlan is not { } pending) return;
		_pendingRuntimePlan = null;

		if (OperatingSystem.IsWindows()) return;

		foreach (MinecraftRuntimeFile file in pending.Plan.Files.Where(f => f.Executable))
		{
			string path = Path.Combine(pending.Root, file.RelativePath);
			try
			{
				if (File.Exists(path))
					File.SetUnixFileMode(path, File.GetUnixFileMode(path) |
						UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Minecraft", $"marking {file.RelativePath} executable", ex); }
		}

		foreach (MinecraftRuntimeLink link in pending.Plan.Links)
		{
			string path = Path.Combine(pending.Root, link.RelativePath);
			try
			{
				if (File.Exists(path) || Directory.Exists(path)) continue;
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				File.CreateSymbolicLink(path, link.Target);
			}
			catch (Exception ex) { DiagnosticLog.WriteException("Minecraft", $"linking {link.RelativePath}", ex); }
		}
	}

	/// <summary>
	/// Fetches a list of files, several at a time, and returns the names of the ones that could not be had.
	///
	/// <para>
	/// Progress is reported by bytes rather than by file count, because a download of one 41 MB jar and five
	/// thousand two-kilobyte sounds is otherwise 99 percent complete while almost none of it has arrived —
	/// and a progress announcement that lies is worse than none. The reports are serialised: the announcer
	/// decides deciles from fields of its own and eight threads racing through those would stutter and
	/// repeat.
	/// </para>
	/// </summary>
	private async Task<List<string>> FetchAllAsync(IReadOnlyList<MinecraftFetch> files, string progressName)
	{
		// Every file counts for at least one byte so that a list whose sizes are all unknown still advances.
		long total = files.Sum(f => Math.Max(f.Bytes, 1));
		long fetched = 0;

		var failed = new ConcurrentBag<string>();
		ProgressAnnouncer progress = NewProgress(progressName, installing: false);
		var reporting = new object();

		using var gate = new SemaphoreSlim(MinecraftFetchParallelism);
		var running = new List<Task>(files.Count);

		foreach (MinecraftFetch file in files)
		{
			await gate.WaitAsync();
			running.Add(Task.Run(async () =>
			{
				try
				{
					if (!await FetchOneAsync(file)) failed.Add(Path.GetFileName(file.Destination));
				}
				finally
				{
					gate.Release();
					long done = Interlocked.Add(ref fetched, Math.Max(file.Bytes, 1));
					lock (reporting) progress.Report(done * 100.0 / total);
				}
			}));
		}

		await Task.WhenAll(running);
		progress.Complete();
		return failed.ToList();
	}

	/// <summary>
	/// Fetches one file and verifies it, trying twice before giving up.
	///
	/// <para>
	/// A file that arrives corrupted is deleted rather than kept, and that matters more here than it looks: a
	/// truncated asset is not reported by anything, it simply is not the sound it claims to be, and it would
	/// be treated as present forever after. The second attempt is there because five thousand requests over a
	/// domestic connection will drop one or two, and failing a whole install over that would be absurd.
	/// </para>
	/// </summary>
	private static async Task<bool> FetchOneAsync(MinecraftFetch file)
	{
		// The jar and the runtime's biggest pieces are tens of megabytes and get the patient client; the
		// thousands of small objects get the one whose timeout means something.
		HttpClient client = file.Bytes >= 8L * 1024 * 1024 ? KinetixHttp.Downloads : KinetixHttp.Api;

		for (int attempt = 0; attempt < 2; attempt++)
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(file.Destination)!);

				using HttpResponseMessage response =
					await client.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead);
				response.EnsureSuccessStatusCode();

				await using (FileStream stream = File.Create(file.Destination))
					await response.Content.CopyToAsync(stream);

				if (string.IsNullOrEmpty(file.Sha1) || FileHasSha1(file.Destination, file.Sha1!)) return true;

				File.Delete(file.Destination);
			}
			catch (Exception ex)
			{
				DiagnosticLog.WriteException("Minecraft", $"fetching {file.Url}", ex);
			}
		}

		return false;
	}

	/// <summary>Says what could not be fetched, and answers <c>false</c> so the caller stops.</summary>
	private bool FailedToInstall(string gameVersion, int count)
	{
		Speak(Loc.T("mc.game.failedSpeak", gameVersion, count));
		SpeakBox(Loc.T("mc.game.failedBox", gameVersion, count), Loc.T("mc.game.failedTitle"),
			MessageBoxButtons.OK, MessageBoxIcon.Warning);
		return false;
	}

	/// <summary>
	/// What to do when Mojang cannot be reached and the version is not on disk at all.
	///
	/// Asked rather than decided, because the manager does not know why it failed. A player who is offline
	/// knows they are offline; a player whose version is in fact fine and whose manifest request merely timed
	/// out should not be stopped from playing by a check that was meant to help them.
	/// </summary>
	private bool AskToGoOnWithoutMojang(string gameVersion)
	{
		Speak(Loc.T("mc.game.offlineSpeak", gameVersion));
		return SpeakBox(Loc.T("mc.game.offlineBox", gameVersion), Loc.T("mc.game.offlineTitle"),
			MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
	}

	/// <summary>Fetches and parses a JSON document, or <c>null</c> when it could not be had.</summary>
	private static async Task<JObject?> FetchJsonAsync(string url)
	{
		try
		{
			using HttpResponseMessage response = await KinetixHttp.Api.GetAsync(url);
			response.EnsureSuccessStatusCode();
			return JObject.Parse(await response.Content.ReadAsStringAsync());
		}
		catch (Exception ex)
		{
			DiagnosticLog.WriteException("Minecraft", $"fetching {url}", ex);
			return null;
		}
	}
}
